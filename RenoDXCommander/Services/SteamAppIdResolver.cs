using System.Net;
using System.Text.Json;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Resolves Steam AppIDs through a 5-step priority chain:
/// manifest override → cached AppID → DetectedGame.SteamAppId → steam_appid.txt → Steam Store search API.
/// </summary>
public class SteamAppIdResolver : ISteamAppIdResolver
{
    private readonly HttpClient _http;
    private readonly IGameDetectionService _gameDetection;

    /// <summary>Rate-limits Steam Store API requests to one at a time.</summary>
    private static readonly SemaphoreSlim _rateLimiter = new(1, 1);

    public SteamAppIdResolver(HttpClient http, IGameDetectionService gameDetection)
    {
        _http = http;
        _gameDetection = gameDetection;
    }

    /// <inheritdoc />
    public async Task<int?> ResolveAsync(
        string gameName,
        int? detectedAppId,
        string installPath,
        RemoteManifest? manifest,
        Dictionary<string, int>? appIdCache = null)
    {
        // 1. Manifest steamAppIdOverrides (highest priority).
        if (manifest?.SteamAppIdOverrides != null
            && manifest.SteamAppIdOverrides.TryGetValue(gameName, out var overrideId))
        {
            return overrideId;
        }

        // 2. Cached AppID.
        var normalized = _gameDetection.NormalizeName(gameName);
        if (!string.IsNullOrEmpty(normalized)
            && appIdCache != null
            && appIdCache.TryGetValue(normalized, out var cachedId))
        {
            return cachedId;
        }

        // 3. DetectedGame.SteamAppId (from ACF parsing).
        if (detectedAppId.HasValue)
        {
            return detectedAppId.Value;
        }

        // 4. steam_appid.txt in install directory.
        var fileAppId = ReadSteamAppIdFile(installPath);
        if (fileAppId.HasValue)
        {
            return fileAppId.Value;
        }

        // 5. Steam Store search API (rate-limited).
        return await SearchSteamStoreAsync(gameName).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public int? FindMatchingAppId(string gameName, List<SteamStoreSearchItem> results)
    {
        if (results == null || results.Count == 0)
            return null;

        var normalizedGame = _gameDetection.NormalizeName(gameName);
        if (string.IsNullOrEmpty(normalizedGame))
            return null;

        foreach (var item in results)
        {
            var normalizedItem = _gameDetection.NormalizeName(item.Name);
            if (string.Equals(normalizedGame, normalizedItem, StringComparison.Ordinal))
                return item.Id;
        }

        return null;
    }

    /// <summary>
    /// Reads the first line of steam_appid.txt in the install directory and parses it as an integer.
    /// Returns null if the file doesn't exist, is empty, or contains a non-integer value.
    /// </summary>
    internal static int? ReadSteamAppIdFile(string installPath)
    {
        try
        {
            var filePath = Path.Combine(installPath, "steam_appid.txt");
            if (!File.Exists(filePath))
                return null;

            var firstLine = File.ReadLines(filePath).FirstOrDefault();
            if (firstLine != null && int.TryParse(firstLine.Trim(), out var appId))
                return appId;

            CrashReporter.Log($"[SteamAppIdResolver] steam_appid.txt unparseable in {installPath}");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[SteamAppIdResolver] Error reading steam_appid.txt — {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Queries the Steam Store search API for the game name, rate-limited to 1 request per second.
    /// Returns the AppID of the first result whose normalized name matches, or null.
    /// </summary>
    private async Task<int?> SearchSteamStoreAsync(string gameName)
    {
        await _rateLimiter.WaitAsync().ConfigureAwait(false);
        try
        {
            var encodedName = Uri.EscapeDataString(gameName);
            var url = $"https://store.steampowered.com/api/storesearch/?term={encodedName}&l=english&cc=US";

            using var response = await _http.GetAsync(url).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                CrashReporter.Log("[SteamAppIdResolver] Steam Store API rate limit (429) — skipping");
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                CrashReporter.Log($"[SteamAppIdResolver] Steam Store API returned {(int)response.StatusCode} — skipping");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            SteamStoreSearchResponse? searchResult;
            try
            {
                searchResult = JsonSerializer.Deserialize<SteamStoreSearchResponse>(json);
            }
            catch (JsonException ex)
            {
                CrashReporter.Log($"[SteamAppIdResolver] Malformed JSON from Steam Store API — {ex.Message}");
                return null;
            }

            if (searchResult?.Items == null || searchResult.Items.Count == 0)
                return null;

            return FindMatchingAppId(gameName, searchResult.Items);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[SteamAppIdResolver] Steam Store search failed — {ex.Message}");
            return null;
        }
        finally
        {
            // Enforce 1-second delay between requests for rate limiting.
            await Task.Delay(1000).ConfigureAwait(false);
            _rateLimiter.Release();
        }
    }
}
