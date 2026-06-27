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

            AuxInstallService.CopyRsPresetIniIfPresent(card.InstallPath);
            bool presetDeployed = File.Exists(AuxInstallService.RsPresetIniPath);
            card.LumaActionMessage = presetDeployed
                ? "✅ reshade.ini merged & ReShadePreset.ini copied."
                : "✅ reshade.ini merged into game folder.";
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

    // ── Component Cog Button Handlers ────────────────────────────────────────────

    private async void RsCogButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;
        if (string.IsNullOrEmpty(card.InstallPath)) return;

        var content = new StackPanel { Spacing = 8 };

        // Deploy reshade.ini
        var deployIniBtn = new Button
        {
            Content = "Deploy reshade.ini",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush),
            Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 7, 12, 7), FontSize = 12,
        };
        deployIniBtn.Click += (s, ev) =>
        {
            try
            {
                var screenshotPath = BuildScreenshotSavePath(card.GameName);
                var overlayHotkey = ViewModel.Settings.OverlayHotkey;
                var screenshotHotkey = ViewModel.Settings.ScreenshotHotkey;
                if (card.RequiresVulkanInstall)
                {
                    AuxInstallService.MergeRsVulkanIni(card.InstallPath, card.GameName, screenshotPath, overlayHotkey, screenshotHotkey);
                    VulkanFootprintService.Create(card.InstallPath);
                    ViewModel.DeployShadersForCard(card.GameName);
                }
                else
                    AuxInstallService.MergeRsIni(card.InstallPath, screenshotPath, overlayHotkey, screenshotHotkey);

                if (card.UseUeExtended && card.Status == GameStatus.Installed)
                    AuxInstallService.ApplyRenoDxNativeHdrSettings(card.InstallPath);

                card.RsActionMessage = "✅ reshade.ini deployed.";
            }
            catch (Exception ex) { card.RsActionMessage = $"❌ {ex.Message}"; }
        };
        content.Children.Add(deployIniBtn);

        // Deploy ReShadePreset.ini
        var deployPresetBtn = new Button
        {
            Content = "Deploy ReShadePreset.ini",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush),
            Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 7, 12, 7), FontSize = 12,
            IsEnabled = File.Exists(AuxInstallService.RsPresetIniPath),
        };
        deployPresetBtn.Click += (s, ev) =>
        {
            try
            {
                AuxInstallService.CopyRsPresetIniIfPresent(card.InstallPath);
                card.RsActionMessage = "✅ ReShadePreset.ini deployed.";
            }
            catch (Exception ex) { card.RsActionMessage = $"❌ {ex.Message}"; }
        };
        if (!File.Exists(AuxInstallService.RsPresetIniPath))
            ToolTipService.SetToolTip(deployPresetBtn, "No ReShadePreset.ini found in RHI config folder");
        content.Children.Add(deployPresetBtn);

        // Open reshade.ini
        var openIniBtn = new Button
        {
            Content = "Open reshade.ini",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.BorderStrongBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 7, 12, 7), FontSize = 12,
            IsEnabled = File.Exists(Path.Combine(card.InstallPath, "reshade.ini")),
        };
        openIniBtn.Click += async (s, ev) =>
        {
            var iniPath = Path.Combine(card.InstallPath, "reshade.ini");
            if (File.Exists(iniPath))
                await Windows.System.Launcher.LaunchUriAsync(new Uri(iniPath));
        };
        content.Children.Add(openIniBtn);

        // Open reshade.log
        var openLogBtn = new Button
        {
            Content = "Open reshade.log",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.BorderStrongBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 7, 12, 7), FontSize = 12,
            IsEnabled = File.Exists(Path.Combine(card.InstallPath, "ReShade.log")),
        };
        openLogBtn.Click += async (s, ev) =>
        {
            var logPath = Path.Combine(card.InstallPath, "ReShade.log");
            if (File.Exists(logPath))
                await Windows.System.Launcher.LaunchUriAsync(new Uri(logPath));
        };
        content.Children.Add(openLogBtn);

        // Copy reshade.log to clipboard
        var copyLogBtn = new Button
        {
            Content = "Copy reshade.log to clipboard",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.BorderStrongBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 7, 12, 7), FontSize = 12,
            IsEnabled = File.Exists(Path.Combine(card.InstallPath, "ReShade.log")),
        };
        copyLogBtn.Click += (s, ev) =>
        {
            var logPath = Path.Combine(card.InstallPath, "ReShade.log");
            if (File.Exists(logPath))
            {
                try
                {
                    var logContent = File.ReadAllText(logPath);
                    var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
                    dataPackage.SetText(logContent);
                    Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
                    card.RsActionMessage = "✅ reshade.log copied to clipboard.";
                    card.FadeMessage(m => card.RsActionMessage = m, card.RsActionMessage);
                }
                catch (Exception ex) { card.RsActionMessage = $"❌ {ex.Message}"; }
            }
        };
        content.Children.Add(copyLogBtn);

        var dialog = new ContentDialog
        {
            Title = "ReShade Settings",
            Content = content,
            CloseButtonText = "Close",
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        await DialogService.ShowSafeAsync(dialog);
    }

    private async void RdxCogButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;
        if (string.IsNullOrEmpty(card.InstallPath)) return;

        var iniPath = Path.Combine(card.InstallPath, "reshade.ini");
        var content = new StackPanel { Spacing = 8 };

        // UE-Extended toggle (keep existing functionality)
        if (card.UeExtendedToggleVisibility == Visibility.Visible)
        {
            var uePanel = new StackPanel { Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal, Spacing = 8 };
            uePanel.Children.Add(new TextBlock { Text = "UE-Extended", FontSize = 12, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush), VerticalAlignment = VerticalAlignment.Center });
            var ueCombo = new ComboBox { FontSize = 12, MinWidth = 80 };
            ueCombo.Items.Add("Off");
            ueCombo.Items.Add("On");
            ueCombo.SelectedIndex = card.UseUeExtended ? 1 : 0;
            ueCombo.SelectionChanged += (s, ev) =>
            {
                bool enable = ueCombo.SelectedIndex == 1;
                if (enable != card.UseUeExtended)
                    ViewModel.ToggleUeExtended(card);
            };
            uePanel.Children.Add(ueCombo);
            content.Children.Add(uePanel);
        }

        // ── Upgrade settings from [renodx] section ────────────────────────────
        if (File.Exists(iniPath))
        {
            var ini = AuxInstallService.ParseIni(File.ReadAllLines(iniPath));
            if (ini.TryGetValue("renodx", out var renodxSection))
            {
                // Collect Upgrade_* and Set_Path keys
                var upgradeKeys = renodxSection
                    .Where(kv => kv.Key.StartsWith("Upgrade_", StringComparison.OrdinalIgnoreCase)
                              || kv.Key.Equals("Set_Path", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (upgradeKeys.Count > 0)
                {
                    content.Children.Add(new TextBlock
                    {
                        Text = "Compatibility Settings",
                        FontSize = 13,
                        Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
                        Margin = new Thickness(0, 8, 0, 0),
                    });

                    var settingsGrid = new Grid { ColumnSpacing = 12, RowSpacing = 6 };
                    settingsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    settingsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130, GridUnitType.Pixel) });

                    for (int i = 0; i < upgradeKeys.Count; i++)
                    {
                        var kv = upgradeKeys[i];
                        settingsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                        // Label
                        var label = new TextBlock
                        {
                            Text = kv.Key,
                            FontSize = 11,
                            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
                            VerticalAlignment = VerticalAlignment.Center,
                        };
                        Grid.SetRow(label, i);
                        Grid.SetColumn(label, 0);
                        settingsGrid.Children.Add(label);

                        // ComboBox
                        var combo = new ComboBox { FontSize = 11, MinWidth = 120, HorizontalAlignment = HorizontalAlignment.Stretch };
                        bool isSetPath = kv.Key.Equals("Set_Path", StringComparison.OrdinalIgnoreCase);

                        if (isSetPath)
                        {
                            combo.Items.Add("Off");
                            combo.Items.Add("On");
                        }
                        else
                        {
                            combo.Items.Add("Off");
                            combo.Items.Add("Output size");
                            combo.Items.Add("Output ratio");
                            combo.Items.Add("Any size");
                        }

                        int.TryParse(kv.Value, out var currentVal);
                        combo.SelectedIndex = isSetPath
                            ? (currentVal >= 0 && currentVal <= 1 ? currentVal : 0)
                            : (currentVal >= 0 && currentVal <= 3 ? currentVal : 0);

                        // Capture key for closure
                        var capturedKey = kv.Key;
                        combo.SelectionChanged += (s, ev) =>
                        {
                            if (combo.SelectedIndex < 0) return;
                            renodxSection[capturedKey] = combo.SelectedIndex.ToString();
                            try { AuxInstallService.WriteIni(iniPath, ini); }
                            catch (Exception ex) { card.ActionMessage = $"❌ {ex.Message}"; }
                        };

                        Grid.SetRow(combo, i);
                        Grid.SetColumn(combo, 1);
                        settingsGrid.Children.Add(combo);
                    }

                    content.Children.Add(settingsGrid);
                }
            }
        }
        else
        {
            content.Children.Add(new TextBlock
            {
                Text = "No reshade.ini found in game folder.",
                FontSize = 11,
                Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
                FontStyle = Windows.UI.Text.FontStyle.Italic,
            });
        }

        var dialog = new ContentDialog
        {
            Title = "RenoDX Settings",
            Content = new ScrollViewer { Content = content, MaxHeight = 500, Padding = new Thickness(0, 0, 16, 0) },
            CloseButtonText = "Close",
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 600.0;
        await DialogService.ShowSafeAsync(dialog);
        // Refresh panel after dialog closes (UE toggle may have changed state)
        _detailPanelBuilder?.UpdateDetailComponentRows(card);
    }

    private async void UlCogButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;
        var content = new StackPanel { Spacing = 12 };
        var deployBtn = new Button
        {
            Content = "Deploy relimiter.ini",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush),
            Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 7, 12, 7), FontSize = 12,
        };
        deployBtn.Click += (s, ev) =>
        {
            if (string.IsNullOrEmpty(card.InstallPath)) return;
            try
            {
                AuxInstallService.CopyUlIni(card.InstallPath);
                card.UlActionMessage = "✅ relimiter.ini copied to game folder.";
            }
            catch (Exception ex) { card.UlActionMessage = $"❌ {ex.Message}"; }
        };
        content.Children.Add(deployBtn);

        var dialog = new ContentDialog
        {
            Title = "ReLimiter Settings",
            Content = content,
            CloseButtonText = "Close",
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        await DialogService.ShowSafeAsync(dialog);
    }

    private async void DcCogButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;
        var content = new StackPanel { Spacing = 12 };
        var deployBtn = new Button
        {
            Content = "Deploy DisplayCommander.ini",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush),
            Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 7, 12, 7), FontSize = 12,
        };
        deployBtn.Click += (s, ev) =>
        {
            if (string.IsNullOrEmpty(card.InstallPath)) return;
            try
            {
                AuxInstallService.CopyDcIni(card.InstallPath);
                card.DcActionMessage = "✅ DisplayCommander.ini copied to game folder.";
                card.FadeMessage(m => card.DcActionMessage = m, card.DcActionMessage);
            }
            catch (Exception ex) { card.DcActionMessage = $"❌ {ex.Message}"; }
        };
        content.Children.Add(deployBtn);

        var dialog = new ContentDialog
        {
            Title = "Display Commander Settings",
            Content = content,
            CloseButtonText = "Close",
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        await DialogService.ShowSafeAsync(dialog);
    }

    private async void OsCogButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;
        var content = new StackPanel { Spacing = 12 };
        var deployBtn = new Button
        {
            Content = "Deploy OptiScaler.ini",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush),
            Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 7, 12, 7), FontSize = 12,
        };
        deployBtn.Click += (s, ev) => _installEventHandler.CopyOsIniButton_Click(sender, e);
        content.Children.Add(deployBtn);

        var dialog = new ContentDialog
        {
            Title = "OptiScaler Settings",
            Content = content,
            CloseButtonText = "Close",
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        await DialogService.ShowSafeAsync(dialog);
    }

    private async void DxvkCogButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;
        var content = new StackPanel { Spacing = 12 };
        var deployBtn = new Button
        {
            Content = "Deploy dxvk.conf",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush),
            Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 7, 12, 7), FontSize = 12,
        };
        deployBtn.Click += (s, ev) => ViewModel.CopyDxvkConf(card);
        content.Children.Add(deployBtn);

        var dialog = new ContentDialog
        {
            Title = "DXVK Settings",
            Content = content,
            CloseButtonText = "Close",
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        await DialogService.ShowSafeAsync(dialog);
    }

    private void SupportGuide_Click(object sender, RoutedEventArgs e)
    {
        _ = Windows.System.Launcher.LaunchUriAsync(
            new Uri("https://github.com/RankFTW/RHI/blob/main/docs/DETAILED_GUIDE.md"));
    }

    private void SupportKofi_Click(object sender, RoutedEventArgs e)
    {
        _ = Windows.System.Launcher.LaunchUriAsync(
            new Uri("https://ko-fi.com/rankftw"));
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

    private void ViewGrid_Click(object sender, RoutedEventArgs e)
        => SwitchToView(ViewLayout.Grid);

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
            case ViewLayout.Grid:
                RebuildCardGrid();
                break;
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
            case ViewLayout.Grid:
                RebuildCardGrid();
                break;
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

    // ── Settings handlers ─────────────────────────────────────────────────────────

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
        => _settingsHandler.SettingsButton_Click(sender, e);

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

    private void CopyLogsArchive_Click(object sender, RoutedEventArgs e)
        => _settingsHandler.CopyLogsArchive_Click(sender, e);

    private void AdminModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => _settingsHandler.AdminModeCombo_SelectionChanged(sender, e);

    private void OpenAppDataFolder_Click(object sender, RoutedEventArgs e)
        => _settingsHandler.OpenAppDataFolder_Click(sender, e);

    private void OpenCustomFolder_Click(object sender, RoutedEventArgs e)
        => _settingsHandler.OpenCustomFolder_Click(sender, e);

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

    private void ApplyReShadeHotkeys_Click(object sender, RoutedEventArgs e)
        => _settingsHandler.ApplyReShadeHotkeys_Click(sender, e);

    private void ScreenshotHotkeyBox_PreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        => _settingsHandler.ScreenshotHotkeyBox_PreviewKeyDown(sender, e);

    private void ApplyScreenshotHotkey_Click(object sender, RoutedEventArgs e)
        => _settingsHandler.ApplyScreenshotHotkey_Click(sender, e);

    private void ResetScreenshotHotkey_Click(object sender, RoutedEventArgs e)
        => _settingsHandler.ResetScreenshotHotkey_Click(sender, e);

    private void UlHotkeyBox_PreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        => _settingsHandler.UlHotkeyBox_PreviewKeyDown(sender, e);

    private void ApplyUlOsdHotkey_Click(object sender, RoutedEventArgs e)
        => _settingsHandler.ApplyUlOsdHotkey_Click(sender, e);

    private void UlSharedPresetsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => _settingsHandler.UlSharedPresetsCombo_SelectionChanged(sender, e);

    private void UlDlssHooksCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => _settingsHandler.UlDlssHooksCombo_SelectionChanged(sender, e);

    private void OsHotkeyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => _settingsHandler.OsHotkeyCombo_SelectionChanged(sender, e);

    private void OsGpuCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => _settingsHandler.OsGpuCombo_SelectionChanged(sender, e);

    private void OsDlssInputsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => _settingsHandler.OsDlssInputsCombo_SelectionChanged(sender, e);

    private async void GlobalUpdateInclusion_Click(object sender, RoutedEventArgs e)
        => await _settingsHandler.GlobalUpdateInclusion_ClickAsync(sender, e);

    private void CacheAllShadersToggle_Toggled(object sender, RoutedEventArgs e)
        => _settingsHandler.CacheAllShadersToggle_Toggled(sender, e);

    private void ApplyOsHotkey_Click(object sender, RoutedEventArgs e)
        => _settingsHandler.ApplyOsHotkey_Click(sender, e);

    private void MassDeployRsIni_Click(object sender, RoutedEventArgs e)
        => _massDeployHandler.MassDeployRsIni_Click(sender, e);

    private void MassDeployUlIni_Click(object sender, RoutedEventArgs e)
        => _massDeployHandler.MassDeployUlIni_Click(sender, e);

    private void MassDeployDcIni_Click(object sender, RoutedEventArgs e)
        => _massDeployHandler.MassDeployDcIni_Click(sender, e);

    private void MassDeployOsIni_Click(object sender, RoutedEventArgs e)
        => _massDeployHandler.MassDeployOsIni_Click(sender, e);

    private async void MassPresetInstall_Click(object sender, RoutedEventArgs e)
        => await _massDeployHandler.MassPresetInstall_ClickAsync(Content.XamlRoot);

    private async void MassDlssDeploy_Click(object sender, RoutedEventArgs e)
    {
        var dlssService = App.Services.GetRequiredService<IDlssStreamlineService>();
        var dialog = new MassDlssDeployDialog(ViewModel, dlssService, Content.XamlRoot, onComplete: () =>
        {
            // Rebuild the detail panel for the currently selected game so versions update
            if (ViewModel.SelectedGame is { } selectedCard)
            {
                PopulateDetailPanel(selectedCard);
                BuildOverridesPanel(selectedCard);
            }
        });
        await dialog.ShowAsync();
    }

    private async void DlssDefaults_Click(object sender, RoutedEventArgs e)
    {
        var dlssService = App.Services.GetRequiredService<IDlssStreamlineService>();
        var presetService = App.Services.GetRequiredService<DlssPresetService>();
        await DlssDefaultsDialog.ShowAsync(ViewModel, dlssService, presetService, Content.XamlRoot);
        RefreshDlssDefaultsSummary();

        // Rebuild the detail panel so Quick Apply button reflects new defaults state
        if (ViewModel.SelectedGame is { } selectedCard && ViewModel.CurrentViewLayout == Models.ViewLayout.Detail)
        {
            PopulateDetailPanel(selectedCard);
            BuildOverridesPanel(selectedCard);
        }
    }

    internal void RefreshDlssDefaultsSummary()
    {
        var s = ViewModel.Settings;
        DlssDefaultsSummaryPanel.Children.Clear();

        bool hasAny = !string.IsNullOrEmpty(s.DefaultDlssVersion) || !string.IsNullOrEmpty(s.DefaultDlssdVersion)
            || !string.IsNullOrEmpty(s.DefaultDlssgVersion) || !string.IsNullOrEmpty(s.DefaultStreamlineVersion)
            || s.DefaultSrPreset != 0 || s.DefaultRrPreset != 0 || s.DefaultFgPreset != 0
            || s.DefaultSrRenderScale != 0 || s.DefaultRrRenderScale != 0;

        if (!hasAny)
        {
            DlssDefaultsSummaryPanel.Children.Add(new TextBlock
            {
                Text = "No defaults configured yet.",
                FontSize = 11,
                Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
            });
            return;
        }

        // Build a 4-column grid matching the dialog layout
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var srCol = new StackPanel { Spacing = 2 };
        srCol.Children.Add(new TextBlock { Text = "DLSS", FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush) });
        if (!string.IsNullOrEmpty(s.DefaultDlssVersion)) srCol.Children.Add(MakeSummaryText(s.DefaultDlssVersion));
        if (s.DefaultSrPreset != 0) srCol.Children.Add(MakeSummaryText($"Preset {DlssPresetService.SrPresets.FirstOrDefault(p => p.Value == s.DefaultSrPreset).Name ?? "?"}"));
        if (s.DefaultSrRenderScale != 0) srCol.Children.Add(MakeSummaryText($"{s.DefaultSrRenderScale}%"));
        Grid.SetColumn(srCol, 0);
        grid.Children.Add(srCol);

        grid.Children.Add(MakeSummaryDivider(1));

        var rrCol = new StackPanel { Spacing = 2 };
        rrCol.Children.Add(new TextBlock { Text = "RR", FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush) });
        if (!string.IsNullOrEmpty(s.DefaultDlssdVersion)) rrCol.Children.Add(MakeSummaryText(s.DefaultDlssdVersion));
        if (s.DefaultRrPreset != 0) rrCol.Children.Add(MakeSummaryText($"Preset {DlssPresetService.RrPresets.FirstOrDefault(p => p.Value == s.DefaultRrPreset).Name ?? "?"}"));
        if (s.DefaultRrRenderScale != 0) rrCol.Children.Add(MakeSummaryText($"{s.DefaultRrRenderScale}%"));
        Grid.SetColumn(rrCol, 2);
        grid.Children.Add(rrCol);

        grid.Children.Add(MakeSummaryDivider(3));

        var fgCol = new StackPanel { Spacing = 2 };
        fgCol.Children.Add(new TextBlock { Text = "FG", FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush) });
        if (!string.IsNullOrEmpty(s.DefaultDlssgVersion)) fgCol.Children.Add(MakeSummaryText(s.DefaultDlssgVersion));
        if (s.DefaultFgPreset != 0) fgCol.Children.Add(MakeSummaryText($"Preset {DlssPresetService.FgPresets.FirstOrDefault(p => p.Value == s.DefaultFgPreset).Name ?? "?"}"));
        Grid.SetColumn(fgCol, 4);
        grid.Children.Add(fgCol);

        grid.Children.Add(MakeSummaryDivider(5));

        var slCol = new StackPanel { Spacing = 2 };
        slCol.Children.Add(new TextBlock { Text = "SL", FontSize = 10, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush) });
        if (!string.IsNullOrEmpty(s.DefaultStreamlineVersion)) slCol.Children.Add(MakeSummaryText(s.DefaultStreamlineVersion));
        Grid.SetColumn(slCol, 6);
        grid.Children.Add(slCol);

        DlssDefaultsSummaryPanel.Children.Add(grid);
    }

    private static TextBlock MakeSummaryText(string text) => new TextBlock
    {
        Text = text,
        FontSize = 10,
        Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
    };

    private static Border MakeSummaryDivider(int column)
    {
        var div = new Border { Width = 1, Background = UIFactory.Brush(ResourceKeys.BorderSubtleBrush), VerticalAlignment = VerticalAlignment.Stretch };
        Grid.SetColumn(div, column);
        return div;
    }

    internal bool _dlssIndicatorInitializing = true;

    internal bool _shaderCacheComboInit = true;

    private void ShaderCacheSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_shaderCacheComboInit) return;
        if (sender is not ComboBox combo || combo.SelectedIndex < 0) return;
        var options = DlssPresetService.ShaderCacheSizeOptions;
        if (combo.SelectedIndex < options.Length)
        {
            var presetService = App.Services.GetRequiredService<DlssPresetService>();
            presetService.SetShaderCacheSize(options[combo.SelectedIndex].Value);
        }
    }

    private void ShaderPrecompileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_shaderCacheComboInit) return;
        if (sender is not ComboBox combo || combo.SelectedIndex < 0) return;
        var options = DlssPresetService.ShaderPrecompileOptions;
        if (combo.SelectedIndex < options.Length)
        {
            var presetService = App.Services.GetRequiredService<DlssPresetService>();
            presetService.SetShaderPrecompile(options[combo.SelectedIndex].Value);
        }
    }

    private void GSyncModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_shaderCacheComboInit) return;
        if (sender is not ComboBox combo || combo.SelectedIndex < 0) return;
        var options = DlssPresetService.GSyncModeOptions;
        if (combo.SelectedIndex < options.Length)
        {
            var presetService = App.Services.GetRequiredService<DlssPresetService>();
            presetService.SetGSyncMode(options[combo.SelectedIndex].Value);
        }
    }

    private void PreferredRefreshRateCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_shaderCacheComboInit) return;
        if (sender is not ComboBox combo || combo.SelectedIndex < 0) return;
        var options = DlssPresetService.PreferredRefreshRateOptions;
        if (combo.SelectedIndex < options.Length)
        {
            var presetService = App.Services.GetRequiredService<DlssPresetService>();
            presetService.SetPreferredRefreshRate(options[combo.SelectedIndex].Value);
        }
    }

    private void GlobalReBarEnableCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_shaderCacheComboInit) return;
        if (sender is not ComboBox combo || combo.SelectedIndex < 0) return;
        var presetService = App.Services.GetRequiredService<DlssPresetService>();
        bool enabled = combo.SelectedIndex == 1; // 0=Off, 1=On
        presetService.SetGlobalReBarEnabled(enabled);
        // Update size combo enabled state and reset to default when off
        GlobalReBarSizeCombo.IsEnabled = enabled;
        GlobalReBarSizeCombo.Opacity = enabled ? 1.0 : 0.4;
        if (!enabled)
            GlobalReBarSizeCombo.SelectedIndex = 1; // Reset to 1GB (Default)
        // Force detail panel rebuild if a game is selected
        if (ViewModel.SelectedGame != null)
            DispatcherQueue?.TryEnqueue(() => _detailPanelBuilder?.BuildOverridesPanel(ViewModel.SelectedGame));
    }

    private void GlobalReBarSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_shaderCacheComboInit) return;
        if (sender is not ComboBox combo || combo.SelectedIndex < 0) return;
        var options = DlssPresetService.ReBarSizeLimits;
        if (combo.SelectedIndex < options.Length)
        {
            var presetService = App.Services.GetRequiredService<DlssPresetService>();
            presetService.SetGlobalReBarSizeLimit(options[combo.SelectedIndex].Value);
        }
        // Force detail panel rebuild if a game is selected
        if (ViewModel.SelectedGame != null)
            DispatcherQueue?.TryEnqueue(() => _detailPanelBuilder?.BuildOverridesPanel(ViewModel.SelectedGame));
    }

    private void GlobalVSyncCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_shaderCacheComboInit) return;
        if (sender is not ComboBox combo || combo.SelectedIndex < 0) return;
        var options = DlssPresetService.VSyncModeOptions;
        if (combo.SelectedIndex < options.Length)
        {
            var presetService = App.Services.GetRequiredService<DlssPresetService>();
            presetService.SetGlobalVSyncMode(options[combo.SelectedIndex].Value);
        }
        // Force detail panel rebuild if a game is selected
        if (ViewModel.SelectedGame != null)
            DispatcherQueue?.TryEnqueue(() => _detailPanelBuilder?.BuildOverridesPanel(ViewModel.SelectedGame));
    }

    private async void ExportNvidiaProfiles_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var presetService = App.Services.GetRequiredService<DlssPresetService>();
            var games = ViewModel.AllCards
                .Where(c => !string.IsNullOrEmpty(c.InstallPath))
                .Select(c => (c.GameName, c.InstallPath))
                .ToList();

            // Run on background thread — FindProfile does exe scanning per game
            var data = await Task.Run(() => presetService.ExportProfiles(games));

            if (data.Count == 0)
            {
                await DialogService.ShowSafeAsync(new ContentDialog
                {
                    Title = "Export",
                    Content = "No custom profile settings found to export.",
                    CloseButtonText = "OK",
                    XamlRoot = Content.XamlRoot,
                    RequestedTheme = ElementTheme.Dark,
                });
                return;
            }

            var json = System.Text.Json.JsonSerializer.Serialize(data, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            var path = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RHI", "nvidia_profiles_backup.json");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, json);

            await DialogService.ShowSafeAsync(new ContentDialog
            {
                Title = "Export Complete",
                Content = $"Exported {data.Count} profile(s) to:\n{path}",
                CloseButtonText = "OK",
                XamlRoot = Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            });
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[ExportNvidiaProfiles_Click] Error: {ex.GetType().Name}: {ex.Message}");
            await DialogService.ShowSafeAsync(new ContentDialog
            {
                Title = "Export Failed",
                Content = $"An error occurred during export:\n{ex.Message}",
                CloseButtonText = "OK",
                XamlRoot = Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            });
        }
    }

    private async void ImportNvidiaProfiles_Click(object sender, RoutedEventArgs e)
    {
        var path = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RHI", "nvidia_profiles_backup.json");

        if (!System.IO.File.Exists(path))
        {
            await DialogService.ShowSafeAsync(new ContentDialog
            {
                Title = "Import",
                Content = $"No backup file found at:\n{path}\n\nExport profiles first.",
                CloseButtonText = "OK",
                XamlRoot = Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            });
            return;
        }

        var json = System.IO.File.ReadAllText(path);
        var data = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json);
        if (data == null || data.Count == 0) return;

        var presetService = App.Services.GetRequiredService<DlssPresetService>();
        var count = presetService.ImportProfiles(data);

        // Refresh settings page to reflect imported global values
        _settingsHandler.RefreshGlobalNvidiaSettings();

        await DialogService.ShowSafeAsync(new ContentDialog
        {
            Title = "Import Complete",
            Content = $"Imported {count} profile(s) from backup.",
            CloseButtonText = "OK",
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        });
    }

    private async void ResetAllNvidiaProfiles_Click(object sender, RoutedEventArgs e)
    {
        var confirmDialog = new ContentDialog
        {
            Title = "Reset all game profiles?",
            Content = new TextBlock
            {
                Text = "This will remove ALL per-game NVIDIA driver profile overrides (DLSS/Streamline versions, presets, render scales, ReBAR, VSync, Smooth Motion, Low Latency, Power Mode — everything) AND reset global settings (Shader Cache, G-Sync, Refresh Rate, ReBAR) to defaults.\n\nAll profiles will return to NVIDIA factory defaults. This cannot be undone.",
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                FontSize = 13,
            },
            PrimaryButtonText = "Reset All",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        var result = await DialogService.ShowSafeAsync(confirmDialog);
        if (result != ContentDialogResult.Primary) return;

        try
        {
            var presetSvc = ViewModel.DlssPresetServiceInstance;
            var gamesList = ViewModel.AllCards
                .Where(c => !string.IsNullOrEmpty(c.InstallPath))
                .Select(c => (c.GameName, c.InstallPath!))
                .ToList();

            // Show progress dialog
            var progressPanel = new StackPanel { Spacing = 8 };
            var progressRing = new ProgressRing { IsActive = true, Width = 20, Height = 20 };
            var progressText = new TextBlock { Text = "Preparing...", FontSize = 13, Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush) };
            var progressRow = new StackPanel { Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal, Spacing = 12 };
            progressRow.Children.Add(progressRing);
            progressRow.Children.Add(progressText);
            progressPanel.Children.Add(progressRow);

            var progressDialog = new ContentDialog
            {
                Title = "Resetting...",
                Content = progressPanel,
                XamlRoot = Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            };
            _ = DialogService.ShowSafeAsync(progressDialog);

            int resetCount = 0;
            await Task.Run(() =>
            {
                for (int i = 0; i < gamesList.Count; i++)
                {
                    var (gameName, installPath) = gamesList[i];
                    DispatcherQueue?.TryEnqueue(() =>
                        progressText.Text = $"[{i + 1}/{gamesList.Count}] {gameName}");
                    try
                    {
                        if (presetSvc.RestoreProfileDefaults(gameName, installPath))
                            resetCount++;
                    }
                    catch { }
                }

                // Reset global profile
                DispatcherQueue?.TryEnqueue(() =>
                    progressText.Text = "Resetting global settings...");
                presetSvc.ResetGlobalProfile();
            });

            progressDialog.Hide();

            // Refresh all cards so the detail panel reflects cleared presets
            foreach (var card in ViewModel.AllCards)
                card.NotifyAll();

            // Rebuild the detail panel for the selected game to refresh NVIDIA section
            if (ViewModel.SelectedGame != null)
            {
                PopulateDetailPanel(ViewModel.SelectedGame);
                _detailPanelBuilder.BuildOverridesPanel(ViewModel.SelectedGame);
            }

            // Re-initialize the Global Nvidia Settings combos to reflect cleared values
            _settingsHandler.RefreshGlobalNvidiaSettings();

            await DialogService.ShowSafeAsync(new ContentDialog
            {
                Title = "Profiles Reset",
                Content = $"Reset {resetCount} game profile(s) and global settings to factory defaults.",
                CloseButtonText = "OK",
                XamlRoot = Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            });
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[ResetAllNvidiaProfiles_Click] Error: {ex.GetType().Name}: {ex.Message}");
            await DialogService.ShowSafeAsync(new ContentDialog
            {
                Title = "Reset Failed",
                Content = $"Error: {ex.Message}",
                CloseButtonText = "OK",
                XamlRoot = Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            });
        }
    }

    private async void ClearNvidiaShaderCache_Click(object sender, RoutedEventArgs e)
    {
        var confirmDialog = new ContentDialog
        {
            Title = "Clear NVIDIA Shader Cache",
            Content = "This will permanently delete the NVIDIA DXCache and GLCache folders. This cannot be undone.\n\nAll games will need to rebuild their shader caches on next launch, which may cause brief stuttering or longer load times the first time. This can fix shader corruption, persistent stuttering, or graphical issues after driver updates.",
            PrimaryButtonText = "Clear",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };

        var result = await DialogService.ShowSafeAsync(confirmDialog);
        if (result != ContentDialogResult.Primary) return;

        int filesDeleted = 0;
        int filesFailed = 0;
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var cachePaths = new[]
        {
            Path.Combine(localAppData, "NVIDIA", "DXCache"),
            Path.Combine(localAppData, "NVIDIA", "GLCache"),
        };

        foreach (var path in cachePaths)
        {
            if (!Directory.Exists(path)) continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { File.Delete(file); filesDeleted++; }
                    catch { filesFailed++; }
                }
                // Try to remove empty subdirectories
                foreach (var dir in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
                {
                    try { Directory.Delete(dir); } catch { }
                }
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[ClearNvidiaShaderCache] Error enumerating '{path}': {ex.Message}");
            }
        }

        var message = filesFailed > 0
            ? $"Shader cache folders cleared. {filesDeleted} files deleted, {filesFailed} files skipped (in use by running games). Shaders will recompile on next game launch."
            : $"Shader cache folders cleared. {filesDeleted} files deleted. Shaders will recompile on next game launch.";

        await DialogService.ShowSafeAsync(new ContentDialog
        {
            Title = "Shader Cache Cleared",
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        });
    }

    private async void DlssIndicatorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_dlssIndicatorInitializing) return;
        if (sender is not ComboBox combo || combo.SelectedIndex < 0) return;

        // 0 = Enabled (0x400), 1 = Disabled (0x0)
        uint value = combo.SelectedIndex == 0 ? 0x400u : 0u;

        try
        {
            // Writing to HKLM requires admin — use a reg.exe process with elevation
            var regValue = value.ToString();
            var psi = new System.Diagnostics.ProcessStartInfo("reg.exe",
                $"add \"HKLM\\SOFTWARE\\NVIDIA Corporation\\Global\\NGXCore\" /v ShowDlssIndicator /t REG_DWORD /d {regValue} /f")
            {
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true,
            };
            var proc = System.Diagnostics.Process.Start(psi);
            if (proc != null)
            {
                await proc.WaitForExitAsync();
                _crashReporter.Log($"[DlssIndicatorCombo] Set ShowDlssIndicator to {value} (exit code: {proc.ExitCode})");
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // User cancelled UAC — revert the combo without re-triggering the handler
            _crashReporter.Log("[DlssIndicatorCombo] UAC cancelled — reverting selection");
            _dlssIndicatorInitializing = true;
            combo.SelectedIndex = combo.SelectedIndex == 0 ? 1 : 0;
            _dlssIndicatorInitializing = false;
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[DlssIndicatorCombo] Failed to set registry — {ex.Message}");
        }
    }

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
    {
        AboutVersionText.Text = $"v{CrashReporter.AppVersion}  ·  Simplified PC Gaming by RankFTW";
        ViewModel.NavigateToAboutCommand.Execute(null);
    }

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
            var addonService = ViewModel.AddonPackServiceInstance;
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

    // ── Update All handlers ──────────────────────────────────────────────────

    private async void UpdateAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.Settings.GlobalSkipRsUpdates)
            await ViewModel.UpdateAllReShadeAsync();
        if (!ViewModel.Settings.GlobalSkipRdxUpdates)
            await ViewModel.UpdateAllRenoDxAsync();
        if (!ViewModel.Settings.GlobalSkipUlUpdates)
            await ViewModel.UpdateAllUlAsync();
        if (!ViewModel.Settings.GlobalSkipDcUpdates)
            await ViewModel.UpdateAllDcAsync();
        if (!ViewModel.Settings.GlobalSkipOsUpdates)
            await ViewModel.UpdateAllOsAsync();
        if (!ViewModel.Settings.GlobalSkipRefUpdates)
            await ViewModel.UpdateAllRefAsync();
        await ViewModel.UpdateAllDxvkAsync();
        await ViewModel.UpdateAllLumaAsync();
        await ViewModel.UpdateAllDofFixAsync();
    }

    private async void UpdateAllRenoDx_Click(object sender, RoutedEventArgs e)
        => await ViewModel.UpdateAllRenoDxAsync();

    private async void UpdateAllReShade_Click(object sender, RoutedEventArgs e)
        => await ViewModel.UpdateAllReShadeAsync();

    internal void InstallUlButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.InstallUlButton_Click(sender, e);

    internal void UninstallUlButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.UninstallUlButton_Click(sender, e);

    internal void InstallDcButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.InstallDcButton_Click(sender, e);

    internal void UninstallDcButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.UninstallDcButton_Click(sender, e);

    internal void InstallOsButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.InstallOsButton_Click(sender, e);

    internal void UninstallOsButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.UninstallOsButton_Click(sender, e);

    internal void InstallRefButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.InstallRefButton_Click(sender, e);

    internal void UninstallRefButton_Click(object sender, RoutedEventArgs e)
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

    // ── DXVK event handlers ──────────────────────────────────────────────────

    private async void InstallDxvkButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;
        if (card.DxvkIsInstalling) return;
        if (card.DxvkStatus == GameStatus.UpdateAvailable)
            await ViewModel.UpdateDxvkAsync(card);
        else if (card.DxvkStatus == GameStatus.Installed)
            await ViewModel.InstallDxvkAsync(card, Content.XamlRoot); // reinstall
        else
            await ViewModel.InstallDxvkAsync(card, Content.XamlRoot);
    }

    private void UninstallDxvkButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;
        ViewModel.UninstallDxvk(card);
    }

    private void CopyDxvkConfButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;
        ViewModel.CopyDxvkConf(card);
    }

    private async void DxvkInfoButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;

        // Build DXVK info content with game-specific notes from manifest
        var content = "DXVK translates DirectX 8/9/10/11 API calls into Vulkan.\n\n"
            + "Benefits:\n"
            + "• Enables ReShade compute shaders on older DX games\n"
            + "• May improve performance and reduce shader stutter\n"
            + "• Enables HDR output via dxvk.conf\n"
            + "• Borderless fullscreen recommended over exclusive fullscreen\n\n"
            + "⚠ Anti-cheat games may ban players using DXVK.\n"
            + "⚠ Game overlays (Steam, NVIDIA, RTSS) may conflict.";

        // Append game-specific notes from manifest
        var manifest = ViewModel.Manifest;
        if (manifest?.DxvkGameNotes != null
            && manifest.DxvkGameNotes.TryGetValue(card.GameName, out var noteEntry)
            && !string.IsNullOrWhiteSpace(noteEntry.Notes))
        {
            content += $"\n\n── Game Notes ──\n{noteEntry.Notes}";
        }

        var dialog = new ContentDialog
        {
            Title = "ℹ DXVK Info",
            Content = new TextBlock
            {
                Text = content,
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                FontSize = 13,
                Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            },
            CloseButtonText = "OK",
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        await DialogService.ShowSafeAsync(dialog);
    }

    private void DxvkVariantCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => _settingsHandler.DxvkVariantCombo_SelectionChanged(sender, e);

    private void ReShadeChannelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => _settingsHandler.ReShadeChannelCombo_SelectionChanged(sender, e);

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

    private async void DetailLumaStatus_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var card = ViewModel.SelectedGame;
        if (card == null) return;

        if (card.IsLumaInstalled)
            await Windows.System.Launcher.LaunchUriAsync(
                new Uri("https://github.com/Filoppi/Luma-Framework/releases"));
    }

    private async void DetailDxvkStatus_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var card = ViewModel.SelectedGame;
        if (card == null || !card.IsDxvkInstalled) return;

        var variant = ViewModel.ResolveDxvkVariant(card.GameName);
        var url = variant switch
        {
            Models.DxvkVariant.LiliumHdr => "https://github.com/EndlesslyFlowering/dxvk/releases",
            Models.DxvkVariant.Stable => "https://github.com/doitsujin/dxvk/releases",
            _ => "https://github.com/doitsujin/dxvk/releases", // Development doesn't have a stable release page
        };
        await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
    }

    // ── DOF Fix event handlers ───────────────────────────────────────────────

    private async void InstallDofFixButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;
        if (card.DofFixIsInstalling || string.IsNullOrEmpty(card.InstallPath)) return;

        card.DofFixIsInstalling = true;
        card.DofFixActionMessage = "Installing DOF Fix...";
        card.DofFixProgress = 0;
        try
        {
            var progress = new Progress<(string msg, double pct)>(p =>
            {
                card.DofFixActionMessage = p.msg;
                card.DofFixProgress = p.pct;
            });
            var success = await ViewModel.DofFixServiceInstance.InstallAsync(card.InstallPath, progress);
            if (success)
            {
                card.DofFixInstalledVersion = ViewModel.DofFixServiceInstance.StagedVersion;
                card.DofFixStatus = Models.GameStatus.Installed;
                card.DofFixActionMessage = "✅ DOF Fix installed!";
                card.NotifyAll();
                card.FadeMessage(m => card.DofFixActionMessage = m, card.DofFixActionMessage);
            }
            else
            {
                card.DofFixActionMessage = "❌ Install failed";
            }
        }
        catch (Exception ex)
        {
            card.DofFixActionMessage = $"❌ {ex.Message}";
        }
        finally
        {
            card.DofFixIsInstalling = false;
        }
    }

    private void UninstallDofFixButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;
        if (string.IsNullOrEmpty(card.InstallPath)) return;

        var success = ViewModel.DofFixServiceInstance.Uninstall(card.InstallPath);
        if (success)
        {
            card.DofFixStatus = Models.GameStatus.NotInstalled;
            card.DofFixInstalledVersion = null;
            card.DofFixActionMessage = "✖ DOF Fix removed.";
            card.NotifyAll();
            card.FadeMessage(m => card.DofFixActionMessage = m, card.DofFixActionMessage);
        }
        else
        {
            card.DofFixActionMessage = "❌ Uninstall failed";
        }
    }

    private async void DofFixInfoButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;

        var notes = ViewModel.DofFixServiceInstance.ReleaseNotes;
        if (string.IsNullOrEmpty(notes))
        {
            await ViewModel.DofFixServiceInstance.CheckForUpdateAsync();
            notes = ViewModel.DofFixServiceInstance.ReleaseNotes;
        }

        var dialog = new ContentDialog
        {
            Title = "UE DOF Fix — Release Notes",
            Content = new ScrollViewer
            {
                Content = new TextBlock
                {
                    Text = notes ?? "No release notes available.",
                    TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                    IsTextSelectionEnabled = true,
                },
                MaxHeight = 400,
            },
            CloseButtonText = "OK",
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        await DialogService.ShowSafeAsync(dialog);
    }

    private async void DofFixCogButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "DOF Fix Settings",
            Content = new TextBlock
            {
                Text = "No configurable settings available for this component.",
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
            },
            CloseButtonText = "OK",
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        await DialogService.ShowSafeAsync(dialog);
    }

    private async void DetailDofFixStatus_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var card = ViewModel.SelectedGame;
        if (card == null || !card.IsDofFixInstalled) return;

        var version = card.DofFixInstalledVersion;
        if (!string.IsNullOrEmpty(version))
        {
            var url = ViewModel.DofFixServiceInstance.GetReleaseUrl(version);
            await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
        }
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

    internal void HideButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetCardFromSender(sender) is { } card)
            ViewModel.ToggleHideGameCommand.Execute(card);
    }

    internal void LaunchGame_Click(object sender, RoutedEventArgs e)
    {
        var card = GetCardFromSender(sender) ?? ViewModel.SelectedGame;
        if (card == null) return;
        LaunchGame(card);
    }

    private void GameList_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        if (ViewModel.SelectedGame is { } card)
            LaunchGame(card);
    }

    private void LaunchGame(GameCardViewModel card)
    {
        try
        {
            var gameName = card.GameName;
            var launchArgs = ViewModel.GameNameServiceInstance.LaunchArgsOverrides
                .TryGetValue(gameName, out var args) ? args : null;

            // 1. User override (absolute path)
            if (ViewModel.GameNameServiceInstance.LaunchExeOverrides.TryGetValue(gameName, out var userExe)
                && !string.IsNullOrEmpty(userExe) && File.Exists(userExe))
            {
                _crashReporter.Log($"[MainWindow.LaunchGame] Launching '{gameName}' via user override: {userExe} {launchArgs}");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(userExe)
                {
                    Arguments = launchArgs ?? "",
                    UseShellExecute = true,
                });
                return;
            }

            // 2. Manifest override (relative path from InstallPath)
            if (ViewModel.Manifest?.LaunchExeOverrides != null
                && ViewModel.Manifest.LaunchExeOverrides.TryGetValue(gameName, out var manifestExe)
                && !string.IsNullOrEmpty(manifestExe) && !string.IsNullOrEmpty(card.InstallPath))
            {
                var fullPath = Path.Combine(card.InstallPath, manifestExe);
                if (File.Exists(fullPath))
                {
                    _crashReporter.Log($"[MainWindow.LaunchGame] Launching '{gameName}' via manifest override: {fullPath} {launchArgs}");
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(fullPath)
                    {
                        Arguments = launchArgs ?? "",
                        UseShellExecute = true,
                    });
                    return;
                }
            }

            // 3. Steam protocol (use -applaunch when args are set for reliable arg passing)
            var steamAppId = card.DetectedGame?.SteamAppId;
            if (steamAppId != null && steamAppId > 0)
            {
                if (!string.IsNullOrEmpty(launchArgs))
                {
                    var steamExe = GetSteamExePath();
                    if (steamExe != null)
                    {
                        _crashReporter.Log($"[MainWindow.LaunchGame] Launching '{gameName}' via Steam -applaunch {steamAppId} {launchArgs}");
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(steamExe)
                        {
                            Arguments = $"-applaunch {steamAppId} {launchArgs}",
                            UseShellExecute = true,
                        });
                    }
                    else
                    {
                        // Fallback: use URL protocol (args may not pass reliably)
                        var steamUri = $"steam://rungameid/{steamAppId}";
                        _crashReporter.Log($"[MainWindow.LaunchGame] Launching '{gameName}' via Steam URL (args may not apply): {steamUri}");
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(steamUri) { UseShellExecute = true });
                    }
                }
                else
                {
                    var steamUri = $"steam://rungameid/{steamAppId}";
                    _crashReporter.Log($"[MainWindow.LaunchGame] Launching '{gameName}' via Steam: {steamUri}");
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(steamUri) { UseShellExecute = true });
                }
                return;
            }

            // 4. Epic Games Store protocol (skip if args set — protocol doesn't support game args)
            if (!string.IsNullOrEmpty(card.DetectedGame?.EpicAppName) && string.IsNullOrEmpty(launchArgs))
            {
                var epicUri = $"com.epicgames.launcher://apps/{card.DetectedGame.EpicAppName}?action=launch&silent=true";
                _crashReporter.Log($"[MainWindow.LaunchGame] Launching '{gameName}' via Epic protocol: {epicUri}");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(epicUri) { UseShellExecute = true });
                return;
            }

            // 5. Direct exe — find the game exe in InstallPath
            if (!string.IsNullOrEmpty(card.InstallPath) && Directory.Exists(card.InstallPath))
            {
                var exes = Directory.GetFiles(card.InstallPath, "*.exe", SearchOption.TopDirectoryOnly);
                var excludeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    { "unins000", "UnityCrashHandler64", "UnityCrashHandler32", "CrashReporter", "launcher" };
                var gameExe = exes
                    .Where(e => !excludeNames.Contains(Path.GetFileNameWithoutExtension(e)))
                    .OrderByDescending(e => new FileInfo(e).Length)
                    .FirstOrDefault();

                if (gameExe != null)
                {
                    _crashReporter.Log($"[MainWindow.LaunchGame] Launching '{gameName}' via auto-detected exe: {gameExe} {launchArgs}");
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(gameExe)
                    {
                        Arguments = launchArgs ?? "",
                        UseShellExecute = true,
                    });
                    return;
                }
            }

            _crashReporter.Log($"[MainWindow.LaunchGame] No launch method found for '{gameName}'");
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainWindow.LaunchGame] Failed to launch '{card.GameName}' — {ex.Message}");
        }
    }

    private static string? GetSteamExePath()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam")
                         ?? Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
            var installPath = key?.GetValue("InstallPath") as string;
            if (!string.IsNullOrEmpty(installPath))
            {
                var exe = Path.Combine(installPath, "steam.exe");
                if (File.Exists(exe)) return exe;
            }
        }
        catch { }
        return null;
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

    private void OpenAppData_Click(object sender, RoutedEventArgs e)
    {
        var card = GetCardFromSender(sender);
        if (card == null || string.IsNullOrEmpty(card.InstallPath)) return;

        // Open the exact folder where Engine.ini is deployed
        var configDir = AuxInstallService.ResolveEngineIniDir(card.InstallPath, card.EngineIniProjectOverride, card.GameName);
        if (configDir != null && System.IO.Directory.Exists(configDir))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(configDir) { UseShellExecute = true });
            return;
        }

        // Fallback: open the project root in AppData
        var projectName = card.EngineIniProjectOverride
            ?? AuxInstallService.ResolveUeProjectName(card.InstallPath);

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(projectName))
        {
            var appDataDir = System.IO.Path.Combine(localAppData, projectName);
            if (System.IO.Directory.Exists(appDataDir))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(appDataDir) { UseShellExecute = true });
                return;
            }
        }
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

        // Show progress dialog
        var progressPanel = new StackPanel { Spacing = 8 };
        var progressRow = new StackPanel { Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal, Spacing = 12 };
        var progressRing = new ProgressRing { IsActive = true, Width = 20, Height = 20 };
        var progressText = new TextBlock { Text = "Clearing caches...", FontSize = 13, Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush) };
        progressRow.Children.Add(progressRing);
        progressRow.Children.Add(progressText);
        progressPanel.Children.Add(progressRow);

        var progressDialog = new ContentDialog
        {
            Title = "Full Refresh",
            Content = progressPanel,
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        _ = DialogService.ShowSafeAsync(progressDialog);

        var uiProgress = new Progress<string>(msg => DispatcherQueue?.TryEnqueue(() => progressText.Text = msg));
        await ViewModel.FullRefreshAsync(uiProgress);

        progressDialog.Hide();
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
