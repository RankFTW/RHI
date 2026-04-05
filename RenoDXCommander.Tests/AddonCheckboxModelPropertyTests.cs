using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based test for addon selection checkbox model.
/// Feature: reshade-addons, Property 12: Addon selection checkbox model
/// </summary>
public class AddonCheckboxModelPropertyTests
{
    // ── Generators ────────────────────────────────────────────────────────────────

    private static readonly Gen<string> GenSectionId =
        Gen.Elements(
            "01", "02", "03", "04", "05", "06", "07", "08", "09", "10",
            "11", "12", "13", "14", "15", "16", "17", "18", "19", "20");

    private static readonly Gen<string> GenPackageName =
        from len in Gen.Choose(1, 20)
        from chars in Gen.ArrayOf(len, Gen.Elements(
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".ToCharArray()))
        select new string(chars);

    private static readonly Gen<AddonEntry> GenAddonEntry =
        from sectionId in GenSectionId
        from packageName in GenPackageName
        select new AddonEntry(
            SectionId: sectionId,
            PackageName: packageName,
            PackageDescription: null,
            DownloadUrl: null,
            DownloadUrl32: null,
            DownloadUrl64: null,
            RepositoryUrl: null,
            EffectInstallPath: null);

    /// <summary>
    /// Generates a list of AddonEntry with distinct PackageNames.
    /// </summary>
    private static readonly Gen<List<AddonEntry>> GenAddonList =
        from count in Gen.Choose(0, 10)
        from entries in Gen.ArrayOf(count, GenAddonEntry)
        select entries
            .GroupBy(e => e.PackageName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

    /// <summary>
    /// Generates a current selection that is a random subset of the given package names,
    /// plus possibly some names not in the available set (to test robustness).
    /// </summary>
    private static Gen<List<string>?> GenCurrentSelection(List<string> availableNames) =>
        Gen.OneOf(
            Gen.Constant<List<string>?>(null),
            from subsetMask in Gen.ArrayOf(availableNames.Count, Gen.Elements(true, false))
            let subset = availableNames.Where((_, i) => subsetMask[i]).ToList()
            from extraCount in Gen.Choose(0, 2)
            from extras in Gen.ArrayOf(extraCount, GenPackageName)
            select (List<string>?)subset.Concat(extras).Distinct(StringComparer.OrdinalIgnoreCase).ToList());

    // ── Property 12 ───────────────────────────────────────────────────────────────
    // Feature: reshade-addons, Property 12: Addon selection checkbox model
    // **Validates: Requirements 8.1, 8.2, 8.3, 8.4**

    /// <summary>
    /// For any set of available addons and any current per-game selection,
    /// the computed checkbox model should contain one entry per available addon,
    /// and each entry's IsChecked state should be true if and only if the addon's
    /// package name is in the current selection.
    /// </summary>
    [Property(MaxTest = 20)]
    public Property CheckboxModel_OneEntryPerAddon_IsCheckedMatchesSelection()
    {
        return Prop.ForAll(
            Arb.From(GenAddonList),
            (List<AddonEntry> availableAddons) =>
            {
                var availableNames = availableAddons.Select(a => a.PackageName).ToList();

                return Prop.ForAll(
                    Arb.From(GenCurrentSelection(availableNames)),
                    (List<string>? currentSelection) =>
                    {
                        var model = AddonPopupHelper.ComputeCheckboxModel(availableAddons, currentSelection);

                        // 1. Model has one entry per available addon
                        bool correctCount = model.Count == availableAddons.Count;

                        // 2. Each entry's PackageName matches the corresponding available addon
                        bool correctNames = model
                            .Select(m => m.PackageName)
                            .SequenceEqual(availableAddons.Select(a => a.PackageName));

                        // 3. Each entry's IsChecked is true iff the package name is in the current selection
                        var selectedSet = new HashSet<string>(
                            currentSelection ?? [],
                            StringComparer.OrdinalIgnoreCase);

                        bool correctCheckedState = model.All(m =>
                            m.IsChecked == selectedSet.Contains(m.PackageName));

                        return (correctCount && correctNames && correctCheckedState)
                            .Label($"count={model.Count}/{availableAddons.Count}, " +
                                   $"namesMatch={correctNames}, checkedMatch={correctCheckedState}, " +
                                   $"selection={currentSelection?.Count.ToString() ?? "null"}");
                    });
            });
    }
}
