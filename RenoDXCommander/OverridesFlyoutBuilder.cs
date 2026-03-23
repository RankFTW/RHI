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

        // ── DLL naming override ──
        bool isDllOverride = ViewModel.HasDllOverride(gameName);
        var existingCfg = ViewModel.GetDllOverride(gameName);
        bool is32Bit = card.Is32Bit;
        var defaultRsName = is32Bit ? "ReShade32.dll" : "ReShade64.dll";

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
            "Override the filename ReShade is installed as. " +
            "When enabled, existing RS files are renamed to the custom filename.");
        var existingRsName = existingCfg?.ReShadeFileName ?? "";
        // Initial filter for RS name box
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
        dllOverrideToggle.Toggled += (s, ev) =>
        {
            rsNameBox.IsEnabled = dllOverrideToggle.IsOn;

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
                ViewModel.EnableDllOverride(targetCard, rsName, "");
            }
            else
            {
                ViewModel.DisableDllOverride(targetCard);
            }
        };

        // ── Inline row: [Global Shaders + Select btn | divider | DLL override + RS name] ──
        var modeGrid = new Grid { ColumnSpacing = 0 };
        modeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        modeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        modeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Left: shader toggle + select button
        var shaderColumn = new StackPanel { Spacing = 8 };
        shaderColumn.Children.Add(shaderToggle);
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

        // Right: DLL override toggle + RS name combo
        var dllColumn = new StackPanel { Spacing = 4 };
        dllColumn.Children.Add(dllOverrideToggle);
        dllColumn.Children.Add(rsNameBox);
        Grid.SetColumn(dllColumn, 2);

        modeGrid.Children.Add(shaderColumn);
        modeGrid.Children.Add(modeDivider);
        modeGrid.Children.Add(dllColumn);
        panel.Children.Add(modeGrid);
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
            var rsName = !string.IsNullOrWhiteSpace(rsText) ? rsText.Trim() : rsNameBox.PlaceholderText;
            ViewModel.UpdateDllOverrideNames(targetCard, rsName, "");
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
            ViewModel.UpdateDllOverrideNames(targetCard, rsName, "");
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
        toggleRow.Children.Add(rdxBorder);

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
            shaderToggle.IsOn = true;
            dllOverrideToggle.IsOn = false;
            rsToggle.IsOn = true;
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
            if (ViewModel.IsUpdateAllExcludedRenoDx(capturedName))
                ViewModel.ToggleUpdateAllExclusionRenoDx(capturedName);

            // Disable wiki exclusion
            if (ViewModel.IsWikiExcluded(capturedName))
                ViewModel.ToggleWikiExclusion(capturedName);

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

        flyout.ShowAt(anchor);
    }
}
