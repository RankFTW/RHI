// HsfService.Keybinds.cs — HSF's in-game hotkeys in HSF.ini ([HSF] FrameGenKey / MfgKey / OsdKey, Win32 virtual-key
// codes, 0 = none), the keys HSF's menu sets in its KEYBINDS section. RHI writes Settings › Components › HSF's keys
// into a new HSF.ini at install and on "Write keys to all HSF games"; the HSF ⚙ changes one game's keys.
// HSF re-reads HSF.ini when it changes, so a change applies in a running game.

namespace RenoDXCommander.Services;

public partial class HsfService
{
    /// <summary>One HSF hotkey: its HSF.ini key, the label RHI shows, and HSF's default when the key is absent.</summary>
    public sealed record Keybind(string IniKey, string Label, int Default, string Help);

    /// <summary>The keys RHI manages, in the order HSF's KEYBINDS section shows them.</summary>
    public static readonly Keybind[] Keybinds =
    {
        new("FrameGenKey", "Frame gen on/off", 0, "Turns HSF's frame generation on and off in game. HSF has no key for it by default."),
        new("MfgKey", "Multiplier", 0x76, "Cycles HSF's multiplier in game (2x up to what your card can do). HSF's default is F7."),
        new("OsdKey", "Overlay on/off", 0, "Turns HSF's on-screen overlay off, and back to the look it had. HSF has no key for it by default."),
    };

    /// <summary>Every [HSF] key HSF uses as a hotkey, so one key never does two things (HSF clears the other one the same way).</summary>
    internal static readonly string[] AllHotkeyIniKeys =
        { "FrameGenKey", "MfgKey", "OsdKey", "ViewKey", "InputKey", "DepthKey", "RecordKey", "DlssgCameraKey" };

    /// <summary>The game's key for <paramref name="bind"/>: HSF.ini's value, else HSF's default.</summary>
    public static int ReadKeybind(string installPath, Keybind bind)
    {
        var value = Dlssg20_30Service.ReadIniValue(IniPath(installPath), "HSF", bind.IniKey);
        return int.TryParse(value, out var vk) && vk is >= 0 and <= 255 ? vk : bind.Default;
    }

    /// <summary>
    /// Sets one key in the game's HSF.ini (0 = none). Another HSF hotkey on the same key is set to none, as HSF's
    /// own menu does. False when HSF.ini can't be written.
    /// </summary>
    public bool WriteKeybind(string installPath, Keybind bind, int vk)
    {
        var ini = IniPath(installPath);
        try
        {
            if (!File.Exists(ini)) return false;
            if (vk > 0) ClearDuplicateKeys(ini, bind.IniKey, vk);
            Dlssg20_30Service.WriteIniValue(ini, "HSF", bind.IniKey, vk.ToString(System.Globalization.CultureInfo.InvariantCulture));
            _crashReporter.Log($"[HsfService.WriteKeybind] {bind.IniKey}={vk} → '{ini}'");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"[HsfService.WriteKeybind] {bind.IniKey} not written to '{ini}' — {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Writes the global keys (Settings › Components › HSF) that are set (not 0) into the game's HSF.ini.
    /// Returns how many were written.
    /// </summary>
    public int ApplyKeybinds(string installPath, IReadOnlyDictionary<string, int> keys)
    {
        int written = 0;
        foreach (var bind in Keybinds)
            if (keys.TryGetValue(bind.IniKey, out var vk) && vk > 0 && WriteKeybind(installPath, bind, vk))
                written++;
        return written;
    }

    /// <summary>Sets every other HSF hotkey that uses <paramref name="vk"/> to none.</summary>
    private void ClearDuplicateKeys(string ini, string iniKey, int vk)
    {
        var text = vk.ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (var other in AllHotkeyIniKeys)
        {
            if (other.Equals(iniKey, StringComparison.OrdinalIgnoreCase)) continue;
            var current = Dlssg20_30Service.ReadIniValue(ini, "HSF", other);
            // MfgKey is F7 when absent: an absent MfgKey still clashes with F7
            bool clash = current == text || (current == null && other == "MfgKey" && vk == 0x76);
            if (!clash) continue;
            Dlssg20_30Service.WriteIniValue(ini, "HSF", other, "0");
            _crashReporter.Log($"[HsfService.ClearDuplicateKeys] {other} was the same key as {iniKey} ({vk}), set to none");
        }
    }
}
