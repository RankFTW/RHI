// MainViewModel.Settings.cs -- Settings persistence, name mappings, overrides, and per-game configuration.

using System.Text.Json;
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
    public DllDisableResult DisableDllOverride(GameCardViewModel card)
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

    /// <summary>Returns the per-game addon mode override, or "Global" if no override set.</summary>
    public string GetPerGameAddonMode(string gameName)
        => _gameNameService.PerGameAddonMode.TryGetValue(gameName, out var mode) ? mode : "Global";

    /// <summary>Sets the per-game addon mode override. "Global" removes the override and clears per-game selection.</summary>
    public void SetPerGameAddonMode(string gameName, string mode)
    {
        if (mode == "Global")
        {
            _gameNameService.PerGameAddonMode.Remove(gameName);
            // Discard per-game addon selection when reverting to global (Req 6.6)
            _gameNameService.PerGameAddonSelection.Remove(gameName);
        }
        else
            _gameNameService.PerGameAddonMode[gameName] = mode;
        SaveNameMappings();
    }

    /// <summary>
    /// Deploys addons for a single game card (by name).
    /// Called after install/uninstall of ReShade, after addon selection changes,
    /// and after global addon set changes. Mirrors DeployShadersForCard.
    /// </summary>
    public void DeployAddonsForCard(string gameName)
    {
        var card = _allCards.FirstOrDefault(c =>
            c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase));
        if (card == null || string.IsNullOrEmpty(card.InstallPath)) return;

        _ = Task.Run(() =>
        {
            try
            {
                bool rsInstalled = card.RequiresVulkanInstall
                    ? VulkanFootprintService.Exists(card.InstallPath)
                    : card.RsStatus == GameStatus.Installed || card.RsStatus == GameStatus.UpdateAvailable;

                if (!rsInstalled) return;

                bool is32Bit = card.Is32Bit;

                // Skip addon deployment for normal ReShade games (Req 3.1, 3.2)
                if (card.UseNormalReShade)
                {
                    _addonPackService.DeployAddonsForGame(gameName, card.InstallPath, is32Bit,
                        useGlobalSet: true, perGameSelection: new List<string>());
                    return;
                }

                string addonMode = GetPerGameAddonMode(gameName);
                bool useGlobalSet = addonMode != "Select";

                List<string>? selection = null;
                if (useGlobalSet)
                {
                    selection = _settingsViewModel.EnabledGlobalAddons;
                }
                else
                {
                    _gameNameService.PerGameAddonSelection.TryGetValue(gameName, out selection);
                }

                _addonPackService.DeployAddonsForGame(gameName, card.InstallPath, is32Bit,
                    useGlobalSet, selection);
            }
            catch (Exception ex)
            {
                _crashReporter.Log($"[MainViewModel.DeployAddonsForCard] Failed for '{gameName}' — {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Deploys addons to all installed game locations.
    /// Mirrors DeployAllShaders — runs on a background thread.
    /// </summary>
    public void DeployAllAddons()
    {
        _ = Task.Run(() =>
        {
            try
            {
                foreach (var card in _allCards)
                {
                    if (string.IsNullOrEmpty(card.InstallPath)) continue;

                    bool rsInstalled = card.RequiresVulkanInstall
                        ? VulkanFootprintService.Exists(card.InstallPath)
                        : card.RsStatus == GameStatus.Installed || card.RsStatus == GameStatus.UpdateAvailable;

                    if (!rsInstalled) continue;

                    bool is32Bit = card.Is32Bit;

                    // Skip addon deployment for normal ReShade games (Req 3.1, 3.2)
                    if (card.UseNormalReShade)
                    {
                        _addonPackService.DeployAddonsForGame(card.GameName, card.InstallPath, is32Bit,
                            useGlobalSet: true, perGameSelection: new List<string>());
                        continue;
                    }

                    string addonMode = GetPerGameAddonMode(card.GameName);
                    bool useGlobalSet = addonMode != "Select";

                    List<string>? selection = null;
                    if (useGlobalSet)
                    {
                        selection = _settingsViewModel.EnabledGlobalAddons;
                    }
                    else
                    {
                        _gameNameService.PerGameAddonSelection.TryGetValue(card.GameName, out selection);
                    }

                    _addonPackService.DeployAddonsForGame(card.GameName, card.InstallPath, is32Bit,
                        useGlobalSet, selection);
                }
            }
            catch (Exception ex)
            {
                _crashReporter.Log($"[MainViewModel.DeployAllAddons] Failed — {ex.Message}");
            }
        });
    }

    public bool AnyUpdateAvailable =>
        _allCards.Any(c =>
            !c.IsHidden && !c.DllOverrideEnabled
            && !string.IsNullOrEmpty(c.InstallPath)
            && Directory.Exists(c.InstallPath)
            && ((c.Status   == GameStatus.UpdateAvailable && !c.ExcludeFromUpdateAllRenoDx) ||
                (c.RsStatus == GameStatus.UpdateAvailable && !c.ExcludeFromUpdateAllReShade && !c.RequiresVulkanInstall && !c.IsLumaMode) ||
                (c.UlStatus == GameStatus.UpdateAvailable && !c.ExcludeFromUpdateAllUl) ||
                (c.DcStatus == GameStatus.UpdateAvailable && !c.ExcludeFromUpdateAllDc) ||
                (c.OsStatus == GameStatus.UpdateAvailable && !c.ExcludeFromUpdateAllOs) ||
                (c.RefStatus == GameStatus.UpdateAvailable && !c.ExcludeFromUpdateAllRef)));

    // Button colours — purple when updates available, dim when idle
    public string UpdateAllBtnBackground => AnyUpdateAvailable ? "#201838" : "#1E242C";
    public string UpdateAllBtnForeground  => AnyUpdateAvailable ? "#B898E8" : "#6B7A8E";
    public string UpdateAllBtnBorder      => AnyUpdateAvailable ? "#3A2860" : "#283240";

    public bool IsUpdateAllExcludedReShade(string gameName) => _updateAllExcludedReShade.Contains(gameName);
    public bool IsUpdateAllExcludedRenoDx(string gameName) => _updateAllExcludedRenoDx.Contains(gameName);
    public bool IsUpdateAllExcludedUl(string gameName) => _updateAllExcludedUl.Contains(gameName);
    public bool IsUpdateAllExcludedDc(string gameName) => _updateAllExcludedDc.Contains(gameName);
    public bool IsUpdateAllExcludedOs(string gameName) => _updateAllExcludedOs.Contains(gameName);
    public bool IsUpdateAllExcludedRef(string gameName) => _updateAllExcludedRef.Contains(gameName);

    /// <summary>Returns true if the game is configured to use normal (non-addon) ReShade.</summary>
    public bool IsNormalReShadeGame(string gameName) => _normalReShadeGames.Contains(gameName);

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

    public void ToggleUpdateAllExclusionDc(string gameName)
    {
        var set = _gameNameService.UpdateAllExcludedDc;
        if (!set.Remove(gameName)) set.Add(gameName);
        SaveNameMappings();
        var card = _allCards.FirstOrDefault(c => c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase));
        if (card != null) card.ExcludeFromUpdateAllDc = set.Contains(gameName);
        NotifyUpdateButtonChanged();
    }

    public void ToggleUpdateAllExclusionOs(string gameName)
    {
        var set = _gameNameService.UpdateAllExcludedOs;
        if (!set.Remove(gameName)) set.Add(gameName);
        SaveNameMappings();
        var card = _allCards.FirstOrDefault(c => c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase));
        if (card != null) card.ExcludeFromUpdateAllOs = set.Contains(gameName);
        NotifyUpdateButtonChanged();
    }

    public void ToggleUpdateAllExclusionRef(string gameName)
    {
        var set = _gameNameService.UpdateAllExcludedRef;
        if (!set.Remove(gameName)) set.Add(gameName);
        SaveNameMappings();
        var card = _allCards.FirstOrDefault(c => c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase));
        if (card != null) card.ExcludeFromUpdateAllRef = set.Contains(gameName);
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
                layout => CurrentViewLayout = layout,
                val => _filterViewModel.RestoreFilterMode(val),
                filters =>
                {
                    _filterViewModel.CustomFilters.Clear();
                    foreach (var f in filters)
                        _filterViewModel.CustomFilters.Add(f);
                });
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

    public string? GetUserNameMapping(string detectedName)
        => _gameNameService.GetUserNameMapping(detectedName);

    public void RemoveNameMapping(string detectedName)
    {
        _gameNameService.RemoveNameMapping(detectedName);
        SaveNameMappings();
        DispatcherQueue?.TryEnqueue(() => { _ = InitializeAsync(forceRescan: false); });
    }

    public bool IsWikiExcluded(string gameName) =>
        _wikiExclusions.Contains(gameName);

    // ── Preset Shader Resolution ─────────────────────────────────────────────

    /// <summary>
    /// Builds a dictionary mapping each available shader pack ID to its recorded
    /// file list, read from the ShaderPackService settings JSON file.
    /// This is the input format <see cref="ShaderResolver.Resolve"/> expects.
    /// </summary>
    internal Dictionary<string, IReadOnlyList<string>> BuildPackFileLists()
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        var settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RHI", "settings.json");

        if (!File.Exists(settingsPath))
            return result;

        Dictionary<string, string>? settings;
        try
        {
            settings = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(settingsPath));
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainViewModel.BuildPackFileLists] Failed to read settings — {ex.Message}");
            return result;
        }

        if (settings is null)
            return result;

        foreach (var (packId, _, _) in _shaderPackService.AvailablePacks)
        {
            var key = $"ShaderPack_{packId}_Files";
            if (settings.TryGetValue(key, out var json) && !string.IsNullOrEmpty(json))
            {
                try
                {
                    var files = JsonSerializer.Deserialize<List<string>>(json);
                    if (files is not null)
                        result[packId] = files;
                }
                catch (Exception ex)
                {
                    _crashReporter.Log($"[MainViewModel.BuildPackFileLists] Failed to parse file list for '{packId}' — {ex.Message}");
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Reads techniques from the given preset file paths, resolves required shader packs,
    /// switches the game to Per_Game_Shader_Mode "Select", merges resolved packs with
    /// existing selection (union), persists, and calls SyncGameFolder.
    /// </summary>
    public async Task ApplyPresetShadersAsync(string gameName, IEnumerable<string> presetFilePaths)
    {
        try
        {
            // 1. Collect all required .fx files from all presets
            var allFxFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var presetPath in presetFilePaths)
            {
                try
                {
                    var content = File.ReadAllText(presetPath);
                    foreach (var line in content.Split('\n'))
                    {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("Techniques=", StringComparison.OrdinalIgnoreCase) ||
                            trimmed.StartsWith("Techniques =", StringComparison.OrdinalIgnoreCase))
                        {
                            var eqIndex = trimmed.IndexOf('=');
                            if (eqIndex >= 0)
                            {
                                var value = trimmed[(eqIndex + 1)..];
                                var fxFiles = TechniquesParser.ExtractFxFiles(value);
                                allFxFiles.UnionWith(fxFiles);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _crashReporter.Log($"[MainViewModel.ApplyPresetShaders] Failed to read preset '{presetPath}' — {ex.Message}");
                }
            }

            if (allFxFiles.Count == 0)
            {
                _crashReporter.Log("[MainViewModel.ApplyPresetShaders] No .fx files found in presets");
                return;
            }

            // 2. Ensure packs with missing file lists are downloaded so the resolver
            //    can match .fx files to packs. Only downloads packs that don't already
            //    have a file list entry in settings.json — avoids re-downloading everything.
            var existingFileLists = BuildPackFileLists();
            var missingPacks = _shaderPackService.AvailablePacks
                .Where(p => !existingFileLists.ContainsKey(p.Id))
                .Select(p => p.Id)
                .ToList();
            if (missingPacks.Count > 0)
            {
                _crashReporter.Log($"[MainViewModel.ApplyPresetShaders] Downloading {missingPacks.Count} pack(s) missing file lists: {string.Join(", ", missingPacks)}");
                await _shaderPackService.EnsurePacksAsync(missingPacks);
            }

            // 3. Build pack file lists and resolve
            var packFileLists = BuildPackFileLists();
            var (matchedPackIds, unresolvedFiles) = ShaderResolver.Resolve(allFxFiles, packFileLists);

            // 4. Log unresolved files
            foreach (var unresolved in unresolvedFiles)
                _crashReporter.Log($"[MainViewModel.ApplyPresetShaders] Unresolved shader: {unresolved}");

            if (matchedPackIds.Count == 0)
            {
                _crashReporter.Log("[MainViewModel.ApplyPresetShaders] No matching shader packs found");
                return;
            }

            // 5. Set per-game mode to "Select"
            SetPerGameShaderMode(gameName, "Select");

            // 6. Merge resolved pack IDs with existing selection (union)
            if (_gameNameService.PerGameShaderSelection.TryGetValue(gameName, out var existing))
            {
                var merged = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
                merged.UnionWith(matchedPackIds);
                _gameNameService.PerGameShaderSelection[gameName] = merged.ToList();
            }
            else
            {
                _gameNameService.PerGameShaderSelection[gameName] = matchedPackIds.ToList();
            }

            // 7. Persist
            SaveNameMappings();

            // 8. Deploy
            DeployShadersForCard(gameName);

            _crashReporter.Log($"[MainViewModel.ApplyPresetShaders] Applied {matchedPackIds.Count} shader pack(s) for '{gameName}'");
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainViewModel.ApplyPresetShaders] Failed for '{gameName}' — {ex.Message}");
        }
    }

    /// <summary>Public entry point to persist all settings to disk.</summary>
    public void SaveSettingsPublic() => SaveNameMappings();

    private void SaveNameMappings()
    {
        _gameNameService.SaveNameMappings(
            _dllOverrideService,
            _settingsViewModel,
            CurrentViewLayout,
            _isLoadingSettings,
            _filterViewModel.FilterMode,
            _filterViewModel.CustomFilters.ToList());
    }

    private void LoadThemeAndDensity()
    {
        _settingsViewModel.LoadThemeAndDensity();
    }
}

