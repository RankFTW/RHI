// DetailPanelBuilder.Overrides.DllShared.cs — Display Commander and OptiScaler DLL names (they were also in Game Overrides until 2.9.6.3)
// and the Display Commander / OptiScaler ⚙s (the ReShade one is in DetailPanelBuilder.Overrides.RsShared.cs).
// The three names exclude each other: a component can't take a name another one uses.

using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    private static (string[] Items, string Selected) NameChoices(IEnumerable<string> names, string? current)
    {
        const string sentinel = DllOverrideService.PlaceholderName;
        var items = names.Prepend(sentinel).ToList();
        if (string.IsNullOrEmpty(current)) return (items.ToArray(), sentinel);
        var match = items.FirstOrDefault(n => n.Equals(current, StringComparison.OrdinalIgnoreCase));
        if (match == null) { items.Add(current); match = current; }
        return (items.ToArray(), match);
    }

    // ── Display Commander ───────────────────────────────────────────────────────

    /// <summary>DC DLL name items ("--------" = default first; ReShade's and OptiScaler's names left out) and the saved name.</summary>
    internal (string[] Items, string Selected) GetDcDllNameChoices(GameCardViewModel card)
    {
        var cfg = _window.ViewModel.GetDllOverride(card.GameName);
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(cfg?.ReShadeFileName)) excluded.Add(cfg.ReShadeFileName);
        if (!string.IsNullOrEmpty(cfg?.OsFileName)) excluded.Add(cfg.OsFileName);
        return NameChoices(DcDllOverrideNames.Where(n => !excluded.Contains(n)), cfg?.DcFileName);
    }

    /// <summary>True when this name is taken by a file that isn't Display Commander's, so picking it asks first.</summary>
    internal static bool DcNamePickNeedsDialog(GameCardViewModel card, string dcName)
    {
        if (dcName == DllOverrideService.PlaceholderName || string.IsNullOrEmpty(card.InstallPath)) return false;
        if (dcName.Equals(card.DcInstalledFile, StringComparison.OrdinalIgnoreCase)) return false;
        return File.Exists(Path.Combine(ModInstallService.GetAddonDeployPath(card.InstallPath), dcName))
            || File.Exists(Path.Combine(card.InstallPath, dcName));
    }

    /// <summary>
    /// Saves a Display Commander DLL name and renames the installed DLL ("--------" = back to the default name).
    /// Asks first when another program's file has that name. False = not changed (ReShade's name, or cancelled).
    /// </summary>
    internal async Task<bool> ApplyDcDllNameAsync(GameCardViewModel card, string capturedName, string dcName)
    {
        var rsName = _window.ViewModel.GetDllOverride(capturedName)?.ReShadeFileName ?? "";

        // -------- = clear DC override, revert DLL to default name
        if (dcName == DllOverrideService.PlaceholderName)
        {
            if (!string.IsNullOrEmpty(card.DcInstalledFile))
            {
                // UpdateDllOverrideNames renames the installed file back to the default name
                _window.ViewModel.UpdateDllOverrideNames(card, rsName, MainViewModel.GetDcFileName(card.Is32Bit));
                card.NotifyAll();
            }
            _window.ViewModel.SetDllOverrideNames(card.GameName, rsName, "");
            if (!_window.ViewModel.HasDllOverride(capturedName))
                card.DllOverrideEnabled = false;
            CrashReporter.Log($"[DetailPanelBuilder] DC DLL name back to default for '{capturedName}'");
            return true;
        }

        if (!string.IsNullOrEmpty(rsName) && dcName.Equals(rsName, StringComparison.OrdinalIgnoreCase)) return false;

        // Another program's file with that name: ask first
        if (!await _dllOverrideService.CheckDcForeignDllConflictAsync(card, dcName)) return false;

        CrashReporter.Log($"[DetailPanelBuilder] DC DLL name changed → '{dcName}' for '{capturedName}'");
        _window.ViewModel.UpdateDllOverrideNames(card, rsName, dcName);
        card.NotifyAll();
        return true;
    }

    // ── OptiScaler ──────────────────────────────────────────────────────────────

    /// <summary>OptiScaler DLL name items ("--------" = default first; names ReShade or Display Commander use left out) and the saved name.</summary>
    internal (string[] Items, string Selected) GetOsDllNameChoices(GameCardViewModel card)
    {
        var cfg = _window.ViewModel.GetDllOverride(card.GameName);
        var names = _dllOverrideService.GetAvailableOsDllNames(card.GameName, card.Is32Bit,
            rsInstalledAs: !string.IsNullOrEmpty(card.RsInstalledFile) ? card.RsInstalledFile : cfg?.ReShadeFileName,
            dcInstalledAs: !string.IsNullOrEmpty(card.DcInstalledFile) ? card.DcInstalledFile : cfg?.DcFileName);
        return NameChoices(names, cfg?.OsFileName);
    }

    /// <summary>
    /// Saves an OptiScaler DLL name and renames the installed DLL ("--------" = back to the default name).
    /// False = not changed (the name is ReShade's or Display Commander's).
    /// </summary>
    internal bool ApplyOsDllName(GameCardViewModel card, string capturedName, string osName)
    {
        // -------- = clear OS override, revert DLL to default name
        if (osName == DllOverrideService.PlaceholderName)
        {
            _dllOverrideService.SetOsDllOverride(capturedName, "");
            if (!_window.ViewModel.HasDllOverride(capturedName))
                card.DllOverrideEnabled = false;
            var currentOsInstalled = card.OsInstalledFile;
            if (card.IsOsInstalled && !string.IsNullOrEmpty(currentOsInstalled) && !string.IsNullOrEmpty(card.InstallPath))
            {
                var defName = OptiScalerService.GetDefaultDllName(card.GraphicsApi);
                var oldP = Path.Combine(card.InstallPath, currentOsInstalled);
                var newP = Path.Combine(card.InstallPath, defName);
                try
                {
                    if (File.Exists(oldP) && !oldP.Equals(newP, StringComparison.OrdinalIgnoreCase) && !File.Exists(newP))
                    {
                        File.Move(oldP, newP);
                        card.OsInstalledFile = defName;
                        var rec = _auxInstallService.FindRecord(capturedName, card.InstallPath, "OptiScaler");
                        if (rec != null) { rec.InstalledAs = defName; _auxInstallService.SaveAuxRecord(rec); }
                        RhiInstallManifest.UpdateInstalledAs(card.InstallPath, currentOsInstalled, defName);
                        CrashReporter.Log($"[DetailPanelBuilder] Reverted OS DLL '{currentOsInstalled}' → '{defName}' for '{capturedName}'");
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    CrashReporter.Log($"[DetailPanelBuilder] OS revert failed — {ex.Message}");
                }
            }
            return true;
        }

        // Use the card directly (not a re-lookup by name) to avoid a multi-store card mismatch
        var cfg = _window.ViewModel.GetDllOverride(capturedName);
        var effectiveRsName = !string.IsNullOrEmpty(card.RsInstalledFile) ? card.RsInstalledFile : cfg?.ReShadeFileName;
        var effectiveDcName = !string.IsNullOrEmpty(card.DcInstalledFile) ? card.DcInstalledFile : cfg?.DcFileName;
        if ((!string.IsNullOrEmpty(effectiveRsName) && osName.Equals(effectiveRsName, StringComparison.OrdinalIgnoreCase))
         || (!string.IsNullOrEmpty(effectiveDcName) && osName.Equals(effectiveDcName, StringComparison.OrdinalIgnoreCase)))
        {
            CrashReporter.Log($"[DetailPanelBuilder] Blocking OS DLL selection '{osName}' — collides with RS/DC name for '{capturedName}'.");
            return false;
        }

        _dllOverrideService.SetOsDllOverride(capturedName, osName);
        // Mark the card as having a user DLL override so the background scan's
        // ApplyManifestDllRenames doesn't re-apply a manifest rename over this.
        card.DllOverrideEnabled = true;

        // If OptiScaler is installed, rename the DLL in the game folder
        if (card.IsOsInstalled && !string.IsNullOrEmpty(card.OsInstalledFile) && !string.IsNullOrEmpty(card.InstallPath))
        {
            var oldPath = Path.Combine(card.InstallPath, card.OsInstalledFile);
            var newPath = Path.Combine(card.InstallPath, osName);
            try
            {
                if (File.Exists(oldPath) && !oldPath.Equals(newPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(newPath)) File.Delete(newPath);
                    File.Move(oldPath, newPath);
                    CrashReporter.Log($"[DetailPanelBuilder] Renamed OptiScaler DLL '{card.OsInstalledFile}' → '{osName}' for '{capturedName}'");
                    var prevOsName = card.OsInstalledFile;
                    card.OsInstalledFile = osName;

                    var osRecord = _auxInstallService.FindRecord(capturedName, card.InstallPath, "OptiScaler");
                    if (osRecord != null)
                    {
                        osRecord.InstalledAs = osName;
                        _auxInstallService.SaveAuxRecord(osRecord);
                    }
                    // Update rhi_install.txt and rename the .original sentinel
                    RhiInstallManifest.UpdateInstalledAs(card.InstallPath, prevOsName, osName);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                CrashReporter.Log($"[DetailPanelBuilder.ApplyOsDllName] Failed to rename OS DLL for '{capturedName}' — {ex.Message}");
            }
        }
        return true;
    }
}
