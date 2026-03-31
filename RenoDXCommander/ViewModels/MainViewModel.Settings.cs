// MainViewModel.Settings.cs -- Settings persistence, name mappings, overrides, and per-game configuration.

using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    /// <summary>Returns the persisted Vulkan rendering path for a game, or "DirectX" if none set.</summary>
    public string GetVulkanRenderingPath(string gameName)
        => _vulkanRenderingPaths.TryGetValue(gameName, out var path) ? path : "DirectX";

    /// <summary>Sets the per-game Vulkan rendering path preference. "DirectX" removes the override (default).</summary>
    public void SetVulkanRenderingPath(string gameName, string renderingPath)
    {
        if (renderingPath == "DirectX")
            _vulkanRenderingPaths.Remove(gameName);
        else
            _vulkanRenderingPaths[gameName] = renderingPath;
        SaveNameMappings();
        var card = _allCards.FirstOrDefault(c => c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase));
        if (card != null)
        {
            card.VulkanRenderingPath = renderingPath;
            card.NotifyAll();
        }
    }

    /// <summary>Returns the persisted bitness override for a game, or null if no override set.</summary>
    public string? GetBitnessOverride(string gameName)
        => _bitnessOverrides.TryGetValue(gameName, out var value) ? value : null;

    /// <summary>Sets the per-game bitness override. Null or "Auto" removes the override; "32" or "64" sets it.</summary>
    public void SetBitnessOverride(string gameName, string? value)
    {
        if (value == null || value.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            _bitnessOverrides.Remove(gameName);
        else
            _bitnessOverrides[gameName] = value;
        SaveNameMappings();
    }

    // ── API Override ─────────────────────────────────────────────────────────────

    /// <summary>Returns the persisted API override for a game, or null if no override set.</summary>
    public List<string>? GetApiOverride(string gameName)
        => _apiOverrides.TryGetValue(gameName, out var apis) ? apis : null;

    /// <summary>Sets the per-game API override. Null removes the override; otherwise stores the list of enabled API names.</summary>
    public void SetApiOverride(string gameName, List<string>? apis)
    {
        if (apis == null)
            _apiOverrides.Remove(gameName);
        else
            _apiOverrides[gameName] = apis;
        SaveNameMappings();
    }

    // ── DLL Naming Override ───────────────────────────────────────────────────────

    /// <summary>Per-game DLL naming overrides — delegated to DllOverrideService.</summary>
    private Dictionary<string, DllOverrideConfig> _dllOverrides => _dllOverrideService.GetAllOverrides();

    /// <summary>
    /// Tracks games whose DLL override was injected from the remote manifest rather than set by the user.
    /// These entries are shown in the UI like user overrides but are NOT persisted to settings.json —
    /// they are re-applied from the manifest on every launch/refresh.
    /// </summary>
    private HashSet<string> _manifestDllOverrideGames => _dllOverrideService.ManifestDllOverrideGames;

    /// <summary>
    /// Games where the user has explicitly disabled a manifest-driven DLL override.
    /// These are persisted to settings.json so the opt-out survives refreshes.
    /// </summary>
    private HashSet<string> _manifestDllOverrideOptOuts => _dllOverrideService.ManifestDllOverrideOptOuts;

    // ── Folder Override ──────────────────────────────────────────────────────────

    /// <summary>Per-game install folder overrides. Key = game name, Value = "overridePath|originalPath".</summary>
    private Dictionary<string, string> _folderOverrides => _gameNameService.FolderOverrides;

    public void SetFolderOverride(string gameName, string folderPath)
    {
        // Preserve the original path if this is the first override
        string original = "";
        if (_folderOverrides.TryGetValue(gameName, out var existing))
        {
            var parts = existing.Split('|');
            original = parts.Length > 1 ? parts[1] : parts[0];
        }
        else
        {
            // First time — find the current card's path as original
            var card = _allCards.FirstOrDefault(c =>
                c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase));
            original = card?.DetectedGame?.InstallPath ?? card?.InstallPath ?? "";
        }
        _folderOverrides[gameName] = $"{folderPath}|{original}";
        SaveNameMappings();
        SaveLibrary();
    }

    /// <summary>
    /// Resets the folder for an auto-detected game back to its original detected path.
    /// For manual games, removes the game entirely.
    /// </summary>
    public void ResetFolderOverride(GameCardViewModel card)
    {
        if (card.IsManuallyAdded)
        {
            RemoveManualGameCommand.Execute(card);
            return;
        }

        // Retrieve original path
        var originalPath = "";
        if (_folderOverrides.TryGetValue(card.GameName, out var stored))
        {
            var parts = stored.Split('|');
            originalPath = parts.Length > 1 ? parts[1] : "";
        }

        _folderOverrides.Remove(card.GameName);

        if (!string.IsNullOrEmpty(originalPath))
        {
            card.InstallPath = originalPath;
            if (card.DetectedGame != null)
                card.DetectedGame.InstallPath = originalPath;
        }

        SaveNameMappings();
        SaveLibrary();
        card.NotifyAll();
    }

    public string? GetFolderOverride(string gameName)
    {
        if (_folderOverrides.TryGetValue(gameName, out var stored))
        {
            var parts = stored.Split('|');
            return parts[0]; // Return just the override path
        }
        return null;
    }

    public bool HasDllOverride(string gameName) => _dllOverrideService.HasDllOverride(gameName);

    public DllOverrideConfig? GetDllOverride(string gameName)
        => _dllOverrideService.GetDllOverride(gameName);

    public void SetDllOverride(string gameName, string reshadeFileName, string dcFileName)
        => _dllOverrideService.SetDllOverride(gameName, reshadeFileName, dcFileName);

    public void RemoveDllOverride(string gameName)
        => _dllOverrideService.RemoveDllOverride(gameName);

    /// <summary>
    /// Called when DLL override is toggled ON — renames existing ReShade and DC
    /// files in the game folder to the custom filenames so they stay installed.
    /// </summary>
    public void EnableDllOverride(GameCardViewModel card, string reshadeFileName, string dcFileName)
        => _dllOverrideService.EnableDllOverride(card, reshadeFileName, dcFileName);

    /// <summary>
    /// Called when DLL override is already ON and the filenames are updated —
    /// renames existing files on disk to the new custom names.
    /// </summary>
    public void UpdateDllOverrideNames(GameCardViewModel card, string newRsName, string newDcName)
        => _dllOverrideService.UpdateDllOverrideNames(card, newRsName, newDcName);

    /// <summary>
    /// Called when DLL override is toggled OFF — removes the custom-named DLL files from the game folder.
    /// </summary>
    public void DisableDllOverride(GameCardViewModel card)
        => _dllOverrideService.DisableDllOverride(card);

    /// <summary>Returns the per-game shader mode override, or "Global" if no override set.</summary>
    public string GetPerGameShaderMode(string gameName)
        => _perGameShaderMode.TryGetValue(gameName, out var mode) ? mode : "Global";

    /// <summary>Sets the per-game shader mode override. "Global" removes the override.</summary>
    public void SetPerGameShaderMode(string gameName, string mode)
    {
        if (mode == "Global")
        {
            _perGameShaderMode.Remove(gameName);
            // Discard per-game shader selection when reverting to global
            _gameNameService.PerGameShaderSelection.Remove(gameName);
        }
        else
            _perGameShaderMode[gameName] = mode;
        SaveNameMappings();
        var card = _allCards.FirstOrDefault(c => c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase));
        if (card != null)
        {
            card.ShaderModeOverride = mode == "Global" ? null : mode;
        }
    }

    public bool AnyUpdateAvailable =>
        _allCards.Any(c =>
            !c.IsHidden && !c.DllOverrideEnabled
            && !string.IsNullOrEmpty(c.InstallPath)
            && Directory.Exists(c.InstallPath)
            && ((c.Status   == GameStatus.UpdateAvailable && !c.ExcludeFromUpdateAllRenoDx) ||
                (c.RsStatus == GameStatus.UpdateAvailable && !c.ExcludeFromUpdateAllReShade && !c.RequiresVulkanInstall) ||
                (c.UlStatus == GameStatus.UpdateAvailable && !c.ExcludeFromUpdateAllUl) ||
                (c.RefStatus == GameStatus.UpdateAvailable && !c.ExcludeFromUpdateAllRef)));

    // Button colours — purple when updates available, dim when idle
    public string UpdateAllBtnBackground => AnyUpdateAvailable ? "#201838" : "#1E242C";
    public string UpdateAllBtnForeground  => AnyUpdateAvailable ? "#B898E8" : "#6B7A8E";
    public string UpdateAllBtnBorder      => AnyUpdateAvailable ? "#3A2860" : "#283240";

    public bool IsUpdateAllExcludedReShade(string gameName) => _updateAllExcludedReShade.Contains(gameName);
    public bool IsUpdateAllExcludedRenoDx(string gameName) => _updateAllExcludedRenoDx.Contains(gameName);
    public bool IsUpdateAllExcludedUl(string gameName) => _updateAllExcludedUl.Contains(gameName);

    public void ToggleUpdateAllExclusionReShade(string gameName)
    {
        var set = _gameNameService.UpdateAllExcludedReShade;
        if (!set.Remove(gameName)) set.Add(gameName);
        SaveNameMappings();
        var card = _allCards.FirstOrDefault(c => c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase));
        if (card != null) card.ExcludeFromUpdateAllReShade = set.Contains(gameName);
        NotifyUpdateButtonChanged();
    }

    public void ToggleUpdateAllExclusionRenoDx(string gameName)
    {
        var set = _gameNameService.UpdateAllExcludedRenoDx;
        if (!set.Remove(gameName)) set.Add(gameName);
        SaveNameMappings();
        var card = _allCards.FirstOrDefault(c => c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase));
        if (card != null) card.ExcludeFromUpdateAllRenoDx = set.Contains(gameName);
        NotifyUpdateButtonChanged();
    }

    public void ToggleUpdateAllExclusionUl(string gameName)
    {
        var set = _gameNameService.UpdateAllExcludedUl;
        if (!set.Remove(gameName)) set.Add(gameName);
        SaveNameMappings();
        var card = _allCards.FirstOrDefault(c => c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase));
        if (card != null) card.ExcludeFromUpdateAllUl = set.Contains(gameName);
        NotifyUpdateButtonChanged();
    }

    private void LoadNameMappings()
    {
        _isLoadingSettings = true;
        try
        {
            _gameNameService.LoadNameMappings(
                _dllOverrideService,
                _settingsViewModel,
                grid => IsGridLayout = grid,
                val => _filterViewModel.RestoreFilterMode(val));
            _crashReporter.Log("[MainViewModel.LoadNameMappings] Delegated to GameNameService");
        }
        finally
        {
            _isLoadingSettings = false;
        }
    }

    /// <summary>
    /// Renames a game everywhere: card, detected game, all settings HashSets/Dicts,
    /// persisted install records (RenoDX, DC, ReShade, Luma), and library file.
    /// Call from the UI thread. Triggers a non-destructive rescan so wiki matching
    /// picks up the corrected name.
    /// </summary>
    public void RenameGame(string oldName, string newName)
    {
        _gameNameService.RenameGame(oldName, newName, _allCards, _manualGames, _dllOverrideService);
        SaveNameMappings();
        SaveLibrary();
        var card = _allCards.FirstOrDefault(c =>
            c.GameName.Equals(newName, StringComparison.OrdinalIgnoreCase));
        card?.NotifyAll();
        DispatcherQueue?.TryEnqueue(() => { _ = InitializeAsync(forceRescan: false); });
    }

    /// <summary>
    /// Returns the original store-detected name for a game, before any user rename.
    /// If the game was never renamed, returns null.
    /// </summary>
    public string? GetOriginalStoreName(string currentName)
        => _gameNameService.GetOriginalStoreName(currentName);

    /// <summary>
    /// Removes any persisted rename for the given game, restoring it to its
    /// store-detected name on the next refresh.
    /// </summary>
    public void RemoveGameRename(string gameName)
    {
        _gameNameService.RemoveGameRename(gameName, _allCards);
        SaveNameMappings();
    }

    private static void MigrateHashSet(HashSet<string> set, string oldName, string newName)
        => GameNameService.MigrateHashSet(set, oldName, newName);

    private static void MigrateDict<TValue>(Dictionary<string, TValue> dict, string oldName, string newName)
        => GameNameService.MigrateDict(dict, oldName, newName);

    private void ApplyGameRenames(List<DetectedGame> games)
        => _gameNameService.ApplyGameRenames(games);

    private void ApplyFolderOverrides(List<DetectedGame> games)
        => _gameNameService.ApplyFolderOverrides(games);

    public void AddNameMapping(string detectedName, string wikiKey)
    {
        _gameNameService.AddNameMapping(detectedName, wikiKey);
        SaveNameMappings();
        DispatcherQueue?.TryEnqueue(() => { _ = InitializeAsync(forceRescan: false); });
    }

    public string? GetNameMapping(string detectedName)
        => _gameNameService.GetNameMapping(detectedName);

    public void RemoveNameMapping(string detectedName)
    {
        _gameNameService.RemoveNameMapping(detectedName);
        SaveNameMappings();
        DispatcherQueue?.TryEnqueue(() => { _ = InitializeAsync(forceRescan: false); });
    }

    public bool IsWikiExcluded(string gameName) =>
        _wikiExclusions.Contains(gameName);

    /// <summary>Public entry point to persist all settings to disk.</summary>
    public void SaveSettingsPublic() => SaveNameMappings();

    private void SaveNameMappings()
    {
        _gameNameService.SaveNameMappings(
            _dllOverrideService,
            _settingsViewModel,
            IsGridLayout,
            _isLoadingSettings,
            _filterViewModel.FilterMode);
    }

    private void LoadThemeAndDensity()
    {
        _settingsViewModel.LoadThemeAndDensity();
    }
}

