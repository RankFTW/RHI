using System.Text.RegularExpressions;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Fetches the Ultra+ game list from theultraplace.com/games/, caches to disk
/// with a 24-hour TTL, builds a normalized-name lookup dictionary, and resolves
/// per-game Ultra+ page URLs.
/// </summary>
public partial class UltraPlusService : IUltraPlusService
{
    private readonly HttpClient _http;
    private readonly IGameDetectionService _gameDetection;

    private const string GamesPageUrl = "https://theultraplace.com/games/";

    private static readonly string CachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "ultraplus_games.html");

    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    /// <summary>Normalized game name → Ultra+ page URL.</summary>
    private Dictionary<string, string> _lookup = new(StringComparer.Ordinal);

    // Matches nav links like: [Game Name](https://theultraplace.com/games/slug/)
    [GeneratedRegex(@"\[([^\]]+)\]\((https://theultraplace\.com/games/[^)]+)\)", RegexOptions.IgnoreCase)]
    private static partial Regex GameLinkRegex();

    public UltraPlusService(HttpClient http, IGameDetectionService gameDetection)
    {
        _http = http;
        _gameDetection = gameDetection;
    }

    /// <inheritdoc />
    public async Task InitAsync()
    {
        string? html = null;

        // Check cache freshness
        bool cacheFresh = false;
        try
        {
            if (File.Exists(CachePath))
            {
                var lastWrite = File.GetLastWriteTimeUtc(CachePath);
                cacheFresh = (DateTime.UtcNow - lastWrite) < CacheTtl;
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[UltraPlusService.InitAsync] Cache freshness check failed — {ex.Message}");
        }

        if (cacheFresh)
        {
            try
            {
                html = File.ReadAllText(CachePath);
                CrashReporter.Log("[UltraPlusService.InitAsync] Cache is fresh, skipping fetch");
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[UltraPlusService.InitAsync] Cache read failed — {ex.Message}");
            }
        }

        // Fetch from network if cache was not fresh or unreadable
        if (html == null)
        {
            try
            {
                html = await _http.GetStringAsync(GamesPageUrl).ConfigureAwait(false);
                CrashReporter.Log("[UltraPlusService.InitAsync] Fetched games page from theultraplace.com");

                // Persist to cache
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
                    await File.WriteAllTextAsync(CachePath, html).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[UltraPlusService.InitAsync] Cache write failed — {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[UltraPlusService.InitAsync] Fetch failed — {ex.Message}");

                // Fallback to stale cache
                try
                {
                    if (File.Exists(CachePath))
                        html = File.ReadAllText(CachePath);
                }
                catch { }

                if (html == null)
                {
                    CrashReporter.Log("[UltraPlusService.InitAsync] No cache available — Ultra+ resolution disabled");
                    return;
                }
            }
        }

        // Parse game links from the HTML
        try
        {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            var regex = GameLinkRegex();

            // Track seen URLs to avoid duplicates (the page has game cards + nav links)
            var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Match match in regex.Matches(html))
            {
                var gameName = match.Groups[1].Value.Trim();
                var url = match.Groups[2].Value.Trim();

                if (string.IsNullOrEmpty(gameName) || string.IsNullOrEmpty(url)) continue;
                if (seenUrls.Contains(url)) continue;
                seenUrls.Add(url);

                var key = _gameDetection.NormalizeName(gameName);
                if (!string.IsNullOrEmpty(key))
                    dict.TryAdd(key, url);
            }

            _lookup = dict;
            CrashReporter.Log($"[UltraPlusService.InitAsync] Built dictionary with {dict.Count} entries");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[UltraPlusService.InitAsync] Parse failed — {ex.Message}");
        }
    }

    /// <inheritdoc />
    public string? ResolveUrl(string gameName, RemoteManifest? manifest)
    {
        try
        {
            var normalized = _gameDetection.NormalizeName(gameName);
            if (string.IsNullOrEmpty(normalized)) return null;

            // 1. Manifest override (highest priority)
            if (manifest?.UltraPlusUrlOverrides != null
                && manifest.UltraPlusUrlOverrides.TryGetValue(gameName, out var overrideUrl))
                return overrideUrl;

            // 2. Dictionary lookup
            if (_lookup.TryGetValue(normalized, out var url))
                return url;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[UltraPlusService.ResolveUrl] Failed for '{gameName}' — {ex.Message}");
        }

        return null;
    }
}
