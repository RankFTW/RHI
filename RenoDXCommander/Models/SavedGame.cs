namespace RenoDXCommander.Models;

public class SavedGame
{
    public string Name { get; set; } = "";
    public string InstallPath { get; set; } = "";
    public string Source { get; set; } = "";
    public bool IsManuallyAdded { get; set; }

    // Store launch IDs, saved so cards built from the cache launch through the store
    // before the background scan finishes (otherwise they fall back to the exe).
    public int? SteamAppId { get; set; }
    public string? EpicCatalogNamespace { get; set; }
    public string? EpicCatalogItemId { get; set; }
    public string? EpicAppName { get; set; }
    public string? XboxAumid { get; set; }
}
