// SteamLibraryArtService.cs -- the artwork the user set for their games in their own Steam library, so Spotlight
// shows the same covers and banners as Steam does. Steam keeps them per account in
// {Steam}\userdata\{account}\config\grid\:  {id}p.png|jpg = cover (600x900), {id}_hero.png|jpg = banner,
// {id}.png = wide capsule, {id}_logo.png = logo. {id} is the Steam AppID, or for a non-Steam game added to Steam
// the shortcut's id from config\shortcuts.vdf (binary KeyValues; matched to RHI's game by exe folder or name).
// Read-only: RHI never writes into Steam's folders. File reads are cheap and cached until the folder changes.

namespace RenoDXCommander.Services;

internal static class SteamLibraryArtService
{
    private static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".webp" };
    private static readonly object Lock = new();

    private static string? _configDir;          // {Steam}\userdata\{account}\config, or "" when there's none
    private static DateTime _configCheckedUtc;
    private static DateTime _gridStampUtc;
    private static Dictionary<string, string> _grid = new(StringComparer.OrdinalIgnoreCase); // "{id}p" → file
    private static DateTime _shortcutsStampUtc;
    private static List<Shortcut> _shortcuts = new();

    internal sealed record Shortcut(uint AppId, string AppName, string Exe, string StartDir);

    /// <summary>
    /// The user's own Steam artwork for this game: a non-Steam shortcut's art first (that's the entry the user
    /// sees for a game owned elsewhere), then the Steam AppID's. Null when there's none.
    /// </summary>
    internal static string? TryGet(int? appId, string gameName, string? installPath, SteamArtworkService.Kind kind)
    {
        try
        {
            lock (Lock)
            {
                if (!Refresh()) return null;
                if (_grid.Count == 0) return null;
                string suffix = kind == SteamArtworkService.Kind.Cover ? "p" : "_hero";
                foreach (var sc in MatchShortcuts(gameName, installPath))
                    if (_grid.TryGetValue(sc.AppId + suffix, out var f)) return f;
                if (appId is int id && id > 0 && _grid.TryGetValue(id + suffix, out var file)) return file;
            }
        }
        catch (IOException ex) { CrashReporter.Log($"[SteamLibraryArtService.TryGet] {gameName}: {ex.Message}"); }
        catch (UnauthorizedAccessException ex) { CrashReporter.Log($"[SteamLibraryArtService.TryGet] {gameName}: {ex.Message}"); }
        return null;
    }

    /// <summary>Re-reads the grid folder and shortcuts.vdf when they changed. False when there's no Steam account folder.</summary>
    private static bool Refresh()
    {
        // The account rarely changes: look it up again at most once a minute
        if (_configDir == null || DateTime.UtcNow - _configCheckedUtc > TimeSpan.FromMinutes(1))
        {
            _configCheckedUtc = DateTime.UtcNow;
            var dir = "";
            if (SteamAchievementService.SteamPath() is { } steam && SteamAchievementService.AccountId(steam) is { } account)
            {
                var candidate = Path.Combine(steam, "userdata", account.ToString(), "config");
                if (Directory.Exists(candidate)) dir = candidate;
            }
            if (!string.Equals(dir, _configDir, StringComparison.OrdinalIgnoreCase))
            {
                _configDir = dir;
                _gridStampUtc = default;
                _shortcutsStampUtc = default;
                _grid = new(StringComparer.OrdinalIgnoreCase);
                _shortcuts = new();
                if (dir.Length > 0) CrashReporter.Log($"[SteamLibraryArtService.Refresh] Steam library art from {dir}");
            }
        }
        if (string.IsNullOrEmpty(_configDir)) return false;

        // A folder's write time changes when files are added, removed or renamed
        var gridDir = Path.Combine(_configDir, "grid");
        var gridStamp = Directory.Exists(gridDir) ? Directory.GetLastWriteTimeUtc(gridDir) : DateTime.MinValue;
        if (gridStamp != _gridStampUtc)
        {
            _gridStampUtc = gridStamp;
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (gridStamp != DateTime.MinValue)
                foreach (var f in Directory.EnumerateFiles(gridDir))
                {
                    var ext = Path.GetExtension(f);
                    if (!Extensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) continue;
                    var stem = Path.GetFileNameWithoutExtension(f);
                    // png before jpg when both exist (Steam writes one; tools sometimes leave the other)
                    if (!map.TryGetValue(stem, out var existing) || Rank(ext) < Rank(Path.GetExtension(existing)))
                        map[stem] = f;
                }
            _grid = map;
        }

        var vdf = Path.Combine(_configDir, "shortcuts.vdf");
        var vdfStamp = File.Exists(vdf) ? File.GetLastWriteTimeUtc(vdf) : DateTime.MinValue;
        if (vdfStamp != _shortcutsStampUtc)
        {
            _shortcutsStampUtc = vdfStamp;
            _shortcuts = vdfStamp == DateTime.MinValue ? new() : ParseShortcuts(File.ReadAllBytes(vdf));
        }
        return true;
    }

    private static int Rank(string ext) => Array.FindIndex(Extensions, e => e.Equals(ext, StringComparison.OrdinalIgnoreCase));

    /// <summary>Shortcuts for this game: its exe is inside the game's folder, or the names match ignoring punctuation.</summary>
    internal static IEnumerable<Shortcut> MatchShortcuts(string gameName, string? installPath) =>
        MatchShortcuts(_shortcuts, gameName, installPath);

    internal static IEnumerable<Shortcut> MatchShortcuts(IReadOnlyList<Shortcut> shortcuts, string gameName, string? installPath)
    {
        if (shortcuts.Count == 0) yield break;
        string? folder = string.IsNullOrWhiteSpace(installPath)
            ? null : installPath.Replace('/', '\\').TrimEnd('\\') + "\\";
        var name = PcgwCentralData.LooseKey(gameName);
        foreach (var sc in shortcuts)
        {
            var exe = sc.Exe.Trim().Trim('"').Replace('/', '\\');
            bool byPath = folder != null && folder.Length > 3 && exe.StartsWith(folder, StringComparison.OrdinalIgnoreCase);
            bool byName = name.Length > 0 && PcgwCentralData.LooseKey(sc.AppName) == name;
            if (byPath || byName) yield return sc;
        }
    }

    // ── shortcuts.vdf (binary KeyValues) ─────────────────────────────────────
    // Each entry: type byte, NUL-terminated UTF-8 name, value. 0x00 = nested map (ends with 0x08),
    // 0x01 = string, 0x02 = int32, 0x03 = float, 0x04 = pointer, 0x06 = colour (4 bytes), 0x07/0x0A = 64-bit.

    /// <summary>The shortcuts in a shortcuts.vdf file. Entries without an id are skipped; a damaged file gives what was read.</summary>
    internal static List<Shortcut> ParseShortcuts(byte[] data)
    {
        var result = new List<Shortcut>();
        int pos = 0;
        try
        {
            var root = ReadMap(data, ref pos);
            if (!TryGet(root, "shortcuts", out var listObj) || listObj is not Dictionary<string, object> list) return result;
            foreach (var entry in list.Values)
            {
                if (entry is not Dictionary<string, object> sc) continue;
                if (!TryGet(sc, "appid", out var idObj) || idObj is not int id || id == 0) continue;
                result.Add(new Shortcut(unchecked((uint)id),
                    TryGet(sc, "AppName", out var n) && n is string ns ? ns : "",
                    TryGet(sc, "Exe", out var e) && e is string es ? es : "",
                    TryGet(sc, "StartDir", out var d) && d is string ds ? ds : ""));
            }
        }
        catch (IndexOutOfRangeException) { CrashReporter.Log($"[SteamLibraryArtService.ParseShortcuts] File ends early; read {result.Count} shortcut(s)"); }
        catch (ArgumentException ex) { CrashReporter.Log($"[SteamLibraryArtService.ParseShortcuts] {ex.Message}"); }
        return result;
    }

    private static bool TryGet(Dictionary<string, object> map, string key, out object? value)
    {
        foreach (var kv in map)
            if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)) { value = kv.Value; return true; }
        value = null;
        return false;
    }

    private static Dictionary<string, object> ReadMap(byte[] d, ref int pos)
    {
        var map = new Dictionary<string, object>(StringComparer.Ordinal);
        while (pos < d.Length)
        {
            byte type = d[pos++];
            if (type == 0x08) break;
            var name = ReadString(d, ref pos);
            object value;
            switch (type)
            {
                case 0x00: value = ReadMap(d, ref pos); break;
                case 0x01: value = ReadString(d, ref pos); break;
                case 0x02: value = BitConverter.ToInt32(d, pos); pos += 4; break;
                case 0x03 or 0x04 or 0x06: value = 0; pos += 4; break;
                case 0x07 or 0x0A: value = 0L; pos += 8; break;
                default: throw new ArgumentException($"Unknown value type 0x{type:X2} at {pos}");
            }
            map[name] = value;
        }
        return map;
    }

    private static string ReadString(byte[] d, ref int pos)
    {
        int end = Array.IndexOf(d, (byte)0, pos);
        if (end < 0) throw new IndexOutOfRangeException();
        var s = System.Text.Encoding.UTF8.GetString(d, pos, end - pos);
        pos = end + 1;
        return s;
    }
}
