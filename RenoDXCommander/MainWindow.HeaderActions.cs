// MainWindow.HeaderActions.cs -- the game header's buttons and their click handlers, for the Tabs
// layout's mockup header (DetailLayoutHost.Header.cs). That header copies its state from the
// Classic header controls (filled by DetailPanelBuilder.PopulateDetailPanel) and calls these
// handlers with the Classic button as sender, so header rules stay in one place.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    internal Button HeaderLaunchButton => DetailLaunchBtn;
    internal Button HeaderHdrButton => DetailHdrToggleBtn;
    internal TextBlock HeaderHdrText => DetailHdrToggleText;
    internal Button HeaderResButton => DetailResToggleBtn;
    internal TextBlock HeaderResText => DetailResToggleText;
    internal Button HeaderFavButton => DetailFavBtn;
    internal Button HeaderHideButton => DetailHideBtn;
    internal TextBlock HeaderHideText => DetailHideIcon;
    internal Button HeaderFolderButton => DetailFolderBtn;
    internal Button HeaderConfigButton => DetailAppDataBtn;
    internal Button HeaderNexusButton => DetailNexusModsBtn;
    internal Button HeaderPcgwButton => DetailPcgwBtn;
    internal Button HeaderUwButton => DetailUwFixBtn;
    internal Button HeaderUltraPlusButton => DetailUltraPlusBtn;
    internal TextBlock HeaderGameName => DetailGameName;
    internal TextBlock HeaderInstallPath => DetailInstallPath;
    internal FrameworkElement HeaderSourceBadge => DetailSourceBadge;
    internal TextBlock HeaderSourceText => DetailSourceText;
    internal FrameworkElement HeaderEngineBadge => DetailEngineBadge;
    internal TextBlock HeaderEngineText => DetailEngineText;
    internal Panel HeaderApiBadges => DetailGraphicsApiBadgePanel;
    internal Panel HeaderAuthorBadges => DetailAuthorBadgePanel;

    internal void HeaderLaunch(RoutedEventArgs e) => LaunchGame_Click(DetailLaunchBtn, e);
    internal void HeaderToggleHdr(RoutedEventArgs e) => HdrToggle_Click(DetailHdrToggleBtn, e);
    internal void HeaderToggleRes(RoutedEventArgs e) => ResToggle_Click(DetailResToggleBtn, e);
    internal void HeaderFavourite(RoutedEventArgs e) => FavouriteButton_Click(DetailFavBtn, e);
    internal void HeaderHide(RoutedEventArgs e) => HideButton_Click(DetailHideBtn, e);
    internal void HeaderOpenFolder(RoutedEventArgs e) => OpenFolder_Click(DetailFolderBtn, e);
    internal void HeaderOpenConfig(RoutedEventArgs e) => OpenAppData_Click(DetailAppDataBtn, e);
    internal void HeaderNexus(RoutedEventArgs e) => NexusModsLink_Click(DetailNexusModsBtn, e);
    internal void HeaderPcgw(RoutedEventArgs e) => PcgwLink_Click(DetailPcgwBtn, e);
    internal void HeaderUwFix(RoutedEventArgs e) => UwFixLink_Click(DetailUwFixBtn, e);
    internal void HeaderUltraPlus(RoutedEventArgs e) => UltraPlusLink_Click(DetailUltraPlusBtn, e);
    internal Task HeaderCreateShortcutAsync(ViewModels.GameCardViewModel card) => CreateDesktopShortcutAsync(card);
}
