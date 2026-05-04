using System.Text;
using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Services;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for the backup skip logic in DxvkService.BackupIfNeeded.
/// Feature: dxvk-integration, Property 12: Backup skip for managed DLLs
///
/// BackupIfNeeded is a private method that skips backup when the existing file is
/// identified as an RHI-managed DLL (DXVK, OptiScaler, or ReShade). This test
/// validates the detection logic that drives the backup decision by creating temp
/// files with and without RHI signatures and verifying the correct skip/backup
/// classification.
/// </summary>
public class DxvkBackupSkipForManagedDllsPropertyTests : IDisposable
{
    // ── Temp directory for test files ──────────────────────────────────────────

    private readonly string _tempDir;

    public DxvkBackupSkipForManagedDllsPropertyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DxvkBackupSkipTests_" + Guid.NewGuid().ToString("N"));
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

    // ── Signature bytes ───────────────────────────────────────────────────────

    private static readonly byte[] DxvkLower = Encoding.ASCII.GetBytes("dxvk");
    private static readonly byte[] DxvkUpper = Encoding.ASCII.GetBytes("DXVK_");
    private static readonly byte[] OptiScalerSig = Encoding.ASCII.GetBytes("OptiScaler");
    // ReShade requires "ReShade" (exact case) AND ("reshade.me" OR "crosire")
    private static readonly byte[] ReShadeMarker = Encoding.ASCII.GetBytes("ReShade");
    private static readonly byte[] ReshadeMeMarker = Encoding.ASCII.GetBytes("reshade.me");
    private static readonly byte[] CrosireMarker = Encoding.ASCII.GetBytes("crosire");

    // All known RHI signature patterns to scrub from "clean" byte arrays
    private static readonly byte[][] AllSignatures =
    [
        DxvkLower, DxvkUpper, OptiScalerSig,
        ReShadeMarker, ReshadeMeMarker, CrosireMarker
    ];

    // ── Generators ────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a random byte array of size 200–2000 that does NOT contain
    /// any RHI-managed DLL signatures.
    /// </summary>
    private static readonly Gen<byte[]> GenCleanBytes =
        Gen.Choose(200, 2000)
           .SelectMany(size =>
               Gen.ArrayOf(size, Gen.Choose(0, 255).Select(i => (byte)i)))
           .Select(ScrubAllSignatures);

    /// <summary>
    /// Picks which DXVK signature to inject: "dxvk" or "DXVK_".
    /// </summary>
    private static readonly Gen<byte[]> GenDxvkSignature =
        Gen.Elements(DxvkLower, DxvkUpper);

    /// <summary>
    /// Removes any accidental occurrences of all RHI signatures from a byte array
    /// by replacing the first byte of each match with 0x00.
    /// </summary>
    private static byte[] ScrubAllSignatures(byte[] data)
    {
        foreach (var sig in AllSignatures)
        {
            for (int i = 0; i <= data.Length - sig.Length; i++)
            {
                if (MatchesAt(data, i, sig))
                {
                    data[i] = 0x00;
                }
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

    /// <summary>
    /// Injects a byte pattern at a random valid position in a cloned byte array.
    /// </summary>
    private static Gen<byte[]> InjectSignature(Gen<byte[]> baseGen, byte[] signature)
    {
        return baseGen.SelectMany(cleanBytes =>
        {
            int maxPos = cleanBytes.Length - signature.Length;
            return Gen.Choose(0, Math.Max(0, maxPos)).Select(pos =>
            {
                var data = (byte[])cleanBytes.Clone();
                Buffer.BlockCopy(signature, 0, data, pos, signature.Length);
                return data;
            });
        });
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes a byte array to a uniquely-named temp file and returns the path.
    /// </summary>
    private string WriteTempFile(byte[] content)
    {
        var filePath = Path.Combine(_tempDir, Guid.NewGuid().ToString("N") + ".dll");
        File.WriteAllBytes(filePath, content);
        return filePath;
    }

    /// <summary>
    /// Simulates the BackupIfNeeded skip logic: returns true if the file would
    /// be SKIPPED (no backup created) because it's an RHI-managed DLL.
    /// This mirrors the three checks in BackupIfNeeded:
    ///   if (IsDxvkFile(destPath)) return;
    ///   if (_optiScalerService.IsOptiScalerFile(destPath)) return;
    ///   if (AuxInstallService.IsReShadeFile(destPath)) return;
    /// </summary>
    private static bool WouldSkipBackup(string filePath)
    {
        if (DxvkService.IsDxvkFileStatic(filePath)) return true;
        if (OptiScalerService.IsOptiScalerFileStatic(filePath)) return true;
        if (AuxInstallService.IsReShadeFile(filePath)) return true;
        return false;
    }

    // ── Property 12: Backup skip for managed DLLs ─────────────────────────────
    // Feature: dxvk-integration, Property 12: Backup skip for managed DLLs
    // **Validates: Requirements 3.7**

    /// <summary>
    /// For any DLL file containing a DXVK signature ("dxvk" or "DXVK_"),
    /// the backup logic SHALL skip backup (file is RHI-managed).
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Dxvk_Managed_Files_Are_Skipped_For_Backup()
    {
        var gen = GenDxvkSignature.SelectMany(sig => InjectSignature(GenCleanBytes, sig)
            .Select(data => (data, sig)));

        return Prop.ForAll(
            Arb.From(gen),
            ((byte[] data, byte[] sig) testCase) =>
            {
                var filePath = WriteTempFile(testCase.data);
                try
                {
                    var skipped = WouldSkipBackup(filePath);
                    var sigName = testCase.sig.Length == 4 ? "dxvk" : "DXVK_";
                    return skipped
                        .Label($"Expected backup skip for DXVK file with '{sigName}' signature " +
                               $"(fileSize={testCase.data.Length}), but WouldSkipBackup returned false");
                }
                finally
                {
                    try { File.Delete(filePath); } catch { }
                }
            });
    }

    /// <summary>
    /// For any DLL file containing an OptiScaler signature ("OptiScaler"),
    /// the backup logic SHALL skip backup (file is RHI-managed).
    /// </summary>
    [Property(MaxTest = 100)]
    public Property OptiScaler_Managed_Files_Are_Skipped_For_Backup()
    {
        var gen = InjectSignature(GenCleanBytes, OptiScalerSig);

        return Prop.ForAll(
            Arb.From(gen),
            (byte[] data) =>
            {
                var filePath = WriteTempFile(data);
                try
                {
                    var skipped = WouldSkipBackup(filePath);
                    return skipped
                        .Label($"Expected backup skip for OptiScaler file " +
                               $"(fileSize={data.Length}), but WouldSkipBackup returned false");
                }
                finally
                {
                    try { File.Delete(filePath); } catch { }
                }
            });
    }

    /// <summary>
    /// For any DLL file containing ReShade signatures ("ReShade" + "reshade.me"),
    /// the backup logic SHALL skip backup (file is RHI-managed).
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ReShade_Managed_Files_Are_Skipped_For_Backup()
    {
        // ReShade detection requires "ReShade" AND ("reshade.me" OR "crosire")
        // We inject both "ReShade" and "reshade.me" to ensure detection
        var gen = GenCleanBytes.SelectMany(cleanBytes =>
        {
            // Need room for both markers
            int minSize = ReShadeMarker.Length + ReshadeMeMarker.Length + 10;
            if (cleanBytes.Length < minSize)
                return Gen.Constant(cleanBytes); // too small, will be filtered

            int maxPos1 = cleanBytes.Length / 2 - ReShadeMarker.Length;
            int minPos2 = cleanBytes.Length / 2;
            int maxPos2 = cleanBytes.Length - ReshadeMeMarker.Length;

            return Gen.Choose(0, Math.Max(0, maxPos1)).SelectMany(pos1 =>
                Gen.Choose(minPos2, Math.Max(minPos2, maxPos2)).Select(pos2 =>
                {
                    var data = (byte[])cleanBytes.Clone();
                    Buffer.BlockCopy(ReShadeMarker, 0, data, pos1, ReShadeMarker.Length);
                    Buffer.BlockCopy(ReshadeMeMarker, 0, data, pos2, ReshadeMeMarker.Length);
                    return data;
                }));
        });

        return Prop.ForAll(
            Arb.From(gen),
            (byte[] data) =>
            {
                var filePath = WriteTempFile(data);
                try
                {
                    var skipped = WouldSkipBackup(filePath);
                    return skipped
                        .Label($"Expected backup skip for ReShade file " +
                               $"(fileSize={data.Length}), but WouldSkipBackup returned false");
                }
                finally
                {
                    try { File.Delete(filePath); } catch { }
                }
            });
    }

    /// <summary>
    /// For any DLL file that does NOT contain any RHI-managed signatures
    /// (no DXVK, no OptiScaler, no ReShade markers), the backup logic SHALL
    /// create a backup (file is NOT skipped).
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Non_Managed_Files_Are_Backed_Up()
    {
        return Prop.ForAll(
            Arb.From(GenCleanBytes),
            (byte[] cleanData) =>
            {
                var filePath = WriteTempFile(cleanData);
                try
                {
                    var skipped = WouldSkipBackup(filePath);
                    return (!skipped)
                        .Label($"Expected backup creation for non-managed file " +
                               $"(fileSize={cleanData.Length}), but WouldSkipBackup returned true");
                }
                finally
                {
                    try { File.Delete(filePath); } catch { }
                }
            });
    }
}
