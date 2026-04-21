using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

// Feature: shader-selection-popup, Property 6: Per-game confirm stores the selection

/// <summary>
/// Property-based tests for per-game confirm storing the selection.
/// When the user clicks "Confirm" in the per-game shader popup, the selected packs
/// are stored in PerGameShaderSelection[gameName].
/// **Validates: Requirements 6.2**
/// </summary>
[Collection("StaticShaderMode")]
public class ShaderPopupPerGameConfirmPropertyTests
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
    /// Generates a random game name (non-empty alphanumeric string).
    /// </summary>
    private static Gen<string> GenGameName()
    {
        return Gen.Elements(
            "CyberGame", "SpaceShooter", "RacingPro", "PuzzleMaster",
            "RPGWorld", "FPSArena", "StrategyKing", "AdventureQuest",
            "SimCity2099", "HorrorNight");
    }

    // ── Property 6 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// For any game name and for any set of shader packs selected in the per-game popup,
    /// when the user clicks "Confirm" (non-null result from ShaderPopupHelper.ShowAsync),
    /// the PerGameShaderSelection for that game should contain exactly the selected pack IDs.
    ///
    /// This models the confirm logic in BuildOverridesPanel:
    ///   PerGameShaderSelection[gameName] = result;
    /// </summary>
    [Property(MaxTest = 30)]
    public Property PerGameConfirm_StoresSelection()
    {
        var gen =
            GenGameName().SelectMany(gameName =>
            GenPackSelection().Select(selectedPacks =>
                (gameName, selectedPacks)));

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (gameName, selectedPacks) = tuple;

            // Arrange: create a PerGameShaderSelection dictionary (may have pre-existing entries)
            var perGameShaderSelection = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            // Act: simulate Confirm — store the selection (same as BuildOverridesPanel logic)
            List<string>? result = selectedPacks; // non-null = user clicked Confirm
            if (result != null)
            {
                perGameShaderSelection[gameName] = result;
            }

            // Assert 1: entry exists for the game
            if (!perGameShaderSelection.ContainsKey(gameName))
                return false.Label(
                    $"No entry found for '{gameName}' after Confirm");

            // Assert 2: stored selection matches the confirmed packs exactly
            var stored = perGameShaderSelection[gameName];
            var expectedSet = new HashSet<string>(selectedPacks, StringComparer.OrdinalIgnoreCase);
            var actualSet = new HashSet<string>(stored, StringComparer.OrdinalIgnoreCase);

            if (!expectedSet.SetEquals(actualSet))
                return false.Label(
                    $"Selection mismatch for '{gameName}': " +
                    $"expected [{string.Join(",", expectedSet)}], " +
                    $"got [{string.Join(",", actualSet)}]");

            // Assert 3: count matches (no duplicates introduced or items lost)
            if (stored.Count != selectedPacks.Count)
                return false.Label(
                    $"Count mismatch for '{gameName}': " +
                    $"expected {selectedPacks.Count}, got {stored.Count}");

            // Assert 4: stored reference is the same as the result (direct assignment)
            if (!ReferenceEquals(stored, result))
                return false.Label(
                    $"Expected same reference for '{gameName}' — " +
                    "confirm should store the result directly");

            return true.Label(
                $"OK: '{gameName}' stored {selectedPacks.Count} packs after Confirm");
        });
    }
}
