// OsForkService.cs -- recommended OptiScaler forks from the manifest (osForks). Each appears as an OptiScaler
// version ("Fork:<id>") and, unlike custom builds, is kept up to date from its GitHub releases:
//   * releases are listed (not /releases/latest, which skips the prereleases most forks publish), drafts
//     skipped, newest by publish date first; the first release with a usable asset wins;
//   * the asset is the manifest's assetPattern match, else the best-ranked .7z / .zip (prefers "optiscaler"
//     in the name, skips "Optional-*", forwarder, troubleshooting and source archives);
//   * staged in %LocalAppData%\RHI\optiscaler-forks\<id>\build\ with fork.json (definition + staged tag),
//     so a fork keeps working offline and before the manifest loads.
// Download / unpack reuse OsCustomBuildService. Plan: docs/custom-optiscaler-builds-plan.md.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>A recommended fork: its manifest definition plus what's staged.</summary>
internal sealed record OsFork(string Id, ManifestOsFork Definition, string Dir, string? StagedTag, string? DllVersion)
{
    public string Variant => OsForkService.Prefix + Id;
    public string Label => string.IsNullOrWhiteSpace(Definition.Label) ? Id : Definition.Label!;
    public bool IsStaged => File.Exists(Path.Combine(Dir, "OptiScaler.dll"));
    /// <summary>OptiScaler 10 layout. Unknown until staged: assumed yes (every current fork is).</summary>
    public bool IsOs10 => !IsStaged || Directory.Exists(Path.Combine(Dir, "OptiScaler"));
    public bool NeedsNrDll => Definition.NeedsNrDll == true;
    public string? RepoUrl => string.IsNullOrWhiteSpace(Definition.Repo) ? null : "https://github.com/" + Definition.Repo;
}

internal static class OsForkService
{
    public const string Prefix = "Fork:";
    private const string MetaFile = "fork.json";

    internal static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "optiscaler-forks");

    private static readonly Regex IdPattern = new("^[a-z0-9][a-z0-9-]{0,39}$", RegexOptions.Compiled);
    private static readonly Regex RepoPattern = new(@"^[A-Za-z0-9-]+/[A-Za-z0-9._-]+$", RegexOptions.Compiled);

    private static readonly object Gate = new();
    private static Dictionary<string, ManifestOsFork> _definitions = new(StringComparer.Ordinal);
    private static List<OsFork>? _forks;
    private static readonly ConcurrentDictionary<string, bool> UpdateFlags = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> StageLocks = new(StringComparer.Ordinal);

    private sealed class Meta
    {
        public ManifestOsFork? Definition { get; set; }
        public string? Tag { get; set; }
        public string? Asset { get; set; }
        public DateTime? PublishedUtc { get; set; }
        public string? DllVersion { get; set; }
    }

    public static bool IsFork(string? variant) =>
        variant != null && variant.StartsWith(Prefix, StringComparison.Ordinal);

    // ── Definitions ───────────────────────────────────────────────────────────

    /// <summary>Called whenever a manifest is loaded (cached or fresh). Null keeps what's known.</summary>
    internal static void SetDefinitions(Dictionary<string, ManifestOsFork>? forks)
    {
        if (forks == null) return;
        var valid = new Dictionary<string, ManifestOsFork>(StringComparer.Ordinal);
        foreach (var (id, def) in forks)
        {
            if (!IdPattern.IsMatch(id) || def?.Repo == null || !RepoPattern.IsMatch(def.Repo))
            {
                CrashReporter.Log($"[OsForkService.SetDefinitions] Skipping invalid fork '{id}'");
                continue;
            }
            valid[id] = def;
        }
        lock (Gate) { _definitions = valid; _forks = null; }
    }

    /// <summary>Forks to list: the manifest's (not disabled), plus staged ones the manifest no longer has.</summary>
    internal static IReadOnlyList<OsFork> All
    {
        get
        {
            lock (Gate)
            {
                if (_forks != null) return _forks;
                var list = new List<OsFork>();
                var staged = ReadStaged();
                foreach (var (id, def) in _definitions)
                {
                    if (def.Disabled == true) continue;
                    staged.TryGetValue(id, out var meta);
                    list.Add(Make(id, def, meta));
                }
                // Staged but gone from the manifest (or the manifest isn't loaded yet): keep it usable
                foreach (var (id, meta) in staged)
                    if (!_definitions.ContainsKey(id) && meta.Definition != null)
                        list.Add(Make(id, meta.Definition, meta));
                _forks = list.OrderBy(f => f.Label, StringComparer.OrdinalIgnoreCase).ToList();
                return _forks;
            }
        }
    }

    internal static OsFork? Get(string? variant)
    {
        if (!IsFork(variant)) return null;
        var id = variant![Prefix.Length..];
        var listed = All.FirstOrDefault(f => f.Id == id);
        if (listed != null) return listed;
        // Disabled in the manifest but installed somewhere: still usable for updates / uninstall
        lock (Gate)
        {
            if (_definitions.TryGetValue(id, out var def))
                return Make(id, def, ReadStaged().GetValueOrDefault(id));
        }
        return null;
    }

    private static OsFork Make(string id, ManifestOsFork def, Meta? meta) =>
        new(id, def, Path.Combine(Root, id, "build"), meta?.Tag, meta?.DllVersion);

    private static Dictionary<string, Meta> ReadStaged()
    {
        var result = new Dictionary<string, Meta>(StringComparer.Ordinal);
        try
        {
            if (!Directory.Exists(Root)) return result;
            foreach (var dir in Directory.GetDirectories(Root))
            {
                var id = Path.GetFileName(dir);
                if (!IdPattern.IsMatch(id)) continue;
                var path = Path.Combine(dir, MetaFile);
                if (!File.Exists(path)) continue;
                try
                {
                    var meta = JsonSerializer.Deserialize<Meta>(File.ReadAllText(path));
                    if (meta != null) result[id] = meta;
                }
                catch (JsonException ex) { CrashReporter.Log($"[OsForkService.ReadStaged] {id}: {ex.Message}"); }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[OsForkService.ReadStaged] {ex.Message}");
        }
        return result;
    }

    // ── Updates ───────────────────────────────────────────────────────────────

    public static bool HasUpdate(string? variant) =>
        Get(variant) is { } f && UpdateFlags.TryGetValue(f.Id, out var u) && u;

    internal sealed record Release(string Tag, DateTime Published, string AssetName, string AssetUrl);

    /// <summary>Newest release with a usable asset, or null (offline, rate limited, nothing matches).</summary>
    private static async Task<Release?> FindLatestAsync(OsFork fork)
    {
        var url = $"https://api.github.com/repos/{fork.Definition.Repo}/releases?per_page=20";
        string? json;
        try
        {
            var cache = App.Services.GetRequiredService<GitHubETagCache>();
            json = await cache.GetWithETagAsync(App.Services.GetRequiredService<HttpClient>(), url).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) { CrashReporter.Log($"[OsForkService.FindLatestAsync] {fork.Id}: {ex.Message}"); return null; }
        if (json == null) return null;
        return PickRelease(json, fork.Definition);
    }

    /// <summary>
    /// Picks the newest release (by publish date) that has a usable asset. Pure (unit tested).
    /// </summary>
    internal static Release? PickRelease(string json, ManifestOsFork def)
    {
        Regex? pattern = null;
        if (!string.IsNullOrWhiteSpace(def.AssetPattern))
        {
            try { pattern = new Regex(def.AssetPattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)); }
            catch (ArgumentException ex) { CrashReporter.Log($"[OsForkService.PickRelease] Bad assetPattern — {ex.Message}"); }
        }
        bool prereleases = def.IncludePrereleases ?? true;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            var candidates = new List<Release>();
            foreach (var rel in doc.RootElement.EnumerateArray())
            {
                if (rel.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True) continue;
                if (!prereleases && rel.TryGetProperty("prerelease", out var p) && p.ValueKind == JsonValueKind.True) continue;
                var tag = rel.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
                if (string.IsNullOrEmpty(tag)) continue;
                var published = rel.TryGetProperty("published_at", out var pa) && pa.ValueKind == JsonValueKind.String
                    && DateTime.TryParse(pa.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt)
                    ? dt : DateTime.MinValue;
                if (!rel.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) continue;
                (string Name, string Url, int Score, DateTime Updated)? best = null;
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                    var dl = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                    if (name == null || dl == null || !dl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) continue;
                    int score = pattern != null ? (pattern.IsMatch(name) ? 100 : 0) : ScoreAsset(name);
                    // Some forks keep many dated builds in one release: on a tie the newest upload wins
                    var updated = a.TryGetProperty("updated_at", out var ua) && ua.ValueKind == JsonValueKind.String
                        && DateTime.TryParse(ua.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var udt)
                        ? udt : DateTime.MinValue;
                    if (score > 0 && (best == null || score > best.Value.Score || (score == best.Value.Score && updated > best.Value.Updated)))
                        best = (name, dl, score, updated);
                }
                if (best != null) candidates.Add(new Release(tag!, published, best.Value.Name, best.Value.Url));
            }
            return candidates.OrderByDescending(r => r.Published).FirstOrDefault();
        }
        catch (JsonException ex) { CrashReporter.Log($"[OsForkService.PickRelease] {ex.Message}"); return null; }
    }

    /// <summary>How likely an asset is the main OptiScaler build (0 = not usable).</summary>
    internal static int ScoreAsset(string name)
    {
        var n = name.ToLowerInvariant();
        if (!(n.EndsWith(".7z") || n.EndsWith(".zip"))) return 0;
        if (n.StartsWith("optional") || n.Contains("forwarder") || n.Contains("troubleshoot")
            || n.Contains("source") || n.Contains("symbols") || n.Contains("pdb")) return 0;
        int score = 10;
        if (n.Contains("optiscaler")) score += 20;
        if (n.EndsWith(".7z")) score += 1; // official layout; ties go to 7z
        return score;
    }

    /// <summary>Checks the fork's releases and sets its update flag. Returns the flag.</summary>
    internal static async Task<bool> CheckForUpdateAsync(string variant)
    {
        if (Get(variant) is not { } fork) return false;
        var latest = await FindLatestAsync(fork).ConfigureAwait(false);
        if (latest == null) return HasUpdate(variant);
        bool update = fork.IsStaged && !string.Equals(fork.StagedTag, latest.Tag, StringComparison.Ordinal);
        UpdateFlags[fork.Id] = update;
        CrashReporter.Log($"[OsForkService.CheckForUpdateAsync] {fork.Id}: staged={fork.StagedTag ?? "(none)"}, latest={latest.Tag}, update={update}");
        return update;
    }

    /// <summary>
    /// Makes sure the fork's newest build is staged (downloads when missing or out of date) and returns
    /// its folder, or null when there's nothing usable (offline with nothing staged keeps the old copy).
    /// </summary>
    internal static async Task<string?> EnsureStagedAsync(string variant, IProgress<(string message, double percent)>? progress, CancellationToken ct)
    {
        if (Get(variant) is not { } fork) return null;
        var gate = StageLocks.GetOrAdd(fork.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            fork = Get(variant)!;
            progress?.Report(($"Checking {fork.Label}...", 8));
            var latest = await FindLatestAsync(fork).ConfigureAwait(false);
            if (latest == null)
            {
                CrashReporter.Log($"[OsForkService.EnsureStagedAsync] {fork.Id}: no release found{(fork.IsStaged ? " — using the staged copy" : "")}");
                return fork.IsStaged ? fork.Dir : null;
            }
            if (fork.IsStaged && string.Equals(fork.StagedTag, latest.Tag, StringComparison.Ordinal))
            {
                UpdateFlags[fork.Id] = false;
                return fork.Dir;
            }

            progress?.Report(($"Downloading {fork.Label} {latest.Tag}...", 15));
            var work = Path.Combine(Path.GetTempPath(), "RHI", "osfork-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(work, "dl"));
                var (err, saved) = await OsCustomBuildService.DownloadAsync(new Uri(latest.AssetUrl),
                    Path.Combine(work, "dl", Path.GetFileName(latest.AssetName)), null, ct).ConfigureAwait(false);
                if (err != null)
                {
                    CrashReporter.Log($"[OsForkService.EnsureStagedAsync] {fork.Id}: {err}");
                    return fork.IsStaged ? fork.Dir : null;
                }
                progress?.Report(($"Unpacking {fork.Label}...", 40));
                var x = Path.Combine(work, "x");
                var exErr = await OsCustomBuildService.ExtractAsync(saved, x, ct).ConfigureAwait(false);
                var dll = exErr == null ? OsCustomBuildService.FindOptiScalerDll(x) : null;
                if (dll == null)
                {
                    CrashReporter.Log($"[OsForkService.EnsureStagedAsync] {fork.Id}: {exErr ?? "no OptiScaler.dll in " + latest.AssetName}");
                    return fork.IsStaged ? fork.Dir : null;
                }

                var forkDir = Path.Combine(Root, fork.Id);
                var tmp = forkDir + ".tmp";
                if (Directory.Exists(tmp)) Directory.Delete(tmp, recursive: true);
                OsCustomBuildService.CopyTree(Path.GetDirectoryName(dll)!, Path.Combine(tmp, "build"));
                var info = FileVersionInfo.GetVersionInfo(Path.Combine(tmp, "build", "OptiScaler.dll"));
                var meta = new Meta
                {
                    Definition = fork.Definition,
                    Tag = latest.Tag,
                    Asset = latest.AssetName,
                    PublishedUtc = latest.Published,
                    DllVersion = string.IsNullOrWhiteSpace(info.ProductVersion) ? info.FileVersion : info.ProductVersion,
                };
                File.WriteAllText(Path.Combine(tmp, MetaFile), JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));
                if (Directory.Exists(forkDir)) Directory.Delete(forkDir, recursive: true);
                Directory.Move(tmp, forkDir);
                UpdateFlags[fork.Id] = false;
                lock (Gate) _forks = null;
                CrashReporter.Log($"[OsForkService.EnsureStagedAsync] {fork.Id}: staged {latest.Tag} ({latest.AssetName}), was {fork.StagedTag ?? "(none)"}");
                if (fork.StagedTag != null)
                {
                    try
                    {
                        App.Services.GetRequiredService<IUpdateLogService>().Record(new UpdateLogEntry
                        {
                            Timestamp = DateTime.UtcNow,
                            Category = "OptiScaler Fork",
                            ComponentName = $"OptiScaler ({fork.Label})",
                            OldVersion = fork.StagedTag,
                            NewVersion = latest.Tag,
                        });
                    }
                    catch (InvalidOperationException ex) { CrashReporter.Log($"[OsForkService.EnsureStagedAsync] Update log — {ex.Message}"); }
                }
                return forkDir + Path.DirectorySeparatorChar + "build";
            }
            finally
            {
                try { if (Directory.Exists(work)) Directory.Delete(work, recursive: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { CrashReporter.Log($"[OsForkService.EnsureStagedAsync] Temp cleanup — {ex.Message}"); }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or InvalidDataException)
        {
            CrashReporter.Log($"[OsForkService.EnsureStagedAsync] {variant}: {ex.Message}");
            return Get(variant) is { IsStaged: true } f ? f.Dir : null;
        }
        finally { gate.Release(); }
    }
}
