using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for GameCardViewModel.NotifyAll() notification behavior.
/// </summary>
public class NotifyAllPropertyTests
{
    // ── Generators ────────────────────────────────────────────────────────────────

    private static readonly Gen<GameStatus> GenStatus =
        Gen.Elements(GameStatus.NotInstalled, GameStatus.Available,
                     GameStatus.Installed, GameStatus.UpdateAvailable);

    private static readonly Gen<string> GenName =
        Gen.Elements("Cyberpunk 2077", "Elden Ring", "Starfield", "Baldur's Gate 3",
                     "Hogwarts Legacy", "Alan Wake 2", "Returnal", "Hades II");

    private static readonly Gen<string> GenSource =
        Gen.Elements("Steam", "GOG", "Epic", "EA App", "Ubisoft", "Manual", "Xbox", "Battle.net");

    private static readonly Gen<string> GenPath =
        Gen.Elements(@"C:\Games\Game1", @"D:\SteamLibrary\common\Game2", @"E:\GOG\Game3", "");

    /// <summary>
    /// Generates a GameCardViewModel with arbitrary observable state.
    /// </summary>
    private static readonly Gen<GameCardViewModel> GenCard =
        from name in GenName
        from source in GenSource
        from path in GenPath
        from rdxStatus in GenStatus
        from rsStatus in GenStatus
        from lumaStatus in GenStatus
        from isInstalling in Arb.Default.Bool().Generator
        from rsInstalling in Arb.Default.Bool().Generator
        from lumaInstalling in Arb.Default.Bool().Generator
        from isFav in Arb.Default.Bool().Generator
        from isHidden in Arb.Default.Bool().Generator
        from isLumaMode in Arb.Default.Bool().Generator
        from lumaEnabled in Arb.Default.Bool().Generator
        from is32Bit in Arb.Default.Bool().Generator
        from isExternalOnly in Arb.Default.Bool().Generator
        from componentExpanded in Arb.Default.Bool().Generator
        select new GameCardViewModel
        {
            GameName = name,
            Source = source,
            InstallPath = path,
            Status = rdxStatus,
            RsStatus = rsStatus,
            LumaStatus = lumaStatus,
            IsInstalling = isInstalling,
            RsIsInstalling = rsInstalling,
            IsLumaInstalling = lumaInstalling,
            IsFavourite = isFav,
            IsHidden = isHidden,
            IsLumaMode = isLumaMode,
            LumaFeatureEnabled = lumaEnabled,
            Is32Bit = is32Bit,
            IsExternalOnly = isExternalOnly,
            ComponentExpanded = componentExpanded
        };

    // ── Property 1: NotifyAll produces no duplicate notifications ──────────────────
    // Feature: codebase-optimization, Property 1: NotifyAll produces no duplicate notifications
    // **Validates: Requirements 2.2, 2.3**
    [Property(MaxTest = 100)]
    public Property NotifyAll_ProducesNoDuplicateNotifications()
    {
        return Prop.ForAll(
            Arb.From(GenCard),
            (GameCardViewModel card) =>
            {
                var notifiedProperties = new List<string>();

                card.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName is not null)
                        notifiedProperties.Add(e.PropertyName);
                };

                card.NotifyAll();

                var duplicates = notifiedProperties
                    .GroupBy(p => p)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key)
                    .ToList();

                return (duplicates.Count == 0)
                    .Label($"Duplicate notifications: [{string.Join(", ", duplicates)}]");
            });
    }

    // ── Property 8: NotifyAll followed by reading computed properties does not throw ──
    // Feature: codebase-optimization, Property 8: NotifyAll followed by reading computed properties does not throw
    // **Validates: Requirements 10.7**
    [Property(MaxTest = 100)]
    public Property NotifyAll_ThenReadAllComputedProperties_DoesNotThrow()
    {
        return Prop.ForAll(
            Arb.From(GenCard),
            (GameCardViewModel card) =>
            {
                // Ensure EngineHint is non-null so computed properties that call Contains on it don't throw
                card.EngineHint ??= "";

                card.NotifyAll();

                var errors = new List<string>();

                void Read(string name, Func<object?> accessor)
                {
                    try { _ = accessor(); }
                    catch (Exception ex) { errors.Add($"{name}: {ex.GetType().Name} — {ex.Message}"); }
                }

                // ── UI.cs computed properties ──────────────────────────────────
                Read(nameof(card.SidebarItemBackground), () => card.SidebarItemBackground);
                Read(nameof(card.SidebarItemBorderBrush), () => card.SidebarItemBorderBrush);
                Read(nameof(card.SidebarItemForeground), () => card.SidebarItemForeground);
                Read(nameof(card.CardBackground), () => card.CardBackground);
                Read(nameof(card.CardBorderBrush), () => card.CardBorderBrush);
                Read(nameof(card.CardRdxStatusDot), () => card.CardRdxStatusDot);
                Read(nameof(card.CardRsStatusDot), () => card.CardRsStatusDot);
                Read(nameof(card.CardLumaStatusDot), () => card.CardLumaStatusDot);
                Read(nameof(card.CardLumaVisible), () => card.CardLumaVisible);
                Read(nameof(card.CardPrimaryActionLabel), () => card.CardPrimaryActionLabel);
                Read(nameof(card.HasInfoIndicator), () => card.HasInfoIndicator);
                Read(nameof(card.CanCardInstall), () => card.CanCardInstall);
                Read(nameof(card.CardRdxInstallEnabled), () => card.CardRdxInstallEnabled);
                Read(nameof(card.CardRsInstallEnabled), () => card.CardRsInstallEnabled);
                Read(nameof(card.CardLumaInstallEnabled), () => card.CardLumaInstallEnabled);
                Read(nameof(card.WikiStatusLabel), () => card.WikiStatusLabel);
                Read(nameof(card.WikiStatusIcon), () => card.WikiStatusIcon);
                Read(nameof(card.WikiStatusIconVisible), () => card.WikiStatusIconVisible);
                Read(nameof(card.WikiStatusBadgeBackground), () => card.WikiStatusBadgeBackground);
                Read(nameof(card.WikiStatusBadgeBorderBrush), () => card.WikiStatusBadgeBorderBrush);
                Read(nameof(card.WikiStatusBadgeForeground), () => card.WikiStatusBadgeForeground);
                Read(nameof(card.SourceIcon), () => card.SourceIcon);
                Read(nameof(card.SourceIconPath), () => card.SourceIconPath);
                Read(nameof(card.SourceIconUri), () => card.SourceIconUri);
                Read(nameof(card.HasSourceIcon), () => card.HasSourceIcon);
                Read(nameof(card.SourceIconImageVisibility), () => card.SourceIconImageVisibility);
                Read(nameof(card.SourceIconTextVisibility), () => card.SourceIconTextVisibility);
                Read(nameof(card.Is32BitBadgeVisibility), () => card.Is32BitBadgeVisibility);
                Read(nameof(card.Is32BitUeWipVisibility), () => card.Is32BitUeWipVisibility);
                Read(nameof(card.InstallPathDisplay), () => card.InstallPathDisplay);
                Read(nameof(card.InstalledFileLabel), () => card.InstalledFileLabel);
                Read(nameof(card.HasNotes), () => card.HasNotes);
                Read(nameof(card.IsUnityGeneric), () => card.IsUnityGeneric);
                Read(nameof(card.HasDualBitMod), () => card.HasDualBitMod);
                Read(nameof(card.HasExtraLinks), () => card.HasExtraLinks);
                Read(nameof(card.HasNameUrl), () => card.HasNameUrl);
                Read(nameof(card.HideButtonLabel), () => card.HideButtonLabel);
                Read(nameof(card.StarForeground), () => card.StarForeground);
                Read(nameof(card.IsFavouriteVisibility), () => card.IsFavouriteVisibility);
                Read(nameof(card.IsNotFavouriteVisibility), () => card.IsNotFavouriteVisibility);
                Read(nameof(card.SourceBadgeVisibility), () => card.SourceBadgeVisibility);
                Read(nameof(card.GenericBadgeVisibility), () => card.GenericBadgeVisibility);
                Read(nameof(card.EngineBadgeVisibility), () => card.EngineBadgeVisibility);
                Read(nameof(card.NotesButtonVisibility), () => card.NotesButtonVisibility);
                Read(nameof(card.ProgressVisibility), () => card.ProgressVisibility);
                Read(nameof(card.ExternalBtnVisibility), () => card.ExternalBtnVisibility);
                Read(nameof(card.ExtraLinkVisibility), () => card.ExtraLinkVisibility);
                Read(nameof(card.InstalledFileLabelVisible), () => card.InstalledFileLabelVisible);
                Read(nameof(card.InstallOnlyBtnVisibility), () => card.InstallOnlyBtnVisibility);
                Read(nameof(card.ReinstallRowVisibility), () => card.ReinstallRowVisibility);
                Read(nameof(card.DualBitInstallVisibility), () => card.DualBitInstallVisibility);
                Read(nameof(card.UpdateBadgeVisibility), () => card.UpdateBadgeVisibility);
                Read(nameof(card.IsHiddenVisibility), () => card.IsHiddenVisibility);
                Read(nameof(card.IsNotHiddenVisibility), () => card.IsNotHiddenVisibility);
                Read(nameof(card.NameLinkVisibility), () => card.NameLinkVisibility);
                Read(nameof(card.NoModVisibility), () => card.NoModVisibility);
                Read(nameof(card.SwitchToLumaVisibility), () => card.SwitchToLumaVisibility);
                Read(nameof(card.ComponentDetailVisibility), () => card.ComponentDetailVisibility);
                Read(nameof(card.ExpandChevron), () => card.ExpandChevron);
                Read(nameof(card.AuthorList), () => card.AuthorList);
                Read(nameof(card.HasAuthors), () => card.HasAuthors);

                // ── RenoDX computed properties ─────────────────────────────────
                Read(nameof(card.InstallActionLabel), () => card.InstallActionLabel);
                Read(nameof(card.CanInstall), () => card.CanInstall);
                Read(nameof(card.GenericModLabel), () => card.GenericModLabel);
                Read(nameof(card.InstallBtnBackground), () => card.InstallBtnBackground);
                Read(nameof(card.InstallBtnForeground), () => card.InstallBtnForeground);
                Read(nameof(card.InstallBtnBorderBrush), () => card.InstallBtnBorderBrush);
                Read(nameof(card.UeExtendedLabel), () => card.UeExtendedLabel);
                Read(nameof(card.UeExtendedBackground), () => card.UeExtendedBackground);
                Read(nameof(card.UeExtendedForeground), () => card.UeExtendedForeground);
                Read(nameof(card.UeExtendedBorderBrush), () => card.UeExtendedBorderBrush);
                Read(nameof(card.UeExtendedToggleVisibility), () => card.UeExtendedToggleVisibility);
                Read(nameof(card.CombinedStatusDot), () => card.CombinedStatusDot);
                Read(nameof(card.CombinedActionLabel), () => card.CombinedActionLabel);
                Read(nameof(card.CanCombinedInstall), () => card.CanCombinedInstall);
                Read(nameof(card.CombinedBtnBackground), () => card.CombinedBtnBackground);
                Read(nameof(card.CombinedBtnForeground), () => card.CombinedBtnForeground);
                Read(nameof(card.CombinedBtnBorderBrush), () => card.CombinedBtnBorderBrush);
                Read(nameof(card.CombinedRowVisibility), () => card.CombinedRowVisibility);
                Read(nameof(card.ComponentExpandVisibility), () => card.ComponentExpandVisibility);
                Read(nameof(card.ChevronCornerRadius), () => card.ChevronCornerRadius);
                Read(nameof(card.ChevronBorderThickness), () => card.ChevronBorderThickness);
                Read(nameof(card.RdxStatusText), () => card.RdxStatusText);
                Read(nameof(card.RdxStatusColor), () => card.RdxStatusColor);
                Read(nameof(card.RdxShortAction), () => card.RdxShortAction);
                Read(nameof(card.IsNotInstalling), () => card.IsNotInstalling);
                Read(nameof(card.R7bInstallCornerRadius), () => card.R7bInstallCornerRadius);
                Read(nameof(card.R7bInstallBorderThickness), () => card.R7bInstallBorderThickness);
                Read(nameof(card.R7bInstallMargin), () => card.R7bInstallMargin);
                Read(nameof(card.R7bLumaSwitchVisibility), () => card.R7bLumaSwitchVisibility);
                Read(nameof(card.R7bLumaSwitchCornerRadius), () => card.R7bLumaSwitchCornerRadius);
                Read(nameof(card.R7bLumaSwitchBorderThickness), () => card.R7bLumaSwitchBorderThickness);
                Read(nameof(card.R7bLumaSwitchMargin), () => card.R7bLumaSwitchMargin);
                Read(nameof(card.IsManaged), () => card.IsManaged);
                Read(nameof(card.IsRdxInstalled), () => card.IsRdxInstalled);

                // ── ReShade computed properties ────────────────────────────────
                Read(nameof(card.RsStatusDot), () => card.RsStatusDot);
                Read(nameof(card.RsActionLabel), () => card.RsActionLabel);
                Read(nameof(card.RsBtnBackground), () => card.RsBtnBackground);
                Read(nameof(card.RsBtnForeground), () => card.RsBtnForeground);
                Read(nameof(card.RsBtnBorderBrush), () => card.RsBtnBorderBrush);
                Read(nameof(card.RsProgressVisibility), () => card.RsProgressVisibility);
                Read(nameof(card.RsInstalledVisible), () => card.RsInstalledVisible);
                Read(nameof(card.RsDeleteVisibility), () => card.RsDeleteVisibility);
                Read(nameof(card.RsStatusText), () => card.RsStatusText);
                Read(nameof(card.RsStatusColor), () => card.RsStatusColor);
                Read(nameof(card.RsShortAction), () => card.RsShortAction);
                Read(nameof(card.IsRsNotInstalling), () => card.IsRsNotInstalling);
                Read(nameof(card.IsRsInstalled), () => card.IsRsInstalled);
                Read(nameof(card.RsInstallCornerRadius), () => card.RsInstallCornerRadius);
                Read(nameof(card.RsInstallBorderThickness), () => card.RsInstallBorderThickness);
                Read(nameof(card.RsInstallMargin), () => card.RsInstallMargin);
                Read(nameof(card.RsIniExists), () => card.RsIniExists);
                Read(nameof(card.RsIniCornerRadius), () => card.RsIniCornerRadius);
                Read(nameof(card.RsIniBorderThickness), () => card.RsIniBorderThickness);
                Read(nameof(card.RsIniMargin), () => card.RsIniMargin);
                Read(nameof(card.ReShadeRowVisibility), () => card.ReShadeRowVisibility);

                // ── Luma computed properties ───────────────────────────────────
                Read(nameof(card.IsLumaAvailable), () => card.IsLumaAvailable);
                Read(nameof(card.LumaBadgeVisibility), () => card.LumaBadgeVisibility);
                Read(nameof(card.LumaBadgeLabel), () => card.LumaBadgeLabel);
                Read(nameof(card.LumaBadgeBackground), () => card.LumaBadgeBackground);
                Read(nameof(card.LumaBadgeForeground), () => card.LumaBadgeForeground);
                Read(nameof(card.LumaBadgeBorderBrush), () => card.LumaBadgeBorderBrush);
                Read(nameof(card.LumaInstallVisibility), () => card.LumaInstallVisibility);
                Read(nameof(card.LumaReinstallVisibility), () => card.LumaReinstallVisibility);
                Read(nameof(card.IsLumaNotInstalling), () => card.IsLumaNotInstalling);
                Read(nameof(card.LumaProgressVisibility), () => card.LumaProgressVisibility);
                Read(nameof(card.LumaActionLabel), () => card.LumaActionLabel);
                Read(nameof(card.LumaStatusText), () => card.LumaStatusText);
                Read(nameof(card.LumaStatusColor), () => card.LumaStatusColor);
                Read(nameof(card.LumaShortAction), () => card.LumaShortAction);
                Read(nameof(card.LumaBtnBackground), () => card.LumaBtnBackground);
                Read(nameof(card.LumaBtnForeground), () => card.LumaBtnForeground);
                Read(nameof(card.LumaBtnBorderBrush), () => card.LumaBtnBorderBrush);
                Read(nameof(card.IsLumaInstalled), () => card.IsLumaInstalled);
                Read(nameof(card.RenoDxRowVisibility), () => card.RenoDxRowVisibility);

                return (errors.Count == 0)
                    .Label($"Properties that threw: [{string.Join("; ", errors)}]");
            });
    }
}
