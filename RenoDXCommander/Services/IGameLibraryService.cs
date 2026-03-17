using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Loads and saves the local game library cache.
/// </summary>
public interface IGameLibraryService
{
    SavedGameLibrary? Load();

    void Save(List<DetectedGame> games, Dictionary<string, bool> addonCache,
        HashSet<string> hiddenGames, HashSet<string> favouriteGames, List<DetectedGame> manualGames,
        Dictionary<string, string>? engineTypeCache = null,
        Dictionary<string, string>? resolvedPathCache = null,
        Dictionary<string, string>? addonFileCache = null,
        Dictionary<string, MachineType>? bitnessCache = null);

    List<DetectedGame> ToDetectedGames(SavedGameLibrary lib);

    List<DetectedGame> ToManualGames(SavedGameLibrary lib);
}
