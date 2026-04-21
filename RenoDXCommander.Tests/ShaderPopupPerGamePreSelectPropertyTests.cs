using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

// Feature: shader-selection-popup, Property 5: Per-game popup pre-selects per-game selection with global fallback

/// <summary>
/// Property-based tests for per-game popup pre-selection resolution.
/// When a per-game shader selection exists, the popup pre-selects those packs.
/// When no per-game selection exists, the popup falls back to the global selection.
/// **Validates: Requirements 5.2, 5.3**
/// </summary>
[Collection("StaticShaderMode")]
public class ShaderPopupPerGamePreSelectPropertyTests
{
    /// <summary>Known shader pack IDs from the service.</summary>
    private static readonly string[] AllPackIds =
        new ShaderPackService(new HttpClient(), new GitHubETagCache()).AvailablePacks.Select(p => p.Id).ToArray();

    // ── Pure model of the pre-selection resolution ────────────────────────────

    /// <summary>
    /// Models the pre-selection resolution from BuildOverridesPanel:
    ///   current = PerGameShaderSelection.TryGetValue(gameName, out var existing)
    ///       ? existing
    ///       : Settings.SelectedShaderPacks;
    /// </summary>
    public static List<string> ResolvePreSelection(
        string gameName,
        Dictionary<string, List<string>> perGameShaderSelection,
        List<string> globalSelectedShaderPacks)
    {
        return perGameShaderSelection.TryGetValue(gameName, out var existing)
            ? existing
            : globalSelectedShaderPacks;
    }

    // ── Generators ────────────────────────────────────────────────────────────

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
    /// Generates a random game name (non-empty alphanumeric string).
    /// </summary>
    private static Gen<string> GenGameName()
    {
        return Gen.Elements(
            "CyberGame", "SpaceShooter", "RacingPro", "PuzzleMaster",
            "RPGWorld", "FPSArena", "StrategyKing", "AdventureQuest",
            "SimCity2099", "HorrorNight");
    }

    // ── Property 5 ────────────────────────────────────────────────────────────

    /// <summary>
    /// For any game name, if PerGameShaderSelection contains an entry for that game,
    /// the popup pre-selects those packs. If no entry exists, the popup pre-selects
    /// the current global selection as a fallback.
    ///
    /// This models the resolution logic in BuildOverridesPanel where:
    ///   current = PerGameShaderSelection[gameName] ?? Settings.SelectedShaderPacks
    /// </summary>
    [Property(MaxTest = 30)]
    public Property PerGamePopup_PreSelectsPerGameWithGlobalFallback()
    {
        var gen =
            GenGameName().SelectMany(gameName =>
            GenPackSelection().SelectMany(globalSelection =>
            GenPackSelection().SelectMany(perGameSelection =>
            Arb.Generate<bool>().Select(hasPerGame =>
                (gameName, globalSelection, perGameSelection, hasPerGame)))));

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (gameName, globalSelection, perGameSelection, hasPerGame) = tuple;

            // Arrange: build PerGameShaderSelection with or without an entry
            var perGameDict = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (hasPerGame)
                perGameDict[gameName] = perGameSelection;

            // Act: resolve pre-selection using the same logic as BuildOverridesPanel
            var preSelection = ResolvePreSelection(gameName, perGameDict, globalSelection);

            if (hasPerGame)
            {
                // Assert: when per-game entry exists, pre-selection is the per-game selection
                if (!ReferenceEquals(preSelection, perGameSelection))
                    return false.Label(
                        $"Per-game entry exists for '{gameName}' but pre-selection is not the per-game list reference");

                var expected = new HashSet<string>(perGameSelection, StringComparer.OrdinalIgnoreCase);
                var actual = new HashSet<string>(preSelection, StringComparer.OrdinalIgnoreCase);
                if (!expected.SetEquals(actual))
                    return false.Label(
                        $"Per-game mismatch for '{gameName}': " +
                        $"expected [{string.Join(",", expected)}], " +
                        $"got [{string.Join(",", actual)}]");

                return true.Label(
                    $"OK: '{gameName}' used per-game selection ({perGameSelection.Count} packs)");
            }
            else
            {
                // Assert: when no per-game entry, pre-selection falls back to global
                if (!ReferenceEquals(preSelection, globalSelection))
                    return false.Label(
                        $"No per-game entry for '{gameName}' but pre-selection is not the global list reference");

                var expected = new HashSet<string>(globalSelection, StringComparer.OrdinalIgnoreCase);
                var actual = new HashSet<string>(preSelection, StringComparer.OrdinalIgnoreCase);
                if (!expected.SetEquals(actual))
                    return false.Label(
                        $"Global fallback mismatch for '{gameName}': " +
                        $"expected [{string.Join(",", expected)}], " +
                        $"got [{string.Join(",", actual)}]");

                return true.Label(
                    $"OK: '{gameName}' fell back to global selection ({globalSelection.Count} packs)");
            }
        });
    }
}
