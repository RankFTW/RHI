// GameCollectionStore.cs -- the user's game collections (named lists of games, keyed "GameName|Store")
// and which one is active in the game list. Owned by GameNameService (saved in settings.json with the
// other per-game lists, "GameCollections" + "ActiveCollection"). Changed on the UI thread, read by the
// debounced save on a timer thread, so every access takes the lock and reads hand out copies.
// Pure logic (no UI), unit tested in GameCollectionTests.

using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public sealed class GameCollectionStore
{
    public const int MaxNameLength = 40;

    /// <summary>Names of the built-in list filters; a collection can't use them (it would read like the filter).</summary>
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
        { "All", "All games", "Installed", "Favourites", "Favorites", "Hidden", "Detected" };

    private readonly object _lock = new();
    private List<GameCollection> _items = new();
    private string? _activeName;

    /// <summary>The collection shown in the game list, or null (a normal filter is shown).</summary>
    public string? ActiveName
    {
        get { lock (_lock) return _activeName; }
        set { lock (_lock) _activeName = value == null ? null : FindLocked(value)?.Name; }
    }

    public IReadOnlyList<string> Names
    {
        get { lock (_lock) return _items.Select(c => c.Name).ToList(); }
    }

    public int Count
    {
        get { lock (_lock) return _items.Count; }
    }

    public bool Exists(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        lock (_lock) return FindLocked(name) != null;
    }

    /// <summary>The stored spelling of a collection's name, or null.</summary>
    public string? GetName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        lock (_lock) return FindLocked(name)?.Name;
    }

    /// <summary>
    /// Checks a name for a new collection (or for renaming <paramref name="renaming"/>): trimmed,
    /// not empty, at most <see cref="MaxNameLength"/> characters, not a built-in filter name, not
    /// used by another collection (ignoring case). Returns the problem as a sentence, or null.
    /// </summary>
    public string? ValidateName(string? name, string? renaming, out string clean)
    {
        lock (_lock) return ValidateLocked(name, renaming, out clean);
    }

    private string? ValidateLocked(string? name, string? renaming, out string clean)
    {
        clean = (name ?? "").Trim();
        if (clean.Length == 0) return "Enter a name for the collection.";
        if (clean.Length > MaxNameLength) return $"Collection names can be up to {MaxNameLength} characters.";
        if (clean.Any(char.IsControl)) return "Collection names can't contain control characters.";
        if (ReservedNames.Contains(clean)) return $"\"{clean}\" is the name of a built-in filter. Pick another name.";
        var existing = FindLocked(clean);
        if (existing != null && (renaming == null || !existing.Name.Equals(renaming, StringComparison.OrdinalIgnoreCase)))
            return $"You already have a collection called \"{existing.Name}\".";
        return null;
    }

    /// <summary>Adds an empty collection at the end. Returns the problem with the name, or null when created.</summary>
    public string? Create(string? name, out string created)
    {
        lock (_lock)
        {
            var error = ValidateLocked(name, null, out created);
            if (error != null) return error;
            _items.Add(new GameCollection { Name = created });
            return null;
        }
    }

    /// <summary>Renames a collection (the active one stays active). Returns the problem, or null when renamed.</summary>
    public string? Rename(string oldName, string? newName, out string renamed)
    {
        lock (_lock)
        {
            renamed = "";
            var c = FindLocked(oldName);
            if (c == null) return "That collection no longer exists.";
            var error = ValidateLocked(newName, c.Name, out renamed);
            if (error != null) return error;
            bool wasActive = _activeName != null && _activeName.Equals(c.Name, StringComparison.OrdinalIgnoreCase);
            c.Name = renamed;
            if (wasActive) _activeName = renamed;
            return null;
        }
    }

    /// <summary>Deletes a collection. The games themselves are untouched. False when it didn't exist.</summary>
    public bool Delete(string name)
    {
        lock (_lock)
        {
            var c = FindLocked(name);
            if (c == null) return false;
            _items.Remove(c);
            if (_activeName != null && _activeName.Equals(c.Name, StringComparison.OrdinalIgnoreCase))
                _activeName = null;
            return true;
        }
    }

    /// <summary>Moves a collection <paramref name="delta"/> places (negative = left). False when it can't move.</summary>
    public bool Move(string name, int delta)
    {
        lock (_lock)
        {
            var c = FindLocked(name);
            if (c == null || delta == 0) return false;
            int from = _items.IndexOf(c);
            int to = Math.Clamp(from + delta, 0, _items.Count - 1);
            if (to == from) return false;
            _items.RemoveAt(from);
            _items.Insert(to, c);
            return true;
        }
    }

    /// <summary>The position of a collection in the user's order, or -1.</summary>
    public int IndexOf(string name)
    {
        lock (_lock)
        {
            var c = FindLocked(name);
            return c == null ? -1 : _items.IndexOf(c);
        }
    }

    public bool Contains(string name, string gameKey)
    {
        lock (_lock)
            return FindLocked(name)?.Games.Contains(gameKey, StringComparer.OrdinalIgnoreCase) == true;
    }

    /// <summary>Puts a game in a collection or takes it out. True when something changed.</summary>
    public bool SetMembership(string name, string gameKey, bool member)
    {
        if (string.IsNullOrEmpty(gameKey)) return false;
        lock (_lock)
        {
            var c = FindLocked(name);
            if (c == null) return false;
            int i = c.Games.FindIndex(k => k.Equals(gameKey, StringComparison.OrdinalIgnoreCase));
            if (member)
            {
                if (i >= 0) return false;
                c.Games.Add(gameKey);
                return true;
            }
            if (i < 0) return false;
            c.Games.RemoveAt(i);
            return true;
        }
    }

    /// <summary>Flips a game's membership. Returns whether it's in the collection afterwards.</summary>
    public bool Toggle(string name, string gameKey)
    {
        bool now = !Contains(name, gameKey);
        SetMembership(name, gameKey, now);
        return Contains(name, gameKey);
    }

    /// <summary>A copy of a collection's game keys (case-insensitive set), or null when it doesn't exist.</summary>
    public IReadOnlySet<string>? GetMembers(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        lock (_lock)
        {
            var c = FindLocked(name);
            return c == null ? null : new HashSet<string>(c.Games, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>The collections a game is in, in the user's order.</summary>
    public IReadOnlyList<string> CollectionsContaining(string gameKey)
    {
        lock (_lock)
            return _items.Where(c => c.Games.Contains(gameKey, StringComparer.OrdinalIgnoreCase)).Select(c => c.Name).ToList();
    }

    /// <summary>A renamed game keeps its collections: every "OldName|Store" key becomes "NewName|Store".</summary>
    public void MigrateGameRename(string oldName, string newName)
    {
        if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName)) return;
        lock (_lock)
        {
            foreach (var c in _items)
            {
                bool changed = false;
                var migrated = new List<string>(c.Games.Count);
                foreach (var key in c.Games)
                {
                    var parsed = GameKey.Parse(key);
                    if (parsed.MatchesName(oldName))
                    {
                        migrated.Add(new GameKey(newName, parsed.Store).ToKey());
                        changed = true;
                    }
                    else migrated.Add(key);
                }
                if (changed) c.Games = migrated.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
        }
    }

    /// <summary>A deep copy for saving.</summary>
    public List<GameCollection> Snapshot()
    {
        lock (_lock)
            return _items.Select(c => new GameCollection { Name = c.Name, Games = c.Games.ToList() }).ToList();
    }

    /// <summary>
    /// Replaces everything with saved data. Bad entries (empty / duplicate / built-in names) are
    /// dropped, duplicate game keys merged; the active collection is kept only if it still exists.
    /// </summary>
    public void Load(IEnumerable<GameCollection>? saved, string? activeName)
    {
        var items = new List<GameCollection>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in saved ?? Enumerable.Empty<GameCollection>())
        {
            var name = (c?.Name ?? "").Trim();
            if (name.Length == 0 || name.Length > MaxNameLength || ReservedNames.Contains(name) || !names.Add(name)) continue;
            var games = (c!.Games ?? new List<string>())
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            items.Add(new GameCollection { Name = name, Games = games });
        }
        lock (_lock)
        {
            _items = items;
            _activeName = activeName == null ? null : FindLocked(activeName)?.Name;
        }
    }

    private GameCollection? FindLocked(string name)
    {
        var trimmed = name.Trim();
        return _items.FirstOrDefault(c => c.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
    }
}
