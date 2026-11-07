// MainWindow.Collections.cs -- game collections in the window: the chip row (one chip per collection
// plus "+ Collection") under the All / Installed / Favourites switch, the chip's menu (Rename, Move,
// Delete), the game menu's "Add to collection" submenu, the name / delete dialogs and the empty-list
// message. Built in C# (XAML constraint). The chips are Buttons so a controller can focus them.
// Placement: Classic / Tabs = in the sidebar's search stack under the switch; Spotlight = at the right
// of the search row, just left of the Filters button (an extra first column of _countsGrid).

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    private const string CollectionChipSelectedTag = "CollectionSelected";
    private const string CollectionNewChipTag = "CollectionNew";

    private Border? _collectionsHost;
    private Controls.WrapPanel? _collectionsWrap;
    private string? _collectionsKey;
    private TextBlock? _collectionEmptyText;

    private static readonly TabsTheme.Look ChipLook = new("#141A26", "#263042", TabsTheme.TextSecondary, "#1E2738", false);
    private static readonly TabsTheme.Look ChipSelectedLook = new("#22304A", TabsTheme.Accent, TabsTheme.TextPrimary, "#22304A", true);

    /// <summary>Builds the chip row and the empty-list message once (from BuildTabsChrome).</summary>
    private void EnsureCollectionsRow()
    {
        if (_collectionsHost != null) return;
        _collectionsWrap = new Controls.WrapPanel { HorizontalSpacing = 6, VerticalSpacing = 6 };
        _collectionsHost = new Border
        {
            Child = _collectionsWrap,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _collectionEmptyText = new TextBlock
        {
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Foreground = TabsTheme.B(TabsTheme.TextMuted),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        Canvas.SetZIndex(_collectionEmptyText, 5);

        ViewModel.CollectionsChanged += () => { RefreshCollectionChips(); PlaceCollectionsHost(); UpdateCollectionEmptyText(); };
        ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName is nameof(ViewModel.ActiveCollectionName) or nameof(ViewModel.FilterMode))
                RefreshCollectionChips();
            if (e.PropertyName is nameof(ViewModel.ActiveCollectionName) or nameof(ViewModel.TotalGames))
                UpdateCollectionEmptyText();
        };
        RefreshCollectionChips();
        PlaceCollectionsHost();
    }

    /// <summary>Puts the chip row where the current layout wants it (see the file header).</summary>
    private void PlaceCollectionsHost()
    {
        if (_collectionsHost == null || _searchRowStack == null) return;
        try
        {
            var listGrid = _searchRow?.Parent as Grid;
            bool besideFilters = _spotlightOn && _countsGrid != null;
            Panel target = besideFilters ? _countsGrid! : _searchRowStack;
            if (VisualTreeHelper.GetParent(_collectionsHost) is Panel current && !ReferenceEquals(current, target))
                current.Children.Remove(_collectionsHost);
            if (_searchRowStack.Children.Contains(_collectionsHost) && !ReferenceEquals(target, _searchRowStack))
                _searchRowStack.Children.Remove(_collectionsHost);
            if (_countsGrid != null && _countsGrid.Children.Contains(_collectionsHost) && !ReferenceEquals(target, _countsGrid))
                _countsGrid.Children.Remove(_collectionsHost);
            SetCollectionsColumn(besideFilters);

            if (besideFilters)
            {
                // Spotlight: at the right of the search row, just left of the Filters button
                Grid.SetRow(_collectionsHost, 0);
                Grid.SetColumn(_collectionsHost, 0);
                _collectionsHost.Padding = new Thickness(0);
                _collectionsHost.MaxWidth = 640; // many collections wrap instead of running under the search
                _collectionsHost.HorizontalAlignment = HorizontalAlignment.Right;
                if (!_countsGrid!.Children.Contains(_collectionsHost)) _countsGrid.Children.Add(_collectionsHost);
            }
            else
            {
                Grid.SetRow(_collectionsHost, 0);
                Grid.SetColumn(_collectionsHost, 0);
                _collectionsHost.Padding = new Thickness(0);
                _collectionsHost.MaxWidth = double.PositiveInfinity;
                _collectionsHost.HorizontalAlignment = HorizontalAlignment.Stretch;
                if (!_searchRowStack.Children.Contains(_collectionsHost))
                {
                    // Straight after the All / Installed / Favourites switch
                    int at = _segmentHost != null ? _searchRowStack.Children.IndexOf(_segmentHost) + 1 : _searchRowStack.Children.Count;
                    _searchRowStack.Children.Insert(Math.Clamp(at, 0, _searchRowStack.Children.Count), _collectionsHost);
                }
            }
            PlaceCollectionEmptyText(listGrid);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.PlaceCollectionsHost] {ex.GetType().Name}: {ex.Message}");
        }
    }

    private bool _countsHasCollectionsColumn;

    /// <summary>
    /// Spotlight gives the Filters / counts grid an extra first column for the chips; the other layouts
    /// don't have it (its column spacing would push the Filters button over).
    /// </summary>
    private void SetCollectionsColumn(bool on)
    {
        if (_countsGrid == null || on == _countsHasCollectionsColumn) return;
        int shift = on ? 1 : -1;
        if (on) _countsGrid.ColumnDefinitions.Insert(0, new ColumnDefinition { Width = GridLength.Auto });
        foreach (var child in _countsGrid.Children)
            if (child is FrameworkElement fe && !ReferenceEquals(fe, _collectionsHost))
                Grid.SetColumn(fe, Math.Max(0, Grid.GetColumn(fe) + shift));
        if (!on) _countsGrid.ColumnDefinitions.RemoveAt(0);
        _countsHasCollectionsColumn = on;
    }

    /// <summary>Rebuilds the chips when the collections or the active one changed.</summary>
    private void RefreshCollectionChips()
    {
        if (_collectionsWrap == null) return;
        try
        {
            var names = ViewModel.CollectionNames;
            var active = ViewModel.ActiveCollectionName;
            var key = string.Join("\u001F", names) + "|" + (active ?? "");
            if (key == _collectionsKey) return;
            _collectionsKey = key;
            _collectionsWrap.Children.Clear();
            foreach (var name in names)
            {
                bool on = string.Equals(name, active, StringComparison.OrdinalIgnoreCase);
                var chip = MakeCollectionChip(name, on);
                string n = name;
                chip.Click += (s, e) =>
                {
                    try { ActivateCollectionFromUi(n); }
                    catch (Exception ex) { CrashReporter.Log($"[MainWindow.CollectionChip_Click] {ex.Message}"); }
                };
                chip.RightTapped += (s, e) => { e.Handled = true; ShowCollectionMenu(chip, n); };
                ToolTipService.SetToolTip(chip, on
                    ? $"Showing {name}. Click again to show all games. Right-click to rename, move or delete it."
                    : $"Show the games in {name}. Right-click to rename, move or delete it.");
                _collectionsWrap.Children.Add(chip);
            }
            var add = MakeCollectionChip("+ Collection", false);
            add.Tag = CollectionNewChipTag;
            add.Click += async (s, e) =>
            {
                try { await NewCollectionAsync(add.XamlRoot ?? Content?.XamlRoot, null); }
                catch (Exception ex) { CrashReporter.Log($"[MainWindow.NewCollectionChip_Click] {ex.Message}"); }
            };
            ToolTipService.SetToolTip(add, "New collection. Add games to it from a game's menu (right-click a game).");
            _collectionsWrap.Children.Add(add);
            UpdateCollectionEmptyText();
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.RefreshCollectionChips] {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>A chip: a Button (focusable for the controller), replaced rather than restyled when its state changes.</summary>
    private static Button MakeCollectionChip(string text, bool selected)
    {
        var chip = new Button
        {
            Content = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 220 },
            Padding = new Thickness(12, 0, 12, 0),
            Tag = selected ? CollectionChipSelectedTag : null,
        };
        TabsTheme.Apply(chip, selected ? ChipSelectedLook : ChipLook, height: 28, fontSize: 12);
        chip.CornerRadius = new CornerRadius(14);
        return chip;
    }

    /// <summary>The active collection's chip (the controller lands on it from the covers), or null.</summary>
    private Button? ActiveCollectionChip()
    {
        if (_collectionsWrap == null || ViewModel.ActiveCollectionName == null) return null;
        foreach (var child in _collectionsWrap.Children)
            if (child is Button { Tag: CollectionChipSelectedTag } b && b.IsLoaded) return b;
        return null;
    }

    /// <summary>True when the element is one of the collection chips (not "+ Collection").</summary>
    private bool IsCollectionChip(DependencyObject? element, out string name)
    {
        name = "";
        if (element == null || _collectionsWrap == null || _collectionsHost == null || !IsWithin(element, _collectionsHost)) return false;
        var names = ViewModel.CollectionNames;
        for (int i = 0; i < _collectionsWrap.Children.Count && i < names.Count; i++)
            if (IsWithin(element, _collectionsWrap.Children[i])) { name = names[i]; return true; }
        return false;
    }

    private void ActivateCollectionFromUi(string name)
    {
        ViewModel.ActivateCollection(name);
        RefreshFilterButtonStyles();
        RefreshTabsChrome();
    }

    // ── Chip menu ─────────────────────────────────────────────────────────────

    private void ShowCollectionMenu(FrameworkElement anchor, string name)
    {
        try
        {
            var names = ViewModel.CollectionNames;
            int index = ViewModel.CollectionStore.IndexOf(name);
            if (index < 0) return;
            var menu = new MenuFlyout();
            var rename = new MenuFlyoutItem { Text = "Rename…" };
            rename.Click += async (s, e) =>
            {
                try { await RenameCollectionAsync(anchor.XamlRoot ?? Content?.XamlRoot, name); }
                catch (Exception ex) { CrashReporter.Log($"[MainWindow.CollectionRename_Click] {ex.Message}"); }
            };
            var left = new MenuFlyoutItem { Text = "Move left", IsEnabled = index > 0 };
            left.Click += (s, e) => MoveCollectionFromUi(name, -1);
            var right = new MenuFlyoutItem { Text = "Move right", IsEnabled = index < names.Count - 1 };
            right.Click += (s, e) => MoveCollectionFromUi(name, 1);
            var delete = new MenuFlyoutItem { Text = "Delete…" };
            delete.Click += async (s, e) =>
            {
                try { await DeleteCollectionAsync(anchor.XamlRoot ?? Content?.XamlRoot, name); }
                catch (Exception ex) { CrashReporter.Log($"[MainWindow.CollectionDelete_Click] {ex.Message}"); }
            };
            menu.Items.Add(rename);
            menu.Items.Add(left);
            menu.Items.Add(right);
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(delete);
            menu.ShowAt(anchor, new FlyoutShowOptions { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft });
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.ShowCollectionMenu] {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void MoveCollectionFromUi(string name, int delta)
    {
        try
        {
            if (!ViewModel.MoveCollection(name, delta)) return;
            // Keep the moved chip focused for a controller or keyboard user
            AfterLayout(() =>
            {
                int i = ViewModel.CollectionStore.IndexOf(name);
                if (_padHints?.Visibility == Visibility.Visible && _collectionsWrap != null && i >= 0 && i < _collectionsWrap.Children.Count)
                    PadFocus(_collectionsWrap.Children[i]);
            });
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.MoveCollectionFromUi] {ex.Message}");
        }
    }

    // ── Game menu ─────────────────────────────────────────────────────────────

    /// <summary>The game menu's "Add to collection" submenu: a tick per collection, then "New collection…".</summary>
    private MenuFlyoutSubItem BuildCollectionsMenu(GameCardViewModel card)
    {
        var sub = new MenuFlyoutSubItem { Text = "Add to collection" };
        var names = ViewModel.CollectionNames;
        foreach (var name in names)
        {
            var item = new ToggleMenuFlyoutItem { Text = name, IsChecked = ViewModel.IsInCollection(name, card) };
            string n = name;
            // IsChecked covers a click and the controller's Toggle pattern alike; setting membership is idempotent
            item.RegisterPropertyChangedCallback(ToggleMenuFlyoutItem.IsCheckedProperty, (d, p) =>
            {
                try
                {
                    bool member = ((ToggleMenuFlyoutItem)d).IsChecked;
                    ViewModel.SetCollectionMembership(n, card, member);
                    if (_padHints?.Visibility == Visibility.Visible)
                        ShowPadToast(member ? $"Added {card.GameName} to {n}" : $"Removed {card.GameName} from {n}");
                }
                catch (Exception ex) { CrashReporter.Log($"[MainWindow.CollectionToggle] {ex.Message}"); }
            });
            sub.Items.Add(item);
        }
        if (names.Count > 0) sub.Items.Add(new MenuFlyoutSeparator());
        var create = new MenuFlyoutItem { Text = "New collection…" };
        create.Click += async (s, e) =>
        {
            try { await NewCollectionAsync((s as FrameworkElement)?.XamlRoot ?? Content?.XamlRoot, card); }
            catch (Exception ex) { CrashReporter.Log($"[MainWindow.NewCollectionMenu_Click] {ex.Message}"); }
        };
        sub.Items.Add(create);
        return sub;
    }

    // ── Dialogs ───────────────────────────────────────────────────────────────

    /// <summary>New collection (with <paramref name="firstGame"/> in it when given).</summary>
    private async Task NewCollectionAsync(XamlRoot? xamlRoot, GameCardViewModel? firstGame)
    {
        if (xamlRoot == null) return;
        var name = await PromptCollectionNameAsync(xamlRoot, "New collection",
            firstGame != null ? $"Name the new collection. {firstGame.GameName} will be added to it." : "Name the new collection. Add games to it from a game's menu.",
            "Create", "", null);
        if (name == null) return;
        var error = ViewModel.CreateCollection(name, firstGame, out var created);
        if (error != null) { CrashReporter.Log($"[MainWindow.NewCollectionAsync] {error}"); return; }
        if (_padHints?.Visibility == Visibility.Visible)
            ShowPadToast(firstGame != null ? $"Added {firstGame.GameName} to {created}" : $"Created {created}");
    }

    private async Task RenameCollectionAsync(XamlRoot? xamlRoot, string name)
    {
        if (xamlRoot == null) return;
        var newName = await PromptCollectionNameAsync(xamlRoot, "Rename collection", $"New name for {name}:", "Rename", name, name);
        if (newName == null || newName == name) return;
        var error = ViewModel.RenameCollection(name, newName);
        if (error != null) CrashReporter.Log($"[MainWindow.RenameCollectionAsync] {error}");
    }

    private async Task DeleteCollectionAsync(XamlRoot? xamlRoot, string name)
    {
        if (xamlRoot == null) return;
        int count = ViewModel.CollectionStore.GetMembers(name)?.Count ?? 0;
        var dialog = new ContentDialog
        {
            Title = "Delete collection",
            Content = new TextBlock
            {
                Text = $"Delete the collection \"{name}\"{(count > 0 ? $" ({count} {(count == 1 ? "game" : "games")})" : "")}?\n\n" +
                       "Only the collection is removed. The games stay in your library with their mods and settings.",
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        if (await DialogService.ShowSafeAsync(dialog) != ContentDialogResult.Primary) return;
        ViewModel.DeleteCollection(name);
        RefreshFilterButtonStyles();
        RefreshTabsChrome();
    }

    /// <summary>
    /// Asks for a collection name, checked as it's typed (empty, too long, a built-in filter name,
    /// already used). Null when cancelled.
    /// </summary>
    private async Task<string?> PromptCollectionNameAsync(XamlRoot xamlRoot, string title, string prompt,
        string primary, string initial, string? renaming)
    {
        var store = ViewModel.CollectionStore;
        var box = new TextBox { Text = initial, MaxLength = GameCollectionStore.MaxNameLength, Width = 340, PlaceholderText = "e.g. Racing" };
        var error = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = TabsTheme.B("#FF9AA8"),
            Visibility = Visibility.Collapsed,
        };
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(box);
        panel.Children.Add(error);

        var dialog = new ContentDialog
        {
            Title = title,
            Content = panel,
            PrimaryButtonText = primary,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };

        string? Check() => store.ValidateName(box.Text, renaming, out _);
        void ShowCheck(bool showEmpty)
        {
            var problem = Check();
            dialog.IsPrimaryButtonEnabled = problem == null;
            bool show = problem != null && (showEmpty || !string.IsNullOrWhiteSpace(box.Text));
            error.Text = problem ?? "";
            error.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }
        box.TextChanged += (s, e) => ShowCheck(false);
        dialog.PrimaryButtonClick += (s, e) =>
        {
            if (Check() != null) { e.Cancel = true; ShowCheck(true); }
        };
        box.Loaded += (s, e) => { box.Focus(FocusState.Programmatic); box.SelectAll(); };
        ShowCheck(false);

        if (await DialogService.ShowSafeAsync(dialog) != ContentDialogResult.Primary) return null;
        return box.Text.Trim();
    }

    // ── Empty list ────────────────────────────────────────────────────────────

    /// <summary>The message sits over the game list (Classic / Tabs) or the covers (Spotlight).</summary>
    private void PlaceCollectionEmptyText(Grid? listGrid)
    {
        if (_collectionEmptyText == null || listGrid == null) return;
        if (!listGrid.Children.Contains(_collectionEmptyText)) listGrid.Children.Add(_collectionEmptyText);
        bool bottom = _spotlightOn && ViewModel.Settings.SpotlightShelfAtBottom;
        Grid.SetRow(_collectionEmptyText, _spotlightOn ? (bottom ? 0 : 3) : 3);
        _collectionEmptyText.Margin = _spotlightOn ? new Thickness(24, 0, 24, 0) : new Thickness(16, 12, 16, 0);
        _collectionEmptyText.VerticalAlignment = _spotlightOn ? VerticalAlignment.Center : VerticalAlignment.Top;
        _collectionEmptyText.HorizontalAlignment = _spotlightOn ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        UpdateCollectionEmptyText();
    }

    private void UpdateCollectionEmptyText()
    {
        if (_collectionEmptyText == null) return;
        var name = ViewModel.ActiveCollectionName;
        if (name == null || ViewModel.DisplayedGames.Count > 0)
        {
            _collectionEmptyText.Visibility = Visibility.Collapsed;
            return;
        }
        int members = ViewModel.CollectionStore.GetMembers(name)?.Count ?? 0;
        _collectionEmptyText.Text = members == 0
            ? $"No games in {name} yet. Add games from a game's menu."
            : !string.IsNullOrWhiteSpace(ViewModel.SearchQuery) || ViewModel.Filter.ActiveCustomFilterName != null
                ? $"None of the games in {name} match the search."
                : $"The games in {name} are hidden or no longer detected.";
        _collectionEmptyText.Visibility = Visibility.Visible;
    }
}
