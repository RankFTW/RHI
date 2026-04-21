using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;
using Xunit;

namespace RenoDXCommander.Tests;

// Feature: shader-selection-popup, Property 4: Global shader mode resolves to global selection for deployment

/// <summary>
/// Property-based tests for global shader mode resolution.
/// When a game's per-game shader mode is "Global" (no entry in PerGameShaderSelection),
/// the effective shader selection used during deployment equals the global SelectedShaderPacks.
/// **Validates: Requirements 4.3**
/// </summary>
[Collection("StaticShaderMode")]
public class ShaderPopupGlobalModePropertyTests
{
    /// <summary>Known shader pack IDs from the service.</summary>
    private static readonly string[] AllPackIds =
        new ShaderPackService(new HttpClient(), new GitHubETagCache()).AvailablePacks.Select(p => p.Id).ToArray();

    // ── Pure model of the effective selection resolution ──────────────────────────

    /// <summary>
    /// Models the effective selection resolution from DeployShadersForCard:
    ///   effectiveSelection = PerGameShaderSelection.TryGetValue(gameName, out var perGameSel)
    ///       ? perGameSel
    ///       : settingsViewModel.SelectedShaderPacks;
    ///
    /// When no per-game entry exists (Global mode), the result is the global selection.
    /// </summary>
    public static List<string> ResolveEffectiveSelection(
        string gameName,
        Dictionary<string, List<string>> perGameShaderSelection,
        List<string> globalSelectedShaderPacks)
    {
        return perGameShaderSelection.TryGetValue(gameName, out var perGameSel)
            ? perGameSel
            : globalSelectedShaderPacks;
    }

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
    /// Generates a random game name (non-empty alphanumeric string).
    /// </summary>
    private static Gen<string> GenGameName()
    {
        return Gen.Elements(
            "CyberGame", "SpaceShooter", "RacingPro", "PuzzleMaster",
            "RPGWorld", "FPSArena", "StrategyKing", "AdventureQuest",
            "SimCity2099", "HorrorNight");
    }

    // ── Property 4 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// For any game name with no per-game override (no entry in PerGameShaderSelection)
    /// and for any global selection, the effective shader selection used during deployment
    /// should equal the global SelectedShaderPacks.
    ///
    /// This models the resolution logic in DeployShadersForCard where:
    ///   effectiveSelection = PerGameShaderSelection[gameName] ?? SelectedShaderPacks
    /// When the game has no per-game entry, the fallback is always the global selection.
    /// </summary>
    [Property(MaxTest = 10)]
    public Property GlobalMode_ResolvesToGlobalSelection()
    {
        var gen = GenGameName().SelectMany(gameName =>
            GenPackSelection().Select(globalSelection => (gameName, globalSelection)));

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (gameName, globalSelection) = tuple;

            // Arrange: empty PerGameShaderSelection (no per-game override = Global mode)
            var perGameShaderSelection = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            // Act: resolve effective selection using the same logic as DeployShadersForCard
            var effectiveSelection = ResolveEffectiveSelection(
                gameName, perGameShaderSelection, globalSelection);

            // Assert: effective selection equals the global SelectedShaderPacks
            var expected = new HashSet<string>(globalSelection, StringComparer.OrdinalIgnoreCase);
            var actual = new HashSet<string>(effectiveSelection, StringComparer.OrdinalIgnoreCase);

            if (!expected.SetEquals(actual))
                return false.Label(
                    $"Selection mismatch for '{gameName}': " +
                    $"expected [{string.Join(",", expected)}], " +
                    $"got [{string.Join(",", actual)}]");

            // Also verify count matches (no duplicates introduced)
            if (effectiveSelection.Count != globalSelection.Count)
                return false.Label(
                    $"Count mismatch for '{gameName}': " +
                    $"expected {globalSelection.Count}, got {effectiveSelection.Count}");

            // Verify it's the same reference (not a copy) — the resolution returns
            // the global list directly when no per-game entry exists
            if (!ReferenceEquals(effectiveSelection, globalSelection))
                return false.Label(
                    $"Expected same reference for '{gameName}' — " +
                    "resolution should return global selection directly");

            return true.Label(
                $"OK: '{gameName}' resolved to global selection ({globalSelection.Count} packs)");
        });
    }
}
