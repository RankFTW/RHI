using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using RHI.Linux.Core;
using RenoDXCommander.Services;

namespace RHI.Linux;

public sealed partial class MainWindow
{
    private Window Dialog(string title, StackPanel body, double width = 560)
    {
        var window = new Window { Title = title + " — RHI", Width = width, SizeToContent = SizeToContent.Height, MaxHeight = 730,
            MinWidth = width, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var content = new StackPanel { Margin = new Thickness(24), Spacing = 18, Children = { Label(title, 20, null, true), body } };
        var close = Plain("Close", window.Close); close.HorizontalAlignment = HorizontalAlignment.Right; content.Children.Add(close);
        window.Content = new ScrollViewer { Content = content }; return window;
    }
    private async Task Message(string title, string message)
    {
        var dialog = Dialog(title, new StackPanel { Children = { Label(message, 13, Secondary) } });
        await dialog.ShowDialog(this);
    }
    private async Task<bool> Confirm(string title, string text, string accept)
    {
        var panel = new StackPanel { Spacing = 16, Children = { Label(text, 13, Secondary) } };
        var dialog = Dialog(title, panel); bool confirmed = false;
        var yes = Plain(accept, () => { confirmed = true; dialog.Close(); }); yes.Classes.Add("danger"); panel.Children.Add(yes);
        await dialog.ShowDialog(this); return confirmed;
    }
    private Button DialogAction(string title, StackPanel body, Func<Task> action, string style = "action")
    {
        var button = new Button { Content = title }; button.Classes.Add(style);
        button.Click += async (_, _) =>
        {
            body.IsEnabled = false;
            try { await action(); }
            catch (Exception ex) { _status.Text = ex.Message; await Message("Could not finish", ex.Message); }
            finally { body.IsEnabled = true; }
        };
        return button;
    }
    private async Task ShowReShadeSettings(Game game)
    {
        var prefs = _settings.For(game); var installed = State(game).Get("ReShade");
        var body = new StackPanel { Spacing = 14 };
        body.Children.Add(Label("Installed: " + (installed.Version ?? "Not installed"), 13, installed.Installed ? Green : Secondary));
        body.Children.Add(Label("ReShade channel", 13, null, true));
        var channel = new ComboBox { ItemsSource = new[] { "Nightly", "Stable" }, SelectedItem = prefs.Channel, HorizontalAlignment = HorizontalAlignment.Stretch, Name = "ReShadeChannel" };
        body.Children.Add(channel);
        body.Children.Add(Label("Nightly — recommended for Proton. Includes newer DirectX 12 compatibility fixes that can resolve black screens in affected games.\n\nStable — the official release. Version 6.8.0 is missing that Proton fix.", 12, Secondary));
        body.Children.Add(Label("Apply downloads and installs the selected channel. The choice is saved for this game's future updates.", 12, Muted));
        var dialog = Dialog("ReShade settings", body);
        body.Children.Add(DialogAction("Apply & install", body, async () =>
        {
            prefs.Channel = channel.SelectedItem?.ToString() ?? "Nightly"; _settings.Save();
            await _setup.InstallReShade(game, prefs, Progress); await ReadStates(); ShowGame(); dialog.Close();
            _status.Text = "ReShade " + prefs.Channel + " installed and saved for " + game.Name;
        }));
        await dialog.ShowDialog(this);
    }
    private async Task ShowHdrSettings(Game game)
    {
        var body = new StackPanel { Spacing = 14 };
        var mod = _setup.Mod(game, _settings.For(game));
        var extended = mod?.SnapshotUrl?.Contains("ue-extended", StringComparison.OrdinalIgnoreCase) == true;
        body.Children.Add(Label(State(game).HdrConfigured ? "✓ HDR settings applied" : "Game HDR settings", 14, State(game).HdrConfigured ? Green : Secondary, true));
        body.Children.Add(Label("Enable HDR on your display in Bazzite's display settings. RHI can apply the UE Extended settings required by supported games, keeping a backup for Restore. Use Home in the game to adjust RenoDX brightness.", 13, Secondary));
        var inis = IniSettings.FindEngineInis(game);
        var dialog = Dialog("RenoDX HDR settings", body);
        body.Children.Add(DialogAction("HDR launch options…", body, async () => await new HdrLaunchWindow(game).ShowDialog(dialog)));
        if (extended && inis.Count == 1)
        {
            body.Children.Add(DialogAction("Apply recommended HDR settings", body, async () => { GameSetup.ApplyHdr(game, inis[0]); await ReadStates(); ShowGame(); dialog.Close(); }));
        }
        else body.Children.Add(Label(extended ? "Launch the game once to create its settings. If there are multiple configurations, choose the correct one in Advanced settings." : "Follow this mod's game-specific instructions. The general UE Extended recipe does not apply to every mod.", 12, Muted));
        if (State(game).HdrConfigured) body.Children.Add(DialogAction("Restore previous HDR settings", body, async () => { GameSetup.RestoreHdr(game); await ReadStates(); ShowGame(); dialog.Close(); }));
        var instructions = Plain("Mod instructions ↗", () => Proton.Open(mod?.NameUrl ?? mod?.NexusUrl ?? "https://github.com/clshortfuse/renodx/wiki/Mods")); body.Children.Add(instructions);
        await dialog.ShowDialog(this);
    }
    private async Task ShowSteamSetup(Game game)
    {
        var body = new StackPanel { Spacing = 14 };
        var configs = Proton.LocalConfigs(game).ToList();
        var dialog = Dialog("Finish Steam setup", body);
        var extras = Extras(game);
        string? Proxy() => new Installation(game.InstallDirectory).ReadState().Proxy;
        if (configs.Count == 0)
        {
            body.Children.Add(Label("Open this game in Steam once, then refresh RHI. For games from another launcher, add these settings to that launcher's environment variables / launch options:", 13, Secondary));
            var options = new TextBox { Text = Proton.LaunchOptions("%command%", Proxy(), extras), IsReadOnly = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Name = "LauncherOptions" };
            body.Children.Add(options);
            body.Children.Add(DialogAction("Copy settings", body, async () => { if (Clipboard != null) await Clipboard.SetTextAsync(options.Text ?? ""); _status.Text = "Launch settings copied."; }));
        }
        else
        {
            body.Children.Add(Label("RHI will save the required launch settings for " + game.Name + ". Existing launch options are kept and backed up.", 13, Secondary));
            body.Children.Add(Label(Proton.SteamRunning() ? "Close any running games. Steam will briefly close and reopen to apply this setup." : "Steam is closed, so the settings can be applied now.", 12, Amber));
            var picker = new ComboBox { ItemsSource = configs.Select(c => "Steam account " + Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(c)))).ToList(), SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
            if (_settings.For(game).SteamConfig is { } saved && configs.Contains(saved)) picker.SelectedIndex = configs.IndexOf(saved);
            if (configs.Count > 1) { body.Children.Add(Label("Choose your Steam account", 12, Secondary)); body.Children.Add(picker); }
            var progress = Label("", 12, Teal); body.Children.Add(progress);
            body.Children.Add(DialogAction(Proton.SteamRunning() ? "Apply & restart Steam" : "Apply Steam setup", body, async () =>
            {
                await GameSetup.ConfigureSteam(game, configs[picker.SelectedIndex], new Progress<string>(s => progress.Text = s), extras);
                _settings.For(game).SteamConfig = configs[picker.SelectedIndex]; _settings.Save();
                await ReadStates(); ShowGame(); _status.Text = "Steam setup applied. Ready to launch."; dialog.Close();
            }, "success"));
            var copy = DialogAction("Copy settings instead", body, async () =>
            {
                var proxy = Proxy() ?? (State(game).Get("ReShade").Installed ? Installation.ProxyFor(_setup.Api(game, _settings.For(game))) : null);
                var options = Proton.LaunchOptions(Proton.ReadOptions(configs[picker.SelectedIndex], game.AppId!) ?? "", proxy, extras);
                if (Clipboard != null) await Clipboard.SetTextAsync(options);
                progress.Text = "Copied. In Steam, open the game's Properties → General → Launch Options and paste. RHI checks this automatically.";
            });
            body.Children.Add(copy);
        }
        await dialog.ShowDialog(this);
    }
    private async Task ShowShaders()
    {
        if (Selected is not { } game) return;
        var body = new StackPanel { Spacing = 14, Children = { Label("Shaders and addons for " + game.Name, 13, Secondary), Label("Shader packs are optional. After installation, press Home in the game to choose effects.", 12, Muted) } };
        foreach (var pack in new[] { "Standard", "Lilium HDR" }) body.Children.Add(DialogAction("Install / update " + pack, body, () => InstallShaders(game, pack)));
        body.Children.Add(DialogAction("Install a custom addon…", body, async () =>
        {
            GameSetup.RequireClosed(game);
            var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "Choose a ReShade addon", AllowMultiple = false, FileTypeFilter = [new("ReShade addons") { Patterns = ["*.addon64", "*.addon32"] }] });
            if (files.FirstOrDefault()?.TryGetLocalPath() is not { } file) return;
            Downloads.ValidatePe(file, game.Architecture);
            var suffix = game.Architecture == MachineType.I386 ? ".addon32" : ".addon64";
            if (!file.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) throw new IOException("Choose the " + suffix + " version for this game.");
            if (!InstallationStatus.Read(game).Get("ReShade").Installed) await _setup.InstallReShade(game, _settings.For(game), Progress);
            GameSetup.RequireClosed(game);
            var component = Path.GetFileName(file).StartsWith("renodx-", StringComparison.OrdinalIgnoreCase) ? "RenoDX" : "Addon: " + Path.GetFileName(file);
            await Task.Run(() => new Installation(game.InstallDirectory).Install(component, "Local", [new(Path.GetFileName(file), File.ReadAllBytes(file))]));
            await Changed(game, "Custom addon installed", offerSteam: false);
        }));
        await Dialog("Shaders / Addons", body).ShowDialog(this);
    }
    private async Task ShowLinks()
    {
        var body = new StackPanel { Spacing = 10 };
        foreach (var (label, url) in new[] { ("RenoDX mods & instructions", "https://github.com/clshortfuse/renodx/wiki/Mods"), ("ReShade", "https://reshade.me"), ("RHI on GitHub", "https://github.com/RankFTW/RHI") })
            body.Children.Add(Plain(label + " ↗", () => Proton.Open(url)));
        await Dialog("Links", body).ShowDialog(this);
    }
    private Task ShowHelp() => Message("Quick Start", "1. Select your game in the library.\n\n2. Choose Install recommended. RHI picks the executable, installs ReShade and the matching RenoDX mod, and applies supported game settings.\n\n3. If shown, choose Finish Steam setup. RHI can save the launch settings and restart Steam for you.\n\n4. Choose Launch. Press Home in the game to adjust RenoDX or enable optional shader effects.\n\nGreen Installed means the files are verified. Applied means the current plugins were loaded at the last recorded launch. Confirm the game renders correctly too.\n\nNightly is the recommended ReShade channel for Proton. It includes newer DirectX 12 compatibility fixes for affected games.");
    private async Task ShowSettings()
    {
        var body = new StackPanel { Spacing = 14, Children = { Label("Steam libraries are detected automatically. Add an external library if a game is missing.", 13, Secondary) } };
        body.Children.Add(DialogAction("Add Steam library…", body, AddLibrary));
        body.Children.Add(DialogAction("Add Windows game…", body, AddGame));
        body.Children.Add(Label("DLSS", 13, null, true));
        body.Children.Add(Label("Default DLSS versions and presets used by Quick Apply in the Nvidia Profile Overrides section. " + _dlss.Status + " " + _releases.Status, 12, Muted));
        body.Children.Add(DialogAction("DLSS defaults…", body, ShowDlssDefaults));
        body.Children.Add(Plain("Open custom DLSS folder", () => { Directory.CreateDirectory(DlssFiles.CustomDirectory); Directory.CreateDirectory(DlssFiles.CustomStreamlineDirectory); Proton.Open(Path.GetDirectoryName(DlssFiles.CustomDirectory)!); }));
        body.Children.Add(Label("OptiScaler", 13, null, true));
        body.Children.Add(Label("Used for every OptiScaler install. " + _os.Status, 12, Muted));
        body.Children.Add(OptiScalerGlobalFields());
        body.Children.Add(Plain("Open RHI data folder", () => Proton.Open(LinuxPaths.Data)));
        body.Children.Add(Plain("Linux guide", () => Proton.Open(Path.Combine(AppContext.BaseDirectory, "LINUX.md"))));
        await Dialog("Settings", body).ShowDialog(this);
    }
    private async Task OpenAdvanced()
    {
        _settings.Save();
        await new AdvancedWindow().ShowDialog(this);
        _settings = Settings.Load(); await Scan();
    }
    private async Task AddGame()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "Choose the game's Windows executable", AllowMultiple = false, FileTypeFilter = [new("Windows game") { Patterns = ["*.exe", "*.EXE"] }] });
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } exe) return;
        Downloads.RequireArchitecture(new PeHeaderService().DetectArchitecture(exe));
        var game = new Game { Name = Path.GetFileNameWithoutExtension(exe), Root = LinuxPaths.Canonical(Path.GetDirectoryName(exe)!), Executable = exe };
        if (_settings.ManualGames.All(g => g.Id != game.Id)) _settings.ManualGames.Add(game);
        _settings.For(game).Executable = exe; _settings.LastGameId = game.Id; _settings.Save(); await Scan();
    }
    private async Task AddLibrary()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new() { Title = "Choose a Steam library", AllowMultiple = false });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } folder) return;
        if (Path.GetFileName(folder) == "steamapps") folder = Path.GetDirectoryName(folder)!;
        if (!Directory.Exists(Path.Combine(folder, "steamapps"))) throw new IOException("Choose the Steam library folder containing steamapps.");
        _settings.SteamRoots.Add(LinuxPaths.Canonical(folder)); _settings.SteamRoots = _settings.SteamRoots.Distinct().ToList(); _settings.Save(); await Scan();
    }
}
