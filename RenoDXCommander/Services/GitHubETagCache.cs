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
    private readonly Action<string> _log;
    private readonly Func<string?> _getToken;

    public GitHubETagCache(Action<string>? log = null, Func<string?>? getToken = null)
    {
        _log = log ?? (_ => { });
        _getToken = getToken ?? (() => null);
    }

    private readonly ConcurrentDictionary<string, (string etag, string body)> _cache = new();

    /// <summary>Session-scoped flag — set when GitHub returns 403 (rate limited).</summary>
    private volatile bool _rateLimited;

    /// <summary>True if a 403 Forbidden response has been received this session.</summary>
    public bool IsRateLimited => _rateLimited;

    /// <summary>
    /// Removes the cached ETag and body for <paramref name="url"/> so the next
    /// <see cref="GetWithETagAsync"/> call sends an unconditional GET.
    /// Use when you know the remote content has changed (e.g. on Refresh).
    /// </summary>
    public void Invalidate(string url) => _cache.TryRemove(url, out _);

    /// <summary>Removes all cached ETags and bodies, forcing unconditional GETs on all URLs.</summary>
    public void InvalidateAll() => _cache.Clear();

    /// <summary>
    /// Sends a GET request with ETag caching. If the resource hasn't changed,
    /// returns the cached body without consuming a rate limit point.
    /// </summary>
    public async Task<string?> GetWithETagAsync(HttpClient http, string url, string? userAgent = null)
    {
        // ── Rate-limit guard: return cached body or null if rate-limited ──
        if (_rateLimited)
        {
            if (_cache.TryGetValue(url, out var stale) && stale.body != null)
                return stale.body;
            _log($"[GitHubETagCache] Rate limited — skipping {TruncateUrl(url)}");
            return null;
        }

        // Build request
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("RHI", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        // Add GitHub API token if available (raises rate limit from 60 to 5000 req/hour)
        var token = _getToken();
        if (!string.IsNullOrEmpty(token))
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");

        if (userAgent != null)
        {
            request.Headers.UserAgent.Clear();
            request.Headers.UserAgent.ParseAdd(userAgent);
        }

        // Add cached ETag if we have one
        if (_cache.TryGetValue(url, out var cached))
            request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(cached.etag));

        using var response = await http.SendAsync(request).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotModified && cached.body != null)
        {
            _log($"[GitHubETagCache] 304 Not Modified for {TruncateUrl(url)}");
            return cached.body;
        }

        if (!response.IsSuccessStatusCode)
        {
            // Detect rate limiting: only set session-wide flag when GitHub explicitly reports
            // remaining=0, not on every 403. A transient 403 (auth scope, IP block, etc.)
            // should not kill all API calls for the rest of the session.
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                var remaining = response.Headers.TryGetValues("X-RateLimit-Remaining", out var vals)
                    ? vals.FirstOrDefault() : null;
                bool actuallyRateLimited = remaining == "0";
                var hasAuthHeader = request.Headers.Contains("Authorization");

                if (actuallyRateLimited && !_rateLimited)
                {
                    _rateLimited = true;
                    _log($"[GitHubETagCache] GitHub API rate limited (X-RateLimit-Remaining=0) for {TruncateUrl(url)} — authenticated={hasAuthHeader}");
                }
                else if (!actuallyRateLimited)
                {
                    _log($"[GitHubETagCache] 403 Forbidden for {TruncateUrl(url)} (not rate limited, remaining={remaining ?? "unknown"}, authenticated={hasAuthHeader}) — skipping this URL only");
                }
            }

            // If we have a cached body (e.g. from a previous successful request) and the
            // server returned an error (403 rate-limited, 5xx, etc.), return the stale
            // cached body rather than null. This avoids unnecessary fallback requests and
            // keeps the app working with slightly stale data.
            if (cached.body != null)
            {
                _log($"[GitHubETagCache] {response.StatusCode} for {TruncateUrl(url)} — returning cached body");
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
