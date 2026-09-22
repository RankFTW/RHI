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

    /// <summary>
    /// Per-game OptiScaler DLL filename overrides from the remote manifest.
    /// Key = game name, Value = DLL filename string (e.g. "winmm.dll").
    /// Re-evaluated on every manifest load.
    /// </summary>
    private Dictionary<string, string> _manifestOsDllOverrides = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised when the overrides dictionary changes and settings should be persisted.</summary>
    public Action? OverridesChanged { get; set; }

    /// <summary>
    /// Async callback set by the UI layer. Called when a DC DLL override name
    /// conflicts with an existing non-DC file in the game folder.
    /// Returns true if the user confirms overwrite, false to cancel.
    /// </summary>
    public Func<GameCardViewModel, string, Task<bool>>? ConfirmForeignDcOverwrite { get; set; }

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

    public bool HasDllOverride(string gameName)
    {
        if (!_dllOverrides.TryGetValue(gameName, out var cfg)) return false;
        // An entry with all empty fields is a ghost left by SetOsDllOverride("") on uninstall — treat as no override
        return !string.IsNullOrEmpty(cfg.ReShadeFileName)
            || !string.IsNullOrEmpty(cfg.DcFileName)
            || !string.IsNullOrEmpty(cfg.OsFileName);
    }

    public DllOverrideConfig? GetDllOverride(string gameName)
        => _dllOverrides.TryGetValue(gameName, out var cfg) ? cfg : null;

    public void SetDllOverride(string gameName, string reshadeFileName, string dcFileName)
    {
        var existing = GetDllOverride(gameName);
        _dllOverrides[gameName] = new DllOverrideConfig
        {
            ReShadeFileName = reshadeFileName.Trim(),
            DcFileName = dcFileName.Trim(),
            OsFileName = existing?.OsFileName ?? "",
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

        // Collision guard: skip RS rename if reshadeFileName matches effective DC name
        bool skipRsRename = !string.IsNullOrEmpty(dcFileName)
            && reshadeFileName.Equals(dcFileName, StringComparison.OrdinalIgnoreCase);

        // Rename existing ReShade to the custom filename
        if (!skipRsRename && card.RsRecord != null && !string.IsNullOrEmpty(card.InstallPath))
        {
            var oldPath = Path.Combine(card.InstallPath, card.RsRecord.InstalledAs);
            var newPath = Path.Combine(card.InstallPath, reshadeFileName);
            CrashReporter.Log($"[DllOverrideService.EnableDllOverride] RS rename check: InstalledAs='{card.RsRecord.InstalledAs}', targetName='{reshadeFileName}', sameFile={oldPath.Equals(newPath, StringComparison.OrdinalIgnoreCase)}");
            try
            {
                if (File.Exists(oldPath) && !oldPath.Equals(newPath, StringComparison.OrdinalIgnoreCase))
                {
                    // Delete target if it exists — use overwrite-safe pattern
                    if (File.Exists(newPath))
                    {
                        CrashReporter.Log($"[DllOverrideService.EnableDllOverride] Target '{reshadeFileName}' exists — deleting before rename");
                        try { File.Delete(newPath); }
                        catch (Exception delEx)
                        {
                            CrashReporter.Log($"[DllOverrideService.EnableDllOverride] Cannot delete existing '{newPath}' — {delEx.Message}. Trying overwrite via temp file.");
                            // Fallback: copy source to temp, delete source, rename temp
                            var tempPath = newPath + ".tmp";
                            File.Copy(oldPath, tempPath, overwrite: true);
                            File.Delete(oldPath);
                            if (File.Exists(newPath)) File.Delete(newPath);
                            File.Move(tempPath, newPath);
                            card.RsRecord.InstalledAs = reshadeFileName;
                            _auxInstaller.SaveAuxRecord(card.RsRecord);
                            card.RsInstalledFile = reshadeFileName;
                            goto rsRenameDone;
                        }
                    }
                    File.Move(oldPath, newPath);
                    card.RsRecord.InstalledAs = reshadeFileName;
                    _auxInstaller.SaveAuxRecord(card.RsRecord);
                    card.RsInstalledFile = reshadeFileName;
                }
                rsRenameDone:;
            }
            catch (Exception ex) { CrashReporter.Log($"[DllOverrideService.EnableDllOverride] Failed to rename RS file for '{name}' — {ex.Message}"); }
        }

        // Rename existing DC to the custom filename if DC is installed
        // Collision guard: skip DC rename if dcFileName matches reshadeFileName
        if (!string.IsNullOrEmpty(dcFileName)
            && !dcFileName.Equals(reshadeFileName, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(card.DcInstalledFile) && !string.IsNullOrEmpty(card.InstallPath))
        {
            RenameDcFile(card, card.DcInstalledFile, dcFileName, "EnableDllOverride");
        }

        SetDllOverride(name, reshadeFileName, dcFileName);
        // User is explicitly enabling — clear any manifest opt-out for this game
        _manifestDllOverrideOptOuts.Remove(name);
        card.DllOverrideEnabled = true;
        card.ExcludeFromUpdateAllReShade = true;
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

        // Determine current RS source name: use saved config value, fall back to what's actually on disk.
        // When the config was created for an OS-only override, ReShadeFileName is empty even though
        // ReShade is physically installed as ReShade64.dll (or whatever InstalledAs says).
        var currentRsFileName = !string.IsNullOrEmpty(oldCfg.ReShadeFileName)
            ? oldCfg.ReShadeFileName
            : card.RsRecord?.InstalledAs ?? card.RsInstalledFile;

        // Rename ReShade if filename changed
        if (!string.IsNullOrEmpty(currentRsFileName)
            && !currentRsFileName.Equals(newRsName, StringComparison.OrdinalIgnoreCase))
        {
            var oldPath = Path.Combine(installPath, currentRsFileName);
            var newPath = Path.Combine(installPath, newRsName);
            try
            {
                if (File.Exists(oldPath) && !File.Exists(newPath))
                {
                    // Collision guard: skip RS rename if the target name is occupied by OptiScaler
                    var osEffectiveName = GetEffectiveOsName(name);
                    if (newRsName.Equals(osEffectiveName, StringComparison.OrdinalIgnoreCase))
                    {
                        CrashReporter.Log($"[DllOverrideService.UpdateDllOverrideNames] Skipping RS rename — '{newRsName}' collides with OptiScaler name for '{name}'.");
                    }
                    else
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
            }
            catch (Exception ex) { CrashReporter.Log($"[DllOverrideService.UpdateDllOverrideNames] Failed to rename RS file for '{name}' — {ex.Message}"); }
        }

        // Rename DC if filename changed and DC is installed
        // Collision guard: skip DC rename if newDcName matches newRsName
        if (!string.IsNullOrEmpty(card.DcInstalledFile)
            && !newDcName.Equals(newRsName, StringComparison.OrdinalIgnoreCase))
        {
            var oldDcName = oldCfg.DcFileName;
            var currentDcFile = card.DcInstalledFile;
            // Determine what the DC file should be renamed from
            var dcOldName = !string.IsNullOrEmpty(oldDcName) ? oldDcName : currentDcFile;
            if (!string.IsNullOrEmpty(newDcName)
                && !dcOldName.Equals(newDcName, StringComparison.OrdinalIgnoreCase))
            {
                RenameDcFile(card, currentDcFile, newDcName, "UpdateDllOverrideNames");
            }
        }

        SetDllOverride(name, newRsName, newDcName);
        // NotifyAll intentionally NOT called here — callers are on the UI thread and
        // call it themselves (or rely on BuildOverridesPanel). Calling it from a
        // background thread causes a COMException in XAML bindings.
    }

    /// <summary>
    /// Called when DLL override is toggled OFF — removes the custom-named DLL files from the game folder.
    /// Checks for collisions before reverting to default names.
    /// </summary>
    public DllDisableResult DisableDllOverride(GameCardViewModel card)
    {
        var name = card.GameName;
        var cfg = GetDllOverride(name);
        bool rsReverted = true;
        bool dcReverted = true;
        CrashReporter.Log($"[DllOverrideService.DisableDllOverride] '{name}' — cfg.ReShadeFileName='{cfg?.ReShadeFileName}', RsRecord.InstalledAs='{card.RsRecord?.InstalledAs}', RsInstalledFile='{card.RsInstalledFile}'");

        if (cfg != null && !string.IsNullOrEmpty(card.InstallPath))
        {
            // IMPORTANT: Revert DC first, then RS. This ensures that if DC was renamed to
            // dxgi.dll (the RS default), it gets moved out of the way before RS tries to
            // reclaim dxgi.dll. Without this ordering, RS would see dxgi.dll occupied and
            // fall back to ReShade64/32.dll unnecessarily.

            // Revert DC file to default addon name if DC is installed with a custom name
            if (!string.IsNullOrWhiteSpace(cfg.DcFileName) && !string.IsNullOrEmpty(card.DcInstalledFile))
            {
                var defaultDcName = MainViewModel.GetDcFileName(card.Is32Bit);

                var deployPath = ModInstallService.GetAddonDeployPath(card.InstallPath);

                // Find the DC file — check addon deploy path first, then game install path
                string? dcOldPath = null;
                if (File.Exists(Path.Combine(deployPath, card.DcInstalledFile)))
                    dcOldPath = Path.Combine(deployPath, card.DcInstalledFile);
                else if (File.Exists(Path.Combine(card.InstallPath, card.DcInstalledFile)))
                    dcOldPath = Path.Combine(card.InstallPath, card.DcInstalledFile);

                if (dcOldPath != null)
                {
                    var dcNewPath = Path.Combine(deployPath, defaultDcName);

                    if (dcOldPath.Equals(dcNewPath, StringComparison.OrdinalIgnoreCase))
                    {
                        // Already at default name — nothing to do
                    }
                    else if (File.Exists(dcNewPath))
                    {
                        // Default DC name is occupied — keep DC under its current name
                        dcReverted = false;
                    }
                    else
                    {
                        // Default name is free — rename normally via the existing helper
                        RenameDcFile(card, card.DcInstalledFile, defaultDcName, "DisableDllOverride");
                    }
                }
            }

            // Revert the custom-named ReShade file back to the default name
            // (done after DC revert so dxgi.dll is likely free now)
            if (!string.IsNullOrWhiteSpace(cfg.ReShadeFileName) && card.RsRecord != null)
            {
                var defaultRsName = AuxInstallService.RsNormalName;
                var rsOldPath = Path.Combine(card.InstallPath, cfg.ReShadeFileName);
                var rsNewPath = Path.Combine(card.InstallPath, defaultRsName);

                // Already at default name — nothing to do
                if (rsOldPath.Equals(rsNewPath, StringComparison.OrdinalIgnoreCase))
                {
                    // No rename needed, already default
                }
                else if (!File.Exists(rsOldPath))
                {
                    // Source file doesn't exist — nothing to rename
                }
                else
                {
                    // Check if the default name is occupied by another file (DC or foreign)
                    bool defaultOccupied = File.Exists(rsNewPath);
                    if (defaultOccupied)
                    {
                        // Default name is occupied — fall back to ReShade64/32.dll based on bitness
                        var fallbackName = card.Is32Bit ? AuxInstallService.RsStaged32 : AuxInstallService.RsStaged64;
                        var fallbackPath = Path.Combine(card.InstallPath, fallbackName);
                        try
                        {
                            if (!rsOldPath.Equals(fallbackPath, StringComparison.OrdinalIgnoreCase))
                            {
                                if (File.Exists(fallbackPath)) File.Delete(fallbackPath);
                                File.Move(rsOldPath, fallbackPath);
                                card.RsRecord.InstalledAs = fallbackName;
                                _auxInstaller.SaveAuxRecord(card.RsRecord);
                                card.RsInstalledFile = fallbackName;
                            }
                        }
                        catch (Exception ex)
                        {
                            CrashReporter.Log($"[DllOverrideService.DisableDllOverride] Failed to rename RS file to fallback '{fallbackName}' — {ex.Message}");
                        }
                        rsReverted = false;
                    }
                    else
                    {
                        // Default name is free — rename normally
                        try
                        {
                            File.Move(rsOldPath, rsNewPath);
                            card.RsRecord.InstalledAs = defaultRsName;
                            _auxInstaller.SaveAuxRecord(card.RsRecord);
                            card.RsInstalledFile = defaultRsName;
                        }
                        catch (Exception ex)
                        {
                            CrashReporter.Log($"[DllOverrideService.DisableDllOverride] Failed to rename RS file back to default — {ex.Message}");
                            rsReverted = false;
                        }
                    }
                }
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
        // NotifyAll intentionally NOT called here — must be called by the caller on the UI thread.

        return new DllDisableResult(rsReverted, dcReverted);
    }

    // ── DC file rename helper ────────────────────────────────────────────────

    /// <summary>
    /// Renames the DC file on disk from <paramref name="oldFileName"/> to <paramref name="newFileName"/>.
    /// Searches both the addon deploy path and the game install path.
    /// Updates the AuxInstalledRecord and card.DcInstalledFile on success.
    /// </summary>
    private void RenameDcFile(GameCardViewModel card, string oldFileName, string newFileName, string caller)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return;

        // Collision guard: abort if newFileName matches the RS name AND RS is actually installed
        if (card.RsRecord != null || !string.IsNullOrEmpty(card.RsInstalledFile))
        {
            var effectiveRs = GetEffectiveRsName(card.GameName);
            if (newFileName.Equals(effectiveRs, StringComparison.OrdinalIgnoreCase))
            {
                CrashReporter.Log($"[DllOverrideService.{caller}] Skipping DC rename — '{newFileName}' collides with RS name for '{card.GameName}'.");
                return;
            }
        }

        var deployPath = ModInstallService.GetAddonDeployPath(card.InstallPath);

        // Find the DC file — check addon deploy path first, then game install path
        string? oldPath = null;
        if (File.Exists(Path.Combine(deployPath, oldFileName)))
            oldPath = Path.Combine(deployPath, oldFileName);
        else if (File.Exists(Path.Combine(card.InstallPath, oldFileName)))
            oldPath = Path.Combine(card.InstallPath, oldFileName);

        if (oldPath == null) return;

        // Deploy new file to the addon deploy path
        var newPath = Path.Combine(deployPath, newFileName);

        try
        {
            if (!oldPath.Equals(newPath, StringComparison.OrdinalIgnoreCase))
            {
                // Guard: do not delete the target file if it belongs to RS
                if (File.Exists(newPath))
                {
                    var rsInstalledAs = card.RsRecord?.InstalledAs ?? card.RsInstalledFile;
                    if (!string.IsNullOrEmpty(rsInstalledAs)
                        && newFileName.Equals(rsInstalledAs, StringComparison.OrdinalIgnoreCase))
                    {
                        CrashReporter.Log($"[DllOverrideService.{caller}] Skipping DC rename — target '{newFileName}' is the RS file for '{card.GameName}'.");
                        return;
                    }
                    File.Delete(newPath);
                }
                File.Move(oldPath, newPath);

                // Update the AuxInstalledRecord
                var dcRecord = _auxInstaller.FindRecord(card.GameName, card.InstallPath, "DisplayCommander");
                if (dcRecord != null)
                {
                    dcRecord.InstalledAs = newFileName;
                    _auxInstaller.SaveAuxRecord(dcRecord);
                }
                card.DcInstalledFile = newFileName;
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DllOverrideService.{caller}] Failed to rename DC file for '{card.GameName}' — {ex.Message}");
        }
    }

    /// <summary>
    /// Checks if the target DC DLL override filename conflicts with an existing
    /// non-DC file in the game folder. Returns true if no conflict or user confirms overwrite.
    /// Returns false if user cancels — caller should revert the dropdown.
    /// </summary>
    public async Task<bool> CheckDcForeignDllConflictAsync(GameCardViewModel card, string newDcFileName)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return true;

        var deployPath = ModInstallService.GetAddonDeployPath(card.InstallPath);
        var targetPath = Path.Combine(deployPath, newDcFileName);

        // Also check the game install path directly
        var targetPathDirect = Path.Combine(card.InstallPath, newDcFileName);

        // No conflict if the file doesn't exist at either location
        if (!File.Exists(targetPath) && !File.Exists(targetPathDirect)) return true;

        // No conflict if the existing file IS the current DC file
        if (!string.IsNullOrEmpty(card.DcInstalledFile)
            && newDcFileName.Equals(card.DcInstalledFile, StringComparison.OrdinalIgnoreCase))
            return true;

        // Foreign file detected — ask user for confirmation
        var conflictPath = File.Exists(targetPath) ? targetPath : targetPathDirect;
        if (ConfirmForeignDcOverwrite != null)
            return await ConfirmForeignDcOverwrite(card, conflictPath);

        // No callback registered — allow by default
        return true;
    }

    // ── Name collision helpers ─────────────────────────────────────────────

    /// <summary>
    /// Returns the effective RS filename for a game — from override config if
    /// active, otherwise the default <see cref="AuxInstallService.RsNormalName"/>.
    /// </summary>
    public string GetEffectiveRsName(string gameName)
    {
        var cfg = GetDllOverride(gameName);
        return cfg != null && !string.IsNullOrWhiteSpace(cfg.ReShadeFileName)
            ? cfg.ReShadeFileName
            : AuxInstallService.RsNormalName;
    }

    /// <summary>
    /// Returns the effective DC filename for a game — from override config if
    /// active, otherwise the default addon name based on bitness.
    /// </summary>
    public string GetEffectiveDcName(string gameName, bool is32Bit)
    {
        var cfg = GetDllOverride(gameName);
        return cfg != null && !string.IsNullOrWhiteSpace(cfg.DcFileName)
            ? cfg.DcFileName
            : MainViewModel.GetDcFileName(is32Bit);
    }

    /// <summary>
    /// Checks whether <paramref name="targetName"/> is already in use by the
    /// <em>other</em> component (RS or DC) in the same game folder.
    /// <para>
    /// <paramref name="component"/> is the component that <em>wants</em> to use
    /// the name — <c>"RS"</c> or <c>"DC"</c>. The method checks the opposite
    /// component's effective name (from override config) and also scans the
    /// physical files on disk.
    /// </para>
    /// Comparison is case-insensitive (Windows filenames).
    /// </summary>
    public bool IsNameOccupiedByOtherComponent(
        string gameName,
        string targetName,
        string component,
        bool is32Bit,
        string? installPath)
    {
        if (string.IsNullOrWhiteSpace(targetName))
            return false;

        // Check the other component's effective name from config / defaults
        string otherEffectiveName;
        if (component.Equals("RS", StringComparison.OrdinalIgnoreCase))
            otherEffectiveName = GetEffectiveDcName(gameName, is32Bit);
        else
            otherEffectiveName = GetEffectiveRsName(gameName);

        if (targetName.Equals(otherEffectiveName, StringComparison.OrdinalIgnoreCase))
            return true;

        // Check physical files on disk when an install path is available
        if (!string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
        {
            if (component.Equals("RS", StringComparison.OrdinalIgnoreCase))
            {
                // RS wants this name — check if DC physically occupies it
                var deployPath = ModInstallService.GetAddonDeployPath(installPath);
                if (File.Exists(Path.Combine(deployPath, targetName)))
                    return true;
                // Also check the game install path for DC files that may live there
                if (!deployPath.Equals(installPath, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(Path.Combine(installPath, targetName))
                    && !targetName.Equals(GetEffectiveRsName(gameName), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else
            {
                // DC wants this name — check if RS physically occupies it
                var rsPath = Path.Combine(installPath, targetName);
                if (File.Exists(rsPath)
                    && targetName.Equals(GetEffectiveRsName(gameName), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    // ── OptiScaler DLL naming ─────────────────────────────────────────────

    /// <summary>
    /// Returns the effective OptiScaler DLL filename for a game.
    /// Priority: user override > manifest override > default (dxgi.dll).
    /// </summary>
    public string GetEffectiveOsName(string gameName)
    {
        var cfg = GetDllOverride(gameName);
        if (cfg != null && !string.IsNullOrWhiteSpace(cfg.OsFileName))
            return cfg.OsFileName;

        if (_manifestOsDllOverrides.TryGetValue(gameName, out var manifestName)
            && !string.IsNullOrWhiteSpace(manifestName))
            return manifestName;

        return OptiScalerService.DefaultDllName;
    }

    /// <summary>
    /// Returns the supported OptiScaler DLL names filtered to exclude
    /// names actively in use by ReShade or Display Commander for the same game.
    /// Only excludes a name when that component is physically installed
    /// (non-null <paramref name="rsInstalledAs"/> / <paramref name="dcInstalledAs"/>).
    /// When a component is not installed, its default name is NOT excluded so the
    /// user can freely pick any name for OptiScaler.
    /// </summary>
    public string[] GetAvailableOsDllNames(string gameName, bool is32Bit,
        string? rsInstalledAs = null, string? dcInstalledAs = null)
    {
        return OptiScalerService.SupportedDllNames
            .Where(n => (string.IsNullOrEmpty(rsInstalledAs) || !n.Equals(rsInstalledAs, StringComparison.OrdinalIgnoreCase))
                     && (string.IsNullOrEmpty(dcInstalledAs)  || !n.Equals(dcInstalledAs,  StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    /// <summary>
    /// Sets or updates the OptiScaler DLL filename override for the specified game.
    /// </summary>
    public void SetOsDllOverride(string gameName, string osFileName)
    {
        var existing = GetDllOverride(gameName);
        if (existing != null)
        {
            existing.OsFileName = osFileName.Trim();
            // If all fields are now empty, remove the ghost entry entirely
            if (string.IsNullOrEmpty(existing.ReShadeFileName)
                && string.IsNullOrEmpty(existing.DcFileName)
                && string.IsNullOrEmpty(existing.OsFileName))
            {
                _dllOverrides.Remove(gameName);
            }
        }
        else if (!string.IsNullOrEmpty(osFileName))
        {
            // Only create a new entry if there's actually something to store
            _dllOverrides[gameName] = new DllOverrideConfig
            {
                OsFileName = osFileName.Trim(),
            };
        }
        OverridesChanged?.Invoke();
    }

    /// <summary>
    /// Loads OptiScaler DLL overrides from the remote manifest.
    /// Called during manifest application.
    /// </summary>
    public void LoadManifestOsDllOverrides(Dictionary<string, string>? overrides)
    {
        _manifestOsDllOverrides.Clear();
        if (overrides == null) return;
        foreach (var (key, value) in overrides)
            _manifestOsDllOverrides[key] = value;
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
