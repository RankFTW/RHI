// TunePresetStore.cs -- Tune presets data (pure, thread-safe, unit tested in TunePresetTests). The preset list
// (built-in "RHI Tuned" always first in a fresh list and always present) and each tuned game's record of what Tune did.
// No file or UI work: GameNameService.Tune.cs saves the snapshot in settings.json, MainWindow.Tune.cs does the work.

using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public sealed class TunePresetStore
{
    public const int MaxPresets = 10;
    public const int MaxNameLength = 24;
    public const string BuiltInId = "rhi-tuned";
    public const string BuiltInName = "RHI Tuned";

    private readonly object _lock = new();
    private List<TunePreset> _presets = new();
    private Dictionary<string, TunedGameState> _games = new(StringComparer.OrdinalIgnoreCase);

    public TunePresetStore() => Load(null);

    /// <summary>The built-in preset as shipped: ReShade, RenoDX, ReLimiter, newest DLSS / Streamline, SR M, RR F, FG B.</summary>
    public static TunePreset CreateBuiltIn() => new()
    {
        Id = BuiltInId,
        Name = BuiltInName,
        ReShade = true,
        HdrMod = TuneHdrMod.RenoDx,
        FrameLimiter = TuneLimiter.ReLimiter,
        DlssSr = TuneDll.Newest,
        DlssRr = TuneDll.Newest,
        DlssFg = TuneDll.Newest,
        Streamline = TuneDll.Newest,
        Nvidia = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [TuneItemKeys.SrPreset] = "13", // M
            [TuneItemKeys.RrPreset] = "6",  // F
            [TuneItemKeys.FgPreset] = "2",  // B
        },
    };

    public static bool IsBuiltIn(string? id) => BuiltInId.Equals(id, StringComparison.OrdinalIgnoreCase);

    /// <summary>Replaces everything with saved data (null = just the built-in preset).</summary>
    public void Load(TuneData? data)
    {
        lock (_lock)
        {
            _presets = new();
            foreach (var p in data?.Presets ?? new())
            {
                if (p == null || string.IsNullOrWhiteSpace(p.Id) || string.IsNullOrWhiteSpace(p.Name)) continue;
                if (_presets.Any(x => x.Id.Equals(p.Id, StringComparison.OrdinalIgnoreCase))) continue;
                if (_presets.Count >= MaxPresets) break;
                var c = p.Clone();
                c.Name = c.Name.Trim();
                _presets.Add(c);
            }
            // The built-in preset can't be deleted: put it back first if a save lost it
            if (!_presets.Any(p => IsBuiltIn(p.Id)))
            {
                if (_presets.Count >= MaxPresets) _presets.RemoveAt(_presets.Count - 1);
                _presets.Insert(0, CreateBuiltIn());
            }

            _games = new(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, st) in data?.Games ?? new())
            {
                if (string.IsNullOrWhiteSpace(key) || st == null) continue;
                _games[key] = CopyState(st);
            }
        }
    }

    /// <summary>A deep copy of everything, for saving.</summary>
    public TuneData Snapshot()
    {
        lock (_lock)
        {
            var d = new TuneData();
            foreach (var p in _presets) d.Presets.Add(p.Clone());
            foreach (var (k, st) in _games) d.Games[k] = CopyState(st);
            return d;
        }
    }

    // ── Presets ───────────────────────────────────────────────────────────────

    public IReadOnlyList<TunePreset> Presets
    {
        get { lock (_lock) return _presets.Select(p => p.Clone()).ToList(); }
    }

    /// <summary>Presets shown on game pages (not hidden), in order.</summary>
    public IReadOnlyList<TunePreset> Visible
    {
        get { lock (_lock) return _presets.Where(p => !p.Hidden).Select(p => p.Clone()).ToList(); }
    }

    public TunePreset? Get(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        lock (_lock) return Find(id)?.Clone();
    }

    /// <summary>Null when the name can be used (trimmed), else the reason.</summary>
    public string? ValidateName(string? name, string? exceptId)
    {
        var n = (name ?? "").Trim();
        if (n.Length == 0) return "Enter a name.";
        if (n.Length > MaxNameLength) return $"Use {MaxNameLength} characters or fewer.";
        lock (_lock)
            if (_presets.Any(p => p.Name.Equals(n, StringComparison.OrdinalIgnoreCase)
                                  && !p.Id.Equals(exceptId ?? "", StringComparison.OrdinalIgnoreCase)))
                return "A preset with that name already exists.";
        return null;
    }

    /// <summary>Adds a preset (a copy of <paramref name="copyFrom"/>'s settings, or RHI Tuned's); returns its id or null.</summary>
    public string? Add(string name, string? copyFrom = null)
    {
        if (ValidateName(name, null) != null) return null;
        lock (_lock)
        {
            if (_presets.Count >= MaxPresets) return null;
            var src = Find(copyFrom)?.Clone() ?? CreateBuiltIn();
            string id;
            do id = "t" + Guid.NewGuid().ToString("N")[..8];
            while (_presets.Any(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase)));
            src.Id = id;
            src.Name = name.Trim();
            src.Hidden = false;
            _presets.Add(src);
            return id;
        }
    }

    public bool Rename(string id, string name)
    {
        if (ValidateName(name, id) != null) return false;
        lock (_lock)
        {
            var p = Find(id);
            if (p == null) return false;
            p.Name = name.Trim();
            return true;
        }
    }

    /// <summary>Moves a preset up (-1) or down (+1).</summary>
    public bool Move(string id, int delta)
    {
        lock (_lock)
        {
            int i = _presets.FindIndex(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            int j = i + delta;
            if (i < 0 || j < 0 || j >= _presets.Count) return false;
            (_presets[i], _presets[j]) = (_presets[j], _presets[i]);
            return true;
        }
    }

    public bool SetHidden(string id, bool hidden)
    {
        lock (_lock)
        {
            var p = Find(id);
            if (p == null || p.Hidden == hidden) return false;
            p.Hidden = hidden;
            return true;
        }
    }

    /// <summary>Deletes a preset (never the built-in one). Tuned games keep their record, so they can still be untuned.</summary>
    public bool Delete(string id)
    {
        if (IsBuiltIn(id)) return false;
        lock (_lock) return _presets.RemoveAll(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) > 0;
    }

    /// <summary>Saves a preset's settings (id, name and hidden state are kept).</summary>
    public bool Update(TunePreset changed)
    {
        lock (_lock)
        {
            int i = _presets.FindIndex(p => p.Id.Equals(changed.Id, StringComparison.OrdinalIgnoreCase));
            if (i < 0) return false;
            var c = changed.Clone();
            c.Name = _presets[i].Name;
            c.Hidden = _presets[i].Hidden;
            _presets[i] = c;
            return true;
        }
    }

    /// <summary>Puts RHI Tuned's settings back to the shipped ones (keeps its name, place and hidden state).</summary>
    public bool ResetBuiltIn()
    {
        var fresh = CreateBuiltIn();
        lock (_lock)
        {
            int i = _presets.FindIndex(p => IsBuiltIn(p.Id));
            if (i < 0) return false;
            fresh.Name = _presets[i].Name;
            fresh.Hidden = _presets[i].Hidden;
            _presets[i] = fresh;
            return true;
        }
    }

    // ── Tuned games ───────────────────────────────────────────────────────────

    public TunedGameState? GameState(string gameKey)
    {
        lock (_lock) return _games.TryGetValue(gameKey, out var st) ? CopyState(st) : null;
    }

    public bool IsTuned(string gameKey)
    {
        lock (_lock) return _games.ContainsKey(gameKey);
    }

    public void SetGameState(string gameKey, TunedGameState state)
    {
        lock (_lock) _games[gameKey] = CopyState(state);
    }

    public bool ClearGame(string gameKey)
    {
        lock (_lock) return _games.Remove(gameKey);
    }

    /// <summary>How many games are tuned with a preset.</summary>
    public int CountTunedWith(string presetId)
    {
        lock (_lock) return _games.Values.Count(g => g.PresetId.Equals(presetId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A game was renamed (GameNameService.RenameGame): its record follows, for every store.</summary>
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

    private TunePreset? Find(string? id)
        => id == null ? null : _presets.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    private static TunedGameState CopyState(TunedGameState s) => new()
    {
        PresetId = s.PresetId ?? "",
        PresetName = s.PresetName ?? "",
        TunedUtc = s.TunedUtc,
        Installed = new List<string>(s.Installed ?? new()),
        Dlls = (s.Dlls ?? new()).Where(kv => kv.Value != null).ToDictionary(kv => kv.Key, kv => new TunedDll
        {
            Path = kv.Value.Path ?? "",
            HadBackup = kv.Value.HadBackup,
            PreviousVersion = kv.Value.PreviousVersion,
            NewVersion = kv.Value.NewVersion,
        }, StringComparer.OrdinalIgnoreCase),
        NvidiaBefore = new Dictionary<string, string>(s.NvidiaBefore ?? new(), StringComparer.OrdinalIgnoreCase),
        SettingsBefore = new Dictionary<string, string>(s.SettingsBefore ?? new(), StringComparer.OrdinalIgnoreCase),
        NrMethodInstalled = s.NrMethodInstalled,
    };
}
