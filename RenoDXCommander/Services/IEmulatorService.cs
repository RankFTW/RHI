using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Manages emulator sub-game detection, addon swapping at launch, and per-game storage.
/// </summary>
public interface IEmulatorService
{
    /// <summary>
    /// Scans Ryubing's AppData for games that have been played at least once.
    /// Returns metadata for each detected game.
    /// </summary>
    List<RyubingGameMetadata> ScanRyubingGames();

    /// <summary>
    /// Resolves the ROM file path for a given title ID by scanning the configured game_dirs.
    /// Returns null if no matching ROM is found.
    /// </summary>
    string? ResolveRomPath(string titleId);

    /// <summary>
    /// Swaps the active addon and reshade.ini for the target game before launch.
    /// Saves the current active game's reshade.ini back to its subfolder first.
    /// </summary>
    void SwapActiveGame(string emulatorPath, string titleId);

    /// <summary>
    /// Installs an addon to the per-game subfolder (_rhi_emulator/{titleId}/).
    /// </summary>
    Task InstallAddonAsync(string emulatorPath, string titleId, string addonUrl, string addonFileName);

    /// <summary>
    /// Removes the per-game subfolder and cleans up the root if this was the active game.
    /// </summary>
    void UninstallAddon(string emulatorPath, string titleId);

    /// <summary>
    /// Returns true if the given exe path is a Ryubing/Ryujinx emulator.
    /// </summary>
    bool IsRyubingExe(string exePath);

    /// <summary>
    /// Gets the currently active game's title ID from _rhi_emulator/active.txt.
    /// Returns null if no game is active.
    /// </summary>
    string? GetActiveGame(string emulatorPath);
}
