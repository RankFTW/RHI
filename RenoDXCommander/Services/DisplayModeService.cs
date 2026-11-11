// DisplayModeService.cs -- switches a game between Display modes, and turns its mode off. For every item the game
// swaps: remember the game's current value as its value for the mode it's leaving (or, coming from Off, as its
// value from before modes), then write its value for the new mode: its own, else the mode's default, else its
// normal value (the global setting, else its value from before modes). Turning off writes the normal values.
// Launch items (HDR, resolution, ...) aren't written: their value is used when RHI launches the game.
// File and NVIDIA work only: call it from a background thread. One switch at a time (a game's files mustn't be
// written by two switches at once).

namespace RenoDXCommander.Services;

public sealed class DisplayModeService
{
    private readonly DisplayModeStore _store;
    private readonly object _gate = new();

    public DisplayModeService(DisplayModeStore store) => _store = store;

    public DisplayModeStore Store => _store;

    /// <summary>
    /// The global setting for an item (game, item key) -> value, or null when there's none (most items).
    /// Set by MainViewModel from Settings (peak brightness, ReShade keys, ReLimiter cap / OSD key, NVIDIA FPS limit).
    /// </summary>
    public Func<DisplayModeContext, string, string?>? GlobalValue { get; set; }

    /// <summary>What happened to one item.</summary>
    public sealed record ItemResult(DisplayModeItem Item, bool Written, string? Value, string? Problem);

    /// <summary>Result of one switch.</summary>
    public sealed record SwitchResult(string GameName, string ModeName, IReadOnlyList<ItemResult> Items, bool TurnedOff = false)
    {
        public int WrittenCount => Items.Count(i => i.Written);
        public IEnumerable<ItemResult> Problems => Items.Where(i => i.Problem != null);

        /// <summary>One line for the status bar / a toast.</summary>
        public string Summary
        {
            get
            {
                var written = Items.Where(i => i.Written && !i.Item.AtLaunch).ToList();
                var launch = Items.Where(i => i.Item.AtLaunch && i.Value != null).ToList();
                var parts = new List<string>();
                if (written.Count > 0)
                    parts.Add(string.Join(", ", written.Select(i => $"{i.Item.Label} {i.Item.Format(i.Value)}")));
                if (launch.Count > 0)
                    parts.Add("on launch: " + string.Join(", ", launch.Select(i => $"{i.Item.Label} {i.Item.Format(i.Value)}")));
                var text = TurnedOff ? $"{GameName}: display mode off, using global settings" : $"{GameName}: {ModeName} mode";
                if (parts.Count > 0) text += ". " + string.Join("; ", parts);
                else text += TurnedOff ? ". Nothing to change." : ". Nothing to change (no values set for this mode yet).";
                var failed = Items.Where(i => i.Problem != null && i.Written == false && i.Value != null).ToList();
                if (failed.Count > 0) text += ". Couldn't set: " + string.Join(", ", failed.Select(i => i.Item.Label));
                return text;
            }
        }
    }

    /// <summary>
    /// Switches the game to <paramref name="targetModeId"/>. Switching to the mode it's already in re-applies that
    /// mode's values (after remembering nothing, since nothing is being left).
    /// </summary>
    public SwitchResult Switch(DisplayModeContext ctx, string targetModeId)
    {
        lock (_gate)
        {
            var key = ctx.Key;
            var from = _store.ActiveModeId(key); // null = Off
            bool leaving = from == null || !from.Equals(targetModeId, StringComparison.OrdinalIgnoreCase);
            var results = new List<ItemResult>();
            var entries = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            foreach (var itemKey in _store.ItemsFor(key))
            {
                var item = DisplayModeItems.Get(itemKey);
                if (item == null) continue;

                if (item.AtLaunch)
                {
                    results.Add(new ItemResult(item, false, _store.Effective(key, targetModeId, item.Key), null));
                    continue;
                }

                string? reason = Try(() => item.Unavailable(ctx), item, "check");
                if (reason != null)
                {
                    entries[item.Key] = null; // not readable in this mode: nothing to remember when leaving it
                    results.Add(new ItemResult(item, false, null, reason));
                    continue;
                }

                var current = Try(() => item.Read(ctx), item, "read");
                if (leaving && current != null) RememberLeaving(key, from, item, current);

                // The mode's value, else the game's normal value (a mode only needs what differs)
                var value = _store.Effective(key, targetModeId, item.Key);
                bool normal = value == null;
                value ??= NormalValue(ctx, item);
                if (value == null || (normal && value.Equals(current, StringComparison.Ordinal)))
                {
                    entries[item.Key] = current; // nothing to write
                    results.Add(new ItemResult(item, false, null, null));
                    continue;
                }
                var error = WriteItem(ctx, item, value);
                entries[item.Key] = error == null ? Try(() => item.Read(ctx), item, "read") ?? value : current;
                results.Add(new ItemResult(item, error == null, value, error));
            }

            _store.SetActive(key, targetModeId, Folders(ctx));
            _store.SetEntries(key, entries);
            var modeName = _store.NameOf(targetModeId) ?? targetModeId;
            CrashReporter.Log($"[DisplayModeService.Switch] '{key}' {_store.NameOf(from) ?? "Off"} → {modeName}: "
                + string.Join(", ", results.Select(r => $"{r.Item.Key}={(r.Written ? r.Value : r.Problem ?? "-")}")));
            return new SwitchResult(ctx.GameName, modeName, results);
        }
    }

    /// <summary>
    /// Turns the game's mode off: remembers its values for the mode it's leaving, then writes its normal values
    /// (the global setting, else what it had before it entered a mode). Launch items aren't written: the HDR /
    /// Resolution buttons and Game Overrides apply again. Already off = nothing to do.
    /// </summary>
    public SwitchResult TurnOff(DisplayModeContext ctx)
    {
        lock (_gate)
        {
            var key = ctx.Key;
            var from = _store.ActiveModeId(key);
            var results = new List<ItemResult>();
            if (from == null) return new SwitchResult(ctx.GameName, "Off", results, TurnedOff: true);

            foreach (var itemKey in _store.ItemsFor(key))
            {
                var item = DisplayModeItems.Get(itemKey);
                if (item == null || item.AtLaunch) continue;
                string? reason = Try(() => item.Unavailable(ctx), item, "check");
                if (reason != null)
                {
                    results.Add(new ItemResult(item, false, null, reason));
                    continue;
                }
                var current = Try(() => item.Read(ctx), item, "read");
                if (current != null) RememberLeaving(key, from, item, current);

                var value = NormalValue(ctx, item);
                if (value == null || value.Equals(current, StringComparison.Ordinal))
                {
                    results.Add(new ItemResult(item, false, null, null)); // no normal value, or it already has it
                    continue;
                }
                var error = WriteItem(ctx, item, value);
                results.Add(new ItemResult(item, error == null, value, error));
            }

            _store.SetOff(key);
            CrashReporter.Log($"[DisplayModeService.TurnOff] '{key}' {_store.NameOf(from)} → Off: "
                + string.Join(", ", results.Select(r => $"{r.Item.Key}={(r.Written ? r.Value : r.Problem ?? "-")}")));
            return new SwitchResult(ctx.GameName, "Off", results, TurnedOff: true);
        }
    }

    /// <summary>
    /// Before a new value is written: keeps the game's current value. Coming from Off it's the value from before modes.
    /// Leaving a mode it's kept for that mode only when it's the game's own: a change made while in that mode
    /// (e.g. in game). A value it still has from before (the mode had none) isn't that mode's, so it isn't copied in.
    /// No entry recorded (an item added to the list while in the mode, or a game switched before entries existed):
    /// it's the game's normal value.
    /// </summary>
    private void RememberLeaving(string key, string? from, DisplayModeItem item, string current)
    {
        if (from == null)
        {
            _store.SetOriginal(key, item.Key, current);
            return;
        }
        bool recorded = _store.TryGetEntry(key, item.Key, out var atEntry);
        if (!recorded)
        {
            if (_store.OriginalValue(key, item.Key) == null) _store.SetOriginal(key, item.Key, current);
        }
        else if (atEntry != null && !atEntry.Equals(current, StringComparison.Ordinal))
            _store.SetGameValue(key, from, item.Key, current);
    }

    /// <summary>The game's normal value for an item: the global setting, else its value from before modes. Null = none.</summary>
    public string? NormalValue(DisplayModeContext ctx, DisplayModeItem item)
    {
        if (item.AtLaunch) return null;
        string? global = null;
        try { global = GlobalValue?.Invoke(ctx, item.Key); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            CrashReporter.Log($"[DisplayModeService.NormalValue] {item.Key} — {ex.Message}");
        }
        return global ?? _store.OriginalValue(ctx.Key, item.Key);
    }

    /// <summary>What the game gets for an item in a mode: its own value, else the mode default, else its normal value.</summary>
    public string? ValueFor(DisplayModeContext ctx, string modeId, DisplayModeItem item)
        => _store.Effective(ctx.Key, modeId, item.Key) ?? NormalValue(ctx, item);

    /// <summary>Writes one value now (an edit to the game's value for its active mode). Null = done, else the problem.</summary>
    public string? ApplyNow(DisplayModeContext ctx, DisplayModeItem item, string value)
    {
        if (item.AtLaunch) return null;
        lock (_gate)
        {
            var reason = Try(() => item.Unavailable(ctx), item, "check");
            if (reason != null) return reason;
            var error = WriteItem(ctx, item, value);
            if (error == null && _store.ActiveModeId(ctx.Key) is { } active)
            {
                _store.SetActive(ctx.Key, active, Folders(ctx));
                _store.SetEntry(ctx.Key, item.Key, Try(() => item.Read(ctx), item, "read") ?? value);
            }
            return error;
        }
    }

    /// <summary>The game's current values (for the per-game dialog). Null entries = no value / not available.</summary>
    public Dictionary<string, string?> ReadCurrent(DisplayModeContext ctx, IEnumerable<DisplayModeItem> items)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        lock (_gate)
            foreach (var item in items)
            {
                if (item.AtLaunch || Try(() => item.Unavailable(ctx), item, "check") != null) { result[item.Key] = null; continue; }
                result[item.Key] = Try(() => item.Read(ctx), item, "read");
            }
        return result;
    }

    /// <summary>Null when available, else why not (for the per-game dialog).</summary>
    public static string? Unavailable(DisplayModeContext ctx, DisplayModeItem item)
        => item.AtLaunch ? null : Try(() => item.Unavailable(ctx), item, "check");

    /// <summary>
    /// For writers that push one value to many games (ReShade / ReLimiter installs and updates, the Settings
    /// "write to all games" buttons): if the game in <paramref name="folder"/> is managed by Display modes and swaps
    /// any of <paramref name="itemKeys"/>, reads those values now and returns an action that writes them back
    /// afterwards (or the active mode's value when the file had none, e.g. a fresh ini). Null = nothing to keep.
    /// </summary>
    public Action? Keep(string folder, IReadOnlyList<string> itemKeys)
    {
        var key = _store.FindManagedByFolder(folder);
        if (key == null) return null;
        var items = itemKeys.Where(k => _store.Swaps(key, k)).Select(DisplayModeItems.Get)
            .Where(i => i is { AtLaunch: false }).Cast<DisplayModeItem>().ToList();
        if (items.Count == 0) return null;

        var parsed = Models.GameKey.Parse(key);
        var ctx = new DisplayModeContext
        {
            GameName = parsed.Name,
            Store = parsed.Store,
            InstallPath = InstallFolderOf(key) ?? folder,
            HasRenoDx = true, // reads / writes only; whether to swap was decided when the item was ticked
        };
        var captured = items.Select(i => (Item: i, Value: Try(() => i.Read(ctx), i, "read"))).ToList();
        var mode = _store.ActiveModeId(key);
        if (mode == null) return null;
        CrashReporter.Log($"[DisplayModeService.Keep] '{key}': keeping {string.Join(", ", captured.Select(c => $"{c.Item.Key}={c.Value ?? "(mode value)"}"))}");
        return () =>
        {
            lock (_gate)
                foreach (var (item, value) in captured)
                {
                    var v = value ?? ValueFor(ctx, mode, item);
                    if (v == null) continue;
                    var error = WriteItem(ctx, item, v);
                    if (error != null) CrashReporter.Log($"[DisplayModeService.Keep] '{key}' {item.Key} couldn't be put back — {error}");
                    // A fresh ini (e.g. ReLimiter just installed) now holds the mode's value: that's what it entered with
                    else if (value == null) _store.SetEntry(key, item.Key, v);
                }
        };
    }

    private string? InstallFolderOf(string key)
    {
        var snap = _store.Snapshot();
        return snap.Games.TryGetValue(key, out var st) && st.Folders is { Count: > 0 } ? st.Folders[0] : null;
    }

    private static IEnumerable<string> Folders(DisplayModeContext ctx)
    {
        var list = new List<string>();
        if (!string.IsNullOrEmpty(ctx.InstallPath)) list.Add(ctx.InstallPath);
        try { if (!string.IsNullOrEmpty(ctx.AddonDir)) list.Add(ctx.AddonDir); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { CrashReporter.Log($"[DisplayModeService.Folders] {ex.Message}"); }
        return list;
    }

    private static string? WriteItem(DisplayModeContext ctx, DisplayModeItem item, string value)
    {
        try
        {
            return item.Write(ctx, value);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                       or ArgumentException or NotSupportedException or System.Runtime.InteropServices.COMException)
        {
            CrashReporter.Log($"[DisplayModeService.Write] '{ctx.GameName}' {item.Key}={value} failed — {ex.Message}");
            return ex is UnauthorizedAccessException ? "Access denied" : ex.Message;
        }
    }

    private static string? Try(Func<string?> f, DisplayModeItem item, string what)
    {
        try { return f(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                       or ArgumentException or NotSupportedException or System.Runtime.InteropServices.COMException)
        {
            CrashReporter.Log($"[DisplayModeService] {item.Key} {what} failed — {ex.Message}");
            return what == "check" ? ex.Message : null;
        }
    }
}
