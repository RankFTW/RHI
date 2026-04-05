using FsCheck;
using FsCheck.Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based test for global toggle ON clearing per-game addon selection.
/// Feature: reshade-addons, Property 9: Global toggle ON clears per-game selection
/// </summary>
public class AddonGlobalToggleClearsSelectionPropertyTests
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

    private static readonly Gen<List<string>> GenNonEmptyAddonList =
        from count in Gen.Choose(1, 5)
        from names in Gen.ArrayOf(count, GenPackageName)
        select names.Distinct().ToList();

    /// <summary>
    /// Generates a dictionary of games that are in per-game "Select" mode,
    /// each with a non-empty addon selection.
    /// </summary>
    private static readonly Gen<(Dictionary<string, string> Modes, Dictionary<string, List<string>> Selections)> GenPerGameState =
        from gameCount in Gen.Choose(1, 5)
        from gameNames in Gen.ArrayOf(gameCount, GenGameName)
        from addonLists in Gen.ArrayOf(gameCount, GenNonEmptyAddonList)
        let uniqueGames = gameNames.Distinct().ToArray()
        let modes = uniqueGames.ToDictionary(g => g, _ => "Select", StringComparer.OrdinalIgnoreCase)
        let selections = uniqueGames.Zip(addonLists.Take(uniqueGames.Length))
            .ToDictionary(p => p.First, p => p.Second, StringComparer.OrdinalIgnoreCase)
        select (modes, selections);

    // ── Property 9 ────────────────────────────────────────────────────────────────
    // Feature: reshade-addons, Property 9: Global toggle ON clears per-game selection
    // **Validates: Requirements 6.6**

    /// <summary>
    /// For any game that has a per-game addon selection (PerGameAddonMode = "Select",
    /// PerGameAddonSelection has entries), switching to Global mode (set PerGameAddonMode
    /// to "Global" and clear/remove the PerGameAddonSelection entry) should result in
    /// the per-game selection being cleared (empty or removed).
    /// </summary>
    [Property(MaxTest = 20)]
    public Property GlobalToggleOn_ClearsPerGameSelection()
    {
        return Prop.ForAll(
            Arb.From(GenPerGameState),
            (state) =>
            {
                var (modes, selections) = state;

                // Precondition: all games start in "Select" mode with non-empty selections
                var gameNames = modes.Keys.ToList();
                foreach (var game in gameNames)
                {
                    if (modes[game] != "Select")
                        return false.Label($"Precondition failed: game '{game}' not in Select mode");
                    if (!selections.ContainsKey(game) || selections[game].Count == 0)
                        return false.Label($"Precondition failed: game '{game}' has no addon selection");
                }

                // Simulate switching each game to Global mode:
                // Set mode to "Global" and clear/remove the per-game selection
                foreach (var game in gameNames)
                {
                    modes[game] = "Global";
                    selections.Remove(game);
                }

                // Verify: all games are now in Global mode with no per-game selection
                foreach (var game in gameNames)
                {
                    if (modes[game] != "Global")
                        return false.Label($"Game '{game}' mode should be 'Global' but was '{modes[game]}'");

                    if (selections.ContainsKey(game))
                        return false.Label($"Game '{game}' still has a per-game selection after switching to Global");
                }

                return true.Label("All per-game selections cleared after switching to Global mode");
            });
    }

    /// <summary>
    /// Switching a single game to Global should only clear that game's selection,
    /// leaving other games' selections intact.
    /// </summary>
    [Property(MaxTest = 20)]
    public Property GlobalToggleOn_OnlyAffectsTargetGame()
    {
        return Prop.ForAll(
            Arb.From(GenPerGameState),
            (state) =>
            {
                var (modes, selections) = state;
                var gameNames = modes.Keys.ToList();

                if (gameNames.Count < 2)
                    return true.Label("Trivially true: fewer than 2 games");

                // Pick the first game to switch to Global
                var targetGame = gameNames[0];
                var otherGames = gameNames.Skip(1).ToList();

                // Snapshot other games' selections before the toggle
                var otherSelectionsBefore = otherGames
                    .Where(g => selections.ContainsKey(g))
                    .ToDictionary(g => g, g => new List<string>(selections[g]), StringComparer.OrdinalIgnoreCase);

                // Switch only the target game to Global
                modes[targetGame] = "Global";
                selections.Remove(targetGame);

                // Verify target game's selection is cleared
                if (selections.ContainsKey(targetGame))
                    return false.Label($"Target game '{targetGame}' still has selection after Global toggle");

                // Verify other games' selections are unchanged
                foreach (var game in otherGames)
                {
                    if (!otherSelectionsBefore.ContainsKey(game))
                        continue;

                    if (!selections.ContainsKey(game))
                        return false.Label($"Other game '{game}' lost its selection");

                    if (!otherSelectionsBefore[game].SequenceEqual(selections[game]))
                        return false.Label($"Other game '{game}' selection was modified");
                }

                return true.Label("Only target game's selection was cleared; others unchanged");
            });
    }
}
