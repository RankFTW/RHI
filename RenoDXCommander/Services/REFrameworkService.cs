using System.IO.Compression;
using System.Text.Json;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Installs and manages RE Framework (dinput8.dll) for RE Engine games.
/// Downloads MHWILDS.zip from GitHub nightly releases, extracts dinput8.dll,
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
    /// Each RE Engine game has a game-specific build of RE Framework.
    /// Keys are compared case-insensitively via ResolveZipName.
    /// </summary>
    private static readonly Dictionary<string, string> GameZipMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Devil May Cry 5"]                     = "DMC5.zip",
        ["Dragon's Dogma 2"]                    = "DD2.zip",
        ["Monster Hunter Rise"]                 = "MHRISE.zip",
        ["Monster Hunter Rise: Sunbreak"]       = "MHRISE.zip",
        ["Monster Hunter Wilds"]                = "MHWILDS.zip",
        ["Pragmata"]                            = "PRAGMATA.zip",
        ["Resident Evil 2"]                     = "RE2.zip",
        ["Resident Evil 3"]                     = "RE3.zip",
        ["Resident Evil 4"]                     = "RE4.zip",
        ["Resident Evil Village"]               = "RE8.zip",
        ["Resident Evil 7"]                     = "RE7.zip",
        ["Resident Evil 7 Biohazard"]           = "RE7.zip",
        ["RESIDENT EVIL 7 biohazard"]           = "RE7.zip",
        ["Resident Evil 9"]                     = "RE9.zip",
        ["Resident Evil Requiem"]                = "RE9.zip",
        ["Resident Evil Requiem"]                = "RE9.zip",
        ["Street Fighter 6"]                    = "SF6.zip",
        ["Street Fighter™ 6"]                   = "SF6.zip",
        ["Monster Hunter Stories 3"]            = "MHSTORIES3.zip",
    };

    // ── Paths ─────────────────────────────────────────────────────────────────────

    private static readonly string CacheDir = Path.Combine(DownloadPaths.Root, "reframework");

    private static readonly string DbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "reframework_installed.json");

    // ── State ─────────────────────────────────────────────────────────────────────

    private readonly HttpClient _http;

    /// <summary>Session-level cache for the latest release tag to avoid repeated API calls.</summary>
    private string? _cachedLatestVersion;

    public REFrameworkService(HttpClient http)
    {
        _http = http;
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
    /// Resolves the game-specific RE Framework ZIP filename from the game name.
    /// Falls back to MHWILDS.zip for unknown games (most common RE Engine game).
    /// </summary>
    private static string ResolveZipName(string gameName)
    {
        if (GameZipMap.TryGetValue(gameName, out var zip))
            return zip;

        // Strip ™®© and retry exact match
        var stripped = gameName.Replace("™", "").Replace("®", "").Replace("©", "").Trim();
        if (stripped != gameName && GameZipMap.TryGetValue(stripped, out zip))
            return zip;

        // Try partial matching for common variations (e.g. "RESIDENT EVIL 2" vs "Resident Evil 2")
        foreach (var kvp in GameZipMap)
        {
            if (stripped.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                return kvp.Value;
        }

        CrashReporter.Log($"[REFrameworkService.ResolveZipName] No mapping for '{gameName}', falling back to MHWILDS.zip");
        return "MHWILDS.zip";
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
            var json = await _http.GetStringAsync(ReleasesApiUrl);
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
