using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace RenoDXCommander.Services;

/// <summary>
/// Manages the DLSS NR Cost Scaler proxy — download, staging, and file operations.
///
/// Install contract (called AFTER the NR method has already deployed nvngx_dlssnr.dll):
///   1. Rename nvngx_dlssnr.dll → nvngx_dlssnr_real.dll
///   2. Copy proxy nvngx_dlssnr.dll from staging
///   3. Copy nvngx_dlssnr.ini from staging (only when absent: it holds the user's settings)
///   4. Copy dlssnr-companion.addon64 from staging
///
/// Uninstall contract (called BEFORE the NR method uninstall runs):
///   1. Delete proxy nvngx_dlssnr.dll
///   2. Rename nvngx_dlssnr_real.dll → nvngx_dlssnr.dll  (restores the real NR DLL)
///   3. Delete nvngx_dlssnr.ini
///   4. Delete dlssnr-companion.addon64
///   Then the normal NR uninstall removes nvngx_dlssnr.dll via its own sentinel pattern.
/// </summary>
public class DlssNrCostScalerService
{
    private const string GitHubApiUrl = "https://api.github.com/repos/xenmods/DLSSNR-Cost-Scaler/releases/latest";

    public const string ProxyDllName       = "nvngx_dlssnr.dll";
    public const string RealDllName        = "nvngx_dlssnr_real.dll";
    public const string IniFileName        = "nvngx_dlssnr.ini";
    public const string CompanionAddonName = "dlssnr-companion.addon64";

    private readonly HttpClient     _http;
    private readonly ICrashReporter _crashReporter;
    private readonly GitHubETagCache _etagCache;
    private readonly string         _stagingDir;
    private readonly string         _versionFile;

    public DlssNrCostScalerService(HttpClient http, ICrashReporter crashReporter, GitHubETagCache etagCache)
    {
        _http          = http;
        _crashReporter = crashReporter;
        _etagCache     = etagCache;
        _stagingDir    = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RHI", "dlssnr-cost-scaler");
        _versionFile   = Path.Combine(_stagingDir, "version.txt");
    }

    public string? StagedVersion => File.Exists(_versionFile)
        ? File.ReadAllText(_versionFile).Trim() : null;

    public bool IsStagingReady =>
        File.Exists(Path.Combine(_stagingDir, ProxyDllName)) &&
        File.Exists(Path.Combine(_stagingDir, IniFileName));

    public bool HasUpdate     { get; private set; }
    public string? LatestVersion { get; private set; }

    /// <summary>Returns true if the Cost Scaler proxy is active in the given game folder.</summary>
    public static bool IsInstalled(string installPath) =>
        !string.IsNullOrEmpty(installPath) &&
        File.Exists(Path.Combine(installPath, RealDllName));

    /// <summary>
    /// The file that holds the real NR DLL for <paramref name="nrDllPath"/>. When the Cost Scaler is
    /// installed in that folder (<see cref="RealDllName"/> beside it), <c>nvngx_dlssnr.dll</c> is the
    /// proxy and the real DLL is <c>nvngx_dlssnr_real.dll</c>; anything that swaps, replaces or restores
    /// the NR DLL must write that file instead, or the proxy is lost. Otherwise returns the path unchanged.
    /// </summary>
    public static string ResolveRealNrDllPath(string nrDllPath)
    {
        if (string.IsNullOrEmpty(nrDllPath)
            || !Path.GetFileName(nrDllPath).Equals(ProxyDllName, StringComparison.OrdinalIgnoreCase))
            return nrDllPath;
        var dir = Path.GetDirectoryName(nrDllPath);
        if (string.IsNullOrEmpty(dir)) return nrDllPath;
        var real = Path.Combine(dir, RealDllName);
        return File.Exists(real) ? real : nrDllPath;
    }

    /// <summary>
    /// When the Cost Scaler is installed beside <paramref name="nrDllPath"/>, replaces the real NR DLL
    /// behind the proxy with <paramref name="sourcePath"/> and returns true; returns false (nothing done)
    /// otherwise, so the caller does its normal replace. The backup keeps the NR DLL's own name
    /// (<c>nvngx_dlssnr.dll.original</c>, never replaced when present): the Cost Scaler uninstall moves
    /// the real DLL back to that name before the NR uninstall restores it.
    /// Throws <see cref="IOException"/> / <see cref="UnauthorizedAccessException"/> (backup undone).
    /// </summary>
    public static bool ReplaceRealNrDll(string nrDllPath, string sourcePath)
    {
        var real = ResolveRealNrDllPath(nrDllPath);
        if (string.Equals(real, nrDllPath, StringComparison.OrdinalIgnoreCase)) return false;

        var backup = nrDllPath + ".original";
        bool madeBackup = false;
        if (!File.Exists(backup))
        {
            File.Copy(real, backup);
            madeBackup = true;
        }
        try
        {
            File.Copy(sourcePath, real, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (madeBackup)
            {
                try { File.Delete(backup); }
                catch (Exception ex2) when (ex2 is IOException or UnauthorizedAccessException)
                { CrashReporter.Log($"[DlssNrCostScalerService.ReplaceRealNrDll] Failed to undo backup '{backup}' — {ex2.Message}"); }
            }
            throw;
        }
        CrashReporter.Log($"[DlssNrCostScalerService.ReplaceRealNrDll] Replaced '{real}' behind the Cost Scaler proxy");
        return true;
    }

    /// <summary>
    /// When the Cost Scaler is installed beside <paramref name="nrDllPath"/>, restores a real
    /// <c>nvngx_dlssnr.dll.original</c> into the real NR DLL behind the proxy and returns true (a 0-byte
    /// sentinel is left alone: the proxy needs the NR DLL RHI placed). Returns false when there is no
    /// Cost Scaler there, so the caller does its normal restore.
    /// </summary>
    public static bool RestoreRealNrDll(string nrDllPath)
    {
        var real = ResolveRealNrDllPath(nrDllPath);
        if (string.Equals(real, nrDllPath, StringComparison.OrdinalIgnoreCase)) return false;

        var backup = nrDllPath + ".original";
        try
        {
            if (!File.Exists(backup)) return true;
            if (new FileInfo(backup).Length == 0)
            {
                CrashReporter.Log($"[DlssNrCostScalerService.RestoreRealNrDll] Kept '{real}' — placed by RHI (0-byte .original), the Cost Scaler proxy uses it");
                return true;
            }
            File.Copy(backup, real, overwrite: true);
            File.Delete(backup);
            CrashReporter.Log($"[DlssNrCostScalerService.RestoreRealNrDll] Restored the original NR DLL into '{real}'");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[DlssNrCostScalerService.RestoreRealNrDll] Failed for '{real}' — {ex.Message}");
        }
        return true;
    }

    // ── Staging ───────────────────────────────────────────────────────────────

    public async Task EnsureStagingAsync()
    {
        if (IsStagingReady && !HasUpdate)
        {
            _crashReporter.Log("[DlssNrCostScalerService.EnsureStaging] Already up to date");
            return;
        }

        Directory.CreateDirectory(_stagingDir);

        var (version, downloadUrl) = await FetchLatestReleaseInfoAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(version) || string.IsNullOrEmpty(downloadUrl))
        {
            _crashReporter.Log("[DlssNrCostScalerService.EnsureStaging] Could not resolve latest release");
            return;
        }

        try
        {
            var bytes = await _http.GetByteArrayAsync(downloadUrl).ConfigureAwait(false);
            using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);

            foreach (var entry in zip.Entries)
            {
                var name = Path.GetFileName(entry.FullName);
                if (string.IsNullOrEmpty(name)) continue;
                if (name.Equals(ProxyDllName, StringComparison.OrdinalIgnoreCase) ||
                    name.Equals(IniFileName, StringComparison.OrdinalIgnoreCase) ||
                    name.Equals(CompanionAddonName, StringComparison.OrdinalIgnoreCase))
                {
                    // Through a temp file: a failed extraction never leaves a partial file that IsStagingReady accepts
                    var dest = Path.Combine(_stagingDir, name);
                    await FileHelper.WriteStreamAtomicAsync(entry.Open, dest, entry.Length).ConfigureAwait(false);
                }
            }

            var previousVersion = StagedVersion;
            File.WriteAllText(_versionFile, version);
            HasUpdate = false;
            _crashReporter.Log($"[DlssNrCostScalerService.EnsureStaging] Staged v{version}");
            UpdateHistory.Record("Neural Rendering", "DLSSNR Cost Scaler", previousVersion, version);
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[DlssNrCostScalerService.EnsureStaging] Download failed — {ex.Message}");
        }
    }

    public async Task CheckForUpdateAsync(bool forceRefresh = false)
    {
        var (version, _) = await FetchLatestReleaseInfoAsync(forceRefresh).ConfigureAwait(false);
        if (string.IsNullOrEmpty(version)) return;

        LatestVersion = version;
        var current = StagedVersion;
        HasUpdate = !string.Equals(current, version, StringComparison.OrdinalIgnoreCase);
        _crashReporter.Log($"[DlssNrCostScalerService.CheckForUpdate] Cached={current ?? "(none)"}, Remote={version}, HasUpdate={HasUpdate}");
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
                if (assetName?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true)
                {
                    var url = asset.TryGetProperty("browser_download_url", out var up) ? up.GetString() : null;
                    return (tag, url);
                }
            }
            return (tag, null);
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[DlssNrCostScalerService.FetchRelease] {ex.Message}");
            return (null, null);
        }
    }

    // ── Install ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Called AFTER the NR method has deployed nvngx_dlssnr.dll.
    /// Renames it to _real.dll and deploys the proxy, INI, and companion addon.
    /// </summary>
    public void Install(string installPath)
    {
        if (string.IsNullOrEmpty(installPath) || !IsStagingReady) return;

        var proxyDst  = Path.Combine(installPath, ProxyDllName);
        var realDst   = Path.Combine(installPath, RealDllName);
        var iniDst    = Path.Combine(installPath, IniFileName);
        var addonDst  = Path.Combine(installPath, CompanionAddonName);

        try
        {
            // Rename the real NR DLL to _real if it's there and hasn't been renamed yet
            if (File.Exists(proxyDst) && !File.Exists(realDst))
            {
                File.Move(proxyDst, realDst);
                _crashReporter.Log($"[DlssNrCostScalerService.Install] Renamed '{ProxyDllName}' → '{RealDllName}'");
            }

            // Deploy proxy, INI, companion
            File.Copy(Path.Combine(_stagingDir, ProxyDllName), proxyDst, overwrite: true);
            // The ini holds the user's settings: only seed it, never reset it on update / reinstall
            if (!File.Exists(iniDst))
                File.Copy(Path.Combine(_stagingDir, IniFileName), iniDst);
            else
                _crashReporter.Log($"[DlssNrCostScalerService.Install] Kept existing '{IniFileName}'");

            var stagedAddon = Path.Combine(_stagingDir, CompanionAddonName);
            if (File.Exists(stagedAddon))
                File.Copy(stagedAddon, addonDst, overwrite: true);

            _crashReporter.Log($"[DlssNrCostScalerService.Install] Deployed to '{installPath}'");
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[DlssNrCostScalerService.Install] Failed — {ex.Message}");
        }
    }

    // ── Uninstall ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Called BEFORE the NR method uninstall runs.
    /// Restores _real.dll → nvngx_dlssnr.dll so the normal NR uninstall can handle it.
    /// </summary>
    public void Uninstall(string installPath)
    {
        if (string.IsNullOrEmpty(installPath)) return;

        var proxyDst = Path.Combine(installPath, ProxyDllName);
        var realDst  = Path.Combine(installPath, RealDllName);
        var iniDst   = Path.Combine(installPath, IniFileName);
        var addonDst = Path.Combine(installPath, CompanionAddonName);

        try
        {
            // Delete proxy and restore real DLL
            if (File.Exists(realDst))
            {
                try { if (File.Exists(proxyDst)) File.Delete(proxyDst); } catch (Exception ex) { _crashReporter.Log($"[DlssNrCostScalerService.Uninstall] Failed to delete '{ProxyDllName}' before restore — {ex.Message}"); }
                File.Move(realDst, proxyDst);
                _crashReporter.Log($"[DlssNrCostScalerService.Uninstall] Restored '{ProxyDllName}' from '{RealDllName}'");
            }
            else if (File.Exists(proxyDst))
            {
                // No _real — proxy was deployed to a game with no prior NR DLL. Delete it.
                File.Delete(proxyDst);
            }

            try { if (File.Exists(iniDst))   File.Delete(iniDst);   } catch (Exception ex) { _crashReporter.Log($"[DlssNrCostScalerService.Uninstall] Failed to delete '{IniFileName}' — {ex.Message}"); }
            try { if (File.Exists(addonDst)) File.Delete(addonDst); } catch (Exception ex) { _crashReporter.Log($"[DlssNrCostScalerService.Uninstall] Failed to delete '{CompanionAddonName}' — {ex.Message}"); }

            _crashReporter.Log($"[DlssNrCostScalerService.Uninstall] Cleaned up '{installPath}'");
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[DlssNrCostScalerService.Uninstall] Failed — {ex.Message}");
        }
    }
}
