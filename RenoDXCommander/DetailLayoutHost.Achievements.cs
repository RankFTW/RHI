// DetailLayoutHost.Achievements.cs -- Steam achievements in the Spotlight layout (opt-in:
// Settings › Achievements). A compact strip under the Launch / HDR / Resolution buttons in the hero
// (progress, latest unlock, a row of recent unlock icons) opens the full list in the slide-in panel:
// unlocked newest first, then locked in the game's order, with unlock dates and rarity.
// Data: Services/SteamAchievementService.cs (Steam's local cache). The strip hides itself when the
// setting is off, outside Spotlight, or when the game has no achievement data on this PC.
// A FileSystemWatcher on Steam's stats folder refreshes it when Steam writes new unlocks.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using RenoDXCommander.Services;

namespace RenoDXCommander;

internal sealed partial class DetailLayoutHost
{
    internal const string AchievementsKey = "Achievements";

    private const double AchIconSmall = 26;
    private const double AchIconLatest = 36;
    private const double AchIconRow = 56;
    private const string AchBarFill = "#3D8BFF";
    private const string AchBarTrack = "#263042";

    private Border? _achSlot;            // in the header's right column, under the toggles
    private Button? _achStrip;
    private int? _achAppId;              // the game the strip shows (or is loading)
    private SteamAchievementSet? _achSet;
    private int _achLoadSeq;
    private FileSystemWatcher? _achWatcher;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _achReloadTimer;

    // Panel
    private TextBox? _achSearch;
    private Border? _achSummary;
    private StackPanel? _achList;
    private int _achListSeq;

    private bool AchievementsOn => IsSpotlight && _window.ViewModel.Settings.SteamAchievements;

    /// <summary>The empty slot the strip lives in; added to the header's right column by BuildHeader.</summary>
    private Border MakeAchievementsSlot(FrameworkElement sizeFrom)
    {
        _achSlot = new Border { Visibility = Visibility.Collapsed, Width = 260 };
        // The header column is as wide as its widest child: follow the toggles' width so long names
        // in the strip never widen it (they trim instead)
        sizeFrom.SizeChanged += (s, e) =>
        {
            if (e.NewSize.Width < 1 || Math.Abs(_achSlot.Width - e.NewSize.Width) < 0.5) return;
            _achSlot.Width = e.NewSize.Width;
            if (_achSet != null && _achSlot.Visibility == Visibility.Visible) RenderStrip();
        };
        return _achSlot;
    }

    // ── Loading ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Shows the selected game's achievements in the hero (or hides the strip). Cheap when the game
    /// hasn't changed; <paramref name="force"/> re-reads Steam's files.
    /// </summary>
    internal void RefreshAchievements(bool force = false)
    {
        if (_achSlot == null) return;
        if (!AchievementsOn)
        {
            HideAchievements();
            StopAchievementWatcher();
            return;
        }
        EnsureAchievementWatcher();

        var card = _window.ViewModel.SelectedGame;
        int? appId = card == null ? null : DetailPanelBuilder.ResolveCommunityAppId(card, _window.ViewModel.Manifest);
        if (!force && appId == _achAppId) return;
        if (appId != _achAppId)
        {
            _achSet = null;
            _achSlot.Visibility = Visibility.Collapsed;
            if (IsInPanel(AchievementsKey)) ClosePanel();
        }
        _achAppId = appId;
        if (appId is not int id) return;

        int seq = ++_achLoadSeq;
        var token = _window.LifetimeToken;
        _ = Task.Run(async () =>
        {
            try
            {
                var set = SteamAchievementService.Load(id);
                if (token.IsCancellationRequested) return;
                _window.DispatcherQueue.TryEnqueue(() => ApplyAchievements(seq, set));
                if (set == null || set.HasRarity) return;
                var rarity = await SteamAchievementService.GetRarityAsync(id, token).ConfigureAwait(false);
                if (rarity == null || token.IsCancellationRequested) return;
                var withRarity = SteamAchievementService.WithRarity(set, rarity);
                _window.DispatcherQueue.TryEnqueue(() => ApplyAchievements(seq, withRarity));
            }
            catch (Exception ex) { CrashReporter.Log($"[DetailLayoutHost.RefreshAchievements] {id}: {ex.Message}"); }
        });
    }

    private void ApplyAchievements(int seq, SteamAchievementSet? set)
    {
        if (seq != _achLoadSeq || !AchievementsOn || _achSlot == null) return;
        _achSet = set;
        if (set == null) { HideAchievements(); return; }
        RenderStrip();
        _achSlot.Visibility = Visibility.Visible;
        if (IsInPanel(AchievementsKey)) RenderPanel(keepScroll: true);
    }

    private void HideAchievements()
    {
        _achSet = null;
        _achAppId = null;
        if (_achSlot != null) _achSlot.Visibility = Visibility.Collapsed;
        if (IsInPanel(AchievementsKey)) ClosePanel();
    }

    /// <summary>Settings › Achievements changed.</summary>
    internal void AchievementsSettingChanged() => RefreshAchievements(force: true);

    // Steam rewrites UserGameStats_<account>_<appid>.bin when achievements unlock (in game or on exit)
    private void EnsureAchievementWatcher()
    {
        if (_achWatcher != null) return;
        var dir = SteamAchievementService.StatsFolder();
        if (dir == null) return;
        try
        {
            var watcher = new FileSystemWatcher(dir, "UserGameStats*.bin")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                IncludeSubdirectories = false,
            };
            watcher.Changed += OnAchievementFileChanged;
            watcher.Created += OnAchievementFileChanged;
            watcher.Renamed += OnAchievementFileChanged;
            watcher.EnableRaisingEvents = true;
            _achWatcher = watcher;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[DetailLayoutHost.EnsureAchievementWatcher] {ex.Message}");
        }
    }

    private void StopAchievementWatcher()
    {
        if (_achWatcher == null) return;
        _achWatcher.EnableRaisingEvents = false;
        _achWatcher.Dispose();
        _achWatcher = null;
    }

    private void OnAchievementFileChanged(object sender, FileSystemEventArgs e)
    {
        // Watcher thread: only the shown game matters (…_<appid>.bin, the stats or the schema file)
        var shown = _achAppId;
        if (shown is not int id || e.Name is not { } name
            || !name.EndsWith($"_{id}.bin", StringComparison.OrdinalIgnoreCase)) return;
        if (_window.IsShuttingDown) return;
        _window.DispatcherQueue.TryEnqueue(() =>
        {
            // Steam writes the file in several steps: wait for it to settle
            _achReloadTimer ??= CreateAchReloadTimer();
            _achReloadTimer.Stop();
            _achReloadTimer.Start();
        });
    }

    private Microsoft.UI.Dispatching.DispatcherQueueTimer CreateAchReloadTimer()
    {
        var t = _window.DispatcherQueue.CreateTimer();
        t.Interval = TimeSpan.FromMilliseconds(1500);
        t.IsRepeating = false;
        t.Tick += (s, e) => RefreshAchievements(force: true);
        return t;
    }

    // ── Hero strip ────────────────────────────────────────────────────────────

    private void RenderStrip()
    {
        if (_achSlot == null || _achSet is not { } set) return;
        double width = double.IsNaN(_achSlot.Width) ? 260 : _achSlot.Width;
        const double pad = 12;

        var body = new StackPanel { Spacing = 8 };

        // "You've unlocked 13/66 (19%)" + bar
        var count = new TextBlock { FontSize = 12, Foreground = TabsTheme.B(TabsTheme.TextControl), TextTrimming = TextTrimming.CharacterEllipsis };
        count.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = $"You've unlocked {set.Unlocked}/{set.Total} " });
        count.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = $"({set.PercentDone}%)", Foreground = TabsTheme.B(TabsTheme.TextMuted) });
        body.Children.Add(count);
        body.Children.Add(MakeProgressBar(set, 6));

        var unlocked = set.Items.Where(a => a.Unlocked).ToList();
        if (unlocked.Count == 0)
        {
            body.Children.Add(new TextBlock
            {
                Text = "No achievements unlocked yet",
                FontSize = 12,
                Foreground = TabsTheme.B(TabsTheme.TextMuted),
            });
        }
        else
        {
            // Latest unlock: icon, name, description
            var latest = unlocked[0];
            var latestRow = new Grid { ColumnSpacing = 10 };
            latestRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            latestRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            latestRow.Children.Add(MakeAchIcon(set.AppId, latest.Icon, AchIconLatest));
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock
            {
                Text = latest.Name,
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = TabsTheme.B(TabsTheme.TextPrimary),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            if (latest.Description.Length > 0)
                text.Children.Add(new TextBlock
                {
                    Text = latest.Description,
                    FontSize = 11,
                    Foreground = TabsTheme.B(TabsTheme.TextSecondary),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
            Grid.SetColumn(text, 1);
            latestRow.Children.Add(text);
            body.Children.Add(latestRow);

            // The next most recent unlocks, as many as fit, then "+N"
            var rest = unlocked.Skip(1).ToList();
            if (rest.Count > 0)
            {
                const double gap = 4;
                int fit = Math.Max(1, (int)((width - pad * 2 + gap) / (AchIconSmall + gap)));
                int show = rest.Count <= fit ? rest.Count : fit - 1;
                var icons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = gap };
                foreach (var a in rest.Take(show))
                {
                    var icon = MakeAchIcon(set.AppId, a.Icon, AchIconSmall);
                    ToolTipService.SetToolTip(icon, a.Name);
                    icons.Children.Add(icon);
                }
                if (rest.Count > show)
                    icons.Children.Add(new Border
                    {
                        Width = AchIconSmall,
                        Height = AchIconSmall,
                        CornerRadius = new CornerRadius(4),
                        Background = TabsTheme.B("#1E2738"),
                        Child = new TextBlock
                        {
                            Text = $"+{rest.Count - show}",
                            FontSize = 10,
                            Foreground = TabsTheme.B(TabsTheme.TextControl),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                        },
                    });
                body.Children.Add(icons);
            }
        }

        var card = new Border
        {
            Background = TabsTheme.B(TabsTheme.Card),
            BorderBrush = TabsTheme.B(TabsTheme.CardBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(pad, 10, pad, 10),
            Child = body,
        };
        TabsTheme.UseHandCursor(card);
        TabsTheme.SetHandTip(card, "See all achievements");
        // A new button each time keeps the tile's hover / focus colours right; keep keyboard /
        // controller focus on it if the old one had it
        bool hadFocus = _achStrip?.FocusState is FocusState.Keyboard;
        _achStrip = TabsTheme.WrapTile(card, () => OpenPanel(AchievementsKey));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_achStrip,
            $"Achievements: {set.Unlocked} of {set.Total} unlocked");
        if (hadFocus)
        {
            var strip = _achStrip;
            void OnLoaded(object s, RoutedEventArgs e) { strip.Loaded -= OnLoaded; strip.Focus(FocusState.Keyboard); }
            strip.Loaded += OnLoaded;
        }
        _achSlot.Child = _achStrip;
    }

    private static Grid MakeProgressBar(SteamAchievementSet set, double height)
    {
        var bar = new Grid { Height = height, CornerRadius = new CornerRadius(height / 2), Background = TabsTheme.B(AchBarTrack) };
        double done = set.Total == 0 ? 0 : (double)set.Unlocked / set.Total;
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(done, 0.0001), GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - done, 0.0001), GridUnitType.Star) });
        if (done > 0)
            bar.Children.Add(new Border { Background = TabsTheme.B(AchBarFill), CornerRadius = new CornerRadius(height / 2) });
        return bar;
    }

    /// <summary>An achievement icon: the cached file, or downloaded in the background and shown when it arrives.</summary>
    private Border MakeAchIcon(int appId, string? file, double size, bool dim = false)
    {
        var image = new Image { Width = size, Height = size, Stretch = Stretch.UniformToFill };
        var frame = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size >= 40 ? 6 : 4),
            Background = TabsTheme.B("#1A2130"),
            BorderBrush = TabsTheme.B("#2A3448"),
            BorderThickness = new Thickness(1),
            Opacity = dim ? 0.6 : 1,
            Child = image,
        };
        if (file == null) return frame;
        int decode = (int)Math.Ceiling(size * Math.Max(1, _window.Content?.XamlRoot?.RasterizationScale ?? 1));
        void Show(string path)
        {
            try { image.Source = new BitmapImage(new Uri(path)) { DecodePixelWidth = decode }; }
            catch (UriFormatException ex) { CrashReporter.Log($"[DetailLayoutHost.MakeAchIcon] {ex.Message}"); }
        }
        if (SteamAchievementService.TryGetCachedIcon(appId, file) is { } cached) { Show(cached); return frame; }
        var token = _window.LifetimeToken;
        _ = Task.Run(async () =>
        {
            var path = await SteamAchievementService.GetIconAsync(appId, file, token).ConfigureAwait(false);
            if (path == null || token.IsCancellationRequested) return;
            _window.DispatcherQueue.TryEnqueue(() => Show(path));
        });
        return frame;
    }

    // ── Side panel ────────────────────────────────────────────────────────────

    /// <summary>The panel's content for the achievements list (OpenPanel).</summary>
    private FrameworkElement? BuildAchievementsPanel()
    {
        if (_achSet == null) return null;
        var root = new StackPanel { Spacing = 14 };
        _achSearch = new TextBox { PlaceholderText = "Search achievements" };
        TabsTheme.StyleInput(_achSearch);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_achSearch, "Search achievements");
        _achSearch.TextChanged += (s, e) => RenderPanel(keepScroll: false);
        _achList = new StackPanel { Spacing = 6 };
        _achSummary = new Border(); // filled by RenderPanel
        root.Children.Add(_achSummary);
        root.Children.Add(_achSearch);
        root.Children.Add(_achList);
        RenderPanel(keepScroll: false);
        return root;
    }

    private void RenderPanel(bool keepScroll)
    {
        if (_achSet is not { } set || _achList == null) return;
        if (_achSummary != null) _achSummary.Child = MakeSummaryCard(set);

        double? offset = keepScroll ? _panelScroll?.VerticalOffset : null;
        var query = _achSearch?.Text?.Trim() ?? "";
        bool Matches(SteamAchievement a)
        {
            if (query.Length == 0) return true;
            bool masked = a.Hidden && !a.Unlocked;
            return (masked ? "Hidden achievement" : a.Name).Contains(query, StringComparison.OrdinalIgnoreCase)
                || (!masked && a.Description.Contains(query, StringComparison.OrdinalIgnoreCase));
        }
        var unlocked = set.Items.Where(a => a.Unlocked && Matches(a)).ToList();
        var locked = set.Items.Where(a => !a.Unlocked && Matches(a)).ToList();

        // Built in chunks so a game with hundreds of achievements doesn't stall the UI
        var rows = new List<Func<FrameworkElement>>();
        if (unlocked.Count > 0)
        {
            rows.Add(() => ListHeading($"Unlocked ({unlocked.Count})"));
            rows.AddRange(unlocked.Select(a => (Func<FrameworkElement>)(() => MakeAchRow(set.AppId, a))));
        }
        if (locked.Count > 0)
        {
            rows.Add(() => ListHeading($"Locked ({locked.Count})"));
            rows.AddRange(locked.Select(a => (Func<FrameworkElement>)(() => MakeAchRow(set.AppId, a))));
        }
        if (rows.Count == 0)
            rows.Add(() => new TextBlock { Text = "No achievements match your search.", FontSize = 13, Foreground = TabsTheme.B(TabsTheme.TextMuted) });

        int seq = ++_achListSeq;
        _achList.Children.Clear();
        int next = 0;
        void AddChunk()
        {
            if (seq != _achListSeq || _achList == null) return;
            int end = Math.Min(rows.Count, next + (next == 0 ? 30 : 40));
            for (; next < end; next++) _achList.Children.Add(rows[next]());
            if (next < rows.Count)
                _window.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, AddChunk);
            else if (offset is double y)
                _panelScroll?.ChangeView(null, y, null, disableAnimation: true);
        }
        AddChunk();
    }

    private static Border MakeSummaryCard(SteamAchievementSet set)
    {
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(new TextBlock
        {
            Text = $"{set.Unlocked} of {set.Total} achievements earned",
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = TabsTheme.B(TabsTheme.TextPrimary),
        });
        var pct = new TextBlock
        {
            Text = $"({set.PercentDone}%)",
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = TabsTheme.B(TabsTheme.TextSecondary),
        };
        Grid.SetColumn(pct, 1);
        top.Children.Add(pct);
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(top);
        body.Children.Add(MakeProgressBar(set, 8));
        if (!set.HasRarity)
            body.Children.Add(new TextBlock
            {
                Text = "Rarity isn't available right now (Steam's global stats couldn't be reached).",
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Foreground = TabsTheme.B(TabsTheme.TextMuted),
            });
        return TabsTheme.MakeCard(body, 16);
    }

    private static TextBlock ListHeading(string text) => new()
    {
        Text = text,
        FontSize = 12,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Foreground = TabsTheme.B(TabsTheme.TextMuted),
        Margin = new Thickness(2, 8, 0, 2),
    };

    private Border MakeAchRow(int appId, SteamAchievement a)
    {
        bool masked = a.Hidden && !a.Unlocked;
        var icon = MakeAchIcon(appId, a.Unlocked ? a.Icon : a.IconGray ?? a.Icon, AchIconRow, dim: !a.Unlocked);

        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = masked ? "Hidden achievement" : a.Name,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = TabsTheme.B(a.Unlocked ? TabsTheme.TextPrimary : TabsTheme.TextSecondary),
        });
        var desc = masked ? "Details are shown once you unlock it." : a.Description;
        if (desc.Length > 0)
            text.Children.Add(new TextBlock
            {
                Text = desc,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = TabsTheme.B(a.Unlocked ? TabsTheme.TextSecondary : TabsTheme.TextMuted),
            });
        if (a.Percent is double p)
            text.Children.Add(new TextBlock
            {
                Text = $"{p.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture)}% of players have this achievement",
                FontSize = 11,
                Foreground = TabsTheme.B(TabsTheme.TextMuted),
            });

        var grid = new Grid { ColumnSpacing = 14 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(icon);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        if (a.Unlocked)
        {
            var when = new TextBlock
            {
                Text = a.UnlockedUtc is DateTime t ? "Unlocked " + FormatUnlock(t) : "Unlocked",
                FontSize = 12,
                Foreground = TabsTheme.B(TabsTheme.TextSecondary),
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Right,
            };
            Grid.SetColumn(when, 2);
            grid.Children.Add(when);
        }

        return new Border
        {
            Background = TabsTheme.B(a.Unlocked ? TabsTheme.Card : "#0F141E"),
            BorderBrush = TabsTheme.B(TabsTheme.CardBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 16, 10),
            Child = grid,
        };
    }

    private static string FormatUnlock(DateTime utc)
    {
        var local = utc.ToLocalTime();
        var c = System.Globalization.CultureInfo.CurrentCulture;
        return local.ToString("d MMM yyyy", c) + ", " + local.ToString("t", c);
    }

    private void ReturnAchievementsPanel()
    {
        _achListSeq++; // stop any chunks still being added
        _achList = null;
        _achSearch = null;
        _achSummary = null;
    }
}
