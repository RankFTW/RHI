// MainViewModel.ReLimiterV4.cs -- which ReLimiter major version (v3 / v4) each game gets.
// ReLimiter v4 is a normal "latest" release, but only some games should run it (UlV4Rules has the rules).
// Games that don't qualify stay on the last v3 release (staged like a pinned version, never updated again).
// The installed version always comes from the game's own addon file (FileVersion), not ul_meta.json.

using System.Collections.Concurrent;
using System.Diagnostics;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    // ── Per-game choice ─────────────────────────────────────────────────────────

    /// <summary>The game's ReLimiter major choice: "3", "4", or null = Automatic.</summary>
    public string? GetUlMajorChoice(string gameName, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        return _gameNameService.UlMajorChoice.TryGetValue(key, out var v) && v is "3" or "4" ? v : null;
    }

    /// <summary>Sets the game's ReLimiter major choice ("3" / "4"; null or anything else = Automatic).</summary>
    public void SetUlMajorChoice(string gameName, string? choice, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        if (choice is "3" or "4") _gameNameService.UlMajorChoice[key] = choice;
        else _gameNameService.UlMajorChoice.Remove(key);
        CrashReporter.Log($"[MainViewModel.SetUlMajorChoice] '{key}' → {(choice is "3" or "4" ? "v" + choice : "Automatic")}");
        SaveNameMappings();
    }

    // ── Rules inputs ────────────────────────────────────────────────────────────

    /// <summary>The graphics card as GpuInfoService last saw it. Before the first query finishes = Unknown.</summary>
    internal static UlGpuState UlGpuNow()
    {
        var gpus = GpuInfoService.Cached;
        if (gpus == null) return UlGpuState.Unknown;
        if (gpus.Count > 0) return UlGpuState.Nvidia;
        return GpuInfoService.QueryFailed ? UlGpuState.Unknown : UlGpuState.NotNvidia;
    }

    /// <summary>
    /// Whether the game runs DirectX 12, in this order: the user's Rendering API override; the manifest's
    /// graphicsApiOverrides; PCGW (lists DX12 and RHI's scan has DX12 = yes, PCGW lists APIs but no DX12 = no);
    /// RHI's scan alone (only DX12 = yes, only older APIs = no); then ReShade.log from the last launch.
    /// The card's DetectedApis stay as they are (Luma needs DX11 listed for Unreal games).
    /// </summary>
    internal UlDx12State UlDx12For(GameCardViewModel card)
    {
        if (card.RequiresVulkanInstall) return UlDx12State.No;

        if (GetApiOverride(card.GameName, card.Source ?? "") is { Count: > 0 } apis)
            return apis.Contains(nameof(GraphicsApiType.DirectX12), StringComparer.OrdinalIgnoreCase) ? UlDx12State.Yes : UlDx12State.No;

        bool scanDx12 = card.GraphicsApi == GraphicsApiType.DirectX12 || card.DetectedApis.Contains(GraphicsApiType.DirectX12);
        if (_manifest?.GraphicsApiOverrides?.ContainsKey(card.GameName) == true)
            return scanDx12 ? UlDx12State.Yes : UlDx12State.No;

        var pcgw = _pcgwService.GetCachedApiInfo(card.GameName);
        bool pcgwHasApis = pcgw != null && (pcgw.HasDirectX9 || pcgw.HasDirectX10 || pcgw.HasDirectX11
            || pcgw.HasDirectX12 || pcgw.HasVulkan || pcgw.HasOpenGL);
        if (pcgwHasApis)
        {
            if (!pcgw!.HasDirectX12) return UlDx12State.No;
            if (scanDx12) return UlDx12State.Yes;
        }
        else if (scanDx12)
        {
            bool older = card.DetectedApis.Any(a => a is GraphicsApiType.DirectX8 or GraphicsApiType.DirectX9
                or GraphicsApiType.DirectX10 or GraphicsApiType.DirectX11 or GraphicsApiType.OpenGL);
            if (!older) return UlDx12State.Yes;
        }
        else if (card.GraphicsApi != GraphicsApiType.Unknown || card.DetectedApis.Count > 0)
        {
            return UlDx12State.No;
        }

        return ReShadeLogApi(card.InstallPath) switch
        {
            UlLogApi.Dx12 => UlDx12State.Yes,
            UlLogApi.Older or UlLogApi.Vulkan => UlDx12State.No,
            _ => UlDx12State.Unknown,
        };
    }

    /// <summary>The ReLimiter v4 verdict for a game (blocked / allowed / automatic).</summary>
    internal UlV4Verdict UlV4VerdictFor(GameCardViewModel card)
    {
        bool dxvk = card.IsDxvkInstalled || (card.Mod?.IsGenericSource == true && card.InstalledRecord != null);
        var dx12 = UlDx12For(card);
        bool openGl = card.GraphicsApi == GraphicsApiType.OpenGL && dx12 != UlDx12State.Yes;
        // A log showing only Vulkan means the game really rendered with Vulkan (e.g. Ryubing, whose card says DX12)
        bool vulkan = card.RequiresVulkanInstall || ReShadeLogApi(card.InstallPath) == UlLogApi.Vulkan;
        return UlV4Rules.Check(card.Is32Bit, vulkan, dxvk, openGl, UlGpuNow(), dx12);
    }

    /// <summary>True when the game should be on ReLimiter v4 (its choice + the verdict).</summary>
    internal bool UlWantsV4(GameCardViewModel card) =>
        UlV4Rules.WantsV4(GetUlMajorChoice(card.GameName, card.Source ?? ""), UlV4VerdictFor(card));

    // ── ReShade.log ─────────────────────────────────────────────────────────────

    private static readonly ConcurrentDictionary<string, (long Len, DateTime Time, UlLogApi? Api)> _rsLogApiCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The API the game last started on according to ReShade.log in its folder, or null. Cached by size + date.</summary>
    private static UlLogApi? ReShadeLogApi(string? installPath)
    {
        if (string.IsNullOrEmpty(installPath)) return null;
        var path = Path.Combine(installPath, "ReShade.log");
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists || fi.Length == 0) return null;
            if (_rsLogApiCache.TryGetValue(path, out var c) && c.Len == fi.Length && c.Time == fi.LastWriteTimeUtc) return c.Api;

            // The device lines come in the first seconds of a launch: 4 MB is plenty (logs are usually far smaller)
            const int cap = 4 * 1024 * 1024;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buf = new byte[(int)Math.Min(fs.Length, cap)];
            int read = 0, n;
            while (read < buf.Length && (n = fs.Read(buf, read, buf.Length - read)) > 0) read += n;
            var api = UlV4Rules.ApiFromReShadeLog(System.Text.Encoding.UTF8.GetString(buf, 0, read));
            _rsLogApiCache[path] = (fi.Length, fi.LastWriteTimeUtc, api);
            return api;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[MainViewModel.ReShadeLogApi] Couldn't read '{path}' — {ex.Message}");
            return null;
        }
    }

    // ── Installed version from the game's file ──────────────────────────────────

    private static readonly ConcurrentDictionary<string, (long Len, DateTime Time, Version? Ver)> _ulFileVersionCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The ReLimiter addon in the game (addon folder first, then the install folder), or null.</summary>
    internal static string? FindUlAddon(string? installPath, bool is32Bit)
    {
        if (string.IsNullOrEmpty(installPath)) return null;
        var name = GetUlFileName(is32Bit);
        var inAddonDir = Path.Combine(ModInstallService.GetAddonDeployPath(installPath), name);
        if (File.Exists(inAddonDir)) return inAddonDir;
        var direct = Path.Combine(installPath, name);
        return File.Exists(direct) ? direct : null;
    }

    /// <summary>A ReLimiter addon's FileVersion, or null (missing, no version resource). Cached by size + date.</summary>
    internal static Version? ReadUlAddonVersion(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) return null;
            if (_ulFileVersionCache.TryGetValue(path, out var c) && c.Len == fi.Length && c.Time == fi.LastWriteTimeUtc) return c.Ver;
            var info = FileVersionInfo.GetVersionInfo(path);
            Version? ver = info.FileMajorPart > 0 || info.FileMinorPart > 0
                ? new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart)
                : UlV4Rules.ParseVersion(info.FileVersion);
            _ulFileVersionCache[path] = (fi.Length, fi.LastWriteTimeUtc, ver);
            return ver;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException)
        {
            CrashReporter.Log($"[MainViewModel.ReadUlAddonVersion] '{path}' — {ex.Message}");
            return null;
        }
    }

    /// <summary>The ReLimiter version installed in a game, from its addon file, or null.</summary>
    internal static Version? UlInstalledFileVersion(GameCardViewModel card) =>
        ReadUlAddonVersion(FindUlAddon(card.InstallPath, card.Is32Bit));

    // ── The last v3 release ─────────────────────────────────────────────────────

    /// <summary>
    /// The newest v3 release with a build for this bitness (from the release list the Version picker uses, ETag cached),
    /// or null when the list can't be fetched. v3 gets no more releases once v4 is out.
    /// </summary>
    internal static async Task<string?> GetLastV3TagAsync(bool is32Bit)
    {
        var releases = UlReleaseService.Cached ?? await UlReleaseService.ListAsync().ConfigureAwait(false);
        if (releases == null) return null;
        return UlV4Rules.NewestTagWithMajor(releases.Where(r => r.UrlFor(is32Bit) != null).Select(r => r.Tag), 3);
    }

    /// <summary>Major version of the latest ReLimiter release from the last check, or null when not known yet.</summary>
    private int? LatestUlMajor => UlV4Rules.MajorOf(_latestUlVersion);

    // ── Dev test build (unlock.txt only) ────────────────────────────────────────

    /// <summary>
    /// Developer testing only (needs unlock.txt): a 64-bit ReLimiter build placed at
    /// %LocalAppData%\RHI\relimiter-v4-test\relimiter.addon64 is treated as the latest release (its FileVersion is the
    /// latest version, installs copy it instead of the GitHub download), so the v4 rules can be tried before v4 is on
    /// GitHub. The last v3 still comes from GitHub. Remove the file to go back to normal.
    /// </summary>
    internal static readonly string UlDevTestPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "relimiter-v4-test", UltraLimiterFileName64);

    /// <summary>The dev test build's version when the override is on, else null.</summary>
    internal static Version? UlDevTestVersion() =>
        DevUnlockService.IsUnlocked && File.Exists(UlDevTestPath) ? ReadUlAddonVersion(UlDevTestPath) : null;

    /// <summary>Makes the dev test build the latest release (sets _latestUlVersion). Returns true when it's on.</summary>
    private bool ApplyUlDevOverride()
    {
        if (UlDevTestVersion() is not { } dev) return false;
        var tag = "v" + UlV4Rules.DisplayVersion(dev);
        if (!string.Equals(_latestUlVersion, tag, StringComparison.OrdinalIgnoreCase))
            CrashReporter.Log($"[MainViewModel.ApplyUlDevOverride] Dev test build {tag} at '{UlDevTestPath}' is the latest ReLimiter (GitHub said {_latestUlVersion ?? "nothing"})");
        _latestUlVersion = tag;
        return true;
    }

    /// <summary>
    /// For a game that isn't pinned: the last v3 tag when the latest release is v4 and this game shouldn't run it,
    /// else null (install the latest). Throws when v3 is needed but its release can't be found.
    /// No ConfigureAwait(false): the install continues on the caller's context.
    /// </summary>
    private async Task<string?> LastV3TagInsteadOfLatestAsync(GameCardViewModel card)
    {
        ApplyUlDevOverride();
        if (string.IsNullOrEmpty(_latestUlVersion)) await FetchLatestUlReleaseInfoAsync(card.Is32Bit);
        // Offline: the staged latest build says which major "latest" is
        int? latestMajor = LatestUlMajor ?? ReadUlAddonVersion(GetUlCachePath(card.Is32Bit))?.Major;
        if (latestMajor is null || latestMajor < UlV4Rules.V4Major) return null;

        await GpuInfoService.GetNvidiaGpusAsync(); // 32-bit games too: they can pick v4
        if (UlWantsV4(card)) return null;

        var tag = await GetLastV3TagAsync(card.Is32Bit);
        if (tag == null)
            throw new InvalidOperationException("Couldn't find the last ReLimiter v3 release on GitHub. Try again later.");
        CrashReporter.Log($"[MainViewModel.LastV3TagInsteadOfLatestAsync] '{card.GameName}' stays on v3 ({tag})");
        return tag;
    }

    // ── relimiter.ini on a major version change ─────────────────────────────────

    /// <summary>
    /// A relimiter.ini for a game on this major version: the user's template (inis\relimiter.ini) without the other
    /// version's keys, plus RHI's settings (OSD key, shared presets, DLSS info hooks, frame rate cap, v4 PC Latency).
    /// No template = only RHI's lines.
    /// </summary>
    private string UlIniTextFor(int major)
    {
        string? template = null;
        try { if (File.Exists(AuxInstallService.UlIniPath)) template = File.ReadAllText(AuxInstallService.UlIniPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[MainViewModel.UlIniTextFor] template — {ex.Message}");
        }
        return UlV4Rules.BuildSeededIni(template, major,
            _settingsViewModel.UlOsdHotkey, _settingsViewModel.UlSharedPresets, _settingsViewModel.UlDlssHooks, _settingsViewModel.UlTargetFps,
            nativePcl: major >= UlV4Rules.V4Major && PclAllowed(),
            pacingPreference: _settingsViewModel.UlPacingPreference);
    }

    /// <summary>The major version of the ReLimiter installed in this game (its addon's FileVersion), or null.</summary>
    internal static int? UlInstalledMajor(GameCardViewModel card)
        => ReadUlAddonVersion(FindUlAddon(card.InstallPath, card.Is32Bit))?.Major;

    /// <summary>
    /// The user allowed PC Latency in Settings › Components › ReLimiter (their account is in Performance Log Users,
    /// or the current sign-in already has it). v4 inis then get native_pcl_enabled=true.
    /// </summary>
    private static bool PclAllowed()
    {
        var s = PclPermissionService.GetStatus();
        return s.DirectMember == true || (s.DirectMember == null && s.InToken);
    }

    /// <summary>On a v4 install / update with PC Latency allowed: native_pcl_enabled=true in the game's relimiter.ini. Never throws.</summary>
    private void ApplyUlNativePclIfAllowed(string installPath)
    {
        var ini = Path.Combine(ModInstallService.GetAddonDeployPath(installPath), UlV4Rules.IniName);
        try
        {
            if (!File.Exists(ini) || !PclAllowed()) return;
            AuxInstallService.ApplyUlNativePcl(ini);
            CrashReporter.Log($"[MainViewModel.ApplyUlNativePclIfAllowed] native_pcl_enabled=true in '{ini}'");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[MainViewModel.ApplyUlNativePclIfAllowed] '{ini}' — {ex.Message}");
        }
    }

    /// <summary>
    /// After the PC Latency permission is granted or removed in Settings: native_pcl_enabled=true / false in every game
    /// that has ReLimiter v4 (its relimiter.ini next to the addon, and RE Framework's _storage_ copy), so they don't wait
    /// for their next ReLimiter update. Games that are running are skipped: ReLimiter writes its settings back when it
    /// saves, which would undo the change (they get it at their next ReLimiter install or update). Touches the disk:
    /// call off the UI thread with a snapshot of the cards. Returns (inis written, running games skipped).
    /// </summary>
    internal static (int Written, int Running) SetUlNativePclInV4Games(IEnumerable<GameCardViewModel> cards, bool on)
    {
        int written = 0, running = 0;
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var card in cards)
        {
            if (!card.IsUlInstalled || string.IsNullOrEmpty(card.InstallPath)) continue;
            if ((UlInstalledMajor(card) ?? 0) < UlV4Rules.V4Major) continue;
            var ini = Path.Combine(ModInstallService.GetAddonDeployPath(card.InstallPath), UlV4Rules.IniName);
            if (!done.Add(ini)) continue;
            if (card.IsRunning) { running++; continue; }
            var targets = new List<string> { ini };
            var storage = Path.Combine(card.InstallPath, "_storage_", UlV4Rules.IniName);
            if (File.Exists(storage)) targets.Add(storage);
            foreach (var path in targets)
            {
                // Off: only where an ini exists (no file = ReLimiter's default, off). On: a missing ini gets just this line.
                if (!on && !File.Exists(path)) continue;
                try
                {
                    AuxInstallService.ApplyUlNativePcl(path, on);
                    written++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    CrashReporter.Log($"[MainViewModel.SetUlNativePclInV4Games] '{path}' — {ex.Message}");
                }
            }
        }
        CrashReporter.Log($"[MainViewModel.SetUlNativePclInV4Games] native_pcl_enabled={(on ? "true" : "false")} in {written} relimiter.ini file(s); {running} running game(s) skipped");
        return (written, running);
    }

    /// <summary>Writes relimiter.ini from the template (see <see cref="UlIniTextFor"/>) next to the addon when the game has none. Never throws.</summary>
    private void WriteUlIniIfAbsent(string installPath, int major)
    {
        var ini = Path.Combine(ModInstallService.GetAddonDeployPath(installPath), UlV4Rules.IniName);
        try
        {
            if (File.Exists(ini)) return;
            File.WriteAllText(ini, UlIniTextFor(major));
            CrashReporter.Log($"[MainViewModel.WriteUlIniIfAbsent] Wrote relimiter.ini for v{major} to '{ini}'");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[MainViewModel.WriteUlIniIfAbsent] '{ini}' — {ex.Message}");
        }
    }

    /// <summary>
    /// v3 ↔ v4: backs up the game's relimiter.ini as relimiter.ini.v{old}-backup and replaces it with a fresh one
    /// (RHI's lines only), also RE Framework's _storage_ copy. Display mode values the game swaps are kept.
    /// Returns true when the ini next to the addon was replaced. Never throws.
    /// </summary>
    private bool ResetUlIniForMajorChange(GameCardViewModel card, int oldMajor, int newMajor)
    {
        var keepModes = AuxInstallService.KeepDisplayModeValues(card.InstallPath, DisplayModeItemKeys.ReLimiterItems);
        var content = UlIniTextFor(newMajor);
        bool replaced = Replace(Path.Combine(ModInstallService.GetAddonDeployPath(card.InstallPath), UlV4Rules.IniName), whenAbsent: true);
        if (card.IsRefInstalled)
        {
            var storage = Path.Combine(card.InstallPath, "_storage_", UlV4Rules.IniName);
            if (File.Exists(storage)) Replace(storage, whenAbsent: false);
        }
        keepModes?.Invoke();
        CrashReporter.Log($"[MainViewModel.ResetUlIniForMajorChange] '{card.GameName}' v{oldMajor} → v{newMajor}: fresh relimiter.ini {(replaced ? "written" : "failed")}");
        return replaced;

        bool Replace(string ini, bool whenAbsent)
        {
            try
            {
                if (File.Exists(ini))
                    File.Copy(ini, Path.Combine(Path.GetDirectoryName(ini)!, UlV4Rules.BackupName(oldMajor)), overwrite: true);
                else if (!whenAbsent) return false;
                File.WriteAllText(ini, content);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                CrashReporter.Log($"[MainViewModel.ResetUlIniForMajorChange] '{ini}' — {ex.Message}");
                return false;
            }
        }
    }
}
