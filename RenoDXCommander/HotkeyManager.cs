namespace RenoDXCommander;

/// <summary>
/// Shared hotkey parsing, formatting, and building utilities.
/// Extracted from SettingsHandler — all methods are pure/static.
/// </summary>
public static class HotkeyManager
{
    // ── Hotkey helper methods ──────────────────────────────────────────

    private static readonly Dictionary<int, string> VkNames = new()
    {
        [8] = "Backspace", [9] = "Tab", [13] = "Enter", [19] = "Pause", [20] = "Caps Lock",
        [27] = "Escape", [32] = "Space", [33] = "Page Up", [34] = "Page Down",
        [35] = "End", [36] = "Home", [37] = "Left", [38] = "Up", [39] = "Right", [40] = "Down",
        [44] = "Print Screen", [45] = "Insert", [46] = "Delete",
        [48] = "0", [49] = "1", [50] = "2", [51] = "3", [52] = "4",
        [53] = "5", [54] = "6", [55] = "7", [56] = "8", [57] = "9",
        [65] = "A", [66] = "B", [67] = "C", [68] = "D", [69] = "E", [70] = "F",
        [71] = "G", [72] = "H", [73] = "I", [74] = "J", [75] = "K", [76] = "L",
        [77] = "M", [78] = "N", [79] = "O", [80] = "P", [81] = "Q", [82] = "R",
        [83] = "S", [84] = "T", [85] = "U", [86] = "V", [87] = "W", [88] = "X",
        [89] = "Y", [90] = "Z",
        [96] = "Num 0", [97] = "Num 1", [98] = "Num 2", [99] = "Num 3", [100] = "Num 4",
        [101] = "Num 5", [102] = "Num 6", [103] = "Num 7", [104] = "Num 8", [105] = "Num 9",
        [106] = "Num *", [107] = "Num +", [109] = "Num -", [110] = "Num .", [111] = "Num /",
        [112] = "F1", [113] = "F2", [114] = "F3", [115] = "F4", [116] = "F5", [117] = "F6",
        [118] = "F7", [119] = "F8", [120] = "F9", [121] = "F10", [122] = "F11", [123] = "F12",
        [124] = "F13", [125] = "F14", [126] = "F15", [127] = "F16", [128] = "F17", [129] = "F18",
        [130] = "F19", [131] = "F20", [132] = "F21", [133] = "F22", [134] = "F23", [135] = "F24",
        [144] = "Num Lock", [145] = "Scroll Lock",
        [186] = ";", [187] = "=", [188] = ",", [189] = "-", [190] = ".", [191] = "/",
        [192] = "`", [219] = "[", [220] = "\\", [221] = "]", [222] = "'",
    };

    /// <summary>
    /// Parses a KeyOverlay format string "vk,shift,ctrl,alt" into its components.
    /// Returns (vkCode, shift, ctrl, alt). Returns default (36, false, false, false) on invalid input.
    /// </summary>
    public static (int vk, bool shift, bool ctrl, bool alt) ParseHotkeyString(string value)
    {
        try
        {
            var parts = value.Split(',');
            if (parts.Length != 4) return (36, false, false, false);
            return (int.Parse(parts[0]), parts[1] != "0", parts[2] != "0", parts[3] != "0");
        }
        catch
        {
            return (36, false, false, false);
        }
    }

    /// <summary>
    /// Builds a KeyOverlay format string from components.
    /// </summary>
    public static string BuildHotkeyString(int vk, bool shift, bool ctrl, bool alt)
    {
        return $"{vk},{(shift ? 1 : 0)},{(ctrl ? 1 : 0)},{(alt ? 1 : 0)}";
    }

    /// <summary>
    /// Formats a hotkey into a human-readable display string.
    /// Modifier order: Ctrl, Shift, Alt, then the main key name.
    /// </summary>
    public static string FormatHotkeyDisplay(int vk, bool shift, bool ctrl, bool alt)
    {
        var parts = new List<string>();
        if (ctrl) parts.Add("Ctrl");
        if (shift) parts.Add("Shift");
        if (alt) parts.Add("Alt");
        parts.Add(VkNames.TryGetValue(vk, out var name) ? name : $"Key {vk}");
        return string.Join(" + ", parts);
    }

    /// <summary>
    /// Formats a KeyOverlay string "vk,shift,ctrl,alt" into a human-readable display string.
    /// </summary>
    public static string FormatHotkeyDisplay(string keyOverlayValue)
    {
        var (vk, shift, ctrl, alt) = ParseHotkeyString(keyOverlayValue);
        return FormatHotkeyDisplay(vk, shift, ctrl, alt);
    }

    /// <summary>
    /// Returns true if the given KeyOverlay string represents the default Home key (36,0,0,0).
    /// </summary>
    public static bool IsDefaultHotkey(string keyOverlayValue)
    {
        return keyOverlayValue == "36,0,0,0";
    }

    /// <summary>
    /// Builds a ReLimiter-format hotkey string from VK code and modifiers.
    /// Format: [Ctrl+][Alt+][Shift+]KeyName (e.g. "Ctrl+F12", "Alt+P", "F1")
    /// </summary>
    public static string BuildUlHotkeyString(int vk, bool shift, bool ctrl, bool alt)
    {
        var parts = new List<string>();
        if (ctrl) parts.Add("Ctrl");
        if (alt) parts.Add("Alt");
        if (shift) parts.Add("Shift");
        // ReLimiter expects key names without spaces (e.g. "PageUp" not "Page Up")
        var keyName = VkNames.TryGetValue(vk, out var name) ? name.Replace(" ", "") : $"0x{vk:X2}";
        parts.Add(keyName);
        return string.Join("+", parts);
    }
}
