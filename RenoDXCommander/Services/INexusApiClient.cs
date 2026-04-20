using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Low-level HTTP client for Nexus Mods V1 REST and V2 GraphQL APIs.
/// Handles auth headers, rate limit tracking, and error mapping.
/// </summary>
public interface INexusApiClient
{
    /// <summary>Validates an API key via GET /v1/users/validate.json.</summary>
    Task<NexusValidationResponse?> ValidateKeyAsync(string apiKey);

    /// <summary>Queries mod files via V2 GraphQL.</summary>
    Task<List<NexusFileInfo>> GetModFilesGraphQLAsync(string gameDomain, int modId);

    /// <summary>Gets file list via V1 REST (for identifying latest main file).</summary>
    Task<List<NexusFileInfo>> GetModFilesV1Async(string gameDomain, int modId);

    /// <summary>Gets CDN download links for a specific file (premium or nxm key+expires).</summary>
    Task<List<NexusDownloadLink>> GetDownloadLinksAsync(
        string gameDomain, int modId, int fileId,
        string? nxmKey = null, long? expires = null);

    /// <summary>Current daily API requests remaining.</summary>
    int DailyRequestsRemaining { get; }

    /// <summary>Whether the rate limit has been hit.</summary>
    bool IsRateLimited { get; }
}
