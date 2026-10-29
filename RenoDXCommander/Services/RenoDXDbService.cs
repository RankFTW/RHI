using System.Text.Json;
using System.Text.Json.Serialization;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Fetches the RHI-maintained RenoDX database JSON files from GitHub.
/// These replace wiki scraping as the primary source for named mod info and
/// engine-specific configuration.
///
/// Four files are fetched:
///   RenoDXdb.json        — named mods (snapshotUrl, nexusUrl, author, notes, etc.)
///   RenoDXdb-unreal.json — UE-Extended games (Method → Set_Path / Engine.ini / Upgrades)
///   RenoDXdb-unity.json  — Unity games (Upgrades → per-game [renodx] INI overrides)
///   RenoDXdb-source.json — Source engine games (listed in Available Mods only for now)
///
/// All use ETag caching via GitHubETagCache — no re-download when files haven't changed.
/// </summary>
public class RenoDXDbService : IRenoDXDbService
{
    private readonly HttpClient _http;
    private readonly GitHubETagCache _etagCache;

    private const string NamedModsUrl =
        "https://raw.githubusercontent.com/RankFTW/rhi-repo/main/database/RenoDXdb.json";

    private const string UnrealUrl =
        "https://raw.githubusercontent.com/RankFTW/rhi-repo/main/database/RenoDXdb-unreal.json";

    private const string UnityUrl =
        "https://raw.githubusercontent.com/RankFTW/rhi-repo/main/database/RenoDXdb-unity.json";

    private const string SourceUrl =
        "https://raw.githubusercontent.com/RankFTW/rhi-repo/main/database/RenoDXdb-source.json";

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // ── In-memory cache (last successful fetch) ───────────────────────────────
    private List<GameMod> _cachedMods = new();
    private Dictionary<string, RenoDXDbUnrealEntry> _cachedUnreal =
        new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, RenoDXDbUnityEntry> _cachedUnity =
        new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, RenoDXDbSourceEntry> _cachedSource =
        new(StringComparer.OrdinalIgnoreCase);

    public RenoDXDbService(HttpClient http, GitHubETagCache etagCache)
    {
        _http = http;
        _etagCache = etagCache;
    }

    /// <summary>
    /// Clears the ETag cache entries for all three DB URLs so the next
    /// <see cref="FetchAllAsync"/> call sends unconditional GETs and picks up
    /// any changes made to the remote files since the last fetch.
    /// </summary>
    public void InvalidateCache()
    {
        _etagCache.Invalidate(NamedModsUrl);
        _etagCache.Invalidate(UnrealUrl);
        _etagCache.Invalidate(UnityUrl);
        _etagCache.Invalidate(SourceUrl);
        CrashReporter.Log("[RenoDXDbService.InvalidateCache] ETag cache cleared for all four DB URLs");
    }

    public async Task<DbFetchResult> FetchAllAsync()
    {
        var modsTask   = FetchNamedModsAsync();
        var unrealTask = FetchUnrealEntriesAsync();
        var unityTask  = FetchUnityEntriesAsync();
        var sourceTask = FetchSourceEntriesAsync();

        await Task.WhenAll(modsTask, unrealTask, unityTask, sourceTask).ConfigureAwait(false);

        var mods   = modsTask.Result;
        var unreal = unrealTask.Result;
        var unity  = unityTask.Result;
        var source = sourceTask.Result;

        // Update in-memory cache on success
        if (mods.Count > 0)   _cachedMods   = mods;
        if (unreal.Count > 0) _cachedUnreal  = unreal;
        if (unity.Count > 0)  _cachedUnity   = unity;
        if (source.Count > 0) _cachedSource  = source;

        CrashReporter.Log($"[RenoDXDbService.FetchAllAsync] Named mods: {mods.Count}, UE entries: {unreal.Count}, Unity entries: {unity.Count}, Source entries: {source.Count}");
        return new DbFetchResult(mods, unreal, unity, source);
    }

    // ── Private fetch helpers ─────────────────────────────────────────────────

    private async Task<List<GameMod>> FetchNamedModsAsync()
    {
        try
        {
            var json = await _etagCache.GetWithETagAsync(_http, NamedModsUrl).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                CrashReporter.Log("[RenoDXDbService.FetchNamedModsAsync] Empty or null response");
                return _cachedMods; // return stale cache on failure
            }

            var entries = JsonSerializer.Deserialize<List<DbNamedModEntry>>(json, _jsonOpts);
            if (entries == null) return _cachedMods;

            var result = new List<GameMod>(entries.Count);
            foreach (var e in entries)
            {
                if (string.IsNullOrWhiteSpace(e.Name)) continue;

                // Promote snapshotUrl32 to primary when no 64-bit URL exists
                var snap64 = e.SnapshotUrl;
                var snap32 = e.SnapshotUrl32;
                if (snap64 == null && snap32 != null) snap64 = snap32;

                result.Add(new GameMod
                {
                    Name          = e.Name.Trim(),
                    Maintainer    = e.Author?.Trim() ?? "",
                    SnapshotUrl   = snap64,
                    SnapshotUrl32 = e.SnapshotUrl32,
                    NexusUrl      = e.NexusUrl,
                    DiscordUrl    = e.DiscordUrl,
                    Status        = MapStatus(e.Status),
                    Notes         = e.Notes,
                    // discussionUrl has no GameMod field yet — stored in NameUrl for display
                    NameUrl       = e.DiscussionUrl,
                });
            }

            CrashReporter.Log($"[RenoDXDbService.FetchNamedModsAsync] Parsed {result.Count} named mods");
            return result;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[RenoDXDbService.FetchNamedModsAsync] Failed — {ex.Message}");
            return _cachedMods;
        }
    }

    private async Task<Dictionary<string, RenoDXDbUnrealEntry>> FetchUnrealEntriesAsync()
    {
        try
        {
            var json = await _etagCache.GetWithETagAsync(_http, UnrealUrl).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                CrashReporter.Log("[RenoDXDbService.FetchUnrealEntriesAsync] Empty or null response");
                return _cachedUnreal;
            }

            var entries = JsonSerializer.Deserialize<List<RenoDXDbUnrealEntry>>(json, _jsonOpts);
            if (entries == null) return _cachedUnreal;

            var result = new Dictionary<string, RenoDXDbUnrealEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in entries)
            {
                if (string.IsNullOrWhiteSpace(e.Name)) continue;
                result[e.Name.Trim()] = e;
            }

            CrashReporter.Log($"[RenoDXDbService.FetchUnrealEntriesAsync] Parsed {result.Count} UE entries");
            return result;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[RenoDXDbService.FetchUnrealEntriesAsync] Failed — {ex.Message}");
            return _cachedUnreal;
        }
    }

    private async Task<Dictionary<string, RenoDXDbUnityEntry>> FetchUnityEntriesAsync()
    {
        try
        {
            var json = await _etagCache.GetWithETagAsync(_http, UnityUrl).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                CrashReporter.Log("[RenoDXDbService.FetchUnityEntriesAsync] Empty or null response");
                return _cachedUnity;
            }

            var entries = JsonSerializer.Deserialize<List<RenoDXDbUnityEntry>>(json, _jsonOpts);
            if (entries == null) return _cachedUnity;

            var result = new Dictionary<string, RenoDXDbUnityEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in entries)
            {
                if (string.IsNullOrWhiteSpace(e.Name)) continue;
                result[e.Name.Trim()] = e;
            }

            CrashReporter.Log($"[RenoDXDbService.FetchUnityEntriesAsync] Parsed {result.Count} Unity entries");
            return result;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[RenoDXDbService.FetchUnityEntriesAsync] Failed — {ex.Message}");
            return _cachedUnity;
        }
    }

    private async Task<Dictionary<string, RenoDXDbSourceEntry>> FetchSourceEntriesAsync()
    {
        try
        {
            var json = await _etagCache.GetWithETagAsync(_http, SourceUrl).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                CrashReporter.Log("[RenoDXDbService.FetchSourceEntriesAsync] Empty or null response");
                return _cachedSource;
            }

            var entries = JsonSerializer.Deserialize<List<RenoDXDbSourceEntry>>(json, _jsonOpts);
            if (entries == null) return _cachedSource;

            var result = new Dictionary<string, RenoDXDbSourceEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in entries)
            {
                if (string.IsNullOrWhiteSpace(e.Name)) continue;
                result[e.Name.Trim()] = e;
            }

            CrashReporter.Log($"[RenoDXDbService.FetchSourceEntriesAsync] Parsed {result.Count} Source entries");
            return result;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[RenoDXDbService.FetchSourceEntriesAsync] Failed — {ex.Message}");
            return _cachedSource;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string MapStatus(string? status) =>
        string.Equals(status, "WIP", StringComparison.OrdinalIgnoreCase) ? "🚧" : "✅";

    // ── Private deserialization model for RenoDXdb.json ──────────────────────
    // Uses lowercase field names as stored in the hosted file.
    private class DbNamedModEntry
    {
        [JsonPropertyName("name")]        public string? Name { get; set; }
        [JsonPropertyName("status")]      public string? Status { get; set; }
        [JsonPropertyName("author")]      public string? Author { get; set; }
        [JsonPropertyName("snapshotUrl")] public string? SnapshotUrl { get; set; }
        [JsonPropertyName("snapshotUrl32")] public string? SnapshotUrl32 { get; set; }
        [JsonPropertyName("nexusUrl")]    public string? NexusUrl { get; set; }
        [JsonPropertyName("discordUrl")]  public string? DiscordUrl { get; set; }
        [JsonPropertyName("discussionUrl")] public string? DiscussionUrl { get; set; }
        [JsonPropertyName("notes")]       public string? Notes { get; set; }
    }
}
