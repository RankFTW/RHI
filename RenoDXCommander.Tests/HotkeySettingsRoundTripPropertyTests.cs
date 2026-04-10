using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for hotkey settings round-trip (Save → Load).
/// Feature: reshade-hotkey-settings, Property 1: Hotkey settings round-trip
/// **Validates: Requirements 3.1, 3.2, 3.4**
/// </summary>
public class HotkeySettingsRoundTripPropertyTests
{
    // ── Generators ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a valid KeyOverlay format string "vk,shift,ctrl,alt"
    /// where vk is 1–254 and shift/ctrl/alt are 0 or 1.
    /// </summary>
    private static readonly Gen<string> GenKeyOverlayString =
        from vk in Gen.Choose(1, 254)
        from shift in Gen.Elements(0, 1)
        from ctrl in Gen.Elements(0, 1)
        from alt in Gen.Elements(0, 1)
        select $"{vk},{shift},{ctrl},{alt}";

    // ── Property 1 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Property 1: Hotkey settings round-trip
    /// For any valid KeyOverlay format string, setting OverlayHotkey on a SettingsViewModel,
    /// calling SaveSettingsToDict, then LoadSettingsFromDict on a fresh SettingsViewModel
    /// produces the same OverlayHotkey value.
    /// **Validates: Requirements 3.1, 3.2, 3.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property SaveThenLoad_RoundTrips_OverlayHotkey()
    {
        return Prop.ForAll(
            Arb.From(GenKeyOverlayString),
            (string hotkeyValue) =>
            {
                // 1. Create a VM and set the hotkey
                var sourceVm = new SettingsViewModel();
                sourceVm.OverlayHotkey = hotkeyValue;

                // 2. Save to dictionary
                var dict = new Dictionary<string, string>();
                sourceVm.SaveSettingsToDict(dict);

                // 3. Load into a fresh VM
                var targetVm = new SettingsViewModel();
                targetVm.LoadSettingsFromDict(dict);

                // 4. Assert round-trip equality
                return (targetVm.OverlayHotkey == hotkeyValue)
                    .Label($"Round-trip failed: input='{hotkeyValue}', " +
                           $"saved dict value='{(dict.TryGetValue("OverlayHotkey", out var v) ? v : "<missing>")}', " +
                           $"loaded='{targetVm.OverlayHotkey}'");
            });
    }
}
