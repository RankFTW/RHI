// MainViewModel.DisplayModes.cs -- Display modes (Monitor / TV / ...) for the UI: switch one game or every game,
// edit a game's values, and the HDR / resolution / launch argument values used at launch. The data lives in
// GameNameService.DisplayModes (settings.json); file and NVIDIA work is DisplayModeService on a background thread.
// Everything here is a no-op while Settings › Display › Display modes is off.

using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    private DisplayModeService? _displayModeService;

    public DisplayModeService DisplayModeService => _displayModeService ??= new DisplayModeService(_gameNameService.DisplayModes);
    public DisplayModeStore DisplayModeStore => _gameNameService.DisplayModes;

    /// <summary>True when the mode switch is shown on game pages (Settings › Display).</summary>
    public bool DisplayModesOn => _settingsViewModel.DisplayModesEnabled;

    /// <summary>Raised (UI thread) after modes are added / renamed / deleted / reordered or a game is switched.</summary>
    public event Action? DisplayModesChanged;

    public void NotifyDisplayModesChanged()
    {
        try { DisplayModesChanged?.Invoke(); }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            CrashReporter.Log($"[MainViewModel.NotifyDisplayModesChanged] {ex.Message}");
        }
    }

    /// <summary>Called once at startup: lets global ini writers keep games' display mode values.</summary>
    private void WireDisplayModes()
    {
        AuxInstallService.DisplayModeKeeper = (folder, items) => DisplayModesOn ? DisplayModeService.Keep(folder, items) : null;
    }

    public static string DisplayModeKey(GameCardViewModel card) => GameKey.FromCard(card.GameName, card.Source ?? "").ToKey();

    /// <summary>What the mode items need to know about the game (UI thread; plain values only).</summary>
    public DisplayModeContext BuildDisplayModeContext(GameCardViewModel card) => new()
    {
        GameName = card.GameName,
        Store = card.Source ?? "",
        InstallPath = card.InstallPath ?? "",
        HasRenoDx = card.Status is GameStatus.Installed or GameStatus.UpdateAvailable || card.InstalledRecord != null,
        IsRtxHdrEnabled = card.IsRtxHdrEnabled,
        Nvidia = _dlssPresetService,
    };

    public string ActiveDisplayModeId(GameCardViewModel card) => DisplayModeStore.ActiveModeId(DisplayModeKey(card));

    public string ActiveDisplayModeName(GameCardViewModel card) => DisplayModeStore.NameOf(ActiveDisplayModeId(card)) ?? "";

    /// <summary>Switches one game. Returns null (with <paramref name="refused"/> set) when it can't switch now.</summary>
    public async Task<DisplayModeService.SwitchResult?> SwitchDisplayModeAsync(GameCardViewModel card, string modeId, Action<string>? refused = null)
    {
        if (!DisplayModesOn) return null;
        if (card.IsRunning)
        {
            refused?.Invoke($"Close {card.GameName} first: games write their settings when they close, which would undo the switch.");
            return null;
        }
        if (string.IsNullOrEmpty(card.InstallPath))
        {
            refused?.Invoke("This game has no install folder.");
            return null;
        }
        var ctx = BuildDisplayModeContext(card);
        var svc = DisplayModeService;
        var result = await Task.Run(() => svc.Switch(ctx, modeId));
        SaveNameMappings();
        NotifyDisplayModesChanged();
        return result;
    }

    /// <summary>
    /// Switches every game that isn't hidden or running and has a folder (Settings › Display modes › Switch all games).
    /// Returns (switched, skipped because running).
    /// </summary>
    public async Task<(int Switched, int Running)> SwitchAllDisplayModesAsync(string modeId, IProgress<(string msg, double pct)>? progress, CancellationToken ct)
    {
        if (!DisplayModesOn) return (0, 0);
        var cards = _allCards.Where(c => !c.IsHidden && !string.IsNullOrEmpty(c.InstallPath)).ToList();
        int running = cards.Count(c => c.IsRunning);
        var targets = cards.Where(c => !c.IsRunning)
            .Select(c => (Card: c, Ctx: BuildDisplayModeContext(c))).ToList();
        var svc = DisplayModeService;
        var store = DisplayModeStore;
        string modeName = store.NameOf(modeId) ?? modeId;
        int done = 0;
        await Task.Run(() =>
        {
            for (int i = 0; i < targets.Count; i++)
            {
                if (ct.IsCancellationRequested) break;
                var (card, ctx) = targets[i];
                if (store.ItemsFor(ctx.Key).Count == 0) continue;
                progress?.Report(($"{modeName} mode: {ctx.GameName}", (double)i / Math.Max(1, targets.Count) * 100));
                try
                {
                    if (!Directory.Exists(ctx.InstallPath)) continue;
                    svc.Switch(ctx, modeId);
                    done++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    CrashReporter.Log($"[MainViewModel.SwitchAllDisplayModesAsync] '{ctx.GameName}' — {ex.Message}");
                }
            }
        }, ct);
        CrashReporter.Log($"[MainViewModel.SwitchAllDisplayModesAsync] {modeName}: {done} game(s) switched, {running} running (skipped)");
        SaveNameMappings();
        NotifyDisplayModesChanged();
        return (done, running);
    }

    /// <summary>
    /// Sets the game's own value for a mode (null = use the mode default). When that mode is the active one and the
    /// item is written to the game, it's written now. Returns a problem message, or null.
    /// </summary>
    public async Task<string?> SetDisplayModeGameValueAsync(GameCardViewModel card, string modeId, DisplayModeItem item, string? value)
    {
        var key = DisplayModeKey(card);
        var store = DisplayModeStore;
        store.SetGameValue(key, modeId, item.Key, value);
        SaveNameMappings();
        string? problem = null;
        bool isActive = store.ActiveModeId(key).Equals(modeId, StringComparison.OrdinalIgnoreCase);
        var effective = store.Effective(key, modeId, item.Key);
        if (isActive && store.IsManaged(key) && !item.AtLaunch && effective != null && store.Swaps(key, item.Key))
        {
            if (card.IsRunning) return "Saved. The game is running, so it's written the next time you switch modes.";
            var ctx = BuildDisplayModeContext(card);
            var svc = DisplayModeService;
            problem = await Task.Run(() => svc.ApplyNow(ctx, item, effective));
        }
        return problem;
    }

    /// <summary>The game's own item list (null = the default list).</summary>
    public void SetDisplayModeItems(GameCardViewModel card, IEnumerable<string>? items)
    {
        DisplayModeStore.SetItems(DisplayModeKey(card), items);
        SaveNameMappings();
        NotifyDisplayModesChanged();
    }

    /// <summary>Reset Overrides: forget the game's display mode state (files are left as they are).</summary>
    public void ResetDisplayModeState(GameCardViewModel card)
    {
        if (DisplayModeStore.RemoveGame(DisplayModeKey(card)))
        {
            CrashReporter.Log($"[MainViewModel.ResetDisplayModeState] Cleared display modes for '{DisplayModeKey(card)}'");
            SaveNameMappings();
        }
    }

    // ── Launch (HDR, HDR display, resolution, launch arguments) ───────────────

    /// <summary>The active mode's value for a launch item, or null when modes are off / the game doesn't swap it / no value.</summary>
    public string? DisplayModeLaunchValue(GameCardViewModel card, string itemKey)
        => DisplayModesOn ? DisplayModeStore.ActiveValue(DisplayModeKey(card), itemKey) : null;

    /// <summary>True when the game's HDR button is set by its display mode.</summary>
    public bool DisplayModeOwnsHdr(GameCardViewModel card)
        => DisplayModesOn && DisplayModeStore.Swaps(DisplayModeKey(card), DisplayModeItemKeys.Hdr);

    public bool DisplayModeOwnsResolution(GameCardViewModel card)
        => DisplayModesOn && DisplayModeStore.Swaps(DisplayModeKey(card), DisplayModeItemKeys.Resolution);

    /// <summary>HDR on launch for the game's current mode: On / Off from the mode, else null (use the normal rule).</summary>
    public bool? DisplayModeHdrOn(GameCardViewModel card) => DisplayModeLaunchValue(card, DisplayModeItemKeys.Hdr) switch
    {
        "On" => true,
        "Off" => false,
        _ => null,
    };

    /// <summary>Resolution on launch for the current mode: "Off", "On" (Settings target) or a "WxH@Hz" key; null = normal rule.</summary>
    public string? DisplayModeResolution(GameCardViewModel card) => DisplayModeLaunchValue(card, DisplayModeItemKeys.Resolution);

    /// <summary>HDR displays for the current mode, or null for the Settings choice.</summary>
    public List<uint>? DisplayModeHdrTargets(GameCardViewModel card)
    {
        var v = DisplayModeLaunchValue(card, DisplayModeItemKeys.HdrDisplay);
        return v != null && uint.TryParse(v, out var id) ? new List<uint> { id } : null;
    }

    /// <summary>The HDR / RES header button was clicked while the mode owns it: flips the current mode's value for this game.</summary>
    public void SetDisplayModeLaunchValue(GameCardViewModel card, string itemKey, string value)
    {
        var key = DisplayModeKey(card);
        DisplayModeStore.SetGameValue(key, DisplayModeStore.ActiveModeId(key), itemKey, value);
        SaveNameMappings();
        CrashReporter.Log($"[MainViewModel.SetDisplayModeLaunchValue] '{key}' {DisplayModeStore.NameOf(DisplayModeStore.ActiveModeId(key))}: {itemKey}={value}");
    }

    // ── Modes (Settings card) ─────────────────────────────────────────────────

    public string? AddDisplayMode(string name)
    {
        var id = DisplayModeStore.AddMode(name);
        if (id != null) { SaveNameMappings(); NotifyDisplayModesChanged(); }
        return id;
    }

    public bool RenameDisplayMode(string id, string name)
    {
        bool ok = DisplayModeStore.RenameMode(id, name);
        if (ok) { SaveNameMappings(); NotifyDisplayModesChanged(); }
        return ok;
    }

    public bool DeleteDisplayMode(string id)
    {
        bool ok = DisplayModeStore.DeleteMode(id);
        if (ok) { SaveNameMappings(); NotifyDisplayModesChanged(); }
        return ok;
    }

    public bool MoveDisplayMode(string id, int delta)
    {
        bool ok = DisplayModeStore.MoveMode(id, delta);
        if (ok) { SaveNameMappings(); NotifyDisplayModesChanged(); }
        return ok;
    }

    public void SetDisplayModeDefault(string modeId, string item, string? value)
    {
        DisplayModeStore.SetModeDefault(modeId, item, value);
        SaveNameMappings();
    }

    public void SetDisplayModeDefaultItems(IEnumerable<string> items)
    {
        DisplayModeStore.SetDefaultItems(items);
        SaveNameMappings();
        NotifyDisplayModesChanged();
    }

    public void SetDisplayModesEnabled(bool on)
    {
        if (_settingsViewModel.DisplayModesEnabled == on) return;
        _settingsViewModel.DisplayModesEnabled = on;
        SaveNameMappings();
        CrashReporter.Log($"[MainViewModel.SetDisplayModesEnabled] {(on ? "On" : "Off")}");
        NotifyDisplayModesChanged();
    }
}
