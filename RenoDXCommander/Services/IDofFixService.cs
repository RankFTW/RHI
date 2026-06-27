namespace RenoDXCommander.Services;

/// <summary>
/// Interface for the UE DOF Fix component service.
/// Handles staging, install, uninstall, and update detection for the
/// Universal UE DOF Fix addon targeting UE 5.0–5.6 games.
/// </summary>
public interface IDofFixService
{
    /// <summary>The addon filename deployed to game folders.</summary>
    static abstract string FileName { get; }

    /// <summary>The currently staged version (from version.txt), or null if not staged.</summary>
    string? StagedVersion { get; }

    /// <summary>Whether the staging directory has the addon ready for deployment.</summary>
    bool IsStagingReady { get; }

    /// <summary>Whether an update is available (set after CheckForUpdateAsync).</summary>
    bool HasUpdate { get; }

    /// <summary>The latest remote version tag (set after CheckForUpdateAsync).</summary>
    string? LatestVersion { get; }

    /// <summary>The release body/notes from the latest version.</summary>
    string? ReleaseNotes { get; }

    /// <summary>Optional manifest URL override.</summary>
    string? ManifestUrlOverride { get; set; }

    /// <summary>
    /// Ensures the addon is staged (downloaded). Downloads if not present or if an update is available.
    /// </summary>
    Task EnsureStagingAsync(IProgress<(string message, double percent)>? progress = null);

    /// <summary>
    /// Checks GitHub for a newer version than what's currently staged.
    /// </summary>
    Task CheckForUpdateAsync();

    /// <summary>
    /// Installs the DOF Fix addon to a game folder.
    /// </summary>
    Task<bool> InstallAsync(string installPath, IProgress<(string message, double percent)>? progress = null);

    /// <summary>
    /// Uninstalls the DOF Fix addon from a game folder.
    /// </summary>
    bool Uninstall(string installPath);

    /// <summary>
    /// Detects if the DOF Fix is installed in a game folder.
    /// </summary>
    bool IsInstalledIn(string installPath);

    /// <summary>
    /// Returns the release page URL for a given version tag.
    /// </summary>
    string GetReleaseUrl(string version);

    /// <summary>
    /// Determines if a game is eligible for DOF Fix based on engine hint.
    /// Must be Unreal Engine 5.0–5.6, 64-bit, and not in skip list.
    /// </summary>
    bool IsGameEligible(string? engineHint, bool is32Bit, string? gameName);
}
