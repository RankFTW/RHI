// OverlayWindow.UltraPlus.cs -- Ultra+ settings in the RHI overlay (unlock_extra.txt only). "Ultra+ settings" in the
// This game box switches the overlay into Ultra+ mode: the window shrinks to a panel on the right of the game's
// monitor (the rest of the screen is the game, unpaused, so changes are seen live) and its content becomes the
// Ultra+ panel. Back / Esc returns to the full dashboard. Values are written to UltraPlusConfig.ini as they change;
// Ultra+ reloads them live with AutoApplyConfig=on. Files: Services/UltraPlus. Plan: docs/ultraplus-overlay-plan.md.
// Mouse and keyboard only for now (the game also reads the controller while the panel has focus).
using System.Globalization;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

using RenoDXCommander.Services;
using RenoDXCommander.Services.UltraPlus;

namespace RenoDXCommander.Overlay;

internal sealed partial class OverlayWindow
{
    private const double UpPanelWidth = 560; // layout units
    private const string UpAll = "All", UpChanged = "Changed", UpOther = "Other";

    private Grid _dashRoot = null!;
    private Grid? _upRoot;
    private bool _upMode;
    private string? _upReopenFor;             // closed while in Ultra+ mode: reopen in it for the same game
    private RectInt32 _monitorRect;
    private UltraPlusState? _up;
    private readonly Dictionary<string, string> _upValues = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, UpRow> _upRows = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _upCategoryOf = new(StringComparer.OrdinalIgnoreCase);
    private List<string> _upCategoryOrder = new();
    private string _upCategory = UpAll;
    private string _upSearch = "";
    private UpPrefs _upPrefs = UpPrefs.Load();
    private StackPanel _upList = null!;
    private ScrollViewer _upScroller = null!;
    private ComboBox _upCategories = null!;
    private TextBlock _upStatus = null!;
    private Button _upUndo = null!, _upDeletePreset = null!;
    private TextBlock _upInfo = null!;
    private ComboBox _upPresets = null!;
    private Grid _upSaveRow = null!;
    private TextBox _upSaveName = null!;
    private Dictionary<string, string>? _upUndoValues;
    private string? _upLastCustomPreset;
    private FileSystemWatcher? _upWatcher;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _upReloadTimer;
    private bool _upSilent;                   // setting control values from code: don't write
    private int _upWriteSeq;

    /// <summary>One setting's row: its controls, for live updates.</summary>
    private sealed class UpRow
    {
        public UltraPlusSetting Setting = null!;
        public Border Root = null!;
        public ComboBox? Combo;
        public Slider? Slider;
        public TextBlock? SliderValue;
        public CheckBox? GameDefault;
        public TextBlock Changed = null!, Cost = null!, State = null!, Description = null!;
        public Button Reset = null!;
        public Microsoft.UI.Dispatching.DispatcherQueueTimer? Debounce;
    }

    /// <summary>The panel's own view choices, kept in %LocalAppData%\RHI\ultraplus\ui.json.</summary>
    private sealed class UpPrefs
    {
        public bool ShowDescriptions { get; set; }
        /// <summary>Number settings as a slider (false) or as UPM's list of values (true).</summary>
        public bool NumbersAsList { get; set; }
        private static string PathFor => Path.Combine(UltraPlusData.CacheDir, "ui.json");

        public static UpPrefs Load()
        {
            try
            {
                return File.Exists(PathFor) ? JsonSerializer.Deserialize<UpPrefs>(File.ReadAllText(PathFor)) ?? new() : new();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                CrashReporter.Log($"[OverlayWindow.UltraPlus] ui.json — {ex.Message}");
                return new();
            }
        }

        public void Save()
        {
            try { Directory.CreateDirectory(UltraPlusData.CacheDir); File.WriteAllText(PathFor, JsonSerializer.Serialize(this)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                CrashReporter.Log($"[OverlayWindow.UltraPlus] ui.json not saved — {ex.Message}");
            }
        }
    }

    // ── Entry and exit ───────────────────────────────────────────────────────

    /// <summary>The "Ultra+ settings" button for the This game box, or null (no unlock_extra.txt, or no Ultra+ here).</summary>
    private Button? MakeUltraPlusButton(RenoDXCommander.ViewModels.GameCardViewModel card)
    {
        if (!DevUnlockService.IsExtraUnlocked || UltraPlusFiles.Find(card.InstallPath) == null) return null;
        var btn = new Button { Content = "Ultra+ settings", HorizontalAlignment = HorizontalAlignment.Left };
        TabsTheme.Apply(btn, TabsTheme.PurpleButton, height: 40, fontSize: 14);
        ToolTipService.SetToolTip(btn, "Change this game's Ultra+ settings and watch them change in the game");
        btn.Click += (_, _) => EnterUltraPlus();
        return btn;
    }

    /// <summary>Opens Ultra+ mode; any failure is logged and shown on the dashboard instead of crashing the overlay.</summary>
    private async void EnterUltraPlus()
    {
        try { await EnterUltraPlusAsync(); }
        catch (Exception ex)
        {
            CrashReporter.Log($"[OverlayWindow.UltraPlus] Couldn't open Ultra+ settings — {ex}");
            if (_upMode) ExitUltraPlus();
            _hint.Text = "Couldn't open the Ultra+ settings. The log has the details.";
        }
    }

    private async Task EnterUltraPlusAsync()
    {
        if (_upMode || _game?.Card is not { } card) return;
        int open = _openCount;
        var installPath = card.InstallPath;
        var state = await Task.Run(() => UltraPlusFiles.Load(installPath));
        if (open != _openCount || !IsOpen) return;
        if (state == null)
        {
            CrashReporter.Log($"[OverlayWindow.UltraPlus] No readable Ultra+ files for '{card.GameName}'");
            return;
        }

        string? notice = null;
        if (!state.AutoApplyOn)
        {
            // Ultra+ only reloads its settings live with AutoApplyConfig=on
            var path = state.ConfigPath;
            if (await Task.Run(() => UltraPlusFiles.Write(path, new Dictionary<string, string> { [UltraPlusConfig.AutoApplyKey] = "on" })))
            {
                state = await Task.Run(() => UltraPlusFiles.Load(installPath)) ?? state;
                notice = "Turned on Ultra+'s Automatically Apply Configuration so changes show straight away. Press F12 in the game once (or restart it) for Ultra+ to pick that up.";
            }
            else notice = "Ultra+'s Automatically Apply Configuration is off and RHI couldn't turn it on: changes apply when you press F12 in the game.";
        }

        _up = state;
        _upValues.Clear();
        foreach (var kv in state.Values) _upValues[kv.Key] = kv.Value;
        BuildUpCategories();
        // Display scale, read while the dashboard is still the content
        var scale = Math.Max(1, _dashRoot.XamlRoot?.RasterizationScale ?? 1);
        _upRoot = BuildUltraPlusRoot(card.GameName);
        _upMode = true;
        Content = _upRoot;
        GamePauser.Resume(); // the game must run to show the changes

        // A panel on the right of the game's monitor; the rest of the screen is the game
        int width = (int)Math.Min(Math.Round(UpPanelWidth * scale), _monitorRect.Width * 0.5);
        AppWindow.MoveAndResize(new RectInt32(_monitorRect.X + _monitorRect.Width - width, _monitorRect.Y, width, _monitorRect.Height));

        RebuildUpList();
        SetUpStatus(notice);
        StartUpWatcher(state.ConfigPath);
        CrashReporter.Log($"[OverlayWindow.UltraPlus] Opened for '{card.GameName}' ({state.Settings.Count} settings, {state.Presets.Count} presets, Ultra+ {state.Version ?? "?"})");
    }

    /// <summary>Back to the full dashboard (the window covers the monitor again).</summary>
    private void ExitUltraPlus(bool closing = false)
    {
        if (!_upMode) return;
        _upMode = false;
        StopUpWatcher();
        foreach (var row in _upRows.Values) row.Debounce?.Stop();
        Content = _dashRoot;
        if (_monitorRect.Width > 0) AppWindow.MoveAndResize(_monitorRect);
        if (!closing)
        {
            if (_settings().RhiOverlayPauseGame && _game?.Card != null) GamePauser.Pause(_game.ProcessId);
            BuildDashboard(); // values shown on the dashboard may have changed
            _backButton.Focus(FocusState.Programmatic);
        }
    }

    // ── Layout ───────────────────────────────────────────────────────────────

    private Grid BuildUltraPlusRoot(string gameName)
    {
        var root = new Grid
        {
            Background = UIFactory.ThemedBrush(Windows.UI.Color.FromArgb(0xD8, 0x0B, 0x0F, 0x17)),
            Padding = new Thickness(22, 22, 22, 16),
            RowSpacing = 12,
        };
        for (int i = 0; i < 5; i++)
            root.RowDefinitions.Add(new RowDefinition { Height = i == 3 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        root.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Escape) { e.Handled = true; ExitUltraPlus(); }
        };

        // Header: Ultra+ / game / version, and Back
        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titles = new StackPanel { Spacing = 2 };
        titles.Children.Add(new TextBlock { Text = "ULTRA+ SETTINGS", FontSize = 11, CharacterSpacing = 120, Foreground = TabsTheme.B(TabsTheme.TextMuted) });
        titles.Children.Add(new TextBlock
        {
            Text = gameName, FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = TabsTheme.B(TabsTheme.TextPrimary), TextTrimming = TextTrimming.CharacterEllipsis,
        });
        _upSubtitle = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap };
        titles.Children.Add(_upSubtitle);
        UpdateUpSubtitle();
        header.Children.Add(titles);
        var back = new Button { Content = "Full overlay", VerticalAlignment = VerticalAlignment.Top };
        TabsTheme.Apply(back, TabsTheme.BlueButton, height: 36, fontSize: 13);
        ToolTipService.SetToolTip(back, "Back to the full RHI overlay (Esc)");
        back.Click += (_, _) => ExitUltraPlus();
        Grid.SetColumn(back, 1);
        header.Children.Add(back);
        root.Children.Add(header);

        // Search and category on one row; view switches below
        var tools = new StackPanel { Spacing = 10 };
        var findRow = new Grid { ColumnSpacing = 8 };
        findRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        findRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        var search = new TextBox { PlaceholderText = "Search settings", Text = _upSearch, FontSize = 14 };
        search.TextChanged += (_, _) => { _upSearch = search.Text.Trim(); RebuildUpList(); };
        findRow.Children.Add(search);
        _upCategories = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 14 };
        ToolTipService.SetToolTip(_upCategories, "Show one group of settings, or only the ones changed from Ultra+'s defaults");
        _upCategories.SelectionChanged += (_, _) =>
        {
            if (_upSilent || _upCategories.SelectedItem is not ComboBoxItem { Tag: string name } || name == _upCategory) return;
            _upCategory = name;
            RebuildUpList();
        };
        Grid.SetColumn(_upCategories, 1);
        findRow.Children.Add(_upCategories);
        tools.Children.Add(findRow);
        RebuildUpChips();

        var switches = new Grid { ColumnSpacing = 18 };
        switches.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        switches.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var desc = MakeUpSwitch("Descriptions", new[] { "Off", "On" }, _upPrefs.ShowDescriptions ? 1 : 0,
            "On: every setting's description. Off: the description of the setting under the mouse shows at the bottom.",
            i => { _upPrefs.ShowDescriptions = i == 1; _upPrefs.Save(); ApplyUpDescriptionMode(); });
        var numbers = MakeUpSwitch("Numbers", new[] { "Slider", "List" }, _upPrefs.NumbersAsList ? 1 : 0,
            "How number settings are changed: a slider, or a list of every allowed value (like Ultra+ Manager).",
            i => { _upPrefs.NumbersAsList = i == 1; _upPrefs.Save(); RebuildUpList(keepScroll: true); });
        switches.Children.Add(desc);
        Grid.SetColumn(numbers, 1);
        switches.Children.Add(numbers);
        tools.Children.Add(switches);
        Grid.SetRow(tools, 1);
        root.Children.Add(tools);

        // Presets, Save, Restore defaults, status
        var presetsArea = new StackPanel { Spacing = 8 };
        var presetRow = new Grid { ColumnSpacing = 8 };
        presetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        presetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        presetRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _upPresets = new ComboBox { PlaceholderText = "Apply a preset", HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 14 };
        ToolTipService.SetToolTip(_upPresets, "Presets that came with Ultra+ for this game, then your own");
        FillUpPresets();
        _upPresets.SelectionChanged += (_, _) =>
        {
            if (_upSilent || _upPresets.SelectedItem is not ComboBoxItem { Tag: UltraPlusPreset p }) return;
            ApplyUpPreset(p);
            _upSilent = true; _upPresets.SelectedIndex = -1; _upSilent = false;
        };
        presetRow.Children.Add(_upPresets);
        var save = new Button { Content = "Save as preset" };
        TabsTheme.Apply(save, TabsTheme.BlueButton, height: 34, fontSize: 13);
        ToolTipService.SetToolTip(save, "Save the current settings as your own preset for this game (Ultra+ Manager sees it too)");
        save.Click += (_, _) => { _upSaveRow.Visibility = Visibility.Visible; _upSaveName.Text = ""; _upSaveName.Focus(FocusState.Programmatic); };
        Grid.SetColumn(save, 1);
        presetRow.Children.Add(save);
        var restore = new Button { Content = "Restore defaults" };
        TabsTheme.Apply(restore, TabsTheme.AmberButton, height: 34, fontSize: 13);
        ToolTipService.SetToolTip(restore, "Set every setting back to Ultra+'s default. You can undo it.");
        restore.Click += (_, _) => RestoreUpDefaults();
        Grid.SetColumn(restore, 2);
        presetRow.Children.Add(restore);
        presetsArea.Children.Add(presetRow);

        _upSaveRow = new Grid { ColumnSpacing = 8, Visibility = Visibility.Collapsed };
        _upSaveRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _upSaveRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _upSaveRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _upSaveName = new TextBox { PlaceholderText = "Preset name", FontSize = 14, MaxLength = 60 };
        _upSaveName.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { e.Handled = true; SaveUpPreset(); } };
        _upSaveRow.Children.Add(_upSaveName);
        var saveOk = new Button { Content = "Save" };
        TabsTheme.Apply(saveOk, TabsTheme.GreenButton, height: 34, fontSize: 13);
        saveOk.Click += (_, _) => SaveUpPreset();
        Grid.SetColumn(saveOk, 1);
        _upSaveRow.Children.Add(saveOk);
        var saveCancel = new Button { Content = "Cancel" };
        TabsTheme.Apply(saveCancel, TabsTheme.IconButton, height: 34, fontSize: 13);
        saveCancel.Click += (_, _) => _upSaveRow.Visibility = Visibility.Collapsed;
        Grid.SetColumn(saveCancel, 2);
        _upSaveRow.Children.Add(saveCancel);
        presetsArea.Children.Add(_upSaveRow);

        var statusRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        _upStatus = new TextBlock { FontSize = 13, Foreground = TabsTheme.B(TabsTheme.TextSecondary), TextWrapping = TextWrapping.Wrap, MaxWidth = 380, VerticalAlignment = VerticalAlignment.Center };
        _upUndo = new Button { Content = "Undo", Visibility = Visibility.Collapsed };
        TabsTheme.Apply(_upUndo, TabsTheme.BlueButton, height: 30, fontSize: 12);
        _upUndo.Click += (_, _) => UndoUp();
        _upDeletePreset = new Button { Content = "Delete preset", Visibility = Visibility.Collapsed };
        TabsTheme.Apply(_upDeletePreset, TabsTheme.DangerButton, height: 30, fontSize: 12);
        _upDeletePreset.Click += (_, _) => DeleteUpPreset();
        statusRow.Children.Add(_upStatus);
        statusRow.Children.Add(_upUndo);
        statusRow.Children.Add(_upDeletePreset);
        presetsArea.Children.Add(statusRow);
        Grid.SetRow(presetsArea, 2);
        root.Children.Add(presetsArea);

        // The settings
        _upList = new StackPanel { Spacing = 8, Padding = new Thickness(0, 0, 12, 12) }; // room below the last setting
        _upScroller = new ScrollViewer { Content = _upList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(_upScroller, 3);
        root.Children.Add(_upScroller);

        // Bottom: the selected setting's description (descriptions off), and the credit
        // Fixed height (five lines, "…" beyond, full text in the tooltip) so pointing at a setting never resizes the list
        var bottom = new StackPanel
        {
            Spacing = 6,
            Padding = new Thickness(0, 10, 0, 0),
            BorderBrush = TabsTheme.B(TabsTheme.CardBorder),
            BorderThickness = new Thickness(0, 1, 0, 0),
        };
        _upInfo = new TextBlock
        {
            FontSize = 13, LineHeight = 19, Height = 5 * 19, MaxLines = 5,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = TabsTheme.B(TabsTheme.TextControl), TextWrapping = TextWrapping.Wrap,
        };
        bottom.Children.Add(_upInfo);
        bottom.Children.Add(new TextBlock
        {
            Text = UltraPlusData.HasData
                ? "Ultra+ by the Ultra+ team. Setting names, descriptions and costs from Ultra+ Manager. Esc: full overlay."
                : "Ultra+ by the Ultra+ team. Esc: full overlay.",
            FontSize = 11, Foreground = TabsTheme.B(TabsTheme.TextMuted), TextWrapping = TextWrapping.Wrap,
        });
        Grid.SetRow(bottom, 4);
        root.Children.Add(bottom);

        TabsTheme.Restyle(root);
        ApplyUpDescriptionMode();
        return root;
    }

    // ── Categories ───────────────────────────────────────────────────────────

    private void BuildUpCategories()
    {
        _upCategoryOf = new(StringComparer.OrdinalIgnoreCase);
        _upCategoryOrder = new();
        foreach (var (name, keys) in UltraPlusData.Categories)
            foreach (var k in keys)
                if (!_upCategoryOf.ContainsKey(k)) _upCategoryOf[k] = name;
        foreach (var (name, _) in UltraPlusData.Categories) _upCategoryOrder.Add(name);
        if (!_upCategoryOrder.Contains(_upCategory) && _upCategory is not (UpAll or UpChanged or UpOther)) _upCategory = UpAll;
    }

    private string CategoryOf(UltraPlusSetting s) => _upCategoryOf.TryGetValue(s.Key, out var c) ? c : UpOther;

    private IEnumerable<UltraPlusSetting> VisibleUpSettings() => _up?.Settings ?? Enumerable.Empty<UltraPlusSetting>();

    /// <summary>Fills the category list: All, Changed, then UPM's groups present in this game (with counts).</summary>
    private void RebuildUpChips()
    {
        if (_upCategories == null) return;
        var visible = VisibleUpSettings().ToList();
        var present = visible.Select(CategoryOf).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var names = new List<string> { UpAll, UpChanged };
        names.AddRange(_upCategoryOrder.Where(present.Contains));
        if (present.Contains(UpOther) && _upCategoryOrder.Count > 0) names.Add(UpOther);
        if (!names.Contains(_upCategory)) _upCategory = UpAll;

        _upSilent = true;
        try
        {
            _upCategories.Items.Clear();
            foreach (var name in names)
            {
                int count = name switch
                {
                    UpAll => visible.Count,
                    UpChanged => visible.Count(IsUpChanged),
                    _ => visible.Count(s => CategoryOf(s).Equals(name, StringComparison.OrdinalIgnoreCase)),
                };
                var label = name switch { UpAll => "All settings", UpChanged => "Changed from default", _ => ShortCategory(name) };
                var item = new ComboBoxItem { Content = $"{label}  ({count})", Tag = name };
                if (label != name && name is not (UpAll or UpChanged)) ToolTipService.SetToolTip(item, name);
                _upCategories.Items.Add(item);
                if (name == _upCategory) _upCategories.SelectedItem = item;
            }
        }
        finally { _upSilent = false; }
    }

    /// <summary>A labelled Off / On style switch that redraws itself when picked.</summary>
    private static FrameworkElement MakeUpSwitch(string title, string[] labels, int selected, string tooltip, Action<int> onPick)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 12, Foreground = TabsTheme.B(TabsTheme.TextMuted) });
        var host = new Border();
        void Draw(int sel) => host.Child = TabsTheme.MakeSegmented(labels, sel, i => { Draw(i); onPick(i); });
        Draw(selected);
        panel.Children.Add(host);
        ToolTipService.SetToolTip(panel, tooltip);
        return panel;
    }

    private static string ShortCategory(string name) =>
        name.StartsWith("Upscaling", StringComparison.OrdinalIgnoreCase) ? "Upscaling and FG" : name;

    // ── Rows ─────────────────────────────────────────────────────────────────

    private void RebuildUpList(bool keepScroll = false)
    {
        var offset = _upScroller?.VerticalOffset ?? 0;
        if (_up == null || _upList == null) return;
        foreach (var r in _upRows.Values) r.Debounce?.Stop();
        _upRows.Clear();
        _upList.Children.Clear();

        IEnumerable<UltraPlusSetting> list = VisibleUpSettings();
        if (_upCategory == UpChanged) list = list.Where(IsUpChanged);
        else if (_upCategory != UpAll) list = list.Where(s => CategoryOf(s).Equals(_upCategory, StringComparison.OrdinalIgnoreCase));
        if (_upSearch.Length > 0)
            list = list.Where(s => UltraPlusData.NameOf(s.Key).Contains(_upSearch, StringComparison.OrdinalIgnoreCase)
                || s.Key.Contains(_upSearch, StringComparison.OrdinalIgnoreCase)
                || (UltraPlusData.DescriptionOf(s)?.Contains(_upSearch, StringComparison.OrdinalIgnoreCase) ?? false));

        // In All / Changed / search: group by category (in UPM's order) under small headings
        var items = list.ToList();
        bool grouped = _upCategory is UpAll or UpChanged && _upCategoryOrder.Count > 0;
        if (grouped)
        {
            var order = _upCategoryOrder.Append(UpOther).ToList();
            items = items.OrderBy(s => order.IndexOf(CategoryOf(s)) is var i && i >= 0 ? i : int.MaxValue)
                         .ThenBy(s => KeyIndexInCategory(s)).ToList();
        }
        else if (_upCategoryOrder.Count > 0) items = items.OrderBy(KeyIndexInCategory).ToList();

        string? lastCat = null;
        foreach (var s in items)
        {
            if (grouped && CategoryOf(s) is var cat && cat != lastCat)
            {
                lastCat = cat;
                _upList.Children.Add(new TextBlock
                {
                    Text = cat.ToUpperInvariant(), FontSize = 11, CharacterSpacing = 100,
                    Foreground = TabsTheme.B(TabsTheme.TextMuted), Margin = new Thickness(2, _upList.Children.Count == 0 ? 0 : 10, 0, 0),
                });
            }
            var row = BuildUpRow(s);
            _upRows[s.Key] = row;
            _upList.Children.Add(row.Root);
        }
        if (items.Count == 0)
            _upList.Children.Add(new TextBlock
            {
                Text = _upCategory == UpChanged ? "Nothing is changed from Ultra+'s defaults." : "No settings match.",
                FontSize = 14, Foreground = TabsTheme.B(TabsTheme.TextSecondary), Margin = new Thickness(2, 8, 0, 0),
            });
        TabsTheme.Restyle(_upList);
        _upScroller?.ChangeView(null, keepScroll ? offset : 0, null, disableAnimation: true);
    }

    private int KeyIndexInCategory(UltraPlusSetting s)
    {
        foreach (var (name, keys) in UltraPlusData.Categories)
            if (name.Equals(CategoryOf(s), StringComparison.OrdinalIgnoreCase))
            {
                var i = keys.FindIndex(k => k.Equals(s.Key, StringComparison.OrdinalIgnoreCase));
                return i >= 0 ? i : int.MaxValue;
            }
        return int.MaxValue;
    }

    private UpRow BuildUpRow(UltraPlusSetting s)
    {
        var row = new UpRow { Setting = s };
        var grid = new Grid { ColumnSpacing = 14 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });

        var left = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new TextBlock
        {
            Text = UltraPlusData.NameOf(s.Key), FontSize = 15, TextWrapping = TextWrapping.Wrap,
            Foreground = TabsTheme.B(TabsTheme.TextPrimary),
        });
        var meta = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Changed = new TextBlock { Text = "Changed", FontSize = 11, Foreground = TabsTheme.B("#D9C8FF"), VerticalAlignment = VerticalAlignment.Center };
        ToolTipService.SetToolTip(row.Changed, "Not Ultra+'s default");
        row.Cost = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        row.State = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        meta.Children.Add(row.Changed);
        meta.Children.Add(row.Cost);
        if (!string.IsNullOrEmpty(s.Shortcut))
        {
            var key = new TextBlock { Text = s.Shortcut, FontSize = 11, Foreground = TabsTheme.B(TabsTheme.TextMuted), VerticalAlignment = VerticalAlignment.Center };
            ToolTipService.SetToolTip(key, $"Ultra+ toggles this with {s.Shortcut} in the game when its keybinds are on");
            meta.Children.Add(key);
        }
        row.Reset = new Button { Content = "Reset", Padding = new Thickness(8, 1, 8, 2) };
        TabsTheme.Apply(row.Reset, TabsTheme.IconButton, height: 24, fontSize: 11);
        row.Reset.Click += (_, _) => { if (_up != null) WriteUpValues(new() { [s.Key] = _up.DefaultOf(s) }, undo: null, status: null); };
        meta.Children.Add(row.Reset);
        meta.Children.Add(row.State);
        left.Children.Add(meta);
        row.Description = new TextBlock
        {
            Text = UltraPlusData.DescriptionOf(s) ?? "", FontSize = 13, LineHeight = 19, TextWrapping = TextWrapping.Wrap,
            Foreground = TabsTheme.B(TabsTheme.TextSecondary),
        };
        left.Children.Add(row.Description);
        grid.Children.Add(left);

        var control = BuildUpControl(row);
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);

        row.Root = new Border
        {
            Background = UIFactory.ThemedBrush(Windows.UI.Color.FromArgb(0xE6, 0x12, 0x18, 0x24)),
            BorderBrush = TabsTheme.B(TabsTheme.CardBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10, 14, 10),
            Child = grid,
        };
        void ShowInfo()
        {
            if (_upPrefs.ShowDescriptions) return;
            var text = UltraPlusData.DescriptionOf(s) ?? UltraPlusData.NameOf(s.Key);
            _upInfo.Text = text;
            ToolTipService.SetToolTip(_upInfo, text.Length > 300 ? text : null); // a long one may be cut off: full text here
        }
        row.Root.PointerEntered += (_, _) => ShowInfo();
        row.Root.GotFocus += (_, _) => ShowInfo();
        row.Description.Visibility = _upPrefs.ShowDescriptions && row.Description.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshUpRow(row);
        return row;
    }

    /// <summary>Numbers get a slider (plus a Game default box when the game's own value is allowed); everything else a list.</summary>
    private FrameworkElement BuildUpControl(UpRow row)
    {
        var s = row.Setting;
        var choices = UltraPlusConfig.Choices(s);
        // A slider when the range is complete and every choice is on it ("-1" only when the range starts there);
        // otherwise the list UPM shows
        bool slider = !_upPrefs.NumbersAsList && s.IsNumeric && s.Min is double min && s.Max is double max && s.Step is double step && step > 0
            && max > min && (max - min) / step <= 2000
            && (!choices.Contains("-1") || min == -1);
        if (slider)
        {
            var panel = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.SliderValue = new TextBlock { FontSize = 14, Foreground = TabsTheme.B(TabsTheme.TextPrimary), HorizontalAlignment = HorizontalAlignment.Right };
            if (choices.Contains("game", StringComparer.OrdinalIgnoreCase))
            {
                row.GameDefault = new CheckBox { Content = "Game default", FontSize = 12, MinWidth = 0 };
                ToolTipService.SetToolTip(row.GameDefault, "Use the game's own value");
                row.GameDefault.Click += (_, _) =>
                {
                    if (row.GameDefault.IsChecked == true) WriteUpValues(new() { [s.Key] = "game" }, null, null);
                    else WriteUpValues(new() { [s.Key] = FormatUpNumber(s, row.Slider!.Value) }, null, null);
                };
                top.Children.Add(row.GameDefault);
            }
            Grid.SetColumn(row.SliderValue, 1);
            top.Children.Add(row.SliderValue);
            panel.Children.Add(top);
            row.Slider = new Slider
            {
                Minimum = s.Min!.Value, Maximum = s.Max!.Value, StepFrequency = s.Step!.Value, SmallChange = s.Step.Value,
                LargeChange = s.Step.Value * 5, IsThumbToolTipEnabled = false,
            };
            row.Slider.ValueChanged += (_, e) =>
            {
                row.SliderValue!.Text = FormatUpNumber(s, e.NewValue);
                if (_upSilent) return;
                if (row.GameDefault != null) { _upSilent = true; row.GameDefault.IsChecked = false; _upSilent = false; }
                // Write after a short pause while dragging (and at once on release / keys)
                row.Debounce ??= CreateDebounce(row);
                row.Debounce.Stop();
                row.Debounce.Start();
            };
            panel.Children.Add(row.Slider);
            return panel;
        }

        row.Combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center, FontSize = 14 };
        foreach (var c in choices) row.Combo.Items.Add(c);
        row.Combo.SelectionChanged += (_, _) =>
        {
            if (_upSilent || row.Combo.SelectedItem is not string v) return;
            WriteUpValues(new() { [s.Key] = v }, null, null);
        };
        return row.Combo;
    }

    private Microsoft.UI.Dispatching.DispatcherQueueTimer CreateDebounce(UpRow row)
    {
        var t = DispatcherQueue.CreateTimer();
        t.Interval = TimeSpan.FromMilliseconds(180);
        t.IsRepeating = false;
        t.Tick += (_, _) =>
        {
            if (row.Slider != null) WriteUpValues(new() { [row.Setting.Key] = FormatUpNumber(row.Setting, row.Slider.Value) }, null, null);
        };
        return t;
    }

    private static string FormatUpNumber(UltraPlusSetting s, double v) =>
        s.ValueType == "int"
            ? ((int)Math.Round(v)).ToString(CultureInfo.InvariantCulture)
            : UltraPlusConfig.NormalizeNumeric(v.ToString("R", CultureInfo.InvariantCulture), s.Step);

    private string UpValueOf(UltraPlusSetting s) =>
        UltraPlusConfig.Normalize(s, _upValues.TryGetValue(s.Key, out var v) ? v : s.Default);

    private bool IsUpChanged(UltraPlusSetting s) =>
        _up != null && !string.Equals(UpValueOf(s), _up.DefaultOf(s), StringComparison.OrdinalIgnoreCase);

    /// <summary>Puts the current value into the row's control and updates its marks.</summary>
    private void RefreshUpRow(UpRow row)
    {
        var s = row.Setting;
        var value = UpValueOf(s);
        _upSilent = true;
        try
        {
            if (row.Combo != null)
            {
                int idx = -1;
                for (int i = 0; i < row.Combo.Items.Count; i++)
                    if (string.Equals(row.Combo.Items[i] as string, value, StringComparison.OrdinalIgnoreCase)) { idx = i; break; }
                if (idx < 0 && value.Length > 0) { row.Combo.Items.Add(value); idx = row.Combo.Items.Count - 1; } // a value the list doesn't have: show it
                row.Combo.SelectedIndex = idx;
            }
            else if (row.Slider != null)
            {
                bool isGame = value.Equals("game", StringComparison.OrdinalIgnoreCase);
                if (row.GameDefault != null) row.GameDefault.IsChecked = isGame;
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
                    row.Slider.Value = Math.Clamp(n, row.Slider.Minimum, row.Slider.Maximum);
                row.Slider.Opacity = isGame ? 0.45 : 1;
                row.SliderValue!.Text = isGame ? "Game" : FormatUpNumber(s, row.Slider.Value);
            }
        }
        finally { _upSilent = false; }

        bool changed = IsUpChanged(s);
        row.Changed.Visibility = changed ? Visibility.Visible : Visibility.Collapsed;
        row.Reset.Visibility = changed ? Visibility.Visible : Visibility.Collapsed;
        if (_up != null) ToolTipService.SetToolTip(row.Reset, $"Back to Ultra+'s default: {_up.DefaultOf(s)}");
        var cost = UltraPlusData.CostOf(s.Key, value);
        (string text, string colour) = cost?.ToLowerInvariant() switch
        {
            "low" => ("Low cost", "#7FE0A0"),
            "medium" => ("Medium cost", "#F0C35A"),
            "high" => ("High cost", "#FF9F6B"),
            "very_high" => ("Very high cost", "#FF7A8A"),
            _ => ("", TabsTheme.TextMuted),
        };
        row.Cost.Text = text;
        row.Cost.Foreground = TabsTheme.B(colour);
        row.Cost.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (text.Length > 0) ToolTipService.SetToolTip(row.Cost, "Performance cost of the current value (Ultra+ Manager's estimate)");
    }

    private void ApplyUpDescriptionMode()
    {
        foreach (var r in _upRows.Values)
            r.Description.Visibility = _upPrefs.ShowDescriptions && r.Description.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_upInfo != null)
        {
            _upInfo.Visibility = _upPrefs.ShowDescriptions ? Visibility.Collapsed : Visibility.Visible;
            if (!_upPrefs.ShowDescriptions && string.IsNullOrEmpty(_upInfo.Text))
                _upInfo.Text = "Point at a setting to see what it does.";
        }
    }

    // ── Writing ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes values (only those that differ), updates the rows and the status. <paramref name="undo"/> = values to
    /// restore with Undo (presets, Restore defaults); a single setting has no Undo (its Reset puts the default back).
    /// </summary>
    private void WriteUpValues(Dictionary<string, string> values, Dictionary<string, string>? undo, string? status)
    {
        if (_up == null) return;
        var changes = values.Where(kv => !string.Equals(
                _upValues.TryGetValue(kv.Key, out var cur) ? cur : null, kv.Value, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        if (changes.Count == 0) { if (status != null) SetUpStatus(status); return; }

        foreach (var kv in changes) _upValues[kv.Key] = kv.Value; // the watcher sees our own write as nothing new
        var path = _up.ConfigPath;
        int seq = ++_upWriteSeq;
        foreach (var key in changes.Keys)
            if (_upRows.TryGetValue(key, out var row)) { RefreshUpRow(row); row.State.Text = ""; }

        _ = Task.Run(() => UltraPlusFiles.Write(path, changes)).ContinueWith(t =>
        {
            bool ok = t.Status == TaskStatus.RanToCompletion && t.Result;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_upMode) return;
                foreach (var key in changes.Keys)
                {
                    if (!_upRows.TryGetValue(key, out var row)) continue;
                    row.State.Text = ok ? "Applied" : "Not applied";
                    row.State.Foreground = TabsTheme.B(ok ? "#7FE0A0" : "#FF9AA8");
                    ToolTipService.SetToolTip(row.State, ok ? null : "RHI couldn't write Ultra+'s settings file. Is it read-only, or open in another program?");
                    if (ok) ClearAppliedLater(row, seq);
                }
                if (status != null || undo != null || !ok)
                {
                    _upUndoValues = ok ? undo : null;
                    SetUpStatus(ok ? status : "Couldn't save the change: Ultra+'s settings file couldn't be written.", showUndo: ok && undo != null);
                }
                RebuildUpChipCounts();
            });
        });
    }

    private void ClearAppliedLater(UpRow row, int seq)
    {
        var t = DispatcherQueue.CreateTimer();
        t.Interval = TimeSpan.FromSeconds(1.6);
        t.IsRepeating = false;
        t.Tick += (_, _) => { t.Stop(); if (seq == _upWriteSeq && row.State.Text == "Applied") row.State.Text = ""; };
        t.Start();
    }

    private int _upChangedCount = -1;
    private TextBlock? _upSubtitle;

    /// <summary>Header line under the game: says changes are live only while AutoApplyConfig is on.</summary>
    private void UpdateUpSubtitle()
    {
        if (_upSubtitle == null) return;
        bool live = _upValues.TryGetValue(UltraPlusConfig.AutoApplyKey, out var v) && v.Equals("on", StringComparison.OrdinalIgnoreCase);
        var version = _up?.Version != null ? $"Ultra+ {_up.Version} · " : "";
        _upSubtitle.Text = version + (live
            ? "changes apply in the game as you make them"
            : "Automatically Apply Configuration is off: press F12 in the game to apply changes");
        _upSubtitle.Foreground = TabsTheme.B(live ? TabsTheme.TextSecondary : "#F0C35A");
    }

    /// <summary>The category list's counts follow Changed (refilled only when that count moves); the header follows AutoApplyConfig.</summary>
    private void RebuildUpChipCounts()
    {
        UpdateUpSubtitle();
        int count = VisibleUpSettings().Count(IsUpChanged);
        if (count == _upChangedCount) return;
        _upChangedCount = count;
        RebuildUpChips();
    }

    private void SetUpStatus(string? text, bool showUndo = false, bool showDelete = false)
    {
        if (_upStatus == null) return;
        _upStatus.Text = text ?? "";
        _upStatus.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        _upUndo.Visibility = showUndo ? Visibility.Visible : Visibility.Collapsed;
        _upDeletePreset.Visibility = showDelete || (_upLastCustomPreset != null && showUndo) ? Visibility.Visible : Visibility.Collapsed;
    }

    private Dictionary<string, string> CurrentUpValues(IEnumerable<string> keys) =>
        keys.ToDictionary(k => k, k => _upValues.TryGetValue(k, out var v) ? v
            : _up?.Settings.FirstOrDefault(s => s.Key.Equals(k, StringComparison.OrdinalIgnoreCase))?.Default ?? "", StringComparer.OrdinalIgnoreCase);

    private void ApplyUpPreset(UltraPlusPreset p)
    {
        if (_up == null) return;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in p.Values)
            if (_up.Settings.FirstOrDefault(s => s.Key.Equals(kv.Key, StringComparison.OrdinalIgnoreCase)) is { } s
                && UltraPlusConfig.Normalize(s, kv.Value) is var v && UltraPlusConfig.IsAllowed(s, v))
                values[s.Key] = v;
        int changed = values.Count(kv => !string.Equals(UpValueOf(_up.Settings.First(s => s.Key == kv.Key)), kv.Value, StringComparison.OrdinalIgnoreCase));
        _upLastCustomPreset = p.IsCustom ? p.StorageName : null;
        var undo = CurrentUpValues(values.Keys);
        WriteUpValues(values, undo, changed == 0
            ? $"{p.DisplayName}: the settings already match."
            : $"Applied {p.DisplayName} ({changed} setting{(changed == 1 ? "" : "s")} changed).");
        CrashReporter.Log($"[OverlayWindow.UltraPlus] Preset '{p.StorageName}' ({changed} changed)");
        if (changed == 0) SetUpStatus(_upStatus.Text, showDelete: p.IsCustom);
    }

    private void RestoreUpDefaults()
    {
        if (_up == null) return;
        var values = _up.Settings.Where(IsUpChanged).ToDictionary(s => s.Key, s => _up.DefaultOf(s), StringComparer.OrdinalIgnoreCase);
        // Keep live-apply on: restoring the defaults would otherwise switch it off again
        values.Remove(UltraPlusConfig.AutoApplyKey);
        _upLastCustomPreset = null;
        if (values.Count == 0) { SetUpStatus("Every setting is already at Ultra+'s default."); return; }
        WriteUpValues(values, CurrentUpValues(values.Keys), $"Restored Ultra+'s defaults ({values.Count} setting{(values.Count == 1 ? "" : "s")}).");
    }

    private void UndoUp()
    {
        if (_upUndoValues is not { } undo) return;
        _upUndoValues = null;
        _upLastCustomPreset = null;
        WriteUpValues(undo, null, "Undone.");
        SetUpStatus("Undone.");
    }

    private void FillUpPresets()
    {
        _upSilent = true;
        _upPresets.Items.Clear();
        foreach (var p in _up?.Presets ?? Array.Empty<UltraPlusPreset>())
        {
            var item = new ComboBoxItem { Content = p.IsCustom ? $"{p.DisplayName} (yours)" : p.DisplayName, Tag = p };
            if (p.Description != null) ToolTipService.SetToolTip(item, p.Description);
            _upPresets.Items.Add(item);
        }
        _upPresets.IsEnabled = _upPresets.Items.Count > 0;
        if (_upPresets.Items.Count == 0) _upPresets.PlaceholderText = "No presets for this game";
        _upSilent = false;
    }

    private async void SaveUpPreset()
    {
        try
        {
            if (_up == null) return;
            var name = _upSaveName.Text.Trim();
            var values = _up.Settings.Select(s => new KeyValuePair<string, string>(s.Key, UpValueOf(s))).ToList();
            var dir = _up.DataDir;
            var error = await Task.Run(() => UltraPlusFiles.SavePreset(dir, name, null, values));
            if (!_upMode) return;
            if (error != null) { SetUpStatus(error); return; }
            _upSaveRow.Visibility = Visibility.Collapsed;
            await ReloadUpStateAsync();
            SetUpStatus($"Saved your preset \"{name}\".");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[OverlayWindow.UltraPlus.SaveUpPreset] {ex.Message}");
        }
    }

    private async void DeleteUpPreset()
    {
        try
        {
            if (_up == null || _upLastCustomPreset is not { } name) return;
            var dir = _up.DataDir;
            bool ok = await Task.Run(() => UltraPlusFiles.DeletePreset(dir, name));
            _upLastCustomPreset = null;
            await ReloadUpStateAsync();
            SetUpStatus(ok ? $"Deleted your preset \"{name}\"." : "Couldn't delete the preset.");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[OverlayWindow.UltraPlus.DeleteUpPreset] {ex.Message}");
        }
    }

    /// <summary>Re-reads presets (after saving or deleting one); values are kept as they are.</summary>
    private async Task ReloadUpStateAsync()
    {
        var installPath = _game?.Card?.InstallPath;
        var state = await Task.Run(() => UltraPlusFiles.Load(installPath));
        if (!_upMode || state == null) return;
        _up = state;
        FillUpPresets();
    }

    // ── Changes made elsewhere (in-game keybinds, Ultra+ Manager) ─────────────

    private void StartUpWatcher(string configPath)
    {
        StopUpWatcher();
        try
        {
            _upWatcher = new FileSystemWatcher(Path.GetDirectoryName(configPath)!, Path.GetFileName(configPath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };
            FileSystemEventHandler onChange = (_, _) => DispatcherQueue.TryEnqueue(QueueUpReload);
            _upWatcher.Changed += onChange;
            _upWatcher.Created += onChange;
            _upWatcher.Renamed += (_, _) => DispatcherQueue.TryEnqueue(QueueUpReload);
            _upWatcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[OverlayWindow.UltraPlus] Can't watch '{configPath}' — {ex.Message}");
        }
    }

    private void StopUpWatcher()
    {
        _upReloadTimer?.Stop();
        if (_upWatcher == null) return;
        _upWatcher.EnableRaisingEvents = false;
        _upWatcher.Dispose();
        _upWatcher = null;
    }

    private void QueueUpReload()
    {
        if (!_upMode) return;
        if (_upReloadTimer == null)
        {
            _upReloadTimer = DispatcherQueue.CreateTimer();
            _upReloadTimer.Interval = TimeSpan.FromMilliseconds(350);
            _upReloadTimer.IsRepeating = false;
            _upReloadTimer.Tick += async (_, _) =>
            {
                try { await ReloadUpValuesAsync(); }
                catch (Exception ex) { CrashReporter.Log($"[OverlayWindow.UltraPlus] Reload failed — {ex.Message}"); }
            };
        }
        _upReloadTimer.Stop();
        _upReloadTimer.Start();
    }

    private async Task ReloadUpValuesAsync()
    {
        if (!_upMode || _up == null) return;
        var path = _up.ConfigPath;
        var values = await Task.Run(() =>
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var sr = new StreamReader(fs);
                return UltraPlusConfig.ParseValues(sr.ReadToEnd());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        });
        if (!_upMode || values == null) return;
        int updated = 0;
        foreach (var s in _up.Settings)
        {
            var now = values.TryGetValue(s.Key, out var v) ? v : null;
            var had = _upValues.TryGetValue(s.Key, out var h) ? h : null;
            if (now == null || string.Equals(UltraPlusConfig.Normalize(s, now), had == null ? null : UltraPlusConfig.Normalize(s, had), StringComparison.OrdinalIgnoreCase)) continue;
            _upValues[s.Key] = now;
            updated++;
            if (_upRows.TryGetValue(s.Key, out var row) && !(row.Slider?.FocusState is FocusState.Pointer or FocusState.Keyboard))
                RefreshUpRow(row);
        }
        if (updated > 0)
        {
            CrashReporter.Log($"[OverlayWindow.UltraPlus] {updated} value(s) changed outside RHI");
            RebuildUpChipCounts();
        }
    }
}
