// DetailPanelBuilder.cs — Core scaffolding: class declaration, constructor, current detail card state, and detail panel population.

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;
using System.IO;

namespace RenoDXCommander;

/// <summary>
/// Helper class responsible for detail panel population and overrides panel construction.
/// Extracted from MainWindow code-behind to reduce file size.
/// </summary>
public partial class DetailPanelBuilder
{
    private readonly MainWindow _window;
    private readonly DispatcherQueue _dispatcherQueue;
    private GameCardViewModel? _currentDetailCard;

    public DetailPanelBuilder(MainWindow window)
    {
        _window = window;
        _dispatcherQueue = window.DispatcherQueue;

        // Set hand cursor on link buttons so they feel like clickable links
        var handCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
        var cursorProp = typeof(UIElement).GetProperty("ProtectedCursor",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        cursorProp?.SetValue(_window.DetailNexusModsBtn, handCursor);
        cursorProp?.SetValue(_window.DetailPcgwBtn, handCursor);
        cursorProp?.SetValue(_window.DetailUwFixBtn, handCursor);
        cursorProp?.SetValue(_window.DetailUltraPlusBtn, handCursor);
    }

    /// <summary>Gets the currently displayed detail card (if any).</summary>
    public GameCardViewModel? CurrentDetailCard => _currentDetailCard;

    public void PopulateDetailPanel(GameCardViewModel card)
    {
        // Unsubscribe from previous card
        if (_currentDetailCard != null)
            _currentDetailCard.PropertyChanged -= DetailCard_PropertyChanged;

        _currentDetailCard = card;
        card.PropertyChanged += DetailCard_PropertyChanged;

        // Header
        _window.DetailGameName.Text = card.GameName;

        // Source badge
        if (card.HasSourceIcon)
        {
            _window.DetailSourceIcon.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(card.SourceIconUri);
            _window.DetailSourceIcon.Visibility = Visibility.Visible;
        }
        else
        {
            _window.DetailSourceIcon.Visibility = Visibility.Collapsed;
        }
        _window.DetailSourceText.Text = card.Source;
        _window.DetailSourceBadge.Visibility = string.IsNullOrEmpty(card.Source) ? Visibility.Collapsed : Visibility.Visible;

        // Engine badge
        if (!string.IsNullOrEmpty(card.EngineHint))
        {
            _window.DetailEngineText.Text = card.EngineHint;
            // Set engine icon
            if (card.EngineHint.IndexOf("Unreal", StringComparison.OrdinalIgnoreCase) >= 0)
                _window.DetailEngineIcon.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/icons/unrealengine.ico"));
            else if (card.EngineHint.IndexOf("Unity", StringComparison.OrdinalIgnoreCase) >= 0)
                _window.DetailEngineIcon.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/icons/unity.ico"));
            else
                _window.DetailEngineIcon.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/icons/engine.ico"));
            _window.DetailEngineBadge.Visibility = Visibility.Visible;
        }
        else _window.DetailEngineBadge.Visibility = Visibility.Collapsed;

        // Graphics API badge
        if (card.HasGraphicsApiBadge)
        {
            _window.DetailGraphicsApiText.Text = card.GraphicsApiLabel;
            _window.DetailGraphicsApiBadge.Visibility = Visibility.Visible;
        }
        else _window.DetailGraphicsApiBadge.Visibility = Visibility.Collapsed;

        // Generic badge
        if (card.IsGenericMod)
        {
            _window.DetailGenericText.Text = card.GenericModLabel;
            _window.DetailGenericBadge.Visibility = Visibility.Visible;
        }
        else _window.DetailGenericBadge.Visibility = Visibility.Collapsed;

        // 32-bit / 64-bit badge
        _window.Detail32BitBadge.Visibility = card.Is32Bit ? Visibility.Visible : Visibility.Collapsed;
        _window.Detail64BitBadge.Visibility = !card.Is32Bit ? Visibility.Visible : Visibility.Collapsed;

        // Wiki status badge — hidden from main UI, shown inside Info button dialog instead
        _window.DetailWikiBadge.Visibility = Visibility.Collapsed;
        _window.DetailSepPlatformStatus.Visibility = Visibility.Collapsed;

        // Author badges
        _window.DetailAuthorBadgePanel.Children.Clear();
        if (card.HasAuthors)
        {
            foreach (var author in card.AuthorList)
            {
                var donationUrl = GameCardViewModel.GetAuthorDonationUrl(author);
                var textBlock = new TextBlock
                {
                    Text = author,
                    FontSize = 11,
                    Foreground = UIFactory.Brush(ResourceKeys.ChipTextBrush),
                    TextDecorations = donationUrl != null ? Windows.UI.Text.TextDecorations.Underline : Windows.UI.Text.TextDecorations.None,
                };
                var badge = new Border
                {
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(6, 2, 6, 2),
                    Background = UIFactory.Brush(ResourceKeys.ChipDefaultBrush),
                    BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
                    BorderThickness = new Thickness(1),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = textBlock,
                };
                if (donationUrl != null)
                {
                    badge.PointerPressed += async (s, e) =>
                        await Windows.System.Launcher.LaunchUriAsync(new Uri(donationUrl));
                    var handCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
                    var arrowCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Arrow);
                    var cursorProp = typeof(UIElement).GetProperty("ProtectedCursor",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    badge.PointerEntered += (s, e) => cursorProp?.SetValue(badge, handCursor);
                    badge.PointerExited += (s, e) => cursorProp?.SetValue(badge, arrowCursor);
                    ToolTipService.SetToolTip(badge, $"Mod author: {author} — click to open Ko-fi donation page");
                }
                else
                {
                    ToolTipService.SetToolTip(badge, $"Mod author: {author}");
                }
                _window.DetailAuthorBadgePanel.Children.Add(badge);
            }
            _window.DetailAuthorBadgePanel.Visibility = Visibility.Visible;
        }
        else
        {
            _window.DetailAuthorBadgePanel.Visibility = Visibility.Collapsed;
        }

        // Install path + installed file
        _window.DetailInstallPath.Text = card.InstallPath;
        if (!string.IsNullOrEmpty(card.InstalledAddonFileName))
        {
            _window.DetailInstalledFile.Text = $"{card.InstalledAddonFileName}";
            _window.DetailInstalledFileBadge.Visibility = Visibility.Visible;
            _window.DetailSepModPlatform.Visibility = Visibility.Visible;
        }
        else
        {
            _window.DetailInstalledFileBadge.Visibility = Visibility.Collapsed;
            _window.DetailSepModPlatform.Visibility = Visibility.Collapsed;
        }

        // Utility buttons — set Tag for event handlers
        _window.DetailFavBtn.Tag = card;
        _window.DetailFavIcon.Text = "Favourite";
        var favColor = card.IsFavourite
            ? ((SolidColorBrush)Application.Current.Resources[ResourceKeys.AccentAmberBrush]).Color
            : ((SolidColorBrush)Application.Current.Resources[ResourceKeys.ChipTextBrush]).Color;
        _window.DetailFavIcon.Foreground = new SolidColorBrush(favColor);
        _window.DetailFavBtn.BorderBrush = card.IsFavourite
            ? new SolidColorBrush(((SolidColorBrush)Application.Current.Resources[ResourceKeys.AccentAmberBrush]).Color)
            : UIFactory.Brush(ResourceKeys.BorderSubtleBrush);

        _window.DetailHideBtn.Tag = card;
        _window.DetailHideIcon.Text = card.IsHidden ? "Show" : "Hide";
        _window.DetailHideBtn.Foreground = UIFactory.Brush(ResourceKeys.ChipTextBrush);

        // Folder management buttons
        _window.DetailFolderBtn.Tag = card;

        // AppData button — visible only for UE games with a resolvable AppData config folder
        _window.DetailAppDataBtn.Tag = card;
        var ueProjectName = card.EngineIniProjectOverride
            ?? AuxInstallService.ResolveUeProjectName(card.InstallPath ?? "");
        if (!string.IsNullOrEmpty(ueProjectName))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appDataDir = Path.Combine(localAppData, ueProjectName);
            _window.DetailAppDataBtn.Visibility = Directory.Exists(appDataDir) ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            _window.DetailAppDataBtn.Visibility = Visibility.Collapsed;
        }

        // PCGW link button
        _window.DetailPcgwBtn.Tag = card;
        _window.DetailPcgwBtn.Visibility = card.HasPcgwUrl ? Visibility.Visible : Visibility.Collapsed;

        // Nexus Mods link button
        _window.DetailNexusModsBtn.Tag = card;
        _window.DetailNexusModsBtn.Visibility = card.HasNexusModsUrl ? Visibility.Visible : Visibility.Collapsed;

        // UW Fix link button
        _window.DetailUwFixBtn.Tag = card;
        _window.DetailUwFixBtn.Visibility = card.HasUwFixUrl ? Visibility.Visible : Visibility.Collapsed;
        if (card.HasUwFixUrl)
        {
            var source = card.UwFixSource ?? "creator";
            ToolTipService.SetToolTip(_window.DetailUwFixBtn, $"Open {source}'s ultrawide fix page");
        }

        // Ultra+ link button
        _window.DetailUltraPlusBtn.Tag = card;
        _window.DetailUltraPlusBtn.Visibility = card.HasUltraPlusUrl ? Visibility.Visible : Visibility.Collapsed;

        // Luma toggle and info text (shown when game supports Luma)
        if (card.LumaBadgeVisibility == Visibility.Visible)
        {
            _window.DetailLumaToggle.Visibility = Visibility.Visible;
            _window.DetailLumaToggle.IsChecked = card.IsLumaMode;
            _window.UpdateLumaToggleStyle(card.IsLumaMode);

            // Dynamic info text explaining Luma support
            _window.DetailLumaInfoText.Visibility = Visibility.Visible;
            var isAutoLuma = _window.ViewModel.Manifest?.LumaDefaultGames
                ?.Any(g => g.Equals(card.GameName, StringComparison.OrdinalIgnoreCase)) == true;
            _window.DetailLumaInfoText.Text = isAutoLuma
                ? "This game is automatically configured for Luma HDR. Both RenoDX and Luma are supported. Toggle →"
                : "This game supports both RenoDX and Luma HDR. Use the toggle to switch →";
        }
        else
        {
            _window.DetailLumaToggle.Visibility = Visibility.Collapsed;
            _window.DetailLumaInfoText.Visibility = Visibility.Collapsed;
        }

        // Populate component rows
        UpdateDetailComponentRows(card);
    }
}
