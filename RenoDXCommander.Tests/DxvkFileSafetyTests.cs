using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// DXVK's DX9 direct-mode install must keep the right backup of whatever was at d3d9.dll.
/// </summary>
public class DxvkFileSafetyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rhi-dxvksafety-" + Guid.NewGuid().ToString("N"));
    private string D3d9 => Path.Combine(_dir, "d3d9.dll");

    public DxvkFileSafetyTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* temp folder; ignore */ }
    }

    private static readonly byte[] Dxvk = { 0xD1 };
    private static readonly byte[] ReShade = { 0x5E, 0x5E };
    private static readonly byte[] Game = { 0x9A, 0x9A, 0x9A };

    private static bool IsDxvk(string p) => File.ReadAllBytes(p).SequenceEqual(Dxvk);
    private static bool IsReShade(string p) => File.ReadAllBytes(p).SequenceEqual(ReShade);

    [Fact]
    public void Reinstall_OverDxvk_KeepsReShadeBackup()
    {
        File.WriteAllBytes(D3d9, Dxvk);
        File.WriteAllBytes(D3d9 + ".reshade_backup", ReShade);

        Assert.Null(DxvkService.PrepareDx9Target(D3d9, IsDxvk, IsReShade, out var foreign));
        Assert.False(foreign);
        Assert.Equal(ReShade, File.ReadAllBytes(D3d9 + ".reshade_backup"));
    }

    [Fact]
    public void ReShade_MovedToReShadeBackup()
    {
        File.WriteAllBytes(D3d9, ReShade);

        Assert.Null(DxvkService.PrepareDx9Target(D3d9, IsDxvk, IsReShade, out var foreign));
        Assert.False(foreign);
        Assert.False(File.Exists(D3d9));
        Assert.Equal(ReShade, File.ReadAllBytes(D3d9 + ".reshade_backup"));
    }

    [Fact]
    public void ReShade_ExistingBackupNeverReplaced()
    {
        var older = new byte[] { 0x5E };
        File.WriteAllBytes(D3d9, ReShade);
        File.WriteAllBytes(D3d9 + ".reshade_backup", older);

        Assert.Null(DxvkService.PrepareDx9Target(D3d9, IsDxvk, IsReShade, out _));
        Assert.Equal(older, File.ReadAllBytes(D3d9 + ".reshade_backup"));
    }

    [Fact]
    public void ForeignFile_GoesToOriginal_NotReShadeBackup()
    {
        File.WriteAllBytes(D3d9, Game);

        Assert.Null(DxvkService.PrepareDx9Target(D3d9, IsDxvk, IsReShade, out var foreign));
        Assert.True(foreign);
        Assert.False(File.Exists(D3d9 + ".reshade_backup"));
        Assert.Equal(Game, File.ReadAllBytes(D3d9 + ".original"));
    }

    [Theory]
    [InlineData(true)]   // a real backup another component owns
    [InlineData(false)]  // a 0-byte sentinel (dgVoodoo placed d3d9.dll)
    public void ForeignFile_OriginalSlotTaken_Refused(bool realBackup)
    {
        File.WriteAllBytes(D3d9, Game);
        File.WriteAllBytes(D3d9 + ".original", realBackup ? ReShade : Array.Empty<byte>());

        Assert.NotNull(DxvkService.PrepareDx9Target(D3d9, IsDxvk, IsReShade, out var foreign));
        Assert.False(foreign);
        Assert.Equal(Game, File.ReadAllBytes(D3d9));
    }

    [Fact]
    public void UninstallPreflight_LockedDll_Blocks()
    {
        File.WriteAllBytes(D3d9, Dxvk);
        File.WriteAllBytes(D3d9 + ".reshade_backup", ReShade);
        var record = new RenoDXCommander.Models.DxvkInstalledRecord { InstalledDlls = new List<string> { "d3d9.dll" } };

        Assert.Null(DxvkService.UninstallPreflight(_dir, record));
        using (new FileStream(D3d9, FileMode.Open, FileAccess.Read, FileShare.Read)) // game running
            Assert.NotNull(DxvkService.UninstallPreflight(_dir, record));
    }

    [Fact]
    public void UninstallPreflight_UnsafeRecordPath_Ignored()
    {
        var record = new RenoDXCommander.Models.DxvkInstalledRecord { InstalledDlls = new List<string> { @"..\..\evil.dll" } };
        Assert.Null(DxvkService.UninstallPreflight(_dir, record));
    }

    [Fact]
    public void Reinstall_KeepsEarlierBackups()
    {
        // First install backed up the game's own d3d11.dll; the reinstall finds DXVK there and backs up nothing
        var merged = DxvkService.MergeBackedUpFiles(new List<string>(), new[] { "d3d11.dll" });
        Assert.Equal(new[] { "d3d11.dll" }, merged);

        merged = DxvkService.MergeBackedUpFiles(new List<string> { "D3D11.dll" }, new[] { "d3d11.dll", "dxgi.dll" });
        Assert.Equal(2, merged.Count);
        Assert.Empty(DxvkService.MergeBackedUpFiles(new List<string>(), null));
    }

    [Fact]
    public void Absent_NothingToDo()
    {
        Assert.Null(DxvkService.PrepareDx9Target(D3d9, IsDxvk, IsReShade, out var foreign));
        Assert.False(foreign);
    }

    [Fact]
    public void ForeignDxvk_NotInRecord_BackedUpAsOriginal()
    {
        // A hand-installed / other mod's DXVK: RHI's record doesn't list d3d9.dll, so it's a foreign file
        File.WriteAllBytes(D3d9, Dxvk);
        bool owns = DxvkService.RecordOwnsD3d9(null);
        Assert.Null(DxvkService.PrepareDx9Target(D3d9, p => owns && IsDxvk(p), IsReShade, out var foreign));
        Assert.True(foreign);
        Assert.False(File.Exists(D3d9));
        Assert.Equal(Dxvk, File.ReadAllBytes(D3d9 + ".original"));
    }

    [Fact]
    public void RecordOwnsD3d9_OnlyWhenListed()
    {
        Assert.False(DxvkService.RecordOwnsD3d9(null));
        Assert.False(DxvkService.RecordOwnsD3d9(new RenoDXCommander.Models.DxvkInstalledRecord { InstalledDlls = new List<string> { "dxgi.dll" } }));
        Assert.True(DxvkService.RecordOwnsD3d9(new RenoDXCommander.Models.DxvkInstalledRecord { InstalledDlls = new List<string> { "D3D9.dll" } }));
    }

    [Fact]
    public void FailedInstall_PutsGamesFileBack()
    {
        File.WriteAllBytes(D3d9, Game);
        Assert.Null(DxvkService.PrepareDx9Target(D3d9, _ => false, IsReShade, out var foreign));
        Assert.True(foreign);
        File.WriteAllBytes(D3d9, Dxvk); // the copy happened, a later step threw
        DxvkService.UndoDx9Target(D3d9, foreign, reshadeMovedAside: false);
        Assert.Equal(Game, File.ReadAllBytes(D3d9));
        Assert.False(File.Exists(D3d9 + ".original"));
    }

    [Fact]
    public void FailedInstall_PutsReShadeBack()
    {
        File.WriteAllBytes(D3d9, ReShade);
        Assert.Null(DxvkService.PrepareDx9Target(D3d9, IsDxvk, IsReShade, out var foreign));
        Assert.False(foreign);
        // the copy failed before anything was written
        DxvkService.UndoDx9Target(D3d9, foreign, reshadeMovedAside: true);
        Assert.Equal(ReShade, File.ReadAllBytes(D3d9));
        Assert.False(File.Exists(D3d9 + ".reshade_backup"));
    }

    [Fact]
    public void FailedInstall_NothingMoved_NothingChanged()
    {
        File.WriteAllBytes(D3d9 + ".original", Game); // an earlier install's backup
        File.WriteAllBytes(D3d9, Dxvk);
        DxvkService.UndoDx9Target(D3d9, foreignBackedUp: false, reshadeMovedAside: false);
        Assert.Equal(Dxvk, File.ReadAllBytes(D3d9));
        Assert.Equal(Game, File.ReadAllBytes(D3d9 + ".original"));
    }

    // ── OptiScaler coexistence copy: OptiScaler\plugins (10 builds) or <game>\plugins (0.9) ──

    private static readonly byte[] DxvkSig = System.Text.Encoding.ASCII.GetBytes("xx dxvk xx");
    private string RootPlugins => Path.Combine(_dir, "plugins");
    private string OsPlugins => Path.Combine(_dir, "OptiScaler", "plugins");

    [Fact]
    public void FindPluginCopy_Os09_RootPluginsDxvk()
    {
        Directory.CreateDirectory(RootPlugins);
        File.WriteAllBytes(Path.Combine(RootPlugins, "dxgi.dll"), DxvkSig);
        Assert.Equal(Path.Combine(RootPlugins, "dxgi.dll"), DxvkService.FindPluginCopy(_dir, "dxgi.dll"));
    }

    [Fact]
    public void FindPluginCopy_RootPluginsGameFile_NotDxvks()
    {
        // The root plugins folder can be the game's: a same-name file there that isn't DXVK isn't DXVK's copy
        Directory.CreateDirectory(RootPlugins);
        File.WriteAllBytes(Path.Combine(RootPlugins, "dxgi.dll"), Game);
        Assert.Null(DxvkService.FindPluginCopy(_dir, "dxgi.dll"));
        Assert.False(DxvkService.CanWritePluginCopy(Path.Combine(RootPlugins, "dxgi.dll")));
        Assert.True(DxvkService.CanWritePluginCopy(Path.Combine(RootPlugins, "d3d11.dll")));
    }

    [Fact]
    public void FindPluginCopy_Os10_OptiScalerPlugins()
    {
        Directory.CreateDirectory(OsPlugins);
        File.WriteAllBytes(Path.Combine(OsPlugins, "dxgi.dll"), DxvkSig);
        Assert.Equal(Path.Combine(OsPlugins, "dxgi.dll"), DxvkService.FindPluginCopy(_dir, "dxgi.dll"));
    }

    [Theory]
    [InlineData(true)]   // OptiScaler 10 layout: OptiScaler\plugins first
    [InlineData(false)]  // 0.9 layout: <game>\plugins first
    public void PluginCopyDirs_InstalledBuildFirst(bool os10)
    {
        if (os10) Directory.CreateDirectory(Path.Combine(_dir, "OptiScaler"));
        var dirs = DxvkService.PluginCopyDirs(_dir);
        Assert.Equal(os10 ? OsPlugins : RootPlugins, dirs[0]);
        Assert.Equal(os10 ? RootPlugins : OsPlugins, dirs[1]);
    }

    [Fact]
    public void FindPluginCopy_LeftInOtherFolder_StillFound()
    {
        // An OptiScaler version change (or an older RHI) left the copy in the other build's folder
        Directory.CreateDirectory(Path.Combine(_dir, "OptiScaler"));
        Directory.CreateDirectory(RootPlugins);
        File.WriteAllBytes(Path.Combine(RootPlugins, "dxgi.dll"), DxvkSig);
        Assert.Equal(Path.Combine(RootPlugins, "dxgi.dll"), DxvkService.FindPluginCopy(_dir, "dxgi.dll"));
    }

    [Fact]
    public void UninstallPreflight_LockedRootPluginsCopy_Blocks()
    {
        Directory.CreateDirectory(RootPlugins);
        var copy = Path.Combine(RootPlugins, "dxgi.dll");
        File.WriteAllBytes(copy, DxvkSig);
        var record = new RenoDXCommander.Models.DxvkInstalledRecord
        {
            InstalledDlls = new List<string> { "d3d11.dll" },
            PluginFolderDlls = new List<string> { "dxgi.dll" },
        };
        Assert.Null(DxvkService.UninstallPreflight(_dir, record));
        using (new FileStream(copy, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.NotNull(DxvkService.UninstallPreflight(_dir, record));
    }

    [Fact]
    public void RemoveEmptyRootPlugins_OnlyEmptyRootFolder()
    {
        Directory.CreateDirectory(RootPlugins);
        File.WriteAllBytes(Path.Combine(RootPlugins, "cet.asi"), Game);
        DxvkService.RemoveEmptyRootPlugins(_dir, Path.Combine(RootPlugins, "dxgi.dll"));
        Assert.True(Directory.Exists(RootPlugins)); // still holds the game's file

        File.Delete(Path.Combine(RootPlugins, "cet.asi"));
        Directory.CreateDirectory(OsPlugins);
        DxvkService.RemoveEmptyRootPlugins(_dir, Path.Combine(OsPlugins, "dxgi.dll"));
        Assert.True(Directory.Exists(OsPlugins)); // OptiScaler\plugins is OptiScaler's to remove
        DxvkService.RemoveEmptyRootPlugins(_dir, Path.Combine(RootPlugins, "dxgi.dll"));
        Assert.False(Directory.Exists(RootPlugins));
    }

    [Fact]
    public void UninstallPreflight_InUse_SaysInUse()
    {
        File.WriteAllBytes(D3d9, Dxvk);
        var record = new RenoDXCommander.Models.DxvkInstalledRecord { InstalledDlls = new List<string> { "d3d9.dll" } };
        using (new FileStream(D3d9, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var msg = DxvkService.UninstallPreflight(_dir, record, "Some Game");
            Assert.NotNull(msg);
            Assert.Contains("in use", msg);
            Assert.Contains("Some Game", msg);
        }
    }
}
