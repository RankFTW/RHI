// MainViewModel.MfgPlacement.cs — keeps Universal MFG Unlocker in the right place:
// a proxy DLL next to the exe normally, or MFGUnlocker.asi in OptiScaler\plugins when the game
// has an OptiScaler 10 build (nightly, F5, DLSS NR) installed by RHI. Stable 0.9.x is left alone.

using RenoDXCommander.Services;
using Directory = System.IO.Directory;
using File = System.IO.File;
using Path = System.IO.Path;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    /// <summary>Hooks OptiScaler install/uninstall so the mod moves with it. Called once at startup.</summary>
    private void WireMfgPluginHooks()
    {
        OptiScalerService.BeforeV10DeployHook               = card => MoveMfgIntoOptiScaler(card);
        OptiScalerService.V10InstallFailedHook              = card => MoveMfgOutOfOptiScaler(card);
        OptiScalerService.BeforeOptiScalerFolderRemovedHook = card => MoveMfgOutOfOptiScaler(card);
    }

    /// <summary>
    /// True when the mod should be installed as an OptiScaler plugin for this game: RHI installed
    /// OptiScaler here and it is an OptiScaler 10 build (it has the OptiScaler\ subfolder).
    /// </summary>
    public bool IsOptiScalerPluginHost(GameCardViewModel card)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return false;
        if (!Directory.Exists(Path.Combine(card.InstallPath, "OptiScaler"))) return false;
        return _auxInstaller.FindRecord(card.GameName, card.InstallPath, OptiScalerService.AddonType) != null;
    }

    /// <summary>Moves a proxy-DLL install of the mod into OptiScaler\plugins. No-op when not installed or already there.</summary>
    internal void MoveMfgIntoOptiScaler(GameCardViewModel card)
    {
        var store   = card.Source ?? "";
        var current = GetRtx40MfgInstalledAs(card.GameName, store);
        if (string.IsNullOrEmpty(current) || Rtx40MfgService.IsPluginInstall(current)) return;

        var moved = _rtx40MfgService.MoveToOptiScalerPlugin(card.InstallPath, current);
        if (moved != null) RecordMfgLocation(card, moved);
    }

    /// <summary>Moves the mod out of OptiScaler\plugins back to a proxy DLL. No-op when it isn't a plugin install.</summary>
    internal void MoveMfgOutOfOptiScaler(GameCardViewModel card)
    {
        var store   = card.Source ?? "";
        var current = GetRtx40MfgInstalledAs(card.GameName, store);
        if (!Rtx40MfgService.IsPluginInstall(current)) return;

        var moved = _rtx40MfgService.MoveOutOfOptiScalerPlugin(card.InstallPath);
        if (moved != null) RecordMfgLocation(card, moved);
    }

    /// <summary>
    /// Makes sure Universal MFG Unlocker is installed for an OptiScaler preset that needs it (UMFG XeFG).
    /// With an OptiScaler 10 build it goes in OptiScaler\plugins (a proxy install is moved there); otherwise
    /// it goes in as a proxy under the first free name. Returns an error message, or null when it's in place.
    /// </summary>
    public async Task<string?> EnsureUniversalMfgForPresetAsync(GameCardViewModel card)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return "This game has no install folder.";
        if (card.MfgAdaInstalled) return "MFG Ada Unlock is installed, which conflicts with Universal MFG Unlocker. Remove it from the addon picker first.";

        var installPath = card.InstallPath;
        var store       = card.Source ?? "";
        var current     = GetRtx40MfgInstalledAs(card.GameName, store);
        bool installed  = !string.IsNullOrEmpty(current) && _rtx40MfgService.IsInstalledIn(installPath, current);
        bool asPlugin   = await Task.Run(() => IsOptiScalerPluginHost(card));

        try
        {
            if (installed)
            {
                if (asPlugin && !Rtx40MfgService.IsPluginInstall(current)) MoveMfgIntoOptiScaler(card);
                return null;
            }

            // Proxy names, in order of preference; one already in the folder belongs to something else.
            string target = asPlugin
                ? Rtx40MfgService.PluginRelPath
                : new[] { "version.dll", "winmm.dll", "dinput8.dll" }
                    .FirstOrDefault(n => !File.Exists(Path.Combine(installPath, n))) ?? "";
            if (target.Length == 0)
                return "Couldn't install Universal MFG Unlocker: version.dll, winmm.dll and dinput8.dll are all taken in this game. Install it from Extras and pick a name.";

            bool ok = await Task.Run(async () =>
            {
                if (!_rtx40MfgService.IsStagingReady || _rtx40MfgService.HasUpdate)
                    await _rtx40MfgService.EnsureStagingAsync().ConfigureAwait(false);
                if (!asPlugin)
                    await _rtx40MfgService.EnsureXessStagingAsync().ConfigureAwait(false);
                return _rtx40MfgService.Install(installPath, target);
            });
            if (!ok) return "Couldn't download Universal MFG Unlocker. Try again, or install it from Extras.";

            RecordMfgLocation(card, target);
            _crashReporter.Log($"[MainViewModel.EnsureUniversalMfgForPreset] Installed for '{card.GameName}' as {target}");
            return null;
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Net.Http.HttpRequestException)
        {
            _crashReporter.Log($"[MainViewModel.EnsureUniversalMfgForPreset] '{card.GameName}' — {ex.Message}");
            return $"Couldn't install Universal MFG Unlocker: {ex.Message}";
        }
    }

    /// <summary>
    /// Saves the mod's new location. Done straight away on the UI thread so a following step in
    /// the same handler sees it; queued to the UI thread otherwise (settings are UI-thread state).
    /// </summary>
    private void RecordMfgLocation(GameCardViewModel card, string installedAs)
    {
        void Apply()
        {
            SetRtx40MfgInstalledAs(card.GameName, installedAs, card.Source ?? "");
            card.SetMfgState(mfgAdaInstalled: card.MfgAdaInstalled, mfgAdaRtx40Conflict: card.MfgAdaInstalled, rtx40MfgInstalled: true);
        }
        if (DispatcherQueue == null || DispatcherQueue.HasThreadAccess) Apply();
        else DispatcherQueue.TryEnqueue(Apply);
    }

    /// <summary>
    /// Startup pass: games that have both OptiScaler 10 and the mod as a proxy DLL get the mod
    /// moved into OptiScaler\plugins; plugin installs whose OptiScaler is gone (or is now 0.9)
    /// move back out. Hidden games are left alone.
    /// </summary>
    private void ReconcileMfgPlacementForAllGames()
    {
        foreach (var card in _allCards.ToList())
        {
            if (card.IsHidden || string.IsNullOrEmpty(card.InstallPath)) continue;
            var current = GetRtx40MfgInstalledAs(card.GameName, card.Source ?? "");
            if (string.IsNullOrEmpty(current)) continue;
            try
            {
                bool host = IsOptiScalerPluginHost(card);
                if (host && !Rtx40MfgService.IsPluginInstall(current)) MoveMfgIntoOptiScaler(card);
                else if (!host && Rtx40MfgService.IsPluginInstall(current)) MoveMfgOutOfOptiScaler(card);
            }
            catch (Exception ex)
            {
                _crashReporter.Log($"[MainViewModel.ReconcileMfgPlacement] '{card.GameName}' — {ex.Message}");
            }
        }
    }
}
