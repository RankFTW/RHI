using System.IO.Compression;
using System.Text.Json;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Installs and manages RE Framework (dinput8.dll) for RE Engine games.
/// Downloads REFramework.zip from GitHub nightly releases, extracts dinput8.dll,
/// caches it locally, and copies it to the game directory.
/// </summary>
public class REFrameworkService : IREFrameworkService
{
    // ── Constants ──────────────────────────────────────────────────────────────────

    private const string DllFileName = "dinput8.dll";
    private const string DownloadBaseUrl = "https://github.com/praydog/REFramework-nightly/releases/latest/download/";
    private const string ReleasesApiUrl = "https://api.github.com/repos/praydog/REFramework-nightly/releases";

    /// <summary>
    /// Maps game names to their RE Framework nightly ZIP filename.
    /// Since the monolithic REFramework.zip build, all RE Engine games use the
    /// same zip. The map is retained for any future per-game overrides.
    /// </summary>
    private static readonly Dictionary<string, string> GameZipMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // All RE Engine games now use the monolithic REFramework.zip.
        // Per-game zips (DMC5.zip, RE2.zip, etc.) are no longer published.
    };

    /// <summary>The monolithic zip that works for all supported RE Engine games.</summary>
    private const string MonolithicZipName = "REFramework.zip";

    // ── Paths ─────────────────────────────────────────────────────────────────────

    private static readonly string CacheDir = Path.Combine(DownloadPaths.Root, "reframework");

    private static readonly string DbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "reframework_installed.json");

    // ── State ─────────────────────────────────────────────────────────────────────

    private readonly HttpClient _http;
    private readonly GitHubETagCache _etagCache;

    /// <summary>Session-level cache for the latest release tag to avoid repeated API calls.</summary>
    private string? _cachedLatestVersion;

    public REFrameworkService(HttpClient http, GitHubETagCache etagCache)
    {
        _http = http;
        _etagCache = etagCache;
    }

    // ── InstallAsync ──────────────────────────────────────────────────────────────

    public async Task<REFrameworkInstalledRecord> InstallAsync(
        string gameName, string installPath,
        IProgress<(string message, double percent)>? progress = null)
    {
        var zipName = ResolveZipName(gameName);
        var downloadUrl = DownloadBaseUrl + zipName;
        var gameCacheDir = Path.Combine(CacheDir, Path.GetFileNameWithoutExtension(zipName));
        Directory.CreateDirectory(gameCacheDir);

        var zipPath = Path.Combine(gameCacheDir, zipName);
        var cachedDll = Path.Combine(gameCacheDir, DllFileName);
        var destPath = Path.Combine(installPath, DllFileName);

        try
        {
            // ── Download game-specific ZIP ────────────────────────────────────
            progress?.Report(("Downloading RE Framework...", 10));
            CrashReporter.Log($"[REFrameworkService.InstallAsync] Downloading {downloadUrl}");

            using var response = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await response.Content.CopyToAsync(fs);
            }

            progress?.Report(("Extracting dinput8.dll...", 50));

            // ── Extract dinput8.dll from ZIP ──────────────────────────────────
            ExtractDllFromZip(zipPath, cachedDll);

            // ── Delete ZIP after successful extraction ────────────────────────
            try { File.Delete(zipPath); }
            catch (Exception ex)
            {
                CrashReporter.Log($"[REFrameworkService.InstallAsync] Failed to delete ZIP — {ex.Message}");
            }

            // ── Copy cached DLL to game directory ─────────────────────────────
            progress?.Report(("Installing dinput8.dll...", 80));
            File.Copy(cachedDll, destPath, overwrite: true);

            // ── Fetch version tag ─────────────────────────────────────────────
            var version = await GetLatestVersionAsync() ?? "unknown";

            progress?.Report(("RE Framework installed!", 100));

            // ── Persist install record ────────────────────────────────────────
            var record = new REFrameworkInstalledRecord
            {
                GameName = gameName,
                InstallPath = installPath,
                InstalledVersion = version,
                InstalledAt = DateTime.UtcNow,
            };
            SaveRecord(record);
            return record;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[REFrameworkService.InstallAsync] Failed for '{gameName}' — {ex.Message}");

            // Clean up partial ZIP on failure
            try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { /* best-effort */ }

            throw;
        }
    }

    // ── ZIP extraction ────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts dinput8.dll from the given ZIP archive to the specified destination path.
    /// </summary>
    private static void ExtractDllFromZip(string zipPath, string destDllPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);

        var entry = archive.Entries.FirstOrDefault(e =>
            e.Name.Equals(DllFileName, StringComparison.OrdinalIgnoreCase));

        if (entry == null)
            throw new FileNotFoundException($"{DllFileName} not found inside {Path.GetFileName(zipPath)}");

        // Extract to destination, overwriting any existing cached copy
        entry.ExtractToFile(destDllPath, overwrite: true);
    }

    // ── ZIP name resolution ───────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the RE Framework ZIP filename. Since the switch to the monolithic
    /// build, all RE Engine games use REFramework.zip. The GameZipMap is checked
    /// first for any future per-game overrides.
    /// </summary>
    private static string ResolveZipName(string gameName)
    {
        if (GameZipMap.TryGetValue(gameName, out var zip))
            return zip;

        // Strip ™®© and retry exact match
        var stripped = gameName.Replace("™", "").Replace("®", "").Replace("©", "").Trim();
        if (stripped != gameName && GameZipMap.TryGetValue(stripped, out zip))
            return zip;

        return MonolithicZipName;
    }

    // ── Version tracking ──────────────────────────────────────────────────────────

    public async Task<string?> GetLatestVersionAsync()
    {
        // Return session-cached value if available
        if (_cachedLatestVersion != null)
            return _cachedLatestVersion;

        try
        {
            CrashReporter.Log("[REFrameworkService.GetLatestVersionAsync] Fetching latest release tag");

            // Use the /releases endpoint and pick the first (latest) release
            var json = await _etagCache.GetWithETagAsync(_http, ReleasesApiUrl).ConfigureAwait(false);
            if (json == null)
            {
                CrashReporter.Log("[REFrameworkService.GetLatestVersionAsync] GitHub API returned error");
                return null;
            }
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                var first = root[0];
                if (first.TryGetProperty("tag_name", out var tagProp))
                {
                    _cachedLatestVersion = ExtractVersionNumber(tagProp.GetString());
                    CrashReporter.Log($"[REFrameworkService.GetLatestVersionAsync] Latest tag: {_cachedLatestVersion}");
                    return _cachedLatestVersion;
                }
            }

            CrashReporter.Log("[REFrameworkService.GetLatestVersionAsync] No releases found");
            return null;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[REFrameworkService.GetLatestVersionAsync] Failed — {ex.Message}");
            return null;
        }
    }

    // ── Update checking ───────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the numeric build number from a nightly tag like "nightly-01302-abcdef1".
    /// Returns the raw tag if no number is found.
    /// </summary>
    private static string? ExtractVersionNumber(string? tag)
    {
        if (string.IsNullOrEmpty(tag)) return tag;
        // Tags look like "nightly-01302-abcdef1" — grab the first numeric segment
        foreach (var part in tag.Split('-'))
        {
            if (part.Length > 0 && part.All(char.IsDigit))
                return part;
        }
        return tag;
    }

    public async Task<bool> CheckForUpdateAsync(string installedVersion)
    {
        var latest = await GetLatestVersionAsync();
        if (latest == null) return false;

        // Simple string comparison — nightly tags are monotonically increasing numbers
        return !string.Equals(installedVersion, latest, StringComparison.OrdinalIgnoreCase);
    }

    // ── Uninstall ─────────────────────────────────────────────────────────────────

    public void Uninstall(string gameName, string installPath)
    {
        var dllPath = Path.Combine(installPath, DllFileName);
        if (File.Exists(dllPath))
        {
            File.Delete(dllPath);
            CrashReporter.Log($"[REFrameworkService.Uninstall] Deleted {DllFileName} from {installPath}");
        }

        RemoveRecord(gameName, installPath);
    }

    // ── PD-Upscaler (OptiScaler compatibility) ────────────────────────────────────

    private const string PdUpscalerDownloadBase =
        "https://nightly.link/praydog/REFramework/workflows/dev-release/pd-upscaler/";

    private const string StandardBackupSuffix = ".rhi_standard_backup";

    /// <inheritdoc />
    public async Task InstallPdUpscalerAsync(
        string gameName, string installPath, string artifactName,
        IProgress<(string message, double percent)>? progress = null)
    {
        var downloadUrl = $"{PdUpscalerDownloadBase}{artifactName}.zip";
        var destDll = Path.Combine(installPath, DllFileName);
        var backupPath = destDll + StandardBackupSuffix;
        var tempDir = Path.Combine(Path.GetTempPath(), $"rhi_pdupscaler_{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempDir);

            // ── Download outer ZIP from nightly.link ──────────────────────────
            progress?.Report(("Downloading PD-Upscaler REFramework...", 10));
            CrashReporter.Log($"[REFrameworkService.InstallPdUpscalerAsync] Downloading {downloadUrl}");

            var outerZipPath = Path.Combine(tempDir, $"{artifactName}_outer.zip");
            using (var response = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                await using var fs = new FileStream(outerZipPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await response.Content.CopyToAsync(fs);
            }

            progress?.Report(("Extracting PD-Upscaler REFramework...", 40));

            // ── Extract outer ZIP → inner {artifactName}.zip ──────────────────
            var outerExtractDir = Path.Combine(tempDir, "outer");
            ZipFile.ExtractToDirectory(outerZipPath, outerExtractDir);

            var innerZipPath = Path.Combine(outerExtractDir, $"{artifactName}.zip");
            if (!File.Exists(innerZipPath))
            {
                // Fallback: look for any .zip inside the outer archive
                innerZipPath = Directory.GetFiles(outerExtractDir, "*.zip").FirstOrDefault()
                    ?? throw new FileNotFoundException(
                        $"Inner ZIP not found in pd-upscaler download for {artifactName}");
            }

            // ── Extract inner ZIP → dinput8.dll ───────────────────────────────
            var innerExtractDir = Path.Combine(tempDir, "inner");
            ZipFile.ExtractToDirectory(innerZipPath, innerExtractDir);

            var extractedDll = Path.Combine(innerExtractDir, DllFileName);
            if (!File.Exists(extractedDll))
                throw new FileNotFoundException(
                    $"{DllFileName} not found inside pd-upscaler inner ZIP for {artifactName}");

            progress?.Report(("Backing up standard REFramework...", 60));

            // ── Back up existing standard dinput8.dll ─────────────────────────
            if (File.Exists(destDll) && !File.Exists(backupPath))
            {
                File.Copy(destDll, backupPath, overwrite: false);
                CrashReporter.Log($"[REFrameworkService.InstallPdUpscalerAsync] Backed up standard {DllFileName} → {Path.GetFileName(backupPath)}");
            }

            // ── Copy pd-upscaler DLL to game folder ──────────────────────────
            progress?.Report(("Installing PD-Upscaler REFramework...", 80));
            File.Copy(extractedDll, destDll, overwrite: true);

            // ── Update install record with PD-Upscaler version ───────────────
            var records = LoadRecords();
            var existing = records.FirstOrDefault(r =>
                r.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase) &&
                r.InstallPath.Equals(installPath, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                existing.InstalledVersion = "PD-Upscaler";
                existing.InstalledAt = DateTime.UtcNow;
                SaveRecords(records);
            }
            else
            {
                SaveRecord(new REFrameworkInstalledRecord
                {
                    GameName = gameName,
                    InstallPath = installPath,
                    InstalledVersion = "PD-Upscaler",
                    InstalledAt = DateTime.UtcNow,
                });
            }

            progress?.Report(("PD-Upscaler REFramework installed!", 100));
            CrashReporter.Log($"[REFrameworkService.InstallPdUpscalerAsync] PD-Upscaler installed for '{gameName}'");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[REFrameworkService.InstallPdUpscalerAsync] Failed for '{gameName}' — {ex.Message}");
            throw;
        }
        finally
        {
            // ── Clean up temp files ───────────────────────────────────────────
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
            catch (Exception ex)
            {
                CrashReporter.Log($"[REFrameworkService.InstallPdUpscalerAsync] Temp cleanup failed — {ex.Message}");
            }
        }
    }

    /// <inheritdoc />
    public void RestoreStandardREFramework(string gameName, string installPath)
    {
        var dllPath = Path.Combine(installPath, DllFileName);
        var backupPath = dllPath + StandardBackupSuffix;

        if (!File.Exists(backupPath))
        {
            CrashReporter.Log($"[REFrameworkService.RestoreStandardREFramework] No backup found at {backupPath}");
            return;
        }

        try
        {
            // Delete the pd-upscaler DLL and restore the standard backup
            if (File.Exists(dllPath))
                File.Delete(dllPath);

            File.Move(backupPath, dllPath);

            // Restore the version in the install record
            var records = LoadRecords();
            var existing = records.FirstOrDefault(r =>
                r.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase) &&
                r.InstallPath.Equals(installPath, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                // Restore to the cached latest version, or "unknown" if unavailable
                existing.InstalledVersion = _cachedLatestVersion ?? "unknown";
                existing.InstalledAt = DateTime.UtcNow;
                SaveRecords(records);
            }

            CrashReporter.Log($"[REFrameworkService.RestoreStandardREFramework] Restored standard {DllFileName} for '{gameName}'");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[REFrameworkService.RestoreStandardREFramework] Failed for '{gameName}' — {ex.Message}");
        }
    }

    // ── Persistence ───────────────────────────────────────────────────────────────

    public List<REFrameworkInstalledRecord> GetRecords() => LoadRecords();

    private List<REFrameworkInstalledRecord> LoadRecords()
    {
        try
        {
            if (!File.Exists(DbPath)) return new();
            return JsonSerializer.Deserialize<List<REFrameworkInstalledRecord>>(
                File.ReadAllText(DbPath)) ?? new();
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[REFrameworkService.LoadRecords] Failed — {ex.Message}");
            return new();
        }
    }

    private void SaveRecords(List<REFrameworkInstalledRecord> records)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
        var json = JsonSerializer.Serialize(records,
            new JsonSerializerOptions { WriteIndented = true });
        FileHelper.WriteAllTextWithRetry(DbPath, json, "REFrameworkService.SaveRecords");
    }

    private void SaveRecord(REFrameworkInstalledRecord record)
    {
        var records = LoadRecords();
        var i = records.FindIndex(r =>
            r.GameName.Equals(record.GameName, StringComparison.OrdinalIgnoreCase) &&
            r.InstallPath.Equals(record.InstallPath, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) records[i] = record; else records.Add(record);
        SaveRecords(records);
    }

    private void RemoveRecord(string gameName, string installPath)
    {
        var records = LoadRecords();
        records.RemoveAll(r =>
            r.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase) &&
            r.InstallPath.Equals(installPath, StringComparison.OrdinalIgnoreCase));
        SaveRecords(records);
    }
}
