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

    private static readonly string UrlCachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "pcgw_url_cache.json");

    /// <summary>Marker file — if absent on first 2.4.2 launch, wipes the stale URL cache built with broken appid.php URLs.</summary>
    private static readonly string UrlCacheMarkerPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "pcgw_cache_v2.txt");

    private static readonly JsonSerializerOptions s_writeOptions = new() { WriteIndented = true };

    /// <summary>Normalized game name → Steam AppID.</summary>
    private Dictionary<string, int> _appIdCache = new(StringComparer.Ordinal);

    /// <summary>Normalized game name → resolved PCGW wiki URL.</summary>
    private System.Collections.Concurrent.ConcurrentDictionary<string, string> _urlCache = new(StringComparer.Ordinal);

    /// <summary>Debounce timer — resets on every <see cref="SaveCacheAsync"/> call.</summary>
    private Timer? _saveDebounceTimer;

    /// <summary>Guards <see cref="_saveDebounceTimer"/> creation/reset.</summary>
    private readonly object _saveLock = new();

    /// <summary>
    /// Circuit breaker: once PCGW returns an error or times out, skip all further
    /// lookups for the rest of the session to avoid blocking card builds.
    /// </summary>
    private volatile bool _pcgwDown;

    /// <summary>
    /// Shared cancellation source — cancelled when the circuit breaker trips so
    /// all in-flight PCGW requests abort immediately instead of each waiting
    /// their own 5-second timeout.
    /// </summary>
    private readonly CancellationTokenSource _pcgwCts = new();

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

        // Versioned migration: wipe URL cache if it was built with an older resolution method.
        // Version 1 = OpenSearch (appid.php was broken). Version 2 = back to appid.php (when restored).
        // The required version can also be driven remotely via manifest.PcgwUrlCacheVersion.
        const int UrlCacheVersion = 1;
        int existingVersion = 0;
        if (File.Exists(UrlCacheMarkerPath))
            int.TryParse(File.ReadAllText(UrlCacheMarkerPath).Trim(), out existingVersion);

        if (existingVersion < UrlCacheVersion)
        {
            try
            {
                if (File.Exists(UrlCachePath)) File.Delete(UrlCachePath);
                File.WriteAllText(UrlCacheMarkerPath, UrlCacheVersion.ToString());
                CrashReporter.Log($"[PcgwService.LoadCacheAsync] URL cache wiped (version {existingVersion} → {UrlCacheVersion})");
            }
            catch { /* non-critical */ }
        }

        // Load URL cache (wiki URLs resolved via OpenSearch)
        try
        {
            if (File.Exists(UrlCachePath))
            {
                var urlJson = await File.ReadAllTextAsync(UrlCachePath).ConfigureAwait(false);
                var loadedUrls = JsonSerializer.Deserialize<Dictionary<string, string>>(urlJson);
                if (loadedUrls != null)
                    _urlCache = new System.Collections.Concurrent.ConcurrentDictionary<string, string>(loadedUrls, StringComparer.Ordinal);
            }
        }
        catch { /* non-critical — start with empty URL cache */ }
    }

    public async Task<string?> ResolveUrlAsync(string gameName, int? steamAppId, string installPath, RemoteManifest? manifest)
    {
        // 1. Manifest pcgwUrlOverrides (highest priority).
        if (manifest?.PcgwUrlOverrides != null
            && manifest.PcgwUrlOverrides.TryGetValue(gameName, out var overrideUrl)
            && !string.IsNullOrEmpty(overrideUrl))
        {
            return overrideUrl;
        }

        var normalized = _gameDetection.NormalizeName(gameName);

        // 2. Cached wiki URL — avoids HTTP calls every session.
        if (!string.IsNullOrEmpty(normalized) && _urlCache.TryGetValue(normalized, out var cachedUrl))
            return cachedUrl;

        // 3. Check for cached negative result — avoids HTTP calls for non-PCGW games.
        if (!string.IsNullOrEmpty(normalized) && _appIdCache.TryGetValue(normalized, out var cachedId) && cachedId == -1)
            return null;

        // 4. Resolve Steam AppID via the priority chain (passing our cache).
        var appId = await _steamAppIdResolver.ResolveAsync(
            gameName, steamAppId, installPath, manifest, _appIdCache).ConfigureAwait(false);

        if (appId.HasValue)
        {
            if (!string.IsNullOrEmpty(normalized))
            {
                _appIdCache[normalized] = appId.Value;
                await SaveCacheAsync().ConfigureAwait(false);
            }

            // Use appid.php when the manifest flag is on (endpoint restored), otherwise OpenSearch.
            if (manifest?.PcgwUseAppId == true)
                return BuildAppIdUrl(appId.Value);

            // appid.php currently unreliable — use OpenSearch for the actual wiki URL.
            var wikiUrl = await OpenSearchFallbackAsync(gameName).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(normalized) && wikiUrl != null)
            {
                _urlCache[normalized] = wikiUrl;
                SaveUrlCacheToDisk();
            }

            return wikiUrl;
        }

        // 5. OpenSearch fallback (no AppID resolved).
        var result = await OpenSearchFallbackAsync(gameName).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(normalized))
        {
            if (result != null)
            {
                _urlCache[normalized] = result;
                SaveUrlCacheToDisk();
            }
            else
            {
                // Cache negative result so we don't retry HTTP calls next session.
                _appIdCache[normalized] = -1;
                await SaveCacheAsync().ConfigureAwait(false);
            }
        }

        return result;
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

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_pcgwCts.Token);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            var response = await _http.GetAsync(url, cts.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                CrashReporter.Log($"[PcgwService.OpenSearchFallback] OpenSearch returned {(int)response.StatusCode} for '{gameName}'");
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
            try { _pcgwCts.Cancel(); } catch { }
            return null;
        }
    }

    /// <inheritdoc />
    public void ClearNegativeCache()
    {
        var negativeKeys = _appIdCache.Where(kv => kv.Value == -1).Select(kv => kv.Key).ToList();
        foreach (var key in negativeKeys)
            _appIdCache.Remove(key);
        if (negativeKeys.Count > 0)
        {
            WriteCacheToDisk();
            CrashReporter.Log($"[PcgwService.ClearNegativeCache] Cleared {negativeKeys.Count} negative sentinel(s)");
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

    private Timer? _urlSaveDebounceTimer;

    private void SaveUrlCacheToDisk()
    {
        lock (_saveLock)
        {
            if (_urlSaveDebounceTimer != null)
                _urlSaveDebounceTimer.Change(500, Timeout.Infinite);
            else
                _urlSaveDebounceTimer = new Timer(_ => WriteUrlCacheToDisk(), null, 500, Timeout.Infinite);
        }
    }

    /// <summary>
    /// Called after the manifest is fetched. If the manifest requests a higher cache version
    /// than what's stored locally, wipes the URL cache so links re-resolve on next BuildCards.
    /// This allows a remote manifest change to force a clean re-resolve without a new app build.
    /// </summary>
    public void CheckManifestCacheVersion(RemoteManifest? manifest)
    {
        if (manifest == null || manifest.PcgwUrlCacheVersion <= 0) return;

        int existingVersion = 0;
        if (File.Exists(UrlCacheMarkerPath))
            int.TryParse(File.ReadAllText(UrlCacheMarkerPath).Trim(), out existingVersion);

        if (manifest.PcgwUrlCacheVersion > existingVersion)
        {
            try
            {
                _urlCache.Clear();
                if (File.Exists(UrlCachePath)) File.Delete(UrlCachePath);
                File.WriteAllText(UrlCacheMarkerPath, manifest.PcgwUrlCacheVersion.ToString());
                CrashReporter.Log($"[PcgwService] URL cache wiped via manifest (version {existingVersion} → {manifest.PcgwUrlCacheVersion})");
            }
            catch { /* non-critical */ }
        }
    }

    private void WriteUrlCacheToDisk()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(UrlCachePath)!);
            var snapshot = new Dictionary<string, string>(_urlCache, StringComparer.Ordinal);
            var json = JsonSerializer.Serialize(snapshot, s_writeOptions);
            FileHelper.WriteAllTextWithRetry(UrlCachePath, json, "PcgwService.SaveUrlCache");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[PcgwService.SaveUrlCache] Write failed — {ex.Message}");
        }
    }

    // ── PCGW API info scraping ────────────────────────────────────────────────

    private static readonly string ApiCachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "pcgw_api_cache.json");

    /// <summary>Normalized game name → scraped API info. Loaded/saved to pcgw_api_cache.json.</summary>
    private Dictionary<string, PcgwApiInfo> _apiInfoCache = new(StringComparer.Ordinal);

    /// <summary>
    /// Returns the cached API info for a game, or null if not yet scraped.
    /// </summary>
    public PcgwApiInfo? GetCachedApiInfo(string gameName)
    {
        var normalized = _gameDetection.NormalizeName(gameName);
        return _apiInfoCache.TryGetValue(normalized, out var info) ? info : null;
    }

    /// <summary>
    /// Loads the API info cache from disk. Called alongside LoadCacheAsync.
    /// </summary>
    public async Task LoadApiCacheAsync()
    {
        try
        {
            if (!File.Exists(ApiCachePath)) return;
            var json = await File.ReadAllTextAsync(ApiCachePath).ConfigureAwait(false);
            var loaded = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, PcgwApiInfo>>(json);
            if (loaded != null)
                _apiInfoCache = new Dictionary<string, PcgwApiInfo>(loaded, StringComparer.Ordinal);
            CrashReporter.Log($"[PcgwService.LoadApiCacheAsync] Loaded {_apiInfoCache.Count} cached API entries");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[PcgwService.LoadApiCacheAsync] Failed — {ex.Message}");
        }
    }

    /// <summary>
    /// Fetches and scrapes the PCGW wiki page for a game to extract its graphics API support.
    /// Only called when the wiki URL is already resolved (no new URL lookup triggered).
    /// Caches result in memory + disk. Returns null if the page can't be fetched or parsed.
    /// </summary>
    public async Task<PcgwApiInfo?> FetchApiInfoAsync(string gameName, string wikiUrl)
    {
        if (_pcgwDown) return null;

        var normalized = _gameDetection.NormalizeName(gameName);

        // Return cached result if we already have it
        if (_apiInfoCache.TryGetValue(normalized, out var cached))
            return cached;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_pcgwCts.Token);
            cts.CancelAfter(TimeSpan.FromSeconds(8));

            var response = await _http.GetAsync(wikiUrl, cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                CrashReporter.Log($"[PcgwService.FetchApiInfoAsync] HTTP {(int)response.StatusCode} for '{gameName}'");
                return null;
            }

            var html = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            var info = ParseApiSection(html);

            if (info != null)
            {
                _apiInfoCache[normalized] = info;
                SaveApiCacheToDisk();
                CrashReporter.Log($"[PcgwService.FetchApiInfoAsync] '{gameName}': " +
                    $"DX9={info.HasDirectX9} DX10={info.HasDirectX10} DX11={info.HasDirectX11} " +
                    $"DX12={info.HasDirectX12} Vulkan={info.HasVulkan} OGL={info.HasOpenGL}");
            }

            return info;
        }
        catch (OperationCanceledException)
        {
            CrashReporter.Log($"[PcgwService.FetchApiInfoAsync] Timeout for '{gameName}'");
            return null;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[PcgwService.FetchApiInfoAsync] Failed for '{gameName}' — {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Parses the "API / Technical specs" section from PCGW page HTML.
    /// Looks for the text block containing "Direct3D", "Vulkan", "OpenGL" etc.
    /// The section renders as a plain-text table in the HTML body.
    /// </summary>
    private static PcgwApiInfo? ParseApiSection(string html)
    {
        try
        {
            // PCGW renders the API section as plain text in a table. The pattern is:
            // "API\nTechnical specs\nSupported\nNotes\nDirect3D\n9\n..." etc.
            // We look for "Direct3D" in the text and grab version numbers after it.
            // The section ends when we hit the next major section header (Executable, Middleware, etc.)
            var info = new PcgwApiInfo();

            // Strip tags for plain-text analysis
            var plain = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", "\n");
            // Collapse whitespace
            plain = System.Text.RegularExpressions.Regex.Replace(plain, @"\s+", " ");

            // Find the API section — look for "Direct3D" or "Vulkan" or "OpenGL" near "Technical specs"
            // The section reliably contains "Technical specs" followed by API names + version numbers
            int apiIdx = plain.IndexOf("Technical specs", StringComparison.OrdinalIgnoreCase);
            if (apiIdx < 0) return null;

            // Take a window of text after "Technical specs" up to the next major section
            int windowEnd = plain.IndexOf("Executable", apiIdx, StringComparison.OrdinalIgnoreCase);
            if (windowEnd < 0) windowEnd = Math.Min(apiIdx + 1500, plain.Length);
            var window = plain.Substring(apiIdx, windowEnd - apiIdx);

            // Direct3D versions — look for "Direct3D" then digits after it
            var d3dMatch = System.Text.RegularExpressions.Regex.Match(
                window, @"Direct3D\s*([\d\s,/]+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (d3dMatch.Success)
            {
                var versions = d3dMatch.Groups[1].Value;
                if (System.Text.RegularExpressions.Regex.IsMatch(versions, @"\b9\b"))  info.HasDirectX9  = true;
                if (System.Text.RegularExpressions.Regex.IsMatch(versions, @"\b10\b")) info.HasDirectX10 = true;
                if (System.Text.RegularExpressions.Regex.IsMatch(versions, @"\b11\b")) info.HasDirectX11 = true;
                if (System.Text.RegularExpressions.Regex.IsMatch(versions, @"\b12\b")) info.HasDirectX12 = true;
            }

            // Vulkan
            if (System.Text.RegularExpressions.Regex.IsMatch(window, @"\bVulkan\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                info.HasVulkan = true;

            // OpenGL
            if (System.Text.RegularExpressions.Regex.IsMatch(window, @"\bOpenGL\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                info.HasOpenGL = true;

            // Metal
            if (System.Text.RegularExpressions.Regex.IsMatch(window, @"\bMetal\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                info.HasMetal = true;

            // Only return if we found at least one API — avoids caching empty results for parse failures
            bool anyFound = info.HasDirectX9 || info.HasDirectX10 || info.HasDirectX11 ||
                            info.HasDirectX12 || info.HasVulkan || info.HasOpenGL || info.HasMetal;
            return anyFound ? info : null;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[PcgwService.ParseApiSection] Parse failed — {ex.Message}");
            return null;
        }
    }

    private void SaveApiCacheToDisk()
    {
        try
        {
            var dir = Path.GetDirectoryName(ApiCachePath)!;
            Directory.CreateDirectory(dir);
            var json = System.Text.Json.JsonSerializer.Serialize(_apiInfoCache,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
            FileHelper.WriteAllTextWithRetry(ApiCachePath, json, "PcgwService.SaveApiCache");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[PcgwService.SaveApiCache] Write failed — {ex.Message}");
        }
    }
}
