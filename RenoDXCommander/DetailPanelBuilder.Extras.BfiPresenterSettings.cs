// DetailPanelBuilder.Extras.BfiPresenterSettings.cs — BFI Presenter ⚙: this game's setup checked against the author's
// requirements (fixed refresh, VSync, no frame limiter, no Smooth Motion; Fix buttons for the NVIDIA profile), the
// tested-games entry, the main BFIPresenter.ini settings (keys and defaults from BFIPresenter.example.ini and the
// settings guide), and the Vulkan layer (all users, load order after ReShade, remove). Everything else stays in the
// BFI Presenter tab of the ReShade overlay.

using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    private static readonly string[] BfiOffOn = { "Off", "On" };
    private static readonly string[] BfiZeroOne = { "0", "1" };

    private async Task ShowBfiPresenterSettingsAsync(GameCardViewModel card)
    {
        var vm          = _window.ViewModel;
        var svc         = App.Services.GetRequiredService<BfiPresenterService>();
        var gameName    = card.GameName;
        var store       = card.Source ?? "";
        var installPath = card.InstallPath ?? "";
        var installedAs = vm.GetBfiPresenterInstalledAs(gameName, store);
        bool installed  = BfiPresenterService.IsInstalledIn(installPath, installedAs);
        var route       = installed ? BfiPresenterService.RouteOf(installedAs!) : BfiRouteFor(card).Route;
        var iniPath     = installed ? BfiPresenterService.IniPathOf(installPath, installedAs) : null;
        bool canEdit    = iniPath != null;
        bool dx = route == BfiRoute.Dx, vulkan = route == BfiRoute.Vulkan;

        // Gathered off the UI thread before the dialog is built: NVIDIA profile, tested list, RTX Encore settings
        var tested = BfiPresenterService.FindTested(await svc.GetTestedGamesAsync(), gameName);
        var issues = await Task.Run(() => CheckBfiSetup(card, installPath));

        var cog = new CogDialog("BFI Presenter", _window.Content.XamlRoot);
        cog.AddIntro("Black frame insertion clocked by the display: sharper motion at a fixed refresh rate. Most settings are also in the BFI Presenter tab of the ReShade overlay"
            + (vulkan ? " (BFI Presenter (Vulkan) tab)." : ".") + " Ctrl+Shift+B turns the strobe off.");
        if (!installed)
            cog.AddNote("Install BFI Presenter to change these settings.");
        else
        {
            var where = Path.GetDirectoryName(BfiPresenterService.FullPathOf(installPath, installedAs)!)!;
            cog.AddNote($"Installed as the {BfiRouteLabel(route!.Value, card.Is32Bit)}, {svc.AnyStagedVersion ?? "version unknown"}. "
                + (vulkan ? $"BFIPresenter.ini in {where} switches the layer on for this game; it must be next to the exe that runs."
                          : $"Settings file: BFIPresenter.ini in {where}"
                            + (File.Exists(iniPath) ? "." : " (the add-on creates it the first time the game starts; changes made here go in first).")));
        }
        if (card.IsRunning)
            cog.AddWarning(vulkan
                ? "The game is running: the layer picks up changes within a second. Saving in the overlay overwrites them."
                : "The game is running: press Ctrl+Shift+R in the game to reload BFIPresenter.ini. Saving in the overlay overwrites these changes.");

        // ── Tested-games list ───────────────────────────────────────────────────
        cog.AddSection("Tested games list");
        cog.AddNote(tested == null
            ? "This game isn't in BFI Presenter's tested-games list yet. The standard setup usually works."
            : $"{tested.Name}: {tested.Status}. {tested.Notes}".Trim());

        // ── Setup check ─────────────────────────────────────────────────────────
        cog.AddSection("This game's setup");
        if (issues.Count == 0)
            cog.AddNote(_dlssPresetService.IsSupported
                ? "No problems found: G-SYNC, VSync and frame limits look right for BFI."
                : "Set a fixed refresh rate (VRR off), VSync on or app-controlled, and no frame limiter for this game.");
        foreach (var issue in issues)
        {
            if (issue.Fix == null) { cog.AddWarning(issue.Text); continue; }
            var text = new TextBlock { Text = issue.Text, TextWrapping = TextWrapping.Wrap, FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
                Foreground = UIFactory.Brush(ResourceKeys.AccentAmberBrush) };
            Button? btn = null;
            btn = cog.MakeButton("Fix", CogButtonKind.Secondary, async () =>
            {
                btn!.IsEnabled = false;
                bool ok = await Task.Run(issue.Fix);
                if (ok)
                {
                    RefreshDriverSettingsIfShown(card);
                    text.Text = issue.FixedText;
                    text.Foreground = UIFactory.Brush(ResourceKeys.AccentGreenBrush);
                    cog.SetSuccess("Saved to the game's NVIDIA profile. Restart the game if it's running.");
                }
                else
                {
                    btn.IsEnabled = true;
                    cog.SetError("Couldn't change the NVIDIA profile. Try again, or set it in Driver Settings.");
                }
            });
            cog.AddContent(CogDialog.Inline(text, btn));
        }

        // ── Settings ────────────────────────────────────────────────────────────
        CogDialog.CompactCell Choice(string label, string help, string key, string[] displays, string[] values, string def)
        {
            var current = canEdit ? BfiPresenterService.GetIni(iniPath!, key) : null;
            int idx = Array.FindIndex(values, v => string.Equals(v, current ?? def, StringComparison.OrdinalIgnoreCase));
            if (idx < 0 && current != null)
            {
                // A value set in the overlay that isn't in the list: show it as it is
                displays = displays.Append(current).ToArray();
                values = values.Append(current).ToArray();
                idx = values.Length - 1;
            }
            if (idx < 0) idx = Math.Max(0, Array.IndexOf(values, def));
            return cog.CompactCombo(label, help, displays, idx, i =>
            {
                if (!canEdit) return false;
                BfiPresenterService.SetIni(iniPath!, key, values[i]);
                CrashReporter.Log($"[BfiPresenterCog] '{gameName}': {key} = {values[i]}");
                cog.SetSuccess(vulkan ? "Saved." : "Saved. In a running game, press Ctrl+Shift+R to reload.");
                return true;
            }, isEnabled: canEdit);
        }
        CogDialog.CompactCell Number(string label, string help, string key, double min, double max, string def)
            => new(label, help, BuildBfiNumberBox(cog, iniPath, gameName, key, label, min, max, def));

        cog.AddSection("Strobe");
        cog.AddCompactGrid(
            Choice("Method", "Variable MPRT (default): dark and mid-tones flash like BFI, highlights too bright for one flash spill into the dark refresh, so brightness is kept. BFI: a truly black dark refresh, sharpest motion, dimmer highlights. Ctrl+Shift+M switches.",
                "Method", new[] { "BFI", "Variable MPRT" }, BfiZeroOne, "1"),
            Choice("Frames per cycle", "Refreshes per flash: 2 = one lit, one dark (a 60 Hz strobe at 120 Hz). More is sharper but darker and flickers more: for 240 Hz and up, with the One game frame per strobe cycle cap.",
                "Frames", new[] { "2", "3", "4", "5", "6" }, new[] { "2", "3", "4", "5", "6" }, "2"),
            Choice("Gain", "How much of the lost brightness is won back. 0.5 (BFI Presenter's default): the lit refresh as the game drew it, half the average brightness. 1.0 (recommended on bright displays, especially HDR): close to normal brightness.",
                "Gain", new[] { "0.5", "0.75", "1.0" }, new[] { "0.5", "0.75", "1.0" }, "0.5"),
            Choice("Dark frame floor", "How dark the dark refresh is. 0 = black. 0.05 to 0.15 leaves a faint picture there: less flicker, a bit more blur.",
                "DarkFloor", new[] { "0", "0.05", "0.1", "0.15" }, new[] { "0.0", "0.05", "0.1", "0.15" }, "0.0"),
            Choice("Rolling scan", "Lights the screen in bands from the top down, like a CRT beam, so flicker feels calmer. Meant for 240 Hz and up; at 120 Hz there are two halves with a seam. Ctrl+Shift+S switches.",
                "RollingScan", BfiOffOn, BfiZeroOne, "0"),
            Choice("On-screen display", "A small panel over the game: frame rate, lost and repeated frames, missed refreshes, a frame-time graph. Ctrl+Shift+F switches.",
                "Osd", BfiOffOn, BfiZeroOne, "0"));

        cog.AddSection("Your display");
        cog.AddCompactGrid(
            Choice("LCD: prevent image retention", "Turn on for LCD monitors (IPS, VA, TN): with BFI every lit refresh gets the same drive voltage, which can leave a ghost image. Not needed on OLED, or with an odd number of frames per cycle.",
                "LcdPhaseSwap", BfiOffOn, BfiZeroOne, "0"),
            Choice("LCD saver mode", "Immediate: every few seconds a short fade to a steady picture while the lit refresh moves. Gradual: the light slides slowly to the other refresh, no fade or stutter (plain strobe only).",
                "LcdSaverGradual", new[] { "Immediate", "Gradual" }, BfiZeroOne, "0"),
            Choice("Output", "Auto reads SDR or HDR (and which HDR format) from the game. Force it only if colours look clearly wrong.",
                "Output", new[] { "Auto", "SDR", "scRGB", "HDR10" }, new[] { "auto", "sdr", "scrgb", "hdr10" }, "auto"),
            Number("Display peak (nits)", "Your display's real peak brightness (a review such as RTINGS lists it). In HDR the lit refresh never asks for more. 50 to 10000; below 100 the picture is darker than with BFI off. Press Enter or click away to save.",
                "DisplayPeakNits", 50, 10000, "1400"),
            Choice("ABL guard", "HDR only. OLEDs dim a refresh that puts out too much light overall; the guard lowers the boost in very bright scenes just enough to stay within the panel's limit.",
                "AblGuard", BfiOffOn, BfiZeroOne, "1"),
            Number("Panel budget (nits)", "Your display's full-white (100% window) brightness: about 250 for the Sony A95L and similar QD-OLEDs, 150 to 200 for many LG-panel OLEDs. 50 to 1500. Press Enter or click away to save.",
                "AblGuardBudgetNits", 50, 1500, "250"));

        cog.AddSection("Smoothness and latency");
        cog.AddCompactGrid(
            Choice("Game frame cap", "Keeps the game in step with the strobe. Refresh rate (default) with the automatic cap below suits most games, also with frame generation. Half: every frame gets exactly one flash (60 fps at 120 Hz). Quarter: for heavy games. One per strobe cycle: for 240 Hz and up with more frames per cycle.",
                "CapGameToRefresh", new[] { "Off", "Refresh rate", "Half refresh", "Quarter refresh", "One per strobe cycle" }, new[] { "0", "1", "2", "3", "4" }, "1"),
            Choice("Automatic frame cap", "At the Refresh rate cap: when the game can't hold the refresh rate, steps down to one game frame per strobe cycle (e.g. 60 at 120 Hz) instead of judder, and back up when it can.",
                "AutoFrameCap", BfiOffOn, BfiZeroOne, "1"),
            Choice("Frame pacing", "Lowest latency (default): always the newest frame. Smooth: every frame shown once, in order, about a refresh more delay; only worth it at Half or Quarter.",
                "FramePacing", new[] { "Smooth", "Lowest latency" }, BfiZeroOne, "1"),
            Choice("Later release", "Refresh rate cap, Lowest latency: the game starts each frame as late as safely possible, up to about 5 ms less input delay in light games. Not with frame generation.",
                "LaterRelease", BfiOffOn, BfiZeroOne, "0"));

        cog.AddSection("SDR games in HDR");
        cog.AddCompactGrid(
            Choice("SDR to HDR", "With Windows HDR on, SDR games are sent as HDR10 so the lit refresh has room to get brighter. Turn RTX HDR and Windows Auto HDR off for the game: the conversion should happen once, before the strobe."
                    + (vulkan ? " In DXVK games On keeps them SDR (HDR10 hung the NVIDIA driver there); Force in DXVK needs the NVIDIA present method set to Prefer native." : ""),
                "SdrToHdr", vulkan ? new[] { "Off", "On", "Force in DXVK" } : BfiOffOn, vulkan ? new[] { "0", "1", "2" } : BfiZeroOne, "1"),
            Number("SDR white (nits)", "How bright normal white looks in that mode. Match Windows' SDR content brightness (80 + 4 x the slider). Press Enter or click away to save.",
                "SdrWhiteNits", 80, 1000, "200"));

        cog.AddSection("Compatibility");
        if (vulkan)
            cog.AddCompactGrid(
                Choice("Vulkan layer in this game", "Off keeps the layer out of this game without deleting BFIPresenter.ini.",
                    "VulkanLayer", BfiOffOn, BfiZeroOne, "1"),
                Choice("Exclusive fullscreen", "The presenter's own output runs in exclusive fullscreen (the game's window mode is unchanged), so overlays and alt-tab can't hand it to the desktop compositor, where it flickers. Applies at the next game start.",
                    "VulkanExclusiveFullscreen", BfiOffOn, BfiZeroOne, "1"),
                Choice("Queue one refresh ahead", "Keeps one finished refresh ready, so a busy GPU doesn't cause single flashes (GPU-heavy games). Costs one refresh of latency.",
                    "QueueAhead", BfiOffOn, BfiZeroOne, "0"),
                Choice("Frame generation DLL counts", "A loaded frame-generation DLL counts as frame generation on, so the automatic cap stays at Refresh. Off for a game that loads one without using it (BFIPresenterVK.log names it).",
                    "FrameGenDllCheck", BfiOffOn, BfiZeroOne, "1"));
        else
            cog.AddCompactGrid(
                Choice("Resize to start BFI", "BFI starts when the game first resizes its swap chain. For games that never do (Hades II, Wreckfest 2, Pragmata) the presenter does it after 3 s. Also recreated: for games that recreate their swap chain once at start (Onimusha). If the game freezes or goes black, turn it off and change display mode by hand.",
                    "PresenterResize", new[] { "Off", "On", "Also recreated" }, new[] { "0", "1", "2" }, "0"),
                Choice("Queue one refresh ahead", "Keeps one present queued, so an occasional late refresh doesn't flash (GPU-heavy and frame-generation games, or when G-SYNC sneaks in). Costs one refresh of latency.",
                    "QueueAhead", BfiOffOn, BfiZeroOne, "0"),
                Choice("Presenter buffers", "Real swap-chain buffers behind the presenter. 3 (default) suits most games; 4 gives more slack if it flashes. Applies at the game's next resize.",
                    "PresenterBuffers", new[] { "2", "3", "4" }, new[] { "2", "3", "4" }, "3"),
                Choice("Show overlays", "64-bit DirectX games: lets the RivaTuner OSD and Steam overlay draw on the strobed picture. Steam's overlay crashed some games this way: turn it off if the game crashes. Applies at the next game start.",
                    "ShowOverlays", BfiOffOn, BfiZeroOne, "0"));

        // ── GPU priority (the BFI Presenter Manager's GPU priority row) ─────────
        cog.AddSection("GPU priority");
        cog.AddCompactGrid(
            Choice("Presenter GPU priority", "How urgently the graphics card treats the flashes. Realtime (default) runs the presenter's small amount of GPU work ahead of the game's, so flashes stay on time however busy the GPU is; it needs the Windows right below, otherwise it falls back to High. Lower to High or Normal if the game freezes with BFI Presenter. Applies at the next game start."
                    + (vulkan ? " Vulkan games get realtime only when they leave the compute queue family free (BFIPresenterVK.log says so)." : ""),
                "PresenterQueuePriority", new[] { "Normal", "High", "Realtime" }, new[] { "0", "1", "2" }, "2"));
        var prio = await Task.Run(RealtimeGpuPriorityService.GetStatus);
        string PrioStatus(RealtimeGpuPriorityService.Status s) => s switch
        {
            { Granted: true, InToken: false } => "Allowed for your Windows account. Sign out of Windows and back in, then games get the realtime GPU queue.",
            { Granted: true } => "Allowed: games get the realtime GPU queue.",
            { Granted: false, InToken: true } => "Removed from your account. Games keep realtime until you sign out of Windows.",
            { Granted: false } => "Not allowed: games use the high-priority GPU queue unless they run as administrator. Allow it if BFI Presenter flashes or flickers in GPU-heavy moments.",
            _ => "Not allowed yet as far as RHI knows (Windows only shows this setting to administrators). If you allowed it with BFI Presenter's Manager, sign out and back in and this updates; otherwise Allow it here.",
        };
        var prioNote = cog.AddNote(PrioStatus(prio));
        bool prioGranted = prio.Granted == true;
        Button? prioBtn = null;
        prioBtn = cog.MakeButton(prioGranted ? "Remove from my account" : "Allow for my account", CogButtonKind.Secondary, async () =>
        {
            prioBtn!.IsEnabled = false;
            var (ok, err) = await RealtimeGpuPriorityService.SetAsync(grant: !prioGranted);
            var now = await Task.Run(RealtimeGpuPriorityService.GetStatus);
            prioGranted = now.Granted == true;
            prioNote.Text = PrioStatus(now);
            prioBtn.Content = prioGranted ? "Remove from my account" : "Allow for my account";
            prioBtn.IsEnabled = true;
            if (ok) cog.SetSuccess(prioGranted
                ? "Added. Sign out of Windows and back in, then start the game: its log says the realtime GPU priority privilege is enabled."
                : "Removed. It stays in effect until you sign out of Windows.");
            else cog.SetError(err ?? "Not changed.");
        }, "Gives your Windows account the right 'Increase scheduling priority' (one administrator prompt), as BFI Presenter's Manager does, "
         + "so games can run the presenter's GPU queue at realtime priority without being run as administrator. Windows grants rights to accounts, "
         + "not programs, so any program you run can then raise its own priority too. Remove takes it away again.");
        cog.AddButtons(prioBtn);

        // ── Vulkan layer ────────────────────────────────────────────────────────
        if (vulkan || BfiPresenterService.LayerFilesPresent)
        {
            cog.AddSection("Vulkan layer");
            string LayerStatus()
            {
                if (!BfiPresenterService.IsLayerInstalled) return "The Vulkan layer isn't installed. Installing BFI Presenter in a Vulkan game installs it.";
                var s = "Installed for your Windows user" + (BfiPresenterService.IsLayerRegisteredForAllUsers ? " and for all users." : ".");
                if (BfiPresenterService.LayerOrderWrong) s += " ReShade is listed after BFI Presenter: fix the load order.";
                if (!BfiPresenterService.IsLayerRegisteredForAllUsers)
                    s += " Games run as administrator (User Account Control off, Steam or the game set to run as administrator) need it for all users too.";
                return s;
            }
            var layerNote = cog.AddNote(LayerStatus());
            var buttons = new List<Button>();
            if (BfiPresenterService.IsLayerInstalled)
            {
                bool all = BfiPresenterService.IsLayerRegisteredForAllUsers;
                buttons.Add(cog.MakeButton(all ? "Remove for all users" : "Register for all users", CogButtonKind.Secondary, async () =>
                {
                    bool ok = await svc.SetAllUsersAsync(remove: BfiPresenterService.IsLayerRegisteredForAllUsers);
                    layerNote.Text = LayerStatus();
                    if (ok) cog.SetSuccess("Done.");
                    else cog.SetError("Not changed: it needs administrator rights.");
                }, "Registers the layer in the machine-wide Vulkan list too (asks for administrator rights)."));
                if (BfiPresenterService.LayerOrderWrong)
                    buttons.Add(cog.MakeButton("Fix load order", CogButtonKind.Secondary, async () =>
                    {
                        bool ok = await Task.Run(svc.FixLayerOrder);
                        if (!ok && BfiPresenterService.IsLayerRegisteredForAllUsers) ok = await svc.SetAllUsersAsync(remove: false);
                        layerNote.Text = LayerStatus();
                        if (ok) cog.SetSuccess("BFI Presenter is listed after ReShade now.");
                        else cog.SetError("Not fixed: the all-users list needs administrator rights.");
                    }));
                buttons.Add(cog.MakeButton("Remove the Vulkan layer", CogButtonKind.Danger, async () =>
                {
                    var err = await svc.UninstallLayerAsync();
                    layerNote.Text = LayerStatus();
                    if (err == null) cog.SetSuccess("Removed. BFI Presenter no longer runs in any Vulkan game until the layer is installed again.");
                    else cog.SetError(err);
                }, "Removes the layer from this PC: every Vulkan game stops using it, including games set up with the BFI Presenter Manager."));
            }
            if (buttons.Count > 0) cog.AddButtons(buttons.ToArray());
        }

        var openIni = cog.MakeOpenFileButton("Open BFIPresenter.ini", iniPath != null && File.Exists(iniPath) ? iniPath : null);
        cog.AddButtons(openIni,
            cog.MakeButton("Settings guide", CogButtonKind.Secondary, async () => { await Windows.System.Launcher.LaunchUriAsync(new Uri(BfiPresenterService.SettingsGuideUrl)); }),
            cog.MakeButton("Tested games", CogButtonKind.Secondary, async () => { await Windows.System.Launcher.LaunchUriAsync(new Uri(BfiPresenterService.TestedGamesUrl)); }));

        await cog.ShowAsync();
    }

    /// <summary>
    /// The game's Driver Settings section reads the NVIDIA profile when it's built: rebuild it after BFI Presenter
    /// changed G-SYNC, VSync, the frame limit, power or Smooth Motion, or it keeps showing the old values.
    /// </summary>
    internal void RefreshDriverSettingsIfShown(GameCardViewModel card) =>
        _window.DispatcherQueue?.TryEnqueue(() =>
        {
            if (_currentDetailCard == card) BuildDriverProfileSection(card, card.GameName);
        });

    /// <summary>A problem with the game's setup for BFI, optionally with a fix (run off the UI thread).</summary>
    private sealed record BfiSetupIssue(string Text, Func<bool>? Fix = null, string FixedText = "Fixed.");

    /// <summary>The author's per-game requirements, checked against what RHI can read. Background thread (NVAPI).</summary>
    private List<BfiSetupIssue> CheckBfiSetup(GameCardViewModel card, string installPath)
    {
        var issues = new List<BfiSetupIssue>();
        var gn = card.GameName;
        var p = _dlssPresetService;
        if (p.IsSupported && !string.IsNullOrEmpty(installPath))
        {
            try
            {
                if (p.GetPerGameGSyncEnabled(gn, installPath))
                    issues.Add(new("G-SYNC isn't off for this game. It can pull the display below its fixed refresh rate (the overlay warns when VRR takes over).",
                        () => p.SetPerGameGSyncEnabled(gn, installPath, false), "G-SYNC is off for this game."));
                var vs = p.GetVSyncMode(gn, installPath);
                if (vs is VSyncForceOff or VSyncFastSync)
                    issues.Add(new($"Vertical sync is {(vs == VSyncFastSync ? "Fast Sync" : "Force off")} for this game: the strobe loses its cadence.",
                        () => p.SetVSyncMode(gn, installPath, VSyncForceOn), "Vertical sync is Force on."));
                var own = p.GetPerGameFpsLimit(gn, installPath);
                uint limit = own ?? p.GetGlobalFpsLimit();
                if (limit > 0)
                    issues.Add(new($"The NVIDIA frame rate limit ({limit} fps{(own == null ? ", global" : "")}) acts on the game: use BFI Presenter's own frame cap.",
                        () => p.SetPerGameFpsLimit(gn, installPath, 0), "The NVIDIA frame rate limit is off for this game."));
                if (p.GetSmoothMotionEnable(gn, installPath) == 1)
                    issues.Add(new("NVIDIA Smooth Motion is on for this game. Turn it off for BFI (the DirectX 11 path stays off while it's active).",
                        () => p.SetSmoothMotionEnable(gn, installPath, 0), "Smooth Motion is off for this game."));
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or TimeoutException)
            {
                CrashReporter.Log($"[CheckBfiSetup] NVIDIA profile read failed — {ex.Message}");
            }
        }
        if (card.IsUlInstalled)
            issues.Add(new("ReLimiter is installed in this game. Frame limiters break the strobe: remove it, or set its limit off."));
        if (card.IsDcInstalled)
            issues.Add(new("Display Commander is installed in this game. Make sure its frame limiter is off: BFI Presenter does the pacing."));
        var encoreAs = _window.ViewModel.GetRtxEncoreInstalledAs(gn, card.Source ?? "");
        var encore = App.Services.GetRequiredService<RtxEncoreService>();
        if (!string.IsNullOrEmpty(installPath) && encore.IsInstalledIn(installPath, encoreAs))
        {
            var cfg = encore.ConfigPath(installPath, encoreAs!);
            var limit = RtxEncoreConfig.Get(cfg, "nativeMenuFeatures", "reflexFrameLimit");
            if (limit != null && limit != "0")
                issues.Add(new($"RTX Encore's frame limit is {limit} fps. Set it to 0 in RTX Encore's ⚙: BFI Presenter does the pacing."));
            if (RtxEncoreConfig.Get(cfg, "smoothMotion", "smoothMotionEnabled") == "true")
                issues.Add(new("RTX Encore's Smooth Motion is on. Turn it off for BFI Presenter."));
        }
        if (card.IsOsInstalled && !string.IsNullOrEmpty(installPath))
        {
            // OptiScaler.ini [Framerate] FramerateLimit: auto / 0 = no limit
            var osLimit = Dlssg20_30Service.ReadIniValue(Path.Combine(installPath, "OptiScaler.ini"), "Framerate", "FramerateLimit");
            if (osLimit != null && !osLimit.Equals("auto", StringComparison.OrdinalIgnoreCase)
                && double.TryParse(osLimit, NumberStyles.Float, CultureInfo.InvariantCulture, out var osFps) && osFps > 0)
                issues.Add(new($"OptiScaler's frame rate limit is {osLimit} fps. Set FramerateLimit to 0 in its ⚙: BFI Presenter does the pacing (OptiScaler's frame generation works with it)."));
        }
        return issues;
    }

    /// <summary>Number box for one BFIPresenter.ini key: saves on Enter and when focus leaves.</summary>
    private static TextBox BuildBfiNumberBox(CogDialog cog, string? iniPath, string gameName, string key, string label,
        double min, double max, string def)
    {
        var current = iniPath != null ? BfiPresenterService.GetIni(iniPath, key) : null;
        var box = CogDialog.MakeTextBox(current ?? def, def);
        box.IsEnabled = iniPath != null;
        string saved = box.Text;
        void Save()
        {
            if (iniPath == null) return;
            var text = box.Text.Trim();
            if (text == saved) return;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || v < min || v > max)
            {
                cog.SetError($"{label} must be a number from {min.ToString(CultureInfo.InvariantCulture)} to {max.ToString(CultureInfo.InvariantCulture)}.");
                box.Text = saved;
                return;
            }
            try
            {
                var value = v.ToString(CultureInfo.InvariantCulture);
                BfiPresenterService.SetIni(iniPath, key, value);
                saved = value;
                box.Text = value;
                CrashReporter.Log($"[BfiPresenterCog] '{gameName}': {key} = {value}");
                cog.SetSuccess("Saved.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                cog.SetError($"Couldn't save {label}: {ex.Message}");
                box.Text = saved;
            }
        }
        box.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { Save(); e.Handled = true; } };
        box.LostFocus += (_, _) => Save();
        return box;
    }
}
