using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Services;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for MergeRsIni writing non-default overlay hotkey.
/// Feature: reshade-hotkey-settings, Property 7: MergeRsIni writes non-default hotkey
/// **Validates: Requirements 5.1, 5.3**
/// </summary>
public class MergeRsIniHotkeyPropertyTests
{
    // ── Generators ────────────────────────────────────────────────────────────────

    private static readonly Gen<string> GenSectionName =
        from len in Gen.Choose(1, 10)
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
        from len in Gen.Choose(1, 8)
        from chars in Gen.ListOf(len, Gen.Elements(
            'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J',
            'K', 'L', 'M', 'N', 'O', 'P', 'Q', 'R', 'S', 'T',
            'U', 'V', 'W', 'X', 'Y', 'Z',
            'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j',
            'k', 'l', 'm', 'n', 'o', 'p', 'q', 'r', 's', 't',
            'u', 'v', 'w', 'x', 'y', 'z'))
        select new string(chars.ToArray());

    private static readonly Gen<string> GenValue =
        from len in Gen.Choose(0, 15)
        from chars in Gen.ListOf(len, Gen.Choose(32, 126).Select(i => (char)i))
        select new string(chars.ToArray());

    /// <summary>
    /// Generates a non-default KeyOverlay string (anything other than "36,0,0,0").
    /// Uses valid VK codes (1–254) with modifier flags, filtering out the default.
    /// </summary>
    private static readonly Gen<string> GenNonDefaultHotkey =
        (from vk in Gen.Choose(1, 254)
         from shift in Gen.Elements(0, 1)
         from ctrl in Gen.Elements(0, 1)
         from alt in Gen.Elements(0, 1)
         select $"{vk},{shift},{ctrl},{alt}")
        .Where(s => s != "36,0,0,0");

    /// <summary>
    /// Generates valid INI file content as lines.
    /// </summary>
    private static readonly Gen<string[]> GenIniContent =
        from sectionCount in Gen.Choose(1, 3)
        from sections in Gen.ListOf(sectionCount, GenIniSection())
        select sections.SelectMany(s => s).ToArray();

    private static Gen<string[]> GenIniSection()
    {
        return from name in GenSectionName
               from keyCount in Gen.Choose(1, 3)
               from keys in Gen.ListOf(keyCount, GenKeyName)
               from values in Gen.ListOf(keyCount, GenValue)
               let lines = new[] { $"[{name}]" }
                   .Concat(keys.Zip(values).Select(kv => $"{kv.First}={kv.Second}"))
                   .Concat(new[] { "" })
               select lines.ToArray();
    }

    // ── Property 7 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Property 7: MergeRsIni writes non-default hotkey
    /// For any valid INI template content, any valid game INI content, and any non-default
    /// KeyOverlay string (not "36,0,0,0"), after simulating MergeRsIni with that value,
    /// the resulting reshade.ini should contain an [INPUT] section with KeyOverlay equal
    /// to the provided value.
    ///
    /// Since MergeRsIni reads from AuxInstallService.RsIniPath (a static path), we test
    /// at a lower level using ParseIni/WriteIni/ApplyOverlayHotkey to verify the merge
    /// + overlay hotkey integration logic.
    /// **Validates: Requirements 5.1, 5.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property MergeRsIni_With_NonDefault_Hotkey_Writes_Correct_KeyOverlay()
    {
        return Prop.ForAll(
            Arb.From(GenIniContent),
            Arb.From(GenIniContent),
            Arb.From(GenNonDefaultHotkey),
            (string[] templateLines, string[] gameLines, string overlayHotkey) =>
            {
                var tempFile = Path.GetTempFileName();
                try
                {
                    // Simulate what MergeRsIni does:
                    // 1. Parse both template and game INI
                    var gameIni = AuxInstallService.ParseIni(gameLines);
                    var templateIni = AuxInstallService.ParseIni(templateLines);

                    // 2. Merge: template keys overwrite, game-only keys preserved
                    foreach (var (section, templateKeys) in templateIni)
                    {
                        if (!gameIni.TryGetValue(section, out var gameKeys))
                        {
                            gameIni[section] = new AuxInstallService.OrderedDict(templateKeys);
                        }
                        else
                        {
                            foreach (var (key, value) in templateKeys)
                                gameKeys[key] = value;
                        }
                    }

                    // 3. Write merged INI
                    AuxInstallService.WriteIni(tempFile, gameIni);

                    // 4. Apply overlay hotkey (as MergeRsIni does when overlayHotkey is non-default)
                    //    MergeRsIni checks: overlayHotkey != null && !IsDefaultHotkey(overlayHotkey)
                    //    Since we generate non-default values, this always applies.
                    AuxInstallService.ApplyOverlayHotkey(tempFile, overlayHotkey);

                    // 5. Verify result
                    var resultLines = File.ReadAllLines(tempFile);
                    var parsed = AuxInstallService.ParseIni(resultLines);

                    if (!parsed.ContainsKey("INPUT"))
                        return false.Label("Missing [INPUT] section after merge + apply hotkey");

                    if (!parsed["INPUT"].TryGetValue("KeyOverlay", out var resultValue))
                        return false.Label("Missing KeyOverlay key in [INPUT] section");

                    if (resultValue != overlayHotkey)
                        return false.Label(
                            $"KeyOverlay mismatch: expected='{overlayHotkey}', got='{resultValue}'");

                    return true.Label($"OK: KeyOverlay='{overlayHotkey}' after merge");
                }
                finally
                {
                    try { File.Delete(tempFile); } catch { }
                }
            });
    }
}
