// OverlayWindow.Panels.cs -- the RHI overlay's dashboard: every section on one screen, in columns that
// adapt to the width (ArrangeDashboard), each column scrolling on its own.
//   Home: session time, total playtime (Steam's, else RHI's own count), battery.
//   Achievements (OverlayWindow.Achievements.cs), Screenshots (OverlayWindow.Screenshots.cs).
//   This game: what RHI has installed (with versions and update marks) and the DLSS / Streamline files.
//   Info and links: your own notes for the game, RenoDX / Luma notes and the game's links.
// Sections are rebuilt each time the overlay opens (cheap, and always current).
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Overlay;

internal sealed partial class OverlayWindow
{
    private TextBlock? _sessionText;

    // One box per section; ArrangeDashboard moves the boxes between the columns
    private readonly StackPanel _homeBox = new() { Spacing = 12 };
    private readonly StackPanel _gameBox = new() { Spacing = 12 };
    private readonly StackPanel _infoBox = new() { Spacing = 12 };
    private readonly StackPanel _shotBox = new() { Spacing = 16 };
    private readonly StackPanel _achBox = new() { Spacing = 12 };
    private readonly StackPanel[] _cols = { new() { Spacing = 12 }, new() { Spacing = 12 }, new() { Spacing = 12 } };
    private readonly ScrollViewer[] _colScrollers = new ScrollViewer[3];
    private readonly Grid _dash = new() { ColumnSpacing = 28 };
    private Grid _body = null!;
    private Grid _viewerLayer = null!;
    private readonly StackPanel _viewerBox = new() { Spacing = 14 };
    private ScrollViewer _viewerScroller = null!;
    private int _layoutMode = -1;

    // Widths in layout units (window width / scale). Three columns from 1350, two from 860.
    private const double ThreeColumnsFrom = 1350, TwoColumnsFrom = 860;

    private Grid BuildDashboardShell()
    {
        for (int c = 0; c < 3; c++)
        {
            _dash.ColumnDefinitions.Add(new ColumnDefinition());
            _colScrollers[c] = new ScrollViewer
            {
                Content = _cols[c],
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(0, 0, 14, 0),
            };
            Grid.SetColumn(_colScrollers[c], c);
            _dash.Children.Add(_colScrollers[c]);
        }

        _viewerScroller = new ScrollViewer
        {
            Content = _viewerBox,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        _viewerLayer = new Grid { Visibility = Visibility.Collapsed };
        _viewerLayer.Children.Add(_viewerScroller);

        _body = new Grid();
        _body.Children.Add(_dash);
        _body.Children.Add(_viewerLayer);
        _body.SizeChanged += (_, e) => ArrangeDashboard(e.NewSize.Width);
        return _body;
    }

    /// <summary>Builds every section for the game the overlay is open over.</summary>
    private void BuildDashboard()
    {
        SaveNotes();
        _viewerIndex = -1;
        _shotsChanged = false;
        _viewerBox.Children.Clear();
        _viewerLayer.Visibility = Visibility.Collapsed;
        _dash.Visibility = Visibility.Visible;
        _ovAchListSeq++; // stop any achievement rows still being added
        _sessionText = null;
        _notesBox = null;
        foreach (var col in _cols) col.Children.Clear(); // boxes leave their columns before being refilled
        foreach (var box in new[] { _homeBox, _gameBox, _infoBox, _shotBox, _achBox }) box.Children.Clear();

        BuildHomePanel();
        if (_game?.Card is { } card)
        {
            BuildAchievementsPanel(card);
            BuildScreenshotsPanel(card);
            BuildGamePanel(card);
            BuildInfoPanel(card);
        }
        else StopShotWatcher();

        _layoutMode = -1;
        ArrangeDashboard(_body.ActualWidth);
        foreach (var sv in _colScrollers) sv.ChangeView(null, 0, null, disableAnimation: true);
    }

    /// <summary>
    /// Puts the section boxes into columns for the width: three (info | screenshots | achievements), two
    /// (info and screenshots | achievements) or one (everything stacked).
    /// </summary>
    private void ArrangeDashboard(double width)
    {
        if (width <= 0) width = 1600;
        int mode = width >= ThreeColumnsFrom ? 3 : width >= TwoColumnsFrom ? 2 : 1;
        var defs = _dash.ColumnDefinitions;
        if (mode == 3)
        {
            defs[0].Width = new GridLength(Math.Clamp(width * 0.26, 400, 540));
            defs[1].Width = new GridLength(1, GridUnitType.Star);
            defs[2].Width = new GridLength(Math.Clamp(width * 0.3, 440, 680));
        }
        else if (mode == 2)
        {
            defs[0].Width = new GridLength(1, GridUnitType.Star);
            defs[1].Width = new GridLength(0);
            defs[2].Width = new GridLength(Math.Clamp(width * 0.42, 380, 620));
        }
        else
        {
            defs[0].Width = new GridLength(1, GridUnitType.Star);
            defs[1].Width = new GridLength(0);
            defs[2].Width = new GridLength(0);
        }
        _dash.ColumnSpacing = mode == 1 ? 0 : 28;
        if (mode == _layoutMode) return;
        _layoutMode = mode;

        // Empty every column before adding a box elsewhere (an element can have only one parent)
        foreach (var col in _cols) col.Children.Clear();
        bool known = _game?.Card != null;
        void Put(int c, StackPanel box) { if (box.Children.Count > 0) _cols[c].Children.Add(box); }
        if (!known) Put(0, _homeBox);
        else if (mode == 3)
        {
            Put(0, _homeBox); Put(0, _gameBox); Put(0, _infoBox);
            Put(1, _shotBox);
            Put(2, _achBox);
        }
        else if (mode == 2)
        {
            Put(0, _homeBox); Put(0, _shotBox); Put(0, _gameBox); Put(0, _infoBox);
            Put(2, _achBox);
        }
        else
        {
            Put(0, _homeBox); Put(0, _achBox); Put(0, _shotBox); Put(0, _gameBox); Put(0, _infoBox);
        }
        for (int c = 0; c < 3; c++)
            _colScrollers[c].Visibility = _cols[c].Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Controller helpers ────────────────────────────────────────────────────

    private ScrollViewer? ScrollerOf(DependencyObject? element)
    {
        for (var d = element; d != null; d = VisualTreeHelper.GetParent(d))
            if (d is ScrollViewer sv && Array.IndexOf(_colScrollers, sv) >= 0) return sv;
        return null;
    }

    private ScrollViewer? FirstVisibleScroller() => _colScrollers.FirstOrDefault(s => s.Visibility == Visibility.Visible);

    private void FocusColumn(int index)
    {
        var visible = _colScrollers.Where(s => s.Visibility == Visibility.Visible).ToList();
        if (visible.Count == 0) return;
        var sv = visible[Math.Clamp(index, 0, visible.Count - 1)];
        if (Microsoft.UI.Xaml.Input.FocusManager.FindFirstFocusableElement(sv) is Control c) c.Focus(FocusState.Keyboard);
    }

    /// <summary>LB / RB: the first control of the previous / next column.</summary>
    private void StepColumn(DependencyObject? focused, int delta)
    {
        var visible = _colScrollers.Where(s => s.Visibility == Visibility.Visible).ToList();
        if (visible.Count == 0 || ViewerOpen) return;
        int at = ScrollerOf(focused) is { } sv ? visible.IndexOf(sv) : -1;
        FocusColumn(((at < 0 ? (delta > 0 ? -1 : 0) : at) + delta + visible.Count) % visible.Count);
    }

    // ── Home ──────────────────────────────────────────────────────────────────

    private void BuildHomePanel()
    {
        // Equal tiles that fill the column (two side by side when there's room)
        var tiles = new Controls.AutoFitGrid { MinColumnWidth = 140, ColumnSpacing = 12, RowSpacing = 12 };

        _sessionText = BigText("");
        _sessionSince = SmallText("");
        var session = new StackPanel { Spacing = 4 };
        session.Children.Add(_sessionText);
        session.Children.Add(_sessionSince);
        tiles.Children.Add(MakeCard("Session", session, stretch: true));
        UpdateSessionText();

        // Total playtime from Steam's own records (read off the UI thread)
        var card = _game?.Card;
        int? appId = null;
        if (card != null)
        {
            try { appId = DetailPanelBuilder.ResolveCommunityAppId(card, _manifest()); }
            catch (Exception ex) { CrashReporter.Log($"[OverlayWindow.Home] App id lookup failed — {ex.Message}"); }
        }
        if (appId is int id)
        {
            var value = BigText("…");
            var note = SmallText("On Steam, not counting this session");
            var rhiCount = RhiPlaytime(card!);
            var body = new StackPanel { Spacing = 4 };
            body.Children.Add(value);
            body.Children.Add(note);
            var tile = MakeCard("Total playtime", body, stretch: true);
            tiles.Children.Add(tile);
            int openCount = _openCount;
            _ = Task.Run(() => SteamPlaytimeService.GetMinutes(id)).ContinueWith(t =>
            {
                int? minutes = t.IsCompletedSuccessfully ? t.Result : null;
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (openCount != _openCount) return;
                    if (minutes is int m && m > 0) value.Text = FormatDuration(TimeSpan.FromMinutes(m));
                    else if (rhiCount is var (time, since))
                    {
                        value.Text = FormatDuration(time);
                        note.Text = since;
                    }
                    else tile.Visibility = Visibility.Collapsed;
                });
            }, TaskScheduler.Default);
        }

        else if (card != null && RhiPlaytime(card) is var (time, since))
        {
            var body = new StackPanel { Spacing = 4 };
            body.Children.Add(BigText(FormatDuration(time)));
            body.Children.Add(SmallText(since));
            tiles.Children.Add(MakeCard("Total playtime", body, stretch: true));
        }

        if (MakeHdrTile() is { } hdrTile) tiles.Children.Add(hdrTile);

        if (OverlayNative.Battery() is var (percent, charging))
            tiles.Children.Add(MakeCard("Battery", BigText($"{percent}%{(charging ? ", charging" : "")}"), stretch: true));

        _homeBox.Children.Add(tiles);
    }

    /// <summary>RHI's own playtime count for the game and a "since" line, or null when there's none yet.</summary>
    private static (TimeSpan Time, string Since)? RhiPlaytime(GameCardViewModel card)
    {
        var time = GameJournalStore.GetPlaytime(GameKey.FromCard(card.GameName, card.Source).ToKey());
        if (time < TimeSpan.FromMinutes(1)) return null;
        var since = GameJournalStore.CountingSince();
        return (time, since is DateTime d
            ? $"Counted by RHI while the game is in front, since {d.ToLocalTime():d MMM yyyy}"
            : "Counted by RHI while the game is in front");
    }

    private void UpdateSessionText()
    {
        if (_sessionText == null) return;
        // Big: the time played; small: since when (so the big line fits a narrow tile)
        _sessionText.Text = _gameStart is DateTime start ? FormatDuration(DateTime.Now - start) : "Playing now";
        if (_sessionSince != null)
            _sessionSince.Text = _gameStart is DateTime s ? $"Playing since {s:t}" : "";
    }

    private TextBlock? _sessionSince;

    // ── This game ─────────────────────────────────────────────────────────────

    private void BuildGamePanel(GameCardViewModel card)
    {
        var rows = new List<(string Name, string Value, bool Update)>();
        void Add(string name, GameStatus status, string text)
        {
            if (status is not (GameStatus.Installed or GameStatus.UpdateAvailable)) return;
            var value = text is "Installed" or "Update" or "Ready" ? "" : text;
            rows.Add((name, value, status == GameStatus.UpdateAvailable));
        }
        Add("ReShade", card.RsStatus, card.RsStatusText);
        Add("RenoDX", card.Status, card.RdxStatusText);
        if (card.IsLumaMode) Add("Luma", card.LumaStatus, card.LumaStatusText);
        Add("ReLimiter", card.UlStatus, card.UlStatusText);
        Add("Display Commander", card.DcStatus, card.DcStatusText);
        if (card.IsREEngineGame) Add("RE Framework", card.RefStatus, card.RefStatusText);
        Add("OptiScaler", card.OsStatus, card.OsStatusText);
        Add("DOF Fix", card.DofFixStatus, card.DofFixStatusText);
        Add("DXVK", card.DxvkStatus, card.DxvkStatusText);

        var installed = rows.Count > 0
            ? MakeTable(rows)
            : (UIElement)SmallText("RHI hasn't installed anything in this game yet.");
        var gameBody = new StackPanel { Spacing = 10 };
        gameBody.Children.Add(installed);
        if (MakeDlssSection(card) is { } dlss)
        {
            gameBody.Children.Add(SubHeading("DLSS and Streamline"));
            gameBody.Children.Add(dlss);
        }
        _gameBox.Children.Add(MakeCard("This game", gameBody, stretch: true));
    }

    // ── Info and links ────────────────────────────────────────────────────────

    private void BuildInfoPanel(GameCardViewModel card)
    {
        // Your own notes: saved as you type (and when the page or overlay closes)
        _notesKey = GameKey.FromCard(card.GameName, card.Source).ToKey();
        _notesBox = new TextBox
        {
            Text = GameJournalStore.GetNotes(_notesKey),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 140,
            FontSize = 16,
            PlaceholderText = "Codes, routes, settings to remember… Only you see these.",
        };
        TabsTheme.StyleInput(_notesBox);
        _notesBox.MinHeight = 76; // StyleInput sets a one-line height and small text
        _notesBox.FontSize = 15;
        ScrollViewer.SetVerticalScrollBarVisibility(_notesBox, ScrollBarVisibility.Auto);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_notesBox, "Your notes for this game");
        _notesBox.TextChanged += (_, _) => QueueNotesSave();
        _notesBox.LostFocus += (_, _) => SaveNotes();
        _infoBox.Children.Add(MakeCard("Your notes", _notesBox, stretch: true));

        // Mod notes and links share one card (kept short so the column fits without scrolling)
        var info = new StackPanel { Spacing = 10 };
        bool both = !string.IsNullOrWhiteSpace(card.Notes) && card.IsLumaMode && !string.IsNullOrWhiteSpace(card.LumaNotes);
        if (!string.IsNullOrWhiteSpace(card.Notes))
        {
            if (both) info.Children.Add(SubHeading("RenoDX"));
            info.Children.Add(NotesText(card.Notes!));
        }
        if (card.IsLumaMode && !string.IsNullOrWhiteSpace(card.LumaNotes))
        {
            if (both) info.Children.Add(SubHeading("Luma"));
            info.Children.Add(NotesText(card.LumaNotes!));
        }

        var links = new List<(string Label, string Url)>();
        void Link(string label, string? url)
        {
            if (!string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url, UriKind.Absolute, out var u)
                && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp))
                links.Add((label, url!));
        }
        Link("Nexus Mods", card.NexusModsUrl);
        Link("PCGamingWiki", card.PcgwUrl);
        Link("Ultrawide fix", card.UwFixUrl);
        Link("Ultra+", card.UltraPlusUrl);
        Link(card.IsLumaMode ? "RenoDX notes" : "Mod notes", card.NotesUrl);
        if (card.IsLumaMode) Link("Luma notes", card.LumaNotesUrl);
        Link("Discord", card.DiscordUrl);
        if (links.Count > 0)
        {
            var wrap = new Controls.WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 8 };
            foreach (var (label, url) in links)
            {
                var btn = new Button { Content = label };
                TabsTheme.Apply(btn, TabsTheme.BlueButton, height: 36, fontSize: 14);
                ToolTipService.SetToolTip(btn, "Opens in your browser, outside the game");
                btn.Click += async (_, _) =>
                {
                    Close(returnToGame: false); // the browser comes to the front
                    try { await Windows.System.Launcher.LaunchUriAsync(new Uri(url)); }
                    catch (Exception ex) { CrashReporter.Log($"[OverlayWindow.Link] Could not open {url} — {ex.Message}"); }
                };
                wrap.Children.Add(btn);
            }
            info.Children.Add(wrap);
        }
        if (info.Children.Count > 0)
            _infoBox.Children.Add(MakeCard(links.Count > 0 && info.Children.Count > 1 ? "Mod notes and links" : links.Count > 0 ? "Links" : "Mod notes", info, stretch: true));
    }

    private TextBox? _notesBox;
    private string? _notesKey;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _notesTimer;

    private void QueueNotesSave()
    {
        if (_notesTimer == null)
        {
            _notesTimer = DispatcherQueue.CreateTimer();
            _notesTimer.Interval = TimeSpan.FromMilliseconds(800);
            _notesTimer.IsRepeating = false;
            _notesTimer.Tick += (_, _) => SaveNotes();
        }
        _notesTimer.Stop();
        _notesTimer.Start();
    }

    private void SaveNotes()
    {
        _notesTimer?.Stop();
        if (_notesBox == null || _notesKey == null) return;
        try { GameJournalStore.SetNotes(_notesKey, _notesBox.Text ?? ""); }
        catch (Exception ex) { CrashReporter.Log($"[OverlayWindow.SaveNotes] {ex.Message}"); }
    }

    // ── Building blocks ───────────────────────────────────────────────────────

    private static Border MakeCard(string title, UIElement body, bool stretch = false)
    {
        var inner = new StackPanel { Spacing = 10 };
        inner.Children.Add(new TextBlock { Text = title, FontSize = 14, Foreground = TabsTheme.B(TabsTheme.TextMuted) });
        inner.Children.Add(body);
        return new Border
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xE6, 0x12, 0x18, 0x24)),
            BorderBrush = TabsTheme.B(TabsTheme.CardBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(22, 16, 22, 18),
            MinWidth = stretch ? 0 : 190, // stretched cards take the space they are given (three tiles share a row)
            HorizontalAlignment = stretch ? HorizontalAlignment.Stretch : HorizontalAlignment.Left,
            Child = inner,
        };
    }

    /// <summary>Name | value | "Update available" rows.</summary>
    private static Grid MakeTable(List<(string Name, string Value, bool Update)> rows)
    {
        var grid = new Grid { ColumnSpacing = 32, RowSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < rows.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var (name, value, update) = rows[i];
            var n = new TextBlock { Text = name, FontSize = 17, Foreground = TabsTheme.B(TabsTheme.TextSecondary) };
            var v = new TextBlock { Text = value, FontSize = 17, Foreground = TabsTheme.B(TabsTheme.TextPrimary) };
            Grid.SetRow(n, i);
            Grid.SetRow(v, i);
            Grid.SetColumn(v, 1);
            grid.Children.Add(n);
            grid.Children.Add(v);
            if (update)
            {
                var pill = new Border
                {
                    Background = TabsTheme.B("#2A2050"),
                    BorderBrush = TabsTheme.B("#4A3A7A"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(10, 2, 10, 3),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = "Update available", FontSize = 13, Foreground = TabsTheme.B("#D9C8FF") },
                };
                Grid.SetRow(pill, i);
                Grid.SetColumn(pill, 2);
                grid.Children.Add(pill);
            }
        }
        return grid;
    }

    private static TextBlock BigText(string text) => new()
    {
        Text = text,
        FontSize = 22,
        Foreground = TabsTheme.B(TabsTheme.TextPrimary),
        TextWrapping = TextWrapping.Wrap,
    };

    private static TextBlock SmallText(string text) => new()
    {
        Text = text,
        FontSize = 14,
        Foreground = TabsTheme.B(TabsTheme.TextSecondary),
        TextWrapping = TextWrapping.Wrap,
    };

    private static TextBlock SubHeading(string text) => new()
    {
        Text = text,
        FontSize = 13,
        Foreground = TabsTheme.B(TabsTheme.TextMuted),
    };

    private static TextBlock NotesText(string text) => new()
    {
        MaxLines = 4,
        TextTrimming = TextTrimming.CharacterEllipsis,
        Text = text.Trim(),
        FontSize = 16,
        LineHeight = 24,
        Foreground = TabsTheme.B(TabsTheme.TextControl),
        TextWrapping = TextWrapping.Wrap,
        IsTextSelectionEnabled = true,
    };
}
