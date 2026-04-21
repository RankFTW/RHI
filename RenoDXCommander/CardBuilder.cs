// CardBuilder.cs — Class declaration, constructor, and BuildGameCard (card border, header, status dots, action row, bottom row, PropertyChanged subscription).

using Microsoft.UI;
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
/// Helper class responsible for imperative card UI construction.
/// Extracted from MainWindow code-behind to reduce file size.
/// </summary>
public partial class CardBuilder
{
    private readonly MainWindow _window;
    private readonly DispatcherQueue _dispatcherQueue;

    public CardBuilder(MainWindow window)
    {
        _window = window;
        _dispatcherQueue = window.DispatcherQueue;
    }

    public Border BuildGameCard(GameCardViewModel card)
    {
        var gameName = string.IsNullOrEmpty(card.GameName) ? "Unknown Game" : card.GameName;

        // ── Outer border ──────────────────────────────────────────────────────
        var border = new Border
        {
            Width = 280,
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Background = UIFactory.GetBrush(card.CardBackground),
            BorderBrush = UIFactory.GetBrush(card.CardBorderBrush),
            Padding = new Thickness(14, 12, 14, 12),
            Tag = card,
        };

        var root = new StackPanel { Spacing = 8 };

        // ── Header row: source icon, game name, favourite star, overrides gear ──
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // icon
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // name
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // fav
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // gear

        // Source icon
        if (card.HasSourceIcon)
        {
            var srcImg = new Image
            {
                Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(card.SourceIconUri),
                Width = 16, Height = 16,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(srcImg, 0);
            header.Children.Add(srcImg);
        }
        else
        {
            var srcText = new TextBlock
            {
                Text = card.SourceIcon,
                FontSize = 14,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(srcText, 0);
            header.Children.Add(srcText);
        }

        // Game name (trimmed)
        var nameBlock = new TextBlock
        {
            Text = gameName.Length > 28 ? gameName[..25] + "…" : gameName,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1,
        };
        ToolTipService.SetToolTip(nameBlock, gameName);
        Grid.SetColumn(nameBlock, 1);
        header.Children.Add(nameBlock);

        // Favourite star button
        var favBtn = new Button
        {
            Content = card.IsFavourite ? "⭐" : "☆",
            Tag = card,
            FontSize = 14,
            Padding = new Thickness(4, 2, 4, 2),
            MinWidth = 0, MinHeight = 0,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        favBtn.Click += _window.CardFavouriteButton_Click;
        Grid.SetColumn(favBtn, 2);
        header.Children.Add(favBtn);

        // More options (...) button
        var moreBtn = new Button
        {
            Content = "⋯",
            Tag = card,
            FontSize = 14,
            Padding = new Thickness(4, 0, 4, 0),
            MinWidth = 0, MinHeight = 0,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
            Foreground = UIFactory.GetBrush("#6B7A8E"),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        moreBtn.Click += _window.CardMoreMenu_Click;
        Grid.SetColumn(moreBtn, 3);
        header.Children.Add(moreBtn);

        root.Children.Add(header);

        // ── Graphics API badge (shown only when detected) ──
        if (card.HasGraphicsApiBadge)
        {
            var apiBadge = new Border
            {
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(6, 2, 6, 2),
                Background = UIFactory.Brush(ResourceKeys.ChipDefaultBrush),
                BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock
                {
                    Text = card.GraphicsApiLabel,
                    FontSize = 11,
                    Foreground = UIFactory.Brush(ResourceKeys.ChipTextBrush),
                },
            };
            root.Children.Add(apiBadge);
        }

        // ── Status row: RS/RDX status dots with labels, conditionally Luma, wiki status icon ──
        var statusRow = new Grid();
        statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // dots
        statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // wiki icon

        var dotsPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };

        var rdxDotPanel = UIFactory.MakeStatusDot("RDX", card.CardRdxStatusDot);
        var rsDotPanel = UIFactory.MakeStatusDot("RS", card.CardRsStatusDot);
        var ulDotPanel = UIFactory.MakeStatusDot("UL", card.CardUlStatusDot);
        ulDotPanel.Visibility = card.UlRowVisibility;
        var refDotPanel = UIFactory.MakeStatusDot("REF", card.CardRefStatusDot);
        refDotPanel.Visibility = card.RefRowVisibility;
        dotsPanel.Children.Add(rdxDotPanel);
        dotsPanel.Children.Add(rsDotPanel);
        dotsPanel.Children.Add(ulDotPanel);
        dotsPanel.Children.Add(refDotPanel);

        StackPanel? lumaDotPanel = null;
        if (card.CardLumaVisible)
        {
            lumaDotPanel = UIFactory.MakeStatusDot("Luma", card.CardLumaStatusDot);
            dotsPanel.Children.Add(lumaDotPanel);
        }

        Grid.SetColumn(dotsPanel, 0);
        statusRow.Children.Add(dotsPanel);

        // Wiki status icon (right-aligned, icon only, hidden in Luma mode)
        if (card.WikiStatusIconVisible)
        {
            var wikiIcon = new TextBlock
            {
                Text = card.WikiStatusIcon,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            ToolTipService.SetToolTip(wikiIcon, card.WikiStatusLabel);
            Grid.SetColumn(wikiIcon, 1);
            statusRow.Children.Add(wikiIcon);
        }

        root.Children.Add(statusRow);

        // ── Action row: full-width install button with flyout ──
        var installFlyout = new Flyout
        {
            Placement = FlyoutPlacementMode.Bottom,
        };
        installFlyout.Opening += _window.CardInstallFlyout_Opening;

        // Apply dark background to the flyout presenter via its style
        var flyoutPresenterStyle = new Style(typeof(FlyoutPresenter));
        flyoutPresenterStyle.Setters.Add(new Setter(Control.BackgroundProperty, UIFactory.GetBrush("#0C1018")));
        flyoutPresenterStyle.Setters.Add(new Setter(Control.BorderBrushProperty, UIFactory.GetBrush("#2A4468")));
        flyoutPresenterStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12)));
        installFlyout.FlyoutPresenterStyle = flyoutPresenterStyle;

        var installBtn = new Button
        {
            Content = card.CardPrimaryActionLabel,
            Tag = card,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            FontSize = 12,
            Padding = new Thickness(8, 5, 8, 5),
            IsEnabled = card.CanCardInstall,
            Background = UIFactory.GetBrush("#182840"),
            Foreground = UIFactory.GetBrush("#7AACDD"),
            BorderBrush = UIFactory.GetBrush("#2A4468"),
            CornerRadius = new CornerRadius(6),
        };

        // Use SetAttachedFlyout + ShowAt so that flyout.Target is set to installBtn
        // before the Opening event fires. Assigning Button.Flyout alone does not
        // reliably populate Target in all WinUI 3 versions.
        FlyoutBase.SetAttachedFlyout(installBtn, installFlyout);
        installBtn.Click += (s, e) => FlyoutBase.ShowAttachedFlyout(installBtn);

        root.Children.Add(installBtn);

        // ── Bottom row: info buttons (left) + overrides button (right) ──
        var bottomRow = new Grid();
        bottomRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottomRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Left side: info/notes buttons
        if (card.HasInfoIndicator)
        {
            var infoRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            if (card.HasNameUrl)
            {
                var infoBtn = new Button
                {
                    Content = "Wiki",
                    Tag = card,
                    FontSize = 11,
                    Padding = new Thickness(6, 2, 6, 2),
                    MinWidth = 0, MinHeight = 0,
                    Background = UIFactory.GetBrush("#1E242C"),
                    Foreground = UIFactory.GetBrush("#6B7A8E"),
                    BorderBrush = UIFactory.GetBrush("#283240"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                };
                ToolTipService.SetToolTip(infoBtn, "Open discussion / instructions");
                infoBtn.Click += _window.CardInfoLink_Click;
                infoRow.Children.Add(infoBtn);
            }
            if (card.HasNotes)
            {
                var notesBtn = new Button
                {
                    Content = "Info",
                    Tag = card,
                    DataContext = card.IsLumaMode ? AddonType.Luma : AddonType.RenoDX,
                    FontSize = 11,
                    Padding = new Thickness(6, 2, 6, 2),
                    MinWidth = 0, MinHeight = 0,
                    Background = UIFactory.GetBrush("#1E242C"),
                    Foreground = UIFactory.GetBrush("#6B7A8E"),
                    BorderBrush = UIFactory.GetBrush("#283240"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                };
                ToolTipService.SetToolTip(notesBtn, "View notes");
                notesBtn.Click += _window.InfoButton_Click;
                infoRow.Children.Add(notesBtn);
            }
            Grid.SetColumn(infoRow, 0);
            bottomRow.Children.Add(infoRow);
        }

        // Right side: overrides button
        var overridesBtn = new Button
        {
            Content = "⚙",
            Tag = card,
            FontSize = 12,
            Padding = new Thickness(4, 2, 4, 2),
            MinWidth = 0, MinHeight = 0,
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
            Foreground = UIFactory.GetBrush("#6B7A8E"),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        ToolTipService.SetToolTip(overridesBtn, "Overrides");
        overridesBtn.Click += _window.CardOverridesButton_Click;
        Grid.SetColumn(overridesBtn, 1);
        bottomRow.Children.Add(overridesBtn);

        root.Children.Add(bottomRow);

        // ── Click-to-highlight on the card border ──
        border.PointerPressed += _window.Card_PointerPressed;

        border.Child = root;

        // ── Subscribe to PropertyChanged for live updates ──
        card.PropertyChanged += (s, e) =>
        {
            if (s is not GameCardViewModel c) return;
            _dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    switch (e.PropertyName)
                    {
                        case nameof(c.CardBackground):
                            border.Background = UIFactory.GetBrush(c.CardBackground);
                            break;
                        case nameof(c.CardBorderBrush):
                            border.BorderBrush = UIFactory.GetBrush(c.CardBorderBrush);
                            break;
                        case nameof(c.CardPrimaryActionLabel):
                            installBtn.Content = c.CardPrimaryActionLabel;
                            break;
                        case nameof(c.CanCardInstall):
                            installBtn.IsEnabled = c.CanCardInstall;
                            break;
                        case nameof(c.IsFavourite):
                            favBtn.Content = c.IsFavourite ? "⭐" : "☆";
                            break;
                        case nameof(c.CardRdxStatusDot):
                            if (rdxDotPanel.Children[0] is Microsoft.UI.Xaml.Shapes.Ellipse rdxEllipse)
                                rdxEllipse.Fill = UIFactory.GetBrush(c.CardRdxStatusDot);
                            break;
                        case nameof(c.CardRsStatusDot):
                            if (rsDotPanel.Children[0] is Microsoft.UI.Xaml.Shapes.Ellipse rsEllipse)
                                rsEllipse.Fill = UIFactory.GetBrush(c.CardRsStatusDot);
                            break;
                        case nameof(c.CardUlStatusDot):
                            if (ulDotPanel.Children[0] is Microsoft.UI.Xaml.Shapes.Ellipse ulEllipse)
                                ulEllipse.Fill = UIFactory.GetBrush(c.CardUlStatusDot);
                            break;
                        case nameof(c.CardRefStatusDot):
                            if (refDotPanel.Children[0] is Microsoft.UI.Xaml.Shapes.Ellipse refEllipse)
                                refEllipse.Fill = UIFactory.GetBrush(c.CardRefStatusDot);
                            break;
                        case nameof(c.RefRowVisibility):
                            refDotPanel.Visibility = c.RefRowVisibility;
                            break;
                        case nameof(c.CardLumaStatusDot):
                            if (lumaDotPanel?.Children[0] is Microsoft.UI.Xaml.Shapes.Ellipse lumaEllipse)
                                lumaEllipse.Fill = UIFactory.GetBrush(c.CardLumaStatusDot);
                            break;
                        case nameof(c.CardLumaVisible):
                            bool effectiveLuma = c.LumaFeatureEnabled && c.IsLumaMode;
                            // Hide/show RDX/RS/UL dots based on Luma mode
                            rdxDotPanel.Visibility = effectiveLuma ? Visibility.Collapsed : Visibility.Visible;
                            rsDotPanel.Visibility = effectiveLuma ? Visibility.Collapsed : Visibility.Visible;
                            ulDotPanel.Visibility = c.UlRowVisibility;
                            refDotPanel.Visibility = c.RefRowVisibility;
                            // Add/remove Luma dot
                            if (c.CardLumaVisible && lumaDotPanel == null)
                            {
                                lumaDotPanel = UIFactory.MakeStatusDot("Luma", c.CardLumaStatusDot);
                                statusRow.Children.Add(lumaDotPanel);
                            }
                            else if (!c.CardLumaVisible && lumaDotPanel != null)
                            {
                                statusRow.Children.Remove(lumaDotPanel);
                                lumaDotPanel = null;
                            }
                            break;
                    }
                }
                catch (Exception ex) { CrashReporter.Log($"[CardBuilder.BuildGameCard] PropertyChanged update error for '{c.GameName}' — {ex.Message}"); }
            });
        };

        return border;
    }
}
