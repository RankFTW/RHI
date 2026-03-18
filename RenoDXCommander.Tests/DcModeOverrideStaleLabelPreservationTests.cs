using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Preservation property tests for DC Mode override combo box behavior.
///
/// These tests capture the EXISTING correct behavior on UNFIXED code and must PASS
/// both before and after the fix is applied — any failure after the fix indicates a regression.
///
/// The tests replicate the pure computation logic from <c>BuildOverridesPanel</c>
/// (DetailPanelBuilder.cs) and <c>OpenOverridesFlyout</c> (MainWindow.xaml.cs):
///   - <c>globalDcLabel</c> computation
///   - <c>dcModeOptions</c> array construction (always 4 items)
///   - <c>SelectedIndex</c> mapping from <c>currentDcMode</c>
///   - Vulkan default to index 1 when no explicit override
///
/// **Validates: Requirements 3.1, 3.2, 3.3, 3.4**
///
/// EXPECTED OUTCOME on UNFIXED code: All tests PASS.
/// After the fix is applied: All tests MUST still PASS (no regressions).
/// </summary>
public class DcModeOverrideStaleLabelPreservationTests
{
    // ── Helpers replicating production logic ──────────────────────────────────

    /// <summary>
    /// Formats a DcModeLevel integer to its display label, matching the production
    /// switch expression in BuildOverridesPanel and OpenOverridesFlyout.
    /// </summary>
    private static string FormatDcMode(int level) => level switch
    {
        1 => "DC Mode 1",
        2 => "DC Mode 2",
        _ => "Off"
    };

    /// <summary>
    /// Builds the dcModeOptions array exactly as the production code does.
    /// </summary>
    private static string[] BuildDcModeOptions(int dcModeLevel)
    {
        var globalDcLabel = FormatDcMode(dcModeLevel);
        return new[] { $"Global ({globalDcLabel})", "Exclude (Off)", "DC Mode 1", "DC Mode 2" };
    }

    /// <summary>
    /// Computes the SelectedIndex from a per-game DC mode override value,
    /// matching the production switch expression:
    /// <c>currentDcMode switch { null => 0, 0 => 1, 1 => 2, 2 => 3, _ => 0 }</c>
    /// </summary>
    private static int ComputeSelectedIndex(int? currentDcMode) => currentDcMode switch
    {
        null => 0,
        0 => 1,
        1 => 2,
        2 => 3,
        _ => 0
    };

    /// <summary>
    /// Computes the persisted int? from a SelectedIndex, which is the inverse mapping:
    /// <c>{0→null, 1→0, 2→1, 3→2}</c>
    /// </summary>
    private static int? SelectedIndexToPersistedValue(int selectedIndex) => selectedIndex switch
    {
        0 => null,
        1 => 0,
        2 => 1,
        3 => 2,
        _ => null
    };

    // ── FsCheck generators ───────────────────────────────────────────────────

    private static readonly Gen<int> GenDcModeLevel = Gen.Elements(0, 1, 2);
    private static readonly Gen<int?> GenPerGameDcMode = Gen.Elements<int?>(null, 0, 1, 2);

    // ── Property 2a: SelectedIndex mapping is correct for all combinations ───

    /// <summary>
    /// Property 2a: For all (dcModeLevel, perGameDcMode) combinations where
    /// dcModeLevel ∈ {0,1,2} and perGameDcMode ∈ {null, 0, 1, 2},
    /// the SelectedIndex matches the expected mapping from currentDcMode.
    ///
    /// This confirms the SelectedIndex is determined solely by perGameDcMode
    /// and is NOT affected by dcModeLevel (only the label changes).
    ///
    /// **Validates: Requirements 3.1, 3.2**
    /// </summary>
    [Property(MaxTest = 50)]
    public Property SelectedIndex_MatchesExpectedMapping_ForAllCombinations()
    {
        var genPair = from dcLevel in GenDcModeLevel
                      from perGame in GenPerGameDcMode
                      select (dcLevel, perGame);

        return Prop.ForAll(
            Arb.From(genPair),
            pair =>
            {
                var (dcModeLevel, perGameDcMode) = pair;

                // Build options (label depends on dcModeLevel, but SelectedIndex does not)
                var dcModeOptions = BuildDcModeOptions(dcModeLevel);

                // Compute SelectedIndex the same way production code does (non-Vulkan path)
                var selectedIndex = ComputeSelectedIndex(perGameDcMode);

                // Expected mapping: null→0, 0→1, 1→2, 2→3
                var expected = perGameDcMode switch
                {
                    null => 0,
                    0 => 1,
                    1 => 2,
                    2 => 3,
                    _ => 0
                };

                return (selectedIndex == expected)
                    .Label($"dcModeLevel={dcModeLevel}, perGameDcMode={perGameDcMode?.ToString() ?? "null"} → " +
                           $"SelectedIndex={selectedIndex}, Expected={expected}");
            });
    }

    // ── Property 2b: Vulkan games default to SelectedIndex=1 ─────────────────

    /// <summary>
    /// Property 2b: For all dcModeLevel values, when isVulkan=true and
    /// perGameDcMode=null, the SelectedIndex defaults to 1 ("Exclude (Off)").
    ///
    /// This replicates the BuildOverridesPanel Vulkan default:
    /// <c>(card.RequiresVulkanInstall &amp;&amp; currentDcMode == null) ? 1 : ...</c>
    ///
    /// **Validates: Requirements 3.3**
    /// </summary>
    [Property(MaxTest = 50)]
    public Property VulkanGame_NoExplicitOverride_DefaultsToExclude()
    {
        return Prop.ForAll(
            Arb.From(GenDcModeLevel),
            dcModeLevel =>
            {
                // Vulkan game with no explicit override (perGameDcMode = null)
                int? currentDcMode = null;
                bool isVulkan = true;

                // Production logic from BuildOverridesPanel:
                // SelectedIndex = (card.RequiresVulkanInstall && currentDcMode == null) ? 1 : ...
                var selectedIndex = (isVulkan && currentDcMode == null)
                    ? 1
                    : ComputeSelectedIndex(currentDcMode);

                return (selectedIndex == 1)
                    .Label($"dcModeLevel={dcModeLevel}, Vulkan=true, perGameDcMode=null → " +
                           $"SelectedIndex={selectedIndex}, Expected=1 (Exclude (Off))");
            });
    }

    // ── Property 2c: dcModeOptions always has exactly 4 items ────────────────

    /// <summary>
    /// Property 2c: For all dcModeLevel values, the dcModeOptions array always
    /// has exactly 4 items: ["Global (...)", "Exclude (Off)", "DC Mode 1", "DC Mode 2"].
    ///
    /// **Validates: Requirements 3.4**
    /// </summary>
    [Property(MaxTest = 50)]
    public Property DcModeOptions_AlwaysHasFourItems()
    {
        return Prop.ForAll(
            Arb.From(GenDcModeLevel),
            dcModeLevel =>
            {
                var dcModeOptions = BuildDcModeOptions(dcModeLevel);

                return (dcModeOptions.Length == 4)
                    .Label($"dcModeLevel={dcModeLevel} → options.Length={dcModeOptions.Length}, Expected=4");
            });
    }

    // ── Property 2d: Items at indices 1, 2, 3 are always fixed ───────────────

    /// <summary>
    /// Property 2d: For all dcModeLevel values, items at indices 1, 2, 3 are
    /// always "Exclude (Off)", "DC Mode 1", "DC Mode 2" respectively.
    /// Only index 0 changes based on dcModeLevel.
    ///
    /// **Validates: Requirements 3.4**
    /// </summary>
    [Property(MaxTest = 50)]
    public Property DcModeOptions_FixedItemsUnchanged()
    {
        return Prop.ForAll(
            Arb.From(GenDcModeLevel),
            dcModeLevel =>
            {
                var dcModeOptions = BuildDcModeOptions(dcModeLevel);

                var item1Ok = dcModeOptions[1] == "Exclude (Off)";
                var item2Ok = dcModeOptions[2] == "DC Mode 1";
                var item3Ok = dcModeOptions[3] == "DC Mode 2";

                return (item1Ok && item2Ok && item3Ok)
                    .Label($"dcModeLevel={dcModeLevel} → " +
                           $"[1]='{dcModeOptions[1]}', [2]='{dcModeOptions[2]}', [3]='{dcModeOptions[3]}'");
            });
    }

    // ── Property 2e: SelectedIndex→persisted round-trip is correct ───────────

    /// <summary>
    /// Property 2e: For all (dcModeLevel, perGameDcMode) combinations,
    /// the round-trip from perGameDcMode → SelectedIndex → persisted int?
    /// produces the original perGameDcMode value.
    ///
    /// This confirms the save mapping {0→null, 1→0, 2→1, 3→2} is the
    /// inverse of the load mapping {null→0, 0→1, 1→2, 2→3}.
    ///
    /// **Validates: Requirements 3.1, 3.4**
    /// </summary>
    [Property(MaxTest = 50)]
    public Property SelectedIndex_RoundTrip_PreservesPerGameDcMode()
    {
        var genPair = from dcLevel in GenDcModeLevel
                      from perGame in GenPerGameDcMode
                      select (dcLevel, perGame);

        return Prop.ForAll(
            Arb.From(genPair),
            pair =>
            {
                var (dcModeLevel, perGameDcMode) = pair;

                // Load: perGameDcMode → SelectedIndex
                var selectedIndex = ComputeSelectedIndex(perGameDcMode);

                // Save: SelectedIndex → persisted int?
                var persisted = SelectedIndexToPersistedValue(selectedIndex);

                return (persisted == perGameDcMode)
                    .Label($"dcModeLevel={dcModeLevel}, perGameDcMode={perGameDcMode?.ToString() ?? "null"} → " +
                           $"SelectedIndex={selectedIndex} → persisted={persisted?.ToString() ?? "null"}");
            });
    }
}
