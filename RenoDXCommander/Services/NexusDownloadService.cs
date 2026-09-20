// NexusDownloadService.cs — Nexus Mods v1 REST API integration.
// Handles API key validation, mod file listing, download link resolution, and file download.
// All download-related functionality is gated behind DevUnlockService.IsUnlocked.
// Premium users get direct CDN download links; free users get NXM key-based links.

using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Services;

public class NexusDownloadService
{
    private readonly HttpClient _http;
    private readonly SettingsViewModel _settings;

    private const string BaseUrl    = "https://api.nexusmods.com/v1";
    private const string AppName    = "RHI";
    // Read from assembly so it stays current across releases
    private static readonly string AppVersion =
        typeof(NexusDownloadService).Assembly.GetName().Version?.ToString(3) ?? "2.7.1";

    public NexusDownloadService(HttpClient http, SettingsViewModel settings)
    {
        _http = http;
        _settings = settings;
    }

    // ── Public state ──────────────────────────────────────────────────────────

    public bool IsApiKeyConfigured => !string.IsNullOrEmpty(_settings.NexusApiKey);
    public bool IsPremium => _settings.NexusIsPremium;

    // ── API key validation ────────────────────────────────────────────────────

    /// <summary>
    /// Validates an API key against the Nexus v1 users/validate endpoint.
    /// Returns user info on success, null on failure (invalid key or network error).
    /// </summary>
    public async Task<NexusUserInfo?> ValidateApiKeyAsync(string apiKey)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/users/validate.json");
            AddHeaders(request, apiKey);

            var response = await SendWithRateLimitAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                CrashReporter.Log($"[NexusDownloadService.ValidateApiKeyAsync] HTTP {(int)response.StatusCode}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<NexusValidateResponse>(json);
            if (result == null) return null;

            return new NexusUserInfo(result.UserId, result.Name ?? "", result.IsPremium);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[NexusDownloadService.ValidateApiKeyAsync] Failed — {ex.Message}");
            return null;
        }
    }

    // ── Mod files listing ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns all files for a mod. Callers should filter by CategoryName == "MAIN"
    /// and pick the highest UploadedTimestamp.
    /// </summary>
    public async Task<List<NexusModFile>> GetModFilesAsync(string domain, int modId)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"{BaseUrl}/games/{domain}/mods/{modId}/files.json");
            AddHeaders(request);

            var response = await SendWithRateLimitAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                CrashReporter.Log($"[NexusDownloadService.GetModFilesAsync] HTTP {(int)response.StatusCode} for {domain}/mods/{modId}");
                return new();
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<NexusFilesResponse>(json);
            return result?.Files ?? new();
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[NexusDownloadService.GetModFilesAsync] Failed — {ex.Message}");
            return new();
        }
    }

    /// <summary>
    /// Returns the latest MAIN file for a mod (highest UploadedTimestamp among MAIN files).
    /// Returns null if no MAIN file exists.
    /// </summary>
    public async Task<NexusModFile?> GetLatestMainFileAsync(string domain, int modId)
    {
        var files = await GetModFilesAsync(domain, modId).ConfigureAwait(false);
        return files
            .Where(f => string.Equals(f.CategoryName, "MAIN", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.UploadedTimestamp)
            .FirstOrDefault();
    }

    // ── Download link resolution ──────────────────────────────────────────────

    /// <summary>
    /// Gets a direct CDN download URI for premium users.
    /// Returns null for free users (HTTP 403) or on error.
    /// CDN links expire ~30 minutes — always generate fresh, never cache.
    /// </summary>
    public async Task<string?> GetDownloadUriAsync(string domain, int modId, int fileId)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"{BaseUrl}/games/{domain}/mods/{modId}/files/{fileId}/download_link");
            AddHeaders(request);

            var response = await SendWithRateLimitAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = "";
                try { body = await response.Content.ReadAsStringAsync().ConfigureAwait(false); } catch { }
                CrashReporter.Log($"[NexusDownloadService.GetDownloadUriAsync] HTTP {(int)response.StatusCode} — {(body.Length > 200 ? body[..200] : body)}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var links = JsonSerializer.Deserialize<List<NexusDownloadLink>>(json);
            var preferred = links?.FirstOrDefault(l =>
                    string.Equals(l.ShortName, "Nexus", StringComparison.OrdinalIgnoreCase))
                ?? links?.FirstOrDefault();
            return preferred?.Uri;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[NexusDownloadService.GetDownloadUriAsync] Failed — {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Gets a download URI using NXM key params (free user path — key/expires from nxm:// URL).
    /// Note: user_id is NOT sent — the Nexus API does not accept it on this endpoint.
    /// </summary>
    public async Task<string?> GetDownloadUriWithNxmKeyAsync(
        string domain, int modId, int fileId,
        string key, string expires, string userId)
    {
        try
        {
            // Correct endpoint: /download_link (singular, no .json suffix)
            // Correct params: key + expires only — user_id causes 404
            var url = $"{BaseUrl}/games/{domain}/mods/{modId}/files/{fileId}/download_link" +
                      $"?key={Uri.EscapeDataString(key)}&expires={Uri.EscapeDataString(expires)}";

            CrashReporter.Log($"[NexusDownloadService.GetDownloadUriWithNxmKeyAsync] GET {url}");

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddHeaders(request);

            var response = await SendWithRateLimitAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = "";
                try { body = await response.Content.ReadAsStringAsync().ConfigureAwait(false); } catch { }
                CrashReporter.Log($"[NexusDownloadService.GetDownloadUriWithNxmKeyAsync] HTTP {(int)response.StatusCode} — {(body.Length > 200 ? body[..200] : body)}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var links = JsonSerializer.Deserialize<List<NexusDownloadLink>>(json);
            var preferred = links?.FirstOrDefault(l =>
                    string.Equals(l.ShortName, "Nexus", StringComparison.OrdinalIgnoreCase))
                ?? links?.FirstOrDefault();
            return preferred?.Uri;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[NexusDownloadService.GetDownloadUriWithNxmKeyAsync] Failed — {ex.Message}");
            return null;
        }
    }

    // ── File download ─────────────────────────────────────────────────────────

    /// <summary>
    /// Downloads a file from the given URI to a temp path, reporting progress.
    /// Returns the temp file path on success, null on failure.
    /// </summary>
    public async Task<string?> DownloadToTempAsync(
        string uri,
        IProgress<(string message, double percent)>? progress = null)
    {
        try
        {
            progress?.Report(("Начинаю загрузку...", 0));

            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                CrashReporter.Log($"[NexusDownloadService.DownloadToTempAsync] HTTP {(int)response.StatusCode}");
                progress?.Report(($"Ошибка загрузки: HTTP {(int)response.StatusCode}", 0));
                return null;
            }

            var totalBytes = response.Content.Headers.ContentLength;
            var fileName = GetFileNameFromResponse(response) ?? $"nexus_download_{DateTime.Now:yyyyMMddHHmmss}.zip";
            var tempPath = Path.Combine(Path.GetTempPath(), fileName);

            // Remove stale temp file if present
            if (File.Exists(tempPath)) File.Delete(tempPath);

            using var contentStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 81920, useAsync: true);

            var buffer = new byte[81920];
            long downloaded = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer).ConfigureAwait(false)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead)).ConfigureAwait(false);
                downloaded += bytesRead;

                if (totalBytes.HasValue && totalBytes.Value > 0)
                {
                    var pct = (double)downloaded / totalBytes.Value * 100;
                    var mb = downloaded / 1_048_576.0;
                    progress?.Report(($"Downloading... {mb:F1} MB", pct));
                }
                else
                {
                    var mb = downloaded / 1_048_576.0;
                    progress?.Report(($"Downloading... {mb:F1} MB", 50));
                }
            }

            CrashReporter.Log($"[NexusDownloadService.DownloadToTempAsync] Downloaded {downloaded:N0} bytes to {tempPath}");
            progress?.Report(("Загрузка завершена", 100));
            return tempPath;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[NexusDownloadService.DownloadToTempAsync] Failed — {ex.Message}");
            progress?.Report(($"Ошибка загрузки: {ex.Message}", 0));
            return null;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void AddHeaders(HttpRequestMessage request, string? apiKeyOverride = null)
    {
        var key = apiKeyOverride ?? _settings.NexusApiKey;
        if (!string.IsNullOrEmpty(key))
            request.Headers.Add("apikey", key);
        request.Headers.Add("Application-Name", AppName);
        request.Headers.Add("Application-Version", AppVersion);
    }

    /// <summary>
    /// Sends a request with automatic 429 retry. Inspects Retry-After header and waits
    /// the indicated number of seconds (capped at 60s) before retrying once.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithRateLimitAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken = default)
    {
        var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if ((int)response.StatusCode == 429)
        {
            // Inspect Retry-After (seconds)
            int waitSeconds = 30; // default
            if (response.Headers.TryGetValues("Retry-After", out var retryValues)
                && int.TryParse(retryValues.FirstOrDefault(), out var parsedWait))
                waitSeconds = Math.Clamp(parsedWait, 1, 60);

            CrashReporter.Log($"[NexusDownloadService] Rate limited (429) — waiting {waitSeconds}s before retry");
            await Task.Delay(TimeSpan.FromSeconds(waitSeconds), cancellationToken).ConfigureAwait(false);

            // Rebuild request (HttpRequestMessage can't be sent twice)
            using var retry = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
                retry.Headers.TryAddWithoutValidation(header.Key, header.Value);
            if (request.Content != null)
                retry.Content = request.Content;

            response = await _http.SendAsync(retry, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }

    private static string? GetFileNameFromResponse(HttpResponseMessage response)
    {
        try
        {
            var disposition = response.Content.Headers.ContentDisposition;
            if (disposition?.FileName != null)
                return disposition.FileName.Trim('"');

            // Fall back to URL path
            var requestUri = response.RequestMessage?.RequestUri;
            if (requestUri != null)
            {
                var seg = requestUri.Segments.LastOrDefault();
                if (!string.IsNullOrEmpty(seg) && seg.Contains('.'))
                    return Uri.UnescapeDataString(seg);
            }
        }
        catch { }
        return null;
    }
}

// ── Models ────────────────────────────────────────────────────────────────────

public record NexusUserInfo(int UserId, string Name, bool IsPremium);

public class NexusModFile
{
    [JsonPropertyName("file_id")]
    public int FileId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("category_name")]
    public string CategoryName { get; set; } = "";

    [JsonPropertyName("is_primary")]
    public bool IsPrimary { get; set; }

    [JsonPropertyName("uploaded_timestamp")]
    public long UploadedTimestamp { get; set; }

    [JsonPropertyName("size_kb")]
    public long SizeKb { get; set; }

    [JsonPropertyName("file_name")]
    public string FileName { get; set; } = "";
}

// ── Internal response models ──────────────────────────────────────────────────

internal class NexusValidateResponse
{
    [JsonPropertyName("user_id")]
    public int UserId { get; set; }

    [JsonPropertyName("key")]
    public string? Key { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("is_premium")]
    public bool IsPremium { get; set; }

    [JsonPropertyName("is_supporter")]
    public bool IsSupporter { get; set; }
}

internal class NexusFilesResponse
{
    [JsonPropertyName("files")]
    public List<NexusModFile>? Files { get; set; }
}

internal class NexusDownloadLink
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("short_name")]
    public string? ShortName { get; set; }

    [JsonPropertyName("URI")]
    public string? Uri { get; set; }
}
