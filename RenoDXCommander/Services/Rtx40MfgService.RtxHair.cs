// Rtx40MfgService.RtxHair.cs — Universal MFG Unlocker: Bink proxy names and the RTX Hair files.
//
// Bink: the mod's docs/install.md says to rename the game's binkw64.dll to binkw64Hooked.dll (bink2w64.dll to
// bink2w64Hooked.dll), give the mod the original name and keep both: these routes need the game's own Bink DLL.
// Same handling as RtxEncoreService (a 0-byte .original marks "the game had none").
//
// RTX Hair (docs/compatibility.md, "Path traced hair"): in DirectX 12 games with NVIDIA RTX Hair the mod turns the
// hair strands into triangles so RTX 40 and RTX 30 get RTX Hair too. It needs Microsoft's dxcompiler.dll and
// dxil.dll beside the game (Vulkan needs none). RHI stages them from microsoft/DirectXShaderCompiler (latest
// release, dxc_*.zip, bin\x64) and copies them beside the game's exe. A game's own copies are never replaced.
// What RHI copied is listed (name|sha256) in RTXMFG.rhi-dxc.txt beside them; removal deletes only files that
// still match. RTXMFG.RtxHair.ini ([RtxHair] Universal=0 turns it off) is the user's setting file.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RenoDXCommander.Services;

public partial class Rtx40MfgService
{
    // ── Bink ──────────────────────────────────────────────────────────────────

    /// <summary>binkw64.dll / bink2w64.dll: the game's own file must stay beside the mod as &lt;name&gt;Hooked.dll.</summary>
    public static bool IsBinkName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        var file = Path.GetFileName(name.Replace('/', '\\'));
        return file.Equals("binkw64.dll", StringComparison.OrdinalIgnoreCase)
            || file.Equals("bink2w64.dll", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>binkw64.dll → binkw64Hooked.dll in the same folder.</summary>
    public static string BinkHookedPath(string dest)
        => Path.Combine(Path.GetDirectoryName(dest) ?? "", Path.GetFileNameWithoutExtension(dest) + "Hooked.dll");

    /// <summary>
    /// Before the mod is copied as a Bink name: the game's own file becomes &lt;name&gt;Hooked.dll. A real .original
    /// (installs from before this fix) is the game's file and moves there too. With no game file, a 0-byte .original
    /// marks the name as RHI's. <paramref name="destIsMod"/>: the file at the name is already the mod (update).
    /// </summary>
    private void KeepGameBinkAsHooked(string dest, bool destIsMod)
    {
        var hooked   = BinkHookedPath(dest);
        var original = dest + ".original";
        if (File.Exists(hooked)) return; // already set up: dest is the mod
        if (File.Exists(original))
        {
            if (new FileInfo(original).Length == 0) return; // RHI's own name (the game has no Bink DLL)
            File.Move(original, hooked);
            _crashReporter.Log($"[Rtx40MfgService.KeepGameBinkAsHooked] Moved the game's '{Path.GetFileName(dest)}' from .original to '{Path.GetFileName(hooked)}'");
            return;
        }
        if (File.Exists(dest) && !destIsMod)
        {
            File.Move(dest, hooked);
            _crashReporter.Log($"[Rtx40MfgService.KeepGameBinkAsHooked] Kept the game's '{Path.GetFileName(dest)}' as '{Path.GetFileName(hooked)}'");
            return;
        }
        if (File.Exists(dest))
            _crashReporter.Log($"[Rtx40MfgService.KeepGameBinkAsHooked] '{dest}' is already the mod and the game's Bink DLL isn't there (no .original, no Hooked copy) — verify the game's files to get it back");
        File.WriteAllBytes(original, Array.Empty<byte>());
    }

    /// <summary>
    /// Hands a proxy name back after the mod leaves it: a Bink name gets the game's &lt;name&gt;Hooked.dll back under
    /// its real name; other names use the .original sentinel.
    /// </summary>
    private void RestoreProxySlot(string path, string installedAs)
    {
        var hooked = BinkHookedPath(path);
        if (IsBinkName(installedAs) && !IsPluginInstall(installedAs) && !IsHsfModsInstall(installedAs) && File.Exists(hooked))
        {
            if (File.Exists(path)) File.Delete(path);
            File.Move(hooked, path);
            var sentinel = path + ".original";
            if (File.Exists(sentinel) && new FileInfo(sentinel).Length == 0) File.Delete(sentinel);
            _crashReporter.Log($"[Rtx40MfgService.RestoreProxySlot] Put the game's '{Path.GetFileName(path)}' back from '{Path.GetFileName(hooked)}'");
            return;
        }
        AuxInstallService.SentinelRestore(path);
    }

    /// <summary>
    /// Installs from before the Bink fix moved the game's Bink DLL to .original and left the mod in its place
    /// with no &lt;name&gt;Hooked.dll, which breaks the game. Moves the .original to the Hooked name when the file at
    /// the name is the mod. Returns true when something was repaired.
    /// </summary>
    private bool RepairBinkInstall(string installPath, string installedAs)
    {
        if (!IsBinkName(installedAs) || IsPluginInstall(installedAs) || IsHsfModsInstall(installedAs)) return false;
        var dest     = Path.Combine(installPath, installedAs);
        var hooked   = BinkHookedPath(dest);
        var original = dest + ".original";
        var staged   = Path.Combine(_stagingDir, StagedDllName);
        try
        {
            if (!File.Exists(dest) || File.Exists(hooked) || !File.Exists(original) || new FileInfo(original).Length == 0) return false;
            // The mod: same file as staged, or the same product by version info (an older build; the update
            // that follows goes through Install, which does the same move)
            if (!File.Exists(staged) || !(FilesEqual(dest, staged) || AuxInstallService.SameProductName(dest, staged)))
            {
                _crashReporter.Log($"[Rtx40MfgService.RepairBinkInstall] '{dest}' isn't recognisably the mod — left as it is");
                return false;
            }
            File.Move(original, hooked);
            _crashReporter.Log($"[Rtx40MfgService.RepairBinkInstall] Moved the game's '{installedAs}' from .original to '{Path.GetFileName(hooked)}' in '{installPath}'");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"[Rtx40MfgService.RepairBinkInstall] Failed in '{installPath}' — {ex.Message}");
            return false;
        }
    }

    // ── RTX Hair files (Microsoft DirectX Shader Compiler) ────────────────────

    private const string DxcApiUrl = "https://api.github.com/repos/microsoft/DirectXShaderCompiler/releases/latest";

    /// <summary>The two files the mod's RTX Hair conversion needs beside the game in DirectX 12.</summary>
    public static readonly string[] DxcFiles = { "dxcompiler.dll", "dxil.dll" };

    /// <summary>Beside the game's exe: lists the DXC files RHI copied (name|sha256).</summary>
    public const string DxcMarkerFile = "RTXMFG.rhi-dxc.txt";

    /// <summary>The mod's RTX Hair settings file beside the game's exe ([RtxHair] Universal=0 turns it off).</summary>
    public const string RtxHairIniName = "RTXMFG.RtxHair.ini";

    /// <summary>%LocalAppData%\RHI\dxc (beside the mod's own rtx40mfg staging folder).</summary>
    private string DxcStagingDir => Path.Combine(Path.GetDirectoryName(_stagingDir) ?? _stagingDir, "dxc");

    private static readonly SemaphoreSlim DxcLock = new(1, 1);
    private bool _dxcCheckedThisSession;

    /// <summary>Set by MainViewModel: true when the game in this install folder runs DirectX 12 (not Vulkan only).</summary>
    public Func<string, bool>? RunsDx12Provider { get; set; }

    public bool IsDxcStagingReady => DxcFiles.All(f => File.Exists(Path.Combine(DxcStagingDir, f)));

    public string? DxcStagedVersion
    {
        get
        {
            var p = Path.Combine(DxcStagingDir, "version.txt");
            try { return File.Exists(p) ? File.ReadAllText(p).Trim() : null; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        }
    }

    /// <summary>
    /// The deploy rule (pure): the mod is installed (not in HSF's helper), the game runs DirectX 12, and the card is an
    /// RTX 40 or RTX 30 (RTX 50 has RTX Hair itself). <paramref name="gpu"/> null = unknown card: deploy.
    /// </summary>
    internal static bool RtxHairFilesWanted(bool modInstalled, bool hsfModsInstall, bool runsDx12, GpuFamily? gpu)
        => modInstalled && !hsfModsInstall && runsDx12 && (gpu is null or GpuFamily.Rtx30 or GpuFamily.Rtx40);

    /// <summary>The PC's card for the rule above: null when it hasn't been read or no NVIDIA card was reported.</summary>
    private static GpuFamily? CurrentGpuFamily()
    {
        var gpus = GpuInfoService.Cached;
        if (gpus == null) return null;
        return GpuInfoService.Primary(gpus)?.Family;
    }

    /// <summary>True when this install should have RHI's dxcompiler.dll / dxil.dll beside the game's exe.</summary>
    public bool WantsRtxHairFiles(string installPath, string? installedAs)
    {
        if (string.IsNullOrEmpty(installPath) || string.IsNullOrEmpty(installedAs)) return false;
        bool runsDx12;
        try { runsDx12 = RunsDx12Provider?.Invoke(installPath) == true; }
        catch (InvalidOperationException ex) // the card list changed while it was read
        {
            _crashReporter.Log($"[Rtx40MfgService.WantsRtxHairFiles] '{installPath}' — {ex.Message}");
            return false;
        }
        return RtxHairFilesWanted(IsInstalledIn(installPath, installedAs), IsHsfModsInstall(installedAs), runsDx12, CurrentGpuFamily());
    }

    /// <summary>Stages the DXC files when this install wants them (call before <see cref="Install"/> so it can copy them).</summary>
    public async Task PrepareRtxHairStagingAsync(string installPath, string installedAs)
    {
        await GpuInfoService.GetNvidiaGpusAsync().ConfigureAwait(false);
        if (IsHsfModsInstall(installedAs) || string.IsNullOrEmpty(installPath)) return;
        bool runsDx12;
        try { runsDx12 = RunsDx12Provider?.Invoke(installPath) == true; }
        catch (InvalidOperationException ex) // the card list changed while it was read
        {
            _crashReporter.Log($"[Rtx40MfgService.PrepareRtxHairStaging] '{installPath}' — {ex.Message}");
            return;
        }
        if (!RtxHairFilesWanted(true, false, runsDx12, CurrentGpuFamily())) return;
        await EnsureDxcStagingAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Downloads dxcompiler.dll and dxil.dll from Microsoft's latest DirectX Shader Compiler release. Checks for a
    /// newer release at most once per session (ETag-cached). Returns true when the files are staged.
    /// </summary>
    public async Task<bool> EnsureDxcStagingAsync()
    {
        if (IsDxcStagingReady && _dxcCheckedThisSession) return true;
        await DxcLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (IsDxcStagingReady && _dxcCheckedThisSession) return true;
            var (tag, url) = await FetchDxcReleaseAsync().ConfigureAwait(false);
            if (string.IsNullOrEmpty(tag) || string.IsNullOrEmpty(url))
                return IsDxcStagingReady; // offline / rate limited: keep what's staged; try again next session
            _dxcCheckedThisSession = true;
            if (IsDxcStagingReady && string.Equals(DxcStagedVersion, tag, StringComparison.OrdinalIgnoreCase)) return true;

            Directory.CreateDirectory(DxcStagingDir);
            var zipPath = Path.Combine(DxcStagingDir, "download.zip.tmp");
            try
            {
                using (var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                {
                    resp.EnsureSuccessStatusCode();
                    await using var src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    await using var dst = File.Create(zipPath);
                    await src.CopyToAsync(dst).ConfigureAwait(false);
                }

                // Extract both to temp files first; replace the staged pair only when both are good
                var temps = new List<(string Temp, string Dest)>();
                using (var zip = ZipFile.OpenRead(zipPath))
                {
                    foreach (var file in DxcFiles)
                    {
                        var entry = zip.Entries.FirstOrDefault(e =>
                            e.FullName.Replace('/', '\\').EndsWith("bin\\x64\\" + file, StringComparison.OrdinalIgnoreCase));
                        if (entry == null)
                        {
                            _crashReporter.Log($"[Rtx40MfgService.EnsureDxcStaging] {tag} has no bin/x64/{file} — keeping staged {DxcStagedVersion ?? "(none)"}");
                            foreach (var t in temps) File.Delete(t.Temp);
                            return IsDxcStagingReady;
                        }
                        var dest = Path.Combine(DxcStagingDir, file);
                        var temp = dest + ".tmp";
                        entry.ExtractToFile(temp, overwrite: true);
                        if (!LooksLikeDll(temp))
                        {
                            _crashReporter.Log($"[Rtx40MfgService.EnsureDxcStaging] '{entry.FullName}' in {tag} is not a DLL — skipped");
                            File.Delete(temp);
                            foreach (var t in temps) File.Delete(t.Temp);
                            return IsDxcStagingReady;
                        }
                        temps.Add((temp, dest));
                    }
                }
                foreach (var (temp, dest) in temps) File.Move(temp, dest, overwrite: true);
                var previous = DxcStagedVersion;
                File.WriteAllText(Path.Combine(DxcStagingDir, "version.txt"), tag);
                UpdateHistory.Record("Frame Generation", "DirectX Shader Compiler (RTX Hair)", previous, tag);
                _crashReporter.Log($"[Rtx40MfgService.EnsureDxcStaging] Staged dxcompiler.dll + dxil.dll from {tag}");
                return true;
            }
            finally
            {
                try { if (File.Exists(zipPath)) File.Delete(zipPath); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { _crashReporter.Log($"[Rtx40MfgService.EnsureDxcStaging] Couldn't delete the download — {ex.Message}"); }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException
                                      or TaskCanceledException or InvalidDataException)
        {
            _crashReporter.Log($"[Rtx40MfgService.EnsureDxcStaging] Failed — {ex.Message}");
            return IsDxcStagingReady;
        }
        finally { DxcLock.Release(); }
    }

    private static bool LooksLikeDll(string path)
    {
        var fi = new FileInfo(path);
        if (fi.Length < 1024) return false;
        using var fs = File.OpenRead(path);
        return fs.ReadByte() == 'M' && fs.ReadByte() == 'Z';
    }

    private async Task<(string? tag, string? url)> FetchDxcReleaseAsync()
    {
        try
        {
            var json = await _etagCache.GetWithETagAsync(_http, DxcApiUrl).ConfigureAwait(false);
            if (json == null) return (null, null);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            if (string.IsNullOrEmpty(tag) || !root.TryGetProperty("assets", out var assets)) return (null, null);
            foreach (var a in assets.EnumerateArray())
            {
                var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (name != null && IsDxcWindowsZip(name))
                    return (tag, a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null);
            }
            _crashReporter.Log($"[Rtx40MfgService.FetchDxcRelease] {tag} has no dxc_*.zip asset");
            return (tag, null);
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            _crashReporter.Log($"[Rtx40MfgService.FetchDxcRelease] {ex.Message}");
            return (null, null);
        }
    }

    /// <summary>The Windows release zip ("dxc_2025_07_14.zip"), not the Linux tarball or a source archive.</summary>
    internal static bool IsDxcWindowsZip(string assetName)
        => assetName.StartsWith("dxc_", StringComparison.OrdinalIgnoreCase)
        && assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, string> ReadDxcMarker(string marker)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(marker)) return map;
        foreach (var raw in File.ReadAllLines(marker))
        {
            var parts = raw.Trim().Split('|');
            if (parts.Length >= 2 && DxcFiles.Contains(parts[0], StringComparer.OrdinalIgnoreCase)) map[parts[0]] = parts[1];
        }
        return map;
    }

    /// <summary>The DXC files RHI copied into this folder (from the marker).</summary>
    public static IReadOnlyCollection<string> DxcFilesOwnedIn(string dir)
        => string.IsNullOrEmpty(dir) ? Array.Empty<string>() : ReadDxcMarker(Path.Combine(dir, DxcMarkerFile)).Keys.ToList();

    private static string Sha256Of(string path)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(fs));
    }

    /// <summary>
    /// Copies the staged DXC files beside the game's exe. A file RHI didn't copy (the game's own) is never replaced;
    /// RHI's copies are refreshed when the staged version changed. <paramref name="recreateRemoved"/>: put back a file
    /// RHI copied before that is now missing (explicit installs only; automatic passes treat it as removed by the user).
    /// </summary>
    public bool DeployDxcFiles(string installPath, bool recreateRemoved)
    {
        if (string.IsNullOrEmpty(installPath) || !IsDxcStagingReady || !Directory.Exists(installPath)) return false;
        var marker = Path.Combine(installPath, DxcMarkerFile);
        try
        {
            var owned = ReadDxcMarker(marker);
            bool changed = false;
            foreach (var file in DxcFiles)
            {
                var dest   = Path.Combine(installPath, file);
                var staged = Path.Combine(DxcStagingDir, file);
                if (File.Exists(dest))
                {
                    if (!owned.TryGetValue(file, out var recorded))
                    {
                        _crashReporter.Log($"[Rtx40MfgService.DeployDxcFiles] Keeping the game's own '{file}' in '{installPath}'");
                        continue;
                    }
                    if (SameContent(dest, staged)) continue;
                    if (!Sha256Of(dest).Equals(recorded, StringComparison.OrdinalIgnoreCase))
                    {
                        // Replaced since RHI copied it (a game update): it's the game's now
                        owned.Remove(file);
                        changed = true;
                        _crashReporter.Log($"[Rtx40MfgService.DeployDxcFiles] '{file}' in '{installPath}' was replaced after RHI copied it — left to the game");
                        continue;
                    }
                }
                else if (owned.ContainsKey(file) && !recreateRemoved)
                {
                    continue; // removed by the user: not put back
                }
                File.Copy(staged, dest, overwrite: true);
                owned[file] = Sha256Of(dest);
                changed = true;
                _crashReporter.Log($"[Rtx40MfgService.DeployDxcFiles] Copied '{file}' ({DxcStagedVersion}) to '{installPath}'");
            }
            if (changed)
            {
                if (owned.Count > 0) File.WriteAllLines(marker, owned.Select(kv => $"{kv.Key}|{kv.Value}"));
                else if (File.Exists(marker)) File.Delete(marker);
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"[Rtx40MfgService.DeployDxcFiles] Failed for '{installPath}' — {ex.Message}");
            return false;
        }
    }

    /// <summary>Deletes the DXC files RHI copied (only those still unchanged) and the marker.</summary>
    public void RemoveDxcFiles(string installPath)
    {
        if (string.IsNullOrEmpty(installPath)) return;
        var marker = Path.Combine(installPath, DxcMarkerFile);
        if (!File.Exists(marker)) return;
        try
        {
            foreach (var (file, recorded) in ReadDxcMarker(marker))
            {
                var p = Path.Combine(installPath, file);
                if (!File.Exists(p)) continue;
                if (!Sha256Of(p).Equals(recorded, StringComparison.OrdinalIgnoreCase))
                {
                    _crashReporter.Log($"[Rtx40MfgService.RemoveDxcFiles] Keeping '{p}': it was replaced after RHI copied it");
                    continue;
                }
                File.Delete(p);
            }
            File.Delete(marker);
            _crashReporter.Log($"[Rtx40MfgService.RemoveDxcFiles] Removed RHI's RTX Hair files from '{installPath}'");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"[Rtx40MfgService.RemoveDxcFiles] Failed for '{installPath}' — {ex.Message}");
        }
    }

    /// <summary>
    /// Launch / periodic pass: installs that want the RTX Hair files get them (existing installs once, RHI's copies
    /// refreshed after a new staged version); installs that no longer qualify (API, card, mod gone) lose RHI's copies.
    /// Runs in the background.
    /// </summary>
    private void ReconcileDxcFiles(IReadOnlyList<(string InstallPath, string InstalledAs)> installs)
    {
        if (installs.Count == 0) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await GpuInfoService.GetNvidiaGpusAsync().ConfigureAwait(false);
                var wanted = new List<string>();
                foreach (var (path, installedAs) in installs)
                {
                    bool wants = WantsRtxHairFiles(path, installedAs);
                    bool hasMarker = File.Exists(Path.Combine(path, DxcMarkerFile));
                    if (!wants && hasMarker) RemoveDxcFiles(path);
                    else if (wants) wanted.Add(path);
                }
                wanted = wanted.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (wanted.Count == 0) return;
                if (!await EnsureDxcStagingAsync().ConfigureAwait(false)) return;
                foreach (var path in wanted) DeployDxcFiles(path, recreateRemoved: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or InvalidOperationException)
            {
                _crashReporter.Log($"[Rtx40MfgService.ReconcileDxcFiles] Failed — {ex.Message}");
            }
        });
    }

    /// <summary>After an install: copy the RTX Hair files when wanted (staging them in the background if needed).</summary>
    private void DeployDxcAfterInstall(string installPath, string installedAs, bool recreateRemoved)
    {
        if (!WantsRtxHairFiles(installPath, installedAs)) return;
        if (IsDxcStagingReady) { DeployDxcFiles(installPath, recreateRemoved); return; }
        _ = Task.Run(async () =>
        {
            try
            {
                if (await EnsureDxcStagingAsync().ConfigureAwait(false)) DeployDxcFiles(installPath, recreateRemoved);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException)
            {
                _crashReporter.Log($"[Rtx40MfgService.DeployDxcAfterInstall] Failed — {ex.Message}");
            }
        });
    }

    // ── RTXMFG.RtxHair.ini ────────────────────────────────────────────────────

    /// <summary>True unless RTXMFG.RtxHair.ini beside the exe has Universal=0 under [RtxHair].</summary>
    public static bool IsRtxHairOn(string installPath)
    {
        var p = Path.Combine(installPath, RtxHairIniName);
        try
        {
            if (!File.Exists(p)) return true;
            return GetIniValue(File.ReadAllLines(p), "RtxHair", "Universal")?.Trim() != "0";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[Rtx40MfgService.IsRtxHairOn] '{p}' — {ex.Message}");
            return true;
        }
    }

    /// <summary>On = removes Universal from [RtxHair] (the mod's default is on); Off = Universal=0. Other keys are kept.</summary>
    public static bool SetRtxHairOn(string installPath, bool on)
    {
        var p = Path.Combine(installPath, RtxHairIniName);
        try
        {
            var lines = File.Exists(p) ? File.ReadAllLines(p) : Array.Empty<string>();
            var result = SetIniValue(lines, "RtxHair", "Universal", on ? null : "0");
            // Nothing left but section headers / blank lines: drop the file
            if (result.All(l => l.Trim().Length == 0 || l.Trim().StartsWith('[')))
            {
                if (File.Exists(p)) File.Delete(p);
            }
            else File.WriteAllLines(p, result);
            CrashReporter.Log($"[Rtx40MfgService.SetRtxHairOn] RTX Hair {(on ? "on" : "off")} in '{installPath}'");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[Rtx40MfgService.SetRtxHairOn] '{p}' — {ex.Message}");
            return false;
        }
    }

    /// <summary>The value of <paramref name="key"/> in <paramref name="section"/>, or null.</summary>
    internal static string? GetIniValue(IEnumerable<string> lines, string section, string key)
    {
        bool inSection = false;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith('[')) { inSection = line.Equals($"[{section}]", StringComparison.OrdinalIgnoreCase); continue; }
            if (!inSection || line.StartsWith(';') || line.StartsWith('#')) continue;
            int eq = line.IndexOf('=');
            if (eq > 0 && line[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) return line[(eq + 1)..].Trim();
        }
        return null;
    }

    /// <summary>
    /// Sets (or with <paramref name="value"/> null, removes) one key in one section, keeping every other line.
    /// A missing section is appended.
    /// </summary>
    internal static List<string> SetIniValue(IEnumerable<string> lines, string section, string key, string? value)
    {
        var result = new List<string>();
        bool inSection = false, sectionSeen = false, done = false;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                if (inSection && !done && value != null) { result.Add($"{key}={value}"); done = true; }
                inSection = line.Equals($"[{section}]", StringComparison.OrdinalIgnoreCase);
                sectionSeen |= inSection;
                result.Add(raw);
                continue;
            }
            int eq = line.IndexOf('=');
            if (inSection && eq > 0 && !line.StartsWith(';') && !line.StartsWith('#')
                && line[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                if (value != null && !done) { result.Add($"{key}={value}"); done = true; }
                continue; // removed (or a duplicate)
            }
            result.Add(raw);
        }
        if (value != null && !done)
        {
            if (inSection) result.Add($"{key}={value}");
            else
            {
                if (!sectionSeen)
                {
                    if (result.Count > 0 && result[^1].Trim().Length > 0) result.Add("");
                    result.Add($"[{section}]");
                }
                result.Add($"{key}={value}");
            }
        }
        return result;
    }
}
