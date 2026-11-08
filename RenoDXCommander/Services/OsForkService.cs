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

    /// <summary>Newest release with a usable asset, or null (offline, rate limited, nothing matches).</summary>
    private static async Task<OsRelease?> FindLatestAsync(OsFork fork)
    {
        var json = await OsReleaseService.FetchAsync(fork.Definition.Repo!).ConfigureAwait(false);
        return json == null ? null : PickRelease(json, fork.Definition);
    }

    /// <summary>The newest release (by publish date) with a usable asset. Pure (unit tested).</summary>
    internal static OsRelease? PickRelease(string json, ManifestOsFork def) =>
        OsReleaseService.ListReleases(json, def.AssetPattern, def.IncludePrereleases ?? true).FirstOrDefault();

    internal static int ScoreAsset(string name) => OsReleaseService.ScoreAsset(name);

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

            var forkDir = Path.Combine(Root, fork.Id);
            var err = await OsReleaseService.StageAsync(latest, forkDir, (tmp, ver) =>
                File.WriteAllText(Path.Combine(tmp, MetaFile), JsonSerializer.Serialize(new Meta
                {
                    Definition = fork.Definition,
                    Tag = latest.Tag,
                    Asset = latest.AssetName,
                    PublishedUtc = latest.Published,
                    DllVersion = ver,
                }, new JsonSerializerOptions { WriteIndented = true })), progress, fork.Label, ct).ConfigureAwait(false);
            if (err != null)
            {
                CrashReporter.Log($"[OsForkService.EnsureStagedAsync] {fork.Id}: {err}");
                return fork.IsStaged ? fork.Dir : null;
            }
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
            return Path.Combine(forkDir, "build");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or InvalidDataException)
        {
            CrashReporter.Log($"[OsForkService.EnsureStagedAsync] {variant}: {ex.Message}");
            return Get(variant) is { IsStaged: true } f ? f.Dir : null;
        }
        finally { gate.Release(); }
    }
}
