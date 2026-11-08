namespace RenoDXCommander.Models;

public class DetectedGame
{
    public string Name { get; set; } = "";
    public string InstallPath { get; set; } = "";
    public string Source { get; set; } = "";
    public bool IsManuallyAdded { get; set; }
    public int? SteamAppId { get; set; }

    /// <summary>Epic Games Store catalog namespace (for protocol launch).</summary>
    public string? EpicCatalogNamespace { get; set; }

    /// <summary>Epic Games Store catalog item id (for protocol launch; the new launcher needs namespace:item:app).</summary>
    public string? EpicCatalogItemId { get; set; }

    /// <summary>Epic Games Store app name (for protocol launch).</summary>
    public string? EpicAppName { get; set; }

    /// <summary>Xbox/Game Pass App User Model ID (for shell:AppsFolder launch).</summary>
    public string? XboxAumid { get; set; }
}
