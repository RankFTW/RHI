// CollectionManagerDialog.cs -- the Manage collections window (one ContentDialog for Classic, Tabs and
// Spotlight). Left: the collections with their game counts, New / Rename / Delete / Move up / Move down
// (name editing and the delete confirmation are inline: WinUI can't open a second ContentDialog).
// Right: the games, a tick per game. "In collection" lists the collection's games (including ones no
// longer detected, so they can be taken out); "All games" lists the library to tick games in. Search
// narrows both; Add / Remove all listed work on everything the search matches. Every change goes
// through MainViewModel (saved by the debounced SaveNameMappings, list refiltered).
// Controller: everything is a Button or CheckBox (A presses / toggles); the selected collection, or the
// inline editor's button while it's open, is tagged "PadStart". Buttons whose look changes are
// replaced, not restyled (WinUI reads hover colours once), and get focus back through _focus.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using RenoDXCommander.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

internal sealed class CollectionManagerDialog
{
    private const int PageSize = 150;
    private const string PadStartTag = "PadStart";

    private static readonly TabsTheme.Look RowLook = new("#141A26", "#263042", TabsTheme.TextSecondary, "#1E2738", false);
    private static readonly TabsTheme.Look RowSelectedLook = new("#22304A", TabsTheme.Accent, TabsTheme.TextPrimary, "#22304A", true);

    private readonly MainViewModel _vm;
    private readonly Action<Control> _focus;
    private readonly bool _pad;         // opened with the controller: start on buttons, not in text boxes
    private readonly ContentDialog _dialog;

    private string? _selected;
    private bool _showAll;
    private int _limit = PageSize;
    private string? _editing;          // null = not editing; "" = new collection; else the collection being renamed
    private bool _confirmingDelete;
    private bool _confirmBulkRemove;

    // Left
    private readonly StackPanel _listPanel = new() { Spacing = 4 };
    private readonly StackPanel _editPanel = new() { Spacing = 6, Visibility = Visibility.Collapsed };
    private readonly TextBox _nameBox = new() { MaxLength = GameCollectionStore.MaxNameLength, PlaceholderText = "e.g. Racing" };
    private readonly TextBlock _nameError = MakeText(12, "#FF9AA8");
    private Button _saveBtn = new();
    private readonly StackPanel _confirmPanel = new() { Spacing = 6, Visibility = Visibility.Collapsed };
    private readonly TextBlock _confirmText = MakeText(12, TabsTheme.TextSecondary);
    private readonly WrapPanel _actions = new() { HorizontalSpacing = 6, VerticalSpacing = 6 };
    private Button _renameBtn = new(), _deleteBtn = new(), _upBtn = new(), _downBtn = new();

    // Right
    private readonly TextBlock _title = new() { FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _subtitle = MakeText(12, TabsTheme.TextMuted);
    private readonly TextBox _search = new() { PlaceholderText = "Search games" };
    private readonly Grid _segmentHost = new() { ColumnSpacing = 4 };
    private readonly StackPanel _gamesPanel = new() { Spacing = 2 };
    private readonly ScrollViewer _gamesScroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly WrapPanel _bulkActions = new() { HorizontalSpacing = 6, VerticalSpacing = 6 };
    private readonly TextBlock _status = MakeText(12, TabsTheme.TextMuted);
    private readonly Grid _rightContent = new() { RowSpacing = 10 };
    private readonly TextBlock _noCollections = MakeText(13, TabsTheme.TextSecondary);

    private readonly DispatcherTimer _searchDebounce = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private List<Entry> _matches = new();

    /// <summary>One row on the right: a detected game (Card set) or a member that is no longer detected.</summary>
    private sealed record Entry(string Key, string Name, string Store, GameCardViewModel? Card);

    public CollectionManagerDialog(MainViewModel vm, XamlRoot xamlRoot, string? select, bool pad, Action<Control> focus)
    {
        _vm = vm;
        _focus = focus;
        _pad = pad;
        _selected = vm.CollectionStore.GetName(select) ?? vm.CollectionNames.FirstOrDefault();
        // An empty collection opens on All games (there's nothing to show in it yet)
        _showAll = _selected != null && (vm.CollectionStore.GetMembers(_selected)?.Count ?? 0) == 0;

        double height = Math.Clamp(xamlRoot.Size.Height - 230, 320, 560);
        var root = new Grid { Width = 860, Height = height, ColumnSpacing = 20 };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = BuildLeft();
        var right = BuildRight();
        Grid.SetColumn(right, 1);
        root.Children.Add(left);
        root.Children.Add(right);

        _dialog = new ContentDialog
        {
            Title = "Manage collections",
            Content = root,
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.None,
            XamlRoot = xamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        _dialog.Resources["ContentDialogMaxWidth"] = 960.0;
        _dialog.Resources["ContentDialogMaxHeight"] = Math.Max(500, xamlRoot.Size.Height - 40);

        _searchDebounce.Tick += (s, e) => { _searchDebounce.Stop(); _limit = PageSize; RefreshGames(); };
        _search.TextChanged += (s, e) => { _searchDebounce.Stop(); _searchDebounce.Start(); };
        _search.KeyDown += (s, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            e.Handled = true;
            _searchDebounce.Stop();
            _limit = PageSize;
            RefreshGames();
        };
        _dialog.Closed += (s, e) => _searchDebounce.Stop();

        RefreshAll();
    }

    public async Task ShowAsync()
    {
        CrashReporter.Log($"[CollectionManagerDialog.ShowAsync] open ({_vm.CollectionNames.Count} collections, selected '{_selected ?? "(none)"}')");
        await DialogService.ShowSafeAsync(_dialog);
    }

    // ── Layout ────────────────────────────────────────────────────────────────

    private Grid BuildLeft()
    {
        var left = new Grid { RowSpacing = 10 };
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = MakeText(12, TabsTheme.TextMuted);
        heading.Text = "COLLECTIONS";
        heading.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        left.Children.Add(heading);

        var listScroll = new ScrollViewer { Content = _listPanel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 10, 0) };
        Grid.SetRow(listScroll, 1);
        left.Children.Add(listScroll);

        // Inline name editor (New / Rename)
        _nameBox.KeyDown += (s, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            e.Handled = true;
            SaveName();
        };
        _nameBox.TextChanged += (s, e) => CheckName(false);
        Grid.SetRow(_editPanel, 2);
        left.Children.Add(_editPanel);

        Grid.SetRow(_confirmPanel, 3);
        left.Children.Add(_confirmPanel);

        Grid.SetRow(_actions, 4);
        left.Children.Add(_actions);
        return left;
    }

    private Grid BuildRight()
    {
        var right = new Grid();
        _rightContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _rightContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _rightContent.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _rightContent.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel { Spacing = 2 };
        header.Children.Add(_title);
        header.Children.Add(_subtitle);
        _rightContent.Children.Add(header);

        var toolbar = new Grid { ColumnSpacing = 8 };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.Children.Add(_search);
        Grid.SetColumn(_segmentHost, 1);
        toolbar.Children.Add(_segmentHost);
        Grid.SetRow(toolbar, 1);
        _rightContent.Children.Add(toolbar);

        _gamesScroll.Content = _gamesPanel;
        _gamesScroll.Padding = new Thickness(0, 0, 12, 0);
        Grid.SetRow(_gamesScroll, 2);
        _rightContent.Children.Add(_gamesScroll);

        var footer = new StackPanel { Spacing = 6 };
        footer.Children.Add(_bulkActions);
        footer.Children.Add(_status);
        Grid.SetRow(footer, 3);
        _rightContent.Children.Add(footer);

        _noCollections.Text = "You don't have any collections yet. Click New to make one, then tick the games that belong in it. A game can be in as many collections as you like.";
        _noCollections.VerticalAlignment = VerticalAlignment.Center;

        right.Children.Add(_rightContent);
        right.Children.Add(_noCollections);
        return right;
    }

    // ── Refresh ───────────────────────────────────────────────────────────────

    private void RefreshAll()
    {
        RefreshList();
        RefreshActions();
        RefreshRight();
    }

    private void RefreshList(bool focusSelected = false)
    {
        _listPanel.Children.Clear();
        Button? selectedBtn = null;
        foreach (var name in _vm.CollectionNames)
        {
            bool on = string.Equals(name, _selected, StringComparison.OrdinalIgnoreCase);
            int count = _vm.CollectionStore.GetMembers(name)?.Count ?? 0;
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            var countText = new TextBlock { Text = count.ToString(), Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(countText, 1);
            row.Children.Add(countText);

            var btn = new Button
            {
                Content = row,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(12, 0, 12, 0),
                Tag = on && _editing == null && !_confirmingDelete ? PadStartTag : null,
            };
            TabsTheme.Apply(btn, on ? RowSelectedLook : RowLook, height: 34, fontSize: 13);
            ToolTipService.SetToolTip(btn, $"{name}: {count} {(count == 1 ? "game" : "games")}");
            string n = name;
            btn.Click += (s, e) => Select(n);
            _listPanel.Children.Add(btn);
            if (on) selectedBtn = btn;
        }
        if (focusSelected && selectedBtn != null) FocusWhenLoaded(selectedBtn);
    }

    private void RefreshActions()
    {
        _actions.Children.Clear();
        bool has = _selected != null;
        int index = has ? _vm.CollectionStore.IndexOf(_selected!) : -1;
        int total = _vm.CollectionNames.Count;
        var newBtn = MakeButton("New", TabsTheme.BlueButton, () => StartEdit(""));
        ToolTipService.SetToolTip(newBtn, "Make a new collection.");
        _renameBtn = MakeButton("Rename", TabsTheme.IconButton, () => { if (_selected != null) StartEdit(_selected); });
        _renameBtn.IsEnabled = has;
        _upBtn = MakeButton("Move up", TabsTheme.IconButton, () => Move(-1));
        _upBtn.IsEnabled = has && index > 0;
        ToolTipService.SetToolTip(_upBtn, "Move this collection earlier in the collection chips.");
        _downBtn = MakeButton("Move down", TabsTheme.IconButton, () => Move(1));
        _downBtn.IsEnabled = has && index >= 0 && index < total - 1;
        ToolTipService.SetToolTip(_downBtn, "Move this collection later in the collection chips.");
        _deleteBtn = MakeButton("Delete", TabsTheme.DangerButton, StartDelete);
        _deleteBtn.IsEnabled = has;
        ToolTipService.SetToolTip(_deleteBtn, "Delete this collection. The games stay in your library.");
        _actions.Children.Add(newBtn);
        _actions.Children.Add(_renameBtn);
        _actions.Children.Add(_upBtn);
        _actions.Children.Add(_downBtn);
        _actions.Children.Add(_deleteBtn);
    }

    private void RefreshRight()
    {
        bool has = _selected != null;
        _rightContent.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        _noCollections.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
        if (!has) return;
        _title.Text = _selected;
        RefreshSubtitle();
        RefreshSegment(false);
        RefreshGames();
    }

    private void RefreshSubtitle()
    {
        if (_selected == null) return;
        int count = _vm.CollectionStore.GetMembers(_selected)?.Count ?? 0;
        _subtitle.Text = $"{count} {(count == 1 ? "game" : "games")}. Tick a game to add it, untick to take it out. Hidden games stay out of the game list.";
    }

    /// <summary>In collection / All games switch (rebuilt when it changes; refocuses the pressed side).</summary>
    private void RefreshSegment(bool focusSelected)
    {
        _segmentHost.Children.Clear();
        _segmentHost.ColumnDefinitions.Clear();
        Button? selectedBtn = null;
        int col = 0;
        foreach (var (text, all) in new[] { ("In collection", false), ("All games", true) })
        {
            bool on = all == _showAll;
            var b = new Button { Content = text, Padding = new Thickness(12, 0, 12, 0) };
            TabsTheme.Apply(b, on ? RowSelectedLook : RowLook, height: 32, fontSize: 12);
            bool a = all;
            b.Click += (s, e) =>
            {
                if (_showAll == a) return;
                _showAll = a;
                _limit = PageSize;
                RefreshSegment(true);
                RefreshGames();
            };
            ToolTipService.SetToolTip(b, all ? "Every game in your library, ticked when it's in this collection." : "Only the games in this collection.");
            _segmentHost.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(b, col++);
            _segmentHost.Children.Add(b);
            if (on) selectedBtn = b;
        }
        if (focusSelected && selectedBtn != null) FocusWhenLoaded(selectedBtn);
    }

    private void RefreshGames()
    {
        if (_selected == null) return;
        try
        {
            _confirmBulkRemove = false;
            var members = _vm.CollectionStore.GetMembers(_selected) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var query = (_search.Text ?? "").Trim();
            _matches = BuildEntries(members)
                .Where(e => query.Length == 0 || e.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

            _gamesPanel.Children.Clear();
            foreach (var entry in _matches.Take(_limit))
                _gamesPanel.Children.Add(MakeGameRow(entry, members.Contains(entry.Key)));

            if (_matches.Count > _limit)
            {
                int left = _matches.Count - _limit;
                var more = MakeButton($"Show more ({left} more)", TabsTheme.IconButton, () =>
                {
                    int firstNew = _limit;
                    _limit += PageSize;
                    RefreshGames();
                    if (firstNew < _gamesPanel.Children.Count && _gamesPanel.Children[firstNew] is Control c) FocusWhenLoaded(c);
                });
                more.Margin = new Thickness(0, 6, 0, 0);
                _gamesPanel.Children.Add(more);
            }
            if (_matches.Count == 0)
            {
                var empty = MakeText(13, TabsTheme.TextMuted);
                empty.Margin = new Thickness(0, 8, 0, 0);
                empty.Text = query.Length > 0 ? $"No games match \"{query}\"."
                    : _showAll ? "No games detected yet."
                    : "No games in this collection yet. Switch to All games and tick the ones you want.";
                _gamesPanel.Children.Add(empty);
            }
            RefreshBulkActions();
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[CollectionManagerDialog.RefreshGames] {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// All games: every detected game (one per "GameName|Store") plus members no longer detected.
    /// In collection: only members. Sorted by name.
    /// </summary>
    private List<Entry> BuildEntries(IReadOnlySet<string> members)
    {
        var byKey = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        foreach (var card in _vm.AllCards.ToList())
        {
            var key = MainViewModel.CollectionKeyOf(card);
            if (byKey.ContainsKey(key)) continue;
            if (!_showAll && !members.Contains(key)) continue;
            byKey[key] = new Entry(key, card.GameName, card.Source ?? "", card);
        }
        foreach (var key in members)
        {
            if (byKey.ContainsKey(key)) continue;
            var parsed = GameKey.Parse(key);
            byKey[key] = new Entry(key, parsed.Name, parsed.Store, null);
        }
        return byKey.Values
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Store, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private CheckBox MakeGameRow(Entry entry, bool member)
    {
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = entry.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        string note = entry.Card == null ? "not detected"
            : entry.Card.IsHidden ? "hidden"
            : "";
        var store = new TextBlock
        {
            Text = string.Join(" · ", new[] { entry.Store, note }.Where(t => !string.IsNullOrEmpty(t))),
            FontSize = 12,
            Foreground = TabsTheme.B(TabsTheme.TextMuted),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(store, 1);
        grid.Children.Add(store);

        var box = new CheckBox
        {
            Content = grid,
            IsChecked = member,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            MinHeight = 32,
            Padding = new Thickness(8, 0, 0, 0),
        };
        if (entry.Card == null)
            ToolTipService.SetToolTip(box, $"{entry.Name} isn't detected right now. It stays in the collection until you untick it.");
        // Handlers after IsChecked is set, so building the row changes nothing
        box.Checked += (s, e) => SetMember(entry, true);
        box.Unchecked += (s, e) => SetMember(entry, false);
        return box;
    }

    private void RefreshBulkActions()
    {
        _bulkActions.Children.Clear();
        if (_selected == null) return;
        var members = _vm.CollectionStore.GetMembers(_selected) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int toAdd = _matches.Count(e => !members.Contains(e.Key));
        int toRemove = _matches.Count(e => members.Contains(e.Key));
        bool searching = !string.IsNullOrWhiteSpace(_search.Text);

        if (_showAll)
        {
            var add = MakeButton(searching ? $"Add all matching ({toAdd})" : $"Add all ({toAdd})", TabsTheme.BlueButton, () => Bulk(true));
            add.IsEnabled = toAdd > 0;
            ToolTipService.SetToolTip(add, "Add every game in the list below (all matches, not only the ones shown) to this collection.");
            _bulkActions.Children.Add(add);
        }
        string removeText = _confirmBulkRemove ? $"Click again to remove {toRemove} games"
            : searching ? $"Remove all matching ({toRemove})" : $"Remove all ({toRemove})";
        var remove = MakeButton(removeText, TabsTheme.DangerButton, () =>
        {
            // Many games at once: ask with a second click (the games themselves are never touched)
            if (toRemove > 5 && !_confirmBulkRemove)
            {
                _confirmBulkRemove = true;
                RefreshBulkActions();
                if (_bulkActions.Children.LastOrDefault() is Control c) FocusWhenLoaded(c);
                return;
            }
            Bulk(false);
        });
        remove.IsEnabled = toRemove > 0;
        ToolTipService.SetToolTip(remove, "Take every game in the list below out of this collection. The games stay in your library.");
        _bulkActions.Children.Add(remove);
    }

    // ── Changes ───────────────────────────────────────────────────────────────

    private void Select(string name)
    {
        if (string.Equals(name, _selected, StringComparison.OrdinalIgnoreCase)) return;
        CloseEditors();
        _selected = name;
        _limit = PageSize;
        _showAll = (_vm.CollectionStore.GetMembers(name)?.Count ?? 0) == 0;
        RefreshList(focusSelected: true);
        RefreshActions();
        RefreshRight();
    }

    private void SetMember(Entry entry, bool member)
    {
        if (_selected == null) return;
        try
        {
            if (_vm.SetCollectionMembership(_selected, new[] { entry.Key }, member) == 0) return;
            _status.Text = member ? $"Added {entry.Name} to {_selected}." : $"Removed {entry.Name} from {_selected}.";
            _confirmBulkRemove = false;
            AfterMembershipChange();
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[CollectionManagerDialog.SetMember] {ex.Message}");
        }
    }

    private void Bulk(bool member)
    {
        if (_selected == null) return;
        try
        {
            var keys = _matches.Select(e => e.Key).ToList();
            int changed = _vm.SetCollectionMembership(_selected, keys, member);
            _status.Text = member
                ? $"Added {changed} {(changed == 1 ? "game" : "games")} to {_selected}."
                : $"Removed {changed} {(changed == 1 ? "game" : "games")} from {_selected}.";
            _confirmBulkRemove = false;
            // Keep the rows where they are (unticked ones stay listed until the view changes)
            // The store has already changed, so the rows' own handlers find nothing to do
            foreach (var child in _gamesPanel.Children)
                if (child is CheckBox box && box.IsChecked != member) box.IsChecked = member;
            AfterMembershipChange();
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[CollectionManagerDialog.Bulk] {ex.Message}");
        }
    }

    private void AfterMembershipChange()
    {
        RefreshSubtitle();
        UpdateCounts();
        RefreshBulkActions();
    }

    /// <summary>The count beside each collection on the left, without rebuilding the buttons (keeps focus).</summary>
    private void UpdateCounts()
    {
        var names = _vm.CollectionNames;
        for (int i = 0; i < _listPanel.Children.Count && i < names.Count; i++)
        {
            if (_listPanel.Children[i] is not Button { Content: Grid row } btn || row.Children.Count < 2 || row.Children[1] is not TextBlock count) continue;
            int n = _vm.CollectionStore.GetMembers(names[i])?.Count ?? 0;
            count.Text = n.ToString();
            ToolTipService.SetToolTip(btn, $"{names[i]}: {n} {(n == 1 ? "game" : "games")}");
        }
    }

    private void Move(int delta)
    {
        if (_selected == null || !_vm.MoveCollection(_selected, delta)) return;
        RefreshList();
        RefreshActions();
        // Keep focus on the same button for a controller or keyboard user (it was replaced)
        var again = delta < 0 ? _upBtn : _downBtn;
        FocusWhenLoaded(again.IsEnabled ? again : delta < 0 ? _downBtn : _upBtn);
    }

    // ── New / Rename (inline) ─────────────────────────────────────────────────

    private void StartEdit(string renaming)
    {
        CloseEditors();
        _editing = renaming;
        bool isNew = renaming.Length == 0;
        _editPanel.Children.Clear();
        var label = MakeText(12, TabsTheme.TextSecondary);
        label.Text = isNew ? "Name for the new collection" : $"New name for {renaming}";
        _editPanel.Children.Add(label);
        _nameBox.Text = isNew ? SuggestName() : renaming;
        _editPanel.Children.Add(_nameBox);
        _editPanel.Children.Add(_nameError);
        var buttons = new WrapPanel { HorizontalSpacing = 6, VerticalSpacing = 6 };
        _saveBtn = MakeButton(isNew ? "Create" : "Save", TabsTheme.BlueButton, SaveName);
        _saveBtn.Tag = PadStartTag;
        var cancel = MakeButton("Cancel", TabsTheme.IconButton, () => { CloseEditors(); RefreshList(focusSelected: true); });
        buttons.Children.Add(_saveBtn);
        buttons.Children.Add(cancel);
        _editPanel.Children.Add(buttons);
        _editPanel.Visibility = Visibility.Visible;
        RefreshList(); // drops the PadStart tag from the selected collection
        CheckName(false);
        _nameBox.SelectAll();
        // Keyboard / mouse: type straight away. Controller: the name is already filled in, so start on Create
        _nameBox.Loaded += NameBoxLoaded;
        if (_nameBox.IsLoaded) NameBoxLoaded(_nameBox, new RoutedEventArgs());
    }

    private void NameBoxLoaded(object sender, RoutedEventArgs e)
    {
        _nameBox.Loaded -= NameBoxLoaded;
        if (_pad) FocusWhenLoaded(_saveBtn);
        else _dialog.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            _focus(_nameBox);
            _nameBox.SelectAll();
        });
    }

    /// <summary>"Collection 1", "Collection 2"… the first free one (a controller can't type easily).</summary>
    private string SuggestName()
    {
        for (int i = 1; i < 1000; i++)
        {
            var name = $"Collection {i}";
            if (!_vm.CollectionStore.Exists(name)) return name;
        }
        return "";
    }

    private string? CheckName(bool showEmpty)
    {
        var problem = _vm.CollectionStore.ValidateName(_nameBox.Text, string.IsNullOrEmpty(_editing) ? null : _editing, out _);
        _saveBtn.IsEnabled = problem == null;
        bool show = problem != null && (showEmpty || !string.IsNullOrWhiteSpace(_nameBox.Text));
        _nameError.Text = problem ?? "";
        _nameError.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        return problem;
    }

    private void SaveName()
    {
        if (_editing == null) return;
        if (CheckName(true) != null) return;
        try
        {
            if (_editing.Length == 0)
            {
                var error = _vm.CreateCollection(_nameBox.Text, (IReadOnlyList<GameCardViewModel>?)null, out var created);
                if (error != null) { _nameError.Text = error; _nameError.Visibility = Visibility.Visible; return; }
                _selected = created;
                _showAll = true; // a new collection is empty: show the library to tick games in
                _status.Text = $"Created {created}. Tick the games that belong in it.";
            }
            else
            {
                var error = _vm.RenameCollection(_editing, _nameBox.Text);
                if (error != null) { _nameError.Text = error; _nameError.Visibility = Visibility.Visible; return; }
                _selected = _vm.CollectionStore.GetName(_nameBox.Text.Trim()) ?? _selected;
                _status.Text = $"Renamed to {_selected}.";
            }
            CloseEditors();
            _limit = PageSize;
            RefreshList(focusSelected: true);
            RefreshActions();
            RefreshRight();
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[CollectionManagerDialog.SaveName] {ex.Message}");
        }
    }

    // ── Delete (inline confirmation) ──────────────────────────────────────────

    private void StartDelete()
    {
        if (_selected == null) return;
        CloseEditors();
        _confirmingDelete = true;
        int count = _vm.CollectionStore.GetMembers(_selected)?.Count ?? 0;
        _confirmPanel.Children.Clear();
        _confirmText.Text = $"Delete \"{_selected}\"{(count > 0 ? $" ({count} {(count == 1 ? "game" : "games")})" : "")}? Only the collection is removed. The games stay in your library with their mods and settings.";
        _confirmPanel.Children.Add(_confirmText);
        var buttons = new WrapPanel { HorizontalSpacing = 6, VerticalSpacing = 6 };
        var delete = MakeButton("Delete", TabsTheme.DangerButton, ConfirmDelete);
        var cancel = MakeButton("Cancel", TabsTheme.IconButton, () => { CloseEditors(); RefreshList(focusSelected: true); });
        cancel.Tag = PadStartTag; // Cancel is the safe default
        buttons.Children.Add(delete);
        buttons.Children.Add(cancel);
        _confirmPanel.Children.Add(buttons);
        _confirmPanel.Visibility = Visibility.Visible;
        RefreshList();
        FocusWhenLoaded(cancel);
    }

    private void ConfirmDelete()
    {
        if (_selected == null) return;
        try
        {
            var names = _vm.CollectionNames.ToList();
            int index = names.FindIndex(n => n.Equals(_selected, StringComparison.OrdinalIgnoreCase));
            var deleted = _selected;
            _vm.DeleteCollection(deleted);
            var left = _vm.CollectionNames;
            _selected = left.Count == 0 ? null : left[Math.Clamp(index, 0, left.Count - 1)];
            _showAll = _selected != null && (_vm.CollectionStore.GetMembers(_selected)?.Count ?? 0) == 0;
            _status.Text = $"Deleted {deleted}.";
            CloseEditors();
            _limit = PageSize;
            RefreshList(focusSelected: true);
            RefreshActions();
            RefreshRight();
            if (_selected == null && _actions.Children.FirstOrDefault() is Control newBtn) FocusWhenLoaded(newBtn);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[CollectionManagerDialog.ConfirmDelete] {ex.Message}");
        }
    }

    private void CloseEditors()
    {
        _editing = null;
        _confirmingDelete = false;
        _editPanel.Visibility = Visibility.Collapsed;
        _confirmPanel.Visibility = Visibility.Collapsed;
        _nameError.Visibility = Visibility.Collapsed;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void FocusWhenLoaded(Control target)
    {
        _dialog.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            try { _focus(target); }
            catch (Exception ex) { CrashReporter.Log($"[CollectionManagerDialog.Focus] {ex.Message}"); }
        });
    }

    private static Button MakeButton(string text, TabsTheme.Look look, Action onClick)
    {
        var b = new Button { Content = text, Padding = new Thickness(12, 0, 12, 0) };
        TabsTheme.Apply(b, look, height: 32, fontSize: 12);
        b.Click += (s, e) =>
        {
            try { onClick(); }
            catch (Exception ex) { CrashReporter.Log($"[CollectionManagerDialog.Button '{text}'] {ex.Message}"); }
        };
        return b;
    }

    private static TextBlock MakeText(double size, string hex) => new()
    {
        FontSize = size,
        TextWrapping = TextWrapping.Wrap,
        Foreground = TabsTheme.B(hex),
    };
}
