namespace RenoDXCommander.Services;

/// <summary>
/// Checks GitHub Releases for a newer version of RDXC and downloads the installer.
/// </summary>
public interface IUpdateService
{
    Version CurrentVersion { get; }

    Task<UpdateInfo?> CheckForUpdateAsync(bool betaOptIn = false);

    Task<string?> DownloadInstallerAsync(
        string downloadUrl,
        IProgress<(string msg, double pct)>? progress = null);

    void LaunchInstallerAndExit(string installerPath, Action closeApp);
}
