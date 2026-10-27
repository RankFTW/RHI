// GitHubAuthService.cs — GitHub Device Flow OAuth for raising the API rate limit
// (60 req/hr anonymous → 5,000 req/hr authenticated).
// Docs: https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/generating-a-user-access-token-for-a-github-app

using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RenoDXCommander.Services;

/// <summary>
/// Returned by <see cref="GitHubAuthService.RequestDeviceCodeAsync"/>.
/// Contains the user-facing code and the URL they need to visit.
/// </summary>
public sealed record DeviceCodeResponse(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    int ExpiresIn,
    int Interval);

/// <summary>
/// GitHub Device Flow OAuth helper.
/// Starts the device flow, polls for the access token, and returns it.
/// The token is then stored in settings and applied to the shared HttpClient.
/// </summary>
public sealed class GitHubAuthService
{
    /// <summary>
    /// GitHub App Client ID.  This is NOT a secret — device flow only uses it to identify the app.
    /// Replace with the actual Client ID from the GitHub App settings page once the app is created.
    /// </summary>
    public const string ClientId = "Iv23lijkKOAsRqnn4m17";

    private const string DeviceCodeUrl    = "https://github.com/login/device/code";
    private const string AccessTokenUrl   = "https://github.com/login/oauth/access_token";

    private readonly HttpClient _http;

    public GitHubAuthService(HttpClient http) => _http = http;

    /// <summary>
    /// Step 1 of device flow: request a device code and user code from GitHub.
    /// Returns null on failure.
    /// </summary>
    public async Task<DeviceCodeResponse?> RequestDeviceCodeAsync(CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, DeviceCodeUrl);
            req.Headers.Accept.ParseAdd("application/json");
            req.Content = new StringContent(
                $"client_id={Uri.EscapeDataString(ClientId)}",
                Encoding.UTF8, "application/x-www-form-urlencoded");

            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                CrashReporter.Log($"[GitHubAuthService.RequestDeviceCodeAsync] HTTP {(int)resp.StatusCode}");
                return null;
            }

            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var doc  = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (!root.TryGetProperty("device_code",      out var dc)  ||
                !root.TryGetProperty("user_code",         out var uc)  ||
                !root.TryGetProperty("verification_uri",  out var uri) ||
                !root.TryGetProperty("expires_in",        out var exp) ||
                !root.TryGetProperty("interval",          out var inv))
            {
                CrashReporter.Log("[GitHubAuthService.RequestDeviceCodeAsync] Unexpected response shape");
                return null;
            }

            return new DeviceCodeResponse(
                dc.GetString()  ?? "",
                uc.GetString()  ?? "",
                uri.GetString() ?? "https://github.com/login/device",
                exp.GetInt32(),
                inv.GetInt32());
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[GitHubAuthService.RequestDeviceCodeAsync] {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Step 2 of device flow: poll GitHub until the user authorises (or the code expires).
    /// Returns the access token string on success, null on failure/timeout/cancellation.
    /// </summary>
    public async Task<string?> PollForTokenAsync(
        DeviceCodeResponse deviceCode,
        IProgress<string>? statusProgress = null,
        CancellationToken ct = default)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(deviceCode.Interval, 5));
        var deadline = DateTime.UtcNow.AddSeconds(deviceCode.ExpiresIn);

        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            await Task.Delay(interval, ct).ConfigureAwait(false);

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, AccessTokenUrl);
                req.Headers.Accept.ParseAdd("application/json");
                req.Content = new StringContent(
                    $"client_id={Uri.EscapeDataString(ClientId)}" +
                    $"&device_code={Uri.EscapeDataString(deviceCode.DeviceCode)}" +
                    $"&grant_type={Uri.EscapeDataString("urn:ietf:params:oauth:grant-type:device_code")}",
                    Encoding.UTF8, "application/x-www-form-urlencoded");

                using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var doc  = JsonDocument.Parse(body);
                var root = doc.RootElement;

                // Success
                if (root.TryGetProperty("access_token", out var at))
                {
                    var token = at.GetString();
                    if (!string.IsNullOrEmpty(token))
                        return token;
                }

                // Known wait states
                if (root.TryGetProperty("error", out var err))
                {
                    var error = err.GetString();
                    switch (error)
                    {
                        case "authorization_pending":
                            statusProgress?.Report("Waiting for you to authorise in your browser...");
                            break;
                        case "slow_down":
                            // GitHub asks us to back off — add 5 seconds
                            interval += TimeSpan.FromSeconds(5);
                            statusProgress?.Report("Waiting for you to authorise in your browser...");
                            break;
                        case "expired_token":
                            CrashReporter.Log("[GitHubAuthService.PollForTokenAsync] Device code expired");
                            return null;
                        case "access_denied":
                            CrashReporter.Log("[GitHubAuthService.PollForTokenAsync] Access denied by user");
                            return null;
                        default:
                            CrashReporter.Log($"[GitHubAuthService.PollForTokenAsync] Error: {error}");
                            return null;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[GitHubAuthService.PollForTokenAsync] Poll error — {ex.Message}");
                return null;
            }
        }

        CrashReporter.Log("[GitHubAuthService.PollForTokenAsync] Timed out waiting for user authorisation");
        return null;
    }

    /// <summary>
    /// Fetches the authenticated user's login name from the GitHub API.
    /// Used to show a "Connected as @username" status after authorisation.
    /// </summary>
    public async Task<string?> GetUsernameAsync(string token, CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            req.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");

            using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var doc  = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("login", out var login) ? login.GetString() : null;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[GitHubAuthService.GetUsernameAsync] {ex.Message}");
            return null;
        }
    }

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
