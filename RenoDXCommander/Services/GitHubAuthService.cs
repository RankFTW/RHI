// GitHubAuthService.cs — applies the GitHub token that raises the API rate limit
// (60 req/hr anonymous → 5,000 req/hr authenticated).

using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RenoDXCommander.Services;

/// <summary>
/// Applies the saved GitHub token (a personal access token, or a session from the
/// sign-in option RHI used to offer) to the shared HttpClient.
/// </summary>
public sealed class GitHubAuthService
{
    private readonly HttpClient _http;

    public GitHubAuthService(HttpClient http) => _http = http;

    /// <summary>
    /// Applies the token to the shared HttpClient default headers.
    /// Removes any existing Authorization header first.
    /// </summary>
    public static void ApplyTokenToHttpClient(HttpClient http, string? token)
    {
        // The token is attached per request to GitHub hosts only (GitHubTokenHandler).
        // Clear any default header so it can't leak to other hosts.
        http.DefaultRequestHeaders.Remove("Authorization");
        GitHubTokenHandler.SetToken(token);
    }
}
