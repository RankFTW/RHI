using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based test for Addons.ini parse/print round-trip.
/// Feature: reshade-addons, Property 1: Addons.ini parse/print round-trip
/// </summary>
public class AddonsIniParserRoundTripPropertyTests
{
    // ── Generators ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a valid section ID: two-digit alphanumeric string not in ExcludedSections.
    /// </summary>
    private static readonly Gen<string> GenSectionId =
        Gen.Elements(
            "01", "02", "03", "04", "05", "06", "07", "08", "09", "10",
            "11", "12", "13", "14", "15", "16", "17", "18", "19", "20",
            "22", "23", "24", "25", "27", "28", "29", "30")
        .Where(id => !AddonsIniParser.ExcludedSections.Contains(id));

    /// <summary>
    /// Generates a non-empty package name without newlines or '=' characters.
    /// </summary>
    private static readonly Gen<string> GenPackageName =
        Gen.Elements(
            "Generic Depth", "Effect Runtime Sync", "Shader Toggle",
            "RenoDX DevKit", "AddonX", "FrameGen Helper", "HDR Tools",
            "ReShade FPS", "DepthBuffer Access", "Screenshot Helper");

    /// <summary>
    /// Generates an optional string value (no newlines or '=').
    /// </summary>
    private static readonly Gen<string?> GenOptionalString =
        Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Elements<string?>(
                "Some description here",
                "Another description",
                "Tool for advanced users",
                "Provides depth buffer access"));

    /// <summary>
    /// Generates an optional URL string.
    /// </summary>
    private static readonly Gen<string?> GenOptionalUrl =
        Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Elements<string?>(
                "https://example.com/addon.addon64",
                "https://github.com/user/repo/releases/download/v1/file.addon32",
                "https://example.com/pack.zip"));

    /// <summary>
    /// Generates an optional repository URL.
    /// </summary>
    private static readonly Gen<string?> GenOptionalRepoUrl =
        Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Elements<string?>(
                "https://github.com/user/repo",
                "https://github.com/another/project"));

    /// <summary>
    /// Generates an optional effect install path.
    /// </summary>
    private static readonly Gen<string?> GenOptionalEffectPath =
        Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Elements<string?>(
                "reshade-shaders\\Shaders",
                "effects\\custom"));

    /// <summary>
    /// Generates a single valid AddonEntry with a unique section ID.
    /// </summary>
    private static readonly Gen<AddonEntry> GenAddonEntry =
        from sectionId in GenSectionId
        from packageName in GenPackageName
        from description in GenOptionalString
        from downloadUrl in GenOptionalUrl
        from downloadUrl32 in GenOptionalUrl
        from downloadUrl64 in GenOptionalUrl
        from repoUrl in GenOptionalRepoUrl
        from effectPath in GenOptionalEffectPath
        select new AddonEntry(
            SectionId: sectionId,
            PackageName: packageName,
            PackageDescription: description,
            DownloadUrl: downloadUrl,
            DownloadUrl32: downloadUrl32,
            DownloadUrl64: downloadUrl64,
            RepositoryUrl: repoUrl,
            EffectInstallPath: effectPath);

    /// <summary>
    /// Generates a list of 0–5 AddonEntry objects with unique section IDs.
    /// </summary>
    private static readonly Gen<List<AddonEntry>> GenAddonEntryList =
        GenAddonEntry.ListOf()
            .Select(entries => entries
                .GroupBy(e => e.SectionId)
                .Select(g => g.First())
                .Take(5)
                .ToList());

    // ── Property 1 ────────────────────────────────────────────────────────────────
    // Feature: reshade-addons, Property 1: Addons.ini parse/print round-trip
    // **Validates: Requirements 1.2, 1.6, 1.7**

    /// <summary>
    /// For any valid list of AddonEntry objects, pretty-printing them to INI format
    /// and then parsing the result back should produce an equivalent list of AddonEntry objects.
    /// </summary>
    [Property(MaxTest = 20)]
    public Property AddonsIni_ParsePrintRoundTrip_PreservesEntries()
    {
        return Prop.ForAll(
            Arb.From(GenAddonEntryList),
            (List<AddonEntry> original) =>
            {
                // Act: pretty-print then parse back
                var iniText = AddonsIniParser.PrettyPrint(original);
                var parsed = AddonsIniParser.Parse(iniText);

                // Assert: same count
                bool sameCount = parsed.Count == original.Count;

                // Assert: each entry matches field-by-field
                bool sameContent = original
                    .Zip(parsed, (o, p) =>
                        o.SectionId == p.SectionId &&
                        o.PackageName == p.PackageName &&
                        o.PackageDescription == p.PackageDescription &&
                        o.DownloadUrl == p.DownloadUrl &&
                        o.DownloadUrl32 == p.DownloadUrl32 &&
                        o.DownloadUrl64 == p.DownloadUrl64 &&
                        o.RepositoryUrl == p.RepositoryUrl &&
                        o.EffectInstallPath == p.EffectInstallPath)
                    .All(match => match);

                return (sameCount && sameContent)
                    .Label($"sameCount={sameCount}, sameContent={sameContent} " +
                           $"(original count={original.Count}, parsed count={parsed.Count})");
            });
    }
}
