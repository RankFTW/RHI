using System.Text.Json;
using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for GameLibraryService save/load round-trip.
/// Feature: codebase-optimization, Property 5: GameLibrary save/load round-trip
/// </summary>
public class GameLibraryRoundTripTests
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    // ── Generators ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a safe non-null string suitable for game names and paths.
    /// Avoids null and control characters that could break JSON round-trip.
    /// </summary>
    private static readonly Gen<string> GenSafeString =
        from segments in Gen.NonEmptyListOf(
            Gen.Elements(
                "Cyberpunk 2077", "Elden Ring", "Starfield", "Hogwarts Legacy",
                "Alan Wake 2", "Baldur's Gate 3", "Final Fantasy XVI",
                "The Witcher 3", "Red Dead Redemption 2", "Halo Infinite",
                "Doom Eternal", "Resident Evil 4", "God of War",
                "Death Stranding", "Control", "Returnal", "Sifu"))
        select segments.First();

    private static readonly Gen<string> GenInstallPath =
        from drive in Gen.Elements("C", "D", "E", "F")
        from count in Gen.Choose(1, 4)
        from segments in Gen.ArrayOf(count,
            Gen.Elements("Games", "SteamLibrary", "common", "Program Files",
                         "GOG Galaxy", "Epic Games", "Xbox", "EA Games",
                         "Ubisoft", "Rockstar Games", "Battle.net"))
        from gameName in Gen.Elements(
            "Cyberpunk 2077", "EldenRing", "Starfield", "HogwartsLegacy",
            "AlanWake2", "BaldursGate3", "FinalFantasyXVI", "Witcher3")
        select $@"{drive}:\{string.Join(@"\", segments)}\{gameName}";

    private static readonly Gen<string> GenSource =
        Gen.Elements("Steam", "GOG", "Epic", "EA", "Xbox", "Ubisoft", "Battle.net", "Rockstar");

    private static readonly Gen<DetectedGame> GenDetectedGame =
        from name in GenSafeString
        from path in GenInstallPath
        from source in GenSource
        select new DetectedGame { Name = name, InstallPath = path, Source = source };

    private static readonly Gen<List<DetectedGame>> GenGameList =
        from count in Gen.Choose(0, 20)
        from games in Gen.ListOf(count, GenDetectedGame)
        select games.ToList();

    // ── Property 5: GameLibrary save/load round-trip ──────────────────────────────
    // Feature: codebase-optimization, Property 5: GameLibrary save/load round-trip
    // **Validates: Requirements 10.4**
    [Property(MaxTest = 100)]
    public Property SaveAndLoad_RoundTrip_PreservesGameNamesAndInstallPaths()
    {
        return Prop.ForAll(
            Arb.From(GenGameList),
            (List<DetectedGame> games) =>
            {
                // Build the SavedGameLibrary the same way GameLibraryService.Save does
                var lib = new SavedGameLibrary
                {
                    LastScanned = DateTime.UtcNow,
                    AddonScanCache = new Dictionary<string, bool>(),
                    HiddenGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    FavouriteGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    Games = games.Select(g => new SavedGame
                    {
                        Name = g.Name,
                        InstallPath = g.InstallPath,
                        Source = g.Source
                    }).ToList(),
                    ManualGames = new List<SavedGame>(),
                };

                // Serialize to JSON (same as Save) and deserialize (same as Load)
                var json = JsonSerializer.Serialize(lib, JsonOpts);
                var loaded = JsonSerializer.Deserialize<SavedGameLibrary>(json);

                if (loaded is null)
                    return false.Label("Deserialized library was null");

                // Use ToDetectedGames the same way the service does
                var service = new GameLibraryService();
                var roundTripped = service.ToDetectedGames(loaded);

                if (roundTripped.Count != games.Count)
                    return false.Label(
                        $"Count mismatch: original={games.Count}, roundTripped={roundTripped.Count}");

                for (int i = 0; i < games.Count; i++)
                {
                    if (roundTripped[i].Name != games[i].Name)
                        return false.Label(
                            $"Name mismatch at index {i}: original='{games[i].Name}', roundTripped='{roundTripped[i].Name}'");

                    if (roundTripped[i].InstallPath != games[i].InstallPath)
                        return false.Label(
                            $"InstallPath mismatch at index {i}: original='{games[i].InstallPath}', roundTripped='{roundTripped[i].InstallPath}'");
                }

                return true.Label("Round-trip preserved all game names and install paths");
            });
    }
}
