// DetailPanelBuilder.Overrides.ShadersAddons.cs — Shaders, Addons, Launch, and Reset Overrides sections.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    /// <summary>Builds the Shaders/Addons row, Launch executable, and Reset Overrides handler.</summary>
    private void BuildShadersAddonsSection(OverridesPanelCtx ctx)
    {
        var card = ctx.Card;
        var gameName = ctx.GameName;
        var isLumaMode = ctx.IsLumaMode;

        // ── Combined "Shaders and Addons" Row (3 columns: Star | Auto | Star) ──
        var shadersAddonsRowGrid = new Grid { ColumnSpacing = 0 };
        shadersAddonsRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        shadersAddonsRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        shadersAddonsRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // ── Left column: "Shaders and Addons" ──
        var shadersAddonsLeftColumn = new StackPanel { Spacing = 6 };
        shadersAddonsLeftColumn.Children.Add(new TextBlock
        {
            Text = "Shaders and Addons",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            Margin = new Thickness(0, 0, 0, 4),
        });

        // Shader + Addon ComboBoxes side by side in a 2-column grid
        var shaderAddonGrid = new Grid { ColumnSpacing = 12 };
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
        Grid.SetRow(ctx.ShaderModeCombo, 1); Grid.SetColumn(ctx.ShaderModeCombo, 0);
        shaderAddonGrid.Children.Add(ctx.ShaderModeCombo);

        // ── Per-game Addon mode ComboBox ─────────────────────────────────────
        string currentAddonMode = _window.ViewModel.GetPerGameAddonMode(gameName);
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
            CrashReporter.Log($"[DetailPanelBuilder.AddonMode] '{ctx.CapturedName}' selection changed to: '{selected}'");

            if (selected == "Select")
            {
                List<string>? current = _gameNameService.PerGameAddonSelection.TryGetValue(gameName, out var existingAddons)
                    ? existingAddons
                    : null;

                IAddonPackService? addonPackService = null;
                var addonSvcProp = _window.ViewModel.GetType().GetProperty("AddonPackServiceInstance");
                if (addonSvcProp != null)
                    addonPackService = addonSvcProp.GetValue(_window.ViewModel) as IAddonPackService;

                if (addonPackService == null)
                {
                    var infoDlg = new ContentDialog
                    {
                        Title = "Select Addons",
                        Content = new TextBlock
                        {
                            Text = "Addon service is not yet wired. Complete Task 9.1 to enable addon selection.",
                            FontSize = 13,
                            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
                        },
                        CloseButtonText = "OK",
                        XamlRoot = _window.Content.XamlRoot,
                        Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
                        RequestedTheme = ElementTheme.Dark,
                    };
                    await DialogService.ShowSafeAsync(infoDlg);
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
                    _gameNameService.PerGameAddonSelection[gameName] = result;
                    _window.ViewModel.SetPerGameAddonMode(ctx.CapturedName, "Select");
                    _window.ViewModel.DeployAddonsForCard(ctx.CapturedName);
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
            {
                _window.ViewModel.SetPerGameAddonMode(ctx.CapturedName, "Off");
                _window.ViewModel.DeployAddonsForCard(ctx.CapturedName);
            }
            else // "Global"
            {
                _window.ViewModel.SetPerGameAddonMode(ctx.CapturedName, "Global");
                _window.ViewModel.DeployAddonsForCard(ctx.CapturedName);
            }
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

        shadersAddonsLeftColumn.Children.Add(shaderAddonGrid);

        // "Select ReShade Preset" button
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
            Margin = new Thickness(0, 8, 0, 0),
        };
        ToolTipService.SetToolTip(presetBtn,
            "Pick .ini preset files to copy to this game's folder. Place presets in the reshade-presets folder.");
        presetBtn.Click += async (s, ev) =>
        {
            var selected = await PresetPopupHelper.ShowAsync(_window.Content.XamlRoot);
            if (selected != null && selected.Count > 0)
            {
                var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(ctx.CapturedName, StringComparison.OrdinalIgnoreCase));
                if (targetCard != null && !string.IsNullOrEmpty(targetCard.InstallPath))
                {
                    int count = PresetPopupHelper.DeployPresets(selected, targetCard.InstallPath);
                    CrashReporter.Log($"[DetailPanelBuilder] Deployed {count} preset(s) to '{ctx.CapturedName}'");

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
                            await _window.ViewModel.ApplyPresetShadersAsync(ctx.CapturedName, presetPaths);

                            // Rebuild overrides panel so the shader combo reflects the new "Select" mode
                            var refreshCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                                c.GameName.Equals(ctx.CapturedName, StringComparison.OrdinalIgnoreCase));
                            if (refreshCard != null)
                                BuildOverridesPanel(refreshCard);
                        }
                    }
                }
            }
        };
        shadersAddonsLeftColumn.Children.Add(presetBtn);

        Grid.SetColumn(shadersAddonsLeftColumn, 0);
        shadersAddonsRowGrid.Children.Add(shadersAddonsLeftColumn);

        // Vertical divider
        var shadersAddonsDivider = new Border
        {
            Width = 1,
            Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(12, 0, 12, 0),
        };
        Grid.SetColumn(shadersAddonsDivider, 1);
        shadersAddonsRowGrid.Children.Add(shadersAddonsDivider);

        // ── Right column: "Launch executable" (grid-aligned with left column) ──
        var shadersAddonsRightColumn = new Grid { RowSpacing = 6 };
        shadersAddonsRightColumn.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 0: label + spacer (matches left title + sub-labels)
        shadersAddonsRightColumn.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 1: exe path + args side by side
        shadersAddonsRightColumn.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 2: buttons (aligns with preset btn)

        // Label + spacer to match left column's "Shaders and Addons" title + sub-label row
        var launchExeHeaderPanel = new StackPanel { Spacing = 4 };
        launchExeHeaderPanel.Children.Add(new TextBlock
        {
            Text = "Launch executable",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
        });
        // Invisible spacer matching the "Shaders" / "Addons" sub-label height
        launchExeHeaderPanel.Children.Add(new TextBlock
        {
            Text = " ",
            FontSize = 11,
        });
        Grid.SetRow(launchExeHeaderPanel, 0);
        shadersAddonsRightColumn.Children.Add(launchExeHeaderPanel);

        var currentLaunchExe = _gameNameService.LaunchExeOverrides
            .TryGetValue(ctx.CapturedName, out var savedExe) ? savedExe : "";
        var launchExeBox = new TextBox
        {
            Text = currentLaunchExe,
            PlaceholderText = "Auto-detect (or paste path)",
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        ToolTipService.SetToolTip(launchExeBox,
            "Override the executable used when launching this game. Leave blank for auto-detection (largest exe in install folder).");
        launchExeBox.LostFocus += (s, ev) =>
        {
            var newPath = launchExeBox.Text.Trim();
            if (string.IsNullOrEmpty(newPath))
                _gameNameService.LaunchExeOverrides.Remove(ctx.CapturedName);
            else
                _gameNameService.LaunchExeOverrides[ctx.CapturedName] = newPath;
            _window.ViewModel.SaveSettingsPublic();
        };

        var currentLaunchArgs = _gameNameService.LaunchArgsOverrides
            .TryGetValue(ctx.CapturedName, out var savedArgs) ? savedArgs : "";
        var launchArgsBox = new TextBox
        {
            Text = currentLaunchArgs,
            PlaceholderText = "Launch arguments",
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var argsTooltip = "Command-line arguments passed to the game on launch. Saves on focus lost.";
        if (card.Source.Equals("Epic", StringComparison.OrdinalIgnoreCase))
            argsTooltip += "\n\nNote: Setting arguments disables Epic protocol launch. EOS-protected games may fail to launch with arguments.";
        ToolTipService.SetToolTip(launchArgsBox, argsTooltip);
        launchArgsBox.LostFocus += (s, ev) =>
        {
            var newArgs = launchArgsBox.Text.Trim();
            if (string.IsNullOrEmpty(newArgs))
                _gameNameService.LaunchArgsOverrides.Remove(ctx.CapturedName);
            else
                _gameNameService.LaunchArgsOverrides[ctx.CapturedName] = newArgs;
            _window.ViewModel.SaveSettingsPublic();
        };

        var launchBoxRow = new Grid { ColumnSpacing = 8 };
        launchBoxRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        launchBoxRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(launchExeBox, 0);
        Grid.SetColumn(launchArgsBox, 1);
        launchBoxRow.Children.Add(launchExeBox);
        launchBoxRow.Children.Add(launchArgsBox);

        Grid.SetRow(launchBoxRow, 1);
        shadersAddonsRightColumn.Children.Add(launchBoxRow);

        var launchBtnRow = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 8, 0, 0) };
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
                _gameNameService.LaunchExeOverrides[ctx.CapturedName] = filePath;
                _window.ViewModel.SaveSettingsPublic();
            }
        };
        Grid.SetColumn(browseLaunchBtn, 0);
        ToolTipService.SetToolTip(browseLaunchBtn, "Browse for a game executable to use as the launch target.");
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
            _gameNameService.LaunchExeOverrides.Remove(ctx.CapturedName);
            _window.ViewModel.SaveSettingsPublic();
        };
        Grid.SetColumn(resetLaunchBtn, 1);
        ToolTipService.SetToolTip(resetLaunchBtn, "Clear the launch executable override and revert to auto-detection.");
        launchBtnRow.Children.Add(resetLaunchBtn);
        Grid.SetRow(launchBtnRow, 2);
        shadersAddonsRightColumn.Children.Add(launchBtnRow);

        Grid.SetColumn(shadersAddonsRightColumn, 2);
        shadersAddonsRowGrid.Children.Add(shadersAddonsRightColumn);

        _window.OverridesPanel.Children.Add(shadersAddonsRowGrid);

        var resetOverridesBtn = new Button
        {
            Content = "Reset Overrides",
            FontSize = 12,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = UIFactory.Brush(ResourceKeys.AccentRedBgBrush),
            Foreground = UIFactory.Brush(ResourceKeys.AccentRedBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.AccentPurpleBorderBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
        };
        ctx.ResetOverridesBtn = resetOverridesBtn;
        resetOverridesBtn.Click += (s, ev) =>
        {
            // Reset all controls to defaults
            ctx.DetectedBox.Text = ctx.OriginalStoreName ?? gameName;
            ctx.WikiBox.Text = "";
            ctx.ShaderComboInitializing = true;
            ctx.ShaderModeCombo.SelectedItem = "Global";
            ctx.ShaderComboInitializing = false;
            addonComboInitializing = true;
            addonModeCombo.SelectedItem = "Global";
            addonComboInitializing = false;
            if (ctx.RenderPathCombo != null) ctx.RenderPathCombo.SelectedItem = "DirectX";
            ctx.DllOverrideToggle.IsOn = false;
            // Reset update inclusion to all-included
            if (_window.ViewModel.IsUpdateAllExcludedReShade(ctx.CapturedName))
                _window.ViewModel.ToggleUpdateAllExclusionReShade(ctx.CapturedName);
            if (_window.ViewModel.IsUpdateAllExcludedRenoDx(ctx.CapturedName))
                _window.ViewModel.ToggleUpdateAllExclusionRenoDx(ctx.CapturedName);
            if (_window.ViewModel.IsUpdateAllExcludedUl(ctx.CapturedName))
                _window.ViewModel.ToggleUpdateAllExclusionUl(ctx.CapturedName);
            if (_window.ViewModel.IsUpdateAllExcludedDc(ctx.CapturedName))
                _window.ViewModel.ToggleUpdateAllExclusionDc(ctx.CapturedName);
            if (_window.ViewModel.IsUpdateAllExcludedOs(ctx.CapturedName))
                _window.ViewModel.ToggleUpdateAllExclusionOs(ctx.CapturedName);
            UpdateInclusionHelper.RefreshSummary(ctx.UpdateSummaryText, _window.ViewModel, ctx.CapturedName, card.IsREEngineGame, card.DxvkEnabled);
            ctx.WikiExcludeCombo.SelectedItem = "Included";

            // Persist all reset values immediately
            var resetName = (ctx.OriginalStoreName ?? gameName).Trim();
            bool nameChanged = !resetName.Equals(ctx.CapturedName, StringComparison.OrdinalIgnoreCase);
            if (nameChanged && !string.IsNullOrWhiteSpace(resetName))
            {
                _window.ViewModel.RenameGame(ctx.CapturedName, resetName);
                ctx.CapturedName = resetName;
            }

            // Remove wiki mapping
            if (_window.ViewModel.GetNameMapping(ctx.CapturedName) != null)
                _window.ViewModel.RemoveNameMapping(ctx.CapturedName);

            // Shader mode → Global
            if (_window.ViewModel.GetPerGameShaderMode(ctx.CapturedName) != "Global")
            {
                _window.ViewModel.SetPerGameShaderMode(ctx.CapturedName, "Global");
                _gameNameService.PerGameShaderSelection.Remove(ctx.CapturedName);
                _window.ViewModel.DeployShadersForCard(ctx.CapturedName);
            }

            // Addon mode → Global
            if (_window.ViewModel.GetPerGameAddonMode(ctx.CapturedName) != "Global")
            {
                _window.ViewModel.SetPerGameAddonMode(ctx.CapturedName, "Global");
                _window.ViewModel.DeployAddonsForCard(ctx.CapturedName);
            }

            // Disable DLL override
            if (_window.ViewModel.HasDllOverride(ctx.CapturedName))
            {
                var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(ctx.CapturedName, StringComparison.OrdinalIgnoreCase));
                if (targetCard != null)
                    _window.ViewModel.DisableDllOverride(targetCard);
            }

            // Include all in Update All
            if (_window.ViewModel.IsUpdateAllExcludedReShade(ctx.CapturedName))
                _window.ViewModel.ToggleUpdateAllExclusionReShade(ctx.CapturedName);
            if (_window.ViewModel.IsUpdateAllExcludedRenoDx(ctx.CapturedName))
                _window.ViewModel.ToggleUpdateAllExclusionRenoDx(ctx.CapturedName);
            if (_window.ViewModel.IsUpdateAllExcludedUl(ctx.CapturedName))
                _window.ViewModel.ToggleUpdateAllExclusionUl(ctx.CapturedName);
            if (_window.ViewModel.IsUpdateAllExcludedDc(ctx.CapturedName))
                _window.ViewModel.ToggleUpdateAllExclusionDc(ctx.CapturedName);
            if (_window.ViewModel.IsUpdateAllExcludedOs(ctx.CapturedName))
                _window.ViewModel.ToggleUpdateAllExclusionOs(ctx.CapturedName);

            // Disable wiki exclusion
            if (_window.ViewModel.IsWikiExcluded(ctx.CapturedName))
                _window.ViewModel.ToggleWikiExclusion(ctx.CapturedName);

            // Reset Normal ReShade (via channel combo)
            {
                var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(ctx.CapturedName, StringComparison.OrdinalIgnoreCase));
                if (targetCard != null && targetCard.UseNormalReShade)
                    _window.ViewModel.SetUseNormalReShade(targetCard, false);
            }

            // Reset DXVK toggles
            if (ctx.DxvkToggle != null)
            {
                ctx.DxvkToggle.IsOn = false;
                var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(ctx.CapturedName, StringComparison.OrdinalIgnoreCase));
                if (targetCard != null && targetCard.DxvkEnabled)
                    _ = _window.ViewModel.HandleDxvkToggleAsync(targetCard, false, _window.Content.XamlRoot);
            }

            // Reset DXVK update exclusion via the shared Update Inclusion system
            if (_window.ViewModel.IsUpdateAllExcludedDxvk(ctx.CapturedName))
                _window.ViewModel.ToggleUpdateAllExclusionDxvk(ctx.CapturedName);

            // Reset bitness override to Auto
            ctx.BitnessCombo.SelectedItem = "Auto";
            _window.ViewModel.SetBitnessOverride(ctx.CapturedName, null);

            // Reset API overrides
            ctx.ApiCombo.SelectedItem = "Auto";
            _window.ViewModel.SetApiOverride(ctx.CapturedName, null);

            // Reset ReShade channel override
            ctx.ChannelCombo.SelectedItem = "Stable";
            _window.ViewModel.SetReShadeChannelOverride(ctx.CapturedName, null);

            // Reset custom ReShade DLL selection
            _gameNameService.CustomReShadeSelection.Remove(ctx.CapturedName);

            // Reset launch exe override
            _gameNameService.LaunchExeOverrides.Remove(ctx.CapturedName);
            _gameNameService.LaunchArgsOverrides.Remove(ctx.CapturedName);
            _window.ViewModel.SaveSettingsPublic();
            launchExeBox.Text = "";
            launchArgsBox.Text = "";

            // Revert card properties to auto-detected values
            {
                var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(ctx.CapturedName, StringComparison.OrdinalIgnoreCase));
                if (targetCard != null)
                {
                    // Re-resolve bitness from PE header auto-detection
                    var detectedMachine = _peHeaderService.DetectGameArchitecture(targetCard.InstallPath);
                    targetCard.Is32Bit = _window.ViewModel.ResolveIs32Bit(ctx.CapturedName, detectedMachine);

                    // Re-detect APIs from scanning (overrides are now cleared)
                    targetCard.DetectedApis = _window.ViewModel._DetectAllApisForCard(targetCard.InstallPath, ctx.CapturedName);
                    targetCard.IsDualApiGame = GraphicsApiDetector.IsDualApi(targetCard.DetectedApis);
                    targetCard.GraphicsApi = _window.ViewModel.DetectGraphicsApi(
                        targetCard.InstallPath, EngineType.Unknown, ctx.CapturedName);

                    // Bitness changed — no need to update placeholder

                    targetCard.NotifyAll();
                }
            }

            // Reset DLSS presets to Default
            {
                var presetSvc = _dlssPresetService;
                if (presetSvc.IsSupported)
                {
                    presetSvc.SetSrPreset(ctx.CapturedName, card.InstallPath, 0);
                    presetSvc.SetRrPreset(ctx.CapturedName, card.InstallPath, 0);
                    presetSvc.SetFgPreset(ctx.CapturedName, card.InstallPath, 0);
                }
            }

            CrashReporter.Log($"[DetailPanelBuilder.BuildOverridesPanel] Overrides reset for: {ctx.CapturedName}");

            // Only reselect if the game name actually changed
            if (nameChanged)
                _window.RequestReselect(ctx.CapturedName);
        };
        // resetOverridesBtn is hidden — triggered via Management panel automation peer
        resetOverridesBtn.Visibility = Visibility.Collapsed;
        _window.OverridesPanel.Children.Add(resetOverridesBtn);
    }
}