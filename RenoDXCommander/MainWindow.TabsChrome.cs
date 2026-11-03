// MainWindow.TabsChrome.cs -- the Tabs layout's window chrome (mockup A): top bar, sidebar and
// footer. Built in C# beside the XAML ones (XAML constraint); the XAML controls stay in the window,
// hidden or restyled, and every new control calls the existing handlers. Classic is untouched:
// switching back hides the Tabs chrome and restores the original looks.
// For now only the Tabs layout uses this top bar; the plan is to offer it in every layout once
// it has been tried (docs/layouts-plan-2.9.0.md, decision 1).

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Dispatching;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    private bool _tabsChromeBuilt;
    private bool _tabsChromeOn;

    // Top bar
    private Border? _xamlTopBar;
    private UIElement? _xamlTopBarContent;
    private Grid? _tabsTopBar;
    private Button? _settingsGear;
    private Border? _navModsNew;
    private TextBlock? _checkedText;
    private Grid? _updateAllHost;
    private (Brush? Bg, Thickness Pad, Brush? Border)? _topBarClassic;

    // Sidebar
    private Border? _sidebar;
    private Border? _searchRow;
    private Border? _chipsRow;
    private Border? _countsRow;
    private UIElement? _mainFilterChips;
    private StackPanel? _searchRowStack;
    private Border? _segmentHost;
    private Grid? _countsGrid;
    private Button? _filtersBtn;
    private bool _filtersOpen;
    private readonly Dictionary<FrameworkElement, (Brush? Bg, Brush? Border, Thickness Pad, Thickness Thick)> _sidebarClassic = new();

    // Footer
    private Border? _footer;
    private (Brush? Bg, Brush? Border)? _footerClassic;

    private DispatcherQueueTimer? _checkedTimer;
    private bool _chromeRefreshQueued;

    /// <summary>Switches the window chrome between Classic, Tabs and Spotlight (which builds on Tabs).</summary>
    internal void ApplyTabsChrome()
    {
        ApplyTabsChromeCore();
        ApplySpotlightChrome();
    }

    private void ApplyTabsChromeCore()
    {
        bool tabs = _detailPanelBuilder?.Layout.IsTabs == true;
        if (tabs == _tabsChromeOn && (_tabsChromeBuilt || !tabs)) return;
        try
        {
            if (tabs && !_tabsChromeBuilt) BuildTabsChrome();
            if (!_tabsChromeBuilt) return;
            _tabsChromeOn = tabs;
            ShowTabsTopBar(tabs);
            ShowTabsSidebar(tabs);
            ShowTabsFooter(tabs);
            if (tabs)
            {
                RefreshTabsChrome();
                _checkedTimer ??= CreateCheckedTimer();
                _checkedTimer.Start();
            }
            else _checkedTimer?.Stop();
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.ApplyTabsChrome] Failed — {ex.Message}");
        }
    }

    private DispatcherQueueTimer CreateCheckedTimer()
    {
        var t = DispatcherQueue.CreateTimer();
        t.Interval = TimeSpan.FromSeconds(30);
        t.Tick += (_, _) => { if (_tabsChromeOn) RefreshTabsChrome(); };
        return t;
    }

    private static T? FindAncestor<T>(DependencyObject? from, Func<T, bool>? match = null) where T : class
    {
        var cur = from;
        while (cur != null)
        {
            if (cur is T t && (match == null || match(t))) return t;
            cur = (cur as FrameworkElement)?.Parent ?? VisualTreeHelper.GetParent(cur);
        }
        return null;
    }

    private void BuildTabsChrome()
    {
        // ── Locate the XAML regions (none have names) ─────────────────────────
        _xamlTopBar = FindAncestor<Border>(SettingsBtn, b => b.BorderThickness.Bottom > 0 && b.Padding.Left >= 10);
        _searchRow = FindAncestor<Border>(SearchBox);
        _chipsRow = FindAncestor<Border>(FilterDetected);
        _countsRow = FindAncestor<Border>(GameCountText);
        _sidebar = FindAncestor<Border>((_searchRow as FrameworkElement)?.Parent, b => b.BorderThickness.Right > 0);
        _footer = FindAncestor<Border>(StatusBarText);
        _mainFilterChips = FilterDetected.Parent as UIElement;
        if (_xamlTopBar == null || _searchRow == null || _chipsRow == null || _countsRow == null || _footer == null)
        {
            CrashReporter.Log("[MainWindow.BuildTabsChrome] Window regions not found — Tabs chrome not added");
            return;
        }

        // ── Top bar: XAML content and the Tabs bar share the Border ───────────
        _xamlTopBarContent = _xamlTopBar.Child;
        _xamlTopBar.Child = null;
        var holder = new Grid();
        if (_xamlTopBarContent != null) holder.Children.Add(_xamlTopBarContent);
        _tabsTopBar = BuildTabsTopBar();
        _tabsTopBar.Visibility = Visibility.Collapsed;
        holder.Children.Add(_tabsTopBar);
        _xamlTopBar.Child = holder;
        _topBarClassic = (_xamlTopBar.Background, _xamlTopBar.Padding, _xamlTopBar.BorderBrush);

        // ── Sidebar: segmented filter under the search box, Filters button by the counts ──
        var searchContent = _searchRow.Child;
        _searchRow.Child = null;
        _searchRowStack = new StackPanel { Spacing = 12 };
        if (searchContent != null) _searchRowStack.Children.Add(searchContent);
        _segmentHost = new Border { Visibility = Visibility.Collapsed };
        _searchRowStack.Children.Add(_segmentHost);
        _searchRow.Child = _searchRowStack;

        var countsContent = _countsRow.Child as FrameworkElement;
        _countsRow.Child = null;
        _countsGrid = new Grid { ColumnSpacing = 8 };
        _countsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _countsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _filtersBtn = new Button { Padding = new Thickness(10, 0, 10, 0), Visibility = Visibility.Collapsed };
        TabsTheme.Apply(_filtersBtn, TabsTheme.IconButton, height: 30, fontSize: 12);
        _filtersBtn.CornerRadius = new CornerRadius(6);
        _filtersBtn.Click += (s, e) => { _filtersOpen = !_filtersOpen; RefreshTabsChrome(); };
        ToolTipService.SetToolTip(_filtersBtn, "Engine, API, mod and store filters");
        _countsGrid.Children.Add(_filtersBtn);
        if (countsContent != null)
        {
            countsContent.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetColumn(countsContent, 1);
            _countsGrid.Children.Add(countsContent);
        }
        _countsRow.Child = _countsGrid;

        foreach (var el in new FrameworkElement?[] { _sidebar, _searchRow, _chipsRow, _countsRow })
            if (el is Border b) _sidebarClassic[b] = (b.Background, b.BorderBrush, b.Padding, b.BorderThickness);
        _footerClassic = (_footer.Background, _footer.BorderBrush);

        // Only the properties the chrome shows (the update count checks game folders, so not on every change)
        ViewModel.PropertyChanged += (s, e) =>
        {
            if (!_tabsChromeOn) return;
            if (e.PropertyName is null or nameof(ViewModel.AnyUpdateAvailable) or nameof(ViewModel.CurrentPage)
                or nameof(ViewModel.FilterMode) or nameof(ViewModel.NewWikiModsButtonVisibility))
            {
                if (e.PropertyName is null or nameof(ViewModel.AnyUpdateAvailable)) _updateCountDirty = true;
                RefreshTabsChromeSoon();
            }
        };
        _tabsChromeBuilt = true;
    }

    // ── Top bar ───────────────────────────────────────────────────────────────

    private Grid BuildTabsTopBar()
    {
        // Brand: the app icon (the mockup's placeholder mark) + RHI / Simplified PC Gaming
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, MinWidth = 220, VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(new Border
        {
            Width = 32, Height = 32, CornerRadius = new CornerRadius(8),
            Background = TabsTheme.B("#16203A"), BorderBrush = TabsTheme.B("#2B3A63"), BorderThickness = new Thickness(1),
            Child = new Image
            {
                Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///icon.ico")),
                Width = 24, Height = 24, Stretch = Stretch.Uniform,
            },
        });
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock { Text = "RHI", FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.Bold, CharacterSpacing = 20, Foreground = TabsTheme.B(TabsTheme.TextPrimary) });
        names.Children.Add(new TextBlock { Text = "Simplified PC Gaming", FontSize = 11, Foreground = TabsTheme.B(TabsTheme.TextMuted) });
        brand.Children.Add(names);
        // The brand takes you back to your games (from Settings)
        brand.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        brand.Tapped += (s, e) => ViewModel.NavigateToGameView();
        TabsTheme.UseHandCursor(brand);

        // Nav: Available Mods (New) / Shaders & Addons. Both open over the game page, so there is
        // no Library tab: the brand and the Settings gear lead back to the games.
        var nav = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        _navModsNew = new Border
        {
            Padding = new Thickness(7, 1, 7, 1), CornerRadius = new CornerRadius(6),
            Background = TabsTheme.B("#1C3A2A"), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = "New", FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = TabsTheme.B("#7FE0A0") },
            Visibility = Visibility.Collapsed,
        };
        var mods = MakeNavItem("Available Mods", _navModsNew, (s, e) =>
        {
            if (NewModsBtn.Visibility == Visibility.Visible) NewModsButton_Click(NewModsBtn, e);
            else HdrModsListBtn_Click(HdrModsListBtn, e);
        });
        TabsTheme.SetHandTip(mods, ToolTipService.GetToolTip(HdrModsListBtn) as string ?? "Browse every RenoDX and Luma mod");
        var shaders = MakeNavItem("Shaders & Addons", null, null);
        shaders.Tapped += (s, e) =>
        {
            var menu = new MenuFlyout();
            var a = new MenuFlyoutItem { Text = "Global Shaders" };
            a.Click += (s2, e2) => ChooseShadersButton_Click(ChooseShadersItem, e2);
            var b = new MenuFlyoutItem { Text = "Global Addons" };
            b.Click += (s2, e2) => ReShadeAddonsButton_Click(ReShadeAddonsItem, e2);
            menu.Items.Add(a);
            menu.Items.Add(b);
            menu.ShowAt(shaders, new FlyoutShowOptions { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft });
        };
        nav.Children.Add(mods);
        nav.Children.Add(shaders);

        // Right: checked time, refresh, Update All, Help, Settings
        _checkedText = new TextBlock { FontSize = 12, Foreground = TabsTheme.B(TabsTheme.TextMuted), VerticalAlignment = VerticalAlignment.Center };
        var refresh = TabsTheme.MakeIconButton("", TabsTheme.IconButton, 16); // refresh
        refresh.Click += (s, e) => RefreshButton_Click(RefreshBtn, e);
        ToolTipService.SetToolTip(refresh, ToolTipService.GetToolTip(RefreshBtn) as string ?? "Check for updates and rescan your games");
        _updateAllHost = new Grid();

        var help = new Button { Padding = new Thickness(12, 0, 12, 0) };
        var helpContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        helpContent.Children.Add(new TextBlock { Text = "Help", VerticalAlignment = VerticalAlignment.Center });
        helpContent.Children.Add(TabsTheme.Icon("", 11));
        help.Content = helpContent;
        TabsTheme.Apply(help, TabsTheme.IconButton, height: 38, fontSize: 14);
        help.Click += (s, e) => ShowHelpMenu(help);

        var settings = TabsTheme.MakeIconButton(TabsTheme.GlyphSettings, TabsTheme.IconButton, 16);
        // Opens Settings; on Settings it goes back to the games
        settings.Click += (s, e) =>
        {
            if (ViewModel.CurrentPage == AppPage.Settings) ViewModel.NavigateToGameView();
            else SettingsButton_Click(SettingsBtn, e);
        };
        ToolTipService.SetToolTip(settings, "Settings");
        _settingsGear = settings;

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(_checkedText);
        right.Children.Add(refresh);
        right.Children.Add(_updateAllHost);
        right.Children.Add(help);
        right.Children.Add(settings);

        var bar = new Grid { ColumnSpacing = 24 };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.Children.Add(brand);
        Grid.SetColumn(nav, 1); bar.Children.Add(nav);
        Grid.SetColumn(right, 2); bar.Children.Add(right);
        return bar;
    }

    private Border MakeNavItem(string text, UIElement? extra, TappedEventHandler? onTap)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        sp.Children.Add(new TextBlock { Text = text, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
        if (extra != null) sp.Children.Add(extra);
        var item = new Border
        {
            Padding = new Thickness(14, 8, 14, 8),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Child = sp,
        };
        SetNavSelected(item, false);
        TabsTheme.UseHandCursor(item);
        item.PointerEntered += (s, e) =>
        {
            if (item.Tag is not true) item.Background = TabsTheme.B("#1E2738");
        };
        item.PointerExited += (s, e) => { if (item.Tag is not true) item.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent); };
        if (onTap != null) item.Tapped += onTap;
        return item;
    }

    private static void SetNavSelected(Border item, bool selected)
    {
        item.Tag = selected;
        item.Background = selected ? TabsTheme.B("#18202E") : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        if (item.Child is StackPanel { Children.Count: > 0 } sp && sp.Children[0] is TextBlock tb)
        {
            tb.Foreground = TabsTheme.B(selected ? TabsTheme.TextPrimary : TabsTheme.TextSecondary);
            tb.FontWeight = selected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
        }
    }

    private void ShowHelpMenu(FrameworkElement anchor)
    {
        var menu = new MenuFlyout();
        void Add(string text, RoutedEventHandler h)
        {
            var i = new MenuFlyoutItem { Text = text };
            i.Click += h;
            menu.Items.Add(i);
        }
        Add("Quick Start", (s, e) => FaqButton_Click(FaqBtn, e));
        Add("Guide", (s, e) => SupportGuide_Click(s, e));
        Add("Discord", (s, e) => SupportDiscord_Click(s, e));
        menu.Items.Add(new MenuFlyoutSeparator());
        Add("RenoDX Wiki", (s, e) => LinkRenoDxWiki_Click(s, e));
        Add("Luma Wiki", (s, e) => LinkLumaWiki_Click(s, e));
        Add("RHI GitHub", (s, e) => LinkRhiGithub_Click(s, e));
        Add("ReLimiter GitHub", (s, e) => LinkReLimiterGithub_Click(s, e));
        menu.Items.Add(new MenuFlyoutSeparator());
        Add("Patch notes", (s, e) => PatchNotesLink_Click(s, e));
        Add("Message of the day", (s, e) => MotdButton_Click(s, e));
        Add("Update log", (s, e) => UpdateLogButton_Click(UpdateLogBtn, e));
        Add("About", (s, e) => AboutButton_Click(s, e));
        Add("Donate", (s, e) => DonateButton_Click(DonateBtn, e));
        menu.ShowAt(anchor, new FlyoutShowOptions { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight });
    }

    private void ShowTabsTopBar(bool tabs)
    {
        if (_xamlTopBar == null || _tabsTopBar == null) return;
        if (_xamlTopBarContent != null) _xamlTopBarContent.Visibility = tabs ? Visibility.Collapsed : Visibility.Visible;
        _tabsTopBar.Visibility = tabs ? Visibility.Visible : Visibility.Collapsed;
        if (tabs)
        {
            _xamlTopBar.Background = TabsTheme.B(TabsTheme.PanelBg);
            _xamlTopBar.BorderBrush = TabsTheme.B(TabsTheme.CardBorder);
            _xamlTopBar.Padding = new Thickness(20, 12, 20, 12);
        }
        else if (_topBarClassic is { } c)
        {
            _xamlTopBar.Background = c.Bg;
            _xamlTopBar.Padding = c.Pad;
            _xamlTopBar.BorderBrush = c.Border;
        }
    }

    // ── Sidebar ───────────────────────────────────────────────────────────────

    private static readonly (string Tag, string Label)[] MainFilters =
        { ("Detected", "All"), ("Installed", "Installed"), ("Favourites", "Favourites"), ("Hidden", "Hidden") };

    private Button MainFilterButton(string tag) => tag switch
    {
        "Installed"  => FilterInstalled,
        "Favourites" => FilterFavourites,
        "Hidden"     => FilterHidden,
        _            => FilterDetected,
    };

    private void ShowTabsSidebar(bool tabs)
    {
        if (_searchRow == null || _chipsRow == null || _countsRow == null) return;
        if (_mainFilterChips != null) _mainFilterChips.Visibility = tabs ? Visibility.Collapsed : Visibility.Visible;
        if (_segmentHost != null) _segmentHost.Visibility = tabs ? Visibility.Visible : Visibility.Collapsed;
        if (_filtersBtn != null) _filtersBtn.Visibility = tabs ? Visibility.Visible : Visibility.Collapsed;
        if (!tabs)
        {
            _chipsRow.Visibility = Visibility.Visible;
            foreach (var (el, c) in _sidebarClassic)
                if (el is Border b) { b.Background = c.Bg; b.BorderBrush = c.Border; b.Padding = c.Pad; b.BorderThickness = c.Thick; }
            return;
        }

        if (_sidebar != null)
        {
            _sidebar.Background = TabsTheme.B(TabsTheme.PanelBg);
            _sidebar.BorderBrush = TabsTheme.B(TabsTheme.CardBorder);
        }
        // Game list rows: mockup selection (#1A2336 with an accent ring) and hover (#151C29).
        // Read by list items when they're created, so rows already shown pick it up as they refresh.
        var r = GameList.Resources;
        TabsTheme.SetResource(r, "ListViewItemBackgroundSelected", TabsTheme.B("#1A2336"));
        TabsTheme.SetResource(r, "ListViewItemBackgroundSelectedPointerOver", TabsTheme.B("#1E2840"));
        TabsTheme.SetResource(r, "ListViewItemBackgroundSelectedPressed", TabsTheme.B("#1A2336"));
        TabsTheme.SetResource(r, "ListViewItemBackgroundPointerOver", TabsTheme.B(TabsTheme.RowHover));
        TabsTheme.SetResource(r, "ListViewItemSelectionIndicatorBrush", TabsTheme.B(TabsTheme.Accent));

        _searchRow.Background = TabsTheme.B(TabsTheme.PanelBg);
        _searchRow.Padding = new Thickness(16, 16, 16, 6);
        _chipsRow.Background = TabsTheme.B(TabsTheme.PanelBg);
        _chipsRow.BorderThickness = new Thickness(0);
        _chipsRow.Padding = new Thickness(16, 6, 16, 6);
        _countsRow.Padding = new Thickness(16, 6, 16, 8);
    }

    private void RefreshSidebar()
    {
        if (_segmentHost == null || _filtersBtn == null || _chipsRow == null) return;
        var active = ViewModel.ActiveFilters;
        int sel = Array.FindIndex(MainFilters, f => active.Contains(f.Tag));
        if (sel < 0) sel = 0;
        var key = string.Join(",", active.OrderBy(x => x)) + "|" + sel;
        if (_segmentHost.Tag as string != key)
        {
            _segmentHost.Tag = key;
            _segmentHost.Child = TabsTheme.MakeSegmented(MainFilters.Select(f => f.Label).ToList(), sel,
                i => Filter_Click(MainFilterButton(MainFilters[i].Tag), new RoutedEventArgs()),
                track: TabsTheme.Card);
        }
        int extra = active.Count(f => !MainFilters.Any(m => m.Tag == f));
        _filtersBtn.Content = MakeFiltersLabel(extra);
        _chipsRow.Visibility = _filtersOpen ? Visibility.Visible : Visibility.Collapsed;
    }

    private static StackPanel MakeFiltersLabel(int extra)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        sp.Children.Add(TabsTheme.Icon("", 12)); // filter
        sp.Children.Add(new TextBlock { Text = extra > 0 ? $"Filters ({extra})" : "Filters", VerticalAlignment = VerticalAlignment.Center });
        return sp;
    }

    // ── Footer ────────────────────────────────────────────────────────────────

    private void ShowTabsFooter(bool tabs)
    {
        if (_footer == null) return;
        if (tabs)
        {
            _footer.Background = TabsTheme.B(TabsTheme.PanelBg);
            _footer.BorderBrush = TabsTheme.B(TabsTheme.CardBorder);
        }
        else if (_footerClassic is { } c)
        {
            _footer.Background = c.Bg;
            _footer.BorderBrush = c.Border;
        }
    }

    // ── State ─────────────────────────────────────────────────────────────────

    private void RefreshTabsChromeSoon()
    {
        if (_chromeRefreshQueued) return;
        _chromeRefreshQueued = true;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            _chromeRefreshQueued = false;
            if (_tabsChromeOn) RefreshTabsChrome();
        });
    }

    private int _lastUpdateCount = -1;
    private bool _updateCountDirty = true;

    private void RefreshTabsChrome()
    {
        if (!_tabsChromeBuilt) return;
        try
        {
            if (_settingsGear != null)
            {
                // Lit while Settings is open (set directly: hover colours are fixed at template load)
                bool onSettings = ViewModel.CurrentPage == AppPage.Settings;
                _settingsGear.Background  = TabsTheme.B(onSettings ? "#22304A" : TabsTheme.IconButton.Bg);
                _settingsGear.BorderBrush = TabsTheme.B(onSettings ? TabsTheme.Accent : TabsTheme.IconButton.Border);
                _settingsGear.Foreground  = TabsTheme.B(onSettings ? TabsTheme.TextPrimary : TabsTheme.IconButton.Fg);
                ToolTipService.SetToolTip(_settingsGear, onSettings ? "Back to your games" : "Settings");
            }
            if (_navModsNew != null) _navModsNew.Visibility = NewModsBtn.Visibility;

            if (_checkedText != null)
                _checkedText.Text = DateTime.TryParse(ViewModel.Settings.LastUpdateCheckUtc, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var last)
                    ? "Checked " + Ago(DateTime.UtcNow - last.ToUniversalTime())
                    : "";

            int count = _lastUpdateCount;
            if (_updateCountDirty || _lastUpdateCount < 0)
            {
                count = ViewModel.UpdateAllGameCount;
                _updateCountDirty = false;
            }
            if (count != _lastUpdateCount && _updateAllHost != null)
            {
                _updateAllHost.Children.Clear();
                _updateAllHost.Children.Add(MakeUpdateAllButton(count));
                _lastUpdateCount = count; // only once it's shown, so a failure retries next time
            }
            RefreshSidebar();
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.RefreshTabsChrome] {ex.GetType().Name}: {ex.Message} | {ex.StackTrace}");
        }
    }

    private static string Ago(TimeSpan t)
    {
        if (t.TotalMinutes < 1) return "just now";
        if (t.TotalMinutes < 60) return $"{(int)t.TotalMinutes}m ago";
        if (t.TotalHours < 24) return $"{(int)t.TotalHours}h {(int)t.Minutes}m ago";
        return $"{(int)t.TotalDays}d ago";
    }

    /// <summary>Update All: purple with a count when there's something to update, else "All up to date".</summary>
    private Button MakeUpdateAllButton(int count)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        Button b;
        if (count > 0)
        {
            sp.Children.Add(new TextBlock { Text = "Update All", VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(new Border
            {
                Padding = new Thickness(8, 1, 8, 1), CornerRadius = new CornerRadius(6),
                Background = TabsTheme.B("#4A3A7A"), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = count.ToString(), FontSize = 12, Foreground = TabsTheme.B("#F1E9FF") },
            });
            b = new Button { Content = sp, Padding = new Thickness(16, 0, 16, 0) };
            TabsTheme.Apply(b, TabsTheme.PurpleButton, height: 38, fontSize: 14);
            b.Click += (s, e) => UpdateAllButton_Click(UpdateBtn, e);
            ToolTipService.SetToolTip(b, ToolTipService.GetToolTip(UpdateBtn) as string ?? $"Update {count} game(s)");
        }
        else
        {
            var tick = TabsTheme.Icon("", 13); // checkmark
            tick.Foreground = TabsTheme.B("#5ECB7D");
            sp.Children.Add(tick);
            sp.Children.Add(new TextBlock { Text = "All up to date", VerticalAlignment = VerticalAlignment.Center });
            b = new Button { Content = sp, Padding = new Thickness(16, 0, 16, 0), IsEnabled = false };
            TabsTheme.Apply(b, new TabsTheme.Look("#121824", "#263042", TabsTheme.TextMuted, "#121824", true), height: 38, fontSize: 14);
        }
        return b;
    }
}
