// OsReleaseService.cs -- GitHub releases for the OptiScaler versions that come from a release list: Stable,
// DLSS NR, F5 and the recommended forks (Nightly has its own dated builds; custom builds have none).
// Used for:
//   * the per-game Release picker in the OptiScaler ⚙ (pin a game to an older release), staged in
//     %LocalAppData%\RHI\optiscaler-pinned\<version>\<tag>\build\
//   * the ⚙'s "Latest" line and Check now
//   * OsForkService (staging a fork's newest release).
// Releases are listed (not /releases/latest), drafts skipped, newest by publish date first.

using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>A release with the asset RHI would install from it.</summary>
internal sealed record OsRelease(string Tag, DateTime Published, string AssetName, string AssetUrl);

/// <summary>Where a version's releases come from.</summary>
internal sealed record OsReleaseSource(string Repo, string? AssetPattern, bool IncludePrereleases);

internal static class OsReleaseService
{
    internal static readonly string PinnedRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "optiscaler-pinned");

    /// <summary>The release source for a version, or null (Nightly, custom builds).</summary>
    internal static OsReleaseSource? SourceFor(string? variant) => variant switch
    {
        OsVariants.Stable => new("optiscaler/OptiScaler", null, false),
        // The built-in DLSS NR download takes the first zip, which is the -rtx40-mfg build
        OsVariants.DlssNr => new("wilsjo2/OptiScaler-DLSSNR-PreSR-Multipass", @"-rtx40-mfg\.zip$", true),
        OsVariants.F5 => new("janblade/OptiScaler-F5-DLSSNR-Multipass", null, true),
        _ when OsForkService.Get(variant) is { } f => new(f.Definition.Repo!, f.Definition.AssetPattern, f.Definition.IncludePrereleases ?? true),
        _ => null,
    };

    /// <summary>True when a game on this version can be pinned to a release (Nightly has its own build picker).</summary>
    internal static bool SupportsPinning(string? variant) => SourceFor(variant) != null;

    // ── Listing ───────────────────────────────────────────────────────────────

    /// <summary>The version's releases, newest first. Null when they can't be fetched.</summary>
    internal static async Task<List<OsRelease>?> ListAsync(string? variant)
    {
        if (SourceFor(variant) is not { } src) return null;
        var json = await FetchAsync(src.Repo).ConfigureAwait(false);
        return json == null ? null : ListReleases(json, src.AssetPattern, src.IncludePrereleases);
    }

    internal static async Task<string?> FetchAsync(string repo)
    {
        try
        {
            var cache = App.Services.GetRequiredService<GitHubETagCache>();
            return await cache.GetWithETagAsync(App.Services.GetRequiredService<HttpClient>(),
                $"https://api.github.com/repos/{repo}/releases?per_page=30").ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            CrashReporter.Log($"[OsReleaseService.FetchAsync] {repo}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Releases that have a usable asset, newest by publish date first. An asset matching
    /// <paramref name="assetPattern"/> is preferred; otherwise the best by <see cref="ScoreAsset"/>, ties to
    /// the newest upload. Pure (unit tested).
    /// </summary>
    internal static List<OsRelease> ListReleases(string json, string? assetPattern, bool includePrereleases)
    {
        Regex? pattern = null;
        if (!string.IsNullOrWhiteSpace(assetPattern))
        {
            try { pattern = new Regex(assetPattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)); }
            catch (ArgumentException ex) { CrashReporter.Log($"[OsReleaseService.ListReleases] Bad assetPattern — {ex.Message}"); }
        }
        var list = new List<OsRelease>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
            foreach (var rel in doc.RootElement.EnumerateArray())
            {
                if (rel.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True) continue;
                if (!includePrereleases && rel.TryGetProperty("prerelease", out var p) && p.ValueKind == JsonValueKind.True) continue;
                var tag = rel.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
                if (string.IsNullOrEmpty(tag)) continue;
                var published = ParseDate(rel, "published_at");
                if (!rel.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) continue;
                (string Name, string Url, int Score, DateTime Updated)? best = null;
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                    var dl = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                    if (name == null || dl == null || !dl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) continue;
                    int score = ScoreAsset(name);
                    if (score > 0 && pattern != null && pattern.IsMatch(name)) score += 100;
                    var updated = ParseDate(a, "updated_at");
                    if (score > 0 && (best == null || score > best.Value.Score || (score == best.Value.Score && updated > best.Value.Updated)))
                        best = (name, dl, score, updated);
                }
                if (best != null) list.Add(new OsRelease(tag!, published, best.Value.Name, best.Value.Url));
            }
        }
        catch (JsonException ex) { CrashReporter.Log($"[OsReleaseService.ListReleases] {ex.Message}"); }
        return list.OrderByDescending(r => r.Published).ToList();
    }

    private static DateTime ParseDate(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
        && DateTime.TryParse(v.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt)
            ? dt : DateTime.MinValue;

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

    /// <summary>"v0.1.37" and "0.1.37" are the same version (the built-ins store tags without the v).</summary>
    internal static bool SameVersion(string? a, string? b) =>
        a != null && b != null && string.Equals(a.Trim().TrimStart('v', 'V'), b.Trim().TrimStart('v', 'V'), StringComparison.OrdinalIgnoreCase);

    // ── Staging ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Downloads a release's asset and puts the build in <paramref name="slotDir"/>\build (replacing what's
    /// there). <paramref name="writeMeta"/> gets the new slot folder (not yet in place) and the DLL version.
    /// Returns an error, or null.
    /// </summary>
    internal static async Task<string?> StageAsync(OsRelease release, string slotDir, Action<string, string?> writeMeta,
        IProgress<(string message, double percent)>? progress, string label, CancellationToken ct)
    {
        progress?.Report(($"Downloading {label} {release.Tag}...", 15));
        var work = Path.Combine(Path.GetTempPath(), "RHI", "osrelease-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(work, "dl"));
            var (err, saved) = await OsCustomBuildService.DownloadAsync(new Uri(release.AssetUrl),
                Path.Combine(work, "dl", Path.GetFileName(release.AssetName)), null, ct).ConfigureAwait(false);
            if (err != null) return err;
            progress?.Report(($"Unpacking {label}...", 40));
            var x = Path.Combine(work, "x");
            var exErr = await OsCustomBuildService.ExtractAsync(saved, x, ct).ConfigureAwait(false);
            if (exErr != null) return exErr;
            var dll = OsCustomBuildService.FindOptiScalerDll(x);
            if (dll == null) return $"No OptiScaler.dll in {release.AssetName}.";

            var tmp = slotDir.TrimEnd('\\', '/') + ".tmp";
            if (Directory.Exists(tmp)) Directory.Delete(tmp, recursive: true);
            OsCustomBuildService.CopyTree(Path.GetDirectoryName(dll)!, Path.Combine(tmp, "build"));
            var info = FileVersionInfo.GetVersionInfo(Path.Combine(tmp, "build", "OptiScaler.dll"));
            writeMeta(tmp, string.IsNullOrWhiteSpace(info.ProductVersion) ? info.FileVersion : info.ProductVersion);
            if (Directory.Exists(slotDir)) Directory.Delete(slotDir, recursive: true);
            Directory.CreateDirectory(Path.GetDirectoryName(slotDir.TrimEnd('\\', '/'))!);
            Directory.Move(tmp, slotDir);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or InvalidDataException)
        {
            CrashReporter.Log($"[OsReleaseService.StageAsync] {label} {release.Tag}: {ex.Message}");
            return ex.Message;
        }
        finally
        {
            try { if (Directory.Exists(work)) Directory.Delete(work, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { CrashReporter.Log($"[OsReleaseService.StageAsync] Temp cleanup — {ex.Message}"); }
        }
    }

    // ── Pinned releases ───────────────────────────────────────────────────────

    private static string SafeName(string s)
    {
        var chars = s.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_').ToArray();
        var name = new string(chars).Trim('.');
        return name.Length == 0 ? "_" : name.Length > 60 ? name[..60] : name;
    }

    private static string PinnedSlot(string variant, string tag) =>
        Path.Combine(PinnedRoot, SafeName(variant.Replace(':', '-')), SafeName(tag));

    /// <summary>The staged build folder for a pinned release, or null when it isn't downloaded.</summary>
    internal static string? PinnedDir(string variant, string tag)
    {
        var dir = Path.Combine(PinnedSlot(variant, tag), "build");
        return File.Exists(Path.Combine(dir, "OptiScaler.dll")) ? dir : null;
    }

    /// <summary>Downloads the pinned release if needed and returns its build folder, or null with an error logged.</summary>
    internal static async Task<string?> EnsurePinnedAsync(string variant, string tag,
        IProgress<(string message, double percent)>? progress, CancellationToken ct)
    {
        if (PinnedDir(variant, tag) is { } ready) return ready;
        var releases = await ListAsync(variant).ConfigureAwait(false);
        var release = releases?.FirstOrDefault(r => string.Equals(r.Tag, tag, StringComparison.Ordinal))
                   ?? releases?.FirstOrDefault(r => SameVersion(r.Tag, tag));
        if (release == null)
        {
            CrashReporter.Log($"[OsReleaseService.EnsurePinnedAsync] {variant} {tag}: release not found{(releases == null ? " (couldn't list releases)" : "")}");
            return null;
        }
        var slot = PinnedSlot(variant, tag);
        var err = await StageAsync(release, slot, (tmp, ver) =>
            File.WriteAllText(Path.Combine(tmp, "pin.json"), JsonSerializer.Serialize(new { variant, tag = release.Tag, asset = release.AssetName, dllVersion = ver })),
            progress, OsVariants.Label(variant), ct).ConfigureAwait(false);
        if (err != null)
        {
            CrashReporter.Log($"[OsReleaseService.EnsurePinnedAsync] {variant} {tag}: {err}");
            return null;
        }
        CrashReporter.Log($"[OsReleaseService.EnsurePinnedAsync] Staged {variant} {release.Tag} ({release.AssetName})");
        return PinnedDir(variant, tag);
    }
}
