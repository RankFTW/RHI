using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Handles encrypted persistence of API keys and OAuth tokens using DPAPI.
/// </summary>
public interface INexusTokenStore
{
    /// <summary>Saves an API key or access token, encrypted with DPAPI.</summary>
    void SaveApiKey(string apiKey);

    /// <summary>Reads and decrypts the stored API key. Returns null if absent or corrupted.</summary>
    string? LoadApiKey();

    /// <summary>Saves OAuth tokens (access, refresh, expiry).</summary>
    void SaveOAuthTokens(string accessToken, string refreshToken, DateTimeOffset expiry);

    /// <summary>Loads OAuth tokens. Returns null if absent or corrupted.</summary>
    OAuthTokenData? LoadOAuthTokens();

    /// <summary>Deletes all stored tokens from disk.</summary>
    void ClearAll();

    /// <summary>Returns true if any valid token exists on disk.</summary>
    bool HasStoredToken();
}
