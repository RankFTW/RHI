// MainWindow.SpotlightChrome.cs -- the Spotlight layout's window (mockup D): the sidebar becomes a
// full-width strip above the game page, with the search box, the All / Installed / Favourites /
// Hidden switch and Filters in one row and a horizontal shelf of cover tiles below. The shelf
// shows the same filtered list as the sidebar (DisplayedGames) and selecting a tile selects that
// game in the (hidden) game list, so filtering, selection and the detail rebuild are unchanged.
// Uses the Tabs chrome (top bar, sidebar restyle, footer), which must be built first.

using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    /// <summary>Settings › Achievements changed: show or hide the hero's achievements strip.</summary>
    internal void AchievementsSettingChanged() => _detailPanelBuilder.Layout.AchievementsSettingChanged();

    private bool _spotlightOn;
    private ListView? _shelf;
    private bool _shelfSyncing;
    private bool _shelfWheelHooked;
    private Grid? _contentColumn;
    private readonly HashSet<ShelfTile> _shelfTiles = new();
    private readonly ConditionalWeakTable<Border, ShelfTile> _shelfTileOf = new();
    private readonly Dictionary<string, BitmapImage> _coverCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int?> _appIdCache = new(StringComparer.OrdinalIgnoreCase);

    // Tile size: TileWidth / CoverHeight in MainWindow.SpotlightCoverSize.cs (resizable)

    private sealed class ShelfTile
    {
        internal required Border Root;
        internal required Border Cover;
        internal required Image Image;
        internal required TextBlock Fallback;
        internal required TextBlock Name;
        internal required Border Badge;
        internal GameCardViewModel? Card;
        internal System.ComponentModel.PropertyChangedEventHandler? Watch;
    }

    // An available update: purple "Update" label on the cover and a purple outline (Tabs uses purple for updates too)
    private const string UpdateOutline = "#8F74E6";

    private string CoverOutline(ShelfTile t) =>
        t.Card != null && ReferenceEquals(t.Card, ViewModel.SelectedGame) ? TabsTheme.Accent
        : t.Card?.UpdateBadgeVisibility == Visibility.Visible ? UpdateOutline
        : TabsTheme.CardBorder;

    /// <summary>Shows the tile's update state; follows the card, so it changes after an update check or an install.</summary>
    private void ApplyTileState(ShelfTile t)
    {
        bool update = t.Card?.UpdateBadgeVisibility == Visibility.Visible;
        t.Badge.Visibility = update ? Visibility.Visible : Visibility.Collapsed;
        t.Cover.BorderBrush = TabsTheme.B(CoverOutline(t));
        ToolTipService.SetToolTip(t.Root, t.Card == null ? null
            : update ? $"{t.Card.GameName}: update available" : t.Card.GameName);
    }

    private void WatchCard(ShelfTile t, GameCardViewModel? card)
    {
        if (t.Card != null && t.Watch != null) t.Card.PropertyChanged -= t.Watch;
        t.Card = card;
        t.Watch = null;
        if (card == null) return;
        t.Watch = (s, e) =>
        {
            if (e.PropertyName is not (null or "" or nameof(GameCardViewModel.UpdateBadgeVisibility))) return;
            DispatcherQueue.TryEnqueue(() =>
            {
                try { if (ReferenceEquals(t.Card, card)) ApplyTileState(t); }
                catch (Exception ex) { CrashReporter.Log($"[MainWindow.Shelf] Update badge refresh failed — {ex.Message}"); }
            });
        };
        card.PropertyChanged += t.Watch;
    }

    /// <summary>Switches the window between the Spotlight strip + shelf and the normal sidebar.</summary>
    private void ApplySpotlightChrome()
    {
        bool on = _detailPanelBuilder?.Layout.IsSpotlight == true && _tabsChromeBuilt;
        if (on == _spotlightOn) return;
        try
        {
            if (_sidebar == null || _searchRow == null || _countsRow == null) return;
            _contentColumn ??= DetailScrollViewer.Parent as Grid;
            if (_contentColumn == null) return;
            var gv = GameViewPanel;

            // Two rows: the strip (Auto) and the page (*). Everything else spans both.
            if (gv.RowDefinitions.Count == 0)
            {
                gv.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                gv.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                foreach (var child in gv.Children)
                    if (child is FrameworkElement fe) Grid.SetRowSpan(fe, 2);
            }

            _spotlightOn = on;
            var list = _searchRow.Parent as Grid;
            if (on)
            {
                Grid.SetRow(_sidebar, 0); Grid.SetRowSpan(_sidebar, 1); Grid.SetColumn(_sidebar, 0); Grid.SetColumnSpan(_sidebar, 2);
                Grid.SetRow(_contentColumn, 1); Grid.SetRowSpan(_contentColumn, 1); Grid.SetColumn(_contentColumn, 0); Grid.SetColumnSpan(_contentColumn, 2);
                _sidebar.BorderThickness = new Thickness(0, 0, 0, 1);

                // One header row: search + switch on the left, Filters + counts on the right
                if (_searchRowStack != null)
                {
                    _searchRowStack.Orientation = Orientation.Horizontal;
                    _searchRowStack.Spacing = 16;
                    if (_searchRowStack.Children.FirstOrDefault() is FrameworkElement search) search.Width = 320;
                    _searchRowStack.HorizontalAlignment = HorizontalAlignment.Left;
                }
                if (_segmentHost != null) _segmentHost.Width = 340;
                PlaceFiltersButton(spotlight: true);
                _countsRow.HorizontalAlignment = HorizontalAlignment.Right;
                _countsRow.VerticalAlignment = VerticalAlignment.Center;

                GameList.Visibility = Visibility.Collapsed;
                EnsureShelf(list);
                _shelf!.Visibility = Visibility.Visible;
                if (_coverGrip != null) _coverGrip.Visibility = Visibility.Visible;
                // Above or below the page (rows, borders, paddings)
                ApplySpotlightShelfPosition();
                SyncShelfFromList();
            }
            else
            {
                // Back to the normal sidebar: undo the bottom placement
                if (gv.RowDefinitions.Count == 2)
                {
                    gv.RowDefinitions[0].Height = GridLength.Auto;
                    gv.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
                }
                Grid.SetRow(_searchRow, 0);
                if (_chipsRow != null) Grid.SetRow(_chipsRow, 1);
                if (_shelfPositionBtn != null) _shelfPositionBtn.Visibility = Visibility.Collapsed;
                Grid.SetRow(_sidebar, 0); Grid.SetRowSpan(_sidebar, 2); Grid.SetColumn(_sidebar, 0); Grid.SetColumnSpan(_sidebar, 1);
                Grid.SetRow(_contentColumn, 0); Grid.SetRowSpan(_contentColumn, 2); Grid.SetColumn(_contentColumn, 1); Grid.SetColumnSpan(_contentColumn, 1);
                _sidebar.BorderThickness = new Thickness(0, 0, 1, 0);
                if (_searchRowStack != null)
                {
                    _searchRowStack.Orientation = Orientation.Vertical;
                    _searchRowStack.Spacing = 12;
                    if (_searchRowStack.Children.FirstOrDefault() is FrameworkElement search) search.Width = double.NaN;
                    _searchRowStack.HorizontalAlignment = HorizontalAlignment.Stretch;
                }
                if (_segmentHost != null) _segmentHost.Width = double.NaN;
                PlaceFiltersButton(spotlight: false);
                Grid.SetRow(_countsRow, 2);
                _countsRow.HorizontalAlignment = HorizontalAlignment.Stretch;
                _countsRow.VerticalAlignment = VerticalAlignment.Stretch;
                // Re-apply the sidebar paddings
                ShowTabsSidebar();
                RefreshSidebar();
                GameList.Visibility = Visibility.Visible;
                if (_shelf != null) _shelf.Visibility = Visibility.Collapsed;
                if (_coverGrip != null) _coverGrip.Visibility = Visibility.Collapsed;
                PlaceCollectionsHost();
                if (GameList.SelectedItem != null) GameList.ScrollIntoView(GameList.SelectedItem);
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.ApplySpotlightChrome] Failed — {ex.Message}");
        }
    }

    /// <summary>
    /// Spotlight: the Filters button is an icon just left of the All / Installed / Favourites switch.
    /// Other layouts: the labelled button by the counts, as before.
    /// </summary>
    private void PlaceFiltersButton(bool spotlight)
    {
        if (_filtersBtn == null || _searchRowStack == null || _countsGrid == null || _segmentHost == null) return;
        if (VisualTreeHelper.GetParent(_filtersBtn) is Panel current) current.Children.Remove(_filtersBtn);
        _searchRowStack.Children.Remove(_filtersBtn);
        _countsGrid.Children.Remove(_filtersBtn);
        if (spotlight)
        {
            int at = Math.Max(0, _searchRowStack.Children.IndexOf(_segmentHost));
            _searchRowStack.Children.Insert(at, _filtersBtn);
            _filtersBtn.Height = double.NaN;               // as tall as the switch next to it
            _filtersBtn.VerticalAlignment = VerticalAlignment.Stretch;
            _filtersBtn.MinWidth = 40;
            _filtersBtn.Padding = new Thickness(10, 0, 10, 0);
            _filtersBtn.CornerRadius = new CornerRadius(10);
        }
        else
        {
            // Column 0, or 1 while the Spotlight collections column is still there (removed and shifted later)
            Grid.SetColumn(_filtersBtn, _countsHasCollectionsColumn ? 1 : 0);
            _countsGrid.Children.Add(_filtersBtn);
            _filtersBtn.Height = 30;
            _filtersBtn.VerticalAlignment = VerticalAlignment.Center;
            _filtersBtn.MinWidth = 0;
            _filtersBtn.CornerRadius = new CornerRadius(6);
        }
        RefreshSidebar();
    }

    // ── Shelf above / below the page ──────────────────────────────────────────

    /// <summary>
    /// Spotlight: puts the strip (search, filters, covers) above the game page, or below it when
    /// <c>SpotlightShelfAtBottom</c> is on. At the bottom the strip is mirrored: covers next to the
    /// page, search and filters on the outside, so controller Up / Down still go page → covers →
    /// filters in one direction.
    /// </summary>
    private void ApplySpotlightShelfPosition()
    {
        if (!_spotlightOn || _sidebar == null || _searchRow == null || _countsRow == null || _contentColumn == null) return;
        bool bottom = ViewModel.Settings.SpotlightShelfAtBottom;
        var gv = GameViewPanel;
        if (gv.RowDefinitions.Count == 2)
        {
            gv.RowDefinitions[0].Height = bottom ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
            gv.RowDefinitions[1].Height = bottom ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        }
        Grid.SetRow(_sidebar, bottom ? 1 : 0);
        Grid.SetRow(_contentColumn, bottom ? 0 : 1);
        _sidebar.BorderThickness = bottom ? new Thickness(0, 1, 0, 0) : new Thickness(0, 0, 0, 1);

        // Strip rows (the list grid: 0 search, 1 filter chips, 2 counts, 3 covers). On top: search + counts 0,
        // filter chips 1, covers 3; at the bottom: covers 0, search + counts 2, chips 3. The collection chips sit
        // in the counts grid, left of the Filters button (PlaceCollectionsHost)
        int searchRow = bottom ? 2 : 0;
        Grid.SetRow(_searchRow, searchRow);
        Grid.SetRow(_countsRow, searchRow); // Filters + counts sit at the right of the search row
        if (_chipsRow != null) Grid.SetRow(_chipsRow, searchRow + 1);
        PlaceCollectionsHost();
        if (_shelf != null)
        {
            Grid.SetRow(_shelf, bottom ? 0 : 3);
            // Keep the side padding (UpdateShelfCentering widens it to centre a short Favourites row)
            _shelf.Padding = bottom ? new Thickness(_shelf.Padding.Left, 12, _shelf.Padding.Right, 2)
                                    : new Thickness(_shelf.Padding.Left, 4, _shelf.Padding.Right, 10);
            PlaceCoverGrip(bottom);
        }
        _searchRow.Padding = bottom ? new Thickness(24, 6, 24, 14) : new Thickness(24, 14, 24, 6);
        _countsRow.Padding = bottom ? new Thickness(0, 0, 24, 8) : new Thickness(0, 8, 24, 0);

        if (_shelfPositionBtn != null)
        {
            _shelfPositionBtn.Visibility = Visibility.Visible;
            _shelfPositionBtn.Content = bottom ? "Covers at top" : "Covers at bottom";
            ToolTipService.SetToolTip(_shelfPositionBtn, bottom
                ? "Move the search, filters and game covers back above the game page"
                : "Move the search, filters and game covers below the game page");
        }
    }

    private void ShelfPositionBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ViewModel.Settings.SpotlightShelfAtBottom = !ViewModel.Settings.SpotlightShelfAtBottom;
            ViewModel.SaveSettingsPublic();
            CrashReporter.Log($"[MainWindow.ShelfPosition] Covers {(ViewModel.Settings.SpotlightShelfAtBottom ? "at bottom" : "at top")}");
            ApplySpotlightShelfPosition();
            UpdateShelfCentering();
            // Keep the button focused (it moved) so a controller or keyboard user stays on it
            AfterLayout(() =>
            {
                if (_shelfPositionBtn == null) return;
                if (_padHints?.Visibility == Visibility.Visible) PadFocus(_shelfPositionBtn);
                else _shelfPositionBtn.Focus(FocusState.Programmatic);
            });
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.ShelfPositionBtn_Click] Failed — {ex.Message}");
        }
    }

    // ── Shelf ─────────────────────────────────────────────────────────────────

    private const string XamlNs = "xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'";

    private void EnsureShelf(Grid? parent)
    {
        if (_shelf != null || parent == null) return;
        _shelf = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            ItemsSource = ViewModel.DisplayedGames,
            Height = CoverHeight + 58,
            Padding = new Thickness(20, 4, 20, 10),
            ItemsPanel = (ItemsPanelTemplate)XamlReader.Load($"<ItemsPanelTemplate {XamlNs}><ItemsStackPanel Orientation='Horizontal'/></ItemsPanelTemplate>"),
            ItemTemplate = (DataTemplate)XamlReader.Load($"<DataTemplate {XamlNs}><Border/></DataTemplate>"),
            ItemContainerStyle = (Style)XamlReader.Load(
                $"<Style {XamlNs} TargetType='ListViewItem'>" +
                "<Setter Property='Padding' Value='0'/><Setter Property='Margin' Value='4,0,4,0'/>" +
                "<Setter Property='MinWidth' Value='0'/><Setter Property='MinHeight' Value='0'/>" +
                // No white focus box: the tile's blue selection ring already marks it (focus is always on the selected cover)
                "<Setter Property='UseSystemFocusVisuals' Value='False'/>" +
                "<Setter Property='VerticalContentAlignment' Value='Top'/></Style>"),
        };
        ScrollViewer.SetHorizontalScrollMode(_shelf, ScrollMode.Enabled);
        ScrollViewer.SetHorizontalScrollBarVisibility(_shelf, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollMode(_shelf, ScrollMode.Disabled);
        // Cached cards have no Steam AppID until the background scan fills it in: try covers again then
        ViewModel.CardsMerged += RetryMissingCovers;
        ScrollViewer.SetVerticalScrollBarVisibility(_shelf, ScrollBarVisibility.Disabled);
        // The tile draws its own selection ring and hover
        var r = _shelf.Resources;
        var clear = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        foreach (var key in new[] { "ListViewItemBackgroundSelected", "ListViewItemBackgroundSelectedPointerOver",
                                    "ListViewItemBackgroundSelectedPressed", "ListViewItemBackgroundPointerOver",
                                    "ListViewItemBackgroundPressed", "ListViewItemSelectionIndicatorBrush" })
            TabsTheme.SetResource(r, key, clear);

        _shelf.ContainerContentChanging += Shelf_ContainerContentChanging;
        _shelf.SelectionChanged += (s, e) =>
        {
            if (_shelfSyncing) return;
            if (_shelf.SelectedItem is GameCardViewModel card && !ReferenceEquals(GameList.SelectedItem, card))
                GameList.SelectedItem = card;
            UpdateShelfHighlight();
        };
        _shelf.DoubleTapped += GameList_DoubleTapped;
        _shelf.Loaded += (s, e) => { HookShelfWheel(); UpdateShelfCentering(); };
        _shelf.SizeChanged += (s, e) => UpdateShelfCentering();
        ViewModel.DisplayedGames.CollectionChanged += (s, e) => UpdateShelfCentering();
        GameList.SelectionChanged += (s, e) => { if (_spotlightOn) SyncShelfFromList(); };

        Grid.SetRow(_shelf, 3);
        parent.Children.Add(_shelf);
        EnsureCoverGrip(parent);
        HookShelfDrag();
        PlaceCoverGrip(ViewModel.Settings.SpotlightShelfAtBottom);
    }

    /// <summary>
    /// With the Favourites filter or a collection on, centres the covers when they all fit on screen (extra side
    /// padding); otherwise they start at the left as usual.
    /// </summary>
    private void UpdateShelfCentering()
    {
        if (_shelf == null) return;
        try
        {
            double side = 20;
            double width = _shelf.ActualWidth;
            if (width > 0 && (ViewModel.ActiveFilters.Contains("Favourites") || ViewModel.ActiveCollectionName != null))
            {
                double items = ViewModel.DisplayedGames.Count * (TileWidth + 8); // tile + item margin 4,0,4,0
                if (items > 0 && items + 40 <= width) side = Math.Floor((width - items) / 2);
            }
            if (Math.Abs(_shelf.Padding.Left - side) > 0.5)
                _shelf.Padding = new Thickness(side, _shelf.Padding.Top, side, _shelf.Padding.Bottom);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.UpdateShelfCentering] {ex.Message}");
        }
    }

    /// <summary>The mouse wheel scrolls the shelf sideways (it has no vertical scrolling).</summary>
    private void HookShelfWheel()
    {
        if (_shelfWheelHooked || _shelf == null) return;
        var sv = FindChild<ScrollViewer>(_shelf);
        if (sv == null) return;
        _shelfWheelHooked = true;
        sv.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler((s, e) =>
        {
            var delta = e.GetCurrentPoint(sv).Properties.MouseWheelDelta;
            if (delta == 0) return;
            sv.ChangeView(Math.Max(0, sv.HorizontalOffset - delta * 1.5), null, null);
            e.Handled = true;
        }), handledEventsToo: true);
    }

    private static T? FindChild<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T t) return t;
            if (FindChild<T>(child) is { } found) return found;
        }
        return null;
    }

    private void SyncShelfFromList()
    {
        if (_shelf == null) return;
        _shelfSyncing = true;
        try { _shelf.SelectedItem = GameList.SelectedItem; }
        finally { _shelfSyncing = false; }
        // The controller centres the cover itself (CenterShelf)
        if (GameList.SelectedItem != null && !_padStepping) _shelf.ScrollIntoView(GameList.SelectedItem);
        UpdateShelfHighlight();
    }

    private void UpdateShelfHighlight()
    {
        var selected = ViewModel.SelectedGame;
        foreach (var t in _shelfTiles)
            t.Cover.BorderBrush = TabsTheme.B(CoverOutline(t));
    }

    private void Shelf_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer.ContentTemplateRoot is not Border root) return;
        if (!_shelfTileOf.TryGetValue(root, out var tile))
        {
            tile = BuildShelfTile(root);
            _shelfTileOf.Add(root, tile);
            _shelfTiles.Add(tile);
        }
        args.Handled = true;
        if (args.InRecycleQueue)
        {
            WatchCard(tile, null);
            tile.Image.Source = null;
            return;
        }
        if (args.Item is not GameCardViewModel card) return;
        WatchCard(tile, card);
        root.Tag = card; // right-click menu (GameRow_RightTapped) reads the card from Tag
        tile.Name.Text = card.GameName;
        tile.Fallback.Text = card.GameName;
        tile.Name.Foreground = TabsTheme.B(card.IsHidden ? TabsTheme.TextMuted : TabsTheme.TextPrimary);
        ApplyTileState(tile);
        tile.Image.Source = null;
        LoadCover(tile, card);
    }

    private ShelfTile BuildShelfTile(Border root)
    {
        var fallback = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(10),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = TabsTheme.B(TabsTheme.TextMuted),
        };
        var image = new Image { Stretch = Stretch.UniformToFill };
        var badge = new Border
        {
            Margin = new Thickness(6),
            Padding = new Thickness(8, 2, 8, 3),
            CornerRadius = new CornerRadius(6),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Background = TabsTheme.B("#F02A2050"),
            BorderBrush = TabsTheme.B(UpdateOutline),
            BorderThickness = new Thickness(1),
            Visibility = Visibility.Collapsed,
            Child = new TextBlock
            {
                Text = "Update",
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = TabsTheme.B("#E4D8FF"),
            },
        };
        var art = new Grid();
        art.Children.Add(fallback);
        art.Children.Add(image);
        art.Children.Add(badge);
        var cover = new Border
        {
            Height = CoverHeight,
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(2),
            BorderBrush = TabsTheme.B(TabsTheme.CardBorder),
            Background = TabsTheme.B("#161D2B"),
            Child = art,
        };
        var name = new TextBlock
        {
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(2, 0, 2, 0),
        };
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(cover);
        stack.Children.Add(name);
        root.Width = TileWidth;
        root.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        root.Child = stack;
        root.RightTapped += GameRow_RightTapped;
        root.PointerEntered += (s, e) => { if (!ReferenceEquals(root.Tag, ViewModel.SelectedGame)) cover.BorderBrush = TabsTheme.B("#3A4A66"); };
        var tile = new ShelfTile { Root = root, Cover = cover, Image = image, Fallback = fallback, Name = name, Badge = badge };
        root.PointerExited += (s, e) => cover.BorderBrush = TabsTheme.B(CoverOutline(tile));
        TabsTheme.UseHandCursor(root);
        return tile;
    }

    private int? ShelfAppId(GameCardViewModel card)
    {
        if (_appIdCache.TryGetValue(card.GameName, out var id)) return id;
        id = DetailPanelBuilder.ResolveCommunityAppId(card, ViewModel.Manifest);
        // Only a found ID is kept: during startup the scan, manifest and PCGW data may not be ready yet
        if (id != null) _appIdCache[card.GameName] = id;
        return id;
    }

    /// <summary>Loads covers for shown tiles that have none yet (called after the background scan merge).</summary>
    private void RetryMissingCovers()
    {
        try
        {
            if (_detailPanelBuilder?.Layout.IsSpotlight != true) return;
            int n = 0;
            foreach (var t in _shelfTiles)
                if (t.Card != null && t.Image.Source == null) { LoadCover(t, t.Card); n++; }
            if (n > 0) CrashReporter.Log($"[MainWindow.Shelf] Retrying covers for {n} tile(s)");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.Shelf] Cover retry failed — {ex.Message}");
        }
    }

    /// <summary>Shows the game's Steam cover: from memory, from disk, or downloaded in the background.</summary>
    private void LoadCover(ShelfTile tile, GameCardViewModel card)
    {
        if (SteamArtworkService.TryGetCustom(ArtKey(card), SteamArtworkService.Kind.Cover) is { } custom)
        {
            tile.Image.Source = CoverImage(custom);
            return;
        }
        if (ShelfAppId(card) is not int appId) return;
        if (SteamArtworkService.TryGetCached(appId, SteamArtworkService.Kind.Cover) is { } cached)
        {
            tile.Image.Source = CoverImage(cached);
            return;
        }
        var token = LifetimeToken;
        _ = Task.Run(async () =>
        {
            var path = await SteamArtworkService.GetAsync(appId, SteamArtworkService.Kind.Cover, token).ConfigureAwait(false);
            if (path == null || token.IsCancellationRequested) return;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (ReferenceEquals(tile.Card, card)) tile.Image.Source = CoverImage(path);
            });
        });
    }

    internal static string ArtKey(GameCardViewModel card) => Models.GameKey.FromCard(card.GameName, card.Source).ToKey();

    /// <summary>Shows a game's cover again after its custom art changed.</summary>
    internal void ReloadCover(GameCardViewModel card)
    {
        foreach (var t in _shelfTiles)
            if (ReferenceEquals(t.Card, card)) { t.Image.Source = null; LoadCover(t, card); }
    }

    private BitmapImage CoverImage(string path)
    {
        if (_coverCache.TryGetValue(path, out var img)) return img;
        // Decoded at tile size: a full 600x900 cover per tile would waste memory
        _coverDecodeWidth = CoverDecodeWidth;
        img = new BitmapImage { DecodePixelWidth = _coverDecodeWidth, UriSource = new Uri(path) };
        _coverCache[path] = img;
        return img;
    }

    // ── Custom artwork (game menu › Artwork) ──────────────────────────────────

    /// <summary>"Artwork" submenu for the game menu: set or reset the cover and the banner.</summary>
    private MenuFlyoutSubItem BuildArtworkMenu(GameCardViewModel card)
    {
        var key = ArtKey(card);
        var sub = new MenuFlyoutSubItem { Text = "Artwork" };
        void Add(string text, Action action)
        {
            var item = new MenuFlyoutItem { Text = text };
            item.Click += (s, e) => action();
            sub.Items.Add(item);
        }
        Add("Set custom cover...", () => _ = PickCustomArtAsync(card, SteamArtworkService.Kind.Cover));
        Add("Set custom banner...", () => _ = PickCustomArtAsync(card, SteamArtworkService.Kind.Hero));
        bool customCover = SteamArtworkService.TryGetCustom(key, SteamArtworkService.Kind.Cover) != null;
        bool customHero = SteamArtworkService.TryGetCustom(key, SteamArtworkService.Kind.Hero) != null;
        if (customCover || customHero) sub.Items.Add(new MenuFlyoutSeparator());
        if (customCover) Add("Use Steam cover", () => ResetCustomArt(card, SteamArtworkService.Kind.Cover));
        if (customHero) Add("Use Steam banner", () => ResetCustomArt(card, SteamArtworkService.Kind.Hero));
        return sub;
    }

    private async Task PickCustomArtAsync(GameCardViewModel card, SteamArtworkService.Kind kind)
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var title = kind == SteamArtworkService.Kind.Cover
                ? $"Cover for {card.GameName} (tall image, about 2:3)"
                : $"Banner for {card.GameName} (wide image)";
            var file = await Task.Run(() =>
            {
                var ofn = new NativeInterop.OpenFileName();
                ofn.structSize = System.Runtime.InteropServices.Marshal.SizeOf(ofn);
                ofn.hwndOwner = hwnd;
                ofn.filter = "Images (*.jpg;*.jpeg;*.png;*.webp;*.bmp)\0*.jpg;*.jpeg;*.png;*.webp;*.bmp\0All Files (*.*)\0*.*\0";
                ofn.file = new string(new char[260]);
                ofn.maxFile = ofn.file.Length;
                ofn.title = title;
                ofn.flags = 0x00080000 | 0x00001000; // OFN_EXPLORER | OFN_FILEMUSTEXIST
                return NativeInterop.GetOpenFileName(ref ofn) ? ofn.file.TrimEnd('\0') : null;
            });
            if (string.IsNullOrEmpty(file)) return;

            var saved = await Task.Run(() => SteamArtworkService.SetCustom(ArtKey(card), kind, file));
            if (saved == null)
            {
                ShowStatusBarMessage($"Couldn't save the image for {card.GameName}", TimeSpan.FromSeconds(8));
                return;
            }
            ShowArtChange(card, kind);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.PickCustomArtAsync] {card.GameName}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Screenshots panel › Set as banner: the screenshot becomes the game's custom banner (same as Artwork ›
    /// Set custom banner; Use Steam banner puts the old one back).
    /// </summary>
    internal async Task SetBannerFromFileAsync(GameCardViewModel card, string file)
    {
        try
        {
            var saved = await Task.Run(() => SteamArtworkService.SetCustom(ArtKey(card), SteamArtworkService.Kind.Hero, file));
            if (saved == null)
            {
                ShowStatusBarMessage($"Couldn't set the banner for {card.GameName}", TimeSpan.FromSeconds(8));
                return;
            }
            ShowArtChange(card, SteamArtworkService.Kind.Hero);
            ShowStatusBarMessage($"Banner set for {card.GameName}. Right-click the banner › Artwork › Use Steam banner to undo.", TimeSpan.FromSeconds(8));
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.SetBannerFromFileAsync] {card.GameName}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void ResetCustomArt(GameCardViewModel card, SteamArtworkService.Kind kind)
    {
        SteamArtworkService.ClearCustom(ArtKey(card), kind);
        ShowArtChange(card, kind);
    }

    private void ShowArtChange(GameCardViewModel card, SteamArtworkService.Kind kind)
    {
        if (kind == SteamArtworkService.Kind.Cover) ReloadCover(card);
        else if (ReferenceEquals(ViewModel.SelectedGame, card)) _detailPanelBuilder.Layout.ReloadHeroArt();
    }
}
