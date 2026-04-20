using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// HTTP client for Nexus Mods V1 REST and V2 GraphQL APIs.
/// Tracks rate limits via response headers and exposes remaining daily quota.
/// </summary>
public class NexusApiClient : INexusApiClient
{
    private const string V1BaseUrl = "https://api.nexusmods.com/v1/";
    private const string V2GraphQLUrl = "https://api.nexusmods.com/v2/graphql";
    private const string RateLimitHeader = "X-RL-Daily-Remaining";
    private const int LowRemainingThreshold = 50;

    private readonly HttpClient _http;
    private readonly string _version;

    private int _dailyRequestsRemaining = int.MaxValue;
    private bool _isRateLimited;
    private string? _apiKey;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Creates a new NexusApiClient.
    /// </summary>
    /// <param name="http">HttpClient instance (injected for DI/testing).</param>
    /// <param name="version">Application version string for the Application-Version header.</param>
    public NexusApiClient(HttpClient http, string version)
    {
        _http = http;
        _version = version;

        // Set default headers that go on every request
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Application-Name", "RHI");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Application-Version", version);
    }

    /// <inheritdoc />
    public int DailyRequestsRemaining => _dailyRequestsRemaining;

    /// <inheritdoc />
    public bool IsRateLimited => _isRateLimited;

    /// <summary>
    /// Sets the API key used for authenticating V1 REST and V2 GraphQL requests.
    /// Called by NexusAuthService after successful validation.
    /// </summary>
    public void SetApiKey(string? apiKey) => _apiKey = apiKey;

    /// <inheritdoc />
    public async Task<NexusValidationResponse?> ValidateKeyAsync(string apiKey)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{V1BaseUrl}users/validate.json");
            request.Headers.TryAddWithoutValidation("apikey", apiKey);

            using var response = await _http.SendAsync(request).ConfigureAwait(false);
            TrackRateLimit(response);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _isRateLimited = true;
                CrashReporter.Log("[NexusApiClient.ValidateKeyAsync] Rate limited (HTTP 429)");
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                CrashReporter.Log($"[NexusApiClient.ValidateKeyAsync] Failed with status {(int)response.StatusCode}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonSerializer.Deserialize<NexusValidationResponse>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[NexusApiClient.ValidateKeyAsync] Exception — {ex.Message}");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<List<NexusFileInfo>> GetModFilesGraphQLAsync(string gameDomain, int modId)
    {
        try
        {
            var query = BuildModFilesGraphQLQuery(gameDomain, modId);
            using var request = new HttpRequestMessage(HttpMethod.Post, V2GraphQLUrl);
            request.Content = new StringContent(query, Encoding.UTF8, "application/json");
            if (!string.IsNullOrEmpty(_apiKey))
                request.Headers.TryAddWithoutValidation("apikey", _apiKey);

            using var response = await _http.SendAsync(request).ConfigureAwait(false);
            TrackRateLimit(response);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _isRateLimited = true;
                CrashReporter.Log("[NexusApiClient.GetModFilesGraphQLAsync] Rate limited (HTTP 429)");
                return [];
            }

            if (!response.IsSuccessStatusCode)
            {
                CrashReporter.Log($"[NexusApiClient.GetModFilesGraphQLAsync] Failed with status {(int)response.StatusCode}");
                return [];
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return ParseGraphQLModFilesResponse(json);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[NexusApiClient.GetModFilesGraphQLAsync] Exception — {ex.Message}");
            return [];
        }
    }

    /// <inheritdoc />
    public async Task<List<NexusFileInfo>> GetModFilesV1Async(string gameDomain, int modId)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"{V1BaseUrl}games/{gameDomain}/mods/{modId}/files.json");
            if (!string.IsNullOrEmpty(_apiKey))
                request.Headers.TryAddWithoutValidation("apikey", _apiKey);

            using var response = await _http.SendAsync(request).ConfigureAwait(false);
            TrackRateLimit(response);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _isRateLimited = true;
                CrashReporter.Log("[NexusApiClient.GetModFilesV1Async] Rate limited (HTTP 429)");
                return [];
            }

            if (!response.IsSuccessStatusCode)
            {
                CrashReporter.Log($"[NexusApiClient.GetModFilesV1Async] Failed with status {(int)response.StatusCode}");
                return [];
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return ParseV1ModFilesResponse(json);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[NexusApiClient.GetModFilesV1Async] Exception — {ex.Message}");
            return [];
        }
    }

    /// <inheritdoc />
    public async Task<List<NexusDownloadLink>> GetDownloadLinksAsync(
        string gameDomain, int modId, int fileId,
        string? nxmKey = null, long? expires = null)
    {
        try
        {
            var url = $"{V1BaseUrl}games/{gameDomain}/mods/{modId}/files/{fileId}/download_link.json";

            // Append nxm key and expires as query parameters if provided
            if (!string.IsNullOrEmpty(nxmKey) && expires.HasValue)
            {
                url += $"?key={Uri.EscapeDataString(nxmKey)}&expires={expires.Value}";
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(_apiKey))
                request.Headers.TryAddWithoutValidation("apikey", _apiKey);

            using var response = await _http.SendAsync(request).ConfigureAwait(false);
            TrackRateLimit(response);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _isRateLimited = true;
                CrashReporter.Log("[NexusApiClient.GetDownloadLinksAsync] Rate limited (HTTP 429)");
                return [];
            }

            if (!response.IsSuccessStatusCode)
            {
                CrashReporter.Log($"[NexusApiClient.GetDownloadLinksAsync] Failed with status {(int)response.StatusCode}");
                return [];
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonSerializer.Deserialize<List<NexusDownloadLink>>(json, JsonOptions) ?? [];
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[NexusApiClient.GetDownloadLinksAsync] Exception — {ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// Reads the X-RL-Daily-Remaining header from the response and updates tracking state.
    /// Logs a warning when remaining requests drop below the threshold.
    /// </summary>
    internal void TrackRateLimit(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues(RateLimitHeader, out var values))
        {
            var headerValue = values.FirstOrDefault();
            if (int.TryParse(headerValue, out var remaining))
            {
                _dailyRequestsRemaining = remaining;

                if (remaining < LowRemainingThreshold)
                {
                    CrashReporter.Log($"[NexusApiClient] Daily requests remaining is low: {remaining}");
                }
            }
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            _isRateLimited = true;
        }
    }

    /// <summary>
    /// Builds the GraphQL query payload for fetching mod files.
    /// </summary>
    private static string BuildModFilesGraphQLQuery(string gameDomain, int modId)
    {
        var query = $$"""
        {
            "query": "query ModFiles($gameDomain: String!, $modId: Int!) { modFiles(gameDomain: $gameDomain, modId: $modId) { fileId fileName version sizeKb category uploadedAt } }",
            "variables": {
                "gameDomain": "{{gameDomain}}",
                "modId": {{modId}}
            }
        }
        """;
        return query;
    }

    /// <summary>
    /// Parses the GraphQL response for mod files into a list of NexusFileInfo.
    /// </summary>
    private static List<NexusFileInfo> ParseGraphQLModFilesResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0)
            {
                var errorMsg = errors[0].TryGetProperty("message", out var msg) ? msg.GetString() : "Unknown GraphQL error";
                CrashReporter.Log($"[NexusApiClient.ParseGraphQLModFilesResponse] GraphQL error — {errorMsg}");
                return [];
            }

            if (!root.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("modFiles", out var modFiles))
            {
                CrashReporter.Log("[NexusApiClient.ParseGraphQLModFilesResponse] Unexpected response structure");
                return [];
            }

            var results = new List<NexusFileInfo>();
            foreach (var file in modFiles.EnumerateArray())
            {
                var fileInfo = new NexusFileInfo(
                    FileId: file.GetProperty("fileId").GetInt32(),
                    FileName: file.GetProperty("fileName").GetString() ?? "",
                    Version: file.GetProperty("version").GetString() ?? "",
                    SizeKb: file.GetProperty("sizeKb").GetInt64(),
                    Category: file.GetProperty("category").GetString() ?? "",
                    UploadedAt: file.TryGetProperty("uploadedAt", out var uploadedAt)
                        ? ParseDateTimeOffset(uploadedAt)
                        : DateTimeOffset.MinValue
                );
                results.Add(fileInfo);
            }

            return results;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[NexusApiClient.ParseGraphQLModFilesResponse] Parse failed — {ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// Parses the V1 REST response for mod files into a list of NexusFileInfo.
    /// The V1 response wraps files in a "files" array.
    /// </summary>
    private static List<NexusFileInfo> ParseV1ModFilesResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            JsonElement filesArray;
            if (root.TryGetProperty("files", out var files))
            {
                filesArray = files;
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                filesArray = root;
            }
            else
            {
                CrashReporter.Log("[NexusApiClient.ParseV1ModFilesResponse] Unexpected response structure");
                return [];
            }

            var results = new List<NexusFileInfo>();
            foreach (var file in filesArray.EnumerateArray())
            {
                var fileInfo = new NexusFileInfo(
                    FileId: GetIntProperty(file, "file_id", "id"),
                    FileName: GetStringProperty(file, "file_name", "name"),
                    Version: GetStringProperty(file, "version"),
                    SizeKb: GetLongProperty(file, "size_kb", "size"),
                    Category: GetStringProperty(file, "category_name", "category"),
                    UploadedAt: file.TryGetProperty("uploaded_timestamp", out var ts)
                        ? DateTimeOffset.FromUnixTimeSeconds(ts.GetInt64())
                        : file.TryGetProperty("uploaded_time", out var ut)
                            ? ParseDateTimeOffset(ut)
                            : DateTimeOffset.MinValue
                );
                results.Add(fileInfo);
            }

            return results;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[NexusApiClient.ParseV1ModFilesResponse] Parse failed — {ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// Attempts to parse a DateTimeOffset from a JSON element (string or number).
    /// </summary>
    private static DateTimeOffset ParseDateTimeOffset(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            if (DateTimeOffset.TryParse(element.GetString(), out var dto))
                return dto;
        }
        else if (element.ValueKind == JsonValueKind.Number)
        {
            return DateTimeOffset.FromUnixTimeSeconds(element.GetInt64());
        }
        return DateTimeOffset.MinValue;
    }

    /// <summary>
    /// Gets an int property from a JSON element, trying multiple property names.
    /// </summary>
    private static int GetIntProperty(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number)
                return prop.GetInt32();
        }
        return 0;
    }

    /// <summary>
    /// Gets a long property from a JSON element, trying multiple property names.
    /// </summary>
    private static long GetLongProperty(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number)
                return prop.GetInt64();
        }
        return 0;
    }

    /// <summary>
    /// Gets a string property from a JSON element, trying multiple property names.
    /// </summary>
    private static string GetStringProperty(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
                return prop.GetString() ?? "";
        }
        return "";
    }
}
