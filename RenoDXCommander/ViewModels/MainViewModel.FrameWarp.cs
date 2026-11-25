// MainViewModel.FrameWarp.cs — FrameWarp / XPAR (asynchronous reprojection, a ReShade add-on): what blocks it in a game,
// what to warn about before installing (frame limiters, the NVIDIA frame rate limit, GPU scheduling, the VC++ runtime),
// install / remove from the Experimental row, NVIDIA Latewarp on / off, and keeping RHI installs up to date at launch and
// with the 4-hour refresh (nothing is downloaded unless a game uses it).

using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    /// <summary>Why FrameWarp can't go into this game (a short button label and the full reason), or null when it can.</summary>
    public (string Short, string Long)? FrameWarpBlock(GameCardViewModel card)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return ("No install folder", "This game has no install folder.");
        if (card.Is32Bit) return ("64-bit games only", "FrameWarp is a 64-bit add-on: it works in 64-bit games only.");
        bool vulkan = card.RequiresVulkanInstall || card.GraphicsApi == GraphicsApiType.Vulkan;
        if (!vulkan && card.GraphicsApi is GraphicsApiType.DirectX8 or GraphicsApiType.DirectX9 or GraphicsApiType.DirectX10 or GraphicsApiType.OpenGL)
            return ("DX11 / DX12 / Vulkan only", "FrameWarp works in DirectX 12, DirectX 11 and Vulkan games (not DirectX 9, DirectX 10 or OpenGL).");
        if (card.IsDxvkBlacklisted) return ("Anti-cheat game", "This game is on RHI's anti-cheat list. FrameWarp hooks the game's rendering: never use it online.");
        if (!card.IsRsInstalled) return ("Install ReShade first", "Install ReShade first: FrameWarp is a ReShade add-on.");
        if (card.UseNormalReShade) return ("Needs ReShade with addons", "FrameWarp needs ReShade with add-on support. Change this game's ReShade channel from No Addons.");
        return null;
    }

    /// <summary>The NVIDIA frame rate limit this game gets (its own value, else the global one); 0 = off or not readable.</summary>
    public uint FrameWarpDriverFpsLimit(GameCardViewModel card)
    {
        if (string.IsNullOrEmpty(card.InstallPath) || !_dlssPresetService.IsSupported) return 0;
        try { return _dlssPresetService.GetPerGameFpsLimit(card.GameName, card.InstallPath) ?? _dlssPresetService.GetGlobalFpsLimit(); }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            _crashReporter.Log($"[MainViewModel.FrameWarpDriverFpsLimit] '{card.GameName}' — {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// What to warn about before FrameWarp goes into this game (it still installs): another frame limiter, the NVIDIA frame
    /// rate limit, GPU scheduling off, no VC++ runtime. Touches NVAPI: call off the UI thread.
    /// </summary>
    public (List<string> Lines, uint DriverLimit) FrameWarpWarnings(GameCardViewModel card)
    {
        var lines = new List<string>();
        if (card.IsUlInstalled)
            lines.Add("ReLimiter is installed in this game. FrameWarp paces the game itself and asks for frame rate limiters to be off: remove ReLimiter, or set its frame rate cap to Off, if reprojection doesn't reach your refresh rate.");
        if (card.IsDcInstalled)
            lines.Add("Display Commander is installed in this game. FrameWarp paces the game itself and asks for frame rate limiters to be off: turn off Display Commander's FPS limit if reprojection doesn't reach your refresh rate.");
        uint limit = FrameWarpDriverFpsLimit(card);
        if (limit > 0)
            lines.Add($"The NVIDIA driver limits this game to {limit} fps. FrameWarp asks for frame rate limiters to be off.");
        if (FrameWarpService.IsHagsOn() == false)
            lines.Add("Hardware-accelerated GPU scheduling is off. FrameWarp needs it to reach your refresh rate: turn it on in Windows Settings > System > Display > Graphics, then restart.");
        if (!FrameWarpService.HasVcRuntime())
            lines.Add("The Microsoft Visual C++ 2015-2022 runtime (x64) isn't installed. FrameWarp's presenter needs it: install it from aka.ms/vs/17/release/vc_redist.x64.exe.");
        return (lines, limit);
    }

    /// <summary>Turns the NVIDIA frame rate limit off for this game (its own profile; the global value is left alone).</summary>
    public bool TurnOffDriverFpsLimitForGame(GameCardViewModel card)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return false;
        try
        {
            bool ok = _dlssPresetService.SetPerGameFpsLimit(card.GameName, card.InstallPath, 0);
            _crashReporter.Log($"[MainViewModel.TurnOffDriverFpsLimitForGame] '{card.GameName}': {(ok ? "off" : "not changed")}");
            if (ok) DriverProfileChanged?.Invoke(card);
            return ok;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            _crashReporter.Log($"[MainViewModel.TurnOffDriverFpsLimitForGame] '{card.GameName}' — {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Downloads (or updates) FrameWarp and installs it into the game, keeps Generic Depth on where it needs it, then
    /// XPAR.addonfx through the shader sync. Returns an error, or null.
    /// </summary>
    public async Task<string?> InstallFrameWarpAsync(GameCardViewModel card)
    {
        if (FrameWarpBlock(card) is { } block) return block.Long;
        var installPath = card.InstallPath!;
        var svc = App.Services.GetRequiredService<FrameWarpService>();
        try
        {
            var error = await Task.Run(async () =>
            {
                if (!await svc.EnsureStagingAsync().ConfigureAwait(false))
                    return svc.LastError ?? "Couldn't download FrameWarp. Try again later.";
                svc.Install(installPath);
                // Generic Depth: depth for games without DLSS or FSR (not in RDR2: see App.xaml.cs)
                AuxInstallService.ApplyShippedAddonsSettingToGame(installPath);
                return null;
            });
            if (error != null) return error;
            // Unreal Engine games with DLSS through Streamline need FrameWarp's launch options: added to RHI's launch
            // arguments (used when the game starts from RHI), and taken out again with FrameWarp
            bool knownSl = card.HasStreamline;
            if (await Task.Run(() => FrameWarpService.NeedsUeLaunchOptions(installPath, knownSl)))
                AddFrameWarpLaunchOptions(card);
            await DeployShadersForCardAsync(card.GameName);
            _crashReporter.Log($"[MainViewModel.InstallFrameWarp] FrameWarp {svc.StagedVersion} installed for '{card.GameName}'");
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _crashReporter.Log($"[MainViewModel.InstallFrameWarp] '{card.GameName}' — {ex.Message}");
            return ex is UnauthorizedAccessException
                ? "Couldn't write into the game folder (access denied). Close the game, or try Admin Mode."
                : $"Couldn't install FrameWarp: {ex.Message}";
        }
    }

    /// <summary>Removes FrameWarp from the game, puts RHI's Generic Depth default back, then syncs the shaders.</summary>
    public async Task UninstallFrameWarpAsync(GameCardViewModel card)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return;
        var installPath = card.InstallPath;
        var svc = App.Services.GetRequiredService<FrameWarpService>();
        // The launch options RHI added (read before the marker goes with the FrameWarp folder)
        var addedArgs = FrameWarpService.ReadMarker(installPath).TryGetValue("launch_args", out var a) ? a : "";
        await Task.Run(() =>
        {
            svc.Uninstall(installPath);
            AuxInstallService.RestoreGenericDepthDefault(installPath);
        });
        if (addedArgs.Length > 0)
        {
            var current = GetLaunchArgsOverride(card.GameName) ?? "";
            var left = FrameWarpService.RemoveLaunchOptionsFrom(current, addedArgs);
            if (left != current.Trim()) SetLaunchArgsOverride(card.GameName, left.Length > 0 ? left : null);
            _crashReporter.Log($"[MainViewModel.UninstallFrameWarp] '{card.GameName}': launch options '{addedArgs}' taken out");
        }
        await DeployShadersForCardAsync(card.GameName);
    }

    /// <summary>Turns NVIDIA Latewarp on (downloads the hash-checked DLL once) or off for the game. Returns an error, or null.</summary>
    public async Task<string?> SetFrameWarpLatewarpAsync(GameCardViewModel card, bool on)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return "This game has no install folder.";
        var installPath = card.InstallPath;
        var svc = App.Services.GetRequiredService<FrameWarpService>();
        try
        {
            return await Task.Run(async () =>
            {
                if (!on) { svc.RemoveLatewarp(installPath); return null; }
                if (await svc.EnsureLatewarpStagedAsync().ConfigureAwait(false) is { } err) return err;
                svc.InstallLatewarp(installPath);
                return (string?)null;
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _crashReporter.Log($"[MainViewModel.SetFrameWarpLatewarp] '{card.GameName}' — {ex.Message}");
            return ex is UnauthorizedAccessException
                ? "Couldn't write into the game folder (access denied). Close the game, or try Admin Mode."
                : $"Couldn't change NVIDIA Latewarp: {ex.Message} (close the game first)";
        }
    }

    /// <summary>
    /// Adds FrameWarp's Unreal Engine launch options to RHI's launch arguments for this game (only missing ones) and
    /// remembers in FrameWarp's marker which RHI added, so removing FrameWarp takes out just those. Returns the arguments.
    /// </summary>
    public string AddFrameWarpLaunchOptions(GameCardViewModel card)
    {
        var current = GetLaunchArgsOverride(card.GameName) ?? "";
        var (updated, added) = FrameWarpService.AddLaunchOptionsTo(current);
        if (added.Length == 0) return updated;
        SetLaunchArgsOverride(card.GameName, updated);
        if (!string.IsNullOrEmpty(card.InstallPath))
        {
            var before = FrameWarpService.ReadMarker(card.InstallPath).TryGetValue("launch_args", out var b) ? b : "";
            FrameWarpService.SetMarkerValue(card.InstallPath, "launch_args", string.Join(" ", new[] { before, added }.Where(s => s.Length > 0)));
        }
        _crashReporter.Log($"[MainViewModel.AddFrameWarpLaunchOptions] '{card.GameName}': added '{added}' → '{updated}'");
        return updated;
    }

    /// <summary>
    /// Updates FrameWarp in the games RHI installed it in (newest release); XPAR.addonfx follows through the shader sync.
    /// A game that's running keeps its files until the next check.
    /// </summary>
    private async Task RefreshFrameWarpInstallsAsync(string caller)
    {
        try
        {
            var cards = _allCards
                .Where(c => !string.IsNullOrEmpty(c.InstallPath) && FrameWarpService.IsRhiInstalled(c.InstallPath!))
                .GroupBy(c => c.InstallPath!, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
            if (cards.Count == 0) return;

            var svc = App.Services.GetRequiredService<FrameWarpService>();
            await svc.CheckForUpdateAsync().ConfigureAwait(false);
            if (!svc.IsStagingReady || svc.HasUpdate)
                await svc.EnsureStagingAsync().ConfigureAwait(false);
            if (!svc.IsStagingReady) return;

            int updated = 0;
            foreach (var c in cards)
            {
                if (c.IsRunning) continue; // its presenter and add-on are in use
                AuxInstallService.ApplyShippedAddonsSettingToGame(c.InstallPath!);
                if (svc.UpdateInstalled(c.InstallPath!)) updated++;
                await DeployShadersForCardAsync(c.GameName).ConfigureAwait(false); // a new XPAR.addonfx (no-op when current)
            }
            _crashReporter.Log($"[{caller}] FrameWarp {svc.StagedVersion}: {cards.Count} game(s), {updated} updated");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                      or HttpRequestException or TaskCanceledException)
        {
            _crashReporter.Log($"[{caller}] FrameWarp refresh failed — {ex.Message}");
        }
    }
}
