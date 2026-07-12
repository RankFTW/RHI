// NexusUpdateService.cs — Checks Nexus Mods GraphQL v2 API for mod updates.
// No API key required. Uses legacyModsByDomain query with gameDomain + modId.

using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace RenoDXCommander.Services;

public interface INexusUpdateService
{
    /// <summary>
    /// Checks all tracked Nexus mods for updates. Returns a dictionary of
    /// game names that have Nexus updates available.
    /// </summary>
    Task<HashSet<string>> CheckForUpdatesAsync(
        IReadOnlyList<(string GameName, string NexusUrl, string? InstalledVersion)> modsToCheck);

    /// <summary>Resets the baseline for a game (called when the mod is installed/updated).</summary>
    void ResetBaseline(string gameName);

    /// <summary>Returns game names that have a persisted update flag (for restoring on restart).</summary>
    HashSet<string> GetCachedUpdates();

    /// <summary>Loads baselines from disk.</summary>
    void LoadBaselines();

    /// <summary>Saves baselines to disk.</summary>
    void SaveBaselines();
}

public class NexusUpdateService : INexusUpdateService
{
    private readonly HttpClient _http;
    private readonly ICrashReporter _crashReporter;
    private Dictionary<string, NexusBaseline> _baselines = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Cached mod summaries from the last Nexus check. Key = game name, Value = summary text.</summary>
    public Dictionary<string, string> ModSummaries { get; } = new(StringComparer.OrdinalIgnoreCase);

    private static readonly string BaselinesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "nexus_baselines.json");

    private static readonly Regex NexusUrlPattern = new(
        @"nexusmods\.com/([^/]+)/mods/(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public NexusUpdateService(HttpClient http, ICrashReporter crashReporter)
    {
        _http = http;
        _crashReporter = crashReporter;
    }

    public void LoadBaselines()
    {
        try
        {
            if (File.Exists(BaselinesPath))
            {
                var json = File.ReadAllText(BaselinesPath);
                _baselines = JsonSerializer.Deserialize<Dictionary<string, NexusBaseline>>(json)
                    ?? new(StringComparer.OrdinalIgnoreCase);
                _crashReporter.Log($"[NexusUpdateService.LoadBaselines] Loaded {_baselines.Count} baselines");
            }
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[NexusUpdateService.LoadBaselines] Failed — {ex.Message}");
            _baselines = new(StringComparer.OrdinalIgnoreCase);
        }
    }

    public void SaveBaselines()
    {
        try
        {
            var dir = Path.GetDirectoryName(BaselinesPath);
            if (dir != null) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(_baselines, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(BaselinesPath, json);
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[NexusUpdateService.SaveBaselines] Failed — {ex.Message}");
        }
    }

    public void ResetBaseline(string gameName)
    {
        if (_baselines.TryGetValue(gameName, out var baseline))
        {
            baseline.LastKnownUpdate = DateTime.UtcNow.ToString("o");
            baseline.HasUpdate = false;
            SaveBaselines();
            _crashReporter.Log($"[NexusUpdateService.ResetBaseline] Reset baseline for '{gameName}'");
        }
    }

    /// <summary>
    /// Fetches the Nexus mod summary for a single game on-demand (for the Info button).
    /// </summary>
    public async Task FetchSummaryAsync(string gameName, string nexusUrl)
    {
        if (ModSummaries.ContainsKey(gameName)) return;

        var match = NexusUrlPattern.Match(nexusUrl);
        if (!match.Success) return;

        var domain = match.Groups[1].Value;
        var modId = int.Parse(match.Groups[2].Value);

        try
        {
            var ids = new[] { new { gameDomain = domain, modId } };
            var payload = new
            {
                query = @"
                    query legacyModsByDomain($ids: [CompositeDomainWithIdInput!]!) {
                      legacyModsByDomain(ids: $ids) {
                        nodes { modId summary }
                      }
                    }",
                variables = new { ids }
            };

            var jsonPayload = JsonSerializer.Serialize(payload);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync("https://api.nexusmods.com/v2/graphql", content).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return;

            var responseJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<GraphQLResponse>(responseJson);
            var node = result?.Data?.LegacyModsByDomain?.Nodes?.FirstOrDefault();
            if (node != null && !string.IsNullOrWhiteSpace(node.Summary))
                ModSummaries[gameName] = node.Summary;
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[NexusUpdateService.FetchSummaryAsync] Failed for '{gameName}' — {ex.Message}");
        }
    }

    /// <summary>Returns game names that have a persisted update flag (for restoring on restart).</summary>
    public HashSet<string> GetCachedUpdates()
    {
        return new HashSet<string>(
            _baselines.Where(kv => kv.Value.HasUpdate).Select(kv => kv.Key),
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task<HashSet<string>> CheckForUpdatesAsync(
        IReadOnlyList<(string GameName, string NexusUrl, string? InstalledVersion)> modsToCheck)
    {
        var updatedGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (modsToCheck.Count == 0) return updatedGames;

        // Parse Nexus URLs into domain + modId pairs
        var queryItems = new List<(string GameName, string Domain, int ModId, string? InstalledVersion)>();
        foreach (var (gameName, nexusUrl, installedVersion) in modsToCheck)
        {
            var parsed = ParseNexusUrl(nexusUrl);
            if (parsed != null)
                queryItems.Add((gameName, parsed.Value.Domain, parsed.Value.ModId, installedVersion));
        }

        if (queryItems.Count == 0) return updatedGames;

        _crashReporter.Log($"[NexusUpdateService.CheckForUpdatesAsync] Checking {queryItems.Count} Nexus mod(s)...");
        _crashReporter.Log($"[NexusUpdateService.CheckForUpdatesAsync] Games: {string.Join(", ", queryItems.Select(q => $"{q.GameName} ({q.Domain}/mods/{q.ModId})"))}");

        try
        {
            // Build GraphQL request
            var ids = queryItems.Select(q => new Dictionary<string, object>
            {
                { "gameDomain", q.Domain },
                { "modId", q.ModId }
            }).ToList();

            var payload = new
            {
                query = @"
                    query legacyModsByDomain($ids: [CompositeDomainWithIdInput!]!) {
                      legacyModsByDomain(ids: $ids) {
                        nodes {
                          modId
                          version
                          updatedAt
                          summary
                        }
                      }
                    }",
                variables = new { ids }
            };

            var jsonPayload = JsonSerializer.Serialize(payload);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync("https://api.nexusmods.com/v2/graphql", content).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _crashReporter.Log($"[NexusUpdateService.CheckForUpdatesAsync] GraphQL request failed: {response.StatusCode}");
                return updatedGames;
            }

            var responseJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<GraphQLResponse>(responseJson);

            if (result?.Data?.LegacyModsByDomain?.Nodes == null)
            {
                _crashReporter.Log("[NexusUpdateService.CheckForUpdatesAsync] No nodes in response");
                return updatedGames;
            }

            // Build lookup: modId → node
            var nodeLookup = new Dictionary<int, GraphQLModNode>();
            foreach (var node in result.Data.LegacyModsByDomain.Nodes)
                nodeLookup[node.ModId] = node;

            // Compare against baselines
            bool baselinesChanged = false;
            foreach (var (gameName, domain, modId, installedVersion) in queryItems)
            {
                if (!nodeLookup.TryGetValue(modId, out var node)) continue;

                // Cache the summary for the Info button
                if (!string.IsNullOrWhiteSpace(node.Summary))
                    ModSummaries[gameName] = node.Summary;

                if (!_baselines.TryGetValue(gameName, out var baseline))
                {
                    // First time seeing this mod — set baseline, no update shown
                    _baselines[gameName] = new NexusBaseline
                    {
                        Domain = domain,
                        ModId = modId,
                        LastKnownUpdate = node.UpdatedAt ?? DateTime.UtcNow.ToString("o"),
                        InstalledVersion = installedVersion
                    };
                    baselinesChanged = true;
                    continue;
                }

                // Check if installed version changed (user updated the mod manually)
                if (installedVersion != null && baseline.InstalledVersion != null
                    && !string.Equals(installedVersion, baseline.InstalledVersion, StringComparison.OrdinalIgnoreCase))
                {
                    // Version changed — reset baseline
                    baseline.LastKnownUpdate = node.UpdatedAt ?? DateTime.UtcNow.ToString("o");
                    baseline.InstalledVersion = installedVersion;
                    baselinesChanged = true;
                    continue;
                }

                // Update installed version tracking
                if (installedVersion != null && baseline.InstalledVersion != installedVersion)
                {
                    baseline.InstalledVersion = installedVersion;
                    baselinesChanged = true;
                }

                // Compare updatedAt against baseline
                if (node.UpdatedAt != null && baseline.LastKnownUpdate != null)
                {
                    if (DateTime.TryParse(node.UpdatedAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var remoteDate)
                        && DateTime.TryParse(baseline.LastKnownUpdate, null, System.Globalization.DateTimeStyles.RoundtripKind, out var baselineDate))
                    {
                        if (remoteDate > baselineDate)
                        {
                            updatedGames.Add(gameName);
                            baseline.HasUpdate = true;
                            baselinesChanged = true;
                        }
                    }
                }
            }

            if (baselinesChanged)
                SaveBaselines();

            _crashReporter.Log($"[NexusUpdateService.CheckForUpdatesAsync] Complete — {updatedGames.Count} update(s) detected");
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[NexusUpdateService.CheckForUpdatesAsync] Failed — {ex.Message}");
        }

        return updatedGames;
    }

    /// <summary>Parses a Nexus Mods URL into game domain and mod ID.</summary>
    public static (string Domain, int ModId)? ParseNexusUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        var match = NexusUrlPattern.Match(url);
        if (!match.Success) return null;
        if (!int.TryParse(match.Groups[2].Value, out var modId)) return null;
        return (match.Groups[1].Value, modId);
    }

    // ── Response models ──────────────────────────────────────────────────────────

    private class GraphQLResponse
    {
        [JsonPropertyName("data")]
        public GraphQLData? Data { get; set; }
    }

    private class GraphQLData
    {
        [JsonPropertyName("legacyModsByDomain")]
        public GraphQLLegacyModsByDomain? LegacyModsByDomain { get; set; }
    }

    private class GraphQLLegacyModsByDomain
    {
        [JsonPropertyName("nodes")]
        public List<GraphQLModNode>? Nodes { get; set; }
    }

    private class GraphQLModNode
    {
        [JsonPropertyName("modId")]
        public int ModId { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("updatedAt")]
        public string? UpdatedAt { get; set; }

        [JsonPropertyName("summary")]
        public string? Summary { get; set; }
    }
}

/// <summary>Persisted baseline for a single Nexus mod.</summary>
public class NexusBaseline
{
    [JsonPropertyName("domain")]
    public string Domain { get; set; } = "";

    [JsonPropertyName("modId")]
    public int ModId { get; set; }

    [JsonPropertyName("lastKnownUpdate")]
    public string? LastKnownUpdate { get; set; }

    [JsonPropertyName("installedVersion")]
    public string? InstalledVersion { get; set; }

    [JsonPropertyName("hasUpdate")]
    public bool HasUpdate { get; set; }
}
