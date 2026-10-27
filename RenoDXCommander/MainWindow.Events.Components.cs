// MainWindow.Events.Components.cs — Per-component cog button (⚙️) dialog handlers (RS, RDX, UL, DC, OS, DXVK).

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    // ── Component Cog Button Handlers ────────────────────────────────────────────

    private async void RsCogButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;
            if (string.IsNullOrEmpty(card.InstallPath)) return;

            // Read all file state on background thread to avoid blocking UI
            var installPath = card.InstallPath;
            bool rsPresetExists = false;
            bool reshadeIniExists = false;
            bool reshadeLogExists = false;
            string currentHotkey = ViewModel.Settings.OverlayHotkey;
            string currentScreenshotHotkey = ViewModel.Settings.ScreenshotHotkey;
            
            await Task.Run(() =>
            {
                rsPresetExists = File.Exists(AuxInstallService.RsPresetIniPath);
                reshadeIniExists = File.Exists(Path.Combine(installPath, "reshade.ini"));
                reshadeLogExists = File.Exists(Path.Combine(installPath, "ReShade.log"));
                
                // Read current keys from reshade.ini (game folder)
                var iniPath = Path.Combine(installPath, "reshade.ini");
                if (reshadeIniExists)
                {
                    try
                    {
                        var ini = AuxInstallService.ParseIni(File.ReadAllLines(iniPath));
                        if (ini.TryGetValue("INPUT", out var inputSection))
                        {
                            if (inputSection.TryGetValue("KeyOverlay", out var ko) && !string.IsNullOrWhiteSpace(ko))
                                currentHotkey = ko;
                            if (inputSection.TryGetValue("KeyScreenshot", out var ks2) && !string.IsNullOrWhiteSpace(ks2))
                                currentScreenshotHotkey = ks2;
                        }
                    }
                    catch (Exception ex) { CrashReporter.Log($"[MainWindow.RsCogButton_Click] Failed to read ReShade ini hotkeys, using fallback — {ex.Message}"); }
                }
            });

            var content = new StackPanel { Spacing = 8 };

            // Deploy ReShade.ini
            var deployIniBtn = new Button
            {
                Content = "Deploy ReShade.ini",
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

                    // Force-apply manifest [renodx] INI overrides on redeploy
                    if (AuxInstallService.GlobalManifest?.RenodxIniOverrides != null
                        && AuxInstallService.GlobalManifest.RenodxIniOverrides.TryGetValue(card.GameName, out var cogIniOvr))
                        AuxInstallService.ApplyRenodxIniOverrides(card.InstallPath, cogIniOvr, forceOverwrite: true);

                    card.RsActionMessage = "✅ ReShade.ini deployed.";
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
                IsEnabled = rsPresetExists,
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
            if (!rsPresetExists)
                ToolTipService.SetToolTip(deployPresetBtn, "No ReShadePreset.ini found in RHI config folder");
            content.Children.Add(deployPresetBtn);

            // Open ReShade.ini
            var openIniBtn = new Button
            {
                Content = "Open ReShade.ini",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
                Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
                BorderBrush = UIFactory.Brush(ResourceKeys.BorderStrongBrush),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 7, 12, 7), FontSize = 12,
                IsEnabled = reshadeIniExists,
            };
            openIniBtn.Click += async (s, ev) =>
            {
                var iniPath = Path.Combine(card.InstallPath, "reshade.ini");
                if (File.Exists(iniPath))
                    await Windows.System.Launcher.LaunchUriAsync(new Uri(iniPath));
            };
            content.Children.Add(openIniBtn);

            // Open ReShade.log
            var openLogBtn = new Button
            {
                Content = "Open ReShade.log",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
                Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
                BorderBrush = UIFactory.Brush(ResourceKeys.BorderStrongBrush),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 7, 12, 7), FontSize = 12,
                IsEnabled = reshadeLogExists,
            };
            openLogBtn.Click += async (s, ev) =>
            {
                var logPath = Path.Combine(card.InstallPath, "ReShade.log");
                if (File.Exists(logPath))
                    await Windows.System.Launcher.LaunchUriAsync(new Uri(logPath));
            };
            content.Children.Add(openLogBtn);

            // Copy ReShade.log to clipboard (as file, so Discord shows "ReShade.log")
            var copyLogBtn = new Button
            {
                Content = "Copy ReShade.log to clipboard",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
                Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
                BorderBrush = UIFactory.Brush(ResourceKeys.BorderStrongBrush),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 7, 12, 7), FontSize = 12,
                IsEnabled = reshadeLogExists,
            };
            copyLogBtn.Click += async (s, ev) =>
            {
                var logPath = Path.Combine(card.InstallPath, "ReShade.log");
                if (File.Exists(logPath))
                {
                    try
                    {
                        // Copy to temp as "ReShade.log" so clipboard file has the correct name
                        var tempDir = Path.Combine(Path.GetTempPath(), "RHI_clipboard");
                        Directory.CreateDirectory(tempDir);
                        var tempFile = Path.Combine(tempDir, "ReShade.log");
                        File.Copy(logPath, tempFile, overwrite: true);

                        var storageFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(tempFile);
                        var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
                        dataPackage.SetStorageItems(new[] { storageFile });
                        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
                        Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
                        card.RsActionMessage = "✅ ReShade.log copied to clipboard.";
                        card.FadeMessage(m => card.RsActionMessage = m, card.RsActionMessage);
                    }
                    catch (Exception ex) { card.RsActionMessage = $"❌ {ex.Message}"; }
                }
            };
            content.Children.Add(copyLogBtn);

            // ── Overlay Key ───────────────────────────────────────────────────────
            content.Children.Add(new Border { Height = 1, Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush), Margin = new Thickness(0, 2, 0, 6) });

            // ── Overlay Key + Screenshot Key — side by side ───────────────────────
            var hotkeyString = currentHotkey;
            var screenshotHotkeyString = currentScreenshotHotkey;

            var hotkeyBox = new TextBox
            {
                Text = HotkeyManager.FormatHotkeyDisplay(hotkeyString),
                IsReadOnly = true,
                PlaceholderText = "Click then press a key...",
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            ToolTipService.SetToolTip(hotkeyBox, "Click here then press your desired key. Written to all reshade*.ini files for this game.");
            hotkeyBox.GotFocus  += (s, ev) => hotkeyBox.Text = "Press a key...";
            hotkeyBox.KeyDown   += (s, ev) =>
            {
                var vk = (int)ev.Key;
                if (vk == 0 || vk == 16 || vk == 17 || vk == 18) return;
                bool shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
                bool ctrl  = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
                bool alt   = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
                hotkeyString = HotkeyManager.BuildScreenshotHotkeyString(vk, shift, ctrl, alt);
                hotkeyBox.Text = HotkeyManager.FormatHotkeyDisplay(hotkeyString);
                ev.Handled = true;
            };
            hotkeyBox.LostFocus += (s, ev) => { if (hotkeyBox.Text == "Press a key...") hotkeyBox.Text = HotkeyManager.FormatHotkeyDisplay(hotkeyString); };

            var applyKeyBtn = new Button { Content = "Apply", FontSize = 12, Padding = new Thickness(16, 7, 16, 7), HorizontalAlignment = HorizontalAlignment.Right };
            applyKeyBtn.Click += async (s, ev) =>
            {
                if (string.IsNullOrEmpty(card.InstallPath)) return;
                try
                {
                    var installPath = card.InstallPath;
                    var hotkey = hotkeyString;
                    var (count, error) = await Task.Run(() =>
                    {
                        try
                        {
                            var iniFiles = Directory.EnumerateFiles(installPath, "reshade*.ini")
                                .Where(f => Path.GetExtension(f).Equals(".ini", StringComparison.OrdinalIgnoreCase)
                                         && Path.GetFileNameWithoutExtension(f).StartsWith("reshade", StringComparison.OrdinalIgnoreCase))
                                .ToList();
                            foreach (var file in iniFiles)
                                AuxInstallService.ApplyOverlayHotkey(file, hotkey);
                            return (iniFiles.Count, (string?)null);
                        }
                        catch (Exception ex) { return (0, ex.Message); }
                    });
                    if (error != null) { card.RsActionMessage = $"❌ {error}"; return; }
                    applyKeyBtn.Content = "Applied!";
                    _crashReporter.Log($"[RsCogButton_Click] Applied overlay key '{hotkeyString}' to {count} ini file(s) for '{card.GameName}'");
                }
                catch (Exception ex) { card.RsActionMessage = $"❌ {ex.Message}"; }
            };

            var screenshotHotkeyBox = new TextBox
            {
                Text = HotkeyManager.FormatHotkeyDisplay(screenshotHotkeyString),
                IsReadOnly = true,
                PlaceholderText = "Click then press a key...",
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            ToolTipService.SetToolTip(screenshotHotkeyBox, "Press a key to assign it, or Backspace to clear it. Written to all reshade*.ini files for this game.");
            screenshotHotkeyBox.GotFocus  += (s, ev) => screenshotHotkeyBox.Text = "Press a key...";
            screenshotHotkeyBox.KeyDown   += (s, ev) =>
            {
                var vk2 = (int)ev.Key;
                if (vk2 == 0 || vk2 == 16 || vk2 == 17 || vk2 == 18) return;
                bool shift2 = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
                bool ctrl2  = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
                bool alt2   = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
                screenshotHotkeyString = HotkeyManager.BuildScreenshotHotkeyString(vk2, shift2, ctrl2, alt2);
                screenshotHotkeyBox.Text = HotkeyManager.FormatHotkeyDisplay(screenshotHotkeyString);
                ev.Handled = true;
            };
            screenshotHotkeyBox.LostFocus += (s, ev) => { if (screenshotHotkeyBox.Text == "Press a key...") screenshotHotkeyBox.Text = HotkeyManager.FormatHotkeyDisplay(screenshotHotkeyString); };

            var applyScreenshotKeyBtn = new Button { Content = "Apply", FontSize = 12, Padding = new Thickness(16, 7, 16, 7), HorizontalAlignment = HorizontalAlignment.Right };
            applyScreenshotKeyBtn.Click += async (s, ev) =>
            {
                if (string.IsNullOrEmpty(card.InstallPath)) return;
                try
                {
                    var installPath = card.InstallPath;
                    var hotkey = screenshotHotkeyString;
                    var (count, error) = await Task.Run(() =>
                    {
                        try
                        {
                            var iniFiles2 = Directory.EnumerateFiles(installPath, "reshade*.ini")
                                .Where(f => Path.GetExtension(f).Equals(".ini", StringComparison.OrdinalIgnoreCase)
                                         && Path.GetFileNameWithoutExtension(f).StartsWith("reshade", StringComparison.OrdinalIgnoreCase))
                                .ToList();
                            foreach (var file in iniFiles2)
                                AuxInstallService.ApplyScreenshotHotkey(file, hotkey);
                            return (iniFiles2.Count, (string?)null);
                        }
                        catch (Exception ex) { return (0, ex.Message); }
                    });
                    if (error != null) { card.RsActionMessage = $"❌ {error}"; return; }
                    applyScreenshotKeyBtn.Content = "Applied!";
                    _crashReporter.Log($"[RsCogButton_Click] Applied screenshot key '{screenshotHotkeyString}' to {count} ini file(s) for '{card.GameName}'");
                }
                catch (Exception ex) { card.RsActionMessage = $"❌ {ex.Message}"; }
            };

            // Side-by-side layout: [Overlay label+box+btn] | [gap] | [Screenshot label+box+btn]
            var hotkeysGrid = new Grid { ColumnSpacing = 16 };
            hotkeysGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hotkeysGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var overlayCol = new StackPanel { Spacing = 4 };
            overlayCol.Children.Add(new TextBlock { Text = "Overlay Key", FontSize = 12, Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush) });
            var overlayRow = new Grid { ColumnSpacing = 8 };
            overlayRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            overlayRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(hotkeyBox, 0); Grid.SetColumn(applyKeyBtn, 1);
            overlayRow.Children.Add(hotkeyBox); overlayRow.Children.Add(applyKeyBtn);
            overlayCol.Children.Add(overlayRow);

            var screenshotCol = new StackPanel { Spacing = 4 };
            screenshotCol.Children.Add(new TextBlock { Text = "Screenshot Key", FontSize = 12, Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush) });
            var screenshotRow = new Grid { ColumnSpacing = 8 };
            screenshotRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            screenshotRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(screenshotHotkeyBox, 0); Grid.SetColumn(applyScreenshotKeyBtn, 1);
            screenshotRow.Children.Add(screenshotHotkeyBox); screenshotRow.Children.Add(applyScreenshotKeyBtn);
            screenshotCol.Children.Add(screenshotRow);

            Grid.SetColumn(overlayCol, 0); Grid.SetColumn(screenshotCol, 1);
            hotkeysGrid.Children.Add(overlayCol); hotkeysGrid.Children.Add(screenshotCol);
            content.Children.Add(hotkeysGrid);

            // ── Keep ReShade.ini Updated ──────────────────────────────────────────
            content.Children.Add(new Border
            {
                Height = 1, Margin = new Thickness(0, 8, 0, 8),
                Background = UIFactory.Brush(ResourceKeys.BorderSubtleBrush),
            });

            var keepUpdatedLabel = new TextBlock
            {
                Text = "Keep ReShade.ini Updated",
                FontSize = 12,
                Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4),
            };
            content.Children.Add(keepUpdatedLabel);

            var keepUpdatedCombo = new ComboBox
            {
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            ToolTipService.SetToolTip(keepUpdatedCombo, "When No, RHI will not automatically update this game's reshade.ini on ReShade install, update, or Apply to All Games.");
            keepUpdatedCombo.Items.Add("Yes");
            keepUpdatedCombo.Items.Add("No");

            bool keepUpdatedInitializing = true;
            var capturedKeepGameName = card.GameName;
            var capturedKeepSource = card.Source ?? "";
            keepUpdatedCombo.SelectedIndex = ViewModel.GetKeepRsIniUpdated(capturedKeepGameName, capturedKeepSource) ? 0 : 1;
            keepUpdatedCombo.SelectionChanged += (s, ev) =>
            {
                if (keepUpdatedInitializing) return;
                ViewModel.SetKeepRsIniUpdated(capturedKeepGameName, keepUpdatedCombo.SelectedIndex == 0, capturedKeepSource);
            };
            keepUpdatedInitializing = false;
            content.Children.Add(keepUpdatedCombo);

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
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.RsCogButton_Click] Unhandled error — {ex.Message}\n{ex.StackTrace}");
        }
    }

    private async void RefCogButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;
            if (string.IsNullOrEmpty(card.InstallPath)) return;

            var installPath = card.InstallPath;
            var content = new StackPanel { Spacing = 12 };

            // Delete _storage_ folder
            var storageDir = System.IO.Path.Combine(installPath, "_storage_");
            bool storageExists = System.IO.Directory.Exists(storageDir);

            var deleteStorageBtn = new Button
            {
                Content = "Delete _storage_ folder",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = UIFactory.Brush(ResourceKeys.AccentRedBgBrush),
                Foreground = UIFactory.Brush(ResourceKeys.AccentRedBrush),
                BorderBrush = UIFactory.Brush(ResourceKeys.AccentPurpleBorderBrush),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 7, 12, 7),
                FontSize = 12,
                IsEnabled = storageExists,
                Opacity = storageExists ? 1.0 : 0.45,
            };
            var statusText = new TextBlock
            {
                FontSize = 11,
                Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
                Text = storageExists ? $"Found: {storageDir}" : "_storage_ folder not found in game directory.",
                TextWrapping = TextWrapping.Wrap,
            };

            deleteStorageBtn.Click += async (s, ev) =>
            {
                deleteStorageBtn.IsEnabled = false;
                deleteStorageBtn.Content = "Deleting...";
                try
                {
                    await Task.Run(() => System.IO.Directory.Delete(storageDir, recursive: true));
                    statusText.Text = "✅ _storage_ folder deleted.";
                    statusText.Foreground = UIFactory.GetBrush("#5ECB7D");
                    CrashReporter.Log($"[RefCogButton_Click] Deleted _storage_ folder: '{storageDir}'");
                }
                catch (Exception ex)
                {
                    statusText.Text = $"❌ {ex.Message}";
                    statusText.Foreground = UIFactory.GetBrush("#E85050");
                    deleteStorageBtn.IsEnabled = true;
                    deleteStorageBtn.Content = "Delete _storage_ folder";
                    CrashReporter.Log($"[RefCogButton_Click] Failed to delete _storage_ folder: {ex.Message}");
                }
            };

            content.Children.Add(deleteStorageBtn);
            content.Children.Add(statusText);

            var dialog = new ContentDialog
            {
                Title = "RE Framework Settings",
                Content = content,
                CloseButtonText = "Close",
                XamlRoot = Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            };
            await DialogService.ShowSafeAsync(dialog);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.RefCogButton_Click] Unhandled error — {ex.Message}\n{ex.StackTrace}");
        }
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

}
