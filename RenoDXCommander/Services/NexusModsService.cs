using System.Text.Json;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Fetches the Nexus Mods game catalogue, caches it to disk with a 24-hour TTL,
/// builds a normalized-name lookup dictionary, and resolves per-game URLs.
/// </summary>
public class NexusModsService : INexusModsService
{
    private readonly HttpClient _http;
    private readonly IGameDetectionService _gameDetection;

    private const string GamesJsonUrl = "https://data.nexusmods.com/file/nexus-data/games.json";

    private static readonly string CachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "nexus_games.json");

    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    /// <summary>Normalized game name → nexusmods_url.</summary>
    private Dictionary<string, string> _lookup = new(StringComparer.Ordinal);

    public NexusModsService(HttpClient http, IGameDetectionService gameDetection)
    {
        _http = http;
        _gameDetection = gameDetection;
    }

    /// <inheritdoc />
    public async Task InitAsync()
    {
        string? json = null;

        // Skip fetch if the cache file is fresh (< 24 hours old).
        bool cacheFresh = false;
        try
        {
            if (File.Exists(CachePath))
            {
                var lastWrite = File.GetLastWriteTimeUtc(CachePath);
                cacheFresh = (DateTime.UtcNow - lastWrite) < CacheTtl;
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[NexusModsService.InitAsync] Cache freshness check failed — {ex.Message}");
        }

        if (cacheFresh)
        {
            json = LoadCacheFromDisk();
            if (json != null)
            {
                CrashReporter.Log("[NexusModsService.InitAsync] Cache is fresh, skipping fetch");
            }
        }

        // Fetch from network if cache was not fresh or unreadable.
        if (json == null)
        {
            try
            {
                json = await _http.GetStringAsync(GamesJsonUrl).ConfigureAwait(false);
                CrashReporter.Log("[NexusModsService.InitAsync] Fetched games.json from Nexus Mods");

                // Persist to disk cache.
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
                    await File.WriteAllTextAsync(CachePath, json).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[NexusModsService.InitAsync] Cache write failed — {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[NexusModsService.InitAsync] Fetch failed — {ex.Message}");

                // Fallback to disk cache.
                json = LoadCacheFromDisk();
                if (json == null)
                {
                    CrashReporter.Log("[NexusModsService.InitAsync] No cache available — Nexus link resolution disabled");
                    return;
                }
            }
        }

        // Parse and build the lookup dictionary.
        try
        {
            var entries = JsonSerializer.Deserialize<List<NexusModsGame>>(json);
            if (entries != null)
                BuildDictionary(entries);

            CrashReporter.Log($"[NexusModsService.InitAsync] Built dictionary with {_lookup.Count} entries");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[NexusModsService.InitAsync] JSON parse failed — {ex.Message}");

            // Malformed JSON is treated as a fetch failure — try cache if we haven't already.
            if (!cacheFresh)
            {
                var fallback = LoadCacheFromDisk();
                if (fallback != null)
                {
                    try
                    {
                        var entries = JsonSerializer.Deserialize<List<NexusModsGame>>(fallback);
                        if (entries != null)
                            BuildDictionary(entries);
                    }
                    catch (Exception innerEx)
                    {
                        CrashReporter.Log($"[NexusModsService.InitAsync] Cache JSON also invalid — {innerEx.Message}");
                    }
                }
            }
        }
    }

    /// <inheritdoc />
    public string? ResolveUrl(string gameName, RemoteManifest? manifest)
    {
        // 1. Manifest override (highest priority).
        if (manifest?.NexusUrlOverrides != null
            && manifest.NexusUrlOverrides.TryGetValue(gameName, out var overrideUrl)
            && !string.IsNullOrEmpty(overrideUrl))
        {
            return overrideUrl;
        }

        // 2. Dictionary lookup by normalized name.
        var normalized = _gameDetection.NormalizeName(gameName);
        if (!string.IsNullOrEmpty(normalized)
            && _lookup.TryGetValue(normalized, out var url))
        {
            return url;
        }

        // 3. No match.
        return null;
    }

    /// <summary>
    /// Builds the normalized-name → URL lookup dictionary from parsed entries.
    /// First entry wins when multiple entries share the same normalized name.
    /// </summary>
    internal void BuildDictionary(List<NexusModsGame> entries)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var key = _gameDetection.NormalizeName(entry.Name);
            if (!string.IsNullOrEmpty(key))
                dict.TryAdd(key, entry.NexusmodsUrl);
        }
        _lookup = dict;
    }

    /// <summary>
    /// Reads the cache file from disk. Returns null if the file doesn't exist or is unreadable.
    /// </summary>
    private static string? LoadCacheFromDisk()
    {
        try
        {
            if (File.Exists(CachePath))
                return File.ReadAllText(CachePath);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[NexusModsService.LoadCacheFromDisk] Read failed — {ex.Message}");
        }
        return null;
    }
}
