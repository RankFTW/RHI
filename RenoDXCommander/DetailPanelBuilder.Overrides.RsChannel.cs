// DetailPanelBuilder.Overrides.RsChannel.cs — ReShade Channel Override + Update Inclusion + Middle Row Grid.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    /// <summary>Builds RS Channel Override, Update Inclusion, and Middle Row Grid.</summary>
    private void BuildRsChannelSection(OverridesPanelCtx ctx)
    {
        var card = ctx.Card;
        var gameName = ctx.GameName;
        var isLumaMode = ctx.IsLumaMode;
        var bitnessPanel = ctx.BitnessPanel;

        // ── ReShade Channel Override ──
        bitnessPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var channelLabel = new TextBlock
        {
            Text = "RS Channel",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
        };
        ToolTipService.SetToolTip(channelLabel,
            "Override the global ReShade build channel for this game.\nVulkan games: changing this affects ALL Vulkan games.");

        var channelItems = new[] { "Stable", "Nightly", "Custom", "No Addons", "Legacy..." };
        // For Vulkan games, show the effective Vulkan-wide override (any Vulkan game's override applies to all)
        var currentChannelOverride = _window.ViewModel.GetReShadeChannelOverride(gameName);
        if (currentChannelOverride == null && card.RequiresVulkanInstall)
        {
            currentChannelOverride = _window.ViewModel.AllCards
                .Where(c => c.RequiresVulkanInstall && c.GameName != gameName)
                .Select(c => _window.ViewModel.GetReShadeChannelOverride(c.GameName))
                .FirstOrDefault(ch => ch != null);
        }

        // If a legacy version is active, add it to the dropdown items
        var channelItemsList = new List<string>(channelItems);
        string defaultChannelSelection;
        if (card.UseNormalReShade)
        {
            defaultChannelSelection = "No Addons";
        }
        else if (string.Equals(currentChannelOverride, "Custom", StringComparison.OrdinalIgnoreCase))
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
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        ToolTipService.SetToolTip(channelCombo,
            "Override the ReShade build channel for this game.\nVulkan games: changing this affects ALL Vulkan games.");

        bool channelComboInitializing = true;

        // Allow re-opening the Custom picker by clicking Custom when already on Custom
        channelCombo.DropDownClosed += async (s, ev) =>
        {
            if (channelComboInitializing) return;
            var current = channelCombo.SelectedItem as string;
            if (current == "Custom" && string.Equals(_window.ViewModel.GetReShadeChannelOverride(ctx.CapturedName), "Custom", StringComparison.OrdinalIgnoreCase))
            {
                channelComboInitializing = true;
                channelCombo.SelectedItem = "Stable";
                channelComboInitializing = false;
                channelCombo.SelectedItem = "Custom";
            }
        };

        channelCombo.SelectionChanged += async (s, ev) =>
        {
            var selected = channelCombo.SelectedItem as string;
            if (channelComboInitializing) return;
            if (string.IsNullOrEmpty(selected)) return;
            CrashReporter.Log($"[DetailPanelBuilder.RSChannel] '{ctx.CapturedName}' selection changed to: '{selected}'");

            // ── "Legacy..." opens the version picker dialog ──
            if (selected == "Legacy...")
            {
                var legacyVersions = _window.ViewModel.Manifest?.LegacyReShadeAvailable;
                if (legacyVersions == null || legacyVersions.Count == 0)
                {
                    channelCombo.SelectedItem = defaultChannelSelection;
                    return;
                }

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

                if (!AuxInstallService.IsLegacyVersionCached(pickedVersion))
                {
                    var success = await AuxInstallService.DownloadLegacyReShadeAsync(pickedVersion, _window.ViewModel.HttpClient);
                    if (!success)
                    {
                        channelCombo.SelectedItem = defaultChannelSelection;
                        return;
                    }
                }

                _window.ViewModel.SetReShadeChannelOverride(ctx.CapturedName, pickedVersion);

                // Clear No Addons mode if active (legacy is an addon build)
                var targetCardLegacy = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(ctx.CapturedName, StringComparison.OrdinalIgnoreCase));
                if (targetCardLegacy != null && targetCardLegacy.UseNormalReShade)
                    _window.ViewModel.SetUseNormalReShade(targetCardLegacy, false);

                // Update dropdown: remove old legacy version, add new one
                var oldLegacy = channelItemsList.FirstOrDefault(v => MainViewModel.IsLegacyVersion(v) && v != "Legacy...");
                if (oldLegacy != null) channelItemsList.Remove(oldLegacy);
                if (!channelItemsList.Contains(pickedVersion))
                    channelItemsList.Insert(3, pickedVersion);
                channelCombo.ItemsSource = channelItemsList;
                channelCombo.SelectedItem = pickedVersion;
                defaultChannelSelection = pickedVersion;

                var targetCard2 = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(ctx.CapturedName, StringComparison.OrdinalIgnoreCase));
                if (targetCard2 != null)
                    await _window.ViewModel.InstallReShadeCommand.ExecuteAsync(targetCard2);

                _window.ViewModel.NotifyUpdateButtonChanged();

                // Rebuild overrides panel to reflect the new legacy version in the dropdown
                var refreshCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(ctx.CapturedName, StringComparison.OrdinalIgnoreCase));
                if (refreshCard != null)
                    BuildOverridesPanel(refreshCard);
                return;
            }

            // ── If a legacy version is already selected and user picks it again, do nothing ──
            if (selected != "Global" && selected != "Stable" && selected != "Nightly"
                && selected != "No Addons" && selected != "Legacy..." && selected != "Custom"
                && MainViewModel.IsLegacyVersion(selected))
            {
                return;
            }

            // ── "Custom" — use user-supplied ReShade DLLs ──
            if (selected == "Custom")
            {
                CrashReporter.Log($"[DetailPanelBuilder.RSChannel] '{ctx.CapturedName}' → Custom ReShade");

                var customDir = DlssStreamlineService.RsCustomDir;
                string[] dllFiles;
                try
                {
                    Directory.CreateDirectory(customDir);
                    dllFiles = Directory.GetFiles(customDir, "*.dll");
                }
                catch
                {
                    dllFiles = Array.Empty<string>();
                }

                if (dllFiles.Length == 0)
                {
                    // No custom DLLs found — warn user and revert
                    var linkBtn = new HyperlinkButton
                    {
                        Content = customDir,
                        FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                        FontSize = 12,
                        Padding = new Thickness(0),
                    };
                    linkBtn.Click += (_, _) =>
                    {
                        System.IO.Directory.CreateDirectory(customDir);
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(customDir) { UseShellExecute = true });
                    };
                    var warnContent = new StackPanel { Spacing = 8 };
                    warnContent.Children.Add(new TextBlock
                    {
                        Text = "No custom ReShade DLLs found.\n\nPlace your .dll files in:",
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

                // ── Show picker dialog with all DLL files ──
                // Check if a previous selection exists and is still valid
                string? previousSelection = null;
                if (_gameNameService.CustomReShadeSelection.TryGetValue(ctx.CapturedName, out var prevSel))
                {
                    if (dllFiles.Any(f => Path.GetFileName(f).Equals(prevSel, StringComparison.OrdinalIgnoreCase)))
                        previousSelection = prevSel;
                }

                var pickerPanel = new StackPanel { Spacing = 8 };

                // Folder path link at top
                var folderLink = new HyperlinkButton
                {
                    Content = customDir,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                    FontSize = 11,
                    Padding = new Thickness(0),
                };
                folderLink.Click += (_, _) =>
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(customDir) { UseShellExecute = true });
                };
                pickerPanel.Children.Add(folderLink);

                // Radio-button-style CheckBoxes (mutual exclusion)
                var checkBoxes = new List<CheckBox>();
                foreach (var dllPath in dllFiles.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
                {
                    var fileName = Path.GetFileName(dllPath);
                    var cb = new CheckBox
                    {
                        Content = fileName,
                        FontSize = 13,
                        IsChecked = fileName.Equals(previousSelection, StringComparison.OrdinalIgnoreCase),
                    };
                    cb.Checked += (sender, _) =>
                    {
                        // Uncheck all others (radio-button behavior)
                        foreach (var other in checkBoxes)
                        {
                            if (other != sender)
                                other.IsChecked = false;
                        }
                    };
                    checkBoxes.Add(cb);
                    pickerPanel.Children.Add(cb);
                }

                var pickerDialog = new ContentDialog
                {
                    Title = "Select Custom ReShade",
                    PrimaryButtonText = "Deploy",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = _window.Content.XamlRoot,
                    RequestedTheme = ElementTheme.Dark,
                    Content = new ScrollViewer { Content = pickerPanel, MaxHeight = 400 },
                };

                var pickerResult = await DialogService.ShowSafeAsync(pickerDialog);
                if (pickerResult != ContentDialogResult.Primary)
                {
                    channelCombo.SelectedItem = defaultChannelSelection;
                    return;
                }

                // Find selected DLL
                var selectedCb = checkBoxes.FirstOrDefault(cb => cb.IsChecked == true);
                if (selectedCb == null)
                {
                    channelCombo.SelectedItem = defaultChannelSelection;
                    return;
                }
                var selectedFilename = selectedCb.Content as string ?? "";

                // Save per-game selection
                _gameNameService.CustomReShadeSelection[ctx.CapturedName] = selectedFilename;
                _window.ViewModel.SaveSettingsPublic();
                CrashReporter.Log($"[DetailPanelBuilder.RSChannel] '{ctx.CapturedName}' → Custom ReShade selected: '{selectedFilename}'");

                var targetCardCustom = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(ctx.CapturedName, StringComparison.OrdinalIgnoreCase));

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
                    foreach (var vCard in _window.ViewModel.AllCards.Where(c => c.RequiresVulkanInstall))
                    {
                        _window.ViewModel.SetReShadeChannelOverride(vCard.GameName, "Custom");
                        _gameNameService.CustomReShadeSelection[vCard.GameName] = selectedFilename;
                        if (vCard.UseNormalReShade)
                            _window.ViewModel.SetUseNormalReShade(vCard, false);
                        vCard.NotifyAll();
                    }

                    // Copy custom DLL to ProgramData Vulkan layer
                    try
                    {
                        var layerDir = VulkanLayerService.LayerDirectory;
                        var customFilePath = AuxInstallService.GetCustomReShadePathForFile(selectedFilename);
                        var layer64 = Path.Combine(layerDir, VulkanLayerService.LayerDllName);

                        if (File.Exists(customFilePath) && File.Exists(layer64))
                            AuxInstallService.CopyFileWithElevation(customFilePath, layer64);
                    }
                    catch (Exception ex)
                    {
                        CrashReporter.Log($"[DetailPanelBuilder] Failed to update Vulkan layer with custom ReShade — {ex.Message}");
                    }

                    _window.ViewModel.SaveSettingsPublic();
                }
                else
                {
                    // Non-Vulkan: per-game only
                    _window.ViewModel.SetReShadeChannelOverride(ctx.CapturedName, "Custom");
                    if (targetCardCustom != null && targetCardCustom.UseNormalReShade)
                        _window.ViewModel.SetUseNormalReShade(targetCardCustom, false);

                    // Reinstall with custom DLLs
                    if (targetCardCustom != null)
                        await _window.ViewModel.InstallReShadeCommand.ExecuteAsync(targetCardCustom);
                }

                defaultChannelSelection = "Custom";
                _window.ViewModel.NotifyUpdateButtonChanged();

                // Rebuild overrides panel to reflect update inclusion changes
                var refreshCardCustom = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(ctx.CapturedName, StringComparison.OrdinalIgnoreCase));
                if (refreshCardCustom != null)
                    BuildOverridesPanel(refreshCardCustom);
                return;
            }

            // ── "No Addons" — switch to normal (non-addon) ReShade ──
            if (selected == "No Addons")
            {
                CrashReporter.Log($"[DetailPanelBuilder.RSChannel] '{ctx.CapturedName}' → No Addons mode");
                var targetCardNoAddon = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(ctx.CapturedName, StringComparison.OrdinalIgnoreCase));
                if (targetCardNoAddon != null && !targetCardNoAddon.UseNormalReShade)
                {
                    _window.ViewModel.SetUseNormalReShade(targetCardNoAddon, true);
                    // Clear any channel override since we're switching to normal
                    _window.ViewModel.SetReShadeChannelOverride(ctx.CapturedName, null);
                    // Auto-install the normal (no-addon) version
                    if (targetCardNoAddon.RsStatus == GameStatus.NotInstalled || targetCardNoAddon.IsRsInstalled)
                        await _window.ViewModel.InstallReShadeCommand.ExecuteAsync(targetCardNoAddon);
                }
                defaultChannelSelection = "No Addons";
                // Rebuild to grey out addon controls
                var refreshCardNoAddon = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(ctx.CapturedName, StringComparison.OrdinalIgnoreCase));
                if (refreshCardNoAddon != null)
                    BuildOverridesPanel(refreshCardNoAddon);
                return;
            }

            // ── Switching away from "No Addons" — re-enable addon ReShade ──
            {
                var targetCardReEnable = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(ctx.CapturedName, StringComparison.OrdinalIgnoreCase));
                if (targetCardReEnable != null && targetCardReEnable.UseNormalReShade)
                {
                    CrashReporter.Log($"[DetailPanelBuilder.RSChannel] '{ctx.CapturedName}' → leaving No Addons mode, re-enabling addon support");
                    _window.ViewModel.SetUseNormalReShade(targetCardReEnable, false);
                }
            }

            string? channelValue = selected switch
            {
                "Nightly" => "Nightly",
                _ => null, // "Stable" = default, clears the per-game override
            };
            CrashReporter.Log($"[DetailPanelBuilder.RSChannel] '{ctx.CapturedName}' → channelValue={channelValue ?? "Stable (null)"}");

            var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(ctx.CapturedName, StringComparison.OrdinalIgnoreCase));

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
                foreach (var vCard in _window.ViewModel.AllCards.Where(c => c.RequiresVulkanInstall))
                {
                    _window.ViewModel.SetReShadeChannelOverride(vCard.GameName, channelValue);
                    vCard.NotifyAll();
                }

                // Determine the effective channel for the Vulkan layer
                var effectiveChannel = channelValue ?? _window.ViewModel.Settings.ReShadeChannel;

                // Update the Vulkan layer DLLs
                try
                {
                    var layerDir = VulkanLayerService.LayerDirectory;
                    var stagedPath64 = AuxInstallService.GetStagedPathForChannel(effectiveChannel, false);
                    var stagedPath32 = AuxInstallService.GetStagedPathForChannel(effectiveChannel, true);
                    var layer64 = Path.Combine(layerDir, VulkanLayerService.LayerDllName);
                    var layer32 = Path.Combine(layerDir, "ReShade32.dll");

                    if (File.Exists(stagedPath64) && new FileInfo(stagedPath64).Length > AuxInstallService.MinReShadeSize && File.Exists(layer64))
                        AuxInstallService.CopyFileWithElevation(stagedPath64, layer64);
                    if (File.Exists(stagedPath32) && new FileInfo(stagedPath32).Length > AuxInstallService.MinReShadeSize && File.Exists(layer32))
                        AuxInstallService.CopyFileWithElevation(stagedPath32, layer32);
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[DetailPanelBuilder] Failed to update Vulkan layer — {ex.Message}");
                }

                // Mark all Vulkan games as installed (layer updated in-place)
                foreach (var vCard in _window.ViewModel.AllCards.Where(c => c.RequiresVulkanInstall && c.IsRsInstalled))
                {
                    vCard.RsStatus = GameStatus.Installed;
                    vCard.NotifyAll();
                }

                // Rebuild overrides panel to reflect update inclusion changes
                var refreshCardVulkan = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(ctx.CapturedName, StringComparison.OrdinalIgnoreCase));
                if (refreshCardVulkan != null)
                    BuildOverridesPanel(refreshCardVulkan);
            }
            else
            {
                // ── Non-Vulkan game: per-game only ──
                CrashReporter.Log($"[DetailPanelBuilder.RSChannel] '{ctx.CapturedName}' → setting per-game override to: {channelValue ?? "(null/Global)"}");
                _window.ViewModel.SetReShadeChannelOverride(ctx.CapturedName, channelValue);

                // Auto-reinstall ReShade with the new channel if it's currently installed
                if (targetCard != null && targetCard.IsRsInstalled)
                {
                    await _window.ViewModel.InstallReShadeCommand.ExecuteAsync(targetCard);
                }

                // Rebuild overrides panel to reflect update inclusion changes
                var refreshCard2 = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(ctx.CapturedName, StringComparison.OrdinalIgnoreCase));
                if (refreshCard2 != null)
                    BuildOverridesPanel(refreshCard2);
            }

            _window.ViewModel.NotifyUpdateButtonChanged();
        };

        Grid.SetRow(channelLabel, 0); Grid.SetColumn(channelLabel, 2);
        Grid.SetRow(channelCombo, 1); Grid.SetColumn(channelCombo, 2);
        bitnessPanel.Children.Add(channelLabel);
        bitnessPanel.Children.Add(channelCombo);
        channelComboInitializing = false;

        // Disable RS Channel when Luma mode is active (Luma bundles its own ReShade)
        if (card.IsLumaMode)
            channelCombo.IsEnabled = false;

        // ── Global update inclusion (compact: button + summary) ──────────────────
        var capturedCard = card;

        var (updateInclusionBtn, updateSummaryText) = UpdateInclusionHelper.CreateUpdateInclusionControls(
            _window.ViewModel, ctx.CapturedName, card.IsREEngineGame, _window.Content.XamlRoot,
            onSaved: () =>
            {
                // Rebuild the detail panel so component rows reflect the new exclusion state
                // (e.g. ReShade row un-greys when REF is excluded)
                var refreshCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(ctx.CapturedName, StringComparison.OrdinalIgnoreCase));
                if (refreshCard != null)
                {
                    _window.PopulateDetailPanel(refreshCard);
                    BuildOverridesPanel(refreshCard);
                }
            },
            isDxvkEnabled: card.DxvkEnabled);

        var toggleRow = new StackPanel { Spacing = 0 };
        ToolTipService.SetToolTip(updateInclusionBtn, "Choose which components are included in Update All for this game.");
        toggleRow.Children.Add(updateInclusionBtn);
        toggleRow.Children.Add(updateSummaryText);

        // ── Global update inclusion section (Middle Row right column) ────────────
        var globalUpdateColumn = new StackPanel { Spacing = 0 };
        globalUpdateColumn.Children.Add(new TextBlock
        {
            Text = "Global update inclusion",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            Margin = new Thickness(0, 0, 0, 8),
        });
        globalUpdateColumn.Children.Add(toggleRow);

        // ── Middle Row vertical divider ──────────────────────────────────────────
        var middleRowDivider = new Border
        {
            Width = 1,
            Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(12, 0, 12, 0),
        };

        // ── Middle Row Grid (3 columns: Star | Auto | Star) — Bitness/API + Global update ──
        var middleRowGrid = new Grid { ColumnSpacing = 0 };
        middleRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        middleRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        middleRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Grid.SetColumn(bitnessPanel, 0);
        Grid.SetColumn(middleRowDivider, 1);
        Grid.SetColumn(globalUpdateColumn, 2);

        middleRowGrid.Children.Add(bitnessPanel);
        middleRowGrid.Children.Add(middleRowDivider);
        middleRowGrid.Children.Add(globalUpdateColumn);

        _window.OverridesPanel.Children.Add(middleRowGrid);
        _window.OverridesPanel.Children.Add(UIFactory.MakeSeparator());

    }
}