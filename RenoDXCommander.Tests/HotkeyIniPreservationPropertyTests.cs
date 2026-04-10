using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Services;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for ApplyOverlayHotkey preserving other sections and keys.
/// Feature: reshade-hotkey-settings, Property 5: ApplyOverlayHotkey preserves other sections and keys
/// **Validates: Requirements 4.4, 6.4, 6.5**
/// </summary>
public class HotkeyIniPreservationPropertyTests
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
    /// Generates a non-INPUT section name to ensure we have sections that must be preserved.
    /// </summary>
    private static readonly Gen<string> GenNonInputSectionName =
        GenSectionName.Where(s => !s.Equals("INPUT", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Generates a key name that is not KeyOverlay (case-insensitive) for INPUT section extra keys.
    /// </summary>
    private static readonly Gen<string> GenNonOverlayKeyName =
        GenKeyName.Where(k => !k.Equals("KeyOverlay", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Generates INI content with multiple non-INPUT sections and optionally an [INPUT] section
    /// containing extra keys (besides KeyOverlay).
    /// </summary>
    private static Gen<string[]> GenIniWithSections()
    {
        var genNonInputSection =
            from name in GenNonInputSectionName
            from keyCount in Gen.Choose(1, 4)
            from keys in Gen.ListOf(keyCount, GenKeyValue())
            let header = $"[{name}]"
            select new[] { header }.Concat(keys).Concat(new[] { "" }).ToArray();

        var genInputSectionWithExtraKeys =
            from extraCount in Gen.Choose(1, 3)
            from extras in Gen.ListOf(extraCount, GenNonOverlayKeyValue())
            from hasExistingOverlay in Gen.Elements(true, false)
            from existingOverlay in GenValue
            let header = "[INPUT]"
            let overlayLine = hasExistingOverlay ? new[] { $"KeyOverlay={existingOverlay}" } : Array.Empty<string>()
            select new[] { header }.Concat(overlayLine).Concat(extras).Concat(new[] { "" }).ToArray();

        return from nonInputCount in Gen.Choose(1, 3)
               from nonInputSections in Gen.ListOf(nonInputCount, genNonInputSection)
               from includeInput in Gen.Elements(true, false)
               from inputSection in genInputSectionWithExtraKeys
               let allSections = includeInput
                   ? nonInputSections.Concat(new[] { inputSection })
                   : nonInputSections
               select allSections.SelectMany(s => s).ToArray();
    }

    private static Gen<string> GenKeyValue()
    {
        return from key in GenKeyName
               from value in GenValue
               select $"{key}={value}";
    }

    private static Gen<string> GenNonOverlayKeyValue()
    {
        return from key in GenNonOverlayKeyName
               from value in GenValue
               select $"{key}={value}";
    }

    // ── Property 5 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Property 5: ApplyOverlayHotkey preserves other sections and keys
    /// For any valid INI file content with arbitrary sections and keys, after calling
    /// ApplyOverlayHotkey, all sections other than [INPUT] should have identical keys
    /// and values as before the call. Keys within [INPUT] other than KeyOverlay should
    /// also be preserved.
    /// **Validates: Requirements 4.4, 6.4, 6.5**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ApplyOverlayHotkey_Preserves_Other_Sections_And_Keys()
    {
        return Prop.ForAll(
            Arb.From(GenIniWithSections()),
            Arb.From(GenKeyOverlayValue),
            (string[] iniLines, string keyOverlayValue) =>
            {
                var tempFile = Path.GetTempFileName();
                try
                {
                    // Write initial INI content
                    File.WriteAllLines(tempFile, iniLines);

                    // Parse before applying
                    var before = AuxInstallService.ParseIni(File.ReadAllLines(tempFile));

                    // Apply overlay hotkey
                    AuxInstallService.ApplyOverlayHotkey(tempFile, keyOverlayValue);

                    // Parse after applying
                    var after = AuxInstallService.ParseIni(File.ReadAllLines(tempFile));

                    // Check 1: All sections other than INPUT are identical
                    foreach (var (section, beforeKeys) in before)
                    {
                        if (section.Equals("INPUT", StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (!after.TryGetValue(section, out var afterKeys))
                            return false.Label($"Section [{section}] missing after ApplyOverlayHotkey");

                        foreach (var (key, value) in beforeKeys)
                        {
                            if (!afterKeys.TryGetValue(key, out var afterValue))
                                return false.Label($"Key '{key}' in [{section}] missing after ApplyOverlayHotkey");

                            if (afterValue != value)
                                return false.Label(
                                    $"Key '{key}' in [{section}] changed: '{value}' -> '{afterValue}'");
                        }

                        // Ensure no extra keys appeared in this section
                        if (afterKeys.Count != beforeKeys.Count)
                            return false.Label(
                                $"Section [{section}] key count changed: {beforeKeys.Count} -> {afterKeys.Count}");
                    }

                    // Check that no new non-INPUT sections appeared
                    foreach (var section in after.Keys)
                    {
                        if (section.Equals("INPUT", StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (!before.ContainsKey(section))
                            return false.Label($"Unexpected new section [{section}] after ApplyOverlayHotkey");
                    }

                    // Check 2: Keys within [INPUT] other than KeyOverlay are preserved
                    if (before.TryGetValue("INPUT", out var beforeInput))
                    {
                        var afterInput = after["INPUT"];

                        foreach (var (key, value) in beforeInput)
                        {
                            if (key.Equals("KeyOverlay", StringComparison.OrdinalIgnoreCase))
                                continue;

                            if (!afterInput.TryGetValue(key, out var afterValue))
                                return false.Label($"Key '{key}' in [INPUT] missing after ApplyOverlayHotkey");

                            if (afterValue != value)
                                return false.Label(
                                    $"Key '{key}' in [INPUT] changed: '{value}' -> '{afterValue}'");
                        }

                        // Ensure no extra keys appeared in INPUT besides KeyOverlay
                        var beforeNonOverlayCount = beforeInput.Keys
                            .Count(k => !k.Equals("KeyOverlay", StringComparison.OrdinalIgnoreCase));
                        var afterNonOverlayCount = afterInput.Keys
                            .Count(k => !k.Equals("KeyOverlay", StringComparison.OrdinalIgnoreCase));

                        if (afterNonOverlayCount != beforeNonOverlayCount)
                            return false.Label(
                                $"[INPUT] non-KeyOverlay key count changed: {beforeNonOverlayCount} -> {afterNonOverlayCount}");
                    }

                    return true.Label("OK: other sections and INPUT keys preserved");
                }
                finally
                {
                    try { File.Delete(tempFile); } catch { }
                }
            });
    }
}
