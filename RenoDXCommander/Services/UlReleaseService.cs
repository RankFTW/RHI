// UlReleaseService.cs -- GitHub releases of ReLimiter, for the per-game Version picker in the ReLimiter ⚙.
// A pinned version is downloaded to %LocalAppData%\RHI\downloads\framelimiter\pinned\<tag>\relimiter.addon64|32,
// separate from the global "latest" cache (framelimiter\relimiter.addon64) and its ul_meta.json.
// Pinned games are skipped by the update check, so Update All and automatic updates leave them alone.

using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace RenoDXCommander.Services;

/// <summary>A ReLimiter release and its addon downloads (Url32 is null before 32-bit builds existed, v3.3.1).</summary>
internal sealed record UlRelease(string Tag, DateTime Published, string? Url64, string? Url32)
{
    public string? UrlFor(bool is32Bit) => is32Bit ? Url32 : Url64;
}

internal static class UlReleaseService
{
    internal const string Repo = "RankFTW/ReLimiter";
    internal const string FileName64 = "relimiter.addon64";
    internal const string FileName32 = "relimiter.addon32";

    internal static readonly string PinnedRoot = Path.Combine(DownloadPaths.FrameLimiter, "pinned");

    // Fetched once per session (the ⚙ is reopened often); ETag-cached on top of that
    private static List<UlRelease>? _cached;
    private static readonly object _lock = new();

    /// <summary>Releases fetched earlier this session, or null.</summary>
    internal static List<UlRelease>? Cached { get { lock (_lock) return _cached; } }

    /// <summary>ReLimiter's releases, newest first. Null when they can't be fetched.</summary>
    internal static async Task<List<UlRelease>?> ListAsync()
    {
        try
        {
            var cache = App.Services.GetRequiredService<GitHubETagCache>();
            var json = await cache.GetWithETagAsync(App.Services.GetRequiredService<HttpClient>(),
                $"https://api.github.com/repos/{Repo}/releases?per_page=100").ConfigureAwait(false);
            if (json == null) return Cached;
            var list = ParseReleases(json);
            lock (_lock) _cached = list;
            return list;
        }
        catch (HttpRequestException ex)
        {
            CrashReporter.Log($"[UlReleaseService.ListAsync] {ex.Message}");
            return Cached;
        }
    }

    /// <summary>
    /// Releases with at least one ReLimiter addon, newest by publish date first. Drafts and prereleases
    /// are skipped. Pure (unit tested).
    /// </summary>
    internal static List<UlRelease> ParseReleases(string json)
    {
        var list = new List<UlRelease>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
            foreach (var rel in doc.RootElement.EnumerateArray())
            {
                if (rel.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True) continue;
                if (rel.TryGetProperty("prerelease", out var p) && p.ValueKind == JsonValueKind.True) continue;
                var tag = rel.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
                if (string.IsNullOrWhiteSpace(tag)) continue;
                if (!rel.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) continue;
                string? url64 = null, url32 = null;
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                    var url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                    if (name == null || url == null || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) continue;
                    if (name.Equals(FileName64, StringComparison.OrdinalIgnoreCase)) url64 = url;
                    else if (name.Equals(FileName32, StringComparison.OrdinalIgnoreCase)) url32 = url;
                }
                if (url64 == null && url32 == null) continue;
                var published = rel.TryGetProperty("published_at", out var pa) && pa.ValueKind == JsonValueKind.String
                    && DateTime.TryParse(pa.GetString(), CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt)
                    ? dt : DateTime.MinValue;
                list.Add(new UlRelease(tag!.Trim(), published, url64, url32));
            }
        }
        catch (JsonException ex) { CrashReporter.Log($"[UlReleaseService.ParseReleases] {ex.Message}"); }
        return list.OrderByDescending(r => r.Published).ToList();
    }

    /// <summary>A tag as a folder name (letters, digits, . - _ only). Pure.</summary>
    internal static string SafeTag(string tag)
    {
        var chars = tag.Trim().Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_').ToArray();
        var name = new string(chars).Trim('.');
        return name.Length == 0 ? "_" : name.Length > 60 ? name[..60] : name;
    }

    /// <summary>Where a pinned version's addon is kept.</summary>
    internal static string PinnedPath(string tag, bool is32Bit) =>
        Path.Combine(PinnedRoot, SafeTag(tag), is32Bit ? FileName32 : FileName64);

    /// <summary>
    /// The pinned version's addon, downloading it first if needed. Returns (path, null) or (null, error).
    /// </summary>
    internal static async Task<(string? Path, string? Error)> EnsurePinnedAsync(string tag, bool is32Bit,
        IProgress<(string msg, double pct)>? progress = null, CancellationToken ct = default)
    {
        var dest = PinnedPath(tag, is32Bit);
        if (File.Exists(dest) && new FileInfo(dest).Length > 0)
        {
            progress?.Report(("Installing from cache...", 50));
            return (dest, null);
        }

        var releases = Cached ?? await ListAsync().ConfigureAwait(false);
        var rel = releases?.FirstOrDefault(r => string.Equals(r.Tag, tag, StringComparison.OrdinalIgnoreCase));
        if (rel == null) return (null, $"ReLimiter {tag} wasn't found on GitHub.");
        var url = rel.UrlFor(is32Bit);
        if (url == null) return (null, $"ReLimiter {tag} has no {(is32Bit ? "32-bit" : "64-bit")} build.");

        var tmp = dest + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            progress?.Report(($"Downloading ReLimiter {tag}...", 0));
            var http = App.Services.GetRequiredService<HttpClient>();
            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return (null, $"Download failed (HTTP {(int)resp.StatusCode}).");
            var total = resp.Content.Headers.ContentLength ?? -1L;
            long done = 0;
            var buf = new byte[1024 * 1024];
            await using (var net = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var file = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true))
            {
                int read;
                while ((read = await net.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
                {
                    await file.WriteAsync(buf.AsMemory(0, read), ct).ConfigureAwait(false);
                    done += read;
                    if (total > 0) progress?.Report(($"Downloading ReLimiter {tag}... {done / 1024} KB", (double)done / total * 100));
                }
            }
            if (new FileInfo(tmp).Length == 0) return (null, "The download was empty.");
            File.Move(tmp, dest, overwrite: true);
            CrashReporter.Log($"[UlReleaseService.EnsurePinnedAsync] Downloaded {tag} ({(is32Bit ? "32" : "64")}-bit) to '{dest}'");
            return (dest, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or TaskCanceledException)
        {
            CrashReporter.Log($"[UlReleaseService.EnsurePinnedAsync] {tag}: {ex.Message}");
            try { if (File.Exists(tmp)) File.Delete(tmp); }
            catch (Exception ex2) when (ex2 is IOException or UnauthorizedAccessException)
            { CrashReporter.Log($"[UlReleaseService.EnsurePinnedAsync] Temp cleanup — {ex2.Message}"); }
            return (null, ex.Message);
        }
    }
}
