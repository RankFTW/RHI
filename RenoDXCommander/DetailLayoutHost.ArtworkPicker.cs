// DetailLayoutHost.ArtworkPicker.cs -- Spotlight's artwork picker (game menu › Artwork › Browse covers / Browse
// banners): the game's Steam art plus community covers and banners from SteamGridDB in the slide-in panel. Click one
// to use it (it becomes the game's custom art, like Set custom cover / banner); the Steam tile = Use Steam art.
// Needs the user's own SteamGridDB API key (Settings › Accounts & Data). Built panel like Screenshots.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

internal sealed partial class DetailLayoutHost
{
    internal const string ArtworkPickerKey = "ArtworkPicker";
    private const int PickChunk = 12;

    private SteamArtworkService.Kind _pickKind = SteamArtworkService.Kind.Cover;
    private GameCardViewModel? _pickCard;
    private StackPanel? _pickRoot;
    private TextBlock? _pickStatus;
    private Controls.AutoFitGrid? _pickGrid;
    private int _pickSeq;
    private bool _pickBusy;
    private readonly List<(Button Tile, int? ArtId)> _pickTiles = new();

    /// <summary>Opens the picker for this game's covers or banners (game menu › Artwork › Browse...).</summary>
    internal void OpenArtworkPicker(GameCardViewModel card, SteamArtworkService.Kind kind)
    {
        if (!IsSpotlight) return;
        _pickCard = card;
        _pickKind = kind;
        if (IsInPanel(ArtworkPickerKey) && _pickRoot != null)
        {
            RenderPicker();
            return;
        }
        OpenPanel(ArtworkPickerKey);
    }

    private FrameworkElement? BuildArtworkPickerPanel()
    {
        _pickCard ??= _window.ViewModel.SelectedGame;
        if (_pickCard == null) return null;
        _pickRoot = new StackPanel { Spacing = 14 };
        RenderPicker();
        return _pickRoot;
    }

    private void ReturnArtworkPickerPanel()
    {
        _pickSeq++; // drop loads still running
        _pickRoot = null;
        _pickGrid = null;
        _pickStatus = null;
        _pickTiles.Clear();
        _pickBusy = false;
    }

    private bool PickIsCover => _pickKind == SteamArtworkService.Kind.Cover;

    private void RenderPicker()
    {
        if (_pickRoot == null || _pickCard is not { } card) return;
        int seq = ++_pickSeq;
        _pickRoot.Children.Clear();
        _pickTiles.Clear();
        _pickGrid = null;
        _pickBusy = false;

        // Summary card: which game, Cover / Banner switch, status line
        var head = new StackPanel { Spacing = 10 };
        head.Children.Add(new TextBlock
        {
            Text = card.GameName,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = TabsTheme.B(TabsTheme.TextPrimary),
        });
        var seg = TabsTheme.MakeSegmented(new[] { "Cover", "Banner" }, PickIsCover ? 0 : 1, i =>
        {
            _pickKind = i == 0 ? SteamArtworkService.Kind.Cover : SteamArtworkService.Kind.Hero;
            RenderPicker();
        });
        seg.HorizontalAlignment = HorizontalAlignment.Left;
        seg.Width = 240;
        head.Children.Add(seg);
        _pickStatus = MutedText("");
        head.Children.Add(_pickStatus);
        _pickRoot.Children.Add(TabsTheme.MakeCard(head, 16));

        if (!SteamGridDbService.HasKey)
        {
            SetPickStatus("Click Steam to use the game's own art. To browse community covers and banners from "
                        + "SteamGridDB, add your free SteamGridDB API key in Settings › Accounts & Data.");
            AddPickGrid(card);
            AddSettingsButton("Add an API key in Settings");
            return;
        }

        SetPickStatus("Loading from SteamGridDB...");
        AddPickGrid(card);
        LoadPickPage(card, seq, 0);
    }

    private void AddPickGrid(GameCardViewModel card)
    {
        _pickGrid = new Controls.AutoFitGrid
        {
            MinColumnWidth = PickIsCover ? 150 : 320,
            ColumnSpacing = 10,
            RowSpacing = 10,
            StretchFewItems = false,
        };
        _pickRoot!.Children.Add(_pickGrid);
        _pickGrid.Children.Add(MakeSteamPickTile(card));
        RefreshPickHighlight();
    }

    private void AddSettingsButton(string text)
    {
        var btn = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Left };
        TabsTheme.Apply(btn, TabsTheme.BlueButton, height: 36, fontSize: 13);
        btn.Click += (_, _) =>
        {
            ClosePanel();
            _window.OpenSettingsSection("Accounts");
        };
        _pickRoot!.Children.Add(btn);
    }

    private void SetPickStatus(string text)
    {
        if (_pickStatus != null) _pickStatus.Text = text;
    }

    private void LoadPickPage(GameCardViewModel card, int seq, int page)
    {
        var kind = _pickKind;
        var appId = DetailPanelBuilder.ResolveCommunityAppId(card, _window.ViewModel.Manifest);
        var name = card.GameName;
        var token = _window.LifetimeToken;
        _ = Task.Run(async () =>
        {
            SteamGridDbService.ArtPage result;
            try { result = await SteamGridDbService.GetArtAsync(appId, name, kind, page, token).ConfigureAwait(false); }
            catch (Exception ex)
            {
                CrashReporter.Log($"[DetailLayoutHost.LoadPickPage] {name}: {ex.GetType().Name}: {ex.Message}");
                result = new SteamGridDbService.ArtPage(SteamGridDbService.Status.Error, Array.Empty<SteamGridDbService.Art>(), false);
            }
            if (token.IsCancellationRequested) return;
            _window.DispatcherQueue.TryEnqueue(() =>
            {
                if (seq == _pickSeq && IsInPanel(ArtworkPickerKey)) ShowPickPage(card, seq, page, result);
            });
        });
    }

    private void ShowPickPage(GameCardViewModel card, int seq, int page, SteamGridDbService.ArtPage result)
    {
        if (_pickRoot == null || _pickGrid == null) return;
        if (_pickRoot.Children.LastOrDefault() is Button { Tag: "PickMore" } old) _pickRoot.Children.Remove(old);
        string what = PickIsCover ? "covers" : "banners";
        switch (result.Status)
        {
            case SteamGridDbService.Status.BadKey:
                SetPickStatus("SteamGridDB didn't accept your API key. Check it in Settings › Accounts & Data.");
                AddSettingsButton("Open Settings");
                return;
            case SteamGridDbService.Status.NoKey:
                SetPickStatus("Add your SteamGridDB API key in Settings › Accounts & Data to browse community art.");
                AddSettingsButton("Add an API key in Settings");
                return;
            case SteamGridDbService.Status.Offline:
            case SteamGridDbService.Status.Error:
                SetPickStatus(result.Status == SteamGridDbService.Status.Offline
                    ? "Couldn't reach SteamGridDB. Check your connection and try again."
                    : "SteamGridDB sent an unexpected reply. Try again later.");
                var retry = new Button { Content = "Try again", Tag = "PickMore", HorizontalAlignment = HorizontalAlignment.Left };
                TabsTheme.Apply(retry, TabsTheme.IconButton, height: 36, fontSize: 13);
                retry.Click += (_, _) =>
                {
                    _pickRoot?.Children.Remove(retry);
                    SetPickStatus("Loading from SteamGridDB...");
                    LoadPickPage(card, seq, page);
                };
                _pickRoot.Children.Add(retry);
                return;
        }

        int total = _pickTiles.Count(t => t.ArtId != null) + result.Items.Count;
        SetPickStatus(total == 0
            ? $"SteamGridDB has no {what} for this game yet. Click Steam to use the game's own art."
            : $"Community {what} from SteamGridDB. Click one to use it; click Steam to go back to the game's own art.");
        AddPickTiles(card, seq, result.Items, 0, () =>
        {
            if (!result.MayHaveMore || _pickRoot == null) return;
            var more = new Button { Content = $"Show more {what}", Tag = "PickMore", HorizontalAlignment = HorizontalAlignment.Left };
            TabsTheme.Apply(more, TabsTheme.IconButton, height: 36, fontSize: 13);
            more.Click += (_, _) =>
            {
                more.IsEnabled = false;
                more.Content = "Loading...";
                LoadPickPage(card, seq, page + 1);
            };
            _pickRoot.Children.Add(more);
        });
    }

    /// <summary>Adds tiles a chunk at a time at low priority, so a long list doesn't hold up the panel.</summary>
    private void AddPickTiles(GameCardViewModel card, int seq, IReadOnlyList<SteamGridDbService.Art> items, int start, Action done)
    {
        if (seq != _pickSeq || _pickGrid == null) return;
        int end = Math.Min(items.Count, start + PickChunk);
        for (int i = start; i < end; i++)
            _pickGrid.Children.Add(MakeSgdbPickTile(card, seq, items[i]));
        RefreshPickHighlight();
        if (end < items.Count)
            _window.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => AddPickTiles(card, seq, items, end, done));
        else done();
    }

    // ── Tiles ─────────────────────────────────────────────────────────────────

    private double PickAspect => PickIsCover ? 1.5 : 620.0 / 1920.0;

    private (Button Tile, Image Image, TextBlock Fallback) MakePickTile(string caption)
    {
        var image = new Image { Stretch = Stretch.UniformToFill };
        var fallback = new TextBlock
        {
            Text = caption,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(8),
            Foreground = TabsTheme.B(TabsTheme.TextMuted),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        double aspect = PickAspect;
        double minW = PickIsCover ? 150 : 320;
        var cell = new Grid { Height = Math.Round(minW * aspect), Background = TabsTheme.B("#1A2130") };
        cell.SizeChanged += (_, e) =>
        {
            double h = Math.Round(e.NewSize.Width * aspect);
            if (e.NewSize.Width > 0 && Math.Abs(cell.Height - h) > 0.5) cell.Height = h;
        };
        cell.Children.Add(fallback);
        cell.Children.Add(image);

        var btn = new Button
        {
            Content = cell,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(2),
            BorderBrush = TabsTheme.B(TabsTheme.CardBorder),
            CornerRadius = new CornerRadius(8),
            Background = TabsTheme.B("#1A2130"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        return (btn, image, fallback);
    }

    private int DecodeWidth() =>
        (int)Math.Ceiling((PickIsCover ? 240 : 560) * Math.Max(1, _pickRoot?.XamlRoot?.RasterizationScale ?? 1));

    private void ShowPickImage(Image image, TextBlock fallback, string path, int decode)
    {
        try
        {
            var bmp = new BitmapImage { DecodePixelWidth = decode };
            bmp.ImageOpened += (_, _) => fallback.Visibility = Visibility.Collapsed;
            bmp.UriSource = new Uri(path);
            image.Source = bmp;
        }
        catch (UriFormatException ex) { CrashReporter.Log($"[DetailLayoutHost.ShowPickImage] {ex.Message}"); }
    }

    /// <summary>The "Steam" tile: the game's own Steam art. Choosing it removes the custom art.</summary>
    private Button MakeSteamPickTile(GameCardViewModel card)
    {
        var (btn, image, fallback) = MakePickTile("Steam");
        // "Steam" label over the bottom of the image
        if (btn.Content is Grid cell)
            cell.Children.Add(new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(6),
                Padding = new Thickness(8, 2, 8, 3),
                CornerRadius = new CornerRadius(6),
                Background = TabsTheme.B("#CC0B0F17"),
                Child = new TextBlock { Text = "Steam", FontSize = 12, Foreground = TabsTheme.B(TabsTheme.TextPrimary) },
            });
        ToolTipService.SetToolTip(btn, PickIsCover ? "The game's Steam cover" : "The game's Steam banner");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(btn, PickIsCover ? "Steam cover" : "Steam banner");
        _pickTiles.Add((btn, null));

        var kind = _pickKind;
        int decode = DecodeWidth();
        int seq = _pickSeq;
        var appId = DetailPanelBuilder.ResolveCommunityAppId(card, _window.ViewModel.Manifest);
        if (appId is int id)
        {
            if (SteamArtworkService.TryGetCached(id, kind) is { } cached) ShowPickImage(image, fallback, cached, decode);
            else
            {
                var token = _window.LifetimeToken;
                _ = Task.Run(async () =>
                {
                    var path = await SteamArtworkService.GetAsync(id, kind, token).ConfigureAwait(false);
                    if (path == null || token.IsCancellationRequested) return;
                    _window.DispatcherQueue.TryEnqueue(() => { if (seq == _pickSeq) ShowPickImage(image, fallback, path, decode); });
                });
            }
        }
        else fallback.Text = "Steam (no Steam art for this game)";

        btn.Click += (_, _) =>
        {
            if (_pickBusy) return;
            SteamArtworkService.ClearCustom(MainWindow.ArtKey(card), kind);
            _window.ShowArtChange(card, kind);
            RefreshPickHighlight();
            SetPickStatus(kind == SteamArtworkService.Kind.Cover ? "Using the Steam cover." : "Using the Steam banner.");
        };
        return btn;
    }

    private Button MakeSgdbPickTile(GameCardViewModel card, int seq, SteamGridDbService.Art art)
    {
        var (btn, image, fallback) = MakePickTile($"{art.Width}x{art.Height}");
        var tip = string.IsNullOrEmpty(art.Author) ? $"{art.Width} x {art.Height}" : $"By {art.Author}\n{art.Width} x {art.Height}";
        ToolTipService.SetToolTip(btn, tip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(btn,
            string.IsNullOrEmpty(art.Author) ? "SteamGridDB image" : $"SteamGridDB image by {art.Author}");
        _pickTiles.Add((btn, art.Id));

        int decode = DecodeWidth();
        var token = _window.LifetimeToken;
        _ = Task.Run(async () =>
        {
            var path = await SteamGridDbService.GetThumbAsync(art, token).ConfigureAwait(false);
            if (path == null || token.IsCancellationRequested) return;
            _window.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => { if (seq == _pickSeq) ShowPickImage(image, fallback, path, decode); });
        });

        var kind = _pickKind;
        btn.Click += (_, _) => _ = PickSgdbArtAsync(card, kind, art, seq);
        return btn;
    }

    private async Task PickSgdbArtAsync(GameCardViewModel card, SteamArtworkService.Kind kind, SteamGridDbService.Art art, int seq)
    {
        if (_pickBusy) return;
        _pickBusy = true;
        try
        {
            SetPickStatus("Downloading the image...");
            var token = _window.LifetimeToken;
            var key = MainWindow.ArtKey(card);
            var (saved, error) = await Task.Run(async () =>
            {
                var (file, err) = await SteamGridDbService.DownloadFullAsync(art, token).ConfigureAwait(false);
                if (file == null) return ((string?)null, err);
                var custom = SteamArtworkService.SetCustom(key, kind, file);
                if (custom == null) return (null, "Couldn't save the image.");
                SteamGridDbService.RememberChoice(key, kind, art.Id, custom);
                return (custom, (string?)null);
            });
            if (_window.IsShuttingDown) return;
            if (saved == null)
            {
                if (seq == _pickSeq) SetPickStatus(error ?? "Couldn't use this image.");
                return;
            }
            _window.ShowArtChange(card, kind);
            if (seq != _pickSeq) return;
            RefreshPickHighlight();
            SetPickStatus(string.IsNullOrEmpty(art.Author)
                ? (kind == SteamArtworkService.Kind.Cover ? "Cover set." : "Banner set.")
                : (kind == SteamArtworkService.Kind.Cover ? $"Cover set (by {art.Author})." : $"Banner set (by {art.Author})."));
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DetailLayoutHost.PickSgdbArtAsync] {card.GameName}: {ex.GetType().Name}: {ex.Message}");
            if (seq == _pickSeq) SetPickStatus("Couldn't use this image.");
        }
        finally { _pickBusy = false; }
    }

    /// <summary>Outlines the image the game uses now (Steam when it has no custom art).</summary>
    private void RefreshPickHighlight()
    {
        if (_pickCard is not { } card) return;
        var key = MainWindow.ArtKey(card);
        var custom = SteamArtworkService.TryGetCustom(key, _pickKind);
        int? chosen = SteamGridDbService.ChosenId(key, _pickKind, custom);
        foreach (var (tile, artId) in _pickTiles)
        {
            bool on = artId == null ? custom == null : artId == chosen;
            tile.BorderBrush = TabsTheme.B(on ? TabsTheme.Accent : TabsTheme.CardBorder);
        }
    }
}
