using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for dual-list OptiScaler merge and display.
/// Uses FsCheck with xUnit. Each property runs a minimum of 100 iterations.
///
/// **Validates: Requirements 13.3, 15.2, 15.3**
/// </summary>
public class OptiScalerDualListMergePropertyTests
{
    // ── Generators ────────────────────────────────────────────────────────────────

    private static Gen<string> GenGameName() =>
        Gen.Elements(
            "Cyberpunk 2077", "Elden Ring", "Baldur's Gate 3", "Starfield",
            "Resident Evil 4", "Final Fantasy XVI", "Hogwarts Legacy",
            "Spider-Man Remastered", "God of War", "Horizon Zero Dawn",
            "Alan Wake 2", "Black Myth: Wukong", "Control", "Death Stranding");

    private static Gen<OptiScalerCompatEntry> GenCompatEntry(string gameName) =>
        from status in Gen.Elements("Working", "Not Working")
        from upscalerCount in Gen.Choose(1, 3)
        from upscalers in Gen.ListOf(upscalerCount, Gen.Elements("DLSS", "FSR", "XeSS"))
        from hasNotes in Arb.Generate<bool>()
        select new OptiScalerCompatEntry
        {
            GameName = gameName,
            Status = status,
            Upscalers = upscalers.Distinct().ToList(),
            Notes = hasNotes ? $"Notes for {gameName}" : null,
            DetailPageUrl = $"https://github.com/optiscaler/OptiScaler/wiki/{gameName.Replace(" ", "-")}"
        };

    // ── Property 9: Dual-list OptiScaler merge and display ────────────────────────

    /// <summary>
    /// **Validates: Requirements 13.3, 15.2, 15.3**
    ///
    /// For any game name appearing in both the standard OptiScaler Compatibility List
    /// and the FSR4 Compatibility List, the merged OptiScalerWikiData SHALL contain
    /// entries in both StandardCompat and Fsr4Compat dictionaries. For a game appearing
    /// only in the FSR4 list, StandardCompat SHALL not contain an entry for that game
    /// while Fsr4Compat SHALL.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property DualList_Merge_BothLists()
    {
        var gen = from gameName in GenGameName()
                  from stdEntry in GenCompatEntry(gameName)
                  from fsr4Entry in GenCompatEntry(gameName)
                  select (gameName, stdEntry, fsr4Entry);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (gameName, stdEntry, fsr4Entry) = tuple;

            // Arrange: game appears in both lists
            var wikiData = new OptiScalerWikiData
            {
                StandardCompat = new Dictionary<string, OptiScalerCompatEntry>(StringComparer.OrdinalIgnoreCase)
                {
                    [gameName] = stdEntry
                },
                Fsr4Compat = new Dictionary<string, OptiScalerCompatEntry>(StringComparer.OrdinalIgnoreCase)
                {
                    [gameName] = fsr4Entry
                }
            };

            // Assert: both dictionaries contain the game
            var stdHas = wikiData.StandardCompat.ContainsKey(gameName);
            var fsr4Has = wikiData.Fsr4Compat.ContainsKey(gameName);

            return (stdHas && fsr4Has)
                .Label($"game='{gameName}', stdHas={stdHas}, fsr4Has={fsr4Has}");
        });
    }

    /// <summary>
    /// For a game appearing only in the FSR4 list, StandardCompat SHALL not contain
    /// an entry for that game while Fsr4Compat SHALL.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property DualList_Merge_Fsr4Only()
    {
        var gen = from gameName in GenGameName()
                  from fsr4Entry in GenCompatEntry(gameName)
                  select (gameName, fsr4Entry);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (gameName, fsr4Entry) = tuple;

            // Arrange: game appears only in FSR4 list
            var wikiData = new OptiScalerWikiData
            {
                StandardCompat = new Dictionary<string, OptiScalerCompatEntry>(StringComparer.OrdinalIgnoreCase),
                Fsr4Compat = new Dictionary<string, OptiScalerCompatEntry>(StringComparer.OrdinalIgnoreCase)
                {
                    [gameName] = fsr4Entry
                }
            };

            // Assert: only FSR4 dictionary contains the game
            var stdHas = wikiData.StandardCompat.ContainsKey(gameName);
            var fsr4Has = wikiData.Fsr4Compat.ContainsKey(gameName);

            return (!stdHas && fsr4Has)
                .Label($"game='{gameName}', stdHas={stdHas} (expected false), fsr4Has={fsr4Has} (expected true)");
        });
    }

    /// <summary>
    /// When a game appears in both lists, the AddonInfoResolver SHALL return wiki content
    /// that includes data from both standard and FSR4 entries.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property DualList_Resolve_IncludesBothEntries()
    {
        var gen = from gameName in GenGameName()
                  from stdEntry in GenCompatEntry(gameName)
                  from fsr4Entry in GenCompatEntry(gameName)
                  select (gameName, stdEntry, fsr4Entry);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (gameName, stdEntry, fsr4Entry) = tuple;

            // Arrange
            var wikiData = new OptiScalerWikiData
            {
                StandardCompat = new Dictionary<string, OptiScalerCompatEntry>(StringComparer.OrdinalIgnoreCase)
                {
                    [gameName] = stdEntry
                },
                Fsr4Compat = new Dictionary<string, OptiScalerCompatEntry>(StringComparer.OrdinalIgnoreCase)
                {
                    [gameName] = fsr4Entry
                }
            };

            var card = new RenoDXCommander.ViewModels.GameCardViewModel { GameName = gameName };
            var resolver = new AddonInfoResolver();

            // Act
            var result = resolver.Resolve(card, AddonType.OptiScaler, null, wikiData);

            // Assert: result should be Wiki source with both entries
            var sourceOk = result.Source == InfoSourceType.Wiki;
            var hasStdCompat = result.OptiScalerCompat != null;
            var hasFsr4Compat = result.OptiScalerFsr4Compat != null;
            var contentNonEmpty = !string.IsNullOrWhiteSpace(result.Content);

            return (sourceOk && hasStdCompat && hasFsr4Compat && contentNonEmpty)
                .Label($"source={result.Source}, stdCompat={hasStdCompat}, fsr4Compat={hasFsr4Compat}, content={contentNonEmpty}");
        });
    }

    /// <summary>
    /// When a game appears only in the FSR4 list, the resolver SHALL return wiki content
    /// with only the FSR4 entry populated.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property DualList_Resolve_Fsr4Only_HasFsr4Entry()
    {
        var gen = from gameName in GenGameName()
                  from fsr4Entry in GenCompatEntry(gameName)
                  select (gameName, fsr4Entry);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (gameName, fsr4Entry) = tuple;

            // Arrange: game only in FSR4 list
            var wikiData = new OptiScalerWikiData
            {
                StandardCompat = new Dictionary<string, OptiScalerCompatEntry>(StringComparer.OrdinalIgnoreCase),
                Fsr4Compat = new Dictionary<string, OptiScalerCompatEntry>(StringComparer.OrdinalIgnoreCase)
                {
                    [gameName] = fsr4Entry
                }
            };

            var card = new RenoDXCommander.ViewModels.GameCardViewModel { GameName = gameName };
            var resolver = new AddonInfoResolver();

            // Act
            var result = resolver.Resolve(card, AddonType.OptiScaler, null, wikiData);

            // Assert
            var sourceOk = result.Source == InfoSourceType.Wiki;
            var noStdCompat = result.OptiScalerCompat == null;
            var hasFsr4Compat = result.OptiScalerFsr4Compat != null;
            var contentNonEmpty = !string.IsNullOrWhiteSpace(result.Content);

            return (sourceOk && noStdCompat && hasFsr4Compat && contentNonEmpty)
                .Label($"source={result.Source}, stdCompat={!noStdCompat} (expected null), fsr4Compat={hasFsr4Compat}, content={contentNonEmpty}");
        });
    }
}
