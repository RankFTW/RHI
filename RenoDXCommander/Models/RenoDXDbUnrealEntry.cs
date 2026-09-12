using System.Text.Json.Serialization;

namespace RenoDXCommander.Models;

/// <summary>
/// Represents a single entry from the RenoDXdb-unreal.json database.
/// All games in this file use UE-Extended. The Method field encodes how
/// UE-Extended should be configured for the game.
/// </summary>
public class RenoDXDbUnrealEntry
{
    [JsonPropertyName("Name")]
    public string Name { get; set; } = "";

    /// <summary>"Done" or "WIP"</summary>
    [JsonPropertyName("Status")]
    public string? Status { get; set; }

    /// <summary>
    /// "native"  → Set_Path=0, no Engine.ini HDR
    /// "ini"     → Set_Path=0, deploy Engine.ini HDR
    /// "upgrade" → Set_Path=1, apply specific upgrade format from Upgrades field
    /// null      → Set_Path=1, no Engine.ini HDR, no specific upgrade
    /// </summary>
    [JsonPropertyName("Method")]
    public string? Method { get; set; }

    /// <summary>
    /// For Method="upgrade": describes which format and size to set.
    /// Format: "`FORMAT_NAME` `Size option`" e.g. "`B8G8R8A8_TYPELESS` `Output Size`"
    /// </summary>
    [JsonPropertyName("Upgrades")]
    public string? Upgrades { get; set; }

    /// <summary>Game-specific notes shown in the RenoDX info dialog.</summary>
    [JsonPropertyName("Comments")]
    public string? Comments { get; set; }

    // ── Computed helpers ─────────────────────────────────────────────────────

    /// <summary>True when Set_Path should be 1 (SDR upgrade path).</summary>
    [JsonIgnore]
    public bool UsesSdrPath => Method == null || string.Equals(Method, "upgrade", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when Engine.ini HDR keys should be deployed.</summary>
    [JsonIgnore]
    public bool DeployEngineIniHdr => string.Equals(Method, "ini", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Parses the Upgrades field and returns the INI key name to force-write.
    /// e.g. "`B8G8R8A8_TYPELESS` `Output Size`" → "Upgrade_B8G8R8A8_TYPELESS"
    /// Returns null when Method != "upgrade" or Upgrades is empty.
    /// </summary>
    [JsonIgnore]
    public string? UpgradeIniKey
    {
        get
        {
            if (!string.Equals(Method, "upgrade", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(Upgrades))
                return null;

            // Extract the first backtick-quoted token — that's the format name
            var parts = ParseBacktickTokens(Upgrades);
            if (parts.Count == 0) return null;
            return "Upgrade_" + parts[0].Replace(" ", "_");
        }
    }

    /// <summary>
    /// The numeric INI value for the upgrade size option.
    /// "Output size" → "1", "Output ratio" → "2", "Any size" → "3"
    /// Returns null when no size token is found.
    /// </summary>
    [JsonIgnore]
    public string? UpgradeIniValue
    {
        get
        {
            if (!string.Equals(Method, "upgrade", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(Upgrades))
                return null;

            var parts = ParseBacktickTokens(Upgrades);
            if (parts.Count < 2) return null;

            return parts[1].ToLowerInvariant() switch
            {
                "output size"  => "1",
                "output ratio" => "2",
                "any size"     => "3",
                _ => null,
            };
        }
    }

    private static List<string> ParseBacktickTokens(string input)
    {
        var tokens = new List<string>();
        int i = 0;
        while (i < input.Length)
        {
            int start = input.IndexOf('`', i);
            if (start < 0) break;
            int end = input.IndexOf('`', start + 1);
            if (end < 0) break;
            var token = input.Substring(start + 1, end - start - 1).Trim();
            if (!string.IsNullOrEmpty(token))
                tokens.Add(token);
            i = end + 1;
        }
        return tokens;
    }
}
