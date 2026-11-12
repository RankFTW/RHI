// MainViewModel.Hsf.cs — HSF (Hunter's Sloppy Frames): DLSS Frame Generation for DX9 / DX11 / DX12
// games through a ReShade add-on. Install / remove / LaunchPad choice from the Extras row, and keeping
// RHI installs up to date at launch and with the 4-hour refresh (nothing is downloaded unless a game uses it).

using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    /// <summary>Why HSF can't go into this game (a short button label and the full reason), or null when it can.</summary>
    public static (string Short, string Long)? HsfBlock(GameCardViewModel card)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return ("No install folder", "This game has no install folder.");
        if (card.RequiresVulkanInstall || card.GraphicsApi is GraphicsApiType.Vulkan or GraphicsApiType.OpenGL)
            return ("DirectX 9, 11 and 12 only", "HSF supports DirectX 9, 11 and 12 games only.");
        if (card.IsDxvkInstalled) return ("Remove DXVK first", "DXVK is installed in this game. HSF needs the game's own DirectX; remove DXVK first.");
        if (card.IsDxvkBlacklisted) return ("Anti-cheat game", "This game is on RHI's anti-cheat list. Never use HSF in a game with online anti-cheat.");
        if (!card.IsRsInstalled) return ("Install ReShade first", "Install ReShade first: HSF is a ReShade add-on.");
        if (card.UseNormalReShade) return ("Needs ReShade with addons", "HSF needs ReShade with add-on support. Change this game's ReShade channel from No Addons.");
        return null;
    }

    /// <summary>Why HSF can't go into this game, or null when it can.</summary>
    public static string? HsfBlockReason(GameCardViewModel card) => HsfBlock(card)?.Long;

    /// <summary>HSF needs ReShade 6.8 (6.5 for DirectX 9). A note when the installed one is older, else null.</summary>
    public static string? HsfReShadeVersionNote(GameCardViewModel card)
    {
        var text = card.RsInstalledVersion;
        if (string.IsNullOrEmpty(text) || !Version.TryParse(new string(text.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray()), out var v))
            return null;
        if (v >= new Version(6, 8)) return null;
        if (card.GraphicsApi == GraphicsApiType.DirectX9 && v >= new Version(6, 5)) return null;
        return $"ReShade {text} is installed here. HSF is tested with ReShade 6.8 (with add-on support); update ReShade if frame generation doesn't start.";
    }

    private static bool HsfIsDx12(GameCardViewModel card) =>
        card.GraphicsApi == GraphicsApiType.DirectX12 || card.DetectedApis.Contains(GraphicsApiType.DirectX12);

    /// <summary>The D3D12 route RHI writes up front (none until the DLSS scan has run for this game).</summary>
    private static HsfService.Route HsfRouteFor(GameCardViewModel card) =>
        HsfService.PickRoute(HsfIsDx12(card), card.Is32Bit,
            card.DlssDetection == null ? null : card.HasStreamline, card.StreamlineInstalledVersion);

    /// <summary>
    /// Downloads (or updates) HSF and installs it into the game: add-on, HSF_streamline, HSF.ini with
    /// the D3D12 route set, then HSF.fx and LaunchPad through the shader sync. Returns an error, or null.
    /// </summary>
    public async Task<string?> InstallHsfAsync(GameCardViewModel card, bool launchpadOn)
    {
        if (HsfBlockReason(card) is { } reason) return reason;
        var installPath = card.InstallPath!;
        var svc = App.Services.GetRequiredService<HsfService>();
        try
        {
            var error = await Task.Run(async () =>
            {
                if (!await svc.EnsureStagingAsync().ConfigureAwait(false))
                    return svc.LastError ?? "Couldn't download HSF. Try again later.";
                if (launchpadOn && !UpliftService.LaunchpadStaged)
                    await _shaderPackService.EnsurePacksAsync(new[] { UpliftService.LaunchpadPackId }).ConfigureAwait(false);
                svc.Install(installPath, card.Is32Bit, launchpadOn);
                svc.PresetRoute(installPath, HsfRouteFor(card));
                return null;
            });
            if (error != null) return error;
            await DeployShadersForCardAsync(card.GameName);
            _crashReporter.Log($"[MainViewModel.InstallHsf] HSF {svc.StagedVersion} installed for '{card.GameName}' (LaunchPad {(launchpadOn ? "on" : "off")}, route {HsfRouteFor(card)})");
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _crashReporter.Log($"[MainViewModel.InstallHsf] '{card.GameName}' — {ex.Message}");
            return ex is UnauthorizedAccessException
                ? "Couldn't write into the game folder (access denied). Close the game, or try Admin Mode."
                : $"Couldn't install HSF: {ex.Message}";
        }
    }

    /// <summary>
    /// True when HSF's frame generation runs in the game's own process (64-bit DirectX 12), so Universal
    /// MFG Unlocker goes in as a proxy DLL next to the game. Otherwise HSF's helper runs it, and the mod
    /// goes in HSF_streamline\mods, where the helper loads it.
    /// </summary>
    public static bool HsfRunsInGame(GameCardViewModel card) => HsfIsDx12(card) && !card.Is32Bit;

    /// <summary>
    /// Installs Universal MFG Unlocker for HSF (RTX 20 / 30 / 40 cards): as <paramref name="proxyName"/> next
    /// to the game, or in HSF_streamline\mods when it's null. No RTXMFG-XeSS folder (HSF's frame generation
    /// is DLSS). Call after HSF is installed. Returns an error, or null.
    /// </summary>
    public async Task<string?> InstallMfgForHsfAsync(GameCardViewModel card, string? proxyName)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return "This game has no install folder.";
        if (card.MfgAdaInstalled) return "MFG Unlock is installed, which conflicts with Universal MFG Unlocker. Remove it from the addon picker first.";
        var installPath = card.InstallPath;
        try
        {
            var (ok, target) = await Task.Run(async () =>
            {
                var t = proxyName ?? Rtx40MfgService.HsfModsRelPath(installPath);
                if (!_rtx40MfgService.IsStagingReady || _rtx40MfgService.HasUpdate)
                    await _rtx40MfgService.EnsureStagingAsync().ConfigureAwait(false);
                return (_rtx40MfgService.Install(installPath, t), t);
            });
            if (!ok) return "Couldn't download Universal MFG Unlocker. Try again, or install it from its own row.";
            RecordMfgLocation(card, target);
            _crashReporter.Log($"[MainViewModel.InstallMfgForHsf] '{card.GameName}' as {target}");
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Net.Http.HttpRequestException)
        {
            _crashReporter.Log($"[MainViewModel.InstallMfgForHsf] '{card.GameName}' — {ex.Message}");
            return $"Couldn't install Universal MFG Unlocker: {ex.Message}";
        }
    }

    /// <summary>
    /// Removes HSF from the game, then syncs its shaders so HSF.fx (and LaunchPad, if only HSF needed it) leave too.
    /// Universal MFG Unlocker in HSF_streamline\mods goes with it; a proxy copy next to the game stays (its own row removes it).
    /// </summary>
    public async Task UninstallHsfAsync(GameCardViewModel card)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return;
        var installPath = card.InstallPath;
        var store = card.Source ?? "";
        var svc = App.Services.GetRequiredService<HsfService>();
        var mfg = GetRtx40MfgInstalledAs(card.GameName, store);
        bool mfgInMods = Rtx40MfgService.IsHsfModsInstall(mfg);
        await Task.Run(() =>
        {
            if (mfgInMods) _rtx40MfgService.Uninstall(installPath, mfg);
            svc.Uninstall(installPath);
        });
        if (mfgInMods)
        {
            SetRtx40MfgInstalledAs(card.GameName, null, store);
            card.SetMfgState(mfgAdaInstalled: card.MfgAdaInstalled, mfgAdaRtx40Conflict: false, rtx40MfgInstalled: false);
        }
        await DeployShadersForCardAsync(card.GameName);
    }

    /// <summary>Switches LaunchPad (HSF's optical flow) on or off for the game and syncs its shaders. False if it couldn't.</summary>
    public async Task<bool> SetHsfLaunchpadAsync(GameCardViewModel card, bool on)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return false;
        var installPath = card.InstallPath;
        var svc = App.Services.GetRequiredService<HsfService>();
        bool ok = await Task.Run(async () =>
        {
            if (on && !UpliftService.LaunchpadStaged)
                await _shaderPackService.EnsurePacksAsync(new[] { UpliftService.LaunchpadPackId }).ConfigureAwait(false);
            return svc.SetLaunchpad(installPath, on);
        });
        if (ok) await DeployShadersForCardAsync(card.GameName);
        return ok;
    }

    /// <summary>
    /// Updates HSF in the games RHI installed it in (newest release, pre-releases included). Shaders
    /// follow through the shader sync hook. A game that's running keeps its files until the next check.
    /// </summary>
    private async Task RefreshHsfInstallsAsync(string caller)
    {
        try
        {
            var cards = _allCards
                .Where(c => !string.IsNullOrEmpty(c.InstallPath) && HsfService.IsRhiInstalled(c.InstallPath!))
                .GroupBy(c => c.InstallPath!, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
            if (cards.Count == 0) return;

            var svc = App.Services.GetRequiredService<HsfService>();
            await svc.CheckForUpdateAsync().ConfigureAwait(false);
            if (!svc.IsStagingReady || svc.HasUpdate)
                await svc.EnsureStagingAsync().ConfigureAwait(false);
            if (!svc.IsStagingReady) return;

            int updated = 0;
            foreach (var c in cards)
            {
                if (!svc.UpdateInstalled(c.InstallPath!, c.Is32Bit)) continue;
                updated++;
                await DeployShadersForCardAsync(c.GameName).ConfigureAwait(false); // a new HSF.fx
            }
            _crashReporter.Log($"[{caller}] HSF {svc.StagedVersion}: {cards.Count} game(s), {updated} updated");
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[{caller}] HSF refresh failed — {ex.Message}");
        }
    }
}
