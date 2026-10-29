using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using RHI.Linux.Core;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RHI.Linux;

// Extras section with OptiScaler, and its settings dialog, laid out like the Windows app.
public sealed partial class MainWindow
{
    private static readonly (float Fps, string Label)[] OsFpsLimits =
    [
        (59f, "59 (60Hz VRR)"), (73f, "73 (75Hz VRR)"), (97f, "97 (100Hz VRR)"), (116f, "116 (120Hz VRR)"), (138f, "138 (144Hz VRR)"),
        (157f, "157 (165Hz VRR)"), (171f, "171 (180Hz VRR)"), (189f, "189 (200Hz VRR)"), (224f, "224 (240Hz VRR)"), (258f, "258 (280Hz VRR)"),
        (275f, "275 (300Hz VRR)"), (324f, "324 (360Hz VRR)"), (416f, "416 (480Hz VRR)"), (431f, "431 (500Hz VRR)"),
    ];
    private static readonly (string Label, float Ratio)[] OsRenderScales =
    [
        ("Off", 0f), ("100% DLAA", 1.0f), ("99% DLAA Alt", 1.0101f), ("88% DLAA Lite", 1.136f), ("77% Ultra Quality", 1.3f), ("75% Quality+", 1.333f),
        ("67% Quality", 1.5f), ("58% Balanced", 1.724f), ("50% Performance", 2.0f), ("45% Performance-", 2.222f), ("33% Ultra Perf", 3.0f),
    ];
    // Display name → INI value, per upscaler API (the Windows app's lists).
    private static readonly Dictionary<string, (string Label, string Ini)[]> OsUpscalers = new()
    {
        ["DX11"] = [("Auto (Default)", "auto"), ("DLSS", "dlss"), ("FSR 2.2", "fsr22"), ("FSR 3.1", "fsr31"), ("XeSS (Arc only)", "xess"), ("XeSS on DX12", "xess_12"), ("FSR2.1 on DX12", "fsr21_12"), ("FSR2.2 on DX12", "fsr22_12"), ("FSR3 on DX12", "ffx_12")],
        ["DX12"] = [("Auto (Default)", "auto"), ("DLSS", "dlss"), ("XeSS", "xess"), ("FSR 2.1", "fsr21"), ("FSR 2.2", "fsr22"), ("FSR 3.x / FFX", "ffx")],
        ["Vulkan"] = [("Auto (Default)", "auto"), ("DLSS", "dlss"), ("FSR 2.1", "fsr21"), ("FSR 2.2", "fsr22"), ("FSR 3.x / FFX", "ffx"), ("XeSS", "xess"), ("FSR2.1 on DX12", "fsr21_12"), ("FSR3 on DX12", "ffx_12")],
    };
    private static readonly (string Label, string Ini)[] OsFgInputs = [("Auto (Default)", "auto"), ("OptiFG (Upscaler)", "upscaler"), ("DLSSG via Streamline", "dlssg"), ("DLSSG via Nvngx", "nvngxfg"), ("FSR 3.1 FG", "fsrfg"), ("FSR 3.0 FG", "fsrfg30"), ("XeFG", "xefg")];
    private static readonly (string Label, string Ini)[] OsFgOutputs = [("Auto (Default)", "auto"), ("FSR FG", "fsrfg"), ("DLSSG", "dlssg"), ("XeFG", "xefg")];
    private static readonly (string Label, string Ini)[] OsFgNvngx = [("None (Real DLSSG)", "None"), ("Nukem's", "Nukems"), ("Enabler", "Arturs"), ("FSR 3/4 FG", "FFX"), ("Combo", "Combo")];
    private static readonly (string Label, string Ini)[] OsSrPresets = [("Default", "auto"), ("J", "10"), ("K", "11"), ("L", "12"), ("M", "13")];
    private static readonly (string Label, string Ini)[] OsRrPresets = [("Default", "auto"), ("D", "3"), ("E", "4"), ("F", "5")];
    private static readonly (string Label, string Ini)[] OsNrScales = [("Default", "auto"), ("0.5x (half)", "0.5"), ("0.75x", "0.75"), ("1.0x (full)", "1.0"), ("1.5x (supersample)", "1.5")];

    private Control ExtrasSection(Game game, InstallationStatus state)
    {
        var prefs = _settings.For(game);
        var status = OptiScaler.StatusOf(game, state);
        var record = OptiScaler.Record(game);
        var variant = OsVariant.Of(prefs);
        var dll = OptiScaler.DllFor(prefs, _setup.Api(game, prefs));
        var update = _os.UpdateAvailable(game);
        var changed = record != null && (record.Variant != variant || !record.DllName.Equals(dll, StringComparison.OrdinalIgnoreCase));
        var action = update ? "⬆  Update OptiScaler" : changed ? "Apply " + OsVariant.Name(variant) + " as " + dll : status.Installed ? "↻  Reinstall OptiScaler" : "↓  Install OptiScaler";
        var latest = _os.Latest(record?.Variant ?? variant);
        var description = OptiScaler.Description + " RHI adds the DLL override Proton needs to load it (" + dll + ")." +
            (update && latest != null ? $"\n\nUpdate available: {OptiScaler.Label(record!.Variant, latest.Version)}." : "") +
            "\n\nOn AMD RDNA3/RDNA4 with Mesa 25.2 or newer, adding PROTON_FSR4_UPGRADE=1 to the launch options enables FSR 4.";
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(ComponentRow(game, status, "OptiScaler", action, () => InstallOptiScaler(game), () => OptiScalerSettings(game), description,
            game.Architecture == MachineType.x64, "64-bit games only", update || changed ? "update" : "action"));
        if (record != null)
        {
            var reshade = state.State.Files.Any(f => f.Component == "ReShade" && f.Path.Equals(Installation.ReShadeBesideOptiScaler, StringComparison.OrdinalIgnoreCase));
            var line = $"{OsVariant.Name(record.Variant)} · loads as {record.DllName}" + (reshade ? " · ReShade loads through OptiScaler (ReShade64.dll)" : "");
            body.Children.Add(Label(line, 10, Muted));
        }
        else if (status.Installed) body.Children.Add(Label("Installed by the Windows app. Install here to manage it from Linux, or × to remove it.", 10, Muted));
        body.Children.Add(Link("OptiScaler wiki →", OsVariant.ReleasesUrl(record?.Variant ?? variant), Muted));
        return Section("Extras", "Extras", status.Installed ? "OptiScaler: " + status.Version : null, body, "Extras");
    }

    private async Task<bool> ConfirmOptiScalerSetup()
    {
        var settings = _settings.OptiScaler;
        if (settings.SetupConfirmed) return true;
        var body = new StackPanel { Spacing = 14, Children = { Label("OptiScaler uses a different OptiScaler.ini for NVIDIA and for AMD/Intel GPUs. Check these before the first install; you can change them later in Settings.", 13, Secondary) } };
        body.Children.Add(OptiScalerGlobalFields(showHotkey: false));
        var confirmed = false;
        var dialog = Dialog("⚠ OptiScaler Setup", body, 520);
        body.Children.Add(DialogAction("Continue", body, () => { confirmed = true; settings.SetupConfirmed = true; _settings.Save(); dialog.Close(); return Task.CompletedTask; }));
        await dialog.ShowDialog(this);
        return confirmed;
    }

    private async Task InstallOptiScaler(Game game)
    {
        if (!await ConfirmOptiScalerSetup()) return;
        var prefs = _settings.For(game);
        await _os.Install(game, prefs, _settings.OptiScaler, _setup.Api(game, prefs), Progress);
        _settings.Save(); InvalidateDlss(game);
        await Changed(game, "OptiScaler installed");
    }

    private static void ClearOptiScalerPreferences(GamePreferences prefs)
    {
        prefs.OsDeployStreamline = false; prefs.OsStreamlineVersion = null; prefs.OsNrRuntime = null;
        prefs.OsFgInput = null; prefs.OsFgOutput = null; prefs.OsFgNvngx = null; prefs.OsFsrCrashFix = null;
    }

    // Settings → OptiScaler, also shown before the first install.
    private StackPanel OptiScalerGlobalFields(bool showHotkey = true)
    {
        var settings = _settings.OptiScaler;
        var gpu = Combo(["NVIDIA", "AMD / Intel"], settings.EffectiveGpu == "NVIDIA" ? 0 : 1, "OsGpu");
        var inputs = Combo(["Yes", "No"], settings.DlssInputs ? 0 : 1, "OsDlssInputs");
        inputs.IsEnabled = settings.EffectiveGpu != "NVIDIA";
        gpu.SelectionChanged += (_, _) => { settings.Gpu = gpu.SelectedIndex == 0 ? "NVIDIA" : "AMD"; inputs.IsEnabled = gpu.SelectedIndex != 0; _settings.Save(); };
        inputs.SelectionChanged += (_, _) => { settings.DlssInputs = inputs.SelectedIndex == 0; _settings.Save(); };
        var row = new Grid { ColumnDefinitions = new("*,12,*") };
        row.Children.Add(Field("GPU type", gpu, "Chooses RHI's NVIDIA or AMD/Intel OptiScaler.ini template. Detected: " + OptiScaler.DetectGpu() + "."));
        var dlssField = Field("Use DLSS inputs (AMD/Intel)", inputs, "Yes keeps GPU spoofing on, which DLSS inputs, DLSS frame generation and Reflex → AL2 need. No sets Dxgi=false.");
        Grid.SetColumn(dlssField, 2); row.Children.Add(dlssField);
        var panel = new StackPanel { Spacing = 10, Children = { row } };
        if (!showHotkey) return panel;
        var keys = OptiScaler.Hotkeys.Keys.ToList();
        var hotkey = Combo(keys, Math.Max(0, keys.IndexOf(settings.Hotkey)), "OsHotkey");
        hotkey.SelectionChanged += (_, _) => { settings.Hotkey = hotkey.SelectedItem as string ?? "Insert"; _settings.Save(); };
        var apply = DialogAction("Apply hotkey to installed games", panel, async () =>
        {
            var count = 0;
            foreach (var game in _games.Where(g => OptiScaler.Record(g)?.Ini == true))
            { OptiScaler.ApplyHotkey(OptiScaler.IniPath(game), settings.Hotkey); count++; }
            _status.Text = $"OptiScaler hotkey set to {settings.Hotkey} in {count} game(s)."; await Task.CompletedTask;
        }, "");
        apply.Name = "ApplyOsHotkey";
        apply.VerticalAlignment = VerticalAlignment.Bottom;
        var hotkeyRow = new Grid { ColumnDefinitions = new("*,12,*") };
        hotkeyRow.Children.Add(Field("Overlay hotkey", hotkey, "Key that opens the OptiScaler overlay in game (ShortcutKey)."));
        Grid.SetColumn(apply, 2); hotkeyRow.Children.Add(apply);
        panel.Children.Add(hotkeyRow);
        panel.Children.Add(Plain("Open OptiScaler INI templates folder", () => { OptiScaler.Template(settings.EffectiveGpu, settings.DlssInputs, OsVariant.Stable); Directory.CreateDirectory(OptiScaler.InisDirectory); Proton.Open(OptiScaler.InisDirectory); }));
        return panel;
    }

    private static Grid Pair(Control left, Control? right = null)
    {
        var grid = new Grid { ColumnDefinitions = new("*,12,*") };
        grid.Children.Add(left);
        if (right != null) { Grid.SetColumn(right, 2); grid.Children.Add(right); }
        return grid;
    }
    private static ComboBox Choice((string Label, string Ini)[] map, string? value, string name) =>
        Combo(map.Select(m => m.Label), Math.Max(0, Array.FindIndex(map, m => m.Ini.Equals(value ?? "auto", StringComparison.OrdinalIgnoreCase))), name);
    private static string Tristate(string? value) => value?.ToLowerInvariant() switch { "true" => "On", "false" => "Off", _ => "Default" };

    private async Task OptiScalerSettings(Game game)
    {
        var prefs = _settings.For(game);
        var record = OptiScaler.Record(game);
        var installed = record != null;
        var variant = OsVariant.Of(prefs);
        var api = _setup.Api(game, prefs);
        var body = new StackPanel { Spacing = 12 };
        Window? dialog = null;
        var reopen = false;
        string? Ini(string section, string key) => OptiScaler.GetSetting(game, section, key);
        void Write(string section, string key, string value)
        {
            try { OptiScaler.SetSetting(game, section, key, value); _status.Text = $"OptiScaler.ini: [{section}] {key}={value}"; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _status.Text = ex.Message; }
        }
        async Task Background(Func<Task> work)
        {
            try { await work(); }
            catch (Exception ex) { _status.Text = ex.Message; CrashReporter.Log(ex.ToString()); await Message("Could not finish", ex.Message); }
        }

        // Version and DLL name are applied by (re)installing, as the Windows channel switch does.
        var variantCombo = Combo(OsVariant.All.Select(OsVariant.Name), Array.IndexOf(OsVariant.All, variant), "OsVariant");
        var dllItems = new List<string> { "Auto (" + OptiScaler.DllFor(new GamePreferences(), api) + ")" }.Concat(OptiScaler.DllNames).ToList();
        var dllCombo = Combo(dllItems, prefs.OsDllName == null ? 0 : Math.Max(0, dllItems.IndexOf(prefs.OsDllName)), "OsDllName");
        var fpsItems = OsFpsLimits.Select(p => p.Label).Prepend("Off").ToList();
        var fps = float.TryParse(Ini("Framerate", "FramerateLimit"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) && f > 0
            ? OsFpsLimits.FirstOrDefault(p => Math.Abs(p.Fps - f) < 1f).Label : null;
        var fpsCombo = Combo(fpsItems, fps == null ? 0 : fpsItems.IndexOf(fps), "OsFramerate");
        body.Children.Add(Pair(Field("OptiScaler Version", variantCombo, "Stable: official release. Nightly: daily build. DLSS NR: Neural Rendering fork with multi-pass NR support."),
            Field("Framerate Limit", fpsCombo, "Framerate limit using Reflex whenever possible. 'Off' disables the limit.")));
        var apiDefault = api switch { GraphicsApiType.DirectX12 => "DX12", GraphicsApiType.Vulkan => "Vulkan", _ => "DX11" };
        var apiCombo = Combo(OsUpscalers.Keys, OsUpscalers.Keys.ToList().IndexOf(apiDefault), "OsUpscalerApi");
        var upscalerCombo = Combo([], -1, "OsUpscaler");
        var loading = false;
        void LoadUpscaler()
        {
            loading = true;
            var map = OsUpscalers[apiCombo.SelectedItem as string ?? "DX11"];
            upscalerCombo.ItemsSource = map.Select(m => m.Label).ToList();
            upscalerCombo.SelectedIndex = Math.Max(0, Array.FindIndex(map, m => m.Ini == (Ini("Upscalers", UpscalerKey())?.ToLowerInvariant() ?? "auto")));
            loading = false;
        }
        string UpscalerKey() => (apiCombo.SelectedItem as string) switch { "DX12" => "Dx12Upscaler", "Vulkan" => "VulkanUpscaler", _ => "Dx11Upscaler" };
        LoadUpscaler();
        body.Children.Add(Pair(Field("Upscaler API", apiCombo, "Select which graphics API's upscaler to configure."),
            Field("Upscaler", upscalerCombo, "Upscaler for the selected API. 'Auto' lets OptiScaler choose based on your GPU.")));
        body.Children.Add(Pair(Field("DLL name", dllCombo, "The filename OptiScaler loads as. Auto uses dxgi.dll, or winmm.dll for Vulkan games. RHI adds the matching Proton DLL override."),
            Label(installed ? "Changing the version or DLL name reinstalls OptiScaler." : "Settings below are available once OptiScaler is installed.", 11, Muted)));

        variantCombo.SelectionChanged += (_, _) =>
        {
            var selected = OsVariant.All[Math.Max(0, variantCombo.SelectedIndex)];
            prefs.OsVariant = selected == OsVariant.Stable ? null : selected; _settings.Save();
            reopen = true; dialog?.Close();
        };
        dllCombo.SelectionChanged += (_, _) =>
        {
            prefs.OsDllName = dllCombo.SelectedIndex <= 0 ? null : dllCombo.SelectedItem as string; _settings.Save();
            if (installed) { reopen = true; dialog?.Close(); }
        };
        fpsCombo.SelectionChanged += (_, _) =>
        {
            var match = OsFpsLimits.FirstOrDefault(p => p.Label == fpsCombo.SelectedItem as string);
            Write("Framerate", "FramerateLimit", match.Label == null ? "0" : OptiScaler.Number(match.Fps));
        };
        apiCombo.SelectionChanged += (_, _) => LoadUpscaler();
        upscalerCombo.SelectionChanged += (_, _) =>
        {
            if (loading || upscalerCombo.SelectedIndex < 0) return;
            Write("Upscalers", UpscalerKey(), OsUpscalers[apiCombo.SelectedItem as string ?? "DX11"][upscalerCombo.SelectedIndex].Ini);
        };

        var gated = new List<Control> { fpsCombo, apiCombo, upscalerCombo };
        if (OsVariant.Advanced(variant))
        {
            body.Children.Add(Label("Frame Generation Settings", 13, null, true));
            var slVersions = _dlss.Versions(DlssKind.Streamline).ToList();
            var slDefault = prefs.OsStreamlineVersion ?? (slVersions.Contains("2.12.0") ? "2.12.0" : slVersions.FirstOrDefault());
            if (slDefault != null && !slVersions.Contains(slDefault)) slVersions.Insert(0, slDefault);
            var deploySl = Combo(["No", "Yes"], prefs.OsDeployStreamline ? 1 : 0, "OsDeployStreamline");
            var slCombo = Combo(slVersions, Math.Max(0, slVersions.IndexOf(slDefault ?? "")), "OsStreamlineVersion");
            slCombo.IsEnabled = prefs.OsDeployStreamline;
            body.Children.Add(Pair(Field("Deploy Streamline", deploySl, "Deploys Streamline to the game's OptiScaler/Streamline folder, needed for DLSSG via Streamline. DLSS Enabler is not available on Linux."),
                Field("Streamline Version", slCombo)));
            var fgInput = Choice(OsFgInputs, prefs.OsFgInput, "OsFgInput");
            var hudFix = Combo(["Default", "On", "Off"], Array.IndexOf(new[] { "Default", "On", "Off" }, Tristate(Ini("OptiFG", "HUDFix"))), "OsHudFix");
            body.Children.Add(Pair(Field("FG Input", fgInput), Field("HUD Fix", hudFix, "HUD Fix: enables hudless resource tracking for Frame Generation. On = HUDFix=true in [OptiFG].")));
            var fgOutput = Choice(OsFgOutputs, prefs.OsFgOutput, "OsFgOutput");
            var nvngx = Combo(OsFgNvngx.Select(m => (object)new ComboBoxItem { Content = m.Label, IsEnabled = m.Ini != "Arturs" }),
                Math.Max(0, Array.FindIndex(OsFgNvngx, m => m.Ini == (prefs.OsFgNvngx ?? "None"))), "OsFgNvngx");
            nvngx.IsEnabled = prefs.OsFgOutput == "dlssg";
            body.Children.Add(Pair(Field("FG Output", fgOutput), Field("FG Nvngx Override", nvngx, "Only relevant when FG Output = DLSSG. Enabler needs DLSS Enabler, which is not available on Linux.")));
            deploySl.SelectionChanged += async (_, _) =>
            {
                prefs.OsDeployStreamline = deploySl.SelectedIndex == 1; slCombo.IsEnabled = prefs.OsDeployStreamline; _settings.Save();
                await Background(() => _os.ApplyStreamline(game, prefs, Progress));
            };
            slCombo.SelectionChanged += async (_, _) =>
            {
                prefs.OsStreamlineVersion = slCombo.SelectedItem as string; _settings.Save();
                if (prefs.OsDeployStreamline) await Background(() => _os.ApplyStreamline(game, prefs, Progress));
            };
            fgInput.SelectionChanged += (_, _) => { prefs.OsFgInput = OsFgInputs[fgInput.SelectedIndex].Ini; _settings.Save(); Write("FrameGen", "FGInput", prefs.OsFgInput); };
            hudFix.SelectionChanged += (_, _) => Write("OptiFG", "HUDFix", hudFix.SelectedIndex switch { 1 => "true", 2 => "false", _ => "auto" });
            fgOutput.SelectionChanged += (_, _) =>
            {
                prefs.OsFgOutput = OsFgOutputs[fgOutput.SelectedIndex].Ini; _settings.Save(); Write("FrameGen", "FGOutput", prefs.OsFgOutput);
                nvngx.IsEnabled = prefs.OsFgOutput == "dlssg";
            };
            nvngx.SelectionChanged += (_, _) =>
            {
                prefs.OsFgNvngx = OsFgNvngx[nvngx.SelectedIndex].Ini; _settings.Save();
                if (prefs.OsFgOutput == "dlssg") Write("FrameGen", "FGNvngxReplacement", prefs.OsFgNvngx);
            };

            body.Children.Add(Label("Additional Settings", 13, null, true));
            var sr = Choice(OsSrPresets, Ini("DLSS", "RenderPresetForAll"), "OsSrPreset");
            var rr = Choice(OsRrPresets, Ini("DLSSD", "RenderPresetForAll"), "OsRrPreset");
            body.Children.Add(Pair(Field("DLSS SR Preset", sr, "DLSS Super Resolution render preset. J-M are the recommended modern presets."), Field("DLSS RR Preset", rr, "DLSS Ray Reconstruction render preset.")));
            var scale = OsRenderScales[0].Label;
            if (Ini("UpscaleRatio", "UpscaleRatioOverrideEnabled")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true
                && float.TryParse(Ini("UpscaleRatio", "UpscaleRatioOverrideValue"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ratio))
                scale = OsRenderScales.Skip(1).FirstOrDefault(p => Math.Abs(p.Ratio - ratio) < 0.01f).Label ?? scale;
            var scaleCombo = Combo(OsRenderScales.Select(p => p.Label), Array.FindIndex(OsRenderScales, p => p.Label == scale), "OsRenderScale");
            var flip = Combo(["Default", "On"], Ini("NvApi", "DisableFlipMetering") == "true" ? 1 : 0, "OsFlipMetering");
            body.Children.Add(Pair(Field("Render Scale", scaleCombo, "Override the internal render resolution. Off = use in-game quality preset as-is."),
                Field("Disable Flip Metering", flip, "On: DisableFlipMetering=true — fixes thick frametime graph with NukemFG + fakenvapi.")));
            sr.SelectionChanged += (_, _) => { var v = OsSrPresets[sr.SelectedIndex].Ini; Write("DLSS", "RenderPresetOverride", "true"); Write("DLSS", "RenderPresetForAll", v == "auto" ? "0" : v); };
            rr.SelectionChanged += (_, _) => { var v = OsRrPresets[rr.SelectedIndex].Ini; Write("DLSSD", "RenderPresetOverride", "true"); Write("DLSSD", "RenderPresetForAll", v == "auto" ? "0" : v); };
            flip.SelectionChanged += (_, _) => Write("NvApi", "DisableFlipMetering", flip.SelectedIndex == 1 ? "true" : "false");
            scaleCombo.SelectionChanged += (_, _) =>
            {
                var choice = OsRenderScales[scaleCombo.SelectedIndex];
                if (choice.Ratio == 0) { Write("UpscaleRatio", "UpscaleRatioOverrideEnabled", "false"); return; }
                Write("UpscaleRatio", "UpscaleRatioOverrideEnabled", "true"); Write("UpscaleRatio", "UpscaleRatioOverrideValue", OptiScaler.Number(choice.Ratio));
            };
            gated.AddRange([deploySl, slCombo, fgInput, hudFix, fgOutput, nvngx, sr, rr, scaleCombo, flip]);

            if (Directory.Exists(Path.Combine(game.Root, "Engine")))
            {
                body.Children.Add(Label("Engine.ini Settings", 13, null, true));
                var engine = OptiScaler.EngineIni(game);
                var dmv = Combo(["Default", "Off"], OptiScaler.EngineApplied(game, "DilateMotionVectors") ? 1 : 0, "OsDilateMotionVectors");
                var fsrItems = new[] { "None", "FSR2", "FSR3", "FSR3.1" };
                var fsr = Combo(fsrItems, Math.Max(0, Array.IndexOf(fsrItems, OptiScaler.EngineApplied(game, "FsrCrashFix") ? prefs.OsFsrCrashFix ?? "None" : "None")), "OsFsrCrashFix");
                var swap = Combo(["Default", "On"], OptiScaler.EngineApplied(game, "FsrFgSwapchain") ? 1 : 0, "OsFsrFgSwapchain");
                var plugin = Combo(["Default", "On"], OptiScaler.EngineApplied(game, "UpscalerPlugin") ? 1 : 0, "OsUpscalerPlugin");
                body.Children.Add(Pair(Field("Dilated Motion Vectors", dmv, "Off: r.NGX.DLSS.DilateMotionVectors=0 + r.Streamline.DilateMotionVectors=0"),
                    Field("FSR Crash Fix", fsr, "FSR2: r.FidelityFX.FSR2.UseNativeDX12=1\nFSR3: r.FidelityFX.FSR3.UseNativeDX12=1\nFSR3.1: above + r.FidelityFX.FSR3.UseRHI=0")));
                body.Children.Add(Pair(Field("FSR-FG Swapchain", swap, "On: r.FidelityFX.FI.OverrideSwapChainDX12=1"), Field("Upscaler Plugin", plugin, "On: r.AntiAliasingMethod=4 + r.TemporalAA.Upscaler=1")));
                if (engine == null) body.Children.Add(Label("Launch the game once to create its Engine.ini. If there are several, choose the right one in Advanced settings.", 11, Amber));
                void Engine(string group, IniKey[]? keys)
                {
                    try { OptiScaler.SetEngine(game, group, keys); _status.Text = "Engine.ini updated"; }
                    catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException) { _status.Text = ex.Message; }
                }
                dmv.SelectionChanged += (_, _) => Engine("DilateMotionVectors", dmv.SelectedIndex == 1 ? OptiScaler.EngineSettings["DilateMotionVectors"] : null);
                fsr.SelectionChanged += (_, _) =>
                {
                    var choice = fsr.SelectedItem as string ?? "None";
                    prefs.OsFsrCrashFix = choice == "None" ? null : choice; _settings.Save();
                    Engine("FsrCrashFix", choice == "None" ? null : OptiScaler.EngineSettings[choice]);
                };
                swap.SelectionChanged += (_, _) => Engine("FsrFgSwapchain", swap.SelectedIndex == 1 ? OptiScaler.EngineSettings["FsrFgSwapchain"] : null);
                plugin.SelectionChanged += (_, _) => Engine("UpscalerPlugin", plugin.SelectedIndex == 1 ? OptiScaler.EngineSettings["UpscalerPlugin"] : null);
                foreach (var c in new Control[] { dmv, fsr, swap, plugin }) { c.IsEnabled = engine != null; gated.Add(c); }
            }

            if (variant == OsVariant.DlssNr)
            {
                body.Children.Add(Label("Neural Rendering Settings", 13, null, true));
                var nrVersions = _dlss.Versions(DlssKind.NR).ToList();
                var nrCurrent = prefs.OsNrRuntime ?? nrVersions.FirstOrDefault();
                if (nrCurrent != null && !nrVersions.Contains(nrCurrent)) nrVersions.Insert(0, nrCurrent);
                var runtime = Combo(nrVersions, Math.Max(0, nrVersions.IndexOf(nrCurrent ?? "")), "OsNrRuntime");
                var tri = new[] { "Default", "On", "Off" };
                var enabled = Combo(tri, Array.IndexOf(tri, Tristate(Ini("DlssNr", "Enabled"))), "OsNrEnabled");
                var before = Combo(tri, Array.IndexOf(tri, Tristate(Ini("DlssNr", "RunBeforeSR"))), "OsNrRunBeforeSr");
                var passItems = new[] { "Default", "1", "2", "3" };
                var passes = Combo(passItems, Math.Max(0, Array.IndexOf(passItems, Ini("DlssNr", "Passes") ?? "Default")), "OsNrPasses");
                var working = Choice(OsNrScales, Ini("DlssNr", "WorkingScale"), "OsNrWorkingScale");
                var finished = Combo(["Default", "On"], Ini("DlssNr", "FinishedPicture")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true ? 1 : 0, "OsNrFinishedPicture");
                body.Children.Add(Pair(Field("NR Runtime", runtime, "nvngx_dlssnr.dll version deployed beside the game. The installed NVIDIA driver must support it."),
                    Field("NR Enabled", enabled, "Enable DLSS Neural Rendering. Requires nvngx_dlssnr.dll in the game folder.")));
                body.Children.Add(Pair(Field("Run Before SR", before, "Run NR before Super Resolution. On = before upscaling; Off = after upscaling."),
                    Field("Passes", passes, "Number of NR model passes. Each extra pass costs ~2x the model time but increases quality.")));
                body.Children.Add(Pair(Field("Working Scale", working, "Model work resolution as a fraction of frame size. Lower = faster; above 1.0 supersamples the model."),
                    Field("Apply to Finished Picture", finished, "Apply NR to the finished picture (after all game lighting and effects). Helps with green noise.")));
                runtime.SelectionChanged += async (_, _) =>
                {
                    prefs.OsNrRuntime = runtime.SelectedItem as string; _settings.Save();
                    if (installed) await Background(() => _os.ApplyNrRuntime(game, prefs, Progress));
                };
                enabled.SelectionChanged += (_, _) => Write("DlssNr", "Enabled", enabled.SelectedIndex switch { 1 => "true", 2 => "false", _ => "auto" });
                before.SelectionChanged += (_, _) => Write("DlssNr", "RunBeforeSR", before.SelectedIndex switch { 1 => "true", 2 => "false", _ => "auto" });
                passes.SelectionChanged += (_, _) => Write("DlssNr", "Passes", passes.SelectedIndex == 0 ? "auto" : passItems[passes.SelectedIndex]);
                working.SelectionChanged += (_, _) => Write("DlssNr", "WorkingScale", OsNrScales[working.SelectedIndex].Ini);
                finished.SelectionChanged += (_, _) => Write("DlssNr", "FinishedPicture", finished.SelectedIndex == 1 ? "true" : "false");
                gated.AddRange([runtime, enabled, before, passes, working, finished]);
            }
        }
        else body.Children.Add(Label("Frame generation, DLSS preset and render scale settings are available with the Nightly and DLSS NR versions.", 11, Muted));

        var deploy = DialogAction("Deploy OptiScaler.ini", body, async () =>
        {
            OptiScaler.DeployIni(game, prefs, _settings.OptiScaler);
            _status.Text = "OptiScaler.ini copied to the game folder."; reopen = true; dialog?.Close(); await Task.CompletedTask;
        }, "");
        ToolTip.SetTip(deploy, "Replace the game's OptiScaler.ini with RHI's template for your GPU (Settings → OptiScaler), keeping your hotkey and frame generation choices.");
        body.Children.Add(deploy);
        body.Children.Add(Label("If the game crashes with both ReShade and OptiScaler installed, try another DLL name above (for example winmm.dll) so ReShade keeps its own name.", 11, Secondary));
        foreach (var control in gated.Append(deploy)) if (!installed) { control.IsEnabled = false; control.Opacity = 0.45; }

        dialog = Dialog("OptiScaler Settings", body, 640);
        await dialog.ShowDialog(this);
        if (!reopen) return;
        var now = OptiScaler.Record(game);
        if (now != null && (now.Variant != OsVariant.Of(prefs) || !now.DllName.Equals(OptiScaler.DllFor(prefs, api), StringComparison.OrdinalIgnoreCase)))
            await InstallOptiScaler(game);
        else ShowGame();
        await OptiScalerSettings(game);
    }
}
