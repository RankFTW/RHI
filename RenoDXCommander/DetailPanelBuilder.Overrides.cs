// DetailPanelBuilder.Overrides.cs — Overrides panel construction and all inline UI logic for per-game overrides.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    public void BuildOverridesPanel(GameCardViewModel card)
    {
        _window.OverridesPanel.Children.Clear();

        var gameName = card.GameName;
        bool isLumaMode = _window.ViewModel.IsLumaEnabled(gameName);

        // ── Title ────────────────────────────────────────────────────────────────
        _window.OverridesPanel.Children.Add(new TextBlock
        {
            Text = "Overrides",
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
        });

        // ── Game name + Wiki name ────────────────────────────────────────────────
        var detectedBox = new TextBox
        {
            Header = "Game name (editable)",
            Text = gameName,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var wikiBox = new TextBox
        {
            Header = "Wiki mod name",
            PlaceholderText = "Exact wiki name",
            Text = _window.ViewModel.GetNameMapping(gameName) ?? "",
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var originalStoreName = _window.ViewModel.GetOriginalStoreName(gameName);

        // Mutable captured name so rename handler can update it for subsequent handlers
        var capturedName = gameName;

        var resetBtn = new Button
        {
            Content = "↩ Reset",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Bottom,
            Padding = new Thickness(10, 6, 10, 6),
            Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
        };
        ToolTipService.SetToolTip(resetBtn, "Reset game name back to auto-detected and clear wiki name mapping.");
        resetBtn.Click += (s, ev) =>
        {
            var resetName = (originalStoreName ?? gameName).Trim();
            detectedBox.Text = resetName;
            wikiBox.Text = "";

            // Persist wiki mapping removal
            if (_window.ViewModel.GetNameMapping(capturedName) != null)
                _window.ViewModel.RemoveNameMapping(capturedName);

            // Persist rename back to original if name was changed
            if (!resetName.Equals(capturedName, StringComparison.OrdinalIgnoreCase))
            {
                _window.ViewModel.RenameGame(capturedName, resetName);
                capturedName = resetName;
                _window.RequestReselect(resetName);
            }
        };

        // ── DLL naming override (placed in Top Row right column) ───────────
        bool isDllOverride = _window.ViewModel.HasDllOverride(gameName);
        var existingCfg = _window.ViewModel.GetDllOverride(gameName);
        bool is32Bit = card.Is32Bit;
        var defaultRsName = is32Bit ? "ReShade32.dll" : "ReShade64.dll";

        var dllOverrideToggle = new ToggleSwitch
        {
            Header = "DLL naming override",
            IsOn = isDllOverride,
            IsEnabled = !isLumaMode,
            OnContent = "Custom filenames enabled",
            OffContent = "Override ReShade filename",
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            FontSize = 12,
        };
        ToolTipService.SetToolTip(dllOverrideToggle,
            "Override the filenames ReShade is installed as. When enabled, existing RS files are renamed to the custom filenames.");
        var existingRsName = existingCfg?.ReShadeFileName ?? "";

        var rsNameBox = new ComboBox
        {
            IsEditable = true,
            PlaceholderText = defaultRsName,
            Header = (object?)null,
            FontSize = 12,
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
        dllOverrideToggle.Toggled += (s, ev) =>
        {
            rsNameBox.IsEnabled = dllOverrideToggle.IsOn;

            // Auto-save: persist DLL override state immediately
            bool nowOn = dllOverrideToggle.IsOn;
            bool wasOn = _window.ViewModel.HasDllOverride(capturedName);
            if (nowOn == wasOn) return;
            var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard == null) return;
            if (nowOn)
            {
                var rsText = rsNameBox.SelectedItem as string ?? rsNameBox.Text;
                var rsName = !string.IsNullOrWhiteSpace(rsText) ? rsText.Trim() : rsNameBox.PlaceholderText;
                _window.ViewModel.EnableDllOverride(targetCard, rsName, "");
            }
            else
            {
                _window.ViewModel.DisableDllOverride(targetCard);
            }
        };

        // ── Top Row Grid (3 columns: Star | Auto | Star) ─────────────────────
        var topRowGrid = new Grid { ColumnSpacing = 0 };
        topRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Left column: Game Name above (Wiki Name + Reset Button)
        var topLeftColumn = new StackPanel { Spacing = 8 };
        topLeftColumn.Children.Add(detectedBox);

        var wikiResetRow = new Grid { ColumnSpacing = 8 };
        wikiResetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        wikiResetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(wikiBox, 0);
        Grid.SetColumn(resetBtn, 1);
        wikiResetRow.Children.Add(wikiBox);
        wikiResetRow.Children.Add(resetBtn);
        topLeftColumn.Children.Add(wikiResetRow);

        Grid.SetColumn(topLeftColumn, 0);
        topRowGrid.Children.Add(topLeftColumn);

        // Column 1: Vertical divider
        var topRowDivider = new Border
        {
            Width = 1,
            Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(12, 0, 12, 0),
        };
        Grid.SetColumn(topRowDivider, 1);
        topRowGrid.Children.Add(topRowDivider);

        // Column 2: Wiki Exclusion toggle (+ Rendering Path ComboBox added by task 1.3)
        var wikiExcludeToggle = new ToggleSwitch
        {
            Header = "Wiki exclusion",
            IsOn = _window.ViewModel.IsWikiExcluded(gameName),
            OnContent = "Excluded from wiki lookups",
            OffContent = "Included in wiki lookups",
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            FontSize = 12,
        };
        ToolTipService.SetToolTip(wikiExcludeToggle,
            "When enabled, this game will not be looked up on the RenoDX wiki. Useful for games that share a name with an unrelated wiki entry.");

        // Auto-save: Wiki exclusion toggle
        wikiExcludeToggle.Toggled += (s, ev) =>
        {
            if (wikiExcludeToggle.IsOn != _window.ViewModel.IsWikiExcluded(capturedName))
                _window.ViewModel.ToggleWikiExclusion(capturedName);
        };

        // ── Rendering Path (dual-API games only) ─────────────────────────────────
        // Rendering Path ComboBox removed — API toggles make it redundant.
        ComboBox? renderPathCombo = null;

        var topRightColumn = new StackPanel { Spacing = 8 };
        topRightColumn.Children.Add(wikiExcludeToggle);
        // DLL naming override moved here from the old Bottom Row
        topRightColumn.Children.Add(dllOverrideToggle);
        topRightColumn.Children.Add(rsNameBox);
        Grid.SetColumn(topRightColumn, 2);
        topRowGrid.Children.Add(topRightColumn);

        _window.OverridesPanel.Children.Add(topRowGrid);
        _window.OverridesPanel.Children.Add(UIFactory.MakeSeparator());

        // ── Auto-save: Game name on Enter ────────────────────────────────────────
        detectedBox.KeyDown += (s, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            var det = detectedBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(det)) return;
            if (det.Equals(capturedName, StringComparison.OrdinalIgnoreCase)) return;
            _window.ViewModel.RenameGame(capturedName, det);
            _window.RequestReselect(det);
            capturedName = det;
        };

        // ── Auto-save: Wiki name on Enter ────────────────────────────────────────
        wikiBox.KeyDown += (s, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            var key = wikiBox.Text?.Trim();
            if (!string.IsNullOrEmpty(key))
            {
                var existing = _window.ViewModel.GetNameMapping(capturedName);
                if (!key.Equals(existing, StringComparison.OrdinalIgnoreCase))
                    _window.ViewModel.AddNameMapping(capturedName, key);
            }
            else
            {
                if (_window.ViewModel.GetNameMapping(capturedName) != null)
                    _window.ViewModel.RemoveNameMapping(capturedName);
            }
        };

        // ── Per-game Shader mode ─────────────────────────────────────────────
        string currentShaderMode = _window.ViewModel.GetPerGameShaderMode(gameName);
        bool isGlobalShaders = currentShaderMode != "Select";
        var shaderToggle = new ToggleSwitch
        {
            Header = "Global",
            IsOn = isGlobalShaders,
            OnContent = "On",
            OffContent = "Off",
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
            if (newMode != _window.ViewModel.GetPerGameShaderMode(capturedName))
            {
                _window.ViewModel.SetPerGameShaderMode(capturedName, newMode);
                if (newMode == "Global")
                    _window.ViewModel.GameNameServiceInstance.PerGameShaderSelection.Remove(capturedName);
                _window.ViewModel.DeployShadersForCard(capturedName);
            }
        };

        // ── Per-game Custom Shaders toggle ─────────────────────────────────
        bool isPerGameCustom = currentShaderMode == "Custom";
        // Default to ON when global UseCustomShaders is enabled and no per-game override exists
        bool customDefault = isPerGameCustom ||
            (_window.ViewModel.Settings.UseCustomShaders && currentShaderMode == "Global"
             && !_window.ViewModel.GameNameServiceInstance.PerGameShaderMode.ContainsKey(gameName));
        var customShadersToggle = new ToggleSwitch
        {
            Header = "Custom",
            IsOn = customDefault,
            OnContent = "On",
            OffContent = "Off",
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
                _window.ViewModel.SetPerGameShaderMode(capturedName, "Custom");
            }
            else
            {
                _window.ViewModel.SetPerGameShaderMode(capturedName, "Global");
            }
            _window.ViewModel.DeployShadersForCard(capturedName);
        };

        selectShadersBtn.Click += async (s, ev) =>
        {
            List<string>? current = _window.ViewModel.GameNameServiceInstance.PerGameShaderSelection.TryGetValue(gameName, out var existing)
                ? existing
                : _window.ViewModel.Settings.SelectedShaderPacks;
            var result = await ShaderPopupHelper.ShowAsync(
                _window.Content.XamlRoot,
                _window.ViewModel.ShaderPackServiceInstance,
                current,
                ShaderPopupHelper.PopupContext.PerGame);
            if (result != null)
            {
                _window.ViewModel.GameNameServiceInstance.PerGameShaderSelection[gameName] = result;
                _window.ViewModel.DeployShadersForCard(capturedName);
            }
        };

        // ── Shaders section (left column of Middle Row) ──────────────────────
        var shadersLabel = new TextBlock
        {
            Text = "Shaders",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            Margin = new Thickness(0, 0, 0, 8),
        };
        var shaderTogglesRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
        };
        shaderTogglesRow.Children.Add(shaderToggle);
        shaderTogglesRow.Children.Add(customShadersToggle);

        var shaderColumn = new StackPanel { Spacing = 8 };
        shaderColumn.Children.Add(shadersLabel);
        shaderColumn.Children.Add(shaderTogglesRow);
        shaderColumn.Children.Add(selectShadersBtn);
        Grid.SetColumn(shaderColumn, 0);

        // ── Auto-save: RS name box on Enter ──────────────────────────────────────
        rsNameBox.KeyDown += (s, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            if (!dllOverrideToggle.IsOn) return;
            var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard == null) return;
            var rsText = rsNameBox.SelectedItem as string ?? rsNameBox.Text;
            var rsName = !string.IsNullOrWhiteSpace(rsText) ? rsText.Trim() : rsNameBox.PlaceholderText;
            _window.ViewModel.UpdateDllOverrideNames(targetCard, rsName, "");
        };
        // ── Auto-save: RS name box on dropdown selection ─────────────────────────
        rsNameBox.SelectionChanged += (s, e) =>
        {
            if (!dllOverrideToggle.IsOn) return;
            var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard == null) return;
            var rsName = rsNameBox.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(rsName)) return;
            _window.ViewModel.UpdateDllOverrideNames(targetCard, rsName, "");
        };

        // ── Bitness Override ComboBox (left column of Bitness & API Row) ─────────
        var bitnessLabel = new TextBlock
        {
            Text = "Bitness",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            Margin = new Thickness(0, 0, 0, 8),
        };

        var bitnessItems = new[] { "Auto", "32-bit", "64-bit" };
        var currentBitnessOverride = _window.ViewModel.GetBitnessOverride(gameName);
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
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        ToolTipService.SetToolTip(bitnessCombo,
            "Override the auto-detected bitness for this game. Auto uses PE header detection. 32-bit or 64-bit forces the value.");

        bitnessCombo.SelectionChanged += (s, e) =>
        {
            var selected = bitnessCombo.SelectedItem as string;
            string? overrideValue = selected switch
            {
                "32-bit" => "32",
                "64-bit" => "64",
                _ => null,
            };

            _window.ViewModel.SetBitnessOverride(capturedName, overrideValue);

            // Update card.Is32Bit based on selection
            var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard != null)
            {
                if (overrideValue == "32")
                    targetCard.Is32Bit = true;
                else if (overrideValue == "64")
                    targetCard.Is32Bit = false;
                else
                {
                    // "Auto" — re-resolve from auto-detection
                    var detectedMachine = _window.ViewModel.PeHeaderServiceInstance.DetectGameArchitecture(targetCard.InstallPath);
                    targetCard.Is32Bit = _window.ViewModel.ResolveIs32Bit(capturedName, detectedMachine);
                }

                // Update DLL naming section placeholder text to match new bitness
                rsNameBox.PlaceholderText = targetCard.Is32Bit ? "ReShade32.dll" : "ReShade64.dll";

                targetCard.NotifyAll();
            }
        };

        var bitnessPanel = new StackPanel { Spacing = 8 };
        bitnessPanel.Children.Add(bitnessLabel);
        bitnessPanel.Children.Add(bitnessCombo);

        // ── API Override ToggleSwitches (right column of Bitness & API Row) ──────
        var apiLabel = new TextBlock
        {
            Text = "Graphics API",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            Margin = new Thickness(0, 0, 0, 8),
        };
        ToolTipService.SetToolTip(apiLabel,
            "Override the detected graphics APIs for this game.\n\n" +
            "Only one API drives ReShade's DLL filename at a time, even if multiple are selected.\n" +
            "Priority: DX11/12 → Vulkan → OpenGL → DX10 → DX9 → DX8.\n\n" +
            "User overrides set here take precedence over manifest and auto-detected values.\n" +
            "Reset Overrides reverts to auto-detection.");

        // Each entry maps a display label to one or more GraphicsApiType enum names.
        // "DX11/DX12" is a combined toggle that controls both DirectX11 and DirectX12.
        var apiToggleDefs = new (string Label, string[] EnumNames)[]
        {
            ("DirectX8",  new[] { "DirectX8" }),
            ("DirectX9",  new[] { "DirectX9" }),
            ("DirectX10", new[] { "DirectX10" }),
            ("DX11/DX12", new[] { "DirectX11", "DirectX12" }),
            ("Vulkan",    new[] { "Vulkan" }),
            ("OpenGL",    new[] { "OpenGL" }),
        };
        var existingApiOverride = _window.ViewModel.GetApiOverride(gameName);
        var apiToggles = new Dictionary<string, (ToggleSwitch Toggle, string[] EnumNames)>();
        var apiTogglePanel = new Controls.WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 8 };

        foreach (var (label, enumNames) in apiToggleDefs)
        {
            bool isOn;
            if (existingApiOverride != null)
                isOn = enumNames.Any(n => existingApiOverride.Contains(n, StringComparer.OrdinalIgnoreCase));
            else
                isOn = enumNames.Any(n => card.DetectedApis.Contains(Enum.Parse<GraphicsApiType>(n)));

            var toggle = new ToggleSwitch
            {
                Header = label,
                IsOn = isOn,
                OnContent = "On",
                OffContent = "Off",
                Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
                FontSize = 11,
                MinWidth = 0,
            };

            toggle.Toggled += (s, ev) =>
            {
                // Collect all enabled API enum names from current toggle states
                var enabledApis = new List<string>();
                foreach (var kvp in apiToggles)
                {
                    if (kvp.Value.Toggle.IsOn)
                        enabledApis.AddRange(kvp.Value.EnumNames);
                }

                // Persist the override
                _window.ViewModel.SetApiOverride(capturedName, enabledApis);

                // Update card properties
                var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                if (targetCard != null)
                {
                    var newApis = new HashSet<GraphicsApiType>();
                    foreach (var name in enabledApis)
                    {
                        if (Enum.TryParse<GraphicsApiType>(name, out var apiType))
                            newApis.Add(apiType);
                    }
                    targetCard.DetectedApis = newApis;
                    targetCard.IsDualApiGame = GraphicsApiDetector.IsDualApi(newApis);
                    targetCard.GraphicsApi = _window.ViewModel.DetectGraphicsApi(
                        targetCard.InstallPath, EngineType.Unknown, capturedName);
                    targetCard.NotifyAll();
                }
            };

            var toggleBorder = new Border
            {
                Child = toggle,
                BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 6, 8, 6),
            };

            apiToggles[label] = (toggle, enumNames);
            apiTogglePanel.Children.Add(toggleBorder);
        }

        var apiPanel = new StackPanel { Spacing = 8 };
        apiPanel.Children.Add(apiLabel);
        apiPanel.Children.Add(apiTogglePanel);

        // ── Global update inclusion toggles (Middle Row right column) ──────────────
        var rsToggle = new ToggleSwitch
        {
            Header = "ReShade",
            IsOn = !_window.ViewModel.IsUpdateAllExcludedReShade(gameName),
            OnContent = "Yes",
            OffContent = "No",
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            FontSize = 11,
            MinWidth = 0,
        };
        var rdxToggle = new ToggleSwitch
        {
            Header = "RenoDX",
            IsOn = !_window.ViewModel.IsUpdateAllExcludedRenoDx(gameName),
            OnContent = "Yes",
            OffContent = "No",
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            FontSize = 11,
            MinWidth = 0,
        };
        var ulToggle = new ToggleSwitch
        {
            Header = "ReLimiter",
            IsOn = !_window.ViewModel.IsUpdateAllExcludedUl(gameName),
            OnContent = "Yes",
            OffContent = "No",
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

        var toggleRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
        };
        toggleRow.Children.Add(rsBorder);
        toggleRow.Children.Add(rdxBorder);
        toggleRow.Children.Add(ulBorder);

        // ── Auto-save: Update inclusion toggles ──────────────────────────────────
        rsToggle.Toggled += (s, ev) =>
        {
            if (!rsToggle.IsOn != _window.ViewModel.IsUpdateAllExcludedReShade(capturedName))
                _window.ViewModel.ToggleUpdateAllExclusionReShade(capturedName);
        };
        rdxToggle.Toggled += (s, ev) =>
        {
            if (!rdxToggle.IsOn != _window.ViewModel.IsUpdateAllExcludedRenoDx(capturedName))
                _window.ViewModel.ToggleUpdateAllExclusionRenoDx(capturedName);
        };
        ulToggle.Toggled += (s, ev) =>
        {
            if (!ulToggle.IsOn != _window.ViewModel.IsUpdateAllExcludedUl(capturedName))
                _window.ViewModel.ToggleUpdateAllExclusionUl(capturedName);
        };

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

        // ── Middle Row Grid (3 columns: Star | Auto | Star) ─────────────────
        var middleRowGrid = new Grid { ColumnSpacing = 0 };
        middleRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        middleRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        middleRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Grid.SetColumn(shaderColumn, 0);
        Grid.SetColumn(middleRowDivider, 1);
        Grid.SetColumn(globalUpdateColumn, 2);

        middleRowGrid.Children.Add(shaderColumn);
        middleRowGrid.Children.Add(middleRowDivider);
        middleRowGrid.Children.Add(globalUpdateColumn);

        _window.OverridesPanel.Children.Add(middleRowGrid);
        _window.OverridesPanel.Children.Add(UIFactory.MakeSeparator());

        // ── Bitness & API Row Grid (3 columns: Star | Auto divider | Star) ──
        var bitnessApiGrid = new Grid { ColumnSpacing = 0 };
        bitnessApiGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bitnessApiGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bitnessApiGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Grid.SetColumn(bitnessPanel, 0);
        var bitnessApiDivider = new Border
        {
            Width = 1,
            Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(12, 0, 12, 0),
        };
        Grid.SetColumn(bitnessApiDivider, 1);
        Grid.SetColumn(apiPanel, 2);

        bitnessApiGrid.Children.Add(bitnessPanel);
        bitnessApiGrid.Children.Add(bitnessApiDivider);
        bitnessApiGrid.Children.Add(apiPanel);

        _window.OverridesPanel.Children.Add(bitnessApiGrid);
        _window.OverridesPanel.Children.Add(UIFactory.MakeSeparator());

        // ── Button row (Reset only — auto-save replaces Save button) ──────────
        var resetOverridesBtn = new Button
        {
            Content = "Reset Overrides",
            FontSize = 12,
            Padding = new Thickness(16, 8, 16, 8),
            Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
        };
        resetOverridesBtn.Click += (s, ev) =>
        {
            // Reset all controls to defaults
            detectedBox.Text = originalStoreName ?? gameName;
            wikiBox.Text = "";
            shaderToggle.IsOn = true;
            customShadersToggle.IsOn = false;
            if (renderPathCombo != null) renderPathCombo.SelectedItem = "DirectX";
            dllOverrideToggle.IsOn = false;
            rsToggle.IsOn = true;
            rdxToggle.IsOn = true;
            ulToggle.IsOn = true;
            wikiExcludeToggle.IsOn = false;

            // Persist all reset values immediately
            var resetName = (originalStoreName ?? gameName).Trim();
            bool nameChanged = !resetName.Equals(capturedName, StringComparison.OrdinalIgnoreCase);
            if (nameChanged && !string.IsNullOrWhiteSpace(resetName))
            {
                _window.ViewModel.RenameGame(capturedName, resetName);
                capturedName = resetName;
            }

            // Remove wiki mapping
            if (_window.ViewModel.GetNameMapping(capturedName) != null)
                _window.ViewModel.RemoveNameMapping(capturedName);

            // Shader mode → Global
            if (_window.ViewModel.GetPerGameShaderMode(capturedName) != "Global")
            {
                _window.ViewModel.SetPerGameShaderMode(capturedName, "Global");
                _window.ViewModel.GameNameServiceInstance.PerGameShaderSelection.Remove(capturedName);
                _window.ViewModel.DeployShadersForCard(capturedName);
            }

            // Disable DLL override
            if (_window.ViewModel.HasDllOverride(capturedName))
            {
                var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                if (targetCard != null)
                    _window.ViewModel.DisableDllOverride(targetCard);
            }

            // Include all in Update All
            if (_window.ViewModel.IsUpdateAllExcludedReShade(capturedName))
                _window.ViewModel.ToggleUpdateAllExclusionReShade(capturedName);
            if (_window.ViewModel.IsUpdateAllExcludedRenoDx(capturedName))
                _window.ViewModel.ToggleUpdateAllExclusionRenoDx(capturedName);
            if (_window.ViewModel.IsUpdateAllExcludedUl(capturedName))
                _window.ViewModel.ToggleUpdateAllExclusionUl(capturedName);

            // Disable wiki exclusion
            if (_window.ViewModel.IsWikiExcluded(capturedName))
                _window.ViewModel.ToggleWikiExclusion(capturedName);

            // Reset bitness override to Auto
            bitnessCombo.SelectedItem = "Auto";
            _window.ViewModel.SetBitnessOverride(capturedName, null);

            // Reset API overrides
            _window.ViewModel.SetApiOverride(capturedName, null);

            // Revert card properties to auto-detected values
            {
                var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                    c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                if (targetCard != null)
                {
                    // Re-resolve bitness from PE header auto-detection
                    var detectedMachine = _window.ViewModel.PeHeaderServiceInstance.DetectGameArchitecture(targetCard.InstallPath);
                    targetCard.Is32Bit = _window.ViewModel.ResolveIs32Bit(capturedName, detectedMachine);

                    // Re-detect APIs from scanning (overrides are now cleared)
                    targetCard.DetectedApis = _window.ViewModel._DetectAllApisForCard(targetCard.InstallPath, capturedName);
                    targetCard.IsDualApiGame = GraphicsApiDetector.IsDualApi(targetCard.DetectedApis);
                    targetCard.GraphicsApi = _window.ViewModel.DetectGraphicsApi(
                        targetCard.InstallPath, EngineType.Unknown, capturedName);

                    // Reset API toggles to reflect auto-detected state
                    foreach (var kvp in apiToggles)
                    {
                        kvp.Value.Toggle.IsOn = kvp.Value.EnumNames
                            .Any(n => Enum.TryParse<GraphicsApiType>(n, out var apiType) && targetCard.DetectedApis.Contains(apiType));
                    }

                    // Update DLL naming placeholder to match new bitness
                    rsNameBox.PlaceholderText = targetCard.Is32Bit ? "ReShade32.dll" : "ReShade64.dll";

                    targetCard.NotifyAll();
                }
            }

            CrashReporter.Log($"[DetailPanelBuilder.BuildOverridesPanel] Overrides reset for: {capturedName}");

            // Only reselect if the game name actually changed
            if (nameChanged)
                _window.RequestReselect(capturedName);
        };

        _window.OverridesPanel.Children.Add(resetOverridesBtn);
    }
}
