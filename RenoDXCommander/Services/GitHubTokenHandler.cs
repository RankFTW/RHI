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
    public static void SetToken(string? token) =>
        _token = string.IsNullOrWhiteSpace(token) ? null : token.Trim();

    /// <summary>True when <paramref name="uri"/> is a GitHub host that may receive the token.</summary>
    public static bool IsGitHubHost(Uri? uri) =>
        uri != null
        && uri.IsAbsoluteUri
        && uri.Scheme == Uri.UriSchemeHttps
        && GitHubHosts.Contains(uri.Host);

    /// <summary>True when a request to <paramref name="uri"/> will carry the token.</summary>
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

        return base.SendAsync(request, cancellationToken);
    }
}
