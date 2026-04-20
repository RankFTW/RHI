using System.Diagnostics;
using System.Net;
using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Services;

/// <summary>
/// Downloads mod files from Nexus Mods CDN for premium users or opens the browser
/// for free users. Handles nxm:// URI processing for the protocol handler flow.
/// </summary>
public class NexusDownloadService : INexusDownloadService
{
    private readonly INexusApiClient _apiClient;
    private readonly INexusAuthService _authService;
    private readonly HttpClient _httpClient;

    /// <summary>Subfolder under the RHI downloads root for Nexus-sourced files.</summary>
    private static readonly string NexusDownloadsFolder = Path.Combine(DownloadPaths.Root, "nexus");

    public NexusDownloadService(INexusApiClient apiClient, INexusAuthService authService, HttpClient httpClient)
    {
        _apiClient = apiClient;
        _authService = authService;
        _httpClient = httpClient;
    }

    /// <inheritdoc />
    public async Task DownloadAndInstallAsync(
        GameCardViewModel card, string gameDomain, int modId,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (_authService.IsPremium)
        {
            try
            {
                await PremiumDownloadFlowAsync(card, gameDomain, modId, progress, ct);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                // 403 means user is not actually premium for this download — fall back
                CrashReporter.Log($"[NexusDownloadService.DownloadAndInstallAsync] 403 on download link for {gameDomain}/{modId}, falling back to free flow");
                FreeUserFlow(gameDomain, modId);
            }
        }
        else
        {
            FreeUserFlow(gameDomain, modId);
        }
    }

    /// <inheritdoc />
    public async Task<string?> ProcessNxmUriAsync(NxmUri uri)
    {
        // Check expiry
        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= uri.Expires)
        {
            CrashReporter.Log($"[NexusDownloadService.ProcessNxmUriAsync] nxm link expired for {uri.GameDomain}/mods/{uri.ModId}/files/{uri.FileId}");
            // Link has expired — caller should notify user
            return null;
        }

        try
        {
            var links = await _apiClient.GetDownloadLinksAsync(
                uri.GameDomain, uri.ModId, uri.FileId,
                nxmKey: uri.Key, expires: uri.Expires);

            if (links.Count == 0)
            {
                CrashReporter.Log($"[NexusDownloadService.ProcessNxmUriAsync] No download links returned for {uri.GameDomain}/{uri.ModId}/{uri.FileId}");
                return null;
            }

            var downloadUrl = links[0].Uri;
            var fileName = $"{uri.GameDomain}_{uri.ModId}_{uri.FileId}{GetExtensionFromUrl(downloadUrl)}";
            var destPath = Path.Combine(NexusDownloadsFolder, fileName);

            Directory.CreateDirectory(NexusDownloadsFolder);

            await DownloadFileAsync(downloadUrl, destPath, progress: null, ct: CancellationToken.None);

            CrashReporter.Log($"[NexusDownloadService.ProcessNxmUriAsync] Downloaded {fileName} via nxm:// protocol");
            return destPath;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[NexusDownloadService.ProcessNxmUriAsync] Failed to process nxm URI — {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Premium download flow: resolve latest main file, get CDN links, download, and install.
    /// </summary>
    private async Task PremiumDownloadFlowAsync(
        GameCardViewModel card, string gameDomain, int modId,
        IProgress<double>? progress, CancellationToken ct)
    {
        CrashReporter.Log($"[NexusDownloadService.PremiumDownloadFlowAsync] Starting premium download for {gameDomain}/{modId}");

        // Get file list to find the latest main file
        var files = await _apiClient.GetModFilesV1Async(gameDomain, modId);
        var mainFile = files
            .Where(f => f.Category.Equals("MAIN", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.UploadedAt)
            .FirstOrDefault();

        if (mainFile == null)
        {
            // Fall back to the most recent file of any category
            mainFile = files.OrderByDescending(f => f.UploadedAt).FirstOrDefault();
        }

        if (mainFile == null)
        {
            CrashReporter.Log($"[NexusDownloadService.PremiumDownloadFlowAsync] No files found for {gameDomain}/{modId}");
            return;
        }

        // Get CDN download links
        var links = await _apiClient.GetDownloadLinksAsync(gameDomain, modId, mainFile.FileId);

        if (links.Count == 0)
        {
            CrashReporter.Log($"[NexusDownloadService.PremiumDownloadFlowAsync] No download links returned for file {mainFile.FileId}");
            return;
        }

        var downloadUrl = links[0].Uri;
        var fileName = !string.IsNullOrEmpty(mainFile.FileName)
            ? mainFile.FileName
            : $"{gameDomain}_{modId}_{mainFile.FileId}{GetExtensionFromUrl(downloadUrl)}";
        var destPath = Path.Combine(NexusDownloadsFolder, fileName);

        Directory.CreateDirectory(NexusDownloadsFolder);

        await DownloadFileAsync(downloadUrl, destPath, progress, ct);

        CrashReporter.Log($"[NexusDownloadService.PremiumDownloadFlowAsync] Download complete: {fileName}");

        // TODO: Invoke existing mod install flow once wiring is complete.
        // The downloaded file is available at destPath for the install pipeline.
    }

    /// <summary>
    /// Free user flow: open the mod's files page in the default browser.
    /// The user clicks "Download with Manager" on the Nexus website, which triggers
    /// the nxm:// protocol handler.
    /// </summary>
    private static void FreeUserFlow(string gameDomain, int modId)
    {
        var url = $"https://www.nexusmods.com/{gameDomain}/mods/{modId}?tab=files";
        CrashReporter.Log($"[NexusDownloadService.FreeUserFlow] Opening browser for free user: {url}");

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[NexusDownloadService.FreeUserFlow] Failed to open browser — {ex.Message}");
        }
    }

    /// <summary>
    /// Downloads a file from the given URL to the destination path, reporting progress.
    /// </summary>
    private async Task DownloadFileAsync(
        string url, string destPath,
        IProgress<double>? progress, CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        long downloadedBytes = 0;

        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true);

        var buffer = new byte[81920];
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            downloadedBytes += bytesRead;

            if (totalBytes > 0)
            {
                var progressValue = (double)downloadedBytes / totalBytes;
                progress?.Report(progressValue);
            }
        }

        progress?.Report(1.0);
    }

    /// <summary>
    /// Extracts a file extension from a download URL, defaulting to .zip if none found.
    /// </summary>
    private static string GetExtensionFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var ext = Path.GetExtension(uri.LocalPath);
            return string.IsNullOrEmpty(ext) ? ".zip" : ext;
        }
        catch
        {
            return ".zip";
        }
    }
}
