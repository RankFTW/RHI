using System.Text;
using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Services;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for DxvkService.IsDxvkFileStatic.
/// Feature: dxvk-integration, Property 8: Binary signature detection
/// </summary>
public class DxvkBinarySignatureDetectionPropertyTests : IDisposable
{
    // ── Temp directory for test files ──────────────────────────────────────────────

    private readonly string _tempDir;

    public DxvkBinarySignatureDetectionPropertyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DxvkSigTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    // ── Signature bytes ───────────────────────────────────────────────────────────

    private static readonly byte[] DxvkLower = Encoding.ASCII.GetBytes("dxvk");
    private static readonly byte[] DxvkUpper = Encoding.ASCII.GetBytes("DXVK_");

    // ── Generators ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a random byte array of size 100–1000 that does NOT contain
    /// either "dxvk" or "DXVK_" as a substring.
    /// </summary>
    private static readonly Gen<byte[]> GenCleanBytes =
        Gen.Choose(100, 1000)
           .SelectMany(size =>
               Gen.ArrayOf(size, Gen.Choose(0, 255).Select(i => (byte)i)))
           .Select(ScrubSignatures);

    /// <summary>
    /// Picks which DXVK signature to inject: "dxvk" or "DXVK_".
    /// </summary>
    private static readonly Gen<byte[]> GenSignature =
        Gen.Elements(DxvkLower, DxvkUpper);

    /// <summary>
    /// Removes any accidental occurrences of "dxvk" or "DXVK_" from a byte array
    /// by replacing the first byte of each match with 0x00.
    /// </summary>
    private static byte[] ScrubSignatures(byte[] data)
    {
        // Scrub "dxvk" (4 bytes)
        for (int i = 0; i <= data.Length - DxvkLower.Length; i++)
        {
            if (MatchesAt(data, i, DxvkLower))
            {
                data[i] = 0x00;
            }
        }
        // Scrub "DXVK_" (5 bytes)
        for (int i = 0; i <= data.Length - DxvkUpper.Length; i++)
        {
            if (MatchesAt(data, i, DxvkUpper))
            {
                data[i] = 0x00;
            }
        }
        return data;
    }

    private static bool MatchesAt(byte[] data, int offset, byte[] pattern)
    {
        for (int j = 0; j < pattern.Length; j++)
        {
            if (data[offset + j] != pattern[j])
                return false;
        }
        return true;
    }

    // ── Helper ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes a byte array to a uniquely-named temp file and returns the path.
    /// </summary>
    private string WriteTempFile(byte[] content)
    {
        var filePath = Path.Combine(_tempDir, Guid.NewGuid().ToString("N") + ".dll");
        File.WriteAllBytes(filePath, content);
        return filePath;
    }

    // ── Property 8: Binary signature detection ────────────────────────────────────
    // Feature: dxvk-integration, Property 8: Binary signature detection
    // **Validates: Requirements 8.1, 8.2**

    /// <summary>
    /// For any byte array that contains a DXVK signature ("dxvk" or "DXVK_")
    /// injected at a random position, IsDxvkFileStatic SHALL return true.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Files_With_Dxvk_Signature_Are_Detected()
    {
        var genPositiveCase = GenCleanBytes.SelectMany(cleanBytes =>
            GenSignature.SelectMany(signature =>
            {
                // Pick a valid injection position
                int maxPos = cleanBytes.Length - signature.Length;
                return Gen.Choose(0, Math.Max(0, maxPos)).Select(pos =>
                {
                    // Clone and inject signature
                    var data = (byte[])cleanBytes.Clone();
                    Buffer.BlockCopy(signature, 0, data, pos, signature.Length);
                    return (data, signature, pos);
                });
            }));

        return Prop.ForAll(
            Arb.From(genPositiveCase),
            ((byte[] data, byte[] signature, int pos) testCase) =>
            {
                var filePath = WriteTempFile(testCase.data);
                try
                {
                    var result = DxvkService.IsDxvkFileStatic(filePath);
                    var sigName = testCase.signature.Length == 4 ? "dxvk" : "DXVK_";
                    return result
                        .Label($"Expected true for file with '{sigName}' at offset {testCase.pos} " +
                               $"(fileSize={testCase.data.Length}), got false");
                }
                finally
                {
                    try { File.Delete(filePath); } catch { }
                }
            });
    }

    /// <summary>
    /// For any byte array that does NOT contain "dxvk" or "DXVK_",
    /// IsDxvkFileStatic SHALL return false.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Files_Without_Dxvk_Signature_Are_Not_Detected()
    {
        return Prop.ForAll(
            Arb.From(GenCleanBytes),
            (byte[] cleanData) =>
            {
                var filePath = WriteTempFile(cleanData);
                try
                {
                    var result = DxvkService.IsDxvkFileStatic(filePath);
                    return (!result)
                        .Label($"Expected false for file without DXVK signatures " +
                               $"(fileSize={cleanData.Length}), got true");
                }
                finally
                {
                    try { File.Delete(filePath); } catch { }
                }
            });
    }
}
