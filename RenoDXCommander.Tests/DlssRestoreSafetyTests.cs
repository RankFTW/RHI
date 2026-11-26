using RenoDXCommander.Models;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// "Restore DLSS/SL to default" only undoes a version swap: it must not delete or replace DLLs
/// another RHI component (DLSS5 Tool, Feeder, ShortFuse, RTX Encore, OptiScaler, Luma) placed or owns.
/// </summary>
public class DlssRestoreSafetyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rhi-dlssrestore-" + Guid.NewGuid().ToString("N"));

    public DlssRestoreSafetyTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* temp folder; ignore */ }
    }

    private static DlssStreamlineService NewService() => new(new HttpClient(), RenoDXCommander.Tests.TestEnvironment.NewETagCache());

    private string Put(string rel, byte[] content)
    {
        var path = Path.Combine(_dir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static readonly byte[] Rhi = { 1, 2, 3 };
    private static readonly byte[] Game = { 9, 9 };

    [Fact]
    public void ZeroByteSentinel_FileKept()
    {
        // Feeder / Luma placed nvngx_dlssnr.dll in a game that had none
        var dll = Put("nvngx_dlssnr.dll", Rhi);
        Put("nvngx_dlssnr.dll.original", Array.Empty<byte>());

        Assert.NotNull(DlssStreamlineService.RestoreBlockedReason(dll, _dir));
        Assert.False(NewService().Restore(dll, _dir));
        Assert.Equal(Rhi, File.ReadAllBytes(dll));
        Assert.True(File.Exists(dll + ".original"));
    }

    [Fact]
    public void SharedOwner_RealBackup_FileKept()
    {
        // DLSS5 Tool upgraded the game's own nvngx_dlss.dll: real backup, but the tool still needs its DLL
        var dll = Put("nvngx_dlss.dll", Rhi);
        Put("nvngx_dlss.dll.original", Game);
        RhiInstallManifest.AddSharedFileOwner(_dir, "nvngx_dlss.dll", "Dlss5Tool");

        Assert.False(NewService().Restore(dll, _dir));
        Assert.Equal(Rhi, File.ReadAllBytes(dll));
        Assert.Equal(Game, File.ReadAllBytes(dll + ".original"));
    }

    [Fact]
    public void SharedOwner_ByRelativePath_InSubfolder_FileKept()
    {
        // RTX Encore registers its NR DLL by relative path in the install folder's rhi_install.txt
        var dll = Put(Path.Combine("plugins", "nvngx_dlssnr.dll"), Rhi);
        Put(Path.Combine("plugins", "nvngx_dlssnr.dll.original"), Game);
        RhiInstallManifest.AddSharedFileOwner(_dir, @"plugins\nvngx_dlssnr.dll", "RtxEncore");

        Assert.NotNull(DlssStreamlineService.RestoreBlockedReason(dll, _dir));
        // Without the install path the owner can't be seen from the DLL's own folder
        Assert.Null(DlssStreamlineService.RestoreBlockedReason(dll, null));
    }

    [Fact]
    public void SwapBackup_NoOwner_Restored()
    {
        // A plain version swap: game's file backed up, nothing else owns it
        var dll = Put("nvngx_dlss.dll", Rhi);
        Put("nvngx_dlss.dll.original", Game);

        Assert.Null(DlssStreamlineService.RestoreBlockedReason(dll, _dir));
        Assert.True(NewService().Restore(dll, _dir));
        Assert.Equal(Game, File.ReadAllBytes(dll));
        Assert.False(File.Exists(dll + ".original"));
    }

    [Fact]
    public void UnreadableManifest_Blocks()
    {
        var dll = Put("nvngx_dlss.dll", Rhi);
        Put("nvngx_dlss.dll.original", Game);
        Put(RhiInstallManifest.FileName, System.Text.Encoding.UTF8.GetBytes("{ not json"));

        Assert.NotNull(DlssStreamlineService.RestoreBlockedReason(dll, _dir));
    }

    [Fact]
    public void RestoreStreamline_ZeroByteSentinel_NotTurnedIntoEmptyDll()
    {
        var sl = Put("sl.interposer.dll", Rhi);
        Put("sl.interposer.dll.original", Array.Empty<byte>());

        NewService().RestoreStreamline(_dir, _dir);

        Assert.Equal(Rhi, File.ReadAllBytes(sl));
        Assert.True(File.Exists(sl + ".original"));
    }

    [Fact]
    public void StreamlineZipBackup_WrittenWhole_AndKeptOnce()
    {
        Put("sl.interposer.dll", Game);
        var zip = Path.Combine(_dir, "backups", "x.zip");

        Assert.True(DlssStreamlineService.BackupStreamlineToZip(_dir, zip));
        Assert.True(File.Exists(zip));
        Assert.False(File.Exists(zip + ".tmp"));

        // A later (swapped) version doesn't replace the first backup
        Put("sl.interposer.dll", Rhi);
        Assert.True(DlssStreamlineService.BackupStreamlineToZip(_dir, zip));
        using var z = System.IO.Compression.ZipFile.OpenRead(zip);
        using var s = z.GetEntry("sl.interposer.dll")!.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        Assert.Equal(Game, ms.ToArray());
    }

    [Fact]
    public void StreamlineZipBackup_BrokenZipReplaced()
    {
        Put("sl.interposer.dll", Game);
        var zip = Put(Path.Combine("backups", "x.zip"), new byte[] { 1, 2, 3, 4 }); // half-written by an older RHI
        Assert.True(DlssStreamlineService.BackupStreamlineToZip(_dir, zip));
        using var z = System.IO.Compression.ZipFile.OpenRead(zip);
        Assert.NotNull(z.GetEntry("sl.interposer.dll"));
    }

    [Fact]
    public void StreamlineZipBackup_SourceLocked_Fails()
    {
        var sl = Put("sl.interposer.dll", Game);
        var zip = Path.Combine(_dir, "backups", "x.zip");
        using (new FileStream(sl, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.False(DlssStreamlineService.BackupStreamlineToZip(_dir, zip));
        Assert.False(File.Exists(zip));
        Assert.False(File.Exists(zip + ".tmp"));
    }

    [Fact]
    public void RootOwner_DoesNotBlock_GameDllInSubfolder()
    {
        // UE: OptiScaler in the install folder owns its root nvngx_dlss.dll; the game's own DLSS
        // in Engine\Plugins was version-swapped and "Default" must restore it.
        var inst = Path.Combine(_dir, "Game", "Binaries", "Win64");
        Directory.CreateDirectory(inst);
        RhiInstallManifest.AddSharedFileOwner(inst, "nvngx_dlss.dll", "OptiScaler");
        var dll = Put(Path.Combine("Engine", "Plugins", "DLSS", "nvngx_dlss.dll"), Rhi);
        Put(Path.Combine("Engine", "Plugins", "DLSS", "nvngx_dlss.dll.original"), Game);

        Assert.Null(DlssStreamlineService.RestoreBlockedReason(dll, inst));
        Assert.True(NewService().Restore(dll, inst));
        Assert.Equal(Game, File.ReadAllBytes(dll));

        // OptiScaler's own root copy is still blocked
        var root = Path.Combine(inst, "nvngx_dlss.dll");
        File.WriteAllBytes(root, Rhi);
        File.WriteAllBytes(root + ".original", Game);
        Assert.NotNull(DlssStreamlineService.RestoreBlockedReason(root, inst));
    }

    [Fact]
    public void DetectedPathOwner_ByBareName_StillBlocksSubfolderDll()
    {
        // DLSS5 Tool / ShortFuse deploy over the detected DLL (any subfolder) and record it by bare name
        var dll = Put(Path.Combine("Engine", "Plugins", "nvngx_dlss.dll"), Rhi);
        Put(Path.Combine("Engine", "Plugins", "nvngx_dlss.dll.original"), Game);
        RhiInstallManifest.AddSharedFileOwner(_dir, "nvngx_dlss.dll", "Dlss5Tool");

        Assert.NotNull(DlssStreamlineService.RestoreBlockedReason(dll, _dir));
    }

    [Fact]
    public void RestoreStreamline_KeptFile_OthersRestoredFromZip()
    {
        // Custom swap (no .original): only the AppData zip has the game's files. ShortFuse owns one.
        Put("sl.common.dll", Game);
        Put("sl.interposer.dll", Game);
        var zip = DlssStreamlineService.GetStreamlineAppDataBackupPath(_dir)!;
        try
        {
            if (File.Exists(zip)) File.Delete(zip);
            Assert.True(DlssStreamlineService.BackupStreamlineToZip(_dir, zip));
            var common = Put("sl.common.dll", Rhi);
            var interposer = Put("sl.interposer.dll", Rhi);
            RhiInstallManifest.SetComponent(_dir, "ShortFuse", new[] { "sl.common.dll" });

            NewService().RestoreStreamline(_dir, _dir);

            Assert.Equal(Rhi, File.ReadAllBytes(common));      // kept for ShortFuse
            Assert.Equal(Game, File.ReadAllBytes(interposer)); // restored from the zip
            Assert.True(File.Exists(zip));                     // still holds the game's sl.common.dll
        }
        finally
        {
            if (File.Exists(zip)) File.Delete(zip);
        }
    }

    [Fact]
    public void RestoreStreamline_ShortFuseFiles_Kept()
    {
        var sl = Put("sl.common.dll", Rhi);
        Put("sl.common.dll.original", Game);
        RhiInstallManifest.SetComponent(_dir, "ShortFuse", new[] { "sl.common.dll" });

        NewService().RestoreStreamline(_dir, _dir);

        Assert.Equal(Rhi, File.ReadAllBytes(sl));
    }
}
