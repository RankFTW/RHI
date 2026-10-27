using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Services;

/// <summary>
/// Manages Ultimate ASI Loader — staging, download, install, uninstall, and update detection.
/// Supports both 32-bit (Ultimate-ASI-Loader.zip) and 64-bit (Ultimate-ASI-Loader_x64.zip).
/// GitHub: https://github.com/ThirteenAG/Ultimate-ASI-Loader/releases
/// </summary>
public class UltimateAsiLoaderService
{
    public const string AddonType       = "UltimateAsiLoader";
    public const string DefaultDllName  = "dinput8.dll";
    public const string GitHubApiUrl    = "https://api.github.com/repos/ThirteenAG/Ultimate-ASI-Loader/releases/latest";

    // Asset filenames inside each zip
    private const string InnerDllName   = "dinput8.dll";
    private const string Zip32Asset     = "Ultimate-ASI-Loader.zip";
    private const string Zip64Asset     = "Ultimate-ASI-Loader_x64.zip";

    // DLL names supported by UAL (bitness-filtered at call sites)
    public static readonly IReadOnlyList<string> Win32Names = new[]
    {
        "d3d8.dll", "d3d9.dll", "d3d10.dll", "d3d11.dll", "d3d12.dll", "dxgi.dll",
        "ddraw.dll", "dinput.dll", "dinput8.dll", "dsound.dll",
        "msacm32.dll", "msvfw32.dll",
        "version.dll", "wininet.dll", "winmm.dll", "winhttp.dll",
        "xlive.dll", "binkw32.dll", "bink2w32.dll", "vorbisFile.dll",
        "xinput1_1.dll", "xinput1_2.dll", "xinput1_3.dll", "xinput1_4.dll",
        "xinput9_1_0.dll", "xinputuap.dll",
    };

    public static readonly IReadOnlyList<string> Win64Names = new[]
    {
        "d3d9.dll", "d3d10.dll", "d3d11.dll", "d3d12.dll", "dxgi.dll",
        "dinput8.dll", "dsound.dll",
        "version.dll", "wininet.dll", "winmm.dll", "winhttp.dll",
        "binkw64.dll", "bink2w64.dll",
        "xinput1_1.dll", "xinput1_2.dll", "xinput1_3.dll", "xinput1_4.dll",
        "xinput9_1_0.dll", "xinputuap.dll",
    };

    /// <summary>Names flagged as Recommended for most games.</summary>
    public static readonly IReadOnlyList<string> RecommendedNames = new[]
        { "version.dll", "winmm.dll", "winhttp.dll" };

    /// <summary>Names that may conflict with RHI-managed files (ReShade / OptiScaler).</summary>
    public static readonly IReadOnlyList<string> RhiConflictNames = new[]
        { "dxgi.dll", "d3d11.dll", "d3d12.dll", "d3d9.dll" };

    private readonly HttpClient      _http;
    private readonly GitHubETagCache _etagCache;
    private readonly ICrashReporter  _crashReporter;
    private readonly IAuxInstallService _auxInstaller;

    private readonly string _stagingDir32;
    private readonly string _stagingDir64;
    private readonly string _versionFile32;
    private readonly string _versionFile64;

    public UltimateAsiLoaderService(HttpClient http, ICrashReporter crashReporter, IAuxInstallService auxInstaller, GitHubETagCache etagCache)
    {
        _http          = http;
        _etagCache = etagCache;
        _crashReporter = crashReporter;
        _auxInstaller  = auxInstaller;

        var appData    = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _stagingDir32  = Path.Combine(appData, "RHI", "ual");
        _stagingDir64  = Path.Combine(appData, "RHI", "ual64");
        _versionFile32 = Path.Combine(_stagingDir32, "version.txt");
        _versionFile64 = Path.Combine(_stagingDir64, "version.txt");
    }

    // ── State ─────────────────────────────────────────────────────────────────

    public string? StagedVersion32 => File.Exists(_versionFile32) ? File.ReadAllText(_versionFile32).Trim() : null;
    public string? StagedVersion64 => File.Exists(_versionFile64) ? File.ReadAllText(_versionFile64).Trim() : null;

    public bool IsStagingReady32 => File.Exists(Path.Combine(_stagingDir32, DefaultDllName));
    public bool IsStagingReady64 => File.Exists(Path.Combine(_stagingDir64, DefaultDllName));

    // Each bitness is staged and versioned separately. A bitness that has never been staged
    // isn't "outdated" — it downloads on demand the first time a game needs it.
    public bool HasUpdate32 => IsStagingReady32 && IsOutdated(StagedVersion32);
    public bool HasUpdate64 => IsStagingReady64 && IsOutdated(StagedVersion64);
    public bool HasUpdate   => HasUpdate32 || HasUpdate64;

    private bool IsOutdated(string? staged) =>
        LatestVersion != null && !string.Equals(staged, LatestVersion, StringComparison.OrdinalIgnoreCase);

    public string? LatestVersion  { get; private set; }
    public string? ReleaseNotes   { get; private set; }

    // ── Update check ─────────────────────────────────────────────────────────

    public async Task CheckForUpdateAsync()
    {
        var (version, _, body) = await FetchLatestReleaseInfoAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(version))
        {
            _crashReporter.Log("[UltimateAsiLoaderService.CheckForUpdateAsync] Could not resolve latest version");
            return;
        }

        LatestVersion = version;
        ReleaseNotes  = body;

        _crashReporter.Log($"[UltimateAsiLoaderService.CheckForUpdateAsync] 32={StagedVersion32 ?? "(none)"} 64={StagedVersion64 ?? "(none)"} Remote={version} HasUpdate32={HasUpdate32} HasUpdate64={HasUpdate64}");
    }

    // ── Staging ───────────────────────────────────────────────────────────────

    public async Task EnsureStagingAsync(bool is32Bit, IProgress<(string msg, double pct)>? progress = null)
    {
        var stagingDir  = is32Bit ? _stagingDir32  : _stagingDir64;
        var versionFile = is32Bit ? _versionFile32 : _versionFile64;
        var assetName   = is32Bit ? Zip32Asset      : Zip64Asset;
        bool isReady    = is32Bit ? IsStagingReady32 : IsStagingReady64;
        bool hasUpdate  = is32Bit ? HasUpdate32      : HasUpdate64;

        if (isReady && !hasUpdate)
        {
            _crashReporter.Log($"[UltimateAsiLoaderService.EnsureStagingAsync] {(is32Bit ? "32" : "64")}-bit already valid — skipping");
            return;
        }

        Directory.CreateDirectory(stagingDir);
        progress?.Report(("Downloading ASI Loader...", 10));

        var (version, assets, body) = await FetchLatestReleaseInfoAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(version) || assets == null || !assets.TryGetValue(assetName, out var downloadUrl))
        {
            _crashReporter.Log($"[UltimateAsiLoaderService.EnsureStagingAsync] Could not resolve download URL for {assetName}");
            return;
        }

        progress?.Report(("Downloading ASI Loader...", 30));

        try
        {
            var zipBytes = await _http.GetByteArrayAsync(downloadUrl).ConfigureAwait(false);
            progress?.Report(("Extracting...", 70));

            // Extract dinput8.dll from the zip
            using var ms  = new MemoryStream(zipBytes);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
            var entry = zip.GetEntry(InnerDllName)
                     ?? zip.Entries.FirstOrDefault(e => e.Name.Equals(InnerDllName, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                _crashReporter.Log($"[UltimateAsiLoaderService.EnsureStagingAsync] {InnerDllName} not found in archive");
                return;
            }

            var destPath = Path.Combine(stagingDir, DefaultDllName);
            using var src  = entry.Open();
            using var dest = File.Create(destPath);
            await src.CopyToAsync(dest).ConfigureAwait(false);

            File.WriteAllText(versionFile, version);
            ReleaseNotes  = body;
            LatestVersion = version;
            _crashReporter.Log($"[UltimateAsiLoaderService.EnsureStagingAsync] Staged v{version} ({(is32Bit ? "32" : "64")}-bit) to {stagingDir}");
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[UltimateAsiLoaderService.EnsureStagingAsync] Failed — {ex.Message}");
        }

        progress?.Report(("ASI Loader ready", 100));
    }

    // ── Install / Uninstall ───────────────────────────────────────────────────

    /// <summary>
    /// Installs UAL to a game folder as <paramref name="dllName"/>.
    /// If the target name already exists and is NOT an RHI-managed file,
    /// renames it to &lt;name&gt;Hooked.dll first (Hooked chaining).
    /// Returns (success, hookedOriginal) where hookedOriginal is the original filename if chaining happened.
    /// </summary>
    public async Task<(bool success, string? hookedOriginal)> InstallAsync(
        GameCardViewModel card,
        string dllName,
        IProgress<(string msg, double pct)>? progress = null)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return (false, null);

        await EnsureStagingAsync(card.Is32Bit, progress).ConfigureAwait(false);

        bool isReady = card.Is32Bit ? IsStagingReady32 : IsStagingReady64;
        if (!isReady)
        {
            _crashReporter.Log("[UltimateAsiLoaderService.InstallAsync] Staging not ready");
            return (false, null);
        }

        var stagingDir = card.Is32Bit ? _stagingDir32 : _stagingDir64;
        var src        = Path.Combine(stagingDir, DefaultDllName);
        var destPath   = Path.Combine(card.InstallPath, dllName);

        // If reinstalling under a different name, clean up the old installation first
        var existingRecord = _auxInstaller.FindRecord(card.GameName, card.InstallPath, AddonType);
        if (existingRecord != null && !existingRecord.InstalledAs.Equals(dllName, StringComparison.OrdinalIgnoreCase))
        {
            var oldPath    = Path.Combine(card.InstallPath, existingRecord.InstalledAs);
            var oldExt     = Path.GetExtension(existingRecord.InstalledAs);
            var oldNoExt   = Path.GetFileNameWithoutExtension(existingRecord.InstalledAs);
            var oldHooked  = Path.Combine(card.InstallPath, $"{oldNoExt}Hooked{oldExt}");

            try
            {
                if (File.Exists(oldPath)) File.Delete(oldPath);
                // Restore hooked backup if present
                if (File.Exists(oldHooked))
                    File.Move(oldHooked, oldPath, overwrite: false);
                _crashReporter.Log($"[UltimateAsiLoaderService.InstallAsync] Removed old install '{existingRecord.InstalledAs}' for '{card.GameName}'");
            }
            catch (Exception ex)
            {
                _crashReporter.Log($"[UltimateAsiLoaderService.InstallAsync] Old cleanup failed (non-fatal) — {ex.Message}");
            }
        }

        // Hooked chaining — if the target DLL exists and is NOT ours, rename it first
        string? hookedOriginal = null;
        if (File.Exists(destPath) && !IsRhiManagedFile(card, dllName))
        {
            var ext        = Path.GetExtension(dllName);           // ".dll"
            var nameNoExt  = Path.GetFileNameWithoutExtension(dllName); // "version"
            var hookedName = $"{nameNoExt}Hooked{ext}";            // "versionHooked.dll"
            var hookedPath = Path.Combine(card.InstallPath, hookedName);

            try
            {
                File.Move(destPath, hookedPath, overwrite: true);
                hookedOriginal = hookedName;
                _crashReporter.Log($"[UltimateAsiLoaderService.InstallAsync] Chained '{dllName}' → '{hookedName}' for '{card.GameName}'");
            }
            catch (Exception ex)
            {
                _crashReporter.Log($"[UltimateAsiLoaderService.InstallAsync] Chain rename failed — {ex.Message}");
                return (false, null);
            }
        }

        progress?.Report(("Deploying ASI Loader...", 80));

        try
        {
            File.Copy(src, destPath, overwrite: true);
            _crashReporter.Log($"[UltimateAsiLoaderService.InstallAsync] Installed as '{dllName}' in '{card.InstallPath}'");
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[UltimateAsiLoaderService.InstallAsync] Copy failed — {ex.Message}");
            // Undo hooked rename if the install itself failed
            if (hookedOriginal != null)
            {
                try { File.Move(Path.Combine(card.InstallPath, hookedOriginal), destPath, overwrite: true); } catch (Exception ex2) { CrashReporter.Log($"[UltimateAsiLoaderService.InstallAsync] Failed to undo hooked rename — {ex2.Message}"); }
                hookedOriginal = null;
            }
            return (false, null);
        }

        // Persist tracking record
        var record = new AuxInstalledRecord
        {
            GameName    = card.GameName,
            InstallPath = card.InstallPath,
            Store       = card.Source ?? "",
            AddonType   = AddonType,
            InstalledAs = dllName,
            InstalledAt = DateTime.UtcNow,
            SourceUrl   = GitHubApiUrl,
        };
        _auxInstaller.SaveAuxRecord(record);

        progress?.Report(("ASI Loader installed!", 100));
        return (true, hookedOriginal);
    }

    /// <summary>
    /// Uninstalls UAL from a game folder. Restores Hooked backup if present.
    /// </summary>
    public bool Uninstall(GameCardViewModel card)
    {
        var record = _auxInstaller.FindRecord(card.GameName, card.InstallPath ?? "", AddonType);
        if (record == null)
        {
            _crashReporter.Log($"[UltimateAsiLoaderService.Uninstall] No record for '{card.GameName}'");
            return false;
        }

        var dllPath = Path.Combine(record.InstallPath, record.InstalledAs);
        try
        {
            if (File.Exists(dllPath))
                File.Delete(dllPath);

            // Remove the UAL INI file (same name as the DLL, e.g. winmm.dll → winmm.ini)
            var ext       = Path.GetExtension(record.InstalledAs);
            var nameNoExt = Path.GetFileNameWithoutExtension(record.InstalledAs);
            var iniPath   = Path.Combine(record.InstallPath, $"{nameNoExt}.ini");
            if (File.Exists(iniPath))
            {
                try
                {
                    File.Delete(iniPath);
                    _crashReporter.Log($"[UltimateAsiLoaderService.Uninstall] Deleted INI '{nameNoExt}.ini' for '{card.GameName}'");
                }
                catch (Exception iniEx)
                {
                    _crashReporter.Log($"[UltimateAsiLoaderService.Uninstall] Failed to delete INI '{nameNoExt}.ini' — {iniEx.Message}");
                }
            }

            // Restore Hooked backup if it exists
            var hookedPath = Path.Combine(record.InstallPath, $"{nameNoExt}Hooked{ext}");
            if (File.Exists(hookedPath))
            {
                File.Move(hookedPath, dllPath, overwrite: true);
                _crashReporter.Log($"[UltimateAsiLoaderService.Uninstall] Restored '{nameNoExt}Hooked{ext}' → '{record.InstalledAs}' for '{card.GameName}'");
            }

            _auxInstaller.RemoveRecord(record);
            _crashReporter.Log($"[UltimateAsiLoaderService.Uninstall] Removed from '{record.InstallPath}'");
            return true;
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[UltimateAsiLoaderService.Uninstall] Failed — {ex.Message}");
            return false;
        }
    }

    // ── Auto-update (redeploy to already-installed games) ─────────────────────

    /// <summary>
    /// Brings every game with UAL installed up to the staged copy for its bitness.
    /// Compares each game's file with staging (size + modified time; File.Copy keeps the
    /// source's time) instead of relying on an "updated this session" flag, so a game that
    /// was running during an update is caught up on the next launch.
    /// </summary>
    public Task AutoUpdateInstalledGamesAsync(IReadOnlyList<GameCardViewModel> cards)
    {
        var records = _auxInstaller.LoadAll()
            .Where(r => r.AddonType.Equals(AddonType, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (records.Count == 0) return Task.CompletedTask;

        foreach (var card in cards.ToList())
        {
            if (string.IsNullOrEmpty(card.InstallPath)) continue;
            var record = records.FirstOrDefault(r =>
                r.GameName.Equals(card.GameName, StringComparison.OrdinalIgnoreCase)
                && r.InstallPath.Equals(card.InstallPath, StringComparison.OrdinalIgnoreCase));
            if (record == null || string.IsNullOrEmpty(record.InstalledAs)) continue;

            var stagingDir = card.Is32Bit ? _stagingDir32 : _stagingDir64;
            var src        = Path.Combine(stagingDir, DefaultDllName);
            var dest       = FileHelper.SafeCombine(record.InstallPath, record.InstalledAs);

            if (dest == null || !File.Exists(src)) continue;
            // Never re-create a file the user removed.
            if (!File.Exists(dest)) continue;

            var srcInfo  = new FileInfo(src);
            var destInfo = new FileInfo(dest);
            if (srcInfo.Length == destInfo.Length && srcInfo.LastWriteTimeUtc == destInfo.LastWriteTimeUtc)
                continue;

            try
            {
                File.Copy(src, dest, overwrite: true);
                _crashReporter.Log($"[UltimateAsiLoaderService.AutoUpdate] Updated '{card.GameName}' ({record.InstalledAs})");
            }
            catch (Exception ex)
            {
                _crashReporter.Log($"[UltimateAsiLoaderService.AutoUpdate] Failed for '{card.GameName}' — {ex.Message}");
            }
        }
        return Task.CompletedTask;
    }

    // ── Detection ─────────────────────────────────────────────────────────────

    public bool IsInstalledIn(string gameName, string? installPath)
        => !string.IsNullOrEmpty(installPath)
           && _auxInstaller.FindRecord(gameName, installPath, AddonType) != null;

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Returns true if the given DLL name at this game's path is managed by RHI (ReShade/OptiScaler/DC).</summary>
    private static bool IsRhiManagedFile(GameCardViewModel card, string dllName)
    {
        if (!string.IsNullOrEmpty(card.RsInstalledFile)
            && card.RsInstalledFile.Equals(dllName, StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.IsNullOrEmpty(card.OsInstalledFile)
            && card.OsInstalledFile.Equals(dllName, StringComparison.OrdinalIgnoreCase)) return true;
        if (!string.IsNullOrEmpty(card.DcInstalledFile)
            && card.DcInstalledFile.Equals(dllName, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private async Task<(string? version, Dictionary<string, string>? assets, string? body)> FetchLatestReleaseInfoAsync()
    {
        try
        {
            var json = await _etagCache.GetWithETagAsync(_http, GitHubApiUrl).ConfigureAwait(false);
            if (json == null)
            {
                _crashReporter.Log($"[UltimateAsiLoaderService] GitHub API request failed");
                return (null, null, null);
            }
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag     = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() : null;
            var body    = root.TryGetProperty("body",     out var bodyEl) ? bodyEl.GetString() : null;
            var version = tag?.TrimStart('v');  // "v9.7.4" → "9.7.4"

            if (string.IsNullOrEmpty(version)) return (null, null, null);

            // Build asset URL map
            var assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("assets", out var assetsEl))
            {
                foreach (var asset in assetsEl.EnumerateArray())
                {
                    if (asset.TryGetProperty("name", out var nameEl)
                        && asset.TryGetProperty("browser_download_url", out var urlEl))
                    {
                        var name = nameEl.GetString();
                        var url  = urlEl.GetString();
                        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(url))
                            assets[name] = url;
                    }
                }
            }

            _crashReporter.Log($"[UltimateAsiLoaderService.FetchLatestReleaseInfoAsync] Latest = {version}");
            return (version, assets, body);
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[UltimateAsiLoaderService.FetchLatestReleaseInfoAsync] Failed — {ex.Message}");
            return (null, null, null);
        }
    }
}
