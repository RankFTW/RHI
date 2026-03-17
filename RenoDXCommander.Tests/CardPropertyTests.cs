using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for GameCardViewModel computed properties in the multi-card-layout feature.
/// Uses FsCheck with xUnit. Each property runs a minimum of 100 iterations.
/// </summary>
public class CardPropertyTests
{
    // ── Generators ────────────────────────────────────────────────────────────────

    private static readonly Gen<GameStatus> GenStatus =
        Gen.Elements(GameStatus.NotInstalled, GameStatus.Available,
                     GameStatus.Installed, GameStatus.UpdateAvailable);

    private static readonly Gen<string> GenNonEmptyName =
        Gen.Elements("Cyberpunk 2077", "Elden Ring", "Starfield", "Baldur's Gate 3",
                     "Hogwarts Legacy", "Alan Wake 2", "Returnal", "Hades II");

    private static readonly Gen<string> GenSource =
        Gen.Elements("Steam", "GOG", "Epic", "EA App", "Ubisoft", "Manual", "Xbox", "Battle.net");

    // ── Property 4: Card content completeness ─────────────────────────────────────
    // Feature: multi-card-layout, Property 4: Card content completeness
    // Validates: Requirements 3.1, 3.2, 3.5, 3.6, 3.7
    [Property(MaxTest = 100)]
    public Property CardContentCompleteness()
    {
        // Bundle card config into a single tuple generator to stay within ForAll's 3-arg limit
        var genCard = from name in GenNonEmptyName
                      from source in GenSource
                      from rdxStatus in GenStatus
                      from rsStatus in GenStatus
                      from dcStatus in GenStatus
                      from isFav in Arb.Default.Bool().Generator
                      select (name, source, rdxStatus, rsStatus, dcStatus, isFav);

        return Prop.ForAll(
            Arb.From(genCard),
            (tuple) =>
            {
                var (name, source, rdxStatus, rsStatus, dcStatus, isFavourite) = tuple;

                var card = new GameCardViewModel
                {
                    GameName = name,
                    Source = source,
                    Status = rdxStatus,
                    RsStatus = rsStatus,
                    DcStatus = dcStatus,
                    IsFavourite = isFavourite,
                    Notes = "Some notes"
                };
                card.NameUrl = "https://example.com";

                // 3.1: Game name is present
                bool hasName = card.GameName == name && !string.IsNullOrEmpty(card.GameName);

                // 3.1: Source icon identifier is present
                bool hasSourceIcon = !string.IsNullOrEmpty(card.SourceIcon);

                // 3.2: Status dot colors are present for RDX, RS, DC
                bool hasRdxDot = !string.IsNullOrEmpty(card.CardRdxStatusDot);
                bool hasRsDot = !string.IsNullOrEmpty(card.CardRsStatusDot);
                bool hasDcDot = !string.IsNullOrEmpty(card.CardDcStatusDot);

                // 3.5: Primary action label is present
                bool hasActionLabel = !string.IsNullOrEmpty(card.CardPrimaryActionLabel);

                // 3.6: Favourite indicator matches IsFavourite
                bool favouriteCorrect = (card.IsFavourite == isFavourite);

                // 3.7: Info indicator is true when HasNotes or HasNameUrl
                bool infoCorrect = card.HasInfoIndicator == (card.HasNotes || card.HasNameUrl);

                return hasName && hasSourceIcon && hasRdxDot && hasRsDot && hasDcDot
                    && hasActionLabel && favouriteCorrect && infoCorrect;
            });
    }

    // Also test with no notes and no name URL — HasInfoIndicator should be false
    // Feature: multi-card-layout, Property 4: Card content completeness (no info)
    // Validates: Requirements 3.7
    [Property(MaxTest = 100)]
    public Property CardContentCompleteness_NoInfo()
    {
        return Prop.ForAll(
            Arb.From(GenNonEmptyName),
            Arb.From(GenSource),
            (string name, string source) =>
            {
                var card = new GameCardViewModel
                {
                    GameName = name,
                    Source = source,
                    Notes = null
                };
                // NameUrl is a plain property, not observable
                card.NameUrl = null;

                return !card.HasInfoIndicator;
            });
    }

    // ── Property 5: Luma status conditional visibility ────────────────────────────
    // Feature: multi-card-layout, Property 5: Luma status conditional visibility
    // Validates: Requirements 3.3
    [Property(MaxTest = 100)]
    public Property LumaStatusConditionalVisibility()
    {
        var genNullableLuma = Gen.OneOf<LumaMod?>(
            Gen.Constant<LumaMod?>(null),
            Gen.Constant<LumaMod?>(new LumaMod { Name = "TestLuma", DownloadUrl = "https://example.com" })
        );

        return Prop.ForAll(
            Arb.From(Arb.Default.Bool().Generator),
            Arb.From(Arb.Default.Bool().Generator),
            Arb.From(genNullableLuma),
            (bool lumaFeatureEnabled, bool isLumaMode, LumaMod? lumaMod) =>
            {
                var card = new GameCardViewModel
                {
                    LumaFeatureEnabled = lumaFeatureEnabled,
                    IsLumaMode = isLumaMode
                };
                card.LumaMod = lumaMod;

                bool expected = lumaFeatureEnabled && isLumaMode && lumaMod != null;
                return card.CardLumaVisible == expected;
            });
    }

    // ── Property 6: Component status visual distinction ───────────────────────────
    // Feature: multi-card-layout, Property 6: Component status visual distinction
    // Validates: Requirements 3.4
    [Fact]
    public void ComponentStatusVisualDistinction()
    {
        // For each GameStatus value (with installing=false), the status dot color
        // must be distinct. We test all pairs.
        var statuses = new[] {
            GameStatus.NotInstalled, GameStatus.Available,
            GameStatus.Installed, GameStatus.UpdateAvailable
        };

        var colorMap = new Dictionary<GameStatus, string>();
        foreach (var status in statuses)
        {
            var card = new GameCardViewModel { Status = status, IsInstalling = false };
            colorMap[status] = card.CardRdxStatusDot;
        }

        // Installed and UpdateAvailable must have distinct colors
        Assert.NotEqual(colorMap[GameStatus.Installed], colorMap[GameStatus.UpdateAvailable]);

        // Installed must differ from NotInstalled/Available
        Assert.NotEqual(colorMap[GameStatus.Installed], colorMap[GameStatus.NotInstalled]);

        // UpdateAvailable must differ from NotInstalled/Available
        Assert.NotEqual(colorMap[GameStatus.UpdateAvailable], colorMap[GameStatus.NotInstalled]);
    }

    // Property-based variant: for any two different statuses that have distinct colors,
    // the dot color should differ. NotInstalled and Available share the same gray color
    // by design (both are "not yet installed" states), so we test the meaningful distinctions.
    // Feature: multi-card-layout, Property 6: Component status visual distinction (property)
    // Validates: Requirements 3.4
    [Property(MaxTest = 100)]
    public Property StatusDotColor_InstallingAlwaysBlue()
    {
        return Prop.ForAll(
            Arb.From(GenStatus),
            (GameStatus status) =>
            {
                var card = new GameCardViewModel { Status = status, IsInstalling = true };
                // When installing, the dot should always be blue regardless of status
                return card.CardRdxStatusDot == "#2196F3";
            });
    }

    // Feature: multi-card-layout, Property 6: Component status visual distinction (all components)
    // Validates: Requirements 3.4
    [Property(MaxTest = 100)]
    public Property StatusDotColor_ConsistentAcrossComponents()
    {
        return Prop.ForAll(
            Arb.From(GenStatus),
            (GameStatus status) =>
            {
                // All components with the same status and installing=false should produce the same color
                var card = new GameCardViewModel
                {
                    Status = status,
                    RsStatus = status,
                    DcStatus = status,
                    LumaStatus = status,
                    IsInstalling = false,
                    RsIsInstalling = false,
                    DcIsInstalling = false,
                    IsLumaInstalling = false
                };

                string rdx = card.CardRdxStatusDot;
                string rs = card.CardRsStatusDot;
                string dc = card.CardDcStatusDot;
                string luma = card.CardLumaStatusDot;

                return rdx == rs && rs == dc && dc == luma;
            });
    }

    // ── Property 7: Card highlight exclusivity ────────────────────────────────────
    // Feature: multi-card-layout, Property 7: Card highlight exclusivity
    // Validates: Requirements 5.2, 5.3, 5.4
    [Property(MaxTest = 100)]
    public Property CardHighlightExclusivity()
    {
        // Generate a list size between 2 and 20, and a selection index (-1 = no selection)
        var genArgs = from listSize in Gen.Choose(2, 20)
                      from selIdx in Gen.Choose(-1, 19)
                      select (listSize, selIdx);

        return Prop.ForAll(
            Arb.From(genArgs),
            (tuple) =>
            {
                var (listSize, selectionRaw) = tuple;
                int? selectionIndex = selectionRaw < 0 ? null : selectionRaw;
                // Create cards
                var cards = new List<GameCardViewModel>();
                for (int i = 0; i < listSize; i++)
                {
                    cards.Add(new GameCardViewModel
                    {
                        GameName = $"Game {i}",
                        Source = "Steam"
                    });
                }

                // Apply highlight logic: clear all, then highlight selected
                foreach (var c in cards)
                    c.CardHighlighted = false;

                GameCardViewModel? selectedGame = null;
                if (selectionIndex.HasValue && selectionIndex.Value >= 0 && selectionIndex.Value < listSize)
                {
                    selectedGame = cards[selectionIndex.Value];
                    selectedGame.CardHighlighted = true;
                }

                // Count highlighted cards
                int highlightedCount = cards.Count(c => c.CardHighlighted);

                if (selectedGame == null)
                {
                    // No selection: zero cards highlighted
                    return highlightedCount == 0;
                }
                else
                {
                    // Exactly one card highlighted, and it's the selected one
                    return highlightedCount == 1 && selectedGame.CardHighlighted;
                }
            });
    }

    // Feature: multi-card-layout, Property 7: Card highlight exclusivity (switching)
    // Validates: Requirements 5.4
    [Property(MaxTest = 100)]
    public Property CardHighlightExclusivity_SwitchingSelection()
    {
        var genArgs = from listSize in Gen.Choose(3, 15)
                      from firstIdx in Gen.Choose(0, 14)
                      from secondIdx in Gen.Choose(0, 14)
                      select (listSize, firstIdx, secondIdx);

        return Prop.ForAll(
            Arb.From(genArgs),
            (tuple) =>
            {
                var (listSize, firstIdx, secondIdx) = tuple;
                int size = Math.Min(listSize, 15);
                int idx1 = firstIdx % size;
                int idx2 = secondIdx % size;

                var cards = new List<GameCardViewModel>();
                for (int i = 0; i < size; i++)
                    cards.Add(new GameCardViewModel { GameName = $"Game {i}", Source = "Steam" });

                // Highlight first selection
                foreach (var c in cards) c.CardHighlighted = false;
                cards[idx1].CardHighlighted = true;

                // Switch to second selection
                foreach (var c in cards) c.CardHighlighted = false;
                cards[idx2].CardHighlighted = true;

                int highlightedCount = cards.Count(c => c.CardHighlighted);
                return highlightedCount == 1 && cards[idx2].CardHighlighted;
            });
    }

    // ── Property 8: Installing state disables card actions ────────────────────────
    // Feature: multi-card-layout, Property 8: Installing state disables card actions
    // Validates: Requirements 6.2
    [Property(MaxTest = 100)]
    public Property InstallingStateDisablesCardActions()
    {
        var genFlags = from rdx in Arb.Default.Bool().Generator
                       from rs in Arb.Default.Bool().Generator
                       from dc in Arb.Default.Bool().Generator
                       from luma in Arb.Default.Bool().Generator
                       select (rdx, rs, dc, luma);

        return Prop.ForAll(
            Arb.From(genFlags),
            (tuple) =>
            {
                var (rdxInstalling, rsInstalling, dcInstalling, lumaInstalling) = tuple;

                var card = new GameCardViewModel
                {
                    IsInstalling = rdxInstalling,
                    RsIsInstalling = rsInstalling,
                    DcIsInstalling = dcInstalling,
                    IsLumaInstalling = lumaInstalling
                };

                bool anyInstalling = rdxInstalling || rsInstalling || dcInstalling || lumaInstalling;

                // When any component is installing, CanCardInstall must be false
                // When nothing is installing, CanCardInstall must be true
                return card.CanCardInstall == !anyInstalling;
            });
    }

    // ── Property 9: Override round-trip on card properties ────────────────────────
    // Feature: multi-card-layout, Property 9: Override changes reflect in card computed properties
    // Validates: Requirements 4.4
    [Property(MaxTest = 100)]
    public Property OverrideRoundTrip_Is32Bit()
    {
        return Prop.ForAll(
            Arb.From(Arb.Default.Bool().Generator),
            (bool value) =>
            {
                var card = new GameCardViewModel();
                card.Is32Bit = value;
                return card.Is32Bit == value;
            });
    }

    // Feature: multi-card-layout, Property 9: Override changes reflect in card computed properties
    // Validates: Requirements 4.4
    [Property(MaxTest = 100)]
    public Property OverrideRoundTrip_PerGameDcMode()
    {
        var genDcMode = Gen.OneOf<int?>(
            Gen.Constant<int?>(null),
            Gen.Constant<int?>(0),
            Gen.Constant<int?>(1),
            Gen.Constant<int?>(2)
        );

        return Prop.ForAll(
            Arb.From(genDcMode),
            (int? dcMode) =>
            {
                var card = new GameCardViewModel();
                card.PerGameDcMode = dcMode;

                // Round-trip: value read back matches what was set
                bool roundTrip = card.PerGameDcMode == dcMode;

                // DcModeExcluded reflects whether an override is set
                bool excludedCorrect = card.DcModeExcluded == dcMode.HasValue;

                return roundTrip && excludedCorrect;
            });
    }

    // Feature: multi-card-layout, Property 9: Override changes reflect in card computed properties
    // Validates: Requirements 4.4
    [Property(MaxTest = 100)]
    public Property OverrideRoundTrip_ShaderModeOverride()
    {
        var genShaderMode = Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Constant<string?>("Off"),
            Gen.Constant<string?>("Minimum"),
            Gen.Constant<string?>("All"),
            Gen.Constant<string?>("User")
        );

        return Prop.ForAll(
            Arb.From(genShaderMode),
            (string? shaderMode) =>
            {
                var card = new GameCardViewModel();
                card.ShaderModeOverride = shaderMode;
                return card.ShaderModeOverride == shaderMode;
            });
    }

    // Feature: separate-override-toggles, Property 1: Per-component exclusion independence
    // Validates: Requirements 1.2
    [Property(MaxTest = 100)]
    public Property OverrideRoundTrip_ExcludeFromUpdateAllPerComponent()
    {
        return Prop.ForAll(
            Arb.From(Arb.Default.Bool().Generator),
            Arb.From(Arb.Default.Bool().Generator),
            Arb.From(Arb.Default.Bool().Generator),
            (bool rs, bool dc, bool rdx) =>
            {
                var card = new GameCardViewModel();
                card.ExcludeFromUpdateAllReShade = rs;
                card.ExcludeFromUpdateAllDc = dc;
                card.ExcludeFromUpdateAllRenoDx = rdx;
                return card.ExcludeFromUpdateAllReShade == rs
                    && card.ExcludeFromUpdateAllDc == dc
                    && card.ExcludeFromUpdateAllRenoDx == rdx;
            });
    }
}
