// MainViewModel.AnrFg.cs — installs ANR FG (Apex Neural Rendering frame generation) for the
// OptiScaler preset that asks for it (installAnrFg in optiscaler_presets.json).

using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    /// <summary>
    /// Makes sure ANR FG is in the game's OptiScaler custom folder, downloading or updating it first.
    /// Needs RHI's OptiScaler Nightly (the first build with custom frame generation providers).
    /// Returns an error message, or null when it's in place.
    /// </summary>
    public async Task<string?> EnsureAnrFgForPresetAsync(GameCardViewModel card)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return "This game has no install folder.";
        if (!card.IsOsInstalled) return "Install OptiScaler (Nightly) first.";

        var installPath = card.InstallPath;
        var record = _auxInstaller.FindRecord(card.GameName, installPath, OptiScalerService.AddonType);
        var variant = record?.OsVariant ?? OsVariants.Stable;
        if (!variant.Equals(OsVariants.Nightly, StringComparison.OrdinalIgnoreCase))
        {
            _crashReporter.Log($"[MainViewModel.EnsureAnrFgForPreset] '{card.GameName}' has OptiScaler {variant}; ANR FG needs Nightly");
            return "ANR FG needs OptiScaler Nightly. Set Version to Nightly in this cog, then apply the preset again.";
        }

        var anr = App.Services.GetRequiredService<AnrFgService>();
        try
        {
            var error = await Task.Run(async () =>
            {
                if (!await anr.EnsureStagingAsync().ConfigureAwait(false))
                    return "Couldn't download ANR FG. Try again later.";
                return anr.Install(installPath);
            });
            if (error == null)
                _crashReporter.Log($"[MainViewModel.EnsureAnrFgForPreset] ANR FG {anr.StagedVersion} in place for '{card.GameName}'");
            return error;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Net.Http.HttpRequestException)
        {
            _crashReporter.Log($"[MainViewModel.EnsureAnrFgForPreset] '{card.GameName}' — {ex.Message}");
            return $"Couldn't install ANR FG: {ex.Message}";
        }
    }
}
