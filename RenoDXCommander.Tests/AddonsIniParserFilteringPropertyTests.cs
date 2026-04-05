using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based test for Addons.ini parse filtering.
/// Feature: reshade-addons, Property 2: Parse filtering excludes invalid and excluded entries
/// </summary>
public class AddonsIniParserFilteringPropertyTests
{
    // ── Generators ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a valid (non-excluded) section ID.
    /// </summary>
    private static readonly Gen<string> GenValidSectionId =
        Gen.Elements(
            "01", "02", "03", "04", "05", "06", "07", "08", "09", "10",
            "11", "12", "13", "14", "15", "16", "17", "18", "19", "20",
            "22", "23", "24", "25", "27", "28", "29", "30")
        .Where(id => !AddonsIniParser.ExcludedSections.Contains(id));

    /// <summary>
    /// Generates an excluded section ID (00, 21, 26).
    /// </summary>
    private static readonly Gen<string> GenExcludedSectionId =
        Gen.Elements("00", "21", "26");

    /// <summary>
    /// Generates a non-empty package name.
    /// </summary>
    private static readonly Gen<string> GenPackageName =
        Gen.Elements(
            "Generic Depth", "Effect Runtime Sync", "Shader Toggle",
            "AddonX", "FrameGen Helper", "HDR Tools",
            "ReShade FPS", "DepthBuffer Access", "Screenshot Helper");

    /// <summary>
    /// Generates an optional URL string.
    /// </summary>
    private static readonly Gen<string?> GenOptionalUrl =
        Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Elements<string?>(
                "https://example.com/addon.addon64",
                "https://github.com/user/repo/releases/download/v1/file.addon32"));

    /// <summary>
    /// Builds a valid INI section block for a given section ID and package name.
    /// </summary>
    private static Gen<string> GenValidSection(string sectionId, string packageName) =>
        from url in GenOptionalUrl
        select $"[{sectionId}]\nPackageName={packageName}"
             + (url != null ? $"\nDownloadUrl={url}" : "");

    /// <summary>
    /// Builds an excluded INI section block (section ID in ExcludedSections).
    /// </summary>
    private static readonly Gen<string> GenExcludedSection =
        from id in GenExcludedSectionId
        from name in GenPackageName
        select $"[{id}]\nPackageName={name}";

    /// <summary>
    /// Builds a commented-out INI section block.
    /// </summary>
    private static readonly Gen<string> GenCommentedSection =
        from id in GenValidSectionId
        from name in GenPackageName
        from style in Gen.Elements(";", "# ")
        select $"{style}[{id}]\n{style}PackageName={name}";

    /// <summary>
    /// Builds a malformed INI section block (missing PackageName).
    /// </summary>
    private static readonly Gen<string> GenMalformedSection =
        from id in GenValidSectionId
        select $"[{id}]\nDownloadUrl=https://example.com/file.addon64";

    // ── Property 2 ────────────────────────────────────────────────────────────────
    // Feature: reshade-addons, Property 2: Parse filtering excludes invalid and excluded entries
    // **Validates: Requirements 1.3, 1.5**

    /// <summary>
    /// For any INI content containing a mix of valid sections, excluded section IDs,
    /// commented-out sections, and malformed sections, the parser output should contain
    /// only entries with valid required fields whose section IDs are not in the exclusion
    /// list and are not commented out.
    /// </summary>
    [Property(MaxTest = 20)]
    public Property Parse_Filtering_Excludes_Invalid_And_Excluded_Entries()
    {
        // Generate 1-3 valid sections with unique IDs, plus 0-2 of each invalid kind
        var gen =
            from validIds in GenValidSectionId.ListOf()
                .Select(ids => ids.GroupBy(x => x).Select(g => g.Key).Take(3).ToList())
            from validNames in GenPackageName.ListOf()
                .Select(names => names.Take(Math.Max(validIds.Count, 1)).ToList())
            let validCount = Math.Min(validIds.Count, validNames.Count)
            let validPairs = validIds.Take(validCount)
                .Zip(validNames.Take(validCount), (id, name) => (id, name)).ToList()
            from validSections in GenSequence(
                validPairs.Select(p => GenValidSection(p.id, p.name)).ToList())
            from excludedSections in GenExcludedSection.ListOf()
                .Select(l => l.Take(2).ToList())
            from commentedSections in GenCommentedSection.ListOf()
                .Select(l => l.Take(2).ToList())
            from malformedSections in GenMalformedSection.ListOf()
                .Select(l => l.Take(2).ToList())
            from allSections in Shuffle(
                validSections
                    .Concat(excludedSections)
                    .Concat(commentedSections)
                    .Concat(malformedSections)
                    .ToList())
            select new
            {
                IniContent = string.Join("\n\n", allSections),
                ExpectedIds = validPairs.Select(p => p.id).ToHashSet(),
                ExpectedNames = validPairs.Select(p => p.name).ToList(),
                ValidCount = validCount
            };

        return Prop.ForAll(
            Arb.From(gen),
            data =>
            {
                var parsed = AddonsIniParser.Parse(data.IniContent);

                // All parsed entries must have non-excluded section IDs
                bool noExcluded = parsed.All(e =>
                    !AddonsIniParser.ExcludedSections.Contains(e.SectionId));

                // All parsed entries must have a non-empty PackageName
                bool allHavePackageName = parsed.All(e =>
                    !string.IsNullOrWhiteSpace(e.PackageName));

                // Parsed entries should be exactly the valid sections we generated
                bool correctCount = parsed.Count == data.ValidCount;

                // Each expected valid entry should appear in the output
                bool allValidPresent = data.ExpectedIds.All(id =>
                    parsed.Any(e => e.SectionId == id));

                return (noExcluded && allHavePackageName && correctCount && allValidPresent)
                    .Label($"noExcluded={noExcluded}, allHavePackageName={allHavePackageName}, " +
                           $"correctCount={correctCount} (expected={data.ValidCount}, actual={parsed.Count}), " +
                           $"allValidPresent={allValidPresent}");
            });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sequences a list of generators into a generator of a list.
    /// </summary>
    private static Gen<List<string>> GenSequence(List<Gen<string>> gens)
    {
        if (gens.Count == 0)
            return Gen.Constant(new List<string>());

        return gens.Aggregate(
            Gen.Constant(new List<string>()),
            (acc, g) =>
                from list in acc
                from item in g
                select new List<string>(list) { item });
    }

    /// <summary>
    /// Shuffles a list using FsCheck's Gen for randomness.
    /// </summary>
    private static Gen<List<string>> Shuffle(List<string> items)
    {
        if (items.Count <= 1)
            return Gen.Constant(items);

        return Gen.Shuffle(items.ToArray()).Select(arr => arr.ToList());
    }
}
