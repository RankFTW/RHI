// GitHubTokenSetup.cs -- checks a personal access token with GitHub, saves it to %LocalAppData%\RHI\github_api.txt and
// applies it to the shared HttpClient (through GitHubAuthService, so it only goes to api.github.com). Used by
// Settings › Accounts & Data and the first-run screen (SetupWindow), so both save it the same way.

namespace RenoDXCommander.Services;

public static class GitHubTokenSetup
{
    /// <summary>Where to make one: a classic token, no scopes needed (RHI only reads public releases).</summary>
    public const string NewTokenUrl = "https://github.com/settings/tokens/new?description=RHI";

    /// <summary>True when the text looks like a GitHub token (classic or fine-grained).</summary>
    public static bool LooksLikeToken(string pat) =>
        pat.StartsWith("ghp_", StringComparison.OrdinalIgnoreCase) || pat.StartsWith("github_pat_", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Validates the token (GET /user, 5 s), then saves and applies it. Returns the GitHub user name on success,
    /// or an error message the UI can show as is.
    /// </summary>
    public static async Task<(bool Ok, string? Username, string? Error)> ValidateAndSaveAsync(HttpClient http, string pat)
    {
        pat = pat.Trim();
        if (string.IsNullOrEmpty(pat)) return (false, null, "Enter a token first.");

        string? username = null;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {pat}");
            req.Headers.TryAddWithoutValidation("User-Agent", "RHI");
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (!resp.IsSuccessStatusCode)
            {
                CrashReporter.Log($"[GitHubTokenSetup.ValidateAndSaveAsync] GitHub refused the token ({(int)resp.StatusCode})");
                return (false, null, "GitHub didn't accept this token. Check you copied all of it, and that it hasn't expired.");
            }
            using var doc = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (doc.RootElement.TryGetProperty("login", out var login)) username = login.GetString();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            CrashReporter.Log($"[GitHubTokenSetup.ValidateAndSaveAsync] Couldn't check the token — {ex.Message}");
            return (false, null, "RHI couldn't reach GitHub to check the token. Check your connection and try again.");
        }

        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "github_api.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, pat);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[GitHubTokenSetup.ValidateAndSaveAsync] Couldn't save — {ex.Message}");
            return (false, null, $"The token couldn't be saved: {ex.Message}");
        }

        DevUnlockService.ResetTokenCache();
        DevUnlockService.UpdateToken(pat);
        GitHubAuthService.ApplyTokenToHttpClient(http, pat);
        CrashReporter.Log($"[GitHubTokenSetup.ValidateAndSaveAsync] Token saved to github_api.txt (user={username ?? "unknown"})");
        return (true, username, null);
    }
}
