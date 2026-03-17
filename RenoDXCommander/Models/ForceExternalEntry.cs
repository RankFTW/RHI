using System.Text.Json.Serialization;

namespace RenoDXCommander.Models;

public class ForceExternalEntry
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }
}
