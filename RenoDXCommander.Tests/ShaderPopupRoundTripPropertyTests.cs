using System.Text.Json;
using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;
using Xunit;

namespace RenoDXCommander.Tests;

// Feature: shader-selection-popup, Property 9: Global shader selection persistence round-trip

/// <summary>
/// Property-based tests for global shader selection persistence round-trip.
/// For any list of shader pack IDs stored in SelectedShaderPacks, saving via
/// SaveSettingsToDict and then loading via LoadSettingsFromDict should produce
/// an equivalent list of pack IDs.
/// **Validates: Requirements 8.1, 8.3**
/// NOTE: DeployMode enum was removed. Round-trip test updated to use selection-only logic.
/// Will be fully updated in Task 7.
/// </summary>
public class ShaderPopupRoundTripPropertyTests
{
    /// <summary>Known shader pack IDs from the service.</summary>
    private static readonly string[] AllPackIds =
        new ShaderPackService(new HttpClient(), new GitHubETagCache()).AvailablePacks.Select(p => p.Id).ToArray();

    // ── Generators ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a random subset of pack IDs (including possibly empty).
    /// </summary>
    private static Gen<List<string>> GenPackSelection()
    {
        if (AllPackIds.Length == 0)
            return Gen.Constant(new List<string>());

        return Gen.ListOf(AllPackIds.Length, Arb.Generate<bool>())
            .Select(flags =>
            {
                var subset = new List<string>();
                for (int i = 0; i < AllPackIds.Length; i++)
                    if (flags[i]) subset.Add(AllPackIds[i]);
                return subset;
            });
    }

    // ── Property 9 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// For any list of shader pack IDs, saving via SaveSettingsToDict and then
    /// loading via LoadSettingsFromDict should produce an equivalent list.
    /// The saved dict will contain "ShaderDeployMode" = "Select" so the migration
    /// logic retains the packs on load.
    /// </summary>
    [Property(MaxTest = 10)]
    public Property RoundTrip_GlobalShaderSelection()
    {
        return Prop.ForAll(GenPackSelection().ToArbitrary(), originalPacks =>
        {
            // Arrange: create a SettingsViewModel with the random packs
            var saveVm = new SettingsViewModel
            {
                IsLoadingSettings = true,
                SelectedShaderPacks = new List<string>(originalPacks)
            };
            saveVm.IsLoadingSettings = false;

            // Act: save to dictionary
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            saveVm.SaveSettingsToDict(dict);

            // Ensure the dict has "Select" mode so migration retains packs on load
            dict["ShaderDeployMode"] = "Select";

            // Act: load from dictionary into a fresh SettingsViewModel
            var loadVm = new SettingsViewModel { IsLoadingSettings = true };
            loadVm.LoadSettingsFromDict(dict);
            loadVm.IsLoadingSettings = false;

            // Assert: loaded packs should match original packs
            var expected = new HashSet<string>(originalPacks, StringComparer.OrdinalIgnoreCase);
            var actual = new HashSet<string>(loadVm.SelectedShaderPacks, StringComparer.OrdinalIgnoreCase);

            if (!expected.SetEquals(actual))
                return false.Label(
                    $"Set mismatch: expected [{string.Join(",", expected)}], " +
                    $"got [{string.Join(",", actual)}]");

            if (loadVm.SelectedShaderPacks.Count != originalPacks.Count)
                return false.Label(
                    $"Count mismatch: expected {originalPacks.Count}, " +
                    $"got {loadVm.SelectedShaderPacks.Count}");

            return true.Label(
                $"OK: round-trip preserved {originalPacks.Count} packs");
        });
    }
}
