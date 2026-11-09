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

    /// <summary>
    /// The game collection shown in the list (its games, minus hidden ones), or null. Replaces the
    /// main filter while set: picking All / Installed / Favourites / Hidden or an engine chip clears it.
    /// </summary>
    [ObservableProperty] private string? _activeCollectionName;

    /// <summary>A collection's member keys ("GameName|Store") by name, null when it doesn't exist. Set by MainViewModel.</summary>
    public Func<string, IReadOnlySet<string>?>? CollectionMembers { get; set; }

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
        // Any main or engine filter replaces an active collection
        ActiveCollectionName = null;
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

    /// <summary>
    /// Shows a collection's games (null = back to All). The main filter goes back to All underneath,
    /// so clearing the collection shows every game; the search box and a custom filter still narrow it.
    /// </summary>
    public void ActivateCollection(string? name)
    {
        if (name != null && CollectionMembers?.Invoke(name) == null) name = null;
        _activeFilters.Clear();
        _activeFilters.Add("Detected");
        FilterMode = "Detected";
        ActiveCollectionName = name;
        ApplyFilter();
        FilterModeChanged?.Invoke();
    }

    /// <summary>A game shown while a collection is active: in it and not hidden (hidden games stay hidden).</summary>
    internal static bool MatchesCollection(GameCardViewModel card, IReadOnlySet<string> members)
        => !card.IsHidden && members.Contains(GameKey.FromCard(card.GameName, card.Source).ToKey());

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

    /// <summary>
    /// Letters and digits only, lower case: "F.E.A.R. 3" → "fear3", "Half-Life 2" → "halflife2".
    /// </summary>
    internal static string LettersAndDigits(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var ch in s)
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString();
    }

    /// <summary>
    /// Looser name matching, on top of the plain "contains" search:
    /// <list type="bullet">
    /// <item>punctuation and spaces ignored: "fear" finds F.E.A.R., "half life" finds Half-Life 2;</item>
    /// <item>Roman numerals as numbers: "gta 3" / "grand theft auto 3" find Grand Theft Auto III;</item>
    /// <item>initials, from the start: "gta" finds Grand Theft Auto, "ff7" Final Fantasy VII, "hl2" Half-Life 2.</item>
    /// </list>
    /// </summary>
    internal static bool NameMatchesLoosely(string gameName, string query)
    {
        var q = LettersAndDigits(query);
        if (q.Length < 2) return false;
        var f = s_searchForms.GetOrAdd(gameName, SearchForms);
        return f.Plain.Contains(q, StringComparison.Ordinal)
            || f.Numbered.Contains(q, StringComparison.Ordinal)
            || f.Initials.StartsWith(q, StringComparison.Ordinal)
            || f.RawInitials.StartsWith(q, StringComparison.Ordinal);
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string Plain, string Numbered, string Initials, string RawInitials)> s_searchForms = new();

    private static readonly System.Text.RegularExpressions.Regex RomanNumeral =
        new(@"^(X{0,3})(IX|IV|V?I{0,3})$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// A name's search forms (cached per name): letters + digits; the same with Roman numerals as numbers; initials
    /// with numbers ("gta3", "ff7"); initials with numerals as written ("gtaiii", "ffx").
    /// </summary>
    internal static (string Plain, string Numbered, string Initials, string RawInitials) SearchForms(string name)
    {
        // Apostrophes join a word ("Marvel's"); every other non-letter / digit separates words
        var words = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch)) current.Append(ch);
            else if (ch is '\'' or '’') continue;
            else if (current.Length > 0) { words.Add(current.ToString()); current.Clear(); }
        }
        if (current.Length > 0) words.Add(current.ToString());

        var numbered = new System.Text.StringBuilder();
        var initials = new System.Text.StringBuilder();
        var rawInitials = new System.Text.StringBuilder();
        for (int i = 0; i < words.Count; i++)
        {
            var w = words[i];
            bool digits = w.All(char.IsDigit);
            var number = digits ? w : RomanValue(w, isLast: i == words.Count - 1);
            numbered.Append(number ?? w.ToLowerInvariant());
            initials.Append(number ?? char.ToLowerInvariant(w[0]).ToString());
            rawInitials.Append(number != null ? w.ToLowerInvariant() : char.ToLowerInvariant(w[0]).ToString());
        }
        return (LettersAndDigits(name), numbered.ToString(), initials.ToString(), rawInitials.ToString());
    }

    /// <summary>
    /// "III" → "3" for a word written as a capital Roman numeral (I to XXXIX). A lone "I" only at the end of the
    /// name ("I Am Bread" isn't "1 Am Bread").
    /// </summary>
    private static string? RomanValue(string word, bool isLast)
    {
        if (word.Length == 0 || word != word.ToUpperInvariant() || !RomanNumeral.IsMatch(word)) return null;
        if (word == "I" && !isLast) return null;
        int total = 0;
        for (int i = 0; i < word.Length; i++)
        {
            int v = word[i] switch { 'I' => 1, 'V' => 5, 'X' => 10, _ => 0 };
            int next = i + 1 < word.Length ? word[i + 1] switch { 'I' => 1, 'V' => 5, 'X' => 10, _ => 0 } : 0;
            total += v < next ? -v : v;
        }
        return total > 0 ? total.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
    }

    internal static bool MatchesUniversalSearch(GameCardViewModel card, string query)
    {
        return card.GameName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || NameMatchesLoosely(card.GameName, query)
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

        // An active collection: its games only (looked up once, by "GameName|Store")
        IReadOnlySet<string>? collection = ActiveCollectionName is { } collectionName
            ? CollectionMembers?.Invoke(collectionName) : null;

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

            if (collection != null) return MatchesCollection(c, collection);

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
