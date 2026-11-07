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
        string? gameName = null,
        string? store = null);

    Task<bool> CheckForUpdateAsync(InstalledModRecord record);

    void Uninstall(InstalledModRecord record);

    List<InstalledModRecord> LoadAll();

    InstalledModRecord? FindRecord(string gameName, string? installPath = null);

    void SaveRecordPublic(InstalledModRecord record);

    void RemoveRecord(InstalledModRecord record);

    /// <summary>
    /// Install folder migration: points every record of <paramref name="gameName"/> at
    /// <paramref name="oldPath"/> to <paramref name="newPath"/>. A record already at the new path
    /// wins (the old one is dropped). Returns how many records changed.
    /// </summary>
    int RepointRecords(string gameName, string oldPath, string newPath);
}
