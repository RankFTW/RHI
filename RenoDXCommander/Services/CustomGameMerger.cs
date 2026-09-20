using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>What caused a game detection pass.</summary>
public enum CustomScanTrigger
{
    /// <summary>App start (cached library shown first, background scan follows) or first-ever scan.</summary>
    Startup,
    /// <summary>The Refresh button.</summary>
    Refresh,
    /// <summary>The Full Refresh button.</summary>
    FullRefresh,
    /// <summary>The Scan Now button in Settings.</summary>
    Manual,
}

/// <summary>
/// Pure decision / merge logic for custom-folder games, kept free of UI and view-model state so it can be tested.
/// </summary>
public static class CustomGameMerger
{
    /// <summary>Source label used for games that came from a custom folder (never pretends to be Steam/GOG).</summary>
    public const string SourceName = "Custom";

    public static bool IsCustomSource(string? source)
        => string.Equals(source, SourceName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether this pass should look for NEW games in the configured folders.
    /// Nothing is ever scanned unless the user configured at least one folder. Startup, Full Refresh and
    /// Scan Now always scan; a normal Refresh scans only while "Auto-scan on Refresh" is on.
    /// </summary>
    public static bool ShouldScanForNew(CustomScanTrigger trigger, bool autoScanOnRefresh, int configuredFolderCount)
    {
        if (configuredFolderCount <= 0) return false;
        return trigger != CustomScanTrigger.Refresh || autoScanOnRefresh;
    }

    /// <summary>
    /// Splits candidates found by an AUTOMATIC scan (startup / Refresh) into games to import right away and games
    /// waiting for review. Candidates the user declined earlier are neither imported nor re-suggested — only an explicit
    /// Scan Now shows them again. Ambiguous (low-confidence) candidates are never imported without review.
    /// </summary>
    public static (List<CustomGameCandidate> autoImport, List<CustomGameCandidate> needsReview) SplitForAutomaticScan(
        IEnumerable<CustomGameCandidate> candidates, IReadOnlyCollection<string> declinedPaths)
    {
        var autoImport = new List<CustomGameCandidate>();
        var needsReview = new List<CustomGameCandidate>();
        foreach (var c in candidates)
        {
            if (declinedPaths.Any(d => CustomFolderPaths.AreSame(d, c.InstallPath))) continue;
            (c.Confidence == CustomCandidateConfidence.High ? autoImport : needsReview).Add(c);
        }
        return (autoImport, needsReview);
    }

    /// <summary>
    /// Combines a normal store scan with custom-folder games.
    /// <list type="bullet">
    /// <item>Store results are returned untouched (store detection behaviour is not changed).</item>
    /// <item>A custom game that overlaps a store game (same install path) is dropped — the store entry wins.</item>
    /// <item>Previously imported custom games are kept, so a Refresh never forgets them. The only ones removed are games
    /// whose folder is gone while a configured, reachable custom folder that should contain them is present
    /// (i.e. genuinely uninstalled). Offline drives, or folders no longer configured, never delete anything.</item>
    /// <item>Newly discovered games are added unless they overlap something already in the result.</item>
    /// </list>
    /// </summary>
    public static List<DetectedGame> Merge(
        IReadOnlyList<DetectedGame> storeGames,
        IEnumerable<DetectedGame> knownCustom,
        IEnumerable<DetectedGame> newlyDiscovered,
        IReadOnlyList<string> configuredRoots,
        Func<string, bool>? pathExists = null,
        Func<string, bool>? rootAvailable = null)
    {
        pathExists ??= CustomFolderPaths.IsAvailable;
        rootAvailable ??= CustomFolderPaths.IsAvailable;

        var result = new List<DetectedGame>(storeGames);
        var storePaths = storeGames.Select(g => g.InstallPath).Where(p => !string.IsNullOrEmpty(p)).ToList();

        foreach (var game in knownCustom)
        {
            if (string.IsNullOrEmpty(game.InstallPath)) continue;
            if (storePaths.Any(p => CustomFolderPaths.Overlaps(p, game.InstallPath))) continue;   // prefer the store entry

            if (!pathExists(game.InstallPath))
            {
                bool underReachableRoot = configuredRoots.Any(r =>
                    CustomFolderPaths.IsSameOrUnder(game.InstallPath, r) && rootAvailable(r));
                if (underReachableRoot) continue;   // really gone from a folder we can read
            }
            AddIfNew(result, game);
        }

        foreach (var game in newlyDiscovered)
            AddIfNew(result, game);

        return result;
    }

    private static void AddIfNew(List<DetectedGame> result, DetectedGame game)
    {
        if (result.Any(g => CustomFolderPaths.Overlaps(g.InstallPath, game.InstallPath))) return;
        game.Source = SourceName;
        result.Add(game);
    }
}
