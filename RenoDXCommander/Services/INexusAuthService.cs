using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Manages Nexus Mods authentication state, key validation, and (Phase 2) OAuth flow.
/// </summary>
public interface INexusAuthService
{
    /// <summary>Current user profile (null if not authenticated).</summary>
    NexusUserProfile? CurrentUser { get; }

    /// <summary>Whether the current user has premium status.</summary>
    bool IsPremium { get; }

    /// <summary>Whether any valid authentication exists.</summary>
    bool IsAuthenticated { get; }

    /// <summary>Validates a personal API key and stores it if valid.</summary>
    Task<bool> ValidateAndStoreApiKeyAsync(string apiKey);

    /// <summary>Re-validates the stored key at startup.</summary>
    Task<bool> RevalidateStoredKeyAsync();

    /// <summary>Clears all tokens and cached profile.</summary>
    void UnlinkAccount();

    /// <summary>Returns the current Bearer token for API calls.</summary>
    string? GetBearerToken();

    // Phase 2: OAuth

    /// <summary>Initiates OAuth 2.0 + PKCE flow.</summary>
    Task<bool> StartOAuthFlowAsync();

    /// <summary>Refreshes the access token if near expiry.</summary>
    Task<bool> RefreshTokenIfNeededAsync();
}
