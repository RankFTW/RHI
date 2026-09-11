using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using RenoDXCommander.Models;

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
    private const string DlssnrDllName = "nvngx_dlssnr.dll";
    private const string StreamlineIndicator = "sl.common.dll";
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
    private static readonly string DlssnrCacheDir = Path.Combine(BaseStagingDir, "DLSS-NR");
    private static readonly string StreamlineCacheDir = Path.Combine(BaseStagingDir, "Streamline");
    private static readonly string DlssCustomDir = Path.Combine(BaseStagingDir, "Custom", "DLSS");
    private static readonly string StreamlineCustomDir = Path.Combine(BaseStagingDir, "Custom", "Streamline");
    private static readonly string CustomBaseDir = Path.Combine(BaseStagingDir, "Custom");

    /// <summary>AppData backup root for game-original Streamline DLLs — one subfolder per game name.</summary>
    internal static readonly string StreamlineBackupsDir = Path.Combine(BaseStagingDir, "StreamlineBackups");

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

    public IReadOnlyList<string> DlssVersions => BuildVersionList(_manifest?.Dlss, _manifest?.DlssDev);
    public IReadOnlyList<string> DlssdVersions => BuildVersionList(_manifest?.Dlssd, _manifest?.DlssdDev);
    public IReadOnlyList<string> DlssgVersions => BuildVersionList(_manifest?.Dlssg, _manifest?.DlssgDev);
    public IReadOnlyList<string> DlssnrVersions => BuildVersionList(_manifest?.Dlssnr, _manifest?.DlssnrDev);
    public IReadOnlyList<string> StreamlineVersions => BuildVersionList(_manifest?.Streamline, _manifest?.StreamlineDev);

    private static IReadOnlyList<string> BuildVersionList(
        List<DlssManifestEntry>? regular,
        List<DlssManifestEntry>? dev)
    {
        var entries = regular ?? new List<DlssManifestEntry>();
        if (DevUnlockService.IsUnlocked && dev != null && dev.Count > 0)
            entries = dev.Concat(entries).ToList(); // dev entries first (newest)
        return entries.Select(e => FormatVersion(e.Version)).ToList().AsReadOnly();
    }

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
                    $"{_manifest?.Dlssg?.Count ?? 0} FG, {_manifest?.Dlssnr?.Count ?? 0} NR, {_manifest?.Streamline?.Count ?? 0} SL versions");
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

    /// <summary>
    /// Returns the path of the sl.*.dll with the highest file version in the given folder,
    /// excluding sl.common.dll (which is the primary version source).
    /// Used as the version source when sl.common.dll is not present.
    /// </summary>
    private string? GetHighestVersionedSlDll(string folder)
    {
        string? bestPath = null;
        Version? bestVersion = null;
        foreach (var dll in KnownStreamlineDlls)
        {
            if (string.Equals(dll, StreamlineIndicator, StringComparison.OrdinalIgnoreCase)) continue;
            var path = Path.Combine(folder, dll);
            if (!File.Exists(path)) continue;
            var vStr = GetFileVersion(path);
            if (vStr != null && Version.TryParse(vStr, out var v) && (bestVersion == null || v > bestVersion))
            {
                bestVersion = v;
                bestPath = path;
            }
        }
        return bestPath;
    }

    /// <summary>Returns true if <paramref name="a"/> is a higher version than <paramref name="b"/>.</summary>
    private static bool IsHigherVersion(string? a, string? b)
    {
        if (a == null) return false;
        if (b == null) return true;
        return Version.TryParse(a, out var va) && Version.TryParse(b, out var vb) && va > vb;
    }

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
    /// On standard refresh (background scan), re-scans games that are in the skip cache
    /// (confirmed no DLSS after 3+ scans). If DLSS is now found (e.g. preloaded game
    /// got its files on release day), removes the game from the skip cache so it flows
    /// into the trusted path system on the next BuildCards pass.
    /// Does NOT increment counts or change anything for games that still have no DLSS.
    /// </summary>
    public void RecheckSkipList(IReadOnlyList<DetectedGame> games)
    {
        EnsureScanCacheLoaded();
        CrashReporter.Log($"[DlssStreamlineService.RecheckSkipList] Starting recheck, skip cache has {_scanSkipCache!.Count} entries");
        if (_scanSkipCache!.Count == 0) return;

        var toRemove = new List<string>();
        foreach (var game in games)
        {
            if (!ShouldSkipScan(game.Name)) continue;
            if (string.IsNullOrEmpty(game.InstallPath) || !Directory.Exists(game.InstallPath)) continue;

            try
            {
                var result = Detect(game.InstallPath);
                if (result.HasAny)
                {
                    toRemove.Add(game.Name);
                    CrashReporter.Log($"[DlssStreamlineService.RecheckSkipList] '{game.Name}' — DLSS now found, removing from skip cache");
                }
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[DlssStreamlineService.RecheckSkipList] '{game.Name}' scan failed — {ex.Message}");
            }
        }

        if (toRemove.Count > 0)
        {
            foreach (var name in toRemove)
                _scanSkipCache!.Remove(name);
            SaveScanCache();
            CrashReporter.Log($"[DlssStreamlineService.RecheckSkipList] Removed {toRemove.Count} game(s) from skip cache");
        }
        CrashReporter.Log($"[DlssStreamlineService.RecheckSkipList] Done");
    }

    /// <summary>
    /// Invalidates trusted path entries that have null components (partial detection).
    /// Games with fully populated paths keep their trusted status.
    /// Called on Full Refresh to detect newly added DLLs (e.g. game update adds RR/FG).
    /// Also clears the scan skip cache so reinstalled games are re-scanned.
    /// </summary>
    // ── Trusted path cache version ────────────────────────────────────────────
    // Bump this when new DLL types are added to detection (e.g. DlssnrPath).
    // Causes a one-time full rescan on the next Full Refresh for all existing entries.
    private const int CurrentTrustedCacheVersion = 2; // bumped to force rescan of entries missing DlssnrPath

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

        // Invalidate trusted entries that are:
        // - Partial (any required path is null) — new DLLs may have appeared
        // - Outdated (cache version < current) — new DLL types were added since entry was created
        EnsureTrustedCacheLoaded();
        var toRemove = _trustedPathCache!
            .Where(kvp => kvp.Value.DlssPath == null || kvp.Value.DlssdPath == null
                       || kvp.Value.DlssgPath == null || kvp.Value.StreamlineFolder == null
                       || kvp.Value.CacheVersion < CurrentTrustedCacheVersion)
            .Select(kvp => kvp.Key)
            .ToList();

        if (toRemove.Count > 0)
        {
            foreach (var key in toRemove)
                _trustedPathCache!.Remove(key);
            SaveTrustedCache();
            CrashReporter.Log($"[DlssStreamlineService.ClearScanCaches] Invalidated {toRemove.Count} partial/outdated trusted entries for re-scan");
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
        if (entry.DlssnrPath != null)
        {
            if (File.Exists(entry.DlssnrPath)) { result.DlssnrPath = entry.DlssnrPath; result.DlssnrVersion = GetFileVersion(entry.DlssnrPath); anyValid = true; }
            else { InvalidateTrustedPath(gameName); return null; }
        }
        if (entry.StreamlineFolder != null)
        {
            var commonPath = Path.Combine(entry.StreamlineFolder, StreamlineIndicator);
            var versionSourcePath = File.Exists(commonPath) ? commonPath
                : GetHighestVersionedSlDll(entry.StreamlineFolder);

            if (versionSourcePath != null)
            {
                result.StreamlineInterposerPath = versionSourcePath;
                result.StreamlineFolder = entry.StreamlineFolder;
                result.StreamlineVersion = GetFileVersion(versionSourcePath);
                foreach (var slDll in KnownStreamlineDlls)
                    if (File.Exists(Path.Combine(entry.StreamlineFolder, slDll)))
                        result.StreamlineFiles.Add(slDll);
                anyValid = true;
            }
            else { InvalidateTrustedPath(gameName); return null; }
        }

        // Populate original versions from cache
        result.OriginalDlssVersion = entry.OriginalDlssVersion;
        result.OriginalDlssdVersion = entry.OriginalDlssdVersion;
        result.OriginalDlssgVersion = entry.OriginalDlssgVersion;
        result.OriginalDlssnrVersion = entry.OriginalDlssnrVersion;
        result.OriginalStreamlineVersion = entry.OriginalStreamlineVersion;

        // One-time backfill: if original versions aren't cached yet, read them now
        bool needsSave = false;
        if (result.DlssPath != null && entry.OriginalDlssVersion == null)
        {
            var backup = result.DlssPath + ".original";
            result.OriginalDlssVersion = File.Exists(backup) ? GetFileVersion(backup) : result.DlssVersion;
            entry.OriginalDlssVersion = result.OriginalDlssVersion;
            needsSave = true;
        }
        if (result.DlssdPath != null && entry.OriginalDlssdVersion == null)
        {
            var backup = result.DlssdPath + ".original";
            result.OriginalDlssdVersion = File.Exists(backup) ? GetFileVersion(backup) : result.DlssdVersion;
            entry.OriginalDlssdVersion = result.OriginalDlssdVersion;
            needsSave = true;
        }
        if (result.DlssgPath != null && entry.OriginalDlssgVersion == null)
        {
            var backup = result.DlssgPath + ".original";
            result.OriginalDlssgVersion = File.Exists(backup) ? GetFileVersion(backup) : result.DlssgVersion;
            entry.OriginalDlssgVersion = result.OriginalDlssgVersion;
            needsSave = true;
        }
        if (result.DlssnrPath != null && entry.OriginalDlssnrVersion == null)
        {
            var backup = result.DlssnrPath + ".original";
            result.OriginalDlssnrVersion = File.Exists(backup) ? GetFileVersion(backup) : result.DlssnrVersion;
            entry.OriginalDlssnrVersion = result.OriginalDlssnrVersion;
            needsSave = true;
        }
        if (result.StreamlineInterposerPath != null && entry.OriginalStreamlineVersion == null)
        {
            var inGameBackup = result.StreamlineInterposerPath + ".original";
            result.OriginalStreamlineVersion = File.Exists(inGameBackup)
                ? GetFileVersion(inGameBackup)
                : result.StreamlineVersion;
            entry.OriginalStreamlineVersion = result.OriginalStreamlineVersion;
            needsSave = true;
        }
        if (needsSave) SaveTrustedCache();

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
            if (string.Equals(existing.DlssPath, detection.DlssPath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.DlssdPath, detection.DlssdPath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.DlssgPath, detection.DlssgPath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.DlssnrPath, detection.DlssnrPath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.StreamlineFolder, detection.StreamlineFolder, StringComparison.OrdinalIgnoreCase))
                existing.ConfirmCount++;
            else
            {
                existing.DlssPath = detection.DlssPath; existing.DlssdPath = detection.DlssdPath;
                existing.DlssgPath = detection.DlssgPath; existing.DlssnrPath = detection.DlssnrPath;
                existing.StreamlineFolder = detection.StreamlineFolder;
                existing.ConfirmCount = 1;
            }
            // Always keep CacheVersion current so future version bumps don't force unnecessary rescans
            existing.CacheVersion = CurrentTrustedCacheVersion;
            // Update original versions (keep existing if already set, overwrite if detection has them)
            existing.OriginalDlssVersion ??= detection.OriginalDlssVersion;
            existing.OriginalDlssdVersion ??= detection.OriginalDlssdVersion;
            existing.OriginalDlssgVersion ??= detection.OriginalDlssgVersion;
            existing.OriginalDlssnrVersion ??= detection.OriginalDlssnrVersion;
            existing.OriginalStreamlineVersion ??= detection.OriginalStreamlineVersion;
        }
        else
        {
            _trustedPathCache[gameName] = new TrustedPathEntry
            {
                DlssPath = detection.DlssPath, DlssdPath = detection.DlssdPath,
                DlssgPath = detection.DlssgPath, DlssnrPath = detection.DlssnrPath,
                StreamlineFolder = detection.StreamlineFolder,
                ConfirmCount = 1,
                CacheVersion = CurrentTrustedCacheVersion,
                OriginalDlssVersion = detection.OriginalDlssVersion,
                OriginalDlssdVersion = detection.OriginalDlssdVersion,
                OriginalDlssgVersion = detection.OriginalDlssgVersion,
                OriginalDlssnrVersion = detection.OriginalDlssnrVersion,
                OriginalStreamlineVersion = detection.OriginalStreamlineVersion,
            };
        }
        SaveTrustedCache();
    }

    /// <summary>Checks if all non-null paths in the entry are within the given root directory.</summary>
    private static bool PathsAreWithin(TrustedPathEntry entry, string root)
    {
        // Normalize to backslashes so mixed forward/backslash paths don't cause false mismatches
        var normalizedRoot = root.Replace('/', '\\').TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var dlssPath = entry.DlssPath?.Replace('/', '\\');
        var dlssdPath = entry.DlssdPath?.Replace('/', '\\');
        var dlssgPath = entry.DlssgPath?.Replace('/', '\\');
        var dlssnrPath = entry.DlssnrPath?.Replace('/', '\\');
        var streamlineFolder = entry.StreamlineFolder?.Replace('/', '\\');

        if (dlssPath != null && !dlssPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            return false;
        if (dlssdPath != null && !dlssdPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            return false;
        if (dlssgPath != null && !dlssgPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            return false;
        if (dlssnrPath != null && !dlssnrPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            return false;
        if (streamlineFolder != null
            && !streamlineFolder.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(streamlineFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                              root.Replace('/', '\\').TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                              StringComparison.OrdinalIgnoreCase))
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

    /// <summary>
    /// Returns the cached path for the newest DLSS NR DLL, downloading if needed.
    /// </summary>
    public async Task<string?> EnsureNewestDlssnrCachedAsync()
    {
        var newest = _manifest?.Dlssnr?.FirstOrDefault();
        if (newest == null) return null;

        var cachedDir = Path.Combine(DlssnrCacheDir, newest.Version);
        var cachedDll = Path.Combine(cachedDir, DlssnrDllName);

        if (File.Exists(cachedDll))
            return cachedDll;

        await DownloadAndCacheAsync(newest.Url, cachedDir, DlssnrDllName).ConfigureAwait(false);
        return File.Exists(cachedDll) ? cachedDll : null;
    }

    /// <inheritdoc />
    public string? GetCachedNrDllPath()
    {
        var newest = _manifest?.Dlssnr?.FirstOrDefault();
        if (newest == null) return null;
        var cachedDll = Path.Combine(DlssnrCacheDir, newest.Version, DlssnrDllName);
        return File.Exists(cachedDll) ? cachedDll : null;
    }

    /// <summary>
    /// Returns the cached path for a specific DLSS NR DLL version (e.g. "310.8.2"), downloading if needed.
    /// Returns null if the version is not in the manifest or download fails.
    /// </summary>
    public async Task<string?> EnsureSpecificDlssnrCachedAsync(string version)
    {
        var entry = FindDlssnrEntry(version);
        if (entry == null)
        {
            CrashReporter.Log($"[DlssStreamlineService.EnsureSpecificDlssnrCachedAsync] Version '{version}' not found in manifest");
            return null;
        }

        var cachedDir = Path.Combine(DlssnrCacheDir, version);
        var cachedDll = Path.Combine(cachedDir, DlssnrDllName);

        if (File.Exists(cachedDll))
            return cachedDll;

        await DownloadAndCacheAsync(entry.Url, cachedDir, DlssnrDllName).ConfigureAwait(false);
        return File.Exists(cachedDll) ? cachedDll : null;
    }

    /// <inheritdoc />
    public async Task<string?> EnsureNewestStreamlineCachedAsync()
    {
        var newest = _manifest?.Streamline?.FirstOrDefault();
        if (newest == null) return null;

        var cachedDir = Path.Combine(StreamlineCacheDir, newest.Version);
        var indicator = Path.Combine(cachedDir, StreamlineIndicator);
        var fallback  = Path.Combine(cachedDir, "sl.common.dll");

        if (File.Exists(indicator) || File.Exists(fallback))
            return cachedDir;

        await DownloadAndCacheStreamlineAsync(newest.Url, cachedDir).ConfigureAwait(false);
        return (File.Exists(indicator) || File.Exists(fallback)) ? cachedDir : null;
    }
}

// ── Manifest data model ───────────────────────────────────────────────────────

public class DlssManifestData
{
    public List<DlssManifestEntry>? Dlss { get; set; }
    public List<DlssManifestEntry>? Dlssd { get; set; }
    public List<DlssManifestEntry>? Dlssg { get; set; }
    public List<DlssManifestEntry>? Dlssnr { get; set; }
    public List<DlssManifestEntry>? Streamline { get; set; }

    /// <summary>Dev-only DLSS SR versions (only shown when unlock.txt is present).</summary>
    public List<DlssManifestEntry>? DlssDev { get; set; }
    /// <summary>Dev-only DLSS RR versions (only shown when unlock.txt is present).</summary>
    public List<DlssManifestEntry>? DlssdDev { get; set; }
    /// <summary>Dev-only DLSS FG versions (only shown when unlock.txt is present).</summary>
    public List<DlssManifestEntry>? DlssgDev { get; set; }
    /// <summary>Dev-only DLSS NR versions (only shown when unlock.txt is present).</summary>
    public List<DlssManifestEntry>? DlssnrDev { get; set; }
    /// <summary>Dev-only Streamline versions (only shown when unlock.txt is present).</summary>
    public List<DlssManifestEntry>? StreamlineDev { get; set; }
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
    public string? DlssnrPath { get; set; }
    public string? StreamlineFolder { get; set; }
    public int ConfirmCount { get; set; }

    /// <summary>Schema version when this entry was written — used to force rescans when new DLL types are added.</summary>
    public int CacheVersion { get; set; }

    // Cached original/default versions (from .original backup or initial detection)
    public string? OriginalDlssVersion { get; set; }
    public string? OriginalDlssdVersion { get; set; }
    public string? OriginalDlssgVersion { get; set; }
    public string? OriginalDlssnrVersion { get; set; }
    public string? OriginalStreamlineVersion { get; set; }
}
