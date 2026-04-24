namespace RenoDXCommander.Models;

public class LumaInstalledRecord
{
    public string GameName { get; set; } = "";
    public string InstallPath { get; set; } = "";
    public string? DownloadUrl { get; set; }
    public List<string> InstalledFiles { get; set; } = new();
    public DateTime InstalledAt { get; set; }
    /// <summary>
    /// Luma-Framework release build number at the time of install (e.g. 428).
    /// Used to detect when a newer release is available.
    /// </summary>
    public int InstalledBuildNumber { get; set; }
}
