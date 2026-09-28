using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using RHI.Linux.Core;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RHI.Linux;

// Neural Rendering (DLSS 5) section. Mirrors upstream DetailPanelBuilder.NeuralRendering.cs.
public sealed partial class MainWindow
{
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
}
