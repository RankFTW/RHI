// SteamArtworkService.cs -- Steam artwork for the Spotlight layout (hero banner, cover tiles).
// Any game that's also on Steam can use it, whatever store it's owned on (the AppID comes from
// DetailPanelBuilder.ResolveCommunityAppId: manifest override, Steam library, PCGamingWiki).
// Images are downloaded once into %LocalAppData%\RHI\artwork\{appid}\ and only while Spotlight is
// in use. A miss is remembered for a week so missing art isn't requested on every launch.
// Plan and URL notes: docs/layouts-plan-2.9.0.md, "Game artwork".

using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace RenoDXCommander.Services;

internal static class SteamArtworkService
{
    internal enum Kind { Hero, Cover }

    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "artwork");

    private static readonly TimeSpan MissRetry = TimeSpan.FromDays(7);
    private static readonly SemaphoreSlim Gate = new(4); // parallel downloads
    private static readonly ConcurrentDictionary<string, Task<string?>> InFlight = new(StringComparer.Ordinal);

    // Store API lookups: one per AppID per session, and only after a CDN miss
    private static readonly ConcurrentDictionary<int, Task<Dictionary<string, string>?>> AssetLookups = new();

    private static string FileName(Kind kind) => kind == Kind.Hero ? "hero.jpg" : "cover.jpg";

    private static string LocalPath(int appId, Kind kind) => Path.Combine(Root, appId.ToString(), FileName(kind));

    private static string MissPath(int appId, Kind kind) => LocalPath(appId, kind) + ".miss";

    /// <summary>The cached file, or null when it isn't downloaded yet. Cheap (file checks only).</summary>
    internal static string? TryGetCached(int appId, Kind kind)
    {
        var path = LocalPath(appId, kind);
        return File.Exists(path) ? path : null;
    }

    /// <summary>The local file for this artwork, downloading it if needed. Null when Steam has none.</summary>
    internal static Task<string?> GetAsync(int appId, Kind kind, CancellationToken ct)
    {
        if (TryGetCached(appId, kind) is { } cached) return Task.FromResult<string?>(cached);
        var key = $"{appId}:{kind}";
        return InFlight.GetOrAdd(key, _ => Task.Run(async () =>
        {
            try { return await DownloadAsync(appId, kind, ct).ConfigureAwait(false); }
            finally { InFlight.TryRemove(key, out Task<string?>? _); }
        }, CancellationToken.None));
    }

    private static async Task<string?> DownloadAsync(int appId, Kind kind, CancellationToken ct)
    {
        var miss = MissPath(appId, kind);
        try
        {
            if (File.Exists(miss) && DateTime.UtcNow - File.GetLastWriteTimeUtc(miss) < MissRetry) return null;
        }
        catch (IOException ex) { CrashReporter.Log($"[SteamArtworkService.DownloadAsync] {appId}: {ex.Message}"); }

        try { await Gate.WaitAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { return null; }
        try
        {
            var http = App.Services.GetRequiredService<HttpClient>();
            string asset = kind == Kind.Hero ? "library_hero.jpg" : "library_600x900.jpg";
            var urls = new List<string>
            {
                $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/{asset}",
                $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appId}/{asset}",
            };
            foreach (var url in urls)
                if (await TryDownloadAsync(http, url, LocalPath(appId, kind), ct).ConfigureAwait(false))
                    return LocalPath(appId, kind);

            // Newer games use hashed asset paths, which only the store API knows. The wide header
            // image is the hero's last resort; covers without one show the game's name instead.
            var assets = await AssetLookups.GetOrAdd(appId, id => LookupAssetsAsync(http, id, ct)).ConfigureAwait(false);
            var names = kind == Kind.Hero ? new[] { "library_hero", "header" } : new[] { "library_capsule" };
            if (assets != null)
                foreach (var name in names)
                    if (assets.TryGetValue(name, out var url)
                        && await TryDownloadAsync(http, url, LocalPath(appId, kind), ct).ConfigureAwait(false))
                        return LocalPath(appId, kind);

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(miss)!);
                File.WriteAllText(miss, DateTime.UtcNow.ToString("O"));
            }
            catch (IOException ex) { CrashReporter.Log($"[SteamArtworkService.DownloadAsync] Can't record miss for {appId} — {ex.Message}"); }
            catch (UnauthorizedAccessException ex) { CrashReporter.Log($"[SteamArtworkService.DownloadAsync] Can't record miss for {appId} — {ex.Message}"); }
            return null;
        }
        catch (OperationCanceledException) { return null; }
        catch (HttpRequestException ex)
        {
            CrashReporter.Log($"[SteamArtworkService.DownloadAsync] {appId} {kind}: {ex.Message}");
            return null;
        }
        finally { Gate.Release(); }
    }

    private static async Task<bool> TryDownloadAsync(HttpClient http, string url, string dest, CancellationToken ct)
    {
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return false;
        var type = resp.Content.Headers.ContentType?.MediaType ?? "";
        if (!type.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return false;
        var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        if (bytes.Length < 1024) return false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            var tmp = dest + ".tmp";
            await File.WriteAllBytesAsync(tmp, bytes, ct).ConfigureAwait(false);
            File.Move(tmp, dest, overwrite: true);
            return true;
        }
        catch (IOException ex)
        {
            CrashReporter.Log($"[SteamArtworkService.TryDownloadAsync] Can't save {dest} — {ex.Message}");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            CrashReporter.Log($"[SteamArtworkService.TryDownloadAsync] Can't save {dest} — {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// The game's artwork URLs from Steam's IStoreBrowseService (no key needed): asset name
    /// (library_capsule, library_hero, header) to full URL. Null when the lookup fails.
    /// </summary>
    private static async Task<Dictionary<string, string>?> LookupAssetsAsync(HttpClient http, int appId, CancellationToken ct)
    {
        try
        {
            var input = $"{{\"ids\":[{{\"appid\":{appId}}}],\"context\":{{\"language\":\"english\",\"country_code\":\"US\"}},\"data_request\":{{\"include_assets\":true}}}}";
            var json = await http.GetStringAsync(
                "https://api.steampowered.com/IStoreBrowseService/GetItems/v1?input_json=" + Uri.EscapeDataString(input), ct)
                .ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("response", out var resp)
                || !resp.TryGetProperty("store_items", out var items) || items.GetArrayLength() == 0
                || !items[0].TryGetProperty("assets", out var assets)
                || !assets.TryGetProperty("asset_url_format", out var fmt) || fmt.GetString() is not { Length: > 0 } format)
                return null;
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var name in new[] { "library_capsule", "library_hero", "header" })
                if (assets.TryGetProperty(name, out var file) && file.GetString() is { Length: > 0 } f)
                    result[name] = "https://shared.akamai.steamstatic.com/store_item_assets/" + format.Replace("${FILENAME}", f);
            return result;
        }
        catch (HttpRequestException ex) { CrashReporter.Log($"[SteamArtworkService.LookupAssetsAsync] {appId}: {ex.Message}"); }
        catch (JsonException ex) { CrashReporter.Log($"[SteamArtworkService.LookupAssetsAsync] {appId}: {ex.Message}"); }
        catch (OperationCanceledException) { }
        return null;
    }
}
