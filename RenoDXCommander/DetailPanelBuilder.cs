// DetailPanelBuilder.cs — Core scaffolding: class declaration, constructor, current detail card state, and detail panel population.

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

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

        // Wiki status badge
        var hasWikiLabel = !string.IsNullOrEmpty(card.WikiStatusLabel);
        _window.DetailWikiText.Text = card.WikiStatusLabel;
        _window.DetailWikiText.Foreground = UIFactory.GetBrush(card.WikiStatusBadgeForeground);
        _window.DetailWikiBadge.Background = UIFactory.GetBrush(card.WikiStatusBadgeBackground);
        _window.DetailWikiBadge.BorderBrush = UIFactory.GetBrush(card.WikiStatusBadgeBorderBrush);
        _window.DetailWikiBadge.BorderThickness = new Thickness(1);
        _window.DetailWikiBadge.Visibility = hasWikiLabel ? Visibility.Visible : Visibility.Collapsed;
        _window.DetailSepPlatformStatus.Visibility = hasWikiLabel ? Visibility.Visible : Visibility.Collapsed;

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
        _window.DetailFavIcon.Text = card.IsFavourite ? "⭐" : "☆";
        _window.DetailFavIcon.Foreground = new SolidColorBrush(card.IsFavourite
            ? ((SolidColorBrush)Application.Current.Resources[ResourceKeys.AccentAmberBrush]).Color
            : ((SolidColorBrush)Application.Current.Resources[ResourceKeys.TextDisabledBrush]).Color);

        _window.DetailDiscussionBtn.Tag = card;
        _window.DetailDiscussionBtn.Visibility = card.NameLinkVisibility;

        _window.DetailNotesBtn.Tag = card;
        _window.DetailNotesBtn.Visibility = card.NotesButtonVisibility;

        _window.DetailHideBtn.Tag = card;
        _window.DetailHideIcon.Text = card.IsHidden ? "Show" : "Hide";
        _window.DetailHideBtn.Foreground = card.IsHidden
            ? UIFactory.Brush(ResourceKeys.TextTertiaryBrush)
            : UIFactory.Brush(ResourceKeys.TextDisabledBrush);

        // Folder management buttons
        _window.DetailFolderBtn.Tag = card;
        _window.DetailChangeFolderBtn.Tag = card;
        _window.DetailRemoveGameBtn.Tag = card;

        // Luma badge toggle
        if (card.LumaBadgeVisibility == Visibility.Visible)
        {
            _window.DetailLumaBadgeContainer.Visibility = Visibility.Visible;
            _window.DetailLumaToggle.IsChecked = card.IsLumaMode;
            _window.UpdateLumaToggleStyle(card.IsLumaMode);
        }
        else
        {
            _window.DetailLumaBadgeContainer.Visibility = Visibility.Collapsed;
        }

        // Populate component rows
        UpdateDetailComponentRows(card);
    }
}
