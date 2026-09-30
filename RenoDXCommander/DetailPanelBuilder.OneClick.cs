// DetailPanelBuilder.OneClick.cs — "Optimize Game" / "Undo Optimization" orchestration.
//
// Nothing here is hardware specific: it applies the defaults the user configured in "DLSS & Streamline Defaults" and
// installs the Neural Rendering route RHI itself selects. This partial class deliberately lives in its own file:
//  * DLSS/Streamline/NVIDIA-profile work is done by DlssDefaultsApplier (shared with the Quick Apply button).
//  * Neural Rendering install/remove REUSES the existing private methods in DetailPanelBuilder.NeuralRendering.cs
//    (InstallShortFuseAsync, InstallDlss5ToolAsync, InstallBridgeAddonAsync, RestoreDlssDllsWithSentinel, …). Those methods
//    read/write UI controls, so they are given detached controls here rather than being rewritten or duplicated.
//  * The pure rules (status, preflight, undo planning) are in OptimizationEvaluator and unit tested.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

/// <summary>Result of an optimize / undo run, ready to show to the user.</summary>
public sealed record OneClickOutcome(bool Success, string Message, IReadOnlyList<string> Details, IReadOnlyList<PreflightIssue> Issues);

public partial class DetailPanelBuilder
{
    private sealed record NrState(string? Stored, string? Route, bool Supported, bool Installed);

    private GameOptimizationService OptSvc => App.Services.GetRequiredService<GameOptimizationService>();
    private OptimizationStateStore OptStore => App.Services.GetRequiredService<OptimizationStateStore>();

    // ── Reading state ─────────────────────────────────────────────────────────────

    /// <summary>What Neural Rendering looks like for this game. Does file I/O — call off the UI thread.</summary>
    private NrState ResolveNrState(GameCardViewModel card)
    {
        var installPath = card.InstallPath ?? "";
        var rdx5 = App.Services.GetRequiredService<Renodx5AddonService>();
        bool dlss5 = rdx5.IsInstalledIn(installPath);
        bool sf = rdx5.IsSfInstalledIn(installPath);
        bool bridge = File.Exists(Path.Combine(installPath, BridgeDeployFile));
        bool feeder = File.Exists(Path.Combine(installPath, card.Is32Bit ? FeederDeployFile32 : FeederDeployFile64));

        // Same inference as the Neural Rendering panel: a stored choice wins, else what is on disk.
        var stored = _window.ViewModel.GetNrMethodOverride(card.GameName, card.Source ?? "");
        stored ??= sf ? NrMethodShortFuse
                 : dlss5 && bridge ? NrMethodDlss5ToolBridge
                 : dlss5 ? NrMethodDlss5Tool
                 : feeder ? NrMethodFeeder : null;

        var api = card.GraphicsApi;
        bool gl = api == GraphicsApiType.OpenGL, dx11 = api == GraphicsApiType.DirectX11, vk = api == GraphicsApiType.Vulkan;
        bool hasDlss = card.HasAnyDlssStreamline;
        var route = stored ?? OptimizationEvaluator.RecommendNrRoute(hasDlss, card.Is32Bit, gl, dx11, vk);
        bool supported = OptimizationEvaluator.IsNrRouteSupported(route, hasDlss, card.Is32Bit, gl, dx11, vk);
        return new NrState(stored, route, supported, sf || dlss5 || feeder);
    }

    private OptimizationDefaults CurrentDefaults()
        => OptimizationDefaults.From(_window.ViewModel.Settings, FeatureFlags.DlssNr);

    private PreflightInput BuildPreflightInput(GameCardViewModel card, OptimizationDefaults defaults, NrState nr)
    {
        var installPath = card.InstallPath ?? "";
        var pe = App.Services.GetRequiredService<IPeHeaderService>();
        bool exists = Directory.Exists(installPath);
        var exe = exists ? pe.FindGameExe(installPath) : null;
        var machine = exe != null ? pe.DetectArchitecture(exe) : MachineType.Native;
        return new PreflightInput
        {
            InstallPathExists = exists,
            GameExecutable = exe,
            ExecutableIsValidPe = machine != MachineType.Native,
            GameIsRunning = exists && GameOptimizationService.IsGameRunning(installPath),
            ApiDetected = card.GraphicsApi != GraphicsApiType.Unknown,
            BitnessKnown = machine != MachineType.Native,
            NrRoute = nr.Route,
            NrRouteSupported = nr.Supported,
            DefaultsConfigured = defaults.HasAny,
            NvidiaProfileAccessNeeded = defaults.HasAny && card.HasAnyDlssStreamline,
            NvidiaProfileAccessAvailable = _dlssPresetService.IsSupported,
            Elevated = VulkanLayerService.IsRunningAsAdmin(),
            ForeignComponents = GameOptimizationService.DetectForeignComponents(card),
        };
    }

    /// <summary>Optimization status + preflight issues for the selected game (reads NVAPI under the panel semaphore).</summary>
    public async Task<(OptimizationReport report, IReadOnlyList<PreflightIssue> issues)> EvaluateOptimizationAsync(GameCardViewModel card)
    {
        return await Task.Run(async () =>
        {
            bool gotGate = await _panelScanSemaphore.WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
            try
            {
                var defaults = CurrentDefaults();
                var nr = ResolveNrState(card);
                var snap = OptSvc.CaptureSnapshot(card, nr.Route, nr.Supported, nr.Installed, defaults.NrVersion);
                var last = OptStore.Get(OptimizationEvaluator.GameKey(card.GameName, card.Source))?.LastAppliedFingerprint;
                var report = OptimizationEvaluator.Evaluate(defaults, snap, last);
                var issues = OptimizationEvaluator.Preflight(BuildPreflightInput(card, defaults, nr));
                if (OptimizationEvaluator.HasBlocking(issues) && report.State != OptimizationState.Optimized)
                    report = new OptimizationReport { State = OptimizationState.NeedsAttention, Items = report.Items,
                        DefaultsChanged = report.DefaultsChanged, Summary = "Needs attention — " + issues.First(i => i.Severity == PreflightSeverity.Blocking).Message };
                return (report, issues);
            }
            finally { if (gotGate) _panelScanSemaphore.Release(); }
        }).ConfigureAwait(true);
    }

    // ── Optimize ──────────────────────────────────────────────────────────────────

    public async Task<OneClickOutcome> RunOneClickOptimizeAsync(GameCardViewModel card, Action<string> progress)
    {
        var details = new List<string>();
        var name = card.GameName;
        var store = card.Source ?? "";
        var key = OptimizationEvaluator.GameKey(name, store);
        try
        {
            progress("Checking…");
            var defaults = CurrentDefaults();
            var (report, issues) = await EvaluateOptimizationAsync(card);

            // Preflight: any blocking problem means the game is left completely untouched.
            if (OptimizationEvaluator.HasBlocking(issues))
                return new OneClickOutcome(false, "Not started — nothing was changed.", Array.Empty<string>(), issues);

            // Idempotence: never rewrite anything that already matches the configured defaults.
            if (report.State == OptimizationState.Optimized)
                return new OneClickOutcome(true, "Already optimized — nothing to change.", new[] { report.Summary }, issues);

            var nr = await Task.Run(() => ResolveNrState(card));
            bool routeAuto = nr.Supported && OptimizationEvaluator.IsAutoInstallable(nr.Route);
            bool needNr = routeAuto && !nr.Installed;
            // The addon is useless without ReShade. If the addon is there but ReShade is not (removed, or never installed),
            // run the same route install the Install button would: it puts ReShade back and redoes the addon's auto-config.
            bool needReShade = routeAuto && !card.IsRsInstalled;
            bool needDefaults = defaults.HasAny && card.HasAnyDlssStreamline
                                && report.Items.Any(i => !i.Satisfied && i.Name != "Neural Rendering" && !i.Name.StartsWith("ReShade"));

            // Restore point FIRST, saved to disk before anything is touched, so a crash half-way still has an undo.
            progress("Saving restore point…");
            var rp = await Task.Run(() => OptSvc.CaptureRestorePoint(card, defaults, nr.Stored, nr.Installed));
            if (needNr) rp.NrMethodInstalledByOptimize = nr.Route;   // set up-front: Undo then also cleans a partial install
                                                                     // (not set when only ReShade was missing: that addon pre-existed)
            var rec = OptStore.Get(key) ?? new OptimizationRecord();
            rec.RestorePoint = rp;
            OptStore.Set(key, rec);

            bool reshadeMissingAfter = false;
            if (needNr || needReShade)
            {
                progress(needNr ? $"Installing Neural Rendering ({nr.Route})…" : "Installing ReShade…");
                bool reshadeWasThere = card.IsRsInstalled;
                await InstallNrRouteForOptimizeAsync(card, nr.Route!, defaults, progress);
                if (!reshadeWasThere && card.IsRsInstalled) { rp.ReShadeInstalledByOptimize = true; rec.RestorePoint = rp; OptStore.Set(key, rec); }
                reshadeMissingAfter = !card.IsRsInstalled;
                if (needNr) details.Add($"Neural Rendering installed ({nr.Route}).");
                details.Add(reshadeMissingAfter ? "ReShade could NOT be installed — Neural Rendering will not load until it is (see the ReShade section)."
                          : reshadeWasThere ? "ReShade already present." : "ReShade installed (required by Neural Rendering).");
            }
            else if (nr.Supported && !OptimizationEvaluator.IsAutoInstallable(nr.Route) && !nr.Installed)
                details.Add($"Neural Rendering route “{nr.Route}” needs manual set-up — skipped (use the Neural Rendering section).");

            if (needDefaults)
            {
                progress("Applying your DLSS / Streamline defaults…");
                var fresh = await Task.Run(() => _dlssStreamlineService.Detect(card.InstallPath ?? ""));
                card.ApplyDlssDetection(fresh);
                await new DlssDefaultsApplier(_dlssStreamlineService, new DlssPresetProfileAdapter(_dlssPresetService))
                    .ApplyAsync(card, _window.ViewModel.Settings);
                card.RefreshDlssVersions(_dlssStreamlineService);
                details.Add("DLSS / Streamline defaults applied.");
            }

            rec.LastAppliedFingerprint = defaults.Fingerprint();
            rec.LastAppliedUtc = DateTime.UtcNow;
            OptStore.Set(key, rec);
            _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(card));
            CrashReporter.Log($"[OneClickOptimize] '{name}' optimized: {string.Join(" ", details)}");
            if (reshadeMissingAfter)
                return new OneClickOutcome(false, "Partly done — ReShade could not be installed. “Undo Optimization” restores the previous state.", details, issues);
            return new OneClickOutcome(true, "Optimized. “Undo Optimization” restores the previous state.", details, issues);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[OneClickOptimize] '{name}' failed — {ex}");
            details.Add("The restore point was saved before the failure — press “Undo Optimization” to go back.");
            _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(card));
            return new OneClickOutcome(false, $"Failed: {ex.Message}", details, Array.Empty<PreflightIssue>());
        }
    }

    /// <summary>
    /// Runs the SAME install steps as the Neural Rendering “Install” button for the given (auto-installable) method.
    /// The epilogue mirrors that button's click handler; keep them in sync (see OneClickDriftTests).
    /// </summary>
    private async Task InstallNrRouteForOptimizeAsync(GameCardViewModel card, string route, OptimizationDefaults defaults, Action<string> progress)
    {
        var installPath = card.InstallPath!;
        var gameName = card.GameName;
        var store = card.Source ?? "";
        var rdx5Svc = App.Services.GetRequiredService<Renodx5AddonService>();
        var addonSvc = _window.ViewModel.AddonPackServiceInstance;
        var dlssSvc = _dlssStreamlineService;

        // Detached controls stand in for the panel's combos/button: the install code reads the selected version from them
        // ("Latest" or a pinned version) and reports progress through the button content.
        ComboBox Latest(string value) { var c = new ComboBox(); c.Items.Add(value); c.SelectedIndex = 0; return c; }
        var statusBtn = new Button { Content = "" };
        statusBtn.RegisterPropertyChangedCallback(ContentControl.ContentProperty, (_, _) => progress(statusBtn.Content?.ToString() ?? ""));
        var addonVersionCombo = Latest("Latest");
        var packVersionCombo = Latest("Latest");
        var nrVersionCombo = Latest(defaults.NrVersion.Length > 0 ? defaults.NrVersion : "Latest");   // the user's configured NR DLL default

        // All NR methods need ReShade (same rule as the Install button).
        if (!card.IsRsInstalled)
        {
            progress("Installing ReShade…");
            await _window.ViewModel.InstallReShadeCommand.ExecuteAsync(card);
            await Task.Delay(500);
        }

        switch (route)
        {
            case NrMethodDlss5Tool:
                await InstallDlss5ToolAsync(card, statusBtn, addonVersionCombo, nrVersionCombo, rdx5Svc, dlssSvc, addonSvc);
                break;
            case NrMethodDlss5ToolBridge:
                await InstallDlss5ToolAsync(card, statusBtn, addonVersionCombo, nrVersionCombo, rdx5Svc, dlssSvc, addonSvc);
                await InstallBridgeAddonAsync(card, statusBtn, addonSvc, packVersionCombo);
                {
                    var files = RhiInstallManifest.GetComponentFiles(installPath, "Dlss5Tool").ToList();
                    if (!files.Contains(BridgeDeployFile, StringComparer.OrdinalIgnoreCase)) files.Add(BridgeDeployFile);
                    RhiInstallManifest.SetComponent(installPath, "Dlss5Tool", files);
                }
                break;
            case NrMethodShortFuse:
                await InstallShortFuseAsync(card, statusBtn, addonVersionCombo, rdx5Svc, dlssSvc);
                break;
            default:
                throw new InvalidOperationException($"Neural Rendering route '{route}' is not installed automatically.");
        }

        if (_window.ViewModel.GetNrCostScalerEnabled(gameName, store))
        {
            var cs = App.Services.GetRequiredService<DlssNrCostScalerService>();
            if (cs.IsStagingReady) cs.Install(installPath);
        }
        _window.ViewModel.SetNrMethodOverride(gameName, route, store);
        RhiInstallManifest.SetNrMethod(installPath, route);

        // DLSS5 Tool / ShortFuse conflict with the same addons enabled globally — same clean-up as the Install button.
        var conflicting = new[] { "DLSS5 Tool", "DLSS Tool (ShortFuse)" };
        var globalAddons = _window.ViewModel.Settings.EnabledGlobalAddons;
        bool removedAny = false;
        foreach (var c in conflicting)
            if (globalAddons.RemoveAll(a => a.Equals(c, StringComparison.OrdinalIgnoreCase)) > 0) removedAny = true;
        if (removedAny) _window.ViewModel.SaveSettingsPublic();
        RemoveNrConflictingAddonsFromPerGameSelection(gameName, store, conflicting);
        _window.ViewModel.DeployAddonsForCard(gameName);
    }

    // ── Undo ──────────────────────────────────────────────────────────────────────

    public bool HasRestorePoint(GameCardViewModel card)
        => OptStore.Get(OptimizationEvaluator.GameKey(card.GameName, card.Source))?.RestorePoint != null;

    public async Task<OneClickOutcome> UndoOptimizationAsync(GameCardViewModel card, Action<string> progress)
    {
        var name = card.GameName;
        var store = card.Source ?? "";
        var key = OptimizationEvaluator.GameKey(name, store);
        var details = new List<string>();
        var rec = OptStore.Get(key);
        var rp = rec?.RestorePoint;
        if (rp == null) return new OneClickOutcome(false, "There is no optimization to undo for this game.", details, Array.Empty<PreflightIssue>());

        var installPath = card.InstallPath ?? "";
        if (!Directory.Exists(installPath))
            return new OneClickOutcome(false, "The game folder no longer exists — nothing was changed.", details, Array.Empty<PreflightIssue>());
        if (await Task.Run(() => GameOptimizationService.IsGameRunning(installPath)))
            return new OneClickOutcome(false, "The game is running — close it first. Nothing was changed.", details, Array.Empty<PreflightIssue>());

        try
        {
            // Order matters: remove what the optimize installed first (it restores its own DLL backups), then let the
            // state-based DLL plan fix whatever is still different, then the NVIDIA profile.
            if (!string.IsNullOrEmpty(rp.NrMethodInstalledByOptimize))
            {
                progress("Removing Neural Rendering…");
                await RemoveNrRouteForUndoAsync(card, rp.NrMethodInstalledByOptimize!);
                _window.ViewModel.SetNrMethodOverride(name, rp.NrStoredMethodBefore, store);
                details.Add($"Neural Rendering ({rp.NrMethodInstalledByOptimize}) removed.");
                if (rp.ReShadeInstalledByOptimize)
                    details.Add("Note: ReShade, which was installed for Neural Rendering, was left installed (remove it from the game's ReShade section if you do not want it).");
            }

            progress("Restoring DLLs…");
            var fresh = await Task.Run(() => _dlssStreamlineService.Detect(installPath));
            card.ApplyDlssDetection(fresh);
            details.AddRange(await OptSvc.UndoDllsAsync(card, rp));

            progress("Restoring NVIDIA profile…");
            details.AddRange(await Task.Run(() => OptSvc.UndoProfile(card, rp)));

            OptStore.Remove(key);   // restore point consumed; the game is back to "not optimized by RHI"
            _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(card));
            CrashReporter.Log($"[OneClickOptimize] '{name}' undone: {string.Join(" | ", details)}");
            return new OneClickOutcome(true, "Optimization undone — the game is back to how it was before.", details, Array.Empty<PreflightIssue>());
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[OneClickOptimize] Undo of '{name}' failed — {ex}");
            return new OneClickOutcome(false, $"Undo failed part-way: {ex.Message}. The restore point was kept; you can try again.", details, Array.Empty<PreflightIssue>());
        }
    }

    /// <summary>Same removal as the Neural Rendering “Remove” button for the methods One-Click can install.</summary>
    private async Task RemoveNrRouteForUndoAsync(GameCardViewModel card, string method)
    {
        var installPath = card.InstallPath!;
        var gameName = card.GameName;
        var store = card.Source ?? "";
        var rdx5Svc = App.Services.GetRequiredService<Renodx5AddonService>();

        await Task.Run(() =>
        {
            var cs = App.Services.GetRequiredService<DlssNrCostScalerService>();
            cs.Uninstall(installPath);
            _window.ViewModel.SetNrCostScalerEnabled(gameName, false, store);

            switch (method)
            {
                case NrMethodDlss5Tool:
                    rdx5Svc.Uninstall(installPath);
                    RestoreDlssDllsWithSentinel(card, _dlssStreamlineService);
                    break;
                case NrMethodDlss5ToolBridge:
                    rdx5Svc.Uninstall(installPath);
                    RemoveAddonFile(installPath, BridgeDeployFile, "OneClick.Undo.Bridge");
                    RestoreDlssDllsWithSentinel(card, _dlssStreamlineService);
                    break;
                case NrMethodShortFuse:
                {
                    var det = _dlssStreamlineService.Detect(installPath);
                    rdx5Svc.UninstallSf(installPath, det.HasAny ? det : null);
                    _window.ViewModel.RevertSfAutoConfig(card);
                    break;
                }
                default:
                    throw new InvalidOperationException($"Cannot undo Neural Rendering method '{method}' automatically.");
            }
            _window.ViewModel.SetNrMethodOverride(gameName, null, store);
            RhiInstallManifest.SetNrMethod(installPath, null);

            var conflicting = new[] { "DLSS5 Tool", "DLSS Tool (ShortFuse)" };
            var globalAddons = _window.ViewModel.Settings.EnabledGlobalAddons;
            bool removedGlobal = false;
            foreach (var c in conflicting)
                if (globalAddons.RemoveAll(a => a.Equals(c, StringComparison.OrdinalIgnoreCase)) > 0) removedGlobal = true;
            if (removedGlobal) _window.ViewModel.SaveSettingsPublic();
            RemoveNrConflictingAddonsFromPerGameSelection(gameName, store, conflicting);
        });
    }
}
