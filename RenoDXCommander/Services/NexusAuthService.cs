using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Implements Nexus Mods authentication using API key validation (Phase 1)
/// with stubbed OAuth 2.0 + PKCE support (Phase 2).
/// </summary>
public class NexusAuthService : INexusAuthService
{
    private readonly INexusApiClient _apiClient;
    private readonly INexusTokenStore _tokenStore;

    public NexusAuthService(INexusApiClient apiClient, INexusTokenStore tokenStore)
    {
        _apiClient = apiClient;
        _tokenStore = tokenStore;
    }

    /// <summary>
    /// Sets the API key on the underlying HTTP client so all subsequent requests are authenticated.
    /// </summary>
    private void PushApiKeyToClient(string? apiKey)
    {
        if (_apiClient is NexusApiClient concrete)
            concrete.SetApiKey(apiKey);
    }

    /// <inheritdoc />
    public NexusUserProfile? CurrentUser { get; private set; }

    /// <inheritdoc />
    public bool IsPremium { get; private set; }

    /// <inheritdoc />
    public bool IsAuthenticated { get; private set; }

    /// <inheritdoc />
    public async Task<bool> ValidateAndStoreApiKeyAsync(string apiKey)
    {
        try
        {
            var response = await _apiClient.ValidateKeyAsync(apiKey);

            if (response is null)
            {
                // 401 or invalid key — do not store
                CrashReporter.Log("[NexusAuthService.ValidateAndStoreApiKeyAsync] Validation failed — key not stored");
                return false;
            }

            // Store the valid key
            _tokenStore.SaveApiKey(apiKey);
            PushApiKeyToClient(apiKey);

            // Extract profile and premium status
            CurrentUser = new NexusUserProfile(
                response.Name,
                response.Email,
                response.ProfileUrl,
                response.IsPremium,
                response.IsSupporter);

            IsPremium = response.IsPremium;
            IsAuthenticated = true;

            CrashReporter.Log("[NexusAuthService.ValidateAndStoreApiKeyAsync] Key validated and stored successfully");
            return true;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[NexusAuthService.ValidateAndStoreApiKeyAsync] Error — {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> RevalidateStoredKeyAsync()
    {
        try
        {
            var storedKey = _tokenStore.LoadApiKey();

            if (string.IsNullOrEmpty(storedKey))
            {
                CrashReporter.Log("[NexusAuthService.RevalidateStoredKeyAsync] No stored key found");
                return false;
            }

            // Always push the stored key to the client so API calls work even if
            // revalidation fails due to network issues. The key was valid when stored.
            PushApiKeyToClient(storedKey);
            IsAuthenticated = true;

            var response = await _apiClient.ValidateKeyAsync(storedKey);

            if (response is null)
            {
                // Could be 401 (invalid key) or network error — we can't tell from here.
                // Keep the key pushed and auth state set optimistically. If the key is
                // truly invalid, subsequent API calls will fail with 401 and the user
                // can re-link in Settings.
                CrashReporter.Log("[NexusAuthService.RevalidateStoredKeyAsync] Revalidation returned null (network error or invalid key) — keeping stored key optimistically");
                return true; // Return true so update checks still run
            }

            // Update profile and premium status
            CurrentUser = new NexusUserProfile(
                response.Name,
                response.Email,
                response.ProfileUrl,
                response.IsPremium,
                response.IsSupporter);

            IsPremium = response.IsPremium;

            CrashReporter.Log("[NexusAuthService.RevalidateStoredKeyAsync] Stored key re-validated successfully");
            return true;
        }
        catch (Exception ex)
        {
            // Network exception — still push the key optimistically
            var storedKey = _tokenStore.LoadApiKey();
            if (!string.IsNullOrEmpty(storedKey))
            {
                PushApiKeyToClient(storedKey);
                IsAuthenticated = true;
                CrashReporter.Log($"[NexusAuthService.RevalidateStoredKeyAsync] Network error, keeping stored key — {ex.Message}");
                return true;
            }
            CrashReporter.Log($"[NexusAuthService.RevalidateStoredKeyAsync] Error — {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc />
    public void UnlinkAccount()
    {
        _tokenStore.ClearAll();
        PushApiKeyToClient(null);
        CurrentUser = null;
        IsAuthenticated = false;
        IsPremium = false;

        CrashReporter.Log("[NexusAuthService.UnlinkAccount] Account unlinked, tokens cleared");
    }

    /// <inheritdoc />
    public string? GetBearerToken()
    {
        return _tokenStore.LoadApiKey();
    }

    /// <inheritdoc />
    /// <remarks>Phase 2 stub — returns false.</remarks>
    public Task<bool> StartOAuthFlowAsync()
    {
        CrashReporter.Log("[NexusAuthService.StartOAuthFlowAsync] Phase 2 stub — not implemented");
        return Task.FromResult(false);
    }

    /// <inheritdoc />
    /// <remarks>Phase 2 stub — returns false.</remarks>
    public Task<bool> RefreshTokenIfNeededAsync()
    {
        CrashReporter.Log("[NexusAuthService.RefreshTokenIfNeededAsync] Phase 2 stub — not implemented");
        return Task.FromResult(false);
    }
}
