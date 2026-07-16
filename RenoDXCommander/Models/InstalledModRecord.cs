namespace RenoDXCommander.Models;

public class InstalledModRecord
{
    public string GameName { get; set; } = "";
    public string InstallPath { get; set; } = "";
    public string AddonFileName { get; set; } = "";
    public string? FileHash { get; set; }
    public DateTime InstalledAt { get; set; }
    public string? SnapshotUrl { get; set; }
    /// <summary>Last-Modified header from the snapshot at time of last check.</summary>
    public DateTime? SnapshotLastModified { get; set; }
    /// <summary>
    /// Content-Length of the remote snapshot recorded at install time.
    /// CheckForUpdateAsync compares current remote Content-Length against this — stable
    /// across relaunches regardless of local file copy or filesystem behaviour.
    /// </summary>
    public long? RemoteFileSize { get; set; }

    /// <summary>
    /// Whether Engine.ini HDR settings are enabled for this game.
    /// Only relevant for UE-Extended games. Default true (deploy on install/update).
    /// Set to false when user disables via the RenoDX cog dialog.
    /// </summary>
    public bool EngineIniHdr { get; set; } = true;

    /// <summary>
    /// Whether Engine.ini r.LUT.UpdateEveryFrame=1 is enabled for this game.
    /// Applies to all Unreal Engine games with RenoDX. Default true.
    /// Set to false when user disables via the RenoDX cog dialog.
    /// </summary>
    public bool EngineIniLut { get; set; } = true;
}
