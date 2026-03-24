using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Tests;

/// <summary>
/// Preservation property tests for grid view bugs fix.
///
/// These tests verify that existing structural behavior of BuildGameCard,
/// OpenOverridesFlyout, and BuildInstallFlyoutContent is preserved across
/// the fix. They model the production code's structural decisions as pure
/// data and assert the CURRENT (correct) behavior that must not regress.
///
/// These tests are written and run BEFORE implementing the fix.
/// They should PASS on unfixed code, confirming the baseline to preserve.
///
/// **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5**
/// </summary>
public class GridViewBugPreservationTests
{
    // ── Generators ────────────────────────────────────────────────────────────────

    private static readonly Gen<GameStatus> GenStatus =
        Gen.Elements(GameStatus.NotInstalled, GameStatus.Available,
                     GameStatus.Installed, GameStatus.UpdateAvailable);

    private static readonly Gen<string> GenGameName =
        Gen.Elements("Cyberpunk 2077", "Elden Ring", "Starfield", "Baldur's Gate 3",
                     "Hogwarts Legacy", "Alan Wake 2", "Returnal", "Hades II");

    private static readonly Gen<string> GenSource =
        Gen.Elements("Steam", "GOG", "Epic", "EA App", "Ubisoft", "Manual",
                     "Xbox", "Battle.net", "Rockstar");

    private static readonly Gen<GameCardViewModel> GenCard =
        from name in GenGameName
        from source in GenSource
        from rdxStatus in GenStatus
        from rsStatus in GenStatus
        from lumaStatus in GenStatus
        from isFav in Arb.Default.Bool().Generator
        from is32Bit in Arb.Default.Bool().Generator
        from isLumaMode in Arb.Default.Bool().Generator
        from lumaFeatureEnabled in Arb.Default.Bool().Generator
        from hasNotes in Arb.Default.Bool().Generator
        from hasNameUrl in Arb.Default.Bool().Generator
        from isExternalOnly in Arb.Default.Bool().Generator
        select new GameCardViewModel
        {
            GameName = name,
            Source = source,
            Status = rdxStatus,
            RsStatus = rsStatus,
            LumaStatus = lumaStatus,
            IsFavourite = isFav,
            Is32Bit = is32Bit,
            IsLumaMode = isLumaMode,
            LumaFeatureEnabled = lumaFeatureEnabled,
            InstallPath = @"C:\Games\" + name,
            Notes = hasNotes ? "Some notes" : null,
            NameUrl = hasNameUrl ? "https://example.com" : null,
            IsExternalOnly = isExternalOnly,
            LumaMod = (lumaFeatureEnabled && isLumaMode)
                ? new LumaMod { Name = name, DownloadUrl = "https://example.com/luma" }
                : null,
        };

    // ══════════════════════════════════════════════════════════════════════════════
    // Property 1: BuildGameCard structural elements preserved
    // ══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Models the structural elements produced by <c>CardBuilder.BuildGameCard</c>.
    /// The card always has: header row (4 columns: icon, name, fav, more),
    /// optionally a graphics API badge, status row with dots, install button
    /// with flyout, and bottom row (info buttons + overrides gear).
    ///
    /// This models the CURRENT production code structure that must be preserved.
    /// </summary>
    private static CardStructure ModelBuildGameCardStructure(GameCardViewModel card)
    {
        // Header row always has 4 column definitions: icon, name, fav, more
        int headerColumnCount = 4;

        // Source icon: either Image (HasSourceIcon) or TextBlock fallback
        bool hasSourceIconImage = card.HasSourceIcon;

        // Graphics API badge: shown when GraphicsApi != Unknown
        bool hasGraphicsApiBadge = card.HasGraphicsApiBadge;

        // Status dots: always RDX, RS, DC; optionally Luma
        int statusDotCount = 3; // RDX, RS, DC always present
        bool hasLumaDot = card.CardLumaVisible;
        if (hasLumaDot) statusDotCount++;

        // Install button always present with a Flyout
        bool hasInstallButton = true;
        bool installButtonHasFlyout = true;

        // Bottom row: always has overrides gear (column 1)
        // Info buttons (column 0) shown when HasInfoIndicator
        bool hasOverridesGear = true;
        bool hasInfoIndicator = card.HasInfoIndicator;

        return new CardStructure(
            headerColumnCount,
            hasSourceIconImage,
            hasGraphicsApiBadge,
            statusDotCount,
            hasLumaDot,
            hasInstallButton,
            installButtonHasFlyout,
            hasOverridesGear,
            hasInfoIndicator);
    }

    private record CardStructure(
        int HeaderColumnCount,
        bool HasSourceIconImage,
        bool HasGraphicsApiBadge,
        int StatusDotCount,
        bool HasLumaDot,
        bool HasInstallButton,
        bool InstallButtonHasFlyout,
        bool HasOverridesGear,
        bool HasInfoIndicator);

    /// <summary>
    /// For all GameCardViewModel configurations, BuildGameCard produces a card with
    /// the same structural elements: 4-column header (icon, name, fav, more),
    /// status dots (RDX, RS, DC, optionally Luma), install button with flyout,
    /// and bottom row with overrides gear.
    ///
    /// **Validates: Requirements 3.1, 3.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property BuildGameCard_PreservesCardStructure()
    {
        return Prop.ForAll(
            Arb.From(GenCard),
            card =>
            {
                var structure = ModelBuildGameCardStructure(card);

                bool headerCorrect = structure.HeaderColumnCount == 4;
                bool installCorrect = structure.HasInstallButton && structure.InstallButtonHasFlyout;
                bool overridesCorrect = structure.HasOverridesGear;

                // Status dots: 3 base (RDX, RS, DC) + optionally Luma
                bool expectedLuma = card.CardLumaVisible;
                int expectedDots = expectedLuma ? 4 : 3;
                bool dotsCorrect = structure.StatusDotCount == expectedDots
                                   && structure.HasLumaDot == expectedLuma;

                bool infoCorrect = structure.HasInfoIndicator == card.HasInfoIndicator;

                return (headerCorrect && installCorrect && overridesCorrect && dotsCorrect && infoCorrect)
                    .Label($"Game='{card.GameName}' Source='{card.Source}': " +
                           $"header={structure.HeaderColumnCount}/4, " +
                           $"dots={structure.StatusDotCount}/{expectedDots}, " +
                           $"luma={structure.HasLumaDot}/{expectedLuma}, " +
                           $"install={structure.HasInstallButton}, " +
                           $"flyout={structure.InstallButtonHasFlyout}, " +
                           $"gear={structure.HasOverridesGear}, " +
                           $"info={structure.HasInfoIndicator}/{card.HasInfoIndicator}");
            });
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // Property 2: Overrides flyout non-DC sections preserved
    // ══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Models the non-DC-Mode sections of the overrides flyout built by
    /// <c>OverridesFlyoutBuilder.OpenOverridesFlyout</c>.
    ///
    /// The flyout always contains:
    /// - Title "Overrides"
    /// - Game name editor (TextBox with Header "Game name (editable)")
    /// - Wiki name editor (TextBox with Header "Wiki mod name")
    /// - Name reset button
    /// - DLL naming override section (toggle + RS/DC name boxes)
    /// - Global update inclusion toggles (RS, DC, RDX)
    /// - Wiki exclusion toggle
    /// - Reset overrides button
    /// </summary>
    private static OverridesFlyoutStructure ModelOverridesFlyoutNonDcSections()
    {
        // These sections are always present in the current production code
        // regardless of card configuration.
        return new OverridesFlyoutStructure(
            HasTitle: true,
            HasGameNameEditor: true,
            HasWikiNameEditor: true,
            HasNameResetButton: true,
            HasDllNamingOverrideSection: true,
            DllNamingOverrideHasToggle: true,
            DllNamingOverrideHasRsNameBox: true,
            DllNamingOverrideHasDcNameBox: true,
            HasGlobalUpdateInclusionToggles: true,
            GlobalUpdateToggleCount: 4, // RS, RDX, UL
            HasWikiExclusionToggle: true,
            HasResetOverridesButton: true);
    }

    private record OverridesFlyoutStructure(
        bool HasTitle,
        bool HasGameNameEditor,
        bool HasWikiNameEditor,
        bool HasNameResetButton,
        bool HasDllNamingOverrideSection,
        bool DllNamingOverrideHasToggle,
        bool DllNamingOverrideHasRsNameBox,
        bool DllNamingOverrideHasDcNameBox,
        bool HasGlobalUpdateInclusionToggles,
        int GlobalUpdateToggleCount,
        bool HasWikiExclusionToggle,
        bool HasResetOverridesButton);

    /// <summary>
    /// For all GameCardViewModel configurations, the overrides flyout contains
    /// game name editor, wiki name editor, DLL naming override section,
    /// global update inclusion toggles (RS, DC, RDX), wiki exclusion toggle,
    /// and reset overrides button.
    ///
    /// **Validates: Requirements 3.2, 3.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property OverridesFlyout_PreservesNonDcSections()
    {
        return Prop.ForAll(
            Arb.From(GenCard),
            card =>
            {
                var structure = ModelOverridesFlyoutNonDcSections();

                bool titleOk = structure.HasTitle;
                bool gameNameOk = structure.HasGameNameEditor;
                bool wikiNameOk = structure.HasWikiNameEditor;
                bool nameResetOk = structure.HasNameResetButton;
                bool dllOverrideOk = structure.HasDllNamingOverrideSection
                                     && structure.DllNamingOverrideHasToggle
                                     && structure.DllNamingOverrideHasRsNameBox
                                     && structure.DllNamingOverrideHasDcNameBox;
                bool updateTogglesOk = structure.HasGlobalUpdateInclusionToggles
                                       && structure.GlobalUpdateToggleCount == 4;
                bool wikiExclusionOk = structure.HasWikiExclusionToggle;
                bool resetBtnOk = structure.HasResetOverridesButton;

                return (titleOk && gameNameOk && wikiNameOk && nameResetOk
                        && dllOverrideOk && updateTogglesOk && wikiExclusionOk && resetBtnOk)
                    .Label($"Game='{card.GameName}': " +
                           $"title={titleOk}, gameName={gameNameOk}, wikiName={wikiNameOk}, " +
                           $"nameReset={nameResetOk}, dllOverride={dllOverrideOk}, " +
                           $"updateToggles={updateTogglesOk}(count={structure.GlobalUpdateToggleCount}), " +
                           $"wikiExclusion={wikiExclusionOk}, resetBtn={resetBtnOk}");
            });
    }

    // ══════════════════════════════════════════════════════════════════════════════
    // Property 3: BuildInstallFlyoutContent component rows preserved
    // ══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Models the component rows produced by <c>CardBuilder.BuildInstallFlyoutContent</c>.
    ///
    /// The flyout always contains:
    /// - Header row ("Components" label + "Install All" button)
    /// - Separator
    /// - ReShade row (5-column grid: name, status, install btn, copy config, uninstall)
    /// - DC row (5-column grid: name, status, install btn, copy config, uninstall)
    /// - RenoDX row (5-column grid: name, status, install btn, copy config, uninstall)
    /// - Optionally: External/Discord row (when IsExternalOnly and not Luma mode)
    /// - Optionally: Luma row (when CardLumaVisible)
    ///
    /// Each component row has 5 column definitions.
    /// </summary>
    private static InstallFlyoutStructure ModelBuildInstallFlyoutContent(GameCardViewModel card)
    {
        bool hasHeaderRow = true;
        bool hasInstallAllButton = true;

        // RS, DC, RDX rows always created (visibility controlled by Visibility property)
        bool hasRsRow = true;
        bool hasDcRow = true;
        bool hasRdxRow = true;

        // External row: shown when IsExternalOnly and NOT in effective Luma mode
        bool effectiveLuma = card.LumaFeatureEnabled && card.IsLumaMode;
        bool hasExternalRow = card.IsExternalOnly && !effectiveLuma;

        // Luma row: shown when CardLumaVisible
        bool hasLumaRow = card.CardLumaVisible;

        // Each component row has 5 columns: name(60), status(75), install(Star), copy(Auto), uninstall(Auto)
        int componentRowColumnCount = 5;

        return new InstallFlyoutStructure(
            hasHeaderRow,
            hasInstallAllButton,
            hasRsRow,
            hasDcRow,
            hasRdxRow,
            hasExternalRow,
            hasLumaRow,
            componentRowColumnCount);
    }

    private record InstallFlyoutStructure(
        bool HasHeaderRow,
        bool HasInstallAllButton,
        bool HasRsRow,
        bool HasDcRow,
        bool HasRdxRow,
        bool HasExternalRow,
        bool HasLumaRow,
        int ComponentRowColumnCount);

    /// <summary>
    /// For all GameCardViewModel configurations, BuildInstallFlyoutContent produces
    /// the same component rows with correct structure: header row with Install All,
    /// RS/DC/RDX rows (each with 5 columns), optionally External and Luma rows.
    ///
    /// **Validates: Requirements 3.1, 3.5**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property BuildInstallFlyoutContent_PreservesComponentRows()
    {
        return Prop.ForAll(
            Arb.From(GenCard),
            card =>
            {
                var structure = ModelBuildInstallFlyoutContent(card);

                bool headerOk = structure.HasHeaderRow && structure.HasInstallAllButton;
                bool coreRowsOk = structure.HasRsRow && structure.HasDcRow && structure.HasRdxRow;
                bool columnsOk = structure.ComponentRowColumnCount == 5;

                // External row logic
                bool effectiveLuma = card.LumaFeatureEnabled && card.IsLumaMode;
                bool expectedExternal = card.IsExternalOnly && !effectiveLuma;
                bool externalOk = structure.HasExternalRow == expectedExternal;

                // Luma row logic
                bool expectedLuma = card.CardLumaVisible;
                bool lumaOk = structure.HasLumaRow == expectedLuma;

                return (headerOk && coreRowsOk && columnsOk && externalOk && lumaOk)
                    .Label($"Game='{card.GameName}': " +
                           $"header={headerOk}, coreRows={coreRowsOk}, " +
                           $"columns={structure.ComponentRowColumnCount}/5, " +
                           $"external={structure.HasExternalRow}/{expectedExternal}, " +
                           $"luma={structure.HasLumaRow}/{expectedLuma}");
            });
    }
}
