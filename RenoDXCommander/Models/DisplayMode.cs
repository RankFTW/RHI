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
    /// <summary>Active mode id. Null = Off: the game isn't in a mode and follows the global settings.</summary>
    public string? Mode { get; set; }

    /// <summary>Item keys this game swaps. Null = the default list from Settings.</summary>
    public List<string>? Items { get; set; }

    /// <summary>Mode id → item key → this game's own value for that mode (remembered when switching away).</summary>
    public Dictionary<string, Dictionary<string, string>> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Item key → the value the game had right after it entered its active mode (null = the item couldn't be read then).
    /// Leaving the mode remembers a value only when it changed since (an in-game change), so a mode that had no value
    /// doesn't inherit the previous mode's. Null dictionary = never switched: the game's values are its starting mode's.
    /// </summary>
    public Dictionary<string, string?>? Entry { get; set; }

    /// <summary>
    /// Item key → the game's value before it entered a mode from Off. Turning the mode off writes these back for items
    /// with no global setting, and a mode with no value of its own uses them too. Null = none recorded.
    /// </summary>
    public Dictionary<string, string>? Original { get; set; }

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
