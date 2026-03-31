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

        // ReLimiter row
        var ulRow = BuildComponentRow(card, "ReLimiter", "UL",
            card.UlStatusText, card.UlStatusColor, card.UlShortAction,
            card.IsUlNotInstalling, card.IsUlInstalled,
            showCopyConfig: true, copyConfigVisible: card.UlIniExists,
            copyConfigTooltip: "Copy relimiter.ini to game folder",
            btnBackground: card.UlBtnBackground, btnForeground: card.UlBtnForeground, btnBorderBrush: card.UlBtnBorderBrush);
        // Make UL status text a clickable link to the ReLimiter guide (or release page when update available)
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
                        new Uri("https://github.com/RankFTW/Ultra-Limiter?tab=readme-ov-file#ultra-limiter--comprehensive-feature-guide"));
            };
        }
        ulRow.Visibility = card.UlRowVisibility;
        panel.Children.Add(ulRow);

        // RenoDX row
        var rdxRow = BuildComponentRow(card, "RenoDX", "RDX",
            card.RdxStatusText, card.RdxStatusColor, card.RdxShortAction,
            card.CardRdxInstallEnabled, card.IsRdxInstalled,
            showCopyConfig: false, copyConfigVisible: false,
            copyConfigTooltip: null,
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
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });  // name
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
        if (componentTag == "UL")
            copyBtn.Click += _window.CardCopyUlIni_Click;
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
}
