using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace RenoDXCommander.Services;

/// <summary>
/// Manages the Universal MFG Unlocker component (formerly RTX 40 MFG Unlock) — staging, install, and uninstall.
/// GitHub: dashdogy/MFG-Unlocker (formerly dashdogy/RTX40MFG-Unlock).
/// Deploys a single DLL renamed to a user-chosen proxy DLL name (e.g. version.dll, dinput8.dll).
/// Releases ship it as MFGUnlocker.dll (v1.5+) or RTXMFG.dll (older); it is always staged as RTXMFG.dll.
/// </summary>
public class Rtx40MfgService
{
    private const string GitHubApiUrl = "https://api.github.com/repos/dashdogy/MFG-Unlocker/releases/latest";

    /// <summary>The filename of the staged/zip-extracted DLL.</summary>
    public const string StagedDllName = "RTXMFG.dll";

    /// <summary>Legacy filename — only used for migration detection.</summary>
    public const string AsiFileName   = "RTX40MFG.asi";

    /// <summary>DLL names accepted from a release zip, newest naming first.</summary>
    private static readonly string[] ReleaseDllNames = { "MFGUnlocker.dll", StagedDllName };

    /// <summary>
    /// Written after a verified extraction. Staging without it came from a build that could
    /// record a new version without extracting the DLL (MFGUnlocker.dll rename), so it is re-downloaded once.
    /// </summary>
    private const string StagingFormatFile = "staging_format.txt";
    private const string StagingFormat     = "2";

    /// <summary>All valid proxy DLL names that RTXMFG.dll may be deployed as.</summary>
    public static readonly string[] KnownProxyNames =
    {
        "version.dll", "dinput8.dll", "winmm.dll", "d3d9.dll", "d3d10.dll",
        "d3d11.dll", "d3d12.dll", "dxgi.dll", "dsound.dll", "wininet.dll",
        "winhttp.dll", "binkw64.dll", "bink2w64.dll", "xinput1_1.dll",
        "xinput1_2.dll", "xinput1_3.dll", "xinput1_4.dll", "xinput9_1_0.dll",
        "xinputuap.dll",
    };

    private readonly HttpClient      _http;
    private readonly ICrashReporter  _crashReporter;
    private readonly GitHubETagCache _etagCache;
    private readonly string          _stagingDir;
    private readonly string          _versionFile;

    public Rtx40MfgService(HttpClient http, ICrashReporter crashReporter, GitHubETagCache etagCache)
        : this(http, crashReporter, etagCache,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "rtx40mfg"))
    { }

    /// <summary>Tests: use a throwaway staging folder. (DI only uses the public constructor.)</summary>
    internal Rtx40MfgService(HttpClient http, ICrashReporter crashReporter, GitHubETagCache etagCache, string stagingDir)
    {
        _http          = http;
        _crashReporter = crashReporter;
        _etagCache     = etagCache;
        _stagingDir    = stagingDir;
        _versionFile   = Path.Combine(_stagingDir, "version.txt");

        // One-time migration: wipe old ASI-based staging so new RTXMFG.dll will be downloaded
        var legacyAsi = Path.Combine(_stagingDir, AsiFileName);
        if (File.Exists(legacyAsi))
        {
            try
            {
                foreach (var f in Directory.GetFiles(_stagingDir))
                    File.Delete(f);
                _crashReporter.Log("[Rtx40MfgService] Wiped legacy ASI staging — new RTXMFG.dll will be downloaded");
            }
            catch (Exception ex) { CrashReporter.Log($"[Rtx40MfgService] Failed to wipe legacy ASI staging — {ex.Message}"); }
        }

        // One-time migration: older builds wrote version.txt even when the release zip had no
        // RTXMFG.dll (upstream renamed it to MFGUnlocker.dll), so the staged DLL can be older than
        // version.txt claims. Drop version.txt (keep the DLL so installs still work) to force one re-download.
        if (File.Exists(_versionFile) && !File.Exists(Path.Combine(_stagingDir, StagingFormatFile)))
        {
            try
            {
                File.Delete(_versionFile);
                _crashReporter.Log("[Rtx40MfgService] Staging predates format 2 — version cleared to force a fresh download");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _crashReporter.Log($"[Rtx40MfgService] Could not clear stale version file — {ex.Message}");
            }
        }
    }

    public string? StagedVersion => File.Exists(_versionFile)
        ? File.ReadAllText(_versionFile).Trim() : null;

    /// <summary>Returns true when RTXMFG.dll is present in the staging directory.</summary>
    public bool IsStagingReady => File.Exists(Path.Combine(_stagingDir, StagedDllName));

    public bool HasUpdate        { get; private set; }
    public string? LatestVersion { get; private set; }

    /// <summary>Returns true if the component is installed in the given game folder (by stored DLL name).</summary>
    public bool IsInstalledIn(string installPath, string? installedAs) =>
        !string.IsNullOrEmpty(installPath) &&
        !string.IsNullOrEmpty(installedAs) &&
        File.Exists(Path.Combine(installPath, installedAs));

    // ── Staging ───────────────────────────────────────────────────────────────

    // ── OptiScaler plugin install ─────────────────────────────────────────────
    // With an OptiScaler 10 build (nightly, F5, DLSS NR) the mod's author says to load it as an
    // OptiScaler plugin (MFGUnlocker.asi in OptiScaler\plugins) and not as a proxy DLL as well.
    // RHI keeps the per-game "installed as" value as the relative path below, so every
    // Path.Combine(installPath, installedAs) check keeps working.

    public const string PluginFileName = "MFGUnlocker.asi";
    public static readonly string PluginRelPath = Path.Combine("OptiScaler", "plugins", PluginFileName);
    /// <summary>Remembers the proxy name the mod had before moving into OptiScaler\plugins.</summary>
    private const string PreviousProxyFile = "MFGUnlocker.rhi-proxy.txt";

    /// <summary>True when <paramref name="installedAs"/> is the OptiScaler plugin location.</summary>
    public static bool IsPluginInstall(string? installedAs) =>
        !string.IsNullOrEmpty(installedAs)
        && installedAs.Replace('/', '\\').Equals(PluginRelPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Moves a proxy-DLL install into OptiScaler\plugins as MFGUnlocker.asi. The proxy slot is
    /// handed back (the game's own file there is restored) and RHI's RTXMFG-XeSS copy is removed:
    /// XeMFG through OptiScaler uses OptiScaler's XeSS files. Returns the new installed-as value,
    /// or null when nothing was moved.
    /// </summary>
    public string? MoveToOptiScalerPlugin(string installPath, string proxyName)
    {
        if (string.IsNullOrEmpty(installPath) || string.IsNullOrEmpty(proxyName) || IsPluginInstall(proxyName)) return null;
        var src  = Path.Combine(installPath, proxyName);
        var dest = Path.Combine(installPath, PluginRelPath);
        if (!File.Exists(src)) return null;
        try
        {
            var pluginsDir = Path.GetDirectoryName(dest)!;
            Directory.CreateDirectory(pluginsDir);
            AuxInstallService.SentinelBackup(dest);   // 0-byte sentinel: RHI placed this file
            File.Copy(src, dest, overwrite: true);
            File.WriteAllText(Path.Combine(pluginsDir, PreviousProxyFile), proxyName);

            AuxInstallService.SentinelRestore(src);   // removes the proxy, restores the game's own file if it had one
            // Very old installs have no sentinel, so SentinelRestore leaves the file: never keep two copies loaded
            if (File.Exists(src) && FilesEqual(src, dest)) File.Delete(src);

            RemoveXessFiles(installPath);
            _crashReporter.Log($"[Rtx40MfgService.MoveToOptiScalerPlugin] '{proxyName}' → '{PluginRelPath}' in '{installPath}'");
            return PluginRelPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"[Rtx40MfgService.MoveToOptiScalerPlugin] Failed in '{installPath}' — {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Moves the mod out of OptiScaler\plugins back to a proxy DLL next to the game's exe
    /// (the name it had before, else version.dll, else the first free name) and puts RHI's XeSS
    /// files back. Used before OptiScaler is removed. Returns the new proxy name, or null.
    /// </summary>
    public string? MoveOutOfOptiScalerPlugin(string installPath)
    {
        if (string.IsNullOrEmpty(installPath)) return null;
        var pluginPath = Path.Combine(installPath, PluginRelPath);
        if (!File.Exists(pluginPath)) return null;
        var pluginsDir = Path.GetDirectoryName(pluginPath)!;
        var marker     = Path.Combine(pluginsDir, PreviousProxyFile);
        try
        {
            string? preferred = File.Exists(marker) ? File.ReadAllText(marker).Trim() : null;
            if (string.IsNullOrEmpty(preferred) || !KnownProxyNames.Contains(preferred, StringComparer.OrdinalIgnoreCase))
                preferred = "version.dll";
            var name = new[] { preferred, "version.dll", "winmm.dll", "dinput8.dll" }
                .Concat(KnownProxyNames)
                .FirstOrDefault(n => !File.Exists(Path.Combine(installPath, n)));
            if (name == null)
            {
                _crashReporter.Log($"[Rtx40MfgService.MoveOutOfOptiScalerPlugin] No free proxy name in '{installPath}'");
                return null;
            }

            var dest = Path.Combine(installPath, name);
            AuxInstallService.SentinelBackup(dest);
            File.Copy(pluginPath, dest, overwrite: true);
            AuxInstallService.SentinelRestore(pluginPath);
            if (File.Exists(pluginPath) && FilesEqual(pluginPath, dest)) File.Delete(pluginPath);
            if (File.Exists(marker)) File.Delete(marker);

            // Standalone XeMFG needs RTXMFG-XeSS next to the proxy again
            if (IsXessStagingReady) DeployXessFiles(installPath);
            else
                _ = Task.Run(async () =>
                {
                    try { await EnsureXessStagingAsync().ConfigureAwait(false); DeployXessFiles(installPath); }
                    catch (Exception ex) { _crashReporter.Log($"[Rtx40MfgService.MoveOutOfOptiScalerPlugin] XeSS redeploy failed — {ex.Message}"); }
                });

            _crashReporter.Log($"[Rtx40MfgService.MoveOutOfOptiScalerPlugin] '{PluginRelPath}' → '{name}' in '{installPath}'");
            return name;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"[Rtx40MfgService.MoveOutOfOptiScalerPlugin] Failed in '{installPath}' — {ex.Message}");
            return null;
        }
    }

    private static bool FilesEqual(string a, string b)
    {
        var fa = new FileInfo(a); var fb = new FileInfo(b);
        if (fa.Length != fb.Length) return false;
        return File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
    }

    // ── Intel XeSS frame generation runtime (for the mod's standalone XeMFG) ──
    // The mod's zip has only MFGUnlocker.dll. Standalone XeMFG also needs Intel's libxess_fg.dll and
    // libxell.dll in a folder named RTXMFG-XeSS beside the mod's DLL. Intel publishes them in its XeSS
    // repo; the mod's author tested SDK 3.0.2, so the download is pinned to that tag.

    /// <summary>Folder (beside the mod's DLL) the mod looks in for Intel's XeSS FG runtime.</summary>
    public const string XessFolderName = "RTXMFG-XeSS";
    private const string XessSdkTag = "v3.0.2";
    private static readonly string[] XessFiles = { "libxess_fg.dll", "libxell.dll" };
    /// <summary>Lists the files RHI put in RTXMFG-XeSS, so uninstall removes only those.</summary>
    private const string XessMarkerFile = "rhi_installed.txt";

    private string XessStagingDir => Path.Combine(_stagingDir, "xess");

    public bool IsXessStagingReady => XessFiles.All(f => File.Exists(Path.Combine(XessStagingDir, f)));

    /// <summary>Downloads Intel's XeSS FG runtime once. No-op when already staged.</summary>
    public async Task EnsureXessStagingAsync()
    {
        if (IsXessStagingReady) return;
        Directory.CreateDirectory(XessStagingDir);
        foreach (var file in XessFiles)
        {
            var dest = Path.Combine(XessStagingDir, file);
            if (File.Exists(dest)) continue;
            var url = $"https://raw.githubusercontent.com/intel/xess/{XessSdkTag}/bin/{file}";
            try
            {
                var bytes = await _http.GetByteArrayAsync(url).ConfigureAwait(false);
                // A real DLL starts with "MZ"; anything else (an error page, a Git LFS pointer) is rejected
                if (bytes.Length < 1024 || bytes[0] != (byte)'M' || bytes[1] != (byte)'Z')
                {
                    _crashReporter.Log($"[Rtx40MfgService.EnsureXessStaging] '{file}' from {url} is not a DLL ({bytes.Length} bytes) — skipped");
                    return;
                }
                var temp = dest + ".tmp";
                await File.WriteAllBytesAsync(temp, bytes).ConfigureAwait(false);
                File.Move(temp, dest, overwrite: true);
                _crashReporter.Log($"[Rtx40MfgService.EnsureXessStaging] Staged {file} ({bytes.Length} bytes, XeSS SDK {XessSdkTag})");
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException)
            {
                _crashReporter.Log($"[Rtx40MfgService.EnsureXessStaging] Download of '{file}' failed — {ex.Message}");
                return;
            }
        }
    }

    /// <summary>
    /// Copies the XeSS runtime into &lt;installPath&gt;\RTXMFG-XeSS. Files the user put there
    /// themselves are left alone; files RHI put there before are refreshed.
    /// </summary>
    public bool DeployXessFiles(string installPath)
    {
        if (string.IsNullOrEmpty(installPath) || !IsXessStagingReady) return false;
        var dir    = Path.Combine(installPath, XessFolderName);
        var marker = Path.Combine(dir, XessMarkerFile);
        try
        {
            Directory.CreateDirectory(dir);
            var owned = ReadXessMarker(marker);
            foreach (var file in XessFiles)
            {
                var dest = Path.Combine(dir, file);
                if (File.Exists(dest) && !owned.Contains(file))
                {
                    _crashReporter.Log($"[Rtx40MfgService.DeployXessFiles] Keeping the user's own '{file}' in '{dir}'");
                    continue;
                }
                File.Copy(Path.Combine(XessStagingDir, file), dest, overwrite: true);
                owned.Add(file);
            }
            File.WriteAllLines(marker, owned);
            _crashReporter.Log($"[Rtx40MfgService.DeployXessFiles] XeSS runtime in '{dir}'");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"[Rtx40MfgService.DeployXessFiles] Failed for '{dir}' — {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// True when RHI has never set up RTXMFG-XeSS for this game and the user hasn't added the files.
    /// Once RHI has deployed them, missing files are treated as removed by the user and not re-created.
    /// </summary>
    public static bool NeedsXessFiles(string installPath)
    {
        if (string.IsNullOrEmpty(installPath)) return false;
        var dir = Path.Combine(installPath, XessFolderName);
        if (File.Exists(Path.Combine(dir, XessMarkerFile))) return false;
        return !XessFiles.All(f => File.Exists(Path.Combine(dir, f)));
    }

    private static HashSet<string> ReadXessMarker(string marker)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(marker)) return set;
        foreach (var line in File.ReadAllLines(marker))
            if (XessFiles.Contains(line.Trim(), StringComparer.OrdinalIgnoreCase)) set.Add(line.Trim());
        return set;
    }

    private void RemoveXessFiles(string installPath)
    {
        var dir    = Path.Combine(installPath, XessFolderName);
        var marker = Path.Combine(dir, XessMarkerFile);
        if (!File.Exists(marker)) return; // RHI didn't create it — leave the user's files alone
        try
        {
            foreach (var file in ReadXessMarker(marker))
            {
                var p = Path.Combine(dir, file);
                if (File.Exists(p)) File.Delete(p);
            }
            File.Delete(marker);
            if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
            _crashReporter.Log($"[Rtx40MfgService.RemoveXessFiles] Removed RHI's XeSS runtime from '{dir}'");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"[Rtx40MfgService.RemoveXessFiles] Failed for '{dir}' — {ex.Message}");
        }
    }

    public async Task EnsureStagingAsync()
    {
        if (IsStagingReady && !HasUpdate)
        {
            _crashReporter.Log("[Rtx40MfgService.EnsureStaging] Already up to date");
            return;
        }

        Directory.CreateDirectory(_stagingDir);

        var (version, downloadUrl) = await FetchLatestReleaseInfoAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(version) || string.IsNullOrEmpty(downloadUrl))
        {
            _crashReporter.Log("[Rtx40MfgService.EnsureStaging] Could not resolve latest release");
            return;
        }

        try
        {
            var bytes = await _http.GetByteArrayAsync(downloadUrl).ConfigureAwait(false);
            using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);

            ZipArchiveEntry? dllEntry = null;
            foreach (var wanted in ReleaseDllNames)
            {
                dllEntry = zip.Entries.FirstOrDefault(e =>
                    Path.GetFileName(e.FullName).Equals(wanted, StringComparison.OrdinalIgnoreCase));
                if (dllEntry != null) break;
            }

            if (dllEntry == null)
            {
                // Don't record the new version — the staged DLL would not match it
                _crashReporter.Log($"[Rtx40MfgService.EnsureStaging] {version} zip has none of [{string.Join(", ", ReleaseDllNames)}] — keeping staged {StagedVersion ?? "(none)"}");
                return;
            }

            // Extract to a temp file first so a failed write never leaves a half-written staged DLL
            var dest = Path.Combine(_stagingDir, StagedDllName);
            var temp = dest + ".tmp";
            using (var src = dllEntry.Open())
            using (var dst = File.Create(temp))
                await src.CopyToAsync(dst).ConfigureAwait(false);
            File.Move(temp, dest, overwrite: true);

            File.WriteAllText(_versionFile, version);
            File.WriteAllText(Path.Combine(_stagingDir, StagingFormatFile), StagingFormat);
            HasUpdate = false;
            _crashReporter.Log($"[Rtx40MfgService.EnsureStaging] Staged {version} from '{dllEntry.FullName}'");
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[Rtx40MfgService.EnsureStaging] Download failed — {ex.Message}");
        }
    }

    public async Task CheckForUpdateAsync(bool forceRefresh = false)
    {
        var (version, _) = await FetchLatestReleaseInfoAsync(forceRefresh).ConfigureAwait(false);
        if (string.IsNullOrEmpty(version)) return;

        LatestVersion = version;
        var current = StagedVersion;
        HasUpdate = !string.Equals(current, version, StringComparison.OrdinalIgnoreCase);
        _crashReporter.Log($"[Rtx40MfgService.CheckForUpdate] Cached={current ?? "(none)"}, Remote={version}, HasUpdate={HasUpdate}");
    }

    private async Task<(string? version, string? downloadUrl)> FetchLatestReleaseInfoAsync(bool forceRefresh = false)
    {
        try
        {
            string? json;
            // An ETag request always returns current data, so forced checks use the cache too.
            json = await _etagCache.GetWithETagAsync(_http, GitHubApiUrl).ConfigureAwait(false);
            if (json == null) return (null, null);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() : null;
            if (string.IsNullOrEmpty(tag)) return (null, null);

            if (!root.TryGetProperty("assets", out var assets)) return (null, null);
            foreach (var asset in assets.EnumerateArray())
            {
                var assetName = asset.TryGetProperty("name", out var np) ? np.GetString() : null;
                if (assetName?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true
                    && !assetName.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase))
                {
                    var url = asset.TryGetProperty("browser_download_url", out var up) ? up.GetString() : null;
                    return (tag, url);
                }
            }
            return (tag, null);
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[Rtx40MfgService.FetchRelease] {ex.Message}");
            return (null, null);
        }
    }

    // ── Install ───────────────────────────────────────────────────────────────

    /// <summary>
    /// True when the game's installed copy exists and differs from the staged DLL.
    /// Used to push updates by content rather than by the HasUpdate flag, so games
    /// that missed an update (e.g. the game was running) are caught up next time.
    /// </summary>
    public bool IsOutdatedIn(string installPath, string installedAs)
    {
        var src  = Path.Combine(_stagingDir, StagedDllName);
        var dest = Path.Combine(installPath, installedAs);
        try
        {
            if (!File.Exists(src) || !File.Exists(dest)) return false;
            if (new FileInfo(src).Length != new FileInfo(dest).Length) return true;
            return !File.ReadAllBytes(src).AsSpan().SequenceEqual(File.ReadAllBytes(dest));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"[Rtx40MfgService.IsOutdatedIn] Could not compare '{dest}' — {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Copies the staged DLL into every listed game whose copy is outdated. Returns how many were updated.
    /// </summary>
    public int UpdateInstalledGames(IEnumerable<(string InstallPath, string InstalledAs)> installs)
    {
        if (!IsStagingReady) return 0;
        var list = installs.ToList();
        int updated = 0;
        foreach (var (installPath, installedAs) in list)
        {
            if (!IsOutdatedIn(installPath, installedAs)) continue;
            if (Install(installPath, installedAs))
            {
                updated++;
                _crashReporter.Log($"[Rtx40MfgService.UpdateInstalledGames] Updated '{installedAs}' in '{installPath}' to {StagedVersion}");
            }
        }

        // Games installed before RHI added the XeSS runtime get it once
        var needXess = list
            .Where(i => !IsPluginInstall(i.InstalledAs) // OptiScaler plugin installs use OptiScaler's XeSS files
                && IsInstalledIn(i.InstallPath, i.InstalledAs) && NeedsXessFiles(i.InstallPath))
            .Select(i => i.InstallPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (needXess.Count > 0)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await EnsureXessStagingAsync().ConfigureAwait(false);
                    foreach (var path in needXess) DeployXessFiles(path);
                }
                catch (Exception ex) { _crashReporter.Log($"[Rtx40MfgService.UpdateInstalledGames] XeSS catch-up failed — {ex.Message}"); }
            });
        }
        return updated;
    }

    /// <summary>
    /// Deploys RTXMFG.dll to the game folder renamed to <paramref name="dllName"/>.
    /// Writes a sentinel so uninstall knows whether to restore or delete.
    /// </summary>
    public bool Install(string installPath, string dllName)
    {
        if (string.IsNullOrEmpty(installPath) || !IsStagingReady) return false;
        try
        {
            var src  = Path.Combine(_stagingDir, StagedDllName);
            var dest = Path.Combine(installPath, dllName);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!); // OptiScaler\plugins for plugin installs
            AuxInstallService.SentinelBackup(dest);
            File.Copy(src, dest, overwrite: true);
            _crashReporter.Log($"[Rtx40MfgService.Install] Deployed as '{dllName}' to '{installPath}'");
            // XeSS runtime for standalone XeMFG. Not fatal: DLSS frame generation works without it.
            // Not for OptiScaler plugin installs: XeMFG there uses OptiScaler's own XeSS files.
            if (IsPluginInstall(dllName)) { }
            else if (IsXessStagingReady) DeployXessFiles(installPath);
            else _crashReporter.Log("[Rtx40MfgService.Install] XeSS runtime not staged — RTXMFG-XeSS skipped");
            return true;
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[Rtx40MfgService.Install] Failed — {ex.Message}");
            return false;
        }
    }

    // ── Uninstall ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Removes the installed DLL using sentinel restore.
    /// Falls back to deleting legacy ASI files if <paramref name="installedAs"/> is null.
    /// </summary>
    public void Uninstall(string installPath, string? installedAs)
    {
        if (string.IsNullOrEmpty(installPath)) return;
        try
        {
            if (!string.IsNullOrEmpty(installedAs))
            {
                AuxInstallService.SentinelRestore(Path.Combine(installPath, installedAs));
                RemoveXessFiles(installPath);
                if (IsPluginInstall(installedAs))
                {
                    var marker = Path.Combine(installPath, Path.GetDirectoryName(PluginRelPath)!, PreviousProxyFile);
                    if (File.Exists(marker)) File.Delete(marker);
                }
            }
            else
            {
                // Legacy fallback: remove the old ASI-based files
                foreach (var legacyFile in new[] { AsiFileName, "RTX40MFGCore.dll", "RTX40MFG-UI.addon64" })
                {
                    var p = Path.Combine(installPath, legacyFile);
                    try { if (File.Exists(p)) File.Delete(p); } catch (Exception ex) { CrashReporter.Log($"[Rtx40MfgService.Uninstall] Failed to delete legacy file — {ex.Message}"); }
                }
            }
            _crashReporter.Log($"[Rtx40MfgService.Uninstall] Removed from '{installPath}'");
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[Rtx40MfgService.Uninstall] Failed — {ex.Message}");
        }
    }
}
