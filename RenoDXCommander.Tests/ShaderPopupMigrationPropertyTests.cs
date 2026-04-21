using System.Text.Json;
using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;
using Xunit;

namespace RenoDXCommander.Tests;

// Feature: shader-selection-popup, Property 11: Migration from old deploy modes clears or retains selection

/// <summary>
/// Property-based tests for migration from old deploy modes.
/// When loading settings, if the saved ShaderDeployMode is Select, the loaded
/// SelectedShaderPacks should equal the saved list. If the saved mode is any
/// other value (Off, Minimum, All, User), the loaded SelectedShaderPacks should
/// be empty.
/// **Validates: Requirements 8.5**
/// NOTE: DeployMode enum was removed. Tests use string-based mode values.
/// Will be fully updated in Task 7.
/// </summary>
public class ShaderPopupMigrationPropertyTests
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

    /// <summary>Generates a random deploy mode string (old values + Select).</summary>
    private static Gen<string> GenAnyModeString()
    {
        return Gen.Elements("Off", "Minimum", "All", "User", "Select");
    }

    // ── Property 11 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// For any deploy mode string and any saved SelectedShaderPacks list,
    /// after loading settings via LoadSettingsFromDict:
    ///   - If the saved mode was "Select", SelectedShaderPacks equals the saved list
    ///   - If the saved mode was any other value, SelectedShaderPacks is empty
    /// </summary>
    [Property(MaxTest = 30)]
    public Property Migration_ClearsOrRetainsSelection()
    {
        var gen = GenAnyModeString().SelectMany(mode =>
            GenPackSelection().Select(packs => (mode, packs)));

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (savedMode, savedPacks) = tuple;

            var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ShaderDeployMode"] = savedMode,
                ["SelectedShaderPacks"] = JsonSerializer.Serialize(savedPacks)
            };

            var vm = new SettingsViewModel { IsLoadingSettings = true };
            vm.LoadSettingsFromDict(settings);
            vm.IsLoadingSettings = false;

            if (savedMode == "Select")
            {
                var expected = new HashSet<string>(savedPacks, StringComparer.OrdinalIgnoreCase);
                var actual = new HashSet<string>(vm.SelectedShaderPacks, StringComparer.OrdinalIgnoreCase);

                if (!expected.SetEquals(actual))
                    return false.Label(
                        $"Select mode: packs mismatch — " +
                        $"expected [{string.Join(",", expected)}], " +
                        $"got [{string.Join(",", actual)}]");

                if (vm.SelectedShaderPacks.Count != savedPacks.Count)
                    return false.Label(
                        $"Select mode: count mismatch — " +
                        $"expected {savedPacks.Count}, got {vm.SelectedShaderPacks.Count}");
            }
            else
            {
                if (vm.SelectedShaderPacks.Count != 0)
                    return false.Label(
                        $"Non-Select mode ({savedMode}): expected empty packs, " +
                        $"got {vm.SelectedShaderPacks.Count} packs " +
                        $"[{string.Join(",", vm.SelectedShaderPacks)}]");
            }

            return true.Label(
                $"OK: mode={savedMode}, packs={savedPacks.Count}" +
                (savedMode == "Select" ? " (retained)" : " (cleared)"));
        });
    }
}
