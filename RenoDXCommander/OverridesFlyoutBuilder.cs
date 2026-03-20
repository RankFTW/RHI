using Microsoft.UI;
using Microsoft.UI.Dispatching;
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
    private readonly DispatcherQueue _dispatcherQueue;

    public OverridesFlyoutBuilder(MainWindow window)
    {
        _window = window;
        _dispatcherQueue = window.DispatcherQueue;
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

        // ── DC Mode + Shader Mode (side by side) ──
        string? currentDcMode = ViewModel.GetPerGameDcModeOverride(gameName);
        var globalDcLabel = ViewModel.DcModeEnabled ? $"On — {ViewModel.DcDllFileName}" : "Off";
        var dcModeOptions = new[] { $"Global ({globalDcLabel})", "Off", "Custom" };
        var dcModeCombo = new ComboBox
        {
            ItemsSource = dcModeOptions,
            SelectedIndex = currentDcMode switch { "Off" => 1, "Custom" => 2, _ => 0 },
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Header = "DC Mode",
        };
        ToolTipService.SetToolTip(dcModeCombo,
            "Global = use the Settings DC Mode. Off = always use normal naming. " +
            "Custom = use a custom DLL filename.");

        // Subscribe to DcModeEnabled/DcDllFileName changes to keep the "Global (...)" label current
        System.ComponentModel.PropertyChangedEventHandler dcModeLevelHandler = (sender, e) =>
        {
            if (e.PropertyName != "DcModeEnabled" && e.PropertyName != "DcDllFileName") return;
            _dispatcherQueue.TryEnqueue(() =>
            {
                var updatedLabel = ViewModel.DcModeEnabled ? $"On — {ViewModel.DcDllFileName}" : "Off";
                var updatedOptions = new[] { $"Global ({updatedLabel})", "Off", "Custom" };
                var savedIndex = dcModeCombo.SelectedIndex;
                dcModeCombo.ItemsSource = updatedOptions;
                dcModeCombo.SelectedIndex = savedIndex;
            });
        };
        ViewModel.PropertyChanged += dcModeLevelHandler;

        // ── Auto-save: DC Mode on selection change ──
        dcModeCombo.SelectionChanged += (s, e) =>
        {
            string? newDcMode = dcModeCombo.SelectedIndex switch { 1 => "Off", 2 => "Custom", _ => (string?)null };
            var currentOverride = ViewModel.GetPerGameDcModeOverride(capturedName);
            if (newDcMode != currentOverride)
            {
                var previousDcMode = currentOverride;
                ViewModel.SetPerGameDcModeOverride(capturedName, newDcMode);
                ViewModel.ApplyDcModeSwitchForCard(capturedName, previousDcMode);
            }
        };

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

        var modeGrid = new Grid { ColumnSpacing = 8 };
        modeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        modeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(dcModeCombo, 0);
        Grid.SetColumn(shaderToggle, 1);
        modeGrid.Children.Add(dcModeCombo);
        modeGrid.Children.Add(shaderToggle);
        panel.Children.Add(modeGrid);
        panel.Children.Add(selectShadersBtn);
        panel.Children.Add(UIFactory.MakeSeparator());

        // ── DLL naming override ──
        bool isDllOverride = ViewModel.HasDllOverride(gameName);
        var existingCfg = ViewModel.GetDllOverride(gameName);
        bool is32Bit = card.Is32Bit;
        var defaultRsName = is32Bit ? "ReShade32.dll" : "ReShade64.dll";
        var defaultDcName = is32Bit ? "zzz_display_commander.addon32" : "zzz_display_commander.addon64";

        var dllOverrideToggle = new ToggleSwitch
        {
            Header = "DLL naming override",
            IsOn = isDllOverride,
            IsEnabled = !isLumaMode,
            OnContent = "Custom filenames enabled",
            OffContent = "Using default filenames",
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            FontSize = 12,
        };
        ToolTipService.SetToolTip(dllOverrideToggle,
            "Override the filenames ReShade and Display Commander are installed as. " +
            "When enabled, existing RS/DC files are renamed to the custom filenames. " +
            "The game is automatically excluded from DC Mode and Update All.");
        var existingRsName = existingCfg?.ReShadeFileName ?? "";
        var existingDcName = existingCfg?.DcFileName ?? "";
        // Helper: rebuild one ComboBox's ItemsSource excluding the name chosen in the other box
        bool _updatingDllItems = false;
        void SyncDllNameItems(ComboBox box, ComboBox otherBox)
        {
            if (_updatingDllItems) return;
            _updatingDllItems = true;
            var otherSelected = (otherBox.SelectedItem as string ?? otherBox.Text ?? "").Trim();
            var filtered = string.IsNullOrWhiteSpace(otherSelected)
                ? DllOverrideConstants.CommonDllNames
                : DllOverrideConstants.CommonDllNames.Where(n => !n.Equals(otherSelected, StringComparison.OrdinalIgnoreCase)).ToArray();
            var currentSel = box.SelectedItem as string ?? box.Text;
            box.ItemsSource = filtered;
            if (!string.IsNullOrWhiteSpace(currentSel))
            {
                var match = filtered.FirstOrDefault(n => n.Equals(currentSel.Trim(), StringComparison.OrdinalIgnoreCase));
                if (match != null) box.SelectedItem = match;
                else box.Text = currentSel;
            }
            _updatingDllItems = false;
        }

        var rsNameBox = new ComboBox
        {
            IsEditable = true,
            PlaceholderText = defaultRsName,
            Header = "ReShade filename",
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
        var dcNameBox = new ComboBox
        {
            IsEditable = true,
            PlaceholderText = defaultDcName,
            Header = "DC filename",
            FontSize = 12,
            IsEnabled = isDllOverride,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = DllOverrideConstants.CommonDllNames,
        };
        if (!string.IsNullOrEmpty(existingDcName))
        {
            if (DllOverrideConstants.CommonDllNames.Contains(existingDcName, StringComparer.OrdinalIgnoreCase))
                dcNameBox.SelectedItem = DllOverrideConstants.CommonDllNames.First(n => n.Equals(existingDcName, StringComparison.OrdinalIgnoreCase));
            else
            {
                var capturedDc = existingDcName;
                dcNameBox.Loaded += (s, e) => dcNameBox.Text = capturedDc;
            }
        }
        // Initial cross-filter so each box hides the other's current selection
        SyncDllNameItems(dcNameBox, rsNameBox);
        SyncDllNameItems(rsNameBox, dcNameBox);
        dllOverrideToggle.Toggled += (s, ev) =>
        {
            rsNameBox.IsEnabled = dllOverrideToggle.IsOn;
            dcNameBox.IsEnabled = dllOverrideToggle.IsOn;

            // Auto-save: persist DLL override state immediately
            bool nowOn = dllOverrideToggle.IsOn;
            bool wasOn = ViewModel.HasDllOverride(capturedName);
            if (nowOn == wasOn) return;
            var targetCard = ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard == null) return;
            if (nowOn)
            {
                var rsText = rsNameBox.SelectedItem as string ?? rsNameBox.Text;
                var rsName = !string.IsNullOrWhiteSpace(rsText) ? rsText.Trim() : rsNameBox.PlaceholderText;
                var dcText = dcNameBox.SelectedItem as string ?? dcNameBox.Text;
                var dcName = !string.IsNullOrWhiteSpace(dcText) ? dcText.Trim() : dcNameBox.PlaceholderText;
                ViewModel.EnableDllOverride(targetCard, rsName, dcName);
            }
            else
            {
                ViewModel.DisableDllOverride(targetCard);
            }
        };

        var dllNameGrid = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        dllNameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dllNameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(rsNameBox, 0);
        Grid.SetColumn(dcNameBox, 1);
        dllNameGrid.Children.Add(rsNameBox);
        dllNameGrid.Children.Add(dcNameBox);

        var dllGroupPanel = new StackPanel { Spacing = 4 };
        dllGroupPanel.Children.Add(dllOverrideToggle);
        dllGroupPanel.Children.Add(dllNameGrid);
        var dllGroupBorder = new Border
        {
            Child = dllGroupPanel,
            Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.BorderSubtleBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 12),
        };
        panel.Children.Add(dllGroupBorder);
        panel.Children.Add(UIFactory.MakeSeparator());

        // ── Auto-save: RS/DC name boxes on Enter ──
        rsNameBox.KeyDown += (s, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            if (!dllOverrideToggle.IsOn) return;
            var targetCard = ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard == null) return;
            var rsText = rsNameBox.SelectedItem as string ?? rsNameBox.Text;
            var rsName = !string.IsNullOrWhiteSpace(rsText) ? rsText.Trim() : rsNameBox.PlaceholderText;
            var dcText = dcNameBox.SelectedItem as string ?? dcNameBox.Text;
            var dcName = !string.IsNullOrWhiteSpace(dcText) ? dcText.Trim() : dcNameBox.PlaceholderText;
            if (rsName.Equals(dcName, StringComparison.OrdinalIgnoreCase)) return;
            ViewModel.UpdateDllOverrideNames(targetCard, rsName, dcName);
            SyncDllNameItems(dcNameBox, rsNameBox);
        };
        dcNameBox.KeyDown += (s, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            if (!dllOverrideToggle.IsOn) return;
            var targetCard = ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard == null) return;
            var rsText = rsNameBox.SelectedItem as string ?? rsNameBox.Text;
            var rsName = !string.IsNullOrWhiteSpace(rsText) ? rsText.Trim() : rsNameBox.PlaceholderText;
            var dcText = dcNameBox.SelectedItem as string ?? dcNameBox.Text;
            var dcName = !string.IsNullOrWhiteSpace(dcText) ? dcText.Trim() : dcNameBox.PlaceholderText;
            if (rsName.Equals(dcName, StringComparison.OrdinalIgnoreCase)) return;
            ViewModel.UpdateDllOverrideNames(targetCard, rsName, dcName);
            SyncDllNameItems(rsNameBox, dcNameBox);
        };
        // ── Auto-save: RS/DC name boxes on dropdown selection ──
        rsNameBox.SelectionChanged += (s, e) =>
        {
            if (_updatingDllItems) return;
            if (!dllOverrideToggle.IsOn) return;
            var targetCard = ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard == null) return;
            var rsName = rsNameBox.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(rsName)) return;
            var dcText = dcNameBox.SelectedItem as string ?? dcNameBox.Text;
            var dcName = !string.IsNullOrWhiteSpace(dcText) ? dcText.Trim() : dcNameBox.PlaceholderText;
            if (rsName.Equals(dcName, StringComparison.OrdinalIgnoreCase)) return;
            ViewModel.UpdateDllOverrideNames(targetCard, rsName, dcName);
            SyncDllNameItems(dcNameBox, rsNameBox);
        };
        dcNameBox.SelectionChanged += (s, e) =>
        {
            if (_updatingDllItems) return;
            if (!dllOverrideToggle.IsOn) return;
            var targetCard = ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
            if (targetCard == null) return;
            var rsText = rsNameBox.SelectedItem as string ?? rsNameBox.Text;
            var rsName = !string.IsNullOrWhiteSpace(rsText) ? rsText.Trim() : rsNameBox.PlaceholderText;
            var dcName = dcNameBox.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(dcName)) return;
            if (rsName.Equals(dcName, StringComparison.OrdinalIgnoreCase)) return;
            ViewModel.UpdateDllOverrideNames(targetCard, rsName, dcName);
            SyncDllNameItems(rsNameBox, dcNameBox);
        };

        // ── Global update inclusion + Wiki exclusion (inline row) ──
        var rsToggle = new ToggleSwitch
        {
            Header = "ReShade",
            IsOn = !ViewModel.IsUpdateAllExcludedReShade(gameName),
            OnContent = "Yes",
            OffContent = "No",
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            FontSize = 11,
            MinWidth = 0,
        };
        var dcToggle = new ToggleSwitch
        {
            Header = "DC",
            IsOn = !ViewModel.IsUpdateAllExcludedDc(gameName),
            OnContent = "Yes",
            OffContent = "No",
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            FontSize = 11,
            MinWidth = 0,
        };
        var rdxToggle = new ToggleSwitch
        {
            Header = "RenoDX",
            IsOn = !ViewModel.IsUpdateAllExcludedRenoDx(gameName),
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
        var dcBorder = new Border
        {
            Child = dcToggle,
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

        var toggleRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
        };
        toggleRow.Children.Add(rsBorder);
        toggleRow.Children.Add(dcBorder);
        toggleRow.Children.Add(rdxBorder);

        // ── Auto-save: Update inclusion toggles ──
        rsToggle.Toggled += (s, ev) =>
        {
            if (!rsToggle.IsOn != ViewModel.IsUpdateAllExcludedReShade(capturedName))
                ViewModel.ToggleUpdateAllExclusionReShade(capturedName);
        };
        dcToggle.Toggled += (s, ev) =>
        {
            if (!dcToggle.IsOn != ViewModel.IsUpdateAllExcludedDc(capturedName))
                ViewModel.ToggleUpdateAllExclusionDc(capturedName);
        };
        rdxToggle.Toggled += (s, ev) =>
        {
            if (!rdxToggle.IsOn != ViewModel.IsUpdateAllExcludedRenoDx(capturedName))
                ViewModel.ToggleUpdateAllExclusionRenoDx(capturedName);
        };

        var wikiExcludeToggle = new ToggleSwitch
        {
            Header = "Wiki exclusion",
            IsOn = ViewModel.IsWikiExcluded(gameName),
            OnContent = "Excluded from wiki lookups",
            OffContent = "Included in wiki lookups",
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            FontSize = 12,
        };
        ToolTipService.SetToolTip(wikiExcludeToggle,
            "When enabled, this game will not be looked up on the RenoDX wiki. " +
            "Useful for games that share a name with an unrelated wiki entry.");

        // ── Auto-save: Wiki exclusion toggle ──
        wikiExcludeToggle.Toggled += (s, ev) =>
        {
            if (wikiExcludeToggle.IsOn != ViewModel.IsWikiExcluded(capturedName))
                ViewModel.ToggleWikiExclusion(capturedName);
        };

        // Build inline row Grid: [Global update inclusion | divider | Wiki exclusion]
        var inlineRowGrid = new Grid();
        inlineRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inlineRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inlineRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

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

        // Column 1: Vertical divider
        var divider = new Border
        {
            Width = 1,
            Background = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(12, 0, 12, 0),
        };
        Grid.SetColumn(divider, 1);

        // Column 2: Wiki exclusion
        var rightSection = new StackPanel { Spacing = 0 };
        rightSection.Children.Add(wikiExcludeToggle);
        Grid.SetColumn(rightSection, 2);

        inlineRowGrid.Children.Add(leftSection);
        inlineRowGrid.Children.Add(divider);
        inlineRowGrid.Children.Add(rightSection);

        panel.Children.Add(inlineRowGrid);
        panel.Children.Add(UIFactory.MakeSeparator());

        // ── Reset Overrides button ──
        var resetOverridesBtn = new Button
        {
            Content = "Reset Overrides",
            FontSize = 12,
            Padding = new Thickness(16, 8, 16, 8),
            Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 8, 0, 0),
        };
        resetOverridesBtn.Click += (s, ev) =>
        {
            // Reset all controls to defaults
            gameNameBox.Text = originalStoreName ?? gameName;
            wikiNameBox.Text = "";
            dcModeCombo.SelectedIndex = 0;
            shaderToggle.IsOn = true;
            dllOverrideToggle.IsOn = false;
            rsToggle.IsOn = true;
            dcToggle.IsOn = true;
            rdxToggle.IsOn = true;
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

            // DC mode → Global (null)
            if (ViewModel.GetPerGameDcModeOverride(capturedName) != null)
            {
                var prev = ViewModel.GetPerGameDcModeOverride(capturedName);
                ViewModel.SetPerGameDcModeOverride(capturedName, null);
                ViewModel.ApplyDcModeSwitchForCard(capturedName, prev);
            }

            // Shader mode → Global
            if (ViewModel.GetPerGameShaderMode(capturedName) != "Global")
            {
                ViewModel.SetPerGameShaderMode(capturedName, "Global");
                ViewModel.GameNameServiceInstance.PerGameShaderSelection.Remove(capturedName);
                ViewModel.DeployShadersForCard(capturedName);
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
            if (ViewModel.IsUpdateAllExcludedDc(capturedName))
                ViewModel.ToggleUpdateAllExclusionDc(capturedName);
            if (ViewModel.IsUpdateAllExcludedRenoDx(capturedName))
                ViewModel.ToggleUpdateAllExclusionRenoDx(capturedName);

            // Disable wiki exclusion
            if (ViewModel.IsWikiExcluded(capturedName))
                ViewModel.ToggleWikiExclusion(capturedName);

            CrashReporter.Log($"[OverridesFlyoutBuilder.OpenOverridesFlyout] Overrides reset for: {capturedName}");

            // Only reselect/NotifyAll/RebuildCardGrid if game name actually changed
            if (nameChanged)
            {
                _window.RequestReselect(capturedName);
                card.NotifyAll();
                if (ViewModel.IsGridLayout)
                    _window.RebuildCardGrid();
            }
        };
        panel.Children.Add(resetOverridesBtn);

        // Wrap in a ScrollViewer for long content
        var scrollViewer = new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 800,
        };

        var flyoutStyle = new Style(typeof(FlyoutPresenter));
        flyoutStyle.Setters.Add(new Setter(FlyoutPresenter.MaxWidthProperty, 640));
        flyoutStyle.Setters.Add(new Setter(FlyoutPresenter.MaxHeightProperty, 800));

        var flyout = new Flyout
        {
            Content = scrollViewer,
            Placement = FlyoutPlacementMode.BottomEdgeAlignedRight,
            FlyoutPresenterStyle = flyoutStyle,
        };

        // On flyout closed, clean up DcModeLevel subscription
        flyout.Closed += (s, ev) =>
        {
            // Unsubscribe from DcModeLevel changes to avoid leaked subscriptions
            ViewModel.PropertyChanged -= dcModeLevelHandler;
        };

        flyout.ShowAt(anchor);
    }
}
