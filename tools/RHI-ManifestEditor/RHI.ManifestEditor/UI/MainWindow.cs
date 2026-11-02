using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using RHI.ManifestEditor.Json;
using RHI.ManifestEditor.Services;
using Windows.System;

namespace RHI.ManifestEditor.UI;

public sealed partial class MainWindow : Window, IEditorHost
{
    private enum Mode { Fields, Games, Global }

    private readonly EditorSettings _settings = EditorSettings.Load();
    private JsonDoc? _doc;
    private string _path = "";
    private List<LibraryGame> _library = new();
    private Mode _mode = Mode.Fields;
    private string? _currentGame;
    private string? _currentField;
    private readonly Dictionary<string, TextBlock> _fieldSubs = new(StringComparer.Ordinal);
    private string? _currentGlobal;
    private GameEditor? _gameEditor;
    private bool _selfEdit;
    private bool _dialogOpen;
    private bool _forceClose;

    // Deferred typed edits
    private readonly Dictionary<string, Action> _pending = new();
    private readonly List<string> _pendingOrder = new();
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _deferTimer;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _checkTimer;

    // Chrome
    private readonly Grid _root = new();
    private readonly TextBlock _fileText = Ui.T("", 12, fg: Ui.Secondary, wrap: false);
    private readonly TextBlock _statusText = Ui.T("", 12, fg: Ui.Secondary, wrap: false);
    private readonly Button _saveBtn, _undoBtn, _redoBtn, _reviewBtn, _checkBtn, _backBtn, _forwardBtn;
    private readonly TextBlock _checkText = Ui.T("Check", 14, wrap: false);
    private readonly ToggleButton _fieldsTab = new() { Content = "Fields", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ToggleButton _gamesTab = new() { Content = "Games", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ToggleButton _globalTab = new() { Content = "Global", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _search = new() { PlaceholderText = "Search" };
    private readonly CheckBox _showLibrary = new() { Content = "Include games from my RHI library" };
    private readonly ListView _list = new() { SelectionMode = ListViewSelectionMode.Single };
    private readonly TextBlock _listInfo = Ui.T("", 12, fg: Ui.Secondary, wrap: false);
    private readonly ScrollViewer _content = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private bool _suppressList;
    private List<Issue> _issues = new();

    // List item cache: game → (item, subtitle)
    private readonly Dictionary<string, (ListViewItem Item, TextBlock Sub)> _itemCache = new(StringComparer.Ordinal);
    private Dictionary<string, int> _counts = new(StringComparer.Ordinal);

    public JsonDoc Doc => _doc!;
    public string ManifestPath => _path;
    public IReadOnlyList<LibraryGame> LibraryGames => _library;
    public XamlRoot XamlRoot => _root.XamlRoot;

    public MainWindow()
    {
        Title = "RHI Manifest Editor";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1500, 980));
        var icon = Win32.LoadAppIcon();   // the exe's own icon (ApplicationIcon); single-file, so no loose .ico
        if (icon != IntPtr.Zero)
            AppWindow.SetIcon(Microsoft.UI.Win32Interop.GetIconIdFromIcon(icon));

        _deferTimer = DispatcherQueue.CreateTimer();
        _deferTimer.Interval = TimeSpan.FromMilliseconds(500);
        _deferTimer.IsRepeating = false;
        _deferTimer.Tick += (_, _) => Flush();
        _checkTimer = DispatcherQueue.CreateTimer();
        _checkTimer.Interval = TimeSpan.FromMilliseconds(700);
        _checkTimer.IsRepeating = false;
        _checkTimer.Tick += (_, _) => RunChecks();

        _saveBtn = Ui.Btn("Save", () => _ = SaveAsync(), accent: true, tooltip: "Save (Ctrl+S)");
        _undoBtn = Ui.Btn("Undo", Undo, tooltip: "Undo (Ctrl+Z)");
        _redoBtn = Ui.Btn("Redo", Redo, tooltip: "Redo (Ctrl+Y)");
        _reviewBtn = Ui.Btn("Review changes", () => _ = ShowReviewAsync(), tooltip: "See every line you changed since the last save");
        _backBtn = new Button { Content = new FontIcon { Glyph = "", FontSize = 14 }, IsEnabled = false, Padding = new Thickness(10, 8, 10, 8) };
        _backBtn.Click += (_, _) => GoBack();
        ToolTipService.SetToolTip(_backBtn, "Back (mouse back button, Alt+Left)");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_backBtn, "Back");
        _forwardBtn = new Button { Content = new FontIcon { Glyph = "", FontSize = 14 }, IsEnabled = false, Padding = new Thickness(10, 8, 10, 8) };
        _forwardBtn.Click += (_, _) => GoForward();
        ToolTipService.SetToolTip(_forwardBtn, "Forward (mouse forward button, Alt+Right)");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_forwardBtn, "Forward");
        _checkBtn = new Button { Content = _checkText };
        ToolTipService.SetToolTip(_checkBtn, "Problems that would break the manifest for users, and conflicting settings");
        _checkBtn.Click += (_, _) => _ = ShowIssuesAsync();

        BuildLayout();
        Content = _root;
        AppWindow.Closing += OnClosing;
        Activated += OnActivated;

        _root.Loaded += (_, _) => StartUp();
    }

    // ── Layout ───────────────────────────────────────────────────────────────

    private void BuildLayout()
    {
        _root.Background = Ui.Res("ApplicationPageBackgroundThemeBrush");
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Top bar
        var top = new Grid { Padding = new Thickness(16, 10, 16, 10), ColumnSpacing = 16, Background = Ui.LayerBg };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var openLink = new HyperlinkButton { Content = "Open another file...", Padding = new Thickness(6, 0, 6, 0), FontSize = 12 };
        openLink.Click += (_, _) => _ = PickFileAsync();
        var titleBlock = Ui.V(0, Ui.T("RHI Manifest Editor", 18, bold: true, wrap: false), Ui.H(4, _fileText, openLink));
        var nav = Ui.H(4, _backBtn, _forwardBtn);
        nav.VerticalAlignment = VerticalAlignment.Center;
        top.Children.Add(Ui.H(14, nav, titleBlock));
        var pushBtn = Ui.Btn("Push to GitHub...", () => _ = PushAsync(),
            tooltip: "Commit manifest.json and push it to main. Pushing makes it live for every RHI user.");
        var buttons = Ui.H(8, _checkBtn, _reviewBtn, _undoBtn, _redoBtn, _saveBtn, pushBtn);
        buttons.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(buttons, 1);
        top.Children.Add(buttons);
        _root.Children.Add(top);

        // Body
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(body, 1);

        var tabs = new Grid { ColumnSpacing = 6 };
        tabs.ColumnDefinitions.Add(new ColumnDefinition());
        tabs.ColumnDefinitions.Add(new ColumnDefinition());
        tabs.ColumnDefinitions.Add(new ColumnDefinition());
        tabs.Children.Add(_fieldsTab);
        Grid.SetColumn(_gamesTab, 1);
        tabs.Children.Add(_gamesTab);
        Grid.SetColumn(_globalTab, 2);
        tabs.Children.Add(_globalTab);
        ToolTipService.SetToolTip(_fieldsTab, "Pick a manifest field (engine versions, Nexus links, blacklist...) and see every game in it");
        ToolTipService.SetToolTip(_gamesTab, "Pick a game and see every setting it has");
        ToolTipService.SetToolTip(_globalTab, "Packs, presets, authors, feature flags and other settings that aren't per game");
        _fieldsTab.Click += (_, _) => SetMode(Mode.Fields);
        _gamesTab.Click += (_, _) => SetMode(Mode.Games);
        _globalTab.Click += (_, _) => SetMode(Mode.Global);

        _search.TextChanged += (_, _) => { if (_mode != Mode.Global) RebuildList(); };
        _search.KeyDown += (_, e) =>
        {
            if (e.Key != VirtualKey.Enter) return;
            var first = _list.Items.OfType<ListViewItem>().FirstOrDefault(i => i.Tag is string);
            if (first != null) { _list.SelectedItem = first; e.Handled = true; }
        };
        _showLibrary.IsChecked = _settings.ShowLibrary;
        _showLibrary.Click += (_, _) =>
        {
            _settings.ShowLibrary = _showLibrary.IsChecked == true;
            _settings.Save();
            RebuildList();
        };
        _list.SelectionChanged += OnListSelection;

        var left = new Grid { RowSpacing = 8, Padding = new Thickness(12, 12, 8, 8) };
        for (int i = 0; i < 4; i++) left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        left.Children.Add(tabs);
        Grid.SetRow(_search, 1); left.Children.Add(_search);
        Grid.SetRow(_showLibrary, 2); left.Children.Add(_showLibrary);
        Grid.SetRow(_listInfo, 3); left.Children.Add(_listInfo);
        Grid.SetRow(_list, 4); left.Children.Add(_list);
        var leftBorder = new Border
        {
            Child = left,
            BorderBrush = Ui.CardStroke,
            BorderThickness = new Thickness(0, 0, 1, 0),
        };
        body.Children.Add(leftBorder);

        Grid.SetColumn(_content, 1);
        body.Children.Add(_content);
        _root.Children.Add(body);

        // Status bar
        var status = new Border { Padding = new Thickness(16, 6, 16, 6), Background = Ui.LayerBg, Child = _statusText };
        Grid.SetRow(status, 2);
        _root.Children.Add(status);

        // Shortcuts
        AddAccel(VirtualKey.S, () => _ = SaveAsync());
        AddAccel(VirtualKey.Z, Undo);
        AddAccel(VirtualKey.Y, Redo);
        AddAccel(VirtualKey.F, () =>
        {
            if (_mode == Mode.Global) SetMode(Mode.Fields);
            _search.Focus(FocusState.Keyboard);
            _search.SelectAll();
        });
        AddAccel(VirtualKey.Left, GoBack, VirtualKeyModifiers.Menu);
        AddAccel(VirtualKey.Right, GoForward, VirtualKeyModifiers.Menu);
        AddAccel(VirtualKey.GoBack, GoBack, VirtualKeyModifiers.None);
        AddAccel(VirtualKey.GoForward, GoForward, VirtualKeyModifiers.None);
        // Mouse back/forward buttons. handledEventsToo: text boxes and buttons mark presses handled.
        _root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnRootPointerPressed), true);
    }

    private void AddAccel(VirtualKey key, Action action, VirtualKeyModifiers modifiers = VirtualKeyModifiers.Control)
    {
        var acc = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        acc.Invoked += (_, e) => { e.Handled = true; action(); };
        _root.KeyboardAccelerators.Add(acc);
    }

    // ── Loading ──────────────────────────────────────────────────────────────

    private void StartUp()
    {
        _library = Library.Load();
        var candidates = new List<string?> { _settings.LastPath };
        // Walk up from the exe for a repo checkout, then the usual repo location
        var dir = Path.GetDirectoryName(Environment.ProcessPath);
        for (int i = 0; i < 6 && dir != null; i++, dir = Path.GetDirectoryName(dir))
            candidates.Add(Path.Combine(dir, "manifest.json"));
        candidates.Add(@"G:\RDXC\manifest.json");
        var path = candidates.FirstOrDefault(p => !string.IsNullOrEmpty(p) && File.Exists(p));
        // Read before loading: loading sets the tabs, which saves the mode
        var startMode = Enum.TryParse<Mode>(_settings.LastMode, out var m) ? m : Mode.Fields;
        _currentGame = _settings.LastGame;
        _currentField = _settings.LastField;
        if (path != null && TryLoad(path))
            SetMode(startMode);
        else
        {
            SetMode(Mode.Games);
            _content.Content = Ui.V(12,
                Ui.T("Open RHI's manifest.json to start.", 18, bold: true),
                Ui.Btn("Open manifest.json...", () => _ = PickFileAsync(), accent: true));
            ((FrameworkElement)_content.Content).Margin = new Thickness(28);
            UpdateChrome();
        }
    }

    private bool TryLoad(string path)
    {
        try
        {
            var text = File.ReadAllText(path);
            var doc = new JsonDoc(text);
            if (_doc != null) _doc.Changed -= OnDocChanged;
            _doc = doc;
            _doc.Changed += OnDocChanged;
            _path = path;
            _settings.LastPath = path;
            _settings.Save();
            _itemCache.Clear();
            _pending.Clear();
            _pendingOrder.Clear();
            SetMode(_mode, rebuildContent: false);
            RebuildList();
            RunChecks();
            UpdateChrome();
            SetStatus($"Opened {Path.GetFileName(path)}.");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonParseException)
        {
            var msg = ex is JsonParseException jp
                ? $"{jp.Message} (line {JsonParser.LineCol(File.ReadAllText(path), jp.Position).line})"
                : ex.Message;
            _ = ShowMessageAsync("Couldn't open the file", $"{path}\n\n{msg}");
            return false;
        }
    }

    private async Task PickFileAsync()
    {
        if (_doc != null && (_doc.IsDirty || _pending.Count > 0))
        {
            Flush();
            if (!await Confirm("Unsaved changes", "Open another file and lose your unsaved changes?", "Open anyway", danger: true)) return;
        }
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var p = Win32.OpenFile(hwnd, "Open manifest.json", string.IsNullOrEmpty(_path) ? null : Path.GetDirectoryName(_path));
        if (p != null && TryLoad(p)) RebuildCurrentPage();
    }

    private void OnActivated(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState == WindowActivationState.Deactivated || _doc == null || _dialogOpen) return;
        try
        {
            if (!File.Exists(_path)) return;
            var disk = File.ReadAllText(_path);
            if (disk == _doc.SavedText) return;
            if (!_doc.IsDirty && _pending.Count == 0)
            {
                _doc.Reset(disk);
                SetStatus("The file changed on disk (e.g. git pull), so it was reloaded.");
            }
            else
                SetStatus("The file changed on disk since you opened it. Saving will ask before overwriting it.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonParseException)
        {
            SetStatus($"Couldn't check the file on disk: {ex.Message}");
        }
    }

    // ── Modes and lists ──────────────────────────────────────────────────────

    private void SetMode(Mode mode, bool rebuildContent = true)
    {
        Flush();
        bool changed = _mode != mode;
        SetTabs(mode);
        if (changed) _search.Text = "";  // rebuilds the list
        RebuildList();
        if (!rebuildContent || _doc == null) return;
        RebuildCurrentPage();
    }

    private void SetTabs(Mode mode)
    {
        _mode = mode;
        _fieldsTab.IsChecked = mode == Mode.Fields;
        _gamesTab.IsChecked = mode == Mode.Games;
        _globalTab.IsChecked = mode == Mode.Global;
        _search.Visibility = mode == Mode.Global ? Visibility.Collapsed : Visibility.Visible;
        _search.PlaceholderText = mode == Mode.Fields ? "Find a field (e.g. engine, nexus, blacklist)" : "Search, or type a new game name";
        _showLibrary.Visibility = mode == Mode.Games ? Visibility.Visible : Visibility.Collapsed;
        if (_settings.LastMode != mode.ToString())
        {
            _settings.LastMode = mode.ToString();
            _settings.Save();
        }
    }

    private int FieldCount(string key) => Doc.Get(key) switch
    {
        { IsArray: true } a => a.Items!.Count(i => i.IsString),
        { IsObject: true } o => o.Members!.Count,
        _ => 0,
    };

    private static string FieldSubtitle(int count) => count == 0 ? "empty" : count == 1 ? "1 game" : $"{count} games";

    private void RebuildFieldList()
    {
        _fieldSubs.Clear();
        var q = _search.Text.Trim();
        string? lastGroup = null;
        int shown = 0;
        foreach (var v in Catalog.FieldViews)
        {
            if (q.Length > 0 && !(v.Label.Contains(q, StringComparison.OrdinalIgnoreCase)
                                  || v.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                                  || v.Key.Contains(q, StringComparison.OrdinalIgnoreCase)
                                  || v.Group.Contains(q, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (v.Group != lastGroup)
            {
                lastGroup = v.Group;
                var head = Ui.T(v.Group.ToUpperInvariant(), 11, bold: true, fg: Ui.Tertiary, wrap: false);
                head.Margin = new Thickness(0, _list.Items.Count == 0 ? 0 : 10, 0, 0);
                _list.Items.Add(new ListViewItem { Content = head, IsHitTestVisible = false, IsTabStop = false, MinHeight = 0, Padding = new Thickness(12, 4, 12, 2) });
            }
            var sub = Ui.T(FieldSubtitle(FieldCount(v.Key)), 12, fg: Ui.Secondary, wrap: false);
            _fieldSubs[v.Key] = sub;
            var item = new ListViewItem { Tag = "f:" + v.Key, Content = Ui.V(0, Ui.T(v.Label, 14, wrap: false), sub) };
            ToolTipService.SetToolTip(item, $"{v.Title}\n{v.Key}");
            _list.Items.Add(item);
            if (v.Key == _currentField) _list.SelectedItem = item;
            shown++;
        }
        _listInfo.Text = q.Length > 0 ? $"{shown} field{(shown == 1 ? "" : "s")}" : $"{Catalog.FieldViews.Length} per-game fields";
    }

    private Dictionary<string, int> ComputeCounts()
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (_, game) in Catalog.GameEntries(Doc))
            counts[game] = counts.TryGetValue(game, out var c) ? c + 1 : 1;
        return counts;
    }

    private static string SubtitleFor(int count, LibraryGame? lib)
    {
        var parts = new List<string>();
        parts.Add(count == 0 ? "Not in the manifest" : count == 1 ? "1 setting" : $"{count} settings");
        if (lib != null) parts.Add(string.IsNullOrEmpty(lib.Source) ? "in library" : lib.Source);
        return string.Join(" · ", parts);
    }

    private void RebuildList()
    {
        if (_doc == null) return;
        _suppressList = true;
        try
        {
            _list.Items.Clear();
            if (_mode == Mode.Global)
            {
                foreach (var s in GlobalEditor.Sections)
                {
                    var item = new ListViewItem { Tag = "g:" + s.Id, Content = Ui.V(0, Ui.T(s.Title, 14, wrap: false), Ui.T(s.Description, 12, fg: Ui.Secondary, wrap: false)) };
                    _list.Items.Add(item);
                    if (s.Id == _currentGlobal) _list.SelectedItem = item;
                }
                _listInfo.Text = "";
                return;
            }
            if (_mode == Mode.Fields)
            {
                RebuildFieldList();
                return;
            }

            _counts = ComputeCounts();
            var lib = _library.ToDictionary(g => g.Name, StringComparer.Ordinal);
            var names = new HashSet<string>(_counts.Keys, StringComparer.Ordinal);
            if (_showLibrary.IsChecked == true) names.UnionWith(lib.Keys);

            var q = _search.Text.Trim();
            var loose = Library.Loose(q);
            IEnumerable<string> shown = names;
            if (q.Length > 0)
                shown = names.Where(n => n.Contains(q, StringComparison.OrdinalIgnoreCase) || (loose.Length > 0 && Library.Loose(n).Contains(loose)));
            var sorted = shown
                .OrderByDescending(n => q.Length > 0 && n.Equals(q, StringComparison.OrdinalIgnoreCase))
                .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (q.Length > 0 && !names.Contains(q))
            {
                var add = new ListViewItem
                {
                    Tag = "+" + q,
                    Content = Ui.V(0, Ui.T($"Add \"{q}\"", 14, bold: true, fg: Ui.AccentText, wrap: false),
                        Ui.T("New game. The name must match what RHI detects.", 12, fg: Ui.Secondary, wrap: false)),
                };
                _list.Items.Add(add);
            }
            foreach (var n in sorted)
            {
                if (!_itemCache.TryGetValue(n, out var cached))
                {
                    var sub = Ui.T("", 12, fg: Ui.Secondary, wrap: false);
                    var item = new ListViewItem { Tag = "game:" + n, Content = Ui.V(0, Ui.T(n, 14, wrap: false), sub) };
                    ToolTipService.SetToolTip(item, n);
                    cached = (item, sub);
                    _itemCache[n] = cached;
                }
                cached.Sub.Text = SubtitleFor(_counts.GetValueOrDefault(n), lib.GetValueOrDefault(n));
                _list.Items.Add(cached.Item);
                if (n == _currentGame) _list.SelectedItem = cached.Item;
            }
            int inManifest = _counts.Count;
            _listInfo.Text = q.Length > 0
                ? $"{sorted.Count} match{(sorted.Count == 1 ? "" : "es")}"
                : $"{inManifest} games in the manifest" + (_showLibrary.IsChecked == true ? $", {_library.Count} in your library" : "");
        }
        finally { _suppressList = false; }
    }

    /// <summary>Updates the list after an edit without rebuilding it, unless games were added or removed.</summary>
    private void RefreshListCounts()
    {
        if (_mode == Mode.Fields)
        {
            foreach (var (key, sub) in _fieldSubs) sub.Text = FieldSubtitle(FieldCount(key));
            return;
        }
        if (_mode != Mode.Games) return;
        var counts = ComputeCounts();
        bool sameSet = counts.Count == _counts.Count && counts.Keys.All(_counts.ContainsKey);
        if (!sameSet) { RebuildList(); return; }
        var lib = _library.ToDictionary(g => g.Name, StringComparer.Ordinal);
        foreach (var (name, count) in counts)
            if (_counts.GetValueOrDefault(name) != count && _itemCache.TryGetValue(name, out var c))
                c.Sub.Text = SubtitleFor(count, lib.GetValueOrDefault(name));
        _counts = counts;
    }

    private void OnListSelection(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressList || _list.SelectedItem is not ListViewItem { Tag: string tag }) return;
        if (tag.StartsWith("g:")) ShowGlobal(tag[2..]);
        else if (tag.StartsWith("f:")) ShowField(tag[2..]);
        else if (tag.StartsWith("game:")) OpenGame(tag[5..]);
        else if (tag.StartsWith('+')) { var n = tag[1..]; _search.Text = ""; OpenGame(n); }
    }

    // ── Pages ────────────────────────────────────────────────────────────────

    private void ShowWelcome()
    {
        _gameEditor = null;
        bool fields = _mode == Mode.Fields;
        var p = Ui.V(14,
            Ui.T(fields ? "Pick a field on the left" : "Pick a game on the left", 22, bold: true),
            Ui.T("Fields: pick one (engine versions, Nexus links, blacklist...) to see every game in it. Add, edit or remove games right there.", 14, fg: Ui.Secondary),
            Ui.T("Games: pick a game to see every setting it has on one page. Type a name in the search box to add a new one. Games from your RHI library are listed too, so you can pick the exact name RHI detects.", 14, fg: Ui.Secondary),
            Ui.T("Global: packs, presets, authors, feature flags and other settings that aren't per game.", 14, fg: Ui.Secondary),
            Ui.T("Saving only changes the lines you edited. It never reorders or reformats the file, so git diffs stay small.", 14, fg: Ui.Secondary),
            Ui.T("Shortcuts: Ctrl+S save, Ctrl+Z undo, Ctrl+Y redo, Ctrl+F search.", 13, fg: Ui.Tertiary));
        p.Margin = new Thickness(28, 24, 28, 24);
        p.MaxWidth = 760;
        p.HorizontalAlignment = HorizontalAlignment.Left;
        Visit(new Loc(_mode, null));
        _content.Content = p;
    }

    public void OpenGame(string name)
    {
        if (_doc == null) return;
        Flush();
        if (_mode != Mode.Games)
        {
            SetTabs(Mode.Games);
            _search.Text = "";
        }
        _currentGame = name;
        _settings.LastGame = name;
        _settings.Save();
        Visit(new Loc(Mode.Games, name));
        _gameEditor = new GameEditor(this, name);
        var offset = _content.VerticalOffset;
        bool same = (_content.Content as FrameworkElement)?.Tag as string == "game:" + name;
        var page = (FrameworkElement)_gameEditor.Build();
        page.Tag = "game:" + name;
        _content.Content = page;
        if (same) _content.ChangeView(null, offset, null, true);
        else _content.ChangeView(null, 0, null, true);
        RebuildList();
    }

    private void ShowGlobal(string id)
    {
        if (_doc == null) return;
        Flush();
        Visit(new Loc(Mode.Global, id));
        _currentGlobal = id;
        _gameEditor = null;
        var offset = _content.VerticalOffset;
        bool same = (_content.Content as FrameworkElement)?.Tag as string == "global:" + id;
        var page = (FrameworkElement)new GlobalEditor(this).Build(id);
        page.Tag = "global:" + id;
        _content.Content = page;
        _content.ChangeView(null, same ? offset : 0, null, true);
    }

    private void ShowField(string key)
    {
        if (_doc == null) return;
        var view = Catalog.FieldViews.FirstOrDefault(v => v.Key == key);
        if (view == null) { ShowWelcome(); return; }
        Flush();
        _currentField = key;
        if (_settings.LastField != key) { _settings.LastField = key; _settings.Save(); }
        Visit(new Loc(Mode.Fields, key));
        _gameEditor = null;
        var offset = _content.VerticalOffset;
        bool same = (_content.Content as FrameworkElement)?.Tag as string == "field:" + key;
        var page = (FrameworkElement)new FieldEditor(this, view).Build();
        page.Tag = "field:" + key;
        _content.Content = page;
        _content.ChangeView(null, same ? offset : 0, null, true);
    }

    // ── Back / forward ───────────────────────────────────────────────────────

    /// <summary>A page in the navigation history. Offset is where it was scrolled to when you left it.</summary>
    private sealed record Loc(Mode Mode, string? Id)
    {
        public double Offset { get; set; }
    }

    private readonly Stack<Loc> _back = new();
    private readonly Stack<Loc> _forward = new();
    private Loc? _here;
    private bool _navigating;

    /// <summary>Called by every page method before it replaces the page. Records the page being left.</summary>
    private void Visit(Loc loc)
    {
        // Same page rebuilt (edit, undo, list refresh). Compare Mode and Id only: Offset is part of record equality.
        if (_here != null && _here.Mode == loc.Mode && _here.Id == loc.Id) return;
        if (_here != null && !_navigating)
        {
            _here.Offset = _content.VerticalOffset;
            _back.Push(_here);
            _forward.Clear();
        }
        _here = loc;
        UpdateNavButtons();
    }

    private void GoBack() => Go(_back, _forward);
    private void GoForward() => Go(_forward, _back);

    private void Go(Stack<Loc> from, Stack<Loc> to)
    {
        if (_doc == null || _dialogOpen || from.Count == 0) return;
        Flush();
        var target = from.Pop();
        if (_here != null)
        {
            _here.Offset = _content.VerticalOffset;
            to.Push(_here);
        }
        _navigating = true;
        try
        {
            _here = null;  // so Visit records the target without pushing
            switch (target.Mode)
            {
                case Mode.Fields: _currentField = target.Id; break;
                case Mode.Games: _currentGame = target.Id; break;
                case Mode.Global: _currentGlobal = target.Id; break;
            }
            if (target.Mode == Mode.Games && target.Id != null) OpenGame(target.Id);
            else SetMode(target.Mode);
        }
        finally { _navigating = false; }
        if (_here != null) _here.Offset = target.Offset;
        // Restore the scroll position once the page has been laid out
        var offset = target.Offset;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => _content.ChangeView(null, offset, null, true));
        UpdateNavButtons();
    }

    private void UpdateNavButtons()
    {
        _backBtn.IsEnabled = _back.Count > 0;
        _forwardBtn.IsEnabled = _forward.Count > 0;
    }

    private void OnRootPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var props = e.GetCurrentPoint(_root).Properties;
        if (props.IsXButton1Pressed) { GoBack(); e.Handled = true; }
        else if (props.IsXButton2Pressed) { GoForward(); e.Handled = true; }
    }

    private void RebuildCurrentPage()
    {
        if (_mode == Mode.Fields)
        {
            if (_currentField != null) ShowField(_currentField); else ShowWelcome();
        }
        else if (_mode == Mode.Games)
        {
            if (_currentGame != null) OpenGame(_currentGame); else ShowWelcome();
        }
        else if (_currentGlobal != null) ShowGlobal(_currentGlobal);
    }

    // ── Editing ──────────────────────────────────────────────────────────────

    public void Apply(Action edit)
    {
        if (_doc == null) return;
        _selfEdit = true;
        try { edit(); }
        catch (Exception ex) when (ex is InvalidOperationException or JsonParseException or ArgumentException)
        {
            _ = ShowMessageAsync("That change couldn't be made", ex.Message + "\n\nNothing was changed.");
        }
        finally { _selfEdit = false; }
    }

    public void Defer(string id, Action edit)
    {
        if (!_pending.ContainsKey(id)) _pendingOrder.Add(id);
        _pending[id] = edit;
        _deferTimer.Stop();
        _deferTimer.Start();
        UpdateChrome();
    }

    public void Flush()
    {
        _deferTimer.Stop();
        if (_pending.Count == 0) return;
        var actions = _pendingOrder.Select(id => _pending[id]).ToList();
        _pending.Clear();
        _pendingOrder.Clear();
        foreach (var a in actions) Apply(a);
        UpdateChrome();
    }

    private void OnDocChanged()
    {
        if (!_selfEdit) RebuildCurrentPage();  // undo, redo, reload, discard
        else _gameEditor?.UpdateSummary();
        RefreshListCounts();
        UpdateChrome();
        _checkTimer.Stop();
        _checkTimer.Start();
    }

    private void Undo()
    {
        if (_doc == null) return;
        Flush();
        if (!_doc.CanUndo) return;
        _doc.Undo();
        SetStatus("Undone.");
    }

    private void Redo()
    {
        if (_doc == null) return;
        Flush();
        if (!_doc.CanRedo) return;
        _doc.Redo();
        SetStatus("Redone.");
    }

    private void UpdateChrome()
    {
        bool loaded = _doc != null;
        bool dirty = loaded && (_doc!.IsDirty || _pending.Count > 0);
        _saveBtn.IsEnabled = dirty;
        _reviewBtn.IsEnabled = dirty;
        _undoBtn.IsEnabled = loaded && (_doc!.CanUndo || _pending.Count > 0);
        _redoBtn.IsEnabled = loaded && _doc!.CanRedo;
        _checkBtn.IsEnabled = loaded;
        _fileText.Text = loaded ? _path + (dirty ? "  (unsaved changes)" : "") : "No file open";
        Title = loaded ? $"RHI Manifest Editor - {Path.GetFileName(_path)}{(dirty ? " *" : "")}" : "RHI Manifest Editor";
        int errors = _issues.Count(i => i.Severity == Severity.Error), warnings = _issues.Count - errors;
        _checkText.Text = errors > 0 ? $"Check: {errors} problem{(errors == 1 ? "" : "s")}"
                        : warnings > 0 ? $"Check: {warnings} warning{(warnings == 1 ? "" : "s")}" : "Check: OK";
        _checkText.Foreground = errors > 0 ? Ui.Critical : warnings > 0 ? Ui.Caution : Ui.Success;
    }

    private void RunChecks()
    {
        if (_doc == null) return;
        _issues = Checks.Run(_doc, _path);
        UpdateChrome();
    }

    public void SetStatus(string text) => _statusText.Text = $"{DateTime.Now:HH:mm}  {text}";

    // ── Save ─────────────────────────────────────────────────────────────────

    private async Task<bool> SaveAsync()
    {
        if (_doc == null || _dialogOpen) return false;
        Flush();
        if (!_doc.IsDirty) { SetStatus("Nothing to save."); return true; }

        RunChecks();
        var errors = _issues.Where(i => i.Severity == Severity.Error).ToList();
        if (errors.Count > 0 && !await Confirm("Problems found",
                "These would break the manifest for users:\n\n" + string.Join("\n\n", errors.Select(e => "- " + e.Message)) +
                "\n\nFix them before pushing. Save anyway?", "Save anyway", danger: true))
            return false;

        try
        {
            if (File.Exists(_path))
            {
                var disk = File.ReadAllText(_path);
                if (disk != _doc.SavedText && !await Confirm("File changed on disk",
                        "manifest.json changed since you opened it (e.g. git pull or another editor). Saving replaces those changes with yours.\n\nTip: Review changes shows only your edits. Save anyway?",
                        "Overwrite", danger: true))
                    return false;
            }
            var bytes = File.Exists(_path) ? File.ReadAllBytes(_path) : Array.Empty<byte>();
            bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            File.WriteAllText(_path, _doc.Text, new UTF8Encoding(bom));
            _doc.MarkSaved();
            UpdateChrome();
            SetStatus("Saved. Pushing manifest.json to main makes it live for every user.");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await ShowMessageAsync("Couldn't save", ex.Message);
            return false;
        }
    }

    private async void OnClosing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        try
        {
            if (_forceClose || _doc == null) return;
            Flush();
            if (!_doc.IsDirty) return;
            args.Cancel = true;
            if (_dialogOpen) return;
            var d = MakeDialog("Save changes?", Ui.T("You have unsaved changes to the manifest.", 14));
            d.PrimaryButtonText = "Save";
            d.SecondaryButtonText = "Don't save";
            d.CloseButtonText = "Cancel";
            d.DefaultButton = ContentDialogButton.Primary;
            var r = await ShowDialogAsync(d);
            if (r == ContentDialogResult.Primary && !await SaveAsync()) return;
            if (r == ContentDialogResult.None) return;
            _forceClose = true;
            Close();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow.OnClosing] {ex}");
        }
    }

    // ── Dialogs ──────────────────────────────────────────────────────────────

    private ContentDialog MakeDialog(string title, UIElement content)
    {
        var d = new ContentDialog
        {
            Title = title,
            Content = content,
            XamlRoot = _root.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        d.Resources["ContentDialogMaxWidth"] = 1000.0;
        return d;
    }

    private async Task<ContentDialogResult> ShowDialogAsync(ContentDialog d)
    {
        if (_dialogOpen) return ContentDialogResult.None;
        _dialogOpen = true;
        try { return await d.ShowAsync(); }
        finally { _dialogOpen = false; }
    }

    private async Task ShowMessageAsync(string title, string text)
    {
        var d = MakeDialog(title, new ScrollViewer { Content = Ui.T(text, 14), MaxHeight = 500 });
        d.CloseButtonText = "OK";
        await ShowDialogAsync(d);
    }

    public async Task<bool> Confirm(string title, string text, string yes, bool danger = false)
    {
        var d = MakeDialog(title, new ScrollViewer { Content = Ui.T(text, 14), MaxHeight = 520 });
        d.PrimaryButtonText = yes;
        d.CloseButtonText = "Cancel";
        d.DefaultButton = danger ? ContentDialogButton.Close : ContentDialogButton.Primary;
        return await ShowDialogAsync(d) == ContentDialogResult.Primary;
    }

    public async Task<string?> Prompt(string title, string text, string initial, string yes = "OK")
    {
        var box = new TextBox { Text = initial, MinWidth = 420 };
        box.Loaded += (_, _) => { box.Focus(FocusState.Programmatic); box.SelectAll(); };
        var d = MakeDialog(title, Ui.V(10, Ui.T(text, 14, fg: Ui.Secondary), box));
        d.PrimaryButtonText = yes;
        d.CloseButtonText = "Cancel";
        d.DefaultButton = ContentDialogButton.Primary;
        return await ShowDialogAsync(d) == ContentDialogResult.Primary ? box.Text : null;
    }

    private async Task ShowReviewAsync()
    {
        if (_doc == null) return;
        Flush();
        var lines = LineDiff.Compute(_doc.SavedText, _doc.Text);
        int added = lines.Count(l => l.Kind == DiffKind.Added), removed = lines.Count(l => l.Kind == DiffKind.Removed);
        var scroll = DiffView(lines, "No changes since the last save.", 620);
        var d = MakeDialog($"Changes since the last save (+{added} / -{removed} lines)", scroll);
        d.PrimaryButtonText = "Save";
        d.SecondaryButtonText = "Discard all changes";
        d.CloseButtonText = "Close";
        d.DefaultButton = ContentDialogButton.Close;
        var r = await ShowDialogAsync(d);
        if (r == ContentDialogResult.Primary) await SaveAsync();
        else if (r == ContentDialogResult.Secondary
                 && await Confirm("Discard all changes", "Go back to the file as it was last saved? (You can still undo this.)", "Discard", danger: true))
        {
            _doc.ReplaceAll(_doc.SavedText);
            SetStatus("Changes discarded.");
        }
    }

    /// <summary>Line diff as coloured monospace rows in a scroll viewer.</summary>
    private static ScrollViewer DiffView(List<DiffLine> lines, string emptyText, double maxHeight)
    {
        var panel = new StackPanel();
        var mono = new FontFamily("Cascadia Mono, Consolas");
        if (lines.Count == 0) panel.Children.Add(Ui.T(emptyText, 14));
        foreach (var l in lines)
        {
            if (l.Kind == DiffKind.Gap)
            {
                panel.Children.Add(new TextBlock { Text = "...", Foreground = Ui.Tertiary, FontFamily = mono, Margin = new Thickness(0, 4, 0, 4) });
                continue;
            }
            var (prefix, fg, bg) = l.Kind switch
            {
                DiffKind.Added => ("+ ", Ui.Success, new SolidColorBrush(Windows.UI.Color.FromArgb(40, 60, 200, 90))),
                DiffKind.Removed => ("- ", Ui.Critical, new SolidColorBrush(Windows.UI.Color.FromArgb(40, 230, 70, 70))),
                _ => ("  ", Ui.Secondary, null as Brush),
            };
            var num = (l.NewLine ?? l.OldLine)?.ToString() ?? "";
            panel.Children.Add(new Border
            {
                Background = bg,
                Child = new TextBlock
                {
                    Text = $"{num,5} {prefix}{l.Text}",
                    Foreground = fg,
                    FontFamily = mono,
                    FontSize = 12,
                    TextWrapping = TextWrapping.NoWrap,
                    IsTextSelectionEnabled = true,
                },
            });
        }
        return new ScrollViewer
        {
            Content = panel,
            MaxHeight = maxHeight,
            Width = 940,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Auto,
        };
    }

    private async Task ShowIssuesAsync()
    {
        if (_doc == null) return;
        Flush();
        RunChecks();
        var panel = new StackPanel { Spacing = 10 };
        ContentDialog? dialog = null;
        if (_issues.Count == 0)
            panel.Children.Add(Ui.T("No problems found: valid JSON, no duplicate keys, no keys that differ only by case, no conflicting game settings.", 14, fg: Ui.Success));
        foreach (var issue in _issues.OrderBy(i => i.Severity))
        {
            var row = Ui.V(4,
                Ui.T(issue.Severity == Severity.Error ? "Problem" : "Warning", 12, bold: true,
                    fg: issue.Severity == Severity.Error ? Ui.Critical : Ui.Caution),
                Ui.T(issue.Message, 14));
            if (issue.Game != null)
            {
                var g = issue.Game;
                var link = new HyperlinkButton { Content = $"Open {g}", Padding = new Thickness(0) };
                link.Click += (_, _) => { dialog?.Hide(); OpenGame(g); };
                row.Children.Add(link);
            }
            panel.Children.Add(Ui.Card(row, 12));
        }
        panel.Children.Add(Ui.Help("Problems would make the app reject the manifest, so every user would keep the old one. These are the same checks as tools/check-manifest.py."));
        dialog = MakeDialog("Manifest check", new ScrollViewer { Content = panel, MaxHeight = 600, Width = 760 });
        dialog.CloseButtonText = "Close";
        await ShowDialogAsync(dialog);
    }
}
