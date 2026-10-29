using System.Text.Json.Serialization;

namespace RenoDXCommander.Models;

/// <summary>
/// Represents a single entry from the RenoDXdb-source.json database.
/// All games in this file use the generic Source engine RenoDX addon.
/// The file also carries a Bitness field, which RHI ignores (it detects bitness itself).
/// </summary>
public class RenoDXDbSourceEntry
{
    [JsonPropertyName("Name")]
    public string Name { get; set; } = "";

    /// <summary>"Done" or "WIP"</summary>
    [JsonPropertyName("Status")]
    public string? Status { get; set; }

    /// <summary>Game-specific notes from the wiki.</summary>
    [JsonPropertyName("Comments")]
    public string? Comments { get; set; }
}
