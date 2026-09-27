using System.Text.Json.Serialization;

namespace RenoDXCommander.Models;

/// <summary>
/// Represents a single entry from the RenoDXdb-unity.json database.
/// All games in this file use the generic Unity engine RenoDX addon.
/// The Upgrades field encodes per-game [renodx] INI key overrides to apply
/// after the standard Unity placeholder keys have been written.
/// </summary>
public class RenoDXDbUnityEntry
{
    [JsonPropertyName("Name")]
    public string Name { get; set; } = "";

    /// <summary>"Done" or "WIP"</summary>
    [JsonPropertyName("Status")]
    public string? Status { get; set; }

    /// <summary>
    /// Backtick-pair encoded INI overrides for the [renodx] section.
    /// Format: `` `KEY` `VALUE` `KEY` `VALUE` ... ``
    ///
    /// Upgrade format keys (Upgrade_R11G11B10_FLOAT etc.):
    ///   value token is a size word → "Output Size"=1, "Output Ratio"=2, "Any Size"=3
    ///   no value token (bare key)  → value is "" (empty, addon resolves at runtime)
    ///
    /// Boolean/value keys (Use_Swapchain_Proxy, Swapchain_Encoding etc.):
    ///   value token is a numeric string → written directly ("0", "1", "2")
    /// </summary>
    [JsonPropertyName("Upgrades")]
    public string? Upgrades { get; set; }

    /// <summary>Game-specific notes shown in the RenoDX info dialog.</summary>
    [JsonPropertyName("Comments")]
    public string? Comments { get; set; }

    // ── Computed helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Parses the Upgrades string into a list of (Key, Value) pairs ready to
    /// write directly into the [renodx] section of reshade.ini.
    /// Returns an empty list when Upgrades is null or empty.
    /// </summary>
    [JsonIgnore]
    public List<(string Key, string Value)> ParsedUpgrades
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Upgrades)) return new();

            var tokens = ParseBacktickTokens(Upgrades);
            var result = new List<(string Key, string Value)>();

            int i = 0;
            while (i < tokens.Count)
            {
                string key = tokens[i];
                i++;

                // Peek at next token — is it a value or another key?
                string value = "";
                if (i < tokens.Count && IsValueToken(tokens[i]))
                {
                    value = MapValue(tokens[i]);
                    i++;
                }

                result.Add((key, value));
            }

            return result;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns true when a token is a value token (size word or plain numeric),
    /// as opposed to a key token (which starts with Upgrade_ or is a known setting name).
    /// </summary>
    private static bool IsValueToken(string token) =>
        token.Equals("Output Size",  StringComparison.OrdinalIgnoreCase) ||
        token.Equals("Output Ratio", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("Any Size",     StringComparison.OrdinalIgnoreCase) ||
        token == "0" || token == "1" || token == "2";

    /// <summary>
    /// Maps a human-readable size word to its numeric INI value.
    /// Numeric strings pass through unchanged.
    /// </summary>
    private static string MapValue(string token) =>
        token.ToLowerInvariant() switch
        {
            "output size"  => "1",
            "output ratio" => "2",
            "any size"     => "3",
            _              => token,   // "0", "1", "2" pass through
        };

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
