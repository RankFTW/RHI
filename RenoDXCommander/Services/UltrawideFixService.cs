using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Fetches ultrawide fix data from three sources, caches to disk with a
/// 24-hour TTL, builds normalized-name lookup dictionaries, and resolves
/// per-game URLs with tiered priority:
///   1. Manifest uwFixUrlOverrides (highest)
///   2. Lyall (Codeberg API)
///   3. RoseTheFlower (GitHub raw README)
///   4. p1xel8ted (GitHub raw README)
/// </summary>
public partial class UltrawideFixService : IUltrawideFixService
{
    private readonly HttpClient _http;
    private readonly IGameDetectionService _gameDetection;

    // ── Source URLs ────────────────────────────────────────────────────────────
    private const string LyallApiUrl   = "https://codeberg.org/api/v1/users/Lyall/repos?limit=50";
    private const string RoseReadmeUrl = "https://raw.githubusercontent.com/RoseTheFlower/UltrawideIndex/main/README.md";
    private const string P1xelReadmeUrl = "https://raw.githubusercontent.com/p1xel8ted/UltrawideFixes/main/README.md";

    private const string SkipRepoName = "UltrawidePatches";

    // ── Cache paths ───────────────────────────────────────────────────────────
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI");
    private static readonly string LyallCachePath = Path.Combine(CacheDir, "lyall_repos.json");
    private static readonly string RoseCachePath  = Path.Combine(CacheDir, "rose_uw_readme.md");
    private static readonly string P1xelCachePath = Path.Combine(CacheDir, "p1xel_uw_readme.md");

    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    // ── Lookup dictionaries (normalized game name → URL) ──────────────────────
    private Dictionary<string, string> _lyallLookup  = new(StringComparer.Ordinal);
    private Dictionary<string, string> _roseLookup   = new(StringComparer.Ordinal);
    private Dictionary<string, string> _p1xelLookup  = new(StringComparer.Ordinal);

    // ── Regex patterns ────────────────────────────────────────────────────────
    [GeneratedRegex(@"(?:An ASI plugin|A fix|A BepInEx plugin) for (.+?) that ", RegexOptions.IgnoreCase)]
    private static partial Regex LyallDescriptionRegex();

    [GeneratedRegex(@"\s*\(and [^)]*\)", RegexOptions.IgnoreCase)]
    private static partial Regex ParentheticalAndRegex();

    // Rose: "# Game Name ultrawide..." or "# Game Name widescreen..." etc.
    [GeneratedRegex(@"^#\s+(.+?)(?:\s+(?:ultrawide|widescreen|super ultrawide|multi-monitor|improvements|FOV|black bars|resolutions|better ultrawide|cutscenes|field of view))", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex RoseHeaderRegex();

    // Rose: release tag link "releases/tag/tagname"
    [GeneratedRegex(@"releases/tag/(\S+)\)")]
    private static partial Regex RoseReleaseTagRegex();

    // Rose: external link (e.g. Nexus) — "Download page and instructions](url)"
    [GeneratedRegex(@"\[Download page and instructions\]\(([^)]+)\)")]
    private static partial Regex RoseDownloadLinkRegex();

    // p1xel: "## [Game Name](https://github.com/.../releases/tag/Tag)"
    [GeneratedRegex(@"^##\s+\[([^\]]+)\]\((https://github\.com/p1xel8ted/UltrawideFixes/releases/tag/[^)]+)\)", RegexOptions.Multiline)]
    private static partial Regex P1xelHeaderRegex();

    public UltrawideFixService(HttpClient http, IGameDetectionService gameDetection)
    {
        _http = http;
        _gameDetection = gameDetection;
    }

    /// <inheritdoc />
    public async Task InitAsync()
    {
        // Fetch all three sources in parallel
        var lyallTask = Task.Run(async () =>
        {
            try { await InitLyallAsync(); }
            catch (Exception ex) { CrashReporter.Log($"[UltrawideFixService] Lyall init failed — {ex.Message}"); }
        });
        var roseTask = Task.Run(async () =>
        {
            try { await InitRoseAsync(); }
            catch (Exception ex) { CrashReporter.Log($"[UltrawideFixService] Rose init failed — {ex.Message}"); }
        });
        var p1xelTask = Task.Run(async () =>
        {
            try { await InitP1xelAsync(); }
            catch (Exception ex) { CrashReporter.Log($"[UltrawideFixService] p1xel init failed — {ex.Message}"); }
        });

        await Task.WhenAll(lyallTask, roseTask, p1xelTask);

        var total = _lyallLookup.Count + _roseLookup.Count + _p1xelLookup.Count;
        CrashReporter.Log($"[UltrawideFixService.InitAsync] Total: {total} entries (Lyall={_lyallLookup.Count}, Rose={_roseLookup.Count}, p1xel={_p1xelLookup.Count})");
    }

    /// <inheritdoc />
    public string? ResolveUrl(string gameName, RemoteManifest? manifest)
    {
        try
        {
            var normalized = _gameDetection.NormalizeName(gameName);
            if (string.IsNullOrEmpty(normalized)) return null;

            // 1. Manifest override (highest priority)
            if (manifest?.UwFixUrlOverrides != null
                && manifest.UwFixUrlOverrides.TryGetValue(gameName, out var overrideUrl))
                return overrideUrl;

            // 2. Lyall
            if (_lyallLookup.TryGetValue(normalized, out var lyallUrl))
                return lyallUrl;

            // 3. RoseTheFlower
            if (_roseLookup.TryGetValue(normalized, out var roseUrl))
                return roseUrl;

            // 4. p1xel8ted
            if (_p1xelLookup.TryGetValue(normalized, out var p1xelUrl))
                return p1xelUrl;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[UltrawideFixService.ResolveUrl] Failed for '{gameName}' — {ex.Message}");
        }

        return null;
    }

    /// <inheritdoc />
    public string? ResolveSource(string gameName, RemoteManifest? manifest)
    {
        try
        {
            var normalized = _gameDetection.NormalizeName(gameName);
            if (string.IsNullOrEmpty(normalized)) return null;

            if (manifest?.UwFixUrlOverrides != null
                && manifest.UwFixUrlOverrides.ContainsKey(gameName))
                return null; // manifest override — unknown source

            if (_lyallLookup.ContainsKey(normalized))
                return "Lyall";

            if (_roseLookup.ContainsKey(normalized))
                return "Rose";

            if (_p1xelLookup.ContainsKey(normalized))
                return "p1xel8ted";
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[UltrawideFixService.ResolveSource] Failed for '{gameName}' — {ex.Message}");
        }

        return null;
    }

    // ── Lyall (Codeberg API) ──────────────────────────────────────────────────

    private async Task InitLyallAsync()
    {
        var json = await FetchOrLoadCache(LyallApiUrl, LyallCachePath, "Lyall");
        if (json == null) return;

        try
        {
            var repos = JsonSerializer.Deserialize<List<CodebergRepo>>(json);
            if (repos == null) return;

            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            var descRegex = LyallDescriptionRegex();
            var parenRegex = ParentheticalAndRegex();

            foreach (var repo in repos)
            {
                if (string.Equals(repo.Name, SkipRepoName, StringComparison.OrdinalIgnoreCase)) continue;
                if (!repo.HasReleases) continue;
                if (string.IsNullOrEmpty(repo.Description) || string.IsNullOrEmpty(repo.HtmlUrl)) continue;

                var match = descRegex.Match(repo.Description);
                if (!match.Success) continue;

                var name = parenRegex.Replace(match.Groups[1].Value, "").Trim().Replace("**", "");
                if (string.IsNullOrEmpty(name)) continue;

                var key = _gameDetection.NormalizeName(name);
                if (!string.IsNullOrEmpty(key))
                    dict.TryAdd(key, repo.HtmlUrl);
            }

            _lyallLookup = dict;
            CrashReporter.Log($"[UltrawideFixService] Lyall: {dict.Count} entries");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[UltrawideFixService] Lyall parse failed — {ex.Message}");
        }
    }

    // ── RoseTheFlower (GitHub raw README) ─────────────────────────────────────

    private async Task InitRoseAsync()
    {
        var readme = await FetchOrLoadCache(RoseReadmeUrl, RoseCachePath, "Rose");
        if (readme == null) return;

        try
        {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            const string repoBase = "https://github.com/RoseTheFlower/UltrawideIndex/releases/tag/";

            // Split into sections by "# " headers
            var lines = readme.Split('\n');
            string? currentGameName = null;

            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd('\r');

                // Match "# Game Name ultrawide/widescreen/etc."
                if (line.StartsWith("# ") && !line.StartsWith("## "))
                {
                    var headerMatch = RoseHeaderRegex().Match(line);
                    if (headerMatch.Success)
                        currentGameName = headerMatch.Groups[1].Value.Trim();
                    else
                        currentGameName = null;
                    continue;
                }

                // Match "## Game Name" (sub-section in "Other solutions")
                if (line.StartsWith("## ") && !line.StartsWith("### "))
                {
                    var subHeader = line[3..].Trim();
                    // Strip trailing keywords
                    var subMatch = RoseHeaderRegex().Match("# " + subHeader);
                    currentGameName = subMatch.Success ? subMatch.Groups[1].Value.Trim() : subHeader;
                    continue;
                }

                // Look for download link in current section
                if (currentGameName != null)
                {
                    // Check for release tag link
                    var tagMatch = RoseReleaseTagRegex().Match(line);
                    if (tagMatch.Success)
                    {
                        var tag = tagMatch.Groups[1].Value;
                        var url = repoBase + tag;
                        var key = _gameDetection.NormalizeName(currentGameName);
                        if (!string.IsNullOrEmpty(key))
                            dict.TryAdd(key, url);
                        currentGameName = null;
                        continue;
                    }

                    // Check for external download link (e.g. Nexus)
                    var dlMatch = RoseDownloadLinkRegex().Match(line);
                    if (dlMatch.Success)
                    {
                        var url = dlMatch.Groups[1].Value;
                        // Convert relative links to absolute
                        if (url.StartsWith("/../../"))
                            url = "https://github.com/RoseTheFlower/UltrawideIndex/" + url.TrimStart('/');
                        else if (url.StartsWith("/../../../"))
                        {
                            // Links to separate repos: /../../../RepoName#readme
                            var repoName = url.Replace("/../../../", "").Replace("#readme", "");
                            url = $"https://github.com/RoseTheFlower/{repoName}";
                        }
                        var key = _gameDetection.NormalizeName(currentGameName);
                        if (!string.IsNullOrEmpty(key))
                            dict.TryAdd(key, url);
                        currentGameName = null;
                    }
                }
            }

            _roseLookup = dict;
            CrashReporter.Log($"[UltrawideFixService] Rose: {dict.Count} entries");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[UltrawideFixService] Rose parse failed — {ex.Message}");
        }
    }

    // ── p1xel8ted (GitHub raw README) ─────────────────────────────────────────

    private async Task InitP1xelAsync()
    {
        var readme = await FetchOrLoadCache(P1xelReadmeUrl, P1xelCachePath, "p1xel");
        if (readme == null) return;

        try
        {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            var regex = P1xelHeaderRegex();

            foreach (Match match in regex.Matches(readme))
            {
                var gameName = match.Groups[1].Value.Trim();
                var url = match.Groups[2].Value.Trim();

                if (string.IsNullOrEmpty(gameName) || string.IsNullOrEmpty(url)) continue;

                var key = _gameDetection.NormalizeName(gameName);
                if (!string.IsNullOrEmpty(key))
                    dict.TryAdd(key, url);
            }

            _p1xelLookup = dict;
            CrashReporter.Log($"[UltrawideFixService] p1xel: {dict.Count} entries");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[UltrawideFixService] p1xel parse failed — {ex.Message}");
        }
    }

    // ── Shared fetch/cache helper ─────────────────────────────────────────────

    private async Task<string?> FetchOrLoadCache(string url, string cachePath, string label)
    {
        // Check cache freshness
        bool cacheFresh = false;
        try
        {
            if (File.Exists(cachePath))
            {
                var lastWrite = File.GetLastWriteTimeUtc(cachePath);
                cacheFresh = (DateTime.UtcNow - lastWrite) < CacheTtl;
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[UltrawideFixService] {label} cache freshness check failed — {ex.Message}");
        }

        if (cacheFresh)
        {
            try
            {
                var cached = File.ReadAllText(cachePath);
                CrashReporter.Log($"[UltrawideFixService] {label} cache is fresh, skipping fetch");
                return cached;
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[UltrawideFixService] {label} cache read failed — {ex.Message}");
            }
        }

        // Fetch from network
        try
        {
            var content = await _http.GetStringAsync(url).ConfigureAwait(false);
            CrashReporter.Log($"[UltrawideFixService] {label} fetched from network");

            // Persist to cache
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
                await File.WriteAllTextAsync(cachePath, content).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[UltrawideFixService] {label} cache write failed — {ex.Message}");
            }

            return content;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[UltrawideFixService] {label} fetch failed — {ex.Message}");

            // Fallback to stale cache
            try
            {
                if (File.Exists(cachePath))
                    return File.ReadAllText(cachePath);
            }
            catch { }

            CrashReporter.Log($"[UltrawideFixService] {label} no cache available");
            return null;
        }
    }

    // ── Models ────────────────────────────────────────────────────────────────

    internal class CodebergRepo
    {
        [JsonPropertyName("name")]       public string? Name { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("html_url")]   public string? HtmlUrl { get; set; }
        [JsonPropertyName("has_releases")] public bool HasReleases { get; set; }
    }
}
