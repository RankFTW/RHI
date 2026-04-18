// MainWindow.Events.cs — Button click handlers and user-initiated event handlers.

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

    private void FullRefreshButton_Click(object sender, RoutedEventArgs e)
    {
        _crashReporter.Log("[MainWindow.FullRefreshButton_Click] User clicked Full Refresh");
        _ = FullRefreshWithScrollRestore();
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
            if (card.RequiresVulkanInstall)
            {
                AuxInstallService.MergeRsVulkanIni(card.InstallPath, card.GameName, screenshotPath, overlayHotkey);
                VulkanFootprintService.Create(card.InstallPath);
                // Deploy shaders for Vulkan games (no DLL install, so shaders go with INI)
                ViewModel.DeployShadersForCard(card.GameName);
            }
            else
                AuxInstallService.MergeRsIni(card.InstallPath, screenshotPath, overlayHotkey);
            AuxInstallService.CopyRsPresetIniIfPresent(card.InstallPath);
            bool presetDeployed = File.Exists(AuxInstallService.RsPresetIniPath);
            card.RsActionMessage = presetDeployed
                ? "✅ reshade.ini merged & ReShadePreset.ini copied."
                : "✅ reshade.ini merged into game folder.";
        }
        catch (Exception ex)
        {
            card.RsActionMessage = $"❌ {ex.Message}";
        }
    }

    private void SupportDiscord_Click(object sender, RoutedEventArgs e)
    {
        _ = Windows.System.Launcher.LaunchUriAsync(
            new Uri("https://discordapp.com/channels/1296187754979528747/1475173660686815374"));
    }

    private void SupportGuide_Click(object sender, RoutedEventArgs e)
    {
        _ = Windows.System.Launcher.LaunchUriAsync(
            new Uri("https://github.com/RankFTW/rdxc-manifest?tab=readme-ov-file#renodx-commander--detailed-guide"));
    }

    private void SupportKofi_Click(object sender, RoutedEventArgs e)
    {
        _ = Windows.System.Launcher.LaunchUriAsync(
            new Uri("https://ko-fi.com/rankftw"));
    }

    private void LayoutToggle_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.IsGridLayout = !ViewModel.IsGridLayout;
        ViewModel.SaveSettingsPublic(); // persist the chosen layout
        if (ViewModel.IsGridLayout)
        {
            RebuildCardGrid();
        }
        else
        {
            // Switching to detail mode — repopulate detail panel for selected game if any
            if (ViewModel.SelectedGame is { } card)
            {
                PopulateDetailPanel(card);
                DetailPanel.Visibility = Visibility.Visible;
                BuildOverridesPanel(card);
                OverridesContainer.Visibility = Visibility.Visible;
                ManagementContainer.Visibility = Visibility.Visible;
            }
        }
    }

    /// <summary>
    /// Handler for the install flyout opening — builds the flyout content and attaches it.
    /// Called when the install button's flyout is about to open.
    /// </summary>
    internal void CardInstallFlyout_Opening(object? sender, object e)
    {
        if (sender is not Flyout flyout) return;
        if (flyout.Target is not FrameworkElement { Tag: GameCardViewModel card }) return;

        var content = _cardBuilder.BuildInstallFlyoutContent(card);

        var scrollViewer = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 400,
        };

        flyout.Content = scrollViewer;

        // Unsubscribe from PropertyChanged when flyout closes
        flyout.Closed += FlyoutClosed;

        void FlyoutClosed(object? s, object ev)
        {
            flyout.Closed -= FlyoutClosed;
            if (content.Tag is (GameCardViewModel c, System.ComponentModel.PropertyChangedEventHandler h))
            {
                c.PropertyChanged -= h;
            }
        }
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
            if (card.RequiresVulkanInstall)
            {
                AuxInstallService.MergeRsVulkanIni(card.InstallPath, card.GameName, screenshotPath, overlayHotkey);
                VulkanFootprintService.Create(card.InstallPath);
                // Deploy shaders for Vulkan games (no DLL install, so shaders go with INI)
                ViewModel.DeployShadersForCard(card.GameName);
            }
            else
                AuxInstallService.MergeRsIni(card.InstallPath, screenshotPath, overlayHotkey);
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

    internal void CardOverridesButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement anchor && anchor.Tag is GameCardViewModel card)
        {
            ViewModel.SelectedGame = card;
            OpenOverridesFlyout(card, anchor);
        }
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
            };
            notesItem.Click += NotesButton_Click;
            menu.Items.Add(notesItem);
        }

        menu.ShowAt(anchor);
    }

    internal void Card_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        try
        {
            if (sender is not Border b || b.Tag is not GameCardViewModel card) return;

            foreach (var c in ViewModel.DisplayedGames)
                c.CardHighlighted = false;

            card.CardHighlighted = true;
            ViewModel.SelectedGame = card;
        }
        catch (Exception ex) { _crashReporter.Log($"[MainWindow.Card_PointerPressed] Error selecting card — {ex.Message}"); }
    }

    private void NotesButton_Click(object sender, RoutedEventArgs e)
        => _dialogService.NotesButton_Click(sender, e);

    internal async void CardInfoLink_Click(object sender, RoutedEventArgs e)
    {
        var card = GetCardFromSender(sender);
        if (card?.NameUrl != null)
        {
            try { await Windows.System.Launcher.LaunchUriAsync(new Uri(card.NameUrl)); }
            catch (Exception ex) { _crashReporter.Log($"[MainWindow.CardInfoLink_Click] Failed — {ex.Message}"); }
        }
    }

    internal void CardNotesButton_Click(object sender, RoutedEventArgs e)
    {
        NotesButton_Click(sender, e);
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

    // ── Settings handlers ─────────────────────────────────────────────────────────

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
        => _settingsHandler.SettingsButton_Click(sender, e);

    private void SkipUpdateToggle_Toggled(object sender, RoutedEventArgs e)
        => _settingsHandler.SkipUpdateToggle_Toggled(sender, e);

    private void VerboseLoggingToggle_Toggled(object sender, RoutedEventArgs e)
        => _settingsHandler.VerboseLoggingToggle_Toggled(sender, e);

    private async void PatchNotesLink_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await ShowPatchNotesDialogAsync();
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainWindow.PatchNotesLink_Click] Patch notes dialog error — {ex.Message}");
        }
    }

    private void OpenLogsFolder_Click(object sender, RoutedEventArgs e)
        => _settingsHandler.OpenLogsFolder_Click(sender, e);

    private void OpenDownloadsFolder_Click(object sender, RoutedEventArgs e)
        => _settingsHandler.OpenDownloadsFolder_Click(sender, e);

    private void CustomShadersToggle_Toggled(object sender, RoutedEventArgs e)
        => _settingsHandler.CustomShadersToggle_Toggled(sender, e);

    private void ApplyScreenshotPath_Click(object sender, RoutedEventArgs e)
        => _settingsHandler.ApplyScreenshotPath_Click(sender, e);

    private void HotkeyBox_PreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        => _settingsHandler.HotkeyBox_PreviewKeyDown(sender, e);

    private void ApplyOverlayHotkey_Click(object sender, RoutedEventArgs e)
        => _settingsHandler.ApplyOverlayHotkey_Click(sender, e);

    private void UlHotkeyBox_PreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        => _settingsHandler.UlHotkeyBox_PreviewKeyDown(sender, e);

    private void ApplyUlOsdHotkey_Click(object sender, RoutedEventArgs e)
        => _settingsHandler.ApplyUlOsdHotkey_Click(sender, e);

    private void OsHotkeyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => _settingsHandler.OsHotkeyCombo_SelectionChanged(sender, e);

    private void OsGpuCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => _settingsHandler.OsGpuCombo_SelectionChanged(sender, e);

    private void OsDlssInputsToggle_Toggled(object sender, RoutedEventArgs e)
        => _settingsHandler.OsDlssInputsToggle_Toggled(sender, e);

    private void ApplyOsHotkey_Click(object sender, RoutedEventArgs e)
        => _settingsHandler.ApplyOsHotkey_Click(sender, e);

    private void MassDeployRsIni_Click(object sender, RoutedEventArgs e)
        => _settingsHandler.MassDeployRsIni_Click(sender, e);

    private void MassDeployUlIni_Click(object sender, RoutedEventArgs e)
        => _settingsHandler.MassDeployUlIni_Click(sender, e);

    private void MassDeployDcIni_Click(object sender, RoutedEventArgs e)
        => _settingsHandler.MassDeployDcIni_Click(sender, e);

    private void MassDeployOsIni_Click(object sender, RoutedEventArgs e)
        => _settingsHandler.MassDeployOsIni_Click(sender, e);

    private async void MassPresetInstall_Click(object sender, RoutedEventArgs e)
        => await _settingsHandler.MassPresetInstall_ClickAsync(Content.XamlRoot);

    private async void BrowseScreenshotPath_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = await PickFolderAsync(ScreenshotPathBox.Text?.Trim());
            if (!string.IsNullOrEmpty(folder))
            {
                ScreenshotPathBox.Text = folder;
                ViewModel.Settings.ScreenshotPath = folder;
                ViewModel.SaveSettingsPublic();
            }
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainWindow.BrowseScreenshotPath_Click] Folder picker failed — {ex.Message}");
        }
    }

    private void OpenScreenshotFolder_Click(object sender, RoutedEventArgs e)
    {
        var path = ScreenshotPathBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(path) || !System.IO.Directory.Exists(path))
        {
            _crashReporter.Log($"[MainWindow.OpenScreenshotFolder_Click] Path does not exist: '{path}'");
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainWindow.OpenScreenshotFolder_Click] Failed to open folder — {ex.Message}");
        }
    }

    private void SettingsBack_Click(object sender, RoutedEventArgs e)
        => _settingsHandler.SettingsBack_Click(sender, e);

    private void AboutButton_Click(object sender, RoutedEventArgs e)
        => ViewModel.NavigateToAboutCommand.Execute(null);

    private void AboutBack_Click(object sender, RoutedEventArgs e)
        => ViewModel.NavigateToGameViewCommand.Execute(null);

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

        var nameBox = new TextBox { PlaceholderText = "Filter name", Width = 350 };
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

        var result = await dialog.ShowAsync();
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
        // Ask for game name
        var nameBox = new TextBox { PlaceholderText = "Game name (e.g. Cyberpunk 2077)", Width = 350 };
        var nameDialog = new ContentDialog
        {
            Title           = "➕ Add Game Manually",
            Content         = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = "Enter the game name exactly as it appears on the wiki mod list:", TextWrapping = TextWrapping.Wrap, Foreground = Brush(ResourceKeys.TextSecondaryBrush) },
                    nameBox
                }
            },
            PrimaryButtonText   = "Pick Folder →",
            CloseButtonText     = "Cancel",
            XamlRoot            = Content.XamlRoot,
            Background          = Brush(ResourceKeys.SurfaceToolbarBrush),
            RequestedTheme      = ElementTheme.Dark,
        };
        var result = await nameDialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        var gameName = nameBox.Text.Trim();
        if (string.IsNullOrEmpty(gameName)) return;
        _crashReporter.Log($"[MainWindow.AddGameButton_Click] Adding game: {gameName}");

        // Pick the game folder
        var folder = await PickFolderAsync();
        if (folder == null) return;

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

    private void FavouriteButton_Click(object sender, RoutedEventArgs e)
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

    private void InstallButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.InstallButton_Click(sender, e);

    private void Install64Button_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.Install64Button_Click(sender, e);

    private void Install32Button_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.Install32Button_Click(sender, e);

    private async Task EnsurePathAndInstall(GameCardViewModel card, Func<Task> installAction)
        => await _installEventHandler.EnsurePathAndInstall(card, installAction);

    private void UninstallButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.UninstallButton_Click(sender, e);

    private void InstallRsButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.InstallRsButton_Click(sender, e);

    private void UninstallRsButton_Click(object sender, RoutedEventArgs e)
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

                var result = await warningDialog.ShowAsync();

                if (result != ContentDialogResult.Primary)
                    return; // Req 10.5: Cancel — don't persist flag, don't open manager

                // Req 10.4: Persist dismissal flag so warning is not shown again
                ViewModel.Settings.AddonWarningDismissed = true;
                ViewModel.SaveSettingsPublic();
            }

            // Use the ViewModel's AddonPackService (initialized on startup)
            var addonService = ViewModel.AddonPackServiceInstance;
            await addonService.EnsureLatestAsync();
            await AddonManagerDialog.ShowAsync(Content.XamlRoot, addonService,
                ViewModel.Settings.EnabledGlobalAddons,
                () => { ViewModel.SaveSettingsPublic(); ViewModel.DeployAllAddons(); });
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainWindow.ReShadeAddonsButton_Click] Failed — {ex.Message}");
        }
    }

    // ── Update All handlers ──────────────────────────────────────────────────

    private async void UpdateAllButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.UpdateAllReShadeAsync();
        await ViewModel.UpdateAllRenoDxAsync();
        await ViewModel.UpdateAllUlAsync();
        await ViewModel.UpdateAllDcAsync();
        await ViewModel.UpdateAllOsAsync();
        await ViewModel.UpdateAllRefAsync();
    }

    private async void UpdateAllRenoDx_Click(object sender, RoutedEventArgs e)
        => await ViewModel.UpdateAllRenoDxAsync();

    private async void UpdateAllReShade_Click(object sender, RoutedEventArgs e)
        => await ViewModel.UpdateAllReShadeAsync();

    private void InstallUlButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.InstallUlButton_Click(sender, e);

    private void UninstallUlButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.UninstallUlButton_Click(sender, e);

    private void InstallDcButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.InstallDcButton_Click(sender, e);

    private void UninstallDcButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.UninstallDcButton_Click(sender, e);

    private void InstallOsButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.InstallOsButton_Click(sender, e);

    private void UninstallOsButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.UninstallOsButton_Click(sender, e);

    private void InstallRefButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.InstallRefButton_Click(sender, e);

    private void UninstallRefButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.UninstallRefButton_Click(sender, e);

    private void UlIniButton_Click(object sender, RoutedEventArgs e)
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

    private void DcIniButton_Click(object sender, RoutedEventArgs e)
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

    private void CopyOsIniButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.CopyOsIniButton_Click(sender, e);

    private async void DetailOsStatus_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var card = ViewModel.SelectedGame;
        if (card == null) return;

        if (card.IsOsInstalled)
            await Windows.System.Launcher.LaunchUriAsync(
                new Uri("https://github.com/optiscaler/OptiScaler/wiki"));
    }

    private async void DetailUlStatus_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var card = ViewModel.SelectedGame;
        if (card == null) return;

        if (card.UlStatus == Models.GameStatus.UpdateAvailable && ViewModel.LatestUlReleasePageUrl != null)
            await Windows.System.Launcher.LaunchUriAsync(new Uri(ViewModel.LatestUlReleasePageUrl));
        else if (card.UlStatus == Models.GameStatus.Installed || card.UlStatus == Models.GameStatus.UpdateAvailable)
            await Windows.System.Launcher.LaunchUriAsync(
                new Uri("https://github.com/RankFTW/ReLimiter?tab=readme-ov-file#relimiter--comprehensive-feature-guide"));
    }

    private async void DetailDcStatus_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var card = ViewModel.SelectedGame;
        if (card == null) return;

        if (card.IsDcInstalled)
            await Windows.System.Launcher.LaunchUriAsync(
                new Uri("https://github.com/pmnoxx/display-commander/releases/tag/latest_build"));
    }

    private async void DetailRsStatus_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (ViewModel.SelectedGame?.RsStatus == Models.GameStatus.Installed)
            await Windows.System.Launcher.LaunchUriAsync(new Uri("https://reshade.me"));
    }

    private async void DetailRefStatus_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (ViewModel.SelectedGame?.IsRefInstalled == true)
            await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/praydog/REFramework-nightly/releases"));
    }

    private async void DetailRdxStatus_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var card = ViewModel.SelectedGame;
        if (card?.IsRdxInstalled == true)
        {
            var url = !string.IsNullOrEmpty(card.NameUrl)
                ? card.NameUrl
                : "https://github.com/clshortfuse/renodx/wiki/Mods";
            await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
        }
    }

    private void LumaToggle_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.LumaToggle_Click(sender, e);

    // ── Shared cursor handlers for clickable link text ────────────────────────────
    private static readonly Microsoft.UI.Input.InputCursor _handCursor =
        Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
    private static readonly Microsoft.UI.Input.InputCursor _arrowCursor =
        Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Arrow);

    private void LinkText_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is TextBlock tb && tb.TextDecorations == Windows.UI.Text.TextDecorations.Underline)
        {
            var prop = typeof(UIElement).GetProperty("ProtectedCursor",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            prop?.SetValue(tb, _handCursor);
        }
    }

    private void LinkText_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe)
        {
            var prop = typeof(UIElement).GetProperty("ProtectedCursor",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            prop?.SetValue(fe, _arrowCursor);
        }
    }

    private void SwitchToLumaButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.SwitchToLumaButton_Click(sender, e);

    private void InstallLumaButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.InstallLumaButton_Click(sender, e);

    private void UninstallLumaButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.UninstallLumaButton_Click(sender, e);

    private void UeExtendedFlyoutItem_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.UeExtendedFlyoutItem_Click(sender, e);

    internal async Task ShowUeExtendedWarningAsync(GameCardViewModel card)
        => await _dialogService.ShowUeExtendedWarningAsync(card);

    private void HideButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetCardFromSender(sender) is { } card)
            ViewModel.ToggleHideGameCommand.Execute(card);
    }

    internal async void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var card = GetCardFromSender(sender);
        if (card == null) return;
        var suggestedPath = card.InstallPath is { Length: > 0 } p && Directory.Exists(p) ? p
                          : card.DetectedGame?.InstallPath is { Length: > 0 } dp && Directory.Exists(dp) ? dp
                          : null;
        var folder = await PickFolderAsync(suggestedPath);
        if (folder != null)
        {
            card.InstallPath = folder;
            if (card.DetectedGame != null)
                card.DetectedGame.InstallPath = folder;
            // Persist the override so it survives Refresh / app restart
            ViewModel.SetFolderOverride(card.GameName, folder);
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var card = GetCardFromSender(sender);
        if (card == null || string.IsNullOrEmpty(card.InstallPath)) return;
        if (System.IO.Directory.Exists(card.InstallPath))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(card.InstallPath) { UseShellExecute = true });
    }

    internal void RemoveManualGame_Click(object sender, RoutedEventArgs e)
    {
        var card = GetCardFromSender(sender);
        if (card == null) return;

        if (card.IsManuallyAdded)
        {
            // Manual game — remove it entirely
            ViewModel.RemoveManualGameCommand.Execute(card);
        }
        else
        {
            // Auto-detected game — reset folder to original detected path
            ViewModel.ResetFolderOverride(card);
        }
    }

    // ── Scroll restore helpers ────────────────────────────────────────────────────

    private async Task RefreshWithScrollRestore()
    {
        var selectedName = (GameList.SelectedItem as GameCardViewModel)?.GameName;

        await ViewModel.RefreshAsync();

        RestoreScrollAndSelection(selectedName);
    }

    private async Task FullRefreshWithScrollRestore()
    {
        var selectedName = (GameList.SelectedItem as GameCardViewModel)?.GameName;

        await ViewModel.FullRefreshAsync();

        RestoreScrollAndSelection(selectedName);
    }

    private void RestoreScrollAndSelection(string? selectedName)
    {
        // Restore game list selection
        if (!string.IsNullOrEmpty(selectedName))
        {
            _pendingReselect = selectedName;
            DispatcherQueue.TryEnqueue(TryRestoreSelection);
        }
    }
}
