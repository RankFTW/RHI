// HsfService.cs — HSF (Hunter's Sloppy Frames, Capt-Hunter/HSF): a ReShade add-on that runs NVIDIA
// DLSS Frame Generation (up to 6x) in DirectX 9, 11 and 12 games that have none.
//
// Release zip layout (HSF-x.y.z.zip):
//   hsf.addon64 | hsf.addon32            → ReShade addon folder (GetAddonDeployPath)
//   HSF_streamline\                      → same folder: Streamline 2.14, nvngx_dlssg.dll, hsf-dlssg-helper64.exe
//                                          (32-bit games need it too: the 64-bit helper runs DLSS-G for them)
//   HSF.ini                              → same folder: a clean start ("[HSF]"); HSF fills it in itself
//   reshade-shaders\Shaders\HSF.fx       → game reshade-shaders\Shaders (via the shader sync hook)
//   README.txt, LICENSE.txt, licenses\   → not deployed
// HSF finds HSF.ini and HSF_streamline next to its own add-on file.
//
// Motion: DX12 games that run DLSS give HSF the game's own data; everything else needs optical flow
// from a ReShade effect. RHI deploys iMMERSE LaunchPad (the same files GITC Uplift uses) unless the
// user turns it off; HSF switches the LaunchPad / HSF techniques on by itself.
//
// D3D12 route: HSF's D3D12 routes must start before the game makes its device, so on its own HSF
// picks one in the first DX12 run, writes it to HSF.ini and asks for a restart. RHI writes the same
// value up front (InGameDlssg=2 for a game with Streamline 2.x, SlBringIn=2 for one without), so the
// first launch already generates. Value 2 = "HSF's own choice": HSF takes it back by itself if the
// game turns out to run D3D11, or switches to SlBringIn if the game never starts its Streamline.
//
// Releases are pre-releases (/releases/latest skips them), so the release list is read. Downloads go
// through the API asset URL, which also works while the repository is private for accounts with a
// GitHub token that can see it. Files RHI placed are recorded in rhi_install.txt under "HSF",
// relative to the addon folder; HSF_streamline\rhi-hsf.txt holds the installed version + LaunchPad choice.

using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public class HsfService
{
    private const string ReleasesApiUrl = "https://api.github.com/repos/Capt-Hunter/HSF/releases?per_page=10";
    public const string ReleasesPage = "https://github.com/Capt-Hunter/HSF";

    public const string Component       = "HSF";
    public const string DisplayName     = "HSF";
    public const string Addon64         = "hsf.addon64";
    public const string Addon32         = "hsf.addon32";
    public const string StreamlineFolder = "HSF_streamline";
    public const string HelperExe       = "hsf-dlssg-helper64.exe";
    public const string IniName         = "HSF.ini";
    public const string ShaderFx        = "HSF.fx";
    private const string MarkerName     = "rhi-hsf.txt"; // in HSF_streamline\

    /// <summary>Files HSF writes next to its add-on (removed with it).</summary>
    private static readonly string[] HsfLogs =
    {
        "HSF_sl_bring.log", "HSF_sl_bring.old.log", "HSF_sl_probe.log", "HSF_sl_probe.old.log",
    };
    private const string ProfilesFolder = "HSF_profiles";

    /// <summary>Staged files a release must have (relative to the staging folder).</summary>
    private static readonly string[] RequiredFiles =
    {
        Addon64, Addon32, IniName, ShaderFx,
        Path.Combine(StreamlineFolder, "sl.interposer.dll"),
        Path.Combine(StreamlineFolder, "nvngx_dlssg.dll"),
        Path.Combine(StreamlineFolder, HelperExe),
    };

    private readonly HttpClient _http;
    private readonly ICrashReporter _crashReporter;
    private readonly GitHubETagCache _etagCache;
    private readonly SemaphoreSlim _stagingLock = new(1, 1);

    public static readonly string StagingDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "hsf");

    internal sealed record Release(string Version, string AssetApiUrl, string? DownloadUrl, string AssetName);

    public HsfService(HttpClient http, ICrashReporter crashReporter, GitHubETagCache etagCache)
    {
        _http = http;
        _crashReporter = crashReporter;
        _etagCache = etagCache;
    }

    // ── Staging state ─────────────────────────────────────────────────────────

    private static string? ReadVersion(string dir)
    {
        var f = Path.Combine(dir, "version.txt");
        try { return File.Exists(f) ? File.ReadAllText(f).Trim() : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private static bool IsReady(string dir) => RequiredFiles.All(f => File.Exists(Path.Combine(dir, f))) && ReadVersion(dir) != null;

    public string? StagedVersion => ReadVersion(StagingDir);
    public bool IsStagingReady => IsReady(StagingDir);
    public string? LatestVersion { get; private set; }
    public bool HasUpdate { get; private set; }

    /// <summary>Why the last download failed, for the UI (null after a success).</summary>
    public string? LastError { get; private set; }

    // ── Where things go ───────────────────────────────────────────────────────

    public static string AddonDir(string installPath) => ModInstallService.GetAddonDeployPath(installPath);
    public static string AddonFileFor(bool is32Bit) => is32Bit ? Addon32 : Addon64;
    public static string IniPath(string installPath) => Path.Combine(AddonDir(installPath), IniName);

    /// <summary>HSF's add-on is in this game's addon folder (by RHI or by hand).</summary>
    public static bool IsInstalledIn(string installPath, bool is32Bit) =>
        !string.IsNullOrEmpty(installPath) && File.Exists(Path.Combine(AddonDir(installPath), AddonFileFor(is32Bit)));

    /// <summary>RHI installed HSF here (it has a record), as opposed to a manual install.</summary>
    public static bool IsRhiInstalled(string installPath) =>
        !string.IsNullOrEmpty(installPath) && RhiInstallManifest.GetComponentFiles(installPath, Component).Count > 0;

    private static string MarkerPath(string installPath) => Path.Combine(AddonDir(installPath), StreamlineFolder, MarkerName);

    /// <summary>Installed version and the LaunchPad choice ("on" / "off") from the marker.</summary>
    public static (string? Version, bool LaunchpadOn) ReadMarker(string installPath)
    {
        string? version = null;
        bool launchpad = true;
        try
        {
            var path = MarkerPath(installPath);
            if (!File.Exists(path)) return (null, true);
            foreach (var line in File.ReadAllLines(path))
            {
                var i = line.IndexOf('=');
                if (i <= 0) continue;
                var key = line[..i].Trim();
                var val = line[(i + 1)..].Trim();
                if (key == "version") version = val.Length > 0 ? val : null;
                else if (key == "launchpad") launchpad = !val.Equals("off", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return (version, launchpad);
    }

    private static void WriteMarker(string installPath, string? version, bool launchpadOn) =>
        File.WriteAllLines(MarkerPath(installPath), new[] { $"version={version}", $"launchpad={(launchpadOn ? "on" : "off")}" });

    // ── D3D12 route ───────────────────────────────────────────────────────────

    public enum Route { None, GameStreamline, BringIn }

    /// <summary>
    /// The D3D12 route HSF would pick for this game: none for 32-bit or non-DX12 games (HSF's D3D12
    /// routes are 64-bit only), none for Streamline 1.x (HSF leaves it alone), the game's own
    /// Streamline for 2.x, else HSF's own (bring-in). <paramref name="hasStreamline"/> null = the DLSS
    /// scan hasn't run yet: no guess, HSF picks by itself.
    /// </summary>
    public static Route PickRoute(bool isDx12, bool is32Bit, bool? hasStreamline, string? streamlineVersion)
    {
        if (!isDx12 || is32Bit || hasStreamline == null) return Route.None;
        if (hasStreamline == false) return Route.BringIn;
        return streamlineVersion != null && streamlineVersion.TrimStart('v', 'V').StartsWith("1.", StringComparison.Ordinal)
            ? Route.None : Route.GameStreamline;
    }

    /// <summary>
    /// Writes the D3D12 route into HSF.ini when HSF hasn't picked one yet (neither key set), so the
    /// first launch needs no restart. Values the user or HSF set are never changed. True if written.
    /// </summary>
    public bool PresetRoute(string installPath, Route route)
    {
        if (route == Route.None) return false;
        var ini = IniPath(installPath);
        if (!File.Exists(ini)) return false;
        try
        {
            if (Dlssg20_30Service.ReadIniValue(ini, "HSF", "SlBringIn") != null
                || Dlssg20_30Service.ReadIniValue(ini, "HSF", "InGameDlssg") != null)
                return false;
            var key = route == Route.GameStreamline ? "InGameDlssg" : "SlBringIn";
            Dlssg20_30Service.WriteIniValue(ini, "HSF", key, "2");
            _crashReporter.Log($"[HsfService.PresetRoute] {key}=2 written in '{ini}' (D3D12 route ready for the first launch)");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"[HsfService.PresetRoute] '{ini}' — {ex.Message}");
            return false;
        }
    }

    /// <summary>The D3D12 route set in the game's HSF.ini, for the ⚙ dialog.</summary>
    public static string RouteText(string installPath)
    {
        var ini = IniPath(installPath);
        static bool On(string? v) => v != null && v != "0";
        var drive = Dlssg20_30Service.ReadIniValue(ini, "HSF", "InGameDlssg");
        var bring = Dlssg20_30Service.ReadIniValue(ini, "HSF", "SlBringIn");
        if (drive == "0" || bring == "0") return "D3D12 routes switched off in HSF.ini";
        if (On(drive)) return "In game: the game's own Streamline";
        if (On(bring)) return "In game: HSF's own Streamline";
        return "Picked by HSF (DirectX 9 / 11 games, or not decided yet)";
    }

    // ── Shaders (shader sync hook) ────────────────────────────────────────────

    /// <summary>
    /// Shader files the game's reshade-shaders folder must keep while HSF is installed there: HSF.fx,
    /// and LaunchPad (from the iMMERSE pack) unless switched off. Hooked into ShaderPackService's sync.
    /// </summary>
    public IReadOnlyList<(string Rel, string Source)> ShaderFilesFor(string gameDir)
    {
        if (!IsRhiInstalled(gameDir)) return Array.Empty<(string, string)>();
        var list = new List<(string, string)>();
        var fx = Path.Combine(StagingDir, ShaderFx);
        if (File.Exists(fx)) list.Add((Path.Combine("Shaders", ShaderFx), fx));
        if (ReadMarker(gameDir).LaunchpadOn) list.AddRange(UpliftService.LaunchpadFiles());
        return list;
    }

    /// <summary>Switches LaunchPad on or off for this install (the caller syncs the game's shaders afterwards).</summary>
    public bool SetLaunchpad(string installPath, bool on)
    {
        if (!IsRhiInstalled(installPath)) return false;
        try
        {
            WriteMarker(installPath, ReadMarker(installPath).Version, on);
            _crashReporter.Log($"[HsfService.SetLaunchpad] LaunchPad {(on ? "on" : "off")} in '{installPath}'");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"[HsfService.SetLaunchpad] '{installPath}' — {ex.Message}");
            return false;
        }
    }

    // ── Releases ──────────────────────────────────────────────────────────────

    /// <summary>The newest release with an HSF-*.zip (pre-releases included; drafts skipped).</summary>
    internal static Release? ParseNewestRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
        foreach (var rel in doc.RootElement.EnumerateArray())
        {
            if (rel.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True) continue;
            var tag = rel.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            if (string.IsNullOrEmpty(tag) || !rel.TryGetProperty("assets", out var assets)) continue;
            foreach (var a in assets.EnumerateArray())
            {
                var name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                if (!name.StartsWith("HSF", StringComparison.OrdinalIgnoreCase)
                    || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                var api = a.TryGetProperty("url", out var u) ? u.GetString() : null;
                var dl = a.TryGetProperty("browser_download_url", out var b) ? b.GetString() : null;
                if (api == null) continue;
                // Version strings become marker values: keep them to safe characters
                var version = new string(tag.TrimStart('v', 'V').Where(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_').ToArray());
                if (version.Length == 0) continue;
                return new Release(version, api, dl, name);
            }
        }
        return null;
    }

    private async Task<Release?> FetchNewestAsync()
    {
        try
        {
            var json = await _etagCache.GetWithETagAsync(_http, ReleasesApiUrl).ConfigureAwait(false);
            if (json == null)
            {
                LastError = "Couldn't read HSF's releases from GitHub. While HSF is invite-only, add a GitHub token for an account that has access (Settings › Accounts).";
                _crashReporter.Log("[HsfService.FetchNewest] Release list request failed (private repository without access, offline or rate-limited)");
                return null;
            }
            var rel = ParseNewestRelease(json);
            if (rel == null) _crashReporter.Log("[HsfService.FetchNewest] No release with an HSF-*.zip asset");
            return rel;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or InvalidOperationException)
        {
            _crashReporter.Log($"[HsfService.FetchNewest] Failed — {ex.Message}");
            return null;
        }
    }

    /// <summary>Reads the newest release (ETag-cached) and sets <see cref="LatestVersion"/> / <see cref="HasUpdate"/>.</summary>
    public async Task CheckForUpdateAsync()
    {
        var latest = await FetchNewestAsync().ConfigureAwait(false);
        if (latest == null) return;
        LatestVersion = latest.Version;
        HasUpdate = !string.Equals(StagedVersion, latest.Version, StringComparison.OrdinalIgnoreCase);
        _crashReporter.Log($"[HsfService.CheckForUpdate] Staged={StagedVersion ?? "(none)"}, Latest={latest.Version}, HasUpdate={HasUpdate}");
    }

    /// <summary>Downloads the newest release if it isn't staged yet. True when staging is ready afterwards.</summary>
    public async Task<bool> EnsureStagingAsync()
    {
        await _stagingLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var latest = await FetchNewestAsync().ConfigureAwait(false);
            if (latest == null) return IsStagingReady;
            LatestVersion = latest.Version;
            if (IsStagingReady && string.Equals(StagedVersion, latest.Version, StringComparison.OrdinalIgnoreCase))
            {
                HasUpdate = false;
                LastError = null;
                return true;
            }
            bool ok = await StageAsync(latest).ConfigureAwait(false);
            if (ok) { HasUpdate = false; LastError = null; }
            return ok || IsStagingReady;
        }
        finally { _stagingLock.Release(); }
    }

    /// <summary>
    /// The zip: through the API asset URL first (works for a private repository with a token that has
    /// access; the token handler adds it for api.github.com only, and it's dropped on the redirect to
    /// the download host), then the public download URL.
    /// </summary>
    private async Task<byte[]?> DownloadAsync(Release rel)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, rel.AssetApiUrl);
            req.Headers.Accept.ParseAdd("application/octet-stream");
            using var resp = await _http.SendAsync(req).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode) return await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            _crashReporter.Log($"[HsfService.Download] HTTP {(int)resp.StatusCode} for the API asset URL");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        { _crashReporter.Log($"[HsfService.Download] API asset URL failed — {ex.Message}"); }

        if (rel.DownloadUrl == null) return null;
        try { return await _http.GetByteArrayAsync(rel.DownloadUrl).ConfigureAwait(false); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _crashReporter.Log($"[HsfService.Download] {rel.AssetName} failed — {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Which staged path a zip entry goes to, or null for files games don't need (readme, licences).
    /// The add-ons, HSF.ini and HSF.fx by name; HSF_streamline\ keeps its sub-paths.
    /// </summary>
    internal static string? StagedPathFor(string entryFullName)
    {
        var p = entryFullName.Replace('\\', '/').TrimStart('/');
        var name = Path.GetFileName(p);
        if (name.Length == 0) return null;
        if (name.Equals(Addon64, StringComparison.OrdinalIgnoreCase)) return Addon64;
        if (name.Equals(Addon32, StringComparison.OrdinalIgnoreCase)) return Addon32;
        if (name.Equals(ShaderFx, StringComparison.OrdinalIgnoreCase)) return ShaderFx;
        if (name.Equals(IniName, StringComparison.OrdinalIgnoreCase) && !p.Contains('/')) return IniName;
        int at = p.IndexOf(StreamlineFolder + "/", StringComparison.OrdinalIgnoreCase);
        if (at >= 0)
        {
            var rel = p[(at + StreamlineFolder.Length + 1)..];
            if (rel.Length == 0 || rel.EndsWith(".log", StringComparison.OrdinalIgnoreCase)) return null;
            return Path.Combine(StreamlineFolder, rel.Replace('/', '\\'));
        }
        return null;
    }

    private async Task<bool> StageAsync(Release rel)
    {
        var temp = StagingDir + ".tmp";
        try
        {
            _crashReporter.Log($"[HsfService.Stage] Downloading {rel.AssetName} ({rel.Version})");
            var bytes = await DownloadAsync(rel).ConfigureAwait(false);
            if (bytes == null)
            {
                LastError = "Couldn't download HSF. While HSF is invite-only, add a GitHub token for an account that has access (Settings › Accounts).";
                return false;
            }

            if (Directory.Exists(temp)) Directory.Delete(temp, true);
            Directory.CreateDirectory(temp);
            int count = 0;
            using (var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
            {
                foreach (var entry in zip.Entries)
                {
                    if (StagedPathFor(entry.FullName) is not { } rel2) continue;
                    var dest = FileHelper.SafeCombine(temp, rel2);
                    if (dest == null) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    entry.ExtractToFile(dest, overwrite: true);
                    count++;
                }
            }
            File.WriteAllText(Path.Combine(temp, "version.txt"), rel.Version);
            if (!IsReady(temp))
            {
                // Upstream changed the package layout: never record a version without the files
                _crashReporter.Log($"[HsfService.Stage] {rel.AssetName} is missing expected files — not staged");
                LastError = "This HSF release has a layout RHI doesn't know yet.";
                Directory.Delete(temp, true);
                return false;
            }
            if (Directory.Exists(StagingDir)) Directory.Delete(StagingDir, true);
            Directory.Move(temp, StagingDir);
            _crashReporter.Log($"[HsfService.Stage] Staged {rel.Version} ({count} files)");

            await AntivirusGuard.VerifyStagedAsync(DisplayName, rel.Version, new[]
            {
                Path.Combine(StagingDir, Addon64), Path.Combine(StagingDir, Addon32),
                Path.Combine(StagingDir, StreamlineFolder, HelperExe),
            }).ConfigureAwait(false);
            return IsStagingReady;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _crashReporter.Log($"[HsfService.Stage] {rel.Version} failed — {ex.Message}");
            if (AntivirusGuard.IsVirusError(ex))
                AntivirusGuard.Report(DisplayName, rel.Version, Path.Combine(StagingDir, StreamlineFolder, HelperExe));
            LastError = $"Couldn't unpack HSF: {ex.Message}";
            return false;
        }
        finally
        {
            try { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { _crashReporter.Log($"[HsfService.Stage] Temp folder cleanup failed — {ex.Message}"); }
        }
    }

    // ── Game folder ───────────────────────────────────────────────────────────

    /// <summary>Staged files a game gets (relative paths): its add-on and everything in HSF_streamline\.</summary>
    private static IEnumerable<string> DeployFiles(bool is32Bit)
    {
        yield return AddonFileFor(is32Bit);
        var sl = Path.Combine(StagingDir, StreamlineFolder);
        foreach (var f in Directory.EnumerateFiles(sl, "*", SearchOption.AllDirectories))
            yield return Path.Combine(StreamlineFolder, Path.GetRelativePath(sl, f));
    }

    /// <summary>
    /// Copies the add-on (matching the game's bitness), HSF_streamline\ and, when there's none yet,
    /// the release's clean HSF.ini into the addon folder. An existing HSF.ini (HSF's and the user's
    /// settings) is kept. Shaders are deployed by the shader sync (call DeployShadersForCardAsync).
    /// </summary>
    public void Install(string installPath, bool is32Bit, bool launchpadOn)
    {
        if (!IsStagingReady) throw new InvalidOperationException("HSF isn't downloaded");
        var addonDir = AddonDir(installPath);
        Directory.CreateDirectory(addonDir);
        var previous = RhiInstallManifest.GetComponentFiles(installPath, Component);
        var recorded = new List<string>();

        foreach (var rel in DeployFiles(is32Bit))
        {
            var dest = FileHelper.SafeCombine(addonDir, rel);
            if (dest == null) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(Path.Combine(StagingDir, rel), dest, overwrite: true);
            recorded.Add(rel);
        }

        var ini = Path.Combine(addonDir, IniName);
        if (!File.Exists(ini)) File.Copy(Path.Combine(StagingDir, IniName), ini);
        recorded.Add(IniName);

        WriteMarker(installPath, StagedVersion, launchpadOn);
        recorded.Add(Path.Combine(StreamlineFolder, MarkerName));

        RemoveStale(addonDir, previous, recorded);
        RhiInstallManifest.SetComponent(installPath, Component, recorded);
        _crashReporter.Log($"[HsfService.Install] {StagedVersion} ({(is32Bit ? "32" : "64")}-bit) → '{addonDir}'");
    }

    /// <summary>Deletes files an earlier RHI install placed that this one didn't (old release, other bitness).</summary>
    private void RemoveStale(string addonDir, IEnumerable<string> previous, List<string> current)
    {
        foreach (var rel in previous.Except(current, StringComparer.OrdinalIgnoreCase))
            if (FileHelper.SafeCombine(addonDir, rel) is { } path) TryDelete(path);
    }

    /// <summary>
    /// Brings an RHI install up to the staged version. Files are compared by size and date, so nothing
    /// is written when current. HSF.ini is kept. True if anything changed.
    /// </summary>
    public bool UpdateInstalled(string installPath, bool is32Bit)
    {
        if (!IsStagingReady || !IsRhiInstalled(installPath)) return false;
        var addonDir = AddonDir(installPath);
        var previous = RhiInstallManifest.GetComponentFiles(installPath, Component);
        var recorded = new List<string>();
        bool changed = false, failed = false;
        foreach (var rel in DeployFiles(is32Bit))
        {
            recorded.Add(rel);
            var dest = FileHelper.SafeCombine(addonDir, rel);
            if (dest == null) continue;
            var src = new FileInfo(Path.Combine(StagingDir, rel));
            var cur = new FileInfo(dest);
            if (cur.Exists && cur.Length == src.Length && cur.LastWriteTimeUtc == src.LastWriteTimeUtc) continue;
            try
            {
                Directory.CreateDirectory(cur.DirectoryName!);
                File.Copy(src.FullName, dest, overwrite: true);
                changed = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed = true;
                _crashReporter.Log($"[HsfService.UpdateInstalled] '{dest}' not updated (game running?) — {ex.Message}");
            }
        }
        recorded.Add(IniName);
        recorded.Add(Path.Combine(StreamlineFolder, MarkerName));
        if (failed) return changed; // try the rest next time; keep the old record and version

        RemoveStale(addonDir, previous, recorded);
        RhiInstallManifest.SetComponent(installPath, Component, recorded);
        var marker = ReadMarker(installPath);
        if (changed || marker.Version != StagedVersion)
        {
            try { WriteMarker(installPath, StagedVersion, marker.LaunchpadOn); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { _crashReporter.Log($"[HsfService.UpdateInstalled] Marker not updated — {ex.Message}"); }
        }
        if (changed) _crashReporter.Log($"[HsfService.UpdateInstalled] Updated to {StagedVersion} in '{addonDir}'");
        return changed;
    }

    /// <summary>
    /// Removes HSF as its guide describes: the add-on, HSF.ini, HSF_streamline\ (HSF's helper logs live
    /// there too) and the logs / camera profiles HSF writes next to its add-on. HSF.fx and LaunchPad
    /// leave with the next shader sync (the caller runs it).
    /// </summary>
    public void Uninstall(string installPath)
    {
        if (string.IsNullOrEmpty(installPath)) return;
        var addonDir = AddonDir(installPath);
        foreach (var rel in RhiInstallManifest.GetComponentFiles(installPath, Component))
            if (FileHelper.SafeCombine(addonDir, rel) is { } path) TryDelete(path);
        foreach (var name in new[] { Addon64, Addon32, IniName }.Concat(HsfLogs))
            TryDelete(Path.Combine(addonDir, name));
        foreach (var dir in new[] { StreamlineFolder, ProfilesFolder })
        {
            var full = Path.Combine(addonDir, dir);
            try { if (Directory.Exists(full)) Directory.Delete(full, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { _crashReporter.Log($"[HsfService.Uninstall] Couldn't remove '{full}' — {ex.Message}"); }
        }
        RhiInstallManifest.RemoveComponent(installPath, Component);
        _crashReporter.Log($"[HsfService.Uninstall] Removed from '{addonDir}'");
    }

    private void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { _crashReporter.Log($"[HsfService] Couldn't delete '{path}' — {ex.Message}"); }
    }
}
