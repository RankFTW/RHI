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
        _window.ViewModel.SetLastUiAction($"BuildShadersAddonsSection({ctx.Card.GameName})");
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

        // ── Per-game Addon mode ComboBox (logic shared with the ReShade ⚙: DetailPanelBuilder.Overrides.RsShared.cs) ──
        bool addonComboInitializing = true;

        var addonModeCombo = new ComboBox
        {
            ItemsSource = AddonModeItems,
            SelectedItem = GetAddonModeDisplay(card),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = !card.UseNormalReShade,
        };
        ToolTipService.SetToolTip(addonModeCombo,
            "Global = use global addon set. Select = pick per-game addons. Off = no addons for this game.");

        // Allow re-opening the Select picker when already on Select
        addonModeCombo.DropDownClosed += (s, ev) =>
        {
            if (addonComboInitializing) return;
            var current = addonModeCombo.SelectedItem as string;
            if (current == "Select" && _window.ViewModel.GetPerGameAddonMode(ctx.CapturedName, ctx.Card.Source) == "Select")
            {
                addonComboInitializing = true;
                addonModeCombo.SelectedItem = "Global";
                addonComboInitializing = false;
                addonModeCombo.SelectedItem = "Select";
            }
        };

        addonModeCombo.SelectionChanged += async (s, ev) =>
        {
            try
            {
                if (addonComboInitializing) return;
                var selected = addonModeCombo.SelectedItem as string;
                if (string.IsNullOrEmpty(selected)) return;

                if (!await ApplyAddonModeAsync(ctx.Card, ctx.CapturedName, selected))
                {
                    // Cancelled — revert to the saved mode
                    addonComboInitializing = true;
                    addonModeCombo.SelectedItem = GetAddonModeDisplay(ctx.Card);
                    addonComboInitializing = false;
                }
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[DetailPanelBuilder.AddonMode] Addon mode change failed — {ex}");
            }
        };
        addonComboInitializing = false;

        var addonLabel = new TextBlock
        {
            Text = "Addons",
            FontSize = 11,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
        };
        // The Addons combo is disabled under the "No Addons" ReShade channel and WinUI
        // doesn't show tooltips on disabled controls, so the reason goes on the label.
        if (card.UseNormalReShade)
            ToolTipService.SetToolTip(addonLabel,
                "Addons need the addon build of ReShade. Change the ReShade channel to enable this.");
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
            try { await PickReShadePresetAsync(ctx.Card, ctx.CapturedName); }
            catch (Exception ex) { CrashReporter.Log($"[DetailPanelBuilder] Select ReShade Preset failed — {ex}"); }
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

        // Resolve the effective launch exe for display — user override > manifest > auto-detected
        var currentLaunchExe = _window.ViewModel.GetLaunchExeOverride(ctx.CapturedName);
        var _exeExclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "UnityCrashHandler64", "UnityCrashHandler32", "CrashReporter", "CrashHandler",
              "unins000", "Launcher", "BEService", "EasyAntiCheat",
              "VC_redist.x64", "VC_redist.x86", "vcredist_x64", "vcredist_x86",
              "dxwebsetup", "UEPrereqSetup_x64", "UEPrereqSetup_x86" };
        string? effectiveExe = !string.IsNullOrEmpty(currentLaunchExe)
            ? Path.GetFileName(currentLaunchExe)
            : (_window.ViewModel.Manifest?.LaunchExeOverrides?.TryGetValue(ctx.CapturedName, out var manifestExe) == true && !string.IsNullOrEmpty(manifestExe)
                ? Path.GetFileName(manifestExe)
                : null); // exe scan deferred — label updates asynchronously

        var headerText = string.IsNullOrEmpty(effectiveExe)
            ? "Launch executable"
            : $"Launch executable  —  {effectiveExe}";
        var exeHeaderText = new TextBlock
        {
            Text = headerText,
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        ToolTipService.SetToolTip(exeHeaderText,
            "The program RHI starts when you click Launch. Detected automatically unless you set one below.");
        launchExeHeaderPanel.Children.Add(exeHeaderText);

        // Async exe scan — updates the label without blocking the UI thread
        if (effectiveExe == null)
        {
            _ = Task.Run(() =>
            {
                string? scannedExe = null;
                try
                {
                    if (!string.IsNullOrEmpty(card.InstallPath) && Directory.Exists(card.InstallPath))
                        scannedExe = Directory.GetFiles(card.InstallPath, "*.exe", SearchOption.TopDirectoryOnly)
                            .Where(e => !_exeExclusions.Contains(Path.GetFileNameWithoutExtension(e)))
                            .OrderByDescending(e => new FileInfo(e).Length)
                            .Select(Path.GetFileName)
                            .FirstOrDefault();
                }
                catch (Exception ex) { CrashReporter.Log($"[DetailPanelBuilder.BuildShadersAddonsSection] Exe scan failed for '{card.InstallPath}' — {ex.Message}"); }
                if (scannedExe != null)
                    _window.DispatcherQueue?.TryEnqueue(() =>
                    {
                        if (_window.ViewModel.SelectedGame == card)
                            exeHeaderText.Text = $"Launch executable  —  {scannedExe}";
                    });
            });
        }
        // Invisible spacer matching the "Shaders" / "Addons" sub-label height
        launchExeHeaderPanel.Children.Add(new TextBlock
        {
            Text = " ",
            FontSize = 11,
        });
        Grid.SetRow(launchExeHeaderPanel, 0);
        shadersAddonsRightColumn.Children.Add(launchExeHeaderPanel);

        // currentLaunchExe already resolved above
        var launchExeBox = new TextBox
        {
            Text = currentLaunchExe,
            PlaceholderText = "Auto-detect (or paste path)",
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        ToolTipService.SetToolTip(launchExeBox,
            "Override the executable used when launching this game. Leave blank for auto-detection (largest exe in install folder).");
        // Saves on Enter and when focus leaves (shared with the game's ⚙: MainViewModel.LaunchOptions.cs)
        launchExeBox.LostFocus += (s, ev) => _window.ViewModel.SetLaunchExeOverride(ctx.CapturedName, launchExeBox.Text);
        launchExeBox.KeyDown += (s, ev) =>
        {
            if (ev.Key == Windows.System.VirtualKey.Enter) _window.ViewModel.SetLaunchExeOverride(ctx.CapturedName, launchExeBox.Text);
        };

        var currentLaunchArgs = _window.ViewModel.GetLaunchArgsOverride(ctx.CapturedName);
        var launchArgsBox = new TextBox
        {
            Text = currentLaunchArgs,
            PlaceholderText = "Launch arguments",
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var argsTooltip = "Command-line arguments passed to the game on launch. Saves on Enter or when you click elsewhere.";
        if (card.Source.Equals("Epic", StringComparison.OrdinalIgnoreCase))
            argsTooltip += "\n\nNote: Setting arguments disables Epic protocol launch. EOS-protected games may fail to launch with arguments.";
        ToolTipService.SetToolTip(launchArgsBox, argsTooltip);
        launchArgsBox.LostFocus += (s, ev) => _window.ViewModel.SetLaunchArgsOverride(ctx.CapturedName, launchArgsBox.Text);
        launchArgsBox.KeyDown += (s, ev) =>
        {
            if (ev.Key == Windows.System.VirtualKey.Enter) _window.ViewModel.SetLaunchArgsOverride(ctx.CapturedName, launchArgsBox.Text);
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
            try
            {
                var filePath = await MainViewModel.BrowseForLaunchExeAsync(WinRT.Interop.WindowNative.GetWindowHandle(_window), card);
                if (string.IsNullOrEmpty(filePath)) return;
                launchExeBox.Text = filePath;
                _window.ViewModel.SetLaunchExeOverride(ctx.CapturedName, filePath);
            }
            catch (Exception ex) { CrashReporter.Log($"[DetailPanelBuilder] Browse for launch exe failed — {ex.Message}"); }
        };
        Grid.SetColumn(browseLaunchBtn, 0);
        ToolTipService.SetToolTip(browseLaunchBtn, "Browse for a game executable to use as the launch target.");
        launchBtnRow.Children.Add(browseLaunchBtn);

        var resetLaunchBtn = new Button
        {
            Content = "Reset launch options",
            FontSize = 12,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        ApplyResetButtonStyle(resetLaunchBtn);
        resetLaunchBtn.Click += (s, ev) =>
        {
            launchExeBox.Text = "";
            launchArgsBox.Text = "";
            _window.ViewModel.SetLaunchExeOverride(ctx.CapturedName, "");
            _window.ViewModel.SetLaunchArgsOverride(ctx.CapturedName, "");
        };
        Grid.SetColumn(resetLaunchBtn, 1);
        ToolTipService.SetToolTip(resetLaunchBtn, "Clear the launch executable and launch arguments for this game. The executable goes back to auto-detection.");
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
            MinWidth = 160,
            Padding = new Thickness(16, 0, 16, 0),
            Margin = new Thickness(0, 4, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = UIFactory.Brush(ResourceKeys.AccentRedBgBrush),
            Foreground = UIFactory.Brush(ResourceKeys.AccentRedBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.AccentPurpleBorderBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
        };
        ToolTipService.SetToolTip(resetOverridesBtn, "Reset all per-game overrides back to defaults (DLL names, channels, shaders, addons, launch settings, update inclusion).");
        ctx.ResetOverridesBtn = resetOverridesBtn;
        Action resetAction = () =>
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
            ctx.ResetDllOverrides?.Invoke();
            // Reset update inclusion to all-included
            if (_window.ViewModel.IsUpdateAllExcludedReShade(ctx.CapturedName, card.Source))
                _window.ViewModel.ToggleUpdateAllExclusionReShade(ctx.CapturedName, card.Source);
            if (_window.ViewModel.IsUpdateAllExcludedRenoDx(ctx.CapturedName, card.Source))
                _window.ViewModel.ToggleUpdateAllExclusionRenoDx(ctx.CapturedName, card.Source);
            if (_window.ViewModel.IsUpdateAllExcludedUl(ctx.CapturedName, card.Source))
                _window.ViewModel.ToggleUpdateAllExclusionUl(ctx.CapturedName, card.Source);
            if (_window.ViewModel.IsUpdateAllExcludedDc(ctx.CapturedName, card.Source))
                _window.ViewModel.ToggleUpdateAllExclusionDc(ctx.CapturedName, card.Source);
            if (_window.ViewModel.IsUpdateAllExcludedOs(ctx.CapturedName, card.Source))
                _window.ViewModel.ToggleUpdateAllExclusionOs(ctx.CapturedName, card.Source);
            UpdateInclusionHelper.RefreshSummary(ctx.UpdateSummaryText, _window.ViewModel, ctx.CapturedName, card.IsREEngineGame, card.DxvkEnabled, card.Source ?? "");
            ctx.WikiExcludeCombo.SelectedItem = WikiIncludedItem;

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
            if (_window.ViewModel.GetPerGameShaderMode(ctx.CapturedName, ctx.Card.Source) != "Global")
            {
                _window.ViewModel.SetPerGameShaderMode(ctx.CapturedName, "Global", ctx.Card.Source);
                ctx.Card.ShaderModeOverride = null; // update card in-memory so DeployShadersForCard uses global
                var shaderSelKey = GameKey.FromCard(ctx.CapturedName, ctx.Card.Source).ToKey();
                _gameNameService.PerGameShaderSelection.Remove(shaderSelKey);
                _gameNameService.PerGameShaderSelection.Remove(ctx.CapturedName); // legacy fallback
                _window.ViewModel.DeployShadersForCard(ctx.CapturedName);
            }

            // Addon mode → Global
            if (_window.ViewModel.GetPerGameAddonMode(ctx.CapturedName, ctx.Card.Source) != "Global")
            {
                _window.ViewModel.SetPerGameAddonMode(ctx.CapturedName, "Global", ctx.Card.Source);
                _window.ViewModel.DeployAddonsForCard(ctx.CapturedName);
            }

            // Disable DLL override
            if (_window.ViewModel.HasDllOverride(ctx.CapturedName))
            {
                var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(ctx.CapturedName, StringComparison.OrdinalIgnoreCase));
                if (targetCard != null)
                {
                    _window.ViewModel.DisableDllOverride(targetCard);
                    targetCard.NotifyAll();
                }
            }

            // Include all in Update All
            if (_window.ViewModel.IsUpdateAllExcludedReShade(ctx.CapturedName, card.Source))
                _window.ViewModel.ToggleUpdateAllExclusionReShade(ctx.CapturedName, card.Source);
            if (_window.ViewModel.IsUpdateAllExcludedRenoDx(ctx.CapturedName, card.Source))
                _window.ViewModel.ToggleUpdateAllExclusionRenoDx(ctx.CapturedName, card.Source);
            if (_window.ViewModel.IsUpdateAllExcludedUl(ctx.CapturedName, card.Source))
                _window.ViewModel.ToggleUpdateAllExclusionUl(ctx.CapturedName, card.Source);
            if (_window.ViewModel.IsUpdateAllExcludedDc(ctx.CapturedName, card.Source))
                _window.ViewModel.ToggleUpdateAllExclusionDc(ctx.CapturedName, card.Source);
            if (_window.ViewModel.IsUpdateAllExcludedOs(ctx.CapturedName, card.Source))
                _window.ViewModel.ToggleUpdateAllExclusionOs(ctx.CapturedName, card.Source);

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

            // Reset DXVK — uninstall if active, clear variant override and Lilium preset
            {
                var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(ctx.CapturedName, StringComparison.OrdinalIgnoreCase)
                    && (string.IsNullOrEmpty(card.Source) || c.Source == card.Source));
                bool dxvkInstalled = targetCard != null && (targetCard.DxvkEnabled
                    || targetCard.DxvkStatus == GameStatus.Installed
                    || targetCard.DxvkStatus == GameStatus.UpdateAvailable);
                var dxvkResetName  = ctx.CapturedName;
                var dxvkResetStore = ctx.Card.Source;
                // The variant and Lilium preset describe the installed DXVK: clear them only once it's gone,
                // or Update would deploy another variant's DLLs over a kept (game running) install
                _ = ResetDxvkOverridesAsync();
                async Task ResetDxvkOverridesAsync()
                {
                    try
                    {
                        if (dxvkInstalled && targetCard != null)
                        {
                            await _window.ViewModel.HandleDxvkToggleAsync(targetCard, false, _window.Content.XamlRoot);
                            if (targetCard.DxvkStatus is GameStatus.Installed or GameStatus.UpdateAvailable)
                            {
                                CrashReporter.Log($"[DetailPanelBuilder.ResetOverrides] '{dxvkResetName}': DXVK not uninstalled — variant and Lilium preset kept");
                                return;
                            }
                        }
                        _window.ViewModel.SetDxvkVariantOverride(dxvkResetName, null, dxvkResetStore);
                        _window.ViewModel.SetLiliumPreset(dxvkResetName, 0, dxvkResetStore);
                    }
                    catch (Exception ex)
                    {
                        CrashReporter.Log($"[DetailPanelBuilder.ResetOverrides] '{dxvkResetName}': DXVK reset failed — {ex.Message}");
                    }
                }
            }

            // Reset DXVK update exclusion via the shared Update Inclusion system
            if (_window.ViewModel.IsUpdateAllExcludedDxvk(ctx.CapturedName, card.Source))
                _window.ViewModel.ToggleUpdateAllExclusionDxvk(ctx.CapturedName, card.Source);

            // Reset bitness override to Auto
            ctx.BitnessCombo.SelectedItem = "Auto";
            _window.ViewModel.SetBitnessOverride(ctx.CapturedName, null, ctx.Card.Source);

            // Reset API overrides
            ctx.ApiCombo.SelectedItem = "Auto";
            _window.ViewModel.SetApiOverride(ctx.CapturedName, null, ctx.Card.Source);

            // Reset ReShade channel override — if ReShade is installed and channel was overridden, reinstall with Stable
            var previousChannel = _window.ViewModel.GetReShadeChannelOverride(ctx.CapturedName, ctx.Card.Source);
            ctx.ChannelComboInitializing = true;
            ctx.ChannelCombo.SelectedItem = "Stable";
            ctx.ChannelComboInitializing = false;
            _window.ViewModel.SetReShadeChannelOverride(ctx.CapturedName, null, ctx.Card.Source);

            // If ReShade was installed with a non-Stable channel, reinstall with Stable
            if (ctx.Card.RsRecord != null && !string.IsNullOrEmpty(previousChannel))
            {
                _ = _window.ViewModel.InstallReShadeAsync(ctx.Card);
            }

            // Reset custom ReShade DLL selection (composite key + legacy fallback)
            var customKey = GameKey.FromCard(ctx.CapturedName, ctx.Card.Source).ToKey();
            _gameNameService.CustomReShadeSelection.Remove(customKey);
            _gameNameService.CustomReShadeSelection.Remove(ctx.CapturedName); // legacy fallback

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
                    targetCard.Is32Bit = _window.ViewModel.ResolveIs32Bit(ctx.CapturedName, detectedMachine, targetCard.Source ?? "");

                    // Re-detect APIs from scanning (overrides are now cleared)
                    targetCard.DetectedApis = _window.ViewModel._DetectAllApisForCard(targetCard.InstallPath, ctx.CapturedName, targetCard.Source);
                    targetCard.IsDualApiGame = GraphicsApiDetector.IsDualApi(targetCard.DetectedApis);
                    targetCard.GraphicsApi = _window.ViewModel.DetectGraphicsApi(
                        targetCard.InstallPath, EngineType.Unknown, ctx.CapturedName, targetCard.Source);

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

            // Forget the game's display mode state (what it swaps and its values per mode; files are left alone)
            _window.ViewModel.ResetDisplayModeState(card);
            _window.RefreshDisplayModeUi(card);

            // Turn the RHI overlay back on (Spotlight's Game settings panel)
            if (_window.ViewModel.IsOverlayDisabled(ctx.CapturedName, card.Source))
            {
                _window.ViewModel.SetOverlayDisabled(ctx.CapturedName, false, card.Source);
                _window.RefreshOverlayPassthrough();
            }

            CrashReporter.Log($"[DetailPanelBuilder.BuildOverridesPanel] Overrides reset for: {ctx.CapturedName}");

            // Only reselect if the game name actually changed
            if (nameChanged)
                _window.RequestReselect(ctx.CapturedName);
        };
        ctx.ResetAction = resetAction;
        RegisterResetAction(ctx.Card, resetAction); // the game settings ⚙'s Reset uses it too (GameShared)
        resetOverridesBtn.Click += (s, ev) => resetAction();
        // The only Reset Overrides button (Management's was removed in v2.9.6: it duplicated this one)
        _window.OverridesPanel.Children.Add(resetOverridesBtn);
    }
}