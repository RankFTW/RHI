namespace RenoDXCommander.Models;

public class LumaInstalledRecord
{
    public string GameName { get; set; } = "";
    public string InstallPath { get; set; } = "";
    /// <summary>Store/platform where this game is installed (Steam, Xbox, Epic, etc.).</summary>
    public string Store { get; set; } = "";
    public string? DownloadUrl { get; set; }
    public List<string> InstalledFiles { get; set; } = new();
    public DateTime InstalledAt { get; set; }
    /// <summary>
    /// Luma-Framework release build number at the time of install (e.g. 428).
    /// Used to detect when a newer release is available.
    /// </summary>
    public int InstalledBuildNumber { get; set; }
    /// <summary>Nexus Mods file_id installed, for update detection. Null for non-Nexus installs.</summary>
    public int? NexusFileId { get; set; }
}
