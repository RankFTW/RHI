using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Downloads, installs, updates, and uninstalls RenoDX addon files.
/// </summary>
public interface IModInstallService
{
    event Action<InstalledModRecord>? InstallCompleted;

    Task<InstalledModRecord> InstallAsync(
        GameMod mod,
        string gameInstallPath,
        IProgress<(string message, double percent)>? progress = null,
        string? gameName = null);

    Task<bool> CheckForUpdateAsync(InstalledModRecord record);

    void Uninstall(InstalledModRecord record);

    List<InstalledModRecord> LoadAll();

    InstalledModRecord? FindRecord(string gameName, string? installPath = null);

    void SaveRecordPublic(InstalledModRecord record);

    void RemoveRecord(InstalledModRecord record);
}
