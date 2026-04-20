using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Services;

/// <summary>
/// Orchestrates update detection across all resolvable mods by querying
/// the Nexus Mods API and comparing remote versions against local installs.
/// </summary>
public interface INexusUpdateChecker
{
    /// <summary>
    /// Checks all resolvable mods for updates. Updates GameCardViewModel properties.
    /// Skips mods where NexusUrl is null or unparseable.
    /// Stops on HTTP 429 (rate limit).
    /// </summary>
    /// <param name="cards">The game cards to check for updates.</param>
    /// <param name="bypassCache">When true, forces re-query regardless of cache TTL.</param>
    Task CheckAllAsync(IEnumerable<GameCardViewModel> cards, bool bypassCache = false,
        Microsoft.UI.Dispatching.DispatcherQueue? dispatcherQueue = null);

    /// <summary>
    /// Checks a single mod for updates.
    /// </summary>
    /// <param name="gameDomain">The Nexus Mods game domain slug.</param>
    /// <param name="modId">The Nexus Mods mod ID.</param>
    /// <returns>The update result, or null if the check failed.</returns>
    Task<NexusUpdateResult?> CheckSingleAsync(string gameDomain, int modId);

    /// <summary>
    /// Clears the in-memory update cache.
    /// </summary>
    void ClearCache();
}
