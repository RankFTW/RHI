using FsCheck;
using FsCheck.Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for hotkey string round-trip (Build → Parse).
/// Feature: reshade-hotkey-settings, Property 2: Hotkey string round-trip
/// **Validates: Requirements 2.2**
/// </summary>
public class HotkeyStringRoundTripPropertyTests
{
    // ── Generators ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a tuple of (vk code 1–254, shift, ctrl, alt).
    /// </summary>
    private static readonly Gen<Tuple<int, bool, bool, bool>> GenHotkeyTuple =
        from vk in Gen.Choose(1, 254)
        from shift in Arb.Generate<bool>()
        from ctrl in Arb.Generate<bool>()
        from alt in Arb.Generate<bool>()
        select Tuple.Create(vk, shift, ctrl, alt);

    // ── Property 2 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Property 2: Hotkey string round-trip
    /// For any VK code (1–254) and any combination of modifier booleans,
    /// ParseHotkeyString(BuildHotkeyString(vk, shift, ctrl, alt)) returns the original tuple.
    /// **Validates: Requirements 2.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property BuildThenParse_RoundTrips_To_Original()
    {
        return Prop.ForAll(
            Arb.From(GenHotkeyTuple),
            (Tuple<int, bool, bool, bool> t) =>
            {
                var (vk, shift, ctrl, alt) = t;

                var built = HotkeyManager.BuildHotkeyString(vk, shift, ctrl, alt);
                var (parsedVk, parsedShift, parsedCtrl, parsedAlt) = HotkeyManager.ParseHotkeyString(built);

                var vkMatch = parsedVk == vk;
                var shiftMatch = parsedShift == shift;
                var ctrlMatch = parsedCtrl == ctrl;
                var altMatch = parsedAlt == alt;

                return (vkMatch && shiftMatch && ctrlMatch && altMatch)
                    .Label($"Round-trip failed: input=({vk},{shift},{ctrl},{alt}), " +
                           $"built='{built}', " +
                           $"parsed=({parsedVk},{parsedShift},{parsedCtrl},{parsedAlt})");
            });
    }
}
