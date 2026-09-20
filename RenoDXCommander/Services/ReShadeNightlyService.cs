using System.IO.Compression;

namespace RenoDXCommander.Services;

/// <summary>
/// Downloads the latest ReShade nightly build (with addon support) from
/// GitHub Actions via nightly.link, extracts ReShade64.dll and ReShade32.dll,
/// and stages them in a dedicated nightly staging directory.
/// </summary>
public class ReShadeNightlyService
{
    private const string Nightly64Url =
        "https://nightly.link/crosire/reshade/workflows/build/main/ReShade%20(64-bit).zip";
    private const string Nightly32Url =
        "https://nightly.link/crosire/reshade/workflows/build/main/ReShade%20(32-bit).zip";

    private static readonly string CacheDir = AuxInstallService.RsNightlyStagingDir;
    private static readonly string VersionFile = Path.Combine(CacheDir, "reshade_version.txt");

    private readonly HttpClient _http;

    public ReShadeNightlyService(HttpClient http)
    {
        _http = http;
    }

    /// <summary>
    /// Downloads and stages the latest nightly ReShade DLLs if they have changed.
    /// Uses Content-Length comparison to detect changes — nightly.link's Last-Modified
    /// header is unreliable for change detection.
    /// Returns true if new DLLs were staged.
    /// </summary>
    public async Task<bool> EnsureLatestAsync(IProgress<(string msg, double pct)>? progress = null)
    {
        CrashReporter.Log("[ReShadeNightlyService.EnsureLatestAsync] Started");
        Directory.CreateDirectory(CacheDir);

        progress?.Report(("Проверка ночных сборок ReShade...", 5));

        // Get the remote Content-Length to compare against local staged DLL size.
        // If the remote zip size differs from what we last downloaded, there's a new build.
        var remote64Size = await GetRemoteContentLengthAsync(Nightly64Url);
        var lastKnownSize = ReadLastKnownZipSize();

        // If we have valid staged DLLs and the remote zip size hasn't changed, skip
        if (remote64Size > 0 && remote64Size == lastKnownSize
            && File.Exists(AuxInstallService.RsNightlyStagedPath64)
            && File.Exists(AuxInstallService.RsNightlyStagedPath32)
            && new FileInfo(AuxInstallService.RsNightlyStagedPath64).Length > AuxInstallService.MinReShadeSize
            && new FileInfo(AuxInstallService.RsNightlyStagedPath32).Length > AuxInstallService.MinReShadeSize)
        {
            CrashReporter.Log($"[ReShadeNightlyService.EnsureLatestAsync] Remote zip size unchanged ({remote64Size} bytes) — skipping");
            progress?.Report(("ReShade nightly актуален", 100));
            return false;
        }

        // Download 64-bit artifact
        progress?.Report(("Загрузка ReShade nightly (64 бита)...", 15));
        var (success64, downloadedZipSize) = await DownloadAndExtractAsync(Nightly64Url, AuxInstallService.RsNightlyStagedPath64, "ReShade64.dll");
        if (!success64)
        {
            CrashReporter.Log("[ReShadeNightlyService.EnsureLatestAsync] Failed to download 64-bit artifact");
            return false;
        }

        // Download 32-bit artifact
        progress?.Report(("Загрузка ReShade nightly (32 бита)...", 55));
        var (success32, _) = await DownloadAndExtractAsync(Nightly32Url, AuxInstallService.RsNightlyStagedPath32, "ReShade32.dll");
        if (!success32)
        {
            CrashReporter.Log("[ReShadeNightlyService.EnsureLatestAsync] Failed to download 32-bit artifact");
            return false;
        }

        // Write version marker using current UTC timestamp
        var versionTag = $"nightly-{DateTime.UtcNow:yyyy-MM-dd-HHmm}";
        try { File.WriteAllText(VersionFile, versionTag); }
        catch (Exception ex) { CrashReporter.Log($"[ReShadeNightlyService.EnsureLatestAsync] Version file write failed — {ex.Message}"); }

        // Persist the zip size for future comparison
        WriteLastKnownZipSize(downloadedZipSize);

        // Clean up any old stable installer exes (they're from the other channel)
        try
        {
            foreach (var old in Directory.GetFiles(CacheDir, "ReShade_Setup_*.exe"))
                try { File.Delete(old); } catch { }
        }
        catch { }

        progress?.Report(($"ReShade nightly ({versionTag}) готов!", 100));
        CrashReporter.Log($"[ReShadeNightlyService.EnsureLatestAsync] Staged {versionTag} successfully (zip size={downloadedZipSize})");
        return true;
    }

    /// <summary>
    /// Gets the currently staged nightly ReShade version, or null if not yet downloaded.
    /// </summary>
    public static string? GetStagedVersion()
    {
        try
        {
            return File.Exists(VersionFile) ? File.ReadAllText(VersionFile).Trim() : null;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[ReShadeNightlyService.GetStagedVersion] Failed to read version file — {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Checks if a newer nightly build is available by comparing remote Content-Length
    /// against the last known zip size.
    /// </summary>
    public async Task<bool> CheckForUpdateAsync()
    {
        try
        {
            var remote64Size = await GetRemoteContentLengthAsync(Nightly64Url);
            if (remote64Size <= 0) return false;

            var lastKnownSize = ReadLastKnownZipSize();
            if (lastKnownSize <= 0) return true; // nothing staged yet

            return remote64Size != lastKnownSize;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[ReShadeNightlyService.CheckForUpdateAsync] Failed — {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Downloads a nightly.link .zip artifact and extracts the target DLL from it.
    /// Returns (success, zipFileSize).
    /// </summary>
    private async Task<(bool success, long zipSize)> DownloadAndExtractAsync(string url, string destPath, string dllName)
    {
        try
        {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var tempZip = destPath + ".zip.tmp";
            long zipSize = 0;
            try
            {
                using (var netStream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = File.Create(tempZip))
                    await netStream.CopyToAsync(fileStream);

                zipSize = new FileInfo(tempZip).Length;

                // Extract the DLL from the zip
                using var archive = ZipFile.OpenRead(tempZip);
                var entry = archive.Entries.FirstOrDefault(e =>
                    e.Name.Equals(dllName, StringComparison.OrdinalIgnoreCase));

                if (entry == null)
                {
                    // Some artifacts nest the DLL — search all entries
                    entry = archive.Entries.FirstOrDefault(e =>
                        e.FullName.EndsWith(dllName, StringComparison.OrdinalIgnoreCase));
                }

                if (entry == null)
                {
                    CrashReporter.Log($"[ReShadeNightlyService] '{dllName}' not found in zip. Entries: [{string.Join(", ", archive.Entries.Select(e => e.FullName))}]");
                    return (false, 0);
                }

                entry.ExtractToFile(destPath, overwrite: true);

                // Validate size
                if (new FileInfo(destPath).Length < AuxInstallService.MinReShadeSize)
                {
                    CrashReporter.Log($"[ReShadeNightlyService] Extracted '{dllName}' is too small ({new FileInfo(destPath).Length} bytes)");
                    return (false, 0);
                }

                CrashReporter.Log($"[ReShadeNightlyService] Extracted '{dllName}' ({new FileInfo(destPath).Length} bytes)");
                return (true, zipSize);
            }
            finally
            {
                try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[ReShadeNightlyService.DownloadAndExtractAsync] {dllName} — {ex.Message}");
            return (false, 0);
        }
    }

    /// <summary>
    /// Gets the Content-Length of the remote zip via a HEAD request.
    /// Returns the size in bytes, or -1 on failure.
    /// </summary>
    private async Task<long> GetRemoteContentLengthAsync(string url)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                CrashReporter.Log($"[ReShadeNightlyService.GetRemoteContentLengthAsync] HEAD returned {response.StatusCode}");
                return -1;
            }

            var length = response.Content.Headers.ContentLength ?? -1;
            CrashReporter.Log($"[ReShadeNightlyService.GetRemoteContentLengthAsync] Content-Length={length}");
            return length;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[ReShadeNightlyService.GetRemoteContentLengthAsync] {ex.Message}");
            return -1;
        }
    }

    // ── Zip size persistence ──────────────────────────────────────────────────

    private static readonly string ZipSizeFile = Path.Combine(CacheDir, "last_zip_size.txt");

    private static long ReadLastKnownZipSize()
    {
        try
        {
            if (File.Exists(ZipSizeFile) && long.TryParse(File.ReadAllText(ZipSizeFile).Trim(), out var size))
                return size;
        }
        catch { }
        return -1;
    }

    private static void WriteLastKnownZipSize(long size)
    {
        try { File.WriteAllText(ZipSizeFile, size.ToString()); }
        catch (Exception ex) { CrashReporter.Log($"[ReShadeNightlyService.WriteLastKnownZipSize] Failed — {ex.Message}"); }
    }
}
