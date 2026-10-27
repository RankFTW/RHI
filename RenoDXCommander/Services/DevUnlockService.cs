// DevUnlockService.cs — Checks for the presence of %LocalAppData%\RHI\unlock.txt
// to gate developer-only features. Result is cached for the lifetime of the process.

namespace RenoDXCommander.Services;

/// <summary>
/// Gates developer-only features behind the existence of %LocalAppData%\RHI\unlock.txt.
/// The check is performed once and cached — restart required to pick up changes.
/// </summary>
public static class DevUnlockService
{
    private static readonly string UnlockFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "unlock.txt");

    /// <summary>
    /// Dedicated GitHub API token file — any user can place their token here to raise
    /// the GitHub API rate limit from 60 to 5000 requests/hour. No other content needed,
    /// just the raw token on a single line. Takes priority over the github_api= entry
    /// in unlock.txt.
    /// Path: %LocalAppData%\RHI\github_api.txt
    /// </summary>
    private static readonly string GitHubApiTokenFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "github_api.txt");

    private static bool? _isUnlocked;

    /// <summary>
    /// Returns true if %LocalAppData%\RHI\unlock.txt exists.
    /// Result is cached after the first call.
    /// </summary>
    public static bool IsUnlocked => _isUnlocked ??= File.Exists(UnlockFilePath);

    private static string? _gitHubApiToken;
    private static bool _gitHubApiTokenRead;

    /// <summary>
    /// Returns a GitHub API token. Checks in order:
    /// 1. %LocalAppData%\RHI\github_api.txt (raw token, any user)
    /// 2. github_api= line in unlock.txt (dev token)
    /// Returns null if neither is configured.
    /// </summary>
    public static string? GitHubApiToken
    {
        get
        {
            if (_gitHubApiTokenRead) return _gitHubApiToken;
            _gitHubApiTokenRead = true;

            // 1. Dedicated github_api.txt — available to all users
            try
            {
                if (File.Exists(GitHubApiTokenFilePath))
                {
                    var token = File.ReadAllText(GitHubApiTokenFilePath).Trim();
                    if (!string.IsNullOrEmpty(token))
                    {
                        _gitHubApiToken = token;
                        CrashReporter.Log("[DevUnlockService] GitHub API token loaded from github_api.txt");
                        return _gitHubApiToken;
                    }
                }
            }
            catch (Exception ex) { CrashReporter.Log($"[DevUnlockService.GitHubApiToken] Failed to read github_api.txt — {ex.Message}"); }

            // 2. Fallback: github_api= line in unlock.txt
            if (!IsUnlocked) return null;
            try
            {
                foreach (var line in File.ReadLines(UnlockFilePath))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("github_api=", StringComparison.OrdinalIgnoreCase))
                    {
                        _gitHubApiToken = trimmed.Substring("github_api=".Length).Trim();
                        return _gitHubApiToken;
                    }
                }
            }
            catch (Exception ex) { CrashReporter.Log($"[DevUnlockService.GitHubApiToken] Failed to read unlock.txt — {ex.Message}"); }
            return null;
        }
    }

    /// <summary>
    /// Updates the in-memory GitHub API token at runtime (e.g. after a Device Flow OAuth).
    /// Also clears the cached "not found" state so the new token is returned immediately.
    /// </summary>
    public static void UpdateToken(string? token)
    {
        _gitHubApiToken = string.IsNullOrEmpty(token) ? null : token;
        _gitHubApiTokenRead = true; // mark resolved so getter won't re-read from disk
    }

    /// <summary>
    /// Resets the token cache so the next call to <see cref="GitHubApiToken"/> re-reads from disk.
    /// Use when disconnecting an OAuth token so any file-based token can take over.
    /// </summary>
    public static void ResetTokenCache()
    {
        _gitHubApiToken = null;
        _gitHubApiTokenRead = false;
    }
}
