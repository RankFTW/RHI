using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Services;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for ApplyOverlayHotkey writing correct KeyOverlay.
/// Feature: reshade-hotkey-settings, Property 4: ApplyOverlayHotkey writes correct KeyOverlay
/// **Validates: Requirements 4.3, 6.1, 6.3**
/// </summary>
public class ApplyOverlayHotkeyPropertyTests
{
    // ── Generators ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a valid INI section name (alphanumeric, no brackets or newlines).
    /// </summary>
    private static readonly Gen<string> GenSectionName =
        from len in Gen.Choose(1, 12)
        from chars in Gen.ListOf(len, Gen.Elements(
            'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J',
            'K', 'L', 'M', 'N', 'O', 'P', 'Q', 'R', 'S', 'T',
            'U', 'V', 'W', 'X', 'Y', 'Z',
            'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j',
            'k', 'l', 'm', 'n', 'o', 'p', 'q', 'r', 's', 't',
            'u', 'v', 'w', 'x', 'y', 'z',
            '0', '1', '2', '3', '4', '5', '6', '7', '8', '9'))
        select new string(chars.ToArray());

    /// <summary>
    /// Generates a valid INI key name (alphanumeric, no = or newlines).
    /// </summary>
    private static readonly Gen<string> GenKeyName =
        from len in Gen.Choose(1, 10)
        from chars in Gen.ListOf(len, Gen.Elements(
            'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J',
            'K', 'L', 'M', 'N', 'O', 'P', 'Q', 'R', 'S', 'T',
            'U', 'V', 'W', 'X', 'Y', 'Z',
            'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j',
            'k', 'l', 'm', 'n', 'o', 'p', 'q', 'r', 's', 't',
            'u', 'v', 'w', 'x', 'y', 'z'))
        select new string(chars.ToArray());

    /// <summary>
    /// Generates a valid INI value (printable ASCII, no newlines).
    /// </summary>
    private static readonly Gen<string> GenValue =
        from len in Gen.Choose(0, 20)
        from chars in Gen.ListOf(len, Gen.Choose(32, 126).Select(i => (char)i))
        select new string(chars.ToArray());

    /// <summary>
    /// Generates a non-empty KeyOverlay format string (vk,shift,ctrl,alt).
    /// </summary>
    private static readonly Gen<string> GenKeyOverlayValue =
        from vk in Gen.Choose(1, 254)
        from shift in Gen.Elements(0, 1)
        from ctrl in Gen.Elements(0, 1)
        from alt in Gen.Elements(0, 1)
        select $"{vk},{shift},{ctrl},{alt}";

    /// <summary>
    /// Generates valid INI file content as a string array of lines.
    /// </summary>
    private static readonly Gen<string[]> GenIniContent =
        from sectionCount in Gen.Choose(0, 4)
        from sections in Gen.ListOf(sectionCount, GenIniSection())
        select sections.SelectMany(s => s).ToArray();

    private static Gen<string[]> GenIniSection()
    {
        return from name in GenSectionName
               from keyCount in Gen.Choose(1, 4)
               from keys in Gen.ListOf(keyCount, GenKeyValue())
               let header = $"[{name}]"
               select new[] { header }.Concat(keys).Concat(new[] { "" }).ToArray();
    }

    private static Gen<string> GenKeyValue()
    {
        return from key in GenKeyName
               from value in GenValue
               select $"{key}={value}";
    }

    // ── Property 4 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Property 4: ApplyOverlayHotkey writes correct KeyOverlay
    /// For any valid INI file content and any non-empty KeyOverlay format string,
    /// after calling ApplyOverlayHotkey(iniFile, keyOverlayValue), parsing the resulting
    /// INI should yield an [INPUT] section containing KeyOverlay equal to the
    /// provided value.
    /// **Validates: Requirements 4.3, 6.1, 6.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ApplyOverlayHotkey_Writes_Correct_KeyOverlay()
    {
        return Prop.ForAll(
            Arb.From(GenIniContent),
            Arb.From(GenKeyOverlayValue),
            (string[] iniLines, string keyOverlayValue) =>
            {
                var tempFile = Path.GetTempFileName();
                try
                {
                    // Write initial INI content
                    File.WriteAllLines(tempFile, iniLines);

                    // Apply overlay hotkey
                    AuxInstallService.ApplyOverlayHotkey(tempFile, keyOverlayValue);

                    // Parse result and verify
                    var resultLines = File.ReadAllLines(tempFile);
                    var parsed = AuxInstallService.ParseIni(resultLines);

                    if (!parsed.ContainsKey("INPUT"))
                        return false.Label("Missing [INPUT] section after ApplyOverlayHotkey");

                    if (!parsed["INPUT"].TryGetValue("KeyOverlay", out var resultValue))
                        return false.Label("Missing KeyOverlay key in [INPUT] section");

                    if (resultValue != keyOverlayValue)
                        return false.Label(
                            $"KeyOverlay mismatch: expected='{keyOverlayValue}', got='{resultValue}'");

                    return true.Label($"OK: KeyOverlay='{keyOverlayValue}'");
                }
                finally
                {
                    try { File.Delete(tempFile); } catch { }
                }
            });
    }
}
