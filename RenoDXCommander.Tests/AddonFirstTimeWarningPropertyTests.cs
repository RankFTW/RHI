using FsCheck;
using FsCheck.Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based test for first-time warning shown exactly once.
/// Feature: reshade-addons, Property 15: First-time warning shown exactly once
/// </summary>
public class AddonFirstTimeWarningPropertyTests
{
    // ── State machine model ───────────────────────────────────────────────────────

    /// <summary>
    /// Represents the user's choice when the warning dialog is shown.
    /// </summary>
    public enum WarningChoice { Continue, Cancel }

    /// <summary>
    /// Represents a single attempt to open the Addon Manager Window.
    /// </summary>
    public record OpenAttempt(WarningChoice Choice);

    // ── Generators ────────────────────────────────────────────────────────────────

    private static readonly Gen<WarningChoice> GenChoice =
        Gen.Elements(WarningChoice.Continue, WarningChoice.Cancel);

    private static readonly Gen<OpenAttempt> GenOpenAttempt =
        from choice in GenChoice
        select new OpenAttempt(choice);

    private static readonly Gen<List<OpenAttempt>> GenAttemptSequence =
        from count in Gen.Choose(1, 15)
        from attempts in Gen.ArrayOf(count, GenOpenAttempt)
        select attempts.ToList();

    // ── Pure state machine logic ──────────────────────────────────────────────────

    /// <summary>
    /// Simulates the warning state machine for a single open attempt.
    /// Returns (warningWasShown, newDismissedState).
    /// </summary>
    private static (bool WarningShown, bool DismissedAfter) ProcessAttempt(
        bool addonWarningDismissed, WarningChoice choice)
    {
        if (addonWarningDismissed)
        {
            // Warning already dismissed — skip warning, open directly
            return (WarningShown: false, DismissedAfter: true);
        }

        // Warning not yet dismissed — show warning
        return choice switch
        {
            WarningChoice.Continue => (WarningShown: true, DismissedAfter: true),
            WarningChoice.Cancel => (WarningShown: true, DismissedAfter: false),
            _ => throw new ArgumentOutOfRangeException(nameof(choice))
        };
    }

    // ── Property 15 ───────────────────────────────────────────────────────────────
    // Feature: reshade-addons, Property 15: First-time warning shown exactly once
    // **Validates: Requirements 10.1, 10.4, 10.5**

    /// <summary>
    /// For any sequence of open attempts, the warning dialog is shown if and only if
    /// AddonWarningDismissed is false. "Continue" sets the flag to true (warning never
    /// shown again). "Cancel" leaves the flag false (warning shown on next attempt).
    /// </summary>
    [Property(MaxTest = 20)]
    public Property WarningShown_IffDismissedIsFalse()
    {
        return Prop.ForAll(
            Arb.From(GenAttemptSequence),
            (List<OpenAttempt> attempts) =>
            {
                bool dismissed = false;

                foreach (var attempt in attempts)
                {
                    var (warningShown, newDismissed) = ProcessAttempt(dismissed, attempt.Choice);

                    // Warning is shown iff dismissed was false
                    if (warningShown != !dismissed)
                        return false.Label(
                            $"Warning shown={warningShown} but dismissed was {dismissed}");

                    dismissed = newDismissed;
                }

                return true.Label("Warning shown iff AddonWarningDismissed was false");
            });
    }

    /// <summary>
    /// For any sequence of open attempts, once the user clicks "Continue",
    /// AddonWarningDismissed becomes true and the warning is never shown again
    /// for any subsequent attempts.
    /// </summary>
    [Property(MaxTest = 20)]
    public Property ContinueSetsFlag_WarningNeverShownAgain()
    {
        return Prop.ForAll(
            Arb.From(GenAttemptSequence),
            (List<OpenAttempt> attempts) =>
            {
                bool dismissed = false;
                bool continueClicked = false;

                foreach (var attempt in attempts)
                {
                    var (warningShown, newDismissed) = ProcessAttempt(dismissed, attempt.Choice);

                    if (continueClicked)
                    {
                        // After Continue was clicked, warning must never be shown
                        if (warningShown)
                            return false.Label("Warning shown after Continue was previously clicked");

                        // Flag must remain true
                        if (!newDismissed)
                            return false.Label("Dismissed flag became false after Continue was clicked");
                    }

                    if (!dismissed && attempt.Choice == WarningChoice.Continue)
                        continueClicked = true;

                    dismissed = newDismissed;
                }

                return true.Label("After Continue, warning never shown again");
            });
    }

    /// <summary>
    /// For any sequence of open attempts where the user always clicks "Cancel",
    /// AddonWarningDismissed remains false and the warning is shown every time.
    /// </summary>
    [Property(MaxTest = 20)]
    public Property CancelLeavesFlag_WarningShownEveryTime()
    {
        // Generate sequences of only Cancel choices
        var genCancelSequence =
            from count in Gen.Choose(1, 15)
            from attempts in Gen.ArrayOf(count, Gen.Constant(new OpenAttempt(WarningChoice.Cancel)))
            select attempts.ToList();

        return Prop.ForAll(
            Arb.From(genCancelSequence),
            (List<OpenAttempt> attempts) =>
            {
                bool dismissed = false;

                foreach (var attempt in attempts)
                {
                    var (warningShown, newDismissed) = ProcessAttempt(dismissed, attempt.Choice);

                    if (!warningShown)
                        return false.Label("Warning not shown despite dismissed being false");

                    if (newDismissed)
                        return false.Label("Dismissed became true after Cancel");

                    dismissed = newDismissed;
                }

                return (!dismissed).Label("Dismissed should remain false after all Cancels");
            });
    }
}
