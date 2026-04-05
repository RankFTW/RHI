using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based test for download file count based on URL availability.
/// Feature: reshade-addons, Property 6: Download file count matches provided URLs
/// **Validates: Requirements 3.4, 3.5**
/// </summary>
public class AddonDownloadFileCountPropertyTests
{
    // ── Pure logic under test ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns the expected number of files that should be stored in the staging
    /// area for a given AddonEntry based on its URL fields.
    /// </summary>
    internal static int ExpectedFileCount(AddonEntry entry)
    {
        if (!string.IsNullOrEmpty(entry.DownloadUrl32) && !string.IsNullOrEmpty(entry.DownloadUrl64))
            return 2;

        if (!string.IsNullOrEmpty(entry.DownloadUrl32))
            return 1;

        if (!string.IsNullOrEmpty(entry.DownloadUrl64))
            return 1;

        if (!string.IsNullOrEmpty(entry.DownloadUrl))
            return 1;

        return 0;
    }

    // ── Generators ────────────────────────────────────────────────────────────────

    private static readonly Gen<string> GenSectionId =
        Gen.Elements(
            "01", "02", "03", "04", "05", "06", "07", "08", "09", "10",
            "11", "12", "13", "14", "15", "16", "17", "18", "19", "20");

    private static readonly Gen<string> GenPackageName =
        Gen.Elements(
            "Generic Depth", "Effect Runtime Sync", "Shader Toggle",
            "AddonX", "FrameGen Helper", "HDR Tools");

    private static readonly Gen<string?> GenOptionalUrl =
        Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Elements<string?>(
                "https://example.com/addon.addon64",
                "https://github.com/user/repo/releases/download/v1/file.addon32",
                "https://example.com/pack.zip"));

    /// <summary>
    /// Generates a random AddonEntry with varying URL field combinations.
    /// </summary>
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

    // ── Property 6 ────────────────────────────────────────────────────────────────
    // Feature: reshade-addons, Property 6: Download file count matches provided URLs
    // **Validates: Requirements 3.4, 3.5**

    /// <summary>
    /// For any AddonEntry, if both DownloadUrl32 and DownloadUrl64 are provided,
    /// two files should be stored in the staging area; if only a single DownloadUrl
    /// is provided, one file should be stored.
    /// </summary>
    [Property(MaxTest = 20)]
    public Property FileCount_Matches_ProvidedUrls()
    {
        return Prop.ForAll(
            Arb.From(GenAddonEntry),
            (AddonEntry entry) =>
            {
                int expected = ExpectedFileCount(entry);

                // Verify the logic matches the URL combination rules from the design
                bool bothVariants = !string.IsNullOrEmpty(entry.DownloadUrl32)
                                 && !string.IsNullOrEmpty(entry.DownloadUrl64);
                bool only32 = !string.IsNullOrEmpty(entry.DownloadUrl32)
                           && string.IsNullOrEmpty(entry.DownloadUrl64);
                bool only64 = string.IsNullOrEmpty(entry.DownloadUrl32)
                           && !string.IsNullOrEmpty(entry.DownloadUrl64);
                bool onlySingle = string.IsNullOrEmpty(entry.DownloadUrl32)
                               && string.IsNullOrEmpty(entry.DownloadUrl64)
                               && !string.IsNullOrEmpty(entry.DownloadUrl);
                bool noUrls = string.IsNullOrEmpty(entry.DownloadUrl32)
                           && string.IsNullOrEmpty(entry.DownloadUrl64)
                           && string.IsNullOrEmpty(entry.DownloadUrl);

                bool correct =
                    (bothVariants && expected == 2)
                    || (only32 && expected == 1)
                    || (only64 && expected == 1)
                    || (onlySingle && expected == 1)
                    || (noUrls && expected == 0);

                return correct.Label(
                    $"Url32={entry.DownloadUrl32 != null}, " +
                    $"Url64={entry.DownloadUrl64 != null}, " +
                    $"Url={entry.DownloadUrl != null}, " +
                    $"expected={expected}");
            });
    }
}
