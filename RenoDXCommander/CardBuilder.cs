using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

/// <summary>
/// Helper class responsible for imperative card UI construction.
/// Extracted from MainWindow code-behind to reduce file size.
/// </summary>
public class CardBuilder
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

        // ── Status row: RS/DC/RDX status dots with labels, conditionally Luma, wiki status icon ──
        var statusRow = new Grid();
        statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // dots
        statusRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // wiki icon

        var dotsPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };

        var rdxDotPanel = UIFactory.MakeStatusDot("RDX", card.CardRdxStatusDot);
        var rsDotPanel = UIFactory.MakeStatusDot("RS", card.CardRsStatusDot);
        var dcDotPanel = UIFactory.MakeStatusDot("DC", card.CardDcStatusDot);
        dotsPanel.Children.Add(rdxDotPanel);
        dotsPanel.Children.Add(rsDotPanel);
        dotsPanel.Children.Add(dcDotPanel);

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
            Flyout = installFlyout,
        };

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
                notesBtn.Click += _window.CardNotesButton_Click;
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
                        case nameof(c.CardDcStatusDot):
                            if (dcDotPanel.Children[0] is Microsoft.UI.Xaml.Shapes.Ellipse dcEllipse)
                                dcEllipse.Fill = UIFactory.GetBrush(c.CardDcStatusDot);
                            break;
                        case nameof(c.CardLumaStatusDot):
                            if (lumaDotPanel?.Children[0] is Microsoft.UI.Xaml.Shapes.Ellipse lumaEllipse)
                                lumaEllipse.Fill = UIFactory.GetBrush(c.CardLumaStatusDot);
                            break;
                        case nameof(c.CardLumaVisible):
                            bool effectiveLuma = c.LumaFeatureEnabled && c.IsLumaMode;
                            // Hide/show RDX/RS/DC dots based on Luma mode
                            rdxDotPanel.Visibility = effectiveLuma ? Visibility.Collapsed : Visibility.Visible;
                            rsDotPanel.Visibility = effectiveLuma ? Visibility.Collapsed : Visibility.Visible;
                            dcDotPanel.Visibility = effectiveLuma ? Visibility.Collapsed : Visibility.Visible;
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
                catch { /* card may have been removed from visual tree */ }
            });
        };

        return border;
    }

    /// <summary>
    /// Builds the install flyout content panel with per-component rows.
    /// Each row has: component name, status text (colored), install button, copy config 📋 (RS/DC only), uninstall ✕.
    /// </summary>
    public StackPanel BuildInstallFlyoutContent(GameCardViewModel card)
    {
        var panel = new StackPanel { Spacing = 6, Width = 380 };

        // ── Header row: "Components" label + "Install All" button ──
        var headerRow = new Grid();
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var headerLabel = new TextBlock
        {
            Text = "Components",
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = UIFactory.GetBrush("#A0AABB"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(headerLabel, 0);
        headerRow.Children.Add(headerLabel);

        var installAllBtn = new Button
        {
            Content = "Install All",
            Tag = card,
            FontSize = 11,
            Padding = new Thickness(10, 4, 10, 4),
            IsEnabled = card.CanCardInstall,
            Background = UIFactory.GetBrush("#182840"),
            Foreground = UIFactory.GetBrush("#7AACDD"),
            BorderBrush = UIFactory.GetBrush("#2A4468"),
            CornerRadius = new CornerRadius(6),
        };
        installAllBtn.Click += _window.CardInstallButton_Click;
        Grid.SetColumn(installAllBtn, 1);
        headerRow.Children.Add(installAllBtn);

        panel.Children.Add(headerRow);
        panel.Children.Add(UIFactory.MakeSeparator());

        // ── Component rows ──

        // ReShade row
        var rsRow = BuildComponentRow(card, "ReShade", "RS",
            card.RsStatusText, card.RsStatusColor, card.RsShortAction,
            card.CardRsInstallEnabled, card.IsRsInstalled,
            showCopyConfig: true, copyConfigVisible: card.RsIniExists,
            copyConfigTooltip: "Copy ReShade.ini & ReShadePreset.ini",
            btnBackground: card.RsBtnBackground, btnForeground: card.RsBtnForeground, btnBorderBrush: card.RsBtnBorderBrush);
        rsRow.Visibility = card.ReShadeRowVisibility;
        panel.Children.Add(rsRow);

        // Display Commander row
        var dcRow = BuildComponentRow(card, "DC", "DC",
            card.DcStatusText, card.DcStatusColor, card.DcShortAction,
            card.CardDcInstallEnabled, card.IsDcInstalled,
            showCopyConfig: true, copyConfigVisible: card.DcIniExists,
            copyConfigTooltip: "Copy DisplayCommander.toml",
            btnBackground: card.DcBtnBackground, btnForeground: card.DcBtnForeground, btnBorderBrush: card.DcBtnBorderBrush);
        dcRow.Visibility = card.DcRowVisibility;
        ApplyDcStatusLink(dcRow, card.IsDcInstalled);
        panel.Children.Add(dcRow);

        // RenoDX row
        var rdxRow = BuildComponentRow(card, "RenoDX", "RDX",
            card.RdxStatusText, card.RdxStatusColor, card.RdxShortAction,
            card.CardRdxInstallEnabled, card.IsRdxInstalled,
            showCopyConfig: false, copyConfigVisible: false,
            copyConfigTooltip: null,
            btnBackground: card.InstallBtnBackground, btnForeground: card.InstallBtnForeground, btnBorderBrush: card.InstallBtnBorderBrush);
        rdxRow.Visibility = card.RenoDxRowVisibility;
        panel.Children.Add(rdxRow);

        // External/Discord row — shown when game is external-only (no wiki mod)
        Grid? externalRow = null;
        if (card.IsExternalOnly && !(card.LumaFeatureEnabled && card.IsLumaMode))
        {
            externalRow = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            externalRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            externalRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            externalRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var extName = new TextBlock
            {
                Text = "RenoDX",
                FontSize = 12,
                Foreground = UIFactory.GetBrush("#A0AABB"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(extName, 0);
            externalRow.Children.Add(extName);

            var extBtn = new Button
            {
                Content = card.ExternalDisplayLabel,
                Tag = card,
                FontSize = 11,
                Padding = new Thickness(8, 3, 8, 3),
                MinWidth = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = UIFactory.GetBrush("#1A2040"),
                Foreground = UIFactory.GetBrush("#7AACDD"),
                BorderBrush = UIFactory.GetBrush("#2A4468"),
                CornerRadius = new CornerRadius(6),
            };
            extBtn.Click += async (s, ev) =>
            {
                var url = card.ExternalUrl;
                if (!string.IsNullOrEmpty(url))
                    await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
            };
            Grid.SetColumn(extBtn, 1);
            externalRow.Children.Add(extBtn);

            var extDeleteBtn = new Button
            {
                Content = "✕",
                Tag = card,
                FontSize = 11,
                Padding = new Thickness(4, 2, 4, 2),
                MinWidth = 0,
                Background = UIFactory.GetBrush("transparent"),
                Foreground = UIFactory.GetBrush("#FF4444"),
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0),
                Opacity = card.IsRdxInstalled ? 1 : 0,
                IsHitTestVisible = card.IsRdxInstalled,
            };
            ToolTipService.SetToolTip(extDeleteBtn, "Uninstall RenoDX mod");
            extDeleteBtn.Click += (s, ev) =>
            {
                if ((s as FrameworkElement)?.Tag is GameCardViewModel c)
                    _window.ViewModel.UninstallModCommand.Execute(c);
            };
            Grid.SetColumn(extDeleteBtn, 2);
            externalRow.Children.Add(extDeleteBtn);

            panel.Children.Add(externalRow);
        }

        // Luma row (conditional)
        Grid? lumaRow = null;
        if (card.CardLumaVisible)
        {
            lumaRow = BuildComponentRow(card, "Luma", "Luma",
                card.LumaStatusText, card.LumaStatusColor, card.LumaShortAction,
                card.CardLumaInstallEnabled, card.IsLumaInstalled,
                showCopyConfig: false, copyConfigVisible: false,
                copyConfigTooltip: null,
                btnBackground: card.LumaBtnBackground, btnForeground: card.LumaBtnForeground,
                btnBorderBrush: card.LumaBtnBorderBrush);
            panel.Children.Add(lumaRow);
        }

        // ── Subscribe to PropertyChanged for live updates while flyout is open ──
        System.ComponentModel.PropertyChangedEventHandler? handler = null;
        handler = (s, e) =>
        {
            if (s is not GameCardViewModel c) return;
            _dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    // Update Install All button
                    installAllBtn.IsEnabled = c.CanCardInstall;

                    // Update row visibility
                    rsRow.Visibility = c.ReShadeRowVisibility;
                    dcRow.Visibility = c.DcRowVisibility;
                    rdxRow.Visibility = c.RenoDxRowVisibility;

                    // Update each component row's status/buttons
                    UpdateComponentRow(rsRow, c.RsStatusText, c.RsStatusColor, c.RsShortAction,
                        c.CardRsInstallEnabled, c.IsRsInstalled, c.RsIniExists,
                        c.RsBtnBackground, c.RsBtnForeground, c.RsBtnBorderBrush);
                    UpdateComponentRow(dcRow, c.DcStatusText, c.DcStatusColor, c.DcShortAction,
                        c.CardDcInstallEnabled, c.IsDcInstalled, c.DcIniExists,
                        c.DcBtnBackground, c.DcBtnForeground, c.DcBtnBorderBrush);
                    ApplyDcStatusLink(dcRow, c.IsDcInstalled);
                    UpdateComponentRow(rdxRow, c.RdxStatusText, c.RdxStatusColor, c.RdxShortAction,
                        c.CardRdxInstallEnabled, c.IsRdxInstalled, false,
                        c.InstallBtnBackground, c.InstallBtnForeground, c.InstallBtnBorderBrush);

                    if (lumaRow != null)
                    {
                        UpdateComponentRow(lumaRow, c.LumaStatusText, c.LumaStatusColor, c.LumaShortAction,
                            c.CardLumaInstallEnabled, c.IsLumaInstalled, false,
                            c.LumaBtnBackground, c.LumaBtnForeground, c.LumaBtnBorderBrush);
                    }
                }
                catch { /* flyout may have been closed / card removed */ }
            });
        };
        card.PropertyChanged += handler;

        // Store handler reference on the panel so we can unsubscribe on flyout close
        panel.Tag = (card, handler);

        return panel;
    }

    /// <summary>
    /// Builds a single component row Grid with: name, status text, install button, copy config 📋, uninstall ✕.
    /// </summary>
    public Grid BuildComponentRow(
        GameCardViewModel card, string componentName, string componentTag,
        string statusText, string statusColor, string actionLabel,
        bool installEnabled, bool isInstalled,
        bool showCopyConfig, bool copyConfigVisible, string? copyConfigTooltip,
        string? btnBackground = null, string? btnForeground = null, string? btnBorderBrush = null)
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });  // name
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(75) });  // status
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // install btn
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });     // copy config
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });     // uninstall

        // Component name
        var nameText = new TextBlock
        {
            Text = componentName,
            FontSize = 12,
            Foreground = UIFactory.GetBrush("#A0AABB"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(nameText, 0);
        row.Children.Add(nameText);

        // Status text (colored)
        var statusBlock = new TextBlock
        {
            Text = statusText,
            FontSize = 11,
            Foreground = UIFactory.GetBrush(statusColor),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = "StatusText",
        };
        Grid.SetColumn(statusBlock, 1);
        row.Children.Add(statusBlock);

        // Install/update button
        var installBtn = new Button
        {
            Content = actionLabel,
            Tag = card,
            FontSize = 11,
            Padding = new Thickness(8, 3, 8, 3),
            MinWidth = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = installEnabled,
            Background = UIFactory.GetBrush(btnBackground ?? "#182840"),
            Foreground = UIFactory.GetBrush(btnForeground ?? "#7AACDD"),
            BorderBrush = UIFactory.GetBrush(btnBorderBrush ?? "#2A4468"),
            CornerRadius = new CornerRadius(6),
        };
        // Store component tag for the click handler to identify which component
        installBtn.DataContext = componentTag;
        installBtn.Click += _window.CardComponentInstall_Click;
        Grid.SetColumn(installBtn, 2);
        row.Children.Add(installBtn);

        // Copy config 📋 button — always added to reserve column space; hidden when not applicable
        bool copyVisible = showCopyConfig && copyConfigVisible;
        var copyBtn = new Button
        {
            Content = "📋",
            Tag = card,
            FontSize = 11,
            Padding = new Thickness(4, 3, 4, 3),
            MinWidth = 0, MinHeight = 0,
            Margin = new Thickness(4, 0, 0, 0),
            Opacity = copyVisible ? 1 : 0,
            IsHitTestVisible = copyVisible,
            Background = UIFactory.GetBrush("#1A2030"),
            Foreground = UIFactory.GetBrush("#6B7A8E"),
            BorderBrush = UIFactory.GetBrush("#283240"),
            CornerRadius = new CornerRadius(4),
        };
        copyBtn.DataContext = componentTag;
        if (componentTag == "RS")
            copyBtn.Click += _window.CardCopyRsIni_Click;
        else if (componentTag == "DC")
            copyBtn.Click += _window.CardCopyDcToml_Click;
        if (copyConfigTooltip != null)
            ToolTipService.SetToolTip(copyBtn, copyConfigTooltip);
        Grid.SetColumn(copyBtn, 3);
        row.Children.Add(copyBtn);

        // Uninstall ✕ button
        var uninstallBtn = new Button
        {
            Content = "✕",
            Tag = card,
            FontSize = 11,
            Padding = new Thickness(4, 3, 4, 3),
            MinWidth = 0, MinHeight = 0,
            Margin = new Thickness(4, 0, 0, 0),
            Opacity = isInstalled ? 1 : 0,
            IsHitTestVisible = isInstalled,
            Background = UIFactory.GetBrush("#301820"),
            Foreground = UIFactory.GetBrush("#E06060"),
            BorderBrush = UIFactory.GetBrush("#502838"),
            CornerRadius = new CornerRadius(4),
        };
        uninstallBtn.DataContext = componentTag;
        uninstallBtn.Click += _window.CardComponentUninstall_Click;
        Grid.SetColumn(uninstallBtn, 4);
        row.Children.Add(uninstallBtn);

        return row;
    }

    /// <summary>
    /// Updates a component row's status text, color, install button label/enabled, copy config visibility, and uninstall visibility.
    /// </summary>
    public static void UpdateComponentRow(Grid row, string statusText, string statusColor,
        string actionLabel, bool installEnabled, bool isInstalled, bool copyConfigVisible,
        string? btnBackground = null, string? btnForeground = null, string? btnBorderBrush = null)
    {
        foreach (var child in row.Children)
        {
            if (child is TextBlock tb && tb.Tag as string == "StatusText")
            {
                tb.Text = statusText;
                tb.Foreground = UIFactory.GetBrush(statusColor);
            }
            else if (child is Button btn)
            {
                var col = Grid.GetColumn(btn);
                if (col == 2) // install button
                {
                    btn.Content = actionLabel;
                    btn.IsEnabled = installEnabled;
                    if (btnBackground != null)
                        btn.Background = UIFactory.GetBrush(btnBackground);
                    if (btnForeground != null)
                        btn.Foreground = UIFactory.GetBrush(btnForeground);
                    if (btnBorderBrush != null)
                        btn.BorderBrush = UIFactory.GetBrush(btnBorderBrush);
                }
                else if (col == 3) // copy config button
                {
                    btn.Opacity = copyConfigVisible ? 1 : 0;
                    btn.IsHitTestVisible = copyConfigVisible;
                }
                else if (col == 4) // uninstall button
                {
                    btn.Opacity = isInstalled ? 1 : 0;
                    btn.IsHitTestVisible = isInstalled;
                }
            }
        }
    }

    private static readonly Uri _dcCommitUri = new("https://github.com/pmnoxx/display-commander/commit/main");

    /// <summary>
    /// Styles the DC row's status TextBlock as a clickable link when installed.
    /// </summary>
    private static void ApplyDcStatusLink(Grid row, bool isInstalled)
    {
        foreach (var child in row.Children)
        {
            if (child is TextBlock tb && tb.Tag as string == "StatusText")
            {
                if (isInstalled)
                {
                    tb.TextDecorations = Windows.UI.Text.TextDecorations.Underline;
                    tb.PointerPressed -= DcStatusLink_PointerPressed;
                    tb.PointerPressed += DcStatusLink_PointerPressed;
                    ToolTipService.SetToolTip(tb, "Version information");
                }
                else
                {
                    tb.TextDecorations = Windows.UI.Text.TextDecorations.None;
                    tb.PointerPressed -= DcStatusLink_PointerPressed;
                    ToolTipService.SetToolTip(tb, null);
                }
                break;
            }
        }
    }

    private static void DcStatusLink_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _ = Windows.System.Launcher.LaunchUriAsync(_dcCommitUri);
    }

}
