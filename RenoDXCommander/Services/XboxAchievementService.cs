// XboxAchievementService.cs -- the selected Xbox / Game Pass game's achievements for the Spotlight hero
// and side panel, read from the Xbox app's own local cache (no sign-in or API key):
//   %LocalAppData%\Packages\Microsoft.GamingApp_8wekyb3d8bbwe\LocalState\AsyncCache.db
// A SQLite file (read with Windows' winsqlite3, see WinSqlite.cs), one table
// AsyncCache(scope, scopeVersion, key, value) with JSON values:
//   achievements / achievements_<xuid>_<titleId>   the game's list: unlocked, timeUnlocked, rarity, icons
//   achievements / titleInfo_<xuid>_<titleId>      the game's name (for matching by name)
//   product_altid_pfn / <PackageFamilyName>        Store product id
//   product_summary / <productId>                  alternateIds → XBOXTITLEID
//   product_altid_titleid / <titleId>              Store product id (reverse lookup)
// The Xbox app fills it when it shows a game, so it's a snapshot from then. Undocumented: anything
// unexpected means "no achievements" rather than an error. Only used while Settings › Achievements ›
// Xbox is on. Icons are cached in %LocalAppData%\RHI\artwork\xbox\{titleId}\ach\.

using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;

namespace RenoDXCommander.Services;

internal static class XboxAchievementService
{
    private const string DbName = "AsyncCache.db";

    private static readonly string IconRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "artwork", "xbox");

    private static readonly Regex Digits = new(@"^\d{1,12}$", RegexOptions.Compiled);      // title ids
    private static readonly Regex XuidDigits = new(@"^\d{1,20}$", RegexOptions.Compiled);  // player ids (16 digits)
    private static readonly Regex ProductId = new(@"^[A-Za-z0-9]{12}$", RegexOptions.Compiled);
    private static readonly Regex IconFileName = new(@"^[0-9a-f]{40}\.png$", RegexOptions.Compiled);

    private static readonly SemaphoreSlim Gate = new(4); // parallel icon downloads
    private static readonly ConcurrentDictionary<string, Task<string?>> IconsInFlight = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, (DateTime Db, DateTime Wal, AchievementSet? Set)> SetCache = new(StringComparer.OrdinalIgnoreCase);
    private static bool _sqliteMissing;

    // ── Folders and ids ───────────────────────────────────────────────────────

    /// <summary>The Xbox app's LocalState folder when its cache exists, else null.</summary>
    internal static string? CacheFolder()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Packages", "Microsoft.GamingApp_8wekyb3d8bbwe", "LocalState");
        return File.Exists(Path.Combine(dir, DbName)) ? dir : null;
    }

    /// <summary>True for a file the watcher should react to (the database or its journal / WAL).</summary>
    internal static bool IsCacheFile(string? name) =>
        name != null && name.StartsWith(DbName, StringComparison.OrdinalIgnoreCase);

    /// <summary>"Publisher.Game_hash!App" → "Publisher.Game_hash"; null when there's no AUMID.</summary>
    internal static string? PfnFromAumid(string? aumid)
    {
        if (string.IsNullOrWhiteSpace(aumid)) return null;
        int bang = aumid.IndexOf('!');
        var pfn = (bang >= 0 ? aumid[..bang] : aumid).Trim();
        return pfn.Length > 0 ? pfn : null;
    }

    // ── Loading ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The game's achievements from the Xbox app's cache, found by package family name (or, without one,
    /// by name). Null when the cache has none for it. Database reads: call it off the UI thread.
    /// Re-reads only when the database has changed.
    /// </summary>
    internal static AchievementSet? Load(string? pfn, string gameName)
    {
        if (_sqliteMissing) return null;
        var dir = CacheFolder();
        if (dir == null) return null;
        var dbPath = Path.Combine(dir, DbName);
        var cacheKey = (pfn ?? "") + "|" + gameName;
        try
        {
            var dbTime = File.GetLastWriteTimeUtc(dbPath);
            var walPath = dbPath + "-wal";
            var walTime = File.Exists(walPath) ? File.GetLastWriteTimeUtc(walPath) : DateTime.MinValue;
            if (SetCache.TryGetValue(cacheKey, out var c) && c.Db == dbTime && c.Wal == walTime) return c.Set;

            var set = ReadWithFallback(dbPath, db => Read(db, pfn, gameName));
            SetCache[cacheKey] = (dbTime, walTime, set);
            CrashReporter.Log(set == null
                ? $"[XboxAchievementService.Load] '{gameName}' ({pfn ?? "no PFN"}): no achievements in the Xbox app's cache"
                : $"[XboxAchievementService.Load] '{gameName}' title {set.GameId}: {set.Unlocked}/{set.Total} unlocked");
            return set;
        }
        catch (DllNotFoundException ex)
        {
            _sqliteMissing = true;
            CrashReporter.Log($"[XboxAchievementService.Load] winsqlite3 unavailable — {ex.Message}");
        }
        catch (EntryPointNotFoundException ex)
        {
            _sqliteMissing = true;
            CrashReporter.Log($"[XboxAchievementService.Load] winsqlite3 unusable — {ex.Message}");
        }
        catch (WinSqliteException ex) { CrashReporter.Log($"[XboxAchievementService.Load] '{gameName}': {ex.Message}"); }
        catch (IOException ex) { CrashReporter.Log($"[XboxAchievementService.Load] '{gameName}': {ex.Message}"); }
        catch (UnauthorizedAccessException ex) { CrashReporter.Log($"[XboxAchievementService.Load] '{gameName}': {ex.Message}"); }
        return null;
    }

    /// <summary>
    /// Reads the live database read-only; if the Xbox app has it locked or mid-write, reads a private
    /// copy instead (with its WAL, if any).
    /// </summary>
    private static T ReadWithFallback<T>(string dbPath, Func<WinSqliteDb, T> read)
    {
        try
        {
            using var db = WinSqliteDb.Open(dbPath);
            return read(db);
        }
        catch (WinSqliteException ex)
        {
            CrashReporter.Log($"[XboxAchievementService.ReadWithFallback] Live read failed ({ex.Message}), reading a copy");
        }
        var tmpDir = Path.Combine(Path.GetTempPath(), "RHI", "xboxcache-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tmpDir);
            var copy = Path.Combine(tmpDir, DbName);
            File.Copy(dbPath, copy);
            if (File.Exists(dbPath + "-wal")) File.Copy(dbPath + "-wal", copy + "-wal");
            using var db = WinSqliteDb.Open(copy);
            return read(db);
        }
        finally
        {
            try { Directory.Delete(tmpDir, recursive: true); }
            catch (IOException ex) { CrashReporter.Log($"[XboxAchievementService.ReadWithFallback] Can't delete {tmpDir} — {ex.Message}"); }
            catch (UnauthorizedAccessException ex) { CrashReporter.Log($"[XboxAchievementService.ReadWithFallback] Can't delete {tmpDir} — {ex.Message}"); }
        }
    }

    private const string AchScope = "SELECT value FROM AsyncCache WHERE scope = 'achievements' AND key = ?1 ORDER BY scopeVersion DESC LIMIT 1";

    private static AchievementSet? Read(WinSqliteDb db, string? pfn, string gameName)
    {
        var xuid = OwnerXuid(db);
        if (xuid == null) return null;
        foreach (var titleId in CandidateTitleIds(db, xuid, pfn, gameName))
        {
            var json = db.Scalar(AchScope, $"achievements_{xuid}_{titleId}");
            if (json == null) continue;
            return Build(titleId, json);
        }
        return null;
    }

    /// <summary>
    /// The signed-in player: the cache also holds a few friends' lists (for comparing), so pick the XUID
    /// with the most achievement lists.
    /// </summary>
    private static string? OwnerXuid(WinSqliteDb db)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in db.Query("SELECT key FROM AsyncCache WHERE scope = 'achievements' AND key LIKE 'achievements!_%' ESCAPE '!'", 1))
        {
            var parts = row[0]?.Split('_');
            if (parts is not { Length: 3 } || !XuidDigits.IsMatch(parts[1])) continue;
            counts[parts[1]] = counts.GetValueOrDefault(parts[1]) + 1;
        }
        return counts.Count == 0 ? null : counts.OrderByDescending(kv => kv.Value).First().Key;
    }

    /// <summary>Title ids for the game, best first: PFN → product → title id, then a name match.</summary>
    private static IEnumerable<string> CandidateTitleIds(WinSqliteDb db, string xuid, string? pfn, string gameName)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (pfn != null)
        {
            var product = DataString(db.Scalar(
                "SELECT value FROM AsyncCache WHERE scope = 'product_altid_pfn' AND key = ?1 COLLATE NOCASE LIMIT 1", pfn));
            if (product != null && ProductId.IsMatch(product))
            {
                var summary = db.Scalar("SELECT value FROM AsyncCache WHERE scope = 'product_summary' AND key = ?1 LIMIT 1", product);
                foreach (var id in TitleIdsFromSummary(summary))
                    if (seen.Add(id)) yield return id;
                foreach (var row in db.Query(
                    "SELECT key FROM AsyncCache WHERE scope = 'product_altid_titleid' AND value LIKE ?1", 1,
                    $"%\"data\":\"{product}\"%"))
                    if (row[0] is { } id && Digits.IsMatch(id) && seen.Add(id)) yield return id;
            }
        }
        // No PFN (folder-scan detection) or no mapping: the cached title whose name matches
        var want = NormaliseName(gameName);
        if (want.Length == 0) yield break;
        foreach (var row in db.Query(
            "SELECT key, value FROM AsyncCache WHERE scope = 'achievements' AND key LIKE ?1 ESCAPE '!'", 2,
            $"titleInfo!_{xuid}!_%"))
        {
            var id = row[0]?.Split('_').LastOrDefault();
            if (id == null || !Digits.IsMatch(id) || seen.Contains(id)) continue;
            if (NormaliseName(TitleName(row[1])) == want && seen.Add(id)) yield return id;
        }
    }

    /// <summary>The "data" string of a cache value ({"type":2,"data":"9P8RMKXRML7D",…}).</summary>
    private static string? DataString(string? json)
    {
        if (json == null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.String
                ? d.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>XBOXTITLEID entries in a product_summary value.</summary>
    internal static List<string> TitleIdsFromSummary(string? json)
    {
        var ids = new List<string>();
        if (json == null) return ids;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
                || !data.TryGetProperty("alternateIds", out var alt) || alt.ValueKind != JsonValueKind.Array)
                return ids;
            foreach (var a in alt.EnumerateArray())
            {
                if (a.ValueKind != JsonValueKind.Object) continue;
                if (Str(a, "idType") is { } t && t.Equals("XBOXTITLEID", StringComparison.OrdinalIgnoreCase)
                    && Str(a, "id") is { } id && Digits.IsMatch(id))
                    ids.Add(id);
            }
        }
        catch (JsonException ex) { CrashReporter.Log($"[XboxAchievementService.TitleIdsFromSummary] {ex.Message}"); }
        return ids;
    }

    private static string? TitleName(string? json)
    {
        if (json == null) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object ? Str(doc.RootElement, "name") : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Letters and digits only, lower case ("Avowed™" = "avowed").</summary>
    internal static string NormaliseName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString();
    }

    /// <summary>
    /// Builds the list from an achievements_&lt;xuid&gt;_&lt;titleId&gt; value. Pure (unit tested). Null when the
    /// game has none (the Xbox app caches an empty list for games without achievements) or it's unreadable.
    /// </summary>
    internal static AchievementSet? Build(string titleId, string json)
    {
        if (!Digits.IsMatch(titleId)) return null;
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex)
        {
            CrashReporter.Log($"[XboxAchievementService.Build] {titleId}: unreadable — {ex.Message}");
            return null;
        }
        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            var unlocked = new List<(Achievement A, int Order)>();
            var locked = new List<(Achievement A, int Order)>();
            int order = 0;
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                order++;
                var id = Str(e, "id") ?? order.ToString(CultureInfo.InvariantCulture);
                var name = Str(e, "name");
                if (string.IsNullOrWhiteSpace(name)) continue;
                bool isUnlocked = Bool(e, "isUnlocked") && !Bool(e, "isRevoked");
                bool secret = Bool(e, "isSecret");
                string desc = "";
                if (e.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.Object)
                    desc = (isUnlocked ? Str(d, "unlocked") ?? Str(d, "locked") : Str(d, "locked") ?? Str(d, "unlocked")) ?? "";

                DateTime? when = null;
                if (isUnlocked && Str(e, "timeUnlocked") is { } ts
                    && DateTime.TryParse(ts, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t)
                    && t.Year >= 2000)
                    when = t;

                double? percent = null;
                if (e.TryGetProperty("rarity", out var r) && r.ValueKind == JsonValueKind.Object
                    && r.TryGetProperty("currentPercentage", out var rp) && rp.ValueKind == JsonValueKind.Number)
                    percent = Math.Clamp(rp.GetDouble(), 0, 100);

                int? score = null;
                if (e.TryGetProperty("gamerscore", out var g) && g.ValueKind == JsonValueKind.Number && g.TryGetInt32(out var gi) && gi >= 0)
                    score = gi;

                int? progress = null;
                if (!isUnlocked && e.TryGetProperty("percentComplete", out var pc) && pc.ValueKind == JsonValueKind.Number
                    && pc.TryGetDouble(out var pcv) && pcv >= 1 && pcv < 100)
                    progress = (int)Math.Floor(pcv);

                var icon = IconUrl(Str(e, "imageUrl"));
                var a = new Achievement(id, name.Trim(), desc.Trim(), secret, isUnlocked, when, icon, null)
                {
                    Percent = percent,
                    Gamerscore = score,
                    ProgressPercent = progress,
                };
                (isUnlocked ? unlocked : locked).Add((a, order));
            }
            if (unlocked.Count + locked.Count == 0) return null;
            var items = unlocked
                .OrderByDescending(x => x.A.UnlockedUtc ?? DateTime.MinValue).ThenBy(x => x.Order)
                .Concat(locked.OrderBy(x => x.Order))
                .Select(x => x.A).ToList();
            return new AchievementSet(AchievementStore.Xbox, titleId, items);
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>Only https icons from Xbox / Microsoft image hosts (the URL is downloaded).</summary>
    internal static string? IconUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var u)) return null;
        if (u.Scheme != Uri.UriSchemeHttps) return null;
        bool ok = u.Host.EndsWith(".xboxlive.com", StringComparison.OrdinalIgnoreCase)
               || u.Host.EndsWith(".s-microsoft.com", StringComparison.OrdinalIgnoreCase);
        return ok ? url : null;
    }

    // ── Icons ─────────────────────────────────────────────────────────────────

    private const string IconSize = "h=160&format=png";

    // The cache file name is a hash of the URL, so nothing from the cache ends up in a path
    private static string IconPath(string titleId, string url)
    {
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url + IconSize))).ToLowerInvariant();
        return Path.Combine(IconRoot, titleId, "ach", hash + ".png");
    }

    /// <summary>The cached icon file, or null when it isn't downloaded yet.</summary>
    internal static string? TryGetCachedIcon(string titleId, string? url)
    {
        if (url == null || !Digits.IsMatch(titleId) || IconUrl(url) == null) return null;
        var path = IconPath(titleId, url);
        return IconFileName.IsMatch(Path.GetFileName(path)) && File.Exists(path) ? path : null;
    }

    /// <summary>The local icon file, downloading it if needed. Null on failure.</summary>
    internal static Task<string?> GetIconAsync(string titleId, string? url, CancellationToken ct)
    {
        if (url == null || !Digits.IsMatch(titleId) || IconUrl(url) == null) return Task.FromResult<string?>(null);
        if (TryGetCachedIcon(titleId, url) is { } cached) return Task.FromResult<string?>(cached);
        var key = IconPath(titleId, url);
        return IconsInFlight.GetOrAdd(key, _ => Task.Run(async () =>
        {
            try { return await DownloadIconAsync(titleId, url, ct).ConfigureAwait(false); }
            finally { IconsInFlight.TryRemove(key, out Task<string?>? _); }
        }, CancellationToken.None));
    }

    private static async Task<string?> DownloadIconAsync(string titleId, string url, CancellationToken ct)
    {
        try { await Gate.WaitAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { return null; }
        try
        {
            var http = App.Services.GetRequiredService<HttpClient>();
            // The full-size images are 1920x1080 (~800 KB); the image service resizes on request and
            // keeps the shape, so ask by height: the square crop in RHI stays sharp
            var sized = url + (url.Contains('?') ? "&" : "?") + IconSize;
            using var resp = await http.GetAsync(sized, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            var type = resp.Content.Headers.ContentType?.MediaType ?? "";
            if (!type.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return null;
            var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            if (bytes.Length < 100) return null;
            var dest = IconPath(titleId, url);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                var tmp = dest + ".tmp";
                await File.WriteAllBytesAsync(tmp, bytes, ct).ConfigureAwait(false);
                File.Move(tmp, dest, overwrite: true);
                return dest;
            }
            catch (IOException ex) { CrashReporter.Log($"[XboxAchievementService.DownloadIconAsync] Can't save {dest} — {ex.Message}"); }
            catch (UnauthorizedAccessException ex) { CrashReporter.Log($"[XboxAchievementService.DownloadIconAsync] Can't save {dest} — {ex.Message}"); }
            return null;
        }
        catch (OperationCanceledException) { return null; }
        catch (HttpRequestException ex)
        {
            CrashReporter.Log($"[XboxAchievementService.DownloadIconAsync] {titleId}: {ex.Message}");
            return null;
        }
        finally { Gate.Release(); }
    }
}
