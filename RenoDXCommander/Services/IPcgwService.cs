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

    /// <summary>
    /// Removes all negative sentinel (-1) entries from the cache so games that
    /// previously had no Steam AppID or PCGW page are retried on the next launch.
    /// Called on Full Refresh.
    /// </summary>
    void ClearNegativeCache();

    /// <summary>
    /// Checks the manifest-supplied cache version and wipes the URL cache if the manifest
    /// requests a higher version. Call after each manifest fetch.
    /// </summary>
    void CheckManifestCacheVersion(RenoDXCommander.Models.RemoteManifest? manifest);

    /// <summary>Loads the PCGW API info cache from disk.</summary>
    Task LoadApiCacheAsync();

    /// <summary>
    /// Returns the cached API info for a game, or null if not yet scraped.
    /// </summary>
    PcgwApiInfo? GetCachedApiInfo(string gameName);

    /// <summary>
    /// Fetches and scrapes the PCGW wiki page for a game to extract API support info.
    /// Only fetches if the wiki URL is already resolved — never triggers a new URL lookup.
    /// Caches results in memory and disk.
    /// </summary>
    Task<PcgwApiInfo?> FetchApiInfoAsync(string gameName, string wikiUrl);
}
