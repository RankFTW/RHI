// Feature: downloads-folder-reorganisation, Property 1: File classification correctness
using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Services;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests verifying that <see cref="DownloadsMigrationService.ClassifyFile"/>
/// returns exactly one of the five category paths, matching the highest-priority rule.
/// </summary>
public class FileClassificationPropertyTests
{
    // ── Helpers ──────────────────────────────────────────────────────────

    private static readonly string[] AllCategories =
    [
        DownloadPaths.RenoDX,
        DownloadPaths.FrameLimiter,
        DownloadPaths.Luma,
        DownloadPaths.Shaders,
        DownloadPaths.Misc
    ];

    /// <summary>
    /// Reference oracle that mirrors the priority rules from the design doc.
    /// </summary>
    private static string ExpectedCategory(string fileName)
    {
        // Priority 1: renodx-*.addon64 or renodx-*.addon32
        if (fileName.StartsWith("renodx-", StringComparison.OrdinalIgnoreCase) &&
            (fileName.EndsWith(".addon64", StringComparison.OrdinalIgnoreCase) ||
             fileName.EndsWith(".addon32", StringComparison.OrdinalIgnoreCase)))
            return DownloadPaths.RenoDX;

        // Priority 2: relimiter.* or dc_lite.* or ul_meta.json or dc_meta.json
        if (fileName.StartsWith("relimiter.", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("dc_lite.", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("ul_meta.json", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("dc_meta.json", StringComparison.OrdinalIgnoreCase))
            return DownloadPaths.FrameLimiter;

        // Priority 3: luma_*
        if (fileName.StartsWith("luma_", StringComparison.OrdinalIgnoreCase))
            return DownloadPaths.Luma;

        // Priority 4: *.7z or *.zip
        var ext = Path.GetExtension(fileName);
        if (ext.Equals(".7z", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            return DownloadPaths.Shaders;

        // Priority 5: everything else
        return DownloadPaths.Misc;
    }

    // ── Generators ──────────────────────────────────────────────────────

    /// <summary>
    /// Generates filenames that cover all five categories plus edge cases:
    /// mixed case, partial matches, empty extensions, and random strings.
    /// </summary>
    private static Arbitrary<string> FileNameArbitrary()
    {
        var alphaNum = Gen.Elements(
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-.".ToCharArray());
        var shortString = Gen.ArrayOf(Gen.Choose(1, 20).SelectMany(n => Gen.ArrayOf(n, alphaNum)))
            .Select(arr => arr.Length > 0 ? new string(arr[0]) : "file");

        // Category 1: renodx addons
        var renodxGen = Gen.Elements(".addon64", ".addon32")
            .SelectMany(ext => shortString.Select(mid => $"renodx-{mid}{ext}"));

        // Category 2: frame limiter files
        var relimiterGen = shortString.Select(s => $"relimiter.{s}");
        var dcLiteGen = shortString.Select(s => $"dc_lite.{s}");
        var metaGen = Gen.Elements("ul_meta.json", "dc_meta.json");
        var frameLimiterGen = Gen.OneOf(relimiterGen, dcLiteGen, metaGen);

        // Category 3: luma files
        var lumaGen = shortString.Select(s => $"luma_{s}");

        // Category 4: shader archives
        var shaderGen = Gen.Elements(".7z", ".zip")
            .SelectMany(ext => shortString.Select(name => $"{name}{ext}"));

        // Category 5: misc (random filenames that don't match other patterns)
        var miscExts = Gen.Elements(".exe", ".dll", ".txt", ".dat", ".cfg", ".log", "");
        var miscGen = miscExts.SelectMany(ext =>
            shortString.Where(s =>
                !s.StartsWith("renodx-", StringComparison.OrdinalIgnoreCase) &&
                !s.StartsWith("relimiter.", StringComparison.OrdinalIgnoreCase) &&
                !s.StartsWith("dc_lite.", StringComparison.OrdinalIgnoreCase) &&
                !s.StartsWith("luma_", StringComparison.OrdinalIgnoreCase) &&
                !s.Equals("ul_meta.json", StringComparison.OrdinalIgnoreCase) &&
                !s.Equals("dc_meta.json", StringComparison.OrdinalIgnoreCase))
            .Select(name => $"{name}{ext}"));

        // Edge cases: mixed case variants, partial matches
        var mixedCaseRenodx = Gen.Elements(
            "RENODX-test.ADDON64", "Renodx-Foo.addon32", "renodx-BAR.Addon64");
        var mixedCaseMeta = Gen.Elements(
            "UL_META.JSON", "Dc_Meta.Json", "DC_META.json");
        var mixedCaseLuma = Gen.Elements(
            "LUMA_test", "Luma_Foo.zip", "luma_BAR.dat");
        var partialMatches = Gen.Elements(
            "renodx-noext", "renodx.addon64", "relimiter", "dc_lite",
            "luma", "lumafile.zip", "renodx-test.addon", "");
        var edgeCases = Gen.OneOf(mixedCaseRenodx, mixedCaseMeta, mixedCaseLuma, partialMatches);

        // Weighted: ensure all categories are well-represented
        var combined = Gen.Frequency(
            Tuple.Create(3, renodxGen),
            Tuple.Create(3, frameLimiterGen),
            Tuple.Create(3, lumaGen),
            Tuple.Create(3, shaderGen),
            Tuple.Create(3, miscGen),
            Tuple.Create(2, edgeCases));

        return combined.ToArbitrary();
    }

    // ── Properties ──────────────────────────────────────────────────────

    /// <summary>
    /// Feature: downloads-folder-reorganisation, Property 1: File classification correctness
    ///
    /// **Validates: Requirements 8.5, 8.6, 8.7, 8.8, 8.9**
    ///
    /// For any filename string, ClassifyFile shall return exactly one of the
    /// five category paths, and the returned path shall match the highest-priority
    /// rule that applies.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ClassifyFile_Returns_Correct_Category_Per_Priority_Rules()
    {
        return Prop.ForAll(FileNameArbitrary(), (string fileName) =>
        {
            var result = DownloadsMigrationService.ClassifyFile(fileName);
            var expected = ExpectedCategory(fileName);

            var isValidCategory = AllCategories.Contains(result);
            var matchesExpected = result == expected;

            return (isValidCategory && matchesExpected)
                .Label($"fileName=\"{fileName}\", result=\"{Path.GetFileName(result)}\", " +
                       $"expected=\"{Path.GetFileName(expected)}\", " +
                       $"isValidCategory={isValidCategory}, matchesExpected={matchesExpected}");
        });
    }
}
