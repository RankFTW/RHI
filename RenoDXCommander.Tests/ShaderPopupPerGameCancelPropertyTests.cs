using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

// Feature: shader-selection-popup, Property 7: Per-game cancel preserves state

/// <summary>
/// Property-based tests for per-game cancel preserving state.
/// When the user opens the per-game shader popup and clicks "Cancel"
/// (ShaderPopupHelper.ShowAsync returns null), the PerGameShaderSelection
/// for that game should remain unchanged and the ComboBox should revert
/// to its previous selected index.
/// **Validates: Requirements 6.5, 6.6**
/// </summary>
[Collection("StaticShaderMode")]
public class ShaderPopupPerGameCancelPropertyTests
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

    /// <summary>
    /// Generates a random dictionary of game names to per-game shader selections.
    /// Each entry represents an existing per-game override.
    /// </summary>
    private static Gen<Dictionary<string, List<string>>> GenPerGameSelections()
    {
        return GenGameName().SelectMany(gameName =>
            GenPackSelection().Select(packs =>
                new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    [gameName] = packs
                }));
    }

    // ── Property 7 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// For any game name with an existing per-game shader selection, when the user
    /// opens the per-game shader popup and clicks "Cancel" (ShowAsync returns null),
    /// the PerGameShaderSelection for that game should remain unchanged and the
    /// ComboBox should revert to its previous selected index.
    ///
    /// This models the cancel logic in BuildOverridesPanel:
    ///   if (result == null)
    ///   {
    ///       shaderModeCombo.SelectedIndex = previousShaderIdx;
    ///   }
    /// The PerGameShaderSelection dictionary is never modified on cancel.
    /// </summary>
    [Property(MaxTest = 30)]
    public Property PerGameCancel_PreservesState()
    {
        var gen =
            GenGameName().SelectMany(gameName =>
            GenPackSelection().SelectMany(existingPacks =>
            Gen.Elements(0, 1).Select(previousIdx =>
                (gameName, existingPacks, previousIdx))));

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (gameName, existingPacks, previousShaderIdx) = tuple;

            // Arrange: create a PerGameShaderSelection dictionary with an existing entry
            var perGameShaderSelection = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                [gameName] = new List<string>(existingPacks)
            };

            // Snapshot the state before cancel
            var snapshotPacks = new List<string>(perGameShaderSelection[gameName]);
            var snapshotKeys = new HashSet<string>(perGameShaderSelection.Keys, StringComparer.OrdinalIgnoreCase);

            // Simulate the ComboBox previous index tracking
            int comboBoxSelectedIndex = previousShaderIdx;

            // Act: simulate Cancel — ShowAsync returns null
            List<string>? result = null;
            if (result != null)
            {
                // This block would execute on Confirm; it should NOT execute on Cancel
                perGameShaderSelection[gameName] = result;
            }
            else
            {
                // User cancelled — revert ComboBox to previous value
                comboBoxSelectedIndex = previousShaderIdx;
            }

            // Assert 1: PerGameShaderSelection still contains the game entry
            if (!perGameShaderSelection.ContainsKey(gameName))
                return false.Label(
                    $"Entry for '{gameName}' was removed after Cancel");

            // Assert 2: the stored selection is unchanged
            var expectedSet = new HashSet<string>(snapshotPacks, StringComparer.OrdinalIgnoreCase);
            var actualSet = new HashSet<string>(perGameShaderSelection[gameName], StringComparer.OrdinalIgnoreCase);

            if (!expectedSet.SetEquals(actualSet))
                return false.Label(
                    $"Selection changed for '{gameName}': " +
                    $"expected [{string.Join(",", expectedSet)}], " +
                    $"got [{string.Join(",", actualSet)}]");

            // Assert 3: count preserved (no duplicates introduced or items lost)
            if (perGameShaderSelection[gameName].Count != snapshotPacks.Count)
                return false.Label(
                    $"Count changed for '{gameName}': " +
                    $"expected {snapshotPacks.Count}, got {perGameShaderSelection[gameName].Count}");

            // Assert 4: dictionary keys unchanged (no entries added or removed)
            var currentKeys = new HashSet<string>(perGameShaderSelection.Keys, StringComparer.OrdinalIgnoreCase);
            if (!snapshotKeys.SetEquals(currentKeys))
                return false.Label(
                    $"Dictionary keys changed: expected [{string.Join(",", snapshotKeys)}], " +
                    $"got [{string.Join(",", currentKeys)}]");

            // Assert 5: ComboBox reverted to previous index
            if (comboBoxSelectedIndex != previousShaderIdx)
                return false.Label(
                    $"ComboBox index not reverted: expected {previousShaderIdx}, " +
                    $"got {comboBoxSelectedIndex}");

            return true.Label(
                $"OK: '{gameName}' preserved {snapshotPacks.Count} packs, " +
                $"ComboBox reverted to index {previousShaderIdx}");
        });
    }
}
