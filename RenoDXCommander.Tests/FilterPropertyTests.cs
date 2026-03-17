using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Collections;
using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for FilterViewModel.ApplyFilter correctness.
/// </summary>
public class FilterPropertyTests
{
    // ── Generators ────────────────────────────────────────────────────────────────

    private static readonly Gen<string> GenGameName =
        Gen.Elements("Cyberpunk 2077", "ELDEN RING", "starfield", "Baldur's Gate 3",
                     "hogwarts legacy", "Alan Wake 2", "RETURNAL", "Hades II",
                     "The Witcher 3", "DARK SOULS III", "half-life 2", "Portal");

    private static readonly Gen<string> GenMaintainer =
        Gen.Elements("ShortFuse", "ERSH", "pumbo", "MARAT", "clshortfuse",
                     "NotReal", "TestDev", "");

    private static readonly Gen<string> GenSearchQuery =
        Gen.OneOf(
            Gen.Elements("cyber", "ELDEN", "Star", "gate", "WAKE", "hades",
                         "witcher", "DARK", "half", "portal", "SHORT", "ersh",
                         "PUMBO", "marat", "xyz_nomatch"),
            Gen.Constant(""));

    private static readonly Gen<GameStatus> GenStatus =
        Gen.Elements(GameStatus.NotInstalled, GameStatus.Available,
                     GameStatus.Installed, GameStatus.UpdateAvailable);

    /// <summary>
    /// Generates a GameCardViewModel with random name, maintainer, and hidden state.
    /// </summary>
    private static readonly Gen<GameCardViewModel> GenCard =
        from name in GenGameName
        from maintainer in GenMaintainer
        from status in GenStatus
        from isHidden in Arb.Default.Bool().Generator
        select new GameCardViewModel
        {
            GameName = name,
            Maintainer = maintainer,
            Status = status,
            IsHidden = isHidden,
            EngineHint = ""
        };

    /// <summary>
    /// Generates a list of 0–20 GameCardViewModels.
    /// </summary>
    private static readonly Gen<List<GameCardViewModel>> GenCardList =
        GenCard.ListOf().Select(l => l.ToList())
               .Where(l => l.Count <= 20);

    // ── Helpers ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the card's GameName or Maintainer contains the query (case-insensitive).
    /// </summary>
    private static bool MatchesSearch(GameCardViewModel card, string query)
    {
        if (string.IsNullOrEmpty(query)) return true;
        return card.GameName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || card.Maintainer.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    // ── Property 3: Filter produces correct case-insensitive subsets ──────────────
    // Feature: codebase-optimization, Property 3: Filter produces correct case-insensitive subsets
    // **Validates: Requirements 6.1, 10.2**
    [Property(MaxTest = 100)]
    public Property ApplyFilter_ProducesCorrectCaseInsensitiveSubset()
    {
        return Prop.ForAll(
            Arb.From(GenCardList),
            Arb.From(GenSearchQuery),
            (List<GameCardViewModel> cards, string query) =>
            {
                // Arrange: set up FilterViewModel with "Detected" filter (default)
                var displayed = new BatchObservableCollection<GameCardViewModel>();
                var filterVm = new FilterViewModel();
                filterVm.Initialize(displayed);
                filterVm.SetAllCards(cards);
                filterVm.SearchQuery = query;

                // Act: explicitly call ApplyFilter (setter may not fire if value unchanged)
                filterVm.ApplyFilter();

                // Assert 1: every displayed card is from the original list (subset)
                var isSubset = displayed.All(d => cards.Contains(d));

                // Assert 2: every displayed card matches the search query case-insensitively
                var allMatch = displayed.All(d => MatchesSearch(d, query));

                // Assert 3: every card NOT in the result that is also not hidden
                //            must NOT match the search query
                //            (i.e., no valid match was incorrectly excluded)
                var displayedSet = new HashSet<GameCardViewModel>(displayed);
                var noFalseExclusions = cards
                    .Where(c => !displayedSet.Contains(c) && !c.IsHidden)
                    .All(c => !MatchesSearch(c, query));

                return (isSubset && allMatch && noFalseExclusions)
                    .Label($"isSubset={isSubset}, allMatch={allMatch}, noFalseExclusions={noFalseExclusions} " +
                           $"(cards={cards.Count}, displayed={displayed.Count}, query='{query}')");
            });
    }
}
