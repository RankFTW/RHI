namespace RenoDXCommander.Models;

/// <summary>
/// A user-made RTX Encore preset: the values of the settings in RTX Encore's ⚙, as rtx-encore.jsonc holds them.
/// Keys are "section|key" (nested sections "parent/child|key"); values are what RtxEncoreConfig.Get returns
/// (strings without quotes).
/// </summary>
public class RtxEncorePreset
{
    public string? Name { get; set; }
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.Ordinal);
}
