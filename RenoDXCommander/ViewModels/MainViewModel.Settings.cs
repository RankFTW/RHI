// MainViewModel.Settings.cs -- Settings persistence, name mappings, overrides, and per-game configuration.

using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    /// <summary>Returns the persisted Vulkan rendering path for a game, or "DirectX" if none set.</summary>
    public string GetVulkanRenderingPath(string gameName, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        return _vulkanRenderingPaths.TryGetValue(key, out var path) ? path : "DirectX";
    }

    /// <summary>Sets the per-game Vulkan rendering path preference. "DirectX" removes the override (default).</summary>
    public void SetVulkanRenderingPath(string gameName, string renderingPath, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        if (renderingPath == "DirectX")
            _vulkanRenderingPaths.Remove(key);
        else
            _vulkanRenderingPaths[key] = renderingPath;
        SaveNameMappings();
        var card = _allCards.FirstOrDefault(c => c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase)
            && (c.Source ?? "").Equals(store ?? "", StringComparison.OrdinalIgnoreCase));
        if (card != null)
        {
            card.VulkanRenderingPath = renderingPath;
            card.NotifyAll();
        }
    }

    /// <summary>Returns the persisted bitness override for a game, or null if no override set.</summary>
    public string? GetBitnessOverride(string gameName, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        return _bitnessOverrides.TryGetValue(key, out var value) ? value : null;
    }

    /// <summary>Sets the per-game bitness override. Null or "Auto" removes the override; "32" or "64" sets it.</summary>
    public void SetBitnessOverride(string gameName, string? value, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        if (value == null || value.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            _bitnessOverrides.Remove(key);
        else
            _bitnessOverrides[key] = value;
        SaveNameMappings();
    }

    // ── API Override ─────────────────────────────────────────────────────────────

    /// <summary>Returns the persisted API override for a game, or null if no override set.</summary>
    public List<string>? GetApiOverride(string gameName, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        return _apiOverrides.TryGetValue(key, out var apis) ? apis : null;
    }

    /// <summary>Sets the per-game API override. Null removes the override; otherwise stores the list of enabled API names.</summary>
    public void SetApiOverride(string gameName, List<string>? apis, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        if (apis == null)
            _apiOverrides.Remove(key);
        else
            _apiOverrides[key] = apis;
        SaveNameMappings();
    }

    // ── ReShade Channel Override ──────────────────────────────────────────────────

    /// <summary>Returns the persisted ReShade channel override for a game, or null if no override set (uses global default).</summary>
    public string? GetReShadeChannelOverride(string gameName, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        if (_reShadeChannelOverrides.TryGetValue(key, out var value)) return value;
        // Fallback to name-only for legacy entries
        return _reShadeChannelOverrides.TryGetValue(gameName, out value) ? value : null;
    }

    /// <summary>Sets the per-game ReShade channel override. Null removes the override (use global); "Stable" or "Nightly" sets it. "Custom" uses user-supplied DLLs. Any other value is a legacy version string.</summary>
    public void SetReShadeChannelOverride(string gameName, string? value, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        // Also check name-only key for legacy entries set before composite key migration
        var previousValue = _reShadeChannelOverrides.TryGetValue(key, out var prev) ? prev
            : (_reShadeChannelOverrides.TryGetValue(gameName, out prev) ? prev : null);

        if (value == null)
        {
            _reShadeChannelOverrides.Remove(key);
            _reShadeChannelOverrides.Remove(gameName); // clear legacy name-only entry too
        }
        else
            _reShadeChannelOverrides[key] = value;
        SaveNameMappings();

        // Auto-manage update exclusion for legacy and custom versions
        bool wasExcluded = IsLegacyVersion(previousValue) || string.Equals(previousValue, "Custom", StringComparison.OrdinalIgnoreCase);
        bool isExcluded = IsLegacyVersion(value) || string.Equals(value, "Custom", StringComparison.OrdinalIgnoreCase);

        if (isExcluded && !wasExcluded)
        {
            // Entering legacy/custom — exclude from ReShade updates
            if (!_updateAllExcludedReShade.Contains(key))
            {
                _updateAllExcludedReShade.Add(key);
                var card = _allCards.FirstOrDefault(c => c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase)
                    && (string.IsNullOrEmpty(store) || c.Source == store));
                if (card != null) card.ExcludeFromUpdateAllReShade = true;
            }
        }
        else if (!isExcluded && wasExcluded)
        {
            // Leaving legacy/custom — re-include in ReShade updates
            _updateAllExcludedReShade.Remove(key);
            var card = _allCards.FirstOrDefault(c => c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase)
                && (string.IsNullOrEmpty(store) || c.Source == store));
            if (card != null) card.ExcludeFromUpdateAllReShade = false;
        }
    }

    /// <summary>Returns true if the channel value is a legacy version string (not null, not "Stable", not "Nightly", not "Custom").</summary>
    public static bool IsLegacyVersion(string? channel)
    {
        if (string.IsNullOrEmpty(channel)) return false;
        if (string.Equals(channel, "Stable", StringComparison.OrdinalIgnoreCase)) return false;
        if (string.Equals(channel, "Nightly", StringComparison.OrdinalIgnoreCase)) return false;
        if (string.Equals(channel, "Custom", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    /// <summary>
    /// Resolves the effective ReShade channel for a game.
    /// Returns the per-game override if set, otherwise defaults to Stable.
    /// </summary>
    public string ResolveReShadeChannel(string gameName, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        if (_reShadeChannelOverrides.TryGetValue(key, out var perGame))
            return perGame;
        return "Stable";
    }

    // ── DXVK Variant Override ─────────────────────────────────────────────────

    /// <summary>Returns the persisted DXVK variant override for a game, or null if no override set (uses global default).</summary>
    public string? GetDxvkVariantOverride(string gameName, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        return _dxvkVariantOverrides.TryGetValue(key, out var value) ? value : null;
    }

    /// <summary>Sets the per-game DXVK variant override. Null removes the override (use global); "Development", "Stable", or "LiliumHdr" sets it.</summary>
    public void SetDxvkVariantOverride(string gameName, string? value, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        if (value == null)
            _dxvkVariantOverrides.Remove(key);
        else
            _dxvkVariantOverrides[key] = value;
        SaveNameMappings();
    }

    /// <summary>Returns the per-game Lilium HDR DXVK conf preset index (0=Safest, 5=Experimental). Returns 0 if not set.</summary>
    public int GetLiliumPreset(string gameName, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        return _gameNameService.LiliumPresetOverrides.TryGetValue(key, out var idx) ? idx : 0;
    }

    /// <summary>Sets the per-game Lilium HDR DXVK conf preset. 0 removes the override (default = Safest).</summary>
    public void SetLiliumPreset(string gameName, int presetIndex, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        if (presetIndex <= 0)
            _gameNameService.LiliumPresetOverrides.Remove(key);
        else
            _gameNameService.LiliumPresetOverrides[key] = presetIndex;
        SaveNameMappings();
    }

    /// <summary>
    /// Resolves the effective DXVK variant for a game.
    /// Returns the per-game override if set, otherwise the global setting.
    /// </summary>
    public DxvkVariant ResolveDxvkVariant(string gameName, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        if (_dxvkVariantOverrides.TryGetValue(key, out var perGame))
        {
            return perGame switch
            {
                "Stable" => DxvkVariant.Stable,
                "LiliumHdr" => DxvkVariant.LiliumHdr,
                _ => DxvkVariant.Development,
            };
        }
        return _dxvkService.SelectedVariant;
    }

    // ── OptiScaler Variant Override ───────────────────────────────────────────

    /// <summary>Returns the OptiScaler variant for a game. "Stable" or "Nightly". Defaults to "Stable".</summary>
    public string GetOsVariant(string gameName, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        if (_gameNameService.OsVariantOverrides.TryGetValue(key, out var v)) return v;
        // name-only fallback for legacy
        if (_gameNameService.OsVariantOverrides.TryGetValue(gameName, out var v2)) return v2;
        return "Stable";
    }

    /// <summary>Sets the OptiScaler variant for a game. Null or "Stable" removes the override.</summary>
    public void SetOsVariant(string gameName, string? value, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        if (value == null || value == "Stable")
            _gameNameService.OsVariantOverrides.Remove(key);
        else
            _gameNameService.OsVariantOverrides[key] = value;
        SaveNameMappings();
    }

    // ── Deploy Streamline ─────────────────────────────────────────────────────

    // ── Neural Rendering Method ───────────────────────────────────────────────

    /// <summary>Returns the persisted NR method for a game. Null = not set (auto-detect from game state).</summary>
    public string? GetNrMethodOverride(string gameName, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        if (_gameNameService.NrMethodOverrides.TryGetValue(key, out var v)) return v;
        if (_gameNameService.NrMethodOverrides.TryGetValue(gameName, out var v2)) return v2;
        return null;
    }

    /// <summary>Sets the persisted NR method for a game. Null clears the override.</summary>
    public void SetNrMethodOverride(string gameName, string? value, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        if (string.IsNullOrEmpty(value))
        {
            _gameNameService.NrMethodOverrides.Remove(key);
            _gameNameService.NrMethodOverrides.Remove(gameName);
        }
        else
            _gameNameService.NrMethodOverrides[key] = value;
        SaveNameMappings();
    }

    // ── Deploy Streamline (original) ──────────────────────────────────────────

    /// <summary>Returns whether Deploy Streamline is enabled for a game.</summary>
    public bool GetOsDeployStreamline(string gameName, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        return _gameNameService.OsDeployStreamline.Contains(key)
            || _gameNameService.OsDeployStreamline.Contains(gameName);
    }

    /// <summary>Sets whether Deploy Streamline is enabled for a game.</summary>
    public void SetOsDeployStreamline(string gameName, bool value, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        if (value)
            _gameNameService.OsDeployStreamline.Add(key);
        else
        {
            _gameNameService.OsDeployStreamline.Remove(key);
            _gameNameService.OsDeployStreamline.Remove(gameName);
        }
        SaveNameMappings();
    }

    // ── Keep ReShade.ini Updated ──────────────────────────────────────────────

    /// <summary>Returns true if RHI should automatically re-merge reshade.ini for this game (default).</summary>
    public bool GetKeepRsIniUpdated(string gameName, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        // Absent = Yes (keep updated). Present = No (locked).
        return !_gameNameService.RsIniLockedGames.Contains(key)
            && !_gameNameService.RsIniLockedGames.Contains(gameName);
    }

    /// <summary>Sets whether RHI should automatically re-merge reshade.ini for this game.</summary>
    public void SetKeepRsIniUpdated(string gameName, bool value, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        if (!value) // No = locked = add to set
        {
            _gameNameService.RsIniLockedGames.Add(key);
        }
        else // Yes = keep updated = remove from set
        {
            _gameNameService.RsIniLockedGames.Remove(key);
            _gameNameService.RsIniLockedGames.Remove(gameName); // clear any legacy key
        }
        CrashReporter.Log($"[MainViewModel.SetKeepRsIniUpdated] {gameName}|{store} = {(value ? "Yes" : "No")}");
        SaveNameMappings();
    }

    // ── Deploy DLSS Enabler ───────────────────────────────────────────────────

    /// <summary>Returns whether Deploy DLSS Enabler is enabled for a game.</summary>
    public bool GetOsDeployDlssEnabler(string gameName, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        return _gameNameService.OsDeployDlssEnabler.Contains(key)
            || _gameNameService.OsDeployDlssEnabler.Contains(gameName);
    }

    /// <summary>Sets whether Deploy DLSS Enabler is enabled for a game.</summary>
    public void SetOsDeployDlssEnabler(string gameName, bool value, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        if (value)
            _gameNameService.OsDeployDlssEnabler.Add(key);
        else
        {
            _gameNameService.OsDeployDlssEnabler.Remove(key);
            _gameNameService.OsDeployDlssEnabler.Remove(gameName);
        }
        SaveNameMappings();
    }

    // ── Dilated Motion Vectors ────────────────────────────────────────────────

    /// <summary>Returns whether Dilated Motion Vectors is set to Off for a game.</summary>
    public bool GetOsDilatedMotionVectorsOff(string gameName, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        return _gameNameService.OsDilatedMotionVectorsOff.Contains(key)
            || _gameNameService.OsDilatedMotionVectorsOff.Contains(gameName);
    }

    /// <summary>Sets whether Dilated Motion Vectors is Off for a game.</summary>
    public void SetOsDilatedMotionVectorsOff(string gameName, bool value, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        if (value)
            _gameNameService.OsDilatedMotionVectorsOff.Add(key);
        else
        {
            _gameNameService.OsDilatedMotionVectorsOff.Remove(key);
            _gameNameService.OsDilatedMotionVectorsOff.Remove(gameName);
        }
        SaveNameMappings();
    }

    // ── FSR Crash Fix ─────────────────────────────────────────────────────────

    public string GetOsFsrCrashFix(string gameName, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        if (_gameNameService.OsFsrCrashFix.TryGetValue(key, out var v)) return v;
        if (_gameNameService.OsFsrCrashFix.TryGetValue(gameName, out var v2)) return v2;
        return "None";
    }

    public void SetOsFsrCrashFix(string gameName, string? value, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        if (value == null || value == "None")
        {
            _gameNameService.OsFsrCrashFix.Remove(key);
            _gameNameService.OsFsrCrashFix.Remove(gameName);
        }
        else
            _gameNameService.OsFsrCrashFix[key] = value;
        SaveNameMappings();
    }

    // ── FG Input ──────────────────────────────────────────────────────────────

    public string GetOsFgInput(string gameName, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        if (_gameNameService.OsFgInput.TryGetValue(key, out var v)) return v;
        if (_gameNameService.OsFgInput.TryGetValue(gameName, out var v2)) return v2;
        return "auto";
    }

    public void SetOsFgInput(string gameName, string value, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        if (value == "auto") { _gameNameService.OsFgInput.Remove(key); _gameNameService.OsFgInput.Remove(gameName); }
        else _gameNameService.OsFgInput[key] = value;
        SaveNameMappings();
    }

    // ── FG Output ─────────────────────────────────────────────────────────────

    public string GetOsFgOutput(string gameName, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        if (_gameNameService.OsFgOutput.TryGetValue(key, out var v)) return v;
        if (_gameNameService.OsFgOutput.TryGetValue(gameName, out var v2)) return v2;
        return "auto";
    }

    public void SetOsFgOutput(string gameName, string value, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        if (value == "auto") { _gameNameService.OsFgOutput.Remove(key); _gameNameService.OsFgOutput.Remove(gameName); }
        else _gameNameService.OsFgOutput[key] = value;
        SaveNameMappings();
    }

    // ── FG Nvngx Replacement ──────────────────────────────────────────────────

    public string GetOsFgNvngxReplacement(string gameName, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        if (_gameNameService.OsFgNvngxReplacement.TryGetValue(key, out var v)) return v;
        if (_gameNameService.OsFgNvngxReplacement.TryGetValue(gameName, out var v2)) return v2;
        return "None";
    }

    public void SetOsFgNvngxReplacement(string gameName, string value, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        if (value == "None") { _gameNameService.OsFgNvngxReplacement.Remove(key); _gameNameService.OsFgNvngxReplacement.Remove(gameName); }
        else _gameNameService.OsFgNvngxReplacement[key] = value;
        SaveNameMappings();
    }

    // ── FSR-FG Swapchain ──────────────────────────────────────────────────────

    public bool GetOsFsrFgSwapchain(string gameName, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        return _gameNameService.OsFsrFgSwapchain.Contains(key)
            || _gameNameService.OsFsrFgSwapchain.Contains(gameName);
    }

    public void SetOsFsrFgSwapchain(string gameName, bool value, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        if (value) _gameNameService.OsFsrFgSwapchain.Add(key);
        else { _gameNameService.OsFsrFgSwapchain.Remove(key); _gameNameService.OsFsrFgSwapchain.Remove(gameName); }
        SaveNameMappings();
    }

    // ── Upscaler Plugin ───────────────────────────────────────────────────────

    public bool GetOsUpscalerPlugin(string gameName, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        return _gameNameService.OsUpscalerPlugin.Contains(key)
            || _gameNameService.OsUpscalerPlugin.Contains(gameName);
    }

    public void SetOsUpscalerPlugin(string gameName, bool value, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        if (value) _gameNameService.OsUpscalerPlugin.Add(key);
        else { _gameNameService.OsUpscalerPlugin.Remove(key); _gameNameService.OsUpscalerPlugin.Remove(gameName); }
        SaveNameMappings();
    }

    // ── Streamline Version ────────────────────────────────────────────────────

    public string GetOsStreamlineVersion(string gameName, string store)
    {
        var key = GameKey.From(gameName, store).ToKey();
        if (_gameNameService.OsStreamlineVersion.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v)) return v;
        if (_gameNameService.OsStreamlineVersion.TryGetValue(gameName, out var vLegacy) && !string.IsNullOrEmpty(vLegacy)) return vLegacy;
        return "";
    }

    public void SetOsStreamlineVersion(string gameName, string? version, string store)
    {
        var key = GameKey.From(gameName, store).ToKey();
        if (string.IsNullOrEmpty(version))
            _gameNameService.OsStreamlineVersion.Remove(key);
        else
            _gameNameService.OsStreamlineVersion[key] = version;
        SaveNameMappings();
    }

    // ── Ultimate ASI Loader ───────────────────────────────────────────────────

    public string? GetUalInstalledAs(string gameName, string store)
    {
        var key = GameKey.From(gameName, store).ToKey();
        if (_gameNameService.UalInstalledAs.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v)) return v;
        if (_gameNameService.UalInstalledAs.TryGetValue(gameName, out var vLegacy) && !string.IsNullOrEmpty(vLegacy)) return vLegacy;
        return null;
    }

    public void SetUalInstalledAs(string gameName, string? dllName, string store)
    {
        var key = GameKey.From(gameName, store).ToKey();
        if (string.IsNullOrEmpty(dllName))
        {
            _gameNameService.UalInstalledAs.Remove(key);
            _gameNameService.UalInstalledAs.Remove(gameName);
        }
        else
            _gameNameService.UalInstalledAs[key] = dllName;
        SaveNameMappings();
    }

    // ── ShortFuse Auto-Config ─────────────────────────────────────────────────

    /// <summary>Returns true when ShortFuse auto-config is enabled for this game (default when absent).</summary>
    public bool GetSfAutoConfigEnabled(string gameName, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        return !_gameNameService.SfAutoConfigDisabled.Contains(key)
            && !_gameNameService.SfAutoConfigDisabled.Contains(gameName);
    }

    public void SetSfAutoConfigEnabled(string gameName, bool value, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        if (!value) // disabled → add to set
            _gameNameService.SfAutoConfigDisabled.Add(key);
        else // enabled → remove from set (absent = enabled)
        {
            _gameNameService.SfAutoConfigDisabled.Remove(key);
            _gameNameService.SfAutoConfigDisabled.Remove(gameName);
        }
        SaveNameMappings();
    }

    /// <summary>
    /// Post-ShortFuse-install auto-config: renames ReShade → Reshade64.asi,
    /// installs UAL (winmm → version → dinput8 priority), writes [INSTALL] keys to reshade.ini.
    /// Respects GetSfAutoConfigEnabled and GetKeepRsIniUpdated per-game settings.
    /// </summary>
    public async Task ApplySfAutoConfigAsync(GameCardViewModel card)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return;
        var installPath = card.InstallPath;
        var gameName    = card.GameName;
        var store       = card.Source ?? "";

        // ── Step 1: Rename ReShade DLL to Reshade64.asi ───────────────────────
        const string asiName = "Reshade64.asi";
        var rsRecord = card.RsRecord;
        if (rsRecord != null && !string.IsNullOrEmpty(rsRecord.InstalledAs)
            && !rsRecord.InstalledAs.Equals(asiName, StringComparison.OrdinalIgnoreCase))
        {
            var currentPath = System.IO.Path.Combine(installPath, rsRecord.InstalledAs);
            var asiPath     = System.IO.Path.Combine(installPath, asiName);
            try
            {
                if (System.IO.File.Exists(currentPath))
                {
                    if (System.IO.File.Exists(asiPath)) System.IO.File.Delete(asiPath);
                    System.IO.File.Move(currentPath, asiPath);
                    rsRecord.InstalledAs      = asiName;
                    card.RsRecord.InstalledAs = asiName;
                    _auxInstaller.SaveAuxRecord(rsRecord);
                    _crashReporter.Log($"[SfAutoConfig] Renamed ReShade to '{asiName}' for '{gameName}'");
                }
            }
            catch (Exception ex) { _crashReporter.Log($"[SfAutoConfig] ReShade rename failed — {ex.Message}"); }
        }

        // ── Step 2: Auto-install ASI Loader (winmm → version → dinput8) ───────
        var ualSvc = App.Services.GetRequiredService<UltimateAsiLoaderService>();
        bool ualAlreadyInstalled = !string.IsNullOrEmpty(GetUalInstalledAs(gameName, store));

        if (!ualAlreadyInstalled)
        {
            string[] preferenceOrder = { "winmm.dll", "version.dll", "dinput8.dll" };
            string? chosenName = null;
            foreach (var candidate in preferenceOrder)
            {
                var candidatePath = System.IO.Path.Combine(installPath, candidate);
                bool takenByOther = System.IO.File.Exists(candidatePath)
                    && !string.Equals(rsRecord?.InstalledAs, candidate, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(card.OsInstalledFile, candidate, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(card.DcInstalledFile, candidate, StringComparison.OrdinalIgnoreCase);
                if (!takenByOther) { chosenName = candidate; break; }
            }

            if (chosenName != null)
            {
                try
                {
                    var (success, hookedOriginal) = await ualSvc.InstallAsync(card, chosenName).ConfigureAwait(false);
                    if (success)
                    {
                        SetUalInstalledAs(gameName, chosenName, store);
                        _crashReporter.Log($"[SfAutoConfig] Installed UAL as '{chosenName}' for '{gameName}'" +
                            (hookedOriginal != null ? $" (chained '{hookedOriginal}')" : ""));
                    }
                }
                catch (Exception ex) { _crashReporter.Log($"[SfAutoConfig] UAL install failed — {ex.Message}"); }
            }
            else
                _crashReporter.Log($"[SfAutoConfig] No suitable UAL name available for '{gameName}'");
        }
        else
            _crashReporter.Log($"[SfAutoConfig] UAL already installed for '{gameName}' — skipping");

        // ── Step 3: Write [INSTALL] HookStreamline=1 + HookDirectX=1 ─────────
        if (GetKeepRsIniUpdated(gameName, store))
        {
            var iniPath = System.IO.Path.Combine(installPath, "reshade.ini");
            if (System.IO.File.Exists(iniPath))
            {
                try
                {
                    var ini = AuxInstallService.ParseIni(System.IO.File.ReadAllLines(iniPath));
                    if (!ini.ContainsKey("INSTALL"))
                        ini["INSTALL"] = new AuxInstallService.OrderedDict();
                    ini["INSTALL"]["HookStreamline"] = "1";
                    ini["INSTALL"]["HookDirectX"]    = "1";
                    AuxInstallService.WriteIni(iniPath, ini);
                    _crashReporter.Log($"[SfAutoConfig] Wrote [INSTALL] keys to reshade.ini for '{gameName}'");
                }
                catch (Exception ex) { _crashReporter.Log($"[SfAutoConfig] reshade.ini write failed — {ex.Message}"); }
            }
        }
    }

    /// <summary>
    /// Reverts the ShortFuse auto-config applied during install:
    /// renames Reshade64.asi back to the API-correct default DLL name, and
    /// uninstalls ASI Loader if RHI auto-installed it.
    /// Called from the Neural Rendering Remove button for the ShortFuse method.
    /// </summary>
    public void RevertSfAutoConfig(GameCardViewModel card)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return;
        var installPath = card.InstallPath;
        var gameName    = card.GameName;
        var store       = card.Source ?? "";

        // ── Revert ReShade rename ─────────────────────────────────────────────
        const string asiName = "Reshade64.asi";
        var rsRecord = card.RsRecord;
        if (rsRecord != null
            && rsRecord.InstalledAs.Equals(asiName, StringComparison.OrdinalIgnoreCase))
        {
            var defaultName = ResolveAutoReShadeFilename(card.DetectedApis)
                           ?? AuxInstallService.RsNormalName;
            var asiPath     = System.IO.Path.Combine(installPath, asiName);
            var defaultPath = System.IO.Path.Combine(installPath, defaultName);
            try
            {
                if (System.IO.File.Exists(asiPath))
                {
                    if (System.IO.File.Exists(defaultPath)) System.IO.File.Delete(defaultPath);
                    System.IO.File.Move(asiPath, defaultPath);
                    rsRecord.InstalledAs      = defaultName;
                    card.RsRecord.InstalledAs = defaultName;
                    _auxInstaller.SaveAuxRecord(rsRecord);
                    _crashReporter.Log($"[SfAutoConfig.Revert] Renamed '{asiName}' → '{defaultName}' for '{gameName}'");
                }
            }
            catch (Exception ex) { _crashReporter.Log($"[SfAutoConfig.Revert] ReShade rename failed — {ex.Message}"); }
        }

        // ── Remove UAL if RHI installed it ────────────────────────────────────
        var ualInstalled = GetUalInstalledAs(gameName, store);
        if (!string.IsNullOrEmpty(ualInstalled))
        {
            var ualSvc = App.Services.GetRequiredService<UltimateAsiLoaderService>();
            ualSvc.Uninstall(card);
            SetUalInstalledAs(gameName, null, store);
            _crashReporter.Log($"[SfAutoConfig.Revert] Removed UAL ('{ualInstalled}') for '{gameName}'");
        }

        // ── Remove [INSTALL] keys from reshade.ini ────────────────────────────
        var iniPath = System.IO.Path.Combine(installPath, "reshade.ini");
        if (System.IO.File.Exists(iniPath))
        {
            try
            {
                var ini = AuxInstallService.ParseIni(System.IO.File.ReadAllLines(iniPath));
                if (ini.TryGetValue("INSTALL", out var installSection))
                {
                    installSection.Remove("HookStreamline");
                    installSection.Remove("HookDirectX");
                    // Remove the section entirely if now empty
                    if (installSection.Count == 0)
                        ini.Remove("INSTALL");
                    AuxInstallService.WriteIni(iniPath, ini);
                    _crashReporter.Log($"[SfAutoConfig.Revert] Removed [INSTALL] keys from reshade.ini for '{gameName}'");
                }
            }
            catch (Exception ex) { _crashReporter.Log($"[SfAutoConfig.Revert] reshade.ini cleanup failed — {ex.Message}"); }
        }
    }

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

    public void SetFolderOverride(string gameName, string folderPath, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        // Preserve the original path if this is the first override
        string original = "";
        if (_folderOverrides.TryGetValue(key, out var existing))
        {
            var parts = existing.Split('|');
            original = parts.Length > 1 ? parts[1] : parts[0];
        }
        else
        {
            // First time — find the current card's path as original
            var card = _allCards.FirstOrDefault(c =>
                c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase)
                && (c.Source ?? "").Equals(store ?? "", StringComparison.OrdinalIgnoreCase));
            original = card?.DetectedGame?.InstallPath ?? card?.InstallPath ?? "";
        }
        _folderOverrides[key] = $"{folderPath}|{original}";
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

        var key = GameKey.FromCard(card.GameName, card.Source).ToKey();
        // Retrieve original path
        var originalPath = "";
        if (_folderOverrides.TryGetValue(key, out var stored))
        {
            var parts = stored.Split('|');
            originalPath = parts.Length > 1 ? parts[1] : "";
        }

        _folderOverrides.Remove(key);

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

    public string? GetFolderOverride(string gameName, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        if (_folderOverrides.TryGetValue(key, out var stored))
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
    public string GetPerGameShaderMode(string gameName, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        if (_perGameShaderMode.TryGetValue(key, out var mode)) return mode;
        // Fallback to name-only for legacy entries
        return _perGameShaderMode.TryGetValue(gameName, out mode) ? mode : "Global";
    }

    /// <summary>Sets the per-game shader mode override. "Global" removes the override.</summary>
    public void SetPerGameShaderMode(string gameName, string mode, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        if (mode == "Global")
        {
            _perGameShaderMode.Remove(key);
            _perGameShaderMode.Remove(gameName); // clear legacy name-only entry too
            // Discard per-game shader selection when reverting to global
            _gameNameService.PerGameShaderSelection.Remove(key);
            _gameNameService.PerGameShaderSelection.Remove(gameName); // clear legacy name-only entry too
        }
        else
            _perGameShaderMode[key] = mode;
        SaveNameMappings();
        var card = _allCards.FirstOrDefault(c => c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase)
            && (c.Source ?? "").Equals(store ?? "", StringComparison.OrdinalIgnoreCase));
        if (card != null)
        {
            card.ShaderModeOverride = mode == "Global" ? null : mode;
        }
    }

    /// <summary>Returns the per-game addon mode override, or "Global" if no override set.</summary>
    public string GetPerGameAddonMode(string gameName, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        if (_gameNameService.PerGameAddonMode.TryGetValue(key, out var mode)) return mode;
        // Fallback to name-only for legacy entries
        return _gameNameService.PerGameAddonMode.TryGetValue(gameName, out mode) ? mode : "Global";
    }

    /// <summary>Sets the per-game addon mode override. "Global" removes the override and clears per-game selection.</summary>
    public void SetPerGameAddonMode(string gameName, string mode, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        if (mode == "Global")
        {
            _gameNameService.PerGameAddonMode.Remove(key);
            _gameNameService.PerGameAddonMode.Remove(gameName); // clear legacy name-only entry too
            // Do NOT wipe PerGameAddonSelection here — preserve it so switching back to Select
            // restores the previous selection. Selection is only cleared explicitly by the user
            // through the addon picker.
        }
        else
            _gameNameService.PerGameAddonMode[key] = mode;
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

                // Capture SF installed state NOW before DeployAddonsForGame removes the addon file as stale
                var rdx5SvcRef = App.Services.GetRequiredService<Renodx5AddonService>();
                bool sfWasInstalled = rdx5SvcRef.IsSfInstalledIn(card.InstallPath);

                // Skip addon deployment for normal ReShade games (Req 3.1, 3.2)
                if (card.UseNormalReShade)
                {
                    _addonPackService.DeployAddonsForGame(gameName, card.InstallPath, is32Bit,
                        useGlobalSet: true, perGameSelection: new List<string>());
                    // Remove DLSS Fix INI settings since no addons are active
                    ApplyOrRemoveDlssFixIni(card, new List<string>(), true);
                    return;
                }

                string addonMode = GetPerGameAddonMode(gameName, card.Source ?? "");

                // "Off" mode → deploy with empty list (removes all managed addons)
                if (addonMode == "Off")
                {
                    _addonPackService.DeployAddonsForGame(gameName, card.InstallPath, is32Bit,
                        useGlobalSet: true, perGameSelection: new List<string>());
                    // Remove DLSS Fix INI settings since no addons are active
                    ApplyOrRemoveDlssFixIni(card, new List<string>(), true);
                    return;
                }

                bool useGlobalSet = addonMode != "Select";

                List<string>? selection = null;
                if (useGlobalSet)
                {
                    selection = _settingsViewModel.EnabledGlobalAddons;
                }
                else
                {
                    var key = GameKey.FromCard(gameName, card.Source).ToKey();
                    _gameNameService.PerGameAddonSelection.TryGetValue(key, out selection);
                }

                _addonPackService.DeployAddonsForGame(gameName, card.InstallPath, is32Bit,
                    useGlobalSet, selection);

                // If renodx-dlss5 is in the active selection, ensure nvngx_dlssnr.dll is also present
                var effectiveSelection = useGlobalSet ? selection : selection;
                bool rdx5Active = effectiveSelection?.Contains("DLSS5 Tool", StringComparer.OrdinalIgnoreCase) == true
                               || effectiveSelection?.Contains("RenoDX DLSS5", StringComparer.OrdinalIgnoreCase) == true;
                if (rdx5Active && !File.Exists(Path.Combine(card.InstallPath, "nvngx_dlssnr.dll")))
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var rdx5Svc = App.Services.GetRequiredService<Renodx5AddonService>();
                            await rdx5Svc.DeployNrDllIfAbsentAsync(card.InstallPath).ConfigureAwait(false);

                            // Refresh the card's DLSS detection so the NR column shows immediately
                            var detection = _dlssStreamlineService.Detect(card.InstallPath);
                            if (detection.HasAny)
                            {
                                _dlssStreamlineService.RecordDlssFound(card.GameName);
                                _dlssStreamlineService.RecordTrustedPath(card.GameName, detection);
                            }
                            DispatcherQueue?.TryEnqueue(() =>
                            {
                                card.ApplyDlssDetection(detection);
                                card.RefreshDlssVersions(_dlssStreamlineService);
                                RequestOverridesPanelRebuild?.Invoke(card);
                            });
                        }
                        catch (Exception nrEx)
                        {
                            _crashReporter.Log($"[MainViewModel.DeployAddonsForCard] NR DLL deploy failed for '{gameName}' — {nrEx.Message}");
                        }
                    });
                }

                // If SF variant is in the active selection, co-deploy full DLSS+Streamline stack
                // If it's NOT active but was previously installed, uninstall to restore .original files
                bool sfActive = effectiveSelection?.Contains("DLSS Tool (ShortFuse)", StringComparer.OrdinalIgnoreCase) == true;
                bool sfInstalled = sfWasInstalled;
                if (sfActive)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var rdx5Svc = App.Services.GetRequiredService<Renodx5AddonService>();
                            var detection = _dlssStreamlineService.Detect(card.InstallPath);
                            await rdx5Svc.InstallSfAsync(card.InstallPath, detection).ConfigureAwait(false);

                            // Apply SF auto-config (rename ReShade, install UAL, write [INSTALL] keys)
                            if (GetSfAutoConfigEnabled(card.GameName, card.Source ?? ""))
                            {
                                try { await ApplySfAutoConfigAsync(card).ConfigureAwait(false); }
                                catch (Exception acEx) { _crashReporter.Log($"[MainViewModel.DeployAddonsForCard] SfAutoConfig failed — {acEx.Message}"); }
                            }

                            // Refresh card DLSS state
                            var freshDetection = _dlssStreamlineService.Detect(card.InstallPath);
                            if (freshDetection.HasAny)
                            {
                                _dlssStreamlineService.RecordDlssFound(card.GameName);
                                _dlssStreamlineService.RecordTrustedPath(card.GameName, freshDetection);
                            }
                            DispatcherQueue?.TryEnqueue(() =>
                            {
                                card.ApplyDlssDetection(freshDetection);
                                card.RefreshDlssVersions(_dlssStreamlineService);
                                // Full detail panel rebuild so Nvidia Profile section appears
                                RequestCardRebuild?.Invoke(card);
                            });
                        }
                        catch (Exception sfEx)
                        {
                            _crashReporter.Log($"[MainViewModel.DeployAddonsForCard] SF co-deploy failed for '{gameName}' — {sfEx.Message}");
                        }
                    });
                }
                else if (sfInstalled)
                {
                    // SF was installed but is no longer selected — restore .original files and clean up
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            var rdx5Svc = App.Services.GetRequiredService<Renodx5AddonService>();
                            var detection = _dlssStreamlineService.Detect(card.InstallPath);
                            rdx5Svc.UninstallSf(card.InstallPath, detection);

                            var freshDetection = _dlssStreamlineService.Detect(card.InstallPath);
                            if (freshDetection.HasAny)
                                _dlssStreamlineService.RecordTrustedPath(card.GameName, freshDetection);
                            else
                                _dlssStreamlineService.RecordNoDlssFound(card.GameName);
                            DispatcherQueue?.TryEnqueue(() =>
                            {
                                card.ApplyDlssDetection(freshDetection);
                                card.RefreshDlssVersions(_dlssStreamlineService);
                                RequestCardRebuild?.Invoke(card);
                            });
                        }
                        catch (Exception sfEx)
                        {
                            _crashReporter.Log($"[MainViewModel.DeployAddonsForCard] SF uninstall failed for '{gameName}' — {sfEx.Message}");
                        }
                    });
                }

                // Apply/remove DLSS Fix INI settings based on whether DLSS Fix is now deployed
                ApplyOrRemoveDlssFixIni(card, selection, useGlobalSet);
            }
            catch (Exception ex)
            {
                _crashReporter.Log($"[MainViewModel.DeployAddonsForCard] Failed for '{gameName}' — {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Writes or removes [RENODX-DLSSFIX] section and [ADDON] LoadFromDllMain in reshade.ini
    /// based on whether DLSS Fix is deployed and the game has DLSS/Streamline paths detected.
    /// </summary>
    private void ApplyOrRemoveDlssFixIni(GameCardViewModel card, List<string>? perGameSelection, bool useGlobalSet)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return;

        var iniPath = Path.Combine(card.InstallPath, "reshade.ini");
        if (!File.Exists(iniPath)) return;

        // Determine if DLSS Fix is in the active addon selection
        bool dlssFixActive;
        if (perGameSelection != null)
            dlssFixActive = perGameSelection.Any(n => n.Contains("DLSS Fix", StringComparison.OrdinalIgnoreCase));
        else if (useGlobalSet)
            dlssFixActive = _settingsViewModel.EnabledGlobalAddons?.Any(n => n.Contains("DLSS Fix", StringComparison.OrdinalIgnoreCase)) == true;
        else
            dlssFixActive = false;

        try
        {
            var ini = AuxInstallService.ParseIni(File.ReadAllLines(iniPath));

            // Resolve DLSS detection — use card's cached result, or fall back to trusted path cache
            var detection = card.DlssDetection;
            if (detection == null && !string.IsNullOrEmpty(card.InstallPath))
            {
                try { detection = _dlssStreamlineService.TryFastDetect(card.GameName, card.InstallPath); }
                catch { /* non-critical fallback */ }
            }

            bool hasStreamline = card.HasStreamline || !string.IsNullOrEmpty(detection?.StreamlineInterposerPath);

            if (dlssFixActive && hasStreamline && detection != null)
            {
                // Add [ADDON] LoadFromDllMain
                if (!ini.ContainsKey("ADDON"))
                    ini["ADDON"] = new AuxInstallService.OrderedDict();
                ini["ADDON"]["LoadFromDllMain"] = "renodx-dlssfix.addon64";

                // Add [RENODX-DLSSFIX] section with paths
                if (!ini.ContainsKey("RENODX-DLSSFIX"))
                    ini["RENODX-DLSSFIX"] = new AuxInstallService.OrderedDict();

                if (!string.IsNullOrEmpty(detection.DlssPath))
                    ini["RENODX-DLSSFIX"]["DLSSPath"] = detection.DlssPath;
                if (!string.IsNullOrEmpty(detection.StreamlineInterposerPath))
                    ini["RENODX-DLSSFIX"]["StreamlinePath"] = detection.StreamlineInterposerPath;

                AuxInstallService.WriteIni(iniPath, ini);
                _crashReporter.Log($"[ApplyOrRemoveDlssFixIni] Applied DLSS Fix INI settings for '{card.GameName}'");
            }
            else
            {
                // Remove DLSS Fix sections if present
                bool changed = false;
                if (ini.Remove("RENODX-DLSSFIX")) changed = true;
                if (ini.TryGetValue("ADDON", out var addonSection) && addonSection.ContainsKey("LoadFromDllMain"))
                {
                    var val = addonSection["LoadFromDllMain"];
                    if (val.Contains("dlssfix", StringComparison.OrdinalIgnoreCase))
                    {
                        addonSection.Remove("LoadFromDllMain");
                        changed = true;
                    }
                }
                if (changed) AuxInstallService.WriteIni(iniPath, ini);
            }
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[ApplyOrRemoveDlssFixIni] Failed for '{card.GameName}' — {ex.Message}");
        }
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
                var cards = _allCards.ToList(); // Snapshot to avoid collection modification during enumeration
                foreach (var card in cards)
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

                    string addonMode = GetPerGameAddonMode(card.GameName, card.Source ?? "");

                    // "Off" mode → deploy with empty list (removes all managed addons)
                    if (addonMode == "Off")
                    {
                        _addonPackService.DeployAddonsForGame(card.GameName, card.InstallPath, is32Bit,
                            useGlobalSet: true, perGameSelection: new List<string>());
                        continue;
                    }

                    bool useGlobalSet = addonMode != "Select";

                    List<string>? selection = null;
                    if (useGlobalSet)
                    {
                        selection = _settingsViewModel.EnabledGlobalAddons;
                    }
                    else
                    {
                        var key = GameKey.FromCard(card.GameName, card.Source).ToKey();
                        _gameNameService.PerGameAddonSelection.TryGetValue(key, out selection);
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
            !c.IsHidden
            && !string.IsNullOrEmpty(c.InstallPath)
            && Directory.Exists(c.InstallPath)
            && ((c.Status   == GameStatus.UpdateAvailable && !c.ExcludeFromUpdateAllRenoDx && !c.IsExternalOnly) ||
                (c.RsStatus == GameStatus.UpdateAvailable && !c.ExcludeFromUpdateAllReShade && !c.RequiresVulkanInstall) ||
                (c.UlStatus == GameStatus.UpdateAvailable && !c.ExcludeFromUpdateAllUl) ||
                (c.DcStatus == GameStatus.UpdateAvailable && !c.ExcludeFromUpdateAllDc) ||
                (c.OsStatus == GameStatus.UpdateAvailable && !c.ExcludeFromUpdateAllOs) ||
                (c.DxvkStatus == GameStatus.UpdateAvailable && !c.ExcludeFromUpdateAllDxvk) ||
                (c.RefStatus == GameStatus.UpdateAvailable && !c.ExcludeFromUpdateAllRef) ||
                (c.LumaStatus == GameStatus.UpdateAvailable) ||
                (c.DofFixStatus == GameStatus.UpdateAvailable && !c.ExcludeFromUpdateAllDofFix)));

    // Button colours — purple when updates available, dim when idle
    public string UpdateAllBtnBackground => AnyUpdateAvailable ? "#201838" : "#1E242C";
    public string UpdateAllBtnForeground  => AnyUpdateAvailable ? "#B898E8" : "#6B7A8E";
    public string UpdateAllBtnBorder      => AnyUpdateAvailable ? "#3A2860" : "#283240";

    public bool IsUpdateAllExcludedReShade(string gameName, string store = "") => 
        _updateAllExcludedReShade.Contains(GameKey.From(gameName, store).ToKey());
    public bool IsUpdateAllExcludedRenoDx(string gameName, string store = "") => 
        _updateAllExcludedRenoDx.Contains(GameKey.From(gameName, store).ToKey());
    public bool IsUpdateAllExcludedUl(string gameName, string store = "") => 
        _updateAllExcludedUl.Contains(GameKey.From(gameName, store).ToKey());
    public bool IsUpdateAllExcludedDc(string gameName, string store = "") => 
        _updateAllExcludedDc.Contains(GameKey.From(gameName, store).ToKey());
    public bool IsUpdateAllExcludedOs(string gameName, string store = "") => 
        _updateAllExcludedOs.Contains(GameKey.From(gameName, store).ToKey());
    public bool IsUpdateAllExcludedRef(string gameName, string store = "") => 
        _updateAllExcludedRef.Contains(GameKey.From(gameName, store).ToKey());

    /// <summary>Returns true if the game is configured to use normal (non-addon) ReShade.</summary>
    public bool IsNormalReShadeGame(string gameName, string store = "") => 
        _normalReShadeGames.Contains(GameKey.From(gameName, store).ToKey());

    public void ToggleUpdateAllExclusionReShade(string gameName, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        var set = _gameNameService.UpdateAllExcludedReShade;
        if (!set.Remove(key)) set.Add(key);
        bool isExcluded = set.Contains(key);

        var card = _allCards.FirstOrDefault(c => c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase)
            && (c.Source ?? "").Equals(store ?? "", StringComparison.OrdinalIgnoreCase));
        if (card != null) card.ExcludeFromUpdateAllReShade = isExcluded;

        // Vulkan games share a global layer — propagate exclusion to ALL Vulkan games
        if (card?.RequiresVulkanInstall == true)
        {
            foreach (var vCard in _allCards.Where(c => c.RequiresVulkanInstall && !c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase)))
            {
                var vKey = GameKey.FromCard(vCard.GameName, vCard.Source).ToKey();
                if (isExcluded)
                    set.Add(vKey);
                else
                    set.Remove(vKey);
                vCard.ExcludeFromUpdateAllReShade = isExcluded;
            }
        }

        SaveNameMappings();
        NotifyUpdateButtonChanged();
    }

    public void ToggleUpdateAllExclusionRenoDx(string gameName, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        var set = _gameNameService.UpdateAllExcludedRenoDx;
        if (!set.Remove(key)) set.Add(key);
        SaveNameMappings();
        var card = _allCards.FirstOrDefault(c => c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase)
            && (c.Source ?? "").Equals(store ?? "", StringComparison.OrdinalIgnoreCase));
        if (card != null) card.ExcludeFromUpdateAllRenoDx = set.Contains(key);
        NotifyUpdateButtonChanged();
    }

    public void ToggleUpdateAllExclusionUl(string gameName, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        var set = _gameNameService.UpdateAllExcludedUl;
        if (!set.Remove(key)) set.Add(key);
        SaveNameMappings();
        var card = _allCards.FirstOrDefault(c => c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase)
            && (c.Source ?? "").Equals(store ?? "", StringComparison.OrdinalIgnoreCase));
        if (card != null) card.ExcludeFromUpdateAllUl = set.Contains(key);
        NotifyUpdateButtonChanged();
    }

    public void ToggleUpdateAllExclusionDc(string gameName, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        var set = _gameNameService.UpdateAllExcludedDc;
        if (!set.Remove(key)) set.Add(key);
        SaveNameMappings();
        var card = _allCards.FirstOrDefault(c => c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase)
            && (c.Source ?? "").Equals(store ?? "", StringComparison.OrdinalIgnoreCase));
        if (card != null) card.ExcludeFromUpdateAllDc = set.Contains(key);
        NotifyUpdateButtonChanged();
    }

    public void ToggleUpdateAllExclusionOs(string gameName, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        var set = _gameNameService.UpdateAllExcludedOs;
        if (!set.Remove(key)) set.Add(key);
        SaveNameMappings();
        var card = _allCards.FirstOrDefault(c => c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase)
            && (c.Source ?? "").Equals(store ?? "", StringComparison.OrdinalIgnoreCase));
        if (card != null) card.ExcludeFromUpdateAllOs = set.Contains(key);
        NotifyUpdateButtonChanged();
    }

    public void ToggleUpdateAllExclusionRef(string gameName, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        var set = _gameNameService.UpdateAllExcludedRef;
        if (!set.Remove(key)) set.Add(key);
        SaveNameMappings();
        var card = _allCards.FirstOrDefault(c => c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase)
            && (c.Source ?? "").Equals(store ?? "", StringComparison.OrdinalIgnoreCase));
        if (card != null) card.ExcludeFromUpdateAllRef = set.Contains(key);
        NotifyUpdateButtonChanged();
    }

    public bool IsUpdateAllExcludedDxvk(string gameName, string store = "")
    {
        var card = _allCards.FirstOrDefault(c => c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase)
            && (c.Source ?? "").Equals(store ?? "", StringComparison.OrdinalIgnoreCase));
        return card?.ExcludeFromUpdateAllDxvk ?? false;
    }

    public void ToggleUpdateAllExclusionDxvk(string gameName, string store = "")
    {
        var card = _allCards.FirstOrDefault(c => c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase)
            && (c.Source ?? "").Equals(store ?? "", StringComparison.OrdinalIgnoreCase));
        if (card != null)
        {
            card.ExcludeFromUpdateAllDxvk = !card.ExcludeFromUpdateAllDxvk;
            SaveLibrary();
            NotifyUpdateButtonChanged();
        }
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
    public async Task ApplyPresetShadersAsync(string gameName, IEnumerable<string> presetFilePaths, string store = "")
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

            // 3b. Expand dependencies — if a matched pack requires another, include it
            var expandedPackIds = _shaderPackService.ExpandPackDependencies(matchedPackIds).ToList();

            // 4. Log unresolved files
            foreach (var unresolved in unresolvedFiles)
                _crashReporter.Log($"[MainViewModel.ApplyPresetShaders] Unresolved shader: {unresolved}");

            if (matchedPackIds.Count == 0)
            {
                _crashReporter.Log("[MainViewModel.ApplyPresetShaders] No matching shader packs found");
                return;
            }

            // 5. Set per-game mode to "Select"
            SetPerGameShaderMode(gameName, "Select", store);

            // 6. Merge resolved pack IDs with existing selection (union) — use composite key
            var shaderKey = GameKey.From(gameName, store).ToKey();
            if (_gameNameService.PerGameShaderSelection.TryGetValue(shaderKey, out var existing)
                || _gameNameService.PerGameShaderSelection.TryGetValue(gameName, out existing))
            {
                var merged = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
                merged.UnionWith(expandedPackIds);
                _gameNameService.PerGameShaderSelection[shaderKey] = merged.ToList();
            }
            else
            {
                _gameNameService.PerGameShaderSelection[shaderKey] = expandedPackIds;
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

    // ── DLSS / Streamline Auto-Update ─────────────────────────────────────────

    /// <summary>
    /// Checks if the DLSS/Streamline manifest has a newer version than what was
    /// previously the latest. If so, auto-swaps games that are on the previous
    /// latest to the new latest. Games on older manual versions are left alone.
    /// </summary>
    public async Task RunDlssAutoUpdateAsync()
    {
        var dlssVersions = _dlssStreamlineService.DlssVersions;
        var slVersions = _dlssStreamlineService.StreamlineVersions;

        if (dlssVersions.Count == 0 && slVersions.Count == 0) return;

        var newestDlss = dlssVersions.Count > 0 ? dlssVersions[0] : "";
        var newestSl = slVersions.Count > 0 ? slVersions[0] : "";

        var previousDlss = _settingsViewModel.LastKnownNewestDlss;
        var previousSl = _settingsViewModel.LastKnownNewestStreamline;

        // First run: seed the baseline without updating anything
        if (string.IsNullOrEmpty(previousDlss) && !string.IsNullOrEmpty(newestDlss))
        {
            _settingsViewModel.LastKnownNewestDlss = newestDlss;
            SaveSettingsPublic();
        }
        if (string.IsNullOrEmpty(previousSl) && !string.IsNullOrEmpty(newestSl))
        {
            _settingsViewModel.LastKnownNewestStreamline = newestSl;
            SaveSettingsPublic();
            return; // First run — no updates to apply
        }

        bool dlssHasNewVersion = !string.IsNullOrEmpty(previousDlss)
            && !string.Equals(newestDlss, previousDlss, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(newestDlss);

        bool slHasNewVersion = !string.IsNullOrEmpty(previousSl)
            && !string.Equals(newestSl, previousSl, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(newestSl);

        if (!dlssHasNewVersion && !slHasNewVersion) return;

        int dlssUpdated = 0, slUpdated = 0;

        foreach (var card in _allCards)
        {
            if (card.DlssDetection == null || !card.HasAnyDlssStreamline) continue;

            // ── DLSS Auto-Update (SR, RR, FG all use the same version set) ──
            if (_settingsViewModel.AutoUpdateDlss && dlssHasNewVersion)
            {
                try
                {
                    // SR
                    if (card.DlssDetection.DlssPath != null
                        && string.Equals(card.DlssInstalledVersion, previousDlss, StringComparison.OrdinalIgnoreCase)
                        && !(card.DlssInstalledVersion?.StartsWith("1.") == true))
                    {
                        await _dlssStreamlineService.SwapDlssAsync(card.DlssDetection.DlssPath, newestDlss);
                        card.DlssInstalledVersion = _dlssStreamlineService.GetFileVersion(card.DlssDetection.DlssPath);
                        dlssUpdated++;
                    }
                    // RR
                    if (card.DlssDetection.DlssdPath != null
                        && string.Equals(card.DlssdInstalledVersion, previousDlss, StringComparison.OrdinalIgnoreCase)
                        && !(card.DlssdInstalledVersion?.StartsWith("1.") == true))
                    {
                        await _dlssStreamlineService.SwapDlssdAsync(card.DlssDetection.DlssdPath, newestDlss);
                        card.DlssdInstalledVersion = _dlssStreamlineService.GetFileVersion(card.DlssDetection.DlssdPath);
                    }
                    // FG
                    if (card.DlssDetection.DlssgPath != null
                        && string.Equals(card.DlssgInstalledVersion, previousDlss, StringComparison.OrdinalIgnoreCase)
                        && !(card.DlssgInstalledVersion?.StartsWith("1.") == true))
                    {
                        await _dlssStreamlineService.SwapDlssgAsync(card.DlssDetection.DlssgPath, newestDlss);
                        card.DlssgInstalledVersion = _dlssStreamlineService.GetFileVersion(card.DlssDetection.DlssgPath);
                    }
                    // NR (dev-only)
                    if (FeatureFlags.DlssNr
                        && card.DlssDetection.DlssnrPath != null
                        && string.Equals(card.DlssnrInstalledVersion, previousDlss, StringComparison.OrdinalIgnoreCase))
                    {
                        await _dlssStreamlineService.SwapDlssnrAsync(card.DlssDetection.DlssnrPath, newestDlss);
                        card.DlssnrInstalledVersion = _dlssStreamlineService.GetFileVersion(card.DlssDetection.DlssnrPath);
                    }
                }
                catch (Exception ex)
                {
                    _crashReporter.Log($"[RunDlssAutoUpdateAsync] DLSS auto-update failed for '{card.GameName}' — {ex.Message}");
                }
            }

            // ── Streamline Auto-Update ──
            if (_settingsViewModel.AutoUpdateStreamline && slHasNewVersion)
            {
                try
                {
                    if (card.DlssDetection.StreamlineFolder != null
                        && string.Equals(card.StreamlineInstalledVersion, previousSl, StringComparison.OrdinalIgnoreCase)
                        && !(card.StreamlineInstalledVersion?.StartsWith("1.") == true))
                    {
                        await _dlssStreamlineService.SwapStreamlineAsync(card.DlssDetection.StreamlineFolder, newestSl);
                        card.StreamlineInstalledVersion = _dlssStreamlineService.GetFileVersion(
                            card.DlssDetection.StreamlineInterposerPath ?? "");
                        slUpdated++;
                    }
                }
                catch (Exception ex)
                {
                    _crashReporter.Log($"[RunDlssAutoUpdateAsync] Streamline auto-update failed for '{card.GameName}' — {ex.Message}");
                }
            }
        }

        // Update the baseline
        if (dlssHasNewVersion)
            _settingsViewModel.LastKnownNewestDlss = newestDlss;
        if (slHasNewVersion)
            _settingsViewModel.LastKnownNewestStreamline = newestSl;
        SaveSettingsPublic();

        if (dlssUpdated > 0 || slUpdated > 0)
            _crashReporter.Log($"[RunDlssAutoUpdateAsync] Auto-updated {dlssUpdated} game(s) DLSS → {newestDlss}, {slUpdated} game(s) Streamline → {newestSl}");
    }
}
