// DisplayModeStore.cs -- Display modes data (pure, thread-safe, unit tested in DisplayModeStoreTests).
// Modes (Monitor, TV, ...) hold values per item, used by every game. Each game ("GameName|Store") remembers its
// active mode and can have a Custom mode of its own (made in its ⚙: the ticked settings and their values), shown only
// for that game. A game swaps every item a mode has a value for, plus its Custom mode's. A game in no mode is Off (Mode = null):
// it follows the global settings, and global writers don't skip it. No file or UI work here:
// DisplayModeService reads and writes the game files, GameNameService saves the snapshot in settings.json.

using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public sealed class DisplayModeStore
{
    public const int MaxModes = 6;
    public const int MaxNameLength = 24;
    /// <summary>Id of a game's own Custom mode (never a global mode id: those are monitor / tv or "m" + hex).</summary>
    public const string CustomId = "custom";
    public const string CustomName = "Custom";

    private readonly object _lock = new();
    private List<DisplayModeDefinition> _modes = new();
    private Dictionary<string, GameDisplayModeState> _games = new(StringComparer.OrdinalIgnoreCase);

    public DisplayModeStore() => Load(null);

    /// <summary>Monitor and TV, with no values yet.</summary>
    public static DisplayModeData CreateDefaults() => new()
    {
        Modes =
        {
            new DisplayModeDefinition { Id = "monitor", Name = "Monitor" },
            new DisplayModeDefinition { Id = "tv", Name = "TV" },
        },
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
            _modes.RemoveAll(m => IsCustom(m.Id));
            if (_modes.Count == 0) _modes = CreateDefaults().Modes;
            _games = new(StringComparer.OrdinalIgnoreCase);
            if (data.Games != null)
                foreach (var (key, st) in data.Games)
                {
                    if (string.IsNullOrWhiteSpace(key) || st == null) continue;
                    var c = Copy(st);
                    // Older builds kept own values per global mode and a ticked list without a Custom mode: dropped
                    foreach (var mode in c.Values.Keys.Where(m => !IsCustom(m)).ToList()) c.Values.Remove(mode);
                    if (!c.Custom) { c.Items = null; c.Values.Clear(); }
                    if (IsCustom(c.Mode) && !c.Custom) ClearActive(c);
                    if (!IsEmpty(c)) _games[key] = c;
                }
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
        if (IsCustom(modeId)) return CustomName;
        lock (_lock) return Find(modeId)?.Name;
    }

    public static bool IsCustom(string? modeId) => modeId != null && modeId.Equals(CustomId, StringComparison.OrdinalIgnoreCase);

    /// <summary>The modes this game can switch to: the global ones, then its Custom mode if it has one.</summary>
    public IReadOnlyList<DisplayModeDefinition> ModesFor(string gameKey)
    {
        var list = Modes.ToList();
        if (HasCustom(gameKey)) list.Add(new DisplayModeDefinition { Id = CustomId, Name = CustomName });
        return list;
    }

    /// <summary>Null if the name can be used for a mode (other than <paramref name="exceptId"/>), else why not.</summary>
    public string? ValidateName(string? name, string? exceptId = null)
    {
        var n = name?.Trim() ?? "";
        if (n.Length == 0) return "Enter a name.";
        if (n.Length > MaxNameLength) return $"Use {MaxNameLength} characters or fewer.";
        if (n.Equals(CustomName, StringComparison.OrdinalIgnoreCase)) return "Custom is the name of a game's own mode. Pick another name.";
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

    /// <summary>Deletes a mode (at least one stays). Games still in it are turned off; their values for it are dropped.</summary>
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
                if (st.Mode != null && st.Mode.Equals(m.Id, StringComparison.OrdinalIgnoreCase)) ClearActive(st);
            }
            foreach (var k in _games.Keys.ToList()) Prune(k);
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

    // ── Items with a mode value ───────────────────────────────────────────────

    /// <summary>Items any mode has a value for (set in Settings). Every game swaps these, ticked or not.</summary>
    public IReadOnlyList<string> ModeValueItems
    {
        get { lock (_lock) return ModeValueItemsLocked(); }
    }

    private List<string> ModeValueItemsLocked()
        => _modes.SelectMany(m => m.Values.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    // ── Games ─────────────────────────────────────────────────────────────────

    /// <summary>The game's active mode id, or null when it's Off (not in a mode: it follows the global settings).</summary>
    public string? ActiveModeId(string gameKey)
    {
        lock (_lock)
        {
            if (!_games.TryGetValue(gameKey, out var st) || st.Mode == null) return null;
            if (IsCustom(st.Mode)) return st.Custom ? CustomId : null;
            return Find(st.Mode)?.Id;
        }
    }

    /// <summary>True while the game is in a mode: its swapped values are RHI-managed (global writers keep them).</summary>
    public bool IsManaged(string gameKey) => ActiveModeId(gameKey) != null;

    /// <summary>Items the game swaps: everything a mode has a value for, plus what its Custom mode changes.</summary>
    public IReadOnlyList<string> ItemsFor(string gameKey)
    {
        lock (_lock)
        {
            var list = ModeValueItemsLocked();
            foreach (var k in CustomItemsLocked(gameKey))
                if (!list.Contains(k, StringComparer.OrdinalIgnoreCase)) list.Add(k);
            return list;
        }
    }

    public bool Swaps(string gameKey, string item) => ItemsFor(gameKey).Contains(item, StringComparer.OrdinalIgnoreCase);

    // ── A game's Custom mode ──────────────────────────────────────────────────

    public bool HasCustom(string gameKey)
    {
        lock (_lock) return _games.TryGetValue(gameKey, out var st) && st.Custom;
    }

    /// <summary>Makes the game's Custom mode (empty: it changes nothing until settings are ticked).</summary>
    public void CreateCustom(string gameKey)
    {
        lock (_lock) GetOrAdd(gameKey).Custom = true;
    }

    /// <summary>Removes the game's Custom mode. If the game was in it, it's set to Off (write its normal values first).</summary>
    public void DeleteCustom(string gameKey)
    {
        lock (_lock)
        {
            if (!_games.TryGetValue(gameKey, out var st)) return;
            st.Custom = false;
            st.Items = null;
            st.Values.Remove(CustomId);
            if (IsCustom(st.Mode)) ClearActive(st);
            Prune(gameKey);
        }
    }

    /// <summary>Settings the game's Custom mode changes.</summary>
    public IReadOnlyList<string> CustomItems(string gameKey)
    {
        lock (_lock) return CustomItemsLocked(gameKey);
    }

    private List<string> CustomItemsLocked(string gameKey)
        => _games.TryGetValue(gameKey, out var st) && st.Custom && st.Items != null ? st.Items.ToList() : new List<string>();

    public bool InCustom(string gameKey, string item) => CustomItems(gameKey).Contains(item, StringComparer.OrdinalIgnoreCase);

    /// <summary>Ticks or unticks a setting in the game's Custom mode (its value is kept while unticked).</summary>
    public void SetInCustom(string gameKey, string item, bool on)
    {
        lock (_lock)
        {
            if (!_games.TryGetValue(gameKey, out var st) || !st.Custom) return;
            st.Items ??= new();
            st.Items.RemoveAll(k => k.Equals(item, StringComparison.OrdinalIgnoreCase));
            if (on) st.Items.Add(item);
        }
    }

    /// <summary>The Custom mode's value for a setting, or null.</summary>
    public string? CustomValue(string gameKey, string item)
    {
        lock (_lock)
            return _games.TryGetValue(gameKey, out var st)
                && st.Values.TryGetValue(CustomId, out var vals)
                && vals.TryGetValue(item, out var v) ? v : null;
    }

    /// <summary>Sets (or with null clears) the Custom mode's value for a setting.</summary>
    public void SetCustomValue(string gameKey, string item, string? value)
    {
        lock (_lock)
        {
            if (!_games.TryGetValue(gameKey, out var st) || !st.Custom) return;
            if (value == null)
            {
                if (st.Values.TryGetValue(CustomId, out var vals)) vals.Remove(item);
                return;
            }
            if (!st.Values.TryGetValue(CustomId, out var dict))
                st.Values[CustomId] = dict = new(StringComparer.OrdinalIgnoreCase);
            dict[item] = value;
        }
    }

    /// <summary>
    /// What the game gets for an item in a mode: in its Custom mode the value it set (ticked settings only), in a
    /// global mode that mode's value. Null = none (the game's normal value).
    /// </summary>
    public string? Effective(string gameKey, string modeId, string item)
        => IsCustom(modeId)
            ? (InCustom(gameKey, item) ? CustomValue(gameKey, item) : null)
            : ModeDefault(modeId, item);

    /// <summary>Effective value for the game's active mode, or null when it's Off / doesn't swap this item.</summary>
    public string? ActiveValue(string gameKey, string item)
        => Swaps(gameKey, item) && ActiveModeId(gameKey) is { } mode ? Effective(gameKey, mode, item) : null;

    /// <summary>Marks the game as being in <paramref name="modeId"/> and remembers its folders.</summary>
    public void SetActive(string gameKey, string modeId, IEnumerable<string>? folders)
    {
        lock (_lock)
        {
            bool custom = IsCustom(modeId);
            if (custom ? !(_games.TryGetValue(gameKey, out var cs) && cs.Custom) : Find(modeId) == null) return;
            var st = GetOrAdd(gameKey);
            st.Mode = custom ? CustomId : modeId;
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

    /// <summary>The game's value from before it entered a mode (null = none recorded).</summary>
    public string? OriginalValue(string gameKey, string item)
    {
        lock (_lock)
            return _games.TryGetValue(gameKey, out var st) && st.Original != null && st.Original.TryGetValue(item, out var v) ? v : null;
    }

    /// <summary>Records (or with null forgets) the game's value from before it entered a mode.</summary>
    public void SetOriginal(string gameKey, string item, string? value)
    {
        lock (_lock)
        {
            var st = GetOrAdd(gameKey);
            if (value == null)
            {
                st.Original?.Remove(item);
                if (st.Original is { Count: 0 }) st.Original = null;
                Prune(gameKey);
                return;
            }
            st.Original ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            st.Original[item] = value;
        }
    }

    /// <summary>Turns the game's mode off: no active mode, and the entry / before-modes records are dropped.</summary>
    public void SetOff(string gameKey)
    {
        lock (_lock)
        {
            if (!_games.TryGetValue(gameKey, out var st)) return;
            ClearActive(st);
            Prune(gameKey);
        }
    }

    /// <summary>Keys of the games in <paramref name="modeId"/>.</summary>
    public IReadOnlyList<string> GamesIn(string modeId)
    {
        lock (_lock)
            return _games.Where(kv => kv.Value.Mode != null && kv.Value.Mode.Equals(modeId, StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Key).ToList();
    }

    /// <summary>Keys of every game that's in a mode.</summary>
    public IReadOnlyList<string> ManagedGames()
    {
        lock (_lock)
            return _games.Where(kv => kv.Value.Mode != null && (Find(kv.Value.Mode) != null || (IsCustom(kv.Value.Mode) && kv.Value.Custom)))
                .Select(kv => kv.Key).ToList();
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

    private static void ClearActive(GameDisplayModeState st)
    {
        st.Mode = null;
        st.Entry = null;
        st.Original = null;
    }

    private void Prune(string key)
    {
        if (_games.TryGetValue(key, out var st) && IsEmpty(st)) _games.Remove(key);
    }

    private static bool IsEmpty(GameDisplayModeState st) => st.Mode == null && !st.Custom && st.Items == null && st.Values.Count == 0 && st.Entry == null && st.Original == null;

    public static string NormalizeFolder(string folder) => folder.Replace('/', '\\').TrimEnd('\\');

    private static Dictionary<string, string> CopyValues(Dictionary<string, string>? v)
        => v == null ? new(StringComparer.OrdinalIgnoreCase) : new(v.Where(kv => kv.Key != null && kv.Value != null), StringComparer.OrdinalIgnoreCase);

    private static GameDisplayModeState Copy(GameDisplayModeState st)
    {
        var c = new GameDisplayModeState
        {
            Mode = st.Mode,
            Custom = st.Custom,
            Items = st.Items?.ToList(),
            Folders = st.Folders?.ToList(),
            Entry = st.Entry == null ? null : new Dictionary<string, string?>(st.Entry, StringComparer.OrdinalIgnoreCase),
            Original = st.Original == null ? null : CopyValues(st.Original),
        };
        if (st.Values != null)
            foreach (var (mode, vals) in st.Values)
                if (mode != null && vals != null) c.Values[mode] = CopyValues(vals);
        return c;
    }
}
