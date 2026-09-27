using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using RHI.Linux.Core;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RHI.Linux;

// Neural Rendering (DLSS 5) and Nvidia Profile Overrides sections, laid out like the Windows
// detail panel. Windows driver-profile values are applied through dxvk-nvapi launch options.
public sealed partial class MainWindow
{
    private readonly Dictionary<string, NrState> _nrStates = [];
    private readonly Dictionary<string, int> _nrGenerations = [];
    private readonly HashSet<string> _nrScanning = [];
    private static readonly IBrush Blue = Brush("#7AACDD"), Red = Brush("#E07070"), Overlay = Brush("#1E242C");
    private static readonly string? DriverVersion = ReadDriverVersion();

    private static string? ReadDriverVersion()
    {
        try { return File.Exists("/sys/module/nvidia/version") ? File.ReadAllText("/sys/module/nvidia/version").Trim() : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private void InvalidateDlss(Game game)
    {
        _nrStates.Remove(game.Id);
        _nrGenerations[game.Id] = _nrGenerations.GetValueOrDefault(game.Id) + 1;
    }

    // DLSS detection walks the whole game folder, so it runs off the UI thread and is cached.
    private NrState? DlssState(Game game)
    {
        if (_nrStates.TryGetValue(game.Id, out var cached)) return cached;
        var generation = _nrGenerations.GetValueOrDefault(game.Id);
        if (!_nrScanning.Add(game.Id + "#" + generation)) return null;
        _ = Task.Run(() => NeuralRenderingSetup.Read(game)).ContinueWith(t => Dispatcher.UIThread.Post(() =>
        {
            _nrScanning.Remove(game.Id + "#" + generation);
            if (t.IsFaulted) { CrashReporter.Log(t.Exception!.ToString()); return; }
            if (_nrGenerations.GetValueOrDefault(game.Id) != generation || _nrStates.ContainsKey(game.Id)) return;
            _nrStates[game.Id] = t.Result;
            if (Selected?.Id == game.Id) ShowGame();
        }));
        return null;
    }

    private LaunchExtras Extras(Game game) => GameLaunch.Extras(game, _settings.For(game));

    private async Task RefreshDlssCatalogs()
    {
        try { await _dlss.Refresh(); } catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or System.Text.Json.JsonException) { CrashReporter.Log("DLSS list: " + ex.Message); }
        try { await _releases.Refresh(); } catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException) { CrashReporter.Log("NR releases: " + ex.Message); }
        if (_games.Any(g => g.IsREEngine))
            try { await _ref.Refresh(); } catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or System.Text.Json.JsonException) { CrashReporter.Log("RE Framework releases: " + ex.Message); }
        if (_states.Values.Any(s => s.Components.ContainsKey(OptiScaler.Component)))
            try { await _os.Refresh(); } catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or System.Text.Json.JsonException) { CrashReporter.Log("OptiScaler releases: " + ex.Message); }
    }

    // ── Shared building blocks ───────────────────────────────────────────────
    private Border Section(string key, string title, string? summary, Control body, string? name = null)
    {
        var collapsed = _settings.CollapsedSections.Contains(key);
        var arrow = Label(collapsed ? "▶" : "▼", 10, Muted); arrow.Margin = new Thickness(0, 0, 6, 0);
        var heading = Label(title, 13, null, true); heading.TextWrapping = TextWrapping.NoWrap;
        var hint = Label(summary ?? "", 11, Muted); hint.Margin = new Thickness(12, 0, 0, 0); hint.TextTrimming = TextTrimming.CharacterEllipsis; hint.TextWrapping = TextWrapping.NoWrap;
        hint.IsVisible = collapsed && !string.IsNullOrEmpty(summary);
        var header = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.Transparent, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand), Children = { arrow, heading, hint }, Name = name == null ? null : name + "Header" };
        body.IsVisible = !collapsed; body.Margin = new Thickness(0, 10, 0, 0);
        header.PointerEntered += (_, _) => heading.Foreground = Teal;
        header.PointerExited += (_, _) => heading.Foreground = Brush("#E8ECF2");
        header.PointerPressed += (_, _) =>
        {
            var now = body.IsVisible; body.IsVisible = !now; arrow.Text = now ? "▶" : "▼"; hint.IsVisible = now && !string.IsNullOrEmpty(summary);
            if (now) _settings.CollapsedSections.Add(key); else _settings.CollapsedSections.Remove(key);
            _settings.Save();
        };
        var card = Card(new StackPanel { Children = { header, body } }); card.Name = name; return card;
    }
    private static StackPanel Field(string label, Control control, string? tip = null)
    {
        var field = new StackPanel { Spacing = 3, Children = { Label(label, 10, Muted), control } };
        if (tip != null) ToolTip.SetTip(field, tip);
        return field;
    }
    private static ComboBox Combo(IEnumerable<object> items, int selected, string? name = null)
    {
        var combo = new ComboBox { ItemsSource = items.ToList(), SelectedIndex = selected, FontSize = 11, MinHeight = 30, HorizontalAlignment = HorizontalAlignment.Stretch, Name = name };
        return combo;
    }
    private static Button Link(string text, string url, IBrush color, double size = 10)
    {
        var b = new Button { Content = text, Foreground = color, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), MinHeight = 0, FontSize = size };
        b.Classes.Add("link"); b.Click += (_, _) => Proton.Open(url); ToolTip.SetTip(b, url); return b;
    }
    private static string LatestLabel(string? version) => string.IsNullOrEmpty(version) ? "Latest" : $"Latest ({version})";
    private static int IndexOf(IReadOnlyList<string> items, string? value) => value == null ? 0 : Math.Max(0, items.ToList().FindIndex(i => i.Equals(value, StringComparison.OrdinalIgnoreCase)));
    private static string? Pick(ComboBox combo) => combo.SelectedItem as string is { } s && !s.StartsWith("Latest") ? s : null;

    // ── Neural Rendering ─────────────────────────────────────────────────────
    private Control NeuralRenderingSection(Game game, InstallationStatus state)
    {
        var prefs = _settings.For(game);
        var nr = DlssState(game);
        if (nr == null) return Section("NeuralRendering", "Neural Rendering", null, Label("Checking this game's DLSS files…", 11, Muted), "NeuralRendering");
        var is32 = game.Architecture == MachineType.I386; var api = _setup.Api(game, prefs); var hasDlss = nr.Detection.HasAny;
        var installed = nr.Method;
        var method = prefs.NrMethod is { } saved && NrMethod.All.Contains(saved) ? saved : installed ?? NrMethod.Recommended(is32, api, hasDlss);
        var body = new StackPanel { Spacing = 6 };

        // Row 1: Method / [Feeder or Bridge version] / DLSS5 Tool or SF version / NR DLL version
        var methodItems = NrMethod.All.Select(m =>
        {
            var enabled = NrMethod.Available(m, is32, api, hasDlss);
            return (object)new ComboBoxItem { Content = NrMethod.Name(m), IsEnabled = enabled, Opacity = enabled ? 1 : 0.4, Tag = m };
        }).ToList();
        var methodCombo = Combo(methodItems, Array.IndexOf(NrMethod.All, method), "NrMethod");
        var packType = method == NrMethod.Feeder ? AddonReleases.Feeder : AddonReleases.Bridge;
        var packVersions = _releases.Versions(packType).ToList();
        if (prefs.NrPackVersion != null && !packVersions.Contains(prefs.NrPackVersion)) packVersions.Add(prefs.NrPackVersion);
        var packItems = packVersions.Prepend(LatestLabel(_releases.Latest(packType))).ToList();
        var packCombo = Combo(packItems, IndexOf(packItems, prefs.NrPackVersion), "NrPackVersion");
        var addonType = method == NrMethod.ShortFuse ? AddonReleases.ShortFuse : AddonReleases.Dlss5Tool;
        var addonVersions = _releases.Versions(addonType).ToList();
        if (prefs.NrAddonVersion != null && !addonVersions.Contains(prefs.NrAddonVersion)) addonVersions.Add(prefs.NrAddonVersion);
        var addonItems = addonVersions.Prepend(LatestLabel(_releases.Latest(addonType))).ToList();
        var addonCombo = Combo(addonItems, IndexOf(addonItems, prefs.NrAddonVersion), "NrAddonVersion");
        var nrItems = _dlss.Versions(DlssKind.NR).Prepend(LatestLabel(_dlss.Latest(DlssKind.NR))).ToList();
        var nrCombo = Combo(nrItems, IndexOf(nrItems, prefs.NrDllVersion), "NrDllVersion");
        var row = new Grid { ColumnDefinitions = new("*,8,*,8,*,8,*") };
        void Place(Control c, int column) { Grid.SetColumn(c, column); row.Children.Add(c); }
        Place(Field("Method", methodCombo, "DLSS5 Tool: for DX12 native-DLSS games.\nDLSS5 Tool + DX11 Bridge: for DX11/Vulkan native-DLSS games.\nShortFuse DLSS Tool: alternative full-stack install for native-DLSS games.\nDLSS5 Feeder: for games with no native DLSS (DX11, DX12, Vulkan, 32-bit)."), 0);
        if (method is NrMethod.Feeder or NrMethod.Bridge)
            Place(Field(method == NrMethod.Feeder ? "Feeder Version" : "Bridge Version", packCombo, method == NrMethod.Feeder ? "Version of dlss5-feed.addon64 to install." : "Version of dlss5-bridge.addon64 to install."), 2);
        var addonColumn = method is NrMethod.Feeder or NrMethod.Bridge ? 4 : 2;
        Place(Field(method == NrMethod.ShortFuse ? "SF Version" : "DLSS5 Tool Version", addonCombo, method == NrMethod.Feeder
            ? "Version of renodx-dlss5.addon64 deployed as the neural consumer. Changing while installed swaps the file in-place."
            : "Addon version to install. Changing while installed swaps the file in-place without a full reinstall."), addonColumn);
        Place(Field("NR DLL Version", nrCombo, "NR DLL version to deploy. 'Latest' always uses the newest available. Change while installed to swap the NR DLL in-place."), addonColumn + 2);
        body.Children.Add(row);

        // Status line
        var tags = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0), Name = "NrStatus" };
        foreach (var (text, ok) in nr.Tags(method, state.Get("ReShade").Installed))
        { var tag = Label(text, 10, ok ? Green : Muted); tag.Margin = new Thickness(0, 0, 8, 2); tag.TextWrapping = TextWrapping.NoWrap; tags.Children.Add(tag); }
        body.Children.Add(tags);

        // Description panel
        var (linkText, linkUrl) = NrMethod.Link(method);
        body.Children.Add(new Border
        {
            Background = Overlay, CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 6), Margin = new Thickness(0, 4, 0, 0),
            Child = new StackPanel { Spacing = 4, Children = { Label(NrMethod.Description(method, hasDlss, is32), 11, Secondary), Link(linkText, linkUrl, Blue, 11) } }
        });

        // Install / Remove / ShortFuse settings
        var methodInstalled = nr.Installed(method);
        var buttons = new Grid { ColumnDefinitions = new("*,Auto,Auto"), Margin = new Thickness(0, 8, 0, 0) };
        var install = Action(method == NrMethod.Feeder ? "Install Feeder Addon" : methodInstalled ? "Reinstall" : "Install Neural Rendering",
            () => InstallNeuralRendering(game, method), method != NrMethod.Feeder && methodInstalled ? "" : "action", "InstallNeuralRendering");
        install.HorizontalAlignment = HorizontalAlignment.Stretch; install.MinHeight = 34;
        buttons.Children.Add(install);
        if (methodInstalled || installed != null)
        {
            var remove = Action("Remove", () => RemoveNeuralRendering(game), "danger", "RemoveNeuralRendering");
            remove.MinHeight = 34; remove.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(remove, 1); buttons.Children.Add(remove);
            ToolTip.SetTip(remove, "Remove the installed Neural Rendering files and restore the game's original DLSS DLLs.");
        }
        if (method == NrMethod.ShortFuse)
        {
            var cog = Action("⚙", () => ShortFuseSettings(game)); cog.Width = 34; cog.MinHeight = 34; cog.FontSize = 14; cog.Padding = new Thickness(0); cog.Margin = new Thickness(8, 0, 0, 0);
            ToolTip.SetTip(cog, "ShortFuse DLSS Tool settings — auto-configure ReShade for FrameGen"); Grid.SetColumn(cog, 2); buttons.Children.Add(cog);
        }
        body.Children.Add(buttons);

        // NR Cost Scaler preference (applied at install time, like Windows)
        var costToggle = new ToggleSwitch { IsChecked = prefs.NrCostScaler, OnContent = "On", OffContent = "Off", IsEnabled = !nr.AnyInstalled, Opacity = nr.AnyInstalled ? 0.45 : 1, Name = "NrCostScaler", VerticalAlignment = VerticalAlignment.Center };
        costToggle.IsCheckedChanged += (_, _) => { prefs.NrCostScaler = costToggle.IsChecked == true; _settings.Save(); };
        if (nr.AnyInstalled) ToolTip.SetTip(costToggle, "Remove the installed NR method first, then toggle Cost Scaler On before reinstalling");
        var costLabel = Label("NR Cost Scaler", 12, Secondary);
        ToolTip.SetTip(costLabel, "When On, installing a Neural Rendering method will also deploy the DLSS NR Cost Scaler proxy. Runs the neural model at reduced resolution (default 75%) for significant GPU savings while keeping native detail.");
        body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 4, 0, 0), Children = { costLabel, costToggle, Label(nr.CostScaler ? "Installed" : "", 11, Green) } });
        if (method == NrMethod.ShortFuse)
        { var note = Label("Cost Scaler is built into the ShortFuse addon — this toggle is no longer required but remains available if you prefer the standalone version.", 10, Muted); note.Opacity = 0.8; body.Children.Add(note); }
        if (DriverVersion == null)
            body.Children.Add(Label("No NVIDIA driver was detected. Neural Rendering needs an NVIDIA RTX GPU; the neural model itself currently runs on RTX 50-series only.", 11, Amber));
        body.Children.Add(new WrapPanel
        {
            Margin = new Thickness(0, 4, 0, 0), Children =
            {
                Spaced(Link("DLSS5 Tool →", "https://discord.com/channels/1408098019194310818/1543802634991968366", Muted)),
                Spaced(Link("DX11 Bridge →", "https://github.com/NIGos/dlss5-bridge", Muted)),
                Spaced(Link("ShortFuse →", "https://discord.com/channels/1408098019194310818/1543975158937821315", Muted)),
                Spaced(Link("Feeder →", "https://github.com/jlrouzies-fr/DLSS5-Feeder", Muted)),
            }
        });

        // Handlers
        methodCombo.SelectionChanged += async (_, _) =>
        {
            if ((methodCombo.SelectedItem as ComboBoxItem)?.Tag is not string selected || selected == method) return;
            await Run(async () =>
            {
                // Switching method removes the previous one first, as on Windows.
                if (installed != null && installed != selected) { await _nr.Remove(game); _status.Text = NrMethod.Name(installed) + " removed"; }
                prefs.NrMethod = selected; _settings.Save(); InvalidateDlss(game); await ReadStates(); Filter();
            });
        };
        async Task Persist(Action save)
        {
            save(); _settings.Save();
            // Changing a version while installed swaps it in place.
            if (methodInstalled) await Run(() => SwapNeuralRendering(game, method));
        }
        packCombo.SelectionChanged += async (_, _) => { if (Pick(packCombo) != prefs.NrPackVersion) await Persist(() => prefs.NrPackVersion = Pick(packCombo)); };
        addonCombo.SelectionChanged += async (_, _) => { if (Pick(addonCombo) != prefs.NrAddonVersion) await Persist(() => prefs.NrAddonVersion = Pick(addonCombo)); };
        nrCombo.SelectionChanged += async (_, _) => { if (Pick(nrCombo) != prefs.NrDllVersion) await Persist(() => prefs.NrDllVersion = Pick(nrCombo)); };

        var summary = installed == null && !nr.Detection.Has(DlssKind.NR) ? null
            : string.Join("   ", new[] { installed == null ? null : "NR Method: " + NrMethod.Short(installed), nr.Detection.Has(DlssKind.NR) ? "NR DLL: " + nr.Detection.Version(DlssKind.NR) : null }.Where(s => s != null));
        return Section("NeuralRendering", "Neural Rendering", summary, body, "NeuralRendering");
    }
    private static Control Spaced(Control c) { c.Margin = new Thickness(0, 0, 12, 0); return c; }

    private async Task EnsureReShadeFor(Game game, string method)
    {
        var prefs = _settings.For(game);
        var dx9Feeder = method == NrMethod.Feeder && _setup.Api(game, prefs) == GraphicsApiType.DirectX9;
        var reshade = InstallationStatus.Read(game).Get("ReShade");
        var proxy = new Installation(game.InstallDirectory).ReadState().Proxy;
        if (reshade.Installed && dx9Feeder && proxy?.Equals("d3d9.dll", StringComparison.OrdinalIgnoreCase) == true)
        {
            // dgVoodoo2 takes d3d9.dll, so ReShade has to load as dxgi.dll behind it (as on Windows).
            if (!await Confirm("Reinstall ReShade as dxgi.dll?", "The DX9 Feeder runs this game through dgVoodoo2, which replaces d3d9.dll. RHI will reinstall ReShade as dxgi.dll so both load. Addons such as RenoDX must be removed first.", "Reinstall"))
                throw new OperationCanceledException();
            GameSetup.RequireClosed(game);
            await Task.Run(() => new Installation(game.InstallDirectory).Remove("ReShade"));
            reshade = InstallationStatus.Read(game).Get("ReShade");
        }
        if (!reshade.Installed || reshade.Channel != "Local" && reshade.Channel != prefs.Channel)
            await _setup.InstallReShade(game, prefs, Progress, dx9Feeder ? "dxgi.dll" : proxy);
        // The Feeder shaders include ReShade.fxh from the standard pack.
        if (method == NrMethod.Feeder && !InstallationStatus.Read(game).Get("Shaders: Standard").Installed)
        {
            var payload = await _downloads.Shaders("Standard", Progress);
            GameSetup.RequireClosed(game);
            await Task.Run(() => new Installation(game.InstallDirectory).Install("Shaders: Standard", DateTime.UtcNow.ToString("yyyy-MM-dd"), payload));
        }
    }

    private async Task InstallNeuralRendering(Game game, string method)
    {
        var prefs = _settings.For(game);
        try { await EnsureReShadeFor(game, method); }
        catch (OperationCanceledException) { return; }
        await _nr.Install(game, prefs, method, _setup.Api(game, prefs), prefs.Channel, Progress);
        _settings.Save(); InvalidateDlss(game);
        await Changed(game, NrMethod.Name(method) + " installed");
    }

    private async Task SwapNeuralRendering(Game game, string method)
    {
        var prefs = _settings.For(game);
        await _nr.Install(game, prefs, method, _setup.Api(game, prefs), prefs.Channel, Progress);
        _settings.Save(); InvalidateDlss(game);
        await Changed(game, NrMethod.Name(method) + " updated", offerSteam: false);
    }

    private async Task RemoveNeuralRendering(Game game)
    {
        if (!await Confirm("Remove Neural Rendering?", "RHI will remove the Neural Rendering addons and restore the game's original DLSS DLLs. ReShade and your other components are kept.", "Remove")) return;
        await _nr.Remove(game);
        var prefs = _settings.For(game); prefs.NrMethod = null; prefs.NrCostScaler = false; _settings.Save(); InvalidateDlss(game);
        await Changed(game, "Neural Rendering removed", offerSteam: false);
    }

    private async Task ShortFuseSettings(Game game)
    {
        var prefs = _settings.For(game);
        var toggle = new ToggleSwitch { IsChecked = prefs.SfAutoConfig, OnContent = "On", OffContent = "Off", Name = "SfAutoConfig" };
        var body = new StackPanel { Spacing = 12, Children =
        {
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { Label("Auto-configure ReShade for FrameGen", 12, null), toggle } },
            Label("When On, installing ShortFuse DLSS Tool writes HookStreamline=1 and HookDirectX=1 to ReShade.ini so FrameGen works correctly after ReShade.\n\nOn Windows this option also renames ReShade to Reshade64.asi and installs an ASI Loader. That is not needed on Linux: ShortFuse DLSS Tool v0.54 and above no longer require it, and ReShade stays loaded through its Proton DLL override.", 11, Muted),
        } };
        var dialog = Dialog("ShortFuse DLSS Tool Settings", body, 520);
        body.Children.Add(DialogAction("Save", body, () => { prefs.SfAutoConfig = toggle.IsChecked == true; _settings.Save(); dialog.Close(); return Task.CompletedTask; }));
        await dialog.ShowDialog(this);
    }

    // ── Nvidia Profile Overrides (DLSS / Streamline) ─────────────────────────
    private Control NvidiaProfileSection(Game game, InstallationStatus state)
    {
        var title = "Nvidia Profile Overrides" + (DriverVersion == null ? "" : " — Driver " + DriverVersion);
        var nr = DlssState(game);
        if (nr == null) return Section("NvidiaProfile", title, null, Label("Checking this game's DLSS files…", 11, Muted), "NvidiaProfile");
        var prefs = _settings.For(game); var detection = nr.Detection;
        var body = new StackPanel { Spacing = 8 };
        if (!detection.HasAny)
            body.Children.Add(Label("No DLSS or Streamline DLLs were found in this game. Install a Neural Rendering method above, or use Advanced settings if the game keeps them elsewhere.", 11, Muted));
        else
        {
            var grid = new Grid { ColumnDefinitions = new("*,Auto,*,Auto,*,Auto,*,Auto,*") };
            var column = 0;
            foreach (var kind in new[] { DlssKind.SR, DlssKind.RR, DlssKind.FG, DlssKind.NR, DlssKind.Streamline })
            {
                if (column > 0) { var divider = new Border { Width = 1, Background = Brush("#283240"), Margin = new Thickness(8, 0) }; Grid.SetColumn(divider, column - 1); grid.Children.Add(divider); }
                var col = DlssColumn(game, prefs, detection, kind); Grid.SetColumn(col, column); grid.Children.Add(col);
                column += 2;
            }
            body.Children.Add(grid);
        }
        var extras = Extras(game);
        var needsLaunch = !state.DlssLaunchConfigured;
        var launch = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(0, 4, 0, 0) };
        var settingsCount = DlssProfile.Managed.Count(id => DlssProfile.Has(prefs, id));
        launch.Children.Add(new StackPanel { Spacing = 3, Children =
        {
            Row(Badge(needsLaunch ? "Launch settings need updating" : settingsCount == 0 && extras.Dlls.Count == 0 && !extras.Environment.Values.Any(v => v != null) ? "Driver defaults" : "✓ Launch settings applied", !needsLaunch && settingsCount > 0, needsLaunch)),
            Label("Presets, render scale, Multi Frame Gen and NVIDIA Override are applied through dxvk-nvapi (DXVK_NVAPI_DRS_SETTINGS) in the game's Steam launch options.", 10, Muted),
        } });
        if (needsLaunch)
        {
            var apply = Action("Apply launch settings", () => ShowSteamSetup(game), "success", "ApplyDlssLaunch"); apply.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(apply, 1); launch.Children.Add(apply);
        }
        body.Children.Add(launch);
        var summary = string.Join("   ", new[] { DlssKind.SR, DlssKind.RR, DlssKind.FG, DlssKind.NR, DlssKind.Streamline }.Where(detection.Has)
            .Select(k => DlssFiles.Short(k) + ": " + (k != DlssKind.Streamline && k != DlssKind.NR && DlssProfile.DriverOverride(prefs, k) ? "NV Override" : detection.Version(k))));
        return Section("NvidiaProfile", title, summary.Length == 0 ? null : summary, body, "NvidiaProfile");
    }

    private StackPanel DlssColumn(Game game, GamePreferences prefs, DlssDetection detection, DlssKind kind)
    {
        var present = detection.Has(kind) && !(kind is DlssKind.SR or DlssKind.RR or DlssKind.Streamline && detection.Version(kind)?.StartsWith("1.") == true);
        var overridable = kind is DlssKind.SR or DlssKind.RR or DlssKind.FG;
        var overrideActive = overridable && DlssProfile.DriverOverride(prefs, kind);
        var col = new StackPanel { Spacing = 3, Opacity = present || overridable ? 1 : 0.4 };
        col.Children.Add(Label(DlssFiles.Label(kind), 11, null, true));

        // Version: Default (original) / versions / Custom / NVIDIA Override
        var items = new List<string>();
        var installed = detection.IsCustom(kind) ? "Custom" : detection.Version(kind);
        if (!present && !overridable) items.Add("None");
        else
        {
            var original = detection.Original(kind);
            items.Add(original != null ? $"Default ({original})" : "Default");
            items.AddRange(_dlss.Versions(kind));
            items.Add("Custom");
            if (overridable) items.Add("NVIDIA Override");
        }
        var index = 0;
        if (overrideActive) index = items.Count - 1;
        else if (installed != null && present && detection.Path(kind) is { } current)
        {
            // Until RHI swaps a file (leaving a ".original"), the game's own DLL is the Default entry.
            var swapped = kind == DlssKind.Streamline ? DlssFiles.Streamline.Any(f => Sentinel.Placed(Path.Combine(current, f))) : Sentinel.Placed(current);
            if (installed == "Custom") index = items.IndexOf("Custom");
            else if (swapped)
            {
                index = items.FindIndex(1, i => i == installed || DlssCatalog.StripSuffix(i) == installed);
                if (index < 0) { index = items.IndexOf("Custom"); items.Insert(index, installed); }
            }
        }
        var version = Combo(items, Math.Max(0, index), "DlssVersion" + DlssFiles.Short(kind));
        version.IsEnabled = present || overridable;
        ToolTip.SetTip(version, overrideActive
            ? "NVIDIA Override is active — the driver supplies its own latest DLL for this game. Select any other version to disable the override and deploy that version instead."
            : "Selects which DLL version is copied into the game folder. Default restores the original game DLL. Custom uses your own file from " + (kind == DlssKind.Streamline ? DlssFiles.CustomStreamlineDirectory : DlssFiles.CustomDirectory) + "." +
              (overridable ? " NVIDIA Override lets the driver supply its latest version instead (Proton NGX updater)." : ""));
        col.Children.Add(Field("Version", version));
        var initial = version.SelectedItem as string;
        version.SelectionChanged += async (_, _) =>
        {
            if (version.SelectedItem is not string selected || selected == initial) return;
            await Run(async () =>
            {
                if (selected == "NVIDIA Override") DlssProfile.SetDriverOverride(prefs, kind, true);
                else
                {
                    if (overrideActive) DlssProfile.SetDriverOverride(prefs, kind, false);
                    if (detection.Path(kind) is not { } target)
                    { if (selected != "None" && !selected.StartsWith("Default")) throw new IOException($"This game has no {DlssFiles.DllName(kind)} to replace."); }
                    else { GameSetup.RequireClosed(game); await _swap.Apply(kind, target, selected, Progress); }
                }
                _settings.Save(); InvalidateDlss(game); await ReadStates(); Filter();
                _status.Text = $"{DlssFiles.Label(kind)}: {selected}";
            });
        };

        // Preset
        if (kind != DlssKind.Streamline && present)
        {
            var presets = DlssProfile.Presets(kind);
            var current = DlssProfile.Preset(prefs, kind);
            var presetCombo = Combo(presets.Select(p => (object)p.Name), Math.Max(0, Array.FindIndex(presets, p => p.Value == current)), "DlssPreset" + DlssFiles.Short(kind));
            ToolTip.SetTip(presetCombo, kind switch
            {
                DlssKind.SR => "Override the DLSS upscaling model. J/K use the 1st-gen transformer (DLSS 4.0). L/M use the 2nd-gen transformer (DLSS 4.5) with better temporal stability. NVIDIA Recommended uses NVIDIA's per-resolution preset selection.",
                DlssKind.RR => "Override the Ray Reconstruction denoising model. Higher presets are newer model iterations. NVIDIA Recommended uses NVIDIA's per-resolution preset selection.",
                DlssKind.FG => "Override the Frame Generation interpolation model. Higher presets are newer model iterations. NVIDIA Recommended uses NVIDIA's per-resolution preset selection.",
                _ => "Override the Neural Rendering model preset."
            });
            presetCombo.SelectionChanged += async (_, _) =>
            {
                var i = presetCombo.SelectedIndex; if (i < 0 || presets[i].Value == current) return;
                DlssProfile.SetPreset(prefs, kind, presets[i].Value); await DriverSettingChanged(game, $"{DlssFiles.Label(kind)} preset: {presets[i].Name}");
            };
            col.Children.Add(Field("Preset", presetCombo));
        }
        else if (kind == DlssKind.NR) col.Children.Add(new Border { Height = 46 });

        // Render scale (SR and RR)
        if (kind is DlssKind.SR or DlssKind.RR && present)
        {
            var options = DlssProfile.RenderScaleOptions;
            var scale = DlssProfile.RenderScale(prefs, kind);
            var labels = options.Select(o => o.Name).ToList();
            var scaleIndex = scale == 0 ? 0 : Array.FindIndex(options, o => o.Value == scale);
            if (scaleIndex < 0) { scaleIndex = labels.Count - 1; labels[^1] = $"Custom ({scale}%)"; }
            var scaleCombo = Combo(labels, scaleIndex, "DlssScale" + DlssFiles.Short(kind));
            ToolTip.SetTip(scaleCombo, "Override the DLSS render resolution scale. Off = game controls the scale.\nNamed presets set a fixed percentage. Custom lets you enter any value from 33-100%.");
            scaleCombo.SelectionChanged += async (_, _) =>
            {
                var i = scaleCombo.SelectedIndex; if (i < 0 || i == scaleIndex) return;
                uint value = options[i].Value;
                if (options[i].Name == "Custom") { if (await AskPercent(scale) is not { } custom) { ShowGame(); return; } value = custom; }
                DlssProfile.SetRenderScale(prefs, kind, value); await DriverSettingChanged(game, $"{DlssFiles.Label(kind)} render scale: {(value == 0 ? "Off" : value + "%")}");
            };
            col.Children.Add(Field("Render Scale", scaleCombo));
        }

        if (kind == DlssKind.FG)
        {
            var mfg = Action("Multi Frame Gen", () => MultiFrameGen(game), "action", "MultiFrameGen");
            mfg.HorizontalAlignment = HorizontalAlignment.Stretch; mfg.IsEnabled = present; mfg.Opacity = present ? 1 : 0.4; mfg.Margin = new Thickness(0, 17, 0, 0);
            ToolTip.SetTip(mfg, "Configure NVIDIA Multi Frame Generation: mode, frame count multiplier, and dynamic target frame rate. Requires 50 Series GPU.");
            col.Children.Add(mfg);
        }
        if (kind == DlssKind.NR)
        {
            var deploy = Action("Deploy DLL", () => DeployNrDll(game, version.SelectedItem as string), "action", "DeployNrDll");
            deploy.HorizontalAlignment = HorizontalAlignment.Stretch;
            ToolTip.SetTip(deploy, "Download and copy nvngx_dlssnr.dll to the game folder. Supports RTX 40 and 50 Series GPUs. Neural Rendering methods above also deploy it automatically.");
            var delete = Action("✕", async () =>
            {
                if (detection.Path(DlssKind.NR) is not { } path) return;
                if (!Sentinel.Placed(path) && !await Confirm("Delete the game's NR DLL?", "This nvngx_dlssnr.dll came with the game, not from RHI, so there is no backup to restore. Verify the game's files in Steam to get it back.", "Delete")) return;
                GameSetup.RequireClosed(game);
                DlssSwap.Restore(DlssKind.NR, path);
                if (File.Exists(path) && !Sentinel.Placed(path)) File.Delete(path);
                InvalidateDlss(game); await ReadStates(); Filter(); _status.Text = "nvngx_dlssnr.dll removed";
            }, "danger", "DeleteNrDll");
            delete.Width = 36; delete.IsVisible = present;
            ToolTip.SetTip(delete, "Delete nvngx_dlssnr.dll from the game folder.");
            var deployRow = new Grid { ColumnDefinitions = new("*,Auto"), Margin = new Thickness(0, 17, 0, 0) };
            deployRow.Children.Add(deploy); delete.Margin = new Thickness(6, 0, 0, 0); Grid.SetColumn(delete, 1); deployRow.Children.Add(delete);
            col.Children.Add(deployRow);
            if (!present) foreach (var child in col.Children.Where(c => c != deployRow)) child.Opacity = 0.4;
            col.Opacity = 1;
        }
        if (kind == DlssKind.Streamline)
        {
            var defaults = _settings.DlssDefaults;
            var quick = Action("Quick Apply", () => QuickApply(game), defaults.IsEmpty ? "" : "action", "DlssQuickApply");
            quick.HorizontalAlignment = HorizontalAlignment.Stretch; quick.IsEnabled = !defaults.IsEmpty; quick.Margin = new Thickness(0, 17, 0, 0);
            ToolTip.SetTip(quick, "Apply your configured DLSS/Streamline default versions, presets, and render scales to this game (Settings → DLSS defaults). Downloads versions on-demand if not cached.");
            var restoreEnabled = detection.HasBackup || !DlssProfile.IsDefault(prefs);
            var restore = Action("Restore DLSS/SL", async () =>
            {
                GameSetup.RequireClosed(game);
                DlssSwap.RestoreAll(detection); DlssProfile.Reset(prefs); _settings.Save();
                InvalidateDlss(game); await ReadStates(); Filter(); _status.Text = "DLSS and Streamline restored to the game's originals";
            }, restoreEnabled ? "action" : "", "DlssRestoreAll");
            restore.HorizontalAlignment = HorizontalAlignment.Stretch; restore.IsEnabled = restoreEnabled; restore.Margin = new Thickness(0, 8, 0, 0);
            ToolTip.SetTip(restore, "Restore all DLSS and Streamline DLLs to their original game versions and reset presets to Default.");
            col.Children.Add(quick); col.Children.Add(restore);
            if (!present) { col.Opacity = 1; foreach (var child in col.Children.Where(c => c != quick && c != restore)) child.Opacity = 0.4; }
        }
        return col;
    }

    private async Task DriverSettingChanged(Game game, string message)
    {
        _settings.Save(); await ReadStates(); Filter(); _status.Text = message + " · apply the launch settings to use it";
    }

    private async Task<uint?> AskPercent(uint current)
    {
        var box = new TextBox { Watermark = "33-100", Text = current > 0 ? current.ToString() : "", MaxLength = 3, Name = "RenderScalePercent" };
        var body = new StackPanel { Spacing = 12, Children = { Label("Enter a render scale from 33 to 100 percent.", 13, Secondary), box } };
        var dialog = Dialog("Custom render scale", body, 420); uint? result = null;
        body.Children.Add(DialogAction("Apply", body, () =>
        {
            if (!uint.TryParse(box.Text, out var value) || value is < 33 or > 100) throw new FormatException("Enter a whole number from 33 to 100.");
            result = value; dialog.Close(); return Task.CompletedTask;
        }));
        await dialog.ShowDialog(this); return result;
    }

    private async Task DeployNrDll(Game game, string? selection)
    {
        GameSetup.RequireClosed(game);
        var detection = NeuralRenderingSetup.Read(game).Detection;
        var target = detection.Path(DlssKind.NR) ?? LinuxPaths.ResolveCase(game.InstallDirectory, DlssFiles.Nr);
        var choice = selection == null || selection.StartsWith("Default") || selection == "None" ? null : selection;
        if (choice == "Custom") await _swap.Apply(DlssKind.NR, target, "Custom", Progress);
        else Sentinel.Deploy(await _dlss.Fetch(DlssKind.NR, choice, Progress), target);
        // The NR runtime loads beside the DLSS SR DLL, so deploy that to the install root too.
        var sr = LinuxPaths.ResolveCase(game.InstallDirectory, DlssFiles.Sr);
        if (!File.Exists(sr) || Sentinel.Placed(sr)) Sentinel.Deploy(await _dlss.Fetch(DlssKind.SR, null, Progress), sr);
        InvalidateDlss(game); await ReadStates(); Filter(); _status.Text = "nvngx_dlssnr.dll deployed";
    }

    private async Task QuickApply(Game game)
    {
        GameSetup.RequireClosed(game);
        var prefs = _settings.For(game);
        var detection = NeuralRenderingSetup.Read(game).Detection;
        await ApplyDefaults(game, prefs, detection, _settings.DlssDefaults);
        _settings.Save(); InvalidateDlss(game); await ReadStates(); Filter(); _status.Text = "DLSS defaults applied to " + game.Name;
    }

    private async Task ApplyDefaults(Game game, GamePreferences prefs, DlssDetection detection, DlssDefaults defaults)
    {
        foreach (var kind in DlssFiles.Dlls.Append(DlssKind.Streamline))
        {
            if (!detection.Has(kind) || kind is DlssKind.SR or DlssKind.RR or DlssKind.Streamline && detection.Version(kind)?.StartsWith("1.") == true) continue;
            var key = kind.ToString();
            if (defaults.DriverOverrides.Contains(key)) { DlssProfile.SetDriverOverride(prefs, kind, true); continue; }
            if (defaults.Versions.TryGetValue(key, out var version)) await _swap.Apply(kind, detection.Path(kind)!, version, Progress);
            if (kind != DlssKind.Streamline && defaults.Presets.TryGetValue(key, out var preset) && preset != 0) DlssProfile.SetPreset(prefs, kind, preset);
        }
        if (defaults.SrScale != 0 && detection.Has(DlssKind.SR)) DlssProfile.SetRenderScale(prefs, DlssKind.SR, defaults.SrScale);
        if (defaults.RrScale != 0 && detection.Has(DlssKind.RR)) DlssProfile.SetRenderScale(prefs, DlssKind.RR, defaults.RrScale);
    }

    // ── Multi Frame Generation ───────────────────────────────────────────────
    private async Task MultiFrameGen(Game game)
    {
        if (!_settings.MfgWarningDismissed)
        {
            var dismiss = new CheckBox { Content = "Don't show this warning again" };
            var warning = new StackPanel { Spacing = 12, Children =
            {
                Label("Multi Frame Generation (MFG) and Dynamic MFG are only supported on NVIDIA 50 Series GPUs (Blackwell architecture).\n\nMinimum driver requirements:\n• MFG (Fixed): Driver 572.16+\n• DMFG (Dynamic): Driver 595.97+\n\nThese settings will have no effect on 40 Series or older hardware.", 13, Amber),
                dismiss,
            } };
            var proceed = false; var dialog = Dialog("Multi Frame Generation", warning, 480);
            warning.Children.Add(DialogAction("OK", warning, () => { proceed = true; dialog.Close(); return Task.CompletedTask; }));
            await dialog.ShowDialog(this);
            if (!proceed) return;
            if (dismiss.IsChecked == true) { _settings.MfgWarningDismissed = true; _settings.Save(); }
        }
        var prefs = _settings.For(game);
        var mode = DlssProfile.Get(prefs, DlssProfile.MfgModeId);
        var modeCombo = Combo(["Default", "Fixed", "Dynamic"], mode == DlssProfile.MfgFixed ? 1 : mode == DlssProfile.MfgDynamic ? 2 : 0, "MfgMode");
        var countCombo = Combo([], 0, "MfgCount"); var fpsCombo = Combo([], 0, "MfgTargetFps");
        var target = DlssProfile.Get(prefs, DlssProfile.MfgTargetFpsId);
        void Populate()
        {
            var fixedMode = modeCombo.SelectedIndex == 1; var dynamic = modeCombo.SelectedIndex == 2;
            var count = fixedMode ? DlssProfile.Get(prefs, DlssProfile.MfgFactorId) : DlssProfile.Get(prefs, DlssProfile.MfgDynamicMaxId);
            countCombo.ItemsSource = fixedMode ? new[] { "2x", "3x", "4x", "5x", "6x" } : dynamic ? new[] { "Up to 2x", "Up to 3x", "Up to 4x", "Up to 5x", "Up to 6x" } : new[] { "—" };
            countCombo.SelectedIndex = fixedMode || dynamic ? (int)Math.Clamp(count == 0 ? 0 : count - 1, 0, 4) : 0; countCombo.IsEnabled = fixedMode || dynamic;
            var fps = new List<string> { "Off", "Max Refresh Rate" };
            fps.AddRange(DlssProfile.TargetFpsOptions.Select(o => o.Label));
            if (target > 0 && target != DlssProfile.TargetFpsMaxRefresh && DlssProfile.TargetFpsOptions.All(o => o.Fps != target)) fps.Add($"{target} FPS (Custom)");
            fps.Add("Custom…");
            fpsCombo.ItemsSource = dynamic ? fps : new List<string> { "—" }; fpsCombo.IsEnabled = dynamic;
            fpsCombo.SelectedIndex = !dynamic || target == 0 ? 0 : target == DlssProfile.TargetFpsMaxRefresh ? 1
                : Array.FindIndex(DlssProfile.TargetFpsOptions, o => o.Fps == target) is var i and >= 0 ? i + 2 : fps.Count - 2;
        }
        Populate();
        modeCombo.SelectionChanged += (_, _) => Populate();
        var custom = new TextBox { Watermark = "20-1000", IsVisible = false, Name = "MfgCustomFps" };
        fpsCombo.SelectionChanged += (_, _) => custom.IsVisible = fpsCombo.SelectedItem as string == "Custom…";
        var body = new StackPanel { Spacing = 12, Children =
        {
            Field("FG Mode", modeCombo), Field("Frame Count", countCombo), Field("Target Frame Rate", fpsCombo), custom,
            Label("Fixed generates a set number of frames per rendered frame. Dynamic adjusts the multiplier up to the chosen maximum to reach the target frame rate.", 11, Muted),
        } };
        var dialog2 = Dialog("Multi Frame Generation", body, 460);
        body.Children.Add(DialogAction("Save", body, async () =>
        {
            var newMode = modeCombo.SelectedIndex switch { 1 => DlssProfile.MfgFixed, 2 => DlssProfile.MfgDynamic, _ => DlssProfile.MfgOff };
            uint fps = 0;
            if (newMode == DlssProfile.MfgDynamic && fpsCombo.SelectedItem is string choice)
            {
                if (choice == "Custom…") { if (!uint.TryParse(custom.Text, out fps) || fps is < 20 or > 1000) throw new FormatException("Enter a target frame rate from 20 to 1000."); }
                else if (choice == "Max Refresh Rate") fps = DlssProfile.TargetFpsMaxRefresh;
                else if (choice.EndsWith("(Custom)")) fps = target;
                else if (fpsCombo.SelectedIndex >= 2) fps = DlssProfile.TargetFpsOptions[fpsCombo.SelectedIndex - 2].Fps;
            }
            DlssProfile.SetMfg(prefs, newMode, newMode == DlssProfile.MfgOff ? 0 : (uint)countCombo.SelectedIndex + 1, fps);
            dialog2.Close();
            await DriverSettingChanged(game, "Multi Frame Generation: " + (modeCombo.SelectedItem as string));
        }));
        await dialog2.ShowDialog(this);
    }

    // ── Settings → DLSS defaults (Quick Apply / apply to all games) ──────────
    private async Task ShowDlssDefaults()
    {
        var defaults = _settings.DlssDefaults;
        var body = new StackPanel { Spacing = 12, Children = { Label("Choose the DLSS versions and presets that Quick Apply deploys. Leave a component on “Game default” to keep the game's own version.", 12, Secondary) } };
        var versionCombos = new Dictionary<DlssKind, ComboBox>(); var presetCombos = new Dictionary<DlssKind, ComboBox>(); var overrides = new Dictionary<DlssKind, CheckBox>();
        var grid = new Grid { ColumnDefinitions = new("150,*,8,*"), RowDefinitions = new("Auto,Auto,Auto,Auto,Auto,Auto") };
        var r = 0;
        foreach (var kind in DlssFiles.Dlls.Append(DlssKind.Streamline))
        {
            var key = kind.ToString();
            var name = Label(DlssFiles.Label(kind), 12, Secondary); Grid.SetRow(name, r); grid.Children.Add(name);
            var versions = _dlss.Versions(kind).Prepend("Game default").Append("Custom").ToList();
            var v = Combo(versions, defaults.Versions.TryGetValue(key, out var saved) ? Math.Max(0, versions.IndexOf(saved)) : 0, "Default" + key);
            v.Margin = new Thickness(0, 3); Grid.SetRow(v, r); Grid.SetColumn(v, 1); grid.Children.Add(v); versionCombos[kind] = v;
            if (kind != DlssKind.Streamline)
            {
                var presets = DlssProfile.Presets(kind);
                var p = Combo(presets.Select(x => (object)x.Name), Math.Max(0, Array.FindIndex(presets, x => x.Value == defaults.Presets.GetValueOrDefault(key))));
                p.Margin = new Thickness(0, 3); Grid.SetRow(p, r); Grid.SetColumn(p, 3); grid.Children.Add(p); presetCombos[kind] = p;
            }
            r++;
        }
        body.Children.Add(Row(Label("Version", 10, Muted), Label("Preset", 10, Muted))); body.Children.Add(grid);
        var scaleItems = DlssProfile.RenderScaleOptions.Where(o => o.Name != "Custom").Select(o => o.Name).ToList();
        var srScale = Combo(scaleItems, Math.Max(0, Array.FindIndex(DlssProfile.RenderScaleOptions, o => o.Value == defaults.SrScale)));
        var rrScale = Combo(scaleItems, Math.Max(0, Array.FindIndex(DlssProfile.RenderScaleOptions, o => o.Value == defaults.RrScale)));
        var scales = new Grid { ColumnDefinitions = new("*,8,*") }; scales.Children.Add(Field("SR render scale", srScale)); var rrField = Field("RR render scale", rrScale); Grid.SetColumn(rrField, 2); scales.Children.Add(rrField);
        body.Children.Add(scales);
        var overrideRow = new WrapPanel();
        foreach (var kind in new[] { DlssKind.SR, DlssKind.RR, DlssKind.FG })
        { var box = new CheckBox { Content = "NVIDIA Override " + DlssFiles.Short(kind), IsChecked = defaults.DriverOverrides.Contains(kind.ToString()), Margin = new Thickness(0, 0, 12, 0) }; overrides[kind] = box; overrideRow.Children.Add(box); }
        body.Children.Add(overrideRow);
        var dialog = Dialog("DLSS defaults", body, 640);
        void Save()
        {
            defaults.Versions.Clear(); defaults.Presets.Clear(); defaults.DriverOverrides.Clear();
            foreach (var (kind, combo) in versionCombos) if (combo.SelectedIndex > 0 && combo.SelectedItem is string s) defaults.Versions[kind.ToString()] = s;
            foreach (var (kind, combo) in presetCombos) if (combo.SelectedIndex > 0) defaults.Presets[kind.ToString()] = DlssProfile.Presets(kind)[combo.SelectedIndex].Value;
            foreach (var (kind, box) in overrides) if (box.IsChecked == true) defaults.DriverOverrides.Add(kind.ToString());
            defaults.SrScale = DlssProfile.RenderScaleOptions[srScale.SelectedIndex].Value; defaults.RrScale = DlssProfile.RenderScaleOptions[rrScale.SelectedIndex].Value;
            _settings.Save();
        }
        body.Children.Add(Row(DialogAction("Save defaults", body, () => { Save(); dialog.Close(); ShowGame(); return Task.CompletedTask; }),
            DialogAction("Apply to all DLSS games…", body, async () => { Save(); dialog.Close(); await ApplyDefaultsToAll(); })));
        await dialog.ShowDialog(this);
    }

    private async Task ApplyDefaultsToAll()
    {
        if (_settings.DlssDefaults.IsEmpty) { await Message("DLSS defaults", "Choose at least one default version, preset or render scale first."); return; }
        var candidates = new List<(Game Game, DlssDetection Detection)>();
        _status.Text = "Finding games with DLSS…";
        foreach (var game in _games.Where(g => g.Executable != null))
        {
            var detection = await Task.Run(() => DlssScanner.Detect(game.Root));
            if (detection.HasAny) candidates.Add((game, detection));
        }
        if (candidates.Count == 0) { await Message("DLSS defaults", "No installed games with DLSS or Streamline DLLs were found."); return; }
        var boxes = candidates.Select(c => new CheckBox { Content = c.Game.Name + "  —  " + string.Join(", ", DlssFiles.Dlls.Where(c.Detection.Has).Select(k => DlssFiles.Short(k) + " " + c.Detection.Version(k))), IsChecked = true }).ToList();
        var list = new StackPanel { Spacing = 4 }; foreach (var box in boxes) list.Children.Add(box);
        var body = new StackPanel { Spacing = 12, Children = { Label("Apply your DLSS defaults to these games. Close them first; originals are backed up and Restore DLSS/SL undoes it.", 12, Secondary), new ScrollViewer { Content = list, MaxHeight = 360 } } };
        var dialog = Dialog("Apply DLSS defaults", body, 640);
        body.Children.Add(DialogAction("Apply to selected games", body, async () =>
        {
            var done = 0; var failed = new List<string>();
            for (var i = 0; i < candidates.Count; i++)
            {
                if (boxes[i].IsChecked != true) continue;
                var (game, detection) = candidates[i];
                try { GameSetup.RequireClosed(game); await ApplyDefaults(game, _settings.For(game), detection, _settings.DlssDefaults); InvalidateDlss(game); done++; }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException) { failed.Add(game.Name + ": " + ex.Message); }
            }
            _settings.Save(); dialog.Close(); await ReadStates(); Filter();
            _status.Text = $"DLSS defaults applied to {done} game(s)." + (failed.Count > 0 ? " Some failed." : "");
            if (failed.Count > 0) await Message("Some games were skipped", string.Join("\n", failed));
        }, "success"));
        await dialog.ShowDialog(this);
    }
}
