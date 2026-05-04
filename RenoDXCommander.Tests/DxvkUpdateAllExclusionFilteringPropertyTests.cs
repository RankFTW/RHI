// Feature: dxvk-integration, Property 7: Update All exclusion filtering
using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for DXVK Update All exclusion filtering.
/// Uses FsCheck with xUnit.
///
/// **Validates: Requirements 2.5, 16.2**
///
/// For any set of game cards with varying DxvkStatus, ExcludeFromUpdateAllDxvk,
/// and IsHidden values, the Update All filter selects only cards where
/// DxvkStatus == UpdateAvailable AND ExcludeFromUpdateAllDxvk == false AND IsHidden == false.
/// </summary>
public class DxvkUpdateAllExclusionFilteringPropertyTests
{
    /// <summary>
    /// Generates a list of GameCardViewModels with random DXVK states.
    /// Each card has a random DxvkStatus (Installed, UpdateAvailable, NotInstalled),
    /// a random ExcludeFromUpdateAllDxvk flag, and a random IsHidden flag.
    /// </summary>
    private static Gen<List<GameCardViewModel>> GenGameCards()
    {
        var dxvkStatusGen = Gen.Elements(
            GameStatus.NotInstalled,
            GameStatus.Installed,
            GameStatus.UpdateAvailable);

        var cardGen = from dxvkStatus in dxvkStatusGen
                      from excluded in Arb.Default.Bool().Generator
                      from hidden in Arb.Default.Bool().Generator
                      from idx in Gen.Choose(0, 9999)
                      select new GameCardViewModel
                      {
                          GameName = $"Game_{idx}",
                          DxvkStatus = dxvkStatus,
                          ExcludeFromUpdateAllDxvk = excluded,
                          IsHidden = hidden,
                      };

        return Gen.ListOf(cardGen).Select(cards => cards.ToList());
    }

    // ── Property 7: Update All exclusion filtering ────────────────────────────
    // Feature: dxvk-integration, Property 7: Update All exclusion filtering
    // **Validates: Requirements 2.5, 16.2**

    /// <summary>
    /// Property 7: Update All exclusion filtering
    ///
    /// For any set of game cards with varying DxvkStatus, ExcludeFromUpdateAllDxvk,
    /// and IsHidden values, the Update All filter selects only cards where:
    /// - DxvkStatus == UpdateAvailable
    /// - ExcludeFromUpdateAllDxvk == false
    /// - IsHidden == false
    ///
    /// Cards with the exclusion flag set, or without an available update, or that
    /// are hidden, are never selected.
    ///
    /// **Validates: Requirements 2.5, 16.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property UpdateAllDxvk_SelectsCorrectSubset()
    {
        return Prop.ForAll(
            Arb.From(GenGameCards()),
            (List<GameCardViewModel> cards) =>
            {
                // Act: use the same static helper that UpdateAllDxvkAsync uses
                var selected = MainViewModel.GetUpdateAllDxvkEligibleCards(cards);

                // Assert: every selected card must have UpdateAvailable, not excluded, not hidden
                bool allSelectedCorrect = selected.All(c =>
                    c.DxvkStatus == GameStatus.UpdateAvailable
                    && !c.ExcludeFromUpdateAllDxvk
                    && !c.IsHidden);

                // Assert: every non-selected card must be either:
                // - not UpdateAvailable, or
                // - excluded, or
                // - hidden
                var notSelected = cards.Except(selected).ToList();
                bool allNotSelectedCorrect = notSelected.All(c =>
                    c.DxvkStatus != GameStatus.UpdateAvailable
                    || c.ExcludeFromUpdateAllDxvk
                    || c.IsHidden);

                return (allSelectedCorrect && allNotSelectedCorrect)
                    .Label($"cards={cards.Count}, selected={selected.Count}, " +
                           $"allSelectedCorrect={allSelectedCorrect}, " +
                           $"allNotSelectedCorrect={allNotSelectedCorrect}");
            });
    }

    /// <summary>
    /// When all games have ExcludeFromUpdateAllDxvk == true,
    /// no games should be selected for DXVK update.
    ///
    /// **Validates: Requirements 2.5, 16.2**
    /// </summary>
    [Property(MaxTest = 50)]
    public Property UpdateAllDxvk_AllExcluded_SelectsNone()
    {
        var dxvkStatusGen = Gen.Elements(
            GameStatus.NotInstalled,
            GameStatus.Installed,
            GameStatus.UpdateAvailable);

        var cardGen = from dxvkStatus in dxvkStatusGen
                      from idx in Gen.Choose(0, 9999)
                      select new GameCardViewModel
                      {
                          GameName = $"Game_{idx}",
                          DxvkStatus = dxvkStatus,
                          ExcludeFromUpdateAllDxvk = true,
                          IsHidden = false,
                      };

        var cardsGen = Gen.ListOf(cardGen).Select(cards => cards.ToList());

        return Prop.ForAll(
            Arb.From(cardsGen),
            (List<GameCardViewModel> cards) =>
            {
                var selected = MainViewModel.GetUpdateAllDxvkEligibleCards(cards);

                return (selected.Count == 0)
                    .Label($"Expected 0 selected, got {selected.Count} from {cards.Count} cards");
            });
    }

    /// <summary>
    /// When no games have DXVK UpdateAvailable status,
    /// no games should be selected regardless of exclusion flags.
    ///
    /// **Validates: Requirements 2.5, 16.2**
    /// </summary>
    [Property(MaxTest = 50)]
    public Property UpdateAllDxvk_NoUpdatesAvailable_SelectsNone()
    {
        var noUpdateStatusGen = Gen.Elements(
            GameStatus.NotInstalled,
            GameStatus.Installed);

        var cardGen = from dxvkStatus in noUpdateStatusGen
                      from excluded in Arb.Default.Bool().Generator
                      from idx in Gen.Choose(0, 9999)
                      select new GameCardViewModel
                      {
                          GameName = $"Game_{idx}",
                          DxvkStatus = dxvkStatus,
                          ExcludeFromUpdateAllDxvk = excluded,
                          IsHidden = false,
                      };

        var cardsGen = Gen.ListOf(cardGen).Select(cards => cards.ToList());

        return Prop.ForAll(
            Arb.From(cardsGen),
            (List<GameCardViewModel> cards) =>
            {
                var selected = MainViewModel.GetUpdateAllDxvkEligibleCards(cards);

                return (selected.Count == 0)
                    .Label($"Expected 0 selected, got {selected.Count} from {cards.Count} cards");
            });
    }

    /// <summary>
    /// When all games are hidden, no games should be selected
    /// regardless of DXVK status or exclusion flags.
    ///
    /// **Validates: Requirements 2.5, 16.2**
    /// </summary>
    [Property(MaxTest = 50)]
    public Property UpdateAllDxvk_AllHidden_SelectsNone()
    {
        var dxvkStatusGen = Gen.Elements(
            GameStatus.NotInstalled,
            GameStatus.Installed,
            GameStatus.UpdateAvailable);

        var cardGen = from dxvkStatus in dxvkStatusGen
                      from excluded in Arb.Default.Bool().Generator
                      from idx in Gen.Choose(0, 9999)
                      select new GameCardViewModel
                      {
                          GameName = $"Game_{idx}",
                          DxvkStatus = dxvkStatus,
                          ExcludeFromUpdateAllDxvk = excluded,
                          IsHidden = true,
                      };

        var cardsGen = Gen.ListOf(cardGen).Select(cards => cards.ToList());

        return Prop.ForAll(
            Arb.From(cardsGen),
            (List<GameCardViewModel> cards) =>
            {
                var selected = MainViewModel.GetUpdateAllDxvkEligibleCards(cards);

                return (selected.Count == 0)
                    .Label($"Expected 0 selected, got {selected.Count} from {cards.Count} cards");
            });
    }
}
