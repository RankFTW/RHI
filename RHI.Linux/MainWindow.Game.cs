using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using RHI.Linux.Core;

namespace RHI.Linux;

public sealed partial class MainWindow
{
    private void ShowGame()
    {
        if (Selected is not { } game) return;
        _settings.LastGameId = game.Id; _settings.Save();
        var prefs = _settings.For(game); var state = State(game); var mod = _setup.Mod(game, prefs);
        _details.Children.Clear();
        var title = new Grid { ColumnDefinitions = new("*,Auto") };
        title.Children.Add(Label(game.Name, 20, null, true));
        if (!string.IsNullOrWhiteSpace(mod?.Maintainer)) { var author = Badge(mod.Maintainer); Grid.SetColumn(author, 1); title.Children.Add(author); }
        var heading = new StackPanel { Spacing = 3, Children = { title } };
        var path = Label(game.Root, 10, Muted); path.TextTrimming = TextTrimming.CharacterEllipsis; path.TextWrapping = TextWrapping.NoWrap;
        ToolTip.SetTip(path, game.Root); heading.Children.Add(path); _details.Children.Add(heading);

        var info = new StackPanel { Spacing = 10 };
        var actions = new Grid { ColumnDefinitions = new("Auto,*,Auto") };
        var launch = Action("▶ Launch", async () =>
        {
            if (game.AppId == null) { await Message("Launch your game", "Start this game using its usual launcher. RHI has installed the plugins beside its executable."); return; }
            if (State(game).Get("ReShade").Installed && !State(game).LaunchConfigured || !State(game).DlssLaunchConfigured) { await ShowSteamSetup(game); return; }
            Proton.Open("steam://rungameid/" + game.AppId);
        }, "success", "LaunchGame");
        actions.Children.Add(launch);
        var shortcuts = Row(Action("Mod instructions", () => { Proton.Open(mod?.NameUrl ?? mod?.NexusUrl ?? "https://github.com/clshortfuse/renodx/wiki/Mods"); return Task.CompletedTask; }),
            Action(prefs.Favourite ? "★ Favourite" : "Favourite", () => { prefs.Favourite = !prefs.Favourite; _settings.Save(); Filter(); return Task.CompletedTask; }),
            Action(prefs.Hidden ? "Unhide" : "Hide", () => { prefs.Hidden = !prefs.Hidden; _settings.Save(); Filter(); return Task.CompletedTask; }),
            Action("Browse", () => { Proton.Open(game.Root); return Task.CompletedTask; }));
        Grid.SetColumn(shortcuts, 2); actions.Children.Add(shortcuts); info.Children.Add(actions);
        var badges = new WrapPanel { Orientation = Orientation.Horizontal };
        var names = new List<string> { game.Source, "Proton" };
        if (Directory.Exists(Path.Combine(game.Root, "Engine"))) names.Add("Unreal Engine");
        if (game.IsREEngine) names.Add("RE Engine");
        if (game.Executable != null) { names.Add(_setup.Api(game, prefs).ToString().Replace("DirectX", "DX")); names.Add(game.Architecture == RenoDXCommander.Services.MachineType.I386 ? "32-bit" : "64-bit"); }
        if (state.State.Files.FirstOrDefault(f => f.Component == "RenoDX") is { } addon) names.Insert(0, addon.Path);
        foreach (var name in names) { var badge = Badge(name); badge.Margin = new Thickness(0, 0, 6, 2); badges.Children.Add(badge); }
        info.Children.Add(badges); _details.Children.Add(Card(info));

        if (state.Error != null) _details.Children.Add(Card(Label("Could not check the installation: " + state.Error, 12, Amber)));
        var rs = state.Get("ReShade"); var rdx = state.Get("RenoDX"); var re = RefStatus(game, state);
        // RE Engine games need RE Framework before ReShade, as in the Windows app.
        var needsRef = game.IsREEngine && !re.Installed;
        var hdrRequired = mod?.SnapshotUrl?.Contains("ue-extended") == true && mod.Notes?.Contains("Engine.ini", StringComparison.OrdinalIgnoreCase) == true;
        var ready = rs.Installed && state.LaunchConfigured && (mod == null || rdx.Installed) && (!hdrRequired || state.HdrConfigured) && !needsRef;
        var damaged = rs.Damaged || rdx.Damaged || re.Damaged;
        var channelChanged = rs.Installed && rs.Channel != prefs.Channel;
        var summary = new Grid { ColumnDefinitions = new("*,Auto") };
        string summaryTitle, summaryDetail;
        if (damaged) { summaryTitle = "Installation needs attention"; summaryDetail = "Some installed files are missing or changed. Open Advanced settings to review them."; }
        else if (ready) { summaryTitle = "✓  Ready to launch"; summaryDetail = rs.Applied && (mod == null || rdx.Applied) ? "ReShade and installed addons were loaded on the last launch." : "Your plugins and Steam setup are installed. Launch the game to apply them."; }
        else if (rs.Installed && !state.LaunchConfigured) { summaryTitle = "One step left: Steam setup"; summaryDetail = "Your plugins are installed. Let RHI finish the launch setup for you."; }
        else if (hdrRequired && rdx.Installed && !state.HdrConfigured) { summaryTitle = "HDR setup needed"; summaryDetail = "Apply the game's HDR settings to finish setup. Launch the game once first if its settings are missing."; }
        else { summaryTitle = "Ready to install"; summaryDetail = (needsRef ? "RHI will install RE Framework, " : "RHI will install ") + (mod == null ? "ReShade. No matching RenoDX mod is selected." : "ReShade, the matching RenoDX mod and the required game settings."); }
        summary.Children.Add(new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 14, 0), Children = { Label(summaryTitle, 13, ready ? Green : Amber, true), Label(summaryDetail, 11, Secondary) } });
        if (!ready && game.Executable != null)
        {
            var finish = Action(damaged ? "Review installation" : rs.Installed && !state.LaunchConfigured && !needsRef ? "Finish Steam setup" : "Install recommended", async () =>
            {
                if (damaged) await OpenAdvanced();
                else if (rs.Installed && !state.LaunchConfigured && !needsRef) await ShowSteamSetup(game); else await InstallRecommended(game);
            }, "success", "InstallRecommended");
            Grid.SetColumn(finish, 1); summary.Children.Add(finish);
        }
        _details.Children.Add(Card(summary));
        if (game.Executable == null) { _details.Children.Add(Label("Choose the game's Windows executable in Advanced settings to install plugins.", 13, Secondary)); _details.Children.Add(Action("Advanced settings", OpenAdvanced)); return; }

        var table = new StackPanel { Spacing = 10, Children = { Label("Components", 13, null, true) } };
        if (game.IsREEngine)
        {
            var update = _ref.UpdateAvailable(re);
            table.Children.Add(ComponentRow(game, re, "RE Framework", update ? "⬆  Update RE Framework" : re.Installed ? "↻  Reinstall RE Framework" : "↓  Install RE Framework",
                async () => { await _ref.Install(game, Progress); await Changed(game, "RE Framework installed"); }, null,
                REFramework.Description + " RHI adds the dinput8 DLL override Proton needs to load it." + (update ? $"\n\nUpdate available: nightly {_ref.Latest!.Version}." : ""), actionStyle: update ? "update" : "action"));
        }
        table.Children.Add(ComponentRow(game, rs, "ReShade", channelChanged ? "Apply " + prefs.Channel + " channel" : rs.Installed ? "↻  Reinstall ReShade" : "↓  Install ReShade",
            async () => { await _setup.InstallReShade(game, prefs, Progress); _settings.Save(); await Changed(game, "ReShade installed"); },
            () => ShowReShadeSettings(game),
            "ReShade loads RenoDX and shader effects into the game. Applied means the current installed files were loaded on the last recorded game launch; it is not a display-calibration or gameplay test." + (game.IsREEngine ? " RE Engine games need RE Framework installed first." : ""),
            !needsRef || rs.Installed, "⚠  RE Framework required"));
        table.Children.Add(ComponentRow(game, rdx, "RenoDX HDR", rdx.Installed ? "↻  Reinstall RenoDX" : "↓  Install RenoDX",
            async () => { await _setup.InstallRenoDx(game, prefs, Progress); _settings.Save(); await Changed(game, "RenoDX installed"); },
            () => ShowHdrSettings(game), "RenoDX is the game-specific HDR mod. Installing it also installs ReShade if needed, and applies supported game settings automatically.", mod != null));
        if (!_compact)
        {
            var optional = Label("────────  Optional shaders  ────────", 11, Muted); optional.HorizontalAlignment = HorizontalAlignment.Center; optional.Margin = new Thickness(0, 3); table.Children.Add(optional);
            foreach (var pack in new[] { "Standard", "Lilium HDR" })
            {
                var component = state.Get("Shaders: " + pack);
                table.Children.Add(ComponentRow(game, component, pack, component.Installed ? "↻  Reinstall shaders" : "↓  Install shaders", () => InstallShaders(game, pack),
                    () => ShowShaders(), "Optional ReShade effects. Open ReShade with Home in the game to choose which effects to enable; installation does not enable every effect."));
            }
        }
        var applied = Label("✓ Installed = files verified    ·    Applied = loaded at the last recorded game launch", 10, Muted); applied.Margin = new Thickness(0, 3, 0, 0); table.Children.Add(applied);
        _details.Children.Add(Card(table));

        var overrides = new StackPanel { Spacing = 10 };
        overrides.Children.Add(Label("Game overrides", 13, null, true));
        var config = new Grid { ColumnDefinitions = new("*,Auto") };
        var channelLine = Row(Label("ReShade channel", 12, Secondary), Badge(prefs.Channel, good: rs.Channel == prefs.Channel, warning: channelChanged));
        var channels = new StackPanel { Spacing = 6, Children = { channelLine, Label(channelChanged ? $"{rs.Channel} is still installed. Apply the channel change above." : prefs.Channel == "Nightly" ? "Nightly includes newer DirectX 12 compatibility fixes for Proton." : "Nightly is recommended with current Proton versions.", 11, channelChanged ? Amber : Muted) } };
        config.Children.Add(channels);
        var settings = Action("⚙  Change", () => ShowReShadeSettings(game), "", "ReShadeSettings"); Grid.SetColumn(settings, 1); config.Children.Add(settings); overrides.Children.Add(config);
        var setup = Row(Badge(state.LaunchConfigured ? "✓ Steam setup applied" : "Steam setup needed", state.LaunchConfigured, !state.LaunchConfigured));
        if (hdrRequired || state.HdrConfigured) setup.Children.Add(Badge(state.HdrConfigured ? "✓ HDR settings applied" : "HDR settings needed", state.HdrConfigured, !state.HdrConfigured));
        overrides.Children.Add(setup); _details.Children.Add(Card(overrides));
        _details.Children.Add(NeuralRenderingSection(game, state));
        _details.Children.Add(NvidiaProfileSection(game, state));
        _details.Children.Add(ExtrasSection(game, state));
        var advanced = Action("Advanced settings  ›", OpenAdvanced, "", "AdvancedSettings"); advanced.HorizontalAlignment = HorizontalAlignment.Left;
        ToolTip.SetTip(advanced, "Executable, Proton prefix, manual mods, launch options and recovery"); _details.Children.Add(advanced);
    }

    // Windows-installed RE Framework has no Linux record; show it so it can be replaced or removed.
    private static ComponentStatus RefStatus(Game game, InstallationStatus state)
    {
        var status = state.Get(REFramework.Component);
        return status.Version == null && REFramework.FromWindows(game) ? new(REFramework.Component, "Windows RHI", true, false, false) : status;
    }

    private Control ComponentRow(Game game, ComponentStatus status, string name, string action, Func<Task> install, Func<Task>? settings, string description,
        bool available = true, string unavailable = "No matching mod", string actionStyle = "action")
    {
        var row = new Grid { ColumnDefinitions = new("112,112,42,*,42,36"), MinHeight = 48 };
        row.Children.Add(Label(name, 12, Secondary));
        var values = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0) };
        values.Children.Add(Label(status.Installed ? "✓ Installed" : status.Label, 11, status.Damaged ? Amber : status.Installed ? Green : Muted));
        if (status.Version != null)
        {
            var version = status.Name == "ReShade" ? status.Channel == "Nightly" ? "Nightly" : status.Version : status.Version.Split(" • ").Last();
            if (status.Applied && status.Name == "RenoDX" && DateTime.TryParse(version, out var date)) version = date.ToString("dd MMM");
            values.Children.Add(Label((status.Applied ? "Applied · " : "") + version, 10, status.Installed ? Green : Muted));
        }
        ToolTip.SetTip(values, status.Version ?? "Not installed"); Grid.SetColumn(values, 1); row.Children.Add(values);
        var info = Action("Info", () => Message(name, description + (status.Version == null ? "" : "\n\nInstalled: " + status.Version)), "", "Info" + status.Name.Replace(" ", ""));
        info.Padding = new Thickness(4); info.Margin = new Thickness(2, 0); Grid.SetColumn(info, 2); row.Children.Add(info);
        var main = Action(available ? action : unavailable, install, actionStyle, "Install" + status.Name.Replace(" ", ""));
        main.IsEnabled = available; main.HorizontalAlignment = HorizontalAlignment.Stretch; main.Margin = new Thickness(4, 0); Grid.SetColumn(main, 3); row.Children.Add(main);
        if (settings != null) { var cog = Action("⚙", settings); cog.Width = 36; cog.FontSize = 15; cog.Padding = new Thickness(4); cog.Margin = new Thickness(2, 0); ToolTip.SetTip(cog, name + " settings"); Grid.SetColumn(cog, 4); row.Children.Add(cog); }
        if (status.Version != null)
        {
            var remove = Action("×", () => Remove(game, status.Name), "danger", "Remove" + status.Name.Replace(" ", "")); remove.Width = 36; remove.FontSize = 16; remove.Padding = new Thickness(4);
            ToolTip.SetTip(remove, "Remove " + name + " and restore backed-up files"); Grid.SetColumn(remove, 5); row.Children.Add(remove);
        }
        return row;
    }
    private async Task Changed(Game game, string message, bool offerSteam = true)
    {
        await ReadStates(); Filter(); _status.Text = message;
        if (offerSteam && (State(game).Get("ReShade").Installed && !State(game).LaunchConfigured || !State(game).DlssLaunchConfigured)) await ShowSteamSetup(game);
    }
    private async Task InstallRecommended(Game game)
    {
        var prefs = _settings.For(game); var state = State(game);
        if (game.IsREEngine && !RefStatus(game, state).Installed) await _ref.Install(game, Progress);
        if (!state.Get("ReShade").Installed || state.Get("ReShade").Channel != prefs.Channel) await _setup.InstallReShade(game, prefs, Progress);
        if (_setup.Mod(game, prefs) != null) await _setup.InstallRenoDx(game, prefs, Progress);
        _settings.Save(); await Changed(game, "Recommended setup installed");
    }
    private async Task InstallShaders(Game game, string pack)
    {
        GameSetup.RequireClosed(game);
        var payload = await _downloads.Shaders(pack, Progress);
        if (!InstallationStatus.Read(game).Get("ReShade").Installed) await _setup.InstallReShade(game, _settings.For(game), Progress);
        GameSetup.RequireClosed(game);
        await Task.Run(() => new Installation(game.InstallDirectory).Install("Shaders: " + pack, DateTime.UtcNow.ToString("yyyy-MM-dd"), payload));
        await Changed(game, pack + " shaders installed", offerSteam: false);
    }
    private async Task Remove(Game game, string component)
    {
        if (!await Confirm("Remove " + component + "?", "RHI will remove its installed files and restore any originals it backed up. Your saves and unrelated game settings are kept.", "Remove")) return;
        GameSetup.RequireClosed(game);
        if (component is "RenoDX" or "ReShade") GameSetup.RestoreHdr(game);
        if (component == REFramework.Component) await REFramework.Remove(game);
        else if (component == OptiScaler.Component) { await OptiScaler.Remove(game); ClearOptiScalerPreferences(_settings.For(game)); _settings.Save(); InvalidateDlss(game); }
        else await Task.Run(() => new Installation(game.InstallDirectory).Remove(component));
        await Changed(game, component + " removed", offerSteam: false);
    }
    private async Task UpdateAll()
    {
        if (GameSetup.AnySteamGameRunning()) throw new IOException("Close running Steam games before updating their plugins.");
        await RefreshDlssCatalogs();
        var installed = _games.Where(g => State(g).Components.Count > 0).ToList();
        if (installed.Count == 0) { await Message("Update All", "There are no installed plugins to update yet. Select a game and choose Install recommended."); return; }
        foreach (var game in installed)
        {
            var state = State(game); var prefs = _settings.For(game);
            if (state.Get("ReShade").Version is { } rs && rs != "Local") await _setup.InstallReShade(game, prefs, Progress);
            if (state.Get("RenoDX").Version is { } rdx && rdx != "Local") await _setup.InstallRenoDx(game, prefs, Progress);
            if (_ref.UpdateAvailable(state.Get(REFramework.Component))) await _ref.Install(game, Progress);
            if (_os.UpdateAvailable(game) && OptiScaler.Record(game)?.Variant == OsVariant.Of(prefs))
            { await _os.Install(game, prefs, _settings.OptiScaler, _setup.Api(game, prefs), Progress); InvalidateDlss(game); }
            // Neural Rendering set to "Latest" follows new releases, as the Windows app auto-updates it.
            if (NeuralRenderingSetup.LoadRecord(game.InstallDirectory) is { } nr && prefs.NrAddonVersion == null && prefs.NrPackVersion == null && prefs.NrDllVersion == null)
            { await _nr.Install(game, prefs, nr.Method, _setup.Api(game, prefs), prefs.Channel, Progress); InvalidateDlss(game); }
        }
        _settings.Save(); await ReadStates(); Filter(); _status.Text = "Installed RE Framework, ReShade, RenoDX, Neural Rendering and OptiScaler components updated.";
    }
}
