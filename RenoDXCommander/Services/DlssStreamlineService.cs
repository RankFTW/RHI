using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace RenoDXCommander.Services;

/// <summary>
/// Manages DLSS and Streamline DLL detection, version swapping, backup/restore,
/// and on-demand downloading. Implemented as a partial class.
/// </summary>
public partial class DlssStreamlineService : IDlssStreamlineService
{
    // ── Constants ─────────────────────────────────────────────────────────────

    private const string DlssDllName = "nvngx_dlss.dll";
    private const string DlssdDllName = "nvngx_dlssd.dll";
    private const string DlssgDllName = "nvngx_dlssg.dll";
    private const string StreamlineIndicator = "sl.interposer.dll";
    private const string BackupExtension = ".original";

    /// <summary>Known Streamline DLL filenames.</summary>
    public static readonly string[] KnownStreamlineDlls =
    [
        "sl.common.dll",
        "sl.deepdvc.dll",
        "sl.directsr.dll",
        "sl.dlss.dll",
        "sl.dlss_d.dll",
        "sl.dlss_g.dll",
        "sl.interposer.dll",
        "sl.nis.dll",
        "sl.nvperf.dll",
        "sl.pcl.dll",
        "sl.reflex.dll",
    ];

    // ── Staging directories ───────────────────────────────────────────────────

    private static readonly string BaseStagingDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI");

    private static readonly string DlssCacheDir = Path.Combine(BaseStagingDir, "DLSS");
    private static readonly string DlssdCacheDir = Path.Combine(BaseStagingDir, "DLSS-D");
    private static readonly string DlssgCacheDir = Path.Combine(BaseStagingDir, "DLSS-G");
    private static readonly string StreamlineCacheDir = Path.Combine(BaseStagingDir, "Streamline");
    private static readonly string DlssCustomDir = Path.Combine(BaseStagingDir, "Custom", "DLSS");
    private static readonly string StreamlineCustomDir = Path.Combine(BaseStagingDir, "Custom", "Streamline");
    private static readonly string CustomBaseDir = Path.Combine(BaseStagingDir, "Custom");

    // ── Manifest URL ──────────────────────────────────────────────────────────

    private const string DlssManifestUrl =
        "https://raw.githubusercontent.com/RankFTW/RHI/main/dlss_manifest.json";

    private static readonly string ManifestCachePath = Path.Combine(BaseStagingDir, "dlss_manifest.json");

    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly HttpClient _http;
    private readonly GitHubETagCache _etagCache;

    // ── State ─────────────────────────────────────────────────────────────────

    private DlssManifestData? _manifest;
    private static readonly object _cacheSaveLock = new();

    public IReadOnlyList<string> DlssVersions => _manifest?.Dlss?.Select(e => FormatVersion(e.Version)).ToList().AsReadOnly()
        ?? (IReadOnlyList<string>)Array.Empty<string>();
    public IReadOnlyList<string> DlssdVersions => _manifest?.Dlssd?.Select(e => FormatVersion(e.Version)).ToList().AsReadOnly()
        ?? (IReadOnlyList<string>)Array.Empty<string>();
    public IReadOnlyList<string> DlssgVersions => _manifest?.Dlssg?.Select(e => FormatVersion(e.Version)).ToList().AsReadOnly()
        ?? (IReadOnlyList<string>)Array.Empty<string>();
    public IReadOnlyList<string> StreamlineVersions => _manifest?.Streamline?.Select(e => FormatVersion(e.Version)).ToList().AsReadOnly()
        ?? (IReadOnlyList<string>)Array.Empty<string>();

    public DlssStreamlineService(HttpClient http, GitHubETagCache etagCache)
    {
        _http = http;
        _etagCache = etagCache;

        // Try load cached manifest synchronously for immediate availability
        LoadCachedManifest();

        // Migrate old custom folders to new unified Custom\ structure
        MigrateCustomFolders();

        // Ensure custom folders exist so users can drop files in
        try { Directory.CreateDirectory(DlssCustomDir); } catch { }
        try { Directory.CreateDirectory(StreamlineCustomDir); } catch { }
        try { Directory.CreateDirectory(RsCustomDir); } catch { }
    }

    /// <summary>Custom ReShade folder path — exposed for AuxInstallService.</summary>
    public static string RsCustomDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "Custom", "ReShade");

    /// <summary>
    /// One-time migration: moves files from old DLSS-Custom/Streamline-Custom to Custom\DLSS and Custom\Streamline.
    /// </summary>
    private static void MigrateCustomFolders()
    {
        try
        {
            var oldDlssCustom = Path.Combine(BaseStagingDir, "DLSS-Custom");
            var oldStreamlineCustom = Path.Combine(BaseStagingDir, "Streamline-Custom");

            MigrateFolder(oldDlssCustom, DlssCustomDir);
            MigrateFolder(oldStreamlineCustom, StreamlineCustomDir);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DlssStreamlineService.MigrateCustomFolders] Migration failed — {ex.Message}");
        }
    }

    private static void MigrateFolder(string oldPath, string newPath)
    {
        if (!Directory.Exists(oldPath)) return;

        var files = Directory.GetFiles(oldPath);
        if (files.Length == 0)
        {
            // Empty old folder — just delete it
            try { Directory.Delete(oldPath, false); } catch { }
            return;
        }

        Directory.CreateDirectory(newPath);
        foreach (var file in files)
        {
            var destFile = Path.Combine(newPath, Path.GetFileName(file));
            if (!File.Exists(destFile))
                File.Move(file, destFile);
            else
                File.Delete(file); // new location already has the file
        }

        // Remove old folder if now empty
        try
        {
            if (Directory.GetFiles(oldPath).Length == 0 && Directory.GetDirectories(oldPath).Length == 0)
                Directory.Delete(oldPath, false);
        }
        catch { }
    }

    // ── Manifest fetching ─────────────────────────────────────────────────────

    public async Task FetchManifestAsync()
    {
        try
        {
            var json = await _http.GetStringAsync(DlssManifestUrl).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(json))
            {
                var manifest = JsonSerializer.Deserialize<DlssManifestData>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (manifest != null)
                {
                    _manifest = manifest;

                    // Cache to disk
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(ManifestCachePath)!);
                        await File.WriteAllTextAsync(ManifestCachePath, json).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        CrashReporter.Log($"[DlssStreamlineService.FetchManifestAsync] Cache write failed — {ex.Message}");
                    }
                }

                CrashReporter.Log($"[DlssStreamlineService.FetchManifestAsync] Loaded: " +
                    $"{_manifest?.Dlss?.Count ?? 0} SR, {_manifest?.Dlssd?.Count ?? 0} RR, " +
                    $"{_manifest?.Dlssg?.Count ?? 0} FG, {_manifest?.Streamline?.Count ?? 0} SL versions");
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DlssStreamlineService.FetchManifestAsync] Fetch failed — {ex.Message}");
            // Fall back to cached
            LoadCachedManifest();
        }
    }

    private void LoadCachedManifest()
    {
        try
        {
            if (File.Exists(ManifestCachePath))
            {
                var json = File.ReadAllText(ManifestCachePath);
                _manifest = JsonSerializer.Deserialize<DlssManifestData>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                CrashReporter.Log($"[DlssStreamlineService.LoadCachedManifest] Loaded from cache");
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DlssStreamlineService.LoadCachedManifest] Failed — {ex.Message}");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    public string? GetFileVersion(string dllPath)
    {
        try
        {
            if (!File.Exists(dllPath)) return null;
            var info = FileVersionInfo.GetVersionInfo(dllPath);
            return $"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}.{info.FilePrivatePart}";
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DlssStreamlineService.GetFileVersion] Failed for '{dllPath}' — {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Formats a raw 4-part version (e.g. "3.10.6.0") by removing only the last ".0" if present.
    /// Always keeps a minimum of 3 parts (e.g. "310.6.0" stays as-is, "2.7.32.0" → "2.7.32" is wrong,
    /// so we only trim if there are 4+ parts and the last is "0").
    /// </summary>
    public static string FormatVersion(string? rawVersion)
    {
        if (string.IsNullOrEmpty(rawVersion)) return "Unknown";

        // Only trim the last .0 if there are 4 parts and the last part is "0"
        var parts = rawVersion.Split('.');
        if (parts.Length == 4 && parts[3] == "0")
            return $"{parts[0]}.{parts[1]}.{parts[2]}";

        return rawVersion;
    }

    public bool HasBackup(string dllPath) => File.Exists(dllPath + BackupExtension);

    public string? GetNewestDlssVersion() => _manifest?.Dlss?.FirstOrDefault()?.Version;

    // ── DLSS scan skip cache ──────────────────────────────────────────────────

    private static readonly string ScanSkipCachePath = Path.Combine(BaseStagingDir, "dlss_scan_cache.json");
    private Dictionary<string, int>? _scanSkipCache;
    private const int SkipThreshold = 3;

    /// <summary>
    /// Returns true if this game has been scanned 3+ times with no DLSS found.
    /// </summary>
    public bool ShouldSkipScan(string gameName)
    {
        EnsureScanCacheLoaded();
        return _scanSkipCache!.TryGetValue(gameName, out var count) && count >= SkipThreshold;
    }

    /// <summary>
    /// Records that a scan found no DLSS for this game. Increments the counter.
    /// </summary>
    public void RecordNoDlssFound(string gameName)
    {
        EnsureScanCacheLoaded();
        _scanSkipCache!.TryGetValue(gameName, out var count);
        _scanSkipCache[gameName] = count + 1;
        SaveScanCache();
    }

    /// <summary>
    /// Records that DLSS was found — removes the game from the skip cache.
    /// </summary>
    public void RecordDlssFound(string gameName)
    {
        EnsureScanCacheLoaded();
        if (_scanSkipCache!.Remove(gameName))
            SaveScanCache();
    }

    /// <summary>
    /// Invalidates trusted path entries that have null components (partial detection).
    /// Games with fully populated paths keep their trusted status.
    /// Called on Full Refresh to detect newly added DLLs (e.g. game update adds RR/FG).
    /// Also clears the scan skip cache so reinstalled games are re-scanned.
    /// </summary>
    public void ClearScanCaches()
    {
        // Clear the scan skip cache entirely — reinstalled games may now have DLSS
        EnsureScanCacheLoaded();
        if (_scanSkipCache!.Count > 0)
        {
            CrashReporter.Log($"[DlssStreamlineService.ClearScanCaches] Clearing scan skip cache ({_scanSkipCache.Count} entries)");
            _scanSkipCache.Clear();
            SaveScanCache();
        }

        // Invalidate trusted entries with null paths (partial detection — new DLLs may have appeared)
        // Entries with all paths populated stay trusted (validated at read time via PathsAreWithin)
        EnsureTrustedCacheLoaded();
        var toRemove = _trustedPathCache!
            .Where(kvp => kvp.Value.DlssPath == null || kvp.Value.DlssdPath == null
                       || kvp.Value.DlssgPath == null || kvp.Value.StreamlineFolder == null)
            .Select(kvp => kvp.Key)
            .ToList();

        if (toRemove.Count > 0)
        {
            foreach (var key in toRemove)
                _trustedPathCache!.Remove(key);
            SaveTrustedCache();
            CrashReporter.Log($"[DlssStreamlineService.ClearScanCaches] Invalidated {toRemove.Count} partial trusted entries for re-scan");
        }
    }

    private void EnsureScanCacheLoaded()
    {
        if (_scanSkipCache != null) return;
        try
        {
            if (File.Exists(ScanSkipCachePath))
            {
                var json = File.ReadAllText(ScanSkipCachePath);
                _scanSkipCache = JsonSerializer.Deserialize<Dictionary<string, int>>(json)
                    ?? new(StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                _scanSkipCache = new(StringComparer.OrdinalIgnoreCase);
            }
        }
        catch
        {
            _scanSkipCache = new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void SaveScanCache()
    {
        lock (_cacheSaveLock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ScanSkipCachePath)!);
                var json = JsonSerializer.Serialize(_scanSkipCache, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ScanSkipCachePath, json);
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[DlssStreamlineService.SaveScanCache] Failed — {ex.Message}");
            }
        }
    }

    // ── Trusted path cache (skip full scan for games with confirmed DLL locations) ──

    private static readonly string TrustedPathCachePath = Path.Combine(BaseStagingDir, "dlss_trusted_paths.json");
    private Dictionary<string, TrustedPathEntry>? _trustedPathCache;

    /// <summary>
    /// Attempts a fast detection using trusted cached paths. Returns a valid result if all
    /// cached paths still exist and are within the game's install path, or null if a full scan is needed.
    /// </summary>
    public DlssDetectionResult? TryFastDetect(string gameName, string installPath)
    {
        EnsureTrustedCacheLoaded();
        if (!_trustedPathCache!.TryGetValue(gameName, out var entry) || entry.ConfirmCount < SkipThreshold)
            return null;

        // Validate cached paths are inside the game's install tree (not a sibling game)
        var searchRoot = ResolveSearchRoot(installPath);
        if (!PathsAreWithin(entry, searchRoot))
        {
            CrashReporter.Log($"[DlssStreamlineService.TryFastDetect] Cached paths for '{gameName}' are outside install path — invalidating");
            InvalidateTrustedPath(gameName);
            return null;
        }

        // Verify cached paths still exist
        var result = new DlssDetectionResult();
        bool anyValid = false;

        if (entry.DlssPath != null)
        {
            if (File.Exists(entry.DlssPath)) { result.DlssPath = entry.DlssPath; result.DlssVersion = GetFileVersion(entry.DlssPath); anyValid = true; }
            else { InvalidateTrustedPath(gameName); return null; }
        }
        if (entry.DlssdPath != null)
        {
            if (File.Exists(entry.DlssdPath)) { result.DlssdPath = entry.DlssdPath; result.DlssdVersion = GetFileVersion(entry.DlssdPath); anyValid = true; }
            else { InvalidateTrustedPath(gameName); return null; }
        }
        if (entry.DlssgPath != null)
        {
            if (File.Exists(entry.DlssgPath)) { result.DlssgPath = entry.DlssgPath; result.DlssgVersion = GetFileVersion(entry.DlssgPath); anyValid = true; }
            else { InvalidateTrustedPath(gameName); return null; }
        }
        if (entry.StreamlineFolder != null)
        {
            var interposerPath = Path.Combine(entry.StreamlineFolder, StreamlineIndicator);
            if (File.Exists(interposerPath))
            {
                result.StreamlineInterposerPath = interposerPath;
                result.StreamlineFolder = entry.StreamlineFolder;
                result.StreamlineVersion = GetFileVersion(interposerPath);
                foreach (var slDll in KnownStreamlineDlls)
                    if (File.Exists(Path.Combine(entry.StreamlineFolder, slDll)))
                        result.StreamlineFiles.Add(slDll);
                anyValid = true;
            }
            else { InvalidateTrustedPath(gameName); return null; }
        }

        return anyValid ? result : null;
    }

    /// <summary>
    /// Records a successful detection result. Increments confirmation count if paths match.
    /// </summary>
    public void RecordTrustedPath(string gameName, DlssDetectionResult detection)
    {
        EnsureTrustedCacheLoaded();
        if (_trustedPathCache!.TryGetValue(gameName, out var existing))
        {
            if (existing.DlssPath == detection.DlssPath && existing.DlssdPath == detection.DlssdPath
                && existing.DlssgPath == detection.DlssgPath && existing.StreamlineFolder == detection.StreamlineFolder)
                existing.ConfirmCount++;
            else
            {
                existing.DlssPath = detection.DlssPath; existing.DlssdPath = detection.DlssdPath;
                existing.DlssgPath = detection.DlssgPath; existing.StreamlineFolder = detection.StreamlineFolder;
                existing.ConfirmCount = 1;
            }
        }
        else
        {
            _trustedPathCache[gameName] = new TrustedPathEntry
            {
                DlssPath = detection.DlssPath, DlssdPath = detection.DlssdPath,
                DlssgPath = detection.DlssgPath, StreamlineFolder = detection.StreamlineFolder,
                ConfirmCount = 1,
            };
        }
        SaveTrustedCache();
    }

    /// <summary>Checks if all non-null paths in the entry are within the given root directory.</summary>
    private static bool PathsAreWithin(TrustedPathEntry entry, string root)
    {
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (entry.DlssPath != null && !entry.DlssPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            return false;
        if (entry.DlssdPath != null && !entry.DlssdPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            return false;
        if (entry.DlssgPath != null && !entry.DlssgPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            return false;
        if (entry.StreamlineFolder != null && !entry.StreamlineFolder.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    /// <summary>Removes a game from the trusted path cache.</summary>
    public void InvalidateTrustedPath(string gameName)
    {
        EnsureTrustedCacheLoaded();
        if (_trustedPathCache!.Remove(gameName)) SaveTrustedCache();
    }

    private void EnsureTrustedCacheLoaded()
    {
        if (_trustedPathCache != null) return;
        try
        {
            if (File.Exists(TrustedPathCachePath))
            {
                var json = File.ReadAllText(TrustedPathCachePath);
                _trustedPathCache = JsonSerializer.Deserialize<Dictionary<string, TrustedPathEntry>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? new(StringComparer.OrdinalIgnoreCase);
            }
            else _trustedPathCache = new(StringComparer.OrdinalIgnoreCase);
        }
        catch { _trustedPathCache = new(StringComparer.OrdinalIgnoreCase); }
    }

    private void SaveTrustedCache()
    {
        lock (_cacheSaveLock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(TrustedPathCachePath)!);
                var json = JsonSerializer.Serialize(_trustedPathCache, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(TrustedPathCachePath, json);
            }
            catch (Exception ex) { CrashReporter.Log($"[DlssStreamlineService.SaveTrustedCache] Failed — {ex.Message}"); }
        }
    }

    public async Task<string?> EnsureNewestDlssCachedAsync()
    {
        var newest = _manifest?.Dlss?.FirstOrDefault();
        if (newest == null) return null;

        var cachedDir = Path.Combine(DlssCacheDir, newest.Version);
        var cachedDll = Path.Combine(cachedDir, DlssDllName);

        if (File.Exists(cachedDll))
            return cachedDll;

        // Download on-demand
        await DownloadAndCacheAsync(newest.Url, cachedDir, DlssDllName).ConfigureAwait(false);
        return File.Exists(cachedDll) ? cachedDll : null;
    }

    /// <summary>
    /// Returns the cached path for the newest DLSS RR DLL, downloading if needed.
    /// </summary>
    public async Task<string?> EnsureNewestDlssdCachedAsync()
    {
        var newest = _manifest?.Dlssd?.FirstOrDefault();
        if (newest == null) return null;

        var cachedDir = Path.Combine(DlssdCacheDir, newest.Version);
        var cachedDll = Path.Combine(cachedDir, DlssdDllName);

        if (File.Exists(cachedDll))
            return cachedDll;

        await DownloadAndCacheAsync(newest.Url, cachedDir, DlssdDllName).ConfigureAwait(false);
        return File.Exists(cachedDll) ? cachedDll : null;
    }

    /// <summary>
    /// Returns the cached path for the newest DLSS FG DLL, downloading if needed.
    /// </summary>
    public async Task<string?> EnsureNewestDlssgCachedAsync()
    {
        var newest = _manifest?.Dlssg?.FirstOrDefault();
        if (newest == null) return null;

        var cachedDir = Path.Combine(DlssgCacheDir, newest.Version);
        var cachedDll = Path.Combine(cachedDir, DlssgDllName);

        if (File.Exists(cachedDll))
            return cachedDll;

        await DownloadAndCacheAsync(newest.Url, cachedDir, DlssgDllName).ConfigureAwait(false);
        return File.Exists(cachedDll) ? cachedDll : null;
    }
}

// ── Manifest data model ───────────────────────────────────────────────────────

public class DlssManifestData
{
    public List<DlssManifestEntry>? Dlss { get; set; }
    public List<DlssManifestEntry>? Dlssd { get; set; }
    public List<DlssManifestEntry>? Dlssg { get; set; }
    public List<DlssManifestEntry>? Streamline { get; set; }
}

public class DlssManifestEntry
{
    public string Version { get; set; } = "";
    public string Url { get; set; } = "";
}

public class TrustedPathEntry
{
    public string? DlssPath { get; set; }
    public string? DlssdPath { get; set; }
    public string? DlssgPath { get; set; }
    public string? StreamlineFolder { get; set; }
    public int ConfirmCount { get; set; }
}
