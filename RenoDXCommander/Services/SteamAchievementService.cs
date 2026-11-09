// SteamAchievementService.cs -- the selected Steam game's achievements for the Spotlight hero and
// side panel, read from Steam's own local cache (no API key, sign-in or public profile):
//   <Steam>\appcache\stats\UserGameStatsSchema_<appid>.bin            names, descriptions, icons, hidden flag
//   <Steam>\appcache\stats\UserGameStats_<accountid>_<appid>.bin      unlocked bits and unlock times
// Both are binary KeyValues (SteamBinaryKv). Rarity comes from Steam's keyless global stats
// (GetGlobalAchievementPercentagesForApp), cached for a day. Icons are cached in
// %LocalAppData%\RHI\artwork\{appid}\ach\. Only used while Settings › Achievements › Steam is on.
// Plan: docs/ROADMAP.md, "Steam Achievements in Spotlight".

using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace RenoDXCommander.Services;

internal static class SteamAchievementService
{
    private const ulong SteamId64Base = 76561197960265728UL;

    private static readonly string ArtRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "artwork");

    private static readonly TimeSpan RarityMaxAge = TimeSpan.FromDays(1);
    // Newer games use 40-character hash names; older Valve games (Half-Life 2, Portal) use readable ones
    // like "hl2_hit_cancop_withcan.jpg". Plain names only: no slashes or extra dots (used in a path and a URL).
    private static readonly Regex IconName = new(@"^[A-Za-z0-9_-]{1,100}\.(jpg|jpeg|png)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly SemaphoreSlim Gate = new(4); // parallel icon downloads
    private static readonly ConcurrentDictionary<string, Task<string?>> IconsInFlight = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<int, (DateTime Fetched, Dictionary<string, double> Map)> RarityCache = new();
    private static readonly ConcurrentDictionary<int, Task<Dictionary<string, double>?>> RarityInFlight = new();
    private static readonly ConcurrentDictionary<int, (DateTime Schema, DateTime Stats, string Lang, AchievementSet Set)> SetCache = new();

    // ── Steam folders and account ─────────────────────────────────────────────

    /// <summary>Steam's install folder (backslashes), or null when Steam isn't installed.</summary>
    internal static string? SteamPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (key?.GetValue("SteamPath") is string p && p.Length > 0) return p.Replace('/', '\\').TrimEnd('\\');
        }
        catch (System.Security.SecurityException ex) { CrashReporter.Log($"[SteamAchievementService.SteamPath] {ex.Message}"); }
        catch (IOException ex) { CrashReporter.Log($"[SteamAchievementService.SteamPath] {ex.Message}"); }
        return null;
    }

    /// <summary>The folder Steam keeps achievement caches in, or null.</summary>
    internal static string? StatsFolder()
    {
        var steam = SteamPath();
        if (steam == null) return null;
        var dir = Path.Combine(steam, "appcache", "stats");
        return Directory.Exists(dir) ? dir : null;
    }

    /// <summary>Steam's display language ("english", "german", …).</summary>
    private static string Language()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            if (key?.GetValue("Language") is string l && l.Length > 0) return l;
        }
        catch (System.Security.SecurityException ex) { CrashReporter.Log($"[SteamAchievementService.Language] {ex.Message}"); }
        catch (IOException ex) { CrashReporter.Log($"[SteamAchievementService.Language] {ex.Message}"); }
        return "english";
    }

    /// <summary>
    /// The signed-in Steam account id: the running Steam's ActiveUser, else the most recent user in
    /// loginusers.vdf (or the only one). Null when none can be found.
    /// </summary>
    internal static uint? AccountId(string steamPath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess");
            if (key?.GetValue("ActiveUser") is int active && active > 0) return (uint)active;
        }
        catch (System.Security.SecurityException ex) { CrashReporter.Log($"[SteamAchievementService.AccountId] {ex.Message}"); }
        catch (IOException ex) { CrashReporter.Log($"[SteamAchievementService.AccountId] {ex.Message}"); }

        try
        {
            var vdf = Path.Combine(steamPath, "config", "loginusers.vdf");
            if (!File.Exists(vdf)) return null;
            return ParseLoginUsers(File.ReadAllText(vdf));
        }
        catch (IOException ex) { CrashReporter.Log($"[SteamAchievementService.AccountId] {ex.Message}"); }
        catch (UnauthorizedAccessException ex) { CrashReporter.Log($"[SteamAchievementService.AccountId] {ex.Message}"); }
        return null;
    }

    /// <summary>The account id of the MostRecent user in loginusers.vdf text (or the only user).</summary>
    internal static uint? ParseLoginUsers(string vdf)
    {
        var users = Regex.Matches(vdf, "\"(7656\\d{13})\"\\s*\\{([^}]*)\\}");
        ulong? pick = null;
        foreach (Match m in users)
        {
            if (!ulong.TryParse(m.Groups[1].Value, out var id64)) continue;
            if (Regex.IsMatch(m.Groups[2].Value, "\"MostRecent\"\\s*\"1\"", RegexOptions.IgnoreCase)) { pick = id64; break; }
            if (users.Count == 1) pick = id64;
        }
        if (pick is not ulong v || v <= SteamId64Base) return null;
        return (uint)(v - SteamId64Base);
    }

    // ── Loading ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The game's achievements from Steam's cache, or null when there is no (readable) schema for it.
    /// File reads: call it off the UI thread. Re-reads only when a cache file has changed.
    /// </summary>
    internal static AchievementSet? Load(int appId)
    {
        var steam = SteamPath();
        if (steam == null) return null;
        var dir = Path.Combine(steam, "appcache", "stats");
        var schemaPath = Path.Combine(dir, $"UserGameStatsSchema_{appId}.bin");
        try
        {
            if (!File.Exists(schemaPath)) return null;
            var statsPath = FindStatsFile(dir, steam, appId);
            var schemaTime = File.GetLastWriteTimeUtc(schemaPath);
            var statsTime = statsPath != null ? File.GetLastWriteTimeUtc(statsPath) : DateTime.MinValue;
            var lang = Language();
            if (SetCache.TryGetValue(appId, out var c) && c.Schema == schemaTime && c.Stats == statsTime && c.Lang == lang)
                return WithRarity(c.Set, PeekRarity(appId));

            var schema = SteamBinaryKv.Parse(File.ReadAllBytes(schemaPath));
            var stats = statsPath != null ? SteamBinaryKv.Parse(File.ReadAllBytes(statsPath)) : null;
            var set = Build(appId, schema, stats, lang);
            if (set == null) return null;
            SetCache[appId] = (schemaTime, statsTime, lang, set);
            CrashReporter.Log($"[SteamAchievementService.Load] {appId}: {set.Unlocked}/{set.Total} unlocked{(statsPath == null ? " (no stats file)" : "")}");
            return WithRarity(set, PeekRarity(appId));
        }
        catch (FormatException ex) { CrashReporter.Log($"[SteamAchievementService.Load] {appId}: unreadable cache — {ex.Message}"); }
        catch (IOException ex) { CrashReporter.Log($"[SteamAchievementService.Load] {appId}: {ex.Message}"); }
        catch (UnauthorizedAccessException ex) { CrashReporter.Log($"[SteamAchievementService.Load] {appId}: {ex.Message}"); }
        return null;
    }

    /// <summary>The signed-in account's stats file; the newest one for the game when the account isn't known.</summary>
    private static string? FindStatsFile(string dir, string steamPath, int appId)
    {
        if (AccountId(steamPath) is uint acct)
        {
            var path = Path.Combine(dir, $"UserGameStats_{acct}_{appId}.bin");
            return File.Exists(path) ? path : null;
        }
        return Directory.EnumerateFiles(dir, $"UserGameStats_*_{appId}.bin")
            .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
    }

    /// <summary>
    /// Builds the list from parsed schema and stats trees. Pure (unit tested).
    /// Schema: {appid} → stats → {block} → bits → {bit} → name, display → name / desc (per language), hidden, icon, icon_gray.
    /// Stats:  cache → {block} → data (unlocked bitfield), AchievementTimes → {bit} = unix time.
    /// </summary>
    internal static AchievementSet? Build(int appId, Dictionary<string, object> schema, Dictionary<string, object>? stats, string language)
    {
        var root = schema.TryGetValue(appId.ToString(CultureInfo.InvariantCulture), out var r) && r is Dictionary<string, object> rd
            ? rd
            : schema.Values.OfType<Dictionary<string, object>>().FirstOrDefault();
        if (root == null || Child(root, "stats") is not { } blocks) return null;
        var cache = stats != null ? Child(stats, "cache") : null;

        var unlocked = new List<(Achievement A, long Order)>();
        var locked = new List<(Achievement A, long Order)>();
        foreach (var (blockKey, blockObj) in blocks.OrderBy(kv => NumKey(kv.Key)))
        {
            if (blockObj is not Dictionary<string, object> block || Child(block, "bits") is not { } bits) continue;
            var userBlock = cache != null ? Child(cache, blockKey) : null;
            ulong mask = userBlock != null && userBlock.TryGetValue("data", out var d) ? ToMask(d) : 0;
            var times = userBlock != null ? Child(userBlock, "AchievementTimes") : null;

            foreach (var (bitKey, bitObj) in bits.OrderBy(kv => NumKey(kv.Key)))
            {
                if (bitObj is not Dictionary<string, object> bit) continue;
                if (!int.TryParse(bitKey, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bitIndex) || bitIndex is < 0 or > 63) continue;
                var display = Child(bit, "display");
                var apiName = bit.TryGetValue("name", out var n) && n is string ns ? ns : $"{blockKey}_{bitKey}";
                var name = Localised(display, "name", language) ?? apiName;
                var desc = Localised(display, "desc", language) ?? "";
                bool hidden = display != null && display.TryGetValue("hidden", out var h) && ToLong(h) != 0;
                bool isUnlocked = (mask & (1UL << bitIndex)) != 0;
                DateTime? when = null;
                if (isUnlocked && times != null && times.TryGetValue(bitKey, out var t) && ToLong(t) > 0)
                    when = DateTimeOffset.FromUnixTimeSeconds(ToLong(t)).UtcDateTime;
                var a = new Achievement(apiName, name, desc, hidden, isUnlocked, when,
                    IconFile(display, "icon"), IconFile(display, "icon_gray"));
                long order = NumKey(blockKey) * 64 + bitIndex;
                (isUnlocked ? unlocked : locked).Add((a, order));
            }
        }
        if (unlocked.Count + locked.Count == 0) return null;

        var items = unlocked
            .OrderByDescending(x => x.A.UnlockedUtc ?? DateTime.MinValue).ThenBy(x => x.Order)
            .Concat(locked.OrderBy(x => x.Order))
            .Select(x => x.A).ToList();
        return new AchievementSet(AchievementStore.Steam, appId.ToString(CultureInfo.InvariantCulture), items);
    }

    private static Dictionary<string, object>? Child(Dictionary<string, object> node, string key) =>
        node.TryGetValue(key, out var v) ? v as Dictionary<string, object> : null;

    private static long NumKey(string key) =>
        long.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : long.MaxValue / 128;

    private static long ToLong(object v) => v switch
    {
        int i => i,
        long l => l,
        ulong u => (long)u,
        float f => (long)f,
        string s when long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) => p,
        _ => 0,
    };

    // The bitfield is stored as a signed int32: keep its bits, don't sign-extend into the upper half
    private static ulong ToMask(object v) => v switch
    {
        int i => (uint)i,
        ulong u => u,
        long l => (ulong)l,
        _ => (ulong)ToLong(v),
    };

    /// <summary>A display field in Steam's language, English, or any language (some games store a plain string).</summary>
    private static string? Localised(Dictionary<string, object>? display, string field, string language)
    {
        if (display == null || !display.TryGetValue(field, out var v)) return null;
        if (v is string s) return s.Length > 0 ? s : null;
        if (v is not Dictionary<string, object> langs) return null;
        foreach (var key in new[] { language, "english" })
            if (langs.TryGetValue(key, out var x) && x is string xs && xs.Length > 0) return xs;
        return langs.Where(kv => !string.Equals(kv.Key, "token", StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Value).OfType<string>().FirstOrDefault(x => x.Length > 0);
    }

    /// <summary>An icon file name, only when it's a plain file name (it's used in a path and a URL).</summary>
    private static string? IconFile(Dictionary<string, object>? display, string field) =>
        display != null && display.TryGetValue(field, out var v) && v is string s && IconName.IsMatch(s) ? s : null;

    // ── Rarity ────────────────────────────────────────────────────────────────

    private static string RarityPath(int appId) => Path.Combine(ArtRoot, appId.ToString(CultureInfo.InvariantCulture), "ach", "rarity.json");

    /// <summary>The rarity already known for the game (memory or a fresh disk copy), without a download.</summary>
    private static Dictionary<string, double>? PeekRarity(int appId)
    {
        if (RarityCache.TryGetValue(appId, out var c) && DateTime.UtcNow - c.Fetched < RarityMaxAge) return c.Map;
        var disk = ReadRarityFile(appId, maxAge: RarityMaxAge);
        if (disk != null) RarityCache[appId] = (DateTime.UtcNow, disk);
        return disk;
    }

    /// <summary>Adds rarity to a set. Returns the same set when there's no rarity.</summary>
    internal static AchievementSet WithRarity(AchievementSet set, Dictionary<string, double>? rarity)
    {
        if (rarity == null || rarity.Count == 0) return set;
        return set with
        {
            Items = set.Items.Select(a => rarity.TryGetValue(a.ApiName, out var p) ? a with { Percent = p } : a).ToList(),
        };
    }

    /// <summary>The game's global unlock percentages by API name (cached for a day). Null when unavailable.</summary>
    internal static Task<Dictionary<string, double>?> GetRarityAsync(int appId, CancellationToken ct)
    {
        if (PeekRarity(appId) is { } known) return Task.FromResult<Dictionary<string, double>?>(known);
        return RarityInFlight.GetOrAdd(appId, id => Task.Run(async () =>
        {
            try { return await FetchRarityAsync(id, ct).ConfigureAwait(false); }
            finally { RarityInFlight.TryRemove(id, out _); }
        }, CancellationToken.None));
    }

    private static async Task<Dictionary<string, double>?> FetchRarityAsync(int appId, CancellationToken ct)
    {
        try
        {
            var http = App.Services.GetRequiredService<HttpClient>();
            var json = await http.GetStringAsync(
                $"https://api.steampowered.com/ISteamUserStats/GetGlobalAchievementPercentagesForApp/v0002/?gameid={appId}", ct)
                .ConfigureAwait(false);
            var map = ParseRarity(json);
            if (map == null) return ReadRarityFile(appId, maxAge: null);
            RarityCache[appId] = (DateTime.UtcNow, map);
            try
            {
                var path = RarityPath(appId);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path + ".tmp", json, ct).ConfigureAwait(false);
                File.Move(path + ".tmp", path, overwrite: true);
            }
            catch (IOException ex) { CrashReporter.Log($"[SteamAchievementService.FetchRarityAsync] Can't save {appId} — {ex.Message}"); }
            catch (UnauthorizedAccessException ex) { CrashReporter.Log($"[SteamAchievementService.FetchRarityAsync] Can't save {appId} — {ex.Message}"); }
            return map;
        }
        catch (HttpRequestException ex) { CrashReporter.Log($"[SteamAchievementService.FetchRarityAsync] {appId}: {ex.Message}"); }
        catch (OperationCanceledException) { return null; }
        // Offline or failed: an older copy is better than nothing
        return ReadRarityFile(appId, maxAge: null);
    }

    private static Dictionary<string, double>? ReadRarityFile(int appId, TimeSpan? maxAge)
    {
        try
        {
            var path = RarityPath(appId);
            if (!File.Exists(path)) return null;
            if (maxAge is { } age && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) >= age) return null;
            return ParseRarity(File.ReadAllText(path));
        }
        catch (IOException ex) { CrashReporter.Log($"[SteamAchievementService.ReadRarityFile] {appId}: {ex.Message}"); }
        catch (UnauthorizedAccessException ex) { CrashReporter.Log($"[SteamAchievementService.ReadRarityFile] {appId}: {ex.Message}"); }
        return null;
    }

    /// <summary>Parses GetGlobalAchievementPercentagesForApp JSON (percent is a string or a number).</summary>
    internal static Dictionary<string, double>? ParseRarity(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("achievementpercentages", out var ap)
                || !ap.TryGetProperty("achievements", out var list) || list.ValueKind != JsonValueKind.Array)
                return null;
            var map = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var a in list.EnumerateArray())
            {
                if (!a.TryGetProperty("name", out var n) || n.GetString() is not { Length: > 0 } name) continue;
                if (!a.TryGetProperty("percent", out var p)) continue;
                double value;
                if (p.ValueKind == JsonValueKind.Number) value = p.GetDouble();
                else if (p.ValueKind == JsonValueKind.String
                         && double.TryParse(p.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var s)) value = s;
                else continue;
                map[name] = Math.Clamp(value, 0, 100);
            }
            return map;
        }
        catch (JsonException ex) { CrashReporter.Log($"[SteamAchievementService.ParseRarity] {ex.Message}"); }
        return null;
    }

    // ── Icons ─────────────────────────────────────────────────────────────────

    private static string IconPath(int appId, string file) =>
        Path.Combine(ArtRoot, appId.ToString(CultureInfo.InvariantCulture), "ach", file);

    /// <summary>The cached icon file, or null when it isn't downloaded yet.</summary>
    internal static string? TryGetCachedIcon(int appId, string? file)
    {
        if (file == null || !IconName.IsMatch(file)) return null;
        var path = IconPath(appId, file);
        return File.Exists(path) ? path : null;
    }

    /// <summary>The local icon file, downloading it if needed. Null on failure.</summary>
    internal static Task<string?> GetIconAsync(int appId, string? file, CancellationToken ct)
    {
        if (file == null || !IconName.IsMatch(file)) return Task.FromResult<string?>(null);
        if (TryGetCachedIcon(appId, file) is { } cached) return Task.FromResult<string?>(cached);
        var key = $"{appId}/{file}";
        return IconsInFlight.GetOrAdd(key, _ => Task.Run(async () =>
        {
            try { return await DownloadIconAsync(appId, file, ct).ConfigureAwait(false); }
            finally { IconsInFlight.TryRemove(key, out Task<string?>? _); }
        }, CancellationToken.None));
    }

    private static async Task<string?> DownloadIconAsync(int appId, string file, CancellationToken ct)
    {
        try { await Gate.WaitAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { return null; }
        try
        {
            var http = App.Services.GetRequiredService<HttpClient>();
            foreach (var url in new[]
            {
                $"https://cdn.cloudflare.steamstatic.com/steamcommunity/public/images/apps/{appId}/{file}",
                $"https://shared.akamai.steamstatic.com/community_assets/images/apps/{appId}/{file}",
            })
            {
                using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) continue;
                var type = resp.Content.Headers.ContentType?.MediaType ?? "";
                if (!type.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) continue;
                var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                if (bytes.Length < 100) continue;
                var dest = IconPath(appId, file);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    var tmp = dest + ".tmp";
                    await File.WriteAllBytesAsync(tmp, bytes, ct).ConfigureAwait(false);
                    File.Move(tmp, dest, overwrite: true);
                    return dest;
                }
                catch (IOException ex) { CrashReporter.Log($"[SteamAchievementService.DownloadIconAsync] Can't save {dest} — {ex.Message}"); }
                catch (UnauthorizedAccessException ex) { CrashReporter.Log($"[SteamAchievementService.DownloadIconAsync] Can't save {dest} — {ex.Message}"); }
                return null;
            }
            return null;
        }
        catch (OperationCanceledException) { return null; }
        catch (HttpRequestException ex)
        {
            CrashReporter.Log($"[SteamAchievementService.DownloadIconAsync] {appId}/{file}: {ex.Message}");
            return null;
        }
        finally { Gate.Release(); }
    }
}

/// <summary>
/// Reader for Valve's binary KeyValues (appcache\stats files). Types: 0 subtree, 1 string, 2/4/6 int32,
/// 3 float, 7 uint64, 8/11 end of subtree. Keys are case-insensitive. Throws FormatException on bad data.
/// </summary>
internal static class SteamBinaryKv
{
    private const int MaxDepth = 32;

    internal static Dictionary<string, object> Parse(byte[] data)
    {
        int pos = 0;
        var root = ReadNode(data, ref pos, 0);
        return root;
    }

    private static Dictionary<string, object> ReadNode(byte[] b, ref int pos, int depth)
    {
        if (depth > MaxDepth) throw new FormatException("KeyValues nested too deeply");
        var node = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            // A file may end without the final end marker
            if (pos >= b.Length) return node;
            byte type = b[pos++];
            if (type is 8 or 11) return node;
            var key = ReadString(b, ref pos);
            object value = type switch
            {
                0 => ReadNode(b, ref pos, depth + 1),
                1 => ReadString(b, ref pos),
                2 or 4 or 6 => ReadInt32(b, ref pos),
                3 => ReadFloat(b, ref pos),
                7 => ReadUInt64(b, ref pos),
                _ => throw new FormatException($"Unknown KeyValues type {type} at {pos - 1}"),
            };
            node[key] = value;
        }
    }

    private static string ReadString(byte[] b, ref int pos)
    {
        int end = Array.IndexOf(b, (byte)0, pos);
        if (end < 0) throw new FormatException("Unterminated KeyValues string");
        var s = System.Text.Encoding.UTF8.GetString(b, pos, end - pos);
        pos = end + 1;
        return s;
    }

    private static int ReadInt32(byte[] b, ref int pos)
    {
        if (pos + 4 > b.Length) throw new FormatException("Truncated KeyValues int");
        var v = BitConverter.ToInt32(b, pos);
        pos += 4;
        return v;
    }

    private static float ReadFloat(byte[] b, ref int pos)
    {
        if (pos + 4 > b.Length) throw new FormatException("Truncated KeyValues float");
        var v = BitConverter.ToSingle(b, pos);
        pos += 4;
        return v;
    }

    private static ulong ReadUInt64(byte[] b, ref int pos)
    {
        if (pos + 8 > b.Length) throw new FormatException("Truncated KeyValues uint64");
        var v = BitConverter.ToUInt64(b, pos);
        pos += 8;
        return v;
    }
}
