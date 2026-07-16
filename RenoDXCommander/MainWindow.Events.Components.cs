// MainWindow.Events.Components.cs — Per-component cog button (⚙️) dialog handlers (RS, RDX, UL, DC, OS, DXVK).

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
        if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;
        if (string.IsNullOrEmpty(card.InstallPath)) return;

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
            IsEnabled = File.Exists(Path.Combine(card.InstallPath, "reshade.ini")),
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
            IsEnabled = File.Exists(Path.Combine(card.InstallPath, "ReShade.log")),
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
            IsEnabled = File.Exists(Path.Combine(card.InstallPath, "ReShade.log")),
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
        var presetPath = Path.Combine(card.InstallPath, "RHI-RenoDX-Preset.txt");
        var content = new StackPanel { Spacing = 8 };

        // ── Top row: UE-Extended + Engine.ini HDR side by side ─────────────────
        if (card.UeExtendedToggleVisibility == Visibility.Visible || card.UseUeExtended)
        {
            content.Children.Add(new TextBlock
            {
                Text = "UE-Extended Settings",
                FontSize = 13,
                Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            });
        }
        var topRow = new StackPanel { Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal, Spacing = 16 };

        // Engine.ini panel (created upfront, visibility toggled dynamically)
        var enginePanel = new StackPanel { Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal, Spacing = 8 };
        enginePanel.Children.Add(new TextBlock { Text = "Engine.ini HDR", FontSize = 12, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush), VerticalAlignment = VerticalAlignment.Center });
        var engineCombo = new ComboBox { FontSize = 12, MinWidth = 80 };
        engineCombo.Items.Add("Off");
        engineCombo.Items.Add("On");
        ToolTipService.SetToolTip(engineCombo, "Deploys Engine.ini with HDR flags for games that don't have an ingame HDR option. Also disable for SDR.");
        bool engineIniActive = card.InstalledRecord?.EngineIniHdr ?? true;
        engineCombo.SelectedIndex = engineIniActive ? 1 : 0;
        engineCombo.SelectionChanged += (s, ev) =>
        {
            if (engineCombo.SelectedIndex == 1)
            {
                AuxInstallService.ApplyEngineIniHdrSettings(card.InstallPath, card.EngineIniProjectOverride, card.GameName);
                if (card.InstalledRecord != null) card.InstalledRecord.EngineIniHdr = true;
                card.ActionMessage = "✅ Engine.ini HDR settings deployed.";
            }
            else
            {
                AuxInstallService.RemoveEngineIniHdrSettings(card.InstallPath, card.EngineIniProjectOverride, card.GameName);
                if (card.InstalledRecord != null) card.InstalledRecord.EngineIniHdr = false;
                card.ActionMessage = "✅ Engine.ini HDR settings removed.";
            }
            if (card.InstalledRecord != null)
                App.Services.GetRequiredService<IModInstallService>().SaveRecordPublic(card.InstalledRecord);
            card.FadeMessage(m => card.ActionMessage = m, card.ActionMessage);
        };
        enginePanel.Children.Add(engineCombo);
        enginePanel.Visibility = card.UseUeExtended ? Visibility.Visible : Visibility.Collapsed;

        if (card.UeExtendedToggleVisibility == Visibility.Visible)
        {
            var uePanel = new StackPanel { Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal, Spacing = 8 };
            uePanel.Children.Add(new TextBlock { Text = "UE-Extended", FontSize = 12, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush), VerticalAlignment = VerticalAlignment.Center });
            var ueCombo = new ComboBox { FontSize = 12, MinWidth = 80 };
            ueCombo.Items.Add("Off");
            ueCombo.Items.Add("On");
            ToolTipService.SetToolTip(ueCombo, "Switch between using UE-Extended or the game specific mod/generic Unreal RenoDX mod.");
            ueCombo.SelectedIndex = card.UseUeExtended ? 1 : 0;
            ueCombo.SelectionChanged += (s, ev) =>
            {
                bool enable = ueCombo.SelectedIndex == 1;
                if (enable != card.UseUeExtended)
                    ViewModel.ToggleUeExtended(card);
                // Show/hide Engine.ini combo reactively
                enginePanel.Visibility = enable ? Visibility.Visible : Visibility.Collapsed;
            };
            uePanel.Children.Add(ueCombo);
            topRow.Children.Add(uePanel);
        }

        topRow.Children.Add(enginePanel);
        content.Children.Add(topRow);

        // ── Peak Nits (toneMapPeakNits in [renodx-preset*] sections) ──────────
        if (File.Exists(iniPath))
        {
            var peakIni = AuxInstallService.ParseIni(File.ReadAllLines(iniPath));
            var presetWithNits = peakIni.FirstOrDefault(kv =>
                kv.Key.StartsWith("renodx-preset", StringComparison.OrdinalIgnoreCase)
                && kv.Value.ContainsKey("ToneMapPeakNits"));
            string currentNits = "";
            if (presetWithNits.Value != null && presetWithNits.Value.TryGetValue("ToneMapPeakNits", out var nv))
                currentNits = double.TryParse(nv, out var dv) ? ((int)dv).ToString() : nv;

            var nitsPanel = new StackPanel { Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal, Spacing = 10 };
            nitsPanel.Children.Add(new TextBlock
            {
                Text = "Set Maximum Nits",
                FontSize = 12,
                Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
                VerticalAlignment = VerticalAlignment.Center,
            });

            var nitsBox = new TextBox
            {
                Text = currentNits,
                Width = 100,
                FontSize = 12,
                PlaceholderText = "nits",
                VerticalAlignment = VerticalAlignment.Center,
            };

            // Helper: write nits value to all preset sections
            void ApplyNitsValue(string nitsValue)
            {
                if (!int.TryParse(nitsValue, out var val) || val <= 0)
                {
                    card.ActionMessage = "❌ Enter a valid number.";
                    return;
                }
                try
                {
                    var freshIni = AuxInstallService.ParseIni(File.ReadAllLines(iniPath));
                    int updated = 0;
                    foreach (var section in freshIni)
                    {
                        if (section.Key.StartsWith("renodx-preset", StringComparison.OrdinalIgnoreCase))
                        {
                            section.Value["ToneMapPeakNits"] = val.ToString();
                            updated++;
                        }
                    }
                    if (updated == 0)
                    {
                        freshIni["renodx-preset1"] = new AuxInstallService.OrderedDict { ["ToneMapPeakNits"] = val.ToString() };
                        updated = 1;
                    }
                    AuxInstallService.WriteIni(iniPath, freshIni);
                    nitsBox.Text = val.ToString();
                    card.ActionMessage = $"✅ Set toneMapPeakNits={val} in {updated} preset(s).";
                    card.FadeMessage(m => card.ActionMessage = m, card.ActionMessage);
                }
                catch (Exception ex) { card.ActionMessage = $"❌ {ex.Message}"; }
            }

            // Enter key in TextBox applies the value and deselects
            nitsBox.KeyDown += (s, ev) =>
            {
                if (ev.Key == Windows.System.VirtualKey.Enter)
                {
                    ApplyNitsValue(nitsBox.Text);
                    nitsBox.IsEnabled = false;
                    nitsBox.IsEnabled = true;
                    ev.Handled = true;
                }
            };

            var autoBtn = new Button
            {
                Content = "Auto",
                Background = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush),
                Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush),
                BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 5, 10, 5), FontSize = 12,
            };
            ToolTipService.SetToolTip(autoBtn, "Reads your monitor's peak brightness automatically.");
            autoBtn.Click += async (s, ev) =>
            {
                try
                {
                    var devices = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(
                        Windows.Devices.Display.DisplayMonitor.GetDeviceSelector());
                    if (devices.Count == 0) { card.ActionMessage = "❌ No display found."; return; }

                    float maxNitsFound = 0;
                    foreach (var device in devices)
                    {
                        try
                        {
                            var mon = await Windows.Devices.Display.DisplayMonitor.FromInterfaceIdAsync(device.Id);
                            if (mon.MaxLuminanceInNits > maxNitsFound)
                                maxNitsFound = mon.MaxLuminanceInNits;
                        }
                        catch { }
                    }
                    var peakNits = (int)maxNitsFound;
                    if (peakNits <= 0) { card.ActionMessage = "❌ Could not read peak brightness."; return; }

                    ApplyNitsValue(peakNits.ToString());
                }
                catch (Exception ex) { card.ActionMessage = $"❌ {ex.Message}"; }
            };

            nitsPanel.Children.Add(autoBtn);
            nitsPanel.Children.Add(nitsBox);
            content.Children.Add(nitsPanel);
        }

        // ── Compatibility Settings from [renodx] section ──────────────────────
        if (File.Exists(iniPath))
        {
            var ini = AuxInstallService.ParseIni(File.ReadAllLines(iniPath));
            if (ini.TryGetValue("renodx", out var renodxSection))
            {
                var upgradeKeys = renodxSection
                    .Where(kv => (kv.Key.StartsWith("Upgrade_", StringComparison.OrdinalIgnoreCase)
                                  && !kv.Key.Equals("Upgrade_UseSCRGB", StringComparison.OrdinalIgnoreCase)
                                  && !kv.Key.Equals("Upgrade_CopyDestinations", StringComparison.OrdinalIgnoreCase)
                                  && !kv.Key.Equals("Upgrade_SwapChainCompatibility", StringComparison.OrdinalIgnoreCase))
                              || kv.Key.Equals("Set_Path", StringComparison.OrdinalIgnoreCase)
                              || kv.Key.Equals("DumpLUTShaders", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(kv => kv.Key.Equals("DumpLUTShaders", StringComparison.OrdinalIgnoreCase) ? 1 : 0) // DumpLUT last
                    .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (upgradeKeys.Count > 0)
                {
                    content.Children.Add(new Border { Height = 1, Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush), Margin = new Thickness(0, 10, 0, 2) });
                    content.Children.Add(new TextBlock
                    {
                        Text = "Compatibility Settings",
                        FontSize = 13,
                        Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
                        Margin = new Thickness(0, 4, 0, 0),
                    });

                    var settingsGrid = new Grid { ColumnSpacing = 12, RowSpacing = 6 };
                    settingsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    settingsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110, GridUnitType.Pixel) });
                    settingsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    settingsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110, GridUnitType.Pixel) });

                    int totalRows = (upgradeKeys.Count + 1) / 2;
                    for (int r = 0; r < totalRows; r++)
                        settingsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                    for (int i = 0; i < upgradeKeys.Count; i++)
                    {
                        var kv = upgradeKeys[i];
                        int row = i / 2;
                        int col = (i % 2) * 2; // 0 or 2

                        bool isSetPath = kv.Key.Equals("Set_Path", StringComparison.OrdinalIgnoreCase);
                        bool isDumpLut = kv.Key.Equals("DumpLUTShaders", StringComparison.OrdinalIgnoreCase);
                        bool isBinaryToggle = isSetPath || isDumpLut;

                        var label = new TextBlock
                        {
                            Text = isSetPath ? "Upgrade Path" : isDumpLut ? "Dump LUT Shaders" : kv.Key,
                            FontSize = 11,
                            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
                            VerticalAlignment = VerticalAlignment.Center,
                        };
                        Grid.SetRow(label, row);
                        Grid.SetColumn(label, col);
                        settingsGrid.Children.Add(label);

                        var combo = new ComboBox { FontSize = 11, MinWidth = 100, HorizontalAlignment = HorizontalAlignment.Stretch };

                        if (isSetPath) { combo.Items.Add("HDR"); combo.Items.Add("SDR"); }
                        else if (isDumpLut) { combo.Items.Add("Off"); combo.Items.Add("On"); }
                        else { combo.Items.Add("Off"); combo.Items.Add("Output size"); combo.Items.Add("Output ratio"); combo.Items.Add("Any size"); }

                        int.TryParse(kv.Value, out var currentVal);
                        combo.SelectedIndex = isBinaryToggle
                            ? (currentVal >= 0 && currentVal <= 1 ? currentVal : 0)
                            : (currentVal >= 0 && currentVal <= 3 ? currentVal : 0);

                        var capturedKey = kv.Key;
                        combo.SelectionChanged += (s, ev) =>
                        {
                            if (combo.SelectedIndex < 0) return;
                            renodxSection[capturedKey] = combo.SelectedIndex.ToString();
                            try { AuxInstallService.WriteIni(iniPath, ini); }
                            catch (Exception ex) { card.ActionMessage = $"❌ {ex.Message}"; }
                        };

                        Grid.SetRow(combo, row);
                        Grid.SetColumn(combo, col + 1);
                        settingsGrid.Children.Add(combo);
                    }

                    content.Children.Add(settingsGrid);

                    // ── Manifest-driven extra settings ──────────────────────────────────
                    var extraSettings = AuxInstallService.GlobalManifest?.RenodxExtraSettings;
                    if (extraSettings?.Count > 0)
                    {
                        var extraGrid = new Grid { ColumnSpacing = 12, RowSpacing = 6 };
                        extraGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        extraGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110, GridUnitType.Pixel) });
                        extraGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        extraGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110, GridUnitType.Pixel) });

                        int extraTotal = (extraSettings.Count + 1) / 2;
                        for (int r = 0; r < extraTotal; r++)
                            extraGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                        for (int i = 0; i < extraSettings.Count; i++)
                        {
                            var setting = extraSettings[i];
                            int row = i / 2;
                            int col = (i % 2) * 2;

                            var extraLabel = new TextBlock
                            {
                                Text = setting.Label ?? setting.Key,
                                FontSize = 11,
                                Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
                                VerticalAlignment = VerticalAlignment.Center,
                            };
                            Grid.SetRow(extraLabel, row);
                            Grid.SetColumn(extraLabel, col);
                            extraGrid.Children.Add(extraLabel);

                            var extraCombo = new ComboBox { FontSize = 11, MinWidth = 100, HorizontalAlignment = HorizontalAlignment.Stretch };

                            // Use manifest-defined options, or default to Off/On
                            var options = setting.Options?.Count > 0
                                ? setting.Options
                                : new List<RenodxExtraOption> { new() { Value = "0", Name = "Off" }, new() { Value = "1", Name = "On" } };

                            foreach (var opt in options)
                                extraCombo.Items.Add(opt.Name);

                            // Read current value from INI
                            string currentExtraVal = setting.Default;
                            if (renodxSection.TryGetValue(setting.Key, out var existingVal))
                                currentExtraVal = existingVal;
                            var selectedIdx = options.FindIndex(o => o.Value == currentExtraVal);
                            extraCombo.SelectedIndex = selectedIdx >= 0 ? selectedIdx : 0;

                            var capturedSetting = setting;
                            var capturedOptions = options;
                            extraCombo.SelectionChanged += (s, ev) =>
                            {
                                if (extraCombo.SelectedIndex < 0 || extraCombo.SelectedIndex >= capturedOptions.Count) return;
                                renodxSection[capturedSetting.Key] = capturedOptions[extraCombo.SelectedIndex].Value;
                                try { AuxInstallService.WriteIni(iniPath, ini); }
                                catch (Exception ex) { card.ActionMessage = $"❌ {ex.Message}"; }
                            };

                            Grid.SetRow(extraCombo, row);
                            Grid.SetColumn(extraCombo, col + 1);
                            extraGrid.Children.Add(extraCombo);
                        }

                        content.Children.Add(extraGrid);
                    }
                }
            }
            else
            {
                content.Children.Add(new TextBlock
                {
                    Text = "Run the game once with RenoDX installed to generate settings.",
                    FontSize = 11,
                    Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
                    FontStyle = Windows.UI.Text.FontStyle.Italic,
                    Margin = new Thickness(0, 4, 0, 0),
                });
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

        // ── Preset Export/Import buttons (side by side) ───────────────────────
        content.Children.Add(new Border { Height = 1, Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush), Margin = new Thickness(0, 10, 0, 2) });
        content.Children.Add(new TextBlock
        {
            Text = "RenoDX Presets",
            FontSize = 13,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            Margin = new Thickness(0, 8, 0, 0),
        });
        var presetRow = new StackPanel { Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal, Spacing = 8 };

        var exportBtn = new Button
        {
            Content = "Export Presets",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush),
            Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 7, 12, 7), FontSize = 12,
            IsEnabled = File.Exists(iniPath),
        };
        exportBtn.Click += async (s, ev) =>
        {
            try
            {
                var lines = File.ReadAllLines(iniPath);
                var presetLines = new List<string>();
                bool inPreset = false;
                foreach (var line in lines)
                {
                    if (line.TrimStart().StartsWith("[renodx-preset", StringComparison.OrdinalIgnoreCase))
                    {
                        inPreset = true;
                        if (presetLines.Count > 0) presetLines.Add("");
                        presetLines.Add(line);
                    }
                    else if (line.TrimStart().StartsWith('[') && inPreset)
                    {
                        inPreset = false;
                    }
                    else if (inPreset)
                    {
                        presetLines.Add(line);
                    }
                }

                if (presetLines.Count == 0)
                {
                    card.ActionMessage = "❌ No [renodx-preset*] sections found.";
                    return;
                }

                // Add header comment
                presetLines.Insert(0, $"; RenoDX Preset exported from: {card.GameName}");
                presetLines.Insert(1, "; To import: place this file in the game folder and click 'Import Presets' in RHI,");
                presetLines.Insert(2, "; or paste the [renodx-preset*] sections into reshade.ini manually.");
                presetLines.Insert(3, "");

                File.WriteAllLines(presetPath, presetLines);
                // Copy as file to clipboard (shows as RHI-RenoDX-Preset.txt in Discord)
                try
                {
                    var storageFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(presetPath);
                    var dp = new Windows.ApplicationModel.DataTransfer.DataPackage();
                    dp.SetStorageItems(new[] { storageFile });
                    Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
                    Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
                }
                catch { /* clipboard copy is best-effort */ }
                card.ActionMessage = $"✅ Exported {presetLines.Count(l => l.StartsWith("["))} preset(s) & copied to clipboard.";
                card.FadeMessage(m => card.ActionMessage = m, card.ActionMessage);
            }
            catch (Exception ex) { card.ActionMessage = $"❌ {ex.Message}"; }
        };
        ToolTipService.SetToolTip(exportBtn, "Save all RenoDX presets to a file and copy to clipboard for sharing.");
        presetRow.Children.Add(exportBtn);

        var importBtn = new Button
        {
            Content = "Import Presets",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush),
            Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 7, 12, 7), FontSize = 12,
            IsEnabled = File.Exists(presetPath) && File.Exists(iniPath),
        };
        importBtn.Click += (s, ev) =>
        {
            try
            {
                // Read preset file, skip comment lines (header)
                var presetLines = File.ReadAllLines(presetPath)
                    .Where(l => !l.TrimStart().StartsWith(';'))
                    .ToArray();
                var iniLines = File.ReadAllLines(iniPath).ToList();

                // Collect preset section names from the backup file
                var presetSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in presetLines)
                {
                    if (line.TrimStart().StartsWith("[renodx-preset", StringComparison.OrdinalIgnoreCase))
                        presetSections.Add(line.Trim());
                }

                // Remove existing preset sections from reshade.ini
                var filtered = new List<string>();
                bool skipping = false;
                foreach (var line in iniLines)
                {
                    if (line.TrimStart().StartsWith("[renodx-preset", StringComparison.OrdinalIgnoreCase))
                    {
                        skipping = true;
                        continue;
                    }
                    if (line.TrimStart().StartsWith('[') && skipping)
                        skipping = false;
                    if (!skipping)
                        filtered.Add(line);
                }

                // Append imported presets at the end
                filtered.Add("");
                filtered.AddRange(presetLines);

                File.WriteAllLines(iniPath, filtered);
                card.ActionMessage = $"✅ Imported {presetSections.Count} preset(s).";
                card.FadeMessage(m => card.ActionMessage = m, card.ActionMessage);
            }
            catch (Exception ex) { card.ActionMessage = $"❌ {ex.Message}"; }
        };
        if (!File.Exists(presetPath))
            ToolTipService.SetToolTip(importBtn, "No RHI-RenoDX-Preset.txt file found. Export first.");
        else
            ToolTipService.SetToolTip(importBtn, "Restore presets from the exported backup file into reshade.ini.");
        presetRow.Children.Add(importBtn);
        content.Children.Add(presetRow);

        var dialog = new ContentDialog
        {
            Title = "RenoDX Settings",
            Content = new ScrollViewer { Content = content, MaxHeight = 620, Padding = new Thickness(0, 0, 16, 0) },
            CloseButtonText = "Close",
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 800.0;
        await DialogService.ShowSafeAsync(dialog);
        _detailPanelBuilder?.UpdateDetailComponentRows(card);
    }

    private async void UlCogButton_Click(object sender, RoutedEventArgs e)
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
            catch { /* ignore access errors */ }
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
                catch { /* use global default */ }
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

}