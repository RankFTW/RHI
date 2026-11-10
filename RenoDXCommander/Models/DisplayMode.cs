// DisplayMode.cs -- saved data for Display modes (Monitor / TV / ...): the modes with their default values,
// the default list of things a game swaps, and each game's own state (active mode, its own list, and the values
// it remembers per mode). Saved in settings.json as "DisplayModes" (DisplayModeStore.Snapshot).

namespace RenoDXCommander.Models;

/// <summary>One display mode, e.g. "TV", with its default value per item key (DisplayModeItems).</summary>
public sealed class DisplayModeDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Item key → default value for games that have no value of their own for this mode.</summary>
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>One game's display mode state, keyed "GameName|Store".</summary>
public sealed class GameDisplayModeState
{
    /// <summary>Active mode id. Null = the first mode (nothing has been switched yet).</summary>
    public string? Mode { get; set; }

    /// <summary>Item keys this game swaps. Null = the default list from Settings.</summary>
    public List<string>? Items { get; set; }

    /// <summary>Mode id → item key → this game's own value for that mode (remembered when switching away).</summary>
    public Dictionary<string, Dictionary<string, string>> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Install folder and addon folder at the last switch, so global writers can find the game by folder.</summary>
    public List<string>? Folders { get; set; }
}

/// <summary>Everything saved for Display modes.</summary>
public sealed class DisplayModeData
{
    public List<DisplayModeDefinition> Modes { get; set; } = new();
    public List<string> DefaultItems { get; set; } = new();
    public Dictionary<string, GameDisplayModeState> Games { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
