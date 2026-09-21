using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Fetches the Luma Framework wiki, parses mods, and handles install/uninstall.
/// </summary>
public interface ILumaService
{
    Task<List<LumaMod>> FetchCompletedModsAsync(IProgress<string>? progress = null);

    /// <summary>Fetches LumaMod stubs from GitHub release assets (fills gaps not in the wiki).</summary>
    Task<List<LumaMod>> FetchReleasesModsAsync();

    /// <summary>
    /// Fetches and parses the Luma Framework generic Unreal Engine wiki table.
    /// Returns per-game entries with notes, Engine.ini keys, HDR flag, UE version,
    /// and whether -dx11 launch arg is required.
    /// </summary>
    Task<Dictionary<string, LumaGenericGameEntry>> FetchGenericUeTableAsync(IProgress<string>? progress = null);

    Task<LumaInstalledRecord> InstallAsync(
        LumaMod mod,
        string gameInstallPath,
        IEnumerable<string>? selectedShaderPacks = null,
        string? screenshotSavePath = null,
        string? overlayHotkey = null,
        string? screenshotHotkey = null,
        string? gameName = null,
        IProgress<(string message, double percent)>? progress = null,
        string? store = null);

    void Uninstall(LumaInstalledRecord record);

    /// <summary>
    /// Installs a Luma mod from a local archive (zip or 7z) to the game folder.
    /// </summary>
    /// <param name="folderPicker">Optional callback invoked when the archive contains multiple
    /// candidate game folders. Receives the folder names and should return the user's choice,
    /// or null to cancel.</param>
    Task<LumaInstalledRecord> InstallFromArchiveAsync(
        string archivePath,
        string gameInstallPath,
        bool is32Bit,
        IEnumerable<string>? selectedShaderPacks = null,
        string? screenshotSavePath = null,
        string? overlayHotkey = null,
        string? screenshotHotkey = null,
        string? gameName = null,
        Func<List<string>, Task<string?>>? folderPicker = null,
        string? store = null);

    void SaveLumaRecord(LumaInstalledRecord record);

    void RemoveLumaRecord(string gameName, string installPath);

    /// <summary>
    /// Fetches the latest Luma-Framework release build number from GitHub.
    /// Returns 0 if the fetch fails.
    /// </summary>
    Task<int> GetLatestBuildNumberAsync();

    /// <summary>
    /// Checks whether a newer Luma-Framework release is available compared to
    /// the build number stored in the installed record.
    /// </summary>
    Task<bool> CheckForUpdateAsync(LumaInstalledRecord record);
}
