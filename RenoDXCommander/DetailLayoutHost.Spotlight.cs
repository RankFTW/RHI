// DetailLayoutHost.Spotlight.cs -- the Spotlight layout (mockup D, docs/layouts-plan-2.9.0.md step 4).
// Built on the Tabs pieces: the Tabs game header sits in a hero banner over the game's Steam
// artwork, followed by "What's installed" tiles (copied from the hidden Components rows, like the
// Overview cards) and "Tune this game" tiles (the At a glance tiles). A tile opens its section in
// a panel that slides in from the right: the section's container is moved into the panel while it
// is open and put back when it closes, so the shared section builders keep working unchanged.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using RenoDXCommander.Controls;
using RenoDXCommander.Services;

namespace RenoDXCommander;

internal sealed partial class DetailLayoutHost
{
    // Hero
    private Border? _hero;
    private Image? _heroImage;
    private Border? _heroContent;
    private string? _heroGame;
    private const double HeroMinHeight = 340;

    // Page body
    private StackPanel? _spotBody;
    private AutoFitGrid? _statusGrid;
    private AutoFitGrid? _tuneGrid;
    private readonly List<StatusTile> _statusTiles = new();

    // Slide-in panel
    private Grid? _panelOverlay;
    private Border? _panelSheet;
    private TextBlock? _panelTitle;
    private StackPanel? _panelHost;
    private ScrollViewer? _panelScroll;
    private string? _panelKey;

    private bool IsInPanel(string key) => string.Equals(_panelKey, key, StringComparison.Ordinal);

    // ── Hero mode ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Spotlight: the header goes into the hero, followed by the page body, and the tab strip is
    /// dropped. Tabs: header and tab strip as before.
    /// </summary>
    private void ApplyHeroMode(bool spotlight)
    {
        if (_tabChrome == null || _header == null || _hTitle == null) return;
        if (spotlight)
        {
            EnsureSpotlightParts();
            if (_overview == null) BuildOverview();
        }

        bool inHero = _heroContent != null && ReferenceEquals(_heroContent.Child, _header);
        if (inHero != spotlight)
        {
            _tabChrome.Children.Clear();
            if (_heroContent != null) _heroContent.Child = null;
            if (spotlight)
            {
                _heroContent!.Child = _header;
                _tabChrome.Children.Add(_hero!);
                _tabChrome.Children.Add(_spotBody!);
            }
            else
            {
                _tabChrome.Children.Add(_header);
                if (_tabStrip != null) _tabChrome.Children.Add(_tabStrip);
            }
            // Mockup D: a bigger, heavier title over the artwork
            _hTitle.FontSize = spotlight ? 44 : 32;
            _hTitle.LineHeight = spotlight ? 48 : 35;
            _hTitle.FontWeight = spotlight ? Microsoft.UI.Text.FontWeights.ExtraBold : Microsoft.UI.Text.FontWeights.Bold;
        }
        if (spotlight) RefreshSpotlight();
    }

    private void EnsureSpotlightParts()
    {
        if (_hero != null) return;

        _heroImage = new Image
        {
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            Height = HeroMinHeight,
            Opacity = 0,
            OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(250) },
        };
        // Dark fades so the title and buttons stay readable on any artwork
        var bottomFade = new Border { Background = Fade(vertical: true, ("#260B0F17", 0), ("#990B0F17", 0.45), ("#F20B0F17", 1)) };
        var sideFade = new Border { Background = Fade(vertical: false, ("#B30B0F17", 0), ("#400B0F17", 0.65), ("#000B0F17", 1)) };
        _heroContent = new Border { Padding = new Thickness(28), VerticalAlignment = VerticalAlignment.Bottom };
        // The artwork fills the banner and is cropped to it; it never sets the banner's height
        _heroContent.SizeChanged += (s, e) => _heroImage.Height = Math.Max(HeroMinHeight, e.NewSize.Height);

        var layers = new Grid { MinHeight = HeroMinHeight };
        layers.Children.Add(_heroImage);
        layers.Children.Add(bottomFade);
        layers.Children.Add(sideFade);
        layers.Children.Add(_heroContent);
        _hero = new Border
        {
            CornerRadius = new CornerRadius(16),
            BorderBrush = TabsTheme.B(TabsTheme.CardBorder),
            BorderThickness = new Thickness(1),
            Background = TabsTheme.B("#10161F"),
            Child = layers,
        };
        // Right-click: the game menu (with Artwork › Set custom banner)
        _hero.RightTapped += (s, e) =>
        {
            if (_window.ViewModel.SelectedGame is not { } game) return;
            _hero.Tag = game;
            _window.CardMoreMenu_Click(_hero, e);
        };

        // What's installed: one tile per component, then "Manage components"
        _statusGrid = new AutoFitGrid { MinColumnWidth = 200, ColumnSpacing = 10, RowSpacing = 10 };
        var rows = _window.ComponentRows;
        var ul = rows.First(r => r.Key == "Ul");
        var dc = rows.First(r => r.Key == "Dc");
        foreach (var row in rows)
        {
            if (row.Key == "Dc") continue;
            var tile = new StatusTile(this, row.Key == "Ul" ? new[] { ul, dc } : new[] { row });
            _statusTiles.Add(tile);
            _statusGrid.Children.Add(tile.Root);
        }
        var manage = new Button
        {
            Content = "Manage components",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        TabsTheme.Apply(manage, TabsTheme.BlueButton, height: double.NaN, fontSize: 14);
        manage.MinHeight = 60;
        manage.CornerRadius = new CornerRadius(12);
        manage.Click += (s, e) => OpenPanel(OverviewKey);
        ToolTipService.SetToolTip(manage, "Install, update and configure this game's components");
        _statusGrid.Children.Add(manage);

        // Six tiles: three per row (two when narrow), so each summary has room
        _tuneGrid = new AutoFitGrid { MinColumnWidth = 260, MaxColumns = 3, ColumnSpacing = 10, RowSpacing = 10 };

        _spotBody = new StackPanel { Spacing = 18 };
        var status = new StackPanel { Spacing = 10 };
        status.Children.Add(TabsTheme.SectionHeading("What's installed"));
        status.Children.Add(_statusGrid);
        var tune = new StackPanel { Spacing = 10 };
        tune.Children.Add(TabsTheme.SectionHeading("Tune this game"));
        tune.Children.Add(_tuneGrid);
        _spotBody.Children.Add(status);
        _spotBody.Children.Add(tune);
    }

    private static LinearGradientBrush Fade(bool vertical, params (string Colour, double Offset)[] stops)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = vertical ? new Windows.Foundation.Point(0, 1) : new Windows.Foundation.Point(1, 0),
        };
        foreach (var (c, o) in stops)
            brush.GradientStops.Add(new GradientStop { Color = TabsTheme.B(c).Color, Offset = o });
        return brush;
    }

    /// <summary>Spotlight's version of "which section shows": only the one in the panel.</summary>
    private void ApplySpotlightSelection()
    {
        foreach (var key in SectionKeys)
            if (GetContainer(key) is { } container)
                container.Visibility = IsInPanel(key) ? Visibility.Visible : Visibility.Collapsed;
        RefreshSpotlight();
    }

    private void RefreshSpotlight()
    {
        if (!IsSpotlight || _hero == null) return;
        RefreshSpotStatus();
        RefreshTuneTiles();
        RefreshHeroArt();
        if (IsInPanel(OverviewKey)) RefreshOverviewCards();
    }

    internal void RefreshSpotStatus()
    {
        foreach (var tile in _statusTiles) tile.Update();
    }

    internal void RefreshTuneTiles()
    {
        if (_tuneGrid == null || !IsSpotlight) return;
        _tuneGrid.Children.Clear();
        foreach (var key in TabKeys)
            if (key != OverviewKey) _tuneGrid.Children.Add(MakeGlanceTile(key)); // a tap opens the panel (SelectTab)
    }

    // ── Hero artwork ──────────────────────────────────────────────────────────

    private void RefreshHeroArt()
    {
        if (_heroImage == null) return;
        var card = _window.ViewModel.SelectedGame;
        var name = card?.GameName;
        if (string.Equals(name, _heroGame, StringComparison.OrdinalIgnoreCase)) return;
        _heroGame = name;
        _heroImage.Opacity = 0;
        _heroImage.Source = null;
        if (card == null) return;

        if (SteamArtworkService.TryGetCustom(MainWindow.ArtKey(card), SteamArtworkService.Kind.Hero) is { } custom)
        {
            SetHero(custom);
            return;
        }
        var appId = DetailPanelBuilder.ResolveCommunityAppId(card, _window.ViewModel.Manifest);
        if (appId is not int id) return;
        if (SteamArtworkService.TryGetCached(id, SteamArtworkService.Kind.Hero) is { } cached)
        {
            SetHero(cached);
            return;
        }
        var token = _window.LifetimeToken;
        _ = Task.Run(async () =>
        {
            var path = await SteamArtworkService.GetAsync(id, SteamArtworkService.Kind.Hero, token).ConfigureAwait(false);
            if (path == null || token.IsCancellationRequested) return;
            _window.DispatcherQueue.TryEnqueue(() =>
            {
                if (string.Equals(_heroGame, name, StringComparison.OrdinalIgnoreCase)) SetHero(path);
            });
        });
    }

    /// <summary>Loads the selected game's banner again (after its custom art changed).</summary>
    internal void ReloadHeroArt()
    {
        _heroGame = null;
        RefreshHeroArt();
    }

    private void SetHero(string path)
    {
        if (_heroImage == null) return;
        try
        {
            _heroImage.Source = new BitmapImage(new Uri(path)) { DecodePixelWidth = 1920 };
            _heroImage.Opacity = 1;
        }
        catch (UriFormatException ex) { CrashReporter.Log($"[DetailLayoutHost.SetHero] {ex.Message}"); }
    }

    // ── Slide-in panel ────────────────────────────────────────────────────────

    private void EnsurePanel()
    {
        if (_panelOverlay != null) return;

        var scrim = new Border { Background = TabsTheme.B("#99050810") };
        scrim.Tapped += (s, e) => ClosePanel();

        _panelTitle = new TextBlock
        {
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = TabsTheme.B(TabsTheme.TextPrimary),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var close = TabsTheme.MakeIconButton(TabsTheme.GlyphClose, TabsTheme.IconButton, 12);
        close.Click += (s, e) => ClosePanel();
        ToolTipService.SetToolTip(close, "Close");
        var head = new Grid { Padding = new Thickness(24, 20, 20, 12) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(_panelTitle);
        Grid.SetColumn(close, 1);
        head.Children.Add(close);

        _panelHost = new StackPanel { Spacing = 20 };
        _panelScroll = new ScrollViewer
        {
            Content = new Border { Padding = new Thickness(24, 4, 24, 32), Child = _panelHost },
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        var sheetGrid = new Grid();
        sheetGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        sheetGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        sheetGrid.Children.Add(head);
        Grid.SetRow(_panelScroll, 1);
        sheetGrid.Children.Add(_panelScroll);

        _panelSheet = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = TabsTheme.B(TabsTheme.PanelBg),
            BorderBrush = TabsTheme.B(TabsTheme.CardBorder),
            BorderThickness = new Thickness(1, 0, 0, 0),
            Child = sheetGrid,
            Transitions = new TransitionCollection { new PaneThemeTransition { Edge = Microsoft.UI.Xaml.Controls.Primitives.EdgeTransitionLocation.Right } },
        };

        _panelOverlay = new Grid { Visibility = Visibility.Collapsed };
        _panelOverlay.Children.Add(scrim);
        _panelOverlay.Children.Add(_panelSheet);
        _panelOverlay.SizeChanged += (s, e) =>
            _panelSheet.Width = Math.Min(e.NewSize.Width, Math.Clamp(e.NewSize.Width * 0.62, 560, 980));
        Grid.SetColumnSpan(_panelOverlay, 2);
        Grid.SetRowSpan(_panelOverlay, 2);
        _window.GameViewPanel.Children.Add(_panelOverlay);
    }

    /// <summary>Opens a section (or Overview's component cards) in the slide-in panel.</summary>
    internal void OpenPanel(string key)
    {
        if (!IsSpotlight) return;
        EnsurePanel();
        if (IsInPanel(key) && _panelOverlay!.Visibility == Visibility.Visible) return;
        ReturnPanelContent();

        FrameworkElement? content;
        if (key == OverviewKey)
        {
            if (_overview == null) BuildOverview();
            if (_overviewGlance != null) _overviewGlance.Visibility = Visibility.Collapsed;
            SetOverviewHeadingVisible(false); // the panel title already says "Components"
            content = _overview;
        }
        else
        {
            content = GetContainer(key);
            if (content != null) SectionsPanel.Children.Remove(content);
        }
        if (content == null) return;

        _panelHost!.Children.Add(content);
        content.Visibility = Visibility.Visible;
        _panelKey = key;
        _panelTitle!.Text = key == OverviewKey ? "Components" : GetTitle(key);
        if (key == OverviewKey) RefreshOverviewCards();

        // Re-adding the sheet replays its slide-in transition
        if (_panelOverlay!.Visibility != Visibility.Visible)
        {
            _panelOverlay.Children.Remove(_panelSheet);
            _panelOverlay.Children.Add(_panelSheet);
            _panelOverlay.Visibility = Visibility.Visible;
        }
        _panelScroll?.ChangeView(null, 0, null, disableAnimation: true);
    }

    /// <summary>Closes the panel and puts its section back on the (hidden) page.</summary>
    internal void ClosePanel()
    {
        ReturnPanelContent();
        if (_panelOverlay != null) _panelOverlay.Visibility = Visibility.Collapsed;
    }

    private void ReturnPanelContent()
    {
        if (_panelKey == null || _panelHost == null) return;
        var key = _panelKey;
        _panelKey = null;
        _panelHost.Children.Clear();
        if (key == OverviewKey)
        {
            if (_overviewGlance != null) _overviewGlance.Visibility = Visibility.Visible;
            SetOverviewHeadingVisible(true);
            return;
        }
        if (GetContainer(key) is { } container)
        {
            container.Visibility = Visibility.Collapsed;
            SectionsPanel.Children.Add(container); // ApplySectionOrder puts it back in order
        }
    }

    private void SetOverviewHeadingVisible(bool visible)
    {
        if (_overview?.Children.FirstOrDefault() is StackPanel { Children.Count: > 0 } components
            && components.Children[0] is TextBlock heading)
            heading.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── One "What's installed" tile ───────────────────────────────────────────

    /// <summary>
    /// Mockup D status tile: a coloured dot, the component's name and its status. Copies the hidden
    /// Components row like the Overview cards do. The frame limiter tile covers ReLimiter and
    /// Display Commander: the installed one, or "Choose ReLimiter or Display Commander".
    /// </summary>
    private sealed class StatusTile
    {
        private readonly DetailLayoutHost _host;
        private readonly ComponentRowRef[] _rows;
        private readonly Ellipse _dot;
        private readonly TextBlock _name;
        private readonly TextBlock _status;

        internal Border Root { get; }

        internal StatusTile(DetailLayoutHost host, ComponentRowRef[] rows)
        {
            _host = host;
            _rows = rows;
            _dot = new Ellipse { Width = 10, Height = 10, VerticalAlignment = VerticalAlignment.Center };
            _name = new TextBlock
            {
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = TabsTheme.B(TabsTheme.TextPrimary),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            _status = new TextBlock
            {
                FontSize = 12,
                Foreground = TabsTheme.B(TabsTheme.TextSecondary),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(_name);
            text.Children.Add(_status);
            var grid = new Grid { ColumnSpacing = 12 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(_dot);
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            Root = new Border
            {
                Background = TabsTheme.B(TabsTheme.Card),
                BorderBrush = TabsTheme.B(TabsTheme.CardBorder),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16, 14, 16, 14),
                Child = grid,
                Visibility = Visibility.Collapsed,
            };
            TabsTheme.UseHandCursor(Root);
            TabsTheme.SetHandTip(Root, "Manage components");
            Root.PointerEntered += (s, e) => Root.Background = TabsTheme.B(TabsTheme.RowHover);
            Root.PointerExited += (s, e) => Root.Background = TabsTheme.B(TabsTheme.Card);
            Root.Tapped += (s, e) => _host.OpenPanel(OverviewKey);
        }

        internal void Update()
        {
            var visible = _rows.Where(r => r.Row.Visibility == Visibility.Visible).ToList();
            if (visible.Count == 0) { Root.Visibility = Visibility.Collapsed; return; }
            Root.Visibility = Visibility.Visible;

            var installed = visible.FirstOrDefault(r => r.Delete.IsHitTestVisible && r.Delete.Opacity > 0.05);
            if (visible.Count > 1 && installed == null)
            {
                _name.Text = "Frame limiter";
                _status.Text = "Choose " + string.Join(" or ", visible.Select(Name));
                _dot.Fill = TabsTheme.B("#3A4458");
                return;
            }
            var r = installed ?? visible[0];
            _name.Text = Name(r);
            var status = (r.Status.Text ?? "").Replace("\r", "").Replace('\n', ' ').Trim();
            var tone = TabsTheme.ToneOf(r.Status.Foreground);
            bool notInstalled = status.Length == 0 || string.Equals(status, "Ready", StringComparison.OrdinalIgnoreCase);
            _status.Text = notInstalled ? "Not installed"
                : tone != TabsTheme.Tone.Purple ? status
                : status.StartsWith("Update", StringComparison.OrdinalIgnoreCase) ? "Update available"
                : $"Update available · {status}";
            _dot.Fill = TabsTheme.B(notInstalled ? "#3A4458" : tone switch
            {
                TabsTheme.Tone.Purple => "#B898E8",
                TabsTheme.Tone.Amber  => "#F0C35A",
                TabsTheme.Tone.Red    => "#FF9AA8",
                _                     => "#5ECB7D",
            });
        }

        private static string Name(ComponentRowRef r) =>
            string.IsNullOrWhiteSpace(r.Label?.Text) ? r.FallbackName : r.Label!.Text;
    }
}
