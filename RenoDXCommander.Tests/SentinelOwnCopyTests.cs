using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Updating / reinstalling a mod over RHI's own copy with no .original (installs from before RHI wrote
/// sentinels) must not save the mod as the game's "original"; 20/30 FG's automatic proxy switch must
/// not take a name another file uses.
/// </summary>
public class SentinelOwnCopyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rhi-ownsentinel-" + Guid.NewGuid().ToString("N"));

    public SentinelOwnCopyTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* temp folder; ignore */ }
    }

    private string Put(string name, byte[] content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static readonly byte[] Mod = { 1, 2, 3 };
    private static readonly byte[] Game = { 9, 9 };

    [Fact]
    public void OwnCopy_NoSentinel_WritesZeroByteSentinel()
    {
        var dll = Put("version.dll", Mod);
        AuxInstallService.SentinelBackupReplacingOwn(dll, existingIsRhiCopy: true);
        Assert.True(File.Exists(dll));
        Assert.Empty(File.ReadAllBytes(dll + ".original"));
    }

    [Fact]
    public void OwnCopy_RealBackupKept()
    {
        var dll = Put("version.dll", Mod);
        Put("version.dll.original", Game);
        AuxInstallService.SentinelBackupReplacingOwn(dll, existingIsRhiCopy: true);
        Assert.Equal(Game, File.ReadAllBytes(dll + ".original"));
        Assert.Equal(Mod, File.ReadAllBytes(dll));
    }

    [Fact]
    public void ForeignFile_MovedToOriginal()
    {
        var dll = Put("version.dll", Game);
        AuxInstallService.SentinelBackupReplacingOwn(dll, existingIsRhiCopy: false);
        Assert.False(File.Exists(dll));
        Assert.Equal(Game, File.ReadAllBytes(dll + ".original"));
    }

    [Fact]
    public void NoFile_WritesSentinel()
    {
        var dll = Path.Combine(_dir, "version.dll");
        AuxInstallService.SentinelBackupReplacingOwn(dll, existingIsRhiCopy: true);
        Assert.Empty(File.ReadAllBytes(dll + ".original"));
    }

    [Fact]
    public void SameProductName_NoVersionInfo_False()
    {
        var a = Put("a.dll", Mod);
        var b = Put("b.dll", Mod);
        Assert.False(AuxInstallService.SameProductName(a, b));
    }

    [Fact]
    public void Dlssg2030_PickFreeProxyName_SkipsUsedNames()
    {
        Put("version.dll", Game);                   // e.g. ReShade or ASI Loader
        Put("winmm.dll.original", Array.Empty<byte>()); // name RHI handed to another component
        Assert.Equal("dbghelp.dll", Dlssg20_30Service.PickFreeProxyName(_dir));
    }

    [Fact]
    public void Dlssg2030_PickFreeProxyName_NoneFree_Null()
    {
        foreach (var n in Dlssg20_30Service.AutoProxyNames) Put(n, Game);
        Assert.Null(Dlssg20_30Service.PickFreeProxyName(_dir));
    }
}
