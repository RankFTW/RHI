using RenoDXCommander.Models;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// OptiScaler install / update / uninstall must not delete or overwrite the game's own files, and
/// rhi_install.txt (shared by several components) must not lose other components' data.
/// </summary>
public class OptiScalerFileSafetyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rhi-osafety-" + Guid.NewGuid().ToString("N"));
    private string ManifestPath => Path.Combine(_dir, RhiInstallManifest.FileName);

    public OptiScalerFileSafetyTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* temp folder; ignore */ }
    }

    private static RhiInstallManifest OsManifest(params string[] files) => new()
    {
        Component = OptiScalerService.AddonType,
        InstalledAs = "dxgi.dll",
        Files = files.ToList(),
    };

    // ── Uninstall: shared DLSS DLLs (2.2) ───────────────────────────────────

    [Fact]
    public void ReleaseSharedDll_NoOriginal_IsGamesOwnFile()
    {
        // DLSS cache was empty at install: OptiScaler never copied it, so no .original exists
        var m = OsManifest("dxgi.dll", "OptiScaler.ini");
        Assert.False(OptiScalerService.CanReleaseSharedDll(m, "nvngx_dlss.dll", originalExists: false));
        Assert.False(OptiScalerService.CanReleaseSharedDll(null, "nvngx_dlss.dll", originalExists: false));
    }

    [Fact]
    public void ReleaseSharedDll_OptiScalerOnlyOwner_Released()
    {
        var m = OsManifest("dxgi.dll");
        m.SharedFiles["nvngx_dlss.dll"] = ["OptiScaler"];
        Assert.True(OptiScalerService.CanReleaseSharedDll(m, "nvngx_dlss.dll", originalExists: true));
    }

    [Fact]
    public void ReleaseSharedDll_OtherOwner_Kept()
    {
        var m = OsManifest("dxgi.dll", "nvngx_dlss.dll");
        m.SharedFiles["nvngx_dlss.dll"] = ["OptiScaler", "ShortFuse"];
        Assert.False(OptiScalerService.CanReleaseSharedDll(m, "nvngx_dlss.dll", originalExists: true));

        m.SharedFiles["nvngx_dlss.dll"] = ["Dlss5Tool"];
        Assert.False(OptiScalerService.CanReleaseSharedDll(m, "nvngx_dlss.dll", originalExists: true));
    }

    [Fact]
    public void ReleaseSharedDll_NoOwnerEntry_UsesOptiScalerFileList()
    {
        // Installs from before owner tracking: the file list says whether OptiScaler deployed it
        Assert.True(OptiScalerService.CanReleaseSharedDll(OsManifest("dxgi.dll", "nvngx_dlss.dll"), "nvngx_dlss.dll", true));
        // A .original made by something else (e.g. a DLSS swap) with OptiScaler never deploying the file
        Assert.False(OptiScalerService.CanReleaseSharedDll(OsManifest("dxgi.dll"), "nvngx_dlss.dll", true));
    }

    [Fact]
    public void ReleaseSharedDll_NoManifest_RelyOnOriginal()
    {
        Assert.True(OptiScalerService.CanReleaseSharedDll(null, "nvngx_dlss.dll", originalExists: true));
    }

    // ── Update: backups (2.5) ───────────────────────────────────────────────

    [Fact]
    public void UpdateBackup_NewFileTheGameShips_BackedUp()
    {
        var prev = new HashSet<string>(["dxgi.dll", "fakenvapi.dll"], StringComparer.OrdinalIgnoreCase);
        Assert.True(OptiScalerService.UpdateNeedsBackup(fileExists: true, "amd_fidelityfx_dx12.dll", prev, isOsDll: false));
        Assert.True(OptiScalerService.UpdateNeedsBackup(fileExists: true, "nvngx_dlss.dll", prev, isOsDll: false));
    }

    [Fact]
    public void UpdateBackup_OptiScalersOwnFile_NotBackedUp()
    {
        var prev = new HashSet<string>(["dxgi.dll", "fakenvapi.dll"], StringComparer.OrdinalIgnoreCase);
        Assert.False(OptiScalerService.UpdateNeedsBackup(true, "FAKENVAPI.dll", prev, isOsDll: false));
        Assert.False(OptiScalerService.UpdateNeedsBackup(true, "dxgi.dll", prev, isOsDll: true));
        // Unknown file list (very old install): never save OptiScaler's old copy as the "original"
        Assert.False(OptiScalerService.UpdateNeedsBackup(true, "fakenvapi.dll", null, isOsDll: false));
    }

    [Fact]
    public void UpdateBackup_AbsentFile_GetsSentinel()
    {
        Assert.True(OptiScalerService.UpdateNeedsBackup(fileExists: false, "fakenvapi.dll", null, isOsDll: false));
    }

    // ── rhi_install.txt fields (2.6) ────────────────────────────────────────

    private static RhiInstallManifest SharedManifest()
    {
        var m = OsManifest("dxgi.dll", "nvngx_dlss.dll");
        m.SharedFiles["nvngx_dlss.dll"] = ["OptiScaler"];
        m.SharedFiles["nvngx_dlssnr.dll"] = ["OptiScaler", "RtxEncore"];
        m.Components["Feeder"] = new RhiComponentRecord { Files = ["host64/nvngx_dlssnr.dll"] };
        m.NrMethod = "Feeder";
        m.Dlss5Build = RhiInstallManifest.Dlss5BuildFoveated;
        m.Dlss5BuildVersion = "v0.4.0";
        m.Dlss5BuildPinned = true;
        return m;
    }

    [Fact]
    public void SetOptiScalerFields_KeepsOtherComponentsData()
    {
        var m = OptiScalerService.SetOptiScalerFields(SharedManifest(), "Nightly", "20261101", "winmm.dll", ["winmm.dll"], ["OptiScaler"]);
        Assert.Equal("OptiScaler", m.Component);
        Assert.Equal("winmm.dll", m.InstalledAs);
        Assert.Equal("Nightly", m.Variant);
        Assert.True(m.IsFoveatedDlss5);
        Assert.Equal("v0.4.0", m.Dlss5BuildVersion);
        Assert.True(m.Dlss5BuildPinned);
        Assert.Equal("Feeder", m.NrMethod);
        Assert.True(m.Components.ContainsKey("Feeder"));
        Assert.Equal(2, m.SharedFiles.Count);
    }

    [Fact]
    public void ClearOptiScalerFields_RemovesOnlyOptiScaler()
    {
        var m = OptiScalerService.ClearOptiScalerFields(SharedManifest());
        Assert.Equal("", m.InstalledAs);
        Assert.Empty(m.Files);
        Assert.Empty(m.Folders);
        Assert.NotEqual("OptiScaler", m.Component);
        Assert.False(m.SharedFiles.ContainsKey("nvngx_dlss.dll"));
        Assert.Equal(new[] { "RtxEncore" }, m.SharedFiles["nvngx_dlssnr.dll"]);
        Assert.Equal("Feeder", m.NrMethod);
        Assert.True(m.IsFoveatedDlss5);
        Assert.False(m.IsEmpty);
    }

    [Fact]
    public void ClearOptiScalerFields_OnlyOptiScaler_LeavesEmptyManifest()
    {
        var m = OsManifest("dxgi.dll");
        m.SharedFiles["nvngx_dlss.dll"] = ["OptiScaler"];
        Assert.True(OptiScalerService.ClearOptiScalerFields(m).IsEmpty);
    }

    // ── Uninstall when ReShade / DXVK takes the name back (2.8, 2.9) ───────

    [Fact]
    public void Reclaim_KeepsRealBackup_DeletesSentinel()
    {
        Assert.True(OptiScalerService.KeepsBackupForReclaim(4_000_000)); // e.g. Special K saved as dxgi.dll.original
        Assert.False(OptiScalerService.KeepsBackupForReclaim(0));        // OptiScaler's own "nothing was here"
    }

    // ── Newest cached DLSS DLL ──────────────────────────────────────────────

    [Fact]
    public void PickNewestVersionDir_ComparesVersionsNotStrings()
    {
        var dirs = new[] { @"C:\c\DLSS\310.9.16", @"C:\c\DLSS\310.10.0", @"C:\c\DLSS\3.8.10" };
        Assert.Equal(@"C:\c\DLSS\310.10.0", OptiScalerService.PickNewestVersionDir(dirs));
        Assert.Equal(@"C:\c\DLSS\310.9.16", OptiScalerService.PickNewestVersionDir([@"C:\c\DLSS\310.9.16", @"C:\c\DLSS\3.8.10"]));
    }

    [Fact]
    public void PickNewestVersionDir_NonVersionNamesLast()
    {
        Assert.Equal(@"C:\c\310.1.0", OptiScalerService.PickNewestVersionDir([@"C:\c\zzz", @"C:\c\310.1.0"]));
        Assert.Null(OptiScalerService.PickNewestVersionDir([]));
    }

    // ── rhi_install.txt persistence (2.13) ──────────────────────────────────

    [Fact]
    public void Update_PreservesExistingData_NoTempFileLeft()
    {
        RhiInstallManifest.Write(_dir, SharedManifest());
        RhiInstallManifest.AddSharedFileOwner(_dir, "nvngx_dlssd.dll", "Dlss5Tool");

        var m = RhiInstallManifest.Read(_dir, out var status);
        Assert.Equal(RhiInstallManifest.ReadStatus.Ok, status);
        Assert.Equal("OptiScaler", m!.Component);
        Assert.True(m.IsFoveatedDlss5);
        Assert.Contains("Dlss5Tool", m.SharedFiles["nvngx_dlssd.dll"]);
        Assert.False(File.Exists(ManifestPath + ".tmp"));
        Assert.False(File.Exists(ManifestPath + ".bak"));
    }

    [Fact]
    public void UnreadableFile_WritersLeaveItAlone()
    {
        RhiInstallManifest.Write(_dir, SharedManifest());
        var before = File.ReadAllText(ManifestPath);

        using (new FileStream(ManifestPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Null(RhiInstallManifest.Read(_dir, out var status));
            Assert.Equal(RhiInstallManifest.ReadStatus.Unreadable, status);
            Assert.False(RhiInstallManifest.Update(_dir, m => m ?? new RhiInstallManifest(), "test"));
            Assert.False(RhiInstallManifest.RemoveSharedFileOwner(_dir, "nvngx_dlss.dll", "OptiScaler"));
            RhiInstallManifest.AddSharedFileOwner(_dir, "nvngx_dlssg.dll", "ShortFuse");
            RhiInstallManifest.SetComponent(_dir, "HSF", ["hsf.addon64"]);
        }

        Assert.Equal(before, File.ReadAllText(ManifestPath));
    }

    [Fact]
    public void RemoveSharedFileOwner_CorruptFile_KeepsTheFile()
    {
        File.WriteAllText(ManifestPath, "{ \"component\": \"Opti");
        Assert.False(RhiInstallManifest.RemoveSharedFileOwner(_dir, "nvngx_dlss.dll", "OptiScaler"));
    }

    [Fact]
    public void RemoveSharedFileOwner_LastOwner_True_OtherOwner_False()
    {
        var m = new RhiInstallManifest { Component = "RHI" };
        m.SharedFiles["nvngx_dlss.dll"] = ["OptiScaler", "ShortFuse"];
        RhiInstallManifest.Write(_dir, m);

        Assert.False(RhiInstallManifest.RemoveSharedFileOwner(_dir, "nvngx_dlss.dll", "OptiScaler"));
        Assert.True(RhiInstallManifest.RemoveSharedFileOwner(_dir, "nvngx_dlss.dll", "ShortFuse"));
        Assert.Empty(RhiInstallManifest.GetSharedFileOwners(_dir, "nvngx_dlss.dll"));
    }

    [Fact]
    public void UpdateDeleteIfEmpty_RemovesFile_OnlyWhenNothingLeft()
    {
        var m = OsManifest("dxgi.dll");
        m.NrMethod = "ShortFuse";
        RhiInstallManifest.Write(_dir, m);

        RhiInstallManifest.Update(_dir, x => x == null ? null : OptiScalerService.ClearOptiScalerFields(x), "test", deleteIfEmpty: true);
        Assert.True(File.Exists(ManifestPath));
        Assert.Equal("ShortFuse", RhiInstallManifest.Read(_dir)!.NrMethod);

        RhiInstallManifest.SetNrMethod(_dir, null);
        Assert.False(File.Exists(ManifestPath));
    }

    [Fact]
    public void ConcurrentOwnerAdds_NoneLost()
    {
        RhiInstallManifest.Write(_dir, new RhiInstallManifest { Component = "RHI" });
        var names = Enumerable.Range(0, 20).Select(i => $"file{i}.dll").ToArray();
        Parallel.ForEach(names, n => RhiInstallManifest.AddSharedFileOwner(_dir, n, "OptiScaler"));
        Assert.Equal(20, RhiInstallManifest.Read(_dir)!.SharedFiles.Count);
    }

    // ── Install step 4b: DXVK coexistence move and its undo ─────────────────

    [Fact]
    public void DxvkCoexistMove_FailedInstall_PutsDxvkBack()
    {
        var root = Path.Combine(_dir, "dxgi.dll");
        File.WriteAllBytes(root, new byte[] { 0xD1 });
        var move = OptiScalerService.MoveDxvkToPluginsDir(_dir, "dxgi.dll", os10Build: true)!;
        Assert.False(File.Exists(root));
        Assert.True(File.Exists(Path.Combine(_dir, "OptiScaler", "plugins", "dxgi.dll")));

        // Step 5 then backed up the free name (0-byte sentinel) and copied OptiScaler's DLL before failing
        File.WriteAllBytes(root + ".original", Array.Empty<byte>());
        File.WriteAllBytes(root, new byte[] { 0x05 });

        Assert.True(OptiScalerService.UndoDxvkCoexistMoveFiles(move));
        Assert.Equal(new byte[] { 0xD1 }, File.ReadAllBytes(root));
        Assert.False(File.Exists(root + ".original"));
        Assert.False(Directory.Exists(Path.Combine(_dir, "OptiScaler"))); // created by the move, now empty
    }

    [Fact]
    public void DxvkCoexistMove_Undo_KeepsEarlierBackupAndFolders()
    {
        var root = Path.Combine(_dir, "dxgi.dll");
        File.WriteAllBytes(root, new byte[] { 0xD1 });
        File.WriteAllBytes(root + ".original", new byte[] { 0x9A }); // the game's dxgi.dll that DXVK backed up
        Directory.CreateDirectory(Path.Combine(_dir, "OptiScaler", "plugins"));
        File.WriteAllBytes(Path.Combine(_dir, "OptiScaler", "plugins", "OptiPatcher.asi"), new byte[] { 1 });

        var move = OptiScalerService.MoveDxvkToPluginsDir(_dir, "dxgi.dll", os10Build: true)!;
        Assert.True(OptiScalerService.UndoDxvkCoexistMoveFiles(move));
        Assert.Equal(new byte[] { 0xD1 }, File.ReadAllBytes(root));
        Assert.Equal(new byte[] { 0x9A }, File.ReadAllBytes(root + ".original"));
        Assert.True(File.Exists(Path.Combine(_dir, "OptiScaler", "plugins", "OptiPatcher.asi")));
    }

    [Fact]
    public void DxvkCoexistMove_Undo_PluginGone_NothingChanged()
    {
        var root = Path.Combine(_dir, "dxgi.dll");
        File.WriteAllBytes(root, new byte[] { 0xD1 });
        var move = OptiScalerService.MoveDxvkToPluginsDir(_dir, "dxgi.dll", os10Build: true)!;
        File.Delete(move.PluginPath);
        File.WriteAllBytes(root, new byte[] { 0x05 });
        Assert.False(OptiScalerService.UndoDxvkCoexistMoveFiles(move));
        Assert.Equal(new byte[] { 0x05 }, File.ReadAllBytes(root));
    }

    [Fact]
    public void DxvkCoexistMove_Os09_GoesToRootPlugins_NoOptiScalerFolder()
    {
        // OptiScaler 0.9 loads same-name DLLs from <game>\plugins and has no OptiScaler\ folder
        var root = Path.Combine(_dir, "dxgi.dll");
        File.WriteAllBytes(root, new byte[] { 0xD1 });
        var move = OptiScalerService.MoveDxvkToPluginsDir(_dir, "dxgi.dll", os10Build: false);
        Assert.NotNull(move);
        Assert.False(File.Exists(root));
        Assert.Equal(new byte[] { 0xD1 }, File.ReadAllBytes(Path.Combine(_dir, "plugins", "dxgi.dll")));
        Assert.False(Directory.Exists(Path.Combine(_dir, "OptiScaler")));
        Assert.Equal(Path.Combine(_dir, "plugins"), OptiScalerService.GetPluginsDir(_dir)); // still a 0.9 layout
    }

    [Fact]
    public void DxvkCoexistMove_Os09_FailedInstall_PutsDxvkBack_RemovesCreatedFolder()
    {
        var root = Path.Combine(_dir, "dxgi.dll");
        File.WriteAllBytes(root, new byte[] { 0xD1 });
        var move = OptiScalerService.MoveDxvkToPluginsDir(_dir, "dxgi.dll", os10Build: false)!;
        File.WriteAllBytes(root + ".original", Array.Empty<byte>());
        File.WriteAllBytes(root, new byte[] { 0x05 });

        Assert.True(OptiScalerService.UndoDxvkCoexistMoveFiles(move));
        Assert.Equal(new byte[] { 0xD1 }, File.ReadAllBytes(root));
        Assert.False(File.Exists(root + ".original"));
        Assert.False(Directory.Exists(Path.Combine(_dir, "plugins")));
        Assert.True(Directory.Exists(_dir)); // the game folder itself is never removed
    }

    [Fact]
    public void DxvkCoexistMove_Os09_GameOwnedPluginsFolder_Kept()
    {
        var plugins = Path.Combine(_dir, "plugins");
        Directory.CreateDirectory(plugins);
        File.WriteAllBytes(Path.Combine(plugins, "cet.asi"), new byte[] { 7 }); // the game's own plugins folder
        File.WriteAllBytes(Path.Combine(_dir, "dxgi.dll"), new byte[] { 0xD1 });

        var move = OptiScalerService.MoveDxvkToPluginsDir(_dir, "dxgi.dll", os10Build: false)!;
        Assert.False(move.CreatedPluginsDir);
        Assert.True(OptiScalerService.UndoDxvkCoexistMoveFiles(move));
        Assert.True(File.Exists(Path.Combine(plugins, "cet.asi")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DxvkCoexistMove_TargetHoldsForeignFile_NotMoved(bool os10Build)
    {
        // A same-name DLL in the plugins folder that isn't DXVK is never replaced; DXVK's stays in the root
        var root = Path.Combine(_dir, "dxgi.dll");
        File.WriteAllBytes(root, new byte[] { 0xD1 });
        var target = OptiScalerService.PluginsDirForBuild(_dir, os10Build);
        Directory.CreateDirectory(target);
        File.WriteAllBytes(Path.Combine(target, "dxgi.dll"), new byte[] { 0x9A });

        Assert.Null(OptiScalerService.MoveDxvkToPluginsDir(_dir, "dxgi.dll", os10Build));
        Assert.Equal(new byte[] { 0xD1 }, File.ReadAllBytes(root));
        Assert.Equal(new byte[] { 0x9A }, File.ReadAllBytes(Path.Combine(target, "dxgi.dll")));
    }

    [Fact]
    public void DxvkCoexistMove_TargetHoldsOlderDxvk_Replaced()
    {
        var root = Path.Combine(_dir, "dxgi.dll");
        File.WriteAllBytes(root, System.Text.Encoding.ASCII.GetBytes("new dxvk"));
        Directory.CreateDirectory(Path.Combine(_dir, "plugins"));
        File.WriteAllBytes(Path.Combine(_dir, "plugins", "dxgi.dll"), System.Text.Encoding.ASCII.GetBytes("old dxvk"));

        Assert.NotNull(OptiScalerService.MoveDxvkToPluginsDir(_dir, "dxgi.dll", os10Build: false));
        Assert.Equal("new dxvk", File.ReadAllText(Path.Combine(_dir, "plugins", "dxgi.dll")));
    }

    [Fact]
    public void PluginsDirForBuild_MatchesBuild()
    {
        Assert.Equal(Path.Combine(_dir, "OptiScaler", "plugins"), OptiScalerService.PluginsDirForBuild(_dir, os10Build: true));
        Assert.Equal(Path.Combine(_dir, "plugins"), OptiScalerService.PluginsDirForBuild(_dir, os10Build: false));
    }

    // ── Stable staging: RHI's own data folders ──────────────────────────────

    [Fact]
    public void RhiStagingDataDirs_Recognised()
    {
        Assert.True(OptiScalerService.IsRhiStagingDataDir("saved-inis"));
        Assert.True(OptiScalerService.IsRhiStagingDataDir("INI-Reference"));
        Assert.False(OptiScalerService.IsRhiStagingDataDir("D3D12_Optiscaler"));
        Assert.False(OptiScalerService.IsRhiStagingDataDir(null));
    }

    [Fact]
    public void RemoveCopiedRhiDataDirs_OnlyWhenRecorded()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "saved-inis"));
        Directory.CreateDirectory(Path.Combine(_dir, "ini-reference"));
        var m = OsManifest("dxgi.dll");
        m.Folders = ["D3D12_Optiscaler", "saved-inis"];
        RhiInstallManifest.Write(_dir, m);

        OptiScalerService.RemoveCopiedRhiDataDirs(_dir);
        Assert.False(Directory.Exists(Path.Combine(_dir, "saved-inis")));
        Assert.True(Directory.Exists(Path.Combine(_dir, "ini-reference"))); // not listed: could be the game's
    }
}
