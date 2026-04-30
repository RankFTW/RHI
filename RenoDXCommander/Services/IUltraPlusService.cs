using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Fetches the Ultra+ game list from theultraplace.com, caches to disk
/// with a 24-hour TTL, and resolves per-game Ultra+ page URLs.
/// </summary>
public interface IUltraPlusService
{
    /// <summary>
    /// Fetches the games page (or loads from cache), builds the lookup dictionary.
    /// Called once at startup.
    /// </summary>
    Task InitAsync();

    /// <summary>
    /// Returns the Ultra+ page URL for a game, or null if none exists.
    /// Checks manifest overrides first, then the normalized-name dictionary.
    /// </summary>
    string? ResolveUrl(string gameName, RemoteManifest? manifest);
}
