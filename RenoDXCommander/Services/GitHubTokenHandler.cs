// GitHubTokenHandler.cs — attaches the user's GitHub token to requests for GitHub hosts only.
// The shared HttpClient also downloads from reshade.me, Nexus, Codeberg, PCGamingWiki and any
// URL in the manifest, so the token must never be a default header on the client.

using System.Net.Http;

namespace RenoDXCommander.Services;

public sealed class GitHubTokenHandler : DelegatingHandler
{
    // Hosts that may receive the token. Exact match, HTTPS only.
    // API only: the token raises the API rate limit and does nothing elsewhere. Download hosts
    // (codeload, raw.githubusercontent) answer 404 to a token they don't accept (e.g. a
    // fine-grained PAT), which broke shader pack downloads for signed-in users.
    private static readonly HashSet<string> GitHubHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "api.github.com",
    };

    private static volatile string? _token;

    public GitHubTokenHandler(HttpMessageHandler innerHandler) : base(innerHandler) { }

    /// <summary>Sets (or clears, with null/empty) the token used for GitHub requests.</summary>
    public static void SetToken(string? token)
    {
        lock (_tokenLock)
        {
            _token = string.IsNullOrWhiteSpace(token) ? null : token.Trim();
            if (_token != null) TokenRejected = false; // a newly saved token gets a fresh chance
        }
    }

    private static readonly object _tokenLock = new();

    /// <summary>True when <paramref name="uri"/> is a GitHub host that may receive the token.</summary>
    public static bool IsGitHubHost(Uri? uri) =>
        uri != null
        && uri.IsAbsoluteUri
        && uri.Scheme == Uri.UriSchemeHttps
        && GitHubHosts.Contains(uri.Host);

    /// <summary>True when a request to <paramref name="uri"/> will carry the token.</summary>
    /// <summary>True when a GitHub token is set (5,000 API requests an hour instead of 60).</summary>
    public static bool HasToken => _token != null;

    public static bool WillAuthorize(Uri? uri) => _token != null && IsGitHubHost(uri);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = _token;
        if (token != null
            && IsGitHubHost(request.RequestUri)
            && !request.Headers.Contains("Authorization"))
        {
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        }
        else if (request.Headers.Contains("Authorization") && !IsGitHubHost(request.RequestUri)
                 && token != null
                 && request.Headers.TryGetValues("Authorization", out var values)
                 && values.Any(v => v.Contains(token, StringComparison.Ordinal)))
        {
            // Safety net: a caller added the GitHub token to a non-GitHub request.
            request.Headers.Remove("Authorization");
            CrashReporter.Log($"[GitHubTokenHandler.SendAsync] Stripped GitHub token from request to {request.RequestUri?.Host}");
        }

        return SendCheckingTokenAsync(request, token, cancellationToken);
    }

    /// <summary>
    /// True once GitHub rejected the token (401: expired or revoked) this session. The token is
    /// then dropped until a new one is saved, and Settings / the status bar tell the user.
    /// </summary>
    public static bool TokenRejected { get; private set; }

    /// <summary>
    /// Sends the request. If GitHub answers 401 to the token, the token is dropped for the session
    /// and the request is sent again without it. Without this, every API call failed and callers
    /// kept using their cached copies (manifest, release checks) for as long as the bad token stayed.
    /// </summary>
    private async Task<HttpResponseMessage> SendCheckingTokenAsync(HttpRequestMessage request, string? token, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != System.Net.HttpStatusCode.Unauthorized
            || token == null
            || !IsGitHubHost(request.RequestUri)
            || request.Content != null) // only bodiless requests (GET/HEAD) are safe to resend
            return response;

        // Drop the token (only if it's still the one we sent; a new one may have been saved meanwhile)
        bool dropped = false;
        lock (_tokenLock)
        {
            if (_token == token) { _token = null; TokenRejected = true; dropped = true; }
        }
        if (dropped)
            CrashReporter.Log("[GitHubTokenHandler.SendAsync] GitHub rejected the token (401, expired or revoked) — not using it this session; add a new one in Settings");

        var retry = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version };
        foreach (var h in request.Headers)
            if (!h.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                retry.Headers.TryAddWithoutValidation(h.Key, h.Value);
        foreach (var o in request.Options)
            ((IDictionary<string, object?>)retry.Options)[o.Key] = o.Value;

        response.Dispose();
        return await base.SendAsync(retry, cancellationToken).ConfigureAwait(false);
    }
}
