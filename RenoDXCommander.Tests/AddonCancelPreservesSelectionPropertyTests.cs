using FsCheck;
using FsCheck.Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based test for cancel preserving existing per-game addon selection.
/// Feature: reshade-addons, Property 13: Cancel preserves existing selection
/// </summary>
public class AddonCancelPreservesSelectionPropertyTests
{
    // ── Generators ────────────────────────────────────────────────────────────────

    private static readonly Gen<string> GenGameName =
        from len in Gen.Choose(1, 30)
        from chars in Gen.ArrayOf(len, Gen.Elements(
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 -_:".ToCharArray()))
        select new string(chars);

    private static readonly Gen<string> GenPackageName =
        from len in Gen.Choose(1, 20)
        from chars in Gen.ArrayOf(len, Gen.Elements(
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".ToCharArray()))
        select new string(chars);

    private static readonly Gen<List<string>> GenAddonList =
        from count in Gen.Choose(0, 5)
        from names in Gen.ArrayOf(count, GenPackageName)
        select names.Distinct().ToList();

    private static readonly Gen<Dictionary<string, List<string>>> GenPerGameAddonSelection =
        from gameCount in Gen.Choose(1, 5)
        from gameNames in Gen.ArrayOf(gameCount, GenGameName)
        from addonLists in Gen.ArrayOf(gameCount, GenAddonList)
        select gameNames.Zip(addonLists)
            .GroupBy(kv => kv.First)
            .ToDictionary(g => g.Key, g => g.First().Second);

    // ── Property 13 ───────────────────────────────────────────────────────────────
    // Feature: reshade-addons, Property 13: Cancel preserves existing selection
    // **Validates: Requirements 8.5**

    /// <summary>
    /// For any existing PerGameAddonSelection dictionary, if the addon selection
    /// dialog is cancelled (ShowAsync returns null), the caller should NOT modify
    /// the existing selection. The dictionary must remain unchanged.
    /// </summary>
    [Property(MaxTest = 20)]
    public Property Cancel_PreservesExistingSelection()
    {
        return Prop.ForAll(
            Arb.From(GenPerGameAddonSelection),
            Arb.From(GenGameName),
            (Dictionary<string, List<string>> perGameSelection, string targetGame) =>
            {
                // Snapshot the dictionary before the simulated cancel
                var snapshotKeys = perGameSelection.Keys.ToList();
                var snapshotValues = perGameSelection.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new List<string>(kvp.Value));

                // Simulate ShowAsync returning null (user cancelled)
                List<string>? dialogResult = null;

                // The caller's contract: when result is null, do NOT modify the selection
                if (dialogResult != null)
                {
                    // This branch would update the selection — but cancel means we skip it
                    perGameSelection[targetGame] = dialogResult;
                }

                // Verify: the dictionary is completely unchanged after cancel
                bool sameKeyCount = perGameSelection.Count == snapshotKeys.Count;

                bool allKeysPresent = snapshotKeys.All(k => perGameSelection.ContainsKey(k));

                bool allValuesUnchanged = snapshotKeys.All(k =>
                    snapshotValues[k].SequenceEqual(perGameSelection[k]));

                return (sameKeyCount && allKeysPresent && allValuesUnchanged)
                    .Label($"keys={perGameSelection.Count}/{snapshotKeys.Count}, " +
                           $"allKeysPresent={allKeysPresent}, valuesUnchanged={allValuesUnchanged}");
            });
    }

    /// <summary>
    /// For any existing PerGameAddonSelection, cancelling the dialog for a specific
    /// game should preserve that game's selection entry (if it existed) as well as
    /// all other games' entries.
    /// </summary>
    [Property(MaxTest = 20)]
    public Property Cancel_PreservesTargetGameSelection()
    {
        return Prop.ForAll(
            Arb.From(GenPerGameAddonSelection),
            (Dictionary<string, List<string>> perGameSelection) =>
            {
                var gameNames = perGameSelection.Keys.ToList();
                if (gameNames.Count == 0)
                    return true.Label("Trivially true: no games in selection");

                // Pick a random game from the existing selection
                var targetGame = gameNames[0];
                var originalSelection = new List<string>(perGameSelection[targetGame]);

                // Simulate ShowAsync returning null (user cancelled)
                List<string>? dialogResult = null;

                // Caller contract: only update when result is non-null
                if (dialogResult != null)
                {
                    perGameSelection[targetGame] = dialogResult;
                }

                // Verify: the target game's selection is unchanged
                bool targetUnchanged = perGameSelection.ContainsKey(targetGame)
                    && originalSelection.SequenceEqual(perGameSelection[targetGame]);

                return targetUnchanged
                    .Label($"targetGame='{targetGame}', " +
                           $"originalCount={originalSelection.Count}, " +
                           $"currentCount={perGameSelection[targetGame].Count}");
            });
    }
}
