using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PCGWEditor;

public record SyncResult(
    bool    Success,
    string? RemoteContent,
    string? LocalContent,
    bool    HasDiff,
    string? Error);

public static class PcgwDataService
{
    private static readonly HttpClient _http = new();

    private const string RepoOwner = "RankFTW";
    private const string RepoName  = "RHI";
    private const string Branch    = "main";
    private const string RemotePath = "database/pcgw_data.json";

    public static readonly string RawUrl =
        $"https://raw.githubusercontent.com/{RepoOwner}/{RepoName}/{Branch}/{RemotePath}";

    private static readonly JsonSerializerOptions s_readOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonSerializerOptions s_writeOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // ── Local file path (next to exe) ─────────────────────────────────────────

    public static string LocalPath
    {
        get
        {
            var dir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
            return Path.Combine(dir, "pcgw_data.json");
        }
    }

    // ── Load ──────────────────────────────────────────────────────────────────

    public static PcgwDataFile? Load(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            return Deserialize(json);
        }
        catch { return null; }
    }

    public static PcgwDataFile? Deserialize(string json)
    {
        try
        {
            var data = JsonSerializer.Deserialize<PcgwDataFile>(json, s_readOpts);
            if (data == null) return null;

            // Re-key with OrdinalIgnoreCase to handle any case-variant duplicates from PCGW
            // (e.g. both "Cube" and "cube" — last one wins)
            var deduped = new Dictionary<string, PcgwEntryRaw>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in data.Games) deduped[kv.Key] = kv.Value;
            data.Games = deduped;

            return data;
        }
        catch { return null; }
    }

    // ── Save ──────────────────────────────────────────────────────────────────

    public static void Save(PcgwDataFile data, string path)
    {
        // Rebuild metadata
        data.GameCount = data.Games.Count;

        // Re-sort games and name_overrides alphabetically for clean diffs
        // Deduplicate during sort — OrdinalIgnoreCase dict throws on case-variant duplicates
        var sortedGames = new Dictionary<string, PcgwEntryRaw>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in data.Games.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
            sortedGames[kv.Key] = kv.Value;
        data.Games = sortedGames;

        data.NameOverrides = new Dictionary<string, string>(
            data.NameOverrides.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        var json = JsonSerializer.Serialize(data, s_writeOpts);
        File.WriteAllText(path, json, Encoding.UTF8);
    }

    // ── GitHub fetch ──────────────────────────────────────────────────────────

    public static async Task<SyncResult> FetchAsync(string? githubToken = null)
    {
        string? localContent = File.Exists(LocalPath) ? File.ReadAllText(LocalPath) : null;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, RawUrl);
            req.Headers.UserAgent.ParseAdd("PCGWEditor/1.0");
            if (!string.IsNullOrEmpty(githubToken))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", githubToken);

            var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            var remoteContent = await resp.Content.ReadAsStringAsync();

            var rNorm = remoteContent.Replace("\r\n", "\n").TrimEnd();
            var lNorm = localContent?.Replace("\r\n", "\n").TrimEnd();
            bool hasDiff = rNorm != lNorm;

            return new SyncResult(true, remoteContent, localContent, hasDiff, null);
        }
        catch (Exception ex)
        {
            return new SyncResult(false, null, localContent, false, ex.Message);
        }
    }

    public static async Task SaveLocalAsync(string content)
    {
        await File.WriteAllTextAsync(LocalPath, content, Encoding.UTF8);
    }

    // ── GitHub push ───────────────────────────────────────────────────────────

    public static async Task<(bool Success, string? Error)> PushAsync(
        string content, string githubToken, string commitMessage)
    {
        if (string.IsNullOrWhiteSpace(githubToken))
            return (false, "No GitHub token configured.");

        var apiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/contents/{RemotePath}";

        try
        {
            // Get current file SHA
            string? sha = null;
            using (var req = new HttpRequestMessage(HttpMethod.Get, apiUrl))
            {
                req.Headers.UserAgent.ParseAdd("PCGWEditor/1.0");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", githubToken);
                req.Headers.Accept.ParseAdd("application/vnd.github+json");
                var resp = await _http.SendAsync(req);
                if (resp.IsSuccessStatusCode)
                {
                    var j = await resp.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(j);
                    if (doc.RootElement.TryGetProperty("sha", out var shaEl))
                        sha = shaEl.GetString();
                }
            }

            var contentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(content));
            var body = new Dictionary<string, object?> {
                ["message"] = commitMessage,
                ["content"] = contentBase64,
                ["branch"]  = Branch,
            };
            if (sha != null) body["sha"] = sha;

            using var putReq = new HttpRequestMessage(HttpMethod.Put, apiUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            };
            putReq.Headers.UserAgent.ParseAdd("PCGWEditor/1.0");
            putReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", githubToken);
            putReq.Headers.Accept.ParseAdd("application/vnd.github+json");

            var putResp = await _http.SendAsync(putReq);
            if (!putResp.IsSuccessStatusCode)
            {
                var err = await putResp.Content.ReadAsStringAsync();
                return (false, $"GitHub API error {(int)putResp.StatusCode}: {err}");
            }
            return (true, null);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    // ── Token storage ─────────────────────────────────────────────────────────

    private static string TokenPath
    {
        get
        {
            var dir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
            return Path.Combine(dir, "github_token.txt");
        }
    }

    public static string LoadToken()
    {
        try { return File.Exists(TokenPath) ? File.ReadAllText(TokenPath).Trim() : ""; }
        catch { return ""; }
    }

    public static void SaveToken(string token)
    {
        try { File.WriteAllText(TokenPath, token); } catch { }
    }
}
