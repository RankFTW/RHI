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

    /// <summary>
    /// UTC timestamps for when each negative sentinel (-1) was recorded.
    /// Used by ClearNegativeCache to only evict stale entries (>30 days old).
    /// Not persisted — sentinels recorded before app upgrade are treated as old (cleared on next Full Refresh).
    /// </summary>
    private readonly Dictionary<string, DateTime> _negativeCacheTimestamps = new(StringComparer.Ordinal);

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
                _negativeCacheTimestamps[normalized] = DateTime.UtcNow;
                await SaveCacheAsync().ConfigureAwait(false);
            }
        }

        return result;
    }

    /// <summary>
    /// Synchronous cache-only URL resolution. Returns the cached URL if available,
    /// or null if a network lookup would be required. Use this inside Parallel.ForEach
    /// to avoid thread pool starvation from blocking async calls.
    /// </summary>
    public string? TryResolveUrlFromCache(string gameName, RemoteManifest? manifest)
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

        // 3. Check for cached negative result — game is known to have no PCGW page.
        if (!string.IsNullOrEmpty(normalized) && _appIdCache.TryGetValue(normalized, out var cachedId) && cachedId == -1)
            return null;

        // Return null to indicate a network lookup is needed.
        // Caller should schedule ResolveUrlAsync for post-loop resolution.
        return null;
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
    /// Rate-limits OpenSearch calls to one every 500ms — shared across all callers.
    /// Prevents back-to-back requests that hammer PCGW when many games miss the cache.
    /// </summary>
    private static readonly SemaphoreSlim _openSearchLimiter = new(1, 1);

    /// <summary>
    /// Queries the PCGW OpenSearch API and returns the wiki URL for the first result,
    /// or null if no results or an error occurs.
    /// Rate-limited to one request per 500ms.
    /// </summary>
    private async Task<string?> OpenSearchFallbackAsync(string gameName)
    {
        if (_pcgwDown) return null;

        await _openSearchLimiter.WaitAsync().ConfigureAwait(false);
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
        finally
        {
            // Hold the rate limiter for 500ms after each request before releasing.
            // This enforces a minimum 500ms gap between OpenSearch calls across all callers.
            await Task.Delay(500).ConfigureAwait(false);
            _openSearchLimiter.Release();
        }
    }

    /// <inheritdoc />
    public void ClearNegativeCache()
    {
        // Only clear negative sentinels that are older than 30 days.
        // Recent misses (games confirmed not on PCGW) are kept so they don't
        // hammer PCGW again immediately after a Full Refresh.
        var cutoff = DateTime.UtcNow.AddDays(-30);
        var toRemove = _appIdCache
            .Where(kv => kv.Value == -1)
            .Where(kv => !_negativeCacheTimestamps.TryGetValue(kv.Key, out var ts) || ts < cutoff)
            .Select(kv => kv.Key)
            .ToList();
        foreach (var key in toRemove)
        {
            _appIdCache.Remove(key);
            _negativeCacheTimestamps.Remove(key);
        }
        if (toRemove.Count > 0)
        {
            WriteCacheToDisk();
            CrashReporter.Log($"[PcgwService.ClearNegativeCache] Cleared {toRemove.Count} stale negative sentinel(s) (>30 days old)");
        }
        else
        {
            CrashReporter.Log("[PcgwService.ClearNegativeCache] No stale negative sentinels to clear (all recent)");
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

    /// <summary>Bump when ParseApiSection or ParseConfigFilesSection logic changes to force a full rescrape.</summary>
    private const int ApiCacheVersion = 15;
    private static readonly string ApiCacheVersionPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "pcgw_api_cache_v.txt");

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
            // Wipe if parser version changed
            int storedVer = 0;
            if (File.Exists(ApiCacheVersionPath)) int.TryParse(File.ReadAllText(ApiCacheVersionPath).Trim(), out storedVer);
            if (storedVer < ApiCacheVersion)
            {
                if (File.Exists(ApiCachePath)) File.Delete(ApiCachePath);
                File.WriteAllText(ApiCacheVersionPath, ApiCacheVersion.ToString());
                CrashReporter.Log($"[PcgwService.LoadApiCacheAsync] API cache wiped (v{storedVer}→v{ApiCacheVersion})");
                return;
            }

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
                if ((int)response.StatusCode == 429)
                {
                    CrashReporter.Log($"[PcgwService.FetchApiInfoAsync] Rate limited — flushing {_apiInfoCache.Count} entries");
                    _pcgwDown = true;
                    SaveApiCacheToDisk();
                }
                else
                    CrashReporter.Log($"[PcgwService.FetchApiInfoAsync] HTTP {(int)response.StatusCode} for '{gameName}'");
                return null;
            }

            var html = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            var info = ParseApiSection(html);

            // Always attempt config path parsing — even when API section wasn't found
            var (configPath, configPathXbox) = ParseConfigFilesSection(html);
            if (configPath != null || configPathXbox != null)
            {
                info ??= new PcgwApiInfo();
                info.ConfigPath     = configPath;
                info.ConfigPathXbox = configPathXbox;
            }

            if (info != null)
            {
                _apiInfoCache[normalized] = info;
                SaveApiCacheToDisk();
                CrashReporter.Log($"[PcgwService.FetchApiInfoAsync] '{gameName}': " +
                    $"DX9={info.HasDirectX9} DX10={info.HasDirectX10} DX11={info.HasDirectX11} " +
                    $"DX12={info.HasDirectX12} Vulkan={info.HasVulkan} OGL={info.HasOpenGL}" +
                    (info.ConfigPath != null ? $" ConfigPath='{info.ConfigPath}'" : "") +
                    (info.ConfigPathXbox != null ? $" ConfigPathXbox='{info.ConfigPathXbox}'" : ""));
            }
            else
            {
                // Cache negative so we don't re-scrape this game next session
                _apiInfoCache[normalized] = new PcgwApiInfo();
                SaveApiCacheToDisk();
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
            var info = new PcgwApiInfo();

            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(html);

            // Find the API table by its id or by header text
            var apiTable = doc.DocumentNode.SelectSingleNode("//table[@id='table-api']");
            if (apiTable == null)
            {
                var tables = doc.DocumentNode.SelectNodes("//table");
                if (tables != null)
                    foreach (var t in tables)
                        if (t.InnerText.IndexOf("Technical specs", StringComparison.OrdinalIgnoreCase) >= 0)
                        { apiTable = t; break; }
            }

            if (apiTable != null)
            {
                // Each data row: <th scope="row"><abbr title="platform">APIName</abbr></th>
                //                <td>version</td> <td>notes</td>
                // abbr title contains "Windows" if the API is available on Windows
                var rows = apiTable.SelectNodes(".//tr[th[@scope='row']]");
                if (rows != null)
                {
                    foreach (var row in rows)
                    {
                        var th  = row.SelectSingleNode("th[@scope='row']");
                        var tds = row.SelectNodes("td");
                        if (th == null || tds == null || tds.Count < 1) continue;

                        // If abbr title exists and doesn't mention Windows, skip this row
                        var abbr = th.SelectSingleNode("abbr");
                        if (abbr != null)
                        {
                            var title = abbr.GetAttributeValue("title", "");
                            if (!string.IsNullOrEmpty(title) &&
                                title.IndexOf("Windows", StringComparison.OrdinalIgnoreCase) < 0)
                                continue;
                        }

                        var apiName = HtmlAgilityPack.HtmlEntity.DeEntitize(th.InnerText).Trim();
                        var version = HtmlAgilityPack.HtmlEntity.DeEntitize(tds[0].InnerText).Trim();

                        bool isDirect3D = apiName.StartsWith("Direct3D", StringComparison.OrdinalIgnoreCase)
                                       || apiName.StartsWith("DirectX",  StringComparison.OrdinalIgnoreCase);
                        bool isVulkanRow = apiName.IndexOf("Vulkan", StringComparison.OrdinalIgnoreCase) >= 0;
                        bool isOpenGLRow = apiName.IndexOf("OpenGL", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!isDirect3D && !isVulkanRow && !isOpenGLRow) continue;

                        if (isVulkanRow) { info.HasVulkan = true; continue; }
                        if (isOpenGLRow) { info.HasOpenGL = true; continue; }

                        if (System.Text.RegularExpressions.Regex.IsMatch(version, @"\b9\b"))  info.HasDirectX9  = true;
                        if (System.Text.RegularExpressions.Regex.IsMatch(version, @"\b10\b")) info.HasDirectX10 = true;
                        if (System.Text.RegularExpressions.Regex.IsMatch(version, @"\b11\b")) info.HasDirectX11 = true;
                        if (System.Text.RegularExpressions.Regex.IsMatch(version, @"\b12\b")) info.HasDirectX12 = true;
                    }
                }
            }
            else
            {
                // Plain-text fallback
                var plain = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", "\n");
                plain = System.Text.RegularExpressions.Regex.Replace(plain, @"\s+", " ");
                int apiIdx = plain.IndexOf("Technical specs", StringComparison.OrdinalIgnoreCase);
                if (apiIdx < 0) return null;
                int windowEnd = plain.IndexOf("Executable", apiIdx, StringComparison.OrdinalIgnoreCase);
                if (windowEnd < 0) windowEnd = Math.Min(apiIdx + 1500, plain.Length);
                var window = plain.Substring(apiIdx, windowEnd - apiIdx);
                var d3dMatch = System.Text.RegularExpressions.Regex.Match(window, @"Direct3D\s*([\d\s,/]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (d3dMatch.Success)
                {
                    var v = d3dMatch.Groups[1].Value;
                    if (System.Text.RegularExpressions.Regex.IsMatch(v, @"\b9\b"))  info.HasDirectX9  = true;
                    if (System.Text.RegularExpressions.Regex.IsMatch(v, @"\b10\b")) info.HasDirectX10 = true;
                    if (System.Text.RegularExpressions.Regex.IsMatch(v, @"\b11\b")) info.HasDirectX11 = true;
                    if (System.Text.RegularExpressions.Regex.IsMatch(v, @"\b12\b")) info.HasDirectX12 = true;
                }
                if (System.Text.RegularExpressions.Regex.IsMatch(window, @"\bVulkan\b",  System.Text.RegularExpressions.RegexOptions.IgnoreCase)) info.HasVulkan  = true;
                if (System.Text.RegularExpressions.Regex.IsMatch(window, @"\bOpenGL\b",  System.Text.RegularExpressions.RegexOptions.IgnoreCase)) info.HasOpenGL  = true;
            }

            return (info.HasDirectX9 || info.HasDirectX10 || info.HasDirectX11 || info.HasDirectX12 || info.HasVulkan || info.HasOpenGL) ? info : null;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[PcgwService.ParseApiSection] Parse failed — {ex.Message}");
            return null;
        }
    }

    private static bool IsPlatformExcluded(string text)
    {
        var n = text.ToLowerInvariant();
        if (n.Contains("os x") || n.Contains("macos") || n.Contains("mac os")) return true;
        if (System.Text.RegularExpressions.Regex.IsMatch(n, @"\blinux\b.*\bonly\b")) return true;
        if (System.Text.RegularExpressions.Regex.IsMatch(n, @"\bonly\b.*\blinux\b")) return true;
        if (n.Contains("linux default") || n.Contains("linux only")) return true;
        return false;
    }

    /// <summary>
    /// Parses the "Game data → Configuration file(s) location" section from a PCGW page.
    /// Parses the "Game data → Configuration file(s) location" section from a PCGW page.
    /// Returns (windowsPath, xboxPath) — either may be null. Normalised to backslashes.
    /// Only returns paths with %LOCALAPPDATA%/%USERPROFILE% — skips Mac/Linux/Steam paths.
    /// </summary>
    private static (string? Windows, string? Xbox) ParseConfigFilesSection(string html)
    {
        try
        {
            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(html);

            // PCGW renders config location in a table with class containing "table-gamedata"
            // Each row: <tr class="table-gamedata-body-row"><th scope="row">Windows</th><td class="table-gamedata-body-location">...</td></tr>
            var configTable = doc.DocumentNode.SelectSingleNode("//table[@id='table-gamedata-config']")
                           ?? doc.DocumentNode.SelectSingleNode("//table[.//th[contains(@class,'table-gamedata-head-system')]]")
                           ?? doc.DocumentNode.SelectSingleNode("//table[.//td[contains(@class,'table-gamedata-body-location')]]");

            if (configTable == null)
            {
                // Fallback: find the table immediately following the "Configuration file(s)" heading text.
                // Walk all elements looking for a heading-like element containing that text, then
                // take the next sibling table.
                foreach (var node in doc.DocumentNode.Descendants())
                {
                    if (node.NodeType != HtmlAgilityPack.HtmlNodeType.Element) continue;
                    if (!node.InnerText.Contains("Configuration file", StringComparison.OrdinalIgnoreCase)) continue;

                    // Only consider elements that look like headings or caption-like cells
                    var tag = node.Name.ToLowerInvariant();
                    if (tag != "h2" && tag != "h3" && tag != "h4" && tag != "caption" && tag != "th") continue;

                    // Walk siblings/parent siblings to find next table
                    var container = node.Name == "th" ? node.ParentNode?.ParentNode?.ParentNode : node;
                    if (container == null) continue;

                    var sib = container.NextSibling;
                    while (sib != null)
                    {
                        if (sib.Name.Equals("table", StringComparison.OrdinalIgnoreCase))
                        { configTable = sib; break; }
                        if (sib.Name is "h2" or "h3" or "h4") break;
                        sib = sib.NextSibling;
                    }
                    if (configTable != null) break;
                }
            }

            if (configTable != null)
            {
                var rows = configTable.SelectNodes(".//tr[@class and contains(@class,'table-gamedata-body-row')]");
                rows ??= configTable.SelectNodes(".//tr");
                if (rows != null)
                {
                    string? pathWindows = null;
                    string? pathXbox    = null;

                    foreach (var row in rows)
                    {
                        var systemTh = row.SelectSingleNode(".//th[@scope='row']");
                        if (systemTh == null) continue;

                        var system = HtmlAgilityPack.HtmlEntity.DeEntitize(systemTh.InnerText).Trim();

                        // Skip Linux/macOS/Steam Play rows
                        if (system.Contains("Steam Play",  StringComparison.OrdinalIgnoreCase)) continue;
                        if (system.Contains("Linux",       StringComparison.OrdinalIgnoreCase)) continue;
                        if (system.Contains("macOS",       StringComparison.OrdinalIgnoreCase)) continue;
                        if (system.Contains("OS X",        StringComparison.OrdinalIgnoreCase)) continue;

                        bool isXbox = system.Contains("Microsoft Store", StringComparison.OrdinalIgnoreCase)
                                   || system.Contains("Xbox",             StringComparison.OrdinalIgnoreCase)
                                   || system.Contains("Game Pass",        StringComparison.OrdinalIgnoreCase)
                                   || system.Contains("Windows Store",    StringComparison.OrdinalIgnoreCase);
                        bool isWindows = !isXbox && system.Contains("Windows", StringComparison.OrdinalIgnoreCase);

                        if (!isWindows && !isXbox) continue;

                        // Location td — prefer the monospace span text which holds the actual path
                        var locationTd = row.SelectSingleNode(".//td[contains(@class,'table-gamedata-body-location')]")
                                      ?? row.SelectSingleNode(".//td");
                        if (locationTd == null) continue;

                        // Some cells have multiple monospace spans (e.g. <user-id> placeholder first,
                        // then the real path second). Try all spans and take the first valid one.
                        string? normalised = null;
                        var monoSpans = locationTd.SelectNodes(".//span[contains(@class,'monospace')]");
                        if (monoSpans != null)
                        {
                            foreach (var span in monoSpans)
                            {
                                var candidate = NormaliseConfigPath(HtmlAgilityPack.HtmlEntity.DeEntitize(span.InnerText).Trim());
                                if (candidate != null) { normalised = candidate; break; }
                            }
                        }

                        // Fallback: text nodes of the whole td
                        if (normalised == null)
                        {
                            var textNodes = locationTd.SelectNodes(".//text()");
                            var rawPath = textNodes != null
                                ? string.Concat(textNodes.Select(n => HtmlAgilityPack.HtmlEntity.DeEntitize(n.InnerText))).Trim()
                                : HtmlAgilityPack.HtmlEntity.DeEntitize(locationTd.InnerText).Trim();
                            normalised = NormaliseConfigPath(rawPath);
                        }

                        if (normalised == null) continue;

                        if (isXbox)
                            pathXbox = normalised;
                        else
                            pathWindows = normalised;
                    }

                    if (pathWindows != null || pathXbox != null)
                    {
                        CrashReporter.Log($"[PcgwService.ParseConfigFilesSection] Found via table:" +
                            (pathWindows != null ? $" Windows='{pathWindows}'" : "") +
                            (pathXbox    != null ? $" Xbox='{pathXbox}'"    : ""));
                        return (pathWindows, pathXbox);
                    }
                }
            }

            // Plain-text fallback: search the whole page for a Windows env-var path
            // near "Configuration file". Skip the first hit (table of contents) by
            // looking for the second occurrence of "Configuration file".
            {
                var plain = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
                plain = System.Text.RegularExpressions.Regex.Replace(plain, @"\s+", " ");

                // Find the second occurrence of "Configuration file" to skip the TOC entry
                int cfIdx = plain.IndexOf("Configuration file", StringComparison.OrdinalIgnoreCase);
                if (cfIdx >= 0)
                {
                    int cfIdx2 = plain.IndexOf("Configuration file", cfIdx + 1, StringComparison.OrdinalIgnoreCase);
                    int searchFrom = cfIdx2 >= 0 ? cfIdx2 : cfIdx;

                    // Look within the next 800 chars for a Windows env-var path
                    var window = plain.Substring(searchFrom, Math.Min(800, plain.Length - searchFrom));
                    var match = System.Text.RegularExpressions.Regex.Match(
                        window,
                        @"(%LOCALAPPDATA%|%APPDATA%|%USERPROFILE%)[/\\][^\s<>\[\]]+",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        var normalised = NormaliseConfigPath(match.Value);
                        if (normalised != null)
                        {
                            CrashReporter.Log($"[PcgwService.ParseConfigFilesSection] Found via text fallback: '{normalised}'");
                            return (normalised, null);
                        }
                    }
                    else
                    {
                        CrashReporter.Log($"[PcgwService.ParseConfigFilesSection] No env-var path found. Window: '{window.Substring(0, Math.Min(200, window.Length))}'");
                    }
                }
            }

            return (null, null);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[PcgwService.ParseConfigFilesSection] Parse failed — {ex.Message}");
            return (null, null);
        }
    }

    /// <summary>
    /// Normalises a PCGW config path to a Windows-style path suitable for passing to
    /// ResolveEngineIniDir as a projectNameOverride. Converts forward slashes to backslashes,
    /// trims trailing separators. Returns null if the path doesn't look like a Windows config directory.
    /// </summary>
    private static string? NormaliseConfigPath(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        // Must start with a known Windows env var
        if (!raw.StartsWith("%LOCALAPPDATA%", StringComparison.OrdinalIgnoreCase) &&
            !raw.StartsWith("%APPDATA%",      StringComparison.OrdinalIgnoreCase) &&
            !raw.StartsWith("%USERPROFILE%",  StringComparison.OrdinalIgnoreCase))
            return null;

        // Normalise to backslashes and strip trailing separator
        var normalised = raw.Replace('/', '\\').TrimEnd('\\');

        // Strip footnote markers like "[Note 2]" that PCGW appends
        normalised = System.Text.RegularExpressions.Regex.Replace(normalised, @"\s*\[Note\s*\d+\].*$", "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).TrimEnd('\\');

        // Strip placeholder segments like <user-id> — path can't be resolved
        if (normalised.Contains('<') && normalised.Contains('>'))
            return null;

        // If the last segment is a filename (has a file extension), strip it to get the folder
        var lastSegment = normalised.Split('\\').LastOrDefault() ?? "";
        if (System.Text.RegularExpressions.Regex.IsMatch(lastSegment, @"\.[a-zA-Z0-9]{2,5}$"))
        {
            // Strip the filename — keep the folder
            var folderPath = normalised.Substring(0, normalised.Length - lastSegment.Length).TrimEnd('\\');
            if (string.IsNullOrWhiteSpace(folderPath)) return null;
            normalised = folderPath;
        }

        // Reject paths that are clearly save data, not config
        if (normalised.Contains("SaveGames", StringComparison.OrdinalIgnoreCase) ||
            normalised.Contains("\\Saves\\", StringComparison.OrdinalIgnoreCase))
            return null;

        // Reject UWP package storage paths — these are save/sync data, not config folders
        if (normalised.Contains("\\Packages\\", StringComparison.OrdinalIgnoreCase) ||
            normalised.Contains("\\wgs",         StringComparison.OrdinalIgnoreCase))
            return null;

        // Reject paths with parent-directory traversal (..) — can't be safely expanded
        if (normalised.Contains(".."))
            return null;

        // Reject obviously non-Windows paths (Steam compat, Linux, pfx)
        if (normalised.Contains("steamapps", StringComparison.OrdinalIgnoreCase) ||
            normalised.Contains("pfx",        StringComparison.OrdinalIgnoreCase))
            return null;

        return string.IsNullOrWhiteSpace(normalised) ? null : normalised;
    }

    private void SaveApiCacheToDisk()
    {
        try
        {
            var dir = Path.GetDirectoryName(ApiCachePath)!;
            Directory.CreateDirectory(dir);
            var json = System.Text.Json.JsonSerializer.Serialize(_apiInfoCache,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            FileHelper.WriteAllTextWithRetry(ApiCachePath, json, "PcgwService.SaveApiCache");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[PcgwService.SaveApiCache] Write failed — {ex.Message}");
        }
    }
}
