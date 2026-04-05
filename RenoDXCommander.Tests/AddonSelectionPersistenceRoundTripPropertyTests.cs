using System.Text.Json;
using FsCheck;
using FsCheck.Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based test for per-game addon selection persistence round-trip.
/// Feature: reshade-addons, Property 8: Per-game addon selection persistence round-trip
/// </summary>
public class AddonSelectionPersistenceRoundTripPropertyTests
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

    // ── Property 8 ────────────────────────────────────────────────────────────────
    // Feature: reshade-addons, Property 8: Per-game addon selection persistence round-trip
    // **Validates: Requirements 6.5**

    /// <summary>
    /// For any game name and list of addon package names, persisting the per-game
    /// addon selection and then loading it should produce an equivalent list.
    /// Tests the JSON serialization round-trip of Dictionary&lt;string, List&lt;string&gt;&gt;
    /// using System.Text.Json, which is the same mechanism used by GameNameService.
    /// </summary>
    [Property(MaxTest = 20)]
    public Property AddonSelection_JsonRoundTrip_PreservesAllEntries()
    {
        return Prop.ForAll(
            Arb.From(GenPerGameAddonSelection),
            (Dictionary<string, List<string>> original) =>
            {
                // Serialize to JSON (same as SaveNameMappings)
                var json = JsonSerializer.Serialize(original);

                // Deserialize back (same as LoadNameMappings)
                var restored = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json);

                if (restored == null)
                    return false.Label("Deserialized dictionary was null");

                if (original.Count != restored.Count)
                    return false.Label($"Game count mismatch: original={original.Count}, restored={restored.Count}");

                foreach (var kvp in original)
                {
                    if (!restored.TryGetValue(kvp.Key, out var restoredList))
                        return false.Label($"Missing game '{kvp.Key}' after round-trip");

                    if (kvp.Value.Count != restoredList.Count)
                        return false.Label($"Addon count mismatch for game '{kvp.Key}': {kvp.Value.Count} vs {restoredList.Count}");

                    if (!kvp.Value.SequenceEqual(restoredList))
                        return false.Label($"Addon list mismatch for game '{kvp.Key}'");
                }

                return true.Label("All per-game addon selections preserved after JSON round-trip");
            });
    }
}
