// GameJournalStore.cs -- per-game data the RHI overlay keeps: your own notes for a game, and RHI's own
// playtime count (minutes the game was the window in front, for games whose store doesn't give one).
// Saved in %LocalAppData%\RHI\game_journal.json, keyed "GameName|Store" (GameKey). Thread-safe.
// Game renames call MigrateGameRename (GameNameService.RenameGame).
using System.Text.Json;

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

    internal static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "game_journal.json");

    private static Data Load()
    {
        if (_data != null) return _data;
        _data = new Data();
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath));
                if (loaded != null)
                {
                    _data.Notes = new(loaded.Notes ?? new(), StringComparer.OrdinalIgnoreCase);
                    _data.PlaytimeSeconds = new(loaded.PlaytimeSeconds ?? new(), StringComparer.OrdinalIgnoreCase);
                    _data.CountingSince = loaded.CountingSince;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            CrashReporter.Log($"[GameJournalStore.Load] {ex.Message} — starting empty");
        }
        return _data;
    }

    /// <summary>Writes the file if anything changed since the last save.</summary>
    internal static void Save()
    {
        string json;
        lock (Gate)
        {
            if (!_dirty || _data == null) return;
            json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
            _dirty = false;
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[GameJournalStore.Save] {ex.Message}");
            lock (Gate) _dirty = true;
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
