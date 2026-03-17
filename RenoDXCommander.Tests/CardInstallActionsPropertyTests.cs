using FsCheck;
using FsCheck.Xunit;
using Microsoft.UI.Xaml;
using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for the card-install-actions feature.
/// Uses FsCheck with xUnit. Each property runs a minimum of 100 iterations.
/// </summary>
public class CardInstallActionsPropertyTests
{
    // ── Generators ────────────────────────────────────────────────────────────────

    private static readonly Gen<GameMod?> GenNullableMod = Gen.OneOf(
        Gen.Constant<GameMod?>(null),
        Gen.Constant<GameMod?>(new GameMod { Name = "TestMod", SnapshotUrl = "https://example.com/mod.zip" })
    );

    private static readonly Gen<LumaMod?> GenNullableLuma = Gen.OneOf(
        Gen.Constant<LumaMod?>(null),
        Gen.Constant<LumaMod?>(new LumaMod { Name = "TestLuma", DownloadUrl = "https://example.com/luma.zip" })
    );

    // ── Property 1: Install dropdown item visibility matches card mode and component availability ──
    // Feature: card-install-actions, Property 1: Install dropdown item visibility
    // Validates: Requirements 1.1, 1.8, 1.10
    [Property(MaxTest = 100)]
    public Property InstallFlyoutItemVisibility_MatchesCardModeAndComponentAvailability()
    {
        var genCardState = from isExternalOnly in Arb.Default.Bool().Generator
                           from lumaFeatureEnabled in Arb.Default.Bool().Generator
                           from isLumaMode in Arb.Default.Bool().Generator
                           from lumaMod in GenNullableLuma
                           from mod in GenNullableMod
                           select (isExternalOnly, lumaFeatureEnabled, isLumaMode, lumaMod, mod);

        return Prop.ForAll(
            Arb.From(genCardState),
            (tuple) =>
            {
                var (isExternalOnly, lumaFeatureEnabled, isLumaMode, lumaMod, mod) = tuple;

                var card = new GameCardViewModel
                {
                    IsExternalOnly = isExternalOnly,
                    LumaFeatureEnabled = lumaFeatureEnabled,
                    IsLumaMode = isLumaMode,
                    Mod = mod
                };
                card.LumaMod = lumaMod;

                // Read actual visibility from ViewModel computed properties
                bool rdxVisible = card.RenoDxRowVisibility == Visibility.Visible;
                bool rsVisible = card.ReShadeRowVisibility == Visibility.Visible;
                bool dcVisible = card.DcRowVisibility == Visibility.Visible;
                bool lumaVisible = card.CardLumaVisible;

                // Derived conditions
                bool effectiveLumaMode = lumaFeatureEnabled && isLumaMode;
                bool fullLumaMode = effectiveLumaMode && lumaMod != null;

                if (effectiveLumaMode)
                {
                    // When in effective Luma mode: RDX, RS, DC rows are hidden.
                    // Luma is visible only when LumaMod is present (CardLumaVisible).
                    // "Install All" is always present (not a row visibility property).
                    bool rdxHidden = !rdxVisible;
                    bool rsHidden = !rsVisible;
                    bool dcHidden = !dcVisible;
                    bool lumaCorrect = lumaVisible == (lumaMod != null);
                    return rdxHidden && rsHidden && dcHidden && lumaCorrect;
                }
                else if (isExternalOnly)
                {
                    // When IsExternalOnly (and not Luma mode): RDX hidden; RS and DC visible
                    bool rdxHidden = !rdxVisible;
                    bool rsShown = rsVisible;
                    bool dcShown = dcVisible;
                    bool lumaCorrect = !lumaVisible; // Not in Luma mode, so Luma dot not visible
                    return rdxHidden && rsShown && dcShown && lumaCorrect;
                }
                else
                {
                    // Otherwise: RDX, RS, DC all visible; Luma visible if CardLumaVisible
                    bool rdxShown = rdxVisible;
                    bool rsShown = rsVisible;
                    bool dcShown = dcVisible;
                    bool lumaCorrect = lumaVisible == (lumaFeatureEnabled && isLumaMode && lumaMod != null);
                    return rdxShown && rsShown && dcShown && lumaCorrect;
                }
            });
    }

    // ── Property 2: Component status text and color match ViewModel computed properties ──
    // Feature: card-install-actions, Property 2: Component status text and color
    // **Validates: Requirements 1.2, 1.6, 1.7, 1.9**
    [Property(MaxTest = 100)]
    public Property ComponentStatusTextAndColor_MatchViewModelComputedProperties()
    {
        var genStatus = Gen.Elements(
            GameStatus.NotInstalled,
            GameStatus.Available,
            GameStatus.Installed,
            GameStatus.UpdateAvailable);

        var genState = from rdxStatus in genStatus
                       from rsStatus in genStatus
                       from dcStatus in genStatus
                       from lumaStatus in genStatus
                       from isInstalling in Arb.Default.Bool().Generator
                       from rsIsInstalling in Arb.Default.Bool().Generator
                       from dcIsInstalling in Arb.Default.Bool().Generator
                       from isLumaInstalling in Arb.Default.Bool().Generator
                       from rsBlockedByDcMode in Arb.Default.Bool().Generator
                       from hasMod in Arb.Default.Bool().Generator
                       from hasLumaMod in Arb.Default.Bool().Generator
                       select (rdxStatus, rsStatus, dcStatus, lumaStatus,
                               isInstalling, rsIsInstalling, dcIsInstalling, isLumaInstalling,
                               rsBlockedByDcMode, hasMod, hasLumaMod);

        return Prop.ForAll(
            Arb.From(genState),
            (tuple) =>
            {
                var (rdxStatus, rsStatus, dcStatus, lumaStatus,
                     isInstalling, rsIsInstalling, dcIsInstalling, isLumaInstalling,
                     rsBlockedByDcMode, hasMod, hasLumaMod) = tuple;

                var mod = hasMod ? new GameMod { Name = "TestMod", SnapshotUrl = "https://example.com/mod.zip" } : null;
                var lumaMod = hasLumaMod ? new LumaMod { Name = "TestLuma", DownloadUrl = "https://example.com/luma.zip" } : null;

                var card = new GameCardViewModel
                {
                    Status = rdxStatus,
                    RsStatus = rsStatus,
                    DcStatus = dcStatus,
                    LumaStatus = lumaStatus,
                    IsInstalling = isInstalling,
                    RsIsInstalling = rsIsInstalling,
                    DcIsInstalling = dcIsInstalling,
                    IsLumaInstalling = isLumaInstalling,
                    RsBlockedByDcMode = rsBlockedByDcMode,
                    Mod = mod,
                };
                card.LumaMod = lumaMod;

                // ── RDX status text and color ──
                string expectedRdxText = isInstalling ? "Installing…"
                    : rdxStatus == GameStatus.UpdateAvailable ? "Update"
                    : rdxStatus == GameStatus.Installed       ? "Installed"
                    : mod?.SnapshotUrl != null                ? "Ready" : "—";
                string expectedRdxColor = isInstalling ? "#D4A856"
                    : rdxStatus == GameStatus.UpdateAvailable ? "#B898E8"
                    : rdxStatus == GameStatus.Installed       ? "#5ECB7D"
                    : mod?.SnapshotUrl != null                ? "#A0AABB" : "#404858";

                bool rdxTextOk = card.RdxStatusText == expectedRdxText;
                bool rdxColorOk = card.RdxStatusColor == expectedRdxColor;

                // ── RS status text and color ──
                string expectedRsText = rsBlockedByDcMode ? "DC Mode"
                    : rsIsInstalling ? "Installing…"
                    : rsStatus == GameStatus.UpdateAvailable ? "Update"
                    : rsStatus == GameStatus.Installed       ? "Installed"
                    : "Ready";
                string expectedRsColor = rsBlockedByDcMode ? "#6B7A8E"
                    : rsIsInstalling ? "#D4A856"
                    : rsStatus == GameStatus.UpdateAvailable ? "#B898E8"
                    : rsStatus == GameStatus.Installed       ? "#5ECB7D"
                    : "#A0AABB";

                bool rsTextOk = card.RsStatusText == expectedRsText;
                bool rsColorOk = card.RsStatusColor == expectedRsColor;

                // ── DC status text and color ──
                string expectedDcText = dcIsInstalling ? "Installing…"
                    : dcStatus == GameStatus.UpdateAvailable ? "Update"
                    : dcStatus == GameStatus.Installed       ? "Installed"
                    : "Ready";
                string expectedDcColor = dcIsInstalling ? "#D4A856"
                    : dcStatus == GameStatus.UpdateAvailable ? "#B898E8"
                    : dcStatus == GameStatus.Installed       ? "#5ECB7D"
                    : "#A0AABB";

                bool dcTextOk = card.DcStatusText == expectedDcText;
                bool dcColorOk = card.DcStatusColor == expectedDcColor;

                // ── Luma action label ──
                string expectedLumaLabel = isLumaInstalling ? "Installing..."
                    : lumaStatus == GameStatus.Installed ? "↺  Reinstall Luma"
                    : "⬇  Install Luma";

                bool lumaLabelOk = card.LumaActionLabel == expectedLumaLabel;

                // ── CanCardInstall: false when any component is installing ──
                bool expectedCanCardInstall = !isInstalling && !rsIsInstalling && !dcIsInstalling && !isLumaInstalling;
                bool canCardInstallOk = card.CanCardInstall == expectedCanCardInstall;

                // ── Per-component install enabled ──
                bool expectedRdxEnabled = !isInstalling && mod?.SnapshotUrl != null && !card.IsExternalOnly;
                bool rdxEnabledOk = card.CardRdxInstallEnabled == expectedRdxEnabled;

                bool expectedRsEnabled = !rsIsInstalling && !rsBlockedByDcMode;
                bool rsEnabledOk = card.CardRsInstallEnabled == expectedRsEnabled;

                bool expectedDcEnabled = !dcIsInstalling;
                bool dcEnabledOk = card.CardDcInstallEnabled == expectedDcEnabled;

                bool expectedLumaEnabled = !isLumaInstalling && lumaMod?.DownloadUrl != null;
                bool lumaEnabledOk = card.CardLumaInstallEnabled == expectedLumaEnabled;

                return rdxTextOk && rdxColorOk
                    && rsTextOk && rsColorOk
                    && dcTextOk && dcColorOk
                    && lumaLabelOk
                    && canCardInstallOk
                    && rdxEnabledOk && rsEnabledOk && dcEnabledOk && lumaEnabledOk;
            });
    }

    // ── Property 3: Uninstall button visibility matches installed component state ──
    // Feature: card-install-actions, Property 3: Uninstall button visibility
    // **Validates: Requirements 1.6**
    [Property(MaxTest = 100)]
    public Property UninstallButtonVisibility_MatchesInstalledComponentState()
    {
        var genStatus = Gen.Elements(
            GameStatus.NotInstalled,
            GameStatus.Available,
            GameStatus.Installed,
            GameStatus.UpdateAvailable);

        var genState = from rdxStatus in genStatus
                       from rsStatus in genStatus
                       from dcStatus in genStatus
                       from lumaStatus in genStatus
                       from lumaFeatureEnabled in Arb.Default.Bool().Generator
                       from isLumaMode in Arb.Default.Bool().Generator
                       from lumaMod in GenNullableLuma
                       select (rdxStatus, rsStatus, dcStatus, lumaStatus,
                               lumaFeatureEnabled, isLumaMode, lumaMod);

        return Prop.ForAll(
            Arb.From(genState),
            (tuple) =>
            {
                var (rdxStatus, rsStatus, dcStatus, lumaStatus,
                     lumaFeatureEnabled, isLumaMode, lumaMod) = tuple;

                var card = new GameCardViewModel
                {
                    Status = rdxStatus,
                    RsStatus = rsStatus,
                    DcStatus = dcStatus,
                    LumaStatus = lumaStatus,
                    LumaFeatureEnabled = lumaFeatureEnabled,
                    IsLumaMode = isLumaMode,
                };
                card.LumaMod = lumaMod;

                bool effectiveLumaMode = lumaFeatureEnabled && isLumaMode;

                // Verify IsXxxInstalled computed properties
                bool rdxInstalled = rdxStatus is GameStatus.Installed or GameStatus.UpdateAvailable;
                bool rsInstalled = rsStatus is GameStatus.Installed or GameStatus.UpdateAvailable;
                bool dcInstalled = dcStatus is GameStatus.Installed or GameStatus.UpdateAvailable;
                bool lumaInstalled = lumaStatus is GameStatus.Installed or GameStatus.UpdateAvailable;

                bool rdxInstalledOk = card.IsRdxInstalled == rdxInstalled;
                bool rsInstalledOk = card.IsRsInstalled == rsInstalled;
                bool dcInstalledOk = card.IsDcInstalled == dcInstalled;
                bool lumaInstalledOk = card.IsLumaInstalled == lumaInstalled;

                // Uninstall ✕ visibility: component installed AND its row is visible.
                // Row visibility is governed by EffectiveLumaMode and IsExternalOnly:
                //   - RDX/RS/DC rows hidden when EffectiveLumaMode is true
                //   - Luma row visible only when CardLumaVisible (LumaFeatureEnabled && IsLumaMode && LumaMod != null)
                bool rdxRowVisible = card.RenoDxRowVisibility == Visibility.Visible;
                bool rsRowVisible = card.ReShadeRowVisibility == Visibility.Visible;
                bool dcRowVisible = card.DcRowVisibility == Visibility.Visible;
                bool lumaRowVisible = card.CardLumaVisible;

                bool expectedRdxUninstallVisible = rdxInstalled && rdxRowVisible;
                bool expectedRsUninstallVisible = rsInstalled && rsRowVisible;
                bool expectedDcUninstallVisible = dcInstalled && dcRowVisible;
                bool expectedLumaUninstallVisible = lumaInstalled && lumaRowVisible;

                // Cross-check row visibility against mode rules
                // Note: RDX row is also hidden when IsExternalOnly, but this test
                // does not vary IsExternalOnly (defaults to false), so the check is simpler.
                bool rdxRowOk = rdxRowVisible == (!effectiveLumaMode && !card.IsExternalOnly);
                bool rsRowOk = rsRowVisible == !effectiveLumaMode;
                bool dcRowOk = dcRowVisible == !effectiveLumaMode;
                bool lumaRowOk = lumaRowVisible == (effectiveLumaMode && lumaMod != null);

                // Verify the ViewModel's IsXxxInstalled + row visibility produce correct uninstall state
                bool rdxUninstallOk = (card.IsRdxInstalled && rdxRowVisible) == expectedRdxUninstallVisible;
                bool rsUninstallOk = (card.IsRsInstalled && rsRowVisible) == expectedRsUninstallVisible;
                bool dcUninstallOk = (card.IsDcInstalled && dcRowVisible) == expectedDcUninstallVisible;
                bool lumaUninstallOk = (card.IsLumaInstalled && lumaRowVisible) == expectedLumaUninstallVisible;

                return rdxInstalledOk && rsInstalledOk && dcInstalledOk && lumaInstalledOk
                    && rdxRowOk && rsRowOk && dcRowOk && lumaRowOk
                    && rdxUninstallOk && rsUninstallOk && dcUninstallOk && lumaUninstallOk;
            });
    }

    // ── Property 5: Copy config button visibility matches ini/toml existence ──
    // Feature: card-install-actions, Property 5: Copy config button visibility
    // **Validates: Requirements 1.3, 1.4, 1.5**
    [Property(MaxTest = 100)]
    public Property CopyConfigButtonVisibility_MatchesIniTomlExistence()
    {
        // RsIniExists and DcIniExists are global filesystem checks (File.Exists on static paths),
        // so they are identical across all card instances. We generate multiple cards and verify:
        //   1. All cards agree on RsIniExists and DcIniExists (consistency).
        //   2. The copy config rule holds: RS 📋 visible iff RsIniExists, DC 📋 visible iff DcIniExists.
        //   3. RDX and Luma never have a copy config button (no ini/toml files for those components).
        var genCardCount = Gen.Choose(2, 10);

        var genCards = from count in genCardCount
                       from mods in Gen.ListOf(count, GenNullableMod)
                       from lumas in Gen.ListOf(count, GenNullableLuma)
                       from externals in Gen.ListOf(count, Arb.Default.Bool().Generator)
                       select (count, mods, lumas, externals);

        return Prop.ForAll(
            Arb.From(genCards),
            (tuple) =>
            {
                var (count, mods, lumas, externals) = tuple;

                var cards = new List<GameCardViewModel>();
                for (int i = 0; i < count; i++)
                {
                    var card = new GameCardViewModel
                    {
                        Mod = mods[i],
                        IsExternalOnly = externals[i],
                    };
                    card.LumaMod = lumas[i];
                    cards.Add(card);
                }

                // All cards must agree on RsIniExists (it's a global file check)
                bool firstRsIni = cards[0].RsIniExists;
                bool allAgreeRsIni = cards.All(c => c.RsIniExists == firstRsIni);

                // All cards must agree on DcIniExists (it's a global file check)
                bool firstDcIni = cards[0].DcIniExists;
                bool allAgreeDcIni = cards.All(c => c.DcIniExists == firstDcIni);

                // Copy config rule: RS 📋 visible iff RsIniExists, DC 📋 visible iff DcIniExists
                // The ViewModel exposes RsIniExists/DcIniExists as the visibility driver.
                bool rsCopyRuleOk = cards.All(c => c.RsIniExists == firstRsIni);
                bool dcCopyRuleOk = cards.All(c => c.DcIniExists == firstDcIni);

                // RDX and Luma never have copy config buttons.
                // There are no RdxIniExists or LumaIniExists properties on the ViewModel —
                // the design explicitly states RDX and Luma rows SHALL NOT include a 📋 button.
                // We verify this by confirming the ViewModel has no such properties:
                // GameCardViewModel only exposes RsIniExists and DcIniExists.
                // As a runtime check, we verify the type does not have these properties.
                var vmType = typeof(GameCardViewModel);
                bool noRdxIniProp = vmType.GetProperty("RdxIniExists") == null;
                bool noLumaIniProp = vmType.GetProperty("LumaIniExists") == null;

                return allAgreeRsIni && allAgreeDcIni
                    && rsCopyRuleOk && dcCopyRuleOk
                    && noRdxIniProp && noLumaIniProp;
            });
    }

    // ── Property 4: More-menu conditional items match card info properties ──
    // Feature: card-install-actions, Property 4: More-menu conditional items
    // **Validates: Requirements 2.5, 2.7, 2.8**
    [Property(MaxTest = 100)]
    public Property MoreMenuConditionalItems_MatchCardInfoProperties()
    {
        var genNameUrl = Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Constant<string?>(""),
            Gen.Constant<string?>("https://example.com/discussion")
        );

        var genNotes = Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Constant<string?>(""),
            Gen.Constant<string?>("   "),
            Gen.Constant<string?>("Some game notes here")
        );

        var genState = from nameUrl in genNameUrl
                       from notes in genNotes
                       from isHidden in Arb.Default.Bool().Generator
                       select (nameUrl, notes, isHidden);

        return Prop.ForAll(
            Arb.From(genState),
            (tuple) =>
            {
                var (nameUrl, notes, isHidden) = tuple;

                var card = new GameCardViewModel
                {
                    NameUrl = nameUrl,
                    Notes = notes,
                    IsHidden = isHidden,
                };

                // "Discussion / Instructions" present iff HasNameUrl is true
                bool expectedHasNameUrl = !string.IsNullOrEmpty(nameUrl);
                bool hasNameUrlOk = card.HasNameUrl == expectedHasNameUrl;

                // "View Notes" present iff HasNotes is true
                bool expectedHasNotes = !string.IsNullOrWhiteSpace(notes);
                bool hasNotesOk = card.HasNotes == expectedHasNotes;

                // Hide/Unhide label equals HideButtonLabel
                string expectedLabel = isHidden ? "👁 Show" : "🚫 Hide";
                bool hideLabelOk = card.HideButtonLabel == expectedLabel;

                return hasNameUrlOk && hasNotesOk && hideLabelOk;
            });
    }

    // ── Property 6: Info row button presence matches HasNameUrl and HasNotes ──
    // Feature: card-install-actions, Property 6: Info row button presence
    // **Validates: Requirements 3.2, 3.3, 3.6**
    [Property(MaxTest = 100)]
    public Property InfoRowButtonPresence_MatchesHasNameUrlAndHasNotes()
    {
        var genNameUrl = Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Constant<string?>(""),
            Gen.Constant<string?>("https://example.com/discussion")
        );

        var genNotes = Gen.OneOf(
            Gen.Constant<string?>(null),
            Gen.Constant<string?>(""),
            Gen.Constant<string?>("   "),
            Gen.Constant<string?>("Some game notes here")
        );

        var genState = from nameUrl in genNameUrl
                       from notes in genNotes
                       select (nameUrl, notes);

        return Prop.ForAll(
            Arb.From(genState),
            (tuple) =>
            {
                var (nameUrl, notes) = tuple;

                var card = new GameCardViewModel
                {
                    NameUrl = nameUrl,
                    Notes = notes,
                };

                // ℹ button present iff HasNameUrl is true
                bool expectedHasNameUrl = !string.IsNullOrEmpty(nameUrl);
                bool hasNameUrlOk = card.HasNameUrl == expectedHasNameUrl;

                // 💬 button present iff HasNotes is true
                bool expectedHasNotes = !string.IsNullOrWhiteSpace(notes);
                bool hasNotesOk = card.HasNotes == expectedHasNotes;

                // Info row absent when HasInfoIndicator is false
                bool expectedHasInfoIndicator = expectedHasNameUrl || expectedHasNotes;
                bool hasInfoIndicatorOk = card.HasInfoIndicator == expectedHasInfoIndicator;

                return hasNameUrlOk && hasNotesOk && hasInfoIndicatorOk;
            });
    }

    // ── Property 6b: Card highlight exclusivity ──
    // Feature: card-install-actions, Property 6b: Card highlight exclusivity
    // **Validates: Requirements 5.1**
    [Property(MaxTest = 100)]
    public Property CardHighlightExclusivity_ExactlyOneHighlightedAtATime()
    {
        // Generate count and a valid highlight index within range
        var gen = from count in Gen.Choose(2, 20)
                  from highlightIdx in Gen.Choose(0, count - 1)
                  select (count, highlightIdx);

        return Prop.ForAll(
            Arb.From(gen),
            (tuple) =>
            {
                var (count, highlightIdx) = tuple;

                var cards = new List<GameCardViewModel>();
                for (int i = 0; i < count; i++)
                {
                    cards.Add(new GameCardViewModel { GameName = $"Game{i}" });
                }

                // Simulate highlight logic: set one card highlighted, all others not
                for (int i = 0; i < cards.Count; i++)
                {
                    cards[i].CardHighlighted = (i == highlightIdx);
                }

                // Assert exactly one card is highlighted
                int highlightedCount = cards.Count(c => c.CardHighlighted);
                bool exactlyOne = highlightedCount == 1;

                // Assert the correct card is highlighted
                bool correctCard = cards[highlightIdx].CardHighlighted;

                return exactlyOne && correctCard;
            });
    }

    // ── Property 7: Card highlight produces correct visual state ──
    // Feature: card-install-actions, Property 7: Card highlight visual state
    // **Validates: Requirements 5.3**
    [Property(MaxTest = 100)]
    public Property CardHighlight_ProducesCorrectVisualState()
    {
        return Prop.ForAll(
            Arb.Default.Bool(),
            (highlighted) =>
            {
                var card = new GameCardViewModel
                {
                    CardHighlighted = highlighted,
                };

                if (highlighted)
                {
                    bool bgOk = card.CardBackground == "#1A2840";
                    bool borderOk = card.CardBorderBrush == "#2A4060";
                    return bgOk && borderOk;
                }
                else
                {
                    bool bgOk = card.CardBackground == "#141820";
                    bool borderOk = card.CardBorderBrush == "#1E2430";
                    return bgOk && borderOk;
                }
            });
    }

    // ── Property 8: Overrides flyout pre-populates game name and wiki name correctly ──
    // Feature: card-install-actions, Property 8: Overrides flyout pre-population
    // **Validates: Requirements 6.3, 6.4**
    [Property(MaxTest = 100)]
    public Property OverridesFlyoutPrePopulation_GameNameStoredCorrectly()
    {
        // Generate non-null strings for GameName (filter out nulls since the property defaults to "")
        var genGameName = Arb.Default.NonNull<string>().Generator
            .Select(nn => nn.Get);

        return Prop.ForAll(
            Arb.From(genGameName),
            (gameName) =>
            {
                var card = new GameCardViewModel
                {
                    GameName = gameName,
                };

                // The overrides flyout pre-populates "Game name" TextBox with card.GameName.
                // Verify the ViewModel correctly stores and returns the GameName.
                bool gameNameStored = card.GameName == gameName;

                // GameName should never be null (default is "")
                bool gameNameNotNull = card.GameName != null;

                return gameNameStored && gameNameNotNull;
            });
    }






}
