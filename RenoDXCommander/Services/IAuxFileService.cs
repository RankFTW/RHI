using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Exposes the static file-identification and INI-management methods of
/// <see cref="AuxInstallService"/> as instance methods for dependency injection.
/// </summary>
public interface IAuxFileService
{
    bool EnsureReShadeStaging();
    AuxInstallService.DxgiFileType IdentifyDxgiFile(string filePath);
    bool BackupForeignDll(string dllPath);
    void RestoreForeignDll(string dllPath);
    bool IsReShadeFileStrict(string filePath);
    bool IsReShadeFile(string filePath);
    void EnsureInisDir();
    void MergeRsIni(string gameDir, string? screenshotSavePath = null, string? overlayHotkey = null, string? screenshotHotkey = null);
    void MergeRsVulkanIni(string gameDir, string? gameName = null, string? screenshotSavePath = null, string? overlayHotkey = null, string? screenshotHotkey = null);
    void CopyRsIni(string gameDir);
    void CopyRsPresetIniIfPresent(string gameDir);

    string? ReadInstalledVersion(string installPath, string fileName);
    bool CheckReShadeUpdateLocal(AuxInstalledRecord record);
}
