using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;

namespace RenoDXCommander.Services;

/// <summary>
/// Caches GitHub API ETag values and response bodies to enable conditional requests.
/// When a cached ETag is sent via If-None-Match and the resource hasn't changed,
/// GitHub returns 304 Not Modified which doesn't count against the rate limit.
/// When the host supplies <c>load</c>/<c>save</c>, the cache survives restarts so the first
/// requests of each launch are conditional too (Windows saves it to
/// %LocalAppData%\RHI\github_etag_cache.json). Entries unused for 30 days are dropped on load.
/// </summary>
public class GitHubETagCache
{
    private readonly Action<string> _log;
    private readonly Func<string?> _getToken;
    private readonly Func<Uri?, bool> _willAuthorize;
    private readonly Action<Dictionary<string, SavedEntry>>? _save;

    private readonly ConcurrentDictionary<string, (string etag, string body)> _cache = new();

    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);
    private static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Persisted form of one cache entry.</summary>
    public sealed class SavedEntry
    {
        public string ETag { get; set; } = "";
        public string Body { get; set; } = "";
        public DateTime SavedUtc { get; set; }
    }

    // Last-saved time per URL, so old entries can be dropped.
    private readonly ConcurrentDictionary<string, DateTime> _savedUtc = new();

    /// <param name="log">Diagnostic log sink.</param>
    /// <param name="getToken">
    /// Token attached here for GitHub API requests only. Leave null when the HttpClient's own
    /// handler attaches it (Windows uses GitHubTokenHandler).
    /// </param>
    /// <param name="willAuthorize">Reports whether the client's handler will authorize a request (logging only).</param>
    /// <param name="load">Returns the persisted cache, or null when there is none.</param>
    /// <param name="save">Persists the cache; batched to one write 2 s after the last change.</param>
    public GitHubETagCache(
        Action<string>? log = null,
        Func<string?>? getToken = null,
        Func<Uri?, bool>? willAuthorize = null,
        Func<Dictionary<string, SavedEntry>?>? load = null,
        Action<Dictionary<string, SavedEntry>>? save = null)
    {
        _log = log ?? (_ => { });
        _getToken = getToken ?? (() => null);
        _willAuthorize = willAuthorize ?? (_ => false);
        _save = save;

        var saved = load?.Invoke();
        if (saved == null) return;
        var cutoff = DateTime.UtcNow - MaxAge;
        foreach (var (url, e) in saved)
        {
            if (e.SavedUtc < cutoff || string.IsNullOrEmpty(e.ETag) || e.Body == null) continue;
            _cache[url] = (e.ETag, e.Body);
            _savedUtc[url] = e.SavedUtc;
        }
        _log($"[GitHubETagCache] Loaded {_cache.Count} saved ETag(s)");
    }

    // Startup fires many API calls at once; batch their saves into one write 2 s after the last change.
    private System.Threading.Timer? _persistTimer;
    private readonly object _persistLock = new();

    private void Persist()
    {
        if (_save == null) return;
        lock (_persistLock)
        {
            _persistTimer ??= new System.Threading.Timer(_ => WriteNow(), null, Timeout.Infinite, Timeout.Infinite);
            _persistTimer.Change(TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
        }
    }

    private void WriteNow()
    {
        var snapshot = _cache.ToArray().ToDictionary(
            kv => kv.Key,
            kv => new SavedEntry
            {
                ETag = kv.Value.etag,
                Body = kv.Value.body,
                SavedUtc = _savedUtc.TryGetValue(kv.Key, out var t) ? t : DateTime.UtcNow,
            });
        try { _save!(snapshot); }
        catch (Exception ex) { _log($"[GitHubETagCache.Persist] Failed to save — {ex.Message}"); }
    }

    /// <summary>Session-scoped flag — set when GitHub returns 403 (rate limited).</summary>
    private volatile bool _rateLimited;

    /// <summary>True if a 403 Forbidden response has been received this session.</summary>
    public bool IsRateLimited => _rateLimited;

    /// <summary>
    /// Removes the cached ETag and body for <paramref name="url"/> so the next
    /// <see cref="GetWithETagAsync"/> call sends an unconditional GET.
    /// Use when you know the remote content has changed (e.g. on Refresh).
    /// </summary>
    public void Invalidate(string url)
    {
        _checkedUtc.TryRemove(url, out _);
        if (_cache.TryRemove(url, out _)) { _savedUtc.TryRemove(url, out _); Persist(); }
    }

    // Several services read the same URL around the same time (e.g. the rhi-repo release list).
    // Calls that overlap share one request, and an answer is reused for FreshFor.
    private readonly ConcurrentDictionary<string, Lazy<Task<string?>>> _inFlight = new();
    private readonly ConcurrentDictionary<string, DateTime> _checkedUtc = new();
    private static readonly TimeSpan FreshFor = TimeSpan.FromSeconds(60);

    private static bool IsApiUrl(string url) =>
        url.StartsWith("https://api.github.com/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Sends a GET request with ETag caching. If the resource hasn't changed,
    /// returns the cached body without consuming a rate limit point.
    /// </summary>
    public async Task<string?> GetWithETagAsync(HttpClient http, string url, string? userAgent = null)
    {
        if (_checkedUtc.TryGetValue(url, out var checkedAt) && DateTime.UtcNow - checkedAt < FreshFor
            && _cache.TryGetValue(url, out var fresh) && fresh.body != null)
            return fresh.body;

        var lazy = _inFlight.GetOrAdd(url, u => new Lazy<Task<string?>>(() => FetchAsync(http, u, userAgent)));
        try { return await lazy.Value.ConfigureAwait(false); }
        finally { _inFlight.TryRemove(new KeyValuePair<string, Lazy<Task<string?>>>(url, lazy)); }
    }

    private async Task<string?> FetchAsync(HttpClient http, string url, string? userAgent)
    {
        // ── Rate-limit guard (api.github.com only): return cached body or null ──
        if (_rateLimited && IsApiUrl(url))
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

        // The token raises the API rate limit (60 → 5000 req/hour). It goes to the GitHub API
        // only: download hosts answer 404 to tokens they don't accept, and other hosts must never see it.
        var token = _getToken();
        if (!string.IsNullOrEmpty(token) && IsApiUrl(url))
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");

        if (userAgent != null)
        {
            request.Headers.UserAgent.Clear();
            request.Headers.UserAgent.ParseAdd(userAgent);
        }

        // Add cached ETag if we have one
        if (_cache.TryGetValue(url, out var cached))
            request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(cached.etag));

        // API responses are small: give up after 30 s instead of the shared client's 10-minute
        // download timeout, so one stuck call can't hold up startup checks.
        using var timeout = new CancellationTokenSource(ApiTimeout);
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            _log($"[GitHubETagCache] Timed out after {ApiTimeout.TotalSeconds:F0}s for {TruncateUrl(url)}");
            return cached.body; // stale body if we have one, else null
        }
        using var _ = response;

        if (response.StatusCode == HttpStatusCode.NotModified && cached.body != null)
        {
            _log($"[GitHubETagCache] 304 Not Modified for {TruncateUrl(url)}");
            _checkedUtc[url] = DateTime.UtcNow;
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
                var hasAuthHeader = request.Headers.Contains("Authorization") || _willAuthorize(request.RequestUri);

                if (actuallyRateLimited && !_rateLimited && IsApiUrl(url))
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

        var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);

        // Cache the ETag and body for next time
        var etag = response.Headers.ETag?.Tag;
        if (etag != null)
        {
            _cache[url] = (etag, body);
            _savedUtc[url] = DateTime.UtcNow;
            _checkedUtc[url] = DateTime.UtcNow;
            Persist();
        }

        return body;
    }

    private static string TruncateUrl(string url) => url.Length > 80 ? url[..80] + "..." : url;
}
