using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.ViewModels;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based and unit tests for BetaOptIn setting persistence round-trip.
/// Uses FsCheck with xUnit. Each property runs a minimum of 100 iterations.
/// </summary>
public class BetaSettingsRoundTripPropertyTests
{
    // Feature: beta-opt-in, Property 1: BetaOptIn setting round-trip
    /// <summary>
    /// **Validates: Requirements 1.2, 1.3, 1.4**
    ///
    /// For any boolean value (true or false), setting BetaOptIn on a SettingsViewModel,
    /// calling SaveSettingsToDict, then calling LoadSettingsFromDict on a fresh
    /// SettingsViewModel should produce the same BetaOptIn value.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property BetaOptIn_RoundTrips_ThroughSaveAndLoad()
    {
        return Prop.ForAll(Arb.Default.Bool(), value =>
        {
            // Arrange: create a SettingsViewModel and set BetaOptIn
            var source = new SettingsViewModel();
            source.BetaOptIn = value;

            // Act: save to dictionary, then load into a fresh SettingsViewModel
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            source.SaveSettingsToDict(dict);

            var target = new SettingsViewModel();
            target.LoadSettingsFromDict(dict);

            // Assert: the loaded value matches the original
            return (target.BetaOptIn == value)
                .Label($"Original={value}, Saved='{dict.GetValueOrDefault("BetaOptIn")}', Loaded={target.BetaOptIn}");
        });
    }

    /// <summary>
    /// Unit test: BetaOptIn defaults to false when the key is missing from the settings dictionary.
    /// Validates: Requirement 1.5
    /// </summary>
    [Fact]
    public void BetaOptIn_DefaultsFalse_WhenKeyMissing()
    {
        var vm = new SettingsViewModel();
        var emptyDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        vm.LoadSettingsFromDict(emptyDict);

        Assert.False(vm.BetaOptIn);
    }
}
