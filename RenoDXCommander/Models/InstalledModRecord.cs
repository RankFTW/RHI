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
}
