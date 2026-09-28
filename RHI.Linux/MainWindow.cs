using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using RHI.Linux.Core;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RHI.Linux;

public sealed partial class MainWindow : Window
{
    private readonly HttpClient _http;
    private readonly Catalog _catalog;
    private readonly Downloads _downloads;
    private readonly GameSetup _setup;
    private readonly DlssCatalog _dlss;
    private readonly AddonReleases _releases;
    private readonly NeuralRenderingSetup _nr;
    private readonly DlssSwap _swap;
    private readonly REFramework _ref;
    private readonly OptiScaler _os;
    private Settings _settings;
    private List<Game> _games = [];
    private readonly Dictionary<string, InstallationStatus> _states = [];
    private readonly ListBox _library = new() { Name = "GameLibrary" };
    private readonly TextBox _search = new() { Watermark = "Filter games…", Name = "GameSearch" };
    private readonly StackPanel _filters = new() { Spacing = 4 };
    private readonly StackPanel _details = new() { Spacing = 16, Margin = new Thickness(30, 24), MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _status = Label("Starting RHI…", 11, Muted);
    private readonly TextBlock _counts = Label("Scanning library…", 11, Muted);
    private readonly ProgressBar _progress = new() { IsIndeterminate = true, IsVisible = false, Height = 2 };
    private readonly Grid _workspace = new() { ColumnDefinitions = new("300,*") };
    private readonly StackPanel _actions = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(15) };
    private string _filter = "All Games";
    private bool _busy, _rendering, _compact;
    private Game? Selected => _library.SelectedItem as Game;
    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));
    private static readonly IBrush Teal = Brush("#4DC9E6"), Green = Brush("#5ECB7D"), Amber = Brush("#D4A856"), Muted = Brush("#6B7A8E"), Secondary = Brush("#A0AABB");

    public MainWindow(IEnumerable<Game>? initialGames = null, HttpClient? http = null)
    {
        _http = http ?? Downloads.CreateClient();
        Title = "RHI — Simplified PC Gaming"; Width = 1180; Height = 900; MinWidth = 1000; MinHeight = 660;
        SystemDecorations = SystemDecorations.None;
        _catalog = new(_http); _downloads = new(_http); _setup = new(_downloads, _catalog);
        _dlss = new(_http, _downloads); _releases = new(_http, _downloads); _nr = new(_downloads, _dlss, _releases, _catalog); _swap = new(_dlss); _ref = new(_http, _downloads); _os = new(_http, _downloads, _dlss);
        DlssProfile.ApplyManifestPresets(_catalog.ManifestRoot("dlssPresets"));
        try { _settings = Settings.Load(); } catch { _settings = new(); }
        var shell = new Grid { RowDefinitions = new("56,*,Auto,32") };
        var header = new Grid { ColumnDefinitions = new("Auto,*,Auto,Auto"), Background = Brush("#0F1318"), Margin = new Thickness(0) };
        var brand = Row(Asset("rhi", 36), new StackPanel { Spacing = 2, Children = { Label("RHI", 15, Teal, true), Label("Simplified PC Gaming", 10, Brush("#2A7A90")) } });
        brand.Margin = new Thickness(16, 0); header.Children.Add(brand);
        header.PointerPressed += (_, e) =>
        {
            for (var control = e.Source as Control; control != null && control != header; control = control.Parent as Control)
                if (control is Button) return;
            if (e.ClickCount == 2) ToggleMaximize(); else BeginMoveDrag(e);
        };
        _actions.Children.Add(Action("Refresh", async () =>
        {
            _nrStates.Clear();
            await RefreshCatalog();
            await RefreshDlssCatalogs(force: true);
            ShowGame();
        }, "teal", "RefreshLibrary"));
        _actions.Children.Add(Action("Shaders/Addons", ShowShaders, "teal"));
        _actions.Children.Add(Action("Update All", UpdateAll, "teal"));
        _actions.Children.Add(Action("Links", ShowLinks, "teal"));
        _actions.Children.Add(Action("Quick Start", ShowHelp, "teal"));
        _actions.Children.Add(Action("Views", () => { _compact = !_compact; ShowGame(); return Task.CompletedTask; }, "teal"));
        _actions.Children.Add(Action("Settings", ShowSettings, "teal"));
        Grid.SetColumn(_actions, 2); header.Children.Add(_actions);
        var caption = Row(Plain("−", () => WindowState = WindowState.Minimized), Plain("□", ToggleMaximize), Plain("×", Close));
        caption.Margin = new Thickness(8, 0, 8, 0); Grid.SetColumn(caption, 3); header.Children.Add(caption);
        shell.Children.Add(header);
        var sidebar = new Grid { RowDefinitions = new("Auto,Auto,Auto,*"), Background = Brush("#101419") };
        var search = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(10, 9, 10, 8) };
        search.Children.Add(_search);
        var add = Action("+", AddGame, "success", "AddGame"); add.Margin = new Thickness(6, 0, 0, 0); Grid.SetColumn(add, 1); search.Children.Add(add);
        ToolTip.SetTip(add, "Add a Windows game"); sidebar.Children.Add(search);
        _filters.Margin = new Thickness(10, 0, 10, 8); Grid.SetRow(_filters, 1); sidebar.Children.Add(_filters);
        _counts.Margin = new Thickness(12, 4, 8, 10); Grid.SetRow(_counts, 2); sidebar.Children.Add(_counts);
        _library.ItemTemplate = new FuncDataTemplate<Game>((game, _) => game == null ? new Border() : LibraryItem(game));
        Grid.SetRow(_library, 3); sidebar.Children.Add(_library); _workspace.Children.Add(sidebar);
        var scroller = new ScrollViewer { Content = _details, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var separator = new Border { BorderBrush = Brush("#1E2630"), BorderThickness = new Thickness(1, 0, 0, 0), Child = scroller };
        Grid.SetColumn(separator, 1); _workspace.Children.Add(separator);
        Grid.SetRow(_workspace, 1); shell.Children.Add(_workspace); Grid.SetRow(_progress, 2); shell.Children.Add(_progress);
        var footer = new Grid { ColumnDefinitions = new("*,Auto"), Background = Brush("#0D1015") };
        _status.Margin = new Thickness(14, 0); _status.VerticalAlignment = VerticalAlignment.Center; _status.TextTrimming = TextTrimming.CharacterEllipsis;
        footer.Children.Add(_status);
        var platform = Label("Linux / Proton", 10, Muted); platform.Margin = new Thickness(14, 0); platform.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(platform, 1); footer.Children.Add(platform); Grid.SetRow(footer, 3); shell.Children.Add(footer);
        Content = shell;
        _search.TextChanged += (_, _) => Filter();
        _library.SelectionChanged += (_, _) => { if (!_rendering) ShowGame(); };
        Opened += async (_, _) =>
        {
            if (initialGames != null) { _games = initialGames.ToList(); await Run(async () => { await ReadStates(); Filter(); }); }
            else
            {
                await Run(RefreshCatalog);
                _timer.Start();
                // DLSS and Neural Rendering version lists refresh quietly in the background.
                await RefreshDlssCatalogs(); if (!_busy) ShowGame();
            }
        };
        Activated += async (_, _) => { if (!_busy && _games.Count > 0) await RefreshStatus(); };
        _timer.Tick += async (_, _) => { if (!_busy && IsActive) await RefreshStatus(); };
        Closed += (_, _) => { _timer.Stop(); _http.Dispose(); };
        BuildFilters();
    }
    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private static TextBlock Label(string text, double size = 12, IBrush? color = null, bool bold = false) => new()
    { Text = text, FontSize = size, Foreground = color ?? Brush("#E8ECF2"), FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private static StackPanel Row(params Control[] controls)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        foreach (var c in controls) row.Children.Add(c); return row;
    }
    private static Control Asset(string name, double size)
    {
        try { return new Image { Source = new Bitmap(AssetLoader.Open(new Uri($"avares://RHI.Linux/Assets/{name}.png"))), Width = size, Height = size }; }
        catch { return Label(name == "rhi" ? "◈" : "◉", size, Teal); }
    }
    private Button Action(string title, Func<Task> action, string style = "", string? name = null)
    {
        var button = new Button { Content = title, Name = name }; if (style.Length > 0) button.Classes.Add(style);
        button.Click += async (_, _) => await Run(action); return button;
    }
    private static Button Plain(string title, System.Action action)
    {
        var b = new Button { Content = title, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(6), MinWidth = 24 };
        b.Click += (_, _) => action(); return b;
    }
    private static Border Badge(string text, bool good = false, bool warning = false) => new()
    {
        Background = Brush(good ? "#122818" : warning ? "#201C10" : "#1A2030"), BorderBrush = Brush(good ? "#1E4028" : "#283240"),
        BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(7, 3),
        Child = Label(text, 11, good ? Green : warning ? Amber : Secondary)
    };
    private static Border Card(Control child) => new() { Background = Brush("#131820"), BorderBrush = Brush("#283240"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 14), Child = child };
    private IProgress<string> Progress => new Progress<string>(s => _status.Text = s);
    private InstallationStatus State(Game game) => _states.GetValueOrDefault(game.Id) ?? new();
    private async Task Run(Func<Task> action)
    {
        if (_busy) return;
        _busy = true; _workspace.IsEnabled = false; _actions.IsEnabled = false; _progress.IsVisible = true;
        try { await action(); }
        catch (Exception ex) { _status.Text = ex.Message; CrashReporter.Log(ex.ToString()); await Message("Could not finish", ex.Message); }
        finally { _busy = false; _workspace.IsEnabled = true; _actions.IsEnabled = true; _progress.IsVisible = false; }
    }
    private async Task Scan()
    {
        _status.Text = "Finding your installed games…";
        var discovery = new GameDiscovery();
        _games = await Task.Run(() => discovery.Scan(GameDiscovery.DefaultSteamRoots(LinuxPaths.Home).Concat(_settings.SteamRoots), _settings, _catalog));
        await ReadStates();
        Filter();
        _status.Text = $"{_games.Count} games detected · Ready";
        if (discovery.Warnings.Count > 0) _status.Text = string.Join(" · ", discovery.Warnings);
    }
    private async Task RefreshCatalog()
    {
        await RefreshCompatibility();
        // Always discover games with the latest valid overrides, even when the wiki is offline.
        await Scan();
        await _catalog.Refresh(Progress, refreshManifest: false);
        await ReadStates();
        Filter();
        _status.Text = "Library refreshed · " + _catalog.Status;
    }
    private async Task RefreshCompatibility()
    {
        await _catalog.RefreshManifest(Progress);
        DlssProfile.ApplyManifestPresets(_catalog.ManifestRoot("dlssPresets"));
    }
    private async Task ReadStates()
    {
        var snapshots = await Task.Run(() => _games.ToDictionary(g => g.Id, g => InstallationStatus.Read(g, _settings.For(g).SteamConfig, Extras(g))));
        _states.Clear(); foreach (var s in snapshots) _states[s.Key] = s.Value;
    }
    private async Task RefreshStatus()
    {
        var game = Selected; if (game == null) return;
        var old = State(game);
        var fresh = await Task.Run(() => InstallationStatus.Read(game, _settings.For(game).SteamConfig, Extras(game)));
        if (_busy || Selected?.Id != game.Id) return;
        _states[game.Id] = fresh;
        if (System.Text.Json.JsonSerializer.Serialize(old) != System.Text.Json.JsonSerializer.Serialize(fresh)) { ShowGame(); _library.ItemsSource = _library.ItemsSource; }
    }
    private void BuildFilters()
    {
        _filters.Children.Clear();
        foreach (var titles in new[] { new[] { "All Games", "Installed", "Favourites", "Hidden" }, new[] { "Unreal", "Unity", "RE Engine", "RenoDX" } })
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            foreach (var title in titles)
            {
                var b = new Button { Content = title }; b.Click += (_, _) => { _filter = title; BuildFilters(); Filter(); }; b.Classes.Add("chip");
                if (_filter == title) b.Classes.Add("selected"); row.Children.Add(b);
            }
            _filters.Children.Add(row);
        }
    }
    private void Filter()
    {
        var id = Selected?.Id ?? _settings.LastGameId;
        var shown = _games.Where(g => g.Name.Contains(_search.Text ?? "", StringComparison.OrdinalIgnoreCase)).Where(g =>
        {
            var p = _settings.For(g); var mod = _setup.Mod(g, p);
            if (p.Hidden != (_filter == "Hidden")) return false;
            return _filter switch
            {
                "Installed" => State(g).Components.Values.Any(c => c.Installed), "Favourites" => p.Favourite,
                "RenoDX" => mod != null,
                "Unreal" => mod?.SnapshotUrl?.Contains("unreal", StringComparison.OrdinalIgnoreCase) == true || mod?.SnapshotUrl?.Contains("ue-extended") == true || Directory.Exists(Path.Combine(g.Root, "Engine")),
                "RE Engine" => g.IsREEngine,
                "Unity" => Directory.Exists(Path.Combine(g.Root, Path.GetFileNameWithoutExtension(g.Executable) + "_Data")), _ => true
            };
        }).ToList();
        _rendering = true;
        _library.ItemsSource = shown; _library.SelectedItem = shown.FirstOrDefault(g => g.Id == id) ?? shown.FirstOrDefault();
        _rendering = false;
        _counts.Text = $"●  {shown.Count} shown    {_states.Values.Count(s => s.Components.Values.Any(c => c.Installed))} installed";
        if (shown.Count == 0) { _details.Children.Clear(); _details.Children.Add(Label("No games here", 20, null, true)); _details.Children.Add(Label("Try another filter, refresh your library, or use + to add a game.", 13, Secondary)); }
        else ShowGame();
    }
    private Control LibraryItem(Game game)
    {
        var grid = new Grid { ColumnDefinitions = new("18,*,Auto") };
        grid.Children.Add(Asset("steam", 14)); var name = Label(game.Name, 12, Secondary); name.TextTrimming = TextTrimming.CharacterEllipsis; name.TextWrapping = TextWrapping.NoWrap; name.Margin = new Thickness(6, 0);
        Grid.SetColumn(name, 1); grid.Children.Add(name);
        if (State(game).Get("ReShade").Installed || State(game).Get("RenoDX").Installed)
        {
            var dot = Label("●", 10, Green); Grid.SetColumn(dot, 2); grid.Children.Add(dot); ToolTip.SetTip(dot, "Plugins installed");
        }
        return grid;
    }
}
