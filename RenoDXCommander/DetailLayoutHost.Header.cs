// DetailLayoutHost.Header.cs -- the Tabs layout's game header (mockup A): big title with favourite
// star and Hide, tag chips, install path with Open folder / Config folder, links, a large Launch
// split button and the HDR / Resolution toggles.
// Like the Overview cards it holds no rules: it copies text, visibility and on/off state from the
// Classic header controls (filled by DetailPanelBuilder.PopulateDetailPanel; those stay in the
// window, hidden) and its buttons call the Classic buttons' handlers with them as sender.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Controls;

namespace RenoDXCommander;

internal sealed partial class DetailLayoutHost
{
    private Grid? _header;
    private TextBlock? _hTitle;
    private Button? _hFav;
    private FontIcon? _hFavIcon;
    private Button? _hHide;
    private WrapPanel? _hChips;
    private TextBlock? _hPath;
    private HyperlinkButton? _hConfig;
    private StackPanel? _hLinks;
    private TextBlock? _hLaunchText;
    private Button? _hHdr;
    private Button? _hRes;
    private Grid? _hToggles;
    private bool _hFavOn, _hHdrOn, _hResOn;

    /// <summary>Shows the Classic header rows (DetailPanel children 0 and 1) or hides them for Tabs.</summary>
    private void SetClassicHeaderVisible(bool visible)
    {
        var panel = SectionsPanel;
        for (int i = 0; i < Math.Min(FirstSectionIndex, panel.Children.Count); i++)
            panel.Children[i].Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private Grid BuildHeader()
    {
        var w = _window;

        // ── Left: title row, chips, path, links ──────────────────────────────
        _hTitle = new TextBlock
        {
            FontSize = 32,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            LineHeight = 35,
            TextWrapping = TextWrapping.Wrap,
            Foreground = TabsTheme.B(TabsTheme.TextPrimary),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _hFavIcon = TabsTheme.Icon("", 16); // star outline (filled glyph swapped in when on)
        _hFav = new Button { Width = 36, Padding = new Thickness(0), Content = _hFavIcon };
        TabsTheme.Apply(_hFav, FavLook(false), height: 36);
        _hFav.Click += (s, e) => { w.HeaderFavourite(e); RefreshHeaderSoon(); };

        _hHide = new Button { Padding = new Thickness(12, 0, 12, 0) };
        TabsTheme.Apply(_hHide, TabsTheme.IconButton with { Fg = TabsTheme.TextSecondary }, height: 36, fontSize: 12);
        _hHide.Click += (s, e) => { w.HeaderHide(e); RefreshHeaderSoon(); };

        var titleRow = new WrapPanel { HorizontalSpacing = 12, VerticalSpacing = 8 };
        titleRow.Children.Add(_hTitle);
        titleRow.Children.Add(_hFav);
        titleRow.Children.Add(_hHide);

        _hChips = new WrapPanel { HorizontalSpacing = 6, VerticalSpacing = 6 };

        _hPath = new TextBlock
        {
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 12,
            Foreground = TabsTheme.B(TabsTheme.TextMuted),
            TextWrapping = TextWrapping.WrapWholeWords,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var openFolder = MakeLink("Open folder", e => w.HeaderOpenFolder(e), 12, folderIcon: true);
        _hConfig = MakeLink("Config folder", e => w.HeaderOpenConfig(e), 12, folderIcon: true);
        var pathRow = new WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 4 };
        pathRow.Children.Add(_hPath);
        pathRow.Children.Add(openFolder);
        pathRow.Children.Add(_hConfig);

        _hLinks = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };

        var left = new StackPanel { Spacing = 10, VerticalAlignment = VerticalAlignment.Top };
        left.Children.Add(titleRow);
        left.Children.Add(_hChips);
        left.Children.Add(pathRow);
        left.Children.Add(_hLinks);

        // ── Right: Launch split button, HDR / Resolution toggles ─────────────
        _hLaunchText = new TextBlock { Text = "Launch", FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
        var launchContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center };
        launchContent.Children.Add(TabsTheme.Icon("", 15)); // play
        launchContent.Children.Add(_hLaunchText);
        var launch = new Button { Content = launchContent, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(28, 0, 28, 0) };
        TabsTheme.Apply(launch, new TabsTheme.Look("#1D5C36", "#2F7A4A", "#EFFFF4", "#236B40", true), height: 48, fontSize: 16);
        launch.CornerRadius = new CornerRadius(10, 0, 0, 10);
        launch.Click += (s, e) => { w.HeaderLaunch(e); RefreshHeaderSoon(); };
        ToolTipService.SetToolTip(launch, "Launch the game");

        var more = new Button { Width = 44, Padding = new Thickness(0), Content = TabsTheme.Icon("", 13) }; // chevron down
        TabsTheme.Apply(more, new TabsTheme.Look("#1A5231", "#2F7A4A", "#CFF5DC", "#205E3A", false), height: 48);
        more.CornerRadius = new CornerRadius(0, 10, 10, 0);
        more.BorderThickness = new Thickness(0, 1, 1, 1);
        ToolTipService.SetToolTip(more, "Launch options");
        more.Click += (s, e) => ShowLaunchMenu(more);

        var split = new Grid();
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        split.Children.Add(launch);
        Grid.SetColumn(more, 1);
        split.Children.Add(more);

        _hToggles = new Grid { ColumnSpacing = 8 };
        _hToggles.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _hToggles.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _hHdr = MakeToggle("HDR on launch", false, e => w.HeaderToggleHdr(e));
        _hRes = MakeToggle("Resolution switch", false, e => w.HeaderToggleRes(e));
        _hToggles.Children.Add(_hHdr);
        Grid.SetColumn(_hRes, 1);
        _hToggles.Children.Add(_hRes);

        var right = new StackPanel { Spacing = 10, MinWidth = 260, VerticalAlignment = VerticalAlignment.Top };
        right.Children.Add(split);
        right.Children.Add(_hToggles);

        var grid = new Grid { ColumnSpacing = 20 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(left);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    private static TabsTheme.Look FavLook(bool on) => on
        ? new TabsTheme.Look("#221C10", "#4F4123", "#F0C35A", "#2A2214", false)
        : new TabsTheme.Look("#141A26", "#263042", TabsTheme.TextMuted, "#1E2738", false);

    private static TabsTheme.Look ToggleLook(bool on) => on
        ? new TabsTheme.Look("#221A40", "#4A3A7A", "#D9C8FF", "#2A2050", true)
        : new TabsTheme.Look("#141A26", "#263042", TabsTheme.TextSecondary, "#1E2738", false);

    private Button MakeToggle(string label, bool on, Action<RoutedEventArgs> click)
    {
        var b = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(8, 0, 8, 0) };
        TabsTheme.Apply(b, ToggleLook(on), height: 36, fontSize: 12);
        b.Click += (s, e) => { click(e); RefreshHeaderSoon(); };
        return b;
    }

    private static HyperlinkButton MakeLink(string text, Action<RoutedEventArgs> click, double fontSize = 13, bool folderIcon = false)
    {
        object content = text;
        if (folderIcon)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            sp.Children.Add(TabsTheme.Icon("", 13)); // folder
            sp.Children.Add(new TextBlock { Text = text, FontSize = fontSize, VerticalAlignment = VerticalAlignment.Center });
            content = sp;
        }
        var link = new HyperlinkButton
        {
            Content = content,
            FontSize = fontSize,
            Padding = new Thickness(0),
            Foreground = TabsTheme.B(TabsTheme.Link),
            VerticalAlignment = VerticalAlignment.Center,
        };
        TabsTheme.SetResource(link.Resources, "HyperlinkButtonForegroundPointerOver", TabsTheme.B("#B7D2FF"));
        TabsTheme.SetResource(link.Resources, "HyperlinkButtonBackgroundPointerOver", new SolidColorBrush(Microsoft.UI.Colors.Transparent));
        TabsTheme.SetResource(link.Resources, "HyperlinkButtonBackgroundPressed", new SolidColorBrush(Microsoft.UI.Colors.Transparent));
        link.Click += (s, e) => click(e);
        TabsTheme.UseHandCursor(link);
        return link;
    }

    private void ShowLaunchMenu(FrameworkElement anchor)
    {
        var card = _window.ViewModel.SelectedGame;
        if (card == null) return;
        var menu = new MenuFlyout();
        string? exe = null;
        try
        {
            var names = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                .GetRequiredService<Services.IGameNameService>(App.Services);
            if (names.LaunchExeOverrides.TryGetValue(card.GameName, out var saved) && !string.IsNullOrEmpty(saved)) exe = saved;
        }
        catch (InvalidOperationException ex) { Services.CrashReporter.Log($"[DetailLayoutHost.LaunchMenu] {ex.Message}"); }
        if (exe == null && _window.ViewModel.Manifest?.LaunchExeOverrides?.TryGetValue(card.GameName, out var m) == true) exe = m;
        var exeItem = new MenuFlyoutItem
        {
            Text = "Executable: " + (string.IsNullOrWhiteSpace(exe) ? "auto-detect" : System.IO.Path.GetFileName(exe)),
        };
        exeItem.Click += (s, e) => SelectTab("GameOverrides");
        var argsItem = new MenuFlyoutItem { Text = "Launch arguments..." };
        argsItem.Click += (s, e) => SelectTab("GameOverrides");
        var shortcutItem = new MenuFlyoutItem { Text = "Create desktop shortcut" };
        shortcutItem.Click += async (s, e) =>
        {
            try { await _window.HeaderCreateShortcutAsync(card); }
            catch (Exception ex) { Services.CrashReporter.Log($"[DetailLayoutHost.LaunchMenu] Shortcut failed — {ex.Message}"); }
        };
        menu.Items.Add(exeItem);
        menu.Items.Add(argsItem);
        menu.Items.Add(shortcutItem);
        menu.ShowAt(anchor, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
        {
            Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight,
        });
    }

    private bool _headerRefreshQueued;

    /// <summary>Refreshes the header after a click has updated the Classic controls.</summary>
    private void RefreshHeaderSoon()
    {
        if (_headerRefreshQueued) return;
        _headerRefreshQueued = true;
        _window.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            _headerRefreshQueued = false;
            RefreshHeader();
        });
    }

    /// <summary>Copies the Classic header's current state into the Tabs header.</summary>
    internal void RefreshHeader()
    {
        if (_header == null || !IsTabs) return;
        var w = _window;
        var card = w.ViewModel.SelectedGame;

        _hTitle!.Text = w.HeaderGameName.Text;

        // Favourite star (amber, filled) / outline
        bool fav = card?.IsFavourite == true;
        if (fav != _hFavOn || _hFav!.Tag == null)
        {
            _hFavOn = fav;
            _hFav!.Tag = true;
            _hFavIcon!.Glyph = fav ? "" : "";
            TabsTheme.Apply(_hFav, FavLook(fav), height: 36);
        }
        ToolTipService.SetToolTip(_hFav, fav ? "Favourite. Click to remove it from Favourites." : "Add this game to Favourites");

        _hHide!.Content = w.HeaderHideText.Text;
        ToolTipService.SetToolTip(_hHide, ToolTipService.GetToolTip(w.HeaderHideButton) as string);

        // Chips: store, engine, graphics APIs, bitness, then mod authors (blue)
        _hChips!.Children.Clear();
        if (w.HeaderSourceBadge.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(w.HeaderSourceText.Text))
        {
            var chip = MakeChip(w.HeaderSourceText.Text, accent: false);
            if (w.HeaderSourceBadge.Tag is int appId)
            {
                TabsTheme.SetHandTip(chip, "Open the Steam Community page");
                MakeClickable(chip, () => _ = Windows.System.Launcher.LaunchUriAsync(new Uri($"https://steamcommunity.com/app/{appId}")));
            }
            _hChips.Children.Add(chip);
        }
        if (w.HeaderEngineBadge.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(w.HeaderEngineText.Text))
        {
            var chip = MakeChip(w.HeaderEngineText.Text, accent: false);
            if (ToolTipService.GetToolTip(w.HeaderEngineBadge) is string et) ToolTipService.SetToolTip(chip, et);
            _hChips.Children.Add(chip);
        }
        if (w.HeaderApiBadges.Visibility == Visibility.Visible)
            foreach (var badge in w.HeaderApiBadges.Children)
                if (FirstText(badge) is { } api) _hChips.Children.Add(MakeChip(api, accent: false));
        if (card != null) _hChips.Children.Add(MakeChip(card.Is32Bit ? "32-bit" : "64-bit", accent: false));
        if (w.HeaderAuthorBadges.Visibility == Visibility.Visible)
            foreach (var badge in w.HeaderAuthorBadges.Children)
                if (FirstText(badge) is { } author)
                {
                    // "RenoDX: Marat" reads "RenoDX by Marat" in the mockup
                    var text = author.Contains(": ") ? author.Replace(": ", " by ") : author;
                    var chip = MakeChip(text, accent: true);
                    // Same donation link as the Classic author badge
                    if (ViewModels.GameCardViewModel.GetAuthorDonationUrl(author) is { } url)
                    {
                        TabsTheme.SetHandTip(chip, TooltipText(badge));
                        MakeClickable(chip, () => _ = Windows.System.Launcher.LaunchUriAsync(new Uri(url)));
                    }
                    else if (TooltipText(badge) is { } tip) ToolTipService.SetToolTip(chip, tip);
                    _hChips.Children.Add(chip);
                }

        _hPath!.Text = w.HeaderInstallPath.Text;
        _hConfig!.Visibility = w.HeaderConfigButton.Visibility;

        // Links: the same ones the Classic header shows for this game
        _hLinks!.Children.Clear();
        void Link(Button src, string label, Action<RoutedEventArgs> click)
        {
            if (src.Visibility != Visibility.Visible) return;
            var l = MakeLink(label, click);
            TabsTheme.SetHandTip(l, ToolTipService.GetToolTip(src) as string);
            _hLinks.Children.Add(l);
        }
        Link(w.HeaderNexusButton, "Nexus Mods", e => w.HeaderNexus(e));
        Link(w.HeaderPcgwButton, "PCGamingWiki", e => w.HeaderPcgw(e));
        Link(w.HeaderUwButton, "Ultrawide fix", e => w.HeaderUwFix(e));
        Link(w.HeaderUltraPlusButton, "Ultra+", e => w.HeaderUltraPlus(e));
        _hLinks.Visibility = _hLinks.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        _hLaunchText!.Text = card?.IsRunning == true ? "Running" : "Launch";

        // HDR / Resolution: on when the Classic toggle shows its purple "on" colour
        bool hdrOn = TabsTheme.ToneOf(w.HeaderHdrText.Foreground) == TabsTheme.Tone.Purple;
        bool resOn = TabsTheme.ToneOf(w.HeaderResText.Foreground) == TabsTheme.Tone.Purple;
        if (hdrOn != _hHdrOn || _hHdr!.Tag == null) { _hHdrOn = hdrOn; _hHdr = ReplaceToggle(_hHdr!, 0, "HDR on launch", hdrOn, e => w.HeaderToggleHdr(e)); }
        if (resOn != _hResOn || _hRes!.Tag == null) { _hResOn = resOn; _hRes = ReplaceToggle(_hRes!, 1, "Resolution switch", resOn, e => w.HeaderToggleRes(e)); }
        ToolTipService.SetToolTip(_hHdr, ToolTipService.GetToolTip(w.HeaderHdrButton) as string);
        ToolTipService.SetToolTip(_hRes, ToolTipService.GetToolTip(w.HeaderResButton) as string);
        bool resShown = w.HeaderResButton.Visibility == Visibility.Visible;
        _hRes.Visibility = resShown ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetColumnSpan(_hHdr, resShown ? 1 : 2);

        if (IsSpotlight) RefreshHeroArt();
    }

    /// <summary>Hover colours are read when a button loads, so a toggle that changes look is replaced.</summary>
    private Button ReplaceToggle(Button old, int column, string label, bool on, Action<RoutedEventArgs> click)
    {
        var b = MakeToggle(label, on, click);
        b.Tag = true;
        Grid.SetColumn(b, column);
        int idx = _hToggles!.Children.IndexOf(old);
        if (idx >= 0) _hToggles.Children[idx] = b; else _hToggles.Children.Add(b);
        return b;
    }

    private static Border MakeChip(string text, bool accent) => new()
    {
        Padding = new Thickness(10, 3, 10, 3),
        CornerRadius = new CornerRadius(6),
        Background = TabsTheme.B(accent ? "#141F33" : "#151C29"),
        BorderBrush = TabsTheme.B(accent ? "#2A3D63" : "#263042"),
        BorderThickness = new Thickness(1),
        Child = new TextBlock
        {
            Text = text,
            FontSize = 12,
            Foreground = TabsTheme.B(accent ? "#A9C6FF" : TabsTheme.TextControl),
        },
    };

    private static void MakeClickable(Border el, Action onClick)
    {
        TabsTheme.UseHandCursor(el);
        el.Tapped += (s, e) => onClick();
    }
}
