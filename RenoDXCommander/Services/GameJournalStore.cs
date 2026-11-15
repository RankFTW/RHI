// GameJournalStore.cs -- per-game data the RHI overlay keeps: your own notes for a game, and RHI's own
// playtime count (minutes the game was the window in front, for games whose store doesn't give one).
// Saved in %LocalAppData%\RHI\game_journal.json, keyed "GameName|Store" (GameKey). Thread-safe.
// Game renames call MigrateGameRename (GameNameService.RenameGame).

namespace RenoDXCommander.Services;

internal static class GameJournalStore
{
    private sealed class Data
    {
        public Dictionary<string, string> Notes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, long> PlaytimeSeconds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>When RHI started counting playtime (UTC, ISO 8601).</summary>
        public string? CountingSince { get; set; }
    }

    private static readonly object Gate = new();
    private static Data? _data;
    private static bool _dirty;
    // The file exists but couldn't be read (locked, access denied): SafeJsonFile blocks saves so it isn't
    // overwritten, and each access tries again; what was added meanwhile is merged onto the loaded data.
    private static bool _retryLoad;

    internal static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "game_journal.json");

    // Call under Gate. Reads through SafeJsonFile: a corrupt file is kept aside and the .bak used, and an
    // unreadable one blocks saves. (Before Nov 2026 any read error started empty, and the next save, every
    // minute while a game runs, overwrote all notes and playtime.)
    private static Data Load()
    {
        if (_data != null && !_retryLoad) return _data;
        var loaded = SafeJsonFile.Read<Data>(FilePath, out var status);
        if (status == SafeJsonFile.LoadStatus.Unreadable)
        {
            if (_data == null) CrashReporter.Log("[GameJournalStore.Load] game_journal.json couldn't be read — notes and playtime aren't saved until it can");
            _retryLoad = true;
            return _data ??= new Data();
        }
        var fresh = new Data();
        if (loaded != null)
        {
            fresh.Notes = new(loaded.Notes ?? new(), StringComparer.OrdinalIgnoreCase);
            fresh.PlaytimeSeconds = new(loaded.PlaytimeSeconds ?? new(), StringComparer.OrdinalIgnoreCase);
            fresh.CountingSince = loaded.CountingSince;
        }
        if (_data != null) // retry succeeded: keep what was added while the file was unreadable
        {
            foreach (var (k, v) in _data.Notes) fresh.Notes[k] = v;
            foreach (var (k, v) in _data.PlaytimeSeconds)
                fresh.PlaytimeSeconds[k] = (fresh.PlaytimeSeconds.TryGetValue(k, out var s) ? s : 0) + v;
            fresh.CountingSince ??= _data.CountingSince;
            CrashReporter.Log("[GameJournalStore.Load] game_journal.json readable again — merged this session's changes");
        }
        _retryLoad = false;
        return _data = fresh;
    }

    /// <summary>Writes the file if anything changed since the last save.</summary>
    // Written under Gate, so a background save (playtime, every minute) and a notes save can't finish in
    // the wrong order. SafeJsonFile writes atomically, keeps a .bak, and refuses while the file is unreadable.
    internal static void Save()
    {
        lock (Gate)
        {
            if (!_dirty || _data == null) return;
            if (_retryLoad) Load(); // still unreadable = SafeJsonFile skips the write and it stays dirty
            if (SafeJsonFile.Write(FilePath, _data, "GameJournalStore.Save", SafeJsonFile.Indented))
                _dirty = false;
        }
    }

    // ── Notes ─────────────────────────────────────────────────────────────────

    internal static string GetNotes(string key)
    {
        lock (Gate) return Load().Notes.TryGetValue(key, out var n) ? n : "";
    }

    /// <summary>Sets a game's notes (empty removes them) and saves.</summary>
    internal static void SetNotes(string key, string text)
    {
        lock (Gate)
        {
            var d = Load();
            var trimmed = text.TrimEnd();
            if (trimmed.Length == 0) { if (!d.Notes.Remove(key)) return; }
            else if (d.Notes.TryGetValue(key, out var old) && old == trimmed) return;
            else d.Notes[key] = trimmed;
            _dirty = true;
        }
        Save();
    }

    // ── Playtime ──────────────────────────────────────────────────────────────

    /// <summary>Adds time to a game's count (saved later by <see cref="Save"/>).</summary>
    internal static void AddPlaytime(string key, TimeSpan time)
    {
        if (time <= TimeSpan.Zero) return;
        lock (Gate)
        {
            var d = Load();
            d.CountingSince ??= DateTime.UtcNow.ToString("o");
            d.PlaytimeSeconds[key] = (d.PlaytimeSeconds.TryGetValue(key, out var s) ? s : 0) + (long)time.TotalSeconds;
            _dirty = true;
        }
    }

    internal static TimeSpan GetPlaytime(string key)
    {
        lock (Gate) return Load().PlaytimeSeconds.TryGetValue(key, out var s) ? TimeSpan.FromSeconds(s) : TimeSpan.Zero;
    }

    /// <summary>When RHI started counting playtime, or null if it hasn't yet.</summary>
    internal static DateTime? CountingSince()
    {
        lock (Gate)
            return DateTime.TryParse(Load().CountingSince, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d : null;
    }

    // ── Renames ───────────────────────────────────────────────────────────────

    /// <summary>Moves notes and playtime from "oldName|Store" keys to "newName|Store".</summary>
    internal static void MigrateGameRename(string oldName, string newName)
    {
        bool changed = false;
        lock (Gate)
        {
            var d = Load();
            changed |= Migrate(d.Notes, oldName, newName);
            changed |= Migrate(d.PlaytimeSeconds, oldName, newName);
            if (changed) _dirty = true;
        }
        if (changed) Save();
    }

    private static bool Migrate<T>(Dictionary<string, T> dict, string oldName, string newName)
    {
        var prefix = oldName + "|";
        var keys = dict.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var k in keys)
        {
            var value = dict[k];
            dict.Remove(k);
            dict[newName + "|" + k.Substring(prefix.Length)] = value;
        }
        return keys.Count > 0;
    }
}
