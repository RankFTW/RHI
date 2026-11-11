// OverlayWindow.Achievements.cs -- the RHI overlay's Achievements page: the same Steam / Xbox data as the
// Spotlight achievements (SteamAchievementService, XboxAchievementService, same Settings › Achievements
// switches), shown larger, with locked achievements sorted easiest first when rarity is known.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Overlay;

internal sealed partial class OverlayWindow
{
    private const double OvAchIcon = 64;
    private AchievementSet? _ovAchSet;
    private string? _ovAchGameKey;   // which game _ovAchSet belongs to
    private int _ovAchListSeq;
    private TextBox? _ovAchSearch;
    private StackPanel? _ovAchList;
    private Border? _ovAchSummary;

    /// <summary>Which store to read for the game, or null when no enabled store applies (see DetailLayoutHost.AchievementRequest).</summary>
    private Func<AchievementSet?>? AchievementLoader(GameCardViewModel card)
    {
        var settings = _settings();
        int? appId = settings.SteamAchievements ? DetailPanelBuilder.ResolveCommunityAppId(card, _manifest()) : null;
        Func<AchievementSet?> steam = appId is int id ? () => SteamAchievementService.Load(id) : () => null;
        if (settings.XboxAchievements && string.Equals(card.Source, "Xbox", StringComparison.OrdinalIgnoreCase))
        {
            var pfn = XboxAchievementService.PfnFromAumid(card.DetectedGame?.XboxAumid);
            var name = card.GameName;
            return () => XboxAchievementService.Load(pfn, name) ?? steam();
        }
        return appId != null ? steam : null;
    }

    private void BuildAchievementsPanel(GameCardViewModel card)
    {
        var settings = _settings();
        if (!settings.SteamAchievements && !settings.XboxAchievements)
        {
            _achBox.Children.Add(MakeCard("Achievements", SmallText(
                "Achievements are off. Turn them on in RHI's Settings › Achievements (Steam and Xbox).")));
            return;
        }
        var load = AchievementLoader(card);
        if (load == null)
        {
            _achBox.Children.Add(MakeCard("Achievements", SmallText("RHI can't find achievements for this game.")));
            return;
        }

        _ovAchSummary = new Border();
        _ovAchSearch = new TextBox { PlaceholderText = "Search achievements", FontSize = 15 };
        TabsTheme.StyleInput(_ovAchSearch);
        _ovAchSearch.FontSize = 15;
        _ovAchSearch.MinHeight = 40;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_ovAchSearch, "Search achievements");
        _ovAchSearch.TextChanged += (_, _) => RenderAchievementList();
        _ovAchList = new StackPanel { Spacing = 8 };
        _achBox.Children.Add(_ovAchSummary);
        var searchRow = new Grid { ColumnSpacing = 10 };
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchRow.Children.Add(_ovAchSearch);
        var toggle = MakeHiddenAchToggle(searchRow);
        Grid.SetColumn(toggle, 1);
        searchRow.Children.Add(toggle);
        _achBox.Children.Add(searchRow);
        _achBox.Children.Add(_ovAchList);

        var key = card.GameName + "|" + card.Source;
        if (_ovAchSet != null && _ovAchGameKey == key) RenderAchievements();
        else
        {
            _ovAchSet = null;
            _ovAchSummary.Child = SmallText("Loading achievements…");
        }

        // Always re-read (cheap when unchanged): new unlocks show up each time the page opens
        int openCount = _openCount;
        _ = Task.Run(async () =>
        {
            try
            {
                var set = load();
                DispatcherQueue.TryEnqueue(() => ApplyOverlayAchievements(openCount, key, set));
                if (set == null || set.HasRarity || set.Store != AchievementStore.Steam || !int.TryParse(set.GameId, out var appId)) return;
                var rarity = await SteamAchievementService.GetRarityAsync(appId, CancellationToken.None).ConfigureAwait(false);
                if (rarity == null) return;
                var withRarity = SteamAchievementService.WithRarity(set, rarity);
                DispatcherQueue.TryEnqueue(() => ApplyOverlayAchievements(openCount, key, withRarity));
            }
            catch (Exception ex) { CrashReporter.Log($"[OverlayWindow.Achievements] {key}: {ex.Message}"); }
        });
    }

    /// <summary>"Show hidden" toggle (same setting as Spotlight's): names and descriptions of locked hidden achievements.</summary>
    private Button MakeHiddenAchToggle(Grid row)
    {
        bool on = _settings().ShowHiddenAchievements;
        var b = new Button { Content = "Show hidden", Padding = new Thickness(16, 0, 16, 0), VerticalAlignment = VerticalAlignment.Stretch };
        TabsTheme.Apply(b, DetailLayoutHost.ToggleLook(on), height: double.NaN, fontSize: 15);
        ToolTipService.SetToolTip(b, on
            ? "Hidden achievements show their name and description. Click to hide them again."
            : "Show the name and description of hidden achievements (may spoil the story).");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, on ? "Show hidden achievements: on" : "Show hidden achievements: off");
        b.Click += (_, _) =>
        {
            _settings().ShowHiddenAchievements = !on;
            _saveSettings();
            int i = row.Children.IndexOf(b);
            var next = MakeHiddenAchToggle(row);
            Grid.SetColumn(next, 1);
            if (i >= 0) row.Children[i] = next;
            next.Focus(FocusState.Programmatic);
            RenderAchievementList();
        };
        return b;
    }

    private void ApplyOverlayAchievements(int openCount, string key, AchievementSet? set)
    {
        if (openCount != _openCount || _ovAchSummary == null) return;
        if (set != null && _ovAchSet != null && _ovAchGameKey == key && set.Items.SequenceEqual(_ovAchSet.Items)) return;
        _ovAchSet = set;
        _ovAchGameKey = key;
        if (set == null)
        {
            _ovAchSummary.Child = SmallText("No achievements found for this game. Steam lists them once you've run the game on this PC; "
                + "for Xbox games, open the game's page in the Xbox app once.");
            _ovAchList?.Children.Clear();
            return;
        }
        RenderAchievements();
    }

    private void RenderAchievements()
    {
        if (_ovAchSet is not { } set || _ovAchSummary == null) return;
        var body = new StackPanel { Spacing = 12 };
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(BigText($"{set.Unlocked} of {set.Total} earned"));
        var pct = BigText($"{set.PercentDone}%");
        Grid.SetColumn(pct, 1);
        top.Children.Add(pct);
        body.Children.Add(top);
        body.Children.Add(ProgressBar(set));
        if (set.HasGamerscore)
            body.Children.Add(SmallText($"{set.GamerscoreEarned:N0} of {set.GamerscoreTotal:N0} Gamerscore"));
        _ovAchSummary.Child = MakeCard("Achievements", body, stretch: true);
        RenderAchievementList();
    }

    private void RenderAchievementList()
    {
        if (_ovAchSet is not { } set || _ovAchList == null) return;
        var query = _ovAchSearch?.Text?.Trim() ?? "";
        bool showHidden = _settings().ShowHiddenAchievements;
        bool Matches(Achievement a)
        {
            if (query.Length == 0) return true;
            bool masked = a.IsMasked(showHidden);
            return (masked ? "Hidden achievement" : a.Name).Contains(query, StringComparison.OrdinalIgnoreCase)
                || (!masked && a.Description.Contains(query, StringComparison.OrdinalIgnoreCase));
        }
        var unlocked = set.Items.Where(a => a.Unlocked && Matches(a)).ToList();
        // Locked: most-earned first (the easiest next ones) when rarity is known, else the game's order
        var locked = set.Items.Where(a => !a.Unlocked && Matches(a)).ToList();
        if (set.HasRarity) locked = locked.OrderByDescending(a => a.Percent ?? -1).ToList();

        var rows = new List<Func<FrameworkElement>>();
        if (locked.Count > 0)
        {
            rows.Add(() => Heading(set.HasRarity ? $"Locked ({locked.Count}), most earned first" : $"Locked ({locked.Count})"));
            rows.AddRange(locked.Select(a => (Func<FrameworkElement>)(() => AchievementRow(set, a))));
        }
        if (unlocked.Count > 0)
        {
            rows.Add(() => Heading($"Unlocked ({unlocked.Count})"));
            rows.AddRange(unlocked.Select(a => (Func<FrameworkElement>)(() => AchievementRow(set, a))));
        }
        if (rows.Count == 0) rows.Add(() => SmallText("No achievements match your search."));

        // Built in chunks so hundreds of achievements don't stall the overlay
        int seq = ++_ovAchListSeq;
        _ovAchList.Children.Clear();
        int next = 0;
        void AddChunk()
        {
            if (seq != _ovAchListSeq || _ovAchList == null) return;
            int end = Math.Min(rows.Count, next + 30);
            for (; next < end; next++) _ovAchList.Children.Add(rows[next]());
            if (next < rows.Count) DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, AddChunk);
        }
        AddChunk();
    }

    private static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontSize = 15,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Foreground = TabsTheme.B(TabsTheme.TextMuted),
        Margin = new Thickness(2, 10, 0, 2),
    };

    private static Grid ProgressBar(AchievementSet set)
    {
        const double h = 10;
        var bar = new Grid { Height = h, CornerRadius = new CornerRadius(h / 2), Background = TabsTheme.B("#263042") };
        double done = set.Total == 0 ? 0 : (double)set.Unlocked / set.Total;
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(done, 0.0001), GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - done, 0.0001), GridUnitType.Star) });
        if (done > 0) bar.Children.Add(new Border { Background = TabsTheme.B("#3D8BFF"), CornerRadius = new CornerRadius(h / 2) });
        return bar;
    }

    private Border AchievementRow(AchievementSet set, Achievement a)
    {
        bool masked = a.IsMasked(_settings().ShowHiddenAchievements);
        var text = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = masked ? "Hidden achievement" : a.Name,
            FontSize = 17,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = TabsTheme.B(a.Unlocked ? TabsTheme.TextPrimary : TabsTheme.TextSecondary),
        });
        var desc = masked ? "Details are shown once you unlock it." : a.Description;
        if (desc.Length > 0)
            text.Children.Add(new TextBlock { Text = desc, FontSize = 14, TextWrapping = TextWrapping.Wrap, Foreground = TabsTheme.B(TabsTheme.TextSecondary) });
        var c = System.Globalization.CultureInfo.CurrentCulture;
        var facts = new List<string>();
        if (a.ProgressPercent is int done && !masked) facts.Add($"{done}% done");
        if (a.Gamerscore is int g) facts.Add($"{g} Gamerscore");
        if (a.Percent is double p) facts.Add($"{p.ToString("0.0", c)}% of players have it");
        if (facts.Count > 0)
            text.Children.Add(new TextBlock { Text = string.Join(" · ", facts), FontSize = 13, Foreground = TabsTheme.B(TabsTheme.TextMuted) });

        var grid = new Grid { ColumnSpacing = 18 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(AchievementIcon(set, a.Unlocked ? a.Icon : a.IconGray ?? a.Icon, dim: !a.Unlocked));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        if (a.Unlocked && a.UnlockedUtc is DateTime t)
        {
            var local = t.ToLocalTime();
            var when = new TextBlock
            {
                Text = local.ToString("d MMM yyyy", c) + "\n" + local.ToString("t", c),
                FontSize = 13,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = TabsTheme.B(TabsTheme.TextSecondary),
            };
            Grid.SetColumn(when, 2);
            grid.Children.Add(when);
        }
        return new Border
        {
            Background = UIFactory.ThemedBrush(a.Unlocked
                ? Windows.UI.Color.FromArgb(0xE6, 0x12, 0x18, 0x24)
                : Windows.UI.Color.FromArgb(0xD9, 0x0F, 0x14, 0x1E)),
            BorderBrush = TabsTheme.B(TabsTheme.CardBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 12, 20, 12),
            MaxWidth = 1100,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = grid,
        };
    }

    private Border AchievementIcon(AchievementSet set, string? file, bool dim)
    {
        double w = set.Store == AchievementStore.Xbox ? Math.Round(OvAchIcon * 16 / 9) : OvAchIcon;
        var image = new Image { Width = w, Height = OvAchIcon, Stretch = Stretch.UniformToFill };
        var frame = new Border
        {
            Width = w,
            Height = OvAchIcon,
            CornerRadius = new CornerRadius(6),
            Background = TabsTheme.B("#1A2130"),
            Opacity = dim ? 0.6 : 1,
            Child = image,
        };
        if (file == null) return frame;
        int decode = (int)Math.Ceiling(w * Math.Max(1, Content?.XamlRoot?.RasterizationScale ?? 1));
        void Show(string path)
        {
            try { image.Source = new BitmapImage(new Uri(path)) { DecodePixelWidth = decode }; }
            catch (UriFormatException ex) { CrashReporter.Log($"[OverlayWindow.AchievementIcon] {ex.Message}"); }
        }
        string? cached = set.Store == AchievementStore.Xbox
            ? XboxAchievementService.TryGetCachedIcon(set.GameId, file)
            : int.TryParse(set.GameId, out var id) ? SteamAchievementService.TryGetCachedIcon(id, file) : null;
        if (cached != null) { Show(cached); return frame; }
        _ = Task.Run(async () =>
        {
            string? path = set.Store == AchievementStore.Xbox
                ? await XboxAchievementService.GetIconAsync(set.GameId, file, CancellationToken.None).ConfigureAwait(false)
                : int.TryParse(set.GameId, out var appId)
                    ? await SteamAchievementService.GetIconAsync(appId, file, CancellationToken.None).ConfigureAwait(false)
                    : null;
            if (path != null) DispatcherQueue.TryEnqueue(() => Show(path));
        });
        return frame;
    }
}
