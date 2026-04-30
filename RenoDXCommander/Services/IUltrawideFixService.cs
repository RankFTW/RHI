using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Fetches ultrawide fix data from multiple sources (Lyall/Codeberg,
/// RoseTheFlower/GitHub, p1xel8ted/GitHub), caches to disk with a
/// 24-hour TTL, and resolves per-game fix URLs with tiered priority.
/// </summary>
public interface IUltrawideFixService
{
    /// <summary>
    /// Fetches all sources (or loads from cache), builds the lookup dictionaries.
    /// Called once at startup.
    /// </summary>
    Task InitAsync();

    /// <summary>
    /// Returns the best ultrawide fix URL for a game, or null if none exists.
    /// Priority: manifest override > Lyall > RoseTheFlower > p1xel8ted.
    /// </summary>
    string? ResolveUrl(string gameName, RemoteManifest? manifest);
}
