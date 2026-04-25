using System.Text.Json;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Resolves PCGamingWiki URLs via Steam AppID (using appid.php redirect)
/// or OpenSearch fallback. Maintains a persistent AppID cache on disk.
/// </summary>
public class PcgwService : IPcgwService
{
    private readonly HttpClient _http;
    private readonly ISteamAppIdResolver _steamAppIdResolver;
    private readonly IGameDetectionService _gameDetection;

    private static readonly string CachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "steam_appid_cache.json");

    private static readonly JsonSerializerOptions s_writeOptions = new() { WriteIndented = true };

    /// <summary>Normalized game name → Steam AppID.</summary>
    private Dictionary<string, int> _appIdCache = new(StringComparer.Ordinal);

    /// <summary>Debounce timer — resets on every <see cref="SaveCacheAsync"/> call.</summary>
    private Timer? _saveDebounceTimer;

    /// <summary>Guards <see cref="_saveDebounceTimer"/> creation/reset.</summary>
    private readonly object _saveLock = new();

    /// <summary>
    /// Circuit breaker: once PCGW returns an error or times out, skip all further
    /// lookups for the rest of the session to avoid blocking card builds.
    /// </summary>
    private volatile bool _pcgwDown;

    public PcgwService(HttpClient http, ISteamAppIdResolver steamAppIdResolver, IGameDetectionService gameDetection)
    {
        _http = http;
        _steamAppIdResolver = steamAppIdResolver;
        _gameDetection = gameDetection;
    }

    /// <inheritdoc />
    public async Task LoadCacheAsync()
    {
        try
        {
            if (!File.Exists(CachePath))
            {
                CrashReporter.Log("[PcgwService.LoadCacheAsync] No cache file found — starting with empty cache");
                return;
            }

            var json = await File.ReadAllTextAsync(CachePath).ConfigureAwait(false);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
            if (loaded != null)
            {
                _appIdCache = new Dictionary<string, int>(loaded, StringComparer.Ordinal);
                CrashReporter.Log($"[PcgwService.LoadCacheAsync] Loaded {_appIdCache.Count} cached AppIDs");
            }
        }
        catch (JsonException ex)
        {
            CrashReporter.Log($"[PcgwService.LoadCacheAsync] Malformed cache JSON — {ex.Message}");
            _appIdCache = new Dictionary<string, int>(StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[PcgwService.LoadCacheAsync] Cache load failed — {ex.Message}");
            _appIdCache = new Dictionary<string, int>(StringComparer.Ordinal);
        }
    }

    /// <inheritdoc />
    public async Task<string?> ResolveUrlAsync(string gameName, int? steamAppId, string installPath, RemoteManifest? manifest)
    {
        // 1. Manifest pcgwUrlOverrides (highest priority).
        if (manifest?.PcgwUrlOverrides != null
            && manifest.PcgwUrlOverrides.TryGetValue(gameName, out var overrideUrl)
            && !string.IsNullOrEmpty(overrideUrl))
        {
            return overrideUrl;
        }

        // 2. Resolve Steam AppID via the priority chain (passing our cache).
        var appId = await _steamAppIdResolver.ResolveAsync(
            gameName, steamAppId, installPath, manifest, _appIdCache).ConfigureAwait(false);

        if (appId.HasValue)
        {
            // Persist to cache.
            var normalized = _gameDetection.NormalizeName(gameName);
            if (!string.IsNullOrEmpty(normalized))
            {
                _appIdCache[normalized] = appId.Value;
                await SaveCacheAsync().ConfigureAwait(false);
            }

            return BuildAppIdUrl(appId.Value);
        }

        // 3. OpenSearch fallback (no AppID resolved).
        return await OpenSearchFallbackAsync(gameName).ConfigureAwait(false);
    }

    /// <summary>
    /// Constructs the PCGW appid.php redirect URL for a given Steam AppID.
    /// Exposed as static for testability (Property 6).
    /// </summary>
    internal static string BuildAppIdUrl(int appId)
        => $"https://www.pcgamingwiki.com/api/appid.php?appid={appId}";

    /// <summary>
    /// Constructs a PCGW wiki page URL from a page title, replacing spaces with underscores.
    /// Exposed as static for testability (Property 6).
    /// </summary>
    internal static string BuildWikiUrl(string pageTitle)
        => $"https://www.pcgamingwiki.com/wiki/{pageTitle.Replace(' ', '_')}";

    /// <summary>
    /// Queries the PCGW OpenSearch API and returns the wiki URL for the first result,
    /// or null if no results or an error occurs.
    /// </summary>
    private async Task<string?> OpenSearchFallbackAsync(string gameName)
    {
        if (_pcgwDown) return null;

        try
        {
            var encodedName = Uri.EscapeDataString(gameName);
            var url = $"https://www.pcgamingwiki.com/w/api.php?action=opensearch&search={encodedName}&limit=5&format=json";

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var response = await _http.GetAsync(url, cts.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                CrashReporter.Log($"[PcgwService.OpenSearchFallback] OpenSearch returned {(int)response.StatusCode} — disabling PCGW for this session");
                _pcgwDown = true;
                return null;
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            // OpenSearch returns: ["search term", ["Title1", "Title2"], ["Desc1", "Desc2"], ["URL1", "URL2"]]
            JsonElement[]? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<JsonElement[]>(json);
            }
            catch (JsonException ex)
            {
                CrashReporter.Log($"[PcgwService.OpenSearchFallback] Malformed JSON — {ex.Message}");
                return null;
            }

            if (parsed == null || parsed.Length < 2)
                return null;

            var titles = parsed[1];
            if (titles.ValueKind != JsonValueKind.Array || titles.GetArrayLength() == 0)
                return null;

            var firstTitle = titles[0].GetString();
            if (string.IsNullOrEmpty(firstTitle))
                return null;

            return BuildWikiUrl(firstTitle);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[PcgwService.OpenSearchFallback] Failed — {ex.Message} — disabling PCGW for this session");
            _pcgwDown = true;
            return null;
        }
    }

    /// <summary>
    /// Schedules a debounced cache write. Resets a 500 ms timer on each call;
    /// the actual disk write happens only once the timer fires (i.e. 500 ms after
    /// the last call). This avoids ~45 concurrent writes during startup.
    /// </summary>
    private Task SaveCacheAsync()
    {
        lock (_saveLock)
        {
            if (_saveDebounceTimer != null)
            {
                _saveDebounceTimer.Change(500, Timeout.Infinite);
            }
            else
            {
                _saveDebounceTimer = new Timer(_ => WriteCacheToDisk(), null, 500, Timeout.Infinite);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task FlushCacheAsync()
    {
        Timer? timer;
        lock (_saveLock)
        {
            timer = _saveDebounceTimer;
            _saveDebounceTimer = null;
        }

        if (timer != null)
        {
            timer.Dispose();
            WriteCacheToDisk();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Performs the actual disk write with retry logic via <see cref="FileHelper"/>.
    /// </summary>
    private void WriteCacheToDisk()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            var json = JsonSerializer.Serialize(_appIdCache, s_writeOptions);
            FileHelper.WriteAllTextWithRetry(CachePath, json, "PcgwService.SaveCache");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[PcgwService.SaveCacheAsync] Cache write failed — {ex.Message}");
        }
    }
}
