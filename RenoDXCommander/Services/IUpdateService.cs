namespace RenoDXCommander.Services;

/// <summary>
/// Checks GitHub Releases for a newer version of RHI and downloads the installer.
/// </summary>
public interface IUpdateService
{
    Version CurrentVersion { get; }

    Task<UpdateInfo?> CheckForUpdateAsync(bool betaOptIn = false);

    /// <summary>Fetches the latest stable release regardless of whether it is newer than the current build. Used by "Back to stable".</summary>
    Task<UpdateInfo?> FetchStableReleaseAsync();

    Task<string?> DownloadInstallerAsync(
        string downloadUrl,
        IProgress<(string msg, double pct)>? progress = null,
        CancellationToken cancellationToken = default);

    void LaunchInstallerAndExit(string installerPath, Action closeApp);
}
