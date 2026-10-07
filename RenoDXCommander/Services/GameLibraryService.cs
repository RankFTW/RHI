using System.Text.Json;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public class GameLibraryService : IGameLibraryService
{
    private static readonly string LibraryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "game_library.json");
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public SavedGameLibrary? Load()
    {
        try
        {
            if (!File.Exists(LibraryPath)) return null;
            var lib = JsonSerializer.Deserialize<SavedGameLibrary>(File.ReadAllText(LibraryPath));
            if (lib != null)
                MigrateLegacyKeys(lib);
            return lib;
        }
        catch (Exception ex) { CrashReporter.Log($"[GameLibraryService.Load] Failed to load game library from '{LibraryPath}' — {ex.Message}"); return null; }
    }

    /// <summary>
    /// Migrates legacy game-name keys (without "|") to composite key format ("GameName|Store").
    /// Legacy keys are migrated to "GameName|" (empty store) since we don't know the original store.
    /// </summary>
    private static void MigrateLegacyKeys(SavedGameLibrary lib)
    {
        // Migrate HashSets
        lib.DxvkEnabledGames = MigrateHashSet(lib.DxvkEnabledGames);
        lib.ExcludeFromUpdateAllDxvk = MigrateHashSet(lib.ExcludeFromUpdateAllDxvk);
        lib.HiddenGames = MigrateHashSet(lib.HiddenGames);
        lib.FavouriteGames = MigrateHashSet(lib.FavouriteGames);

        // Migrate Dictionaries
        lib.DxvkInstalledVersions = MigrateDict(lib.DxvkInstalledVersions);
        lib.UpdateAvailableSnapshot = MigrateDict(lib.UpdateAvailableSnapshot);
        lib.DlssPathsCache = MigrateDict(lib.DlssPathsCache);
        if (lib.RsInstalledVersions != null)
            lib.RsInstalledVersions = MigrateDict(lib.RsInstalledVersions);
        if (lib.RdxInstalledVersions != null)
            lib.RdxInstalledVersions = MigrateDict(lib.RdxInstalledVersions);

        // Migrate LastSelectedGame to composite format
        if (!string.IsNullOrEmpty(lib.LastSelectedGame) && !lib.LastSelectedGame.Contains('|'))
            lib.LastSelectedGame = $"{lib.LastSelectedGame}|";
    }

    private static HashSet<string> MigrateHashSet(HashSet<string>? set)
    {
        if (set == null) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return new HashSet<string>(
            set.Select(k => k.Contains('|') ? k : $"{k}|"),
            StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, T> MigrateDict<T>(Dictionary<string, T>? dict)
    {
        if (dict == null) return new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        return dict.ToDictionary(
            kv => kv.Key.Contains('|') ? kv.Key : $"{kv.Key}|",
            kv => kv.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    public void Save(List<DetectedGame> games, Dictionary<string, bool> addonCache,
        HashSet<string> hiddenGames, HashSet<string> favouriteGames, List<DetectedGame> manualGames,
        Dictionary<string, string>? engineTypeCache = null,
        Dictionary<string, string>? resolvedPathCache = null,
        Dictionary<string, string>? addonFileCache = null,
        Dictionary<string, MachineType>? bitnessCache = null,
        string? lastSelectedGame = null,
        HashSet<string>? dxvkEnabledGames = null,
        Dictionary<string, string>? dxvkInstalledVersions = null,
        HashSet<string>? excludeFromUpdateAllDxvk = null,
        Dictionary<string, string>? updateAvailableSnapshot = null,
        Dictionary<string, DlssPathCache>? dlssPathsCache = null,
        Dictionary<string, string>? rsInstalledVersions = null,
        Dictionary<string, string>? rdxInstalledVersions = null)
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
            LastSelectedGame  = lastSelectedGame,
            DxvkEnabledGames        = dxvkEnabledGames        ?? new(StringComparer.OrdinalIgnoreCase),
            DxvkInstalledVersions   = dxvkInstalledVersions   ?? new(StringComparer.OrdinalIgnoreCase),
            ExcludeFromUpdateAllDxvk = excludeFromUpdateAllDxvk ?? new(StringComparer.OrdinalIgnoreCase),
            UpdateAvailableSnapshot = updateAvailableSnapshot ?? new(StringComparer.OrdinalIgnoreCase),
            DlssPathsCache          = dlssPathsCache          ?? new(StringComparer.OrdinalIgnoreCase),
            RsInstalledVersions     = rsInstalledVersions,
            RdxInstalledVersions    = rdxInstalledVersions,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(LibraryPath)!);
        var json = JsonSerializer.Serialize(lib, JsonOpts);

        FileHelper.WriteAllTextAtomic(LibraryPath, json, "GameLibraryService.Save");
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
