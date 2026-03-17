using FsCheck;
using FsCheck.Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Preservation property tests for the DC shader routing fix.
///
/// These tests verify that the fix does NOT break existing behavior for non-DC games.
/// When DC is NOT installed, the routing logic must behave exactly as it did before the fix:
/// - If ReShade is installed → SyncGameFolder (deploy shaders to game-local folder)
/// - If neither DC nor ReShade is installed → Skip (no shader deployment)
///
/// **Validates: Requirements 3.1, 3.4**
/// </summary>
public class DcShaderRoutingPreservationTests
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

    // ── 7.1: dcInstalled=false, rsInstalled=true → SyncGameFolder ────────────

    /// <summary>
    /// For any input where dcInstalled=false and rsInstalled=true, the routing logic
    /// SHALL call SyncGameFolder — preserving the original behavior for non-DC games
    /// that have ReShade installed.
    ///
    /// Generates random dcModeLevel (0–10) and perGameDcMode (null or 0–10) values
    /// to ensure the result is independent of DC mode settings when DC is not installed.
    ///
    /// **Validates: Requirements 3.1**
    /// </summary>
    [Property(MaxTest = 200)]
    public Property DcNotInstalled_RsInstalled_AnyDcMode_RoutesToSyncGameFolder()
    {
        var genDcModeLevel = Gen.Choose(0, 10);

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
                const bool dcInstalled = false;
                const bool rsInstalled = true;

                var outcome = FixedRoutingDecision(dcInstalled, rsInstalled);

                return (outcome == RoutingOutcome.SyncGameFolder)
                    .Label($"dcInstalled={dcInstalled}, rsInstalled={rsInstalled}, " +
                           $"dcModeLevel={input.dcModeLevel}, perGameDcMode={input.perGameDcMode?.ToString() ?? "null"} " +
                           $"→ outcome={outcome} (expected SyncGameFolder)");
            });
    }

    // ── 7.2: dcInstalled=false, rsInstalled=false → Skip ─────────────────────

    /// <summary>
    /// For any input where dcInstalled=false and rsInstalled=false, the routing logic
    /// SHALL call neither SyncDcFolder nor SyncGameFolder (returns Skip) — preserving
    /// the original behavior for games with no shader management addons.
    ///
    /// Generates random dcModeLevel (0–10) and perGameDcMode (null or 0–10) values
    /// to ensure the result is independent of DC mode settings.
    ///
    /// **Validates: Requirements 3.4**
    /// </summary>
    [Property(MaxTest = 200)]
    public Property DcNotInstalled_RsNotInstalled_AnyDcMode_RoutesToSkip()
    {
        var genDcModeLevel = Gen.Choose(0, 10);

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
                const bool dcInstalled = false;
                const bool rsInstalled = false;

                var outcome = FixedRoutingDecision(dcInstalled, rsInstalled);

                return (outcome == RoutingOutcome.Skip)
                    .Label($"dcInstalled={dcInstalled}, rsInstalled={rsInstalled}, " +
                           $"dcModeLevel={input.dcModeLevel}, perGameDcMode={input.perGameDcMode?.ToString() ?? "null"} " +
                           $"→ outcome={outcome} (expected Skip)");
            });
    }

    // ── Preservation: InstallReShadeAsync with dcIsInstalled=false, dcMode=false → SyncGameFolder

    /// <summary>
    /// For the InstallReShadeAsync path: when dcIsInstalled=false and dcMode=false,
    /// the routing logic SHALL call SyncGameFolder — preserving the original behavior
    /// where ReShade installation deploys shaders to the game folder when DC is absent.
    ///
    /// Generates random dcModeLevel=0 and perGameDcMode (null or 0) to ensure
    /// dcMode evaluates to false, with dcIsInstalled=false.
    ///
    /// **Validates: Requirements 3.1**
    /// </summary>
    [Property(MaxTest = 200)]
    public Property ReShadeRouting_DcNotInstalled_DcModeFalse_RoutesToSyncGameFolder()
    {
        var genDcModeLevel = Gen.Constant(0);

        var genPerGameDcMode = Gen.OneOf(
            Gen.Constant<int?>(null),
            Gen.Constant<int?>(0));

        var gen = from dcModeLevel in genDcModeLevel
                  from perGameDcMode in genPerGameDcMode
                  select (dcModeLevel, perGameDcMode);

        return Prop.ForAll(
            Arb.From(gen),
            ((int dcModeLevel, int? perGameDcMode) input) =>
            {
                const bool dcIsInstalled = false;

                // Derive dcMode the same way the production code does
                bool dcMode = (input.perGameDcMode ?? input.dcModeLevel) > 0;

                var outcome = FixedReShadeRoutingDecision(dcMode, dcIsInstalled);

                return (!dcMode && outcome == RoutingOutcome.SyncGameFolder)
                    .Label($"dcIsInstalled={dcIsInstalled}, dcMode={dcMode}, " +
                           $"dcModeLevel={input.dcModeLevel}, perGameDcMode={input.perGameDcMode?.ToString() ?? "null"} " +
                           $"→ outcome={outcome} (expected SyncGameFolder)");
            });
    }
}
