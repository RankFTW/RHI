using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;

namespace RenoDXCommander.Services;

/// <summary>
/// Caches GitHub API ETag values and response bodies to enable conditional requests.
/// When a cached ETag is sent via If-None-Match and the resource hasn't changed,
/// GitHub returns 304 Not Modified which doesn't count against the rate limit.
/// Cache is in-memory only (session-scoped) — no disk persistence needed.
/// </summary>
public class GitHubETagCache
{
    private readonly ConcurrentDictionary<string, (string etag, string body)> _cache = new();

    /// <summary>
    /// Sends a GET request with ETag caching. If the resource hasn't changed,
    /// returns the cached body without consuming a rate limit point.
    /// </summary>
    public async Task<string?> GetWithETagAsync(HttpClient http, string url, string? userAgent = null)
    {
        // Build request
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("RHI", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        if (userAgent != null)
        {
            request.Headers.UserAgent.Clear();
            request.Headers.UserAgent.ParseAdd(userAgent);
        }

        // Add cached ETag if we have one
        if (_cache.TryGetValue(url, out var cached))
            request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(cached.etag));

        var response = await http.SendAsync(request).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotModified && cached.body != null)
        {
            CrashReporter.Log($"[GitHubETagCache] 304 Not Modified for {TruncateUrl(url)}");
            return cached.body;
        }

        if (!response.IsSuccessStatusCode)
        {
            // If we have a cached body (e.g. from a previous successful request) and the
            // server returned an error (403 rate-limited, 5xx, etc.), return the stale
            // cached body rather than null. This avoids unnecessary fallback requests and
            // keeps the app working with slightly stale data.
            if (cached.body != null)
            {
                CrashReporter.Log($"[GitHubETagCache] {response.StatusCode} for {TruncateUrl(url)} — returning cached body");
                return cached.body;
            }
            return null; // Let caller handle the error
        }

        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        // Cache the ETag and body for next time
        var etag = response.Headers.ETag?.Tag;
        if (etag != null)
            _cache[url] = (etag, body);

        return body;
    }

    private static string TruncateUrl(string url) => url.Length > 80 ? url[..80] + "..." : url;
}
