using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Services;

/// <summary>
/// Defines the contract for game detection orchestration, deduplication,
/// and manifest application. Owns the pipeline from raw store scans
/// through to fully enriched game lists.
/// </summary>
public interface IGameInitializationService
{
    /// <summary>
    /// Detects games from all supported stores and deduplicates by name and install path.
    /// </summary>
    List<DetectedGame> DetectAllGamesDeduped();

    /// <summary>
    /// Seeds the working sets with values from the remote manifest.
    /// Local user overrides always take priority.
    /// Must be called BEFORE BuildCards.
    /// </summary>
    void ApplyManifest(
        RemoteManifest? manifest,
        IGameNameService gameNameService,
        IDllOverrideService dllOverrideService,
        HashSet<string> manifestNativeHdrGames,
        HashSet<string> manifestBlacklist,
        HashSet<string> manifest32BitGames,
        HashSet<string> manifest64BitGames,
        Dictionary<string, string> manifestEngineOverrides,
        Dictionary<string, ManifestDllNames> manifestDllNameOverrides,
        HashSet<string> manifestWikiUnlinks,
        Dictionary<string, string> installPathOverrides,
        Func<string, string> normalizeForLookup);

    /// <summary>
    /// Applies wiki status overrides from the remote manifest to the fetched mod list.
    /// </summary>
    void ApplyManifestStatusOverrides(RemoteManifest? manifest, List<GameMod> allMods);
}
