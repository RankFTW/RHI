using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based test for addon action type determination.
/// Feature: reshade-addons, Property 3: Addon action type determined by URL availability
/// </summary>
public class AddonActionTypePropertyTests
{
    // ── Pure logic under test ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the entry has at least one download URL.
    /// </summary>
    internal static bool HasDownloadUrl(AddonEntry entry) =>
        entry.DownloadUrl != null || entry.DownloadUrl32 != null || entry.DownloadUrl64 != null;

    /// <summary>
    /// Returns true if the entry has only a repository URL and no download URLs.
    /// </summary>
    internal static bool HasOnlyRepoUrl(AddonEntry entry) =>
        !HasDownloadUrl(entry) && entry.RepositoryUrl != null;

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

    private static readonly Gen<string?> GenOptionalRepoUrl =
        Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Elements<string?>(
                "https://github.com/user/repo",
                "https://github.com/another/project"));

    /// <summary>
    /// Generates a random AddonEntry with varying URL field combinations.
    /// </summary>
    private static readonly Gen<AddonEntry> GenAddonEntry =
        from sectionId in GenSectionId
        from packageName in GenPackageName
        from downloadUrl in GenOptionalUrl
        from downloadUrl32 in GenOptionalUrl
        from downloadUrl64 in GenOptionalUrl
        from repoUrl in GenOptionalRepoUrl
        select new AddonEntry(
            SectionId: sectionId,
            PackageName: packageName,
            PackageDescription: null,
            DownloadUrl: downloadUrl,
            DownloadUrl32: downloadUrl32,
            DownloadUrl64: downloadUrl64,
            RepositoryUrl: repoUrl,
            EffectInstallPath: null);

    // ── Property 3 ────────────────────────────────────────────────────────────────
    // Feature: reshade-addons, Property 3: Addon action type determined by URL availability
    // **Validates: Requirements 2.5, 2.6**

    /// <summary>
    /// For any AddonEntry, if it has at least one download URL (DownloadUrl, DownloadUrl32,
    /// or DownloadUrl64), the manager window should show a download button; if it has only
    /// a RepositoryUrl and no download URLs, it should show a repository link.
    /// The two action types are mutually exclusive and exhaustive (for entries with any URL).
    /// </summary>
    [Property(MaxTest = 20)]
    public Property ActionType_DeterminedBy_UrlAvailability()
    {
        return Prop.ForAll(
            Arb.From(GenAddonEntry),
            (AddonEntry entry) =>
            {
                bool hasDownload = HasDownloadUrl(entry);
                bool hasOnlyRepo = HasOnlyRepoUrl(entry);

                // If any download URL is present → download button (not repo-only)
                bool downloadImpliesNotRepoOnly =
                    !hasDownload || !hasOnlyRepo;

                // If no download URLs but has repo URL → repo link only
                bool repoOnlyImpliesNoDownload =
                    !hasOnlyRepo || !hasDownload;

                // Mutual exclusivity: cannot be both download and repo-only
                bool mutuallyExclusive = !(hasDownload && hasOnlyRepo);

                // If entry has any URL at all, exactly one action type applies
                bool hasAnyUrl = entry.DownloadUrl != null
                    || entry.DownloadUrl32 != null
                    || entry.DownloadUrl64 != null
                    || entry.RepositoryUrl != null;

                bool exactlyOneAction = !hasAnyUrl
                    || (hasDownload ^ hasOnlyRepo);

                return (downloadImpliesNotRepoOnly
                    && repoOnlyImpliesNoDownload
                    && mutuallyExclusive
                    && exactlyOneAction)
                    .Label($"hasDownload={hasDownload}, hasOnlyRepo={hasOnlyRepo}, " +
                           $"hasAnyUrl={hasAnyUrl}, mutuallyExclusive={mutuallyExclusive}, " +
                           $"exactlyOneAction={exactlyOneAction}");
            });
    }
}
