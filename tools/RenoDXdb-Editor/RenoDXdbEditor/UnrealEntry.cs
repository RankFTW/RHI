using System.ComponentModel;
using System.Text.Json.Serialization;

namespace RenoDXdbEditor;

/// <summary>
/// Represents one entry in RenoDXdb-unreal.json.
/// </summary>
public class UnrealEntry : INotifyPropertyChanged
{
    private string  _name     = "";
    private string  _status   = "WIP";
    private string? _method;
    private string? _upgrades;
    private string? _comments;

    [JsonPropertyName("Name")]
    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(nameof(Name)); OnPropertyChanged(nameof(DisplayName)); }
    }

    [JsonPropertyName("Status")]
    public string Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(DisplayName)); }
    }

    /// <summary>null | "native" | "ini" | "upgrade"</summary>
    [JsonPropertyName("Method")]
    public string? Method
    {
        get => _method;
        set { _method = value; OnPropertyChanged(nameof(Method)); }
    }

    /// <summary>
    /// Raw stored string, e.g. "`B8G8R8A8_TYPELESS` `Output Size`".
    /// Multiple upgrade lines are newline-separated in the raw form used internally;
    /// each line is "`Format` `Size`" when serialised.
    /// </summary>
    [JsonPropertyName("Upgrades")]
    public string? Upgrades
    {
        get => _upgrades;
        set { _upgrades = value; OnPropertyChanged(nameof(Upgrades)); }
    }

    [JsonPropertyName("Comments")]
    public string? Comments
    {
        get => _comments;
        set { _comments = value; OnPropertyChanged(nameof(Comments)); }
    }

    [JsonIgnore]
    public string DisplayName => $"{Name}  [{Status}]";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // ── Upgrades helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Valid format tokens (left column).
    /// </summary>
    public static readonly string[] FormatValues =
    [
        "B8G8R8A8_TYPELESS",
        "B8G8R8A8_UNORM",
        "B8G8R8A8_UNORM_SRGB",
        "R8G8B8A8_TYPELESS",
        "R8G8B8A8_UNORM",
        "R8G8B8A8_UNORM_SRGB",
        "R10G10B10A2_TYPELESS",
        "R10G10B10A2_UNORM",
        "R16G16B16A16_TYPELESS",
        "R16G16B16A16_FLOAT",
        "R16G16B16A16_UNORM",
        "R32G32B32A32_TYPELESS",
        "R32G32B32A32_FLOAT",
    ];

    /// <summary>
    /// Valid size tokens (right column).
    /// </summary>
    public static readonly string[] SizeValues =
    [
        "Output Size",
        "Output Ratio",
        "Any Size",
    ];

    /// <summary>
    /// Parses the stored Upgrades string into a list of (format, size) pairs.
    /// Each pair is backtick-encoded: "`Format` `Size`"
    /// </summary>
    public List<(string Format, string Size)> ParseUpgrades()
    {
        var result = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(Upgrades)) return result;

        // Each entry may be space-separated backtick tokens on one line,
        // or multiple entries separated by newlines.
        var lines = Upgrades.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var tokens = ParseBacktickTokens(line.Trim());
            if (tokens.Count >= 2)
                result.Add((tokens[0], tokens[1]));
            else if (tokens.Count == 1)
                result.Add((tokens[0], ""));
        }
        return result;
    }

    /// <summary>
    /// Serialises a list of (format, size) pairs back into the stored string format.
    /// </summary>
    public static string? SerialiseUpgrades(List<(string Format, string Size)> pairs)
    {
        var lines = pairs
            .Where(p => !string.IsNullOrWhiteSpace(p.Format))
            .Select(p => string.IsNullOrWhiteSpace(p.Size)
                ? $"`{p.Format}`"
                : $"`{p.Format}` `{p.Size}`")
            .ToList();
        return lines.Count == 0 ? null : string.Join(" ", lines);
    }

    private static List<string> ParseBacktickTokens(string s)
    {
        var result = new List<string>();
        int i = 0;
        while (i < s.Length)
        {
            if (s[i] == '`')
            {
                int end = s.IndexOf('`', i + 1);
                if (end < 0) break;
                result.Add(s[(i + 1)..end]);
                i = end + 1;
            }
            else i++;
        }
        return result;
    }
}
