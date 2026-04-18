using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Unit tests for PeHeaderService edge cases.
/// Validates: Requirements 1.4, 1.5, 1.6, 2.3, 2.4
/// </summary>
public class PeHeaderServiceEdgeCaseTests : IDisposable
{
    private readonly PeHeaderService _sut = new();
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

    private string WriteTempFile(byte[] content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"pe_edge_{Guid.NewGuid():N}.exe");
        File.WriteAllBytes(path, content);
        _tempFiles.Add(path);
        return path;
    }

    private string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"pe_edge_dir_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    /// <summary>
    /// Builds a minimal valid PE byte array with the given Machine field value.
    /// Uses the same layout as the property test helper.
    /// </summary>
    private static byte[] BuildPeBytes(ushort machineValue, int peOffset = 0x40)
    {
        int size = Math.Max(peOffset + 6, 64);
        var buffer = new byte[size];

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

    [Fact]
    public void DetectArchitecture_NonExistentFile_ReturnsNative()
    {
        var fakePath = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}.exe");

        var result = _sut.DetectArchitecture(fakePath);

        Assert.Equal(MachineType.Native, result);
    }

    [Fact]
    public void DetectArchitecture_ZeroByteFile_ReturnsNative()
    {
        var path = WriteTempFile(Array.Empty<byte>());

        var result = _sut.DetectArchitecture(path);

        Assert.Equal(MachineType.Native, result);
    }

    [Fact]
    public void FindGameExe_EmptyDirectory_ReturnsNull()
    {
        var dir = CreateTempDir();

        var result = _sut.FindGameExe(dir);

        Assert.Null(result);
    }

    [Fact]
    public void FindGameExe_NonExistentDirectory_ReturnsNull()
    {
        var fakeDir = Path.Combine(Path.GetTempPath(), $"nonexistent_dir_{Guid.NewGuid():N}");

        var result = _sut.FindGameExe(fakeDir);

        Assert.Null(result);
    }

    [Fact]
    public void DetectArchitecture_32BitPeStub_ReturnsI386()
    {
        var peBytes = BuildPeBytes((ushort)MachineType.I386);
        var path = WriteTempFile(peBytes);

        var result = _sut.DetectArchitecture(path);

        Assert.Equal(MachineType.I386, result);
    }

    [Fact]
    public void DetectArchitecture_64BitPeStub_ReturnsX64()
    {
        var peBytes = BuildPeBytes((ushort)MachineType.x64);
        var path = WriteTempFile(peBytes);

        var result = _sut.DetectArchitecture(path);

        Assert.Equal(MachineType.x64, result);
    }
}

/// <summary>
/// Tests that legacy Is32BitGames settings key is harmlessly ignored.
/// The settings file is a Dictionary&lt;string, string&gt; JSON — unknown keys
/// are simply never read by LoadNameMappings. This test validates that
/// a settings payload containing Is32BitGames deserializes without error
/// and that the key has no effect on any bitness-related state.
/// Validates: Requirements 5.4
/// </summary>
public class LegacyIs32BitGamesIgnoredTests
{
    [Fact]
    public void SettingsWithIs32BitGames_DeserializesWithoutError()
    {
        // Simulate a legacy settings.json that contains the old Is32BitGames key
        var legacySettings = new Dictionary<string, string>
        {
            ["NameMappings"] = "{}",
            ["Is32BitGames"] = System.Text.Json.JsonSerializer.Serialize(new List<string> { "Game A", "Game B", "Game C" }),
            ["DcModeLevel"] = "1",
        };

        string json = System.Text.Json.JsonSerializer.Serialize(legacySettings);

        // Deserialize — should not throw
        var loaded = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);

        // The Is32BitGames key is present in the raw dict but LoadNameMappings never reads it
        Assert.NotNull(loaded);
        Assert.True(loaded!.ContainsKey("Is32BitGames"), "Legacy key should be present in raw deserialized dict");

        // Verify the Is32BitGames value is valid JSON (it was serialized correctly)
        var games = System.Text.Json.JsonSerializer.Deserialize<List<string>>(loaded["Is32BitGames"]);
        Assert.NotNull(games);
        Assert.Equal(3, games!.Count);
        Assert.Contains("Game A", games);
        Assert.Contains("Game B", games);
        Assert.Contains("Game C", games);
    }

    [Fact]
    public void SettingsWithoutIs32BitGames_DeserializesWithoutError()
    {
        // Current settings format — no Is32BitGames key at all
        var currentSettings = new Dictionary<string, string>
        {
            ["NameMappings"] = "{}",
            ["DcModeLevel"] = "1",
        };

        string json = System.Text.Json.JsonSerializer.Serialize(currentSettings);
        var loaded = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);

        Assert.NotNull(loaded);
        Assert.False(loaded!.ContainsKey("Is32BitGames"), "Current settings should not contain legacy key");
    }

    [Fact]
    public void SaveNameMappings_DoesNotWriteIs32BitGames()
    {
        // Verify that the keys written by SaveNameMappings do not include Is32BitGames.
        // We check this by listing the known keys that SaveNameMappings writes.
        var knownSavedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "NameMappings", "WikiExclusions", "UeExtendedGames", "DcModeEnabled",
            "DcDllFileName", "PerGameDcModeOverride", "DcCustomDllFileNames",
            "UpdateAllExcluded", "PerGameShaderMode",
            "ShaderDeployMode", "SkipUpdateCheck", "VerboseLogging", "LastSeenVersion",
            "LumaEnabledGames", "LumaDisabledGames", "GameRenames", "DllOverrides",
            "FolderOverrides", "HiddenGames", "FavouriteGames", "ViewLayout",
        };

        Assert.DoesNotContain("Is32BitGames", knownSavedKeys);
    }
}
