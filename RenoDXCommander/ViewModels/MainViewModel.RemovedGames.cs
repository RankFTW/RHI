// MainViewModel.RemovedGames.cs -- "Remove from RHI": takes a detected game out of the game list for good
// (unlike Hide, it isn't under the Hidden filter either). Stored as "GameName|Store" in RemovedGames
// (settings.json) and restored from Settings › Tools & Maintenance › Troubleshooting › Removed games.
// Removed cards are kept aside in memory so Restore brings them straight back without a rescan.

using RenoDXCommander.Models;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    private HashSet<string> _removedGames => _gameNameService.RemovedGames;

    /// <summary>Cards left out of the list this session, by key, so Restore can put them back.</summary>
    private readonly Dictionary<string, GameCardViewModel> _removedCards = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _removedCardsLock = new();

    /// <summary>Removed games as (key, name, store), sorted by name.</summary>
    public IReadOnlyList<(string Key, string Name, string Store)> RemovedGameList =>
        _removedGames
            .Select(k => { var g = GameKey.Parse(k); return (k, g.Name, g.Store); })
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private bool IsRemoved(GameCardViewModel c) =>
        _removedGames.Count > 0 && _removedGames.Contains(GameKey.FromCard(c.GameName, c.Source).ToKey());

    /// <summary>Returns the cards without removed games; the removed ones are kept aside for Restore.</summary>
    private List<GameCardViewModel> WithoutRemovedGames(List<GameCardViewModel> cards)
    {
        if (_removedGames.Count == 0) return cards;
        var kept = new List<GameCardViewModel>(cards.Count);
        int removed = 0;
        lock (_removedCardsLock)
        {
            foreach (var c in cards)
            {
                if (IsRemoved(c))
                {
                    _removedCards[GameKey.FromCard(c.GameName, c.Source).ToKey()] = c;
                    removed++;
                }
                else kept.Add(c);
            }
        }
        if (removed > 0)
            _crashReporter.Log($"[MainViewModel.WithoutRemovedGames] Left out {removed} removed game(s)");
        return kept;
    }

    /// <summary>Takes the game out of RHI's list. Undo with <see cref="RestoreRemovedGame"/>.</summary>
    public void RemoveGameFromRhi(GameCardViewModel card)
    {
        var key = GameKey.FromCard(card.GameName, card.Source).ToKey();
        _crashReporter.Log($"[MainViewModel.RemoveGameFromRhi] {key}");

        // Pick the next game in the visible list before this one goes
        GameCardViewModel? next = null;
        if (ReferenceEquals(SelectedGame, card))
        {
            var shown = DisplayedGames;
            int i = shown.IndexOf(card);
            if (i >= 0)
                next = i + 1 < shown.Count ? shown[i + 1] : i > 0 ? shown[i - 1] : null;
        }

        _removedGames.Add(key);
        _hiddenGames.Remove(key);
        card.IsHidden = false;
        lock (_removedCardsLock) _removedCards[key] = card;
        _allCards.Remove(card);

        SaveNameMappings();
        SaveLibrary();
        _filterViewModel.SetAllCards(_allCards);
        _filterViewModel.ApplyFilter();
        _filterViewModel.UpdateCounts();

        if (ReferenceEquals(SelectedGame, card))
            SelectedGame = next;
    }

    /// <summary>
    /// Puts a removed game back. Returns false when the game wasn't found this session
    /// (it comes back on the next refresh if it's still installed).
    /// </summary>
    public bool RestoreRemovedGame(string key)
    {
        _crashReporter.Log($"[MainViewModel.RestoreRemovedGame] {key}");
        _removedGames.Remove(key);
        SaveNameMappings();

        GameCardViewModel? card;
        lock (_removedCardsLock)
        {
            if (_removedCards.Remove(key, out card) == false) card = null;
        }
        if (card == null) return false;

        if (!_allCards.Contains(card))
        {
            card.DispatcherQueue = DispatcherQueue;
            _allCards.Add(card);
            _allCards = _allCards.OrderBy(c => c.GameName, StringComparer.OrdinalIgnoreCase).ToList();
        }
        SaveLibrary();
        _filterViewModel.SetAllCards(_allCards);
        _filterViewModel.ApplyFilter();
        _filterViewModel.UpdateCounts();
        return true;
    }
}
