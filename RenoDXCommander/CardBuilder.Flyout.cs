// CardBuilder.Flyout.cs — Install flyout content construction, component rows, and flyout PropertyChanged handling.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class CardBuilder
{
    private readonly AddonInfoResolver _addonInfoResolver = new();

    /// <summary>
    /// Applies Info button styling (highlighted vs muted) for Card flyout Info buttons.
    /// </summary>
    private static void ApplyCardInfoButtonStyle(Button infoBtn, InfoSourceType sourceType)
    {
        if (sourceType is InfoSourceType.None)
        {
            // Greyed-out state: no content available at all
            infoBtn.Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush);
            infoBtn.Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush);
            infoBtn.BorderBrush = UIFactory.Brush(ResourceKeys.BorderStrongBrush);
            infoBtn.Opacity = 0.3;
            infoBtn.IsHitTestVisible = false;
        }
        else if (sourceType is InfoSourceType.Manifest or InfoSourceType.Wiki)
        {
            // Highlighted style for manifest/wiki content
            infoBtn.Background = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush);
            infoBtn.Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush);
            infoBtn.BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush);
            infoBtn.Opacity = 1.0;
            infoBtn.IsHitTestVisible = true;
        }
        else
        {
            // Default muted style for fallback content
            infoBtn.Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush);
            infoBtn.Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush);
            infoBtn.BorderBrush = UIFactory.Brush(ResourceKeys.BorderStrongBrush);
            infoBtn.Opacity = 1.0;
            infoBtn.IsHitTestVisible = true;
        }
    }
    /// <summary>
    /// Builds the install flyout content panel with per-component rows.
    /// Each row has: component name, status text (colored), install button, copy config 📋 (RS only), uninstall ✕.
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

        // RE Framework row (above ReShade, visible only for RE Engine games)
        var refRow = BuildComponentRow(card, "RE Framework", "REF",
            card.RefStatusText, card.RefStatusColor, card.RefShortAction,
            card.CardRefInstallEnabled, card.IsRefInstalled,
            showCopyConfig: false, copyConfigVisible: false,
            copyConfigTooltip: null,
            addonType: AddonType.REFramework,
            btnBackground: card.RefBtnBackground, btnForeground: card.RefBtnForeground, btnBorderBrush: card.RefBtnBorderBrush);
        refRow.Visibility = card.RefRowVisibility;
        // Make REF status text a clickable link to the REFramework nightly releases
        var refStatusBlock = refRow.Children.OfType<TextBlock>().FirstOrDefault(t => t.Tag as string == "StatusText");
        if (refStatusBlock != null)
        {
            if (card.IsRefInstalled)
                refStatusBlock.TextDecorations = Windows.UI.Text.TextDecorations.Underline;
            refStatusBlock.PointerPressed += async (s, e) =>
            {
                if (card.IsRefInstalled)
                    await Windows.System.Launcher.LaunchUriAsync(
                        new Uri("https://github.com/praydog/REFramework-nightly/releases"));
            };
        }
        panel.Children.Add(refRow);

        // ReShade row
        var rsRow = BuildComponentRow(card, "ReShade", "RS",
            card.RsStatusText, card.RsStatusColor, card.RsShortAction,
            card.CardRsInstallEnabled, card.IsRsInstalled,
            showCopyConfig: true, copyConfigVisible: card.RsIniExists,
            copyConfigTooltip: "Copy ReShade.ini & ReShadePreset.ini",
            addonType: AddonType.ReShade,
            btnBackground: card.RsBtnBackground, btnForeground: card.RsBtnForeground, btnBorderBrush: card.RsBtnBorderBrush);
        rsRow.Visibility = card.ReShadeRowVisibility;
        // Make RS status text a clickable link to reshade.me
        var rsStatusBlock = rsRow.Children.OfType<TextBlock>().FirstOrDefault(t => t.Tag as string == "StatusText");
        if (rsStatusBlock != null)
        {
            if (card.IsRsInstalled)
                rsStatusBlock.TextDecorations = Windows.UI.Text.TextDecorations.Underline;
            rsStatusBlock.PointerPressed += async (s, e) =>
            {
                if (card.IsRsInstalled)
                    await Windows.System.Launcher.LaunchUriAsync(
                        new Uri("https://reshade.me"));
            };
        }
        panel.Children.Add(rsRow);

        // RenoDX row
        var rdxRow = BuildComponentRow(card, "RenoDX", "RDX",
            card.RdxStatusText, card.RdxStatusColor, card.RdxShortAction,
            card.CardRdxInstallEnabled, card.IsRdxInstalled,
            showCopyConfig: false, copyConfigVisible: false,
            copyConfigTooltip: null,
            addonType: AddonType.RenoDX,
            btnBackground: card.InstallBtnBackground, btnForeground: card.InstallBtnForeground, btnBorderBrush: card.InstallBtnBorderBrush);
        rdxRow.Visibility = card.RenoDxRowVisibility;
        // Make RDX status text a clickable link to the RenoDX wiki page
        var rdxStatusBlock = rdxRow.Children.OfType<TextBlock>().FirstOrDefault(t => t.Tag as string == "StatusText");
        if (rdxStatusBlock != null)
        {
            if (card.IsRdxInstalled)
                rdxStatusBlock.TextDecorations = Windows.UI.Text.TextDecorations.Underline;
            rdxStatusBlock.PointerPressed += async (s, e) =>
            {
                if (card.IsRdxInstalled)
                {
                    var url = !string.IsNullOrEmpty(card.NameUrl)
                        ? card.NameUrl
                        : "https://github.com/clshortfuse/renodx/wiki/Mods";
                    await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
                }
            };
        }
        panel.Children.Add(rdxRow);

        // Luma row (conditional — shown above limiter separator in Luma mode)
        Grid? lumaRow = null;
        if (card.CardLumaVisible)
        {
            lumaRow = BuildComponentRow(card, "Luma", "Luma",
                card.LumaStatusText, card.LumaStatusColor, card.LumaShortAction,
                card.CardLumaInstallEnabled, card.IsLumaInstalled,
                showCopyConfig: false, copyConfigVisible: false,
                copyConfigTooltip: null,
                addonType: AddonType.Luma,
                btnBackground: card.LumaBtnBackground, btnForeground: card.LumaBtnForeground,
                btnBorderBrush: card.LumaBtnBorderBrush);
            panel.Children.Add(lumaRow);
        }

        // ── Optional separator + OptiScaler row ──────────────────────────────
        var optionalSep = new TextBlock
        {
            Text = "———  Optional  ———",
            FontSize = 9,
            Foreground = UIFactory.GetBrush("#5A6880"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 2),
        };
        panel.Children.Add(optionalSep);

        // OptiScaler row
        var osRow = BuildComponentRow(card, "OptiScaler", "OS",
            card.OsStatusText, card.OsStatusColor, card.OsShortAction,
            card.CardOsInstallEnabled, card.IsOsInstalled,
            showCopyConfig: true, copyConfigVisible: card.OsIniExists,
            copyConfigTooltip: "Copy OptiScaler.ini to game folder",
            addonType: AddonType.OptiScaler,
            btnBackground: card.OsBtnBackground, btnForeground: card.OsBtnForeground, btnBorderBrush: card.OsBtnBorderBrush);
        // Grey out and disable for 32-bit games
        if (card.Is32Bit)
        {
            osRow.Opacity = 0.35;
            osRow.IsHitTestVisible = false;
            // Apply strikethrough to name and status text
            foreach (var child in osRow.Children)
            {
                if (child is TextBlock tb)
                    tb.TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough;
            }
        }
        panel.Children.Add(osRow);

        // ── Limiter separator + rows ──────────────────────────────────────────
        var limiterSep = new TextBlock
        {
            Text = "———  Frame limiters — Choose one  ———",
            FontSize = 9,
            Foreground = UIFactory.GetBrush("#5A6880"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 2),
        };
        panel.Children.Add(limiterSep);

        // ReLimiter row
        var ulRow = BuildComponentRow(card, "ReLimiter", "UL",
            card.UlStatusText, card.UlStatusColor, card.UlShortAction,
            card.UlInstallEnabled, card.IsUlInstalled,
            showCopyConfig: true, copyConfigVisible: card.UlIniExists,
            copyConfigTooltip: "Copy relimiter.ini to game folder",
            addonType: AddonType.ReLimiter,
            btnBackground: card.UlBtnBackground, btnForeground: card.UlBtnForeground, btnBorderBrush: card.UlBtnBorderBrush);
        var ulStatusBlock = ulRow.Children.OfType<TextBlock>().FirstOrDefault(t => t.Tag as string == "StatusText");
        if (ulStatusBlock != null)
        {
            if (card.IsUlInstalled)
                ulStatusBlock.TextDecorations = Windows.UI.Text.TextDecorations.Underline;
            ulStatusBlock.PointerPressed += async (s, e) =>
            {
                if (card.UlStatus == Models.GameStatus.UpdateAvailable && _window.ViewModel.LatestUlReleasePageUrl != null)
                    await Windows.System.Launcher.LaunchUriAsync(new Uri(_window.ViewModel.LatestUlReleasePageUrl));
                else if (card.IsUlInstalled)
                    await Windows.System.Launcher.LaunchUriAsync(
                        new Uri("https://github.com/RankFTW/ReLimiter?tab=readme-ov-file#relimiter--comprehensive-feature-guide"));
            };
        }
        ulRow.Visibility = card.UlRowVisibility;
        panel.Children.Add(ulRow);

        // Display Commander row
        var dcRow = BuildComponentRow(card, "DC", "DC",
            card.DcStatusText, card.DcStatusColor, card.DcShortAction,
            card.DcInstallEnabled, card.IsDcInstalled,
            showCopyConfig: true, copyConfigVisible: card.DcIniExists,
            copyConfigTooltip: "Copy DisplayCommander.ini to game folder",
            addonType: AddonType.DisplayCommander,
            btnBackground: card.DcBtnBackground, btnForeground: card.DcBtnForeground, btnBorderBrush: card.DcBtnBorderBrush);
        dcRow.Visibility = card.DcRowVisibility;
        panel.Children.Add(dcRow);

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
                    ulRow.Visibility = c.UlRowVisibility;
                    rdxRow.Visibility = c.RenoDxRowVisibility;
                    refRow.Visibility = c.RefRowVisibility;

                    // Update each component row's status/buttons
                    UpdateComponentRow(refRow, c.RefStatusText, c.RefStatusColor, c.RefShortAction,
                        c.CardRefInstallEnabled, c.IsRefInstalled, false,
                        c.RefBtnBackground, c.RefBtnForeground, c.RefBtnBorderBrush);
                    // Keep REF status underline in sync
                    var refSb = refRow.Children.OfType<TextBlock>().FirstOrDefault(t => t.Tag as string == "StatusText");
                    if (refSb != null)
                        refSb.TextDecorations = c.IsRefInstalled
                            ? Windows.UI.Text.TextDecorations.Underline
                            : Windows.UI.Text.TextDecorations.None;
                    UpdateComponentRow(rsRow, c.RsStatusText, c.RsStatusColor, c.RsShortAction,
                        c.CardRsInstallEnabled, c.IsRsInstalled, c.RsIniExists,
                        c.RsBtnBackground, c.RsBtnForeground, c.RsBtnBorderBrush);
                    UpdateComponentRow(ulRow, c.UlStatusText, c.UlStatusColor, c.UlShortAction,
                        c.IsUlNotInstalling, c.IsUlInstalled, c.UlIniExists,
                        c.UlBtnBackground, c.UlBtnForeground, c.UlBtnBorderBrush);
                    // Keep UL status underline in sync
                    var ulSb = ulRow.Children.OfType<TextBlock>().FirstOrDefault(t => t.Tag as string == "StatusText");
                    if (ulSb != null)
                        ulSb.TextDecorations = c.IsUlInstalled
                            ? Windows.UI.Text.TextDecorations.Underline
                            : Windows.UI.Text.TextDecorations.None;
                    UpdateComponentRow(rdxRow, c.RdxStatusText, c.RdxStatusColor, c.RdxShortAction,
                        c.CardRdxInstallEnabled, c.IsRdxInstalled, false,
                        c.InstallBtnBackground, c.InstallBtnForeground, c.InstallBtnBorderBrush);

                    if (lumaRow != null)
                    {
                        UpdateComponentRow(lumaRow, c.LumaStatusText, c.LumaStatusColor, c.LumaShortAction,
                            c.CardLumaInstallEnabled, c.IsLumaInstalled, false,
                            c.LumaBtnBackground, c.LumaBtnForeground, c.LumaBtnBorderBrush);
                    }

                    // Update OptiScaler row
                    UpdateComponentRow(osRow, c.OsStatusText, c.OsStatusColor, c.OsShortAction,
                        c.CardOsInstallEnabled, c.IsOsInstalled, c.OsIniExists,
                        c.OsBtnBackground, c.OsBtnForeground, c.OsBtnBorderBrush);
                    // Keep 32-bit greyed-out state in sync
                    if (c.Is32Bit)
                    {
                        osRow.Opacity = 0.35;
                        osRow.IsHitTestVisible = false;
                        foreach (var child in osRow.Children)
                        {
                            if (child is TextBlock tb)
                                tb.TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough;
                        }
                    }
                    else
                    {
                        osRow.Opacity = 1.0;
                        osRow.IsHitTestVisible = true;
                    }
                }
                catch (Exception ex) { CrashReporter.Log($"[CardBuilder.BuildInstallFlyoutContent] Flyout update error — {ex.Message}"); }
            });
        };
        card.PropertyChanged += handler;

        // Store handler reference on the panel so we can unsubscribe on flyout close
        panel.Tag = (card, handler);

        return panel;
    }

    /// <summary>
    /// Builds a single component row Grid with: name, status text, Info button, install button, copy config 📋, uninstall ✕.
    /// </summary>
    public Grid BuildComponentRow(
        GameCardViewModel card, string componentName, string componentTag,
        string statusText, string statusColor, string actionLabel,
        bool installEnabled, bool isInstalled,
        bool showCopyConfig, bool copyConfigVisible, string? copyConfigTooltip,
        AddonType addonType,
        string? btnBackground = null, string? btnForeground = null, string? btnBorderBrush = null)
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });  // col 0: name
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(75) });  // col 1: status
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });  // col 2: Info button
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // col 3: install btn
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });     // col 4: copy config
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });     // col 5: uninstall

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

        // Info button — styled based on content availability
        var manifest = _window.ViewModel.Manifest;
        var osWikiData = _window.ViewModel.OptiScalerWikiServiceInstance.CachedData;
        var hdrDatabase = _window.ViewModel.HdrDatabaseServiceInstance.CachedData;
        var sourceType = _addonInfoResolver.GetSourceType(card, addonType, manifest, osWikiData, hdrDatabase);
        var tooltip = _addonInfoResolver.GetTooltip(card, addonType, manifest, osWikiData, hdrDatabase);

        var infoBtn = new Button
        {
            Content = "Info",
            Tag = card,
            Name = $"Card{componentTag}InfoBtn",
            FontSize = 11,
            Padding = new Thickness(4, 2, 4, 2),
            MinWidth = 0,
            Width = 36,
            Height = 32,
            CornerRadius = new CornerRadius(8),
            VerticalAlignment = VerticalAlignment.Center,
            BorderThickness = new Thickness(1),
        };
        // Store the AddonType on DataContext for the click handler to identify which addon
        infoBtn.DataContext = addonType;
        ApplyCardInfoButtonStyle(infoBtn, sourceType);
        ToolTipService.SetToolTip(infoBtn, tooltip);
        infoBtn.Click += _window.InfoButton_Click;
        Grid.SetColumn(infoBtn, 2);
        row.Children.Add(infoBtn);

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
        Grid.SetColumn(installBtn, 3);
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
        if (componentTag == "UL")
            copyBtn.Click += _window.CardCopyUlIni_Click;
        if (componentTag == "DC")
            copyBtn.Click += _window.CardCopyDcIni_Click;
        if (componentTag == "OS")
            copyBtn.Click += _window.CardCopyOsIni_Click;
        if (copyConfigTooltip != null)
            ToolTipService.SetToolTip(copyBtn, copyConfigTooltip);
        Grid.SetColumn(copyBtn, 4);
        row.Children.Add(copyBtn);

        // Uninstall ✕ button
        var uninstallBtn = new Button
        {
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
            Content = new TextBlock
            {
                Text = "✕",
                FontSize = 11,
                Foreground = UIFactory.GetBrush("#E06060"),
                HorizontalAlignment = HorizontalAlignment.Center,
            },
        };
        uninstallBtn.DataContext = componentTag;
        uninstallBtn.Click += _window.CardComponentUninstall_Click;
        Grid.SetColumn(uninstallBtn, 5);
        row.Children.Add(uninstallBtn);

        return row;
    }

    /// <summary>
    /// Updates a component row's status text, color, install button label/enabled, copy config visibility,
    /// uninstall visibility, and Info button styling.
    /// </summary>
    public void UpdateComponentRow(Grid row, string statusText, string statusColor,
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
                if (col == 2) // Info button
                {
                    // Refresh Info button styling based on current content availability
                    if (btn.Tag is GameCardViewModel card && btn.DataContext is AddonType addonType)
                    {
                        var manifest = _window.ViewModel.Manifest;
                        var osWikiData = _window.ViewModel.OptiScalerWikiServiceInstance.CachedData;
                        var hdrDatabase = _window.ViewModel.HdrDatabaseServiceInstance.CachedData;
                        var sourceType = _addonInfoResolver.GetSourceType(card, addonType, manifest, osWikiData, hdrDatabase);
                        var tooltip = _addonInfoResolver.GetTooltip(card, addonType, manifest, osWikiData, hdrDatabase);
                        ApplyCardInfoButtonStyle(btn, sourceType);
                        ToolTipService.SetToolTip(btn, tooltip);
                    }
                }
                else if (col == 3) // install button
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
                else if (col == 4) // copy config button
                {
                    btn.Opacity = copyConfigVisible ? 1 : 0;
                    btn.IsHitTestVisible = copyConfigVisible;
                }
                else if (col == 5) // uninstall button
                {
                    btn.Opacity = isInstalled ? 1 : 0;
                    btn.IsHitTestVisible = isInstalled;
                }
            }
        }
    }
}
