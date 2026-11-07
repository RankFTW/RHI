// MainViewModel.Collections.cs -- game collections for the UI: create / rename / delete / reorder,
// add and remove games, and show one in the game list. The data lives in GameNameService.Collections
// (settings.json, saved through the debounced SaveNameMappings); the list filtering is
// FilterViewModel.ActiveCollectionName. Membership is looked up by "GameName|Store", so cards carry no
// collection flags and both init paths need nothing extra.

using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    /// <summary>Raised (UI thread) after a collection is created, renamed, deleted or moved, so the chips are rebuilt.</summary>
    public event Action? CollectionsChanged;

    public GameCollectionStore CollectionStore => _gameNameService.Collections;

    /// <summary>Collection names in the user's order.</summary>
    public IReadOnlyList<string> CollectionNames => _gameNameService.Collections.Names;

    /// <summary>The collection shown in the game list, or null.</summary>
    public string? ActiveCollectionName => _filterViewModel.ActiveCollectionName;

    /// <summary>Wires the filter to the collection data. Called once from the constructor.</summary>
    private void InitCollections()
    {
        _filterViewModel.CollectionMembers = name => _gameNameService.Collections.GetMembers(name);
        _filterViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName != nameof(FilterViewModel.ActiveCollectionName)) return;
            // Kept in the store so the debounced save writes it ("ActiveCollection")
            _gameNameService.Collections.ActiveName = _filterViewModel.ActiveCollectionName;
            OnPropertyChanged(nameof(ActiveCollectionName));
        };
    }

    /// <summary>On launch: show the collection that was active last time, if it still exists.</summary>
    private void RestoreActiveCollection()
    {
        var active = _gameNameService.Collections.ActiveName;
        if (active == null) return;
        _filterViewModel.ActivateCollection(active);
        _crashReporter.Log($"[MainViewModel.RestoreActiveCollection] '{active}'");
    }

    private static string CardKey(GameCardViewModel card) => GameKey.FromCard(card.GameName, card.Source).ToKey();

    public bool IsInCollection(string name, GameCardViewModel card) =>
        _gameNameService.Collections.Contains(name, CardKey(card));

    /// <summary>Shows a collection in the game list (null or the active one again = back to All).</summary>
    public void ActivateCollection(string? name)
    {
        if (name != null && string.Equals(name, _filterViewModel.ActiveCollectionName, StringComparison.OrdinalIgnoreCase))
            name = null;
        _filterViewModel.ActivateCollection(name);
        _crashReporter.Log($"[MainViewModel.ActivateCollection] '{name ?? "(none)"}'");
    }

    /// <summary>Creates a collection, optionally with a first game. Returns the problem with the name, or null.</summary>
    public string? CreateCollection(string? name, GameCardViewModel? firstGame, out string created)
    {
        var error = _gameNameService.Collections.Create(name, out created);
        if (error != null) return error;
        if (firstGame != null) _gameNameService.Collections.SetMembership(created, CardKey(firstGame), true);
        _crashReporter.Log($"[MainViewModel.CreateCollection] '{created}'{(firstGame != null ? $" with '{firstGame.GameName}'" : "")}");
        SaveNameMappings();
        CollectionsChanged?.Invoke();
        return null;
    }

    /// <summary>Renames a collection. Returns the problem with the new name, or null.</summary>
    public string? RenameCollection(string oldName, string? newName)
    {
        bool wasActive = string.Equals(oldName, _filterViewModel.ActiveCollectionName, StringComparison.OrdinalIgnoreCase);
        var error = _gameNameService.Collections.Rename(oldName, newName, out var renamed);
        if (error != null) return error;
        if (wasActive) _filterViewModel.ActiveCollectionName = renamed;
        _crashReporter.Log($"[MainViewModel.RenameCollection] '{oldName}' -> '{renamed}'");
        SaveNameMappings();
        CollectionsChanged?.Invoke();
        return null;
    }

    /// <summary>Deletes a collection (never the games). The list goes back to All if it was showing it.</summary>
    public bool DeleteCollection(string name)
    {
        if (string.Equals(name, _filterViewModel.ActiveCollectionName, StringComparison.OrdinalIgnoreCase))
            _filterViewModel.ActivateCollection(null);
        if (!_gameNameService.Collections.Delete(name)) return false;
        _crashReporter.Log($"[MainViewModel.DeleteCollection] '{name}'");
        SaveNameMappings();
        CollectionsChanged?.Invoke();
        return true;
    }

    /// <summary>Moves a collection left (negative) or right in the chip row.</summary>
    public bool MoveCollection(string name, int delta)
    {
        if (!_gameNameService.Collections.Move(name, delta)) return false;
        SaveNameMappings();
        CollectionsChanged?.Invoke();
        return true;
    }

    /// <summary>Adds a game to a collection or takes it out; the list updates if that collection is shown.</summary>
    public void SetCollectionMembership(string name, GameCardViewModel card, bool member)
    {
        if (!_gameNameService.Collections.SetMembership(name, CardKey(card), member)) return;
        _crashReporter.Log($"[MainViewModel.SetCollectionMembership] '{card.GameName}' {(member ? "added to" : "removed from")} '{name}'");
        SaveNameMappings();
        if (string.Equals(name, _filterViewModel.ActiveCollectionName, StringComparison.OrdinalIgnoreCase))
            _filterViewModel.ApplyFilter();
    }
}
