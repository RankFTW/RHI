// MainViewModel.Tune.cs -- Tune presets for the UI: the preset list (add / rename / move / hide / delete / edit, saved
// through SaveNameMappings), each game's tuned record, the facts the planner needs, and the DLL / NVIDIA parts of
// tuning and untuning (component installs are run by MainWindow.Tune.cs, which owns the install flows).
// Data: GameNameService.TunePresets (settings.json "TunePresets").

using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    public TunePresetStore TunePresetStore => _gameNameService.TunePresets;

    /// <summary>True when the Tune button shows on game pages (Settings › Tune presets, on by default).</summary>
    public bool TunePresetsOn => _settingsViewModel.TunePresetsEnabled;

    /// <summary>Raised after presets change or a game is tuned / untuned (callers marshal to the UI thread).</summary>
    public event Action? TunePresetsChanged;

    public void NotifyTunePresetsChanged()
    {
        try { TunePresetsChanged?.Invoke(); }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            CrashReporter.Log($"[MainViewModel.NotifyTunePresetsChanged] {ex.Message}");
        }
    }

    private void TuneChanged()
    {
        SaveNameMappings();
        NotifyTunePresetsChanged();
    }

    public void SetTunePresetsEnabled(bool on)
    {
        if (_settingsViewModel.TunePresetsEnabled == on) return;
        _settingsViewModel.TunePresetsEnabled = on;
        CrashReporter.Log($"[MainViewModel.SetTunePresetsEnabled] {(on ? "On" : "Off")}");
        TuneChanged();
    }

    public string? AddTunePreset(string name, string? copyFrom = null)
    {
        var id = TunePresetStore.Add(name, copyFrom);
        if (id != null) { CrashReporter.Log($"[MainViewModel.AddTunePreset] '{name}' ({id})"); TuneChanged(); }
        return id;
    }

    public bool RenameTunePreset(string id, string name)
    {
        bool ok = TunePresetStore.Rename(id, name);
        if (ok) TuneChanged();
        return ok;
    }

    public void MoveTunePreset(string id, int delta)
    {
        if (TunePresetStore.Move(id, delta)) TuneChanged();
    }

    public void SetTunePresetHidden(string id, bool hidden)
    {
        if (TunePresetStore.SetHidden(id, hidden)) TuneChanged();
    }

    public bool DeleteTunePreset(string id)
    {
        bool ok = TunePresetStore.Delete(id);
        if (ok) { CrashReporter.Log($"[MainViewModel.DeleteTunePreset] {id}"); TuneChanged(); }
        return ok;
    }

    public bool UpdateTunePreset(TunePreset preset)
    {
        bool ok = TunePresetStore.Update(preset);
        if (ok) { CrashReporter.Log($"[MainViewModel.UpdateTunePreset] '{preset.Name}' ({preset.StepCount} steps)"); TuneChanged(); }
        return ok;
    }

    public bool ResetBuiltInTunePreset()
    {
        bool ok = TunePresetStore.ResetBuiltIn();
        if (ok) TuneChanged();
        return ok;
    }

    public TunedGameState? TunedStateFor(GameCardViewModel card) => TunePresetStore.GameState(DisplayModeKey(card));

    public void SaveTunedState(GameCardViewModel card, TunedGameState state)
    {
        TunePresetStore.SetGameState(DisplayModeKey(card), state);
        TuneChanged();
    }

    public void ClearTunedState(GameCardViewModel card)
    {
        if (TunePresetStore.ClearGame(DisplayModeKey(card))) TuneChanged();
    }

    // ── Facts for the planner ────────────────────────────────────────────────

    /// <summary>
    /// What the planner needs about a game. Card values are read here (UI thread); NVIDIA values (current settings,
    /// NVIDIA Override per DLL) on a background thread, with a time limit so a stuck driver can't hold Tune up.
    /// </summary>
    public async Task<TuneGameFacts> BuildTuneFactsAsync(GameCardViewModel card, TunePreset preset)
    {
        string path = card.InstallPath ?? "";
        string? blocker = null;
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) blocker = "The game's folder wasn't found. Refresh the game list or set its folder first.";
        else if (XboxPaths.IsWindowsApps(path)) blocker = "The game is in a protected Game Pass folder (WindowsApps) that RHI can't write to.";
        else if (card.IsRunning) blocker = "The game is running. Close it first.";

        var det = card.DlssDetection;
        var dlls = new Dictionary<string, (bool Present, string? Version, IReadOnlyList<string> Versions)>(StringComparer.OrdinalIgnoreCase)
        {
            [TuneDllSlots.Sr] = (card.HasDlss, card.DlssInstalledVersion, _dlssStreamlineService.DlssVersions),
            [TuneDllSlots.Rr] = (card.HasDlssd, card.DlssdInstalledVersion, _dlssStreamlineService.DlssdVersions),
            [TuneDllSlots.Fg] = (card.HasDlssg, card.DlssgInstalledVersion, _dlssStreamlineService.DlssgVersions),
            [TuneDllSlots.Sl] = (card.HasStreamline && det?.StreamlineFolder != null, card.StreamlineInstalledVersion, _dlssStreamlineService.StreamlineVersions),
        };

        bool nvidiaReady = _dlssPresetService.IsSupported;
        var now = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var overrides = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        if (nvidiaReady && blocker == null)
        {
            var ctx = BuildDisplayModeContext(card);
            var keys = (preset.Nvidia ?? new()).Keys.ToList();
            string gameName = card.GameName;
            // The read fills its own dictionaries: after a time-out it may still be running, so nothing is shared
            var read = Task.Run(() =>
            {
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var ovr = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                var p = _dlssPresetService;
                p.PrimeProfileCache(gameName, path);
                ovr[TuneDllSlots.Sr] = p.IsSrDriverOverrideActive(gameName, path);
                ovr[TuneDllSlots.Rr] = p.IsRrDriverOverrideActive(gameName, path);
                ovr[TuneDllSlots.Fg] = p.IsFgDriverOverrideActive(gameName, path);
                foreach (var key in keys)
                {
                    try
                    {
                        if (TuneItems.Get(key)?.Read(ctx) is { } v) values[key] = v;
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException)
                    {
                        CrashReporter.Log($"[MainViewModel.BuildTuneFactsAsync] Read {key} failed — {ex.Message}");
                    }
                }
                return (values, ovr);
            });
            if (await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(8))) == read)
                (now, overrides) = await read;
            else
                CrashReporter.Log($"[MainViewModel.BuildTuneFactsAsync] NVIDIA reads timed out for '{gameName}'");
        }

        // Neural Rendering: what's installed is a file check (off the UI thread); the rest comes from the card
        string? nrInstalled = null;
        var nrBlocked = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string nrAuto = "";
        if (!string.IsNullOrEmpty(preset.NrMethod) && blocker == null)
        {
            bool is32 = card.Is32Bit;
            nrInstalled = await Task.Run(() => DetailPanelBuilder.DetectInstalledNrMethod(path, is32));
            nrAuto = DetailPanelBuilder.AutoNrMethod(card);
            foreach (var m in DetailPanelBuilder.TuneNrMethods)
                if (DetailPanelBuilder.NrMethodBlocked(card, m) is { } why) nrBlocked[m] = why;
        }

        return new TuneGameFacts
        {
            GameName = card.GameName,
            Blocker = blocker,
            RsInstalled = card.IsRsInstalled,
            RsBlocked = card.IsREEngineGame && !card.IsRefInstalled && !card.ExcludeFromUpdateAllRef
                ? "Install RE Framework first (RE Engine game)" : null,
            NoAddons = card.UseNormalReShade,
            IsVulkan = card.RequiresVulkanInstall,
            HasRenoDxMod = card.Mod != null && !card.IsExternalOnly,
            RdxInstalled = card.IsRdxInstalled,
            HasLumaMod = card.LumaFeatureEnabled && card.LumaMod?.DownloadUrl != null,
            LumaInstalled = card.IsLumaInstalled,
            UlInstalled = card.IsUlInstalled,
            DcInstalled = card.IsDcInstalled,
            OsInstalled = card.IsOsInstalled,
            RsFileName = card.IsRsInstalled ? card.RsInstalledFile : null,
            DcFileName = card.IsDcInstalled ? card.DcInstalledFile : null,
            NrInstalled = nrInstalled,
            NrAuto = nrAuto,
            NrBlocked = nrBlocked,
            Dlls = dlls.ToDictionary(kv => kv.Key,
                kv => new TuneDllFacts(kv.Value.Present, kv.Value.Version,
                    overrides.TryGetValue(kv.Key, out var o) && o, kv.Value.Versions),
                StringComparer.OrdinalIgnoreCase),
            NvidiaReady = nvidiaReady,
            IsAdmin = VulkanLayerService.IsRunningAsAdmin(),
            NvidiaNow = now,
        };
    }

    // ── DLSS / Streamline files ──────────────────────────────────────────────

    private (string? Path, string? Version) TuneDllTarget(GameCardViewModel card, string slot)
    {
        var det = card.DlssDetection;
        return slot switch
        {
            TuneDllSlots.Sr => (det?.DlssPath, card.DlssInstalledVersion),
            TuneDllSlots.Rr => (det?.DlssdPath, card.DlssdInstalledVersion),
            TuneDllSlots.Fg => (det?.DlssgPath, card.DlssgInstalledVersion),
            TuneDllSlots.Sl => (det?.StreamlineFolder, card.StreamlineInstalledVersion),
            _ => (null, null),
        };
    }

    /// <summary>The file whose .original says whether the game was already on a swapped version.</summary>
    private static string? BackupProbe(GameCardViewModel card, string slot, string target)
        => slot == TuneDllSlots.Sl ? card.DlssDetection?.StreamlineInterposerPath : target;

    /// <summary>Swaps one DLL (or the Streamline set) to <paramref name="version"/>. Returns the record, or an error.</summary>
    public async Task<(TunedDll? Record, string? Error)> ApplyTuneDllAsync(GameCardViewModel card, string slot, string version)
    {
        var (target, current) = TuneDllTarget(card, slot);
        if (string.IsNullOrEmpty(target)) return (null, "File not found");
        if (TunePlanner.SameVersion(current, version))
        {
            CrashReporter.Log($"[MainViewModel.ApplyTuneDllAsync] '{card.GameName}' {slot} already {version} — skipped");
            return (null, null);
        }
        var probe = BackupProbe(card, slot, target);
        var record = new TunedDll
        {
            Path = target,
            HadBackup = probe != null && _dlssStreamlineService.HasBackup(probe),
            PreviousVersion = current,
            NewVersion = version,
        };
        try
        {
            switch (slot)
            {
                case TuneDllSlots.Sr: await _dlssStreamlineService.SwapDlssAsync(target, version); break;
                case TuneDllSlots.Rr: await _dlssStreamlineService.SwapDlssdAsync(target, version); break;
                case TuneDllSlots.Fg: await _dlssStreamlineService.SwapDlssgAsync(target, version); break;
                case TuneDllSlots.Sl: await _dlssStreamlineService.SwapStreamlineAsync(target, version); break;
            }
        }
        catch (Exception ex) when (ex is DlssSwapException or IOException or UnauthorizedAccessException or HttpRequestException or TaskCanceledException)
        {
            CrashReporter.Log($"[MainViewModel.ApplyTuneDllAsync] '{card.GameName}' {slot} → {version} failed — {ex.Message}");
            return (null, ex.Message);
        }
        _dlssStreamlineService.RecordDlssFound(card.GameName);
        card.RefreshDlssVersions(_dlssStreamlineService);
        CrashReporter.Log($"[MainViewModel.ApplyTuneDllAsync] '{card.GameName}' {slot}: {current} → {version} (backup before: {record.HadBackup})");
        return (record, null);
    }

    /// <summary>Puts a DLL Tune swapped back. Returns null when done, else why it was left.</summary>
    public async Task<string?> RevertTuneDllAsync(GameCardViewModel card, string slot, TunedDll d)
    {
        string label = TuneDllSlots.Label(slot);
        var (_, current) = TuneDllTarget(card, slot);
        // Changed since tuning (by hand, Update All, a game update): the game keeps what it has now
        if (d.NewVersion != null && current != null && !TunePlanner.SameVersion(current, d.NewVersion))
            return $"{label} left on {current} (changed since tuning)";
        try
        {
            if (!d.HadBackup)
            {
                // The game's own file is in the .original Tune's swap made
                bool restored = slot == TuneDllSlots.Sl
                    ? _dlssStreamlineService.RestoreStreamline(d.Path, card.InstallPath) == 0
                    : _dlssStreamlineService.Restore(d.Path, card.InstallPath);
                if (!restored) return $"{label} kept (another component owns the file)";
            }
            else
            {
                var versions = slot switch
                {
                    TuneDllSlots.Sr => _dlssStreamlineService.DlssVersions,
                    TuneDllSlots.Rr => _dlssStreamlineService.DlssdVersions,
                    TuneDllSlots.Fg => _dlssStreamlineService.DlssgVersions,
                    _ => _dlssStreamlineService.StreamlineVersions,
                };
                var prev = versions.FirstOrDefault(v => TunePlanner.SameVersion(v, d.PreviousVersion));
                if (prev == null) return $"{label} left on {current} ({d.PreviousVersion ?? "the old version"} isn't in the download list)";
                switch (slot)
                {
                    case TuneDllSlots.Sr: await _dlssStreamlineService.SwapDlssAsync(d.Path, prev); break;
                    case TuneDllSlots.Rr: await _dlssStreamlineService.SwapDlssdAsync(d.Path, prev); break;
                    case TuneDllSlots.Fg: await _dlssStreamlineService.SwapDlssgAsync(d.Path, prev); break;
                    case TuneDllSlots.Sl: await _dlssStreamlineService.SwapStreamlineAsync(d.Path, prev); break;
                }
            }
        }
        catch (Exception ex) when (ex is DlssSwapException or IOException or UnauthorizedAccessException or HttpRequestException or TaskCanceledException)
        {
            CrashReporter.Log($"[MainViewModel.RevertTuneDllAsync] '{card.GameName}' {slot} failed — {ex.Message}");
            return $"{label}: {ex.Message}";
        }
        card.RefreshDlssVersions(_dlssStreamlineService);
        CrashReporter.Log($"[MainViewModel.RevertTuneDllAsync] '{card.GameName}' {slot} put back ({(d.HadBackup ? d.PreviousVersion : "game's own file")})");
        return null;
    }

    // ── NVIDIA settings ──────────────────────────────────────────────────────

    /// <summary>Writes NVIDIA items (background thread). Returns the values before (for undo) and errors.</summary>
    public Task<(Dictionary<string, string> Before, List<string> Errors)> ApplyTuneNvidiaAsync(
        GameCardViewModel card, IReadOnlyList<(string Key, string Value)> items)
    {
        var ctx = BuildDisplayModeContext(card);
        return Task.Run(() =>
        {
            var before = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var errors = new List<string>();
            // Every value before is read first: some writes change other settings too (ReBAR On sets ReBAR mode)
            var olds = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, _) in items)
            {
                try { olds[key] = TuneItems.Get(key)?.Read(ctx); }
                catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException or IOException)
                {
                    CrashReporter.Log($"[MainViewModel.ApplyTuneNvidiaAsync] '{ctx.GameName}' read {key} failed — {ex.Message}");
                }
            }
            foreach (var (key, value) in items)
            {
                var item = TuneItems.Get(key);
                if (item == null) continue;
                try
                {
                    var old = olds.TryGetValue(key, out var o) ? o : null;
                    var err = item.Write(ctx, value);
                    if (err == null)
                    {
                        if (old != null) before[key] = old;
                        CrashReporter.Log($"[MainViewModel.ApplyTuneNvidiaAsync] '{ctx.GameName}' {key}: {old} → {value}");
                    }
                    else errors.Add($"{TunePlanner.NvidiaLabel(item)}: {err}");
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException or IOException)
                {
                    CrashReporter.Log($"[MainViewModel.ApplyTuneNvidiaAsync] '{ctx.GameName}' {key} failed — {ex.Message}");
                    errors.Add($"{TunePlanner.NvidiaLabel(item)}: {ex.Message}");
                }
            }
            return (before, errors);
        });
    }

    /// <summary>Writes the values from before tuning back (background thread). Returns errors.</summary>
    public Task<List<string>> RevertTuneNvidiaAsync(GameCardViewModel card, Dictionary<string, string> before)
    {
        var ctx = BuildDisplayModeContext(card);
        return Task.Run(() =>
        {
            var errors = new List<string>();
            if (ctx.Nvidia?.IsSupported != true)
            {
                if (before.Count > 0) errors.Add("NVIDIA settings not put back: no NVIDIA driver found");
                return errors;
            }
            // Reverse of the order they were applied in (ReBAR Off last, so it clears what ReBAR mode / size put back)
            foreach (var item in TuneItems.All.Reverse())
            {
                if (!before.TryGetValue(item.Key, out var value)) continue;
                var key = item.Key;
                try
                {
                    var err = item.Write(ctx, value);
                    if (err != null) errors.Add($"{TunePlanner.NvidiaLabel(item)}: {err}");
                    else CrashReporter.Log($"[MainViewModel.RevertTuneNvidiaAsync] '{ctx.GameName}' {key} back to {value}");
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException or IOException)
                {
                    CrashReporter.Log($"[MainViewModel.RevertTuneNvidiaAsync] '{ctx.GameName}' {key} failed — {ex.Message}");
                    errors.Add($"{TunePlanner.NvidiaLabel(item)}: {ex.Message}");
                }
            }
            return errors;
        });
    }
}
