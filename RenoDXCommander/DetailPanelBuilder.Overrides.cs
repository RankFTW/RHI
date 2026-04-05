// DetailPanelBuilder.Overrides.cs — Overrides panel construction and all inline UI logic for per-game overrides.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    internal static readonly string[] DcDllOverrideNames =
    [
        "dxgi.dll", "d3d9.dll", "d3d11.dll", "d3d12.dll", "ddraw.dll",
        "hid.dll", "version.dll", "opengl32.dll", "dbghelp.dll",
        "vulkan-1.dll", "winmm.dll",
    ];
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
            Text = _window.ViewModel.GetUserNameMapping(gameName) ?? "",
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
            Header = "DLL naming overrides",
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
            PlaceholderText = "Select ReShade DLL name",
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
        // ── DC DLL naming override ─────────────────────────────────────────
        var existingDcName = existingCfg?.DcFileName ?? "";
        bool isDcDllOverrideOn = isDllOverride && !string.IsNullOrEmpty(existingDcName);

        var dcDllToggle = new ToggleSwitch
        {
            Header = (object?)null,
            IsOn = isDcDllOverrideOn,
            IsEnabled = !isLumaMode,
            OnContent = "Custom DC filename",
            OffContent = "Override DC filename",
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            FontSize = 12,
        };
        ToolTipService.SetToolTip(dcDllToggle,
            "Override the filename Display Commander is installed as. When enabled, existing DC files are renamed to the custom filename.");

        var dcNameBox = new ComboBox
        {
            IsEditable = true,
            PlaceholderText = "Select DC DLL name",
            FontSize = 12,
            IsEnabled = isDcDllOverrideOn,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = DcDllOverrideNames,
        };
        if (!string.IsNullOrEmpty(existingDcName))
        {
            if (DcDllOverrideNames.Contains(existingDcName, StringComparer.OrdinalIgnoreCase))
                dcNameBox.SelectedItem = DcDllOverrideNames.First(n => n.Equals(existingDcName, StringComparison.OrdinalIgnoreCase));
            else
            {
                var capturedDc = existingDcName;
                dcNameBox.Loaded += (s, e) => dcNameBox.Text = capturedDc;
            }
        }

        // Track previous DC selection for revert on foreign DLL conflict cancel
        string? _previousDcSelection = dcNameBox.SelectedItem as string;

        // ── Cross-exclusion: filter out the other component's current name ───────
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
                    ? DcDllOverrideNames
                    : DcDllOverrideNames.Where(n => !n.Equals(rsCurrentName, StringComparison.OrdinalIgnoreCase)).ToArray();
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
                var dcCurrentName = dcDllToggle.IsOn
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

            var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard == null) return;

            if (dllOverrideToggle.IsOn)
            {
                // Turning RS override ON — only proceed if a real DLL name is selected
                var rsText = rsNameBox.SelectedItem as string ?? rsNameBox.Text;
                if (string.IsNullOrWhiteSpace(rsText))
                {
                    // No name selected — don't rename anything, just enable the dropdown
                    return;
                }
                var rsName = rsText.Trim();
                var dcName = dcDllToggle.IsOn ? (dcNameBox.SelectedItem as string ?? "") : "";
                _window.ViewModel.EnableDllOverride(targetCard, rsName, dcName);
            }
            else
            {
                // Turning RS override OFF — revert RS file to default, preserve DC
                var existingCfgNow = _window.ViewModel.GetDllOverride(capturedName);
                var dcName = dcDllToggle.IsOn ? (dcNameBox.SelectedItem as string ?? "") : "";

                // Rename RS back to default if it was custom-named
                if (existingCfgNow != null && !string.IsNullOrWhiteSpace(existingCfgNow.ReShadeFileName)
                    && targetCard.RsRecord != null && !string.IsNullOrEmpty(targetCard.InstallPath))
                {
                    var defaultRsName = Services.AuxInstallService.RsNormalName;
                    var oldPath = System.IO.Path.Combine(targetCard.InstallPath, existingCfgNow.ReShadeFileName);
                    var newPath = System.IO.Path.Combine(targetCard.InstallPath, defaultRsName);
                    try
                    {
                        if (System.IO.File.Exists(oldPath) && !oldPath.Equals(newPath, StringComparison.OrdinalIgnoreCase))
                        {
                            if (System.IO.File.Exists(newPath)) System.IO.File.Delete(newPath);
                            System.IO.File.Move(oldPath, newPath);
                            targetCard.RsRecord.InstalledAs = defaultRsName;
                            targetCard.RsInstalledFile = defaultRsName;
                        }
                    }
                    catch { }
                }

                if (!string.IsNullOrEmpty(dcName))
                {
                    // DC override still active — update config with empty RS, keep DC
                    _window.ViewModel.UpdateDllOverrideNames(targetCard, "", dcName);
                    targetCard.DllOverrideEnabled = true;
                }
                else
                {
                    // Neither override active — remove the config entirely
                    _window.ViewModel.DisableDllOverride(targetCard);
                }
                targetCard.NotifyAll();
                // Clear the dropdown so it shows placeholder text
                rsNameBox.SelectedIndex = -1;
                if (rsNameBox.IsEditable) rsNameBox.Text = "";
            }
        };

        // ── Auto-save: DC DLL toggle ─────────────────────────────────────────────
        dcDllToggle.Toggled += (s, ev) =>
        {
            dcNameBox.IsEnabled = dcDllToggle.IsOn;
            var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard == null) return;

            if (dcDllToggle.IsOn)
            {
                // Turning DC override ON — set DC name, preserve RS
                var rsText = rsNameBox.SelectedItem as string ?? rsNameBox.Text;
                var rsName = dllOverrideToggle.IsOn ? (!string.IsNullOrWhiteSpace(rsText) ? rsText.Trim() : "") : "";
                var dcName = dcNameBox.SelectedItem as string ?? "";
                _window.ViewModel.EnableDllOverride(targetCard, rsName, dcName);
            }
            else
            {
                // Turning DC override OFF — revert DC file to default, preserve RS
                var existingCfgNow = _window.ViewModel.GetDllOverride(capturedName);
                if (existingCfgNow != null && !string.IsNullOrWhiteSpace(existingCfgNow.DcFileName)
                    && !string.IsNullOrEmpty(targetCard.DcInstalledFile) && !string.IsNullOrEmpty(targetCard.InstallPath))
                {
                    var defaultDcName = targetCard.Is32Bit
                        ? "zzz_display_commander_lite.addon32"
                        : "zzz_display_commander_lite.addon64";
                    var deployPath = Services.ModInstallService.GetAddonDeployPath(targetCard.InstallPath);
                    var oldPath = System.IO.Path.Combine(deployPath, targetCard.DcInstalledFile);
                    var newPath = System.IO.Path.Combine(deployPath, defaultDcName);
                    try
                    {
                        if (System.IO.File.Exists(oldPath) && !oldPath.Equals(newPath, StringComparison.OrdinalIgnoreCase))
                        {
                            if (System.IO.File.Exists(newPath)) System.IO.File.Delete(newPath);
                            System.IO.File.Move(oldPath, newPath);
                            targetCard.DcInstalledFile = defaultDcName;
                        }
                    }
                    catch { }
                }

                var rsName = dllOverrideToggle.IsOn
                    ? (!string.IsNullOrWhiteSpace(rsNameBox.SelectedItem as string ?? rsNameBox.Text)
                        ? (rsNameBox.SelectedItem as string ?? rsNameBox.Text).Trim()
                        : "")
                    : "";

                if (!string.IsNullOrEmpty(rsName))
                {
                    // RS override still active — update config with empty DC, keep RS
                    _window.ViewModel.UpdateDllOverrideNames(targetCard, rsName, "");
                    targetCard.DllOverrideEnabled = true;
                }
                else
                {
                    // Neither override active — remove the config entirely
                    _window.ViewModel.DisableDllOverride(targetCard);
                }
                targetCard.NotifyAll();
                // Clear the dropdown so it shows placeholder text
                dcNameBox.SelectedIndex = -1;
                if (dcNameBox.IsEditable) dcNameBox.Text = "";
            }
        };

        // ── Auto-save: DC name box on dropdown selection (with foreign DLL check) ──
        dcNameBox.SelectionChanged += async (s, e) =>
        {
            if (!dcDllToggle.IsOn) return;
            var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard == null) return;
            var dcName = dcNameBox.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(dcName)) return;

            // Check for foreign DLL conflict before proceeding
            bool allowed = await _window.ViewModel.DllOverrideServiceInstance
                .CheckDcForeignDllConflictAsync(targetCard, dcName);
            if (!allowed)
            {
                // Revert dropdown to previous selection
                if (_previousDcSelection != null)
                    dcNameBox.SelectedItem = DcDllOverrideNames.FirstOrDefault(n =>
                        n.Equals(_previousDcSelection, StringComparison.OrdinalIgnoreCase));
                else
                    dcNameBox.SelectedIndex = -1;
                return;
            }

            _previousDcSelection = dcName;
            var rsText = rsNameBox.SelectedItem as string ?? rsNameBox.Text;
            var rsName = !string.IsNullOrWhiteSpace(rsText) ? rsText.Trim() : "";
            _window.ViewModel.UpdateDllOverrideNames(targetCard, rsName, dcName);
            UpdateRsDropdownItems();
        };

        // ── Auto-save: DC name box on Enter (manual typed name) ──────────────────
        dcNameBox.KeyDown += (s, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            if (!dcDllToggle.IsOn) return;
            var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard == null) return;
            var dcName = (dcNameBox.SelectedItem as string ?? dcNameBox.Text)?.Trim();
            if (string.IsNullOrWhiteSpace(dcName)) return;
            var rsText = rsNameBox.SelectedItem as string ?? rsNameBox.Text;
            var rsName = !string.IsNullOrWhiteSpace(rsText) ? rsText.Trim() : "";
            _window.ViewModel.UpdateDllOverrideNames(targetCard, rsName, dcName);
            UpdateRsDropdownItems();
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

        // Wiki exclusion toggle (compact, no header — placed below wiki name)
        var wikiExcludeToggle = new ToggleSwitch
        {
            IsOn = _window.ViewModel.IsWikiExcluded(gameName),
            OnContent = "Excluded from wiki lookups",
            OffContent = "Included in wiki lookups",
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            FontSize = 11,
        };
        ToolTipService.SetToolTip(wikiExcludeToggle,
            "When enabled, this game will not be looked up on the RenoDX wiki. Useful for games that share a name with an unrelated wiki entry.");
        wikiExcludeToggle.Toggled += (s, ev) =>
        {
            if (wikiExcludeToggle.IsOn != _window.ViewModel.IsWikiExcluded(capturedName))
                _window.ViewModel.ToggleWikiExclusion(capturedName);
        };
        topLeftColumn.Children.Add(wikiExcludeToggle);

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

        // ── Rendering Path (dual-API games only) ─────────────────────────────────
        // Rendering Path ComboBox removed — API toggles make it redundant.
        ComboBox? renderPathCombo = null;

        // Column 2: DLL naming override
        var topRightColumn = new StackPanel { Spacing = 8 };
        // DLL naming override moved here from the old Bottom Row
        topRightColumn.Children.Add(dllOverrideToggle);
        topRightColumn.Children.Add(rsNameBox);
        // DC DLL naming override
        topRightColumn.Children.Add(dcDllToggle);
        topRightColumn.Children.Add(dcNameBox);
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
            var rsName = !string.IsNullOrWhiteSpace(rsText) ? rsText.Trim() : "";
            if (string.IsNullOrEmpty(rsName)) return;
            var dcName = dcDllToggle.IsOn ? (dcNameBox.SelectedItem as string ?? dcNameBox.Text ?? "") : "";

            if (_window.ViewModel.HasDllOverride(capturedName))
                _window.ViewModel.UpdateDllOverrideNames(targetCard, rsName, dcName);
            else
                _window.ViewModel.EnableDllOverride(targetCard, rsName, dcName);
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
            var dcName = dcDllToggle.IsOn ? (dcNameBox.SelectedItem as string ?? dcNameBox.Text ?? "") : "";

            if (_window.ViewModel.HasDllOverride(capturedName))
                _window.ViewModel.UpdateDllOverrideNames(targetCard, rsName, dcName);
            else
                _window.ViewModel.EnableDllOverride(targetCard, rsName, dcName);

            UpdateDcDropdownItems();
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

                // Rebuild the detail panel so install buttons reflect the new bitness
                _window.RequestReselect(capturedName);
            }
        };

        var bitnessPanel = new StackPanel { Spacing = 8 };
        bitnessPanel.Children.Add(bitnessLabel);
        bitnessPanel.Children.Add(bitnessCombo);

        // ── API Override ComboBox (single selection, placed in left panel below bitness) ──────
        var apiLabel = new TextBlock
        {
            Text = "Graphics API",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            Margin = new Thickness(0, 0, 0, 8),
        };
        ToolTipService.SetToolTip(apiLabel,
            "Override the detected graphics API for this game.\n\n" +
            "Auto uses the auto-detected value from PE header scanning.\n" +
            "User overrides set here take precedence over manifest and auto-detected values.\n" +
            "Reset Overrides reverts to auto-detection.");

        var apiDropdownItems = new[] { "Auto", "DirectX8", "DirectX9", "DirectX10", "DX11/DX12", "Vulkan", "OpenGL" };
        var existingApiOverride = _window.ViewModel.GetApiOverride(gameName);

        // Determine current selection
        string defaultApiSelection = "Auto";
        if (existingApiOverride != null && existingApiOverride.Count > 0)
        {
            // Map stored override back to dropdown label
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
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        apiCombo.SelectionChanged += (s, ev) =>
        {
            var selected = apiCombo.SelectedItem as string;

            // Map dropdown label to enum names for persistence
            List<string>? apiEnumNames = selected switch
            {
                "DirectX8" => new() { "DirectX8" },
                "DirectX9" => new() { "DirectX9" },
                "DirectX10" => new() { "DirectX10" },
                "DX11/DX12" => new() { "DirectX11", "DirectX12" },
                "Vulkan" => new() { "Vulkan" },
                "OpenGL" => new() { "OpenGL" },
                _ => null, // "Auto" clears the override
            };

            _window.ViewModel.SetApiOverride(capturedName, apiEnumNames);

            // Update card properties
            var targetCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard != null)
            {
                if (apiEnumNames != null)
                {
                    var newApis = new HashSet<GraphicsApiType>();
                    foreach (var name in apiEnumNames)
                    {
                        if (Enum.TryParse<GraphicsApiType>(name, out var apiType))
                            newApis.Add(apiType);
                    }
                    targetCard.DetectedApis = newApis;
                }
                else
                {
                    // "Auto" — re-detect from scanning
                    targetCard.DetectedApis = _window.ViewModel._DetectAllApisForCard(targetCard.InstallPath, capturedName);
                }
                targetCard.IsDualApiGame = GraphicsApiDetector.IsDualApi(targetCard.DetectedApis);
                targetCard.GraphicsApi = _window.ViewModel.DetectGraphicsApi(
                    targetCard.InstallPath, EngineType.Unknown, capturedName);
                targetCard.NotifyAll();

                // Rebuild the detail panel so install buttons reflect the new API
                // (e.g., Vulkan games need the global layer install instead of per-game DLL)
                _window.RequestReselect(capturedName);
            }
        };

        // Add API dropdown to bitness panel (below bitness combo)
        bitnessPanel.Children.Add(apiLabel);
        bitnessPanel.Children.Add(apiCombo);

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
        var dcToggle = new ToggleSwitch
        {
            Header = "DC",
            IsOn = !card.ExcludeFromUpdateAllDc,
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
        dcToggle.Toggled += (s, ev) =>
        {
            if (!dcToggle.IsOn != _window.ViewModel.IsUpdateAllExcludedDc(capturedName))
                _window.ViewModel.ToggleUpdateAllExclusionDc(capturedName);
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

        // ── Bottom Row Grid (3 columns: Star | Auto divider | Star) — Shaders (left) ──
        var bottomRowGrid = new Grid { ColumnSpacing = 0 };
        bottomRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottomRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bottomRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Grid.SetColumn(shaderColumn, 0);
        var bottomRowDivider = new Border
        {
            Width = 1,
            Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(12, 0, 12, 0),
        };
        Grid.SetColumn(bottomRowDivider, 1);

        bottomRowGrid.Children.Add(shaderColumn);
        bottomRowGrid.Children.Add(bottomRowDivider);

        // ── Per-game Addon mode (right column of Bottom Row) ─────────────────
        string currentAddonMode = _window.ViewModel.GetPerGameAddonMode(gameName);
        bool isGlobalAddons = currentAddonMode != "Select";
        var addonToggle = new ToggleSwitch
        {
            Header = "Global",
            IsOn = isGlobalAddons,
            OnContent = "On",
            OffContent = "Off",
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

            // Auto-save: persist addon mode immediately
            var newMode = global ? "Global" : "Select";
            if (newMode != _window.ViewModel.GetPerGameAddonMode(capturedName))
            {
                _window.ViewModel.SetPerGameAddonMode(capturedName, newMode);
                _window.ViewModel.DeployAddonsForCard(capturedName);
            }
        };

        selectAddonsBtn.Click += async (s, ev) =>
        {
            List<string>? current = _window.ViewModel.GameNameServiceInstance.PerGameAddonSelection.TryGetValue(gameName, out var existingAddons)
                ? existingAddons
                : null;

            // AddonPackService may not be wired into MainViewModel yet (Task 9.1).
            // Access it via the exposed instance if available, otherwise show a placeholder message.
            IAddonPackService? addonPackService = null;
            var addonSvcProp = _window.ViewModel.GetType().GetProperty("AddonPackServiceInstance");
            if (addonSvcProp != null)
                addonPackService = addonSvcProp.GetValue(_window.ViewModel) as IAddonPackService;

            if (addonPackService == null)
            {
                // Fallback: show info that addon service is not yet available
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
                _window.ViewModel.GameNameServiceInstance.PerGameAddonSelection[gameName] = result;
                _window.ViewModel.DeployAddonsForCard(capturedName);
            }
        };

        var addonsLabel = new TextBlock
        {
            Text = "Addons",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            Margin = new Thickness(0, 0, 0, 8),
        };

        var addonColumn = new StackPanel { Spacing = 8 };
        addonColumn.Children.Add(addonsLabel);
        addonColumn.Children.Add(addonToggle);
        addonColumn.Children.Add(selectAddonsBtn);
        Grid.SetColumn(addonColumn, 2);

        bottomRowGrid.Children.Add(addonColumn);

        _window.OverridesPanel.Children.Add(bottomRowGrid);
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
            addonToggle.IsOn = true;
            if (renderPathCombo != null) renderPathCombo.SelectedItem = "DirectX";
            dllOverrideToggle.IsOn = false;
            dcDllToggle.IsOn = false;
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

            // Addon mode → Global
            if (_window.ViewModel.GetPerGameAddonMode(capturedName) != "Global")
            {
                _window.ViewModel.SetPerGameAddonMode(capturedName, "Global");
                _window.ViewModel.DeployAddonsForCard(capturedName);
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
            if (_window.ViewModel.IsUpdateAllExcludedDc(capturedName))
                _window.ViewModel.ToggleUpdateAllExclusionDc(capturedName);

            // Disable wiki exclusion
            if (_window.ViewModel.IsWikiExcluded(capturedName))
                _window.ViewModel.ToggleWikiExclusion(capturedName);

            // Reset bitness override to Auto
            bitnessCombo.SelectedItem = "Auto";
            _window.ViewModel.SetBitnessOverride(capturedName, null);

            // Reset API overrides
            apiCombo.SelectedItem = "Auto";
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

                    // Bitness changed — no need to update placeholder

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
