using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using RHI.Linux.Core;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RHI.Linux;

public sealed class AdvancedWindow : Window
{
    private readonly HttpClient _http = Downloads.CreateClient();
    private readonly Catalog _catalog;
    private readonly Downloads _downloads;
    private readonly Settings _settings;
    private readonly GameDiscovery _discovery = new();
    private List<Game> _games = [];
    private readonly ListBox _library = new() { Name = "GameLibrary" };
    private readonly TextBox _search = new() { Watermark = "Search installed games…" };
    private readonly StackPanel _details = new() { Spacing = 16, Margin = new Thickness(24) };
    private readonly TextBlock _status = new() { Text = "Starting RHI…", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16, 10) };
    private readonly TextBlock _count = new() { Margin = new Thickness(4, 8), Foreground = Brushes.LightGray };
    private readonly ProgressBar _progress = new() { IsIndeterminate = true, IsVisible = false, Height = 3 };
    private readonly Grid _workspace;
    private readonly StackPanel _toolbar;
    private bool _busy;
    private bool _building;
    private bool _replaceForeign;
    private Game? Selected => _library.SelectedItem as Game;

    public AdvancedWindow()
    {
        Title = "RHI — Advanced game settings";
        Width = 1180; Height = 880; MinWidth = 850; MinHeight = 620;
        _catalog = new(_http); _downloads = new(_http);
        try { _settings = Settings.Load(); }
        catch (Exception ex) { _settings = new(); _status.Text = "Could not load settings: " + ex.Message; }
        var shell = new Grid { RowDefinitions = new("Auto,*,Auto,Auto") };
        _toolbar = Row(
            new TextBlock { Text = "RHI", FontWeight = FontWeight.Bold, FontSize = 30, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) },
            Button("Rescan games", Scan), Button("Refresh RenoDX catalogue", RefreshCatalog),
            Button("Add game EXE…", AddGame), Button("Add Steam library…", AddLibrary),
            Button("Help", () => { Proton.Open(Path.Combine(AppContext.BaseDirectory, "LINUX.md")); return Task.CompletedTask; }));
        _toolbar.Margin = new Thickness(18, 14);
        shell.Children.Add(_toolbar);
        _workspace = new Grid { ColumnDefinitions = new("290,*") };
        Grid.SetRow(_workspace, 1);
        var sidebar = new Grid { RowDefinitions = new("Auto,Auto,*"), Margin = new Thickness(16, 4, 0, 0) };
        sidebar.Children.Add(_search); Grid.SetRow(_count, 1); sidebar.Children.Add(_count);
        Grid.SetRow(_library, 2); sidebar.Children.Add(_library);
        _workspace.Children.Add(sidebar);
        var scroller = new ScrollViewer { Content = _details, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetColumn(scroller, 1); _workspace.Children.Add(scroller); shell.Children.Add(_workspace);
        Grid.SetRow(_progress, 2); shell.Children.Add(_progress);
        Grid.SetRow(_status, 3); shell.Children.Add(_status); Content = shell;
        _search.TextChanged += (_, _) => Filter();
        _library.SelectionChanged += (_, _) => ShowGame();
        Opened += async (_, _) => { await Run(Scan); await Run(RefreshCatalog); };
        Closed += (_, _) => _http.Dispose();
    }

    private static StackPanel Row(params Control[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var child in children) row.Children.Add(child);
        return row;
    }
    private static TextBlock Text(string text, double size = 14) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
    private static TextBox ReadOnly(string text) => new() { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true };
    private Button Button(string label, Func<Task> action)
    {
        var button = new Button { Content = label };
        button.Click += async (_, _) => await Run(action);
        return button;
    }
    private void Section(string title) => _details.Children.Add(new TextBlock { Text = title, FontSize = 19, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
    private IProgress<string> Progress => new Progress<string>(s => _status.Text = s);

    private async Task Run(Func<Task> action)
    {
        if (_busy) return;
        _busy = true; _workspace.IsEnabled = false; _toolbar.IsEnabled = false; _progress.IsVisible = true;
        try { await action(); }
        catch (Exception ex) { _status.Text = "Could not complete operation: " + ex.Message; CrashReporter.Log(ex.ToString()); }
        finally { _busy = false; _workspace.IsEnabled = true; _toolbar.IsEnabled = true; _progress.IsVisible = false; }
    }

    private async Task Scan()
    {
        _status.Text = "Scanning Steam libraries and Windows executables…";
        _games = await Task.Run(() => _discovery.Scan(GameDiscovery.DefaultSteamRoots(LinuxPaths.Home).Concat(_settings.SteamRoots), _settings, _catalog));
        Filter();
        _status.Text = $"Found {_games.Count} games. " + string.Join(" • ", _discovery.Warnings);
    }
    private void Filter()
    {
        var selected = Selected?.Id ?? _settings.LastGameId;
        var games = _games.Where(g => g.Name.Contains(_search.Text ?? "", StringComparison.OrdinalIgnoreCase)).ToList();
        _library.ItemsSource = games;
        _library.SelectedItem = games.FirstOrDefault(g => g.Id == selected) ?? games.FirstOrDefault();
        _count.Text = $"{games.Count} games • Steam / Proton";
        if (games.Count == 0) { _details.Children.Clear(); _details.Children.Add(Text("No games found. Add a Steam library or a Windows game executable to get started.", 20)); }
    }
    private async Task RefreshCatalog()
    {
        await _catalog.Refresh(Progress); ShowGame(); _status.Text = _catalog.Status;
    }
    private void SavePreference(Action action) { if (_building) return; action(); _settings.Save(); }

    private void ShowGame()
    {
        if (Selected is not { } game) return;
        _building = true;
        try
        {
            _settings.LastGameId = game.Id;
            _settings.Save();
            _details.Children.Clear();
            var prefs = _settings.For(game);
            _details.Children.Add(Text(game.Name, 28));
            _details.Children.Add(Text($"{game.Source}" + (game.AppId is null ? "" : $" • App ID {game.AppId}")));
            _details.Children.Add(ReadOnly(game.Root));
            var openButtons = Row(Button("Open game folder", () => { Proton.Open(game.Root); return Task.CompletedTask; }));
            if (game.AppId != null) openButtons.Children.Add(Button("Launch via Steam", () => { Proton.Open("steam://rungameid/" + game.AppId); return Task.CompletedTask; }));
            _details.Children.Add(openButtons);
            Section("Game executable & Proton prefix");
            var exes = new ComboBox { ItemsSource = game.Executables.Select(e => Path.GetRelativePath(game.Root, e)).ToList(),
                SelectedIndex = game.Executables.IndexOf(game.Executable ?? ""), HorizontalAlignment = HorizontalAlignment.Stretch, Name = "ExecutablePicker" };
            exes.SelectionChanged += (_, _) =>
            {
                if (_building || exes.SelectedIndex < 0) return;
                SavePreference(() => { game.Executable = game.Executables[exes.SelectedIndex]; prefs.Executable = game.Executable; }); ShowGame();
            };
            _details.Children.Add(exes);
            if (game.Executable is null) _details.Children.Add(Text("No Windows executable found. If this is a native Linux game, install its Windows version through Steam compatibility settings to use these plugins."));
            else _details.Children.Add(Text($"Detected: {game.Architecture} • {game.Api}\nPlugins install beside this executable: {game.InstallDirectory}"));
            var prefix = new TextBox { Text = game.Prefix ?? "", Watermark = "Optional custom prefix: …/compatdata/APPID/pfx", HorizontalAlignment = HorizontalAlignment.Stretch };
            _details.Children.Add(prefix);
            _details.Children.Add(Row(Button("Save prefix", () =>
            {
                var path = prefix.Text?.Trim();
                if (!string.IsNullOrEmpty(path) && !Directory.Exists(Path.Combine(path, "drive_c"))) throw new IOException("Choose the prefix folder containing drive_c (usually compatdata/<appid>/pfx).");
                prefs.Prefix = string.IsNullOrEmpty(path) ? null : LinuxPaths.Canonical(path); game.Prefix = prefs.Prefix; _settings.Save();
                _status.Text = "Proton prefix saved."; return Task.CompletedTask;
            }), Button("Open prefix AppData", () =>
            {
                if (string.IsNullOrEmpty(prefix.Text)) throw new IOException("Launch the game once to create its prefix, then rescan, or enter a custom prefix above.");
                Proton.Open(Proton.LocalAppData(prefix.Text)); return Task.CompletedTask;
            })));
            _details.Children.Add(Text("The prefix holds Windows settings and saves. Game DLLs are installed in the executable folder above. A missing prefix is normal before the game's first launch."));
            if (game.Executable is null) return;
            var install = new Installation(game.InstallDirectory);
            var state = install.ReadState();
            Section("ReShade");
            _details.Children.Add(Text("Addon-enabled builds support RenoDX and custom addons. Use these with games that permit modding; anti-cheat games may reject injected DLLs."));
            var apiNames = new[] { "Auto", "DirectX9", "DirectX10", "DirectX11", "DirectX12", "OpenGL", "Vulkan" };
            var api = new ComboBox { ItemsSource = apiNames, SelectedItem = prefs.Api, MinWidth = 140 };
            var channel = new ComboBox { ItemsSource = new[] { "Stable", "Nightly" }, SelectedItem = prefs.Channel, MinWidth = 130 };
            GraphicsApiType ChosenApi()
            {
                if (api.SelectedItem is string value && value != "Auto") return Enum.Parse<GraphicsApiType>(value);
                var mapped = _catalog.ManifestString("graphicsApiOverrides", game.Name)?.Replace("DX", "DirectX");
                return Enum.TryParse<GraphicsApiType>(mapped, out var known) ? known : game.Api;
            }
            api.SelectionChanged += (_, _) => SavePreference(() => prefs.Api = api.SelectedItem?.ToString() ?? "Auto");
            channel.SelectionChanged += (_, _) => SavePreference(() => prefs.Channel = channel.SelectedItem?.ToString() ?? "Nightly");
            _details.Children.Add(Row(Text("Graphics API"), api, Text("Channel"), channel));
            _details.Children.Add(Text("Nightly is recommended for current Proton: it includes D3D12 compatibility fixes missing from ReShade 6.8.0. The channel is saved per game."));
            _details.Children.Add(Text("Choose the game's DirectX API even when Proton translates it to Vulkan. Native Vulkan injection is not available in this build."));
            var replace = new CheckBox { Content = "Back up and replace existing unmanaged plugin files" };
            _details.Children.Add(replace);
            replace.IsCheckedChanged += (_, _) => _replaceForeign = replace.IsChecked == true;
            _replaceForeign = false;
            _details.Children.Add(Row(Button("Install / update ReShade", async () =>
            {
                var proxy = Installation.ProxyFor(ChosenApi());
                var (path, version) = await _downloads.ReShade(channel.SelectedItem?.ToString() ?? "Nightly", game.Architecture, Progress);
                try
                {
                    var compiler = await _downloads.ShaderCompiler(game.Architecture, Progress);
                    await Task.Run(() => install.Install("ReShade", version, [new(proxy, File.ReadAllBytes(path)), Installation.DefaultIni(), compiler], _replaceForeign, proxy));
                }
                finally { File.Delete(path); }
                ShowGame(); _status.Text = $"ReShade {version} installed. Set the Proton launch options below before starting the game.";
            }), Button("Use local ReShade DLL / setup…", async () =>
            {
                var file = await PickFile("Choose ReShade DLL or official setup EXE", ["*.dll", "*.exe"]);
                if (file is null) return;
                var proxy = Installation.ProxyFor(ChosenApi()); var payload = file;
                if (file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    Directory.CreateDirectory(LinuxPaths.Cache);
                    payload = Path.Combine(LinuxPaths.Cache, Guid.NewGuid().ToString("N") + ".dll");
                    await Downloads.Extract(file, game.Architecture == MachineType.I386 ? "ReShade32.dll" : "ReShade64.dll", payload);
                }
                try
                {
                    Downloads.ValidatePe(payload, game.Architecture);
                    var compiler = await _downloads.ShaderCompiler(game.Architecture, Progress);
                    await Task.Run(() => install.Install("ReShade", "Local", [new(proxy, File.ReadAllBytes(payload)), Installation.DefaultIni(), compiler], _replaceForeign, proxy));
                }
                finally { if (payload != file) File.Delete(payload); }
                ShowGame(); _status.Text = "Local ReShade installed. Set the Proton launch options below.";
            })));

            Section("RenoDX");
            var filter = new TextBox { Watermark = "Search RenoDX mods…" };
            var mods = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, MaxDropDownHeight = 300, Name = "ModPicker" };
            void ModList(string search)
            {
                mods.ItemsSource = _catalog.Mods.Where(m => m.Name.Contains(search, StringComparison.OrdinalIgnoreCase)).Select(m => m.Name).ToList();
            }
            ModList("");
            mods.SelectedItem = prefs.ModName ?? _catalog.Match(game)?.Name;
            _details.Children.Add(filter); _details.Children.Add(mods);
            var modNotes = Text(""); _details.Children.Add(modNotes);
            GameMod? ChosenMod() => _catalog.Mods.FirstOrDefault(m => m.Name == mods.SelectedItem?.ToString());
            void Notes() { var m = ChosenMod(); modNotes.Text = m is null ? "No exact match selected. Search the catalogue or use a local addon." : $"{m.Status}  {m.Maintainer}\n{m.Notes}"; }
            Notes();
            filter.TextChanged += (_, _) => ModList(filter.Text ?? "");
            mods.SelectionChanged += (_, _) => { Notes(); SavePreference(() => prefs.ModName = mods.SelectedItem?.ToString()); };
            _details.Children.Add(Row(Button("Install / update RenoDX", async () =>
            {
                RequireReShade(install);
                var mod = ChosenMod() ?? throw new IOException("Select a RenoDX mod first.");
                var url = _catalog.AddonUrl(game, mod) ?? throw new IOException("This mod has no direct download for the game's architecture. Open its instructions or install the author's local addon.");
                var file = await _downloads.Fetch(url, Progress, true);
                Downloads.ValidatePe(file, game.Architecture);
                var filename = Uri.UnescapeDataString(Path.GetFileName(new Uri(url).AbsolutePath));
                var suffix = game.Architecture == MachineType.I386 ? ".addon32" : ".addon64";
                if (!filename.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) filename = "renodx" + suffix;
                await Task.Run(() => install.Install("RenoDX", mod.Name + " • " + DateTime.UtcNow.ToString("yyyy-MM-dd"), [new(filename, File.ReadAllBytes(file))], _replaceForeign));
                ShowGame(); _status.Text = "RenoDX installed. Review the author's game-specific instructions before launching.";
            }), Button("Mod instructions", () =>
            {
                var mod = ChosenMod(); Proton.Open(mod?.NameUrl ?? mod?.NexusUrl ?? "https://github.com/clshortfuse/renodx/wiki/Mods"); return Task.CompletedTask;
            })));
            var gameNotes = _catalog.GameNotes(game);
            if (gameNotes.Length > 0) _details.Children.Add(Text(gameNotes));

            var engineInis = IniSettings.FindEngineInis(game);
            if (engineInis.Count > 0)
            {
                Section("Unreal Engine HDR settings");
                _details.Children.Add(Text("For UE Extended games whose instructions require Engine.ini. Enable HDR in Bazzite / Gamescope first. These edits select the HDR path and real-time sliders; existing settings are backed up. Not recommended for UE4 games."));
                var engineIni = new ComboBox { ItemsSource = engineInis, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
                _details.Children.Add(engineIni);
                _details.Children.Add(Row(Button("Apply UE Extended HDR settings", () =>
                {
                    var target = engineIni.SelectedItem as string ?? throw new IOException("Select an Engine.ini file.");
                    RequireReShade(install);
                    IniSettings.Apply(target, IniSettings.UnrealHdr, readOnly: true);
                    IniSettings.Apply(LinuxPaths.ResolveCase(game.InstallDirectory, "ReShade.ini"), IniSettings.RenoDxHdr);
                    _status.Text = "UE Extended HDR settings applied. Existing keys were retained and the original values were backed up.";
                    return Task.CompletedTask;
                }), Button("Restore previous HDR settings", () =>
                {
                    if (engineIni.SelectedItem is string target) IniSettings.Restore(target);
                    IniSettings.Restore(LinuxPaths.ResolveCase(game.InstallDirectory, "ReShade.ini"));
                    _status.Text = "Previous HDR settings restored; later edits to unrelated keys preserved.";
                    return Task.CompletedTask;
                })));
            }

            Section("Shaders & custom addons");
            _details.Children.Add(Row(Button("Install / update Lilium HDR", () => Shader("Lilium HDR")),
                Button("Install / update standard shaders", () => Shader("Standard"))));
            async Task Shader(string pack)
            {
                RequireReShade(install);
                var files = await _downloads.Shaders(pack, Progress);
                await Task.Run(() => install.Install("Shaders: " + pack, DateTime.UtcNow.ToString("yyyy-MM-dd"), files, _replaceForeign));
                ShowGame(); _status.Text = pack + " shaders installed. Enable the desired effects in ReShade using the Home key.";
            }
            _details.Children.Add(Row(Button("Install local addon…", async () =>
            {
                RequireReShade(install);
                var file = await PickFile("Choose a ReShade addon", ["*.addon64", "*.addon32"]);
                if (file is null) return;
                Downloads.ValidatePe(file, game.Architecture);
                var suffix = game.Architecture == MachineType.I386 ? ".addon32" : ".addon64";
                if (!file.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) throw new IOException("The addon extension must match the game's architecture: " + suffix);
                var component = Path.GetFileName(file).StartsWith("renodx-", StringComparison.OrdinalIgnoreCase) ? "RenoDX" : "Addon: " + Path.GetFileName(file);
                await Task.Run(() => install.Install(component, "Local", [new(Path.GetFileName(file), File.ReadAllBytes(file))], _replaceForeign));
                ShowGame(); _status.Text = "Addon installed.";
            }), Button("Open ReShade configuration", () =>
            {
                var ini = LinuxPaths.ResolveCase(game.InstallDirectory, "ReShade.ini");
                if (!File.Exists(ini)) throw new IOException("Install ReShade first."); Proton.Open(ini); return Task.CompletedTask;
            })));
            _details.Children.Add(Text("Local addons include ReLimiter and Display Commander when their Windows builds work under Proton. Windows NVIDIA driver profiles and Windows HDR toggles are unavailable on Linux. Enable HDR through Bazzite / Gamescope for HDR-capable games."));

            Section("Proton launch options");
            var configs = Proton.LocalConfigs(game).ToList();
            var configPicker = new ComboBox { ItemsSource = configs, SelectedIndex = configs.Count > 0 ? 0 : -1, HorizontalAlignment = HorizontalAlignment.Stretch };
            var options = new TextBox { Watermark = "Existing Steam launch options, including %command%", TextWrapping = TextWrapping.Wrap, MinHeight = 70, Name = "LaunchOptions" };
            void LoadOptions()
            {
                options.Text = configPicker.SelectedItem is string config && game.AppId != null ? Proton.ReadOptions(config, game.AppId) ?? "%command%" : "%command%";
            }
            LoadOptions(); configPicker.SelectionChanged += (_, _) => LoadOptions();
            if (configs.Count > 1) _details.Children.Add(configPicker);
            _details.Children.Add(options);
            _details.Children.Add(Row(Button("Generate / merge options", () =>
            {
                options.Text = Proton.LaunchOptions(options.Text ?? "", install.ReadState().Proxy ?? Installation.ProxyFor(ChosenApi()), NeuralRenderingSetup.Extras(game, prefs));
                _status.Text = "Options generated. Copy into Steam → game Properties → Launch Options, or exit Steam and save directly."; return Task.CompletedTask;
            }), Button("Copy", async () =>
            {
                if (Clipboard != null) await Clipboard.SetTextAsync(options.Text ?? ""); _status.Text = "Launch options copied.";
            }), Button("Save to Steam (Steam closed)", () =>
            {
                if (configPicker.SelectedItem is not string config || game.AppId == null) throw new IOException("No Steam user configuration found. Copy the options into your launcher instead.");
                options.Text = Proton.LaunchOptions(options.Text ?? "", install.ReadState().Proxy ?? Installation.ProxyFor(ChosenApi()), NeuralRenderingSetup.Extras(game, prefs));
                var backup = Proton.SaveOptions(config, game.AppId, options.Text);
                _status.Text = "Steam launch options saved. Backup: " + backup; return Task.CompletedTask;
            })));
            _details.Children.Add(Text("Existing DLL overrides and other launch arguments are preserved when merging. For Heroic/Lutris, set WINEDLLOVERRIDES in the game's environment settings. After uninstalling, remove the RHI DLL override from your launch options."));

            Section("Installed components");
            _details.Children.Add(Text(state.Components.Count == 0 ? "No components managed by RHI in this executable folder." : string.Join("\n", state.Components.Select(c => c.Key + " — " + c.Value))));
            var components = new ComboBox { ItemsSource = state.Components.Keys.ToList(), SelectedIndex = state.Components.Count > 0 ? 0 : -1, MinWidth = 180 };
            _details.Children.Add(Row(components, Button("Remove selected", async () =>
            {
                if (components.SelectedItem is not string component) throw new IOException("Select an installed component.");
                if (component == "RenoDX") RestoreHdr();
                // Neural Rendering also restores DLSS DLLs outside the executable folder and its INI edits.
                if (component == NeuralRenderingSetup.Component) await NeuralRendering().Remove(game);
                else await Task.Run(() => install.Remove(component));
                ShowGame(); _status.Text = component + " removed; originals restored where applicable.";
            })));
            _details.Children.Add(Button("Remove all managed components", async () =>
            {
                RestoreHdr();
                if (NeuralRenderingSetup.LoadRecord(game.InstallDirectory) != null || install.ReadState().Components.ContainsKey(NeuralRenderingSetup.Component)) await NeuralRendering().Remove(game);
                await Task.Run(() => install.Remove()); ShowGame(); _status.Text = "Managed components removed; originals restored. Edited ReShade settings were preserved. Remove the DLL override from Steam launch options.";
            }));
            void RestoreHdr()
            {
                foreach (var path in engineInis) IniSettings.Restore(path);
                IniSettings.Restore(LinuxPaths.ResolveCase(game.InstallDirectory, "ReShade.ini"));
            }
        }
        catch (Exception ex) { _details.Children.Add(Text("Could not load game settings: " + ex.Message)); CrashReporter.Log(ex.ToString()); }
        finally { _building = false; }
    }

    private NeuralRenderingSetup NeuralRendering() => new(_downloads, new DlssCatalog(_http, _downloads), new AddonReleases(_http, _downloads), _catalog);

    private static void RequireReShade(Installation installation)
    {
        var state = installation.ReadState();
        if (!state.Components.ContainsKey("ReShade")) throw new IOException("Install ReShade through RHI first, then install addons or shaders.");
    }
    private async Task<string?> PickFile(string title, string[] patterns)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new() { Title = title, AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(title) { Patterns = patterns }, FilePickerFileTypes.All] });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }
    private async Task AddGame()
    {
        var exe = await PickFile("Choose the game's Windows executable", ["*.exe", "*.EXE"]);
        if (exe == null) return;
        Downloads.RequireArchitecture(new PeHeaderService().DetectArchitecture(exe));
        var game = new Game { Name = Path.GetFileNameWithoutExtension(exe), Root = LinuxPaths.Canonical(Path.GetDirectoryName(exe)!), Executable = exe };
        if (_settings.ManualGames.All(g => g.Id != game.Id)) _settings.ManualGames.Add(game);
        _settings.For(game).Executable = exe; _settings.Save(); await Scan();
        _library.SelectedItem = _games.FirstOrDefault(g => g.Id == game.Id);
    }
    private async Task AddLibrary()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new() { Title = "Choose a Steam library folder (containing steamapps)", AllowMultiple = false });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } folder) return;
        if (Path.GetFileName(folder) == "steamapps") folder = Path.GetDirectoryName(folder)!;
        if (!Directory.Exists(Path.Combine(folder, "steamapps"))) throw new IOException("Choose a directory containing steamapps.");
        _settings.SteamRoots.Add(LinuxPaths.Canonical(folder)); _settings.SteamRoots = _settings.SteamRoots.Distinct().ToList();
        _settings.Save(); await Scan();
    }
}
