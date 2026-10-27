// MainWindow.Events.Components.FrameLimiters.cs — ReLimiter (UL) and Display Commander (DC) cog dialog handlers.

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
    private async void UlCogButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;
            var content = new StackPanel { Spacing = 8 };
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

            // Find the relimiter log file (relimiter_*.log)
            string? logFile = null;
            if (!string.IsNullOrEmpty(card.InstallPath) && Directory.Exists(card.InstallPath))
            {
                try
                {
                    logFile = Directory.GetFiles(card.InstallPath, "relimiter_*.log").FirstOrDefault();
                }
                catch (Exception ex) { CrashReporter.Log($"[MainWindow.UlCogButton_Click] Failed to locate relimiter log file — {ex.Message}"); }
            }

            var logName = logFile != null ? Path.GetFileName(logFile) : "relimiter_*.log";

            // Open relimiter log
            var openLogBtn = new Button
            {
                Content = "Open ReLimiter log",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
                Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
                BorderBrush = UIFactory.Brush(ResourceKeys.BorderStrongBrush),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 7, 12, 7), FontSize = 12,
                IsEnabled = logFile != null,
            };
            openLogBtn.Click += async (s, ev) =>
            {
                if (logFile != null && File.Exists(logFile))
                    await Windows.System.Launcher.LaunchUriAsync(new Uri(logFile));
            };
            content.Children.Add(openLogBtn);

            // Copy relimiter log to clipboard (as file with correct name)
            var copyLogBtn = new Button
            {
                Content = "Copy ReLimiter log to clipboard",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
                Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
                BorderBrush = UIFactory.Brush(ResourceKeys.BorderStrongBrush),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 7, 12, 7), FontSize = 12,
                IsEnabled = logFile != null,
            };
            copyLogBtn.Click += async (s, ev) =>
            {
                if (logFile != null && File.Exists(logFile))
                {
                    try
                    {
                        var tempDir = Path.Combine(Path.GetTempPath(), "RHI_clipboard");
                        Directory.CreateDirectory(tempDir);
                        var tempFile = Path.Combine(tempDir, Path.GetFileName(logFile));
                        File.Copy(logFile, tempFile, overwrite: true);

                        var storageFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(tempFile);
                        var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
                        dataPackage.SetStorageItems(new[] { storageFile });
                        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
                        Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
                        card.UlActionMessage = $"✅ {Path.GetFileName(logFile)} copied to clipboard.";
                        card.FadeMessage(m => card.UlActionMessage = m, card.UlActionMessage);
                    }
                    catch (Exception ex) { card.UlActionMessage = $"❌ {ex.Message}"; }
                }
            };
            content.Children.Add(copyLogBtn);

            // ── Target FPS Setting ────────────────────────────────────────────────
            bool ulIniExists = !string.IsNullOrEmpty(card.InstallPath)
                && File.Exists(Path.Combine(card.InstallPath, "relimiter.ini"));

            content.Children.Add(new Border { Height = 1, Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush), Margin = new Thickness(0, 10, 0, 2) });
            content.Children.Add(new TextBlock
            {
                Text = "Frame Limiter",
                FontSize = 13,
                Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
                Margin = new Thickness(0, 4, 0, 0),
            });

            // Target FPS per-game control
            var targetFpsPanel = new Grid { ColumnSpacing = 12 };
            targetFpsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            targetFpsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var targetFpsLabel = new TextBlock
            {
                Text = "Target FPS",
                FontSize = 12,
                Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(targetFpsLabel, 0);
            targetFpsPanel.Children.Add(targetFpsLabel);
            var targetFpsCombo = new ComboBox { FontSize = 12, MinWidth = 140, HorizontalAlignment = HorizontalAlignment.Right };
            ToolTipService.SetToolTip(targetFpsCombo, "FPS cap for this game. Select a VRR preset or Custom for a manual value.");
            Grid.SetColumn(targetFpsCombo, 1);
            targetFpsPanel.Children.Add(targetFpsCombo);

            // VRR preset options (same as global settings)
            var vrrPresets = new (int Fps, string Label)[]
            {
                (59,  "59 (60Hz VRR)"),
                (73,  "73 (75Hz VRR)"),
                (97,  "97 (100Hz VRR)"),
                (116, "116 (120Hz VRR)"),
                (138, "138 (144Hz VRR)"),
                (157, "157 (165Hz VRR)"),
                (171, "171 (180Hz VRR)"),
                (189, "189 (200Hz VRR)"),
                (224, "224 (240Hz VRR)"),
                (258, "258 (280Hz VRR)"),
                (275, "275 (300Hz VRR)"),
                (324, "324 (360Hz VRR)"),
                (416, "416 (480Hz VRR)"),
                (431, "431 (500Hz VRR)"),
            };
            var vrrFpsSet = new HashSet<int>(vrrPresets.Select(p => p.Fps));

            // Read current per-game value from the game's relimiter.ini
            int currentTargetFps = 0;
            if (!string.IsNullOrEmpty(card.InstallPath))
            {
                var ulIniFile = Path.Combine(card.InstallPath, "relimiter.ini");
                if (File.Exists(ulIniFile))
                {
                    try
                    {
                        var ulIni = AuxInstallService.ParseIni(File.ReadAllLines(ulIniFile));
                        if (ulIni.TryGetValue("FrameLimiter", out var flSection)
                            && flSection.TryGetValue("target_fps", out var fpsVal)
                            && int.TryParse(fpsVal, out var parsedFps))
                        {
                            currentTargetFps = parsedFps;
                        }
                    }
                    catch (Exception ex) { CrashReporter.Log($"[MainWindow.UlCogButton_Click] Failed to read target_fps from ini, using default — {ex.Message}"); }
                }
            }

            // Inline custom FPS input (shown when "Custom..." is selected)
            var customFpsPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Visibility = Visibility.Collapsed };
            var customFpsBox = new TextBox { PlaceholderText = "20-1000", FontSize = 12, MinWidth = 100 };
            var customFpsBtn = new Button { Content = "Set", FontSize = 12 };
            customFpsPanel.Children.Add(customFpsBox);
            customFpsPanel.Children.Add(customFpsBtn);

            // Populate combo
            bool suppressFpsChange = true;
            targetFpsCombo.Items.Add("Off");
            foreach (var preset in vrrPresets)
                targetFpsCombo.Items.Add(preset.Label);

            // If current value is a custom FPS (not in presets), insert it before "Custom..."
            if (currentTargetFps > 0 && !vrrFpsSet.Contains(currentTargetFps))
                targetFpsCombo.Items.Add($"{currentTargetFps} (Custom)");

            targetFpsCombo.Items.Add("Custom...");

            // Select based on current value
            if (currentTargetFps == 0)
                targetFpsCombo.SelectedIndex = 0; // Off
            else
            {
                int matchIdx = Array.FindIndex(vrrPresets, p => p.Fps == currentTargetFps);
                if (matchIdx >= 0)
                    targetFpsCombo.SelectedIndex = matchIdx + 1; // +1 for "Off" at index 0
                else
                {
                    // Custom value — select the "(Custom)" item
                    targetFpsCombo.SelectedIndex = targetFpsCombo.Items.Count - 2; // before "Custom..."
                }
            }
            suppressFpsChange = false;

            // Helper to refresh combo after setting custom value
            void RefreshFpsCombo(int newFps)
            {
                suppressFpsChange = true;
                currentTargetFps = newFps;
                targetFpsCombo.Items.Clear();
                targetFpsCombo.Items.Add("Off");
                foreach (var preset in vrrPresets)
                    targetFpsCombo.Items.Add(preset.Label);
                if (newFps > 0 && !vrrFpsSet.Contains(newFps))
                    targetFpsCombo.Items.Add($"{newFps} (Custom)");
                targetFpsCombo.Items.Add("Custom...");

                if (newFps == 0)
                    targetFpsCombo.SelectedIndex = 0;
                else
                {
                    int idx = Array.FindIndex(vrrPresets, p => p.Fps == newFps);
                    if (idx >= 0)
                        targetFpsCombo.SelectedIndex = idx + 1;
                    else
                        targetFpsCombo.SelectedIndex = targetFpsCombo.Items.Count - 2; // Custom item
                }
                customFpsPanel.Visibility = Visibility.Collapsed;
                suppressFpsChange = false;
            }

            targetFpsCombo.SelectionChanged += (s, ev) =>
            {
                if (suppressFpsChange) return;
                if (string.IsNullOrEmpty(card.InstallPath)) return;

                var selectedText = targetFpsCombo.SelectedItem as string ?? "";

                // "Custom..." shows inline TextBox for manual entry
                if (selectedText == "Custom...")
                {
                    customFpsPanel.Visibility = Visibility.Visible;
                    customFpsBox.Text = "";
                    customFpsBox.Focus(FocusState.Programmatic);
                    return;
                }

                customFpsPanel.Visibility = Visibility.Collapsed;

                // Handle preset/Off selection
                int newFps;
                var idx = targetFpsCombo.SelectedIndex;
                if (idx == 0)
                    newFps = 0; // Off
                else if (idx - 1 < vrrPresets.Length)
                    newFps = vrrPresets[idx - 1].Fps;
                else
                    return; // Custom label item — don't set

                var iniFile = Path.Combine(card.InstallPath, "relimiter.ini");
                if (File.Exists(iniFile))
                {
                    try
                    {
                        AuxInstallService.ApplyUlTargetFps(iniFile, newFps);
                        currentTargetFps = newFps;
                        card.UlActionMessage = newFps == 0
                            ? "✅ Target FPS disabled for this game."
                            : $"✅ Target FPS set to {newFps} for this game.";
                        card.FadeMessage(m => card.UlActionMessage = m, card.UlActionMessage);
                    }
                    catch (Exception ex) { card.UlActionMessage = $"❌ {ex.Message}"; }
                }
            };

            // Custom FPS "Set" button handler
            customFpsBtn.Click += (s, ev) =>
            {
                if (string.IsNullOrEmpty(card.InstallPath)) return;
                if (int.TryParse(customFpsBox.Text, out var customFps) && customFps >= 20 && customFps <= 1000)
                {
                    var iniFile = Path.Combine(card.InstallPath, "relimiter.ini");
                    if (File.Exists(iniFile))
                    {
                        try
                        {
                            AuxInstallService.ApplyUlTargetFps(iniFile, customFps);
                            card.UlActionMessage = $"✅ Target FPS set to {customFps} for this game.";
                            card.FadeMessage(m => card.UlActionMessage = m, card.UlActionMessage);
                            RefreshFpsCombo(customFps);
                        }
                        catch (Exception ex) { card.UlActionMessage = $"❌ {ex.Message}"; }
                    }
                }
            };

            // Enter key also sets custom value
            customFpsBox.KeyDown += (s, ev) =>
            {
                if (ev.Key == Windows.System.VirtualKey.Enter)
                {
                    if (string.IsNullOrEmpty(card.InstallPath)) return;
                    if (int.TryParse(customFpsBox.Text, out var customFps) && customFps >= 20 && customFps <= 1000)
                    {
                        var iniFile = Path.Combine(card.InstallPath, "relimiter.ini");
                        if (File.Exists(iniFile))
                        {
                            try
                            {
                                AuxInstallService.ApplyUlTargetFps(iniFile, customFps);
                                card.UlActionMessage = $"✅ Target FPS set to {customFps} for this game.";
                                card.FadeMessage(m => card.UlActionMessage = m, card.UlActionMessage);
                                RefreshFpsCombo(customFps);
                            }
                            catch (Exception ex) { card.UlActionMessage = $"❌ {ex.Message}"; }
                        }
                    }
                }
            };

            content.Children.Add(targetFpsPanel);
            content.Children.Add(customFpsPanel);
            if (!ulIniExists)
            {
                targetFpsPanel.Opacity = 0.4;
                targetFpsPanel.IsHitTestVisible = false;
                content.Children.Add(new TextBlock
                {
                    Text = "Deploy relimiter.ini to enable these settings",
                    FontSize = 10,
                    Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
                    Margin = new Thickness(0, -2, 0, 0),
                });
            }

            // ── Compatibility Settings ────────────────────────────────────────────
            content.Children.Add(new Border { Height = 1, Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush), Margin = new Thickness(0, 10, 0, 2) });
            content.Children.Add(new TextBlock
            {
                Text = "Compatibility Settings",
                FontSize = 13,
                Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
                Margin = new Thickness(0, 4, 0, 0),
            });

            // DLSS Hooks per-game toggle
            var dlssHooksPanel = new Grid { ColumnSpacing = 12 };
            dlssHooksPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            dlssHooksPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var dlssHooksLabel = new TextBlock
            {
                Text = "DLSS Hooks",
                FontSize = 12,
                Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(dlssHooksLabel, 0);
            dlssHooksPanel.Children.Add(dlssHooksLabel);
            var dlssHooksCombo = new ComboBox { FontSize = 12, MinWidth = 80, HorizontalAlignment = HorizontalAlignment.Right };
            dlssHooksCombo.Items.Add("Off");
            dlssHooksCombo.Items.Add("On");
            ToolTipService.SetToolTip(dlssHooksCombo, "Shows DLSS version/preset info on the ReLimiter OSD. Disable if causing crashes.");
            Grid.SetColumn(dlssHooksCombo, 1);
            dlssHooksPanel.Children.Add(dlssHooksCombo);

            // Read current per-game value from the game's relimiter.ini
            bool currentDlssHooks = ViewModel.Settings.UlDlssHooks; // default to global
            if (!string.IsNullOrEmpty(card.InstallPath))
            {
                var ulIniFile = Path.Combine(card.InstallPath, "relimiter.ini");
                if (File.Exists(ulIniFile))
                {
                    try
                    {
                        var ulIni = AuxInstallService.ParseIni(File.ReadAllLines(ulIniFile));
                        if (ulIni.TryGetValue("FrameLimiter", out var flSection)
                            && flSection.TryGetValue("dlss_info_hooks", out var hooksVal))
                        {
                            currentDlssHooks = hooksVal.Equals("true", StringComparison.OrdinalIgnoreCase);
                        }
                    }
                    catch (Exception ex) { CrashReporter.Log($"[MainWindow.UlCogButton_Click] Failed to read dlss_info_hooks from ini, using global default — {ex.Message}"); }
                }
            }
            dlssHooksCombo.SelectedIndex = currentDlssHooks ? 1 : 0;
            dlssHooksCombo.SelectionChanged += (s, ev) =>
            {
                if (string.IsNullOrEmpty(card.InstallPath)) return;
                var ulIniFile = Path.Combine(card.InstallPath, "relimiter.ini");
                if (File.Exists(ulIniFile))
                {
                    try
                    {
                        AuxInstallService.ApplyUlDlssHooks(ulIniFile, dlssHooksCombo.SelectedIndex == 1);
                        card.UlActionMessage = dlssHooksCombo.SelectedIndex == 1
                            ? "✅ DLSS Hooks enabled for this game."
                            : "✅ DLSS Hooks disabled for this game.";
                        card.FadeMessage(m => card.UlActionMessage = m, card.UlActionMessage);
                    }
                    catch (Exception ex) { card.UlActionMessage = $"❌ {ex.Message}"; }
                }
            };
            content.Children.Add(dlssHooksPanel);
            if (!ulIniExists)
            {
                dlssHooksPanel.Opacity = 0.4;
                dlssHooksPanel.IsHitTestVisible = false;
            }

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
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.UlCogButton_Click] Unhandled error — {ex.Message}\n{ex.StackTrace}");
        }
    }

    private async void DcCogButton_Click(object sender, RoutedEventArgs e)
    {
        try
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
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.DcCogButton_Click] Unhandled error — {ex.Message}\n{ex.StackTrace}");
        }
    }
}
