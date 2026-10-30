// MainWindow.Events.Components.Rdx.cs — RenoDX cog dialog handler (RdxCogButton_Click).
// Built with the shared CogDialog layout. RTX HDR / Auto HDR toggles are in
// MainWindow.Events.Components.Rdx.HdrToggles.cs.

using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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

            var cog = new CogDialog("RenoDX", Content.XamlRoot);
            bool hasRenoDxMod = !card.IsRtxHdrEnabled && (card.Mod?.SnapshotUrl != null || card.Status == GameStatus.Installed || card.Status == GameStatus.UpdateAvailable);

            // ── Main settings (no header) ──────────────────────────────────────────
            // Order: UE-Extended, Max Nits, Engine.ini HDR, LUT Update Every Frame, Upgrade Path
            Action? addNits = hasRenoDxMod && iniExists
                ? () => AddRdxNitsRow(cog, iniPath, currentNits)
                : null;
            Action? addUpgradePath = null;
            if (iniExists && renodxSection != null && renodxSection.ContainsKey("Set_Path"))
            {
                int.TryParse(renodxSection["Set_Path"], out var setPathVal);
                addUpgradePath = () => cog.AddCombo("Upgrade Path", RdxUpgradePathHelp,
                    new List<string> { "HDR / Off", "SDR / On" },
                    setPathVal >= 0 && setPathVal < 2 ? setPathVal : 0,
                    idx => WriteRenodxIniValue(cog, iniPath, "Set_Path", idx.ToString(), "Upgrade Path"));
            }
            AddRdxMainSettings(cog, card, addNits, addUpgradePath);

            // ── Compatibility settings from [renodx] section ──────────────────────
            AddRdxCompatibilitySection(cog, iniPath, iniExists, renodxSection);

            // ── Preset Export/Import ──────────────────────────────────────────────
            if (hasRenoDxMod)
                AddRdxPresetsSection(cog, card, iniPath, presetPath, iniExists, presetExists);

            // ── RTX HDR / Auto HDR ────────────────────────────────────────────────
            await AddRdxHdrToggleSectionsAsync(cog, card);

            await cog.ShowAsync();
            _detailPanelBuilder?.UpdateDetailComponentRows(card);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.RdxCogButton_Click] Unhandled error — {ex.Message}\n{ex.StackTrace}");
        }
    }

    private const string RdxUpgradePathHelp =
        "HDR uses the game's own HDR output. SDR upgrades the game's SDR output to HDR. Saved as Set_Path in reshade.ini.";

    // ── Max Nits ─────────────────────────────────────────────────────────────────

    /// <summary>[TextBox][Auto] row that writes ToneMapPeakNits to every [renodx-preset*] section.</summary>
    private static void AddRdxNitsRow(CogDialog cog, string iniPath, string currentNits)
    {
        var nitsBox = CogDialog.MakeTextBox(currentNits, "nits");

        // Helper: write nits value to all preset sections
        void ApplyNitsValue(string nitsValue)
        {
            if (!int.TryParse(nitsValue, out var val) || val <= 0)
            {
                cog.SetError("Enter a valid number.");
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
                cog.SetSuccess($"Set ToneMapPeakNits to {val} in {updated} preset(s).");
                CrashReporter.Log($"[MainWindow.AddRdxNitsRow] ToneMapPeakNits={val} in {updated} preset(s) of '{iniPath}'");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                CrashReporter.Log($"[MainWindow.AddRdxNitsRow] Failed to write nits — {ex.Message}");
                cog.SetError($"Couldn't save Max Nits: {ex.Message}");
            }
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

        var autoBtn = CogDialog.StyledButton("Auto", CogButtonKind.Primary, compact: true);
        ToolTipService.SetToolTip(autoBtn, "Reads your monitor's peak brightness automatically.");
        autoBtn.Click += async (s, ev) =>
        {
            try
            {
                var devices = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(
                    Windows.Devices.Display.DisplayMonitor.GetDeviceSelector());
                if (devices.Count == 0) { cog.SetError("No display found."); return; }

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
                if (peakNits <= 0) { cog.SetError("Could not read peak brightness."); return; }

                ApplyNitsValue(peakNits.ToString());
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[MainWindow.AddRdxNitsRow] Auto nits failed — {ex.Message}");
                cog.SetError(ex.Message);
            }
        };

        cog.AddRow("Max Nits", CogDialog.Inline(nitsBox, autoBtn),
            "Peak brightness for the RenoDX tone mapper, written to every RenoDX preset in reshade.ini. " +
            "Type a value and press Enter, or click Auto to read it from your monitor.");
    }

    // ── Unreal Engine: UE-Extended, Engine.ini HDR, LUT ──────────────────────────

    /// <summary>
    /// Top block of the RenoDX cog, no header, in this order: UE-Extended, Max Nits,
    /// Engine.ini HDR, LUT Update Every Frame, Upgrade Path. Rows that don't apply are skipped.
    /// </summary>
    private void AddRdxMainSettings(CogDialog cog, GameCardViewModel card, Action? addNits, Action? addUpgradePath)
    {
        bool showUeToggle = card.UeExtendedToggleVisibility == Visibility.Visible;
        bool showEngineHdr = card.UseUeExtended && card.Status == GameStatus.Installed;
        bool showLut = card.EngineHint?.Contains("Unreal") == true && card.Status == GameStatus.Installed;

        bool hasCustomEngineIniFile = AuxInstallService.GlobalManifest?.EngineIniFiles?.ContainsKey(card.GameName) == true;
        const string customEngineIniHelp = "Managed by this game's custom Engine.ini file, so it can't be changed here.";

        if (showUeToggle)
        {
            cog.AddCombo("UE-Extended",
                "Use the UE-Extended RenoDX mod instead of the game's own mod or the generic Unreal mod.",
                RdxOffOn(), card.UseUeExtended ? 1 : 0, idx =>
            {
                bool enable = idx == 1;
                if (enable != card.UseUeExtended)
                    ViewModel.ToggleUeExtended(card);
            });
        }

        addNits?.Invoke();

        if (showEngineHdr)
        {
            if (hasCustomEngineIniFile)
            {
                AddRdxDisabledCombo(cog, "Engine.ini HDR", customEngineIniHelp);
            }
            else
            {
                bool hdrActive = card.InstalledRecord?.EngineIniHdr ?? true;
                cog.AddCombo("Engine.ini HDR",
                    "Adds HDR settings to the game's Engine.ini, for games without an in-game HDR option. Turn off for SDR.",
                    RdxOffOn(), hdrActive ? 1 : 0, idx =>
                {
                    if (idx == 1)
                    {
                        AuxInstallService.ApplyEngineIniHdrSettings(card.InstallPath, card.EngineIniProjectOverride, card.GameName, card.Source);
                        if (card.InstalledRecord != null) card.InstalledRecord.EngineIniHdr = true;
                        cog.SetSuccess("Engine.ini HDR settings deployed.");
                    }
                    else
                    {
                        AuxInstallService.RemoveEngineIniHdrSettings(card.InstallPath, card.EngineIniProjectOverride, card.GameName, card.Source);
                        if (card.InstalledRecord != null) card.InstalledRecord.EngineIniHdr = false;
                        cog.SetSuccess("Engine.ini HDR settings removed.");
                    }
                    if (card.InstalledRecord != null)
                        App.Services.GetRequiredService<IModInstallService>().SaveRecordPublic(card.InstalledRecord);
                });
            }
        }

        if (showLut)
        {
            if (hasCustomEngineIniFile)
            {
                AddRdxDisabledCombo(cog, "LUT Update Every Frame", customEngineIniHelp);
            }
            else
            {
                bool lutActive = card.InstalledRecord?.EngineIniLut ?? true;
                cog.AddCombo("LUT Update Every Frame",
                    "Writes r.LUT.UpdateEveryFrame=1 to Engine.ini so the game recalculates its colour LUTs every frame, for accurate HDR colour.",
                    RdxOffOn(), lutActive ? 1 : 0, idx =>
                {
                    if (idx == 1)
                    {
                        AuxInstallService.ApplyEngineIniLutSetting(card.InstallPath, card.EngineIniProjectOverride, card.GameName, card.Source);
                        if (card.InstalledRecord != null) card.InstalledRecord.EngineIniLut = true;
                        cog.SetSuccess("LUT Update Every Frame enabled in Engine.ini.");
                    }
                    else
                    {
                        AuxInstallService.RemoveEngineIniLutSetting(card.InstallPath, card.EngineIniProjectOverride, card.GameName, card.Source);
                        if (card.InstalledRecord != null) card.InstalledRecord.EngineIniLut = false;
                        cog.SetSuccess("LUT Update Every Frame removed from Engine.ini.");
                    }
                    if (card.InstalledRecord != null)
                        App.Services.GetRequiredService<IModInstallService>().SaveRecordPublic(card.InstalledRecord);
                });
            }
        }

        addUpgradePath?.Invoke();
    }

    /// <summary>Greyed-out Off/On row for settings managed elsewhere.</summary>
    private static void AddRdxDisabledCombo(CogDialog cog, string label, string help)
    {
        var combo = CogDialog.MakeCombo(RdxOffOn(), 0, isEnabled: false);
        var row = cog.AddRow(label, combo, help);
        row.Opacity = CogDialog.DisabledOpacity;
    }

    // ── Compatibility ([renodx] section of reshade.ini) ──────────────────────────

    private static void AddRdxCompatibilitySection(CogDialog cog, string iniPath, bool iniExists, AuxInstallService.OrderedDict? renodxSection)
    {
        if (!iniExists)
        {
            cog.AddSection("Compatibility");
            cog.AddNote("No reshade.ini found in game folder.");
            return;
        }
        if (renodxSection == null)
        {
            cog.AddSection("Compatibility");
            cog.AddNote("Run the game once with RenoDX installed to generate settings.");
            return;
        }

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

        if (upgradeKeys.Count == 0) return;

        cog.AddSection("Compatibility");
        cog.AddNote("These values are read from and saved to the [renodx] section of the game's reshade.ini. " +
                    "Only change them if the mod's instructions tell you to. Hover a setting for details.");

        // Compact two-per-row grid (as before the cog redesign): these are many small toggles
        var cells = new List<RdxCompatCell>();

        foreach (var kv in upgradeKeys)
        {
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
            bool isResourceUpgrade = !(isBinaryToggle || isSwapchainProxy || isSwapchainEncoding || isOffsetRange || isBlitCopyHack
                                       || isCopyDest || isUseScrGb || isSettingsMode || isSwapChainCompat || isProxyRevert);

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

            var items = new List<string>();
            if (isSetPath) { items.Add("HDR / Off"); items.Add("SDR / On"); }
            else if (isDumpLut) { items.Add("Off"); items.Add("On"); }
            else if (isSwapchainProxy) { items.Add("Off"); items.Add("On"); items.Add("On (Compat)"); }
            else if (isSwapchainEncoding) { items.Add("Linear"); items.Add("Gamma"); }
            else if (isBlitCopyHack) { items.Add("Off"); items.Add("Auto"); items.Add("On"); items.Add("Scaling Only"); }
            else if (isCopyDest) { items.Add("Off"); items.Add("On"); items.Add("Auto Upgrade"); }
            else if (isUseScrGb) { items.Add("HDR10"); items.Add("scRGB"); }
            else if (isSettingsMode || isSwapChainCompat || isProxyRevert) { items.Add("Off"); items.Add("On"); }
            else if (isOffsetRange) { for (int v = 0; v <= 5; v++) items.Add(v.ToString()); }
            else if (isUnityBool) { items.Add("Off"); items.Add("On"); }
            else { items.Add("Off"); items.Add("Output size"); items.Add("Output ratio"); items.Add("Any size"); }

            int.TryParse(kv.Value, out var currentVal);
            int selected = !isResourceUpgrade
                ? (currentVal >= 0 && currentVal < items.Count ? currentVal : 0)
                : (currentVal >= 0 && currentVal <= 3 ? currentVal : 0);

            // Specific explanation where the setting's purpose is clear; always name the ini key.
            string? specific = isSetPath ? "HDR uses the game's own HDR output. SDR upgrades the game's SDR output to HDR."
                : isDumpLut         ? "Saves the game's LUT shaders to files. Meant for mod development; leave Off for normal play."
                : isUseScrGb        ? "The HDR output format: HDR10 or scRGB."
                : isSwapchainEncoding ? "Whether the game's output is treated as linear or gamma encoded."
                : kv.Key.Equals("ForceBorderless", StringComparison.OrdinalIgnoreCase) ? "Forces the game to run in a borderless window."
                : kv.Key.Equals("PreventFullscreen", StringComparison.OrdinalIgnoreCase) ? "Stops the game switching to exclusive fullscreen."
                : isResourceUpgrade ? $"Upgrades {labelText} textures to an HDR format. Output size and Output ratio only upgrade textures matching the screen size or aspect ratio; Any size upgrades all of them."
                : null;
            string help = specific != null ? $"{specific} Saved as {kv.Key}." : $"Saved as {kv.Key} in reshade.ini.";

            var capturedKey = kv.Key;
            var capturedLabel = labelText;
            cells.Add(new RdxCompatCell(labelText, help, items, selected,
                idx => WriteRenodxIniValue(cog, iniPath, capturedKey, idx.ToString(), capturedLabel)));
        }

        // ── Manifest-driven extra settings ──────────────────────────────────
        var extraSettings = AuxInstallService.GlobalManifest?.RenodxExtraSettings;
        if (extraSettings?.Count > 0)
        {
            foreach (var setting in extraSettings)
            {
                var options = setting.Options?.Count > 0
                    ? setting.Options
                    : new List<RenodxExtraOption> { new() { Value = "0", Name = "Off" }, new() { Value = "1", Name = "On" } };

                string currentExtraVal = setting.Default;
                if (renodxSection.TryGetValue(setting.Key, out var existingVal))
                    currentExtraVal = existingVal;
                var selectedIdx = options.FindIndex(o => o.Value == currentExtraVal);

                var capturedSetting = setting;
                var capturedOptions = options;
                var label = setting.Label ?? setting.Key;
                cells.Add(new RdxCompatCell(label, $"Saved as {setting.Key} in reshade.ini.",
                    options.Select(o => o.Name).ToList(), selectedIdx >= 0 ? selectedIdx : 0,
                    idx =>
                    {
                        if (idx < 0 || idx >= capturedOptions.Count) return true;
                        return WriteRenodxIniValue(cog, iniPath, capturedSetting.Key, capturedOptions[idx].Value, label);
                    }));
            }
        }

        AddRdxCompactGrid(cog, cells);
    }

    private sealed record RdxCompatCell(string Label, string Help, List<string> Items, int Selected, Func<int, bool> OnChanged);

    /// <summary>
    /// Lays compatibility settings out two per row (label | dropdown | label | dropdown), with the
    /// explanation as a tooltip. Picks that fail to save revert, as in CogDialog.AddCombo.
    /// </summary>
    private static void AddRdxCompactGrid(CogDialog cog, List<RdxCompatCell> cells)
    {
        if (cells.Count == 0) return;
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 6 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        for (int r = 0; r < (cells.Count + 1) / 2; r++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (int i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            int row = i / 2, col = (i % 2) * 2;

            var label = new TextBlock
            {
                Text = cell.Label,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            };
            var combo = CogDialog.MakeCombo(cell.Items, cell.Selected);
            ToolTipService.SetToolTip(label, cell.Help);
            ToolTipService.SetToolTip(combo, cell.Help);

            int last = combo.SelectedIndex;
            bool reverting = false;
            combo.SelectionChanged += (_, _) =>
            {
                if (reverting || combo.SelectedIndex < 0 || combo.SelectedIndex == last) return;
                if (cell.OnChanged(combo.SelectedIndex)) { last = combo.SelectedIndex; return; }
                reverting = true;
                combo.SelectedIndex = last;
                reverting = false;
            };

            Grid.SetRow(label, row); Grid.SetColumn(label, col);
            Grid.SetRow(combo, row); Grid.SetColumn(combo, col + 1);
            grid.Children.Add(label);
            grid.Children.Add(combo);
        }
        cog.Body.Children.Add(grid);
    }

    /// <summary>
    /// Writes key=value into the [renodx] section of reshade.ini (re-read fresh each time).
    /// Returns false (so the dropdown reverts) if the write failed or the section is missing.
    /// </summary>
    private static bool WriteRenodxIniValue(CogDialog cog, string iniPath, string key, string value, string label)
    {
        try
        {
            var iniForWrite = AuxInstallService.ParseIni(File.ReadAllLines(iniPath));
            if (!iniForWrite.TryGetValue("renodx", out var section))
            {
                cog.SetError($"Couldn't save {label}: reshade.ini has no [renodx] section.");
                return false;
            }
            section[key] = value;
            AuxInstallService.WriteIni(iniPath, iniForWrite);
            cog.SetSuccess($"Saved {label}.");
            CrashReporter.Log($"[MainWindow.WriteRenodxIniValue] [renodx] {key}={value} in '{iniPath}'");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[MainWindow.WriteRenodxIniValue] Failed to write {key} — {ex.Message}");
            cog.SetError($"Couldn't save {label}: {ex.Message}");
            return false;
        }
    }

    // ── Presets export / import ──────────────────────────────────────────────────

    private static void AddRdxPresetsSection(CogDialog cog, GameCardViewModel card, string iniPath, string presetPath,
        bool iniExists, bool presetExists)
    {
        cog.AddSection("Presets");
        cog.AddNote("Export saves every RenoDX preset from reshade.ini to RHI-RenoDX-Preset.txt in the game folder and copies the file to the clipboard for sharing. " +
                    "Import replaces the presets in reshade.ini with the ones in that file.");

        var exportBtn = cog.MakeButton("Export Presets", CogButtonKind.Primary, async () =>
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
                    cog.SetError("No [renodx-preset*] sections found.");
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
                cog.SetSuccess($"Exported {presetLines.Count(l => l.StartsWith("["))} preset(s) and copied the file to the clipboard.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                CrashReporter.Log($"[MainWindow.AddRdxPresetsSection] Export failed — {ex.Message}");
                cog.SetError($"Export failed: {ex.Message}");
            }
        }, "Save all RenoDX presets to a file and copy to clipboard for sharing.");
        exportBtn.IsEnabled = iniExists;

        var importBtn = cog.MakeButton("Import Presets", CogButtonKind.Primary, () =>
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
                cog.SetSuccess($"Imported {presetSections.Count} preset(s).");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                CrashReporter.Log($"[MainWindow.AddRdxPresetsSection] Import failed — {ex.Message}");
                cog.SetError($"Import failed: {ex.Message}");
            }
        }, presetExists
            ? "Restore presets from the exported backup file into reshade.ini."
            : "No RHI-RenoDX-Preset.txt file found. Export first.");
        importBtn.IsEnabled = presetExists && iniExists;

        cog.AddButtons(exportBtn, importBtn);
    }
}
