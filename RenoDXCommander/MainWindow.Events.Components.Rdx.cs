// MainWindow.Events.Components.Rdx.cs — RenoDX cog dialog handler (RdxCogButton_Click).

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
    internal async void RdxCogButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;
            if (string.IsNullOrEmpty(card.InstallPath)) return;

            var installPath = card.InstallPath;
            var iniPath = Path.Combine(installPath, "reshade.ini");
            var presetPath = Path.Combine(installPath, "RHI-RenoDX-Preset.txt");
            
            // Read file state on background thread before building dialog
            bool iniExists = false;
            bool presetExists = false;
            AuxInstallService.OrderedDict? renodxSection = null;
            string currentNits = "";
            
            await Task.Run(() =>
            {
                iniExists = File.Exists(iniPath);
                presetExists = File.Exists(presetPath);
                
                if (iniExists)
                {
                    try
                    {
                        var ini = AuxInstallService.ParseIni(File.ReadAllLines(iniPath));
                        
                        // Extract current nits value
                        var presetWithNits = ini.FirstOrDefault(kv =>
                            kv.Key.StartsWith("renodx-preset", StringComparison.OrdinalIgnoreCase)
                            && kv.Value.ContainsKey("ToneMapPeakNits"));
                        if (presetWithNits.Value != null && presetWithNits.Value.TryGetValue("ToneMapPeakNits", out var nv))
                            currentNits = double.TryParse(nv, out var dv) ? ((int)dv).ToString() : nv;
                        
                        // Extract renodx section
                        if (ini.TryGetValue("renodx", out var section))
                            renodxSection = section;
                    }
                    catch (Exception ex) { CrashReporter.Log($"[MainWindow.RdxCogButton_Click] Failed to read RenoDX ini, using fallback — {ex.Message}"); }
                }
            });
            
            var content = new StackPanel { Spacing = 8 };
            bool hasRenoDxMod = !card.IsRtxHdrEnabled && (card.Mod?.SnapshotUrl != null || card.Status == GameStatus.Installed || card.Status == GameStatus.UpdateAvailable);

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
            var topGrid = new Grid { ColumnSpacing = 12, RowSpacing = 6 };
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });  // 0: left label
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110, GridUnitType.Pixel) }); // 1: left combo
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Pixel) });  // 2: vertical divider
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });  // 3: right label
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110, GridUnitType.Pixel) }); // 4: right combo
            int topGridRow = 0;

            if (card.UeExtendedToggleVisibility == Visibility.Visible)
            {
                topGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var ueLabel = new TextBlock { Text = "UE-Extended", FontSize = 11, Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetRow(ueLabel, topGridRow);
                Grid.SetColumn(ueLabel, 0);
                topGrid.Children.Add(ueLabel);

                var ueCombo = new ComboBox { FontSize = 11, MinWidth = 100, HorizontalAlignment = HorizontalAlignment.Stretch };
                ueCombo.Items.Add("Off");
                ueCombo.Items.Add("On");
                ToolTipService.SetToolTip(ueCombo, "Switch between using UE-Extended or the game specific mod/generic Unreal RenoDX mod.");
                ueCombo.SelectedIndex = card.UseUeExtended ? 1 : 0;
                ueCombo.SelectionChanged += (s, ev) =>
                {
                    bool enable = ueCombo.SelectedIndex == 1;
                    if (enable != card.UseUeExtended)
                        ViewModel.ToggleUeExtended(card);
                };
                Grid.SetRow(ueCombo, topGridRow);
                Grid.SetColumn(ueCombo, 1);
                topGrid.Children.Add(ueCombo);
                topGridRow++;
            }

            // ── Peak Nits row (inside topGrid for alignment) ──────────────────────
            if (hasRenoDxMod && iniExists)
            {
                topGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                // Label in column 0
                var nitsLabel = new TextBlock
                {
                    Text = "Set Maximum Nits",
                    FontSize = 11,
                    Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetRow(nitsLabel, topGridRow);
                Grid.SetColumn(nitsLabel, 0);
                Grid.SetRowSpan(nitsLabel, 2);
                topGrid.Children.Add(nitsLabel);

                var nitsBox = new TextBox
                {
                    Text = currentNits,
                    FontSize = 11,
                    PlaceholderText = "nits",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
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
                    CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 5, 10, 5), FontSize = 11,
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
                            catch (Exception ex) { CrashReporter.Log($"[MainWindow.RdxCogButton_Click] Failed to read monitor peak luminance — {ex.Message}"); }
                        }
                        var peakNits = (int)maxNitsFound;
                        if (peakNits <= 0) { card.ActionMessage = "❌ Could not read peak brightness."; return; }

                        ApplyNitsValue(peakNits.ToString());
                    }
                    catch (Exception ex) { card.ActionMessage = $"❌ {ex.Message}"; }
                };

                // nitsBox on its own row
                Grid.SetRow(nitsBox, topGridRow);
                Grid.SetColumn(nitsBox, 1);
                topGrid.Children.Add(nitsBox);
                topGridRow++;

                // Auto button on the next row (doesn't inflate the nits row height)
                topGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(autoBtn, topGridRow);
                Grid.SetColumn(autoBtn, 1);
                topGrid.Children.Add(autoBtn);
                topGridRow++;
            }

            // ── Right column: Upgrade Path, HDR Settings, LUT ─────────────────────
            // These sit in cols 2+3 of topGrid, alongside the left column items.
            // A vertical divider Border spans all rows as a visual separator.
            int rightGridRow = 0;
            bool hasCustomEngineIniFile = AuxInstallService.GlobalManifest?.EngineIniFiles?.ContainsKey(card.GameName) == true;

            // Add vertical divider spanning all rows (added after rows are defined, set RowSpan below)
            var vertDivider = new Border
            {
                Width = 1,
                Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(0),
            };
            Grid.SetColumn(vertDivider, 2);
            Grid.SetRow(vertDivider, 0);
            // RowSpan set after all rows are added

            // ── Upgrade Path (right col row 0) ────────────────────────────────────
            if (iniExists && renodxSection != null && renodxSection.ContainsKey("Set_Path"))
            {
                while (topGrid.RowDefinitions.Count <= rightGridRow)
                    topGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var upLabel = new TextBlock
                {
                    Text = "Upgrade Path",
                    FontSize = 11,
                    Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetRow(upLabel, rightGridRow);
                Grid.SetColumn(upLabel, 3);
                topGrid.Children.Add(upLabel);

                var upCombo = new ComboBox { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Stretch };
                upCombo.Items.Add("HDR / Off");
                upCombo.Items.Add("SDR / On");
                int.TryParse(renodxSection["Set_Path"], out var setPathVal);
                upCombo.SelectedIndex = setPathVal >= 0 && setPathVal < 2 ? setPathVal : 0;
                upCombo.SelectionChanged += (s, ev) =>
                {
                    if (upCombo.SelectedIndex < 0) return;
                    try
                    {
                        var freshIni = AuxInstallService.ParseIni(File.ReadAllLines(iniPath));
                        if (freshIni.TryGetValue("renodx", out var sec))
                        {
                            sec["Set_Path"] = upCombo.SelectedIndex.ToString();
                            AuxInstallService.WriteIni(iniPath, freshIni);
                        }
                    }
                    catch (Exception ex) { card.ActionMessage = $"❌ {ex.Message}"; }
                };
                Grid.SetRow(upCombo, rightGridRow);
                Grid.SetColumn(upCombo, 4);
                topGrid.Children.Add(upCombo);
                rightGridRow++;
            }

            // ── HDR Settings (right col, UE-Extended only) ────────────────────────
            if (card.UseUeExtended && card.Status == GameStatus.Installed)
            {
                while (topGrid.RowDefinitions.Count <= rightGridRow)
                    topGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var hdrLabel = new TextBlock
                {
                    Text = "Engine.ini HDR",
                    FontSize = 11,
                    Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetRow(hdrLabel, rightGridRow);
                Grid.SetColumn(hdrLabel, 3);
                topGrid.Children.Add(hdrLabel);

                var hdrCombo = new ComboBox { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Stretch };
                hdrCombo.Items.Add("Off");
                hdrCombo.Items.Add("On");
                if (hasCustomEngineIniFile)
                {
                    hdrCombo.IsEnabled = false;
                    hdrCombo.Opacity = 0.4;
                    ToolTipService.SetToolTip(hdrCombo, "Managed by custom Engine.ini file — not available for this game.");
                    hdrCombo.SelectedIndex = 0;
                }
                else
                {
                    ToolTipService.SetToolTip(hdrCombo, "Deploys Engine.ini with HDR flags for games that don't have an ingame HDR option. Disable for SDR.");
                    bool hdrActive = card.InstalledRecord?.EngineIniHdr ?? true;
                    hdrCombo.SelectedIndex = hdrActive ? 1 : 0;
                    hdrCombo.SelectionChanged += (s, ev) =>
                    {
                        if (hdrCombo.SelectedIndex == 1)
                        {
                            AuxInstallService.ApplyEngineIniHdrSettings(card.InstallPath, card.EngineIniProjectOverride, card.GameName, card.Source);
                            if (card.InstalledRecord != null) card.InstalledRecord.EngineIniHdr = true;
                            card.ActionMessage = "✅ Engine.ini HDR settings deployed.";
                        }
                        else
                        {
                            AuxInstallService.RemoveEngineIniHdrSettings(card.InstallPath, card.EngineIniProjectOverride, card.GameName, card.Source);
                            if (card.InstalledRecord != null) card.InstalledRecord.EngineIniHdr = false;
                            card.ActionMessage = "✅ Engine.ini HDR settings removed.";
                        }
                        if (card.InstalledRecord != null)
                            App.Services.GetRequiredService<IModInstallService>().SaveRecordPublic(card.InstalledRecord);
                        card.FadeMessage(m => card.ActionMessage = m, card.ActionMessage);
                    };
                }
                Grid.SetRow(hdrCombo, rightGridRow);
                Grid.SetColumn(hdrCombo, 4);
                topGrid.Children.Add(hdrCombo);
                rightGridRow++;
            }

            // ── LUT Update Every Frame (right col, Unreal games with mod installed) ─
            if (card.EngineHint?.Contains("Unreal") == true && card.Status == GameStatus.Installed)
            {
                while (topGrid.RowDefinitions.Count <= rightGridRow)
                    topGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var lutLabel = new TextBlock
                {
                    Text = "LUT Update Every Frame",
                    FontSize = 11,
                    Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetRow(lutLabel, rightGridRow);
                Grid.SetColumn(lutLabel, 3);
                topGrid.Children.Add(lutLabel);

                var lutCombo = new ComboBox { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Stretch };
                lutCombo.Items.Add("Off");
                lutCombo.Items.Add("On");
                if (hasCustomEngineIniFile)
                {
                    lutCombo.IsEnabled = false;
                    lutCombo.Opacity = 0.4;
                    ToolTipService.SetToolTip(lutCombo, "Managed by custom Engine.ini file — not available for this game.");
                    lutCombo.SelectedIndex = 0;
                }
                else
                {
                    ToolTipService.SetToolTip(lutCombo, "Writes r.LUT.UpdateEveryFrame=1 to Engine.ini. Ensures the game recalculates LUTs each frame for accurate HDR color.");
                    bool lutActive = card.InstalledRecord?.EngineIniLut ?? true;
                    lutCombo.SelectedIndex = lutActive ? 1 : 0;
                    lutCombo.SelectionChanged += (s, ev) =>
                    {
                        if (lutCombo.SelectedIndex == 1)
                        {
                            AuxInstallService.ApplyEngineIniLutSetting(card.InstallPath, card.EngineIniProjectOverride, card.GameName, card.Source);
                            if (card.InstalledRecord != null) card.InstalledRecord.EngineIniLut = true;
                            card.ActionMessage = "✅ LUT Update Every Frame enabled in Engine.ini.";
                        }
                        else
                        {
                            AuxInstallService.RemoveEngineIniLutSetting(card.InstallPath, card.EngineIniProjectOverride, card.GameName, card.Source);
                            if (card.InstalledRecord != null) card.InstalledRecord.EngineIniLut = false;
                            card.ActionMessage = "✅ LUT Update Every Frame removed from Engine.ini.";
                        }
                        if (card.InstalledRecord != null)
                            App.Services.GetRequiredService<IModInstallService>().SaveRecordPublic(card.InstalledRecord);
                        card.FadeMessage(m => card.ActionMessage = m, card.ActionMessage);
                    };
                }
                Grid.SetRow(lutCombo, rightGridRow);
                Grid.SetColumn(lutCombo, 4);
                topGrid.Children.Add(lutCombo);
                rightGridRow++;
            }

            // Add vertical divider now that we know the total row count
            if (rightGridRow > 0)
            {
                Grid.SetRowSpan(vertDivider, Math.Max(topGrid.RowDefinitions.Count, 1));
                topGrid.Children.Add(vertDivider);
            }

            content.Children.Add(topGrid);

            // ── Compatibility Settings from [renodx] section ──────────────────────
            if (iniExists && renodxSection != null)
            {
                var upgradeKeys = renodxSection
                    .Where(kv => (kv.Key.StartsWith("Upgrade_", StringComparison.OrdinalIgnoreCase)
                                  && !kv.Key.Equals("Upgrade_UseSCRGB", StringComparison.OrdinalIgnoreCase)
                                  && !kv.Key.Equals("Upgrade_CopyDestinations", StringComparison.OrdinalIgnoreCase))
                              || kv.Key.Equals("DumpLUTShaders", StringComparison.OrdinalIgnoreCase)
                              || kv.Key.Equals("SettingsMode",           StringComparison.OrdinalIgnoreCase)
                              || kv.Key.Equals("Proxy_Revert_State",     StringComparison.OrdinalIgnoreCase)
                              // Unity engine settings
                              || kv.Key.Equals("Use_Swapchain_Proxy",    StringComparison.OrdinalIgnoreCase)
                              || kv.Key.Equals("Swapchain_Encoding",     StringComparison.OrdinalIgnoreCase)
                              || kv.Key.Equals("Force_Pipeline_Cloning", StringComparison.OrdinalIgnoreCase)
                              || kv.Key.Equals("ForceBorderless",        StringComparison.OrdinalIgnoreCase)
                              || kv.Key.Equals("PreventFullscreen",      StringComparison.OrdinalIgnoreCase)
                              || kv.Key.Equals("Blit_Copy_Hack",         StringComparison.OrdinalIgnoreCase)
                              || kv.Key.Equals("Use_Resource_Cloning",   StringComparison.OrdinalIgnoreCase)
                              || kv.Key.Equals("Upgrade_CopyDestinations", StringComparison.OrdinalIgnoreCase)
                              || kv.Key.Equals("Upgrade_UseSCRGB",       StringComparison.OrdinalIgnoreCase)
                              || kv.Key.Equals("Tonemap_Offset",         StringComparison.OrdinalIgnoreCase)
                              || kv.Key.Equals("Scaling_Offset",         StringComparison.OrdinalIgnoreCase))
                    .OrderBy(kv => kv.Key.Equals("DumpLUTShaders", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
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
                        bool isSwapchainProxy = kv.Key.Equals("Use_Swapchain_Proxy", StringComparison.OrdinalIgnoreCase);
                        bool isSwapchainEncoding = kv.Key.Equals("Swapchain_Encoding", StringComparison.OrdinalIgnoreCase);
                        bool isUnityBool = kv.Key.Equals("Force_Pipeline_Cloning", StringComparison.OrdinalIgnoreCase)
                                        || kv.Key.Equals("ForceBorderless",        StringComparison.OrdinalIgnoreCase)
                                        || kv.Key.Equals("PreventFullscreen",      StringComparison.OrdinalIgnoreCase)
                                        || kv.Key.Equals("Use_Resource_Cloning",   StringComparison.OrdinalIgnoreCase);
                        bool isOffsetRange = kv.Key.Equals("Tonemap_Offset",       StringComparison.OrdinalIgnoreCase)
                                          || kv.Key.Equals("Scaling_Offset",       StringComparison.OrdinalIgnoreCase);
                        bool isBlitCopyHack  = kv.Key.Equals("Blit_Copy_Hack",             StringComparison.OrdinalIgnoreCase);
                        bool isCopyDest      = kv.Key.Equals("Upgrade_CopyDestinations",   StringComparison.OrdinalIgnoreCase);
                        bool isUseScrGb      = kv.Key.Equals("Upgrade_UseSCRGB",           StringComparison.OrdinalIgnoreCase);
                        bool isSettingsMode  = kv.Key.Equals("SettingsMode",               StringComparison.OrdinalIgnoreCase);
                        bool isSwapChainCompat = kv.Key.Equals("Upgrade_SwapChainCompatibility", StringComparison.OrdinalIgnoreCase);
                        bool isProxyRevert   = kv.Key.Equals("Proxy_Revert_State",         StringComparison.OrdinalIgnoreCase);
                        bool isBinaryToggle = isSetPath || isDumpLut || isUnityBool;

                        // Label text
                        string labelText = isSetPath ? "Upgrade Path"
                            : isDumpLut          ? "Dump LUT Shaders"
                            : isSwapchainProxy   ? "Swapchain Proxy"
                            : isSwapchainEncoding? "Swapchain Encoding"
                            : isUseScrGb         ? "Swapchain Format"
                            : isCopyDest         ? "Copy Destinations"
                            : isSettingsMode     ? "Proxy Revert State"
                            : isSwapChainCompat  ? "Swapchain Compat"
                            : isProxyRevert      ? "Proxy Revert State"
                            : kv.Key.StartsWith("Upgrade_", StringComparison.OrdinalIgnoreCase) ? kv.Key.Substring(8)
                            : kv.Key.Replace('_', ' ');

                        var label = new TextBlock
                        {
                            Text = labelText,
                            FontSize = 11,
                            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
                            VerticalAlignment = VerticalAlignment.Center,
                        };                    Grid.SetRow(label, row);
                        Grid.SetColumn(label, col);
                        settingsGrid.Children.Add(label);

                        var combo = new ComboBox { FontSize = 11, MinWidth = 100, HorizontalAlignment = HorizontalAlignment.Stretch };

                        if (isSetPath) { combo.Items.Add("HDR / Off"); combo.Items.Add("SDR / On"); }
                        else if (isDumpLut) { combo.Items.Add("Off"); combo.Items.Add("On"); }
                        else if (isSwapchainProxy) { combo.Items.Add("Off"); combo.Items.Add("On"); combo.Items.Add("On (Compat)"); }
                        else if (isSwapchainEncoding) { combo.Items.Add("Linear"); combo.Items.Add("Gamma"); }
                        else if (isBlitCopyHack) { combo.Items.Add("Off"); combo.Items.Add("Auto"); combo.Items.Add("On"); combo.Items.Add("Scaling Only"); }
                        else if (isCopyDest) { combo.Items.Add("Off"); combo.Items.Add("On"); combo.Items.Add("Auto Upgrade"); }
                        else if (isUseScrGb) { combo.Items.Add("HDR10"); combo.Items.Add("scRGB"); }
                        else if (isSettingsMode || isSwapChainCompat || isProxyRevert) { combo.Items.Add("Off"); combo.Items.Add("On"); }
                        else if (isOffsetRange) { for (int v = 0; v <= 5; v++) combo.Items.Add(v.ToString()); }
                        else if (isUnityBool) { combo.Items.Add("Off"); combo.Items.Add("On"); }
                        else { combo.Items.Add("Off"); combo.Items.Add("Output size"); combo.Items.Add("Output ratio"); combo.Items.Add("Any size"); }

                        int.TryParse(kv.Value, out var currentVal);
                        combo.SelectedIndex = (isBinaryToggle || isSwapchainProxy || isSwapchainEncoding || isOffsetRange || isBlitCopyHack || isCopyDest || isUseScrGb || isSettingsMode || isSwapChainCompat || isProxyRevert)
                            ? (currentVal >= 0 && currentVal < combo.Items.Count ? currentVal : 0)
                            : (currentVal >= 0 && currentVal <= 3 ? currentVal : 0);

                        var capturedKey = kv.Key;
                        combo.SelectionChanged += (s, ev) =>
                        {
                            if (combo.SelectedIndex < 0) return;
                            try
                            {
                                var iniForWrite = AuxInstallService.ParseIni(File.ReadAllLines(iniPath));
                                if (iniForWrite.TryGetValue("renodx", out var section))
                                {
                                    section[capturedKey] = combo.SelectedIndex.ToString();
                                    AuxInstallService.WriteIni(iniPath, iniForWrite);
                                }
                            }
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
                        // Append to the existing settings grid (continue from where hardcoded keys left off)
                        int startIdx = upgradeKeys.Count;
                        int extraRows = (startIdx + extraSettings.Count + 1) / 2 - settingsGrid.RowDefinitions.Count;
                        for (int r = 0; r < extraRows; r++)
                            settingsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                        for (int i = 0; i < extraSettings.Count; i++)
                        {
                            var setting = extraSettings[i];
                            int idx = startIdx + i;
                            int row = idx / 2;
                            int col = (idx % 2) * 2;

                            var extraLabel = new TextBlock
                            {
                                Text = setting.Label ?? setting.Key,
                                FontSize = 11,
                                Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
                                VerticalAlignment = VerticalAlignment.Center,
                            };
                            Grid.SetRow(extraLabel, row);
                            Grid.SetColumn(extraLabel, col);
                            settingsGrid.Children.Add(extraLabel);

                            var extraCombo = new ComboBox { FontSize = 11, MinWidth = 100, HorizontalAlignment = HorizontalAlignment.Stretch };

                            var options = setting.Options?.Count > 0
                                ? setting.Options
                                : new List<RenodxExtraOption> { new() { Value = "0", Name = "Off" }, new() { Value = "1", Name = "On" } };

                            foreach (var opt in options)
                                extraCombo.Items.Add(opt.Name);

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
                                try
                                {
                                    var iniForWrite = AuxInstallService.ParseIni(File.ReadAllLines(iniPath));
                                    if (iniForWrite.TryGetValue("renodx", out var section))
                                    {
                                        section[capturedSetting.Key] = capturedOptions[extraCombo.SelectedIndex].Value;
                                        AuxInstallService.WriteIni(iniPath, iniForWrite);
                                    }
                                }
                                catch (Exception ex) { card.ActionMessage = $"❌ {ex.Message}"; }
                            };

                            Grid.SetRow(extraCombo, row);
                            Grid.SetColumn(extraCombo, col + 1);
                            settingsGrid.Children.Add(extraCombo);
                        }
                    }
                }
            }
            else if (iniExists)
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
            if (hasRenoDxMod)
            {
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
                IsEnabled = iniExists,
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
                    catch (Exception ex) { CrashReporter.Log($"[MainWindow.RdxCogButton_Click] Failed to copy preset file to clipboard — {ex.Message}"); }
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
                IsEnabled = presetExists && iniExists,
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
            if (!presetExists)
                ToolTipService.SetToolTip(importBtn, "No RHI-RenoDX-Preset.txt file found. Export first.");
            else
                ToolTipService.SetToolTip(importBtn, "Restore presets from the exported backup file into reshade.ini.");
            presetRow.Children.Add(importBtn);
            content.Children.Add(presetRow);
            } // end hasRenoDxMod

            // ── RTX HDR Toggle ─────────────────────────────────────────────────────
            // autoHdrCombo is declared here at method scope so the RTX HDR SelectionChanged
            // handler can reference it for mutual exclusivity (before the Auto HDR section builds it).
            ComboBox? autoHdrCombo = null;
            content.Children.Add(new Border { Height = 1, Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush), Margin = new Thickness(0, 10, 0, 2) });
            content.Children.Add(new TextBlock
            {
                Text = "RTX HDR",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            });
            content.Children.Add(new TextBlock
            {
                Text = "Requires NVIDIA App with Overlay and Game Filters enabled.",
                FontSize = 11,
                Foreground = UIFactory.Brush(ResourceKeys.InlineDescriptionBrush),
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4),
            });

            var rtxHdrCombo = new ComboBox { FontSize = 11, MinWidth = 100 };
            rtxHdrCombo.Items.Add("Off");
            rtxHdrCombo.Items.Add("On");

            var gameNameService = App.Services.GetRequiredService<IGameNameService>();
            // Read live driver state off the UI thread — GetRtxHdrEnable calls _sessionLock.Wait(5000)
            var dlssPresetServiceCog = App.Services.GetRequiredService<DlssPresetService>();
            bool isRtxHdrEnabled;
            if (dlssPresetServiceCog.IsSupported && !string.IsNullOrEmpty(card.InstallPath))
            {
                var capturedName = card.GameName; var capturedPath = card.InstallPath;
                isRtxHdrEnabled = await Task.Run(() => dlssPresetServiceCog.GetRtxHdrEnable(capturedName, capturedPath) == 0x01);
            }
            else
            {
                isRtxHdrEnabled = gameNameService.RtxHdrGames.Contains(card.GameName);
            }
            // Sync persisted state to match driver
            if (isRtxHdrEnabled) gameNameService.RtxHdrGames.Add(card.GameName);
            else gameNameService.RtxHdrGames.Remove(card.GameName);
            card.IsRtxHdrEnabled = isRtxHdrEnabled;
            rtxHdrCombo.SelectedIndex = isRtxHdrEnabled ? 1 : 0;

            rtxHdrCombo.SelectionChanged += async (s, ev) =>
            {
                bool enable = rtxHdrCombo.SelectedIndex == 1;
                var dlssPresetService = App.Services.GetRequiredService<DlssPresetService>();

                if (enable)
                {
                    gameNameService.RtxHdrGames.Add(card.GameName);
                    card.IsRtxHdrEnabled = true;

                    // Mutual exclusivity: if Auto HDR is on, turn it off
                    if (card.IsAutoHdrEnabled)
                    {
                        gameNameService.AutoHdrGames.Remove(card.GameName);
                        card.IsAutoHdrEnabled = false;
                        autoHdrCombo!.SelectedIndex = 0;
                        var capturedAutoHdrSvc = App.Services.GetRequiredService<IAutoHdrService>();
                        var capturedAutoHdrCard = card;
                        _ = Task.Run(() =>
                        {
                            var exePath = capturedAutoHdrSvc.ResolveExePath(
                                capturedAutoHdrCard.GameName, capturedAutoHdrCard.InstallPath,
                                gameNameService.LaunchExeOverrides, ViewModel.Manifest?.LaunchExeOverrides);
                            if (!string.IsNullOrEmpty(exePath)) capturedAutoHdrSvc.Disable(exePath);
                        });
                    }

                    // Uninstall RenoDX if installed
                    if (card.Status == GameStatus.Installed && card.InstalledRecord != null)
                    {
                        ViewModel.UninstallMod(card);
                    }

                    // Set RTX HDR profile settings (Allow + Enable + sensible defaults)
                    // Default to Gamma 2.2 (Contrast = +25, stored = 125) — matches conventional SDR gamma
                    var enablePeakNits = ViewModel.Settings.PeakNits > 0 ? ViewModel.Settings.PeakNits : 510;
                    // Calculate ITU-correct Middle Grey for Gamma 2.2 at the user's peak nits
                    // paperWhite lookup table: (peak, pw nits) — interpolated
                    static double Lerp(double a, double b, double t) => a + t * (b - a);
                    double enablePaperWhite;
                    (double peak, double pw)[] ituTable = { (400,101),(600,138),(800,172),(1000,203),(1500,276),(2000,343) };
                    if (enablePeakNits <= ituTable[0].peak) enablePaperWhite = ituTable[0].pw;
                    else if (enablePeakNits >= ituTable[^1].peak) enablePaperWhite = ituTable[^1].pw;
                    else
                    {
                        enablePaperWhite = ituTable[^1].pw;
                        for (int i = 0; i < ituTable.Length - 1; i++)
                        {
                            if (enablePeakNits >= ituTable[i].peak && enablePeakNits <= ituTable[i+1].peak)
                            {
                                double t = (enablePeakNits - ituTable[i].peak) / (ituTable[i+1].peak - ituTable[i].peak);
                                enablePaperWhite = Lerp(ituTable[i].pw, ituTable[i+1].pw, t);
                                break;
                            }
                        }
                    }
                    var enableMidGrey = (uint)Math.Clamp((int)Math.Round(enablePaperWhite * Math.Pow(0.5, 2.2)), 10, 100);
                    var capturedEnablePeakNits = enablePeakNits;
                    var capturedGameName = card.GameName;
                    var capturedInstallPath = card.InstallPath;
                    _ = Task.Run(() =>
                    {
                        dlssPresetService.SetRtxHdrEnable(capturedGameName, capturedInstallPath, 0x01);
                        dlssPresetService.SetRtxHdrPeakBrightness(capturedGameName, capturedInstallPath, (uint)capturedEnablePeakNits);
                        dlssPresetService.SetRtxHdrContrast(capturedGameName, capturedInstallPath, 125);
                        dlssPresetService.SetRtxHdrSaturation(capturedGameName, capturedInstallPath, 75);
                        dlssPresetService.SetRtxHdrMiddleGrey(capturedGameName, capturedInstallPath, enableMidGrey);
                        CrashReporter.Log($"[RdxCogButton_Click] RTX HDR enabled for '{capturedGameName}': PeakNits={capturedEnablePeakNits}, Contrast=125 (Gamma 2.2), Sat=75 (-25), MidGrey={enableMidGrey}");
                    });
                }
                else
                {
                    gameNameService.RtxHdrGames.Remove(card.GameName);
                    card.IsRtxHdrEnabled = false;

                    var capturedGameNameD = card.GameName;
                    var capturedInstallPathD = card.InstallPath;
                    _ = Task.Run(() =>
                    {
                        dlssPresetService.SetRtxHdrEnable(capturedGameNameD, capturedInstallPathD, 0x00);
                        dlssPresetService.SetRtxHdrContrast(capturedGameNameD, capturedInstallPathD, 100);
                        dlssPresetService.SetRtxHdrSaturation(capturedGameNameD, capturedInstallPathD, 100);
                        dlssPresetService.SetRtxHdrPeakBrightness(capturedGameNameD, capturedInstallPathD, 0);
                        dlssPresetService.SetRtxHdrMiddleGrey(capturedGameNameD, capturedInstallPathD, 50);
                        dlssPresetService.DeleteSettingRaw(capturedGameNameD, capturedInstallPathD, 0x00432F84);
                        CrashReporter.Log($"[RdxCogButton_Click] RTX HDR disabled for '{capturedGameNameD}' — all settings deleted from profile");
                    });
                }

                card.NotifyAll();
                ViewModel.SaveSettingsPublic();
                _detailPanelBuilder?.UpdateDetailComponentRows(card);
                PopulateDetailPanel(card);
            };

            var rtxHdrRow = new StackPanel { Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal, Spacing = 12 };
            rtxHdrRow.Children.Add(new TextBlock
            {
                Text = "Enable RTX HDR",
                FontSize = 11,
                Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
                VerticalAlignment = VerticalAlignment.Center,
            });
            rtxHdrRow.Children.Add(rtxHdrCombo);
            content.Children.Add(rtxHdrRow);

            // ── Auto HDR Toggle ────────────────────────────────────────────────────
            content.Children.Add(new Border { Height = 1, Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush), Margin = new Thickness(0, 10, 0, 2) });
            content.Children.Add(new TextBlock
            {
                Text = "Auto HDR",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            });
            content.Children.Add(new TextBlock
            {
                Text = "Windows 11 Auto HDR forced via registry. Works without the global Auto HDR setting enabled.",
                FontSize = 11,
                Foreground = UIFactory.Brush(ResourceKeys.InlineDescriptionBrush),
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4),
            });

            autoHdrCombo = new ComboBox { FontSize = 11, MinWidth = 100 };
            autoHdrCombo.Items.Add("Off");
            autoHdrCombo.Items.Add("On");

            var autoHdrService = App.Services.GetRequiredService<IAutoHdrService>();
            bool isAutoHdrEnabled = gameNameService.AutoHdrGames.Contains(card.GameName);
            int autoHdrStrength = gameNameService.AutoHdrStrengths.TryGetValue(card.GameName, out var savedStrength) ? savedStrength : 50;
            card.IsAutoHdrEnabled = isAutoHdrEnabled;
            card.AutoHdrStrength  = autoHdrStrength;
            autoHdrCombo.SelectedIndex = isAutoHdrEnabled ? 1 : 0;

            autoHdrCombo.SelectionChanged += (s, ev) =>
            {
                bool enableAh = autoHdrCombo.SelectedIndex == 1;

                if (enableAh)
                {
                    gameNameService.AutoHdrGames.Add(card.GameName);
                    card.IsAutoHdrEnabled = true;

                    // Mutual exclusivity: if RTX HDR is on, turn it off first
                    if (card.IsRtxHdrEnabled)
                    {
                        gameNameService.RtxHdrGames.Remove(card.GameName);
                        card.IsRtxHdrEnabled = false;
                        rtxHdrCombo.SelectedIndex = 0;
                        var capturedDlssSvc = App.Services.GetRequiredService<DlssPresetService>();
                        var capturedName2 = card.GameName; var capturedPath2 = card.InstallPath;
                        _ = Task.Run(() =>
                        {
                            capturedDlssSvc.SetRtxHdrEnable(capturedName2, capturedPath2, 0x00);
                            capturedDlssSvc.SetRtxHdrContrast(capturedName2, capturedPath2, 100);
                            capturedDlssSvc.SetRtxHdrSaturation(capturedName2, capturedPath2, 100);
                            capturedDlssSvc.SetRtxHdrPeakBrightness(capturedName2, capturedPath2, 0);
                            capturedDlssSvc.SetRtxHdrMiddleGrey(capturedName2, capturedPath2, 50);
                            capturedDlssSvc.DeleteSettingRaw(capturedName2, capturedPath2, 0x00432F84);
                        });
                    }

                    // Mutual exclusivity: uninstall RenoDX if installed
                    if (card.Status == GameStatus.Installed && card.InstalledRecord != null)
                        ViewModel.UninstallMod(card);

                    // Write registry entry on background thread
                    var capturedEnableCard = card;
                    var capturedEnableStrength = card.AutoHdrStrength;
                    _ = Task.Run(() =>
                    {
                        var exePath = autoHdrService.ResolveExePath(
                            capturedEnableCard.GameName,
                            capturedEnableCard.InstallPath,
                            gameNameService.LaunchExeOverrides,
                            ViewModel.Manifest?.LaunchExeOverrides);
                        if (!string.IsNullOrEmpty(exePath))
                            autoHdrService.Enable(exePath, capturedEnableStrength);
                        else
                            CrashReporter.Log($"[RdxCog.AutoHDR] Could not resolve exe for '{capturedEnableCard.GameName}' — registry not written");
                    });
                }
                else
                {
                    gameNameService.AutoHdrGames.Remove(card.GameName);
                    card.IsAutoHdrEnabled = false;

                    var capturedDisableCard = card;
                    _ = Task.Run(() =>
                    {
                        var exePath = autoHdrService.ResolveExePath(
                            capturedDisableCard.GameName,
                            capturedDisableCard.InstallPath,
                            gameNameService.LaunchExeOverrides,
                            ViewModel.Manifest?.LaunchExeOverrides);
                        if (!string.IsNullOrEmpty(exePath))
                            autoHdrService.Disable(exePath);
                    });
                }

                card.NotifyAll();
                ViewModel.SaveSettingsPublic();
                _detailPanelBuilder?.UpdateDetailComponentRows(card);
                PopulateDetailPanel(card);
            };

            var autoHdrRow = new StackPanel { Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal, Spacing = 12 };
            autoHdrRow.Children.Add(new TextBlock
            {
                Text = "Enable Auto HDR",
                FontSize = 11,
                Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
                VerticalAlignment = VerticalAlignment.Center,
            });
            autoHdrRow.Children.Add(autoHdrCombo);
            content.Children.Add(autoHdrRow);

            var dialog = new ContentDialog
            {
                Title = "RenoDX Settings",
                Content = new ScrollViewer { Content = content, MaxHeight = 620, Padding = new Thickness(0, 0, 16, 0) },
                CloseButtonText = "Close",
                XamlRoot = Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            };
            dialog.Resources["ContentDialogMaxWidth"] = 520.0;
            await DialogService.ShowSafeAsync(dialog);
            _detailPanelBuilder?.UpdateDetailComponentRows(card);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.RdxCogButton_Click] Unhandled error — {ex.Message}\n{ex.StackTrace}");
        }
    }
}
