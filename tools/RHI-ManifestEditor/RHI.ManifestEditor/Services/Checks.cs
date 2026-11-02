using System.Text.RegularExpressions;
using RHI.ManifestEditor.Json;

namespace RHI.ManifestEditor.Services;

public enum Severity { Error, Warning }

public sealed record Issue(Severity Severity, string Message, string? Game = null);

/// <summary>
/// Same checks as tools/check-manifest.py (valid JSON, duplicate keys, keys that differ only
/// by case in the fields the app makes case-insensitive), plus conflicting per-game settings.
/// </summary>
public static class Checks
{
    // Fallback if the RHI source isn't next to the manifest (matches ManifestService.Normalize, Nov 2026).
    private static readonly string[] FallbackCaseInsensitive =
    {
        "authorDisplayNames", "displayCommanderGameInfo", "dllNameOverrides", "dlssPathOverrides", "donationUrls",
        "engineOverrides", "forceExternalOnly", "gacSymlinkGames", "gameNotes", "graphicsApiOverrides",
        "installPathOverrides", "installWarnings", "lumaGameInfo", "lumaGameNotes", "lumaNameOverrides",
        "optiScalerGameInfo", "optiScalerWikiNames", "reframeworkGameInfo", "relimiterGameInfo", "reshadeGameInfo",
        "snapshotOverrides", "streamlinePathOverrides", "wikiNameOverrides", "wikiStatusOverrides",
    };

    private static HashSet<string>? _ciFields;

    /// <summary>Fields ManifestService.Normalize() rebuilds case-insensitively (read from the RHI source when available).</summary>
    public static HashSet<string> CaseInsensitiveFields(string manifestPath)
    {
        if (_ciFields != null) return _ciFields;
        var set = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            var root = Path.GetDirectoryName(manifestPath)!;
            var service = Path.Combine(root, "RenoDXCommander", "Services", "ManifestService.cs");
            var model = Path.Combine(root, "RenoDXCommander", "Models", "RemoteManifest.cs");
            if (File.Exists(service) && File.Exists(model))
            {
                var svc = File.ReadAllText(service);
                int at = svc.IndexOf("static void Normalize(", StringComparison.Ordinal);
                var body = at >= 0 ? svc[at..] : svc;
                var props = Regex.Matches(body, @"m\.(\w+)\s*=\s*new Dictionary<.*StringComparer\.OrdinalIgnoreCase")
                    .Select(m => m.Groups[1].Value).ToHashSet();
                var names = new Dictionary<string, string>();
                foreach (Match m in Regex.Matches(File.ReadAllText(model), @"\[JsonPropertyName\(""([^""]+)""\)\]\s*public\s+[^\n]*?\s(\w+)\s*\{"))
                    names[m.Groups[2].Value] = m.Groups[1].Value;
                foreach (var p in props)
                    set.Add(names.TryGetValue(p, out var j) ? j : char.ToLowerInvariant(p[0]) + p[1..]);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or RegexMatchTimeoutException)
        {
            System.Diagnostics.Debug.WriteLine($"[Checks.CaseInsensitiveFields] {ex.Message}");
        }
        if (set.Count == 0) set.UnionWith(FallbackCaseInsensitive);
        return _ciFields = set;
    }

    public static List<Issue> Run(JsonDoc doc, string manifestPath)
    {
        var issues = new List<Issue>();

        // Duplicate keys (also re-checks that the text parses)
        var dups = new List<string>();
        try { JsonParser.Parse(doc.Text, dups); }
        catch (JsonParseException ex)
        {
            var (line, col) = JsonParser.LineCol(doc.Text, ex.Position);
            issues.Add(new(Severity.Error, $"The file isn't valid JSON: {ex.Message} (line {line}, column {col})."));
            return issues;
        }
        foreach (var d in dups)
            issues.Add(new(Severity.Error, $"Duplicate key: {d}. The app only uses the last one."));

        // Strict parse with the same library the app uses
        try { using var _ = System.Text.Json.JsonDocument.Parse(doc.Text); }
        catch (System.Text.Json.JsonException ex)
        {
            issues.Add(new(Severity.Error, $"The app couldn't read this file: {ex.Message}"));
        }

        // Keys that differ only by case in case-insensitive fields
        foreach (var field in CaseInsensitiveFields(manifestPath))
        {
            if (doc.Get(field) is not { IsObject: true } obj) continue;
            foreach (var grp in obj.Members!.GroupBy(m => m.Key.ToLowerInvariant()).Where(g => g.Select(m => m.Key).Distinct().Count() > 1))
            {
                var names = string.Join(" / ", grp.Select(m => $"\"{m.Key}\"").Distinct());
                issues.Add(new(Severity.Error,
                    $"{Catalog.LabelFor(field)} ({field}): {names} differ only by case. The app would reject the whole manifest and every user would keep the old one. Keep one spelling.",
                    grp.First().Key));
            }
        }

        // Conflicting per-game settings
        bool In(string list, string game) => doc.ArrayContains(new[] { list }, game);
        var games = Catalog.GameEntries(doc).Select(e => e.Game).Distinct();
        foreach (var g in games)
        {
            if (In("thirtyTwoBitGames", g) && In("sixtyFourBitGames", g))
                issues.Add(new(Severity.Warning, $"{g}: set to both 32-bit and 64-bit.", g));

            // Native HDR + Engine.ini settings together is normal (the app treats the second as the first).
            var ue = new List<string>();
            if (In("ueExtendedGames", g)) ue.Add("UE-Extended");
            if (In("nativeHdrGames", g) || doc.Get("ueExtendedCompatibility", g) != null) ue.Add("native HDR");
            if (In("noUeExtendedGames", g)) ue.Add("never");
            if (ue.Count > 1)
                issues.Add(new(Severity.Warning, $"{g}: in UE-Extended lists that contradict each other ({string.Join(", ", ue)}).", g));

            if (In("dofFixForceGames", g) && In("dofFixSkipGames", g))
                issues.Add(new(Severity.Warning, $"{g}: set to both always and never offer DOF Fix.", g));

            if (doc.Get("forceExternalOnly", g) is { IsObject: true } fe && string.IsNullOrWhiteSpace(fe.Get("url")?.Str))
                issues.Add(new(Severity.Warning, $"{g}: external page has no link.", g));
        }
        return issues;
    }
}
