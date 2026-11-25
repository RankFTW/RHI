// MainViewModel.Crcgi.cs -- CRCGI (screen-space global illumination for ReShade + its CRCGI Injector add-on): what blocks
// it in a game, install / remove from the Experimental row, and keeping RHI installs up to date at launch and with the
// 4-hour refresh (nothing is downloaded unless a game uses it). Same shape as URTI (MainViewModel.Urti.cs).

using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    /// <summary>Why CRCGI can't go into this game (a short button label and the full reason), or null when it can.</summary>
    public (string Short, string Long)? CrcgiBlock(GameCardViewModel card)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return ("No install folder", "This game has no install folder.");
        if (card.IsDxvkBlacklisted) return ("Anti-cheat game", "This game is on RHI's anti-cheat list. CRCGI's add-on hooks the game's rendering: never use it online.");
        if (CrcgiIsNativeDx9(card))
            return ("Needs DirectX 10 or newer", "CRCGI needs DirectX 10 or newer, Vulkan or OpenGL. DirectX 9 games work through DXVK (install DXVK from Extras first).");
        if (!card.IsRsInstalled) return ("Install ReShade first", "Install ReShade first: CRCGI is a ReShade effect and add-on.");
        if (card.UseNormalReShade) return ("Needs ReShade with addons", "CRCGI's add-on needs ReShade with add-on support. Change this game's ReShade channel from No Addons.");
        return null;
    }

    /// <summary>A DirectX 9 game not running through DXVK (as far as RHI knows: its Graphics API override, else the detected APIs).</summary>
    private bool CrcgiIsNativeDx9(GameCardViewModel card)
    {
        if (card.IsDxvkInstalled) return false;
        if (GetApiOverride(card.GameName, card.Source ?? "") is { Count: > 0 } apis)
            return apis.All(a => a.Equals(nameof(GraphicsApiType.DirectX9), StringComparison.OrdinalIgnoreCase)
                              || a.Equals(nameof(GraphicsApiType.DirectX8), StringComparison.OrdinalIgnoreCase));
        if (card.GraphicsApi is not (GraphicsApiType.DirectX9 or GraphicsApiType.DirectX8)) return false;
        return !card.DetectedApis.Any(a => a is GraphicsApiType.DirectX10 or GraphicsApiType.DirectX11 or GraphicsApiType.DirectX12
                                         or GraphicsApiType.Vulkan or GraphicsApiType.OpenGL);
    }

    /// <summary>Downloads (or updates) CRCGI and installs it into the game, then its effect through the shader sync. Returns an error, or null.</summary>
    public async Task<string?> InstallCrcgiAsync(GameCardViewModel card)
    {
        if (CrcgiBlock(card) is { } block) return block.Long;
        var installPath = card.InstallPath!;
        bool is32 = card.Is32Bit;
        var svc = App.Services.GetRequiredService<CrcgiService>();
        try
        {
            var error = await Task.Run(async () =>
            {
                if (!await svc.EnsureStagingAsync().ConfigureAwait(false))
                    return svc.LastError ?? "Couldn't download CRCGI. Try again later.";
                svc.Install(installPath, is32);
                // The effect reads the game's depth through Generic Depth (RHI's reshade.ini disables it by default)
                AuxInstallService.WriteShippedAddonsSetting(installPath, enabled: true, [AuxInstallService.GenericDepth]);
                // Tick the effect in the game's preset (the game is closed), so it works straight away
                if (CrcgiService.StagedTechniqueToken is { } token)
                    App.Services.GetRequiredService<UpliftService>().EnableTechnique(installPath, token);
                return null;
            });
            if (error != null) return error;
            await DeployShadersForCardAsync(card.GameName);
            _crashReporter.Log($"[MainViewModel.InstallCrcgi] CRCGI {CrcgiService.StagedVersion} installed for '{card.GameName}'");
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _crashReporter.Log($"[MainViewModel.InstallCrcgi] '{card.GameName}' — {ex.Message}");
            return ex is UnauthorizedAccessException
                ? "Couldn't write into the game folder (access denied). Close the game, or try Admin Mode."
                : $"Couldn't install CRCGI: {ex.Message}";
        }
    }

    /// <summary>Removes CRCGI from the game, then syncs its shaders so the effect leaves too.</summary>
    public async Task UninstallCrcgiAsync(GameCardViewModel card)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return;
        var installPath = card.InstallPath;
        var svc = App.Services.GetRequiredService<CrcgiService>();
        await Task.Run(() =>
        {
            var technique = CrcgiService.MarkerValue(installPath, "technique") ?? CrcgiService.StagedTechniqueToken;
            svc.Uninstall(installPath);
            AuxInstallService.RestoreGenericDepthDefault(installPath); // RHI's default (off) again, unless URTI / FrameWarp need it
            if (technique != null) App.Services.GetRequiredService<UpliftService>().DisableTechnique(installPath, technique);
        });
        await DeployShadersForCardAsync(card.GameName);
    }

    /// <summary>
    /// Updates CRCGI in the games RHI installed it in (newest release); the effect follows through the shader sync.
    /// A new major version renames the effect (CRCGI_v2 → v3): the new technique is ticked in place of the old one.
    /// A game that's running keeps its files until the next check.
    /// </summary>
    private async Task RefreshCrcgiInstallsAsync(string caller)
    {
        try
        {
            var cards = _allCards
                .Where(c => !string.IsNullOrEmpty(c.InstallPath) && !c.IsHidden && CrcgiService.IsRhiInstalled(c.InstallPath!))
                .GroupBy(c => c.InstallPath!, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
            if (cards.Count == 0) return;

            var svc = App.Services.GetRequiredService<CrcgiService>();
            await svc.CheckForUpdateAsync().ConfigureAwait(false);
            if (!CrcgiService.IsStagingReady || svc.HasUpdate)
                await svc.EnsureStagingAsync().ConfigureAwait(false);
            if (!CrcgiService.IsStagingReady) return;

            int updated = 0;
            var uplift = App.Services.GetRequiredService<UpliftService>();
            foreach (var c in cards)
            {
                if (c.IsRunning) continue;
                if (svc.UpdateInstalled(c.InstallPath!, out var oldTechnique)) updated++;
                if (oldTechnique != null && CrcgiService.StagedTechniqueToken is { } token)
                {
                    uplift.DisableTechnique(c.InstallPath!, oldTechnique);
                    uplift.EnableTechnique(c.InstallPath!, token);
                }
                await DeployShadersForCardAsync(c.GameName).ConfigureAwait(false); // the new effect (a no-op when current)
            }
            _crashReporter.Log($"[{caller}] CRCGI {CrcgiService.StagedVersion}: {cards.Count} game(s), {updated} updated");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                      or HttpRequestException or TaskCanceledException)
        {
            _crashReporter.Log($"[{caller}] CRCGI refresh failed — {ex.Message}");
        }
    }
}
