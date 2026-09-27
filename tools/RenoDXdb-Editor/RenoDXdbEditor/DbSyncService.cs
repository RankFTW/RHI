using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.IO;

namespace RenoDXdbEditor;

public enum DbType { NamedMods, Unreal, Unity }

public record SyncResult(
    bool Success,
    string? LocalPath,      // path of the locally cached file
    string? RemoteContent,  // raw JSON fetched from remote
    string? LocalContent,   // raw JSON from previously cached file (null if first sync)
    bool HasDiff,           // remote differs from local
    string? Error);

public static class DbSyncService
{
    private static readonly HttpClient _http = new();

    private const string RepoOwner  = "RankFTW";
    private const string RepoName   = "rhi-repo";
    private const string Branch     = "main";

    private static string RemotePath(DbType db) => db switch
    {
        DbType.NamedMods  => "database/RenoDXdb.json",
        DbType.Unreal     => "database/RenoDXdb-unreal.json",
        DbType.Unity      => "database/RenoDXdb-unity.json",
        _                 => throw new ArgumentOutOfRangeException()
    };

    public static string LocalFileName(DbType db) => db switch
    {
        DbType.NamedMods  => "RenoDXdb.json",
        DbType.Unreal     => "RenoDXdb-unreal.json",
        DbType.Unity      => "RenoDXdb-unity.json",
        _                 => throw new ArgumentOutOfRangeException()
    };

    public static string RawUrl(DbType db) =>
        $"https://raw.githubusercontent.com/{RepoOwner}/{RepoName}/{Branch}/{RemotePath(db)}";

    /// <summary>
    /// Returns the path where the file should be cached next to the exe.
    /// </summary>
    public static string LocalCachePath(DbType db)
    {
        var dir = Path.GetDirectoryName(Environment.ProcessPath)
               ?? AppContext.BaseDirectory;
        return Path.Combine(dir, LocalFileName(db));
    }

    /// <summary>
    /// Downloads the remote file and compares it with the local cached copy.
    /// Does NOT overwrite the local file — caller decides what to do with the diff.
    /// </summary>
    public static async Task<SyncResult> FetchAsync(DbType db, string? githubToken = null)
    {
        var localPath = LocalCachePath(db);
        string? localContent = File.Exists(localPath) ? await File.ReadAllTextAsync(localPath) : null;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, RawUrl(db));
            req.Headers.UserAgent.ParseAdd("RenoDXdb-Editor/2.0");
            if (!string.IsNullOrEmpty(githubToken))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", githubToken);

            var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            var remoteContent = await resp.Content.ReadAsStringAsync();

            // Normalise line endings before comparing
            var remoteNorm = remoteContent.Replace("\r\n", "\n").TrimEnd();
            var localNorm  = localContent?.Replace("\r\n", "\n").TrimEnd();

            bool hasDiff = localNorm != remoteNorm;

            return new SyncResult(true, localPath, remoteContent, localContent, hasDiff, null);
        }
        catch (Exception ex)
        {
            return new SyncResult(false, localPath, null, localContent, false, ex.Message);
        }
    }

    /// <summary>
    /// Writes remoteContent to the local cache path.
    /// </summary>
    public static async Task SaveLocalAsync(DbType db, string content)
    {
        await File.WriteAllTextAsync(LocalCachePath(db), content, Encoding.UTF8);
    }

    // ── GitHub push ───────────────────────────────────────────────────────────

    /// <summary>
    /// Pushes the given JSON content to GitHub via the Contents API.
    /// Requires a PAT with repo write access.
    /// </summary>
    public static async Task<(bool Success, string? Error)> PushAsync(
        DbType db,
        string content,
        string githubToken,
        string commitMessage)
    {
        if (string.IsNullOrWhiteSpace(githubToken))
            return (false, "No GitHub token configured.");

        var apiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/contents/{RemotePath(db)}";

        try
        {
            // 1. Get current file SHA (required for update)
            string? sha = null;
            using (var req = new HttpRequestMessage(HttpMethod.Get, apiUrl))
            {
                req.Headers.UserAgent.ParseAdd("RenoDXdb-Editor/2.0");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", githubToken);
                req.Headers.Accept.ParseAdd("application/vnd.github+json");

                var resp = await _http.SendAsync(req);
                if (resp.IsSuccessStatusCode)
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("sha", out var shaEl))
                        sha = shaEl.GetString();
                }
            }

            // 2. PUT updated content
            var contentBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(content));
            var body = new Dictionary<string, object?>
            {
                ["message"] = commitMessage,
                ["content"] = contentBase64,
                ["branch"]  = Branch,
            };
            if (sha != null) body["sha"] = sha;

            var bodyJson = JsonSerializer.Serialize(body);
            using var putReq = new HttpRequestMessage(HttpMethod.Put, apiUrl)
            {
                Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
            };
            putReq.Headers.UserAgent.ParseAdd("RenoDXdb-Editor/2.0");
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
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    // ── Simple line-level diff ────────────────────────────────────────────────

    public record DiffLine(string Text, DiffKind Kind);
    public enum DiffKind { Same, Added, Removed }

    /// <summary>
    /// Produces a simple line-by-line diff (LCS-based) suitable for display.
    /// </summary>
    public static List<DiffLine> ComputeDiff(string oldText, string newText)
    {
        var oldLines = oldText.Replace("\r\n", "\n").Split('\n');
        var newLines = newText.Replace("\r\n", "\n").Split('\n');

        // LCS table
        int m = oldLines.Length, n = newLines.Length;
        int[,] dp = new int[m + 1, n + 1];
        for (int i = m - 1; i >= 0; i--)
            for (int j = n - 1; j >= 0; j--)
                dp[i, j] = oldLines[i] == newLines[j]
                    ? dp[i + 1, j + 1] + 1
                    : Math.Max(dp[i + 1, j], dp[i, j + 1]);

        var result = new List<DiffLine>();
        int oi = 0, ni = 0;
        while (oi < m || ni < n)
        {
            if (oi < m && ni < n && oldLines[oi] == newLines[ni])
            {
                result.Add(new DiffLine(oldLines[oi], DiffKind.Same));
                oi++; ni++;
            }
            else if (ni < n && (oi >= m || dp[oi, ni + 1] >= dp[oi + 1, ni]))
            {
                result.Add(new DiffLine(newLines[ni], DiffKind.Added));
                ni++;
            }
            else
            {
                result.Add(new DiffLine(oldLines[oi], DiffKind.Removed));
                oi++;
            }
        }
        return result;
    }
}
