using System.Text.Json.Serialization;

namespace RenoDXCommander.Models;

public class ManifestDllNames
{
    /// <summary>Filename to install ReShade as (e.g. "d3d9.dll"). Empty = use default.</summary>
    [JsonPropertyName("reshade")]
    public string? ReShade { get; set; }
}
