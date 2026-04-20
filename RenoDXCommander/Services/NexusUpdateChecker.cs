using System.Collections.Concurrent;
using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Services;

/// <summary>
/// Checks Nexus Mods for available updates by querying the V2 GraphQL API
/// and comparing remote file versions against locally installed versions.
/// Results are cached in-memory with a configurable TTL.
/// </summary>
public class NexusUpdateChecker : INexusUpdateChecker
{
    private readonly INexusApiClient _apiClient;
    private readonly ConcurrentDictionary<(string GameDomain, int ModId), CacheEntry> _cache = new();
    private readonly TimeSpan _cacheTtl;

    /// <summary>
    /// Creates a new NexusUpdateChecker.
    /// </summary>
    /// <param name="apiClient">The Nexus API client for querying mod files.</param>
    /// <param name="cacheTtl">Cache time-to-live. Defaults to 1 hour if null.</param>
    public NexusUpdateChecker(INexusApiClient apiClient, TimeSpan? cacheTtl = null)
    {
        _apiClient = apiClient;
        _cacheTtl = cacheTtl ?? TimeSpan.FromHours(1);
    }

    /// <inheritdoc />
    public async Task CheckAllAsync(IEnumerable<GameCardViewModel> cards, bool bypassCache = false,
        Microsoft.UI.Dispatching.DispatcherQueue? dispatcherQueue = null)
    {
        foreach (var card in cards)
        {
            // Stop if rate limited
            if (_apiClient.IsRateLimited)
            {
                CrashReporter.Log("[NexusUpdateChecker.CheckAllAsync] Stopping — rate limited");
                break;
            }

            // Skip snapshot mods — they have their own update detection via direct download comparison.
            // The Nexus version format is completely different from the RHI date-based format,
            // so comparing them would always produce false positives.
            if (card.Mod?.SnapshotUrl != null)
                continue;

            // Resolve the Nexus URL from the card
            var nexusUrl = card.NexusUrl ?? card.Mod?.NexusUrl;
            if (string.IsNullOrWhiteSpace(nexusUrl))
                continue;

            var reference = NexusUrlParser.Parse(nexusUrl);
            if (reference is null || reference.ModId is null)
            {
                CrashReporter.Log($"[NexusUpdateChecker.CheckAllAsync] Skipping unparseable NexusUrl: {nexusUrl}");
                continue;
            }

            var gameDomain = reference.GameDomain;
            var modId = reference.ModId.Value;
            var localVersion = card.RdxInstalledVersion;

            CrashReporter.Log($"[NexusUpdateChecker.CheckAllAsync] Checking {card.GameName}: {gameDomain}/{modId}, local={localVersion ?? "(none)"}");

            var result = await CheckSingleInternalAsync(gameDomain, modId, bypassCache).ConfigureAwait(false);
            if (result is null)
            {
                CrashReporter.Log($"[NexusUpdateChecker.CheckAllAsync] No result for {card.GameName} ({gameDomain}/{modId}) — API returned nothing or rate limited");
                continue;
            }

            CrashReporter.Log($"[NexusUpdateChecker.CheckAllAsync] API returned file for {gameDomain}/{modId}, latest={result.LatestFile.Version ?? "(null)"}, category={result.LatestFile.Category}");

            // Compare remote version against local installed version
            var updateAvailable = !string.IsNullOrEmpty(localVersion)
                && !string.IsNullOrEmpty(result.LatestFile.Version)
                && !string.Equals(localVersion, result.LatestFile.Version, StringComparison.Ordinal);

            CrashReporter.Log($"[NexusUpdateChecker.CheckAllAsync] Update check for {card.GameName}: local={localVersion ?? "(none)"} remote={result.LatestFile.Version ?? "(null)"} updateAvailable={updateAvailable}");

            // Dispatch to UI thread — WinUI 3 requires property changes that drive
            // UI bindings to happen on the dispatcher thread.
            if (dispatcherQueue != null)
                dispatcherQueue.TryEnqueue(() => card.IsNexusUpdateAvailable = updateAvailable);
            else
                card.IsNexusUpdateAvailable = updateAvailable;
        }
    }

    /// <inheritdoc />
    public async Task<NexusUpdateResult?> CheckSingleAsync(string gameDomain, int modId)
    {
        return await CheckSingleInternalAsync(gameDomain, modId, bypassCache: false).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void ClearCache()
    {
        _cache.Clear();
    }

    /// <summary>
    /// Checks whether a cached entry is still valid based on the configured TTL.
    /// </summary>
    internal static bool IsCacheHit(DateTime lastCheckUtc, DateTime currentUtc, TimeSpan ttl)
    {
        return (currentUtc - lastCheckUtc) < ttl;
    }

    /// <summary>
    /// Determines whether an update is available by comparing local and remote version strings.
    /// Returns true when the versions differ (string inequality).
    /// </summary>
    internal static bool IsUpdateAvailable(string? localVersion, string? remoteVersion)
    {
        if (string.IsNullOrEmpty(localVersion) || string.IsNullOrEmpty(remoteVersion))
            return false;

        return !string.Equals(localVersion, remoteVersion, StringComparison.Ordinal);
    }

    /// <summary>
    /// Internal implementation for checking a single mod, with cache support.
    /// </summary>
    private async Task<NexusUpdateResult?> CheckSingleInternalAsync(string gameDomain, int modId, bool bypassCache)
    {
        var cacheKey = (gameDomain, modId);
        var now = DateTime.UtcNow;

        // Check cache unless bypassing
        if (!bypassCache && _cache.TryGetValue(cacheKey, out var cached))
        {
            if (IsCacheHit(cached.CheckedAtUtc, now, _cacheTtl))
                return cached.Result;
        }

        // Stop if rate limited
        if (_apiClient.IsRateLimited)
        {
            CrashReporter.Log($"[NexusUpdateChecker.CheckSingleInternalAsync] Skipping ({gameDomain}/{modId}) — rate limited");
            return null;
        }

        // Query the API for mod files (V1 REST — works with game domain strings)
        var files = await _apiClient.GetModFilesV1Async(gameDomain, modId).ConfigureAwait(false);

        // Check rate limit again after the call
        if (_apiClient.IsRateLimited)
        {
            CrashReporter.Log($"[NexusUpdateChecker.CheckSingleInternalAsync] Rate limited after query for ({gameDomain}/{modId})");
            return null;
        }

        CrashReporter.Log($"[NexusUpdateChecker.CheckSingleInternalAsync] API returned {files.Count} files for {gameDomain}/{modId}");

        if (files.Count == 0)
            return null;

        // Find the latest main file (prefer MAIN category, fall back to most recent upload)
        var mainFiles = files
            .Where(f => string.Equals(f.Category, "MAIN", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.UploadedAt)
            .ToList();

        var latestFile = mainFiles.FirstOrDefault();

        if (mainFiles.Count == 0)
            CrashReporter.Log($"[NexusUpdateChecker.CheckSingleInternalAsync] No MAIN category files for {gameDomain}/{modId}, categories found: [{string.Join(", ", files.Select(f => f.Category).Distinct())}]");
        else
            CrashReporter.Log($"[NexusUpdateChecker.CheckSingleInternalAsync] Found {mainFiles.Count} MAIN file(s) for {gameDomain}/{modId}, latest={latestFile!.Version ?? "(null)"}");

        latestFile ??= files.OrderByDescending(f => f.UploadedAt).First();

        var checkedAt = DateTimeOffset.UtcNow;
        var result = new NexusUpdateResult(
            GameDomain: gameDomain,
            ModId: modId,
            LatestFile: latestFile,
            UpdateAvailable: true, // Caller determines actual availability based on local version
            CheckedAt: checkedAt);

        // Store in cache
        _cache[cacheKey] = new CacheEntry(now, result);

        return result;
    }

    /// <summary>
    /// Represents a cached update check result with its timestamp.
    /// </summary>
    private sealed record CacheEntry(DateTime CheckedAtUtc, NexusUpdateResult Result);
}
