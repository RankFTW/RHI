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
/// and optional nvngx_dlssnr.dll 310.9.16 deployment.
///
/// RTX Encore is a single DLL deployed to the game folder under a user-chosen proxy name, or the
/// release's separate rtx-encore.asi build in plugins\. Bink names keep the game's file as &lt;name&gt;Hooked.dll.
/// Intel XeSS runtimes: Rtx40MfgService.EncoreIntel.cs.
/// GitHub: SilyNoMeta/rtx-encore — picks the first .zip asset in the latest release
/// (asset filename varies per release, e.g. RTX-Encore-v1.0.0-beta.2.zip).
///
/// NR DLL: RTX Encore 1.5.3 accepts nvngx_dlssnr.dll 310.8.0 or 310.9.16; RHI only deploys 310.9.16
/// (older RHI deployments of 310.8.0 are moved to it when RTX Encore is the file's only owner).
/// We use the cached NR DLL from DlssStreamlineService staging (%LocalAppData%\RHI\DLSS-NR\).
/// The file is tracked via RhiInstallManifest SharedFiles so it is only deleted when no other
/// component still owns it.
/// </summary>
public class RtxEncoreService
{
    // ── Constants ─────────────────────────────────────────────────────────────

    private const string GitHubApiUrl   = "https://api.github.com/repos/SilyNoMeta/rtx-encore/releases?per_page=10";
    public  const string RepoUrl        = "https://github.com/SilyNoMeta/rtx-encore";
    public  const string StagedDllName  = "rtx-encore.dll";
    /// <summary>The release's separate ASI build (alternative-proxies\rtx-encore.asi), used for the plugins\ install.</summary>
    public  const string StagedAsiName  = "rtx-encore.asi";
    /// <summary>Written instead of the .asi when a release has no ASI build, so staging isn't retried every launch.</summary>
    private const string NoAsiMarker    = "rtx-encore.asi.none";
    public  const string NrDllName      = "nvngx_dlssnr.dll";
    public  const string NrRequiredVer  = "310.9.16";
    /// <summary>The dlss_manifest.json "dlssnr" entry for <see cref="NrRequiredVer"/>.</summary>
    public  const string NrManifestVersion = "310.9.16 (20/30/40/50)";
    public  const string ComponentName  = "RtxEncore";
    public  const string NrOwnerName    = "RtxEncore";

    /// <summary>
    /// A manifest rtxEncoreDllNames value as one of <see cref="KnownProxyNames"/> (its spelling), or null
    /// when it isn't one, so a typo or an odd value in remote data never reaches a file name.
    /// </summary>
    public static string? ToKnownProxyName(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return KnownProxyNames.FirstOrDefault(n => n.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
    }

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
    /// <summary>Staging also holds the ASI build (or the release has none). False for staging done before RHI kept it.</summary>
    private bool   IsAsiStaged    => File.Exists(Path.Combine(_stagingDir, StagedAsiName)) || File.Exists(Path.Combine(_stagingDir, NoAsiMarker));
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

    /// <summary>Folder holding the RTX Encore DLL (game folder, or plugins\ for the .asi), where its settings and runtimes live.</summary>
    public string ModDir(string installPath, string installedAs)
        => Path.GetDirectoryName(ResolveInstalledPath(installPath, installedAs)) ?? installPath;

    /// <summary>
    /// nvngx_dlssnr.dll relative to the game folder. RTX Encore checks for it beside its own file,
    /// so it's "plugins\nvngx_dlssnr.dll" for the .asi install. Also the shared-file key in rhi_install.txt.
    /// </summary>
    public string NrRelPath(string installPath, string? installedAs)
    {
        if (string.IsNullOrEmpty(installedAs)) return NrDllName;
        var rel = Path.GetRelativePath(installPath, ModDir(installPath, installedAs));
        return rel == "." ? NrDllName : Path.Combine(rel, NrDllName);
    }

    /// <summary>Path of rtx-encore.jsonc for this install.</summary>
    public string ConfigPath(string installPath, string installedAs)
        => Path.Combine(ModDir(installPath, installedAs), RtxEncoreConfig.FileName);

    /// <summary>Returns the cached 310.9.16 nvngx_dlssnr.dll, downloading it first when needed. Null if unavailable.</summary>
    public async Task<string?> EnsureNrDllCachedAsync(IDlssStreamlineService dlss)
        => GetNrDllCachedPath() ?? await dlss.EnsureSpecificDlssnrCachedAsync(NrManifestVersion).ConfigureAwait(false);

    /// <summary>
    /// Moves an RHI deployment of an older NR DLL (310.8.x, from earlier RHI versions) to 310.9.16.
    /// Only when RTX Encore is the file's only owner, so a version chosen elsewhere is never replaced.
    /// </summary>
    public bool UpgradeNrDllIfOlder(string installPath, string installedAs)
    {
        var rel = NrRelPath(installPath, installedAs);
        var dest = Path.Combine(installPath, rel);
        if (!File.Exists(dest) || GetNrDllCachedPath() == null) return false;
        var owners = RhiInstallManifest.GetSharedFileOwners(installPath, rel);
        if (owners.Count != 1 || !owners[0].Equals(NrOwnerName, StringComparison.OrdinalIgnoreCase)) return false;
        var vi = System.Diagnostics.FileVersionInfo.GetVersionInfo(dest);
        var ver = $"{vi.FileMajorPart}.{vi.FileMinorPart}.{vi.FileBuildPart}";
        if (ver == NrRequiredVer || !ver.StartsWith("310.8.", StringComparison.Ordinal)) return false;
        _crashReporter.Log($"[RtxEncoreService.UpgradeNrDllIfOlder] '{installPath}': {ver} → {NrRequiredVer}");
        return DeployNrDll(installPath, installedAs);
    }

    /// <summary>
    /// .asi installs from older RHI versions have the NR DLL in the game folder, where RTX Encore (in plugins\)
    /// doesn't look. Moves RHI's copy beside the mod. Returns true when it moved.
    /// </summary>
    public bool MoveNrDllBesideMod(string installPath, string installedAs)
    {
        var rel = NrRelPath(installPath, installedAs);
        if (rel.Equals(NrDllName, StringComparison.OrdinalIgnoreCase)) return false; // already beside the mod
        var rootOwners = RhiInstallManifest.GetSharedFileOwners(installPath, NrDllName);
        if (!rootOwners.Contains(NrOwnerName, StringComparer.OrdinalIgnoreCase)) return false;
        if (!DeployNrDll(installPath, installedAs)) return false;
        RemoveNrDllAt(installPath, NrDllName);
        _crashReporter.Log($"[RtxEncoreService.MoveNrDllBesideMod] '{installPath}': NR DLL moved to '{rel}'");
        return true;
    }

    /// <summary>
    /// Returns the path to the cached 310.9.16 nvngx_dlssnr.dll from DLSS-NR staging,
    /// or null if not yet staged.
    /// </summary>
    public string? GetNrDllCachedPath()
    {
        // DLSS-NR staging layout: %LocalAppData%\RHI\DLSS-NR\{version}\nvngx_dlssnr.dll
        var nrBase = Path.Combine(BaseStagingDir, "DLSS-NR");
        if (!Directory.Exists(nrBase)) return null;

        var candidate = Path.Combine(nrBase, NrRequiredVer, NrDllName);
        if (File.Exists(candidate)) return candidate;

        // Fallback: scan all version subfolders for the required version
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

    /// <summary>
    /// Deploys nvngx_dlssnr.dll 310.9.16 beside RTX Encore (see <see cref="NrRelPath"/>) using the sentinel
    /// pattern and registers RtxEncore as a SharedFile owner in rhi_install.txt.
    /// </summary>
    public bool DeployNrDll(string installPath, string? installedAs)
    {
        var src = GetNrDllCachedPath();
        if (src == null)
        {
            _crashReporter.Log($"[RtxEncoreService.DeployNrDll] nvngx_dlssnr.dll {NrRequiredVer} not staged — cannot deploy");
            return false;
        }
        var rel = NrRelPath(installPath, installedAs);
        var dest = Path.Combine(installPath, rel);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            AuxInstallService.SentinelBackup(dest);
            File.Copy(src, dest, overwrite: true);
            RhiInstallManifest.AddSharedFileOwner(installPath, rel, NrOwnerName);
            _crashReporter.Log($"[RtxEncoreService.DeployNrDll] Deployed {NrDllName} {NrRequiredVer} to '{dest}'");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"[RtxEncoreService.DeployNrDll] Failed — {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Removes RTX Encore's nvngx_dlssnr.dll only when no other component still owns it.
    /// Uses sentinel restore pattern.
    /// </summary>
    public void RemoveNrDll(string installPath, string? installedAs)
        => RemoveNrDllAt(installPath, NrRelPath(installPath, installedAs));

    private void RemoveNrDllAt(string installPath, string rel)
    {
        bool safeToDelete = RhiInstallManifest.RemoveSharedFileOwner(installPath, rel, NrOwnerName);
        if (!safeToDelete) return;

        var dest = Path.Combine(installPath, rel);
        try
        {
            AuxInstallService.SentinelRestore(dest);
            _crashReporter.Log($"[RtxEncoreService.RemoveNrDll] Removed/restored '{rel}' from '{installPath}'");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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
        if (IsStagingReady && IsAsiStaged && !HasUpdate)
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

                using (var src = entry.Open())
                using (var dst = File.Create(destDll))
                    await src.CopyToAsync(dst).ConfigureAwait(false);

                // The separate ASI build, for the plugins\ install (the docs say to use it rather than renaming the DLL)
                var asiEntry = zip.Entries.FirstOrDefault(e =>
                    string.Equals(e.Name, StagedAsiName, StringComparison.OrdinalIgnoreCase));
                var destAsi = Path.Combine(_stagingDir, StagedAsiName);
                var noAsi = Path.Combine(_stagingDir, NoAsiMarker);
                if (asiEntry != null)
                {
                    using (var src = asiEntry.Open())
                    using (var dst = File.Create(destAsi))
                        await src.CopyToAsync(dst).ConfigureAwait(false);
                    if (File.Exists(noAsi)) File.Delete(noAsi);
                }
                else
                {
                    if (File.Exists(destAsi)) File.Delete(destAsi); // an older release's build
                    File.WriteAllText(noAsi, version);
                    _crashReporter.Log($"[RtxEncoreService.EnsureStaging] No '{StagedAsiName}' in the zip — the .asi install uses the renamed DLL");
                }
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

        // Remove old install if name changed. The NR DLL lives beside the mod, so it goes too when the
        // mod moves between the game folder and plugins\ (the caller redeploys it beside the new file).
        if (!string.IsNullOrEmpty(existingInstalledAs)
            && !existingInstalledAs.Equals(dllName, StringComparison.OrdinalIgnoreCase))
        {
            bool nrMoves = !NrRelPath(installPath, existingInstalledAs).Equals(
                IsAsi(dllName) ? Path.Combine("plugins", NrDllName) : NrDllName, StringComparison.OrdinalIgnoreCase);
            Uninstall(installPath, existingInstalledAs, removeNrDll: nrMoves);
        }

        var src  = Path.Combine(_stagingDir, StagedDllName);
        string dest;

        if (IsAsi(dllName))
        {
            // ASI plugin: deploy to plugins\ subfolder (create if needed), using the release's own ASI build
            var pluginsDir = Path.Combine(installPath, "plugins");
            Directory.CreateDirectory(pluginsDir);
            dest = Path.Combine(pluginsDir, dllName);
            var stagedAsi = Path.Combine(_stagingDir, StagedAsiName);
            if (File.Exists(stagedAsi)) src = stagedAsi;
        }
        else
        {
            dest = Path.Combine(installPath, dllName);
        }

        try
        {
            if (IsBinkName(dllName)) KeepGameBinkAsHooked(dest);
            else AuxInstallService.SentinelBackup(dest);
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
            // Before the mod's file is gone: the NR DLL's location depends on it
            if (removeNrDll) RemoveNrDll(installPath, installedAs);
            try
            {
                var hooked = HookedPath(filePath);
                if (IsBinkName(installedAs) && File.Exists(hooked))
                {
                    // Put the game's own Bink DLL back under its real name
                    if (File.Exists(filePath)) File.Delete(filePath);
                    File.Move(hooked, filePath);
                    var sentinel = filePath + ".original";
                    if (File.Exists(sentinel) && new FileInfo(sentinel).Length == 0) File.Delete(sentinel);
                }
                else
                {
                    AuxInstallService.SentinelRestore(filePath);
                }
                _crashReporter.Log($"[RtxEncoreService.Uninstall] Removed '{installedAs}' from '{installPath}'");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _crashReporter.Log($"[RtxEncoreService.Uninstall] Restore failed — {ex.Message}");
            }
        }
        else if (removeNrDll)
        {
            RemoveNrDll(installPath, null);
        }

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

    private static bool IsAsi(string name) => name.EndsWith(".asi", StringComparison.OrdinalIgnoreCase);

    /// <summary>Bink proxy names: RTX Encore forwards to the game's own Bink DLL, renamed &lt;name&gt;Hooked.dll beside it.</summary>
    private static bool IsBinkName(string name)
        => name.Equals("binkw64.dll", StringComparison.OrdinalIgnoreCase)
        || name.Equals("bink2w64.dll", StringComparison.OrdinalIgnoreCase);

    /// <summary>binkw64.dll → binkw64Hooked.dll in the same folder.</summary>
    private static string HookedPath(string dest)
        => Path.Combine(Path.GetDirectoryName(dest) ?? "", Path.GetFileNameWithoutExtension(dest) + "Hooked.dll");

    /// <summary>
    /// Before RTX Encore is copied as a Bink name: the game's own file becomes &lt;name&gt;Hooked.dll, as the mod's
    /// install guide requires. A real .original from older RHI versions is the game's file and moves there too.
    /// With no game file, a 0-byte .original marks the name as RHI's, so a reinstall doesn't mistake the mod for it.
    /// </summary>
    private void KeepGameBinkAsHooked(string dest)
    {
        var hooked = HookedPath(dest);
        var original = dest + ".original";
        if (File.Exists(hooked)) return; // already set up: dest is RTX Encore
        if (File.Exists(original))
        {
            if (new FileInfo(original).Length == 0) return; // RHI's own name (the game has no Bink DLL)
            File.Move(original, hooked);
            _crashReporter.Log($"[RtxEncoreService.KeepGameBinkAsHooked] Moved the game's '{Path.GetFileName(dest)}' from .original to '{Path.GetFileName(hooked)}'");
            return;
        }
        if (File.Exists(dest))
        {
            File.Move(dest, hooked);
            _crashReporter.Log($"[RtxEncoreService.KeepGameBinkAsHooked] Kept the game's '{Path.GetFileName(dest)}' as '{Path.GetFileName(hooked)}'");
        }
        else
        {
            File.WriteAllBytes(original, Array.Empty<byte>());
            _crashReporter.Log($"[RtxEncoreService.KeepGameBinkAsHooked] No game '{Path.GetFileName(dest)}' — wrote a sentinel");
        }
    }

    private async Task<(string? version, string? downloadUrl)> FetchLatestReleaseInfoAsync(
        bool forceRefresh = false)
    {
        try
        {
            string? json;
            // An ETag request always returns current data, so forced checks use the cache too.
            json = await _etagCache.GetWithETagAsync(_http, GitHubApiUrl).ConfigureAwait(false);
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
