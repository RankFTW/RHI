using System.IO.Compression;
using System.Net.Http;

namespace RenoDXCommander.Services;

/// <summary>
/// Downloads the latest ReShade nightly build (with addon support) from
/// GitHub Actions via nightly.link, extracts ReShade64.dll and ReShade32.dll,
/// and stages them in the same directory as the stable ReShade staging.
/// </summary>
public class ReShadeNightlyService
{
    private const string Nightly64Url =
        "https://nightly.link/crosire/reshade/workflows/build/main/ReShade%20(64-bit).zip";
    private const string Nightly32Url =
        "https://nightly.link/crosire/reshade/workflows/build/main/ReShade%20(32-bit).zip";

    private static readonly string CacheDir = AuxInstallService.RsStagingDir;
    private static readonly string VersionFile = Path.Combine(CacheDir, "reshade_version.txt");

    private readonly HttpClient _http;

    public ReShadeNightlyService(HttpClient http)
    {
        _http = http;
    }

    /// <summary>
    /// Downloads and stages the latest nightly ReShade DLLs if they have changed.
    /// Uses Last-Modified header from nightly.link to detect changes.
    /// Returns true if new DLLs were staged.
    /// </summary>
    public async Task<bool> EnsureLatestAsync(IProgress<(string msg, double pct)>? progress = null)
    {
        CrashReporter.Log("[ReShadeNightlyService.EnsureLatestAsync] Started");
        Directory.CreateDirectory(CacheDir);

        progress?.Report(("Checking for nightly ReShade builds...", 5));

        // Use HEAD request to get Last-Modified for change detection
        var remoteDate = await GetRemoteLastModifiedAsync(Nightly64Url);
        var stagedVersion = ReShadeUpdateService.GetStagedVersion();

        // If we already have a nightly version and the remote hasn't changed, skip
        if (stagedVersion != null
            && stagedVersion.StartsWith("nightly-", StringComparison.OrdinalIgnoreCase)
            && remoteDate != null)
        {
            var stagedDate = stagedVersion.Replace("nightly-", "");
            if (string.Equals(stagedDate, remoteDate, StringComparison.OrdinalIgnoreCase)
                && File.Exists(AuxInstallService.RsStagedPath64)
                && File.Exists(AuxInstallService.RsStagedPath32)
                && new FileInfo(AuxInstallService.RsStagedPath64).Length > AuxInstallService.MinReShadeSize
                && new FileInfo(AuxInstallService.RsStagedPath32).Length > AuxInstallService.MinReShadeSize)
            {
                CrashReporter.Log($"[ReShadeNightlyService.EnsureLatestAsync] Already have nightly-{remoteDate}");
                progress?.Report(($"ReShade nightly ({remoteDate}) is current", 100));
                return false;
            }
        }

        // Download 64-bit artifact
        progress?.Report(("Downloading ReShade nightly (64-bit)...", 15));
        if (!await DownloadAndExtractAsync(Nightly64Url, AuxInstallService.RsStagedPath64, "ReShade64.dll"))
        {
            CrashReporter.Log("[ReShadeNightlyService.EnsureLatestAsync] Failed to download 64-bit artifact");
            return false;
        }

        // Download 32-bit artifact
        progress?.Report(("Downloading ReShade nightly (32-bit)...", 55));
        if (!await DownloadAndExtractAsync(Nightly32Url, AuxInstallService.RsStagedPath32, "ReShade32.dll"))
        {
            CrashReporter.Log("[ReShadeNightlyService.EnsureLatestAsync] Failed to download 32-bit artifact");
            return false;
        }

        // Write version marker using the remote date
        var versionTag = $"nightly-{remoteDate ?? DateTime.UtcNow.ToString("yyyy-MM-dd-HHmm")}";
        try { File.WriteAllText(VersionFile, versionTag); }
        catch (Exception ex) { CrashReporter.Log($"[ReShadeNightlyService.EnsureLatestAsync] Version file write failed — {ex.Message}"); }

        // Clean up any old stable installer exes (they're from the other channel)
        try
        {
            foreach (var old in Directory.GetFiles(CacheDir, "ReShade_Setup_*.exe"))
                try { File.Delete(old); } catch { }
        }
        catch { }

        progress?.Report(($"ReShade nightly ({versionTag}) ready!", 100));
        CrashReporter.Log($"[ReShadeNightlyService.EnsureLatestAsync] Staged {versionTag} successfully");
        return true;
    }

    /// <summary>
    /// Checks if a newer nightly build is available by comparing Last-Modified
    /// against the staged version marker.
    /// </summary>
    public async Task<bool> CheckForUpdateAsync()
    {
        try
        {
            var remoteDate = await GetRemoteLastModifiedAsync(Nightly64Url);
            if (remoteDate == null) return false;

            var stagedVersion = ReShadeUpdateService.GetStagedVersion();
            if (stagedVersion == null) return true; // nothing staged yet

            if (!stagedVersion.StartsWith("nightly-", StringComparison.OrdinalIgnoreCase))
                return true; // switching from stable to nightly

            var stagedDate = stagedVersion.Replace("nightly-", "");
            return !string.Equals(stagedDate, remoteDate, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[ReShadeNightlyService.CheckForUpdateAsync] Failed — {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Downloads a nightly.link .zip artifact and extracts the target DLL from it.
    /// </summary>
    private async Task<bool> DownloadAndExtractAsync(string url, string destPath, string dllName)
    {
        try
        {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var tempZip = destPath + ".zip.tmp";
            try
            {
                using (var netStream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = File.Create(tempZip))
                    await netStream.CopyToAsync(fileStream);

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
                    return false;
                }

                entry.ExtractToFile(destPath, overwrite: true);

                // Validate size
                if (new FileInfo(destPath).Length < AuxInstallService.MinReShadeSize)
                {
                    CrashReporter.Log($"[ReShadeNightlyService] Extracted '{dllName}' is too small ({new FileInfo(destPath).Length} bytes)");
                    return false;
                }

                CrashReporter.Log($"[ReShadeNightlyService] Extracted '{dllName}' ({new FileInfo(destPath).Length} bytes)");
                return true;
            }
            finally
            {
                try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[ReShadeNightlyService.DownloadAndExtractAsync] {dllName} — {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Gets the Last-Modified date string from a HEAD request to nightly.link.
    /// Returns a date string like "2026-05-03" or null on failure.
    /// </summary>
    private async Task<string?> GetRemoteLastModifiedAsync(string url)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode) return null;

            if (response.Content.Headers.LastModified.HasValue)
                return response.Content.Headers.LastModified.Value.UtcDateTime.ToString("yyyy-MM-dd-HHmm");

            // Fallback: use current date
            return DateTime.UtcNow.ToString("yyyy-MM-dd-HHmm");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[ReShadeNightlyService.GetRemoteLastModifiedAsync] {ex.Message}");
            return null;
        }
    }
}
