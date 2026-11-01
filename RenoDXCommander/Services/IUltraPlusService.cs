using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Fetches the Ultra+ game list from theultraplace.com, caches to disk
/// with a 4-hour TTL, and resolves per-game Ultra+ page URLs.
/// </summary>
public interface IUltraPlusService
{
    /// <summary>
    /// Fetches the games page (or loads from cache), builds the lookup dictionary.
    /// <paramref name="forceRefresh"/> skips the cache age check (Refresh button).
    /// </summary>
    Task InitAsync(bool forceRefresh = false);

    /// <summary>
    /// Returns the Ultra+ page URL for a game, or null if none exists.
    /// Checks manifest overrides first, then the normalized-name dictionary.
    /// </summary>
    string? ResolveUrl(string gameName, RemoteManifest? manifest);

    /// <summary>
    /// Returns all game names in the Ultra+ dictionary (for new mods detection).
    /// </summary>
    IReadOnlyCollection<string> GetAllGameNames();
}
