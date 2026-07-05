// MainWindow.Events.cs — Button click handlers and user-initiated event handlers.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    // ── Header buttons ────────────────────────────────────────────────────────────

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        _crashReporter.Log("[MainWindow.RefreshButton_Click] User clicked Refresh");
        _ = RefreshWithScrollRestore();
    }

    private async void FullRefreshButton_Click(object sender, RoutedEventArgs e)
    {
        _crashReporter.Log("[MainWindow.FullRefreshButton_Click] User clicked Full Refresh");

        var dialog = new ContentDialog
        {
            Title = "Full Refresh",
            Content = "This will clear all caches and re-scan everything from scratch:\n\n" +
                      "• Re-detects all games from every storefront\n" +
                      "• Re-scans DLSS/Streamline DLL paths\n" +
                      "• Re-detects graphics APIs and engine types\n" +
                      "• Rebuilds shader and addon deployment state\n\n" +
                      "Try a normal Refresh first — it handles most issues without the full rescan. " +
                      "Use Full Refresh as a last resort if games are missing, paths have changed, or DLSS has been added to a game.\n\n" +
                      "The next couple of restarts may take a few seconds longer while caches are rebuilt.\n\n" +
                      "Do not close RHI while the refresh is in progress — closing early will result in a missing library and the scan will need to be repeated.",
            PrimaryButtonText = "Continue",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };

        var result = await DialogService.ShowSafeAsync(dialog);
        if (result != ContentDialogResult.Primary) return;

        _ = FullRefreshWithScrollRestore();
    }

    private async void CheckForUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        _crashReporter.Log("[MainWindow.CheckForUpdatesButton_Click] User clicked Check For Updates");

        // Show progress dialog
        var progressPanel = new StackPanel { Spacing = 8 };
        var progressRow = new StackPanel { Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal, Spacing = 12 };
        var progressRing = new ProgressRing { IsActive = true, Width = 20, Height = 20 };
        var progressText = new TextBlock { Text = "Fetching manifest...", FontSize = 13, Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush) };
        progressRow.Children.Add(progressRing);
        progressRow.Children.Add(progressText);
        progressPanel.Children.Add(progressRow);

        var progressDialog = new ContentDialog
        {
            Title = "Checking for updates...",
            Content = progressPanel,
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        _ = DialogService.ShowSafeAsync(progressDialog);

        try
        {
            // Force bypass cooldown for the update check
            ViewModel.ForceNextUpdateCheck();

            // Trigger a Refresh (which fetches manifests + wiki + runs update checks)
            DispatcherQueue?.TryEnqueue(() => progressText.Text = "Checking components...");
            await ViewModel.RefreshAsync();

            // Check app update
            DispatcherQueue?.TryEnqueue(() => progressText.Text = "Checking app version...");
            await _dialogService.CheckForAppUpdateAsync();

            progressDialog.Hide();
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[CheckForUpdatesButton_Click] Error: {ex.Message}");
            progressDialog.Hide();
        }
    }

    private async void BrowseAddonWatchFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folderPath = await PickFolderAsync();
            if (!string.IsNullOrEmpty(folderPath))
            {
                AddonWatchFolderBox.Text = folderPath;
                ViewModel.Settings.AddonWatchFolder = folderPath;
                _addonFileWatcher.SetWatchPath(folderPath);
                ViewModel.SaveSettingsPublic();
                _crashReporter.Log($"[MainWindow] Addon watch folder set to: {folderPath}");
            }
        }
        catch (Exception ex) { _crashReporter.Log($"[MainWindow.BrowseAddonWatchFolder] {ex.Message}"); }
    }

    private void ResetAddonWatchFolder_Click(object sender, RoutedEventArgs e)
    {
        AddonWatchFolderBox.Text = "";
        ViewModel.Settings.AddonWatchFolder = "";
        var defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        _addonFileWatcher.SetWatchPath(defaultPath);
        ViewModel.SaveSettingsPublic();
        _crashReporter.Log("[MainWindow] Addon watch folder reset to default Downloads");
    }

    private void RsIniButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;
        if (string.IsNullOrEmpty(card.InstallPath)) return;
        try
        {
            var screenshotPath = BuildScreenshotSavePath(card.GameName);
            var overlayHotkey = ViewModel.Settings.OverlayHotkey;
            var screenshotHotkey = ViewModel.Settings.ScreenshotHotkey;
            if (card.RequiresVulkanInstall)
            {
                AuxInstallService.MergeRsVulkanIni(card.InstallPath, card.GameName, screenshotPath, overlayHotkey, screenshotHotkey);
                VulkanFootprintService.Create(card.InstallPath);
                // Deploy shaders for Vulkan games (no DLL install, so shaders go with INI)
                ViewModel.DeployShadersForCard(card.GameName);
            }
            else
                AuxInstallService.MergeRsIni(card.InstallPath, screenshotPath, overlayHotkey, screenshotHotkey);

            // Apply [renodx] section if UE-Extended is installed
            if (card.UseUeExtended && card.Status == GameStatus.Installed)
                AuxInstallService.ApplyRenoDxNativeHdrSettings(card.InstallPath);

            // Force-apply manifest [renodx] INI overrides on redeploy
            if (AuxInstallService.GlobalManifest?.RenodxIniOverrides != null
                && AuxInstallService.GlobalManifest.RenodxIniOverrides.TryGetValue(card.GameName, out var iniOvr))
                AuxInstallService.ApplyRenodxIniOverrides(card.InstallPath, iniOvr, forceOverwrite: true);

            card.RsActionMessage = "✅ reshade.ini merged into game folder.";
        }
        catch (Exception ex)
        {
            card.RsActionMessage = $"❌ {ex.Message}";
        }
    }

    private void LumaIniButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;
        if (string.IsNullOrEmpty(card.InstallPath)) return;
        try
        {
            var screenshotPath = BuildScreenshotSavePath(card.GameName);
            var overlayHotkey = ViewModel.Settings.OverlayHotkey;
            var screenshotHotkey = ViewModel.Settings.ScreenshotHotkey;
            AuxInstallService.MergeRsIni(card.InstallPath, screenshotPath, overlayHotkey, screenshotHotkey);

            // Apply [renodx] section if UE-Extended is installed
            if (card.UseUeExtended && card.Status == GameStatus.Installed)
                AuxInstallService.ApplyRenoDxNativeHdrSettings(card.InstallPath);

            card.LumaActionMessage = "✅ reshade.ini merged into game folder.";
        }
        catch (Exception ex)
        {
            card.LumaActionMessage = $"❌ {ex.Message}";
        }
    }

    private void SupportDiscord_Click(object sender, RoutedEventArgs e)
    {
        _ = Windows.System.Launcher.LaunchUriAsync(
            new Uri("https://discordapp.com/channels/1296187754979528747/1475173660686815374"));
    }

    // ── Links menu handlers ───────────────────────────────────────────────────

    private void LinkRenoDxWiki_Click(object sender, RoutedEventArgs e)
    {
        _ = Windows.System.Launcher.LaunchUriAsync(
            new Uri("https://github.com/clshortfuse/renodx/wiki/Mods"));
    }

    private void LinkLumaWiki_Click(object sender, RoutedEventArgs e)
    {
        _ = Windows.System.Launcher.LaunchUriAsync(
            new Uri("https://github.com/Filoppi/Luma-Framework/wiki"));
    }

    private void LinkRhiGithub_Click(object sender, RoutedEventArgs e)
    {
        _ = Windows.System.Launcher.LaunchUriAsync(
            new Uri("https://github.com/RankFTW/RHI"));
    }

    private void LinkReLimiterGithub_Click(object sender, RoutedEventArgs e)
    {
        _ = Windows.System.Launcher.LaunchUriAsync(
            new Uri("https://github.com/RankFTW/ReLimiter"));
    }


    // ── Views menu handlers ───────────────────────────────────────────────────

    private void ViewCompact_Click(object sender, RoutedEventArgs e)
        => SwitchToView(ViewLayout.Compact);

    private void ViewDetail_Click(object sender, RoutedEventArgs e)
        => SwitchToView(ViewLayout.Detail);

    private void SwitchToView(ViewLayout target)
    {
        if (ViewModel.CurrentViewLayout == target) return;

        var previousLayout = ViewModel.CurrentViewLayout;
        ViewModel.CurrentViewLayout = target;
        ViewModel.SaveSettingsPublic();

        // Handle window size locking transitions
        if (target == ViewLayout.Compact)
        {
            _windowStateManager.CaptureCurrentBounds();
            _windowStateManager.ApplyCompactSize();
            _windowStateManager.SetSizeLocked(true);
        }
        else if (previousLayout == ViewLayout.Compact)
        {
            _compactViewBuilder?.LeaveCompactMode();
            _windowStateManager.SetSizeLocked(false);
            _windowStateManager.RestoreWindowBounds();
        }

        // Rebuild content for the new layout
        switch (target)
        {
            case ViewLayout.Detail:
                if (ViewModel.SelectedGame is { } card)
                {
                    PopulateDetailPanel(card);
                    DetailPanel.Visibility = Visibility.Visible;
                    BuildOverridesPanel(card);
                    OverridesContainer.Visibility = Visibility.Visible;
                    NvidiaProfileContainer.Visibility = Visibility.Visible;
                    ManagementContainer.Visibility = Visibility.Visible;
                }
                break;
            case ViewLayout.Compact:
                if (ViewModel.SelectedGame is { } compactCard)
                    _compactViewBuilder?.EnterCompactMode(compactCard, ViewModel.CompactPageIndex);
                break;
        }
    }

    private void LayoutToggle_Click(object sender, RoutedEventArgs e)
    {
        var previousLayout = ViewModel.CurrentViewLayout;
        ViewModel.CurrentViewLayout = ViewModel.NextViewLayout();
        ViewModel.SaveSettingsPublic(); // persist the chosen layout

        // Handle window size locking transitions
        if (ViewModel.CurrentViewLayout == ViewLayout.Compact)
        {
            _windowStateManager.CaptureCurrentBounds();
            _windowStateManager.ApplyCompactSize();
            _windowStateManager.SetSizeLocked(true);
        }
        else if (previousLayout == ViewLayout.Compact)
        {
            // Leaving compact mode — restore all sections to visible first
            _compactViewBuilder?.LeaveCompactMode();
            _windowStateManager.SetSizeLocked(false);
            _windowStateManager.RestoreWindowBounds();
        }

        // Rebuild content for the new layout
        switch (ViewModel.CurrentViewLayout)
        {
            case ViewLayout.Detail:
                // Switching to detail mode — repopulate detail panel for selected game if any
                if (ViewModel.SelectedGame is { } card)
                {
                    PopulateDetailPanel(card);
                    DetailPanel.Visibility = Visibility.Visible;
                    BuildOverridesPanel(card);
                    OverridesContainer.Visibility = Visibility.Visible;
                    NvidiaProfileContainer.Visibility = Visibility.Visible;
                    ManagementContainer.Visibility = Visibility.Visible;
                }
                break;
            case ViewLayout.Compact:
                if (ViewModel.SelectedGame is { } compactCard)
                    _compactViewBuilder?.EnterCompactMode(compactCard, ViewModel.CompactPageIndex);
                break;
        }
    }

    private void CompactNavLeft_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.NavigateCompactPage(-1);
        _compactViewBuilder?.NavigateToPage(ViewModel.CompactPageIndex);
    }

    private void CompactNavRight_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.NavigateCompactPage(1);
        _compactViewBuilder?.NavigateToPage(ViewModel.CompactPageIndex);
    }

    // ── Per-component install flyout click handlers ──

    internal async void CardComponentInstall_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not GameCardViewModel card) return;
        var component = btn.DataContext as string;

        // Ensure install path exists (same pattern as CardInstallButton_Click)
        if (string.IsNullOrEmpty(card.InstallPath) || !System.IO.Directory.Exists(card.InstallPath))
        {
            var folder = await PickFolderAsync();
            if (folder == null) return;
            card.InstallPath = folder;
            ViewModel.SaveLibraryPublic();
        }

        switch (component)
        {
            case "RDX":
                await ViewModel.InstallModCommand.ExecuteAsync(card);
                break;
            case "RS":
                await ViewModel.InstallReShadeCommand.ExecuteAsync(card);
                break;
            case "Luma":
                await ViewModel.InstallLumaAsync(card);
                break;
            case "UL":
                await ViewModel.InstallUlAsync(card);
                break;
            case "DC":
                await ViewModel.InstallDcAsync(card);
                break;
            case "REF":
                await ViewModel.InstallREFrameworkCommand.ExecuteAsync(card);
                break;
            case "OS":
                _installEventHandler.InstallOsButton_Click(sender, e);
                break;
        }
    }

    internal void CardComponentUninstall_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not GameCardViewModel card) return;
        var component = btn.DataContext as string;

        switch (component)
        {
            case "RDX":
                ViewModel.UninstallModCommand.Execute(card);
                break;
            case "RS":
                if (card.RequiresVulkanInstall)
                    ViewModel.UninstallVulkanReShadeCommand.Execute(card);
                else
                    ViewModel.UninstallReShadeCommand.Execute(card);
                break;
            case "Luma":
                ViewModel.UninstallLumaCommand.Execute(card);
                break;
            case "UL":
                ViewModel.UninstallUl(card);
                break;
            case "DC":
                ViewModel.UninstallDc(card);
                break;
            case "REF":
                ViewModel.UninstallREFrameworkCommand.Execute(card);
                break;
            case "OS":
                _installEventHandler.UninstallOsButton_Click(sender, e);
                break;
        }
    }

    internal void CardCopyRsIni_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;
        if (string.IsNullOrEmpty(card.InstallPath)) return;
        try
        {
            var screenshotPath = BuildScreenshotSavePath(card.GameName);
            var overlayHotkey = ViewModel.Settings.OverlayHotkey;
            var screenshotHotkey = ViewModel.Settings.ScreenshotHotkey;
            if (card.RequiresVulkanInstall)
            {
                AuxInstallService.MergeRsVulkanIni(card.InstallPath, card.GameName, screenshotPath, overlayHotkey, screenshotHotkey);
                VulkanFootprintService.Create(card.InstallPath);
                // Deploy shaders for Vulkan games (no DLL install, so shaders go with INI)
                ViewModel.DeployShadersForCard(card.GameName);
            }
            else
                AuxInstallService.MergeRsIni(card.InstallPath, screenshotPath, overlayHotkey, screenshotHotkey);

            // Apply [renodx] section if UE-Extended is installed
            if (card.UseUeExtended && card.Status == GameStatus.Installed)
                AuxInstallService.ApplyRenoDxNativeHdrSettings(card.InstallPath);

            card.RsActionMessage = "✅ reshade.ini merged into game folder.";
        }
        catch (Exception ex)
        {
            card.RsActionMessage = $"❌ {ex.Message}";
        }
    }

    internal void CardCopyUlIni_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;
        if (string.IsNullOrEmpty(card.InstallPath)) return;
        try
        {
            AuxInstallService.CopyUlIni(card.InstallPath);
            card.UlActionMessage = "✅ relimiter.ini copied to game folder.";
        }
        catch (Exception ex)
        {
            card.UlActionMessage = $"❌ {ex.Message}";
        }
    }

    internal void CardCopyDcIni_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;
        if (string.IsNullOrEmpty(card.InstallPath)) return;
        try
        {
            AuxInstallService.CopyDcIni(card.InstallPath);
            card.DcActionMessage = "✅ DisplayCommander.ini copied to game folder.";
            card.FadeMessage(m => card.DcActionMessage = m, card.DcActionMessage);
        }
        catch (Exception ex)
        {
            card.DcActionMessage = $"❌ {ex.Message}";
        }
    }

    internal void CardCopyOsIni_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;
        if (string.IsNullOrEmpty(card.InstallPath)) return;
        try
        {
            var sourceIni = Services.OptiScalerService.OsIniPath;
            if (!File.Exists(sourceIni))
            {
                card.OsActionMessage = "❌ No OptiScaler.ini found in INIs folder.";
                return;
            }
            var destIni = Path.Combine(card.InstallPath, Services.OptiScalerService.IniFileName);
            File.Copy(sourceIni, destIni, overwrite: true);
            Services.OptiScalerService.EnforceLoadReshade(destIni);
            card.OsActionMessage = "✅ OptiScaler.ini copied to game folder.";
            card.FadeMessage(m => card.OsActionMessage = m, card.OsActionMessage);
        }
        catch (Exception ex)
        {
            card.OsActionMessage = $"❌ {ex.Message}";
        }
    }

    // ── Card action button handlers ───────────────────────────────────────────────

    internal async void CardInstallButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not GameCardViewModel card) return;

        // Route to Luma install if in Luma mode, otherwise RenoDX combined install
        if (card.LumaFeatureEnabled && card.IsLumaMode && card.LumaMod != null)
        {
            await ViewModel.InstallLumaAsync(card);
        }
        else
        {
            // Ensure install path exists
            if (string.IsNullOrEmpty(card.InstallPath) || !System.IO.Directory.Exists(card.InstallPath))
            {
                var folder = await PickFolderAsync();
                if (folder == null) return;
                card.InstallPath = folder;
                ViewModel.SaveLibraryPublic();
            }
            // Chain: RenoDX → RE Framework → ReShade (skip components that are N/A)
            if (card.Mod?.SnapshotUrl != null)
                await ViewModel.InstallModCommand.ExecuteAsync(card);
            if (card.RefRowVisibility == Visibility.Visible)
                await ViewModel.InstallREFrameworkCommand.ExecuteAsync(card);
            if (card.ReShadeRowVisibility == Visibility.Visible)
                await ViewModel.InstallReShadeCommand.ExecuteAsync(card);
        }
    }

    internal void CardFavouriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not GameCardViewModel card) return;
        ViewModel.ToggleFavouriteCommand.Execute(card);
        btn.Content = card.IsFavourite ? "⭐" : "☆";

        // Also refresh the detail panel icon if this is the selected game
        if (card == ViewModel.SelectedGame)
        {
            DetailFavIcon.Text = "Favourite";
            var favColor = card.IsFavourite
                ? ((SolidColorBrush)Application.Current.Resources[ResourceKeys.AccentAmberBrush]).Color
                : ((SolidColorBrush)Application.Current.Resources[ResourceKeys.ChipTextBrush]).Color;
            DetailFavIcon.Foreground = new SolidColorBrush(favColor);
            DetailFavBtn.BorderBrush = card.IsFavourite
                ? new SolidColorBrush(((SolidColorBrush)Application.Current.Resources[ResourceKeys.AccentAmberBrush]).Color)
                : (Brush)Application.Current.Resources[ResourceKeys.BorderSubtleBrush];
        }
    }

    private void CardOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var card = GetCardFromSender(sender);
        if (card == null || string.IsNullOrEmpty(card.InstallPath)) return;
        if (System.IO.Directory.Exists(card.InstallPath))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(card.InstallPath) { UseShellExecute = true });
    }

    internal void CardMoreMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement anchor || anchor.Tag is not GameCardViewModel card)
            return;

        ViewModel.SelectedGame = card;

        var menu = new MenuFlyout
        {
            Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight,
        };

        // ── Open Folder ──
        var openFolderItem = new MenuFlyoutItem
        {
            Text = "📂 Open Folder",
            Tag = card,
        };
        openFolderItem.Click += CardOpenFolder_Click;
        menu.Items.Add(openFolderItem);

        // ── Hide / Show ──
        var hideItem = new MenuFlyoutItem
        {
            Text = card.HideButtonLabel,
            Tag = card,
        };
        hideItem.Click += (s, ev) => ViewModel.ToggleHideGameCommand.Execute(card);
        menu.Items.Add(hideItem);

        // ── Luma toggle (conditional — only when Luma is available for this game) ──
        if (card.LumaFeatureEnabled && card.IsLumaAvailable)
        {
            var lumaLabel = card.IsLumaMode ? "🟢 Luma Enabled" : "⚫ Enable Luma";
            var lumaItem = new MenuFlyoutItem
            {
                Text = lumaLabel,
                Tag = card,
            };
            lumaItem.Click += (s, ev) => ViewModel.ToggleLumaMode(card);
            menu.Items.Add(lumaItem);
        }

        menu.Items.Add(new MenuFlyoutSeparator());

        // ── Discussion / Instructions (conditional) ──
        if (card.HasNameUrl)
        {
            var discussionItem = new MenuFlyoutItem
            {
                Text = "ℹ Discussion / Instructions",
                Tag = card,
            };
            discussionItem.Click += async (s, ev) =>
            {
                if (card.NameUrl != null)
                    await Windows.System.Launcher.LaunchUriAsync(new Uri(card.NameUrl));
            };
            menu.Items.Add(discussionItem);
        }

        // ── View Notes (conditional) ──
        if (card.HasNotes)
        {
            var notesItem = new MenuFlyoutItem
            {
                Text = "💬 View Notes",
                Tag = card,
            };
            notesItem.Click += async (s, ev) =>
            {
                // Create a temporary Button to pass through ShowAddonInfoDialogAsync
                // which expects a Button with Tag (card) and DataContext (AddonType)
                var tempBtn = new Button { Tag = card, DataContext = card.IsLumaMode ? AddonType.Luma : AddonType.RenoDX };
                await _dialogService.ShowAddonInfoDialogAsync(tempBtn, ev);
            };
            menu.Items.Add(notesItem);
        }

        menu.ShowAt(anchor);
    }

    internal async void InfoButton_Click(object sender, RoutedEventArgs e)
        => await _dialogService.ShowAddonInfoDialogAsync(sender, e);

    internal async void CardInfoLink_Click(object sender, RoutedEventArgs e)
    {
        var card = GetCardFromSender(sender);
        if (card?.NameUrl != null)
        {
            try { await Windows.System.Launcher.LaunchUriAsync(new Uri(card.NameUrl)); }
            catch (Exception ex) { _crashReporter.Log($"[MainWindow.CardInfoLink_Click] Failed — {ex.Message}"); }
        }
    }

    internal async void ExternalLink_Click(object sender, RoutedEventArgs e)
    {
        var card = GetCardFromSender(sender);
        if (card == null) return;
        // When IsExternalOnly the ExternalUrl has already been resolved correctly
        // (e.g. forced to Discord by ApplyCardOverrides). Use it directly so a
        // NexusUrl on the underlying mod can't override the intended destination.
        var url = card.IsExternalOnly ? card.ExternalUrl : (card.NexusUrl ?? card.DiscordUrl ?? card.ExternalUrl);
        if (!string.IsNullOrEmpty(url))
            await Windows.System.Launcher.LaunchUriAsync(new Uri(url));

        // Reset Nexus baseline when user clicks the update button — they're acknowledging the update
        if (card.Status == GameStatus.UpdateAvailable && card.IsExternalOnly)
        {
            var nexusService = App.Services.GetRequiredService<INexusUpdateService>();
            nexusService.ResetBaseline(card.GameName);
            card.Status = GameStatus.Installed;
            card.NotifyAll();
            ViewModel.NotifyUpdateButtonChanged();
        }
    }

    private async void NameLink_Click(object sender, RoutedEventArgs e)
    {
        var card = GetCardFromSender(sender);
        if (card?.NameUrl != null)
            await Windows.System.Launcher.LaunchUriAsync(new Uri(card.NameUrl));
    }

    private async void PcgwLink_Click(object sender, RoutedEventArgs e)
    {
        var card = GetCardFromSender(sender);
        if (card?.PcgwUrl != null)
            await Windows.System.Launcher.LaunchUriAsync(new Uri(card.PcgwUrl));
    }

    private async void NexusModsLink_Click(object sender, RoutedEventArgs e)
    {
        var card = GetCardFromSender(sender);
        if (card?.NexusModsUrl != null)
            await Windows.System.Launcher.LaunchUriAsync(new Uri(card.NexusModsUrl));
    }

    private async void UwFixLink_Click(object sender, RoutedEventArgs e)
    {
        var card = GetCardFromSender(sender);
        if (card?.UwFixUrl != null)
            await Windows.System.Launcher.LaunchUriAsync(new Uri(card.UwFixUrl));
    }

    private async void UltraPlusLink_Click(object sender, RoutedEventArgs e)
    {
        var card = GetCardFromSender(sender);
        if (card?.UltraPlusUrl != null)
            await Windows.System.Launcher.LaunchUriAsync(new Uri(card.UltraPlusUrl));
    }

    // ── Detail panel handlers ─────────────────────────────────────────────────────

    private void DetailScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        const double maxWidth = 850;
        const double padding = 48; // 24 left + 24 right
        var available = e.NewSize.Width - padding;
        DetailPanel.Width = available > maxWidth ? maxWidth : (available > 0 ? available : double.NaN);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ViewModel.SearchQuery = SearchBox.Text;
        // Always show the clear (✕) button
        VisualStateManager.GoToState(SearchBox, "ButtonVisible", true);

        // Show/hide the save filter button based on whether there's a non-whitespace query
        SaveFilterButton.Visibility = string.IsNullOrWhiteSpace(SearchBox.Text)
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;

        // Refresh custom chip styles (active filter may have been deactivated by the query change)
        RebuildCustomFilterChips();
    }

    private async void SaveFilterButton_Click(object sender, RoutedEventArgs e)
    {
        var currentQuery = SearchBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(currentQuery)) return;

        var nameBox = new TextBox { PlaceholderText = "Filter name", Text = currentQuery, Width = 350 };
        var errorText = new TextBlock
        {
            Text = "",
            Foreground = Brush(ResourceKeys.AccentRedBrush),
            Visibility = Visibility.Collapsed,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        };

        var dialog = new ContentDialog
        {
            Title = "Save Custom Filter",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = $"Save the current search \"{currentQuery}\" as a custom filter:",
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = Brush(ResourceKeys.TextSecondaryBrush),
                    },
                    nameBox,
                    errorText,
                }
            },
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            XamlRoot = Content.XamlRoot,
            Background = Brush(ResourceKeys.SurfaceToolbarBrush),
            RequestedTheme = ElementTheme.Dark,
        };

        // Validate inline before closing the dialog
        dialog.PrimaryButtonClick += (s, args) =>
        {
            var name = nameBox.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(name))
            {
                errorText.Text = "Please enter a filter name.";
                errorText.Visibility = Visibility.Visible;
                args.Cancel = true;
                return;
            }
            if (ViewModel.Filter.CustomFilterNameExists(name))
            {
                errorText.Text = $"A filter named \"{name}\" already exists.";
                errorText.Visibility = Visibility.Visible;
                args.Cancel = true;
                return;
            }
        };

        var result = await DialogService.ShowSafeAsync(dialog);
        if (result != ContentDialogResult.Primary) return;

        var filterName = nameBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(filterName)) return;

        ViewModel.Filter.AddCustomFilter(filterName, currentQuery);
        RebuildCustomFilterChips();

        // Clear search box and auto-select the new filter
        SearchBox.Text = "";
        SaveFilterButton.Visibility = Visibility.Collapsed;
        ViewModel.Filter.ActivateCustomFilter(filterName);
        RebuildCustomFilterChips();
    }

    /// <summary>
    /// Rebuilds the custom filter chip UI from <see cref="FilterViewModel.CustomFilters"/>.
    /// </summary>
    private void RebuildCustomFilterChips()
    {
        CustomFilterChipPanel.Children.Clear();

        foreach (var filter in ViewModel.Filter.CustomFilters)
        {
            var chipName = filter.Name;
            bool isActive = string.Equals(ViewModel.Filter.ActiveCustomFilterName, chipName, StringComparison.OrdinalIgnoreCase);

            var chip = new Button
            {
                Content = chipName,
                Tag = chipName,
                Background = new SolidColorBrush(
                    ((SolidColorBrush)Application.Current.Resources[
                        isActive ? ResourceKeys.CustomChipActiveBrush : ResourceKeys.CustomChipDefaultBrush]).Color),
                Foreground = isActive
                    ? new SolidColorBrush(Microsoft.UI.Colors.White)
                    : (SolidColorBrush)Application.Current.Resources[ResourceKeys.CustomChipTextBrush],
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(10, 5, 10, 5),
                FontSize = 11,
            };

            chip.Click += CustomFilterChip_Click;

            // Right-click context menu with "Delete" option (Req 5.1–5.5)
            var flyout = new MenuFlyout();
            var deleteItem = new MenuFlyoutItem { Text = "Delete" };
            deleteItem.Click += (s, args) =>
            {
                ViewModel.Filter.RemoveCustomFilter(chipName);
                RebuildCustomFilterChips();
            };
            flyout.Items.Add(deleteItem);
            chip.ContextFlyout = flyout;

            CustomFilterChipPanel.Children.Add(chip);
        }
    }

    private void CustomFilterChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string name) return;

        ViewModel.Filter.ActivateCustomFilter(name);
        RebuildCustomFilterChips();
    }

    // ── Manual add game ───────────────────────────────────────────────────────────

    private async void AddGameButton_Click(object sender, RoutedEventArgs e)
    {
        _crashReporter.Log("[MainWindow.AddGameButton_Click] Button clicked");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _crashReporter.Log($"[MainWindow.AddGameButton_Click] hwnd={hwnd}");

        // Use Win32 OpenFileDialog as primary method — WinRT FileOpenPicker has
        // COM threading issues on some systems when background work is active.
        string? filePath = null;
        try
        {
            filePath = await Task.Run(() =>
            {
                var ofn = new NativeInterop.OpenFileName();
                ofn.structSize = System.Runtime.InteropServices.Marshal.SizeOf(ofn);
                ofn.hwndOwner = hwnd;
                ofn.filter = "Executables (*.exe)\0*.exe\0All Files (*.*)\0*.*\0";
                ofn.file = new string(new char[260]);
                ofn.maxFile = ofn.file.Length;
                ofn.title = "Select Game Executable";
                ofn.flags = 0x00080000 | 0x00001000; // OFN_EXPLORER | OFN_FILEMUSTEXIST

                return NativeInterop.GetOpenFileName(ref ofn) ? ofn.file.TrimEnd('\0') : null;
            });
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainWindow.AddGameButton_Click] Win32 file dialog failed — {ex.GetType().Name}: {ex.Message}");
            return;
        }

        _crashReporter.Log($"[MainWindow.AddGameButton_Click] Dialog result: {(filePath != null ? filePath : "null (cancelled)")}");
        if (string.IsNullOrEmpty(filePath)) return;

        // Use the exe's parent folder as the install path
        var folder = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(folder)) return;

        // Pre-populate the game name from the folder name
        var suggestedName = Path.GetFileName(folder);

        // Step 2: Ask for the game name
        var nameBox = new TextBox { Text = suggestedName, Width = 350 };
        nameBox.SelectAll();
        var nameDialog = new ContentDialog
        {
            Title           = "Name This Game",
            Content         = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = $"Selected: {filePath}", TextWrapping = TextWrapping.Wrap, Foreground = Brush(ResourceKeys.TextSecondaryBrush), FontSize = 11 },
                    new TextBlock { Text = "Enter the game name:", TextWrapping = TextWrapping.Wrap, Foreground = Brush(ResourceKeys.TextSecondaryBrush) },
                    nameBox
                }
            },
            PrimaryButtonText   = "Add Game",
            CloseButtonText     = "Cancel",
            XamlRoot            = Content.XamlRoot,
            Background          = Brush(ResourceKeys.SurfaceToolbarBrush),
            RequestedTheme      = ElementTheme.Dark,
        };
        var result = await DialogService.ShowSafeAsync(nameDialog);
        if (result != ContentDialogResult.Primary) return;

        var gameName = nameBox.Text.Trim();
        if (string.IsNullOrEmpty(gameName)) return;
        _crashReporter.Log($"[MainWindow.AddGameButton_Click] Adding game: {gameName} at {folder}");

        var game = new DetectedGame
        {
            Name = gameName, InstallPath = folder, Source = "Manual", IsManuallyAdded = true
        };
        ViewModel.AddManualGameCommand.Execute(game);
    }

    // ── Filter tabs ───────────────────────────────────────────────────────────────

    private void Filter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        ViewModel.SetFilterCommand.Execute(btn.Tag as string ?? "Detected");
        RefreshFilterButtonStyles();
    }

    internal void FavouriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not GameCardViewModel card) return;
        ViewModel.ToggleFavouriteCommand.Execute(card);

        // Refresh the detail panel icon to reflect the new state
        DetailFavIcon.Text = "Favourite";
        var favColor = card.IsFavourite
            ? ((SolidColorBrush)Application.Current.Resources[ResourceKeys.AccentAmberBrush]).Color
            : ((SolidColorBrush)Application.Current.Resources[ResourceKeys.ChipTextBrush]).Color;
        DetailFavIcon.Foreground = new SolidColorBrush(favColor);
        DetailFavBtn.BorderBrush = card.IsFavourite
            ? new SolidColorBrush(((SolidColorBrush)Application.Current.Resources[ResourceKeys.AccentAmberBrush]).Color)
            : (Brush)Application.Current.Resources[ResourceKeys.BorderSubtleBrush];
    }

    // ── Card handlers ─────────────────────────────────────────────────────────────

    private void ExpandComponents_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is GameCardViewModel card)
            card.ComponentExpanded = !card.ComponentExpanded;
    }

    private void CombinedInstallButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.CombinedInstallButton_Click(sender, e);

    internal void InstallButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.InstallButton_Click(sender, e);

    private void Install64Button_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.Install64Button_Click(sender, e);

    private void Install32Button_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.Install32Button_Click(sender, e);

    private async Task EnsurePathAndInstall(GameCardViewModel card, Func<Task> installAction)
        => await _installEventHandler.EnsurePathAndInstall(card, installAction);

    internal void UninstallButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.UninstallButton_Click(sender, e);

    internal void InstallRsButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.InstallRsButton_Click(sender, e);

    internal void UninstallRsButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.UninstallRsButton_Click(sender, e);

    private void ChooseShadersButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.ChooseShadersButton_Click(sender, e);

    private async void ReShadeAddonsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Req 10.1–10.5: First-time warning dialog
            if (!ViewModel.Settings.AddonWarningDismissed)
            {
                var warningDialog = new ContentDialog
                {
                    Title = "⚠ ReShade Addons",
                    Content = new TextBlock
                    {
                        Text = "ReShade addons are advanced features intended for experienced users who understand what they are.\n\n" +
                               "Addons can modify game rendering behaviour and may cause instability. " +
                               "Only proceed if you are comfortable managing ReShade addons.",
                        TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                        MaxWidth = 450,
                    },
                    PrimaryButtonText = "Continue",
                    CloseButtonText = "Cancel",
                    XamlRoot = Content.XamlRoot,
                    RequestedTheme = ElementTheme.Dark,
                };

                var result = await DialogService.ShowSafeAsync(warningDialog);

                if (result != ContentDialogResult.Primary)
                    return; // Req 10.5: Cancel — don't persist flag, don't open manager

                // Req 10.4: Persist dismissal flag so warning is not shown again
                ViewModel.Settings.AddonWarningDismissed = true;
                ViewModel.SaveSettingsPublic();
            }

            // Use the ViewModel's AddonPackService (initialized on startup)
            var addonService = _addonPackService;
            await addonService.EnsureLatestAsync();
            // Re-apply manifest overrides (EnsureLatestAsync repopulates from Addons.ini)
            (addonService as RenoDXCommander.Services.AddonPackService)?.ApplyManifestOverrides(ViewModel.Manifest);
            await AddonManagerDialog.ShowAsync(Content.XamlRoot, addonService,
                ViewModel.Settings.EnabledGlobalAddons,
                () => { ViewModel.SaveSettingsPublic(); ViewModel.DeployAllAddons(); });
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainWindow.ReShadeAddonsButton_Click] Failed — {ex.Message}");
        }
    }

}
