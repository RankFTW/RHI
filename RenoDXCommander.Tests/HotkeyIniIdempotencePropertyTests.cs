using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Services;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for ApplyOverlayHotkey idempotence.
/// Feature: reshade-hotkey-settings, Property 6: ApplyOverlayHotkey idempotence
/// **Validates: Requirements 6.2**
/// </summary>
public class HotkeyIniIdempotencePropertyTests
{
    // ── Generators ────────────────────────────────────────────────────────────────

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

    private static readonly Gen<string> GenValue =
        from len in Gen.Choose(0, 20)
        from chars in Gen.ListOf(len, Gen.Choose(32, 126).Select(i => (char)i))
        select new string(chars.ToArray());

    private static readonly Gen<string> GenKeyOverlayValue =
        from vk in Gen.Choose(1, 254)
        from shift in Gen.Elements(0, 1)
        from ctrl in Gen.Elements(0, 1)
        from alt in Gen.Elements(0, 1)
        select $"{vk},{shift},{ctrl},{alt}";

    /// <summary>
    /// Generates two distinct KeyOverlay values.
    /// </summary>
    private static readonly Gen<(string first, string second)> GenTwoDifferentKeyOverlayValues =
        from first in GenKeyOverlayValue
        from second in GenKeyOverlayValue.Where(s => s != first)
        select (first, second);

    /// <summary>
    /// Generates valid INI file content, optionally including an [INPUT] section.
    /// </summary>
    private static Gen<string[]> GenIniContent()
    {
        var genSection =
            from name in GenSectionName
            from keyCount in Gen.Choose(1, 4)
            from keys in Gen.ListOf(keyCount, GenKeyValue())
            let header = $"[{name}]"
            select new[] { header }.Concat(keys).Concat(new[] { "" }).ToArray();

        var genInputSection =
            from keyCount in Gen.Choose(0, 3)
            from keys in Gen.ListOf(keyCount, GenKeyValue())
            from hasOverlay in Gen.Elements(true, false)
            from overlayVal in GenValue
            let header = "[INPUT]"
            let overlayLine = hasOverlay ? new[] { $"KeyOverlay={overlayVal}" } : Array.Empty<string>()
            select new[] { header }.Concat(overlayLine).Concat(keys).Concat(new[] { "" }).ToArray();

        return from sectionCount in Gen.Choose(0, 4)
               from sections in Gen.ListOf(sectionCount, genSection)
               from includeInput in Gen.Elements(true, false)
               from inputSection in genInputSection
               let allSections = includeInput
                   ? sections.Concat(new[] { inputSection })
                   : sections
               select allSections.SelectMany(s => s).ToArray();
    }

    private static Gen<string> GenKeyValue()
    {
        return from key in GenKeyName
               from value in GenValue
               select $"{key}={value}";
    }

    /// <summary>
    /// Counts the number of [INPUT] section headers in raw file lines (case-insensitive).
    /// </summary>
    private static int CountInputSections(string[] lines)
    {
        return lines.Count(line =>
            line.Trim().Equals("[INPUT]", StringComparison.OrdinalIgnoreCase));
    }

    // ── Property 6 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Property 6: ApplyOverlayHotkey idempotence
    /// For any valid INI file content (with or without an existing [INPUT] section),
    /// after calling ApplyOverlayHotkey, the resulting INI should contain exactly one
    /// [INPUT] section. Calling ApplyOverlayHotkey a second time with a different value
    /// should still result in exactly one [INPUT] section with the new value.
    /// **Validates: Requirements 6.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ApplyOverlayHotkey_Is_Idempotent_With_Single_Input_Section()
    {
        return Prop.ForAll(
            Arb.From(GenIniContent()),
            Arb.From(GenTwoDifferentKeyOverlayValues),
            (string[] iniLines, (string first, string second) overlayValues) =>
            {
                var tempFile = Path.GetTempFileName();
                try
                {
                    // Write initial INI content
                    File.WriteAllLines(tempFile, iniLines);

                    // ── First call ──────────────────────────────────────────
                    AuxInstallService.ApplyOverlayHotkey(tempFile, overlayValues.first);

                    var linesAfterFirst = File.ReadAllLines(tempFile);
                    var inputCountFirst = CountInputSections(linesAfterFirst);

                    if (inputCountFirst != 1)
                        return false.Label(
                            $"After first call: expected 1 [INPUT] section, found {inputCountFirst}");

                    var parsedFirst = AuxInstallService.ParseIni(linesAfterFirst);

                    if (!parsedFirst.ContainsKey("INPUT"))
                        return false.Label("After first call: missing [INPUT] in parsed result");

                    if (!parsedFirst["INPUT"].TryGetValue("KeyOverlay", out var firstResult))
                        return false.Label("After first call: missing KeyOverlay in [INPUT]");

                    if (firstResult != overlayValues.first)
                        return false.Label(
                            $"After first call: expected '{overlayValues.first}', got '{firstResult}'");

                    // ── Second call with different value ─────────────────────
                    AuxInstallService.ApplyOverlayHotkey(tempFile, overlayValues.second);

                    var linesAfterSecond = File.ReadAllLines(tempFile);
                    var inputCountSecond = CountInputSections(linesAfterSecond);

                    if (inputCountSecond != 1)
                        return false.Label(
                            $"After second call: expected 1 [INPUT] section, found {inputCountSecond}");

                    var parsedSecond = AuxInstallService.ParseIni(linesAfterSecond);

                    if (!parsedSecond.ContainsKey("INPUT"))
                        return false.Label("After second call: missing [INPUT] in parsed result");

                    if (!parsedSecond["INPUT"].TryGetValue("KeyOverlay", out var secondResult))
                        return false.Label("After second call: missing KeyOverlay in [INPUT]");

                    if (secondResult != overlayValues.second)
                        return false.Label(
                            $"After second call: expected '{overlayValues.second}', got '{secondResult}'");

                    return true.Label("OK: exactly one [INPUT] section after each call with correct value");
                }
                finally
                {
                    try { File.Delete(tempFile); } catch { }
                }
            });
    }
}
