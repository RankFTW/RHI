using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace RenoDXCommander.Services;

/// <summary>
/// Manages the RTX 40 MFG Unlock component (upstream now "MFG Unlocker") — staging, install, and uninstall.
/// GitHub: dashdogy/RTX40MFG-Unlock (renamed to dashdogy/MFG-Unlocker; the old URL redirects).
/// Deploys a single DLL renamed to a user-chosen proxy DLL name (e.g. version.dll, dinput8.dll).
/// Releases ship it as MFGUnlocker.dll (v1.5+) or RTXMFG.dll (older); it is always staged as RTXMFG.dll.
/// </summary>
public class Rtx40MfgService
{
    private const string GitHubApiUrl = "https://api.github.com/repos/dashdogy/RTX40MFG-Unlock/releases/latest";

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
    {
        _http          = http;
        _crashReporter = crashReporter;
        _etagCache     = etagCache;
        _stagingDir    = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RHI", "rtx40mfg");
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
        int updated = 0;
        foreach (var (installPath, installedAs) in installs)
        {
            if (!IsOutdatedIn(installPath, installedAs)) continue;
            if (Install(installPath, installedAs))
            {
                updated++;
                _crashReporter.Log($"[Rtx40MfgService.UpdateInstalledGames] Updated '{installedAs}' in '{installPath}' to {StagedVersion}");
            }
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
            AuxInstallService.SentinelBackup(dest);
            File.Copy(src, dest, overwrite: true);
            _crashReporter.Log($"[Rtx40MfgService.Install] Deployed as '{dllName}' to '{installPath}'");
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
