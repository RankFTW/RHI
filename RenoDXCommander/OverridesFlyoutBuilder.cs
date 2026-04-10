using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

/// <summary>
/// Helper class responsible for building and showing the per-game overrides flyout.
/// Extracted from MainWindow code-behind to reduce file size.
/// </summary>
public class OverridesFlyoutBuilder
{
    private readonly MainWindow _window;
    private readonly ICrashReporter _crashReporter;

    public OverridesFlyoutBuilder(MainWindow window, ICrashReporter crashReporter)
    {
        _window = window;
        _crashReporter = crashReporter;
    }

    /// <summary>
    /// Builds and shows the overrides flyout anchored to the given element.
    /// </summary>
    public void OpenOverridesFlyout(GameCardViewModel card, FrameworkElement anchor)
    {
        var ViewModel = _window.ViewModel;
        var gameName = card.GameName;
        bool isLumaMode = ViewModel.IsLumaEnabled(gameName);

        var panel = new StackPanel { Spacing = 8, Width = 560 };

        // ── Title ──
        panel.Children.Add(new TextBlock
        {
            Text = "Overrides",
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
        });

        // ── Game name + Wiki name ──
        var gameNameBox = new TextBox
        {
            Header = "Game name (editable)",
            Text = gameName,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x1A, 0x20, 0x30)),
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xE2, 0xE8, 0xFF)),
            BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x28, 0x32, 0x40)),
            Padding = new Thickness(8, 4, 8, 4),
        };
        var wikiNameBox = new TextBox
        {
            Header = "Wiki mod name",
            PlaceholderText = "Exact wiki name",
            Text = ViewModel.GetNameMapping(gameName) ?? "",
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x1A, 0x20, 0x30)),
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xE2, 0xE8, 0xFF)),
            BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x28, 0x32, 0x40)),
            Padding = new Thickness(8, 4, 8, 4),
        };
        var originalStoreName = ViewModel.GetOriginalStoreName(gameName);

        // Mutable captured name so rename handler can update it for subsequent handlers
        var capturedName = gameName;

        var nameResetBtn = new Button
        {
            Content = "↩ Reset",
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Bottom,
            Padding = new Thickness(8, 4, 8, 4),
            Background = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x1A, 0x20, 0x30)),
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x6B, 0x7A, 0x8E)),
            BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x28, 0x32, 0x40)),
        };
        ToolTipService.SetToolTip(nameResetBtn,
            "Reset game name back to auto-detected and clear wiki name mapping.");
        nameResetBtn.Click += (s, ev) =>
        {
            var resetName = (originalStoreName ?? gameName).Trim();
            gameNameBox.Text = resetName;
            wikiNameBox.Text = "";

            // Persist wiki mapping removal
            if (ViewModel.GetNameMapping(capturedName) != null)
                ViewModel.RemoveNameMapping(capturedName);

            // Persist rename back to original if name was changed
            if (!resetName.Equals(capturedName, StringComparison.OrdinalIgnoreCase))
            {
                ViewModel.RenameGame(capturedName, resetName);
                capturedName = resetName;
                _window.RequestReselect(resetName);
                card.NotifyAll();
            }
        };

        var nameGrid = new Grid { ColumnSpacing = 8 };
        nameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        nameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        nameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(gameNameBox, 0);
        Grid.SetColumn(wikiNameBox, 1);
        Grid.SetColumn(nameResetBtn, 2);
        nameGrid.Children.Add(gameNameBox);
        nameGrid.Children.Add(wikiNameBox);
        nameGrid.Children.Add(nameResetBtn);
        panel.Children.Add(nameGrid);

        // ── Wiki exclusion toggle ──
        var wikiExcludeToggle = new ToggleSwitch
        {
            IsOn = ViewModel.IsWikiExcluded(gameName),
            OnContent = "Excluded from wiki lookups",
            OffContent = "Included in wiki lookups",
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            FontSize = 11,
        };
        ToolTipService.SetToolTip(wikiExcludeToggle,
            "When enabled, this game will not be looked up on the RenoDX wiki. " +
            "Useful for games that share a name with an unrelated wiki entry.");
        wikiExcludeToggle.Toggled += (s, ev) =>
        {
            if (wikiExcludeToggle.IsOn != ViewModel.IsWikiExcluded(capturedName))
                ViewModel.ToggleWikiExclusion(capturedName);
        };
        panel.Children.Add(wikiExcludeToggle);

        panel.Children.Add(UIFactory.MakeSeparator());

        // ── Auto-save: Game name on Enter ──
        gameNameBox.KeyDown += (s, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            var det = gameNameBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(det)) return;
            if (det.Equals(capturedName, StringComparison.OrdinalIgnoreCase)) return;
            ViewModel.RenameGame(capturedName, det);
            _window.RequestReselect(det);
            card.NotifyAll();
            capturedName = det;
        };

        // ── Auto-save: Wiki name on Enter ──
        wikiNameBox.KeyDown += (s, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            var key = wikiNameBox.Text?.Trim();
            if (!string.IsNullOrEmpty(key))
            {
                var existing = ViewModel.GetNameMapping(capturedName);
                if (!key.Equals(existing, StringComparison.OrdinalIgnoreCase))
                    ViewModel.AddNameMapping(capturedName, key);
            }
            else
            {
                if (ViewModel.GetNameMapping(capturedName) != null)
                    ViewModel.RemoveNameMapping(capturedName);
            }
        };

        // ── Shader Mode ──
        string currentShaderMode = ViewModel.GetPerGameShaderMode(gameName);
        bool isGlobalShaders = currentShaderMode != "Select";
        var shaderToggle = new ToggleSwitch
        {
            Header = "Global Shaders",
            IsOn = isGlobalShaders,
            OnContent = "Using global shader selection",
            OffContent = "Using per-game shader selection",
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            FontSize = 12,
        };
        ToolTipService.SetToolTip(shaderToggle,
            "On = use the global shader selection from Settings. Off = pick specific shader packs for this game.");

        var selectShadersBtn = new Button
        {
            Content = "Select Shaders",
            FontSize = 12,
            Padding = new Thickness(12, 7, 12, 7),
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = !isGlobalShaders,
            Background = UIFactory.Brush(isGlobalShaders ? ResourceKeys.SurfaceOverlayBrush : ResourceKeys.AccentBlueBgBrush),
            Foreground = UIFactory.Brush(isGlobalShaders ? ResourceKeys.TextDisabledBrush : ResourceKeys.AccentBlueBrush),
            BorderBrush = UIFactory.Brush(isGlobalShaders ? ResourceKeys.BorderSubtleBrush : ResourceKeys.AccentBlueBorderBrush),
            BorderThickness = new Thickness(1),
        };
        ToolTipService.SetToolTip(selectShadersBtn, "Choose which shader packs to use for this game");

        shaderToggle.Toggled += (s, ev) =>
        {
            bool global = shaderToggle.IsOn;
            selectShadersBtn.IsEnabled = !global;
            selectShadersBtn.Background = UIFactory.Brush(global ? ResourceKeys.SurfaceOverlayBrush : ResourceKeys.AccentBlueBgBrush);
            selectShadersBtn.Foreground = UIFactory.Brush(global ? ResourceKeys.TextDisabledBrush : ResourceKeys.AccentBlueBrush);
            selectShadersBtn.BorderBrush = UIFactory.Brush(global ? ResourceKeys.BorderSubtleBrush : ResourceKeys.AccentBlueBorderBrush);

            // Auto-save: persist shader mode immediately
            var newMode = global ? "Global" : "Select";
            if (newMode != ViewModel.GetPerGameShaderMode(capturedName))
            {
                ViewModel.SetPerGameShaderMode(capturedName, newMode);
                if (newMode == "Global")
                    ViewModel.GameNameServiceInstance.PerGameShaderSelection.Remove(capturedName);
                ViewModel.DeployShadersForCard(capturedName);
            }
        };

        // ── Per-game Custom Shaders toggle ──
        bool isPerGameCustom = currentShaderMode == "Custom";
        // Default to ON when global UseCustomShaders is enabled and no per-game override exists
        bool customDefault = isPerGameCustom ||
            (ViewModel.Settings.UseCustomShaders && currentShaderMode == "Global"
             && !ViewModel.GameNameServiceInstance.PerGameShaderMode.ContainsKey(gameName));
        var customShadersToggle = new ToggleSwitch
        {
            Header = "Use Custom Shaders",
            IsOn = customDefault,
            OnContent = "Using custom shader directories",
            OffContent = "Using shader packs",
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            FontSize = 12,
        };
        ToolTipService.SetToolTip(customShadersToggle,
            "On = use shaders from the custom shader directories. Off = use shader packs (global or per-game).");

        customShadersToggle.Toggled += (s, ev) =>
        {
            bool customOn = customShadersToggle.IsOn;
            if (customOn)
            {
                ViewModel.SetPerGameShaderMode(capturedName, "Custom");
            }
            else
            {
                ViewModel.SetPerGameShaderMode(capturedName, "Global");
            }
            ViewModel.DeployShadersForCard(capturedName);
        };

        selectShadersBtn.Click += async (s, ev) =>
        {
            List<string>? current = ViewModel.GameNameServiceInstance.PerGameShaderSelection.TryGetValue(gameName, out var existing)
                ? existing
                : ViewModel.Settings.SelectedShaderPacks;
            var result = await ViewModel.ShowPerGameShaderSelectionPicker?.Invoke(gameName, current)!;
            if (result != null)
            {
                ViewModel.GameNameServiceInstance.PerGameShaderSelection[gameName] = result;
                ViewModel.DeployShadersForCard(capturedName);
            }
        };

        // ── DLL naming overrides ──
        bool isDllOverride = ViewModel.HasDllOverride(gameName);
        var existingCfg = ViewModel.GetDllOverride(gameName);
        bool is32Bit = card.Is32Bit;

        var dllOverrideToggle = new ToggleSwitch
        {
            Header = "DLL naming overrides",
            IsOn = isDllOverride,
            IsEnabled = !isLumaMode,
            OnContent = "Custom filenames enabled",
            OffContent = "Override DLL filenames",
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            FontSize = 11,
        };
        ToolTipService.SetToolTip(dllOverrideToggle,
            "Override the filenames ReShade is installed as. When enabled, existing RS files are renamed to the custom filenames.");
        var existingRsName = existingCfg?.ReShadeFileName ?? "";

        var rsNameBox = new ComboBox
        {
            IsEditable = true,
            PlaceholderText = "Select ReShade DLL name",
            Header = (object?)null,
            FontSize = 11,
            IsEnabled = isDllOverride,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = DllOverrideConstants.CommonDllNames,
        };
        if (!string.IsNullOrEmpty(existingRsName))
        {
            if (DllOverrideConstants.CommonDllNames.Contains(existingRsName, StringComparer.OrdinalIgnoreCase))
                rsNameBox.SelectedItem = DllOverrideConstants.CommonDllNames.First(n => n.Equals(existingRsName, StringComparison.OrdinalIgnoreCase));
            else
            {
                var capturedRs = existingRsName;
                rsNameBox.Loaded += (s, e) => rsNameBox.Text = capturedRs;
            }
        }

        // ── DC DLL naming override ──
        var existingDcName = existingCfg?.DcFileName ?? "";
        bool isDcDllOverrideOn = isDllOverride && !string.IsNullOrEmpty(existingDcName);



        var dcNameBox = new ComboBox
        {
            IsEditable = true,
            PlaceholderText = "Select DC DLL name",
            FontSize = 11,
            IsEnabled = isDcDllOverrideOn,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = DetailPanelBuilder.DcDllOverrideNames,
        };
        if (!string.IsNullOrEmpty(existingDcName))
        {
            if (DetailPanelBuilder.DcDllOverrideNames.Contains(existingDcName, StringComparer.OrdinalIgnoreCase))
                dcNameBox.SelectedItem = DetailPanelBuilder.DcDllOverrideNames.First(n => n.Equals(existingDcName, StringComparison.OrdinalIgnoreCase));
            else
            {
                var capturedDc = existingDcName;
                dcNameBox.Loaded += (s, e) => dcNameBox.Text = capturedDc;
            }
        }

        // Track previous DC selection for revert on foreign DLL conflict cancel
        string? _previousDcSelection = dcNameBox.SelectedItem as string;

        // ── Cross-exclusion: filter out the other component's current name ──
        bool _updatingDropdowns = false;

        void UpdateDcDropdownItems()
        {
            if (_updatingDropdowns) return;
            _updatingDropdowns = true;
            try
            {
                var rsCurrentName = dllOverrideToggle.IsOn
                    ? (rsNameBox.SelectedItem as string ?? rsNameBox.Text ?? "").Trim()
                    : Services.AuxInstallService.RsNormalName;
                var filtered = string.IsNullOrEmpty(rsCurrentName)
                    ? DetailPanelBuilder.DcDllOverrideNames
                    : DetailPanelBuilder.DcDllOverrideNames.Where(n => !n.Equals(rsCurrentName, StringComparison.OrdinalIgnoreCase)).ToArray();
                var currentDc = dcNameBox.SelectedItem as string;
                dcNameBox.ItemsSource = filtered;
                if (currentDc != null && filtered.Contains(currentDc, StringComparer.OrdinalIgnoreCase))
                    dcNameBox.SelectedItem = filtered.First(n => n.Equals(currentDc, StringComparison.OrdinalIgnoreCase));
            }
            finally { _updatingDropdowns = false; }
        }

        void UpdateRsDropdownItems()
        {
            if (_updatingDropdowns) return;
            _updatingDropdowns = true;
            try
            {
                var dcCurrentName = dllOverrideToggle.IsOn
                    ? (dcNameBox.SelectedItem as string ?? "").Trim()
                    : "";
                var filtered = string.IsNullOrEmpty(dcCurrentName)
                    ? DllOverrideConstants.CommonDllNames
                    : DllOverrideConstants.CommonDllNames.Where(n => !n.Equals(dcCurrentName, StringComparison.OrdinalIgnoreCase)).ToArray();
                var currentRs = rsNameBox.SelectedItem as string ?? rsNameBox.Text;
                rsNameBox.ItemsSource = filtered;
                if (!string.IsNullOrEmpty(currentRs) && filtered.Contains(currentRs, StringComparer.OrdinalIgnoreCase))
                    rsNameBox.SelectedItem = filtered.First(n => n.Equals(currentRs, StringComparison.OrdinalIgnoreCase));
            }
            finally { _updatingDropdowns = false; }
        }

        // Initial filter
        UpdateDcDropdownItems();
        UpdateRsDropdownItems();

        dllOverrideToggle.Toggled += (s, ev) =>
        {
            rsNameBox.IsEnabled = dllOverrideToggle.IsOn;
            dcNameBox.IsEnabled = dllOverrideToggle.IsOn;

            var targetCard = ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard == null) return;

            if (dllOverrideToggle.IsOn)
            {
                // Turning unified override ON
                var existingCfgNow = ViewModel.GetDllOverride(capturedName);

                string rsName;
                string dcName;

                if (existingCfgNow != null
                    && (!string.IsNullOrEmpty(existingCfgNow.ReShadeFileName) || !string.IsNullOrEmpty(existingCfgNow.DcFileName)))
                {
                    // Prior config exists — restore saved filenames
                    rsName = existingCfgNow.ReShadeFileName ?? "";
                    dcName = existingCfgNow.DcFileName ?? "";

                    // Restore RS dropdown
                    if (!string.IsNullOrEmpty(rsName))
                    {
                        if (DllOverrideConstants.CommonDllNames.Contains(rsName, StringComparer.OrdinalIgnoreCase))
                        {
                            rsNameBox.SelectedItem = DllOverrideConstants.CommonDllNames
                                .First(n => n.Equals(rsName, StringComparison.OrdinalIgnoreCase));
                        }
                        else
                        {
                            var extended = DllOverrideConstants.CommonDllNames.Append(rsName).ToArray();
                            rsNameBox.ItemsSource = extended;
                            rsNameBox.SelectedItem = rsName;
                        }
                    }

                    // Restore DC dropdown
                    if (!string.IsNullOrEmpty(dcName))
                    {
                        if (DetailPanelBuilder.DcDllOverrideNames.Contains(dcName, StringComparer.OrdinalIgnoreCase))
                        {
                            dcNameBox.SelectedItem = DetailPanelBuilder.DcDllOverrideNames
                                .First(n => n.Equals(dcName, StringComparison.OrdinalIgnoreCase));
                        }
                        else
                        {
                            var extendedDc = DetailPanelBuilder.DcDllOverrideNames.Append(dcName).ToArray();
                            dcNameBox.ItemsSource = extendedDc;
                            dcNameBox.SelectedItem = dcName;
                        }
                    }
                }
                else
                {
                    // No prior config — auto-select safe defaults
                    rsName = targetCard.Is32Bit
                        ? Services.AuxInstallService.RsStaged32
                        : Services.AuxInstallService.RsStaged64;

                    if (DllOverrideConstants.CommonDllNames.Contains(rsName, StringComparer.OrdinalIgnoreCase))
                    {
                        rsNameBox.SelectedItem = DllOverrideConstants.CommonDllNames
                            .First(n => n.Equals(rsName, StringComparison.OrdinalIgnoreCase));
                    }
                    else
                    {
                        var extended = DllOverrideConstants.CommonDllNames.Append(rsName).ToArray();
                        rsNameBox.ItemsSource = extended;
                        rsNameBox.SelectedItem = rsName;
                    }

                    dcName = targetCard.Is32Bit
                        ? "zzz_display_commander_lite.addon32"
                        : "zzz_display_commander_lite.addon64";

                    if (DetailPanelBuilder.DcDllOverrideNames.Contains(dcName, StringComparer.OrdinalIgnoreCase))
                    {
                        dcNameBox.SelectedItem = DetailPanelBuilder.DcDllOverrideNames
                            .First(n => n.Equals(dcName, StringComparison.OrdinalIgnoreCase));
                    }
                    else
                    {
                        var extendedDc = DetailPanelBuilder.DcDllOverrideNames.Append(dcName).ToArray();
                        dcNameBox.ItemsSource = extendedDc;
                        dcNameBox.SelectedItem = dcName;
                    }
                }

                ViewModel.EnableDllOverride(targetCard, rsName, dcName);
            }
            else
            {
                // Turning unified override OFF — delegate to service for both RS and DC revert
                var result = ViewModel.DisableDllOverride(targetCard);

                // Disable and clear both dropdowns
                rsNameBox.SelectedIndex = -1;
                if (rsNameBox.IsEditable) rsNameBox.Text = "";
                dcNameBox.SelectedIndex = -1;
                if (dcNameBox.IsEditable) dcNameBox.Text = "";

                // Set tooltips for partial revert failures
                if (!result.RsReverted)
                {
                    ToolTipService.SetToolTip(dllOverrideToggle,
                        "Could not revert ReShade to dxgi.dll — the filename is occupied by another file. ReShade was renamed to a fallback name instead.");
                }
                else if (!result.DcReverted)
                {
                    ToolTipService.SetToolTip(dllOverrideToggle,
                        "Could not revert Display Commander to its default name — the filename is occupied by another file. DC was kept under its current name.");
                }
                else
                {
                    // Both reverted successfully — reset tooltip to default
                    ToolTipService.SetToolTip(dllOverrideToggle,
                        "Override the filenames ReShade is installed as. When enabled, existing RS files are renamed to the custom filenames.");
                }
            }
        };



        // ── Auto-save: DC name box on dropdown selection (with foreign DLL check) ──
        dcNameBox.SelectionChanged += async (s, e) =>
        {
            if (!dllOverrideToggle.IsOn) return;
            var targetCard = ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard == null) return;
            var dcName = dcNameBox.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(dcName)) return;

            // Check for foreign DLL conflict before proceeding
            bool allowed = await ViewModel.DllOverrideServiceInstance
                .CheckDcForeignDllConflictAsync(targetCard, dcName);
            if (!allowed)
            {
                // Revert dropdown to previous selection
                if (_previousDcSelection != null)
                    dcNameBox.SelectedItem = DetailPanelBuilder.DcDllOverrideNames.FirstOrDefault(n =>
                        n.Equals(_previousDcSelection, StringComparison.OrdinalIgnoreCase));
                else
                    dcNameBox.SelectedIndex = -1;
                return;
            }

            _previousDcSelection = dcName;
            var rsText = rsNameBox.SelectedItem as string ?? rsNameBox.Text;
            var rsName = !string.IsNullOrWhiteSpace(rsText) ? rsText.Trim() : "";
            ViewModel.UpdateDllOverrideNames(targetCard, rsName, dcName);
            UpdateRsDropdownItems();
        };

        // ── Auto-save: DC name box on Enter (manual typed name) ──
        dcNameBox.KeyDown += (s, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            if (!dllOverrideToggle.IsOn) return;
            var targetCard = ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard == null) return;
            var dcName = (dcNameBox.SelectedItem as string ?? dcNameBox.Text)?.Trim();
            if (string.IsNullOrWhiteSpace(dcName)) return;
            var rsText = rsNameBox.SelectedItem as string ?? rsNameBox.Text;
            var rsName = !string.IsNullOrWhiteSpace(rsText) ? rsText.Trim() : "";
            ViewModel.UpdateDllOverrideNames(targetCard, rsName, dcName);
            UpdateRsDropdownItems();
        };

        // ── Inline row: [Global Shaders + Select btn | divider | DLL override + RS name] ──
        var modeGrid = new Grid { ColumnSpacing = 0 };
        modeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        modeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        modeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Left: shader toggle + custom shaders toggle + select button
        var shaderColumn = new StackPanel { Spacing = 8 };
        shaderColumn.Children.Add(shaderToggle);
        shaderColumn.Children.Add(customShadersToggle);
        shaderColumn.Children.Add(selectShadersBtn);
        Grid.SetColumn(shaderColumn, 0);

        // Center: vertical divider
        var modeDivider = new Border
        {
            Width = 1,
            Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(12, 0, 12, 0),
        };
        Grid.SetColumn(modeDivider, 1);

        // Right: DLL override toggle + RS name combo + DC toggle + DC name combo
        var dllColumn = new StackPanel { Spacing = 4 };
        dllColumn.Children.Add(dllOverrideToggle);
        dllColumn.Children.Add(rsNameBox);
        dllColumn.Children.Add(dcNameBox);
        Grid.SetColumn(dllColumn, 2);

        modeGrid.Children.Add(shaderColumn);
        modeGrid.Children.Add(modeDivider);
        modeGrid.Children.Add(dllColumn);
        panel.Children.Add(modeGrid);
        panel.Children.Add(UIFactory.MakeSeparator());

        // ── Per-game Addon mode ──
        string currentAddonMode = ViewModel.GetPerGameAddonMode(gameName);
        bool isGlobalAddons = currentAddonMode != "Select";
        var addonToggle = new ToggleSwitch
        {
            Header = "Global Addons",
            IsOn = isGlobalAddons,
            OnContent = "Using global addon set",
            OffContent = "Using per-game addon selection",
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            FontSize = 12,
        };
        ToolTipService.SetToolTip(addonToggle,
            "On = use the global addon set for this game. Off = pick specific addons for this game.");

        var selectAddonsBtn = new Button
        {
            Content = "Select Addons",
            FontSize = 12,
            Padding = new Thickness(12, 7, 12, 7),
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = !isGlobalAddons,
            Background = UIFactory.Brush(isGlobalAddons ? ResourceKeys.SurfaceOverlayBrush : ResourceKeys.AccentBlueBgBrush),
            Foreground = UIFactory.Brush(isGlobalAddons ? ResourceKeys.TextDisabledBrush : ResourceKeys.AccentBlueBrush),
            BorderBrush = UIFactory.Brush(isGlobalAddons ? ResourceKeys.BorderSubtleBrush : ResourceKeys.AccentBlueBorderBrush),
            BorderThickness = new Thickness(1),
        };
        ToolTipService.SetToolTip(selectAddonsBtn, "Choose which addons to deploy for this game");

        addonToggle.Toggled += (s, ev) =>
        {
            bool global = addonToggle.IsOn;
            selectAddonsBtn.IsEnabled = !global;
            selectAddonsBtn.Background = UIFactory.Brush(global ? ResourceKeys.SurfaceOverlayBrush : ResourceKeys.AccentBlueBgBrush);
            selectAddonsBtn.Foreground = UIFactory.Brush(global ? ResourceKeys.TextDisabledBrush : ResourceKeys.AccentBlueBrush);
            selectAddonsBtn.BorderBrush = UIFactory.Brush(global ? ResourceKeys.BorderSubtleBrush : ResourceKeys.AccentBlueBorderBrush);

            var newMode = global ? "Global" : "Select";
            if (newMode != ViewModel.GetPerGameAddonMode(capturedName))
            {
                ViewModel.SetPerGameAddonMode(capturedName, newMode);
                ViewModel.DeployAddonsForCard(capturedName);
            }
        };

        selectAddonsBtn.Click += async (s, ev) =>
        {
            List<string>? current = ViewModel.GameNameServiceInstance.PerGameAddonSelection.TryGetValue(gameName, out var existingAddons)
                ? existingAddons
                : null;

            IAddonPackService? addonPackService = ViewModel.AddonPackServiceInstance;

            if (addonPackService == null)
            {
                var infoDlg = new ContentDialog
                {
                    Title = "Select Addons",
                    Content = new TextBlock
                    {
                        Text = "Addon service is not yet available.",
                        FontSize = 13,
                        Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
                    },
                    CloseButtonText = "OK",
                    XamlRoot = _window.Content.XamlRoot,
                    Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
                    RequestedTheme = ElementTheme.Dark,
                };
                await infoDlg.ShowAsync();
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
                ViewModel.DeployAddonsForCard(capturedName);
            }
        };

        // If normal ReShade is active, disable addon controls on initial build
        if (card.UseNormalReShade)
        {
            addonToggle.IsOn = false;
            addonToggle.IsEnabled = false;
            selectAddonsBtn.IsEnabled = false;
            selectAddonsBtn.Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush);
            selectAddonsBtn.Foreground = UIFactory.Brush(ResourceKeys.TextDisabledBrush);
            selectAddonsBtn.BorderBrush = UIFactory.Brush(ResourceKeys.BorderSubtleBrush);
        }

        var addonColumn = new StackPanel { Spacing = 8 };
        addonColumn.Children.Add(addonToggle);
        addonColumn.Children.Add(selectAddonsBtn);
        panel.Children.Add(addonColumn);
        panel.Children.Add(UIFactory.MakeSeparator());

        // ── Auto-save: RS name box on Enter ──
        rsNameBox.KeyDown += (s, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            if (!dllOverrideToggle.IsOn) return;
            var targetCard = ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard == null) return;
            var rsText = rsNameBox.SelectedItem as string ?? rsNameBox.Text;
            var rsName = !string.IsNullOrWhiteSpace(rsText) ? rsText.Trim() : "";
            if (string.IsNullOrEmpty(rsName)) return;
            var dcName = dllOverrideToggle.IsOn ? (dcNameBox.SelectedItem as string ?? dcNameBox.Text ?? "").Trim() : "";

            if (ViewModel.HasDllOverride(capturedName))
                ViewModel.UpdateDllOverrideNames(targetCard, rsName, dcName);
            else
                ViewModel.EnableDllOverride(targetCard, rsName, dcName);
        };
        // ── Auto-save: RS name box on dropdown selection ──
        rsNameBox.SelectionChanged += (s, e) =>
        {
            if (!dllOverrideToggle.IsOn) return;
            var targetCard = ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard == null) return;
            var rsName = rsNameBox.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(rsName)) return;
            var dcName = dllOverrideToggle.IsOn ? (dcNameBox.SelectedItem as string ?? dcNameBox.Text ?? "") : "";

            if (ViewModel.HasDllOverride(capturedName))
                ViewModel.UpdateDllOverrideNames(targetCard, rsName, dcName);
            else
                ViewModel.EnableDllOverride(targetCard, rsName, dcName);

            UpdateDcDropdownItems();
        };

        // ── Bitness & API Override ──
        var bitnessItems = new[] { "Auto", "32-bit", "64-bit" };
        var currentBitnessOverride = ViewModel.GetBitnessOverride(gameName);
        var defaultBitnessSelection = currentBitnessOverride switch
        {
            "32" => "32-bit",
            "64" => "64-bit",
            _ => "Auto",
        };

        var bitnessCombo = new ComboBox
        {
            ItemsSource = bitnessItems,
            SelectedItem = defaultBitnessSelection,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        ToolTipService.SetToolTip(bitnessCombo,
            "Override the auto-detected bitness for this game.");

        bitnessCombo.SelectionChanged += (s, e) =>
        {
            var selected = bitnessCombo.SelectedItem as string;
            string? overrideValue = selected switch
            {
                "32-bit" => "32",
                "64-bit" => "64",
                _ => null,
            };
            ViewModel.SetBitnessOverride(capturedName, overrideValue);
            var targetCard = ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard != null)
            {
                if (overrideValue == "32") targetCard.Is32Bit = true;
                else if (overrideValue == "64") targetCard.Is32Bit = false;
                else
                {
                    var detectedMachine = ViewModel.PeHeaderServiceInstance.DetectGameArchitecture(targetCard.InstallPath);
                    targetCard.Is32Bit = ViewModel.ResolveIs32Bit(capturedName, detectedMachine);
                }
                targetCard.NotifyAll();
            }
        };

        var apiDropdownItems = new[] { "Auto", "DirectX8", "DirectX9", "DirectX10", "DX11/DX12", "Vulkan", "OpenGL" };
        var existingApiOverride = ViewModel.GetApiOverride(gameName);
        string defaultApiSelection = "Auto";
        if (existingApiOverride != null && existingApiOverride.Count > 0)
        {
            if (existingApiOverride.Contains("DirectX11", StringComparer.OrdinalIgnoreCase)
                || existingApiOverride.Contains("DirectX12", StringComparer.OrdinalIgnoreCase))
                defaultApiSelection = "DX11/DX12";
            else if (existingApiOverride.Contains("Vulkan", StringComparer.OrdinalIgnoreCase))
                defaultApiSelection = "Vulkan";
            else if (existingApiOverride.Contains("OpenGL", StringComparer.OrdinalIgnoreCase))
                defaultApiSelection = "OpenGL";
            else if (existingApiOverride.Contains("DirectX10", StringComparer.OrdinalIgnoreCase))
                defaultApiSelection = "DirectX10";
            else if (existingApiOverride.Contains("DirectX9", StringComparer.OrdinalIgnoreCase))
                defaultApiSelection = "DirectX9";
            else if (existingApiOverride.Contains("DirectX8", StringComparer.OrdinalIgnoreCase))
                defaultApiSelection = "DirectX8";
        }

        var apiCombo = new ComboBox
        {
            ItemsSource = apiDropdownItems,
            SelectedItem = defaultApiSelection,
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        apiCombo.SelectionChanged += (s, ev) =>
        {
            var selected = apiCombo.SelectedItem as string;
            List<string>? apiEnumNames = selected switch
            {
                "DirectX8" => new() { "DirectX8" },
                "DirectX9" => new() { "DirectX9" },
                "DirectX10" => new() { "DirectX10" },
                "DX11/DX12" => new() { "DirectX11", "DirectX12" },
                "Vulkan" => new() { "Vulkan" },
                "OpenGL" => new() { "OpenGL" },
                _ => null,
            };
            ViewModel.SetApiOverride(capturedName, apiEnumNames);
            var targetCard = ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard != null)
            {
                if (apiEnumNames != null)
                {
                    var newApis = new HashSet<GraphicsApiType>();
                    foreach (var name in apiEnumNames)
                        if (Enum.TryParse<GraphicsApiType>(name, out var apiType)) newApis.Add(apiType);
                    targetCard.DetectedApis = newApis;
                }
                else
                {
                    targetCard.DetectedApis = ViewModel._DetectAllApisForCard(targetCard.InstallPath, capturedName);
                }
                targetCard.IsDualApiGame = GraphicsApiDetector.IsDualApi(targetCard.DetectedApis);
                targetCard.GraphicsApi = ViewModel.DetectGraphicsApi(targetCard.InstallPath, EngineType.Unknown, capturedName);
                targetCard.NotifyAll();
            }
        };

        var bitnessApiRow = new Grid { ColumnSpacing = 8 };
        bitnessApiRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bitnessApiRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var bitnessPanel = new StackPanel { Spacing = 4 };
        bitnessPanel.Children.Add(new TextBlock { Text = "Bitness", FontSize = 11, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush) });
        bitnessPanel.Children.Add(bitnessCombo);
        Grid.SetColumn(bitnessPanel, 0);

        var apiPanel = new StackPanel { Spacing = 4 };
        apiPanel.Children.Add(new TextBlock { Text = "Graphics API", FontSize = 11, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush) });
        apiPanel.Children.Add(apiCombo);
        Grid.SetColumn(apiPanel, 1);

        bitnessApiRow.Children.Add(bitnessPanel);
        bitnessApiRow.Children.Add(apiPanel);
        panel.Children.Add(bitnessApiRow);
        panel.Children.Add(UIFactory.MakeSeparator());

        // ── Global update inclusion + Wiki exclusion (inline row) ──
        var rsToggle = new ToggleSwitch
        {
            Header = "ReShade",
            IsOn = !ViewModel.IsUpdateAllExcludedReShade(gameName),
            OnContent = "",
            OffContent = "",
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            FontSize = 11,
            MinWidth = 0,
        };
        var rdxToggle = new ToggleSwitch
        {
            Header = "RenoDX",
            IsOn = !ViewModel.IsUpdateAllExcludedRenoDx(gameName),
            OnContent = "",
            OffContent = "",
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            FontSize = 11,
            MinWidth = 0,
        };
        var ulToggle = new ToggleSwitch
        {
            Header = "ReLimiter",
            IsOn = !ViewModel.IsUpdateAllExcludedUl(gameName),
            OnContent = "",
            OffContent = "",
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            FontSize = 11,
            MinWidth = 0,
        };
        var dcToggle = new ToggleSwitch
        {
            Header = "DC",
            IsOn = !card.ExcludeFromUpdateAllDc,
            OnContent = "",
            OffContent = "",
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            FontSize = 11,
            MinWidth = 0,
        };

        var rsBorder = new Border
        {
            Child = rsToggle,
            BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 6, 8, 6),
        };
        var rdxBorder = new Border
        {
            Child = rdxToggle,
            BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 6, 8, 6),
        };
        var ulBorder = new Border
        {
            Child = ulToggle,
            BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 6, 8, 6),
        };
        var dcBorder = new Border
        {
            Child = dcToggle,
            BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 6, 8, 6),
        };

        var toggleRow = new Grid
        {
            ColumnSpacing = 8,
            RowSpacing = 8,
        };
        toggleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toggleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toggleRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        toggleRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(rsBorder, 0);  Grid.SetColumn(rsBorder, 0);
        Grid.SetRow(rdxBorder, 0); Grid.SetColumn(rdxBorder, 1);
        Grid.SetRow(ulBorder, 1);  Grid.SetColumn(ulBorder, 0);
        Grid.SetRow(dcBorder, 1);  Grid.SetColumn(dcBorder, 1);
        toggleRow.Children.Add(rsBorder);
        toggleRow.Children.Add(rdxBorder);
        toggleRow.Children.Add(ulBorder);
        toggleRow.Children.Add(dcBorder);

        // ── Auto-save: Update inclusion toggles ──
        rsToggle.Toggled += (s, ev) =>
        {
            if (!rsToggle.IsOn != ViewModel.IsUpdateAllExcludedReShade(capturedName))
                ViewModel.ToggleUpdateAllExclusionReShade(capturedName);
        };
        rdxToggle.Toggled += (s, ev) =>
        {
            if (!rdxToggle.IsOn != ViewModel.IsUpdateAllExcludedRenoDx(capturedName))
                ViewModel.ToggleUpdateAllExclusionRenoDx(capturedName);
        };
        ulToggle.Toggled += (s, ev) =>
        {
            if (!ulToggle.IsOn != ViewModel.IsUpdateAllExcludedUl(capturedName))
                ViewModel.ToggleUpdateAllExclusionUl(capturedName);
        };
        dcToggle.Toggled += (s, ev) =>
        {
            if (!dcToggle.IsOn != ViewModel.IsUpdateAllExcludedDc(capturedName))
                ViewModel.ToggleUpdateAllExclusionDc(capturedName);
        };

        // Build inline row Grid: [Global update inclusion]
        var inlineRowGrid = new Grid();
        inlineRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Column 0: Global update inclusion
        var leftSection = new StackPanel { Spacing = 0 };
        leftSection.Children.Add(new TextBlock
        {
            Text = "Global update inclusion",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            Margin = new Thickness(0, 0, 0, 8),
        });
        leftSection.Children.Add(toggleRow);
        Grid.SetColumn(leftSection, 0);

        inlineRowGrid.Children.Add(leftSection);

        panel.Children.Add(inlineRowGrid);
        panel.Children.Add(UIFactory.MakeSeparator());

        // ── Manage row (Star | Auto divider | Star) ──
        var manageRowGrid = new Grid { ColumnSpacing = 0 };
        manageRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        manageRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        manageRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var manageLeftColumn = new StackPanel { Spacing = 6 };

        // ── Change install folder button ──
        var changeFolderBtn = new Button
        {
            Content = "Change install folder",
            FontSize = 12,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Tag = card,
        };
        changeFolderBtn.Click += (s, ev) => _window.BrowseFolder_Click(s, ev);
        manageLeftColumn.Children.Add(changeFolderBtn);

        // ── Reset folder / Remove game button ──
        var removeGameBtn = new Button
        {
            Content = "Reset folder / Remove game",
            FontSize = 12,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = UIFactory.Brush(ResourceKeys.AccentRedBgBrush),
            Foreground = UIFactory.Brush(ResourceKeys.AccentRedBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.AccentPurpleBorderBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Tag = card,
        };
        removeGameBtn.Click += (s, ev) => _window.RemoveManualGame_Click(s, ev);
        manageLeftColumn.Children.Add(removeGameBtn);

        // ── Reset Overrides button ──
        // Forward-declare normalReShadeToggle so the reset handler can reference it
        ToggleSwitch normalReShadeToggle = null!;

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
        resetOverridesBtn.Click += (s, ev) =>
        {
            // Reset all controls to defaults
            gameNameBox.Text = originalStoreName ?? gameName;
            wikiNameBox.Text = "";
            shaderToggle.IsOn = true;
            customShadersToggle.IsOn = false;
            addonToggle.IsOn = true;
            dllOverrideToggle.IsOn = false;
            rsToggle.IsOn = true;
            rdxToggle.IsOn = true;
            ulToggle.IsOn = true;
            dcToggle.IsOn = true;
            wikiExcludeToggle.IsOn = false;

            // Persist all reset values immediately
            var resetName = (originalStoreName ?? gameName).Trim();
            bool nameChanged = !resetName.Equals(capturedName, StringComparison.OrdinalIgnoreCase);
            if (nameChanged && !string.IsNullOrWhiteSpace(resetName))
            {
                ViewModel.RenameGame(capturedName, resetName);
                capturedName = resetName;
            }

            // Remove wiki mapping
            if (ViewModel.GetNameMapping(capturedName) != null)
                ViewModel.RemoveNameMapping(capturedName);

            // Shader mode → Global
            if (ViewModel.GetPerGameShaderMode(capturedName) != "Global")
            {
                ViewModel.SetPerGameShaderMode(capturedName, "Global");
                ViewModel.GameNameServiceInstance.PerGameShaderSelection.Remove(capturedName);
                ViewModel.DeployShadersForCard(capturedName);
            }

            // Addon mode → Global
            if (ViewModel.GetPerGameAddonMode(capturedName) != "Global")
            {
                ViewModel.SetPerGameAddonMode(capturedName, "Global");
                ViewModel.DeployAddonsForCard(capturedName);
            }

            // Disable DLL override
            if (ViewModel.HasDllOverride(capturedName))
            {
                var targetCard = ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                if (targetCard != null)
                    ViewModel.DisableDllOverride(targetCard);
            }

            // Include all in Update All
            if (ViewModel.IsUpdateAllExcludedReShade(capturedName))
                ViewModel.ToggleUpdateAllExclusionReShade(capturedName);
            if (ViewModel.IsUpdateAllExcludedRenoDx(capturedName))
                ViewModel.ToggleUpdateAllExclusionRenoDx(capturedName);
            if (ViewModel.IsUpdateAllExcludedUl(capturedName))
                ViewModel.ToggleUpdateAllExclusionUl(capturedName);
            if (ViewModel.IsUpdateAllExcludedDc(capturedName))
                ViewModel.ToggleUpdateAllExclusionDc(capturedName);

            // Disable wiki exclusion
            if (ViewModel.IsWikiExcluded(capturedName))
                ViewModel.ToggleWikiExclusion(capturedName);

            // Reset Normal ReShade toggle
            normalReShadeToggle.IsOn = false;
            {
                var targetCard = ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                if (targetCard != null && targetCard.UseNormalReShade)
                    _ = ViewModel.SetUseNormalReShade(targetCard, false);
            }

            // Reset bitness and API overrides
            bitnessCombo.SelectedItem = "Auto";
            ViewModel.SetBitnessOverride(capturedName, null);
            apiCombo.SelectedItem = "Auto";
            ViewModel.SetApiOverride(capturedName, null);

            _crashReporter.Log($"[OverridesFlyoutBuilder.OpenOverridesFlyout] Overrides reset for: {capturedName}");

            // Only reselect/NotifyAll/RebuildCardGrid if game name actually changed
            if (nameChanged)
            {
                _window.RequestReselect(capturedName);
                card.NotifyAll();
                if (ViewModel.IsGridLayout)
                    _window.RebuildCardGrid();
            }
        };
        manageLeftColumn.Children.Add(resetOverridesBtn);

        // ── Copy Report button ──
        var reportBtn = new Button
        {
            Content = "Copy Report",
            FontSize = 12,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
        };
        reportBtn.Click += async (s, ev) =>
        {
            var targetCard = ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard != null)
                await GameReportEncoder.ShowAndCopyAsync(_window.Content.XamlRoot, targetCard, ViewModel);
        };
        manageLeftColumn.Children.Add(reportBtn);

        Grid.SetColumn(manageLeftColumn, 0);
        manageRowGrid.Children.Add(manageLeftColumn);

        var manageDivider = new Border
        {
            Width = 1,
            Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(12, 0, 12, 0),
        };
        Grid.SetColumn(manageDivider, 1);
        manageRowGrid.Children.Add(manageDivider);

        // ── Right column — ReShade preset selector + Normal ReShade toggle ──
        var manageRightColumn = new StackPanel { Spacing = 6 };

        var presetBtn = new Button
        {
            Content = "Select ReShade Preset",
            FontSize = 12,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
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
                    c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                if (targetCard != null && !string.IsNullOrEmpty(targetCard.InstallPath))
                {
                    int count = PresetPopupHelper.DeployPresets(selected, targetCard.InstallPath);
                    _crashReporter.Log($"[OverridesFlyoutBuilder] Deployed {count} preset(s) to '{capturedName}'");

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

                        var shaderResult = await shaderDialog.ShowAsync();
                        if (shaderResult == ContentDialogResult.Primary)
                        {
                            var presetPaths = selected.Select(f => Path.Combine(PresetPopupHelper.PresetsDir, f)).ToList();
                            ViewModel.ApplyPresetShaders(capturedName, presetPaths);
                        }
                    }
                }
            }
        };
        manageRightColumn.Children.Add(presetBtn);

        // ── ReShade Without Addon Support toggle ──
        normalReShadeToggle = new ToggleSwitch
        {
            Header = "ReShade Without Addon Support",
            IsOn = card.UseNormalReShade,
            OnContent = "On",
            OffContent = "Off",
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            FontSize = 12,
            IsEnabled = !isLumaMode,
        };
        ToolTipService.SetToolTip(normalReShadeToggle,
            "When enabled, this game uses normal ReShade (without addon support). " +
            "All managed addons will be removed and addon install buttons will be disabled.");
        normalReShadeToggle.Toggled += async (s, ev) =>
        {
            var targetCard = ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard == null) return;
            if (normalReShadeToggle.IsOn != targetCard.UseNormalReShade)
            {
                await ViewModel.SetUseNormalReShade(targetCard, normalReShadeToggle.IsOn);

                // When normal ReShade is enabled, force addon toggle off and disable it
                if (normalReShadeToggle.IsOn)
                {
                    addonToggle.IsOn = false;
                    addonToggle.IsEnabled = false;
                    selectAddonsBtn.IsEnabled = false;
                    selectAddonsBtn.Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush);
                    selectAddonsBtn.Foreground = UIFactory.Brush(ResourceKeys.TextDisabledBrush);
                    selectAddonsBtn.BorderBrush = UIFactory.Brush(ResourceKeys.BorderSubtleBrush);
                }
                else
                {
                    // Re-enable addon toggle when switching back to addon ReShade
                    addonToggle.IsEnabled = true;
                    addonToggle.IsOn = true;
                }
            }
        };
        manageRightColumn.Children.Add(normalReShadeToggle);

        Grid.SetColumn(manageRightColumn, 2);
        manageRowGrid.Children.Add(manageRightColumn);

        panel.Children.Add(manageRowGrid);

        // Style the flyout presenter to allow scrolling and set max dimensions
        var flyoutStyle = new Style(typeof(FlyoutPresenter));
        flyoutStyle.Setters.Add(new Setter(FlyoutPresenter.MaxWidthProperty, 640));
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
