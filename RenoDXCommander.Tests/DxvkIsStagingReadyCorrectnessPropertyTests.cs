using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Services;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for DxvkService.IsStagingReady correctness.
/// Feature: dxvk-integration, Property 10: IsStagingReady correctness
/// </summary>
public class DxvkIsStagingReadyCorrectnessPropertyTests : IDisposable
{
    private readonly string _tempRoot;

    public DxvkIsStagingReadyCorrectnessPropertyTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"RHI_test_staging_{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch { /* best-effort cleanup */ }
    }

    // ── Property 10: IsStagingReady correctness ───────────────────────────────
    // Feature: dxvk-integration, Property 10: IsStagingReady correctness
    // **Validates: Requirements 1.4**

    /// <summary>
    /// For any combination of (staging directory exists, version.txt exists,
    /// d3d9.dll exists in x64/), CheckStagingReady returns true if and only
    /// if all three conditions are true simultaneously.
    ///
    /// Note: On a real filesystem, files cannot exist without their parent
    /// directory. When dirExists is false, the effective file states are also
    /// false regardless of the input booleans. The property holds over the
    /// actual filesystem state.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property CheckStagingReady_Returns_True_Only_When_All_Three_Conditions_Met()
    {
        return Prop.ForAll(
            Arb.From<bool>(),
            Arb.From<bool>(),
            Arb.From<bool>(),
            (bool dirExists, bool versionExists, bool dllExists) =>
            {
                // Create a unique subdirectory per test iteration to avoid interference
                var iterDir = Path.Combine(_tempRoot, Guid.NewGuid().ToString("N"));
                var stagingDir = Path.Combine(iterDir, "dxvk");
                var versionFilePath = Path.Combine(stagingDir, "version.txt");
                var dllCheckPath = Path.Combine(stagingDir, "x64", "d3d9.dll");

                try
                {
                    // Set up filesystem according to the boolean triple.
                    // Files can only exist when the directory exists, so when
                    // dirExists is false we skip all file creation.
                    if (dirExists)
                    {
                        Directory.CreateDirectory(stagingDir);

                        if (versionExists)
                        {
                            File.WriteAllText(versionFilePath, "v2.7.1");
                        }

                        if (dllExists)
                        {
                            Directory.CreateDirectory(Path.Combine(stagingDir, "x64"));
                            File.WriteAllBytes(dllCheckPath, new byte[] { 0x4D, 0x5A });
                        }
                    }

                    // Determine the actual filesystem state
                    bool actualDirExists = Directory.Exists(stagingDir);
                    bool actualVersionExists = File.Exists(versionFilePath);
                    bool actualDllExists = File.Exists(dllCheckPath);

                    // Act
                    var result = DxvkService.CheckStagingReady(stagingDir, versionFilePath, dllCheckPath);

                    // The expected result: true only when ALL three actual conditions are true
                    var expected = actualDirExists && actualVersionExists && actualDllExists;

                    return (result == expected)
                        .Label($"dirExists={dirExists}, versionExists={versionExists}, dllExists={dllExists} " +
                               $"(actual: dir={actualDirExists}, ver={actualVersionExists}, dll={actualDllExists}): " +
                               $"expected={expected}, got={result}");
                }
                finally
                {
                    // Clean up this iteration's directory
                    try
                    {
                        if (Directory.Exists(iterDir))
                            Directory.Delete(iterDir, recursive: true);
                    }
                    catch { /* best-effort cleanup */ }
                }
            });
    }
}
