// DetailPanelBuilder.Overrides.cs — Main overrides panel (DLL names, Bitness, API).

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    private sealed class OverridesPanelCtx
    {
        public required GameCardViewModel Card;
        public required string GameName;
        public string CapturedName = null!;
        public required bool IsLumaMode;
        public required Grid BitnessPanel;
        public required ComboBox BitnessCombo;
        public required ComboBox ApiCombo;
        public required TextBox DetectedBox;
        public required TextBox WikiBox;
        public required ComboBox WikiExcludeCombo;
        public Action? ResetDllOverrides; // clears all DLL name overrides (replaces old ToggleSwitch reset path)
        public required string? OriginalStoreName;
        public ComboBox ChannelCombo = null!;
        public ComboBox ShaderModeCombo = null!;
        public bool ShaderComboInitializing;
        public TextBlock UpdateSummaryText = null!;
        public bool ChannelComboInitializing;
        public Button ResetOverridesBtn = null!;
        public Action? ResetAction; // stored separately so mgmt panel can call it directly (automation peer fails on collapsed buttons)
    }

    // Wiki lookup dropdown items (Game Overrides, under Wiki mod name)
    internal const string WikiIncludedItem = "Use wiki";
    internal const string WikiExcludedItem = "Ignore wiki";

    /// <summary>Plain grey look shared by the Game Overrides reset buttons (name, DLL names, launch options).</summary>
    internal static void ApplyResetButtonStyle(Button btn)
    {
        btn.Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush);
        btn.Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush);
        btn.BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush);
        btn.BorderThickness = new Thickness(1);
        btn.CornerRadius = new CornerRadius(8);
    }

    public void BuildOverridesPanel(GameCardViewModel card)
    {
        // Abort any in-progress drag before clearing the header row — prevents stale
        // _dragging = true from blocking pointer events on the rebuilt panel.
        if (_dragging) DragEnd(save: false);

        // Cancel any in-flight NVAPI background scans from the previous game.
        // Tasks waiting on _panelScanSemaphore will observe the cancellation and
        // bail out immediately, freeing their thread pool threads.
        if (_window.IsShuttingDown) return;
        _panelScanCts.Cancel();
        _panelScanCts = new CancellationTokenSource();

        _window.OverridesPanel.Children.Clear();
        _window.OverridesHeaderRow.Children.Clear();
        _window.OverridesPanel.Children.Add(MakeOverridesMovingNote());

        var gameName = card.GameName;
        bool isLumaMode = _window.ViewModel.IsLumaEnabled(gameName, card.Source ?? "");

        // ── Collapsible header ────────────────────────────────────────────────
        const string overridesSectionKey = "GameOverrides";
        bool ovCollapsed = Layout.IsCollapsed(overridesSectionKey);

        var ovArrow = new TextBlock
        {
            Text      = ovCollapsed ? "▶" : "▼",
            FontSize  = 10,
            Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
            VerticalAlignment = VerticalAlignment.Center,
            Margin    = new Thickness(0, 0, 6, 0),
        };
        var ovTitle = new TextBlock
        {
            Text       = "Game Overrides",
            FontSize   = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(ovArrow, SectionHeaderTooltip);
        ToolTipService.SetToolTip(ovTitle, SectionHeaderTooltip);
        _window.OverridesHeaderRow.Children.Add(MakeDragHandle(_window.OverridesContainer));
        _window.OverridesHeaderRow.Children.Add(ovArrow);
        _window.OverridesHeaderRow.Children.Add(ovTitle);
        Layout.RegisterHeader(overridesSectionKey, _window.OverridesHeaderRow);
        _window.OverridesPanel.Visibility = ovCollapsed ? Visibility.Collapsed : Visibility.Visible;

        // ── Collapsed summary ─────────────────────────────────────────────────
        var ovSummary = BuildGameOverridesSummary(card);
        if (ovSummary != null)
        {
            ovSummary.Visibility = ovCollapsed ? Visibility.Visible : Visibility.Collapsed;
            _window.OverridesHeaderRow.Children.Add(ovSummary);
        }

        // Unsubscribe previous handlers before adding new ones (OverridesHeaderRow is a
        // persistent XAML element — handlers stack up across rebuilds without this)
        if (_ovHeaderPressedHandler != null) _window.OverridesHeaderRow.PointerPressed -= _ovHeaderPressedHandler;
        if (_ovHeaderEnteredHandler != null) _window.OverridesHeaderRow.PointerEntered -= _ovHeaderEnteredHandler;
        if (_ovHeaderExitedHandler  != null) _window.OverridesHeaderRow.PointerExited  -= _ovHeaderExitedHandler;

        _ovHeaderEnteredHandler = (s, e) =>
        {
            ovTitle.Foreground = UIFactory.Brush(ResourceKeys.AccentTealBrush);
            var handCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
            DetailPanelBuilder.CursorProp
                ?.SetValue(_window.OverridesHeaderRow, handCursor);
        };
        _ovHeaderExitedHandler = (s, e) =>
        {
            ovTitle.Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush);
            var arrowCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Arrow);
            DetailPanelBuilder.CursorProp
                ?.SetValue(_window.OverridesHeaderRow, arrowCursor);
        };
        _ovHeaderPressedHandler = (s, e) =>
        {
            bool nowCollapsed = _window.OverridesPanel.Visibility == Visibility.Visible;
            _window.OverridesPanel.Visibility = nowCollapsed ? Visibility.Collapsed : Visibility.Visible;
            ovArrow.Text = nowCollapsed ? "▶" : "▼";
            if (nowCollapsed)
            {
                // Remove stale summary (index 3 = after drag handle + arrow + title)
                while (_window.OverridesHeaderRow.Children.Count > 3)
                    _window.OverridesHeaderRow.Children.RemoveAt(3);
                var rebuilt = BuildGameOverridesSummary(card);
                if (rebuilt != null)
                    _window.OverridesHeaderRow.Children.Add(rebuilt);
            }
            else
            {
                // Hide any summary at index 3
                if (_window.OverridesHeaderRow.Children.Count > 3
                    && _window.OverridesHeaderRow.Children[3] is TextBlock ovSum)
                    ovSum.Visibility = Visibility.Collapsed;
            }
            Layout.SetCollapsed(overridesSectionKey, nowCollapsed);
        };

        _window.OverridesHeaderRow.PointerEntered += _ovHeaderEnteredHandler;
        _window.OverridesHeaderRow.PointerExited  += _ovHeaderExitedHandler;
        _window.OverridesHeaderRow.PointerPressed += _ovHeaderPressedHandler;

        // ── Game name + Wiki name ────────────────────────────────────────────────
        var detectedBox = new TextBox
        {
            Header = "Game name",
            Text = gameName,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        ToolTipService.SetToolTip(detectedBox,
            "The display name for this game. Edit and press Enter to rename. Reset name reverts to the auto-detected store name.");
        var wikiBox = new TextBox
        {
            Header = "Wiki mod name",
            PlaceholderText = "Exact wiki name",
            Text = _window.ViewModel.GetUserNameMapping(gameName) ?? "",
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        ToolTipService.SetToolTip(wikiBox,
            "Override the name used to look up this game on the RenoDX/Luma wiki. Leave blank to use the game name. Press Enter to save.");
        var originalStoreName = _window.ViewModel.GetOriginalStoreName(gameName);

        // Mutable captured name so rename handler can update it for subsequent handlers
        var capturedName = gameName;

        var resetBtn = new Button
        {
            Content = "Reset",
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
            var resetName = GetOriginalGameName(capturedName);
            detectedBox.Text = resetName;
            wikiBox.Text = "";
            SetWikiName(capturedName, "");
            if (RenameGameTo(capturedName, resetName)) capturedName = resetName;
        };

        // ── DLL naming override (placed in Top Row right column) ───────────
        // Undo the 2.9.x bug that renamed a component's DLL to the "--------" placeholder
        if (_dllOverrideService.RepairPlaceholderNames(card)) card.NotifyAll();
        bool isDllOverride = _window.ViewModel.HasDllOverride(gameName);
        var existingCfg = _window.ViewModel.GetDllOverride(gameName);
        bool is32Bit = card.Is32Bit;
        var defaultRsName = is32Bit ? "ReShade32.dll" : "ReShade64.dll";

        const string DllDefaultSentinel = DllOverrideService.PlaceholderName;
        Button? resetDllBtn = null; // declared early so lambdas can capture it
        TextBlock? dllOverrideLabel = null; // declared early so UpdateAllDropdowns can refresh its tooltip
        string DllOverrideLabelTip(bool hasOverride) => hasOverride
            ? "Rename the ReShade, Display Commander and OptiScaler DLLs for this game."
            : "Rename the ReShade, Display Commander and OptiScaler DLLs for this game.\n"
              + "Reset DLL names is available once a name has been overridden.";
        bool _updatingDropdowns = false; // declared early so UpdateAllDropdowns can use it

        var existingRsName = existingCfg?.ReShadeFileName ?? "";
        var rsNames = DllOverrideConstants.CommonDllNames.Prepend(DllDefaultSentinel).ToArray();

        var rsNameBox = new ComboBox
        {
            Header = (object?)null,
            FontSize = 12,
            IsEnabled = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = rsNames,
            SelectedItem = DllDefaultSentinel, // default = no override
        };
        ToolTipService.SetToolTip(rsNameBox,
            "Override the ReShade DLL filename. Select a name to rename the installed DLL immediately. Select -------- to revert to the default name.");
        if (!string.IsNullOrEmpty(existingRsName))
        {
            if (rsNames.Contains(existingRsName, StringComparer.OrdinalIgnoreCase))
                rsNameBox.SelectedItem = rsNames.First(n => n.Equals(existingRsName, StringComparison.OrdinalIgnoreCase));
            else
            {
                var extendedRsNames = rsNames.Append(existingRsName).ToArray();
                rsNameBox.ItemsSource = extendedRsNames;
                rsNameBox.SelectedItem = existingRsName;
            }
        }
        // ── DC DLL naming override ─────────────────────────────────────────
        var existingDcName = existingCfg?.DcFileName ?? "";
        var dcNames = DcDllOverrideNames.Prepend(DllDefaultSentinel).ToArray();

        var dcNameBox = new ComboBox
        {
            FontSize = 12,
            IsEnabled = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = dcNames,
            SelectedItem = DllDefaultSentinel,
        };
        ToolTipService.SetToolTip(dcNameBox,
            "Override the Display Commander DLL file name. Choose -------- for the default name.");
        if (!string.IsNullOrEmpty(existingDcName))
        {
            if (dcNames.Contains(existingDcName, StringComparer.OrdinalIgnoreCase))
                dcNameBox.SelectedItem = dcNames.First(n => n.Equals(existingDcName, StringComparison.OrdinalIgnoreCase));
            else
            {
                var extendedDcNames = dcNames.Append(existingDcName).ToArray();
                dcNameBox.ItemsSource = extendedDcNames;
                dcNameBox.SelectedItem = existingDcName;
            }
        }

        // Track previous OS selection for revert
        // ── OptiScaler DLL naming override ─────────────────────────────────────
        var existingOsName = existingCfg?.OsFileName ?? "";
        // Do NOT fall back to GetEffectiveOsName — leave the combo at sentinel when no saved value
        var osBaseNames = _dllOverrideService
            .GetAvailableOsDllNames(gameName, is32Bit,
                rsInstalledAs: !string.IsNullOrEmpty(card.RsInstalledFile)
                    ? card.RsInstalledFile
                    : existingCfg?.ReShadeFileName,
                dcInstalledAs: !string.IsNullOrEmpty(card.DcInstalledFile)
                    ? card.DcInstalledFile
                    : existingCfg?.DcFileName);
        var availableOsNames = osBaseNames.Prepend(DllDefaultSentinel).ToArray();

        var osNameBox = new ComboBox
        {
            PlaceholderText = "Select OptiScaler DLL name",
            FontSize = 12,
            IsEnabled = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = availableOsNames,
            SelectedItem = DllDefaultSentinel,
        };
        ToolTipService.SetToolTip(osNameBox,
            $"Override the OptiScaler DLL file name. Choose -------- to go back to the default name ({OptiScalerService.GetDefaultDllName(card.GraphicsApi)}).\n"
            + "Names already used by ReShade or Display Commander for this game are left out.");
        if (!string.IsNullOrEmpty(existingOsName))
        {
            if (availableOsNames.Contains(existingOsName, StringComparer.OrdinalIgnoreCase))
                osNameBox.SelectedItem = availableOsNames.First(n => n.Equals(existingOsName, StringComparison.OrdinalIgnoreCase));
            else
            {
                var extendedOsNames = availableOsNames.Append(existingOsName).ToArray();
                osNameBox.ItemsSource = extendedOsNames;
                osNameBox.SelectedItem = existingOsName;
            }
        }

        // Track previous OS selection for revert
        string? _previousOsSelection = osNameBox.SelectedItem as string;

        // ── Auto-save: OS name box on dropdown selection ──────────────────────
        bool _osComboInitializing = true;  // guard against SelectionChanged firing during construction
        osNameBox.SelectionChanged += (s, e) =>
        {
            if (_osComboInitializing || _updatingDropdowns) return;
            var osName = osNameBox.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(osName)) return;
            // Shared with the OptiScaler ⚙ (DetailPanelBuilder.Overrides.DllShared.cs)
            if (!ApplyOsDllName(card, capturedName, osName))
            {
                _updatingDropdowns = true;
                osNameBox.SelectedItem = _previousOsSelection;
                _updatingDropdowns = false;
                return;
            }
            _previousOsSelection = osName;
            // Re-filter all combos so the newly selected OS name greys out in RS/DC
            UpdateAllDropdowns();
        };
        _osComboInitializing = false;

        // ── Cross-exclusion: filter out the other component's current name ───────

        // Rebuilds a combo's ItemsSource removing colliding names entirely.
        // Called after every selection change so all three combos stay in sync.
        void RebuildComboFiltered(ComboBox combo, string[] baseNames, IEnumerable<string> excludedNames)
        {
            var excluded = new HashSet<string>(excludedNames, StringComparer.OrdinalIgnoreCase);
            var current = GetComboValue(combo);
            var filtered = baseNames.Where(n => n == DllDefaultSentinel || !excluded.Contains(n)).ToArray();
            // If the current selection would be excluded (active collision), keep it visible but leave selected
            if (!string.IsNullOrEmpty(current) && current != DllDefaultSentinel
                && !filtered.Contains(current, StringComparer.OrdinalIgnoreCase))
                filtered = filtered.Append(current).ToArray(); // preserve current even if now excluded
            combo.ItemsSource = filtered;
            // Restore selection
            if (!string.IsNullOrEmpty(current))
            {
                var match = filtered.FirstOrDefault(n => string.Equals(n, current, StringComparison.OrdinalIgnoreCase));
                if (match != null) combo.SelectedItem = match;
            }
        }

        // Keep old signature for compatibility
        void RebuildComboWithDisabled(ComboBox combo, string[] baseNames, IEnumerable<string> disabledNames)
            => RebuildComboFiltered(combo, baseNames, disabledNames);

        void UpdateAllDropdowns()
        {
            if (_updatingDropdowns) return;
            _updatingDropdowns = true;
            try
            {
                string GetSelected(ComboBox c) =>
                    c.SelectedItem is ComboBoxItem ci ? (ci.Content as string ?? "") :
                    c.SelectedItem as string ?? "";

                var rsSelected = GetSelected(rsNameBox);
                var dcSelected = GetSelected(dcNameBox);
                var osSelected = GetSelected(osNameBox);

                bool rsIsReal = !string.IsNullOrEmpty(rsSelected) && rsSelected != DllDefaultSentinel;
                bool dcIsReal = !string.IsNullOrEmpty(dcSelected) && dcSelected != DllDefaultSentinel;
                bool osIsReal = !string.IsNullOrEmpty(osSelected) && osSelected != DllDefaultSentinel;

                // RS: grey out DC and OS selections, plus the currently-installed OS DLL name
                // (even when the OS combo shows -------- the physical file still occupies that slot)
                var rsDisabled = new List<string>();
                if (dcIsReal) rsDisabled.Add(dcSelected);
                if (osIsReal) rsDisabled.Add(osSelected);
                // Always grey the installed OS filename so RS can't claim a name already in use by OptiScaler
                var osPhysicalName = card.OsInstalledFile;
                if (!string.IsNullOrEmpty(osPhysicalName) && !rsDisabled.Contains(osPhysicalName, StringComparer.OrdinalIgnoreCase))
                    rsDisabled.Add(osPhysicalName);
                RebuildComboWithDisabled(rsNameBox, rsNames, rsDisabled);

                // DC: grey out RS and OS selections
                var dcDisabled = new List<string>();
                if (rsIsReal) dcDisabled.Add(rsSelected);
                if (osIsReal) dcDisabled.Add(osSelected);
                RebuildComboWithDisabled(dcNameBox, dcNames, dcDisabled);

                // OS: grey out RS and DC selections
                var osDisabled = new List<string>();
                if (rsIsReal) osDisabled.Add(rsSelected);
                if (dcIsReal) osDisabled.Add(dcSelected);
                RebuildComboWithDisabled(osNameBox, availableOsNames, osDisabled);

                // Enable Reset button when any override is active
                if (resetDllBtn != null)
                    resetDllBtn.IsEnabled = _window.ViewModel.HasDllOverride(gameName);
                if (dllOverrideLabel != null)
                    ToolTipService.SetToolTip(dllOverrideLabel, DllOverrideLabelTip(_window.ViewModel.HasDllOverride(gameName)));
            }
            finally { _updatingDropdowns = false; }
        }

        // Kept for compatibility — delegate to unified helper
        void UpdateDcDropdownItems() => UpdateAllDropdowns();

        // Helper — gets the string value from a combo that may contain ComboBoxItem or string
        static string GetComboValue(ComboBox c) => c.SelectedItem as string ?? "";

        // Initial filter — grey out colliding names across all three combos
        UpdateAllDropdowns();

        // ── Auto-save: DC name box on dropdown selection (with foreign DLL check) ──
        dcNameBox.SelectionChanged += async (s, e) =>
        {
            try
            {
                if (_updatingDropdowns) return; // fired by UpdateAllDropdowns — ignore
                if (dcNameBox.SelectedItem is not string dcName || string.IsNullOrWhiteSpace(dcName)) return;
                // Shared with the Display Commander ⚙ (DetailPanelBuilder.Overrides.DllShared.cs)
                if (!await ApplyDcDllNameAsync(card, capturedName, dcName))
                {
                    // Back to the saved name
                    var saved = _window.ViewModel.GetDllOverride(capturedName)?.DcFileName;
                    _updatingDropdowns = true;
                    dcNameBox.SelectedItem = string.IsNullOrEmpty(saved) ? DllDefaultSentinel
                        : (dcNameBox.ItemsSource as IEnumerable<string>)?.FirstOrDefault(n => n.Equals(saved, StringComparison.OrdinalIgnoreCase));
                    _updatingDropdowns = false;
                }
                UpdateAllDropdowns();
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[DetailPanelBuilder] DC DLL name change failed — {ex}");
            }
        };

        // ── Top Row Grid (3 columns: Star | Auto | Star) ─────────────────────
        var topRowGrid = new Grid { ColumnSpacing = 0 };
        topRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Left column: Game Name + Wiki Name side by side, then Reset + Wiki ComboBox below
        var topLeftColumn = new StackPanel { Spacing = 6 };

        // Row 1: Game name + Wiki name side by side
        var nameRow = new Grid { ColumnSpacing = 8 };
        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        nameRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(detectedBox, 0);
        Grid.SetColumn(wikiBox, 1);
        nameRow.Children.Add(detectedBox);
        nameRow.Children.Add(wikiBox);
        topLeftColumn.Children.Add(nameRow);

        // Row 2: Reset button (half) + Wiki lookup ComboBox (half)
        var resetWikiRow = new Grid { ColumnSpacing = 8 };
        resetWikiRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        resetWikiRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        resetBtn.Content = "Reset name";
        resetBtn.FontSize = 12;
        resetBtn.Height = 32;
        resetBtn.HorizontalAlignment = HorizontalAlignment.Stretch;
        resetBtn.VerticalAlignment = VerticalAlignment.Stretch;
        resetBtn.Padding = new Thickness(10, 6, 10, 6);
        ApplyResetButtonStyle(resetBtn);
        Grid.SetColumn(resetBtn, 0);
        resetWikiRow.Children.Add(resetBtn);

        // Wiki lookup ComboBox (replaces ToggleSwitch)
        var wikiExcludeItems = new[] { WikiIncludedItem, WikiExcludedItem };
        var wikiExcludeCombo = new ComboBox
        {
            ItemsSource = wikiExcludeItems,
            SelectedItem = GetWikiLookupDisplay(gameName),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        ToolTipService.SetToolTip(wikiExcludeCombo,
            "Use wiki: look this game up on the RenoDX and Luma wikis to find its mod.\nIgnore wiki: skip wiki lookups for this game.");
        wikiExcludeCombo.SelectionChanged += (s, ev) =>
        {
            if (wikiExcludeCombo.SelectedItem is string selected) SetWikiLookup(capturedName, selected);
        };
        Grid.SetColumn(wikiExcludeCombo, 1);
        resetWikiRow.Children.Add(wikiExcludeCombo);

        topLeftColumn.Children.Add(resetWikiRow);

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

        // Column 2: DLL naming override
        var topRightColumn = new StackPanel { Spacing = 6 };

        dllOverrideLabel = new TextBlock
        {
            Text = "DLL naming overrides",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
        };
        ToolTipService.SetToolTip(dllOverrideLabel, DllOverrideLabelTip(_window.ViewModel.HasDllOverride(gameName)));
        topRightColumn.Children.Add(dllOverrideLabel);

        // 3 DLL name boxes side by side, always visible
        var dllBoxesGrid = new Grid { ColumnSpacing = 8, RowSpacing = 4 };
        dllBoxesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dllBoxesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dllBoxesGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dllBoxesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        dllBoxesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Label row
        var rsLabel = new TextBlock { Text = "ReShade", FontSize = 11, Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush) };
        // "Load ReShade as ASI" (ReShade ⚙) fixes ReShade's name, so its DLL name can't be changed here
        if (_window.ViewModel.GetRsAsiMode(gameName, card.Source ?? ""))
        {
            rsNameBox.IsEnabled = false;
            ToolTipService.SetToolTip(rsLabel,
                $"ReShade is loaded as {AuxInstallService.RsAsiName(card.Is32Bit)} for this game. Turn off Load ReShade as ASI in ReShade's ⚙ to choose a DLL name.");
        }
        var dcLabel = new TextBlock { Text = "Display Commander", FontSize = 11, Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush) };
        var osLabel = new TextBlock { Text = "OptiScaler", FontSize = 11, Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush) };
        Grid.SetColumn(rsLabel, 0); Grid.SetRow(rsLabel, 0);
        Grid.SetColumn(dcLabel, 1); Grid.SetRow(dcLabel, 0);
        Grid.SetColumn(osLabel, 2); Grid.SetRow(osLabel, 0);
        dllBoxesGrid.Children.Add(rsLabel);
        dllBoxesGrid.Children.Add(dcLabel);
        dllBoxesGrid.Children.Add(osLabel);

        Grid.SetColumn(rsNameBox, 0); Grid.SetRow(rsNameBox, 1);
        Grid.SetColumn(dcNameBox, 1); Grid.SetRow(dcNameBox, 1);
        Grid.SetColumn(osNameBox, 2); Grid.SetRow(osNameBox, 1);
        dllBoxesGrid.Children.Add(rsNameBox);
        dllBoxesGrid.Children.Add(dcNameBox);
        dllBoxesGrid.Children.Add(osNameBox);
        topRightColumn.Children.Add(dllBoxesGrid);

        // Reset DLL Names button — reverts all three DLLs to defaults and clears the config
        resetDllBtn = new Button
        {
            Content = "Reset DLL names",
            FontSize = 12,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(10, 6, 10, 6),
            IsEnabled = _window.ViewModel.HasDllOverride(gameName),
        };
        ApplyResetButtonStyle(resetDllBtn);
        ToolTipService.SetToolTip(resetDllBtn,
            "Revert all DLL names back to defaults and clear saved overrides.");
        resetDllBtn.Click += async (s, e) =>
        {
            resetDllBtn.IsEnabled = false;
            CrashReporter.Log($"[DetailPanelBuilder] Reset DLL Names clicked for '{capturedName}'");

            // ── Step 1: Revert OptiScaler DLL FIRST (file I/O only, safe on background thread) ──
            await Task.Run(() =>
            {
                var osCfg = _dllOverrideService.GetDllOverride(capturedName)?.OsFileName;
                if (!string.IsNullOrEmpty(osCfg) && card.IsOsInstalled
                    && !string.IsNullOrEmpty(card.OsInstalledFile)
                    && !string.IsNullOrEmpty(card.InstallPath))
                {
                    var defaultOsName = OptiScalerService.GetDefaultDllName(card.GraphicsApi); // dxgi.dll, winmm.dll for Vulkan
                    var osOldPath = System.IO.Path.Combine(card.InstallPath, card.OsInstalledFile);
                    var osNewPath = System.IO.Path.Combine(card.InstallPath, defaultOsName);
                    try
                    {
                        if (!osOldPath.Equals(osNewPath, StringComparison.OrdinalIgnoreCase)
                            && System.IO.File.Exists(osOldPath))
                        {
                            // If dxgi.dll is occupied by ReShade, move RS out of the way first
                            if (System.IO.File.Exists(osNewPath)
                                && card.RsRecord != null
                                && System.IO.Path.GetFileName(osNewPath).Equals(card.RsRecord.InstalledAs, StringComparison.OrdinalIgnoreCase))
                            {
                                var rsCoexistPath = System.IO.Path.Combine(card.InstallPath, Services.OptiScalerService.ReShadeCoexistName);
                                if (!System.IO.File.Exists(rsCoexistPath))
                                {
                                    System.IO.File.Move(osNewPath, rsCoexistPath);
                                    card.RsRecord.InstalledAs = Services.OptiScalerService.ReShadeCoexistName;
                                    card.RsInstalledFile = Services.OptiScalerService.ReShadeCoexistName;
                                    var rsRec = _auxInstallService.FindRecord(capturedName, card.InstallPath, Services.AuxInstallService.TypeReShade)
                                             ?? _auxInstallService.FindRecord(capturedName, card.InstallPath, Services.AuxInstallService.TypeReShadeNormal);
                                    if (rsRec != null) { rsRec.InstalledAs = Services.OptiScalerService.ReShadeCoexistName; _auxInstallService.SaveAuxRecord(rsRec); }
                                    CrashReporter.Log($"[DetailPanelBuilder] Moved ReShade '{System.IO.Path.GetFileName(osNewPath)}' → '{Services.OptiScalerService.ReShadeCoexistName}' to free '{defaultOsName}' for OS revert");
                                }
                            }
                            if (!System.IO.File.Exists(osNewPath))
                            {
                                var prevOsName = card.OsInstalledFile ?? osCfg!;
                                System.IO.File.Move(osOldPath, osNewPath);
                                card.OsInstalledFile = defaultOsName;
                                var osRecord = _auxInstallService.FindRecord(capturedName, card.InstallPath, "OptiScaler");
                                if (osRecord != null) { osRecord.InstalledAs = defaultOsName; _auxInstallService.SaveAuxRecord(osRecord); }
                                CrashReporter.Log($"[DetailPanelBuilder] Reverted OptiScaler DLL '{osCfg}' → '{defaultOsName}' for '{capturedName}'");
                                RhiInstallManifest.UpdateInstalledAs(card.InstallPath, prevOsName, defaultOsName);
                            }
                        }
                    }
                    catch (Exception ex) { CrashReporter.Log($"[DetailPanelBuilder] Failed to revert OptiScaler DLL for '{capturedName}' — {ex.Message}"); }
                }
                _dllOverrideService.SetOsDllOverride(capturedName, "");

                // ── Step 2 is done on the UI thread (after this Task.Run) to avoid COMException from NotifyAll ──
            });

            // ── Step 2: Revert RS and DC — must run on UI thread so NotifyAll is safe ──
            var result = _window.ViewModel.DisableDllOverride(card);
            CrashReporter.Log($"[DetailPanelBuilder] Reset DLL complete for '{capturedName}' — HasDllOverride={_window.ViewModel.HasDllOverride(capturedName)}, rsReverted={result.RsReverted}");
            card.NotifyAll();

            // Rebuild the panel — re-reads HasDllOverride (now false) so all combos show --------
            _window.DispatcherQueue?.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => BuildOverridesPanel(card));
        };
        topRightColumn.Children.Add(resetDllBtn);

        Grid.SetColumn(topRightColumn, 2);
        topRowGrid.Children.Add(topRightColumn);

        _window.OverridesPanel.Children.Add(topRowGrid);
        _window.OverridesPanel.Children.Add(UIFactory.MakeSeparator());

        // ── Auto-save: Game name and wiki name on Enter (shared with the game settings ⚙ and the RenoDX ⚙) ──
        detectedBox.KeyDown += (s, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            var det = detectedBox.Text?.Trim() ?? "";
            if (RenameGameTo(capturedName, det)) capturedName = det;
        };

        wikiBox.KeyDown += (s, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter) SetWikiName(capturedName, wikiBox.Text ?? "");
        };

        // ── Per-game Shader mode ComboBox (logic shared with the ReShade ⚙: DetailPanelBuilder.Overrides.RsShared.cs) ──
        string effectiveShaderDisplay = GetShaderModeDisplay(card);
        bool shaderComboInitializing = true;

        var shaderModeCombo = new ComboBox
        {
            ItemsSource = ShaderModeItems,
            SelectedItem = effectiveShaderDisplay,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        ToolTipService.SetToolTip(shaderModeCombo,
            "Global = use global shader selection. Custom = use custom shader directories. Select = pick per-game packs. Off = no shaders.");

        // Allow re-opening the Select picker when already on Select
        shaderModeCombo.DropDownClosed += (s, ev) =>
        {
            if (shaderComboInitializing) return;
            var current = shaderModeCombo.SelectedItem as string;
            if (current == "Select" && _window.ViewModel.GetPerGameShaderMode(capturedName, card.Source) == "Select")
            {
                shaderComboInitializing = true;
                shaderModeCombo.SelectedItem = "Global";
                shaderComboInitializing = false;
                shaderModeCombo.SelectedItem = "Select";
            }
        };

        shaderModeCombo.SelectionChanged += async (s, ev) =>
        {
            try
            {
                if (shaderComboInitializing) return;
                var selected = shaderModeCombo.SelectedItem as string;
                if (string.IsNullOrEmpty(selected)) return;

                if (await ApplyShaderModeAsync(card, capturedName, selected))
                {
                    effectiveShaderDisplay = selected;
                }
                else
                {
                    // Cancelled — revert to the saved mode
                    shaderComboInitializing = true;
                    shaderModeCombo.SelectedItem = GetShaderModeDisplay(card);
                    shaderComboInitializing = false;
                }
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[DetailPanelBuilder.ShaderMode] Shader mode change failed — {ex}");
            }
        };
        shaderComboInitializing = false;

        // ── Auto-save: RS name box on dropdown selection (logic shared with the ReShade ⚙) ──
        rsNameBox.SelectionChanged += (s, e) =>
        {
            if (_updatingDropdowns) return; // fired by UpdateAllDropdowns — ignore
            var rsName = GetComboValue(rsNameBox);
            if (string.IsNullOrWhiteSpace(rsName)) return;
            if (ApplyRsDllName(card, capturedName, rsName))
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

        var defaultBitnessSelection = GetBitnessDisplay(card);

        var bitnessCombo = new ComboBox
        {
            ItemsSource = BitnessItems,
            SelectedItem = defaultBitnessSelection,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        ToolTipService.SetToolTip(bitnessCombo,
            "Override the auto-detected bitness for this game. Auto uses PE header detection. 32-bit or 64-bit forces the value.");

        // Shared with the game settings ⚙ (DetailPanelBuilder.Overrides.GameShared.cs)
        bitnessCombo.SelectionChanged += (s, e) =>
        {
            if (bitnessCombo.SelectedItem is string selected)
                ApplyBitnessOverride(card, capturedName, selected, rebuildNow: true);
        };

        var bitnessPanel = new Grid { ColumnSpacing = 12 };
        bitnessPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bitnessPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bitnessPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bitnessPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid.SetRow(bitnessLabel, 0); Grid.SetColumn(bitnessLabel, 0);
        Grid.SetRow(bitnessCombo, 1); Grid.SetColumn(bitnessCombo, 0);
        bitnessPanel.Children.Add(bitnessLabel);
        bitnessPanel.Children.Add(bitnessCombo);

        // ── API Override ComboBox (single selection, placed in left panel below bitness) ──────
        var apiLabel = new TextBlock
        {
            Text = "Graphics API",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
        };
        ToolTipService.SetToolTip(apiLabel,
            "Override the detected graphics API for this game.\n\n" +
            "Auto uses the auto-detected value from PE header scanning.\n" +
            "User overrides set here take precedence over manifest and auto-detected values.\n" +
            "Reset Overrides reverts to auto-detection.");

        var apiCombo = new ComboBox
        {
            ItemsSource = ApiItems,
            SelectedItem = GetApiDisplay(card),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        ToolTipService.SetToolTip(apiCombo,
            "Override the detected graphics API for this game.\nAuto uses PE header scanning. Reset Overrides reverts to auto-detection.");

        // Shared with the ReShade cog's Game overrides section (DetailPanelBuilder.Overrides.ApiShared.cs)
        apiCombo.SelectionChanged += (s, ev) =>
        {
            if (apiCombo.SelectedItem is string selected)
                ApplyApiOverride(card, capturedName, selected, rebuildNow: true);
        };

        // Add API dropdown to bitness panel (right column, side by side)
        Grid.SetRow(apiLabel, 0); Grid.SetColumn(apiLabel, 1);
        Grid.SetRow(apiCombo, 1); Grid.SetColumn(apiCombo, 1);
        bitnessPanel.Children.Add(apiLabel);
        bitnessPanel.Children.Add(apiCombo);

        var ctx = new OverridesPanelCtx
        {
            Card = card,
            GameName = gameName,
            CapturedName = capturedName,
            IsLumaMode = isLumaMode,
            BitnessPanel = bitnessPanel,
            BitnessCombo = bitnessCombo,
            ApiCombo = apiCombo,
            DetectedBox = detectedBox,
            WikiBox = wikiBox,
            WikiExcludeCombo = wikiExcludeCombo,
            OriginalStoreName = originalStoreName,
            ShaderModeCombo = shaderModeCombo,
            ShaderComboInitializing = shaderComboInitializing,
        };

        // Wire the reset delegate so the Reset Overrides action can clear DLL overrides too
        ctx.ResetDllOverrides = () =>
        {
            resetDllBtn.IsEnabled = false;
            rsNameBox.SelectedIndex = -1;
            dcNameBox.SelectedIndex = -1;
            osNameBox.SelectedIndex = -1;
            // The persist / file I/O is sync here (called from the reset action which is already on UI thread)
            _dllOverrideService.SetOsDllOverride(capturedName, "");
            _window.ViewModel.DisableDllOverride(card);
            card.NotifyAll();
        };

        BuildRsChannelSection(ctx);
        CrashReporter.Log($"[BuildOverridesPanel] RsChannel done: '{card.GameName}'");
        BuildShadersAddonsSection(ctx);
        CrashReporter.Log($"[BuildOverridesPanel] ShadersAddons done: '{card.GameName}'");
        // Tabs (mockup A): the same controls as grouped cards, arranged before they're shown
        if (Layout.IsTabs) Layout.ArrangeOverrides(_window.OverridesPanel);

        // Sync mutable state back from context
        capturedName = ctx.CapturedName;

        BuildNeuralRenderingSection(card);
        CrashReporter.Log($"[BuildOverridesPanel] NeuralRendering done: '{card.GameName}'");
        BuildNvidiaProfileSection(card, capturedName);
        CrashReporter.Log($"[BuildOverridesPanel] NvidiaProfileDlss done: '{card.GameName}'");
        BuildDriverProfileSection(card, capturedName);
        CrashReporter.Log($"[BuildOverridesPanel] NvidiaProfileDriver done: '{card.GameName}'");

        BuildManagementSection(card, capturedName, ctx);
        CrashReporter.Log($"[BuildOverridesPanel] Dxvk+Management done: '{card.GameName}'");

        BuildExperimentalSection(card);
        CrashReporter.Log($"[BuildOverridesPanel] Experimental done: '{card.GameName}'");

        // Extras last: its SummaryChanged refreshes every section's tile / glance summary
        BuildExtrasSection(card);
        CrashReporter.Log($"[BuildOverridesPanel] Extras done: '{card.GameName}'");
    }
}
