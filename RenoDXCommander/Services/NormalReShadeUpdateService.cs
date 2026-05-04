using System.Text.RegularExpressions;

namespace RenoDXCommander.Services;

/// <summary>
/// Downloads the latest ReShade without addon support from reshade.me,
/// extracts ReShade64.dll and ReShade32.dll, and stages them in
/// %LocalAppData%\RHI\reshade-normal\.
/// </summary>
public class NormalReShadeUpdateService : INormalReShadeUpdateService
{
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "reshade-normal");

    private static readonly string VersionFile = Path.Combine(CacheDir, "reshade_version.txt");

    private const string ReShadeMeUrl = "https://reshade.me";
    // Pattern: /downloads/ReShade_Setup_X.Y.Z.exe (no _Addon suffix)
    private static readonly Regex DownloadLinkRegex = new(
        @"/downloads/ReShade_Setup_([\d.]+)\.exe",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly HttpClient _http;
    private readonly ISevenZipExtractor _extractor;

    public NormalReShadeUpdateService(HttpClient http, ISevenZipExtractor extractor)
    {
        _http = http;
        _extractor = extractor;
    }

    /// <summary>Gets the currently staged Normal ReShade version, or null if not yet downloaded.</summary>
    public static string? GetStagedVersion()
    {
        try
        {
            return File.Exists(VersionFile) ? File.ReadAllText(VersionFile).Trim() : null;
        }
        catch (Exception ex) { CrashReporter.Log($"[NormalReShadeUpdateService.GetStagedVersion] Failed to read version file — {ex.Message}"); return null; }
    }

    /// <summary>
    /// Checks reshade.me for the latest version without addon support.
    /// </summary>
    public async Task<(string version, string url)?> CheckLatestVersionAsync()
    {
        try
        {
            CrashReporter.Log($"[NormalReShadeUpdateService.CheckLatestVersionAsync] Fetching {ReShadeMeUrl}...");
            var html = await _http.GetStringAsync(ReShadeMeUrl);
            CrashReporter.Log($"[NormalReShadeUpdateService.CheckLatestVersionAsync] Page fetched, {html.Length} chars");

            // Find all matches and pick the one that does NOT have _Addon suffix
            foreach (Match match in DownloadLinkRegex.Matches(html))
            {
                // Skip if the full match path contains _Addon (our regex shouldn't match it,
                // but guard against partial overlaps)
                var fullPath = match.Value;
                if (fullPath.Contains("_Addon", StringComparison.OrdinalIgnoreCase))
                    continue;

                var version = match.Groups[1].Value;
                var url = $"{ReShadeMeUrl}{fullPath}";
                CrashReporter.Log($"[NormalReShadeUpdateService.CheckLatestVersionAsync] Found v{version} at {url}");
                return (version, url);
            }

            // No match found
            var snippet = html.Length > 500 ? html[..500] : html;
            CrashReporter.Log($"[NormalReShadeUpdateService.CheckLatestVersionAsync] Normal download link not found on page. Snippet: {snippet}");
            return null;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[NormalReShadeUpdateService.CheckLatestVersionAsync] Version check failed — {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Downloads and extracts the latest Normal ReShade if needed.
    /// Returns true if new DLLs were staged (i.e. an update occurred).
    /// </summary>
    public async Task<bool> EnsureLatestAsync(IProgress<(string msg, double pct)>? progress = null)
    {
        CrashReporter.Log("[NormalReShadeUpdateService.EnsureLatestAsync] Started");
        Directory.CreateDirectory(CacheDir);

        progress?.Report(("Checking for Normal ReShade updates...", 5));
        var latest = await CheckLatestVersionAsync();
        if (latest == null)
        {
            CrashReporter.Log("[NormalReShadeUpdateService.EnsureLatestAsync] Could not determine latest version");
            return false;
        }

        var (version, url) = latest.Value;
        var stagedVersion = GetStagedVersion();

        // Check if we already have this version AND the DLLs exist
        var stagedPath64 = Path.Combine(CacheDir, AuxInstallService.RsStaged64);
        var stagedPath32 = Path.Combine(CacheDir, AuxInstallService.RsStaged32);
        if (stagedVersion == version
            && File.Exists(stagedPath64)
            && File.Exists(stagedPath32)
            && new FileInfo(stagedPath64).Length > 1_000_000
            && new FileInfo(stagedPath32).Length > 1_000_000)
        {
            CrashReporter.Log($"[NormalReShadeUpdateService.EnsureLatestAsync] Already have v{version}");
            progress?.Report(($"Normal ReShade {version} is current", 100));
            return false;
        }

        // Download the installer exe
        var exeName = $"ReShade_Setup_{version}.exe";
        var exePath = Path.Combine(CacheDir, exeName);

        if (!File.Exists(exePath))
        {
            progress?.Report(($"Downloading Normal ReShade {version}...", 15));
            CrashReporter.Log($"[NormalReShadeUpdateService.EnsureLatestAsync] Downloading {url}");

            try
            {
                // reshade.me requires a Referer header for downloads
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Referrer = new Uri(ReShadeMeUrl + "/");

                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
                CrashReporter.Log($"[NormalReShadeUpdateService.EnsureLatestAsync] Response Content-Type={contentType}, Content-Length={response.Content.Headers.ContentLength}");

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                var tempPath = exePath + ".tmp";

                using (var netStream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = File.Create(tempPath))
                {
                    var buffer = new byte[1024 * 1024]; // 1 MB
                    long downloaded = 0;
                    int read;
                    while ((read = await netStream.ReadAsync(buffer)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, read));
                        downloaded += read;
                        if (totalBytes > 0)
                        {
                            var pct = 15.0 + (downloaded / (double)totalBytes * 60.0);
                            progress?.Report(($"Downloading Normal ReShade {version}... {downloaded / 1024}KB", pct));
                        }
                    }
                }

                // Validate it's actually a PE file (starts with MZ)
                var header = new byte[2];
                using (var fs = File.OpenRead(tempPath))
                    fs.Read(header, 0, 2);
                if (header[0] != 0x4D || header[1] != 0x5A) // "MZ"
                {
                    var snippet = File.ReadAllText(tempPath)[..Math.Min(200, (int)new FileInfo(tempPath).Length)];
                    CrashReporter.Log($"[NormalReShadeUpdateService.EnsureLatestAsync] Downloaded file is NOT a valid PE exe. First bytes: {header[0]:X2} {header[1]:X2}. Snippet: {snippet}");
                    try { File.Delete(tempPath); } catch (Exception cleanupEx) { CrashReporter.Log($"[NormalReShadeUpdateService.EnsureLatestAsync] Failed to clean up invalid temp file — {cleanupEx.Message}"); }
                    return false;
                }

                CrashReporter.Log($"[NormalReShadeUpdateService.EnsureLatestAsync] Download complete, {new FileInfo(tempPath).Length} bytes, valid PE header");

                if (File.Exists(exePath)) File.Delete(exePath);
                File.Move(tempPath, exePath);
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[NormalReShadeUpdateService.EnsureLatestAsync] Download failed — {ex.Message}");
                // Clean up partial download
                var tempPath = exePath + ".tmp";
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch (Exception cleanupEx) { CrashReporter.Log($"[NormalReShadeUpdateService.EnsureLatestAsync] Failed to clean up partial download — {cleanupEx.Message}"); }
                return false;
            }
        }
        else
        {
            // Validate cached exe is actually a PE file, not an HTML error page
            try
            {
                var header = new byte[2];
                using (var fs = File.OpenRead(exePath))
                    fs.Read(header, 0, 2);
                if (header[0] != 0x4D || header[1] != 0x5A) // "MZ"
                {
                    CrashReporter.Log($"[NormalReShadeUpdateService.EnsureLatestAsync] Cached exe is NOT a valid PE (bytes: {header[0]:X2} {header[1]:X2}), deleting and re-downloading");
                    File.Delete(exePath);
                    // Recurse to re-download
                    return await EnsureLatestAsync(progress);
                }
            }
            catch (Exception ex) { CrashReporter.Log($"[NormalReShadeUpdateService.EnsureLatestAsync] Cached exe validation failed — {ex.Message}"); }
            CrashReporter.Log($"[NormalReShadeUpdateService.EnsureLatestAsync] Exe already cached at {exePath}");
        }

        // Validate the downloaded exe is reasonably sized
        var exeSize = new FileInfo(exePath).Length;
        CrashReporter.Log($"[NormalReShadeUpdateService.EnsureLatestAsync] Exe size = {exeSize} bytes at {exePath}");
        if (exeSize < 500_000) // ReShade exe should be several MB
        {
            CrashReporter.Log($"[NormalReShadeUpdateService.EnsureLatestAsync] Downloaded exe too small ({exeSize} bytes), deleting");
            try { File.Delete(exePath); } catch (Exception cleanupEx) { CrashReporter.Log($"[NormalReShadeUpdateService.EnsureLatestAsync] Failed to delete undersized exe — {cleanupEx.Message}"); }
            return false;
        }

        // Extract DLLs from the exe (it has an appended ZIP archive)
        progress?.Report(("Extracting Normal ReShade DLLs...", 80));
        try
        {
            _extractor.ExtractFile(exePath, "ReShade64.dll", stagedPath64);
            _extractor.ExtractFile(exePath, "ReShade32.dll", stagedPath32);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[NormalReShadeUpdateService.EnsureLatestAsync] Extraction failed — {ex.Message}");
            // Log what's inside the archive for diagnostics
            try
            {
                var entries = _extractor.ListEntries(exePath);
                CrashReporter.Log($"[NormalReShadeUpdateService.EnsureLatestAsync] Archive entries: [{string.Join(", ", entries)}]");
            }
            catch (Exception listEx) { CrashReporter.Log($"[NormalReShadeUpdateService.EnsureLatestAsync] Failed to list archive entries — {listEx.Message}"); }
            return false;
        }

        // Write version marker
        try { File.WriteAllText(VersionFile, version); }
        catch (Exception ex) { CrashReporter.Log($"[NormalReShadeUpdateService.EnsureLatestAsync] Version file write failed — {ex.Message}"); }

        // Clean up old installer exes (keep only current)
        // Match ReShade_Setup_*.exe but exclude *_Addon.exe files
        try
        {
            foreach (var old in Directory.GetFiles(CacheDir, "ReShade_Setup_*.exe"))
            {
                if (old.Equals(exePath, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (old.Contains("_Addon", StringComparison.OrdinalIgnoreCase))
                    continue; // don't touch addon installer files
                try { File.Delete(old); } catch (Exception cleanupEx) { CrashReporter.Log($"[NormalReShadeUpdateService.EnsureLatestAsync] Failed to delete old installer '{old}' — {cleanupEx.Message}"); }
            }
        }
        catch (Exception ex) { CrashReporter.Log($"[NormalReShadeUpdateService.EnsureLatestAsync] Failed to clean up old installers — {ex.Message}"); }

        progress?.Report(($"Normal ReShade {version} ready!", 100));
        CrashReporter.Log($"[NormalReShadeUpdateService.EnsureLatestAsync] Staged v{version} successfully");
        return true;
    }
}
