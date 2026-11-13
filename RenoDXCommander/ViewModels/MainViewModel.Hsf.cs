// MainViewModel.Hsf.cs — HSF (Hunter's Sloppy Frames): DLSS Frame Generation for DX9 / DX11 / DX12 / Vulkan
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
        if (card.GraphicsApi == GraphicsApiType.OpenGL && !card.RequiresVulkanInstall)
            return ("Not for OpenGL", "HSF supports DirectX 9, 11, 12 and Vulkan games, not OpenGL.");
        bool vulkan = HsfIsVulkan(card);
        if (vulkan && card.Is32Bit)
            return ("64-bit Vulkan only", "HSF supports 64-bit Vulkan games only.");
        if (card.IsDxvkInstalled) return ("Remove DXVK first", "DXVK is installed in this game. HSF needs the game's own DirectX; remove DXVK first.");
        if (card.IsDxvkBlacklisted) return ("Anti-cheat game", "This game is on RHI's anti-cheat list. Never use HSF in a game with online anti-cheat.");
        if (!card.IsRsInstalled) return ("Install ReShade first", "Install ReShade first: HSF is a ReShade add-on.");
        if (card.UseNormalReShade) return ("Needs ReShade with addons", "HSF needs ReShade with add-on support. Change this game's ReShade channel from No Addons.");
        if (vulkan && File.Exists(Path.Combine(card.InstallPath, "dxgi.dll"))
            && AuxInstallService.IsReShadeFile(Path.Combine(card.InstallPath, "dxgi.dll")))
            return ("Remove ReShade's dxgi.dll", "A DirectX copy of ReShade (dxgi.dll) is in this game's folder. In a Vulkan game it keeps ReShade's Vulkan layer out, and HSF with it: remove that dxgi.dll first.");
        return null;
    }

    /// <summary>
    /// The game runs through ReShade's Vulkan layer (RHI installs ReShade for Vulkan here: a Vulkan game, or a dual
    /// game set to its Vulkan path). DXVK is left out: HSF is blocked there.
    /// </summary>
    public static bool HsfIsVulkan(GameCardViewModel card) =>
        card.RequiresVulkanInstall && !card.DxvkEnabled;

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

    /// <summary>
    /// The game runs DirectX 12 as far as RHI knows: the API set in its Rendering API override when there is one
    /// (a game forced to DirectX 11 isn't), else DirectX 12 among the detected APIs. A dual DirectX 11 / 12 game
    /// counts: HSF takes the route back by itself after a DirectX 11 run, while a DirectX 12 run then starts
    /// generating with no restart.
    /// </summary>
    private bool HsfIsDx12(GameCardViewModel card)
    {
        if (HsfIsVulkan(card)) return false;
        if (GetApiOverride(card.GameName, card.Source ?? "") is { Count: > 0 } apis)
            return apis.Contains(nameof(GraphicsApiType.DirectX12), StringComparer.OrdinalIgnoreCase);
        return card.GraphicsApi == GraphicsApiType.DirectX12 || card.DetectedApis.Contains(GraphicsApiType.DirectX12);
    }

    /// <summary>
    /// The D3D12 route for this game when it runs DirectX 12 (<paramref name="forceDx12"/>: the ⚙ dialog says it
    /// does). Reads the DLSS scan, or looks for the game's Streamline itself when the scan hasn't run, so the
    /// route is ready before the first launch. Touches the disk: call off the UI thread.
    /// </summary>
    public HsfService.Route HsfRouteFor(GameCardViewModel card, bool forceDx12 = false)
    {
        bool? hasSl = card.DlssDetection == null ? null : card.HasStreamline;
        if (hasSl == null && !string.IsNullOrEmpty(card.InstallPath))
            hasSl = HsfService.FindGameStreamline(card.InstallPath);
        return HsfService.PickRoute(forceDx12 || HsfIsDx12(card), card.Is32Bit, hasSl, card.StreamlineInstalledVersion);
    }

    /// <summary>
    /// Downloads (or updates) HSF and installs it into the game: add-on, HSF_streamline, HSF.ini with
    /// the D3D12 route set, then HSF.fx and LaunchPad through the shader sync. Returns an error, or null.
    /// </summary>
    public async Task<string?> InstallHsfAsync(GameCardViewModel card, bool launchpadOn)
    {
        if (HsfBlockReason(card) is { } reason) return reason;
        var installPath = card.InstallPath!;
        var svc = App.Services.GetRequiredService<HsfService>();
        var route = HsfService.Route.None;
        try
        {
            var error = await Task.Run(async () =>
            {
                if (!await svc.EnsureStagingAsync().ConfigureAwait(false))
                    return svc.LastError ?? "Couldn't download HSF. Try again later.";
                if (launchpadOn && !UpliftService.LaunchpadStaged)
                    await _shaderPackService.EnsurePacksAsync(new[] { UpliftService.LaunchpadPackId }).ConfigureAwait(false);
                svc.Install(installPath, card.Is32Bit, launchpadOn);
                route = HsfRouteFor(card);
                svc.PresetRoute(installPath, route);
                return null;
            });
            if (error != null) return error;
            await DeployShadersForCardAsync(card.GameName);
            _crashReporter.Log($"[MainViewModel.InstallHsf] HSF {svc.StagedVersion} installed for '{card.GameName}' (LaunchPad {(launchpadOn ? "on" : "off")}, route {route})");
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
    /// Where Universal MFG Unlocker goes with HSF (HSF's installer guide): next to the exe of a 64-bit game, any
    /// API (in DirectX 12 the game loads it; in DirectX 9 / 11 / Vulkan HSF's helper finds it there by its
    /// product name). 32-bit games: in HSF_streamline\mods, never next to the 32-bit exe.
    /// </summary>
    public static bool HsfMfgNextToExe(GameCardViewModel card) => !card.Is32Bit;

    /// <summary>Sets the game's D3D12 route now (⚙ dialog): on = ready for the first DirectX 12 launch, off = HSF decides.</summary>
    public async Task<bool> SetHsfRouteAsync(GameCardViewModel card, bool on)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return false;
        var svc = App.Services.GetRequiredService<HsfService>();
        return await Task.Run(() =>
        {
            var route = on ? HsfRouteFor(card, forceDx12: true) : HsfService.Route.None;
            if (on && route == HsfService.Route.None) return false; // 32-bit, or Streamline 1.x: HSF has no D3D12 route
            return svc.SetRoute(card.InstallPath!, route);
        });
    }

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
    /// Updates HSF in the games RHI installed it in (newest release). Shaders
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
                // A DirectX 12 game whose route isn't set yet (the DLSS scan hadn't run at install, or HSF took it
                // back after a DirectX 11 run) gets it now, so its next launch generates without a restart
                svc.PresetRoute(c.InstallPath!, HsfRouteFor(c));
                // HSF switches LaunchPad on itself and ReShade lists it last, after HSF_Motion, which reads its flow:
                // put it first (only the order; the game must be closed, ReShade rewrites the preset on exit)
                UpliftService.KeepLaunchpadFirst(c.InstallPath!);
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
