using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// ReShade's backup of a foreign DLL at its deploy name covers every name, never replaces a
/// real .original, and never restores a 0-byte sentinel as a DLL (review item 2.11).
/// </summary>
public class ReShadeForeignDllTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rhi-rsforeign-" + Guid.NewGuid().ToString("N"));

    public ReShadeForeignDllTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* temp folder; ignore */ }
    }

    private string Write(string name, string content)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, content);
        return p;
    }

    [Theory]
    [InlineData("version.dll")]
    [InlineData("winmm.dll")]
    [InlineData("opengl32.dll")]
    [InlineData("d3d12.dll")]
    [InlineData("ReShade64.asi")]
    public void ForeignFile_AtAnyName_IsBackedUp(string name)
    {
        var p = Write(name, "some other mod");
        Assert.True(AuxInstallService.IsForeignForReShade(p));
        Assert.Equal(AuxInstallService.ForeignBackupResult.BackedUp, AuxInstallService.BackupForeignDllChecked(p));
        Assert.False(File.Exists(p));
        Assert.Equal("some other mod", File.ReadAllText(p + ".original"));
    }

    [Fact]
    public void MissingFile_NeedsNoBackup()
    {
        Assert.Equal(AuxInstallService.ForeignBackupResult.NotNeeded,
            AuxInstallService.BackupForeignDllChecked(Path.Combine(_dir, "version.dll")));
    }

    [Fact]
    public void RealOriginal_IsNeverReplaced()
    {
        var p = Write("version.dll", "second mod");
        Write("version.dll.original", "game's own file");
        Assert.Equal(AuxInstallService.ForeignBackupResult.Blocked, AuxInstallService.BackupForeignDllChecked(p));
        Assert.Equal("second mod", File.ReadAllText(p));
        Assert.Equal("game's own file", File.ReadAllText(p + ".original"));
        Assert.False(AuxInstallService.BackupForeignDll(p));
    }

    [Fact]
    public void EmptySentinel_IsReplacedByTheForeignFile()
    {
        var p = Write("winmm.dll", "asi loader");
        Write("winmm.dll.original", "");
        Assert.Equal(AuxInstallService.ForeignBackupResult.BackedUp, AuxInstallService.BackupForeignDllChecked(p));
        Assert.Equal("asi loader", File.ReadAllText(p + ".original"));
    }

    [Fact]
    public void Restore_PutsRealBackupBack()
    {
        var p = Path.Combine(_dir, "dxgi.dll");
        Write("dxgi.dll.original", "special k");
        AuxInstallService.RestoreForeignDll(p);
        Assert.Equal("special k", File.ReadAllText(p));
        Assert.False(File.Exists(p + ".original"));
    }

    [Fact]
    public void TargetBlocked_ForeignWithRealBackup_ChangesNothing()
    {
        var p = Write("d3d11.dll", "special k");
        Write("d3d11.dll.original", "game's own file");
        Assert.NotNull(AuxInstallService.ReShadeTargetBlockedReason("G", _dir, "d3d11.dll"));
        Assert.Equal("special k", File.ReadAllText(p));
        Assert.Equal("game's own file", File.ReadAllText(p + ".original"));
    }

    [Fact]
    public void TargetBlocked_ForeignWithoutBackup_OrMissing_IsAllowed()
    {
        Write("version.dll", "some other mod");
        Assert.Null(AuxInstallService.ReShadeTargetBlockedReason("G", _dir, "version.dll"));
        Assert.Null(AuxInstallService.ReShadeTargetBlockedReason("G", _dir, "dinput8.dll"));
    }

    [Fact]
    public void TargetBlocked_NameHeldByAnotherComponent()
    {
        var prev = AuxInstallService.OtherDllOwnerResolver;
        try
        {
            AuxInstallService.OtherDllOwnerResolver = (game, path, dll) =>
                path == _dir && dll.Equals("winmm.dll", StringComparison.OrdinalIgnoreCase) ? "Universal MFG Unlocker" : null;
            var p = Write("winmm.dll", "mfg proxy");
            Write("winmm.dll.original", "");
            var reason = AuxInstallService.ReShadeTargetBlockedReason("G", _dir, "winmm.dll");
            Assert.NotNull(reason);
            Assert.Contains("Universal MFG Unlocker", reason);
            Assert.Null(AuxInstallService.ReShadeTargetBlockedReason("G", _dir, "dxgi.dll"));
            // Nothing moved: the proxy and its 0-byte sentinel are untouched
            Assert.Equal("mfg proxy", File.ReadAllText(p));
            Assert.Equal(0, new FileInfo(p + ".original").Length);
        }
        finally { AuxInstallService.OtherDllOwnerResolver = prev; }
    }

    [Fact]
    public void Restore_LeavesEmptySentinelAlone()
    {
        var p = Path.Combine(_dir, "dxgi.dll");
        Write("dxgi.dll.original", "");
        AuxInstallService.RestoreForeignDll(p);
        Assert.False(File.Exists(p));
        Assert.True(File.Exists(p + ".original"));
    }
}

public class ReShadeUninstallCleanupTests
{
    [Fact]
    public void Preset_IsNeverInTheCleanupList()
    {
        Assert.DoesNotContain("ReShadePreset.ini", AuxInstallService.ReShadeUninstallCleanupFiles(iniLocked: false), StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("ReShadePreset.ini", AuxInstallService.ReShadeUninstallCleanupFiles(iniLocked: true), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void LockedIni_IsKept_UnlockedIni_IsDeleted()
    {
        Assert.Contains("reshade.ini", AuxInstallService.ReShadeUninstallCleanupFiles(iniLocked: false), StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("reshade.ini", AuxInstallService.ReShadeUninstallCleanupFiles(iniLocked: true), StringComparer.OrdinalIgnoreCase);
        // The second runtime's config (Source engine games) follows reshade.ini
        Assert.Contains("ReShade2.ini", AuxInstallService.ReShadeUninstallCleanupFiles(iniLocked: false), StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("ReShade2.ini", AuxInstallService.ReShadeUninstallCleanupFiles(iniLocked: true), StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("https://example.com/a/renodx-ryubing.addon64", "renodx-ryubing.addon64")]
    [InlineData("https://example.com/a/renodx-x.addon64?raw=1", "renodx-x.addon64")]
    [InlineData("https://example.com/", null)]
    [InlineData("https://example.com/a/renodx-x%20y.addon64", "renodx-x%20y.addon64")] // not decoded (old installs)
    [InlineData("https://example.com/a/renodx-x.addon64#frag", "renodx-x.addon64")]
    public void EmulatorAddonFileName_TakesTheUrlFileName(string url, string? expected)
    {
        Assert.Equal(expected, RenoDXCommander.ViewModels.MainViewModel.EmulatorAddonFileName(url));
    }
}
