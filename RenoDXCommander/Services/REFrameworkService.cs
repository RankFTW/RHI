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
    private const string LatestReleasePageUrl = "https://github.com/praydog/REFramework-nightly/releases/latest";
    private const string ReleasesApiUrl ="https://api.github.com/repos/praydog/REFramework-nightly/releases";

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
        IProgress<(string message, double percent)>? progress = null,
        string? store = null)
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
            var zipSize = new FileInfo(zipPath).Length;
            CrashReporter.Log($"[REFrameworkService.InstallAsync] Downloaded {zipName} ({zipSize:N0} bytes)");

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
            AuxInstallService.SentinelBackup(destPath);
            File.Copy(cachedDll, destPath, overwrite: true);

            // ── Fetch version tag ─────────────────────────────────────────────
            var version = await GetLatestVersionAsync() ?? "unknown";
            CrashReporter.Log($"[REFrameworkService.InstallAsync] Installed RE Framework build {version} ({zipName} {zipSize:N0} bytes, {DllFileName} {new FileInfo(destPath).Length:N0} bytes) for '{gameName}' in '{installPath}'");

            progress?.Report(("RE Framework installed!", 100));

            // ── Update History (an install over an older build; first installs aren't updates) ──
            var previousVersion = LoadRecords().FirstOrDefault(r =>
                r.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase) &&
                r.InstallPath.Equals(installPath, StringComparison.OrdinalIgnoreCase))?.InstalledVersion;
            // One name for every game, so the log's dedupe keeps one entry per build during Update All
            UpdateHistory.Record("RE Framework", "RE Framework", previousVersion, version);

            // ── Persist install record ────────────────────────────────────────
            var record = new REFrameworkInstalledRecord
            {
                GameName = gameName,
                InstallPath = installPath,
                Store = store ?? "",
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

        // The web page redirect costs no API quota (60/hour without a token) and is the same
        // release the zip downloads from. When rate limited, the API below only returns the
        // stale cached list, which left users stuck on an old build.
        var fromRedirect = await GetLatestVersionFromRedirectAsync().ConfigureAwait(false);
        if (fromRedirect != null)
        {
            _cachedLatestVersion = fromRedirect;
            CrashReporter.Log($"[REFrameworkService.GetLatestVersionAsync] Latest tag (web): {_cachedLatestVersion}");
            return _cachedLatestVersion;
        }

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

    /// <summary>
    /// Reads the latest tag from where github.com/.../releases/latest redirects
    /// (…/releases/tag/nightly-01438-…). Returns null if that fails.
    /// </summary>
    private async Task<string?> GetLatestVersionFromRedirectAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var request = new HttpRequestMessage(HttpMethod.Head, LatestReleasePageUrl);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            var finalUrl = response.RequestMessage?.RequestUri?.AbsolutePath ?? "";
            const string marker = "/releases/tag/";
            var idx = finalUrl.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (!response.IsSuccessStatusCode || idx < 0)
            {
                CrashReporter.Log($"[REFrameworkService.GetLatestVersionFromRedirectAsync] No tag in redirect (HTTP {(int)response.StatusCode}, '{finalUrl}')");
                return null;
            }
            var tag = Uri.UnescapeDataString(finalUrl[(idx + marker.Length)..].Trim('/'));
            var version = ExtractVersionNumber(tag);
            return string.IsNullOrEmpty(version) || !version.All(char.IsDigit) ? null : version;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            CrashReporter.Log($"[REFrameworkService.GetLatestVersionFromRedirectAsync] Failed — {ex.Message}");
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
        AuxInstallService.SentinelRestore(dllPath);

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
            lock (SafeJsonFile.LockFor(DbPath))
            {
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
            lock (SafeJsonFile.LockFor(DbPath))
            {
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

    /// <summary>
    /// Updates the InstalledVersion on all non-PD-Upscaler records to the given version
    /// and persists to disk. Called after a version check confirms a newer tag is live
    /// so that subsequent Refreshes show the correct build number.
    /// </summary>
    public void SyncInstalledVersion(string version)
    {
        try
        {
            lock (SafeJsonFile.LockFor(DbPath))
            {
                var records = LoadRecords();
                bool changed = false;
                foreach (var r in records)
                {
                    if (!string.Equals(r.InstalledVersion, "PD-Upscaler", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(r.InstalledVersion, version, StringComparison.OrdinalIgnoreCase))
                    {
                        r.InstalledVersion = version;
                        changed = true;
                    }
                }
                if (changed)
                {
                    SaveRecords(records);
                    CrashReporter.Log($"[REFrameworkService.SyncInstalledVersion] Updated all records to {version}");
                }
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[REFrameworkService.SyncInstalledVersion] Failed — {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public int RepointRecords(string gameName, string oldPath, string newPath)
    {
        lock (SafeJsonFile.LockFor(DbPath))
        {
            var db = LoadRecords();
            int changed = 0;
            foreach (var r in db.Where(r => r.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase)
                                         && r.InstallPath.Equals(oldPath, StringComparison.OrdinalIgnoreCase)
                                         ).ToList())
            {
                bool newExists = db.Any(o => o != r && o.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase) && o.InstallPath.Equals(newPath, StringComparison.OrdinalIgnoreCase));
                if (newExists) db.Remove(r); else r.InstallPath = newPath;
                changed++;
                CrashReporter.Log($"[REFrameworkService.RepointRecords] '{gameName}': {(newExists ? "dropped old record (new folder has one)" : "moved")} '{oldPath}' → '{newPath}'");
            }
            if (changed > 0) SaveRecords(db);
            return changed;
        }
    }

    // SafeJsonFile: atomic write + .bak, corrupt-file recovery, no save over an unreadable file.
    private List<REFrameworkInstalledRecord> LoadRecords() => SafeJsonFile.ReadList<REFrameworkInstalledRecord>(DbPath);

    private void SaveRecords(List<REFrameworkInstalledRecord> records) =>
        SafeJsonFile.Write(DbPath, records, "REFrameworkService.SaveRecords", SafeJsonFile.Indented);

    private void SaveRecord(REFrameworkInstalledRecord record)
    {
        lock (SafeJsonFile.LockFor(DbPath))
        {
            var records = LoadRecords();
            var i = records.FindIndex(r =>
                r.GameName.Equals(record.GameName, StringComparison.OrdinalIgnoreCase) &&
                r.InstallPath.Equals(record.InstallPath, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) records[i] = record; else records.Add(record);
            SaveRecords(records);
        }
    }

    private void RemoveRecord(string gameName, string installPath)
    {
        lock (SafeJsonFile.LockFor(DbPath))
        {
            var records = LoadRecords();
            records.RemoveAll(r =>
                r.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase) &&
                r.InstallPath.Equals(installPath, StringComparison.OrdinalIgnoreCase));
            SaveRecords(records);
        }
    }
}
