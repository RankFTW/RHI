using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PCGWEditor;

/// <summary>
/// Progress report emitted during a fetch operation.
/// </summary>
public record FetchProgress(string Message, int? Percent = null);

/// <summary>
/// Result of a fetch operation.
/// </summary>
public record FetchResult(
    bool   Success,
    int    Added,
    int    Updated,
    int    Total,
    string? Error,
    List<string>? AddedNames   = null,
    List<string>? UpdatedNames = null);

/// <summary>
/// C# port of fetch_pcgw_data.py.
/// Logs into PCGamingWiki via the MediaWiki API (bot password),
/// then paginates through CargoQuery tables to build/update pcgw_data.json.
/// </summary>
public class PcgwFetchService
{
    private const string ApiUrl    = "https://www.pcgamingwiki.com/w/api.php";
    private const string UserAgent = "RHI-PCGW-Fetcher/1.0 (github.com/RankFTW/RHI; rankftw@googlemail.com)";
    private const int    CargoLimit     = 500;
    private const int    ChunkSize      = 50;
    private static readonly TimeSpan RequestDelay = TimeSpan.FromSeconds(2);

    private readonly HttpClient _http;
    private readonly CancellationToken _ct;

    public PcgwFetchService(CancellationToken ct = default)
    {
        // Must use a handler with cookies enabled so login session persists
        var handler = new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = new CookieContainer()
        };
        _http = new HttpClient(handler);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        _ct   = ct;
    }

    // ── Credentials storage (next to exe) ────────────────────────────────────

    private static string CredPath
    {
        get
        {
            var dir = System.IO.Path.GetDirectoryName(Environment.ProcessPath)
                      ?? AppContext.BaseDirectory;
            return System.IO.Path.Combine(dir, "pcgw_credentials.txt");
        }
    }

    public static (string Username, string Password) LoadCredentials()
    {
        try
        {
            if (!System.IO.File.Exists(CredPath)) return ("", "");
            var lines = System.IO.File.ReadAllLines(CredPath);
            var user  = lines.Length > 0 ? lines[0].Trim() : "";
            var pass  = lines.Length > 1 ? lines[1].Trim() : "";
            return (user, pass);
        }
        catch { return ("", ""); }
    }

    public static void SaveCredentials(string username, string password)
    {
        try { System.IO.File.WriteAllText(CredPath, $"{username}\n{password}"); }
        catch { }
    }

    public static bool HasCredentials()
    {
        var (u, p) = LoadCredentials();
        return !string.IsNullOrEmpty(u) && !string.IsNullOrEmpty(p);
    }

    // ── Login ─────────────────────────────────────────────────────────────────

    private async Task LoginAsync(string username, string password)
    {
        // Step 1: get login token
        var tokenResp = await _http.GetAsync(
            ApiUrl + "?action=query&meta=tokens&type=login&format=json", _ct);
        tokenResp.EnsureSuccessStatusCode();
        var tokenJson = await tokenResp.Content.ReadAsStringAsync(_ct);
        using var tokenDoc = JsonDocument.Parse(tokenJson);
        var loginToken = tokenDoc.RootElement
            .GetProperty("query").GetProperty("tokens").GetProperty("logintoken").GetString()
            ?? throw new Exception("Could not get login token.");

        // Step 2: authenticate
        var loginData = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["action"]     = "login",
            ["lgname"]     = username,
            ["lgpassword"] = password,
            ["lgtoken"]    = loginToken,
            ["format"]     = "json",
        });
        var loginResp = await _http.PostAsync(ApiUrl, loginData, _ct);
        loginResp.EnsureSuccessStatusCode();
        var loginJson = await loginResp.Content.ReadAsStringAsync(_ct);
        using var loginDoc = JsonDocument.Parse(loginJson);
        var result = loginDoc.RootElement.GetProperty("login").GetProperty("result").GetString();
        if (result != "Success")
        {
            var reason = loginDoc.RootElement.GetProperty("login")
                .TryGetProperty("reason", out var r) ? r.GetString() : "Unknown";
            throw new Exception($"Login failed: {reason}");
        }
    }

    // ── CargoQuery ────────────────────────────────────────────────────────────

    private async Task<List<Dictionary<string, string>>> CargoQueryPageAsync(
        string tables, string fields,
        string? joinOn = null, string? where = null,
        int offset = 0, int limit = CargoLimit)
    {
        // Note: Do NOT Uri.EscapeDataString the params - MediaWiki API handles them raw
        // and escaping causes issues with the Cargo query parser
        var url = new StringBuilder(ApiUrl)
            .Append("?action=cargoquery")
            .Append("&format=json")
            .Append($"&tables={tables}")
            .Append($"&fields={fields}")
            .Append($"&limit={limit}")
            .Append($"&offset={offset}");
        if (joinOn != null) url.Append($"&join_on={joinOn}");
        if (where  != null) url.Append($"&where={Uri.EscapeDataString(where)}");  // WHERE needs escaping for quotes

        HttpResponseMessage resp;
        // Handle 429 with a single retry after 61s
        resp = await _http.GetAsync(url.ToString(), _ct);
        if (resp.StatusCode == HttpStatusCode.TooManyRequests)
        {
            await Task.Delay(TimeSpan.FromSeconds(61), _ct);
            resp = await _http.GetAsync(url.ToString(), _ct);
        }
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync(_ct);
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("error", out var errEl))
            throw new Exception($"CargoQuery error: {errEl.GetProperty("info").GetString()}");

        var items = new List<Dictionary<string, string>>();
        foreach (var item in doc.RootElement.GetProperty("cargoquery").EnumerateArray())
        {
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in item.GetProperty("title").EnumerateObject())
                row[prop.Name] = prop.Value.GetString() ?? "";
            items.Add(row);
        }
        return items;
    }

    private async Task<List<Dictionary<string, string>>> CargoQueryAllAsync(
        string tables, string fields,
        string? joinOn = null, string? where = null,
        IProgress<FetchProgress>? progress = null,
        int? maxRows = null)
    {
        var results = new List<Dictionary<string, string>>();
        int offset  = 0;
        while (true)
        {
            _ct.ThrowIfCancellationRequested();
            var batch = await CargoQueryPageAsync(tables, fields, joinOn, where, offset, CargoLimit);
            results.AddRange(batch);
            progress?.Report(new FetchProgress($"  {results.Count:N0} rows fetched…"));
            if (batch.Count < CargoLimit) break;
            if (maxRows.HasValue && results.Count >= maxRows.Value) break;
            offset += CargoLimit;
            await Task.Delay(RequestDelay, _ct);
        }
        return results;
    }

    private async Task<List<Dictionary<string, string>>> CargoQuerySpecificPagesAsync(
        string tables, string fields, string? joinOn,
        IEnumerable<string> pageNames,
        IProgress<FetchProgress>? progress = null)
    {
        var results = new List<Dictionary<string, string>>();
        var pages   = pageNames.OrderBy(p => p).ToList();

        // Cargo list-field column names — if a page name contains these as substrings,
        // Cargo replaces the value in the WHERE clause with a column reference and errors.
        // Split such pages into individual queries to avoid the substitution.
        static bool HasCargoConflict(string name) =>
            name.Contains("Series",     StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Genres",     StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Themes",     StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Developers", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Publishers", StringComparison.OrdinalIgnoreCase);

        var safePages      = pages.Where(p => !HasCargoConflict(p)).ToList();
        var conflictPages  = pages.Where(HasCargoConflict).ToList();

        // Batch safe pages normally
        for (int i = 0; i < safePages.Count; i += ChunkSize)
        {
            _ct.ThrowIfCancellationRequested();
            var chunk = safePages.Skip(i).Take(ChunkSize);
            var inList = string.Join(",", chunk.Select(p => $"\"{p.Replace("\"", "\\\"")}\""));
            var where  = $"Game._pageName IN ({inList})";
            var batch  = await CargoQueryPageAsync(tables, fields, joinOn, where);
            results.AddRange(batch);
            progress?.Report(new FetchProgress($"  {results.Count:N0} rows fetched…"));
            await Task.Delay(RequestDelay, _ct);
        }

        // Conflict pages: use LIKE with % anchors so Cargo can't substitute the value
        foreach (var page in conflictPages)
        {
            _ct.ThrowIfCancellationRequested();
            // LIKE with exact match (no wildcards) — avoids the column substitution
            var escaped = page.Replace("'", "\\'").Replace("%", "\\%").Replace("_", "\\_").Replace("\\", "\\\\");
            var where   = $"Game._pageName LIKE '{escaped}'";
            var batch   = await CargoQueryPageAsync(tables, fields, joinOn, where);
            results.AddRange(batch);
            progress?.Report(new FetchProgress($"  {results.Count:N0} rows fetched (incl. conflict page)…"));
            await Task.Delay(RequestDelay, _ct);
        }
        return results;
    }

    // ── Recent changes ────────────────────────────────────────────────────────

    private async Task<HashSet<string>> GetChangedPagesSinceAsync(
        string sinceTs, IProgress<FetchProgress>? progress = null)
    {
        var pages  = new HashSet<string>(StringComparer.Ordinal);
        var url    = new StringBuilder(ApiUrl)
            .Append("?action=query&list=recentchanges")
            .Append("&rcnamespace=0")
            .Append($"&rcstart={Uri.EscapeDataString(sinceTs)}")
            .Append("&rcdir=newer")
            .Append("&rcprop=title%7Ctype")
            .Append("&rclimit=500")
            .Append("&rctype=edit%7Cnew")
            .Append("&format=json");
        string? continueToken = null;

        while (true)
        {
            _ct.ThrowIfCancellationRequested();
            var reqUrl = continueToken != null
                ? url + $"&rccontinue={Uri.EscapeDataString(continueToken)}"
                : url.ToString();

            var resp = await _http.GetAsync(reqUrl, _ct);
            if (resp.StatusCode == HttpStatusCode.TooManyRequests)
            {
                await Task.Delay(TimeSpan.FromSeconds(61), _ct);
                resp = await _http.GetAsync(reqUrl, _ct);
            }
            resp.EnsureSuccessStatusCode();

            var json = await resp.Content.ReadAsStringAsync(_ct);
            using var doc = JsonDocument.Parse(json);

            foreach (var rc in doc.RootElement.GetProperty("query")
                                              .GetProperty("recentchanges")
                                              .EnumerateArray())
            {
                var title = rc.TryGetProperty("title", out var t) ? t.GetString() : null;
                if (!string.IsNullOrWhiteSpace(title)) pages.Add(title!);
            }

            if (doc.RootElement.TryGetProperty("continue", out var cont)
                && cont.TryGetProperty("rccontinue", out var rcc))
            {
                continueToken = rcc.GetString();
                await Task.Delay(TimeSpan.FromSeconds(1), _ct);
            }
            else break;
        }

        progress?.Report(new FetchProgress($"  {pages.Count:N0} changed pages found"));
        return pages;
    }

    // ── Data parsing ──────────────────────────────────────────────────────────

    private static Dictionary<string, PcgwEntryRaw> ParseApiRows(
        IEnumerable<Dictionary<string, string>> rows)
    {
        var result = new Dictionary<string, PcgwEntryRaw>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var page = (row.TryGetValue("Page", out var p) ? p : "").Trim();
            if (string.IsNullOrEmpty(page)) continue;

            var dx  = (row.TryGetValue("DX",     out var d) ? d : "").ToLowerInvariant();
            var vk  = (row.TryGetValue("Vulkan", out var v) ? v : "").ToLowerInvariant();
            var ogl = (row.TryGetValue("OpenGL", out var o) ? o : "").ToLowerInvariant();
            var steamRaw = (row.TryGetValue("SteamAppID", out var s) ? s : "").Trim();

            int steamId = 0;
            if (!string.IsNullOrEmpty(steamRaw))
                foreach (var part in steamRaw.Split(','))
                    if (int.TryParse(part.Trim(), out var val) && val > 0) { steamId = val; break; }

            result[page] = new PcgwEntryRaw
            {
                SteamAppId = steamId,
                Dx9        = dx.Contains("9") || dx.Contains("9.0"),
                Dx10       = dx.Contains("10"),
                Dx11       = dx.Contains("11"),
                Dx12       = dx.Contains("12"),
                Vulkan     = !string.IsNullOrEmpty(vk)  && vk  is not ("" or "false" or "none"),
                OpenGL     = !string.IsNullOrEmpty(ogl) && ogl is not ("" or "false" or "none"),
            };
        }
        return result;
    }

    private static bool EntriesEqual(PcgwEntryRaw a, PcgwEntryRaw b) =>
        a.SteamAppId    == b.SteamAppId    &&
        a.Dx9           == b.Dx9           &&
        a.Dx10          == b.Dx10          &&
        a.Dx11          == b.Dx11          &&
        a.Dx12          == b.Dx12          &&
        a.Vulkan        == b.Vulkan        &&
        a.OpenGL        == b.OpenGL        &&
        a.ConfigPath    == b.ConfigPath    &&
        a.ConfigPathXbox == b.ConfigPathXbox;

    private static void MergeConfigRows(
        IEnumerable<Dictionary<string, string>> rows,
        Dictionary<string, PcgwEntryRaw> target)
    {
        foreach (var row in rows)
        {
            var page     = (row.TryGetValue("Page",     out var p) ? p : "").Trim();
            var platform = (row.TryGetValue("Platform", out var pl) ? pl : "").Trim();
            var paths    = (row.TryGetValue("Paths",    out var pa) ? pa : "").Trim();
            var type     = (row.TryGetValue("Type",     out var t) ? t : "").Trim();

            if (string.IsNullOrEmpty(page) || string.IsNullOrEmpty(paths)) continue;
            if (!string.Equals(type, "Config", StringComparison.OrdinalIgnoreCase)) continue;

            if (!target.TryGetValue(page, out var entry))
                target[page] = entry = new PcgwEntryRaw();

            if (platform is "Windows" or "Steam")
            {
                if (entry.ConfigPath == null || platform == "Windows")
                    entry.ConfigPath = paths;
            }
            else if (platform == "Microsoft Store")
            {
                entry.ConfigPathXbox = paths;
            }
        }
    }

    // ── Public fetch methods ──────────────────────────────────────────────────

    /// <summary>
    /// Fetches only pages changed since <paramref name="sinceTs"/> and merges
    /// them into <paramref name="existing"/>. Fast (~30 seconds typical).
    /// </summary>
    public async Task<FetchResult> FetchUpdatesAsync(
        string username, string password,
        Dictionary<string, PcgwEntryRaw> existing,
        string sinceTs,
        IProgress<FetchProgress>? progress = null)
    {
        try
        {
            progress?.Report(new FetchProgress("Logging in to PCGW…"));
            await LoginAsync(username, password);

            progress?.Report(new FetchProgress($"Fetching recent changes since {sinceTs}…"));
            var changedPages = await GetChangedPagesSinceAsync(sinceTs, progress);
            if (changedPages.Count == 0)
                return new FetchResult(true, 0, 0, existing.Count, null);

            progress?.Report(new FetchProgress($"Fetching API data for {changedPages.Count:N0} pages…"));
            var apiRows = await CargoQuerySpecificPagesAsync(
                "Game,API",
                "Game._pageName=Page,Game.Steam_AppID=SteamAppID,API.Direct3D_versions=DX,API.Vulkan_versions=Vulkan,API.OpenGL_versions=OpenGL",
                "Game._pageID=API._pageID",
                changedPages, progress);

            await Task.Delay(RequestDelay, _ct);

            progress?.Report(new FetchProgress($"Fetching config paths for {changedPages.Count:N0} pages…"));
            var cfgRows = await CargoQuerySpecificPagesAsync(
                "Game,GameData",
                "Game._pageName=Page,GameData.Type=Type,GameData.Platform=Platform,GameData.Paths=Paths",
                "Game._pageID=GameData._pageID",
                changedPages, progress);

            var newEntries = ParseApiRows(apiRows);
            MergeConfigRows(cfgRows, newEntries);

            var addedNames   = newEntries.Keys.Where(k => !existing.ContainsKey(k)).OrderBy(k => k).ToList();
            var updatedNames = newEntries.Keys.Where(k =>  existing.ContainsKey(k)
                                                        && !EntriesEqual(existing[k], newEntries[k]))
                                             .OrderBy(k => k).ToList();

            foreach (var (key, val) in newEntries)
                existing[key] = val;

            return new FetchResult(true, addedNames.Count, updatedNames.Count,
                existing.Count, null, addedNames, updatedNames);
        }
        catch (OperationCanceledException)
        {
            return new FetchResult(false, 0, 0, existing.Count, "Cancelled");
        }
        catch (Exception ex)
        {
            return new FetchResult(false, 0, 0, existing.Count, ex.Message);
        }
    }

    /// <summary>
    /// Full fetch of all ~55k PCGW games. Replaces <paramref name="existing"/> entirely.
    /// Takes ~5–8 minutes.
    /// </summary>
    public async Task<FetchResult> FetchAllAsync(
        string username, string password,
        Dictionary<string, PcgwEntryRaw> existing,
        IProgress<FetchProgress>? progress = null)
    {
        try
        {
            progress?.Report(new FetchProgress("Logging in to PCGW…"));
            await LoginAsync(username, password);

            progress?.Report(new FetchProgress("Fetching API data (DX/VK/OGL + Steam AppIDs)…"));
            var apiRows = await CargoQueryAllAsync(
                "Game,API",
                "Game._pageName=Page,Game.Steam_AppID=SteamAppID,API.Direct3D_versions=DX,API.Vulkan_versions=Vulkan,API.OpenGL_versions=OpenGL",
                joinOn: "Game._pageID=API._pageID",
                progress: progress);

            await Task.Delay(RequestDelay, _ct);

            progress?.Report(new FetchProgress("Fetching config file paths…"));
            var cfgRows = await CargoQueryAllAsync(
                "Game,GameData",
                "Game._pageName=Page,GameData.Type=Type,GameData.Platform=Platform,GameData.Paths=Paths",
                joinOn: "Game._pageID=GameData._pageID",
                where:  "GameData.Type='Config' AND (GameData.Platform='Windows' OR GameData.Platform='Steam' OR GameData.Platform='Microsoft Store')",
                progress: progress);

            var newEntries = ParseApiRows(apiRows);
            MergeConfigRows(cfgRows, newEntries);

            existing.Clear();
            foreach (var (key, val) in newEntries)
                existing[key] = val;

            return new FetchResult(true, newEntries.Count, 0, newEntries.Count, null);
        }
        catch (OperationCanceledException)
        {
            return new FetchResult(false, 0, 0, existing.Count, "Cancelled");
        }
        catch (Exception ex)
        {
            return new FetchResult(false, 0, 0, existing.Count, ex.Message);
        }
    }
}
