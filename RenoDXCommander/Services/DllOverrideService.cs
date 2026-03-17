using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Services;

/// <summary>
/// Owns DLL override CRUD operations and the backing data store.
/// Extracted from MainViewModel per Requirement 1.4.
/// </summary>
public class DllOverrideService : IDllOverrideService
{
    private readonly IAuxInstallService _auxInstaller;

    /// <summary>Per-game DLL naming overrides. Key = game name, Value = config with custom file names.</summary>
    private Dictionary<string, DllOverrideConfig> _dllOverrides = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Games where a manifest-driven DLL override was injected by ApplyManifestDllRenames.
    /// These are NOT persisted — they are re-evaluated on every refresh.
    /// </summary>
    private HashSet<string> _manifestDllOverrideGames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Games where the user has explicitly disabled a manifest-driven DLL override.
    /// These are persisted to settings.json so the opt-out survives refreshes.
    /// </summary>
    private HashSet<string> _manifestDllOverrideOptOuts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised when the overrides dictionary changes and settings should be persisted.</summary>
    public Action? OverridesChanged { get; set; }

    public DllOverrideService(IAuxInstallService auxInstaller)
    {
        _auxInstaller = auxInstaller;
    }

    // ── Data access for serialization ─────────────────────────────────────────

    public Dictionary<string, DllOverrideConfig> GetAllOverrides() => _dllOverrides;
    public HashSet<string> ManifestDllOverrideGames => _manifestDllOverrideGames;
    public HashSet<string> ManifestDllOverrideOptOuts => _manifestDllOverrideOptOuts;

    public void SetOverridesFromSettings(
        Dictionary<string, DllOverrideConfig> overrides,
        HashSet<string> optOuts)
    {
        _dllOverrides = overrides;
        _manifestDllOverrideOptOuts = optOuts;
        _manifestDllOverrideGames.Clear();
    }

    /// <summary>Clears manifest-injected overrides so they can be re-evaluated on refresh.</summary>
    public void ClearManifestOverrides()
    {
        foreach (var name in _manifestDllOverrideGames)
            _dllOverrides.Remove(name);
        _manifestDllOverrideGames.Clear();
    }

    /// <summary>Prunes opt-outs for games no longer in the manifest's dllNameOverrides.</summary>
    public void PruneOptOuts(HashSet<string> manifestKeys, Func<string, string> normalizer)
    {
        _manifestDllOverrideOptOuts.RemoveWhere(name =>
            !manifestKeys.Contains(name)
            && !manifestKeys.Any(k => normalizer(k) == normalizer(name)));
    }

    // ── CRUD ──────────────────────────────────────────────────────────────────

    public bool HasDllOverride(string gameName) => _dllOverrides.ContainsKey(gameName);

    public DllOverrideConfig? GetDllOverride(string gameName)
        => _dllOverrides.TryGetValue(gameName, out var cfg) ? cfg : null;

    public void SetDllOverride(string gameName, string reshadeFileName, string dcFileName)
    {
        _dllOverrides[gameName] = new DllOverrideConfig
        {
            ReShadeFileName = reshadeFileName.Trim(),
            DcFileName = dcFileName.Trim(),
        };
        OverridesChanged?.Invoke();
    }

    public void RemoveDllOverride(string gameName)
    {
        _dllOverrides.Remove(gameName);
        OverridesChanged?.Invoke();
    }

    /// <summary>
    /// Called when DLL override is toggled ON — renames existing ReShade and DC
    /// files in the game folder to the custom filenames so they stay installed.
    /// </summary>
    public void EnableDllOverride(GameCardViewModel card, string reshadeFileName, string dcFileName)
    {
        var name = card.GameName;

        // Rename existing ReShade to the custom filename
        if (card.RsRecord != null && !string.IsNullOrEmpty(card.InstallPath))
        {
            var oldPath = Path.Combine(card.InstallPath, card.RsRecord.InstalledAs);
            var newPath = Path.Combine(card.InstallPath, reshadeFileName);
            try
            {
                if (File.Exists(oldPath) && !oldPath.Equals(newPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(newPath)) File.Delete(newPath);
                    File.Move(oldPath, newPath);
                    card.RsRecord.InstalledAs = reshadeFileName;
                    _auxInstaller.SaveAuxRecord(card.RsRecord);
                    card.RsInstalledFile = reshadeFileName;
                }
            }
            catch (Exception ex) { CrashReporter.Log($"[DllOverrideService.EnableDllOverride] Failed to rename RS file for '{name}' — {ex.Message}"); }
        }

        // Rename existing DC to the custom filename
        if (card.DcRecord != null && !string.IsNullOrEmpty(card.InstallPath))
        {
            var oldPath = Path.Combine(card.InstallPath, card.DcRecord.InstalledAs);
            var newPath = Path.Combine(card.InstallPath, dcFileName);
            try
            {
                if (File.Exists(oldPath) && !oldPath.Equals(newPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(newPath)) File.Delete(newPath);
                    File.Move(oldPath, newPath);
                    card.DcRecord.InstalledAs = dcFileName;
                    _auxInstaller.SaveAuxRecord(card.DcRecord);
                    card.DcInstalledFile = dcFileName;
                }
            }
            catch (Exception ex) { CrashReporter.Log($"[DllOverrideService.EnableDllOverride] Failed to rename DC file for '{name}' — {ex.Message}"); }
        }

        SetDllOverride(name, reshadeFileName, dcFileName);
        // User is explicitly enabling — clear any manifest opt-out for this game
        _manifestDllOverrideOptOuts.Remove(name);
        card.DllOverrideEnabled = true;
        card.ExcludeFromUpdateAllReShade = true;
        card.ExcludeFromUpdateAllDc = true;
        card.ExcludeFromUpdateAllRenoDx = true;
        card.NotifyAll();
    }

    /// <summary>
    /// Called when DLL override is already ON and the filenames are updated —
    /// renames existing files on disk to the new custom names.
    /// </summary>
    public void UpdateDllOverrideNames(GameCardViewModel card, string newRsName, string newDcName)
    {
        var name = card.GameName;
        var installPath = card.InstallPath;
        var oldCfg = GetDllOverride(name);
        if (oldCfg == null || string.IsNullOrEmpty(installPath))
        {
            SetDllOverride(name, newRsName, newDcName);
            return;
        }

        // Rename ReShade if filename changed
        if (!string.IsNullOrEmpty(oldCfg.ReShadeFileName)
            && !oldCfg.ReShadeFileName.Equals(newRsName, StringComparison.OrdinalIgnoreCase))
        {
            var oldPath = Path.Combine(installPath, oldCfg.ReShadeFileName);
            var newPath = Path.Combine(installPath, newRsName);
            try
            {
                if (File.Exists(oldPath) && !File.Exists(newPath))
                {
                    File.Move(oldPath, newPath);
                    if (card.RsRecord != null)
                    {
                        card.RsRecord.InstalledAs = newRsName;
                        _auxInstaller.SaveAuxRecord(card.RsRecord);
                        card.RsInstalledFile = newRsName;
                    }
                }
            }
            catch (Exception ex) { CrashReporter.Log($"[DllOverrideService.UpdateDllOverrideNames] Failed to rename RS file for '{name}' — {ex.Message}"); }
        }

        // Rename DC if filename changed
        if (!string.IsNullOrEmpty(oldCfg.DcFileName)
            && !oldCfg.DcFileName.Equals(newDcName, StringComparison.OrdinalIgnoreCase))
        {
            var oldPath = Path.Combine(installPath, oldCfg.DcFileName);
            var newPath = Path.Combine(installPath, newDcName);
            try
            {
                if (File.Exists(oldPath) && !File.Exists(newPath))
                {
                    File.Move(oldPath, newPath);
                    if (card.DcRecord != null)
                    {
                        card.DcRecord.InstalledAs = newDcName;
                        _auxInstaller.SaveAuxRecord(card.DcRecord);
                        card.DcInstalledFile = newDcName;
                    }
                }
            }
            catch (Exception ex) { CrashReporter.Log($"[DllOverrideService.UpdateDllOverrideNames] Failed to rename DC file for '{name}' — {ex.Message}"); }
        }

        SetDllOverride(name, newRsName, newDcName);
        card.NotifyAll();
    }

    /// <summary>
    /// Called when DLL override is toggled OFF — removes the custom-named DLL files from the game folder.
    /// </summary>
    public void DisableDllOverride(GameCardViewModel card)
    {
        var name = card.GameName;
        var cfg = GetDllOverride(name);
        if (cfg != null && !string.IsNullOrEmpty(card.InstallPath))
        {
            // Remove the custom-named files
            if (!string.IsNullOrWhiteSpace(cfg.ReShadeFileName))
            {
                var rsPath = Path.Combine(card.InstallPath, cfg.ReShadeFileName);
                try { if (File.Exists(rsPath)) File.Delete(rsPath); } catch (Exception ex) { CrashReporter.Log($"[DllOverrideService.DisableDllOverride] Failed to delete RS file '{rsPath}' — {ex.Message}"); }
            }
            if (!string.IsNullOrWhiteSpace(cfg.DcFileName))
            {
                var dcPath = Path.Combine(card.InstallPath, cfg.DcFileName);
                try { if (File.Exists(dcPath)) File.Delete(dcPath); } catch (Exception ex) { CrashReporter.Log($"[DllOverrideService.DisableDllOverride] Failed to delete DC file '{dcPath}' — {ex.Message}"); }
            }
        }

        // If this was a manifest-injected override, record that the user has opted out
        // so ApplyManifestDllRenames doesn't re-enable it on the next refresh.
        if (_manifestDllOverrideGames.Contains(name))
        {
            _manifestDllOverrideOptOuts.Add(name);
            _manifestDllOverrideGames.Remove(name);
        }

        RemoveDllOverride(name);
        card.DllOverrideEnabled = false;
        card.RsStatus = GameStatus.NotInstalled;
        card.DcStatus = GameStatus.NotInstalled;
        card.NotifyAll();
    }

    /// <summary>Migrates a DLL override entry when a game is renamed.</summary>
    public void MigrateOverride(string oldName, string newName)
    {
        if (_dllOverrides.TryGetValue(oldName, out var dllCfg))
        {
            _dllOverrides.Remove(oldName);
            _dllOverrides[newName] = dllCfg;
        }
    }

    /// <summary>
    /// Returns a filtered dictionary excluding manifest-injected overrides (for persistence).
    /// </summary>
    public Dictionary<string, DllOverrideConfig> GetUserOverridesForSave()
    {
        return _dllOverrides
            .Where(kv => !_manifestDllOverrideGames.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
    }
}
