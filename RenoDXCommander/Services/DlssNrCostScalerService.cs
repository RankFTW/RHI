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
///   3. Copy nvngx_dlssnr.ini from staging
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
    private const string GitHubApiUrl = RHI.Core.Sources.CostScalerLatest;

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
                    var dest = Path.Combine(_stagingDir, name);
                    using var src = entry.Open();
                    using var dst = File.Create(dest);
                    await src.CopyToAsync(dst).ConfigureAwait(false);
                }
            }

            File.WriteAllText(_versionFile, version);
            HasUpdate = false;
            _crashReporter.Log($"[DlssNrCostScalerService.EnsureStaging] Staged v{version}");
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
            if (forceRefresh)
            {
                // Bypass ETag cache — make a direct request to get current data
                var req = new HttpRequestMessage(HttpMethod.Get, GitHubApiUrl);
                req.Headers.Add("User-Agent", "RHI");
                var resp = await _http.SendAsync(req).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    _crashReporter.Log($"[DlssNrCostScalerService.FetchRelease] HTTP {(int)resp.StatusCode}");
                    return (null, null);
                }
                json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            else
            {
                json = await _etagCache.GetWithETagAsync(_http, GitHubApiUrl).ConfigureAwait(false);
            }
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
            File.Copy(Path.Combine(_stagingDir, IniFileName),  iniDst,   overwrite: true);

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
                try { if (File.Exists(proxyDst)) File.Delete(proxyDst); } catch { }
                File.Move(realDst, proxyDst);
                _crashReporter.Log($"[DlssNrCostScalerService.Uninstall] Restored '{ProxyDllName}' from '{RealDllName}'");
            }
            else if (File.Exists(proxyDst))
            {
                // No _real — proxy was deployed to a game with no prior NR DLL. Delete it.
                File.Delete(proxyDst);
            }

            try { if (File.Exists(iniDst))   File.Delete(iniDst);   } catch { }
            try { if (File.Exists(addonDst)) File.Delete(addonDst); } catch { }

            _crashReporter.Log($"[DlssNrCostScalerService.Uninstall] Cleaned up '{installPath}'");
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[DlssNrCostScalerService.Uninstall] Failed — {ex.Message}");
        }
    }
}
