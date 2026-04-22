using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Installs and manages ReShade for each game.
/// </summary>
public interface IAuxInstallService
{
    Task<AuxInstalledRecord> InstallReShadeAsync(
        string gameName,
        string installPath,
        string? shaderModeOverride = null,
        bool use32Bit = false,
        string? filenameOverride = null,
        IEnumerable<string>? selectedPackIds = null,
        IProgress<(string message, double percent)>? progress = null,
        string? screenshotSavePath = null,
        bool useNormalReShade = false,
        string? overlayHotkey = null,
        string? screenshotHotkey = null);

    Task<bool> CheckForUpdateAsync(AuxInstalledRecord record);

    void Uninstall(AuxInstalledRecord record);

    /// <summary>
    /// Removes only the DLL file and DB record for the given install, without
    /// triggering shader folder operations (RestoreOriginalIfPresent).
    /// </summary>
    void UninstallDllOnly(AuxInstalledRecord record);

    List<AuxInstalledRecord> LoadAll();

    AuxInstalledRecord? FindRecord(string gameName, string installPath, string addonType);

    void SaveAuxRecord(AuxInstalledRecord record);

    void RemoveRecord(AuxInstalledRecord record);
}
