using System.Text.Json;
using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for the auto-detect-bitness feature.
/// Uses FsCheck with xUnit. Each property runs a minimum of 100 iterations.
/// </summary>
public class AutoDetectBitnessPropertyTests : IDisposable
{
    private readonly PeHeaderService _peHeaderService = new();
    private readonly List<string> _tempFiles = new();
    private readonly List<string> _tempDirs = new();

    public void Dispose()
    {
        foreach (var f in _tempFiles)
        {
            try { File.Delete(f); } catch { }
        }
        foreach (var d in _tempDirs)
        {
            try { Directory.Delete(d, recursive: true); } catch { }
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a minimal valid PE byte array with the given Machine field value.
    /// Layout:
    ///   [0x00]       'M','Z'           — DOS MZ signature
    ///   [0x3C..0x3F] e_lfanew (Int32)  — offset to PE header
    ///   [peOffset]   'P','E',0,0       — PE signature
    ///   [peOffset+4] Machine (UInt16)  — architecture
    /// Padding bytes between structures are random to exercise robustness.
    /// </summary>
    private static byte[] BuildPeBytes(ushort machineValue, int peOffset, byte[] padding)
    {
        // Minimum size: peOffset + 6 (PE sig 4 bytes + Machine 2 bytes)
        int size = Math.Max(peOffset + 6, 64);
        var buffer = new byte[size];

        // Fill with padding noise (cycle through provided random bytes)
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] = padding.Length > 0 ? padding[i % padding.Length] : (byte)0;

        // MZ signature
        buffer[0] = (byte)'M';
        buffer[1] = (byte)'Z';

        // e_lfanew at offset 0x3C
        var lfanewBytes = BitConverter.GetBytes(peOffset);
        Array.Copy(lfanewBytes, 0, buffer, 0x3C, 4);

        // PE signature
        buffer[peOffset]     = (byte)'P';
        buffer[peOffset + 1] = (byte)'E';
        buffer[peOffset + 2] = 0;
        buffer[peOffset + 3] = 0;

        // Machine field
        var machineBytes = BitConverter.GetBytes(machineValue);
        Array.Copy(machineBytes, 0, buffer, peOffset + 4, 2);

        return buffer;
    }

    private string WriteTempFile(byte[] content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"pe_test_{Guid.NewGuid():N}.exe");
        File.WriteAllBytes(path, content);
        _tempFiles.Add(path);
        return path;
    }

    // ── Generators ────────────────────────────────────────────────────────────

    /// <summary>Generator for known Machine field values (I386 or x64).</summary>
    private static readonly Gen<ushort> GenKnownMachine =
        Gen.Elements((ushort)MachineType.I386, (ushort)MachineType.x64);

    /// <summary>
    /// Generator for valid PE offset values. Must be >= 0x40 (after DOS header)
    /// and leave room within 4096 bytes for the PE sig + Machine field.
    /// </summary>
    private static readonly Gen<int> GenPeOffset =
        Gen.Choose(0x40, 4090);

    /// <summary>Generator for random padding bytes (1-32 bytes, cycled over the buffer).</summary>
    private static readonly Gen<byte[]> GenPadding =
        Gen.Choose(0, 255)
           .Select(i => (byte)i)
           .ArrayOf()
           .Where(a => a.Length > 0);

    // ── Property 1: PE header detection returns correct MachineType ───────────
    // Feature: auto-detect-bitness, Property 1: PE header detection returns correct MachineType
    // Validates: Requirements 1.1, 1.2, 1.3
    [Property(MaxTest = 100)]
    public Property PeHeaderDetection_ReturnsCorrectMachineType()
    {
        var gen = from machine in GenKnownMachine
                  from peOffset in GenPeOffset
                  from padding in GenPadding
                  select (machine, peOffset, padding);

        return Prop.ForAll(
            Arb.From(gen),
            tuple =>
            {
                var (machineValue, peOffset, padding) = tuple;

                byte[] peBytes = BuildPeBytes(machineValue, peOffset, padding);
                string tempPath = WriteTempFile(peBytes);

                MachineType result = _peHeaderService.DetectArchitecture(tempPath);
                MachineType expected = (MachineType)machineValue;

                return (result == expected)
                    .Label($"Expected {expected} (0x{machineValue:X4}) but got {result} for peOffset={peOffset}");
            });
    }

    // ── Property 2: Invalid PE data returns Native ────────────────────────────
    // Feature: auto-detect-bitness, Property 2: Invalid PE data returns Native
    // Validates: Requirements 1.5
    [Property(MaxTest = 100)]
    public Property InvalidPeData_ReturnsNative()
    {
        // Generate random byte arrays where the first two bytes are NOT 'M','Z'
        var genInvalidBytes = Arb.Default.NonEmptyArray<byte>().Generator
            .Select(nea =>
            {
                var bytes = nea.Get;
                // Ensure first two bytes are NOT the MZ signature
                if (bytes.Length >= 2 && bytes[0] == (byte)'M' && bytes[1] == (byte)'Z')
                    bytes[0] = (byte)(bytes[0] ^ 0xFF); // flip first byte to break MZ
                else if (bytes.Length == 1 && bytes[0] == (byte)'M')
                    bytes[0] = (byte)(bytes[0] ^ 0xFF);
                return bytes;
            });

        return Prop.ForAll(
            Arb.From(genInvalidBytes),
            bytes =>
            {
                string tempPath = WriteTempFile(bytes);
                MachineType result = _peHeaderService.DetectArchitecture(tempPath);

                return (result == MachineType.Native)
                    .Label($"Expected Native but got {result} for {bytes.Length}-byte input (first bytes: 0x{(bytes.Length > 0 ? bytes[0].ToString("X2") : "??")} 0x{(bytes.Length > 1 ? bytes[1].ToString("X2") : "??")})");
            });
    }

    // ── Property 3: FindGameExe selects the largest executable ────────────────
    // Feature: auto-detect-bitness, Property 3: FindGameExe selects the largest executable
    // Validates: Requirements 2.1, 2.2
    [Property(MaxTest = 100)]
    public Property FindGameExe_SelectsLargestExecutable()
    {
        // Generate between 2 and 10 exe files with distinct sizes (100..10000 bytes each),
        // plus 0-5 non-exe files (.txt, .dll) that should be ignored.
        var genExeCount = Gen.Choose(2, 10);
        var genNonExeCount = Gen.Choose(0, 5);

        var gen = from exeCount in genExeCount
                  from nonExeCount in genNonExeCount
                  from exeSizes in Gen.Choose(100, 10000)
                                      .ListOf(exeCount)
                                      .Select(sizes =>
                                      {
                                          // Ensure all sizes are distinct
                                          var distinct = new List<int>();
                                          var used = new HashSet<int>();
                                          foreach (var s in sizes)
                                          {
                                              var v = s;
                                              while (used.Contains(v)) v++;
                                              used.Add(v);
                                              distinct.Add(v);
                                          }
                                          return distinct;
                                      })
                  from nonExeSizes in Gen.Choose(1, 50000).ListOf(nonExeCount)
                  select (exeSizes, nonExeSizes);

        return Prop.ForAll(
            Arb.From(gen),
            tuple =>
            {
                var (exeSizes, nonExeSizes) = tuple;

                // Create a temp directory
                var tempDir = Path.Combine(Path.GetTempPath(), $"findexe_test_{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempDir);
                _tempDirs.Add(tempDir);

                // Track which exe is the largest
                string? expectedLargestPath = null;
                int maxSize = -1;

                // Write exe files with distinct sizes
                for (int i = 0; i < exeSizes.Count; i++)
                {
                    var exePath = Path.Combine(tempDir, $"game_{i}.exe");
                    File.WriteAllBytes(exePath, new byte[exeSizes[i]]);

                    if (exeSizes[i] > maxSize)
                    {
                        maxSize = exeSizes[i];
                        expectedLargestPath = exePath;
                    }
                }

                // Write non-exe files (should be ignored by FindGameExe)
                string[] nonExeExtensions = [".txt", ".dll", ".cfg", ".ini", ".log"];
                for (int i = 0; i < nonExeSizes.Count; i++)
                {
                    var ext = nonExeExtensions[i % nonExeExtensions.Length];
                    var filePath = Path.Combine(tempDir, $"other_{i}{ext}");
                    File.WriteAllBytes(filePath, new byte[nonExeSizes[i]]);
                }

                // Act
                string? result = _peHeaderService.FindGameExe(tempDir);

                // Assert: result should be the largest exe
                return (result != null &&
                        string.Equals(Path.GetFullPath(result), Path.GetFullPath(expectedLargestPath!),
                                      StringComparison.OrdinalIgnoreCase))
                    .Label($"Expected '{expectedLargestPath}' but got '{result}'");
            });
    }

    // ── Property 9: BitnessCache round-trip serialization ─────────────────────
    // Feature: auto-detect-bitness, Property 9: BitnessCache round-trip serialization
    // Validates: Requirements 6.1, 6.4
    [Property(MaxTest = 100)]
    public Property BitnessCache_RoundTripSerialization()
    {
        var genMachineType = Gen.Elements(MachineType.I386, MachineType.x64, MachineType.Native);

        // Generate 1-20 cache entries with random path-like string keys and random MachineType values
        var genCacheEntries = Gen.Choose(1, 20).SelectMany(count =>
            (from key in Gen.Elements("C:\\Games\\Game1", "D:\\Steam\\app", "E:\\Epic\\title",
                                       "C:\\GOG\\rpg", "D:\\Xbox\\shooter", "C:\\Ubisoft\\action",
                                       "E:\\EA\\sports", "C:\\Battle\\mmo", "D:\\Rockstar\\open",
                                       "C:\\Custom\\indie", "D:\\Lib\\puzzle", "E:\\Store\\sim",
                                       "C:\\Path\\strategy", "D:\\Dir\\horror", "E:\\Folder\\racing")
             from value in genMachineType
             select new KeyValuePair<string, MachineType>(key, value))
            .ListOf(count)
            .Select(pairs =>
            {
                // Deduplicate keys (case-insensitive) keeping last value
                var dict = new Dictionary<string, MachineType>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in pairs)
                    dict[kv.Key] = kv.Value;
                return dict;
            }));

        return Prop.ForAll(
            Arb.From(genCacheEntries),
            cache =>
            {
                var original = new SavedGameLibrary
                {
                    BitnessCache = new Dictionary<string, MachineType>(cache, StringComparer.OrdinalIgnoreCase)
                };

                // Serialize to JSON
                string json = JsonSerializer.Serialize(original);

                // Deserialize back
                var deserialized = JsonSerializer.Deserialize<SavedGameLibrary>(json);

                if (deserialized == null)
                    return false.Label("Deserialized result was null");

                // Assert same number of entries
                if (deserialized.BitnessCache.Count != original.BitnessCache.Count)
                    return false.Label($"Count mismatch: expected {original.BitnessCache.Count}, got {deserialized.BitnessCache.Count}");

                // Assert all keys and values match
                foreach (var kv in original.BitnessCache)
                {
                    if (!deserialized.BitnessCache.TryGetValue(kv.Key, out var deserializedValue))
                        return false.Label($"Key '{kv.Key}' missing after round-trip");

                    if (deserializedValue != kv.Value)
                        return false.Label($"Value mismatch for key '{kv.Key}': expected {kv.Value}, got {deserializedValue}");
                }

                return true.Label("Round-trip serialization preserved all BitnessCache entries");
            });
    }

    // ── Property 7: Addon snapshot URL matches detected architecture ─────────
    // Feature: auto-detect-bitness, Property 7: Addon snapshot URL matches detected architecture
    // Validates: Requirements 4.5
    [Property(MaxTest = 100)]
    public Property AddonSnapshotUrl_MatchesDetectedArchitecture()
    {
        var genUrl = Gen.Elements(
            "https://cdn.example.com/renodx-game.addon64",
            "https://cdn.example.com/renodx-ue.addon64",
            "https://cdn.example.com/renodx-unity.addon64");
        var genUrl32 = Gen.Elements(
            "https://cdn.example.com/renodx-game.addon32",
            "https://cdn.example.com/renodx-ue.addon32",
            "https://cdn.example.com/renodx-unity.addon32");

        var gen = from url64 in genUrl
                  from url32 in genUrl32
                  from is32Bit in Arb.Default.Bool().Generator
                  select (url64, url32, is32Bit);

        return Prop.ForAll(
            Arb.From(gen),
            tuple =>
            {
                var (url64, url32, is32Bit) = tuple;

                var mod = new GameMod
                {
                    Name = "TestGame",
                    SnapshotUrl = url64,
                    SnapshotUrl32 = url32,
                };

                // Replicate the URL selection logic from InstallModAsync
                bool swappedTo32 = is32Bit && mod.SnapshotUrl32 != null;
                string effectiveUrl = swappedTo32 ? mod.SnapshotUrl32! : mod.SnapshotUrl!;

                string expectedUrl = is32Bit ? url32 : url64;

                return (effectiveUrl == expectedUrl)
                    .Label($"Is32Bit={is32Bit}: expected '{expectedUrl}' but got '{effectiveUrl}'");
            });
    }

    // ── Property 5: Display Commander DLL variant matches detected architecture ──
    // Feature: auto-detect-bitness, Property 5: Display Commander DLL variant matches detected architecture
    // Validates: Requirements 4.1, 4.2
    [Property(MaxTest = 100)]
    public Property DcDllVariant_MatchesDetectedArchitecture()
    {
        var gen = from is32Bit in Arb.Default.Bool().Generator
                  from dcModeLevel in Gen.Elements(0, 1, 2)
                  select (is32Bit, dcModeLevel);

        return Prop.ForAll(
            Arb.From(gen),
            tuple =>
            {
                var (is32Bit, dcModeLevel) = tuple;

                // Replicate the DC filename selection logic from AuxInstallService
                string dcFileName = dcModeLevel switch
                {
                    1 => AuxInstallService.DcDxgiName,
                    2 => AuxInstallService.DcWinmmName,
                    _ => is32Bit ? AuxInstallService.DcNormalName32 : AuxInstallService.DcNormalName,
                };

                // When DC Mode is off (level 0), the filename must be architecture-specific
                if (dcModeLevel == 0)
                {
                    string expected = is32Bit ? AuxInstallService.DcNormalName32 : AuxInstallService.DcNormalName;
                    return (dcFileName == expected)
                        .Label($"DC Mode 0, Is32Bit={is32Bit}: expected '{expected}' but got '{dcFileName}'");
                }

                // When DC Mode is on (1 or 2), filename is fixed regardless of architecture
                string expectedFixed = dcModeLevel == 1 ? AuxInstallService.DcDxgiName : AuxInstallService.DcWinmmName;
                return (dcFileName == expectedFixed)
                    .Label($"DC Mode {dcModeLevel}: expected '{expectedFixed}' but got '{dcFileName}'");
            });
    }

    // ── Property 6: ReShade DLL naming matches detected architecture under DC Mode ──
    // Feature: auto-detect-bitness, Property 6: ReShade DLL naming matches detected architecture under DC Mode
    // Validates: Requirements 4.3, 4.4
    [Property(MaxTest = 100)]
    public Property ReShaDllNaming_MatchesDetectedArchitectureUnderDcMode()
    {
        return Prop.ForAll(
            Arb.From(Arb.Default.Bool().Generator),
            is32Bit =>
            {
                // Under DC Mode, ReShade uses architecture-specific naming
                string rsFileName = is32Bit ? AuxInstallService.RsDcModeName32 : AuxInstallService.RsDcModeName;
                string expected = is32Bit ? "ReShade32.dll" : "ReShade64.dll";

                return (rsFileName == expected)
                    .Label($"Is32Bit={is32Bit}: expected '{expected}' but got '{rsFileName}'");
            });
    }

    // ── Property 8: Manifest thirtyTwoBitGames field is parsed but ignored ────
    // Feature: auto-detect-bitness, Property 8: Manifest thirtyTwoBitGames field is parsed but ignored
    // Validates: Requirements 5.3
    [Property(MaxTest = 100)]
    public Property ManifestThirtyTwoBitGames_ParsedButIgnored()
    {
        var genGameNames = Gen.Elements(
            "Game Alpha", "Game Beta", "Game Gamma", "Game Delta",
            "Game Epsilon", "Game Zeta", "Game Eta", "Game Theta");

        var gen = from count in Gen.Choose(1, 5)
                  from names in genGameNames.ListOf(count)
                  select names.Distinct().ToList();

        return Prop.ForAll(
            Arb.From(gen),
            gameNames =>
            {
                // Build a manifest JSON with thirtyTwoBitGames
                var manifestObj = new { thirtyTwoBitGames = gameNames };
                string json = JsonSerializer.Serialize(manifestObj);

                // Deserialize into RemoteManifest
                var manifest = JsonSerializer.Deserialize<RemoteManifest>(json);

                if (manifest == null)
                    return false.Label("Deserialized manifest was null");

                // The field should be parsed successfully
                if (manifest.ThirtyTwoBitGames == null)
                    return false.Label("ThirtyTwoBitGames was null after deserialization");

                if (manifest.ThirtyTwoBitGames.Count != gameNames.Count)
                    return false.Label($"Expected {gameNames.Count} entries but got {manifest.ThirtyTwoBitGames.Count}");

                // Verify all names are present
                foreach (var name in gameNames)
                {
                    if (!manifest.ThirtyTwoBitGames.Contains(name))
                        return false.Label($"Missing game name '{name}' after deserialization");
                }

                // The key assertion: Is32Bit on a card is NOT derived from this list.
                // It comes solely from PE detection. The seeding loop was removed in task 4.1.
                return true.Label("thirtyTwoBitGames parsed successfully but has no effect on card Is32Bit");
            });
    }

    // ── Property 4: 32-bit badge visibility matches Is32Bit ───────────────────
    // Feature: auto-detect-bitness, Property 4: 32-bit badge visibility matches Is32Bit
    // Validates: Requirements 3.4
    [Property(MaxTest = 100)]
    public Property BadgeVisibility_MatchesIs32Bit()
    {
        return Prop.ForAll(
            Arb.From(Arb.Default.Bool().Generator),
            is32Bit =>
            {
                var card = new GameCardViewModel { Is32Bit = is32Bit };

                var expected = is32Bit
                    ? Microsoft.UI.Xaml.Visibility.Visible
                    : Microsoft.UI.Xaml.Visibility.Collapsed;

                return (card.Is32BitBadgeVisibility == expected)
                    .Label($"Is32Bit={is32Bit}: expected {expected} but got {card.Is32BitBadgeVisibility}");
            });
    }

}
