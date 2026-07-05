using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class OverridesFlyoutBuilder
{
    /// <summary>
    /// Builds rows 4-6 (Shaders, Addons, DXVK, Launch), wires the Reset Overrides handler,
    /// and shows the flyout. Continuation of <see cref="OpenOverridesFlyout"/>.
    /// </summary>
    private void BuildShadersAddonsDxvkAndShowFlyout(
        GameCardViewModel card,
        FrameworkElement anchor,
        Grid mainGrid,
        StackPanel panel,
        Border verticalDivider,
        HyperlinkButton resetOverridesLink,
        TextBox gameNameBox,
        TextBox wikiNameBox,
        ToggleSwitch dllOverrideToggle,
        ComboBox bitnessCombo,
        ComboBox apiCombo,
        ComboBox wikiExcludeCombo,
        TextBlock updateSummaryText,
        string originalStoreName,
        string gameName,
        bool isLumaMode,
        Grid bitnessApiRow,
        StackPanel bitnessPanel,
        StackPanel apiPanel)
    {
        var ViewModel = _window.ViewModel;

        // ── ReShade Channel Override ──
        var channelItems = new[] { "Stable", "Nightly", "Custom", "Legacy..." };
        // For Vulkan games, show the effective Vulkan-wide override (any Vulkan game's override applies to all)
        var currentChannelOverride = ViewModel.GetReShadeChannelOverride(_capturedName);
        if (currentChannelOverride == null && card.RequiresVulkanInstall)
        {
            // Check if any other Vulkan game has an override — if so, this game effectively has the same
            currentChannelOverride = ViewModel.AllCards
                .Where(c => c.RequiresVulkanInstall && c.GameName != _capturedName)
                .Select(c => ViewModel.GetReShadeChannelOverride(c.GameName))
                .FirstOrDefault(ch => ch != null);
        }

        // If a legacy version is active, add it to the dropdown items
        var channelItemsList = new List<string>(channelItems);
        string defaultChannelSelection;
        if (string.Equals(currentChannelOverride, "Custom", StringComparison.OrdinalIgnoreCase))
        {
            defaultChannelSelection = "Custom";
        }
        else if (MainViewModel.IsLegacyVersion(currentChannelOverride))
        {
            channelItemsList.Insert(channelItemsList.IndexOf("Legacy..."), currentChannelOverride!);
            defaultChannelSelection = currentChannelOverride!;
        }
        else
        {
            defaultChannelSelection = currentChannelOverride switch
            {
                "Nightly" => "Nightly",
                _ => "Stable",
            };
        }

        var channelCombo = new ComboBox
        {
            ItemsSource = channelItemsList,
            SelectedItem = defaultChannelSelection,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        ToolTipService.SetToolTip(channelCombo,
            "Override the global ReShade build channel for this game.\nVulkan games: changing this affects ALL Vulkan games.");

        channelCombo.SelectionChanged += async (s, ev) =>
        {
            var selected = channelCombo.SelectedItem as string;

            // ── "Legacy..." opens the version picker dialog ──
            if (selected == "Legacy...")
            {
                var legacyVersions = ViewModel.Manifest?.LegacyReShadeAvailable;
                if (legacyVersions == null || legacyVersions.Count == 0)
                {
                    channelCombo.SelectedItem = defaultChannelSelection;
                    return;
                }

                // Build radio button list
                var radioButtons = new RadioButtons { MaxColumns = 1 };
                foreach (var ver in legacyVersions)
                    radioButtons.Items.Add(ver);

                var pickerContent = new StackPanel { Spacing = 12 };
                pickerContent.Children.Add(new TextBlock
                {
                    Text = "⚠ Older ReShade versions may not support newer addons.\nThe game will be excluded from automatic ReShade updates.",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
                    FontSize = 12,
                });
                pickerContent.Children.Add(radioButtons);

                var pickerDialog = new ContentDialog
                {
                    Title = "Select Legacy ReShade Version",
                    Content = new ScrollViewer { Content = pickerContent, MaxHeight = 400 },
                    PrimaryButtonText = "Confirm",
                    CloseButtonText = "Cancel",
                    XamlRoot = _window.Content.XamlRoot,
                    RequestedTheme = ElementTheme.Dark,
                };

                var pickerResult = await DialogService.ShowSafeAsync(pickerDialog);
                if (pickerResult != ContentDialogResult.Primary || radioButtons.SelectedItem is not string pickedVersion)
                {
                    channelCombo.SelectedItem = defaultChannelSelection;
                    return;
                }

                // Download the legacy version if not cached
                if (!AuxInstallService.IsLegacyVersionCached(pickedVersion))
                {
                    var success = await AuxInstallService.DownloadLegacyReShadeAsync(pickedVersion, ViewModel.HttpClient);
                    if (!success)
                    {
                        channelCombo.SelectedItem = defaultChannelSelection;
                        return;
                    }
                }

                // Set the override and reinstall
                ViewModel.SetReShadeChannelOverride(_capturedName, pickedVersion);

                // Update dropdown: remove old legacy version, add new one
                var oldLegacy = channelItemsList.FirstOrDefault(v => MainViewModel.IsLegacyVersion(v) && v != "Legacy...");
                if (oldLegacy != null) channelItemsList.Remove(oldLegacy);
                if (!channelItemsList.Contains(pickedVersion))
                    channelItemsList.Insert(3, pickedVersion);
                channelCombo.ItemsSource = channelItemsList;
                channelCombo.SelectedItem = pickedVersion;
                defaultChannelSelection = pickedVersion;

                var targetCard2 = ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(_capturedName, StringComparison.OrdinalIgnoreCase));
                if (targetCard2 != null && targetCard2.IsRsInstalled)
                    await ViewModel.InstallReShadeCommand.ExecuteAsync(targetCard2);

                ViewModel.NotifyUpdateButtonChanged();
                return;
            }

            // ── If a legacy version is already selected and user picks it again, do nothing ──
            if (MainViewModel.IsLegacyVersion(selected) && selected != "Legacy..." && selected != "Custom")
            {
                return;
            }

            // ── "Custom" — use user-supplied ReShade DLLs ──
            if (selected == "Custom")
            {
                CrashReporter.Log($"[OverridesFlyoutBuilder.RSChannel] '{_capturedName}' → Custom ReShade");

                if (!AuxInstallService.IsCustomReShadeAvailable())
                {
                    var customPath = DlssStreamlineService.RsCustomDir;
                    var linkBtn = new HyperlinkButton
                    {
                        Content = customPath,
                        FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                        FontSize = 12,
                        Padding = new Thickness(0),
                    };
                    linkBtn.Click += (_, _) =>
                    {
                        System.IO.Directory.CreateDirectory(customPath);
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(customPath) { UseShellExecute = true });
                    };
                    var warnContent = new StackPanel { Spacing = 8 };
                    warnContent.Children.Add(new TextBlock
                    {
                        Text = "No custom ReShade DLLs found.\n\nPlace your ReShade64.dll and/or ReShade32.dll in:",
                        TextWrapping = TextWrapping.Wrap,
                    });
                    warnContent.Children.Add(linkBtn);

                    var warnDialog = new ContentDialog
                    {
                        Title = "Custom ReShade Not Found",
                        Content = warnContent,
                        CloseButtonText = "OK",
                        XamlRoot = _window.Content.XamlRoot,
                        RequestedTheme = ElementTheme.Dark,
                    };
                    await DialogService.ShowSafeAsync(warnDialog);
                    channelCombo.SelectedItem = defaultChannelSelection;
                    return;
                }

                var targetCardCustom = ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(_capturedName, StringComparison.OrdinalIgnoreCase));

                // Vulkan game: affects ALL Vulkan games
                if (targetCardCustom?.RequiresVulkanInstall == true)
                {
                    var vDialog = new ContentDialog
                    {
                        Title = "Vulkan ReShade Channel Override",
                        Content = "Vulkan games share a global ReShade layer.\n\n" +
                            "Changing the channel for this game will change it for ALL Vulkan games.",
                        PrimaryButtonText = "Apply to All Vulkan Games",
                        CloseButtonText = "Cancel",
                        XamlRoot = _window.Content.XamlRoot,
                        RequestedTheme = ElementTheme.Dark,
                    };
                    var vResult = await DialogService.ShowSafeAsync(vDialog);
                    if (vResult != ContentDialogResult.Primary)
                    {
                        channelCombo.SelectedItem = defaultChannelSelection;
                        return;
                    }

                    // Apply to ALL Vulkan games
                    foreach (var vCard in ViewModel.AllCards.Where(c => c.RequiresVulkanInstall))
                    {
                        ViewModel.SetReShadeChannelOverride(vCard.GameName, "Custom");
                        vCard.NotifyAll();
                    }

                    // Copy custom DLL to ProgramData Vulkan layer
                    try
                    {
                        var layerDir = VulkanLayerService.LayerDirectory;
                        var stagedPath64 = AuxInstallService.GetCustomReShadePathStatic(false);
                        var stagedPath32 = AuxInstallService.GetCustomReShadePathStatic(true);
                        var layer64 = Path.Combine(layerDir, VulkanLayerService.LayerDllName);
                        var layer32 = Path.Combine(layerDir, "ReShade32.dll");

                        if (File.Exists(stagedPath64) && File.Exists(layer64))
                            AuxInstallService.CopyFileWithElevation(stagedPath64, layer64);
                        if (File.Exists(stagedPath32) && File.Exists(layer32))
                            AuxInstallService.CopyFileWithElevation(stagedPath32, layer32);
                    }
                    catch (Exception ex)
                    {
                        CrashReporter.Log($"[OverridesFlyoutBuilder] Failed to update Vulkan layer with custom ReShade — {ex.Message}");
                    }
                }
                else
                {
                    // Non-Vulkan: per-game only
                    ViewModel.SetReShadeChannelOverride(_capturedName, "Custom");
                    if (targetCardCustom != null && targetCardCustom.IsRsInstalled)
                        await ViewModel.InstallReShadeCommand.ExecuteAsync(targetCardCustom);
                }

                defaultChannelSelection = "Custom";
                ViewModel.NotifyUpdateButtonChanged();
                return;
            }

            string? channelValue = selected switch
            {
                "Nightly" => "Nightly",
                _ => null, // "Stable" = default, clears the per-game override
            };

            var targetCard = ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(_capturedName, StringComparison.OrdinalIgnoreCase));

            // ── Vulkan game: affects ALL Vulkan games ──
            if (targetCard?.RequiresVulkanInstall == true)
            {
                if (channelValue != null)
                {
                    // Setting a specific override on a Vulkan game
                    var dialog = new ContentDialog
                    {
                        Title = "Vulkan ReShade Channel Override",
                        Content = "Vulkan games share a global ReShade layer.\n\n" +
                            "Changing the channel for this game will change it for ALL Vulkan games.",
                        PrimaryButtonText = "Apply to All Vulkan Games",
                        CloseButtonText = "Cancel",
                        XamlRoot = _window.Content.XamlRoot,
                        RequestedTheme = ElementTheme.Dark,
                    };
                    var result = await DialogService.ShowSafeAsync(dialog);
                    if (result != ContentDialogResult.Primary)
                    {
                        channelCombo.SelectedItem = defaultChannelSelection;
                        return;
                    }
                }

                // Apply override (or clear it) on ALL Vulkan games
                foreach (var vCard in ViewModel.AllCards.Where(c => c.RequiresVulkanInstall))
                {
                    ViewModel.SetReShadeChannelOverride(vCard.GameName, channelValue);
                    vCard.NotifyAll();
                }

                // Determine the effective channel for the Vulkan layer
                var effectiveChannel = channelValue ?? ViewModel.Settings.ReShadeChannel;

                // Update the Vulkan layer DLLs
                try
                {
                    var layerDir = VulkanLayerService.LayerDirectory;
                    var stagedPath64 = AuxInstallService.GetStagedPathForChannel(effectiveChannel, false);
                    var stagedPath32 = AuxInstallService.GetStagedPathForChannel(effectiveChannel, true);
                    var layer64 = Path.Combine(layerDir, VulkanLayerService.LayerDllName);
                    var layer32 = Path.Combine(layerDir, "ReShade32.dll");

                    CrashReporter.Log($"[OverridesFlyoutBuilder] Vulkan channel override → {effectiveChannel}: staged64={stagedPath64} (exists={File.Exists(stagedPath64)}), layer64={layer64} (exists={File.Exists(layer64)})");

                    if (File.Exists(stagedPath64) && new FileInfo(stagedPath64).Length > AuxInstallService.MinReShadeSize && File.Exists(layer64))
                        AuxInstallService.CopyFileWithElevation(stagedPath64, layer64);
                    else
                        CrashReporter.Log($"[OverridesFlyoutBuilder] Skipped 64-bit copy: staged exists={File.Exists(stagedPath64)}, size={(File.Exists(stagedPath64) ? new FileInfo(stagedPath64).Length : 0)}, layer exists={File.Exists(layer64)}");

                    if (File.Exists(stagedPath32) && new FileInfo(stagedPath32).Length > AuxInstallService.MinReShadeSize && File.Exists(layer32))
                        AuxInstallService.CopyFileWithElevation(stagedPath32, layer32);

                    CrashReporter.Log($"[OverridesFlyoutBuilder] Updated Vulkan layer to {effectiveChannel}");
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[OverridesFlyoutBuilder] Failed to update Vulkan layer — {ex.Message}");
                }

                // Mark all Vulkan games as installed (layer updated in-place)
                foreach (var vCard in ViewModel.AllCards.Where(c => c.RequiresVulkanInstall && c.IsRsInstalled))
                {
                    vCard.RsStatus = GameStatus.Installed;
                    vCard.NotifyAll();
                }
            }
            else
            {
                // ── Non-Vulkan game: per-game only ──
                ViewModel.SetReShadeChannelOverride(_capturedName, channelValue);

                // Auto-reinstall ReShade with the new channel if it's currently installed
                if (targetCard != null && targetCard.IsRsInstalled)
                {
                    await ViewModel.InstallReShadeCommand.ExecuteAsync(targetCard);
                }
            }

            ViewModel.NotifyUpdateButtonChanged();
        };

        var channelPanel = new StackPanel { Spacing = 4 };
        channelPanel.Children.Add(new TextBlock { Text = "RS Channel", FontSize = 11, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush) });
        channelPanel.Children.Add(channelCombo);
        Grid.SetColumn(channelPanel, 2);

        bitnessApiRow.Children.Add(bitnessPanel);
        bitnessApiRow.Children.Add(apiPanel);
        bitnessApiRow.Children.Add(channelPanel);

        Grid.SetColumn(bitnessApiRow, 0);
        Grid.SetRow(bitnessApiRow, 2);
        mainGrid.Children.Add(bitnessApiRow);

        // ── Right column: Update Inclusion button + summary ──
        var (updateInclusionBtn, updateSummaryText_) = UpdateInclusionHelper.CreateUpdateInclusionControls(
            ViewModel, _capturedName, card.IsREEngineGame, _window.Content.XamlRoot,
            onSaved: () =>
            {
                // Rebuild the detail panel if the same game is selected, so component
                // rows reflect the new exclusion state immediately
                if (ViewModel.SelectedGame is { } sel
                    && sel.GameName.Equals(_capturedName, StringComparison.OrdinalIgnoreCase))
                {
                    _window.PopulateDetailPanel(sel);
                    _window.BuildOverridesPanel(sel);
                }
            },
            isDxvkEnabled: card.DxvkEnabled);
        updateSummaryText = updateSummaryText_;

        var globalUpdateColumn = new StackPanel { Spacing = 4 };
        globalUpdateColumn.Children.Add(new TextBlock
        {
            Text = "Global update inclusion",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
        });
        globalUpdateColumn.Children.Add(updateInclusionBtn);
        globalUpdateColumn.Children.Add(updateSummaryText);

        Grid.SetColumn(globalUpdateColumn, 2);
        Grid.SetRow(globalUpdateColumn, 2);
        mainGrid.Children.Add(globalUpdateColumn);

        // ══════════════════════════════════════════════════════════════════════
        // ROW 3 — Horizontal separator
        // ══════════════════════════════════════════════════════════════════════
        var sep2 = UIFactory.MakeSeparator();
        Grid.SetColumn(sep2, 0);
        Grid.SetRow(sep2, 3);
        Grid.SetColumnSpan(sep2, 3);
        mainGrid.Children.Add(sep2);


        // ══════════════════════════════════════════════════════════════════════
        // ROW 4 — Left: Shaders (Global + Custom toggles + Select btn)
        //         Right: Addons (Global toggle + Select btn)
        // ══════════════════════════════════════════════════════════════════════

        // ── Shader Mode ComboBox ──
        string currentShaderMode = ViewModel.GetPerGameShaderMode(gameName);
        string effectiveShaderDisplay = currentShaderMode;
        if (currentShaderMode == "Global"
            && ViewModel.Settings.UseCustomShaders
            && !ViewModel.GameNameServiceInstance.PerGameShaderMode.ContainsKey(gameName))
            effectiveShaderDisplay = "Custom";

        var shaderModeItems = new[] { "Global", "Custom", "Select", "Off" };
        bool shaderComboInitializing = true;

        var shaderModeCombo = new ComboBox
        {
            ItemsSource = shaderModeItems,
            SelectedItem = effectiveShaderDisplay,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = !card.UseNormalReShade,
        };
        ToolTipService.SetToolTip(shaderModeCombo,
            "Global = use global shader selection. Custom = use custom shader directories. Select = pick per-game packs. Off = no shaders.");

        shaderModeCombo.SelectionChanged += async (s, ev) =>
        {
            if (shaderComboInitializing) return;
            var selected = shaderModeCombo.SelectedItem as string;
            if (string.IsNullOrEmpty(selected)) return;

            if (selected == "Select")
            {
                List<string>? current = ViewModel.GameNameServiceInstance.PerGameShaderSelection.TryGetValue(gameName, out var existing)
                    ? existing
                    : ViewModel.Settings.SelectedShaderPacks;
                var result = await ViewModel.ShowPerGameShaderSelectionPicker?.Invoke(gameName, current)!;
                if (result != null)
                {
                    ViewModel.GameNameServiceInstance.PerGameShaderSelection[gameName] = result;
                    ViewModel.SetPerGameShaderMode(_capturedName, "Select");
                    ViewModel.DeployShadersForCard(_capturedName);
                }
                else
                {
                    shaderComboInitializing = true;
                    shaderModeCombo.SelectedItem = effectiveShaderDisplay;
                    shaderComboInitializing = false;
                }
                return;
            }

            if (selected == "Off")
                ViewModel.SetPerGameShaderMode(_capturedName, "Off");
            else if (selected == "Custom")
                ViewModel.SetPerGameShaderMode(_capturedName, "Custom");
            else
                ViewModel.SetPerGameShaderMode(_capturedName, "Global");
            ViewModel.DeployShadersForCard(_capturedName);
            effectiveShaderDisplay = selected;
        };
        shaderComboInitializing = false;

        var shaderColumn = new StackPanel { Spacing = 6 };

        // Shader + Addon side by side
        var shaderAddonGrid = new Grid { ColumnSpacing = 8 };
        shaderAddonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        shaderAddonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        shaderAddonGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        shaderAddonGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var shaderLabel = new TextBlock
        {
            Text = "Shaders",
            FontSize = 11,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
        };
        Grid.SetRow(shaderLabel, 0); Grid.SetColumn(shaderLabel, 0);
        shaderAddonGrid.Children.Add(shaderLabel);
        Grid.SetRow(shaderModeCombo, 1); Grid.SetColumn(shaderModeCombo, 0);
        shaderAddonGrid.Children.Add(shaderModeCombo);

        shaderColumn.Children.Add(shaderAddonGrid);

        // ── Per-game Addon mode ComboBox ──
        string currentAddonMode = ViewModel.GetPerGameAddonMode(gameName);
        var addonModeItems = new[] { "Global", "Select", "Off" };
        bool addonComboInitializing = true;

        var addonModeCombo = new ComboBox
        {
            ItemsSource = addonModeItems,
            SelectedItem = currentAddonMode == "Off" ? "Off" : (currentAddonMode == "Select" ? "Select" : "Global"),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = !card.UseNormalReShade,
        };
        ToolTipService.SetToolTip(addonModeCombo,
            "Global = use global addon set. Select = pick per-game addons. Off = no addons for this game.");

        addonModeCombo.SelectionChanged += async (s, ev) =>
        {
            if (addonComboInitializing) return;
            var selected = addonModeCombo.SelectedItem as string;
            if (string.IsNullOrEmpty(selected)) return;

            if (selected == "Select")
            {
                List<string>? current = ViewModel.GameNameServiceInstance.PerGameAddonSelection.TryGetValue(gameName, out var existingAddons)
                    ? existingAddons
                    : null;

                IAddonPackService? addonPackService = ViewModel.AddonPackServiceInstance;
                if (addonPackService == null)
                {
                    addonComboInitializing = true;
                    addonModeCombo.SelectedItem = currentAddonMode == "Off" ? "Off" : "Global";
                    addonComboInitializing = false;
                    return;
                }

                var result = await AddonPopupHelper.ShowAsync(
                    _window.Content.XamlRoot,
                    addonPackService,
                    current,
                    AddonPopupHelper.PopupContext.PerGame);
                if (result != null)
                {
                    ViewModel.GameNameServiceInstance.PerGameAddonSelection[gameName] = result;
                    ViewModel.SetPerGameAddonMode(_capturedName, "Select");
                    ViewModel.DeployAddonsForCard(_capturedName);
                }
                else
                {
                    addonComboInitializing = true;
                    addonModeCombo.SelectedItem = currentAddonMode == "Off" ? "Off" : "Global";
                    addonComboInitializing = false;
                }
                return;
            }

            if (selected == "Off")
                ViewModel.SetPerGameAddonMode(_capturedName, "Off");
            else
                ViewModel.SetPerGameAddonMode(_capturedName, "Global");
            ViewModel.DeployAddonsForCard(_capturedName);
        };
        addonComboInitializing = false;

        var addonLabel = new TextBlock
        {
            Text = "Addons",
            FontSize = 11,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
        };
        Grid.SetRow(addonLabel, 0); Grid.SetColumn(addonLabel, 1);
        shaderAddonGrid.Children.Add(addonLabel);
        Grid.SetRow(addonModeCombo, 1); Grid.SetColumn(addonModeCombo, 1);
        shaderAddonGrid.Children.Add(addonModeCombo);

        // Add "Shaders and Addons" section title
        shaderColumn.Children.Insert(0, new TextBlock
        {
            Text = "Shaders and Addons",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            Margin = new Thickness(0, 0, 0, 4),
        });

        Grid.SetColumn(shaderColumn, 0);
        Grid.SetRow(shaderColumn, 4);
        mainGrid.Children.Add(shaderColumn);

        // Right column Row 4: Launch executable label + TextBox
        var launchExeColumn = new StackPanel { Spacing = 6 };
        launchExeColumn.Children.Add(new TextBlock
        {
            Text = "Launch executable",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            Margin = new Thickness(0, 0, 0, 15),
        });

        var currentLaunchExe = ViewModel.GameNameServiceInstance.LaunchExeOverrides
            .TryGetValue(_capturedName, out var savedExe) ? savedExe : "";
        var launchExeBox = new TextBox
        {
            Text = currentLaunchExe,
            PlaceholderText = "Auto-detect (or paste path)",
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        launchExeBox.LostFocus += (s, ev) =>
        {
            var newPath = launchExeBox.Text.Trim();
            if (string.IsNullOrEmpty(newPath))
                ViewModel.GameNameServiceInstance.LaunchExeOverrides.Remove(_capturedName);
            else
                ViewModel.GameNameServiceInstance.LaunchExeOverrides[_capturedName] = newPath;
            ViewModel.SaveSettingsPublic();
        };
        launchExeColumn.Children.Add(launchExeBox);

        Grid.SetColumn(launchExeColumn, 2);
        Grid.SetRow(launchExeColumn, 4);
        mainGrid.Children.Add(launchExeColumn);

        // ══════════════════════════════════════════════════════════════════════
        // ROW 5 — Left: Select ReShade Preset + Right: Browse/Reset (in same section as Row 4)
        // ══════════════════════════════════════════════════════════════════════

        var presetBtn = new Button
        {
            Content = "Select ReShade Preset",
            FontSize = 12,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush),
            Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
        };
        ToolTipService.SetToolTip(presetBtn,
            "Pick .ini preset files to copy to this game's folder. Place presets in the reshade-presets folder.");
        presetBtn.Click += async (s, ev) =>
        {
            var selected = await PresetPopupHelper.ShowAsync(_window.Content.XamlRoot);
            if (selected != null && selected.Count > 0)
            {
                var targetCard = ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(_capturedName, StringComparison.OrdinalIgnoreCase));
                if (targetCard != null && !string.IsNullOrEmpty(targetCard.InstallPath))
                {
                    int count = PresetPopupHelper.DeployPresets(selected, targetCard.InstallPath);
                    _crashReporter.Log($"[OverridesFlyoutBuilder] Deployed {count} preset(s) to '{_capturedName}'");

                    if (count > 0)
                    {
                        var shaderDialog = new ContentDialog
                        {
                            Title = "🔧 Install Shaders?",
                            Content = "Also install the required shaders and textures?",
                            PrimaryButtonText = "Yes",
                            CloseButtonText = "No",
                            XamlRoot = _window.Content.XamlRoot,
                            RequestedTheme = ElementTheme.Dark,
                        };

                        var shaderResult = await DialogService.ShowSafeAsync(shaderDialog);
                        if (shaderResult == ContentDialogResult.Primary)
                        {
                            var presetPaths = selected.Select(f => Path.Combine(PresetPopupHelper.PresetsDir, f)).ToList();
                            await ViewModel.ApplyPresetShadersAsync(_capturedName, presetPaths);

                            // Rebuild overrides panel so the shader toggle reflects the new "Select" mode
                            if (ViewModel.SelectedGame is { } selectedCard
                                && selectedCard.GameName.Equals(_capturedName, StringComparison.OrdinalIgnoreCase))
                            {
                                _window.BuildOverridesPanel(selectedCard);
                            }
                        }
                    }
                }
            }
        };

        presetBtn.Margin = new Thickness(0, 8, 0, 0);
        shaderColumn.Children.Add(presetBtn);
        var launchBtnRow = new Grid { ColumnSpacing = 8 };
        launchBtnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        launchBtnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var browseLaunchBtn = new Button
        {
            Content = "Browse",
            FontSize = 12,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush),
            Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
        };
        browseLaunchBtn.Click += async (s, ev) =>
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
            string? filePath = await Task.Run(() =>
            {
                var ofn = new NativeInterop.OpenFileName();
                ofn.structSize = System.Runtime.InteropServices.Marshal.SizeOf(ofn);
                ofn.hwndOwner = hwnd;
                ofn.filter = "Executables (*.exe)\0*.exe\0All Files (*.*)\0*.*\0";
                ofn.file = new string(new char[260]);
                ofn.maxFile = ofn.file.Length;
                ofn.title = "Select Game Executable";
                ofn.initialDir = card.InstallPath;
                ofn.flags = 0x00080000 | 0x00001000;
                return NativeInterop.GetOpenFileName(ref ofn) ? ofn.file.TrimEnd('\0') : null;
            });
            if (!string.IsNullOrEmpty(filePath))
            {
                launchExeBox.Text = filePath;
                ViewModel.GameNameServiceInstance.LaunchExeOverrides[_capturedName] = filePath;
                ViewModel.SaveSettingsPublic();
            }
        };
        Grid.SetColumn(browseLaunchBtn, 0);
        launchBtnRow.Children.Add(browseLaunchBtn);

        var resetLaunchBtn = new Button
        {
            Content = "Reset",
            FontSize = 12,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush),
            Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
        };
        resetLaunchBtn.Click += (s, ev) =>
        {
            launchExeBox.Text = "";
            ViewModel.GameNameServiceInstance.LaunchExeOverrides.Remove(_capturedName);
            ViewModel.SaveSettingsPublic();
        };
        Grid.SetColumn(resetLaunchBtn, 1);
        launchBtnRow.Children.Add(resetLaunchBtn);

        launchBtnRow.Margin = new Thickness(0, 8, 0, 0);
        launchExeColumn.Children.Add(launchBtnRow);

        // ══════════════════════════════════════════════════════════════════════
        // ROW 5 — Horizontal separator (before DXVK)
        // ══════════════════════════════════════════════════════════════════════
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 5 (separator)
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 6 (DXVK)
        // Extend vertical divider to span the new rows
        Grid.SetRowSpan(verticalDivider, 7);

        var sep4 = UIFactory.MakeSeparator();
        Grid.SetColumn(sep4, 0);
        Grid.SetRow(sep4, 5);
        Grid.SetColumnSpan(sep4, 3);
        mainGrid.Children.Add(sep4);

        // ══════════════════════════════════════════════════════════════════════
        // ROW 6 — Left: DXVK ComboBox (Off/Development/Stable/Lilium HDR)
        // ══════════════════════════════════════════════════════════════════════

        // Forward-declare DXVK toggle for reset handler
        ToggleSwitch dxvkToggle = null!;

        if (card.IsDxvkToggleVisible)
        {
            var dxvkModeItems = new[] { "Off", "Development", "Stable", "Lilium HDR" };
            string defaultDxvkSelection;
            if (!card.DxvkEnabled)
            {
                defaultDxvkSelection = "Off";
            }
            else
            {
                var currentDxvkOverride = ViewModel.GetDxvkVariantOverride(_capturedName);
                if (currentDxvkOverride != null)
                {
                    defaultDxvkSelection = currentDxvkOverride switch
                    {
                        "Development" => "Development",
                        "Stable" => "Stable",
                        "LiliumHdr" => "Lilium HDR",
                        _ => "Development",
                    };
                }
                else
                {
                    // No per-game override — show the effective global variant
                    defaultDxvkSelection = ViewModel.DxvkServiceInstance.SelectedVariant switch
                    {
                        DxvkVariant.Stable => "Stable",
                        DxvkVariant.LiliumHdr => "Lilium HDR",
                        _ => "Development",
                    };
                }
            }

            var dxvkModeCombo = new ComboBox
            {
                ItemsSource = dxvkModeItems,
                SelectedItem = defaultDxvkSelection,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                IsEnabled = card.IsDxvkToggleEnabled && card.DxvkInstallEnabled,
            };
            if (card.DxvkToggleTooltip != null)
                ToolTipService.SetToolTip(dxvkModeCombo, card.DxvkToggleTooltip);
            else
                ToolTipService.SetToolTip(dxvkModeCombo,
                    "Off = DXVK disabled.\nDevelopment/Stable/Lilium HDR = DXVK variant selection.\nDXVK translates DirectX to Vulkan — enables compute shaders.");

            dxvkToggle = new ToggleSwitch { IsOn = card.DxvkEnabled, Visibility = Visibility.Collapsed };

            dxvkModeCombo.SelectionChanged += async (s, ev) =>
            {
                var selected = dxvkModeCombo.SelectedItem as string;
                if (string.IsNullOrEmpty(selected)) return;
                var targetCard = ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(_capturedName, StringComparison.OrdinalIgnoreCase));
                if (targetCard == null) return;

                if (selected == "Off")
                {
                    if (targetCard.DxvkEnabled)
                    {
                        await ViewModel.HandleDxvkToggleAsync(targetCard, false, _window.Content.XamlRoot);
                        ViewModel.SetDxvkVariantOverride(_capturedName, null);
                    }
                }
                else
                {
                    string? variantValue = selected switch
                    {
                        "Development" => "Development",
                        "Stable" => "Stable",
                        "Lilium HDR" => "LiliumHdr",
                        _ => null,
                    };
                    ViewModel.SetDxvkVariantOverride(_capturedName, variantValue);

                    if (!targetCard.DxvkEnabled)
                    {
                        var resolvedVariant = ViewModel.ResolveDxvkVariant(_capturedName);
                        var savedVariant = ViewModel.DxvkServiceInstance.SelectedVariant;
                        ViewModel.DxvkServiceInstance.SelectedVariant = resolvedVariant;
                        await ViewModel.HandleDxvkToggleAsync(targetCard, true, _window.Content.XamlRoot);
                        ViewModel.DxvkServiceInstance.SelectedVariant = savedVariant;
                        if (!targetCard.DxvkEnabled) dxvkModeCombo.SelectedItem = "Off";
                    }
                    else
                    {
                        var resolvedVariant = ViewModel.ResolveDxvkVariant(_capturedName);
                        var savedVariant = ViewModel.DxvkServiceInstance.SelectedVariant;
                        ViewModel.DxvkServiceInstance.SelectedVariant = resolvedVariant;
                        await ViewModel.DxvkServiceInstance.EnsureStagingAsync();
                        if (ViewModel.DxvkServiceInstance.IsStagingReady)
                            await ViewModel.InstallDxvkAsync(targetCard, _window.Content.XamlRoot);
                        ViewModel.DxvkServiceInstance.SelectedVariant = savedVariant;
                    }
                }
            };

            var dxvkColumn = new StackPanel { Spacing = 6 };
            dxvkColumn.Children.Add(new TextBlock
            {
                Text = "DXVK",
                FontSize = 12,
                Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
                Margin = new Thickness(0, 0, 0, 4),
            });
            dxvkColumn.Children.Add(dxvkModeCombo);

            Grid.SetColumn(dxvkColumn, 0);
            Grid.SetRow(dxvkColumn, 6);
            mainGrid.Children.Add(dxvkColumn);
        }

        panel.Children.Add(mainGrid);

        // ══════════════════════════════════════════════════════════════════════
        // Reset Overrides handler (wired to the link near the title)
        // ══════════════════════════════════════════════════════════════════════
        resetOverridesLink.Click += (s, ev) =>
        {
            // Reset all controls to defaults
            gameNameBox.Text = originalStoreName ?? gameName;
            wikiNameBox.Text = "";
            shaderComboInitializing = true;
            shaderModeCombo.SelectedItem = "Global";
            shaderComboInitializing = false;
            addonComboInitializing = true;
            addonModeCombo.SelectedItem = "Global";
            addonComboInitializing = false;
            dllOverrideToggle.IsOn = false;
            wikiExcludeCombo.SelectedItem = "Included";

            // Persist all reset values immediately
            var resetName = (originalStoreName ?? gameName).Trim();
            bool nameChanged = !resetName.Equals(_capturedName, StringComparison.OrdinalIgnoreCase);
            if (nameChanged && !string.IsNullOrWhiteSpace(resetName))
            {
                ViewModel.RenameGame(_capturedName, resetName);
                _capturedName = resetName;
            }

            // Remove wiki mapping
            if (ViewModel.GetNameMapping(_capturedName) != null)
                ViewModel.RemoveNameMapping(_capturedName);

            // Shader mode → Global
            if (ViewModel.GetPerGameShaderMode(_capturedName) != "Global")
            {
                ViewModel.SetPerGameShaderMode(_capturedName, "Global");
                ViewModel.GameNameServiceInstance.PerGameShaderSelection.Remove(_capturedName);
                ViewModel.DeployShadersForCard(_capturedName);
            }

            // Addon mode → Global
            if (ViewModel.GetPerGameAddonMode(_capturedName) != "Global")
            {
                ViewModel.SetPerGameAddonMode(_capturedName, "Global");
                ViewModel.DeployAddonsForCard(_capturedName);
            }

            // Disable DLL override
            if (ViewModel.HasDllOverride(_capturedName))
            {
                var targetCard = ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(_capturedName, StringComparison.OrdinalIgnoreCase));
                if (targetCard != null)
                    ViewModel.DisableDllOverride(targetCard);
            }

            // Include all in Update All
            if (ViewModel.IsUpdateAllExcludedReShade(_capturedName))
                ViewModel.ToggleUpdateAllExclusionReShade(_capturedName);
            if (ViewModel.IsUpdateAllExcludedRenoDx(_capturedName))
                ViewModel.ToggleUpdateAllExclusionRenoDx(_capturedName);
            if (ViewModel.IsUpdateAllExcludedUl(_capturedName))
                ViewModel.ToggleUpdateAllExclusionUl(_capturedName);
            if (ViewModel.IsUpdateAllExcludedDc(_capturedName))
                ViewModel.ToggleUpdateAllExclusionDc(_capturedName);
            if (ViewModel.IsUpdateAllExcludedOs(_capturedName))
                ViewModel.ToggleUpdateAllExclusionOs(_capturedName);

            // Refresh update summary
            UpdateInclusionHelper.RefreshSummary(updateSummaryText, ViewModel, _capturedName, card.IsREEngineGame, card.DxvkEnabled);

            // Disable wiki exclusion
            if (ViewModel.IsWikiExcluded(_capturedName))
                ViewModel.ToggleWikiExclusion(_capturedName);

            // Reset Normal ReShade
            {
                var targetCard = ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(_capturedName, StringComparison.OrdinalIgnoreCase));
                if (targetCard != null && targetCard.UseNormalReShade)
                    ViewModel.SetUseNormalReShade(targetCard, false);
            }

            // Reset bitness and API overrides
            bitnessCombo.SelectedItem = "Auto";
            ViewModel.SetBitnessOverride(_capturedName, null);
            apiCombo.SelectedItem = "Auto";
            ViewModel.SetApiOverride(_capturedName, null);
            channelCombo.SelectedItem = "Stable";
            ViewModel.SetReShadeChannelOverride(_capturedName, null);
            if (card.IsDxvkToggleVisible)
            {
                ViewModel.SetDxvkVariantOverride(_capturedName, null);
            }

            _crashReporter.Log($"[OverridesFlyoutBuilder.OpenOverridesFlyout] Overrides reset for: {_capturedName}");

            // Only reselect/NotifyAll if game name actually changed
            if (nameChanged)
            {
                _window.RequestReselect(_capturedName);
                card.NotifyAll();
            }
        };

        // Style the flyout presenter to allow scrolling and set max dimensions
        var flyoutStyle = new Style(typeof(FlyoutPresenter));
        flyoutStyle.Setters.Add(new Setter(FlyoutPresenter.MaxWidthProperty, 740));
        flyoutStyle.Setters.Add(new Setter(FlyoutPresenter.MaxHeightProperty, 800));
        flyoutStyle.Setters.Add(new Setter(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto));

        var flyout = new Flyout
        {
            Content = panel,
            Placement = FlyoutPlacementMode.BottomEdgeAlignedRight,
            FlyoutPresenterStyle = flyoutStyle,
        };

        flyout.ShowAt(anchor);
    }
}
