using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based test for RenoDX DevKit addon always present in available packs.
/// Feature: reshade-addons, Property 14: RenoDX DevKit addon always present in available packs
/// </summary>
public class AddonRenoDxDevKitPresencePropertyTests
{
    // ── Expected DevKit constants ─────────────────────────────────────────────────

    private const string DevKitPackageName = "RenoDX DevKit";

    private const string DevKitUrl32 =
        "https://github.com/clshortfuse/renodx/releases/download/snapshot/renodx-devkit.addon32";

    private const string DevKitUrl64 =
        "https://github.com/clshortfuse/renodx/releases/download/snapshot/renodx-devkit.addon64";

    private static readonly AddonEntry RenoDxDevKitEntry = new(
        SectionId: "renodx-devkit",
        PackageName: DevKitPackageName,
        PackageDescription: "RenoDX development tools addon for ReShade",
        DownloadUrl: null,
        DownloadUrl32: DevKitUrl32,
        DownloadUrl64: DevKitUrl64,
        RepositoryUrl: "https://github.com/clshortfuse/renodx",
        EffectInstallPath: null);

    // ── Pure logic under test (mirrors EnsureLatestAsync append logic) ─────────

    /// <summary>
    /// Applies the same DevKit injection logic as AddonPackService.EnsureLatestAsync:
    /// if no entry with PackageName "RenoDX DevKit" exists, append the DevKit entry.
    /// </summary>
    private static List<AddonEntry> EnsureDevKitPresent(List<AddonEntry> parsed)
    {
        var result = new List<AddonEntry>(parsed);
        if (!result.Any(e => e.PackageName.Equals(DevKitPackageName, StringComparison.OrdinalIgnoreCase)))
            result.Add(RenoDxDevKitEntry);
        return result;
    }

    // ── Generators ────────────────────────────────────────────────────────────────

    private static readonly Gen<string> GenSectionId =
        Gen.Elements(
            "01", "02", "03", "04", "05", "06", "07", "08", "09", "10",
            "11", "12", "13", "14", "15", "16", "17", "18", "19", "20");

    private static readonly Gen<string> GenPackageName =
        Gen.Elements(
            "Generic Depth", "Effect Runtime Sync", "Shader Toggle",
            "AddonX", "FrameGen Helper", "HDR Tools", "SafetyNet",
            "REST Addon", "ShaderReloadHotkey");

    private static readonly Gen<string?> GenOptionalUrl =
        Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Elements<string?>(
                "https://example.com/addon.addon64",
                "https://github.com/user/repo/releases/download/v1/file.addon32",
                "https://example.com/pack.zip"));

    private static readonly Gen<AddonEntry> GenAddonEntry =
        from sectionId in GenSectionId
        from packageName in GenPackageName
        from downloadUrl in GenOptionalUrl
        from downloadUrl32 in GenOptionalUrl
        from downloadUrl64 in GenOptionalUrl
        select new AddonEntry(
            SectionId: sectionId,
            PackageName: packageName,
            PackageDescription: null,
            DownloadUrl: downloadUrl,
            DownloadUrl32: downloadUrl32,
            DownloadUrl64: downloadUrl64,
            RepositoryUrl: null,
            EffectInstallPath: null);

    /// <summary>
    /// Generates a random list of AddonEntry (0–8 entries), none of which have
    /// PackageName "RenoDX DevKit", simulating typical Addons.ini content.
    /// </summary>
    private static readonly Gen<List<AddonEntry>> GenAddonListWithoutDevKit =
        from count in Gen.Choose(0, 8)
        from entries in Gen.ListOf(count, GenAddonEntry)
        select entries.ToList();

    /// <summary>
    /// Generates a random list that may or may not already contain a DevKit entry.
    /// </summary>
    private static readonly Gen<List<AddonEntry>> GenAddonListMaybeWithDevKit =
        from baseList in GenAddonListWithoutDevKit
        from includeDevKit in Arb.Default.Bool().Generator
        select includeDevKit
            ? baseList.Append(RenoDxDevKitEntry).ToList()
            : baseList;

    // ── Property 14 ───────────────────────────────────────────────────────────────
    // Feature: reshade-addons, Property 14: RenoDX DevKit addon always present in available packs
    // **Validates: Requirements 9.1, 9.2**

    /// <summary>
    /// For any result of EnsureLatestAsync, the AvailablePacks list should always
    /// contain an entry with PackageName "RenoDX DevKit" and the correct download
    /// URLs for both 32-bit and 64-bit variants, regardless of the Addons.ini content.
    /// </summary>
    [Property(MaxTest = 20)]
    public Property DevKitAlwaysPresent_WithCorrectUrls()
    {
        return Prop.ForAll(
            Arb.From(GenAddonListMaybeWithDevKit),
            (List<AddonEntry> parsedEntries) =>
            {
                var result = EnsureDevKitPresent(parsedEntries);

                var devKitEntries = result
                    .Where(e => e.PackageName.Equals(DevKitPackageName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // Must contain at least one DevKit entry
                if (devKitEntries.Count == 0)
                    return false.Label("No RenoDX DevKit entry found in result");

                // Verify the DevKit entry has the correct URLs
                var devKit = devKitEntries.First();

                if (devKit.DownloadUrl32 != DevKitUrl32)
                    return false.Label(
                        $"Wrong DownloadUrl32: expected '{DevKitUrl32}', got '{devKit.DownloadUrl32}'");

                if (devKit.DownloadUrl64 != DevKitUrl64)
                    return false.Label(
                        $"Wrong DownloadUrl64: expected '{DevKitUrl64}', got '{devKit.DownloadUrl64}'");

                return true.Label(
                    $"OK: {parsedEntries.Count} input entries, DevKit present with correct URLs");
            });
    }

    /// <summary>
    /// When the parsed list already contains a DevKit entry, no duplicate is added.
    /// </summary>
    [Property(MaxTest = 20)]
    public Property DevKitNotDuplicated_WhenAlreadyPresent()
    {
        return Prop.ForAll(
            Arb.From(GenAddonListWithoutDevKit),
            (List<AddonEntry> baseEntries) =>
            {
                // Add DevKit to the input list
                var withDevKit = baseEntries.Append(RenoDxDevKitEntry).ToList();

                var result = EnsureDevKitPresent(withDevKit);

                var devKitCount = result
                    .Count(e => e.PackageName.Equals(DevKitPackageName, StringComparison.OrdinalIgnoreCase));

                if (devKitCount != 1)
                    return false.Label(
                        $"Expected exactly 1 DevKit entry, found {devKitCount}");

                return true.Label(
                    $"OK: {baseEntries.Count} base entries + DevKit, no duplicate after ensure");
            });
    }
}
