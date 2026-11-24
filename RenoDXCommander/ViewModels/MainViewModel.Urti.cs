// MainViewModel.Urti.cs — URTI (ray-traced reflections for DirectX 12 games, a ReShade add-on): what blocks it in a
// game, install / remove from the Extras row, and keeping RHI installs up to date at launch and with the 4-hour
// refresh (nothing is downloaded unless a game uses it).

using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    /// <summary>Why URTI can't go into this game (a short button label and the full reason), or null when it can.</summary>
    public (string Short, string Long)? UrtiBlock(GameCardViewModel card)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return ("No install folder", "This game has no install folder.");
        if (card.Is32Bit) return ("64-bit games only", "URTI is a 64-bit add-on: it works in 64-bit DirectX 12 games only.");
        if (card.IsDxvkInstalled || card.RequiresVulkanInstall)
            return ("DirectX 12 games only", "URTI works in DirectX 12 games only (not Vulkan or DXVK).");
        if (!UrtiIsDx12(card)) return ("DirectX 12 games only", "URTI works in DirectX 12 games only. If this game runs DirectX 12, set it in the game's settings ⚙ › Graphics API.");
        if (card.IsDxvkBlacklisted) return ("Anti-cheat game", "This game is on RHI's anti-cheat list. URTI hooks the game's rendering: never use it online.");
        if (GpuInfoService.Cached is { Count: > 0 } gpus && GpuInfoService.Primary(gpus) is { Family: GpuFamily.NotRtx } gpu)
            return ("Needs ray tracing GPU", $"URTI needs a graphics card with DirectX 12 ray tracing (DXR 1.1): NVIDIA RTX 20 or newer, or AMD RX 6000 or newer. This PC has {gpu.Name}.");
        if (!card.IsRsInstalled) return ("Install ReShade first", "Install ReShade first: URTI is a ReShade add-on.");
        if (card.UseNormalReShade) return ("Needs ReShade with addons", "URTI needs ReShade with add-on support. Change this game's ReShade channel from No Addons.");
        return null;
    }

    /// <summary>
    /// The game runs DirectX 12 as far as RHI knows: the API set in its Graphics API override when there is one,
    /// else DirectX 12 among the detected APIs.
    /// </summary>
    private bool UrtiIsDx12(GameCardViewModel card)
    {
        if (GetApiOverride(card.GameName, card.Source ?? "") is { Count: > 0 } apis)
            return apis.Contains(nameof(GraphicsApiType.DirectX12), StringComparer.OrdinalIgnoreCase);
        return card.GraphicsApi == GraphicsApiType.DirectX12 || card.DetectedApis.Contains(GraphicsApiType.DirectX12);
    }

    /// <summary>URTI needs ReShade 6.8. A note when the installed one is older, else null.</summary>
    public static string? UrtiReShadeVersionNote(GameCardViewModel card)
    {
        var text = card.RsInstalledVersion;
        if (string.IsNullOrEmpty(text) || !Version.TryParse(new string(text.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray()), out var v))
            return null;
        return v >= new Version(6, 8) ? null
            : $"ReShade {text} is installed here. URTI needs ReShade 6.8 or newer (with add-on support): update ReShade if it doesn't start.";
    }

    /// <summary>Downloads (or updates) URTI and installs it into the game, then its effects through the shader sync. Returns an error, or null.</summary>
    public async Task<string?> InstallUrtiAsync(GameCardViewModel card)
    {
        if (UrtiBlock(card) is { } block) return block.Long;
        var installPath = card.InstallPath!;
        var svc = App.Services.GetRequiredService<UrtiService>();
        try
        {
            var error = await Task.Run(async () =>
            {
                if (!await svc.EnsureStagingAsync().ConfigureAwait(false))
                    return svc.LastError ?? "Couldn't download URTI. Try again later.";
                svc.Install(installPath);
                // URTI gets the game's depth through Generic Depth (RHI's reshade.ini disables it by default)
                AuxInstallService.WriteShippedAddonsSetting(installPath, enabled: true, [AuxInstallService.GenericDepth]);
                // The "URTI" effect is the switch that draws the reflections: tick it in the game's preset (the game is closed)
                App.Services.GetRequiredService<UpliftService>().EnableTechnique(installPath, UrtiService.TechniqueToken);
                return null;
            });
            if (error != null) return error;
            await DeployShadersForCardAsync(card.GameName);
            _crashReporter.Log($"[MainViewModel.InstallUrti] URTI {svc.StagedVersion} installed for '{card.GameName}'");
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _crashReporter.Log($"[MainViewModel.InstallUrti] '{card.GameName}' — {ex.Message}");
            return ex is UnauthorizedAccessException
                ? "Couldn't write into the game folder (access denied). Close the game, or try Admin Mode."
                : $"Couldn't install URTI: {ex.Message}";
        }
    }

    /// <summary>Removes URTI from the game, then syncs its shaders so its effects leave too.</summary>
    public async Task UninstallUrtiAsync(GameCardViewModel card)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return;
        var installPath = card.InstallPath;
        var svc = App.Services.GetRequiredService<UrtiService>();
        await Task.Run(() =>
        {
            svc.Uninstall(installPath);
            AuxInstallService.RestoreGenericDepthDefault(installPath); // RHI's default (off) again
            App.Services.GetRequiredService<UpliftService>().DisableTechnique(installPath, UrtiService.TechniqueToken);
        });
        await DeployShadersForCardAsync(card.GameName);
    }

    /// <summary>
    /// Updates URTI in the games RHI installed it in (newest release); the effects follow through the shader sync.
    /// A game that's running keeps its files until the next check.
    /// </summary>
    private async Task RefreshUrtiInstallsAsync(string caller)
    {
        try
        {
            var cards = _allCards
                .Where(c => !string.IsNullOrEmpty(c.InstallPath) && UrtiService.IsRhiInstalled(c.InstallPath!))
                .GroupBy(c => c.InstallPath!, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
            if (cards.Count == 0) return;

            var svc = App.Services.GetRequiredService<UrtiService>();
            await svc.CheckForUpdateAsync().ConfigureAwait(false);
            if (!svc.IsStagingReady || svc.HasUpdate)
                await svc.EnsureStagingAsync().ConfigureAwait(false);
            if (!svc.IsStagingReady) return;

            int updated = 0;
            foreach (var c in cards)
            {
                // Installs from before RHI kept Generic Depth on for URTI
                AuxInstallService.ApplyShippedAddonsSettingToGame(c.InstallPath!);
                if (svc.UpdateInstalled(c.InstallPath!)) updated++;
                await DeployShadersForCardAsync(c.GameName).ConfigureAwait(false); // new effects (a no-op when current)
            }
            _crashReporter.Log($"[{caller}] URTI {svc.StagedVersion}: {cards.Count} game(s), {updated} updated");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                      or HttpRequestException or TaskCanceledException)
        {
            _crashReporter.Log($"[{caller}] URTI refresh failed — {ex.Message}");
        }
    }
}
