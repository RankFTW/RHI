// MainWindow.Tune.cs -- the Tune button next to a game's name (Classic+ here; Tabs / Spotlight in DetailLayoutHost.Header)
// and what it does: pick a preset (menu when several are shown), show the plan (what changes, what's skipped and why),
// run it (installs through the normal install flows, then DLSS / Streamline swaps, then NVIDIA settings) and record
// exactly what Tune changed. Clicking a tuned game's button puts back only that. Built in C# (XAML constraint).

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    private Button? _classicTuneBtn;
    private readonly HashSet<string> _tuneBusyGames = new(StringComparer.OrdinalIgnoreCase);

    internal static readonly TabsTheme.Look TuneLook = TabsTheme.BlueButton;
    internal static readonly TabsTheme.Look TunedLook = TabsTheme.GreenButton;

    /// <summary>What the button shows for a game: visible, text, tooltip, tuned.</summary>
    internal (bool Visible, string Text, string Tooltip, bool Tuned, bool Busy) TuneButtonState(GameCardViewModel? card)
    {
        if (card == null || !ViewModel.TunePresetsOn) return (false, "", "", false, false);
        var key = MainViewModel.DisplayModeKey(card);
        bool busy = _tuneBusyGames.Contains(key);
        var st = ViewModel.TunedStateFor(card);
        if (st != null)
            return (true, busy ? "Working..." : "Tuned", busy ? "Working on this game..." :
                $"Tuned with {st.PresetName}. Click to remove what tuning added and put back what it changed.", true, busy);
        var visible = ViewModel.TunePresetStore.Visible;
        if (visible.Count == 0) return (false, "", "", false, false);
        string tip = visible.Count == 1
            ? $"Tune this game with {visible[0].Name}: installs and applies everything in the preset in one go. You see the plan first."
            : "Tune this game with one of your presets: installs and applies everything in it in one go. You see the plan first.";
        return (true, busy ? "Working..." : "Tune", busy ? "Working on this game..." : tip, false, busy);
    }

    /// <summary>Puts the Tune button after the Classic+ header's game settings cog, once.</summary>
    internal void EnsureClassicTuneButton()
    {
        if (_classicTuneBtn != null) return;
        if (DetailGameName.Parent is not Grid row) return; // EnsureClassicGameSettingsButton made it
        var btn = new Button { Content = "Tune", Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        TabsTheme.Apply(btn, TuneLook, height: 28, fontSize: 12);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(btn, "Tune");
        btn.Click += async (s, e) =>
        {
            try { if (btn.Tag is GameCardViewModel c) await TuneButtonClickAsync(btn, c); }
            catch (Exception ex) { CrashReporter.Log($"[MainWindow.Tune] Classic button — {ex.GetType().Name}: {ex.Message}"); }
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(btn, row.ColumnDefinitions.Count - 1);
        row.Children.Add(btn);
        _classicTuneBtn = btn;
    }

    private bool? _classicTuneLookTuned;

    internal void RefreshClassicTuneButton(GameCardViewModel? card)
    {
        HookTuneEvents();
        EnsureClassicTuneButton();
        if (_classicTuneBtn == null) return;
        var (visible, text, tip, tuned, busy) = TuneButtonState(card);
        _classicTuneBtn.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible) return;
        _classicTuneBtn.Tag = card;
        _classicTuneBtn.Content = text;
        _classicTuneBtn.IsEnabled = !busy;
        if (_classicTuneLookTuned != tuned)
        {
            _classicTuneLookTuned = tuned;
            TabsTheme.Apply(_classicTuneBtn, tuned ? TunedLook : TuneLook, height: 28, fontSize: 12);
        }
        ToolTipService.SetToolTip(_classicTuneBtn, tip);
    }

    /// <summary>Redraws every Tune button for the shown game (Classic+ and the Tabs / Spotlight header).</summary>
    internal void RefreshTuneButtons()
    {
        var card = _detailPanelBuilder.CurrentDetailCard;
        RefreshClassicTuneButton(card);
        _detailPanelBuilder.Layout.RefreshHeaderTune(card);
    }

    private bool _tuneEventsHooked;

    private void HookTuneEvents()
    {
        if (_tuneEventsHooked) return;
        _tuneEventsHooked = true;
        ViewModel.TunePresetsChanged += () => DispatcherQueue.TryEnqueue(RefreshTuneButtons);
    }

    // ── Click ───────────────────────────────────────────────────────────────────

    internal async Task TuneButtonClickAsync(FrameworkElement anchor, GameCardViewModel card)
    {
        HookTuneEvents();
        var key = MainViewModel.DisplayModeKey(card);
        if (_tuneBusyGames.Contains(key)) return;
        var xamlRoot = anchor.XamlRoot ?? Content.XamlRoot;

        var st = ViewModel.TunedStateFor(card);
        if (st != null)
        {
            await UntuneAsync(card, st, xamlRoot);
            return;
        }

        var presets = ViewModel.TunePresetStore.Visible;
        if (presets.Count == 0) return;
        if (presets.Count == 1)
        {
            await TuneAsync(card, presets[0], xamlRoot);
            return;
        }

        // Several presets: a menu under the button
        var menu = new MenuFlyout();
        foreach (var p in presets)
        {
            var preset = p;
            var item = new MenuFlyoutItem { Text = preset.Name };
            item.Click += async (s, e) =>
            {
                try { await TuneAsync(card, preset, xamlRoot); }
                catch (Exception ex) { CrashReporter.Log($"[MainWindow.Tune] Menu — {ex.GetType().Name}: {ex.Message}"); }
            };
            menu.Items.Add(item);
        }
        menu.Items.Add(new MenuFlyoutSeparator());
        var manage = new MenuFlyoutItem { Text = "Edit presets..." };
        manage.Click += (s, e) => OpenTunePresetSettings();
        menu.Items.Add(manage);
        menu.ShowAt(anchor);
    }

    /// <summary>Opens Settings at the Tune presets section.</summary>
    internal void OpenTunePresetSettings()
    {
        try
        {
            SettingsButton_Click(this, new RoutedEventArgs());
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => _settingsHandler.JumpToTunePresets());
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            CrashReporter.Log($"[MainWindow.OpenTunePresetSettings] {ex.Message}");
        }
    }

    private void SetTuneBusy(GameCardViewModel card, bool busy)
    {
        var key = MainViewModel.DisplayModeKey(card);
        if (busy) _tuneBusyGames.Add(key); else _tuneBusyGames.Remove(key);
        RefreshTuneButtons();
    }

    // ── Tune ────────────────────────────────────────────────────────────────────

    private async Task TuneAsync(GameCardViewModel card, TunePreset preset, XamlRoot? xamlRoot)
    {
        CrashReporter.Log($"[MainWindow.TuneAsync] '{card.GameName}' with '{preset.Name}'");
        SetTuneBusy(card, true);
        TunePlan plan;
        try
        {
            var facts = await ViewModel.BuildTuneFactsAsync(card, preset);
            if (facts.Blocker == null) await AddExtraFactsAsync(card, preset, facts);
            plan = TunePlanner.Build(preset, facts);
        }
        finally { SetTuneBusy(card, false); }

        foreach (var s in plan.Steps)
            CrashReporter.Log($"[MainWindow.TuneAsync]   {s.Kind} {s.Id}: {(s.Runs ? "run " + s.Detail : "skip — " + s.Skip)}");

        if (plan.Blocker != null)
        {
            await ShowTuneMessageAsync($"Can't tune {card.GameName}", plan.Blocker, xamlRoot);
            return;
        }
        if (!plan.Running.Any())
        {
            await ShowTuneMessageAsync($"{card.GameName} is already tuned",
                $"Nothing in {preset.Name} needs doing for this game:\n\n" + string.Join("\n", plan.Skipped.Select(s => $"{s.Label}: {s.Skip}")), xamlRoot);
            return;
        }
        if (!await ConfirmTunePlanAsync(card, preset, plan, xamlRoot)) return;

        var state = new TunedGameState { PresetId = preset.Id, PresetName = preset.Name, TunedUtc = DateTime.UtcNow };
        var done = new List<string>();
        var failed = new List<string>();
        SetTuneBusy(card, true);
        try
        {
            using (ViewModel.BeginActivity($"Tuning {card.GameName}..."))
            {
                // Components, in the plan's order (ReShade first)
                foreach (var step in plan.Running.Where(s => s.Kind == TuneStepKind.Install))
                {
                    if (IsShuttingDown) return;
                    ShowStatusBarMessage($"Tuning {card.GameName}: installing {step.Label}...", TimeSpan.FromMinutes(5));
                    if (step.Id == TuneComponentIds.NeuralRendering)
                    {
                        await TuneNeuralRenderingAsync(card, preset, step, state, done, failed);
                        continue;
                    }
                    if (TuneComponentIds.IsExtra(step.Id))
                    {
                        await TuneExtraAsync(card, step, state, done, failed, xamlRoot);
                        continue;
                    }
                    if (IsComponentInstalled(card, step.Id)) continue; // came with an earlier step (Luma's ReShade)
                    try
                    {
                        if (step.Id == BackupComponents.OptiScaler && !TuneOsDllName(card, step.Target, state, failed)) continue;
                        if (!await InstallForTuneAsync(card, step.Id, xamlRoot))
                        {
                            failed.Add($"{step.Label}: cancelled");
                            continue;
                        }
                        // Some installs (ReLimiter, Display Commander) set the card's status through a queued UI update
                        await FlushDispatcherAsync();
                        if (IsComponentInstalled(card, step.Id))
                        {
                            state.Installed.Add(step.Id);
                            done.Add($"Installed {step.Label}");
                        }
                        else failed.Add($"{step.Label}: didn't install (the game's page shows why)");
                    }
                    catch (Exception ex)
                    {
                        CrashReporter.Log($"[MainWindow.TuneAsync] {step.Label} — {ex.GetType().Name}: {ex.Message}");
                        failed.Add($"{step.Label}: {ex.Message}");
                    }
                }

                // DLSS / Streamline files
                foreach (var step in plan.Running.Where(s => s.Kind == TuneStepKind.Dll))
                {
                    if (IsShuttingDown) return;
                    ShowStatusBarMessage($"Tuning {card.GameName}: {step.Label} {step.Target}...", TimeSpan.FromMinutes(5));
                    var (rec, err) = await ViewModel.ApplyTuneDllAsync(card, step.Id, step.Target);
                    if (rec == null && err == null) continue; // an earlier step (Neural Rendering) already put that version in
                    if (rec != null)
                    {
                        state.Dlls[step.Id] = rec;
                        done.Add($"{step.Label} {step.Detail}");
                    }
                    else failed.Add($"{step.Label}: {err}");
                }

                // NVIDIA settings
                var nv = plan.Running.Where(s => s.Kind == TuneStepKind.Nvidia).ToList();
                if (nv.Count > 0)
                {
                    ShowStatusBarMessage($"Tuning {card.GameName}: NVIDIA settings...", TimeSpan.FromMinutes(2));
                    var (before, errors) = await ViewModel.ApplyTuneNvidiaAsync(card, nv.Select(s => (s.Id, s.Target)).ToList());
                    foreach (var (k, v) in before) state.NvidiaBefore[k] = v;
                    foreach (var s in nv.Where(s => before.ContainsKey(s.Id))) done.Add($"{s.Label}: {s.Detail}");
                    failed.AddRange(errors);
                }
            }
        }
        finally
        {
            bool changed = state.Installed.Count > 0 || state.Dlls.Count > 0 || state.NvidiaBefore.Count > 0;
            if (changed) ViewModel.SaveTunedState(card, state);
            SetTuneBusy(card, false);
            CrashReporter.Log($"[MainWindow.TuneAsync] '{card.GameName}' done: {done.Count} changed, {failed.Count} failed, tuned={changed}"
                + (state.Installed.Count > 0 ? $"; installed {string.Join(", ", state.Installed)}" : ""));
            foreach (var f in failed) CrashReporter.Log($"[MainWindow.TuneAsync]   failed: {f}");
        }

        ShowStatusBarMessage(failed.Count == 0 ? $"Tuned {card.GameName}." : $"Tuned {card.GameName} with {failed.Count} problem{(failed.Count == 1 ? "" : "s")}.", TimeSpan.FromSeconds(8));
        // Summary first: the DLSS / Driver Settings sections don't draw while a dialog is open (freeze guard)
        await ShowTuneSummaryAsync(card, done, failed, plan.Skipped.Select(s => $"{s.Label}: {s.Skip}").ToList(), tuning: true, xamlRoot);
        RefreshAfterTune(card);
    }

    /// <summary>Lets updates the installs queued on the UI thread run before the card is checked.</summary>
    private Task FlushDispatcherAsync()
    {
        var tcs = new TaskCompletionSource();
        if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => tcs.TrySetResult()))
            tcs.TrySetResult();
        return tcs.Task;
    }

    /// <summary>Installs one component the way its button does (incl. its warnings). False = the user cancelled.</summary>
    private async Task<bool> InstallForTuneAsync(GameCardViewModel card, string id, XamlRoot? xamlRoot)
    {
        var anchor = (FrameworkElement?)_classicTuneBtn ?? (FrameworkElement)Content;
        switch (id)
        {
            case BackupComponents.ReLimiter:
                if (!await _installEventHandler.ConfirmUlV4WarningsAsync(card, anchor)) return false;
                if (!await _installEventHandler.ConfirmLimiterWithFrameWarpAsync(card, anchor, "ReLimiter")) return false;
                break;
            case BackupComponents.DisplayCommander:
                if (!await _installEventHandler.ConfirmLimiterWithFrameWarpAsync(card, anchor, "Display Commander")) return false;
                break;
            case BackupComponents.OptiScaler:
                // The button's flow (install warning, first-time setup notice, DXVK / ReShade handling)
                await _installEventHandler.InstallOptiScalerAsync(card, anchor);
                return true;
        }
        await InstallForRestoreAsync(card, id);
        return true;
    }

    private void RefreshAfterTune(GameCardViewModel card)
    {
        ViewModel.SaveLibraryPublic();
        if (ReferenceEquals(_detailPanelBuilder.CurrentDetailCard, card))
        {
            PopulateDetailPanel(card);
            BuildOverridesPanel(card);
        }
        RefreshTuneButtons();
    }

    /// <summary>Extras the preset installs: installed now, why not, and the DLL name / variant / version to use.</summary>
    private async Task AddExtraFactsAsync(GameCardViewModel card, TunePreset preset, TuneGameFacts facts)
    {
        if (preset.Extras == null || preset.Extras.Count == 0) return;
        var ids = preset.Extras.Keys.Where(id => TuneComponentIds.ExtraOrder.Contains(id, StringComparer.OrdinalIgnoreCase)).ToList();
        var installed = await Task.Run(() => ids.Where(id => _detailPanelBuilder.TuneExtraInstalled(card, id)).ToList());
        foreach (var id in installed) facts.ExtrasInstalled.Add(id);
        foreach (var id in ids)
        {
            try
            {
                // "Needs ReShade" doesn't count when the preset installs ReShade first (checked again just before the install)
                if (_detailPanelBuilder.TuneExtraBlocked(card, id) is { } why
                    && !(preset.ReShade && !card.IsRsInstalled && why.Contains("ReShade", StringComparison.OrdinalIgnoreCase)))
                    facts.ExtrasBlocked[id] = why;
                facts.ExtrasOption[id] = _detailPanelBuilder.TuneExtraResolveOption(card, id, preset.Extras[id] ?? "");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                CrashReporter.Log($"[MainWindow.AddExtraFactsAsync] {id} — {ex.Message}");
                facts.ExtrasBlocked[id] = ex.Message;
            }
        }
    }

    /// <summary>Installs one Extra through its row's install code (its prompts included, pickers left out).</summary>
    private async Task TuneExtraAsync(GameCardViewModel card, TuneStep step, TunedGameState state, List<string> done, List<string> failed, XamlRoot? xamlRoot)
    {
        var id = TuneComponentIds.ExtraOf(step.Id);
        try
        {
            // An extra installed earlier in this run can rule this one out (e.g. two frame generation unlocks)
            if (_detailPanelBuilder.TuneExtraBlocked(card, id) is { } why)
            {
                failed.Add($"{step.Label}: {why}");
                return;
            }
            var settingBefore = _detailPanelBuilder.GetTuneExtraSetting(card, id);
            var err = await _detailPanelBuilder.InstallExtraForTuneAsync(card, id, step.Target, xamlRoot);
            await FlushDispatcherAsync();
            bool now = await Task.Run(() => _detailPanelBuilder.TuneExtraInstalled(card, id));
            if (now)
            {
                state.Installed.Add(step.Id);
                if (id is "DXVK" or "dgVoodoo2") state.SettingsBefore[TuneSettingKeys.ExtraPrefix + id] = settingBefore ?? "";
                done.Add($"Installed {step.Label}" + (string.IsNullOrEmpty(step.Detail) ? "" : $" ({step.Detail})"));
            }
            else
            {
                // Put back a variant / version the install set, since nothing was installed
                if (id is "DXVK" or "dgVoodoo2") _detailPanelBuilder.SetTuneExtraSetting(card, id, settingBefore);
                failed.Add($"{step.Label}: {(err == "Cancelled" ? "cancelled" : err ?? "didn't install (the Extras section shows why)")}");
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.TuneExtraAsync] {id} — {ex.GetType().Name}: {ex.Message}");
            failed.Add($"{step.Label}: {ex.Message}");
        }
    }

    /// <summary>Sets OptiScaler's DLL name for the game before it installs (remembering the old one). False = can't.</summary>
    private bool TuneOsDllName(GameCardViewModel card, string name, TunedGameState state, List<string> failed)
    {
        if (string.IsNullOrEmpty(name)) return true;
        var current = ViewModel.GetDllOverride(card.GameName)?.OsFileName ?? "";
        if (current.Equals(name, StringComparison.OrdinalIgnoreCase)) return true;
        if (!_detailPanelBuilder.ApplyOsDllName(card, card.GameName, name))
        {
            failed.Add($"OptiScaler: {name} is used by ReShade or Display Commander in this game");
            return false;
        }
        state.SettingsBefore[TuneSettingKeys.OsDllName] = current;
        CrashReporter.Log($"[MainWindow.TuneOsDllName] '{card.GameName}': OptiScaler DLL name '{current}' → '{name}'");
        return true;
    }

    /// <summary>Installs the preset's Neural Rendering method with its versions, remembering the game's old choices.</summary>
    private async Task TuneNeuralRenderingAsync(GameCardViewModel card, TunePreset preset, TuneStep step, TunedGameState state,
        List<string> done, List<string> failed)
    {
        string store = card.Source ?? "";
        bool auto = preset.NrMethod.Equals(TuneNr.Auto, StringComparison.OrdinalIgnoreCase);
        var before = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [TuneSettingKeys.NrAddonVersion] = ViewModel.GetNrAddonVersion(card.GameName, store) ?? "",
            [TuneSettingKeys.NrDllVersion] = ViewModel.GetNrDllVersion(card.GameName, store) ?? "",
            [TuneSettingKeys.NrMethod] = ViewModel.GetNrMethodOverride(card.GameName, store) ?? "",
        };
        try
        {
            bool ok = await _detailPanelBuilder.InstallNrForTuneAsync(card, step.Target,
                auto ? "" : preset.NrAddonVersion ?? "", preset.NrDllVersion ?? "");
            await FlushDispatcherAsync(); // the card's DLSS detection is refreshed on the UI thread (the DLSS steps read it)
            foreach (var (k, v) in before) state.SettingsBefore[k] = v; // version choices were saved either way
            if (ok)
            {
                state.Installed.Add(TuneComponentIds.NeuralRendering);
                state.NrMethodInstalled = step.Target;
                done.Add($"Installed Neural Rendering ({step.Detail})");
            }
            else failed.Add($"Neural Rendering: {DetailPanelBuilder.NrMethodLabel(step.Target)} didn't install (the Neural Rendering section shows why)");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.TuneNeuralRenderingAsync] {ex.GetType().Name}: {ex.Message}");
            failed.Add($"Neural Rendering: {ex.Message}");
        }
    }

    // ── Untune ──────────────────────────────────────────────────────────────────

    private async Task UntuneAsync(GameCardViewModel card, TunedGameState st, XamlRoot? xamlRoot)
    {
        if (card.IsRunning)
        {
            await ShowTuneMessageAsync($"Can't remove tuning from {card.GameName}", "The game is running. Close it first.", xamlRoot);
            return;
        }
        var lines = TunePlanner.DescribeUndo(st);
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(new TextBlock
        {
            Text = $"{card.GameName} was tuned with {st.PresetName} on {st.TunedUtc.ToLocalTime():d MMM yyyy}. "
                 + "This removes what tuning added and puts back what it changed. Anything you installed or changed yourself stays.",
            TextWrapping = TextWrapping.Wrap,
        });
        body.Children.Add(BulletList(lines.Count > 0 ? lines : new List<string> { "Nothing to put back" }));
        var dialog = new ContentDialog
        {
            Title = "Remove tuning?",
            Content = new ScrollViewer { Content = body, MaxHeight = 460, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            PrimaryButtonText = "Remove tuning",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot,
        };
        if (await DialogService.ShowSafeAsync(dialog) != ContentDialogResult.Primary) return;

        CrashReporter.Log($"[MainWindow.UntuneAsync] '{card.GameName}' ({st.PresetName})");
        var done = new List<string>();
        var problems = new List<string>();
        SetTuneBusy(card, true);
        try
        {
            using (ViewModel.BeginActivity($"Removing tuning from {card.GameName}..."))
            {
                // NVIDIA settings
                if (st.NvidiaBefore.Count > 0)
                {
                    var errs = await ViewModel.RevertTuneNvidiaAsync(card, st.NvidiaBefore);
                    problems.AddRange(errs);
                    if (errs.Count < st.NvidiaBefore.Count) done.Add("NVIDIA settings put back");
                }

                // DLSS / Streamline files
                foreach (var (slot, d) in st.Dlls)
                {
                    var err = await ViewModel.RevertTuneDllAsync(card, slot, d);
                    if (err == null) done.Add($"{TuneDllSlots.Label(slot)} put back");
                    else problems.Add(err);
                }

                // Components, newest first (limiter and mod before ReShade)
                foreach (var id in Enumerable.Reverse(st.Installed))
                {
                    string label = TuneComponentIds.Label(id);
                    if (id == TuneComponentIds.NeuralRendering)
                    {
                        await UntuneNeuralRenderingAsync(card, st, done, problems);
                        continue;
                    }
                    if (TuneComponentIds.IsExtra(id))
                    {
                        var extra = TuneComponentIds.ExtraOf(id);
                        if (!await Task.Run(() => _detailPanelBuilder.TuneExtraInstalled(card, extra))) continue; // removed by hand
                        try
                        {
                            var err = await _detailPanelBuilder.UninstallExtraForTuneAsync(card, extra);
                            await FlushDispatcherAsync();
                            if (err == null && !await Task.Run(() => _detailPanelBuilder.TuneExtraInstalled(card, extra))) done.Add($"Removed {label}");
                            else problems.Add($"{label}: {err ?? "wasn't removed (the Extras section shows why)"}");
                        }
                        catch (Exception ex)
                        {
                            CrashReporter.Log($"[MainWindow.UntuneAsync] {label} — {ex.GetType().Name}: {ex.Message}");
                            problems.Add($"{label}: {ex.Message}");
                        }
                        continue;
                    }
                    if (!IsComponentInstalled(card, id)) continue; // already removed by hand
                    if (id == BackupComponents.ReShade && card.RequiresVulkanInstall)
                    {
                        problems.Add("ReShade kept: in Vulkan games it's shared by every Vulkan game (remove it from the ReShade row if you want)");
                        continue;
                    }
                    try
                    {
                        UninstallForTune(card, id);
                        await FlushDispatcherAsync();
                        if (!IsComponentInstalled(card, id)) done.Add($"Removed {label}");
                        else problems.Add($"{label} wasn't removed (the game's page shows why)");
                    }
                    catch (Exception ex)
                    {
                        CrashReporter.Log($"[MainWindow.UntuneAsync] {label} — {ex.GetType().Name}: {ex.Message}");
                        problems.Add($"{label}: {ex.Message}");
                    }
                }

                RestoreTuneSettings(card, st);
            }
        }
        finally
        {
            ViewModel.ClearTunedState(card);
            SetTuneBusy(card, false);
            CrashReporter.Log($"[MainWindow.UntuneAsync] '{card.GameName}' done: {done.Count} put back, {problems.Count} left");
            foreach (var p in problems) CrashReporter.Log($"[MainWindow.UntuneAsync]   left: {p}");
        }

        ShowStatusBarMessage(problems.Count == 0 ? $"Removed tuning from {card.GameName}." : $"Removed tuning from {card.GameName}; some things were left.", TimeSpan.FromSeconds(8));
        if (problems.Count > 0)
            await ShowTuneSummaryAsync(card, done, problems, new List<string>(), tuning: false, xamlRoot);
        RefreshAfterTune(card); // after the dialog (freeze guard skips sections while one is open)
    }

    private async Task UntuneNeuralRenderingAsync(GameCardViewModel card, TunedGameState st, List<string> done, List<string> problems)
    {
        var method = st.NrMethodInstalled;
        if (method == null) return;
        var path = card.InstallPath ?? "";
        bool is32 = card.Is32Bit;
        var now = await Task.Run(() => DetailPanelBuilder.DetectInstalledNrMethod(path, is32));
        if (now == null) return; // removed by hand
        if (now != method)
        {
            problems.Add($"Neural Rendering kept: it's {DetailPanelBuilder.NrMethodLabel(now)} now, not what tuning installed");
            return;
        }
        try
        {
            await _detailPanelBuilder.RemoveNrForTuneAsync(card, method);
            var after = await Task.Run(() => DetailPanelBuilder.DetectInstalledNrMethod(path, is32));
            if (after == null) done.Add($"Removed Neural Rendering ({DetailPanelBuilder.NrMethodLabel(method)})");
            else problems.Add("Neural Rendering wasn't fully removed (the Neural Rendering section shows what's left)");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.UntuneNeuralRenderingAsync] {ex.GetType().Name}: {ex.Message}");
            problems.Add($"Neural Rendering: {ex.Message}");
        }
    }

    /// <summary>Puts back the per-game choices Tune changed (OptiScaler DLL name, Neural Rendering versions and method).</summary>
    private void RestoreTuneSettings(GameCardViewModel card, TunedGameState st)
    {
        string store = card.Source ?? "";
        static string? Null(string v) => string.IsNullOrEmpty(v) ? null : v;
        foreach (var (key, value) in st.SettingsBefore)
        {
            switch (key)
            {
                case TuneSettingKeys.OsDllName:
                    if (!card.IsOsInstalled)
                        _detailPanelBuilder.ApplyOsDllName(card, card.GameName, string.IsNullOrEmpty(value) ? DllOverrideService.PlaceholderName : value);
                    break;
                case TuneSettingKeys.NrAddonVersion: ViewModel.SetNrAddonVersion(card.GameName, Null(value), store); break;
                case TuneSettingKeys.NrDllVersion: ViewModel.SetNrDllVersion(card.GameName, Null(value), store); break;
                case TuneSettingKeys.NrMethod: ViewModel.SetNrMethodOverride(card.GameName, Null(value), store); break;
                default:
                    if (key.StartsWith(TuneSettingKeys.ExtraPrefix, StringComparison.Ordinal))
                        _detailPanelBuilder.SetTuneExtraSetting(card, key[TuneSettingKeys.ExtraPrefix.Length..], Null(value));
                    break;
            }
        }
        if (st.SettingsBefore.Count > 0)
            CrashReporter.Log($"[MainWindow.RestoreTuneSettings] '{card.GameName}': {string.Join(", ", st.SettingsBefore.Select(kv => $"{kv.Key}='{kv.Value}'"))}");
    }

    private void UninstallForTune(GameCardViewModel card, string id)
    {
        switch (id)
        {
            case BackupComponents.OptiScaler: _installEventHandler.UninstallOptiScaler(card); break;
            case BackupComponents.ReLimiter: ViewModel.UninstallUl(card); break;
            case BackupComponents.DisplayCommander: ViewModel.UninstallDc(card); break;
            case BackupComponents.RenoDx: ViewModel.UninstallModCommand.Execute(card); break;
            case BackupComponents.Luma: ViewModel.UninstallLumaCommand.Execute(card); break;
            case BackupComponents.ReShade: ViewModel.UninstallReShadeCommand.Execute(card); break;
        }
    }

    // ── Dialogs ─────────────────────────────────────────────────────────────────

    private async Task<bool> ConfirmTunePlanAsync(GameCardViewModel card, TunePreset preset, TunePlan plan, XamlRoot? xamlRoot)
    {
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(new TextBlock
        {
            Text = $"{preset.Name} will do this in {card.GameName}. Clicking the game's Tuned button later removes it again.",
            TextWrapping = TextWrapping.Wrap,
        });
        body.Children.Add(Heading("Changes"));
        body.Children.Add(BulletList(plan.Running.Select(s => s.Kind == TuneStepKind.Install
            ? $"Install {s.Label}" : string.IsNullOrEmpty(s.Detail) ? s.Label : $"{s.Label}: {s.Detail}").ToList()));
        var skipped = plan.Skipped.ToList();
        if (skipped.Count > 0)
        {
            body.Children.Add(Heading("Skipped"));
            body.Children.Add(BulletList(skipped.Select(s => $"{s.Label}: {s.Skip}").ToList(), dim: true));
        }
        var dialog = new ContentDialog
        {
            Title = $"Tune {card.GameName}",
            Content = new ScrollViewer { Content = body, MaxHeight = 460, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            PrimaryButtonText = "Tune",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
        };
        return await DialogService.ShowSafeAsync(dialog) == ContentDialogResult.Primary;
    }

    private async Task ShowTuneSummaryAsync(GameCardViewModel card, List<string> done, List<string> failed, List<string> skipped, bool tuning, XamlRoot? xamlRoot)
    {
        var body = new StackPanel { Spacing = 8 };
        if (done.Count > 0) { body.Children.Add(Heading(tuning ? "Done" : "Put back")); body.Children.Add(BulletList(done)); }
        if (failed.Count > 0) { body.Children.Add(Heading(tuning ? "Didn't work" : "Left as it is")); body.Children.Add(BulletList(failed)); }
        if (skipped.Count > 0) { body.Children.Add(Heading("Skipped")); body.Children.Add(BulletList(skipped, dim: true)); }
        if (tuning && done.Count == 0)
            body.Children.Add(new TextBlock { Text = "Nothing changed, so the game isn't marked as tuned.", TextWrapping = TextWrapping.Wrap });
        var dialog = new ContentDialog
        {
            Title = tuning ? $"Tuned {card.GameName}" : $"Removed tuning from {card.GameName}",
            Content = new ScrollViewer { Content = body, MaxHeight = 460, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            CloseButtonText = "OK",
            XamlRoot = xamlRoot,
        };
        await DialogService.ShowSafeAsync(dialog);
    }

    private static async Task ShowTuneMessageAsync(string title, string text, XamlRoot? xamlRoot)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new ScrollViewer
            {
                Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
                MaxHeight = 400,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            },
            CloseButtonText = "OK",
            XamlRoot = xamlRoot,
        };
        await DialogService.ShowSafeAsync(dialog);
    }

    private static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Margin = new Thickness(0, 4, 0, 0),
    };

    private static StackPanel BulletList(List<string> lines, bool dim = false)
    {
        var panel = new StackPanel { Spacing = 3 };
        foreach (var l in lines)
            panel.Children.Add(new TextBlock
            {
                Text = "• " + l,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Opacity = dim ? 0.7 : 1.0,
            });
        return panel;
    }
}
