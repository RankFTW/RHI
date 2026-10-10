using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Manages the RTX Encore mod — staging, install, uninstall, update detection,
/// and optional nvngx_dlssnr.dll 310.8.0 deployment.
///
/// RTX Encore is a single DLL deployed to the game folder under a user-chosen proxy name.
/// GitHub: SilyNoMeta/rtx-encore — picks the first .zip asset in the latest release
/// (asset filename varies per release, e.g. RTX-Encore-v1.0.0-beta.2.zip).
///
/// NR DLL: nvngx_dlssnr.dll MUST be exactly version 310.8.0. RTX Encore refuses any other
/// version. We use the cached NR DLL from DlssStreamlineService staging (%LocalAppData%\RHI\DLSS-NR\).
/// The file is tracked via RhiInstallManifest SharedFiles so it is only deleted when no other
/// component still owns it.
/// </summary>
public class RtxEncoreService
{
    // ── Constants ─────────────────────────────────────────────────────────────

    private const string GitHubApiUrl   = "https://api.github.com/repos/SilyNoMeta/rtx-encore/releases?per_page=10";
    public  const string RepoUrl        = "https://github.com/SilyNoMeta/rtx-encore";
    public  const string StagedDllName  = "rtx-encore.dll";
    public  const string NrDllName      = "nvngx_dlssnr.dll";
    public  const string NrRequiredVer  = "310.8.0";
    public  const string ComponentName  = "RtxEncore";
    public  const string NrOwnerName    = "RtxEncore";

    /// <summary>All valid proxy DLL names RTX Encore can be deployed as (from INSTALLATION.md).</summary>
    public static readonly string[] KnownProxyNames =
    {
        "version.dll", "dinput8.dll", "winmm.dll", "dxgi.dll",
        "d3d9.dll", "d3d10.dll", "d3d11.dll", "d3d12.dll",
        "dsound.dll", "wininet.dll", "winhttp.dll",
        "binkw64.dll", "bink2w64.dll",
        "xinput1_1.dll", "xinput1_2.dll", "xinput1_3.dll",
        "xinput1_4.dll", "xinput9_1_0.dll", "xinputuap.dll",
        "rtx-encore.asi",   // ASI plugin variant
    };

    private static readonly string BaseStagingDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI");

    private readonly string _stagingDir  = Path.Combine(BaseStagingDir, "rtx-encore");
    private readonly string _versionFile;

    private readonly HttpClient     _http;
    private readonly ICrashReporter _crashReporter;
    private readonly GitHubETagCache _etagCache;

    public RtxEncoreService(HttpClient http, ICrashReporter crashReporter, GitHubETagCache etagCache)
    {
        _http          = http;
        _crashReporter = crashReporter;
        _etagCache     = etagCache;
        _versionFile   = Path.Combine(_stagingDir, "version.txt");
    }

    // ── Properties ────────────────────────────────────────────────────────────

    public bool    IsStagingReady => File.Exists(Path.Combine(_stagingDir, StagedDllName));
    public string? StagedVersion  => File.Exists(_versionFile) ? File.ReadAllText(_versionFile).Trim() : null;
    public bool    HasUpdate      { get; private set; }
    public string? LatestVersion  { get; private set; }

    /// <summary>
    /// Returns true if the component is installed in the given game folder (by stored DLL name).
    /// For the ASI variant, checks the plugins subfolder first, then the root.
    /// </summary>
    public bool IsInstalledIn(string installPath, string? installedAs)
    {
        if (string.IsNullOrEmpty(installPath) || string.IsNullOrEmpty(installedAs)) return false;
        if (installedAs.EndsWith(".asi", StringComparison.OrdinalIgnoreCase))
        {
            // ASI plugin — may be in plugins\ subfolder or root
            var pluginsPath = Path.Combine(installPath, "plugins", installedAs);
            var rootPath    = Path.Combine(installPath, installedAs);
            return File.Exists(pluginsPath) || File.Exists(rootPath);
        }
        return File.Exists(Path.Combine(installPath, installedAs));
    }

    /// <summary>Returns the resolved install path for the stored DLL name.</summary>
    public string ResolveInstalledPath(string installPath, string installedAs)
    {
        if (installedAs.EndsWith(".asi", StringComparison.OrdinalIgnoreCase))
        {
            var pluginsPath = Path.Combine(installPath, "plugins", installedAs);
            if (File.Exists(pluginsPath)) return pluginsPath;
        }
        return Path.Combine(installPath, installedAs);
    }

    // ── NR DLL ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the path to the cached 310.8.0 nvngx_dlssnr.dll from DLSS-NR staging,
    /// or null if not yet staged.
    /// </summary>
    public string? GetNrDllCachedPath()
    {
        // DLSS-NR staging layout: %LocalAppData%\RHI\DLSS-NR\{version}\nvngx_dlssnr.dll
        var nrBase = Path.Combine(BaseStagingDir, "DLSS-NR");
        if (!Directory.Exists(nrBase)) return null;

        var candidate = Path.Combine(nrBase, NrRequiredVer, NrDllName);
        if (File.Exists(candidate)) return candidate;

        // Fallback: scan all version subfolders for 310.8.0
        foreach (var dir in Directory.GetDirectories(nrBase))
        {
            var dirName = Path.GetFileName(dir);
            if (dirName.StartsWith(NrRequiredVer, StringComparison.OrdinalIgnoreCase))
            {
                var path = Path.Combine(dir, NrDllName);
                if (File.Exists(path)) return path;
            }
        }
        return null;
    }

    /// <summary>Returns true if nvngx_dlssnr.dll exists in the game folder.</summary>
    public bool IsNrDllDeployedIn(string installPath)
        => File.Exists(Path.Combine(installPath, NrDllName));

    /// <summary>
    /// Deploys nvngx_dlssnr.dll 310.8.0 to the game folder using the sentinel pattern
    /// and registers RtxEncore as a SharedFile owner in rhi_install.txt.
    /// </summary>
    public bool DeployNrDll(string installPath)
    {
        var src = GetNrDllCachedPath();
        if (src == null)
        {
            _crashReporter.Log("[RtxEncoreService.DeployNrDll] nvngx_dlssnr.dll 310.8.0 not staged — cannot deploy");
            return false;
        }
        var dest = Path.Combine(installPath, NrDllName);
        try
        {
            AuxInstallService.SentinelBackup(dest);
            File.Copy(src, dest, overwrite: true);
            RhiInstallManifest.AddSharedFileOwner(installPath, NrDllName, NrOwnerName);
            _crashReporter.Log($"[RtxEncoreService.DeployNrDll] Deployed {NrDllName} 310.8.0 to '{installPath}'");
            return true;
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[RtxEncoreService.DeployNrDll] Failed — {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Removes nvngx_dlssnr.dll from the game folder only when no other component still owns it.
    /// Uses sentinel restore pattern.
    /// </summary>
    public void RemoveNrDll(string installPath)
    {
        bool safeToDelete = RhiInstallManifest.RemoveSharedFileOwner(installPath, NrDllName, NrOwnerName);
        if (!safeToDelete) return;

        var dest = Path.Combine(installPath, NrDllName);
        try
        {
            AuxInstallService.SentinelRestore(dest);
            _crashReporter.Log($"[RtxEncoreService.RemoveNrDll] Removed/restored {NrDllName} from '{installPath}'");
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[RtxEncoreService.RemoveNrDll] Failed — {ex.Message}");
        }
    }

    // ── Staging ───────────────────────────────────────────────────────────────

    public async Task CheckForUpdateAsync(bool forceRefresh = false)
    {
        var (version, _) = await FetchLatestReleaseInfoAsync(forceRefresh).ConfigureAwait(false);
        if (string.IsNullOrEmpty(version)) return;

        LatestVersion = version;
        var current = StagedVersion;
        HasUpdate = !string.Equals(current, version, StringComparison.OrdinalIgnoreCase);
        _crashReporter.Log($"[RtxEncoreService.CheckForUpdate] Cached={current ?? "(none)"}, Remote={version}, HasUpdate={HasUpdate}");
    }

    public async Task EnsureStagingAsync(IProgress<(string message, double percent)>? progress = null)
    {
        if (IsStagingReady && !HasUpdate)
        {
            _crashReporter.Log("[RtxEncoreService.EnsureStaging] Already up to date");
            return;
        }

        Directory.CreateDirectory(_stagingDir);
        progress?.Report(("Downloading RTX Encore...", 10));

        var (version, downloadUrl) = await FetchLatestReleaseInfoAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(version) || string.IsNullOrEmpty(downloadUrl))
        {
            _crashReporter.Log("[RtxEncoreService.EnsureStaging] Could not resolve latest release");
            return;
        }

        progress?.Report(("Downloading RTX Encore...", 30));

        try
        {
            var bytes   = await _http.GetByteArrayAsync(downloadUrl).ConfigureAwait(false);
            var destDll = Path.Combine(_stagingDir, StagedDllName);

            // The release ships as a zip — extract rtx-encore.dll from root
            var tempZip = Path.Combine(_stagingDir, "_download.zip.tmp");
            await File.WriteAllBytesAsync(tempZip, bytes).ConfigureAwait(false);
            using (var zip = ZipFile.OpenRead(tempZip))
            {
                // Primary: rtx-encore.dll at root of zip
                var entry = zip.Entries.FirstOrDefault(e =>
                    string.Equals(e.Name, StagedDllName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(Path.GetDirectoryName(e.FullName), "", StringComparison.Ordinal));
                // Fallback: any rtx-encore.dll anywhere in the zip
                entry ??= zip.Entries.FirstOrDefault(e =>
                    string.Equals(e.Name, StagedDllName, StringComparison.OrdinalIgnoreCase));

                if (entry == null)
                {
                    _crashReporter.Log($"[RtxEncoreService.EnsureStaging] '{StagedDllName}' not found in zip");
                    File.Delete(tempZip);
                    return;
                }

                using var src = entry.Open();
                using var dst = File.Create(destDll);
                await src.CopyToAsync(dst).ConfigureAwait(false);
            }
            File.Delete(tempZip);

            File.WriteAllText(_versionFile, version);
            HasUpdate = false;
            _crashReporter.Log($"[RtxEncoreService.EnsureStaging] Staged {version} ({new FileInfo(destDll).Length:N0} bytes)");
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[RtxEncoreService.EnsureStaging] Download failed — {ex.Message}");
        }

        progress?.Report(("RTX Encore ready", 100));
    }

    // ── Install ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Installs RTX Encore as <paramref name="dllName"/> in the game folder.
    /// Uses sentinel backup/restore so any existing file with that name is preserved.
    /// Writes a component record to rhi_install.txt.
    /// If previously installed under a different name, removes that first.
    /// </summary>
    public async Task<bool> InstallAsync(
        string installPath,
        string dllName,
        string? existingInstalledAs,
        IProgress<(string message, double percent)>? progress = null)
    {
        if (string.IsNullOrEmpty(installPath)) return false;

        await EnsureStagingAsync(progress).ConfigureAwait(false);
        if (!IsStagingReady)
        {
            _crashReporter.Log("[RtxEncoreService.InstallAsync] Staging not ready");
            return false;
        }

        // Remove old install if name changed
        if (!string.IsNullOrEmpty(existingInstalledAs)
            && !existingInstalledAs.Equals(dllName, StringComparison.OrdinalIgnoreCase))
        {
            Uninstall(installPath, existingInstalledAs, removeNrDll: false);
        }

        var src  = Path.Combine(_stagingDir, StagedDllName);
        string dest;

        if (dllName.EndsWith(".asi", StringComparison.OrdinalIgnoreCase))
        {
            // ASI plugin: deploy to plugins\ subfolder (create if needed)
            var pluginsDir = Path.Combine(installPath, "plugins");
            Directory.CreateDirectory(pluginsDir);
            dest = Path.Combine(pluginsDir, dllName);
        }
        else
        {
            dest = Path.Combine(installPath, dllName);
        }

        try
        {
            AuxInstallService.SentinelBackup(dest);
            File.Copy(src, dest, overwrite: true);
            RhiInstallManifest.SetComponent(installPath, ComponentName, new[] { dest });
            _crashReporter.Log($"[RtxEncoreService.InstallAsync] Installed as '{dllName}' in '{installPath}' (v{StagedVersion})");
            return true;
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[RtxEncoreService.InstallAsync] Failed — {ex.Message}");
            return false;
        }
    }

    // ── Uninstall ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Removes RTX Encore from the game folder using sentinel restore.
    /// Optionally removes nvngx_dlssnr.dll if no other component owns it.
    /// </summary>
    public void Uninstall(string installPath, string? installedAs, bool removeNrDll = true)
    {
        if (string.IsNullOrEmpty(installPath)) return;

        if (!string.IsNullOrEmpty(installedAs))
        {
            var filePath = ResolveInstalledPath(installPath, installedAs);
            try
            {
                AuxInstallService.SentinelRestore(filePath);
                _crashReporter.Log($"[RtxEncoreService.Uninstall] Removed '{installedAs}' from '{installPath}'");
            }
            catch (Exception ex)
            {
                _crashReporter.Log($"[RtxEncoreService.Uninstall] Restore failed — {ex.Message}");
            }
        }

        if (removeNrDll) RemoveNrDll(installPath);

        RhiInstallManifest.RemoveComponent(installPath, ComponentName);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Updates RTX Encore in the game folder — restages and re-deploys under the same name.
    /// </summary>
    public async Task<bool> UpdateAsync(
        string installPath,
        string installedAs,
        IProgress<(string message, double percent)>? progress = null)
    {
        // Re-install under the same name (no name change)
        return await InstallAsync(installPath, installedAs, null, progress).ConfigureAwait(false);
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private async Task<(string? version, string? downloadUrl)> FetchLatestReleaseInfoAsync(
        bool forceRefresh = false)
    {
        try
        {
            string? json;
            if (forceRefresh)
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, GitHubApiUrl);
                req.Headers.Add("User-Agent", "RHI");
                req.Headers.Add("Accept", "application/vnd.github+json");
                using var resp = await _http.SendAsync(req).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    _crashReporter.Log($"[RtxEncoreService.FetchRelease] HTTP {(int)resp.StatusCode}");
                    return (null, null);
                }
                json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            else
            {
                json = await _etagCache.GetWithETagAsync(_http, GitHubApiUrl).ConfigureAwait(false);
            }
            if (json == null) return (null, null);

            using var doc  = JsonDocument.Parse(json);

            // releases list endpoint — pick the first release with a zip asset (includes pre-releases)
            foreach (var release in doc.RootElement.EnumerateArray())
            {
                var tag = release.TryGetProperty("tag_name", out var tp) ? tp.GetString() : null;
                if (string.IsNullOrEmpty(tag)) continue;

                if (!release.TryGetProperty("assets", out var assets)) continue;
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var np) ? np.GetString() : null;
                    if (name?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true
                        && asset.TryGetProperty("browser_download_url", out var up))
                    {
                        return (tag, up.GetString());
                    }
                }
            }
            return (null, null);
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[RtxEncoreService.FetchRelease] {ex.Message}");
            return (null, null);
        }
    }
}
