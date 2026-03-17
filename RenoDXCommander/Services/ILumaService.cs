using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Fetches the Luma Framework wiki, parses mods, and handles install/uninstall.
/// </summary>
public interface ILumaService
{
    Task<List<LumaMod>> FetchCompletedModsAsync(IProgress<string>? progress = null);

    Task<LumaInstalledRecord> InstallAsync(
        LumaMod mod,
        string gameInstallPath,
        IProgress<(string message, double percent)>? progress = null);

    void Uninstall(LumaInstalledRecord record);

    void SaveLumaRecord(LumaInstalledRecord record);

    void RemoveLumaRecord(string gameName, string installPath);
}
