// MainViewModel.ReLimiterDmfg.cs — ReLimiter v4 doesn't support NVIDIA Dynamic Multi Frame Generation (DMFG).
// Installs that would put v4 in a game whose NVIDIA profile has DMFG on warn first (InstallEventHandler), and the
// ReLimiter ⚙ › Version shows the same warning. Games with an Extras MFG mod get one shared, dismissible notice instead.

using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    /// <summary>NVIDIA profile value of the Multi Frame Generation mode setting for Dynamic (DMFG); see MfgDialog.</summary>
    private const uint MfgModeDynamic = 0x00000004;

    internal const string UlDmfgWarning =
        "Dynamic Multi Frame Generation (DMFG) is on for this game in its NVIDIA driver settings, and ReLimiter v4 doesn't support it. " +
        "Turn it off before using ReLimiter v4: DLSS / Streamline › Multi Frame Gen › FG Mode, choose Default or Fixed.";

    /// <summary>
    /// True when the game's NVIDIA profile has Multi Frame Generation set to Dynamic.
    /// Reads the driver profile through NVAPI: call it off the UI thread.
    /// </summary>
    internal bool IsDmfgOn(GameCardViewModel card)
    {
        if (!_dlssPresetService.IsSupported || string.IsNullOrEmpty(card.InstallPath)) return false;
        try
        {
            return _dlssPresetService.GetMfgMode(card.GameName, card.InstallPath) == MfgModeDynamic;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainViewModel.IsDmfgOn] Couldn't read the MFG mode for '{card.GameName}' — {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// The Extras frame generation mods installed in this game that ReLimiter v4 doesn't work as intended with
    /// (Universal MFG Unlocker, MFG Ada Unlock, 20/30 FG Unlock, DLSS Enabler), by display name. Checks files: call off the UI thread.
    /// </summary>
    internal List<string> InstalledMfgModsForUl(GameCardViewModel card)
    {
        var names = new List<string>();
        var installPath = card.InstallPath ?? "";
        if (string.IsNullOrEmpty(installPath)) return names;
        var store = card.Source ?? "";
        try
        {
            if (card.Rtx40MfgInstalled) names.Add("Universal MFG Unlocker");
            if (card.MfgAdaInstalled) names.Add("MFG Ada Unlock");
            if (_dlssg2030Service.IsInstalledIn(installPath, GetDlssg2030InstalledAs(card.GameName, store))) names.Add("20/30 FG Unlock");
            var de = App.Services.GetRequiredService<DlssEnablerService>();
            if (de.IsStandaloneInstalledIn(installPath, GetDeInstalledAs(card.GameName, store))) names.Add("DLSS Enabler");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            CrashReporter.Log($"[MainViewModel.InstalledMfgModsForUl] Check failed for '{card.GameName}' — {ex.Message}");
        }
        return names;
    }

    /// <summary>
    /// One notice shared by every MFG mod found by <see cref="InstalledMfgModsForUl"/>, shown before a ReLimiter v4 install
    /// until the user ticks "Don't show this again" (user decision, Nov 2026: replaces the per-mod warnings).
    /// </summary>
    internal const string UlMfgModsNotice =
        "There's no guarantee ReLimiter v4 will work with the frame generation mods in Extras (Universal MFG Unlocker, "
        + "MFG Ada Unlock, 20/30 FG Unlock, DLSS Enabler). Work has gone into making them compatible, and it's ongoing.";

    /// <summary>
    /// True when installing ReLimiter in this game now would put v4 there: the pinned release, else Automatic / the
    /// game's v3/v4 choice (the last v3 for games that don't get v4), else the latest build (dev test build or a newer
    /// hand-placed cache build included). Same order as <c>InstallUlCoreAsync</c>.
    /// </summary>
    internal async Task<bool> UlInstallWillBeV4Async(GameCardViewModel card)
    {
        var tag = GetUlPinnedVersion(card.GameName, card.Source ?? "") ?? await LastV3TagInsteadOfLatestAsync(card);
        if (tag != null) return UlV4Rules.MajorOf(tag) >= UlV4Rules.V4Major;

        if (!card.Is32Bit && UlDevTestVersion() is { } dev) return dev.Major >= UlV4Rules.V4Major;
        int? major = await Task.Run(() => IsUlCacheNewerThanLatest(card.Is32Bit)
            ? ReadUlAddonVersion(GetUlCachePath(card.Is32Bit))?.Major
            : LatestUlMajor ?? ReadUlAddonVersion(GetUlCachePath(card.Is32Bit))?.Major);
        return major >= UlV4Rules.V4Major;
    }
}
