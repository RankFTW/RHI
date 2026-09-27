using System.Text.Json.Serialization;

namespace RenoDXdbEditor;

/// <summary>
/// Serialization-only record for RenoDXdb-unity.json.
/// Identical to UnrealEntry but without the Method field —
/// Method is not applicable to Unity games.
/// </summary>
public record UnityEntry(
    [property: JsonPropertyName("Name")]     string  Name,
    [property: JsonPropertyName("Status")]   string  Status,
    [property: JsonPropertyName("Upgrades")] string? Upgrades,
    [property: JsonPropertyName("Comments")] string? Comments);
