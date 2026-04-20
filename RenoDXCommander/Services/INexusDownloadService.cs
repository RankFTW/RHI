using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Services;

/// <summary>
/// Handles downloading mod files for both premium and free users.
/// </summary>
public interface INexusDownloadService
{
    /// <summary>Downloads and installs a mod update. Chooses premium or free flow automatically.</summary>
    Task DownloadAndInstallAsync(
        GameCardViewModel card, string gameDomain, int modId,
        IProgress<double>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Processes an nxm:// URI received from the protocol handler.
    /// Returns the downloaded file path on success, or null on failure.
    /// </summary>
    Task<string?> ProcessNxmUriAsync(NxmUri uri);
}
