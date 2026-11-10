// DisplayModeStore.cs -- Display modes data (pure, thread-safe, unit tested in DisplayModeStoreTests).
// Modes (Monitor, TV, ...) hold default values per item; each game ("GameName|Store") remembers its active mode,
// which items it swaps (or the default list) and its own value per mode. No file or UI work here:
// DisplayModeService reads and writes the game files, GameNameService saves the snapshot in settings.json.

using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public sealed class DisplayModeStore
{
    public const int MaxModes = 6;
    public const int MaxNameLength = 24;

    private readonly object _lock = new();
    private List<DisplayModeDefinition> _modes = new();
    private List<string> _defaultItems = new();
    private Dictionary<string, GameDisplayModeState> _games = new(StringComparer.OrdinalIgnoreCase);

    public DisplayModeStore() => Load(null);

    /// <summary>Monitor and TV, swapping peak brightness and the ReLimiter frame rate cap.</summary>
    public static DisplayModeData CreateDefaults() => new()
    {
        Modes =
        {
            new DisplayModeDefinition { Id = "monitor", Name = "Monitor" },
            new DisplayModeDefinition { Id = "tv", Name = "TV" },
        },
        DefaultItems = { DisplayModeItemKeys.RdxPeakNits, DisplayModeItemKeys.UlTargetFps },
    };

    /// <summary>Replaces everything with saved data (null or no modes = the defaults).</summary>
    public void Load(DisplayModeData? data)
    {
        if (data == null) data = CreateDefaults();
        else if (data.Modes == null || data.Modes.Count == 0)
        {
            var d = CreateDefaults();
            if (data.DefaultItems != null) d.DefaultItems = data.DefaultItems;
            if (data.Games != null) d.Games = data.Games;
            data = d;
        }
        lock (_lock)
        {
            _modes = new();
            foreach (var m in data.Modes)
            {
                if (m == null || string.IsNullOrWhiteSpace(m.Id) || string.IsNullOrWhiteSpace(m.Name)) continue;
                if (_modes.Any(x => x.Id.Equals(m.Id, StringComparison.OrdinalIgnoreCase))) continue;
                if (_modes.Count >= MaxModes) break;
                _modes.Add(new DisplayModeDefinition
                {
                    Id = m.Id,
                    Name = m.Name.Trim(),
                    Values = CopyValues(m.Values),
                });
            }
            if (_modes.Count == 0) _modes = CreateDefaults().Modes;
            _defaultItems = (data.DefaultItems ?? new()).Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            _games = new(StringComparer.OrdinalIgnoreCase);
            if (data.Games != null)
                foreach (var (key, st) in data.Games)
                    if (!string.IsNullOrWhiteSpace(key) && st != null) _games[key] = Copy(st);
        }
    }

    /// <summary>A deep copy of everything, for saving.</summary>
    public DisplayModeData Snapshot()
    {
        lock (_lock)
        {
            var d = new DisplayModeData
            {
                Modes = _modes.Select(m => new DisplayModeDefinition { Id = m.Id, Name = m.Name, Values = CopyValues(m.Values) }).ToList(),
                DefaultItems = _defaultItems.ToList(),
            };
            foreach (var (k, st) in _games)
                if (!IsEmpty(st)) d.Games[k] = Copy(st);
            return d;
        }
    }

    // ── Modes ─────────────────────────────────────────────────────────────────

    public IReadOnlyList<DisplayModeDefinition> Modes
    {
        get { lock (_lock) return _modes.Select(m => new DisplayModeDefinition { Id = m.Id, Name = m.Name, Values = CopyValues(m.Values) }).ToList(); }
    }

    public string? NameOf(string? modeId)
    {
        lock (_lock) return Find(modeId)?.Name;
    }

    /// <summary>Null if the name can be used for a mode (other than <paramref name="exceptId"/>), else why not.</summary>
    public string? ValidateName(string? name, string? exceptId = null)
    {
        var n = name?.Trim() ?? "";
        if (n.Length == 0) return "Enter a name.";
        if (n.Length > MaxNameLength) return $"Use {MaxNameLength} characters or fewer.";
        lock (_lock)
            if (_modes.Any(m => m.Name.Equals(n, StringComparison.OrdinalIgnoreCase)
                             && !m.Id.Equals(exceptId ?? "", StringComparison.OrdinalIgnoreCase)))
                return "A mode with that name already exists.";
        return null;
    }

    /// <summary>Adds a mode; returns its id, or null when the name is invalid or there are already <see cref="MaxModes"/>.</summary>
    public string? AddMode(string name)
    {
        if (ValidateName(name) != null) return null;
        lock (_lock)
        {
            if (_modes.Count >= MaxModes) return null;
            string id;
            do id = "m" + Guid.NewGuid().ToString("N")[..8];
            while (_modes.Any(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase)));
            _modes.Add(new DisplayModeDefinition { Id = id, Name = name.Trim() });
            return id;
        }
    }

    public bool RenameMode(string id, string name)
    {
        if (ValidateName(name, id) != null) return false;
        lock (_lock)
        {
            var m = Find(id);
            if (m == null) return false;
            m.Name = name.Trim();
            return true;
        }
    }

    /// <summary>Deletes a mode (at least one stays). Games on it go back to the first remaining mode; their values for it are dropped.</summary>
    public bool DeleteMode(string id)
    {
        lock (_lock)
        {
            if (_modes.Count <= 1) return false;
            var m = Find(id);
            if (m == null) return false;
            _modes.Remove(m);
            foreach (var st in _games.Values)
            {
                st.Values.Remove(m.Id);
                if (st.Mode != null && st.Mode.Equals(m.Id, StringComparison.OrdinalIgnoreCase)) st.Mode = _modes[0].Id;
            }
            return true;
        }
    }

    public bool MoveMode(string id, int delta)
    {
        lock (_lock)
        {
            var m = Find(id);
            if (m == null) return false;
            int i = _modes.IndexOf(m), j = i + delta;
            if (j < 0 || j >= _modes.Count || j == i) return false;
            _modes.RemoveAt(i);
            _modes.Insert(j, m);
            return true;
        }
    }

    public string? ModeDefault(string modeId, string item)
    {
        lock (_lock) return Find(modeId)?.Values.TryGetValue(item, out var v) == true ? v : null;
    }

    /// <summary>Sets (or with null clears) a mode's default value for an item.</summary>
    public void SetModeDefault(string modeId, string item, string? value)
    {
        lock (_lock)
        {
            var m = Find(modeId);
            if (m == null) return;
            if (value == null) m.Values.Remove(item); else m.Values[item] = value;
        }
    }

    // ── Default list ──────────────────────────────────────────────────────────

    public IReadOnlyList<string> DefaultItems
    {
        get { lock (_lock) return _defaultItems.ToList(); }
    }

    public void SetDefaultItems(IEnumerable<string> items)
    {
        lock (_lock) _defaultItems = items.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    // ── Games ─────────────────────────────────────────────────────────────────

    /// <summary>The game's active mode id (the first mode until it has been switched).</summary>
    public string ActiveModeId(string gameKey)
    {
        lock (_lock)
        {
            if (_games.TryGetValue(gameKey, out var st) && st.Mode != null && Find(st.Mode) != null) return Find(st.Mode)!.Id;
            return _modes[0].Id;
        }
    }

    /// <summary>True once the game has been switched (or set) to a mode: from then on its values are RHI-managed.</summary>
    public bool IsManaged(string gameKey)
    {
        lock (_lock) return _games.TryGetValue(gameKey, out var st) && st.Mode != null;
    }

    /// <summary>Items the game swaps: its own list, or the default list.</summary>
    public IReadOnlyList<string> ItemsFor(string gameKey)
    {
        lock (_lock)
            return _games.TryGetValue(gameKey, out var st) && st.Items != null ? st.Items.ToList() : _defaultItems.ToList();
    }

    public bool UsesDefaultItems(string gameKey)
    {
        lock (_lock) return !_games.TryGetValue(gameKey, out var st) || st.Items == null;
    }

    public bool Swaps(string gameKey, string item) => ItemsFor(gameKey).Contains(item, StringComparer.OrdinalIgnoreCase);

    /// <summary>Sets the game's own list (null = use the default list).</summary>
    public void SetItems(string gameKey, IEnumerable<string>? items)
    {
        lock (_lock)
        {
            var st = GetOrAdd(gameKey);
            st.Items = items?.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            Prune(gameKey);
        }
    }

    /// <summary>The game's own value for a mode, or null.</summary>
    public string? GameValue(string gameKey, string modeId, string item)
    {
        lock (_lock)
            return _games.TryGetValue(gameKey, out var st)
                && st.Values.TryGetValue(modeId, out var vals)
                && vals.TryGetValue(item, out var v) ? v : null;
    }

    /// <summary>The game's own value for a mode, else the mode's default, else null.</summary>
    public string? Effective(string gameKey, string modeId, string item)
        => GameValue(gameKey, modeId, item) ?? ModeDefault(modeId, item);

    /// <summary>Effective value for the game's active mode, or null when the game doesn't swap this item.</summary>
    public string? ActiveValue(string gameKey, string item)
        => Swaps(gameKey, item) ? Effective(gameKey, ActiveModeId(gameKey), item) : null;

    /// <summary>Sets (or with null clears) the game's own value for a mode.</summary>
    public void SetGameValue(string gameKey, string modeId, string item, string? value)
    {
        lock (_lock)
        {
            if (Find(modeId) == null) return;
            var st = GetOrAdd(gameKey);
            if (value == null)
            {
                if (st.Values.TryGetValue(modeId, out var vals))
                {
                    vals.Remove(item);
                    if (vals.Count == 0) st.Values.Remove(modeId);
                }
                Prune(gameKey);
                return;
            }
            if (!st.Values.TryGetValue(modeId, out var dict))
                st.Values[modeId] = dict = new(StringComparer.OrdinalIgnoreCase);
            dict[item] = value;
        }
    }

    /// <summary>Marks the game as being in <paramref name="modeId"/> and remembers its folders.</summary>
    public void SetActive(string gameKey, string modeId, IEnumerable<string>? folders)
    {
        lock (_lock)
        {
            if (Find(modeId) == null) return;
            var st = GetOrAdd(gameKey);
            st.Mode = modeId;
            var list = folders?.Where(f => !string.IsNullOrWhiteSpace(f)).Select(NormalizeFolder)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (list is { Count: > 0 }) st.Folders = list;
        }
    }

    /// <summary>
    /// Whether the game's value for <paramref name="item"/> was recorded when it entered its active mode, and what it was
    /// (null = couldn't be read then). False = never recorded (the game hasn't been switched since RHI started tracking).
    /// </summary>
    public bool TryGetEntry(string gameKey, string item, out string? value)
    {
        lock (_lock)
        {
            value = null;
            return _games.TryGetValue(gameKey, out var st) && st.Entry != null && st.Entry.TryGetValue(item, out value);
        }
    }

    /// <summary>Replaces the values recorded when the game entered its active mode.</summary>
    public void SetEntries(string gameKey, IDictionary<string, string?> entries)
    {
        lock (_lock)
            GetOrAdd(gameKey).Entry = new Dictionary<string, string?>(entries, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Updates one recorded entry value (an edit written straight to the game).</summary>
    public void SetEntry(string gameKey, string item, string? value)
    {
        lock (_lock)
        {
            var st = GetOrAdd(gameKey);
            st.Entry ??= new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            st.Entry[item] = value;
        }
    }

    /// <summary>The managed game whose install or addon folder is <paramref name="folder"/>, or null.</summary>
    public string? FindManagedByFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return null;
        var f = NormalizeFolder(folder);
        lock (_lock)
            foreach (var (k, st) in _games)
                if (st.Mode != null && st.Folders != null && st.Folders.Contains(f, StringComparer.OrdinalIgnoreCase)) return k;
        return null;
    }

    /// <summary>Forgets everything about one game (Reset Overrides).</summary>
    public bool RemoveGame(string gameKey)
    {
        lock (_lock) return _games.Remove(gameKey);
    }

    public bool HasGameState(string gameKey)
    {
        lock (_lock) return _games.ContainsKey(gameKey);
    }

    /// <summary>Moves a renamed game's state ("Old|Store" → "New|Store") for every store.</summary>
    public void MigrateGameRename(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName)) return;
        lock (_lock)
        {
            foreach (var key in _games.Keys.ToList())
            {
                var parsed = GameKey.Parse(key);
                if (!parsed.MatchesName(oldName)) continue;
                var st = _games[key];
                _games.Remove(key);
                _games[new GameKey(newName, parsed.Store).ToKey()] = st;
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private DisplayModeDefinition? Find(string? id)
        => id == null ? null : _modes.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private GameDisplayModeState GetOrAdd(string key)
    {
        if (!_games.TryGetValue(key, out var st)) _games[key] = st = new GameDisplayModeState();
        return st;
    }

    private void Prune(string key)
    {
        if (_games.TryGetValue(key, out var st) && IsEmpty(st)) _games.Remove(key);
    }

    private static bool IsEmpty(GameDisplayModeState st) => st.Mode == null && st.Items == null && st.Values.Count == 0 && st.Entry == null;

    public static string NormalizeFolder(string folder) => folder.Replace('/', '\\').TrimEnd('\\');

    private static Dictionary<string, string> CopyValues(Dictionary<string, string>? v)
        => v == null ? new(StringComparer.OrdinalIgnoreCase) : new(v.Where(kv => kv.Key != null && kv.Value != null), StringComparer.OrdinalIgnoreCase);

    private static GameDisplayModeState Copy(GameDisplayModeState st)
    {
        var c = new GameDisplayModeState
        {
            Mode = st.Mode,
            Items = st.Items?.ToList(),
            Folders = st.Folders?.ToList(),
            Entry = st.Entry == null ? null : new Dictionary<string, string?>(st.Entry, StringComparer.OrdinalIgnoreCase),
        };
        if (st.Values != null)
            foreach (var (mode, vals) in st.Values)
                if (mode != null && vals != null) c.Values[mode] = CopyValues(vals);
        return c;
    }
}
