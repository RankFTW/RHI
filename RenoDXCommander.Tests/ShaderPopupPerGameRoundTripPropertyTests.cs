using System.Text.Json;
using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

// Feature: shader-selection-popup, Property 10: Per-game shader selection persistence round-trip

/// <summary>
/// Property-based tests for per-game shader selection persistence round-trip.
/// For any dictionary of game names to shader pack ID lists stored in
/// GameNameService.PerGameShaderSelection, saving via SaveNameMappings and
/// then loading via LoadNameMappings should produce an equivalent dictionary.
/// **Validates: Requirements 8.2, 8.4**
/// </summary>
[Collection("StaticShaderMode")]
public class ShaderPopupPerGameRoundTripPropertyTests
{
    /// <summary>Known shader pack IDs from the service.</summary>
    private static readonly string[] AllPackIds =
        new ShaderPackService(new HttpClient(), new GitHubETagCache()).AvailablePacks.Select(p => p.Id).ToArray();

    // ── Generators ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a random subset of pack IDs (including possibly empty).
    /// </summary>
    private static Gen<List<string>> GenPackSelection()
    {
        if (AllPackIds.Length == 0)
            return Gen.Constant(new List<string>());

        return Gen.ListOf(AllPackIds.Length, Arb.Generate<bool>())
            .Select(flags =>
            {
                var subset = new List<string>();
                for (int i = 0; i < AllPackIds.Length; i++)
                    if (flags[i]) subset.Add(AllPackIds[i]);
                return subset;
            });
    }

    /// <summary>
    /// Generates a safe game name: non-empty alphanumeric string that is valid
    /// as a dictionary key and survives JSON round-trip.
    /// </summary>
    private static Gen<string> GenGameName()
    {
        return Gen.Elements(
            "CyberGame", "SpaceShooter", "RacingPro", "PuzzleMaster",
            "RPGWorld", "FPSArena", "StrategyKing", "PlatformJump",
            "HorrorNight", "SimCity2K", "AdventureQuest", "SportsChamp");
    }

    /// <summary>
    /// Generates a random dictionary of game name → pack ID lists (0 to 6 entries).
    /// Uses distinct game names to avoid key collisions.
    /// </summary>
    private static Gen<Dictionary<string, List<string>>> GenPerGameSelection()
    {
        return Gen.Choose(0, 6).SelectMany(count =>
        {
            if (count == 0)
                return Gen.Constant(new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase));

            // Generate 'count' distinct game names paired with pack selections
            return Gen.ArrayOf(count, GenGameName().SelectMany(name => GenPackSelection().Select(packs => (name, packs))))
                .Select(pairs =>
                {
                    var dict = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                    foreach (var (name, packs) in pairs)
                        dict[name] = packs; // last-write-wins for duplicate names
                    return dict;
                });
        });
    }

    // ── Property 10 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// For any dictionary of game name → pack ID lists, serializing via
    /// JsonSerializer.Serialize (as SaveNameMappings does) and then deserializing
    /// via JsonSerializer.Deserialize (as LoadNameMappings does) should produce
    /// an equivalent dictionary.
    /// </summary>
    [Property(MaxTest = 30)]
    public Property RoundTrip_PerGameShaderSelection()
    {
        return Prop.ForAll(GenPerGameSelection().ToArbitrary(), originalDict =>
        {
            // Act: serialize (same as SaveNameMappings does for PerGameShaderSelection)
            var json = JsonSerializer.Serialize(originalDict);

            // Act: deserialize (same as LoadNameMappings does for PerGameShaderSelection)
            var loaded = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json);
            if (loaded == null)
                return false.Label("Deserialized dictionary was null");

            // Rebuild with case-insensitive comparer (as LoadNameMappings does)
            var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in loaded)
                result[kv.Key] = kv.Value;

            // Assert: same number of game entries
            if (originalDict.Count != result.Count)
                return false.Label(
                    $"Entry count mismatch: expected {originalDict.Count}, got {result.Count}");

            // Assert: each game entry has matching pack lists
            foreach (var kv in originalDict)
            {
                if (!result.TryGetValue(kv.Key, out var loadedPacks))
                    return false.Label($"Missing game key: {kv.Key}");

                var expectedSet = new HashSet<string>(kv.Value, StringComparer.OrdinalIgnoreCase);
                var actualSet = new HashSet<string>(loadedPacks, StringComparer.OrdinalIgnoreCase);

                if (!expectedSet.SetEquals(actualSet))
                    return false.Label(
                        $"Pack mismatch for '{kv.Key}': " +
                        $"expected [{string.Join(",", expectedSet)}], " +
                        $"got [{string.Join(",", actualSet)}]");

                if (kv.Value.Count != loadedPacks.Count)
                    return false.Label(
                        $"Pack count mismatch for '{kv.Key}': " +
                        $"expected {kv.Value.Count}, got {loadedPacks.Count}");
            }

            return true.Label(
                $"OK: round-trip preserved {originalDict.Count} game entries");
        });
    }
}
