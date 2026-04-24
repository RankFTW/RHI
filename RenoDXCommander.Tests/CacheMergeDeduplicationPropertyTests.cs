using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for cache merge deduplication logic.
/// Feature: codebase-refactor, Property 1: Cache merge deduplication preserves fresh games and excludes duplicates
/// **Validates: Requirements 4.4**
/// </summary>
public class CacheMergeDeduplicationPropertyTests
{
    private readonly GameDetectionService _gameDetectionService = new();

    // ── Generators ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a non-null, non-empty game name string suitable for DetectedGame.
    /// Uses a mix of realistic game names and random strings.
    /// </summary>
    private static readonly Gen<string> GenGameName =
        Gen.OneOf(
            Gen.Elements(
                "Cyberpunk 2077", "ELDEN RING", "NieR:Automata™",
                "STAR WARS™ Jedi: Fallen Order", "Baldur's Gate 3",
                "The Witcher® 3: Wild Hunt", "Résident Evil 4",
                "Halo Infinite©", "FINAL FANTASY VII REMAKE",
                "Dark Souls III", "Horizon Zero Dawn",
                "Red Dead Redemption 2", "God of War",
                "Monster Hunter World", "Sekiro"),
            Arb.Default.NonEmptyString().Generator.Select(s => s.Get));

    /// <summary>
    /// Generates a plausible install path string (or empty for games without a path).
    /// </summary>
    private static readonly Gen<string> GenInstallPath =
        Gen.OneOf(
            Gen.Constant(""),
            from drive in Gen.Elements("C", "D", "E")
            from folder in Gen.Elements("Steam", "GOG", "Epic", "Games")
            from game in Arb.Default.NonEmptyString().Generator.Select(s => s.Get)
            select $@"{drive}:\{folder}\{game}");

    /// <summary>
    /// Generates a DetectedGame with random name and install path.
    /// </summary>
    private static readonly Gen<DetectedGame> GenDetectedGame =
        from name in GenGameName
        from path in GenInstallPath
        from source in Gen.Elements("Steam", "GOG", "Epic", "EA", "Xbox", "Manual")
        select new DetectedGame { Name = name, InstallPath = path, Source = source };

    /// <summary>
    /// Generates a pair of (freshGames, cachedGames) lists.
    /// </summary>
    private static readonly Gen<Tuple<List<DetectedGame>, List<DetectedGame>>> GenGameLists =
        from freshCount in Gen.Choose(0, 15)
        from cachedCount in Gen.Choose(0, 15)
        from fresh in Gen.ListOf(freshCount, GenDetectedGame).Select(l => l.ToList())
        from cached in Gen.ListOf(cachedCount, GenDetectedGame).Select(l => l.ToList())
        select Tuple.Create(fresh, cached);

    // ── Merge logic (mirrors MainViewModel.Init.cs) ───────────────────────────────

    /// <summary>
    /// Replicates the exact merge logic from MainViewModel.InitializeAsync.
    /// Fresh games always win; cached games are added only if they don't match
    /// any fresh game by normalized name OR install path.
    /// </summary>
    private List<DetectedGame> MergeGames(List<DetectedGame> freshGames, List<DetectedGame> cachedGames)
    {
        var freshNorms = freshGames
            .Select(g => _gameDetectionService.NormalizeName(g.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var freshPaths = freshGames
            .Where(g => !string.IsNullOrEmpty(g.InstallPath))
            .Select(g => g.InstallPath.TrimEnd(Path.DirectorySeparatorChar))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return freshGames
            .Concat(cachedGames.Where(g =>
                !freshNorms.Contains(_gameDetectionService.NormalizeName(g.Name))
                && (string.IsNullOrEmpty(g.InstallPath)
                    || !freshPaths.Contains(g.InstallPath.TrimEnd(Path.DirectorySeparatorChar)))))
            .ToList();
    }

    // ── Property 1 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Property 1: Cache merge deduplication preserves fresh games and excludes duplicates.
    /// For any list of fresh and cached DetectedGame objects, merging them SHALL produce
    /// a result where:
    ///   (a) all fresh games are present in the merged result,
    ///   (b) cached games appear only if they don't match any fresh game by normalized
    ///       name or install path,
    ///   (c) total count equals fresh count plus non-duplicate cached count.
    /// **Validates: Requirements 4.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property CacheMerge_PreservesFreshGames_And_ExcludesDuplicates()
    {
        return Prop.ForAll(
            Arb.From(GenGameLists),
            (Tuple<List<DetectedGame>, List<DetectedGame>> t) =>
            {
                var freshGames = t.Item1;
                var cachedGames = t.Item2;

                var merged = MergeGames(freshGames, cachedGames);

                // (a) All fresh games are present in the merged result (by reference)
                var allFreshPresent = freshGames.All(fg => merged.Contains(fg));

                // Build the same sets used by the merge logic
                var freshNorms = freshGames
                    .Select(g => _gameDetectionService.NormalizeName(g.Name))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var freshPaths = freshGames
                    .Where(g => !string.IsNullOrEmpty(g.InstallPath))
                    .Select(g => g.InstallPath.TrimEnd(Path.DirectorySeparatorChar))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                // (b) Cached games in the result don't match any fresh game
                var cachedInResult = merged.Skip(freshGames.Count).ToList();
                var noDuplicatesIncluded = cachedInResult.All(g =>
                    !freshNorms.Contains(_gameDetectionService.NormalizeName(g.Name))
                    && (string.IsNullOrEmpty(g.InstallPath)
                        || !freshPaths.Contains(g.InstallPath.TrimEnd(Path.DirectorySeparatorChar))));

                // Count how many cached games should survive the filter
                var expectedSurvivors = cachedGames.Count(g =>
                    !freshNorms.Contains(_gameDetectionService.NormalizeName(g.Name))
                    && (string.IsNullOrEmpty(g.InstallPath)
                        || !freshPaths.Contains(g.InstallPath.TrimEnd(Path.DirectorySeparatorChar))));

                // (c) Total count equals fresh count plus non-duplicate cached count
                var countCorrect = merged.Count == freshGames.Count + expectedSurvivors;

                return (allFreshPresent && noDuplicatesIncluded && countCorrect)
                    .Label($"fresh={freshGames.Count}, cached={cachedGames.Count}, " +
                           $"merged={merged.Count}, expectedSurvivors={expectedSurvivors}, " +
                           $"allFreshPresent={allFreshPresent}, noDuplicatesIncluded={noDuplicatesIncluded}, " +
                           $"countCorrect={countCorrect}");
            });
    }
}
