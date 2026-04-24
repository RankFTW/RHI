using FsCheck;
using FsCheck.Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for FormatHotkeyDisplay ordered format.
/// Feature: reshade-hotkey-settings, Property 3: FormatHotkeyDisplay produces correct ordered format
/// **Validates: Requirements 2.1, 7.1, 7.2, 7.3**
/// </summary>
public class HotkeyDisplayFormatPropertyTests
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

    // ── Property 3 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Property 3: FormatHotkeyDisplay produces correct ordered format
    /// For any VK code (1–254) and any combination of modifier flags,
    /// FormatHotkeyDisplay(vk, shift, ctrl, alt) produces a non-empty string where:
    /// (a) output is non-empty
    /// (b) if any modifiers are true, they appear before the main key name separated by " + "
    /// (c) modifier names appear in the order Ctrl, Shift, Alt
    /// (d) the main key name is non-empty
    /// **Validates: Requirements 2.1, 7.1, 7.2, 7.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property FormatHotkeyDisplay_Produces_Correct_Ordered_Format()
    {
        return Prop.ForAll(
            Arb.From(GenHotkeyTuple),
            (Tuple<int, bool, bool, bool> t) =>
            {
                var (vk, shift, ctrl, alt) = t;

                var display = HotkeyManager.FormatHotkeyDisplay(vk, shift, ctrl, alt);

                // (a) output is non-empty
                var nonEmpty = !string.IsNullOrEmpty(display);

                // Split on " + " to get parts
                var parts = display.Split(" + ");

                // The last part is always the main key name
                var mainKeyName = parts[^1];

                // (d) the main key name is non-empty
                var mainKeyNonEmpty = !string.IsNullOrWhiteSpace(mainKeyName);

                // (b) & (c) Check modifier ordering: Ctrl before Shift before Alt, all before main key
                var modifierParts = parts[..^1]; // everything except the last element

                // Build expected modifier list
                var expectedModifiers = new List<string>();
                if (ctrl) expectedModifiers.Add("Ctrl");
                if (shift) expectedModifiers.Add("Shift");
                if (alt) expectedModifiers.Add("Alt");

                var modifiersMatch = modifierParts.SequenceEqual(expectedModifiers);

                return (nonEmpty && mainKeyNonEmpty && modifiersMatch)
                    .Label($"Failed: vk={vk}, shift={shift}, ctrl={ctrl}, alt={alt}, " +
                           $"display='{display}', " +
                           $"expectedModifiers=[{string.Join(",", expectedModifiers)}], " +
                           $"actualModifiers=[{string.Join(",", modifierParts)}], " +
                           $"mainKey='{mainKeyName}'");
            });
    }
}
