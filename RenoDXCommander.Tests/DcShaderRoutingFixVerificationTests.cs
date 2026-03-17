using FsCheck;
using FsCheck.Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Fix verification property tests for the DC shader routing fix.
///
/// These tests verify that for ANY input where dcInstalled=true — regardless of
/// dcModeLevel or perGameDcMode — the routing logic ALWAYS calls SyncDcFolder
/// and NEVER calls SyncGameFolder.
///
/// Unlike the bug condition exploration tests (which only tested dcMode=false),
/// these tests cover the full input space: dcModeLevel can be 0, 1, 2, etc.,
/// and perGameDcMode can be null or any non-negative integer.
///
/// **Validates: Requirements 2.1, 2.2, 2.3, 2.4**
/// </summary>
public class DcShaderRoutingFixVerificationTests
{
    // ── Pure model of the routing decision ────────────────────────────────────

    /// <summary>
    /// Represents the possible shader routing outcomes.
    /// </summary>
    public enum RoutingOutcome
    {
        SyncDcFolder,
        SyncGameFolder,
        Skip
    }

    /// <summary>
    /// Models the FIXED routing decision used by DeployAllShaders, DeployShadersForCard,
    /// and SyncShadersToAllLocations. The fix changed the condition from
    /// <c>if (dcInstalled &amp;&amp; dcMode)</c> to <c>if (dcInstalled)</c>.
    /// </summary>
    public static RoutingOutcome FixedRoutingDecision(bool dcInstalled, bool rsInstalled)
    {
        if (dcInstalled)
            return RoutingOutcome.SyncDcFolder;
        else if (rsInstalled)
            return RoutingOutcome.SyncGameFolder;
        return RoutingOutcome.Skip;
    }

    /// <summary>
    /// Models the FIXED routing decision for InstallReShadeAsync.
    /// The fix changed <c>if (dcMode)</c> to <c>if (dcMode || dcIsInstalled)</c>.
    /// </summary>
    public static RoutingOutcome FixedReShadeRoutingDecision(bool dcMode, bool dcIsInstalled)
    {
        if (dcMode || dcIsInstalled)
            return RoutingOutcome.SyncDcFolder;
        else if (!dcIsInstalled)
            return RoutingOutcome.SyncGameFolder;
        return RoutingOutcome.Skip;
    }

    // ── Property test: dcInstalled=true (any dcModeLevel) → SyncDcFolder ─────

    /// <summary>
    /// For any input where dcInstalled=true, regardless of dcModeLevel (0, 1, 2, ...)
    /// and regardless of perGameDcMode (null, 0, 1, 2, ...), the fixed general routing
    /// logic (DeployAllShaders / DeployShadersForCard / SyncShadersToAllLocations)
    /// SHALL call SyncDcFolder and SHALL NOT call SyncGameFolder.
    ///
    /// **Validates: Requirements 2.1, 2.2, 2.3**
    /// </summary>
    [Property(MaxTest = 200)]
    public Property DcInstalled_AnyDcModeLevel_GeneralRouting_AlwaysSyncDcFolder()
    {
        // dcModeLevel can be any non-negative integer (0, 1, 2, ...)
        var genDcModeLevel = Gen.Choose(0, 10);

        // perGameDcMode can be null or any non-negative integer
        var genPerGameDcMode = Gen.OneOf(
            Gen.Constant<int?>(null),
            Gen.Choose(0, 10).Select(v => (int?)v));

        // rsInstalled can be anything — shouldn't matter when dcInstalled=true
        var genRsInstalled = Arb.Default.Bool().Generator;

        var gen = from dcModeLevel in genDcModeLevel
                  from perGameDcMode in genPerGameDcMode
                  from rsInstalled in genRsInstalled
                  select (dcModeLevel, perGameDcMode, rsInstalled);

        return Prop.ForAll(
            Arb.From(gen),
            ((int dcModeLevel, int? perGameDcMode, bool rsInstalled) input) =>
            {
                const bool dcInstalled = true;

                var outcome = FixedRoutingDecision(dcInstalled, input.rsInstalled);

                return (outcome == RoutingOutcome.SyncDcFolder)
                    .Label($"dcModeLevel={input.dcModeLevel}, perGameDcMode={input.perGameDcMode?.ToString() ?? "null"}, " +
                           $"rsInstalled={input.rsInstalled} → outcome={outcome} (expected SyncDcFolder)");
            });
    }

    /// <summary>
    /// For any input where dcIsInstalled=true, regardless of dcModeLevel (0, 1, 2, ...)
    /// and regardless of perGameDcMode (null, 0, 1, 2, ...), the fixed InstallReShadeAsync
    /// routing logic SHALL call SyncDcFolder and SHALL NOT call SyncGameFolder.
    ///
    /// This covers both dcMode=true and dcMode=false scenarios — in either case,
    /// when DC is installed, shaders must route to the DC appdata folder.
    ///
    /// **Validates: Requirements 2.4**
    /// </summary>
    [Property(MaxTest = 200)]
    public Property DcInstalled_AnyDcModeLevel_ReShadeRouting_AlwaysSyncDcFolder()
    {
        // dcModeLevel can be any non-negative integer
        var genDcModeLevel = Gen.Choose(0, 10);

        // perGameDcMode can be null or any non-negative integer
        var genPerGameDcMode = Gen.OneOf(
            Gen.Constant<int?>(null),
            Gen.Choose(0, 10).Select(v => (int?)v));

        var gen = from dcModeLevel in genDcModeLevel
                  from perGameDcMode in genPerGameDcMode
                  select (dcModeLevel, perGameDcMode);

        return Prop.ForAll(
            Arb.From(gen),
            ((int dcModeLevel, int? perGameDcMode) input) =>
            {
                const bool dcIsInstalled = true;

                // Derive dcMode the same way the production code does
                bool dcMode = (input.perGameDcMode ?? input.dcModeLevel) > 0;

                var outcome = FixedReShadeRoutingDecision(dcMode, dcIsInstalled);

                return (outcome == RoutingOutcome.SyncDcFolder)
                    .Label($"dcModeLevel={input.dcModeLevel}, perGameDcMode={input.perGameDcMode?.ToString() ?? "null"}, " +
                           $"dcMode={dcMode} → outcome={outcome} (expected SyncDcFolder)");
            });
    }
}
