using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Resolves PCGamingWiki URLs for detected games via Steam AppID
/// or OpenSearch fallback, with manifest override support.
/// </summary>
public interface IPcgwService
{
    /// <summary>
    /// Loads the AppID cache from disk. Called once at startup.
    /// </summary>
    Task LoadCacheAsync();

    /// <summary>
    /// Resolves the PCGW URL for a game, checking:
    /// 1. Manifest pcgwUrlOverrides (highest priority)
    /// 2. Steam AppID → appid.php redirect URL
    /// 3. OpenSearch fallback (for games with no AppID)
    /// Returns null if unresolvable.
    /// </summary>
    Task<string?> ResolveUrlAsync(string gameName, int? steamAppId, string installPath, RemoteManifest? manifest);

    /// <summary>
    /// Forces an immediate write of the AppID cache to disk.
    /// Call during app shutdown to ensure pending debounced writes are flushed.
    /// </summary>
    Task FlushCacheAsync();
}
