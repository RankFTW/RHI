using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for AddonInfoResolver three-tier content resolution.
/// Uses FsCheck with xUnit. Each property runs a minimum of 100 iterations.
/// </summary>
public class AddonInfoResolverPropertyTests
{
    private readonly AddonInfoResolver _resolver = new();

    // ── Generators ────────────────────────────────────────────────────────────────

    private static Gen<string> GenGameName() =>
        Gen.Elements(
            "Cyberpunk 2077", "Elden Ring", "Baldur's Gate 3", "Starfield",
            "Resident Evil 4", "Final Fantasy XVI", "Hogwarts Legacy",
            "Spider-Man Remastered", "God of War", "Horizon Zero Dawn",
            "Death Stranding", "Control");

    private static Gen<AddonType> GenAddonType() =>
        Gen.Elements(
            AddonType.REFramework, AddonType.ReShade, AddonType.RenoDX,
            AddonType.ReLimiter, AddonType.DisplayCommander,
            AddonType.OptiScaler, AddonType.Luma);

    /// <summary>Addons eligible for wiki tier 2 resolution.</summary>
    private static Gen<AddonType> GenWikiEligibleAddon() =>
        Gen.Elements(AddonType.RenoDX, AddonType.OptiScaler, AddonType.Luma);

    private static Gen<GameNoteEntry> GenGameNoteEntry() =>
        from notes in Gen.Elements("Custom note for this game", "Use special settings", "Known issue with HDR")
        from hasUrl in Arb.Generate<bool>()
        from hasLabel in Arb.Generate<bool>()
        select new GameNoteEntry
        {
            Notes = notes,
            NotesUrl = hasUrl ? "https://example.com/notes" : null,
            NotesUrlLabel = hasLabel ? "View details" : null
        };

    /// <summary>
    /// Creates a GameCardViewModel with the given game name and optional wiki data.
    /// </summary>
    private static GameCardViewModel CreateCard(
        string gameName,
        GameMod? mod = null,
        string? nameUrl = null,
        LumaMod? lumaMod = null)
    {
        var card = new GameCardViewModel
        {
            GameName = gameName,
        };
        card.Mod = mod;
        card.NameUrl = nameUrl;
        card.LumaMod = lumaMod;
        return card;
    }

    /// <summary>
    /// Creates a RemoteManifest with a per-addon entry for the given game and addon.
    /// </summary>
    private static RemoteManifest CreateManifestWithEntry(
        string gameName,
        AddonType addon,
        GameNoteEntry entry)
    {
        var manifest = new RemoteManifest();
        var dict = new Dictionary<string, GameNoteEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [gameName] = entry
        };

        switch (addon)
        {
            case AddonType.REFramework:      manifest.ReframeworkGameInfo = dict; break;
            case AddonType.ReShade:          manifest.ReshadeGameInfo = dict; break;
            case AddonType.RenoDX:           manifest.GameNotes = dict; break;
            case AddonType.ReLimiter:        manifest.RelimiterGameInfo = dict; break;
            case AddonType.DisplayCommander: manifest.DisplayCommanderGameInfo = dict; break;
            case AddonType.OptiScaler:       manifest.OptiScalerGameInfo = dict; break;
            case AddonType.Luma:             manifest.LumaGameInfo = dict; break;
        }

        return manifest;
    }

    // ── Property 1: Manifest entry is highest priority ────────────────────────────

    /// <summary>
    /// **Validates: Requirements 4.1, 4.2**
    ///
    /// For any game name, addon type, manifest with a matching per-addon entry,
    /// and any wiki data (present or absent), the AddonInfoResolver.Resolve()
    /// SHALL return the manifest entry content with InfoSourceType.Manifest,
    /// regardless of whether wiki data also exists for that game+addon.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ManifestEntry_IsHighestPriority()
    {
        var gen = from gameName in GenGameName()
                  from addon in GenAddonType()
                  from entry in GenGameNoteEntry()
                  from hasWikiData in Arb.Generate<bool>()
                  select (gameName, addon, entry, hasWikiData);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (gameName, addon, entry, hasWikiData) = tuple;

            // Arrange: manifest with entry for this game+addon
            var manifest = CreateManifestWithEntry(gameName, addon, entry);

            // Optionally add wiki data to prove manifest still wins
            GameMod? mod = null;
            string? nameUrl = null;
            LumaMod? lumaMod = null;
            OptiScalerWikiData? osWikiData = null;

            if (hasWikiData)
            {
                mod = new GameMod { Name = gameName, Notes = "Wiki notes", NameUrl = "https://wiki.example.com" };
                nameUrl = "https://wiki.example.com";
                lumaMod = new LumaMod { Name = gameName, SpecialNotes = "Luma wiki notes" };
                osWikiData = new OptiScalerWikiData
                {
                    StandardCompat = new Dictionary<string, OptiScalerCompatEntry>(StringComparer.OrdinalIgnoreCase)
                    {
                        [gameName] = new OptiScalerCompatEntry
                        {
                            GameName = gameName,
                            Status = "Working",
                            Upscalers = ["DLSS", "FSR"]
                        }
                    }
                };
            }

            var card = CreateCard(gameName, mod, nameUrl, lumaMod);

            // Act
            var result = _resolver.Resolve(card, addon, manifest, osWikiData);

            // Assert
            var sourceOk = result.Source == InfoSourceType.Manifest;
            var contentOk = result.Content == entry.Notes;

            return (sourceOk && contentOk)
                .Label($"addon={addon}, source={result.Source}, content matches={contentOk}, hasWiki={hasWikiData}");
        });
    }

    // ── Property 2: Wiki content is tier 2 for eligible addons ────────────────────

    /// <summary>
    /// **Validates: Requirements 4.3, 4.4, 4.7**
    ///
    /// For any game name where no per-addon manifest entry exists, and the addon
    /// is one of {RenoDX, OptiScaler, Luma}, and wiki data exists for that game,
    /// the AddonInfoResolver.Resolve() SHALL return the wiki content with
    /// InfoSourceType.Wiki.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property WikiContent_IsTier2_ForEligibleAddons()
    {
        return Prop.ForAll(
            GenGameName().ToArbitrary(),
            GenWikiEligibleAddon().ToArbitrary(),
            (gameName, addon) =>
            {
                // Arrange: no manifest entry, but wiki data exists
                var manifest = new RemoteManifest
                {
                    LumaGameNotes = new Dictionary<string, GameNoteEntry>(StringComparer.OrdinalIgnoreCase)
                    {
                        [gameName] = new GameNoteEntry { Notes = "Luma manifest notes" }
                    }
                };

                GameMod? mod = null;
                string? nameUrl = null;
                LumaMod? lumaMod = null;
                OptiScalerWikiData? osWikiData = null;

                switch (addon)
                {
                    case AddonType.RenoDX:
                        mod = new GameMod { Name = gameName, Notes = "RenoDX wiki notes" };
                        nameUrl = "https://wiki.example.com/renodx";
                        break;
                    case AddonType.OptiScaler:
                        osWikiData = new OptiScalerWikiData
                        {
                            StandardCompat = new Dictionary<string, OptiScalerCompatEntry>(StringComparer.OrdinalIgnoreCase)
                            {
                                [gameName] = new OptiScalerCompatEntry
                                {
                                    GameName = gameName,
                                    Status = "Working",
                                    Upscalers = ["DLSS", "FSR", "XeSS"],
                                    Notes = "Works great"
                                }
                            }
                        };
                        break;
                    case AddonType.Luma:
                        lumaMod = new LumaMod { Name = gameName, SpecialNotes = "Luma special notes" };
                        break;
                }

                var card = CreateCard(gameName, mod, nameUrl, lumaMod);

                // Act
                var result = _resolver.Resolve(card, addon, manifest, osWikiData);

                // Assert
                var sourceOk = result.Source == InfoSourceType.Wiki;
                var contentOk = !string.IsNullOrWhiteSpace(result.Content);

                return (sourceOk && contentOk)
                    .Label($"addon={addon}, source={result.Source}, hasContent={contentOk}");
            });
    }

    // ── Property 3: Generic fallback is tier 3 ───────────────────────────────────

    /// <summary>
    /// **Validates: Requirements 4.5, 9.1–9.8**
    ///
    /// For any game name and addon type where no per-addon manifest entry exists
    /// and no wiki content is available, the AddonInfoResolver.Resolve() SHALL
    /// return a non-empty generic fallback string with InfoSourceType.Fallback.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property GenericFallback_IsTier3()
    {
        return Prop.ForAll(
            GenGameName().ToArbitrary(),
            GenAddonType().ToArbitrary(),
            (gameName, addon) =>
            {
                // Arrange: no manifest, no wiki data
                var card = CreateCard(gameName);

                // Act
                var result = _resolver.Resolve(card, addon, null, null);

                // Assert
                var sourceOk = result.Source == InfoSourceType.Fallback;
                var contentOk = !string.IsNullOrWhiteSpace(result.Content);
                var matchesFallback = result.Content == AddonInfoResolver.GetFallbackText(addon);

                return (sourceOk && contentOk && matchesFallback)
                    .Label($"addon={addon}, source={result.Source}, content non-empty={contentOk}, matches fallback={matchesFallback}");
            });
    }

    // ── Property 4: Resolution is independent per addon ──────────────────────────

    /// <summary>
    /// **Validates: Requirements 4.6**
    ///
    /// For any game name and two distinct addon types A and B, setting a manifest
    /// entry for addon A SHALL NOT change the resolution result for addon B.
    /// Each addon's resolution depends only on its own manifest entry, its own
    /// wiki eligibility, and its own fallback text.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Resolution_IsIndependent_PerAddon()
    {
        var genDistinctPair = from a in GenAddonType()
                              from b in GenAddonType()
                              where a != b
                              select (a, b);

        return Prop.ForAll(
            GenGameName().ToArbitrary(),
            genDistinctPair.ToArbitrary(),
            GenGameNoteEntry().ToArbitrary(),
            (gameName, pair, entry) =>
            {
                var (addonA, addonB) = pair;

                // Arrange: manifest entry ONLY for addon A
                var manifest = CreateManifestWithEntry(gameName, addonA, entry);
                var card = CreateCard(gameName);

                // Act: resolve addon B with manifest that only has addon A entry
                var resultB_with = _resolver.Resolve(card, addonB, manifest, null);

                // Also resolve addon B with no manifest at all
                var resultB_without = _resolver.Resolve(card, addonB, null, null);

                // Assert: addon B result should be the same regardless of addon A's manifest entry
                var sourceOk = resultB_with.Source == resultB_without.Source;
                var contentOk = resultB_with.Content == resultB_without.Content;

                // Also verify addon A gets manifest source
                var resultA = _resolver.Resolve(card, addonA, manifest, null);
                var addonAOk = resultA.Source == InfoSourceType.Manifest;

                return (sourceOk && contentOk && addonAOk)
                    .Label($"addonA={addonA}(source={resultA.Source}), addonB={addonB}(with={resultB_with.Source}, without={resultB_without.Source}), independent={sourceOk && contentOk}");
            });
    }

    // ── Property 7: Info result includes all available fields ─────────────────────

    /// <summary>
    /// **Validates: Requirements 7.3, 7.4, 8.5**
    ///
    /// For any AddonInfoResult where the source is Wiki or Manifest, if the source
    /// data contains a URL, the result's Url field SHALL be non-null. If the source
    /// data contains notes text, the result's Content field SHALL contain that text.
    /// For OptiScaler wiki results, if the entry has a status and upscaler list,
    /// those SHALL appear in the formatted content.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property InfoResult_IncludesAllAvailableFields()
    {
        var gen = from gameName in GenGameName()
                  from scenario in Gen.Elements("manifest_with_url", "manifest_no_url",
                      "renodx_wiki", "optiscaler_wiki", "luma_wiki")
                  select (gameName, scenario);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (gameName, scenario) = tuple;
            AddonInfoResult result;

            switch (scenario)
            {
                case "manifest_with_url":
                {
                    // Manifest entry with URL — result.Url SHALL be non-null
                    var entry = new GameNoteEntry
                    {
                        Notes = "Custom manifest notes",
                        NotesUrl = "https://example.com/notes",
                        NotesUrlLabel = "View details"
                    };
                    var manifest = CreateManifestWithEntry(gameName, AddonType.ReShade, entry);
                    var card = CreateCard(gameName);
                    result = _resolver.Resolve(card, AddonType.ReShade, manifest, null);

                    var urlOk = result.Url != null;
                    var contentOk = result.Content.Contains("Custom manifest notes");
                    var sourceOk = result.Source == InfoSourceType.Manifest;

                    return (urlOk && contentOk && sourceOk)
                        .Label($"manifest_with_url: url={result.Url}, content contains notes={contentOk}, source={result.Source}");
                }

                case "manifest_no_url":
                {
                    // Manifest entry without URL — result.Content SHALL contain notes
                    var entry = new GameNoteEntry { Notes = "Notes without URL" };
                    var manifest = CreateManifestWithEntry(gameName, AddonType.ReLimiter, entry);
                    var card = CreateCard(gameName);
                    result = _resolver.Resolve(card, AddonType.ReLimiter, manifest, null);

                    var contentOk = result.Content.Contains("Notes without URL");
                    var sourceOk = result.Source == InfoSourceType.Manifest;

                    return (contentOk && sourceOk)
                        .Label($"manifest_no_url: content contains notes={contentOk}, source={result.Source}");
                }

                case "renodx_wiki":
                {
                    // RenoDX wiki with notes and URL — result SHALL include both
                    var mod = new GameMod { Name = gameName, Notes = "RenoDX wiki notes text" };
                    var nameUrl = "https://wiki.example.com/renodx/" + gameName;
                    var card = CreateCard(gameName, mod: mod, nameUrl: nameUrl);
                    result = _resolver.Resolve(card, AddonType.RenoDX, null, null);

                    var sourceOk = result.Source == InfoSourceType.Wiki;
                    var contentOk = result.Content.Contains("RenoDX wiki notes text");
                    var urlOk = result.Url != null;
                    var badgeOk = result.WikiStatusLabel != null;

                    return (sourceOk && contentOk && urlOk && badgeOk)
                        .Label($"renodx_wiki: source={result.Source}, content={contentOk}, url={urlOk}, badge={badgeOk}");
                }

                case "optiscaler_wiki":
                {
                    // OptiScaler wiki with status and upscalers — SHALL appear in content
                    var card = CreateCard(gameName);
                    var osWikiData = new OptiScalerWikiData
                    {
                        StandardCompat = new Dictionary<string, OptiScalerCompatEntry>(StringComparer.OrdinalIgnoreCase)
                        {
                            [gameName] = new OptiScalerCompatEntry
                            {
                                GameName = gameName,
                                Status = "Working",
                                Upscalers = ["DLSS", "FSR", "XeSS"],
                                Notes = "Runs perfectly",
                                DetailPageUrl = "https://wiki.example.com/optiscaler/" + gameName
                            }
                        }
                    };
                    result = _resolver.Resolve(card, AddonType.OptiScaler, null, osWikiData);

                    var sourceOk = result.Source == InfoSourceType.Wiki;
                    var statusOk = result.Content.Contains("Working");
                    var upscalersOk = result.Content.Contains("DLSS") &&
                                     result.Content.Contains("FSR") &&
                                     result.Content.Contains("XeSS");
                    var notesOk = result.Content.Contains("Runs perfectly");
                    var urlOk = result.Url != null;
                    var compatOk = result.OptiScalerCompat != null;

                    return (sourceOk && statusOk && upscalersOk && notesOk && urlOk && compatOk)
                        .Label($"optiscaler_wiki: source={result.Source}, status={statusOk}, upscalers={upscalersOk}, notes={notesOk}, url={urlOk}, compat={compatOk}");
                }

                case "luma_wiki":
                {
                    // Luma wiki with SpecialNotes — result.Content SHALL contain that text
                    var lumaMod = new LumaMod
                    {
                        Name = gameName,
                        SpecialNotes = "Luma special notes text",
                        FeatureNotes = "Luma feature notes text"
                    };
                    var card = CreateCard(gameName, lumaMod: lumaMod);
                    result = _resolver.Resolve(card, AddonType.Luma, null, null);

                    var sourceOk = result.Source == InfoSourceType.Wiki;
                    var specialOk = result.Content.Contains("Luma special notes text");
                    var featureOk = result.Content.Contains("Luma feature notes text");

                    return (sourceOk && specialOk && featureOk)
                        .Label($"luma_wiki: source={result.Source}, special={specialOk}, feature={featureOk}");
                }

                default:
                    return false.Label("Unknown scenario");
            }
        });
    }

    // ── Property 8: Tooltip and visual style are determined by source type ────────

    /// <summary>
    /// **Validates: Requirements 11.1, 11.2, 12.1, 12.2, 12.3, 12.4**
    ///
    /// For any game name and addon type, the tooltip returned by GetTooltip()
    /// SHALL be "Per-game notes available" when source is Manifest,
    /// "Wiki info available" when source is Wiki, and "General addon info"
    /// when source is Fallback. The source type returned by GetSourceType()
    /// SHALL match the source type of the full Resolve() result.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Tooltip_And_Style_DeterminedBySourceType()
    {
        return Prop.ForAll(
            GenGameName().ToArbitrary(),
            GenAddonType().ToArbitrary(),
            Arb.Default.Bool(),
            (gameName, addon, hasManifest) =>
            {
                // Arrange
                RemoteManifest? manifest = null;
                if (hasManifest)
                {
                    var entry = new GameNoteEntry { Notes = "Manifest notes for " + gameName };
                    manifest = CreateManifestWithEntry(gameName, addon, entry);
                }

                var card = CreateCard(gameName);

                // Act
                var result = _resolver.Resolve(card, addon, manifest, null);
                var tooltip = _resolver.GetTooltip(card, addon, manifest, null);
                var sourceType = _resolver.GetSourceType(card, addon, manifest, null);

                // Assert: source type matches between Resolve and GetSourceType
                var sourceMatch = result.Source == sourceType;

                // Assert: tooltip matches source type
                var expectedTooltip = sourceType switch
                {
                    InfoSourceType.Manifest => "Per-game notes available",
                    InfoSourceType.Wiki     => "Wiki info available",
                    InfoSourceType.Fallback => "General addon info",
                    _ => "General addon info"
                };
                var tooltipOk = tooltip == expectedTooltip;

                return (sourceMatch && tooltipOk)
                    .Label($"addon={addon}, source={result.Source}, sourceType={sourceType}, tooltip='{tooltip}', expected='{expectedTooltip}', sourceMatch={sourceMatch}, tooltipOk={tooltipOk}");
            });
    }
}
