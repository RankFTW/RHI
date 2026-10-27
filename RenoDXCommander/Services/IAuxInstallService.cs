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
        string? screenshotHotkey = null,
        string? channel = null,
        string? store = null,
        bool mergeIni = true);

    Task<bool> CheckForUpdateAsync(AuxInstalledRecord record);

    void Uninstall(AuxInstalledRecord record);

    List<AuxInstalledRecord> LoadAll();

    AuxInstalledRecord? FindRecord(string gameName, string installPath, string addonType);

    void SaveAuxRecord(AuxInstalledRecord record);

    void RemoveRecord(AuxInstalledRecord record);
}
