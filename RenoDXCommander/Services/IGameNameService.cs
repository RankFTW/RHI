using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Services;

/// <summary>
/// Defines the contract for game name mappings, renames, wiki exclusions,
/// and settings persistence. Manages all game-name-keyed data stores.
/// </summary>
public interface IGameNameService
{
    // ── Public accessors ──────────────────────────────────────────────────────

    /// <summary>Maps detected game names to wiki keys.</summary>
    Dictionary<string, string> NameMappings { get; }

    /// <summary>Maps install-path keys to renamed game names.</summary>
    Dictionary<string, string> GameRenames { get; }

    /// <summary>Games excluded from wiki lookups.</summary>
    HashSet<string> WikiExclusions { get; }

    /// <summary>Games hidden from the main game list.</summary>
    HashSet<string> HiddenGames { get; }

    /// <summary>Games marked as favourites.</summary>
    HashSet<string> FavouriteGames { get; }

    /// <summary>Games with UE extended mode enabled.</summary>
    HashSet<string> UeExtendedGames { get; }

    /// <summary>Games excluded from batch ReShade updates.</summary>
    HashSet<string> UpdateAllExcludedReShade { get; }

    /// <summary>Games excluded from batch RenoDX updates.</summary>
    HashSet<string> UpdateAllExcludedRenoDx { get; }

    /// <summary>Games excluded from batch ReLimiter updates.</summary>
    HashSet<string> UpdateAllExcludedUl { get; }

    /// <summary>Games excluded from batch Display Commander updates.</summary>
    HashSet<string> UpdateAllExcludedDc { get; }

    /// <summary>Per-game shader mode settings.</summary>
    Dictionary<string, string> PerGameShaderMode { get; }

    /// <summary>Per-game shader pack selections for Select mode. Key = game name, Value = list of selected pack IDs.</summary>
    Dictionary<string, List<string>> PerGameShaderSelection { get; }

    /// <summary>Per-game addon mode settings. Key = game name, Value = "Global" or "Select".</summary>
    Dictionary<string, string> PerGameAddonMode { get; }

    /// <summary>Per-game addon selections for Select mode. Key = game name, Value = list of selected addon PackageNames.</summary>
    Dictionary<string, List<string>> PerGameAddonSelection { get; }

    /// <summary>Games with Luma explicitly enabled.</summary>
    HashSet<string> LumaEnabledGames { get; }

    /// <summary>Games with Luma explicitly disabled.</summary>
    HashSet<string> LumaDisabledGames { get; }

    /// <summary>Per-game install folder overrides.</summary>
    Dictionary<string, string> FolderOverrides { get; }

    /// <summary>Per-game Vulkan rendering path preferences. Key = game name, Value = "DirectX" or "Vulkan".</summary>
    Dictionary<string, string> VulkanRenderingPaths { get; }

    /// <summary>Per-game bitness overrides. Key = game name, Value = "32" or "64". Absent = auto-detect.</summary>
    Dictionary<string, string> BitnessOverrides { get; }

    /// <summary>Per-game API overrides. Key = game name, Value = list of GraphicsApiType names that are ON. Absent = auto-detect.</summary>
    Dictionary<string, List<string>> ApiOverrides { get; }

    /// <summary>Maps current (renamed) game name to original store-detected name.</summary>
    Dictionary<string, string> OriginalDetectedNames { get; }

    // ── Load / Save ───────────────────────────────────────────────────────────

    /// <summary>
    /// Loads all name mappings and settings from the persisted settings file.
    /// Returns the loaded settings dictionary for further processing by callers.
    /// </summary>
    Dictionary<string, string> LoadNameMappings(
        IDllOverrideService dllOverrideService,
        SettingsViewModel settingsViewModel,
        Action<bool> setIsGridLayout,
        Action<string> setFilterMode,
        Action<List<CustomFilter>> setCustomFilters);

    /// <summary>Persists all settings to disk.</summary>
    void SaveNameMappings(
        IDllOverrideService dllOverrideService,
        SettingsViewModel settingsViewModel,
        bool isGridLayout,
        bool isLoadingSettings,
        string filterMode,
        List<CustomFilter> customFilters);

    // ── Name mapping CRUD ─────────────────────────────────────────────────────

    /// <summary>Adds or updates a name mapping from detected name to wiki key.</summary>
    void AddNameMapping(string detectedName, string wikiKey);

    /// <summary>Returns the wiki key for a detected name, or null if not mapped.</summary>
    string? GetNameMapping(string detectedName);

    /// <summary>Returns the user-set name mapping only (excludes manifest-injected mappings).</summary>
    string? GetUserNameMapping(string detectedName);

    /// <summary>Marks a name mapping key as manifest-origin (not user-set).</summary>
    void MarkManifestNameMapping(string key);

    /// <summary>Removes the name mapping for a detected name (including normalized matches).</summary>
    void RemoveNameMapping(string detectedName);

    // ── Game renames ──────────────────────────────────────────────────────────

    /// <summary>
    /// Renames a game everywhere: card, detected game, all settings HashSets/Dicts,
    /// persisted install records, and library file.
    /// </summary>
    void RenameGame(string oldName, string newName,
        List<GameCardViewModel> allCards,
        List<DetectedGame> manualGames,
        IDllOverrideService dllOverrideService);

    /// <summary>Returns the original store-detected name for a renamed game, or null.</summary>
    string? GetOriginalStoreName(string currentName);

    /// <summary>Removes rename entries for a game.</summary>
    void RemoveGameRename(string gameName, List<GameCardViewModel> allCards);

    // ── Apply methods ─────────────────────────────────────────────────────────

    /// <summary>Applies persisted game renames to a list of detected games.</summary>
    void ApplyGameRenames(List<DetectedGame> games);

    /// <summary>Applies persisted folder overrides to a list of detected games.</summary>
    void ApplyFolderOverrides(List<DetectedGame> games);

    /// <summary>Clears the original-detected-names tracking dictionary.</summary>
    void ClearOriginalDetectedNames();
}
