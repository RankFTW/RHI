namespace RenoDXCommander.Models;

public class LumaInstalledRecord
{
    public string GameName { get; set; } = "";
    public string InstallPath { get; set; } = "";
    public string? DownloadUrl { get; set; }
    public List<string> InstalledFiles { get; set; } = new();
    public DateTime InstalledAt { get; set; }
}
