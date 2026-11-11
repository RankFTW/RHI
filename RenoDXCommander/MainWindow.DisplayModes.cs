// MainWindow.DisplayModes.cs -- Display modes on the game page: the mode switch (Classic+ header beside HDR / RES;
// Tabs and Spotlight header under the HDR / Resolution toggles, built by DetailLayoutHost.Header), the game menu's
// "Display mode" submenu, the HDR / RES buttons' state when a mode sets them, and the per-game dialog (what the game
// swaps, and its values per mode). Shown only while Settings › Display › Display modes is on.
// Data and file work: MainViewModel.DisplayModes.cs, Services/DisplayMode*.cs.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    private StackPanel? _dmClassicHost;
    private bool _dmSwitching;

    /// <summary>Items written to the NVIDIA profile: the DLSS / Driver sections are rebuilt after a switch that wrote one.</summary>
    private static readonly HashSet<string> DmDriverItems = new(StringComparer.OrdinalIgnoreCase)
    {
        DisplayModeItemKeys.DriverFpsCap, DisplayModeItemKeys.MfgMode, DisplayModeItemKeys.DlssSrPreset,
        DisplayModeItemKeys.DlssSrScale, DisplayModeItemKeys.RtxHdrPeak,
    };

    // ── HDR / RES header buttons ─────────────────────────────────────────────

    /// <summary>Colours and tooltips of the Classic header's HDR / RES buttons (the Tabs header copies them).</summary>
    internal void RefreshHeaderLaunchToggles(GameCardViewModel card)
    {
        var (hdrOn, hdrSource) = ResolveHdrButton(card);
        DetailHdrToggleBtn.Tag = card;
        DetailHdrToggleText.Text = "HDR";
        StyleClassicToggle(DetailHdrToggleBtn, DetailHdrToggleText, hdrOn);
        ToolTipService.SetToolTip(DetailHdrToggleBtn, DetailPanelBuilder.HdrToggleTooltip(hdrOn, hdrSource));

        if (FeatureFlags.ResolutionControl)
        {
            var (resOn, resSource) = ResolveResButton(card);
            DetailResToggleBtn.Visibility = Visibility.Visible;
            DetailResToggleBtn.Tag = card;
            DetailResToggleText.Text = "RES";
            StyleClassicToggle(DetailResToggleBtn, DetailResToggleText, resOn);
            ToolTipService.SetToolTip(DetailResToggleBtn, DetailPanelBuilder.ResToggleTooltip(resOn, resSource));
        }
        else
        {
            DetailResToggleBtn.Visibility = Visibility.Collapsed;
        }
    }

    private static void StyleClassicToggle(Button btn, TextBlock text, bool active)
    {
        btn.Background = UIFactory.Brush(active ? ResourceKeys.AccentPurpleBgBrush : ResourceKeys.SurfaceOverlayBrush);
        btn.BorderBrush = UIFactory.Brush(active ? ResourceKeys.AccentPurpleBorderBrush : ResourceKeys.BorderSubtleBrush);
        text.Foreground = UIFactory.Brush(active ? ResourceKeys.AccentPurpleBrush : ResourceKeys.ChipTextBrush);
    }

    /// <summary>HDR on launch for the game and where that comes from (its display mode, its own setting, or Settings).</summary>
    private (bool On, string Source) ResolveHdrButton(GameCardViewModel card)
    {
        if (ViewModel.DisplayModeOwnsHdr(card) && ViewModel.DisplayModeHdrOn(card) is bool modeOn)
            return (modeOn, $"set by its {ViewModel.ActiveDisplayModeName(card)} display mode");
        var ov = _gameNameService.HdrToggleOverrides.TryGetValue(card.GameName, out var v) ? v : null;
        return ov != null
            ? (string.Equals(ov, "On", StringComparison.OrdinalIgnoreCase), "set for this game")
            : (ViewModel.Settings.HdrAutoToggle, "following Settings › Display & HDR");
    }

    private (bool On, string Source) ResolveResButton(GameCardViewModel card)
    {
        if (ViewModel.DisplayModeOwnsResolution(card) && ViewModel.DisplayModeResolution(card) is { } mv)
        {
            var name = ViewModel.ActiveDisplayModeName(card);
            if (mv.Equals("Off", StringComparison.OrdinalIgnoreCase)) return (false, $"set by its {name} display mode");
            if (mv.Equals("On", StringComparison.OrdinalIgnoreCase)) return (true, $"set by its {name} display mode");
            var r = ResolutionToggleService.DisplayResolution.Parse(mv);
            return (true, $"set by its {name} display mode: {r?.Label ?? mv}");
        }
        var ov = _gameNameService.ResToggleOverrides.TryGetValue(card.GameName, out var v) ? v : null;
        return ov != null
            ? (string.Equals(ov, "On", StringComparison.OrdinalIgnoreCase), "set for this game")
            : (ViewModel.Settings.ResolutionAutoToggle, "following Settings › Display & HDR");
    }

    /// <summary>HDR button click while the game's display mode sets HDR: flips it for the current mode. False = not handled.</summary>
    private bool ToggleHdrForDisplayMode(GameCardViewModel card)
    {
        if (!ViewModel.DisplayModeOwnsHdr(card)) return false;
        bool on = ResolveHdrButton(card).On;
        ViewModel.SetDisplayModeLaunchValue(card, DisplayModeItemKeys.Hdr, on ? "Off" : "On");
        RefreshHeaderLaunchToggles(card);
        return true;
    }

    private bool ToggleResForDisplayMode(GameCardViewModel card)
    {
        if (!ViewModel.DisplayModeOwnsResolution(card)) return false;
        bool on = ResolveResButton(card).On;
        ViewModel.SetDisplayModeLaunchValue(card, DisplayModeItemKeys.Resolution, on ? "Off" : "On");
        RefreshHeaderLaunchToggles(card);
        return true;
    }

    // ── Mode switch ──────────────────────────────────────────────────────────

    /// <summary>
    /// Classic+ header: mode buttons + ⚙ at the end of the second row (after the mod file badge), built in C#
    /// (XAML constraint). Not in the first row: Launch, HDR, RES and the links already fill it, and with Ultra+ /
    /// UW Fix links the extra chips made the links run under the store / engine badges.
    /// </summary>
    internal void RefreshClassicDisplayModeSwitch(GameCardViewModel card)
    {
        try
        {
            if (_dmClassicHost == null)
            {
                if (DetailInstalledFileBadge.Parent is not Panel row) return;
                _dmClassicHost = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
                row.Children.Add(_dmClassicHost);
                // Modes added / renamed / switched (Settings card, game menu, Switch all): redraw the shown game's switch
                ViewModel.DisplayModesChanged += () => DispatcherQueue.TryEnqueue(() => RefreshDisplayModeUi());
            }
            _dmClassicHost.Children.Clear();
            if (!ViewModel.DisplayModesOn)
            {
                _dmClassicHost.Visibility = Visibility.Collapsed;
                return;
            }
            _dmClassicHost.Visibility = Visibility.Visible;
            // Divider between the mod badges and the mode switch
            _dmClassicHost.Children.Add(new Border { Width = 1, Height = 16, Background = UIFactory.Brush(ResourceKeys.BorderSubtleBrush), Margin = new Thickness(0, 0, 4, 0) });

            var modes = ViewModel.DisplayModeStore.Modes;
            var active = ViewModel.ActiveDisplayModeId(card);
            if (modes.Count <= 4)
            {
                foreach (var m in modes)
                {
                    bool on = m.Id.Equals(active, StringComparison.OrdinalIgnoreCase);
                    var text = new TextBlock { Text = m.Name, FontSize = 11 };
                    var b = MakeClassicChip(text);
                    StyleClassicToggle(b, text, on);
                    ToolTipService.SetToolTip(b, ModeButtonTooltip(card, m.Name, on));
                    string? id = on ? null : m.Id; // the active mode again = turn it off
                    b.Click += async (s, e) => await SwitchDisplayModeFromUiAsync(card, id);
                    _dmClassicHost.Children.Add(b);
                }
            }
            else
            {
                var text = new TextBlock { Text = ViewModel.ActiveDisplayModeName(card), FontSize = 11 };
                var b = MakeClassicChip(text);
                StyleClassicToggle(b, text, active != null);
                ToolTipService.SetToolTip(b, "Display mode. Click to switch.");
                b.Click += (s, e) => ShowDisplayModeMenu(card, b);
                _dmClassicHost.Children.Add(b);
            }
            var cogText = new TextBlock { Text = "⚙", FontSize = 11 };
            var cog = MakeClassicChip(cogText);
            StyleClassicToggle(cog, cogText, false);
            ToolTipService.SetToolTip(cog, "Display mode settings for this game: what it swaps and its values per mode");
            cog.Click += async (s, e) => await ShowDisplayModeGameDialogAsync(card);
            _dmClassicHost.Children.Add(cog);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            CrashReporter.Log($"[MainWindow.RefreshClassicDisplayModeSwitch] {ex.Message}");
        }
    }

    private static Button MakeClassicChip(TextBlock content) => new()
    {
        Content = content,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(6, 4, 6, 4), // compact: the Classic+ header row is already full
        MinWidth = 0,
        MinHeight = 0,
        FontSize = 11,
    };

    /// <summary>Tabs / Spotlight header row: one button per mode (purple = active) and a ⚙. Null when modes are off.</summary>
    internal Grid? BuildTabsDisplayModeSwitch(GameCardViewModel card, Action afterChange)
    {
        if (!ViewModel.DisplayModesOn) return null;
        var modes = ViewModel.DisplayModeStore.Modes;
        var active = ViewModel.ActiveDisplayModeId(card);
        var grid = new Grid { ColumnSpacing = 8 };

        void AddButton(Button b, bool star)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = star ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            Grid.SetColumn(b, grid.ColumnDefinitions.Count - 1);
            grid.Children.Add(b);
        }

        if (modes.Count <= 4)
        {
            foreach (var m in modes)
            {
                bool on = m.Id.Equals(active, StringComparison.OrdinalIgnoreCase);
                var b = new Button
                {
                    Content = new TextBlock { Text = m.Name, TextTrimming = TextTrimming.CharacterEllipsis },
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(8, 0, 8, 0),
                };
                TabsTheme.Apply(b, DetailLayoutHost.ToggleLook(on), height: 36, fontSize: 12);
                ToolTipService.SetToolTip(b, ModeButtonTooltip(card, m.Name, on));
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, $"{m.Name} display mode");
                string? id = on ? null : m.Id; // the active mode again = turn it off
                b.Click += async (s, e) => { await SwitchDisplayModeFromUiAsync(card, id); afterChange(); };
                AddButton(b, true);
            }
        }
        else
        {
            var b = new Button { Content = $"Display mode: {ViewModel.ActiveDisplayModeName(card)}", HorizontalAlignment = HorizontalAlignment.Stretch };
            TabsTheme.Apply(b, DetailLayoutHost.ToggleLook(active != null), height: 36, fontSize: 12);
            ToolTipService.SetToolTip(b, "Display mode. Click to switch.");
            b.Click += (s, e) => ShowDisplayModeMenu(card, b, afterChange);
            AddButton(b, true);
        }

        var cog = new Button { Width = 36, Padding = new Thickness(0), Content = TabsTheme.Icon("", 15) };
        TabsTheme.Apply(cog, TabsTheme.IconButton with { Fg = TabsTheme.TextSecondary }, height: 36);
        ToolTipService.SetToolTip(cog, "Display mode settings for this game: what it swaps and its values per mode");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(cog, "Display mode settings");
        cog.Click += async (s, e) => { await ShowDisplayModeGameDialogAsync(card); afterChange(); };
        AddButton(cog, false);
        return grid;
    }

    private string ModeButtonTooltip(GameCardViewModel card, string modeName, bool on)
    {
        var items = ViewModel.DisplayModeStore.ItemsFor(MainViewModel.DisplayModeKey(card))
            .Select(DisplayModeItems.Get).Where(i => i != null).Select(i => i!.Label).ToList();
        var what = items.Count == 0 ? "nothing yet (choose what to swap with the ⚙)" : string.Join(", ", items);
        return on
            ? $"{modeName} mode is active for this game.\nSwaps: {what}.\nClick to turn it off: the game goes back to your global settings."
            : $"Switch this game to {modeName} mode.\nSwaps: {what}.";
    }

    private void ShowDisplayModeMenu(GameCardViewModel card, FrameworkElement anchor, Action? afterChange = null)
    {
        var menu = new MenuFlyout();
        var active = ViewModel.ActiveDisplayModeId(card);
        var off = new ToggleMenuFlyoutItem { Text = "Off (global settings)", IsChecked = active == null };
        off.Click += async (s, e) => { await SwitchDisplayModeFromUiAsync(card, null); afterChange?.Invoke(); };
        menu.Items.Add(off);
        foreach (var m in ViewModel.DisplayModeStore.Modes)
        {
            var item = new ToggleMenuFlyoutItem { Text = m.Name, IsChecked = m.Id.Equals(active, StringComparison.OrdinalIgnoreCase) };
            var id = m.Id;
            item.Click += async (s, e) => { await SwitchDisplayModeFromUiAsync(card, id); afterChange?.Invoke(); };
            menu.Items.Add(item);
        }
        menu.ShowAt(anchor);
    }

    /// <summary>Switches the game (null = turn its mode off) and reports the result (status bar, controller toast). Refreshes the header.</summary>
    internal async Task SwitchDisplayModeFromUiAsync(GameCardViewModel card, string? modeId)
    {
        if (_dmSwitching) return;
        _dmSwitching = true;
        try
        {
            string? refusal = null;
            var result = await ViewModel.SwitchDisplayModeAsync(card, modeId, r => refusal = r);
            var message = refusal ?? result?.Summary;
            if (message != null)
            {
                ShowStatusBarMessage(message, TimeSpan.FromSeconds(10));
                if (_padHints?.Visibility == Visibility.Visible) ShowPadToast(message, 5000);
            }
            if (result == null) return;
            RefreshDisplayModeUi(card);
            if (result.Items.Any(i => i.Written && DmDriverItems.Contains(i.Item.Key))
                && ReferenceEquals(ViewModel.SelectedGame, card))
                _detailPanelBuilder.BuildOverridesPanel(card); // DLSS / Driver sections show the new values
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            CrashReporter.Log($"[MainWindow.SwitchDisplayModeFromUiAsync] '{card.GameName}' — {ex.Message}");
            ShowStatusBarMessage($"Couldn't switch display mode: {ex.Message}", TimeSpan.FromSeconds(8));
        }
        finally
        {
            _dmSwitching = false;
        }
    }

    /// <summary>Redraws the mode switch and HDR / RES buttons for the shown game.</summary>
    internal void RefreshDisplayModeUi(GameCardViewModel? card = null)
    {
        card ??= ViewModel.SelectedGame;
        if (card == null || !ReferenceEquals(ViewModel.SelectedGame, card)) return;
        RefreshHeaderLaunchToggles(card);
        RefreshClassicDisplayModeSwitch(card);
        _detailPanelBuilder.Layout.RefreshHeader();
    }

    /// <summary>The game menu's "Display mode" submenu (null while modes are off).</summary>
    private MenuFlyoutSubItem? BuildDisplayModeMenu(GameCardViewModel card)
    {
        if (!ViewModel.DisplayModesOn) return null;
        var sub = new MenuFlyoutSubItem { Text = "Display mode" };
        var active = ViewModel.ActiveDisplayModeId(card);
        var off = new ToggleMenuFlyoutItem { Text = "Off (global settings)", IsChecked = active == null };
        off.Click += async (s, e) =>
        {
            try { await SwitchDisplayModeFromUiAsync(card, null); }
            catch (Exception ex) when (ex is InvalidOperationException) { CrashReporter.Log($"[MainWindow.DisplayModeMenu] {ex.Message}"); }
        };
        sub.Items.Add(off);
        foreach (var m in ViewModel.DisplayModeStore.Modes)
        {
            var item = new ToggleMenuFlyoutItem { Text = m.Name, IsChecked = m.Id.Equals(active, StringComparison.OrdinalIgnoreCase) };
            var id = m.Id;
            item.Click += async (s, e) =>
            {
                try { await SwitchDisplayModeFromUiAsync(card, id); }
                catch (Exception ex) when (ex is InvalidOperationException) { CrashReporter.Log($"[MainWindow.DisplayModeMenu] {ex.Message}"); }
            };
            sub.Items.Add(item);
        }
        sub.Items.Add(new MenuFlyoutSeparator());
        var settings = new MenuFlyoutItem { Text = "Display mode settings..." };
        settings.Click += async (s, e) => await ShowDisplayModeGameDialogAsync(card);
        sub.Items.Add(settings);
        return sub;
    }

    // ── Per-game dialog ──────────────────────────────────────────────────────

    /// <summary>What the game swaps (default list or its own) and its value per mode.</summary>
    internal async Task ShowDisplayModeGameDialogAsync(GameCardViewModel card)
    {
        try
        {
            var vm = ViewModel;
            var store = vm.DisplayModeStore;
            var key = MainViewModel.DisplayModeKey(card);
            var items = DisplayModeItems.All;

            var ctx = vm.BuildDisplayModeContext(card);
            var reasons = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string?> inGame = new(StringComparer.OrdinalIgnoreCase);
            await Task.Run(() =>
            {
                foreach (var i in items) reasons[i.Key] = DisplayModeService.Unavailable(ctx, i);
                inGame = vm.DisplayModeService.ReadCurrent(ctx, items);
            });

            var cog = new CogDialog("Display Mode Settings", Content.XamlRoot);
            cog.AddIntro($"Display modes swap settings for {card.GameName} when you switch between them, e.g. Monitor and TV. " +
                         "The game remembers its own values per mode: when you switch away from a mode, what the game has then " +
                         "(including changes made in game) is kept for that mode. Off = no mode: the game uses your global settings.");
            if (!vm.DisplayModesOn)
                cog.AddWarning("Display modes are off. Turn them on in Settings › Display to show the switch on game pages.");
            if (card.IsRunning)
                cog.AddWarning("The game is running. Close it before switching modes.");

            // ── Mode ──
            cog.AddSection("Mode");
            var modes = store.Modes;
            var modeNames = modes.Select(m => m.Name).ToList();
            int modeIdx = modes.ToList().FindIndex(m => m.Id.Equals(store.ActiveModeId(key) ?? "", StringComparison.OrdinalIgnoreCase));
            int activeIdx = Math.Max(0, modeIdx); // for "Values for" (no Off there)
            StackPanel valuesPanel = new() { Spacing = 10 };
            ComboBox? valuesModeCombo = null;

            // Off first, then the modes
            cog.AddCombo("Current mode",
                "Switching writes this game's values for that mode. Pick the current mode again to re-apply its values. " +
                "Off writes your global settings back (and what the game had before modes, for settings without a global value).",
                new[] { "Off" }.Concat(modeNames).ToList(), modeIdx + 1, i =>
                {
                    if (!vm.DisplayModesOn) { cog.SetError("Turn display modes on in Settings › Display first."); return false; }
                    _ = SwitchFromDialogAsync(i == 0 ? null : modes[i - 1].Id);
                    return true;
                });

            async Task SwitchFromDialogAsync(string? id)
            {
                string? refusal = null;
                var result = await vm.SwitchDisplayModeAsync(card, id, r => refusal = r);
                if (refusal != null) { cog.SetError(refusal); return; }
                if (result == null) return;
                cog.SetSuccess(result.Summary);
                RefreshDisplayModeUi(card);
                // Values were remembered for the mode left: show them
                inGame = await Task.Run(() => vm.DisplayModeService.ReadCurrent(ctx, items));
                BuildValues();
            }

            // ── What it swaps ──
            cog.AddSection("What this game swaps");
            var checks = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);
            bool suppress = false;
            cog.AddCombo("List",
                "The default list is set in Settings › Display › Display modes. Pick Its own list to choose for this game only.",
                new[] { "Default list", "Its own list" }, store.UsesDefaultItems(key) ? 0 : 1, i =>
                {
                    vm.SetDisplayModeItems(card, i == 0 ? null : store.ItemsFor(key));
                    SyncChecks();
                    BuildValues();
                    return true;
                });

            var checkGrid = new Controls.AutoFitGrid { MinColumnWidth = 270 };
            foreach (var group in DisplayModeItems.Groups)
            {
                var head = TabsTheme.SectionHeading(group);
                head.FontSize = 11;
                head.Margin = new Thickness(0, 6, 0, 0);
                cog.AddContent(head);
                checkGrid = new Controls.AutoFitGrid { MinColumnWidth = 270 };
                foreach (var item in items.Where(i => i.Group == group))
                {
                    var reason = reasons.TryGetValue(item.Key, out var r) ? r : null;
                    var cb = new CheckBox
                    {
                        Content = reason == null ? item.Label : $"{item.Label} (not available)",
                        FontSize = 12,
                        MinWidth = 0,
                        Opacity = reason == null ? 1 : 0.6,
                    };
                    ToolTipService.SetToolTip(cb, reason == null ? item.Help : $"{item.Help}\n\nNot available for this game now: {reason}. It's used once that changes.");
                    var k = item.Key;
                    cb.Checked += (_, _) => OnCheck();
                    cb.Unchecked += (_, _) => OnCheck();
                    checks[k] = cb;
                    checkGrid.Children.Add(cb);
                }
                cog.AddContent(checkGrid);
            }

            void SyncChecks()
            {
                suppress = true;
                var list = store.ItemsFor(key);
                bool own = !store.UsesDefaultItems(key);
                foreach (var (k, cb) in checks)
                {
                    cb.IsChecked = list.Contains(k, StringComparer.OrdinalIgnoreCase);
                    cb.IsEnabled = own;
                }
                suppress = false;
            }

            void OnCheck()
            {
                if (suppress) return;
                var list = checks.Where(c => c.Value.IsChecked == true).Select(c => c.Key).ToList();
                vm.SetDisplayModeItems(card, list);
                BuildValues();
            }
            SyncChecks();

            // ── Values ──
            cog.AddSection("Values");
            cog.AddNote("Empty = the mode's default from Settings › Display › Display modes, else the game's normal value (your global setting, " +
                        "or what it had before display modes). Values for the current mode are written to the game as soon as you set them.");
            valuesModeCombo = cog.AddCombo("Values for", null, modeNames, activeIdx, i => { BuildValues(); return true; });
            cog.AddContent(valuesPanel);

            void BuildValues()
            {
                valuesPanel.Children.Clear();
                int mi = valuesModeCombo?.SelectedIndex ?? 0;
                if (mi < 0 || mi >= modes.Count) mi = 0;
                var mode = modes[mi];
                bool isActive = mode.Id.Equals(store.ActiveModeId(key) ?? "", StringComparison.OrdinalIgnoreCase);
                var swapped = store.ItemsFor(key).Select(DisplayModeItems.Get).Where(i => i != null).Cast<DisplayModeItem>()
                    .OrderBy(i => items.ToList().IndexOf(i)).ToList();
                if (swapped.Count == 0)
                {
                    valuesPanel.Children.Add(MutedLine("Tick what this game swaps above."));
                    return;
                }
                foreach (var item in swapped)
                {
                    var def = store.ModeDefault(mode.Id, item.Key);
                    var unset = def != null ? $"Mode default ({item.Format(def)})"
                        : item.AtLaunch ? "Not set (the game's own setting)" : "Not set (normal value)";
                    var editor = DisplayModeEditor.Build(item, store.GameValue(key, mode.Id, item.Key), unset,
                        v => vm.SetDisplayModeGameValueAsync(card, mode.Id, item, v),
                        (text, error) => { if (error) cog.SetError(text); else cog.SetSuccess(text); });
                    var notes = new List<string>();
                    if (item.AtLaunch) notes.Add("Used when RHI launches the game");
                    else if (reasons.TryGetValue(item.Key, out var reason) && reason != null) notes.Add($"Not available now: {reason}");
                    else if (isActive && inGame.TryGetValue(item.Key, out var now)) notes.Add($"In the game now: {item.Format(now)}");
                    valuesPanel.Children.Add(MakeValueRow(item.Label, editor, string.Join(". ", notes), item.Help));
                }
            }
            BuildValues();

            await cog.ShowAsync();
            RefreshDisplayModeUi(card);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            CrashReporter.Log($"[MainWindow.ShowDisplayModeGameDialogAsync] '{card.GameName}' — {ex.Message}");
        }
    }

    /// <summary>A row like CogDialog.AddRow, for rows rebuilt inside a panel.</summary>
    internal static FrameworkElement MakeValueRow(string label, FrameworkElement control, string? note, string? tooltip)
    {
        var row = new StackPanel { Spacing = 3 };
        var grid = new Grid { ColumnSpacing = CogDialog.ColumnGap };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CogDialog.LabelColumnWidth) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var lbl = new TextBlock
        {
            Text = label,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
        };
        if (!string.IsNullOrEmpty(tooltip)) ToolTipService.SetToolTip(lbl, tooltip);
        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        control.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(lbl);
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        row.Children.Add(grid);
        if (!string.IsNullOrWhiteSpace(note))
            row.Children.Add(new TextBlock
            {
                Text = note,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(CogDialog.LabelColumnWidth + CogDialog.ColumnGap, 0, 0, 2),
                Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
            });
        return row;
    }

    private static TextBlock MutedLine(string text) => new()
    {
        Text = text,
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
    };
}
