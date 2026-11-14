// SteamGridDbService.cs -- community artwork from SteamGridDB (https://www.steamgriddb.com) for Spotlight's
// artwork picker (game menu › Artwork › Browse covers / Browse banners). Each user brings their own free API key
// (steamgriddb.com/profile/preferences/api), saved in %LocalAppData%\RHI\steamgriddb_api.txt (not settings.json).
//
// API v2 (base https://www.steamgriddb.com/api/v2, "Authorization: Bearer <key>"; shapes checked against
// SteamGridDB's own node-steamgriddb and decky-steamgriddb clients):
//   covers  GET /grids/steam/{appid}  or /grids/game/{id}   dimensions=600x900,342x482,660x930
//   banners GET /heroes/steam/{appid} or /heroes/game/{id}
//   both with types=static&nsfw=false&humor=false&epilepsy=false&page=N (0-based)
//   search  GET /search/autocomplete/{term} → data[] { id, name, ... }
//   reply   { success, data[] { id, score, url, thumb, width, height, mime, author { name } }, errors[] }
// Lists are cached per game for 24 h and thumbnails on disk (artwork\sgdb\); full images are only downloaded
// when picked. The key is sent per request to steamgriddb.com only, never as a default header.

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace RenoDXCommander.Services;

internal static class SteamGridDbService
{
    internal const string ApiKeyPage = "https://www.steamgriddb.com/profile/preferences/api";
    private const string ApiBase = "https://www.steamgriddb.com/api/v2";
    private const int MinFullPage = 20; // a page with fewer items than this is treated as the last one

    internal enum Status { Ok, NoKey, BadKey, NotFound, Offline, Error }

    internal sealed record Art(int Id, string Url, string Thumb, int Width, int Height, string Mime, string Author, int Score);

    internal sealed record ArtPage(Status Status, IReadOnlyList<Art> Items, bool MayHaveMore);

    private static readonly string LocalRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI");
    private static readonly string KeyPath = Path.Combine(LocalRoot, "steamgriddb_api.txt");
    private static readonly string Root = Path.Combine(LocalRoot, "artwork", "sgdb");
    private static readonly string ChosenPath = Path.Combine(Root, "chosen.json");

    private static readonly TimeSpan ListMaxAge = TimeSpan.FromHours(24);
    private static readonly SemaphoreSlim ThumbGate = new(6);
    private static readonly ConcurrentDictionary<string, Task<string?>> ThumbsInFlight = new(StringComparer.Ordinal);
    private static readonly object ChosenLock = new();

    private static string? _key;
    private static DateTime _keyStamp = DateTime.MinValue;

    // ── API key ───────────────────────────────────────────────────────────────

    /// <summary>The saved API key, or null. Re-read when the file changes.</summary>
    internal static string? ApiKey
    {
        get
        {
            try
            {
                if (!File.Exists(KeyPath)) { _key = null; _keyStamp = DateTime.MinValue; return null; }
                var stamp = File.GetLastWriteTimeUtc(KeyPath);
                if (stamp != _keyStamp)
                {
                    var k = File.ReadAllText(KeyPath).Trim();
                    _key = k.Length > 0 ? k : null;
                    _keyStamp = stamp;
                }
                return _key;
            }
            catch (IOException ex) { CrashReporter.Log($"[SteamGridDbService.ApiKey] {ex.Message}"); }
            catch (UnauthorizedAccessException ex) { CrashReporter.Log($"[SteamGridDbService.ApiKey] {ex.Message}"); }
            return null;
        }
    }

    internal static bool HasKey => ApiKey != null;

    /// <summary>Saves the key (already validated). Returns an error message, or null on success.</summary>
    internal static string? SaveKey(string key)
    {
        try
        {
            Directory.CreateDirectory(LocalRoot);
            File.WriteAllText(KeyPath, key.Trim());
            _keyStamp = DateTime.MinValue;
            CrashReporter.Log("[SteamGridDbService.SaveKey] API key saved");
            return null;
        }
        catch (IOException ex) { CrashReporter.Log($"[SteamGridDbService.SaveKey] {ex.Message}"); return ex.Message; }
        catch (UnauthorizedAccessException ex) { CrashReporter.Log($"[SteamGridDbService.SaveKey] {ex.Message}"); return ex.Message; }
    }

    internal static void ClearKey()
    {
        try
        {
            if (File.Exists(KeyPath)) File.Delete(KeyPath);
            _key = null;
            _keyStamp = DateTime.MinValue;
            CrashReporter.Log("[SteamGridDbService.ClearKey] API key removed");
        }
        catch (IOException ex) { CrashReporter.Log($"[SteamGridDbService.ClearKey] {ex.Message}"); }
        catch (UnauthorizedAccessException ex) { CrashReporter.Log($"[SteamGridDbService.ClearKey] {ex.Message}"); }
    }

    /// <summary>Checks a key with one small request (a search). Ok, BadKey, Offline or Error.</summary>
    internal static async Task<Status> ValidateKeyAsync(string key, CancellationToken ct)
    {
        var (status, _) = await GetJsonAsync(key, "/search/autocomplete/portal", ct).ConfigureAwait(false);
        CrashReporter.Log($"[SteamGridDbService.ValidateKeyAsync] {status}");
        return status == Status.NotFound ? Status.Ok : status;
    }

    // ── Lists ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// One page (0-based) of static covers or banners for a game: by Steam AppID when known, else (or when
    /// SteamGridDB doesn't know the AppID) by a name search. Cached for 24 h.
    /// </summary>
    internal static async Task<ArtPage> GetArtAsync(int? appId, string gameName, SteamArtworkService.Kind kind, int page, CancellationToken ct)
    {
        var key = ApiKey;
        if (key == null) return new ArtPage(Status.NoKey, Array.Empty<Art>(), false);
        string kindName = kind == SteamArtworkService.Kind.Cover ? "cover" : "hero";
        string cacheName = (appId is int a ? $"steam_{a}" : "name_" + Hash(gameName)) + $"_{kindName}_{page}.json";
        string cachePath = Path.Combine(Root, "lists", cacheName);

        if (TryReadCache(cachePath) is { } cached)
            return new ArtPage(Status.Ok, cached, cached.Count >= MinFullPage);

        string? target = appId is int id ? $"steam/{id}" : null;
        ArtPage result = new(Status.NotFound, Array.Empty<Art>(), false);
        if (target != null)
            result = await FetchListAsync(key, target, kind, page, ct).ConfigureAwait(false);
        if (result.Status == Status.NotFound && !string.IsNullOrWhiteSpace(gameName))
        {
            var (st, gameId) = await SearchGameAsync(key, gameName, ct).ConfigureAwait(false);
            if (st != Status.Ok && st != Status.NotFound) return new ArtPage(st, Array.Empty<Art>(), false);
            if (gameId is int gid)
                result = await FetchListAsync(key, $"game/{gid}", kind, page, ct).ConfigureAwait(false);
        }
        if (result.Status is Status.Ok or Status.NotFound) WriteCache(cachePath, result.Items);
        CrashReporter.Log($"[SteamGridDbService.GetArtAsync] {gameName} (AppID {appId?.ToString() ?? "none"}) {kindName} page {page}: {result.Status}, {result.Items.Count} item(s)");
        return result.Status == Status.NotFound ? result with { Status = Status.Ok } : result;
    }

    private static async Task<ArtPage> FetchListAsync(string key, string target, SteamArtworkService.Kind kind, int page, CancellationToken ct)
    {
        string path = kind == SteamArtworkService.Kind.Cover
            ? $"/grids/{target}?dimensions=600x900,342x482,660x930&types=static&nsfw=false&humor=false&epilepsy=false&page={page}"
            : $"/heroes/{target}?types=static&nsfw=false&humor=false&epilepsy=false&page={page}";
        var (status, data) = await GetJsonAsync(key, path, ct).ConfigureAwait(false);
        if (status != Status.Ok || data is not { } arr) return new ArtPage(status, Array.Empty<Art>(), false);
        var items = ParseArt(arr);
        return new ArtPage(Status.Ok, items, items.Count >= MinFullPage);
    }

    /// <summary>The best name match's SteamGridDB game id (first autocomplete result).</summary>
    private static async Task<(Status, int?)> SearchGameAsync(string key, string gameName, CancellationToken ct)
    {
        var term = Uri.EscapeDataString(gameName.Replace('/', ' ').Replace('\\', ' ').Trim());
        var (status, data) = await GetJsonAsync(key, $"/search/autocomplete/{term}", ct).ConfigureAwait(false);
        if (status != Status.Ok || data is not { } arr) return (status, null);
        foreach (var g in arr.EnumerateArray())
            if (g.ValueKind == JsonValueKind.Object && g.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out var id))
            {
                CrashReporter.Log($"[SteamGridDbService.SearchGameAsync] \"{gameName}\" → {id} ({Str(g, "name")})");
                return (Status.Ok, id);
            }
        return (Status.NotFound, null);
    }

    internal static List<Art> ParseArt(JsonElement arr)
    {
        var list = new List<Art>();
        if (arr.ValueKind != JsonValueKind.Array) return list;
        foreach (var e in arr.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object) continue;
            if (!e.TryGetProperty("id", out var idEl) || !idEl.TryGetInt32(out var id)) continue;
            var url = Str(e, "url");
            if (!IsSgdbUrl(url)) continue;
            var thumb = Str(e, "thumb");
            if (!IsSgdbUrl(thumb)) thumb = url;
            string author = e.TryGetProperty("author", out var au) && au.ValueKind == JsonValueKind.Object ? Str(au, "name") : "";
            list.Add(new Art(id, url, thumb, Int(e, "width"), Int(e, "height"), Str(e, "mime"), author, Int(e, "score")));
        }
        return list;
    }

    private static string Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static int Int(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;

    /// <summary>Only https links are followed (images come from SteamGridDB's CDN).</summary>
    private static bool IsSgdbUrl(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps;

    /// <summary>GET with the key; returns the reply's data element (cloned) on success.</summary>
    private static async Task<(Status, JsonElement?)> GetJsonAsync(string key, string path, CancellationToken ct)
    {
        try
        {
            var http = App.Services.GetRequiredService<HttpClient>();
            using var req = new HttpRequestMessage(HttpMethod.Get, ApiBase + path);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(20));
            using var resp = await http.SendAsync(req, cts.Token).ConfigureAwait(false);
            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                CrashReporter.Log($"[SteamGridDbService.GetJsonAsync] {(int)resp.StatusCode} for {path.Split('?')[0]}: key rejected");
                return (Status.BadKey, null);
            }
            if (resp.StatusCode == HttpStatusCode.NotFound) return (Status.NotFound, null);
            var body = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                CrashReporter.Log($"[SteamGridDbService.GetJsonAsync] {(int)resp.StatusCode} for {path.Split('?')[0]}");
                return (Status.Error, null);
            }
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.False)
                return (Status.NotFound, null);
            if (!root.TryGetProperty("data", out var data)) return (Status.Error, null);
            return (Status.Ok, data.Clone());
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            CrashReporter.Log($"[SteamGridDbService.GetJsonAsync] Timed out: {path.Split('?')[0]}");
            return (Status.Offline, null);
        }
        catch (OperationCanceledException) { return (Status.Error, null); }
        catch (HttpRequestException ex)
        {
            CrashReporter.Log($"[SteamGridDbService.GetJsonAsync] {path.Split('?')[0]}: {ex.Message}");
            return (Status.Offline, null);
        }
        catch (JsonException ex)
        {
            CrashReporter.Log($"[SteamGridDbService.GetJsonAsync] Bad reply for {path.Split('?')[0]}: {ex.Message}");
            return (Status.Error, null);
        }
    }

    private static List<Art>? TryReadCache(string path)
    {
        try
        {
            if (!File.Exists(path) || DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > ListMaxAge) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return ParseArt(doc.RootElement);
        }
        catch (IOException ex) { CrashReporter.Log($"[SteamGridDbService.TryReadCache] {ex.Message}"); }
        catch (UnauthorizedAccessException ex) { CrashReporter.Log($"[SteamGridDbService.TryReadCache] {ex.Message}"); }
        catch (JsonException ex) { CrashReporter.Log($"[SteamGridDbService.TryReadCache] {ex.Message}"); }
        return null;
    }

    private static void WriteCache(string path, IReadOnlyList<Art> items)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(items.Select(a => new
            {
                id = a.Id, url = a.Url, thumb = a.Thumb, width = a.Width, height = a.Height,
                mime = a.Mime, score = a.Score, author = new { name = a.Author },
            }));
            File.WriteAllText(path, json);
        }
        catch (IOException ex) { CrashReporter.Log($"[SteamGridDbService.WriteCache] {ex.Message}"); }
        catch (UnauthorizedAccessException ex) { CrashReporter.Log($"[SteamGridDbService.WriteCache] {ex.Message}"); }
    }

    // ── Images ────────────────────────────────────────────────────────────────

    private static string Ext(string url, string mime)
    {
        var e = mime switch { "image/png" => ".png", "image/jpeg" => ".jpg", "image/webp" => ".webp", _ => "" };
        if (e.Length > 0) return e;
        try
        {
            e = Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant();
        }
        catch (UriFormatException) { e = ""; }
        return e is ".png" or ".jpg" or ".jpeg" or ".webp" ? e : ".img";
    }

    /// <summary>The thumbnail on disk, downloading it if needed. Null on failure.</summary>
    internal static Task<string?> GetThumbAsync(Art art, CancellationToken ct)
    {
        var dest = Path.Combine(Root, "thumbs", $"{art.Id}{Ext(art.Thumb, "")}");
        if (File.Exists(dest)) return Task.FromResult<string?>(dest);
        return ThumbsInFlight.GetOrAdd(dest, _ => Task.Run(async () =>
        {
            try
            {
                await ThumbGate.WaitAsync(ct).ConfigureAwait(false);
                try { return await DownloadAsync(art.Thumb, dest, ct).ConfigureAwait(false) ? dest : null; }
                finally { ThumbGate.Release(); }
            }
            catch (OperationCanceledException) { return null; }
            finally { ThumbsInFlight.TryRemove(dest, out Task<string?>? _); }
        }, CancellationToken.None));
    }

    /// <summary>
    /// Downloads the full image for picking (artwork\sgdb\full\). WebP is converted to PNG, which every PC
    /// can show. Returns the file, or null with a reason.
    /// </summary>
    internal static async Task<(string? Path, string? Error)> DownloadFullAsync(Art art, CancellationToken ct)
    {
        var ext = Ext(art.Url, art.Mime);
        var dest = Path.Combine(Root, "full", $"{art.Id}{ext}");
        try
        {
            if (!File.Exists(dest) && !await DownloadAsync(art.Url, dest, ct).ConfigureAwait(false))
                return (null, "Couldn't download the image. Check your connection and try again.");
            if (ext != ".webp") return (dest, null);

            var png = Path.ChangeExtension(dest, ".png");
            if (File.Exists(png)) return (png, null);
            if (await ConvertToPngAsync(dest, png).ConfigureAwait(false)) return (png, null);
            return (null, "This image is in WebP format, which Windows can't open on this PC. Pick another one.");
        }
        catch (OperationCanceledException) { return (null, "Cancelled."); }
    }

    private static async Task<bool> ConvertToPngAsync(string src, string dest)
    {
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(src);
            using var input = await file.OpenReadAsync();
            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(input);
            using var bitmap = await decoder.GetSoftwareBitmapAsync(
                Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);
            var tmp = dest + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.ReadWrite))
            using (var output = fs.AsRandomAccessStream())
            {
                var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, output);
                encoder.SetSoftwareBitmap(bitmap);
                await encoder.FlushAsync();
            }
            File.Move(tmp, dest, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                   or System.Runtime.InteropServices.COMException)
        {
            CrashReporter.Log($"[SteamGridDbService.ConvertToPngAsync] {Path.GetFileName(src)}: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static async Task<bool> DownloadAsync(string url, string dest, CancellationToken ct)
    {
        try
        {
            // Image hosts get no key (it's only for the API)
            var http = App.Services.GetRequiredService<HttpClient>();
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                CrashReporter.Log($"[SteamGridDbService.DownloadAsync] {(int)resp.StatusCode} for {url}");
                return false;
            }
            var type = resp.Content.Headers.ContentType?.MediaType ?? "";
            if (type.Length > 0 && !type.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                && !type.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase))
            {
                CrashReporter.Log($"[SteamGridDbService.DownloadAsync] Not an image ({type}): {url}");
                return false;
            }
            var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            if (bytes.Length < 256) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            var tmp = dest + ".tmp";
            await File.WriteAllBytesAsync(tmp, bytes, ct).ConfigureAwait(false);
            File.Move(tmp, dest, overwrite: true);
            return true;
        }
        catch (HttpRequestException ex) { CrashReporter.Log($"[SteamGridDbService.DownloadAsync] {url}: {ex.Message}"); }
        catch (IOException ex) { CrashReporter.Log($"[SteamGridDbService.DownloadAsync] Can't save {dest}: {ex.Message}"); }
        catch (UnauthorizedAccessException ex) { CrashReporter.Log($"[SteamGridDbService.DownloadAsync] Can't save {dest}: {ex.Message}"); }
        return false;
    }

    // ── Which SteamGridDB image a game uses (for the picker's highlight) ─────

    /// <summary>Remembers that this game's custom art file came from this SteamGridDB image.</summary>
    internal static void RememberChoice(string gameKey, SteamArtworkService.Kind kind, int artId, string customFile)
    {
        lock (ChosenLock)
        {
            var map = ReadChosen();
            map[$"{gameKey}|{kind}"] = $"{artId}|{customFile}";
            try
            {
                Directory.CreateDirectory(Root);
                File.WriteAllText(ChosenPath, JsonSerializer.Serialize(map));
            }
            catch (IOException ex) { CrashReporter.Log($"[SteamGridDbService.RememberChoice] {ex.Message}"); }
            catch (UnauthorizedAccessException ex) { CrashReporter.Log($"[SteamGridDbService.RememberChoice] {ex.Message}"); }
        }
    }

    /// <summary>The SteamGridDB image id the game's current custom art came from, or null.</summary>
    internal static int? ChosenId(string gameKey, SteamArtworkService.Kind kind, string? currentCustom)
    {
        if (currentCustom == null) return null;
        lock (ChosenLock)
        {
            if (!ReadChosen().TryGetValue($"{gameKey}|{kind}", out var v)) return null;
            int bar = v.IndexOf('|');
            if (bar <= 0 || !int.TryParse(v[..bar], out var id)) return null;
            return string.Equals(v[(bar + 1)..], currentCustom, StringComparison.OrdinalIgnoreCase) ? id : null;
        }
    }

    private static Dictionary<string, string> ReadChosen()
    {
        try
        {
            if (File.Exists(ChosenPath))
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(ChosenPath))
                       ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (IOException ex) { CrashReporter.Log($"[SteamGridDbService.ReadChosen] {ex.Message}"); }
        catch (UnauthorizedAccessException ex) { CrashReporter.Log($"[SteamGridDbService.ReadChosen] {ex.Message}"); }
        catch (JsonException ex) { CrashReporter.Log($"[SteamGridDbService.ReadChosen] {ex.Message}"); }
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static string Hash(string s) => Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(
        System.Text.Encoding.UTF8.GetBytes(s.ToLowerInvariant())))[..12];
}
