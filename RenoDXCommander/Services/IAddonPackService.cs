using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Manages addon lifecycle: fetch, parse, download, update, deploy.
/// Mirrors ShaderPackService pattern.
/// </summary>
public interface IAddonPackService
{
    /// <summary>Available addon entries parsed from Addons.ini.</summary>
    IReadOnlyList<AddonEntry> AvailablePacks { get; }

    /// <summary>Fetches Addons.ini, parses, and caches. Called on startup.</summary>
    Task EnsureLatestAsync();

    /// <summary>
    /// Downloads an addon to the staging area. Handles .addon/.zip formats.
    /// When <paramref name="versionOverride"/> is provided, it is used as the stored
    /// version token instead of re-resolving via HEAD request (avoids ETag drift
    /// for /latest/ redirect URLs).
    /// </summary>
    Task DownloadAddonAsync(AddonEntry entry, IProgress<(string msg, double pct)>? progress = null, string? versionOverride = null);

    /// <summary>Checks if an addon is already downloaded in the staging area.</summary>
    bool IsDownloaded(string packageName);

    /// <summary>Returns the list of addon package names currently in the staging area.</summary>
    IReadOnlyList<string> DownloadedAddonNames { get; }

    /// <summary>Checks all downloaded addons for updates and downloads newer versions.</summary>
    Task CheckAndUpdateAllAsync();

    /// <summary>
    /// Removes a downloaded addon from the staging area and version tracking.
    /// </summary>
    void RemoveAddon(string packageName);

    /// <summary>
    /// Deploys addons for a specific game based on its selection (global or per-game).
    /// Copies correct bitness variant. Removes stale addon files.
    /// </summary>
    void DeployAddonsForGame(string gameName, string installPath, bool is32Bit,
        bool useGlobalSet, List<string>? perGameSelection);
}
