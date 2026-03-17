using RenoDXCommander.Services;

namespace RenoDXCommander.Models;

public class SavedGameLibrary
{
    public DateTime LastScanned { get; set; }
    public List<SavedGame> Games { get; set; } = new();
    public Dictionary<string, bool> AddonScanCache { get; set; } = new();
    public HashSet<string> HiddenGames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> FavouriteGames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<SavedGame> ManualGames { get; set; } = new();
    /// <summary>Maps rootPath (lower) → engine type name ("Unreal", "Unity", etc.).</summary>
    public Dictionary<string, string> EngineTypeCache { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Maps rootPath (lower) → resolved install path after engine detection.</summary>
    public Dictionary<string, string> ResolvedPathCache { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Maps resolvedPath (lower) → addon filename on disk (empty string = none found).</summary>
    public Dictionary<string, string> AddonFileCache { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Maps resolvedPath (lower) → detected PE MachineType. Populated during game library scan, cleared on rescan.</summary>
    public Dictionary<string, MachineType> BitnessCache { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
