using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Installs and manages RE Framework (dinput8.dll) for RE Engine games.
/// </summary>
public interface IREFrameworkService
{
    /// <summary>
    /// Downloads MHWILDS.zip from the latest nightly release, extracts dinput8.dll,
    /// caches it, and copies it to the game's install path.
    /// </summary>
    Task<REFrameworkInstalledRecord> InstallAsync(
        string gameName, string installPath,
        IProgress<(string message, double percent)>? progress = null,
        string? store = null);

    /// <summary>Deletes dinput8.dll from the game directory and removes the install record.</summary>
    void Uninstall(string gameName, string installPath);

    /// <summary>Checks GitHub nightly releases for a newer version than the installed one.</summary>
    Task<bool> CheckForUpdateAsync(string installedVersion);
    void SyncInstalledVersion(string version);

    /// <summary>Returns the latest release tag from the nightly API (cached per session).</summary>
    Task<string?> GetLatestVersionAsync();

    /// <summary>Loads all persisted install records from disk.</summary>
    List<REFrameworkInstalledRecord> GetRecords();

    /// <summary>
    /// Downloads and installs the pd-upscaler branch of REFramework for OptiScaler compatibility.
    /// The pd-upscaler builds are double-zipped from nightly.link.
    /// </summary>
    Task InstallPdUpscalerAsync(string gameName, string installPath, string artifactName,
        IProgress<(string message, double percent)>? progress = null);

    /// <summary>
    /// Restores the standard REFramework dinput8.dll from backup after OptiScaler uninstall.
    /// </summary>
    void RestoreStandardREFramework(string gameName, string installPath);
}
