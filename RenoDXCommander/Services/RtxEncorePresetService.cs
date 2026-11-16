using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// The user's own RTX Encore presets: <see cref="SlotCount"/> global slots shared by all games,
/// in %LocalAppData%\RHI\rtx_encore_presets.json.
/// </summary>
public static class RtxEncorePresetService
{
    public const int SlotCount = 6;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "rtx_encore_presets.json");

    /// <summary>Always exactly <see cref="SlotCount"/> entries (null = empty slot).</summary>
    public static RtxEncorePreset?[] Load()
    {
        var result = new RtxEncorePreset?[SlotCount];
        // SafeJsonFile: a corrupt file is kept aside and the .bak used; an unreadable one blocks saves
        var loaded = SafeJsonFile.Read<RtxEncorePreset?[]>(FilePath, out var status);
        if (loaded != null)
        {
            for (int i = 0; i < SlotCount && i < loaded.Length; i++)
                result[i] = loaded[i];
        }
        else if (status != SafeJsonFile.LoadStatus.Missing)
        {
            CrashReporter.Log($"[RtxEncorePresetService.Load] rtx_encore_presets.json {status} — showing empty slots");
        }
        return result;
    }

    /// <summary>Saves all slots. False when it couldn't be written (logged).</summary>
    public static bool Save(RtxEncorePreset?[] presets)
    {
        var toWrite = new RtxEncorePreset?[SlotCount];
        for (int i = 0; i < SlotCount; i++)
            toWrite[i] = i < presets.Length ? presets[i] : null;
        return SafeJsonFile.Write(FilePath, toWrite, "RtxEncorePresetService.Save", SafeJsonFile.Indented);
    }

    public static string KeyOf(string section, string key) => section + "|" + key;

    public static (string Section, string Key) SplitKey(string presetKey)
    {
        int bar = presetKey.LastIndexOf('|');
        return bar < 0 ? ("", presetKey) : (presetKey[..bar], presetKey[(bar + 1)..]);
    }
}
