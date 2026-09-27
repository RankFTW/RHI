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

    /// <summary>
    /// Callback invoked after FilterMode is updated in SetFilter.
    /// MainViewModel sets this to trigger SaveNameMappings.
    /// </summary>
    public Action? FilterModeChanged { get; set; }

    /// <summary>
    /// Called immediately before the filter is applied — allows the UI to capture the current selection.
    /// </summary>
    public Action? PreFilterAction { get; set; }

    /// <summary>
    /// Callback invoked after CustomFilters list is modified (add/remove).
    /// MainViewModel sets this to trigger SaveNameMappings.
    /// </summary>
    public Action? CustomFiltersChanged { get; set; }

    [ObservableProperty] private string _searchQuery = "";
    [ObservableProperty] private string _filterMode = "Detected";
    [ObservableProperty] private ObservableCollection<CustomFilter> _customFilters = new();
    [ObservableProperty] private string? _activeCustomFilterName;
    [ObservableProperty] private bool _showHidden = false;
    [ObservableProperty] private int _totalGames;
    [ObservableProperty] private int _installedCount;
    [ObservableProperty] private int _hiddenCount;
    [ObservableProperty] private int _favouriteCount;

    partial void OnSearchQueryChanged(string value)
    {
        ApplyFilter();
    }
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
        FilterModeChanged?.Invoke();
    }

    /// <summary>
    /// Restores the filter mode from a persisted string value.
    /// Validates the input and falls back to "Detected" on any validation failure.
    /// </summary>
    public void RestoreFilterMode(string? persisted)
    {
        if (string.IsNullOrWhiteSpace(persisted))
        {
            RestoreDefault();
            return;
        }

        var tokens = persisted.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            RestoreDefault();
            return;
        }

        bool hasExclusive = false;
        bool hasCombinable = false;

        foreach (var token in tokens)
        {
            if (ExclusiveFilters.Contains(token))
                hasExclusive = true;
            else if (CombinableFilters.Contains(token))
                hasCombinable = true;
            else
            {
                // Unrecognised token
                RestoreDefault();
                return;
            }
        }

        // Mixed exclusive + combinable is invalid
        if (hasExclusive && hasCombinable)
        {
            RestoreDefault();
            return;
        }

        // More than one exclusive filter is invalid
        if (hasExclusive && tokens.Length > 1)
        {
            RestoreDefault();
            return;
        }

        _activeFilters.Clear();
        foreach (var token in tokens)
            _activeFilters.Add(token);

        FilterMode = string.Join(",", _activeFilters);
        ApplyFilter();
    }

    private void RestoreDefault()
    {
        _activeFilters.Clear();
        _activeFilters.Add("Detected");
        FilterMode = "Detected";
        ApplyFilter();
    }

    /// <summary>
    /// Adds a custom filter with the given name and query.
    /// Returns false if name is empty/whitespace or a duplicate (case-insensitive).
    /// </summary>
    public bool AddCustomFilter(string name, string query)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (CustomFilterNameExists(name))
            return false;

        CustomFilters.Add(new CustomFilter { Name = name, Query = query });
        CustomFiltersChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Removes a custom filter by name. If it was the active custom filter,
    /// resets to "Detected" filter and clears SearchQuery.
    /// </summary>
    public void RemoveCustomFilter(string name)
    {
        var filter = CustomFilters.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (filter == null) return;

        bool wasActive = ActiveCustomFilterName != null
            && ActiveCustomFilterName.Equals(name, StringComparison.OrdinalIgnoreCase);

        CustomFilters.Remove(filter);

        if (wasActive)
        {
            ActiveCustomFilterName = null;
            ApplyFilter();
        }

        CustomFiltersChanged?.Invoke();
    }

    /// <summary>
    /// Activates a custom filter chip: applies the stored query as a filter layer
    /// independent of the search box. Clicking the already-active filter deactivates it.
    /// </summary>
    public void ActivateCustomFilter(string name)
    {
        // Toggle: clicking the active filter deactivates it
        if (ActiveCustomFilterName != null
            && ActiveCustomFilterName.Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            DeactivateCustomFilter();
            ApplyFilter();
            return;
        }

        var filter = CustomFilters.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (filter == null) return;

        ActiveCustomFilterName = filter.Name;
        ApplyFilter();
    }

    /// <summary>
    /// Deactivates the currently active custom filter chip.
    /// </summary>
    public void DeactivateCustomFilter()
    {
        ActiveCustomFilterName = null;
    }

    /// <summary>
    /// Returns true if a custom filter with the given name already exists (case-insensitive).
    /// </summary>
    public bool CustomFilterNameExists(string name)
    {
        return CustomFilters.Any(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool MatchesUniversalSearch(GameCardViewModel card, string query)
    {
        return card.GameName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || card.Maintainer.Contains(query, StringComparison.OrdinalIgnoreCase)
            || card.Source.Contains(query, StringComparison.OrdinalIgnoreCase)
            || card.EngineHint.Contains(query, StringComparison.OrdinalIgnoreCase)
            || card.GraphicsApi.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)
            || card.GraphicsApiLabel.Contains(query, StringComparison.OrdinalIgnoreCase)
            || card.DetectedApis.Any(a => a.ToString().Contains(query, StringComparison.OrdinalIgnoreCase))
            || (card.Is32Bit ? "32-bit" : "64-bit").Contains(query, StringComparison.OrdinalIgnoreCase)
            || (card.IsREEngineGame && "RE Engine".Contains(query, StringComparison.OrdinalIgnoreCase))
            || (card.IsREEngineGame && "RE Framework".Contains(query, StringComparison.OrdinalIgnoreCase))
            || (card.Mod?.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
            || (card.Mod?.Maintainer?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
            || (card.LumaMod?.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
            || (card.LumaMod?.Author?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
            || card.VulkanRenderingPath.Contains(query, StringComparison.OrdinalIgnoreCase)
            || (card.HasUwFixUrl && "UW Fix".Contains(query, StringComparison.OrdinalIgnoreCase))
            || (card.HasUltraPlusUrl && "Ultra+".Contains(query, StringComparison.OrdinalIgnoreCase))
            || (card.HasDlss && "DLSS".Contains(query, StringComparison.OrdinalIgnoreCase))
            || (card.HasDlssd && "Ray Reconstruction".Contains(query, StringComparison.OrdinalIgnoreCase))
            || (card.HasDlssg && "Frame Generation".Contains(query, StringComparison.OrdinalIgnoreCase))
            || (card.HasStreamline && "Streamline".Contains(query, StringComparison.OrdinalIgnoreCase))
            || (card.HasAnyDlssStreamline && "DLSS".Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    public void ApplyFilter()
    {
        if (_displayedGames == null) return;

        // Notify UI to capture current selection before the list is rebuilt
        PreFilterAction?.Invoke();

        var query = SearchQuery.Trim();
        var filters = _activeFilters;

        // If a custom filter is active, get its stored query for an additional filter pass
        string? customQuery = null;
        if (ActiveCustomFilterName is not null)
        {
            var active = CustomFilters.FirstOrDefault(
                f => f.Name.Equals(ActiveCustomFilterName, StringComparison.OrdinalIgnoreCase));
            customQuery = active?.Query?.Trim();
        }

        var filtered = _allCards.Where(c =>
        {
            // Search box match
            var matchSearch = string.IsNullOrEmpty(query)
                || MatchesUniversalSearch(c, query);
            if (!matchSearch) return false;

            // Custom filter match (independent of search box)
            if (!string.IsNullOrEmpty(customQuery))
            {
                if (!MatchesUniversalSearch(c, customQuery)) return false;
            }

            // Hidden tab always shows hidden games regardless of the ShowHidden toggle
            if (filters.Contains("Hidden")) return c.IsHidden;

            // Favourites tab: show favourited games (even if hidden)
            if (filters.Contains("Favourites")) return c.IsFavourite;

            // Installed tab: show games with ReShade installed
            if (filters.Contains("Installed"))
            {
                bool rsInstalled = c.RsStatus == GameStatus.Installed || c.RsStatus == GameStatus.UpdateAvailable;
                return rsInstalled && !c.IsHidden;
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
        InstalledCount  = _allCards.Count(c => c.RsStatus == GameStatus.Installed || c.RsStatus == GameStatus.UpdateAvailable);
        HiddenCount     = _allCards.Count(c => c.IsHidden);
        FavouriteCount  = _allCards.Count(c => c.IsFavourite);
        TotalGames      = _displayedGames?.Count ?? 0;
        OnPropertyChanged(nameof(InstalledCount));
        OnPropertyChanged(nameof(TotalGames));
        OnPropertyChanged(nameof(HiddenCount));
        OnPropertyChanged(nameof(FavouriteCount));
    }
}
