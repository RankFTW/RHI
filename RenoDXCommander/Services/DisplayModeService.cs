// DisplayModeService.cs -- switches a game between Display modes. For every item the game swaps:
// remember the game's current value as its value for the mode it's leaving, then write its value for the new mode
// (its own, else the mode's default). Items with no value for the new mode are left as they are.
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

    /// <summary>What happened to one item.</summary>
    public sealed record ItemResult(DisplayModeItem Item, bool Written, string? Value, string? Problem);

    /// <summary>Result of one switch.</summary>
    public sealed record SwitchResult(string GameName, string ModeName, IReadOnlyList<ItemResult> Items)
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
                var text = $"{GameName}: {ModeName} mode";
                if (parts.Count > 0) text += ". " + string.Join("; ", parts);
                else text += ". Nothing to change (no values set for this mode yet).";
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
            var from = _store.ActiveModeId(key);
            var results = new List<ItemResult>();

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
                    results.Add(new ItemResult(item, false, null, reason));
                    continue;
                }

                // Remember what the game has now for the mode it's leaving (in-game changes are kept this way)
                if (!from.Equals(targetModeId, StringComparison.OrdinalIgnoreCase))
                {
                    var current = Try(() => item.Read(ctx), item, "read");
                    if (current != null) _store.SetGameValue(key, from, item.Key, current);
                }

                var value = _store.Effective(key, targetModeId, item.Key);
                if (value == null)
                {
                    results.Add(new ItemResult(item, false, null, null)); // nothing set for this mode: left alone
                    continue;
                }
                var error = WriteItem(ctx, item, value);
                results.Add(new ItemResult(item, error == null, value, error));
            }

            _store.SetActive(key, targetModeId, Folders(ctx));
            var modeName = _store.NameOf(targetModeId) ?? targetModeId;
            CrashReporter.Log($"[DisplayModeService.Switch] '{key}' {_store.NameOf(from)} → {modeName}: "
                + string.Join(", ", results.Select(r => $"{r.Item.Key}={(r.Written ? r.Value : r.Problem ?? "-")}")));
            return new SwitchResult(ctx.GameName, modeName, results);
        }
    }

    /// <summary>Writes one value now (an edit to the game's value for its active mode). Null = done, else the problem.</summary>
    public string? ApplyNow(DisplayModeContext ctx, DisplayModeItem item, string value)
    {
        if (item.AtLaunch) return null;
        lock (_gate)
        {
            var reason = Try(() => item.Unavailable(ctx), item, "check");
            if (reason != null) return reason;
            var error = WriteItem(ctx, item, value);
            if (error == null) _store.SetActive(ctx.Key, _store.ActiveModeId(ctx.Key), Folders(ctx));
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
        CrashReporter.Log($"[DisplayModeService.Keep] '{key}': keeping {string.Join(", ", captured.Select(c => $"{c.Item.Key}={c.Value ?? "(mode value)"}"))}");
        return () =>
        {
            lock (_gate)
                foreach (var (item, value) in captured)
                {
                    var v = value ?? _store.Effective(key, mode, item.Key);
                    if (v == null) continue;
                    var error = WriteItem(ctx, item, v);
                    if (error != null) CrashReporter.Log($"[DisplayModeService.Keep] '{key}' {item.Key} couldn't be put back — {error}");
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
