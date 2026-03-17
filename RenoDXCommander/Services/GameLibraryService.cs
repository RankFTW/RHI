using System.Text.Json;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public class GameLibraryService : IGameLibraryService
{
    private static readonly string LibraryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RenoDXCommander", "game_library.json");
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public SavedGameLibrary? Load()
    {
        try
        {
            if (!File.Exists(LibraryPath)) return null;
            return JsonSerializer.Deserialize<SavedGameLibrary>(File.ReadAllText(LibraryPath));
        }
        catch (Exception ex) { CrashReporter.Log($"[GameLibraryService.Load] Failed to load game library from '{LibraryPath}' — {ex.Message}"); return null; }
    }

    public void Save(List<DetectedGame> games, Dictionary<string, bool> addonCache,
        HashSet<string> hiddenGames, HashSet<string> favouriteGames, List<DetectedGame> manualGames,
        Dictionary<string, string>? engineTypeCache = null,
        Dictionary<string, string>? resolvedPathCache = null,
        Dictionary<string, string>? addonFileCache = null,
        Dictionary<string, MachineType>? bitnessCache = null)
    {
        var lib = new SavedGameLibrary
        {
            LastScanned    = DateTime.UtcNow,
            AddonScanCache = addonCache,
            HiddenGames    = hiddenGames,
            FavouriteGames = favouriteGames,
            Games = games.Select(g => new SavedGame
            {
                Name = g.Name, InstallPath = g.InstallPath, Source = g.Source
            }).ToList(),
            ManualGames = manualGames.Select(g => new SavedGame
            {
                Name = g.Name, InstallPath = g.InstallPath, Source = "Manual", IsManuallyAdded = true
            }).ToList(),
            EngineTypeCache   = engineTypeCache   ?? new(StringComparer.OrdinalIgnoreCase),
            ResolvedPathCache = resolvedPathCache ?? new(StringComparer.OrdinalIgnoreCase),
            AddonFileCache    = addonFileCache    ?? new(StringComparer.OrdinalIgnoreCase),
            BitnessCache      = bitnessCache      ?? new(StringComparer.OrdinalIgnoreCase),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(LibraryPath)!);
        var json = JsonSerializer.Serialize(lib, JsonOpts);

        // Retry with short delays to handle file contention from concurrent background tasks
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                File.WriteAllText(LibraryPath, json);
                return;
            }
            catch (IOException) when (attempt < 2)
            {
                Thread.Sleep(50 * (attempt + 1));
            }
        }
    }

    public List<DetectedGame> ToDetectedGames(SavedGameLibrary lib) =>
        lib.Games.Select(g => new DetectedGame
        {
            Name = g.Name, InstallPath = g.InstallPath, Source = g.Source
        }).ToList();

    public List<DetectedGame> ToManualGames(SavedGameLibrary lib) =>
        lib.ManualGames.Select(g => new DetectedGame
        {
            Name = g.Name, InstallPath = g.InstallPath, Source = "Manual", IsManuallyAdded = true
        }).ToList();
}
