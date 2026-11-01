// MainViewModel.ReShadeAsi.cs — per-game "Load ReShade as ASI" (ReShade64.asi / ReShade32.asi).
// RHI only renames ReShade; something else (ASI Loader, a mod's plugin folder) has to load it.

using RenoDXCommander.Models;
using RenoDXCommander.Services;
using File = System.IO.File;
using IOException = System.IO.IOException;
using Path = System.IO.Path;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    /// <summary>True when ReShade is installed as an .asi for this game.</summary>
    public bool GetRsAsiMode(string gameName, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        return _gameNameService.RsAsiGames.Contains(key);
    }

    /// <summary>
    /// Turns "Load ReShade as ASI" on or off and renames an installed ReShade to match.
    /// Returns null on success, or a message saying why the file could not be renamed
    /// (the setting is then left unchanged).
    /// </summary>
    public async Task<string?> SetRsAsiModeAsync(GameCardViewModel card, bool on)
    {
        var gameName = card.GameName;
        var store    = card.Source ?? "";
        var key      = GameKey.From(gameName, store).ToKey();

        // Rename the installed ReShade first, so a failure leaves the setting as it was
        var rsRecord = _auxInstaller.FindRecord(gameName, card.InstallPath, AuxInstallService.TypeReShade)
                    ?? _auxInstaller.FindRecord(gameName, card.InstallPath, AuxInstallService.TypeReShadeNormal);
        if (rsRecord != null && !string.IsNullOrEmpty(card.InstallPath) && !card.RequiresVulkanInstall)
        {
            var target = on ? AuxInstallService.RsAsiName(card.Is32Bit) : ResolveRsDllNameForAsiOff(card);
            var error  = await Task.Run(() => RenameInstalledReShade(card.InstallPath, rsRecord, target));
            if (error != null) return error;

            card.RsRecord        = rsRecord;
            card.RsInstalledFile = rsRecord.InstalledAs;
        }

        if (on) _gameNameService.RsAsiGames.Add(key);
        else    _gameNameService.RsAsiGames.Remove(key);
        CrashReporter.Log($"[MainViewModel.SetRsAsiMode] {key} = {(on ? "On" : "Off")}");
        SaveNameMappings();
        return null;
    }

    /// <summary>
    /// ReShade's DLL name once ASI mode is turned off: the same choice a fresh install makes
    /// (user DLL override, manifest name, or the name for the game's graphics API), moved aside
    /// to ReShade64.dll when OptiScaler already uses that name.
    /// </summary>
    private string ResolveRsDllNameForAsiOff(GameCardViewModel card)
    {
        var name = (card.DllOverrideEnabled
                ? GetDllOverride(card.GameName)?.ReShadeFileName
                : (GetManifestDllNames(card.GameName)?.ReShade is { Length: > 0 } mRs
                    ? mRs
                    : ResolveAutoReShadeFilename(card.DetectedApis)))
            ?? AuxInstallService.RsNormalName;
        if (string.IsNullOrWhiteSpace(name)) name = AuxInstallService.RsNormalName;

        var osRecord = _auxInstaller.FindRecord(card.GameName, card.InstallPath, OptiScalerService.AddonType);
        if (osRecord != null && string.Equals(osRecord.InstalledAs, name, StringComparison.OrdinalIgnoreCase))
            name = OptiScalerService.ReShadeCoexistName;
        return name;
    }

    /// <summary>
    /// Renames the installed ReShade to <paramref name="target"/> and updates its record.
    /// A game file already at the target (e.g. the game's own dxgi.dll) is backed up as
    /// .original, and a backup at the old name is restored. Returns an error message or null.
    /// </summary>
    private string? RenameInstalledReShade(string installPath, AuxInstalledRecord rsRecord, string target)
    {
        var current = rsRecord.InstalledAs;
        if (string.IsNullOrEmpty(current) || current.Equals(target, StringComparison.OrdinalIgnoreCase))
            return null;

        var src  = Path.Combine(installPath, current);
        var dest = Path.Combine(installPath, target);
        if (!File.Exists(src))
        {
            // Nothing on disk to rename; the next install or update uses the new name
            CrashReporter.Log($"[MainViewModel.RenameInstalledReShade] '{current}' not found in '{installPath}' — record only");
            return null;
        }

        try
        {
            if (File.Exists(dest))
            {
                AuxInstallService.BackupForeignDll(dest); // only moves recognised game/foreign DLLs aside
                if (File.Exists(dest))
                    return $"{target} already exists in the game folder. Remove it or choose another DLL name, then try again.";
            }
            File.Move(src, dest);
            AuxInstallService.RestoreForeignDll(src);
            rsRecord.InstalledAs = target;
            _auxInstaller.SaveAuxRecord(rsRecord);
            CrashReporter.Log($"[MainViewModel.RenameInstalledReShade] '{current}' → '{target}' in '{installPath}'");
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[MainViewModel.RenameInstalledReShade] Failed '{current}' → '{target}' — {ex.Message}");
            return $"Couldn't rename ReShade: {ex.Message}";
        }
    }
}
