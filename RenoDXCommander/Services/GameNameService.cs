using System.Text.Json;
using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Services;

/// <summary>
/// Owns name mappings, game renames, wiki exclusions, and settings persistence.
/// Extracted from MainViewModel per Requirement 1.5.
/// </summary>
public class GameNameService : IGameNameService
{
    private readonly IGameDetectionService _gameDetectionService;
    private readonly IModInstallService _installer;
    private readonly IAuxInstallService _auxInstaller;
    private readonly ILumaService _lumaService;

    // ── Persisted data ────────────────────────────────────────────────────────
    private Dictionary<string, string> _nameMappings = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _gameRenames = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _wikiExclusions = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _hiddenGames = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _favouriteGames = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _ueExtendedGames = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, int> _perGameDcModeOverride = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _updateAllExcludedReShade = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _updateAllExcludedDc = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _updateAllExcludedRenoDx = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _perGameShaderMode = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<string>> _perGameShaderSelection = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _lumaEnabledGames = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _lumaDisabledGames = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _folderOverrides = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _vulkanRenderingPaths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Maps current (renamed) game name → original store-detected name.</summary>
    private Dictionary<string, string> _originalDetectedNames = new(StringComparer.OrdinalIgnoreCase);

    // ── Public accessors for MainViewModel ────────────────────────────────────
    public Dictionary<string, string> NameMappings => _nameMappings;
    public Dictionary<string, string> GameRenames => _gameRenames;
    public HashSet<string> WikiExclusions => _wikiExclusions;
    public HashSet<string> HiddenGames => _hiddenGames;
    public HashSet<string> FavouriteGames => _favouriteGames;
    public HashSet<string> UeExtendedGames => _ueExtendedGames;
    public Dictionary<string, int> PerGameDcModeOverride => _perGameDcModeOverride;
    public HashSet<string> UpdateAllExcludedReShade => _updateAllExcludedReShade;
    public HashSet<string> UpdateAllExcludedDc => _updateAllExcludedDc;
    public HashSet<string> UpdateAllExcludedRenoDx => _updateAllExcludedRenoDx;
    public Dictionary<string, string> PerGameShaderMode => _perGameShaderMode;
    public Dictionary<string, List<string>> PerGameShaderSelection => _perGameShaderSelection;
    public HashSet<string> LumaEnabledGames => _lumaEnabledGames;
    public HashSet<string> LumaDisabledGames => _lumaDisabledGames;
    public Dictionary<string, string> FolderOverrides => _folderOverrides;
    /// <summary>Per-game Vulkan rendering path preferences. Key = game name, Value = "DirectX" or "Vulkan".</summary>
    public Dictionary<string, string> VulkanRenderingPaths => _vulkanRenderingPaths;
    public Dictionary<string, string> OriginalDetectedNames => _originalDetectedNames;

    public GameNameService(
        IGameDetectionService gameDetectionService,
        IModInstallService installer,
        IAuxInstallService auxInstaller,
        ILumaService lumaService)
    {
        _gameDetectionService = gameDetectionService;
        _installer = installer;
        _auxInstaller = auxInstaller;
        _lumaService = lumaService;
    }

    // ── Load / Save ───────────────────────────────────────────────────────────

    /// <summary>
    /// Loads all name mappings and settings from the persisted settings file.
    /// Returns the loaded settings dictionary for further processing by callers.
    /// </summary>
    public Dictionary<string, string> LoadNameMappings(
        IDllOverrideService dllOverrideService,
        SettingsViewModel settingsViewModel,
        Action<int> setDcModeLevel,
        Action<bool> setIsGridLayout)
    {
        _nameMappings              = new(StringComparer.OrdinalIgnoreCase);
        _wikiExclusions            = new(StringComparer.OrdinalIgnoreCase);
        _ueExtendedGames           = new(StringComparer.OrdinalIgnoreCase);
        _perGameDcModeOverride     = new(StringComparer.OrdinalIgnoreCase);
        _updateAllExcludedReShade  = new(StringComparer.OrdinalIgnoreCase);
        _updateAllExcludedDc       = new(StringComparer.OrdinalIgnoreCase);
        _updateAllExcludedRenoDx   = new(StringComparer.OrdinalIgnoreCase);
        _perGameShaderMode         = new(StringComparer.OrdinalIgnoreCase);
        _perGameShaderSelection    = new(StringComparer.OrdinalIgnoreCase);
        _gameRenames            = new(StringComparer.OrdinalIgnoreCase);
        _folderOverrides        = new(StringComparer.OrdinalIgnoreCase);
        _vulkanRenderingPaths   = new(StringComparer.OrdinalIgnoreCase);
        _lumaEnabledGames       = new(StringComparer.OrdinalIgnoreCase);
        _lumaDisabledGames      = new(StringComparer.OrdinalIgnoreCase);
        _hiddenGames            ??= new(StringComparer.OrdinalIgnoreCase);
        _favouriteGames         ??= new(StringComparer.OrdinalIgnoreCase);

        Dictionary<string, string> s;
        try { s = SettingsViewModel.LoadSettingsFile(); }
        catch (Exception ex)
        {
            CrashReporter.Log($"[GameNameService.LoadNameMappings] Settings file unreadable — {ex.Message}");
            return new(StringComparer.OrdinalIgnoreCase);
        }

        T Load<T>(string key, T fallback)
        {
            try
            {
                if (s.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v))
                    return JsonSerializer.Deserialize<T>(v) ?? fallback;
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[GameNameService.LoadNameMappings] Key '{key}' failed — {ex.Message}");
            }
            return fallback;
        }

        _nameMappings = new(Load<Dictionary<string, string>>("NameMappings",
            new(StringComparer.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase);

        _wikiExclusions = new HashSet<string>(
            Load<List<string>>("WikiExclusions", new()), StringComparer.OrdinalIgnoreCase);

        _ueExtendedGames = new HashSet<string>(
            Load<List<string>>("UeExtendedGames", new()), StringComparer.OrdinalIgnoreCase);

        if (s.TryGetValue("DcModeLevel", out var dcLvl) && int.TryParse(dcLvl, out var parsedLvl))
            setDcModeLevel(parsedLvl);
        else if (s.TryGetValue("DcModeEnabled", out var dcMode))
            setDcModeLevel(dcMode.Equals("True", StringComparison.OrdinalIgnoreCase) ? 1 : 0);

        var pgdmDict = Load<Dictionary<string, int>?>("PerGameDcModeOverride", null);
        if (pgdmDict != null)
        {
            _perGameDcModeOverride = new(pgdmDict, StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            var oldExcluded = Load<List<string>>("DcModeExcluded", new());
            _perGameDcModeOverride = new(StringComparer.OrdinalIgnoreCase);
            foreach (var name in oldExcluded) _perGameDcModeOverride[name] = 0;
        }

        _updateAllExcludedReShade = new HashSet<string>(
            Load<List<string>>("UpdateAllExcludedReShade", new()), StringComparer.OrdinalIgnoreCase);
        _updateAllExcludedDc = new HashSet<string>(
            Load<List<string>>("UpdateAllExcludedDc", new()), StringComparer.OrdinalIgnoreCase);
        _updateAllExcludedRenoDx = new HashSet<string>(
            Load<List<string>>("UpdateAllExcludedRenoDx", new()), StringComparer.OrdinalIgnoreCase);

        // Legacy migration: if old key exists and all three new sets are empty, copy legacy entries into all three
        var legacy = Load<List<string>>("UpdateAllExcluded", new());
        if (legacy.Count > 0 && _updateAllExcludedReShade.Count == 0
            && _updateAllExcludedDc.Count == 0 && _updateAllExcludedRenoDx.Count == 0)
        {
            foreach (var name in legacy)
            {
                _updateAllExcludedReShade.Add(name);
                _updateAllExcludedDc.Add(name);
                _updateAllExcludedRenoDx.Add(name);
            }
        }

        var pgsmDict = Load<Dictionary<string, string>?>("PerGameShaderMode", null);
        if (pgsmDict != null)
        {
            _perGameShaderMode = new(pgsmDict, StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            var oldList = Load<List<string>>("ShaderExcluded", new());
            _perGameShaderMode = new(StringComparer.OrdinalIgnoreCase);
            foreach (var name in oldList) _perGameShaderMode[name] = "Off";
        }

        var pgssDict = Load<Dictionary<string, List<string>>?>("PerGameShaderSelection", null);
        if (pgssDict != null)
        {
            _perGameShaderSelection = new(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in pgssDict)
                _perGameShaderSelection[kv.Key] = kv.Value;
        }

        settingsViewModel.LoadSettingsFromDict(s);

        _lumaEnabledGames = new HashSet<string>(
            Load<List<string>>("LumaEnabledGames", new()), StringComparer.OrdinalIgnoreCase);

        _lumaDisabledGames = new HashSet<string>(
            Load<List<string>>("LumaDisabledGames", new()), StringComparer.OrdinalIgnoreCase);

        _gameRenames = new(Load<Dictionary<string, string>>("GameRenames",
            new(StringComparer.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase);

        var dllOverrides = new Dictionary<string, DllOverrideConfig>(Load<Dictionary<string, DllOverrideConfig>>("DllOverrides",
            new(StringComparer.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase);
        var manifestOptOuts = new HashSet<string>(
            Load<List<string>>("ManifestDllOptOuts", new()), StringComparer.OrdinalIgnoreCase);
        dllOverrideService.SetOverridesFromSettings(dllOverrides, manifestOptOuts);

        _folderOverrides = new(Load<Dictionary<string, string>>("FolderOverrides",
            new(StringComparer.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase);

        _vulkanRenderingPaths = new(Load<Dictionary<string, string>>("VulkanRenderingPaths",
            new(StringComparer.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase);

        _hiddenGames = new HashSet<string>(
            Load<List<string>>("HiddenGames", _hiddenGames?.ToList() ?? new()), StringComparer.OrdinalIgnoreCase);

        _favouriteGames = new HashSet<string>(
            Load<List<string>>("FavouriteGames", _favouriteGames?.ToList() ?? new()), StringComparer.OrdinalIgnoreCase);

        if (s.TryGetValue("GridLayout", out var glVal))
            setIsGridLayout(glVal == "1");

        CrashReporter.Log($"[GameNameService.LoadNameMappings] Loaded {_gameRenames.Count} renames, {dllOverrides.Count} DLL overrides, {_folderOverrides.Count} folder overrides");

        return s;
    }

    /// <summary>Persists all settings to disk.</summary>
    public void SaveNameMappings(
        IDllOverrideService dllOverrideService,
        SettingsViewModel settingsViewModel,
        int dcModeLevel,
        bool isGridLayout,
        bool isLoadingSettings)
    {
        if (isLoadingSettings) return;

        // Retry with short delays to handle file contention from concurrent background tasks
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var s = SettingsViewModel.LoadSettingsFile();
                s["NameMappings"]    = JsonSerializer.Serialize(_nameMappings);
                s["WikiExclusions"]  = JsonSerializer.Serialize(_wikiExclusions.ToList());
                s["UeExtendedGames"] = JsonSerializer.Serialize(_ueExtendedGames.ToList());
                s["DcModeLevel"]     = dcModeLevel.ToString();
                s["PerGameDcModeOverride"]  = JsonSerializer.Serialize(_perGameDcModeOverride);
                s["UpdateAllExcludedReShade"] = JsonSerializer.Serialize(_updateAllExcludedReShade.ToList());
                s["UpdateAllExcludedDc"]      = JsonSerializer.Serialize(_updateAllExcludedDc.ToList());
                s["UpdateAllExcludedRenoDx"]  = JsonSerializer.Serialize(_updateAllExcludedRenoDx.ToList());
                s.Remove("UpdateAllExcluded");
                s["PerGameShaderMode"]    = JsonSerializer.Serialize(_perGameShaderMode);
                s["PerGameShaderSelection"] = JsonSerializer.Serialize(_perGameShaderSelection);
                settingsViewModel.SaveSettingsToDict(s);
                s["LumaEnabledGames"]   = JsonSerializer.Serialize(_lumaEnabledGames.ToList());
                s["LumaDisabledGames"]  = JsonSerializer.Serialize(_lumaDisabledGames.ToList());
                s["GameRenames"]         = JsonSerializer.Serialize(_gameRenames);
                s["DllOverrides"]        = JsonSerializer.Serialize(dllOverrideService.GetUserOverridesForSave());
                s["ManifestDllOptOuts"]  = JsonSerializer.Serialize(dllOverrideService.ManifestDllOverrideOptOuts.ToList());
                s["FolderOverrides"]     = JsonSerializer.Serialize(_folderOverrides);
                s["VulkanRenderingPaths"] = JsonSerializer.Serialize(_vulkanRenderingPaths);
                s["HiddenGames"]         = JsonSerializer.Serialize(_hiddenGames?.ToList() ?? new List<string>());
                s["FavouriteGames"]      = JsonSerializer.Serialize(_favouriteGames?.ToList() ?? new List<string>());
                s["GridLayout"]          = isGridLayout ? "1" : "0";
                SettingsViewModel.SaveSettingsFile(s);
                return;
            }
            catch (IOException) when (attempt < 2)
            {
                Thread.Sleep(50 * (attempt + 1)); // 50ms, 100ms
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[GameNameService.SaveNameMappings] Failed to save settings — {ex.Message}");
                return;
            }
        }
    }

    // ── Name mapping CRUD ─────────────────────────────────────────────────────

    public void AddNameMapping(string detectedName, string wikiKey)
    {
        if (string.IsNullOrWhiteSpace(detectedName) || string.IsNullOrWhiteSpace(wikiKey)) return;
        _nameMappings[detectedName] = wikiKey;
    }

    public string? GetNameMapping(string detectedName)
    {
        if (string.IsNullOrWhiteSpace(detectedName)) return null;
        if (_nameMappings.TryGetValue(detectedName, out var v)) return v;
        var norm = _gameDetectionService.NormalizeName(detectedName);
        foreach (var kv in _nameMappings)
            if (_gameDetectionService.NormalizeName(kv.Key) == norm) return kv.Value;
        return null;
    }

    public void RemoveNameMapping(string detectedName)
    {
        if (string.IsNullOrWhiteSpace(detectedName)) return;
        _nameMappings.Remove(detectedName);
        var norm = _gameDetectionService.NormalizeName(detectedName);
        var toRemove = _nameMappings.Keys
            .Where(k => _gameDetectionService.NormalizeName(k) == norm).ToList();
        foreach (var k in toRemove) _nameMappings.Remove(k);
    }

    // ── Game renames ──────────────────────────────────────────────────────────

    /// <summary>
    /// Renames a game everywhere: card, detected game, all settings HashSets/Dicts,
    /// persisted install records, and library file.
    /// </summary>
    public void RenameGame(string oldName, string newName,
        List<GameCardViewModel> allCards,
        List<DetectedGame> manualGames,
        IDllOverrideService dllOverrideService)
    {
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName)) return;
        if (oldName.Equals(newName, StringComparison.OrdinalIgnoreCase)) return;

        var card = allCards.FirstOrDefault(c =>
            c.GameName.Equals(oldName, StringComparison.OrdinalIgnoreCase));
        if (card != null)
        {
            card.GameName = newName;
            if (card.DetectedGame != null)
                card.DetectedGame.Name = newName;

            if (!string.IsNullOrEmpty(card.InstallPath))
            {
                var key = card.InstallPath.TrimEnd(Path.DirectorySeparatorChar);
                _gameRenames[key] = newName;
            }

            if (_folderOverrides.TryGetValue(oldName, out var ovStored))
            {
                var parts = ovStored.Split('|');
                var origPath = parts.Length > 1 ? parts[1] : "";
                if (!string.IsNullOrEmpty(origPath))
                {
                    var origKey = origPath.TrimEnd(Path.DirectorySeparatorChar);
                    _gameRenames[origKey] = newName;
                }
            }
        }

        // Migrate all game-name-keyed HashSets
        MigrateHashSet(_hiddenGames, oldName, newName);
        MigrateHashSet(_favouriteGames, oldName, newName);
        MigrateHashSet(_wikiExclusions, oldName, newName);
        MigrateHashSet(_ueExtendedGames, oldName, newName);
        MigrateDict(_perGameDcModeOverride, oldName, newName);
        MigrateHashSet(_updateAllExcludedReShade, oldName, newName);
        MigrateHashSet(_updateAllExcludedDc, oldName, newName);
        MigrateHashSet(_updateAllExcludedRenoDx, oldName, newName);
        MigrateHashSet(_lumaEnabledGames, oldName, newName);
        MigrateHashSet(_lumaDisabledGames, oldName, newName);

        // Migrate game-name-keyed Dictionaries
        MigrateDict(_perGameShaderMode, oldName, newName);
        MigrateDict(_perGameShaderSelection, oldName, newName);
        MigrateDict(_nameMappings, oldName, newName);

        // Migrate DLL override config
        dllOverrideService.MigrateOverride(oldName, newName);

        // Migrate folder override
        MigrateDict(_folderOverrides, oldName, newName);

        // Migrate manual games list
        var manualGame = manualGames.FirstOrDefault(g =>
            g.Name.Equals(oldName, StringComparison.OrdinalIgnoreCase));
        if (manualGame != null)
            manualGame.Name = newName;

        // Update persisted install records (RenoDX mod)
        if (card?.InstalledRecord != null)
        {
            _installer.RemoveRecord(card.InstalledRecord);
            card.InstalledRecord.GameName = newName;
            _installer.SaveRecordPublic(card.InstalledRecord);
        }

        // Update persisted aux records (DC / ReShade)
        if (card?.DcRecord != null)
        {
            _auxInstaller.RemoveRecord(card.DcRecord);
            card.DcRecord.GameName = newName;
            _auxInstaller.SaveAuxRecord(card.DcRecord);
        }
        if (card?.RsRecord != null)
        {
            _auxInstaller.RemoveRecord(card.RsRecord);
            card.RsRecord.GameName = newName;
            _auxInstaller.SaveAuxRecord(card.RsRecord);
        }

        // Update persisted Luma record
        if (card?.LumaRecord != null)
        {
            _lumaService.RemoveLumaRecord(card.LumaRecord.GameName, card.LumaRecord.InstallPath);
            card.LumaRecord.GameName = newName;
            _lumaService.SaveLumaRecord(card.LumaRecord);
        }

        card?.NotifyAll();
    }

    public string? GetOriginalStoreName(string currentName)
    {
        if (_originalDetectedNames.TryGetValue(currentName, out var orig))
            return orig;
        return null;
    }

    public void RemoveGameRename(string gameName, List<GameCardViewModel> allCards)
    {
        var card = allCards.FirstOrDefault(c =>
            c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase));
        if (card == null) return;

        var keysToRemove = _gameRenames
            .Where(kv => kv.Value.Equals(gameName, StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key).ToList();
        foreach (var k in keysToRemove)
            _gameRenames.Remove(k);
    }

    // ── Apply methods ─────────────────────────────────────────────────────────

    public void ApplyGameRenames(List<DetectedGame> games)
    {
        if (_gameRenames.Count == 0) return;
        foreach (var g in games)
        {
            var key = g.InstallPath.TrimEnd(Path.DirectorySeparatorChar);
            if (_gameRenames.TryGetValue(key, out var newName))
            {
                if (!g.Name.Equals(newName, StringComparison.OrdinalIgnoreCase))
                    _originalDetectedNames[newName] = g.Name;
                g.Name = newName;
            }
        }
    }

    public void ApplyFolderOverrides(List<DetectedGame> games)
    {
        if (_folderOverrides.Count == 0) return;
        foreach (var g in games)
        {
            if (_folderOverrides.TryGetValue(g.Name, out var stored))
            {
                var overridePath = stored.Split('|')[0];
                if (!string.IsNullOrEmpty(overridePath))
                    g.InstallPath = overridePath;
            }
        }
    }

    public void ClearOriginalDetectedNames() => _originalDetectedNames.Clear();

    // ── Static helpers ────────────────────────────────────────────────────────

    public static void MigrateHashSet(HashSet<string> set, string oldName, string newName)
    {
        if (set.Remove(oldName))
            set.Add(newName);
    }

    public static void MigrateDict<TValue>(Dictionary<string, TValue> dict, string oldName, string newName)
    {
        if (dict.Remove(oldName, out var value))
            dict[newName] = value;
    }
}
