// MainViewModel.RtxEncoreIntel.cs -- per-game choice for RTX Encore's Intel XeSS files: replace the game's own
// copies with XeSS SDK 3.0.2 (what RTX Encore Hub does, and what its XeSS / XeFG need), or keep them as they are.
// Keeping matters for games whose launcher checks file hashes and asks for a repair (Wuthering Waves, issue #184).
// Default: keep for games in the manifest's encoreKeepGameIntelFiles, replace for the rest.

using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    /// <summary>True when the manifest says this game's launcher needs its own Intel files left alone.</summary>
    public bool IsRtxEncoreKeepGameIntelDefault(string gameName)
        => _manifest?.EncoreKeepGameIntelFiles?.Contains(gameName, StringComparer.OrdinalIgnoreCase) == true;

    /// <summary>The user's choice: true = keep the game's files, false = replace them, null = default.</summary>
    public bool? GetRtxEncoreKeepGameIntelChoice(string gameName, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        if (_gameNameService.RtxEncoreKeepGameIntel.TryGetValue(key, out var v)) return v;
        if (_gameNameService.RtxEncoreKeepGameIntel.TryGetValue(gameName, out var v2)) return v2;
        return null;
    }

    /// <summary>Whether RTX Encore installs keep the game's own Intel XeSS / XeLL DLLs.</summary>
    public bool GetRtxEncoreKeepGameIntel(string gameName, string store = "")
        => GetRtxEncoreKeepGameIntelChoice(gameName, store) ?? IsRtxEncoreKeepGameIntelDefault(gameName);

    /// <summary>Saves the choice (null = back to the default). Doesn't touch any files: see <see cref="ApplyRtxEncoreIntelFilesAsync"/>.</summary>
    public void SetRtxEncoreKeepGameIntel(string gameName, bool? keep, string store = "")
    {
        var key = GameKey.From(gameName, store).ToKey();
        _gameNameService.RtxEncoreKeepGameIntel.Remove(gameName);
        if (keep.HasValue) _gameNameService.RtxEncoreKeepGameIntel[key] = keep.Value;
        else _gameNameService.RtxEncoreKeepGameIntel.Remove(key);
        CrashReporter.Log($"[MainViewModel.SetRtxEncoreKeepGameIntel] '{key}' → {(keep.HasValue ? (keep.Value ? "keep the game's files" : "replace them") : "default")}");
        SaveNameMappings();
    }

    /// <summary>
    /// Brings an installed RTX Encore's Intel files in line with the game's choice (puts the game's own copies
    /// back, or replaces them). No-op when RTX Encore isn't installed. Returns false when it couldn't be done.
    /// </summary>
    public async Task<bool> ApplyRtxEncoreIntelFilesAsync(GameCardViewModel card)
    {
        var installPath = card.InstallPath;
        if (string.IsNullOrEmpty(installPath)) return true;
        var store = card.Source ?? "";
        var installedAs = GetRtxEncoreInstalledAs(card.GameName, store);
        var svc = App.Services.GetRequiredService<RtxEncoreService>();
        if (string.IsNullOrEmpty(installedAs) || !svc.IsInstalledIn(installPath, installedAs)) return true;
        var modDir = svc.ModDir(installPath, installedAs);
        bool keep = GetRtxEncoreKeepGameIntel(card.GameName, store);
        if (!await _rtx40MfgService.EnsureEncoreIntelStagingAsync().ConfigureAwait(false)) return false;
        return await Task.Run(() => _rtx40MfgService.DeployEncoreIntelFiles(installPath, modDir, keep)).ConfigureAwait(false);
    }
}
