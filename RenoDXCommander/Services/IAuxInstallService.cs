using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Installs and manages Display Commander and ReShade for each game.
/// </summary>
public interface IAuxInstallService
{
    Task<AuxInstalledRecord> InstallDcAsync(
        string gameName,
        string installPath,
        int dcModeLevel,
        AuxInstalledRecord? existingDcRecord = null,
        AuxInstalledRecord? existingRsRecord = null,
        string? shaderModeOverride = null,
        bool use32Bit = false,
        string? filenameOverride = null,
        IProgress<(string message, double percent)>? progress = null);

    Task<AuxInstalledRecord> InstallReShadeAsync(
        string gameName,
        string installPath,
        bool dcMode,
        bool dcIsInstalled = false,
        string? shaderModeOverride = null,
        bool use32Bit = false,
        string? filenameOverride = null,
        IProgress<(string message, double percent)>? progress = null);

    Task<bool> CheckForUpdateAsync(AuxInstalledRecord record);

    void Uninstall(AuxInstalledRecord record);

    List<AuxInstalledRecord> LoadAll();

    AuxInstalledRecord? FindRecord(string gameName, string installPath, string addonType);

    void SaveAuxRecord(AuxInstalledRecord record);

    void RemoveRecord(AuxInstalledRecord record);
}
