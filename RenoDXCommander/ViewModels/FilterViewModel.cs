using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RenoDXCommander.Collections;
using RenoDXCommander.Models;

namespace RenoDXCommander.ViewModels;

/// <summary>
/// Owns game filtering, search, and filter count logic.
/// Extracted from MainViewModel per Requirement 1.2.
/// </summary>
public partial class FilterViewModel : ObservableObject
{
    private static readonly HashSet<string> ExclusiveFilters = new(StringComparer.OrdinalIgnoreCase)
        { "Detected", "Favourites", "Hidden", "Installed" };

    private static readonly HashSet<string> CombinableFilters = new(StringComparer.OrdinalIgnoreCase)
        { "Unreal", "Unity", "Other", "RenoDX", "Luma" };

    private readonly HashSet<string> _activeFilters = new(StringComparer.OrdinalIgnoreCase) { "Detected" };

    public IReadOnlySet<string> ActiveFilters => _activeFilters;

    [ObservableProperty] private string _searchQuery = "";
    [ObservableProperty] private string _filterMode = "Detected";
    [ObservableProperty] private bool _showHidden = false;
    [ObservableProperty] private int _totalGames;
    [ObservableProperty] private int _installedCount;
    [ObservableProperty] private int _hiddenCount;
    [ObservableProperty] private int _favouriteCount;

    partial void OnSearchQueryChanged(string value) => ApplyFilter();
    partial void OnShowHiddenChanged(bool value) => ApplyFilter();

    /// <summary>
    /// The backing list of all cards. Set by MainViewModel after card building.
    /// </summary>
    private IReadOnlyList<GameCardViewModel> _allCards = Array.Empty<GameCardViewModel>();

    /// <summary>
    /// The observable collection bound to the UI. Set by MainViewModel at startup.
    /// </summary>
    private BatchObservableCollection<GameCardViewModel>? _displayedGames;

    /// <summary>
    /// Initializes the FilterViewModel with the card collection and displayed games collection.
    /// Called by MainViewModel after construction.
    /// </summary>
    public void Initialize(BatchObservableCollection<GameCardViewModel> displayedGames)
    {
        _displayedGames = displayedGames;
    }

    /// <summary>
    /// Updates the backing card list reference. Called by MainViewModel whenever _allCards changes.
    /// </summary>
    public void SetAllCards(IReadOnlyList<GameCardViewModel> allCards)
    {
        _allCards = allCards;
    }

    [RelayCommand]
    public void SetFilter(string filter)
    {
        if (ExclusiveFilters.Contains(filter))
        {
            // Exclusive filter: clear everything, set just this one
            _activeFilters.Clear();
            _activeFilters.Add(filter);
            FilterMode = filter;
        }
        else if (CombinableFilters.Contains(filter))
        {
            // Remove any exclusive filter first
            _activeFilters.RemoveWhere(f => ExclusiveFilters.Contains(f));

            // Toggle the combinable filter
            if (!_activeFilters.Add(filter))
                _activeFilters.Remove(filter);

            // If nothing left, fall back to Detected
            if (_activeFilters.Count == 0)
            {
                _activeFilters.Add("Detected");
                FilterMode = "Detected";
            }
            else
            {
                FilterMode = string.Join(",", _activeFilters);
            }
        }
        ApplyFilter();
    }

    public void ApplyFilter()
    {
        if (_displayedGames == null) return;

        var query = SearchQuery.Trim();
        var filters = _activeFilters;
        var filtered = _allCards.Where(c =>
        {
            // Search match first
            var matchSearch = string.IsNullOrEmpty(query)
                || c.GameName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || c.Maintainer.Contains(query, StringComparison.OrdinalIgnoreCase);
            if (!matchSearch) return false;

            // Hidden tab always shows hidden games regardless of the ShowHidden toggle
            if (filters.Contains("Hidden")) return c.IsHidden;

            // Favourites tab: show favourited games (even if hidden)
            if (filters.Contains("Favourites")) return c.IsFavourite;

            // Installed tab: show games with RenoDX OR Luma installed (not DC/ReShade only)
            if (filters.Contains("Installed"))
            {
                bool rdxInstalled  = c.Status == GameStatus.Installed || c.Status == GameStatus.UpdateAvailable;
                bool lumaInstalled = c.LumaStatus == GameStatus.Installed || c.LumaStatus == GameStatus.UpdateAvailable;
                return (rdxInstalled || lumaInstalled) && !c.IsHidden;
            }

            // "Detected" (All Games): show everything except hidden
            if (filters.Contains("Detected"))
            {
                if (c.IsHidden) return false;
                return true;
            }

            // Combinable filters — match ANY active filter (OR logic)
            bool matched = false;

            if (filters.Contains("Unity"))
            {
                var isUnity = (!string.IsNullOrEmpty(c.EngineHint) && c.EngineHint.IndexOf("Unity", StringComparison.OrdinalIgnoreCase) >= 0)
                              || (c.Mod?.IsGenericUnity == true);
                if (isUnity) matched = true;
            }
            if (filters.Contains("Unreal"))
            {
                var isUnreal = (!string.IsNullOrEmpty(c.EngineHint) && c.EngineHint.IndexOf("Unreal", StringComparison.OrdinalIgnoreCase) >= 0)
                              || (c.Mod?.IsGenericUnreal == true);
                if (isUnreal) matched = true;
            }
            if (filters.Contains("Other"))
            {
                var isUnity = (!string.IsNullOrEmpty(c.EngineHint) && c.EngineHint.IndexOf("Unity", StringComparison.OrdinalIgnoreCase) >= 0)
                              || (c.Mod?.IsGenericUnity == true);
                var isUnreal = (!string.IsNullOrEmpty(c.EngineHint) && c.EngineHint.IndexOf("Unreal", StringComparison.OrdinalIgnoreCase) >= 0)
                              || (c.Mod?.IsGenericUnreal == true);
                if (!isUnity && !isUnreal) matched = true;
            }
            if (filters.Contains("Luma"))
            {
                if (c.LumaMod != null) matched = true;
            }
            if (filters.Contains("RenoDX"))
            {
                if (c.Mod != null) { matched = true; }
                else
                {
                    var isUnreal = !string.IsNullOrEmpty(c.EngineHint) && c.EngineHint.IndexOf("Unreal", StringComparison.OrdinalIgnoreCase) >= 0;
                    var isUnity  = !string.IsNullOrEmpty(c.EngineHint) && c.EngineHint.IndexOf("Unity", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (isUnreal || isUnity) matched = true;
                    if (c.Status == GameStatus.Installed || c.Status == GameStatus.UpdateAvailable) matched = true;
                }
            }

            if (!matched) return false;
            return !c.IsHidden;
        }).OrderBy(c => c.GameName, StringComparer.OrdinalIgnoreCase).ToList();

        _displayedGames.ReplaceAll(filtered);
        UpdateCounts();
    }

    public void UpdateCounts()
    {
        InstalledCount  = _allCards.Count(c => c.Status == GameStatus.Installed || c.Status == GameStatus.UpdateAvailable);
        HiddenCount     = _allCards.Count(c => c.IsHidden);
        FavouriteCount  = _allCards.Count(c => c.IsFavourite);
        TotalGames      = _displayedGames?.Count ?? 0;
        OnPropertyChanged(nameof(InstalledCount));
        OnPropertyChanged(nameof(TotalGames));
        OnPropertyChanged(nameof(HiddenCount));
        OnPropertyChanged(nameof(FavouriteCount));
    }
}
