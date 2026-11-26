using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Universal MFG Unlocker: Bink proxy names keep the game's Bink DLL as &lt;name&gt;Hooked.dll (the mod's install
/// guide), and the RTX Hair files (dxcompiler.dll / dxil.dll) RHI copies beside the exe. Real file moves in a temp folder.
/// </summary>
public class Rtx40MfgBinkRtxHairTests : IDisposable
{
    private sealed class NullReporter : ICrashReporter
    {
        public void Log(string message) { }
        public void WriteCrashReport(string source, Exception? ex, bool isTerminating = false, string? note = null) { }
        public bool VerboseLogging { get; set; }
    }

    private readonly string _root = Path.Combine(Path.GetTempPath(), "rhi-umfg-" + Guid.NewGuid().ToString("N"));
    private readonly string _staging;
    private readonly string _dxc;
    private readonly string _game;
    private readonly Rtx40MfgService _svc;
    private static readonly byte[] ModBytes  = { (byte)'M', (byte)'Z', 1, 2, 3, 4 };
    private static readonly byte[] BinkBytes = { (byte)'M', (byte)'Z', 9, 9, 9 };

    public Rtx40MfgBinkRtxHairTests()
    {
        _staging = Path.Combine(_root, "rtx40mfg");
        _dxc     = Path.Combine(_root, "dxc");
        _game    = Path.Combine(_root, "game");
        Directory.CreateDirectory(_staging);
        Directory.CreateDirectory(_dxc);
        Directory.CreateDirectory(Path.Combine(_game, "OptiScaler"));
        File.WriteAllBytes(Path.Combine(_staging, Rtx40MfgService.StagedDllName), ModBytes);
        File.WriteAllText(Path.Combine(_staging, "staging_format.txt"), "2");
        // Staged XeSS files so nothing downloads in the background
        Directory.CreateDirectory(Path.Combine(_staging, "xess"));
        File.WriteAllBytes(Path.Combine(_staging, "xess", "libxess_fg.dll"), new byte[] { 7 });
        File.WriteAllBytes(Path.Combine(_staging, "xess", "libxell.dll"), new byte[] { 8 });
        File.WriteAllBytes(Path.Combine(_dxc, "dxcompiler.dll"), new byte[] { 1, 1, 1 });
        File.WriteAllBytes(Path.Combine(_dxc, "dxil.dll"), new byte[] { 2, 2 });
        File.WriteAllText(Path.Combine(_dxc, "version.txt"), "v1.0");
        _svc = new Rtx40MfgService(new HttpClient(), new NullReporter(), RenoDXCommander.Tests.TestEnvironment.NewETagCache(), _staging);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* temp folder; ignore */ }
    }

    private string G(string name) => Path.Combine(_game, name);

    // ── Bink ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("binkw64.dll", true)]
    [InlineData("BINK2W64.DLL", true)]
    [InlineData("version.dll", false)]
    [InlineData("binkw64Hooked.dll", false)]
    [InlineData(null, false)]
    public void IsBinkName(string? name, bool expected) => Assert.Equal(expected, Rtx40MfgService.IsBinkName(name));

    [Fact]
    public void BinkInstall_KeepsGameBinkAsHooked_AndUninstallPutsItBack()
    {
        File.WriteAllBytes(G("binkw64.dll"), BinkBytes);

        Assert.True(_svc.Install(_game, "binkw64.dll"));
        Assert.Equal(ModBytes, File.ReadAllBytes(G("binkw64.dll")));
        Assert.Equal(BinkBytes, File.ReadAllBytes(G("binkw64Hooked.dll")));
        Assert.False(File.Exists(G("binkw64.dll.original")));

        // A reinstall keeps the Hooked copy as it is
        Assert.True(_svc.Install(_game, "binkw64.dll"));
        Assert.Equal(BinkBytes, File.ReadAllBytes(G("binkw64Hooked.dll")));

        _svc.Uninstall(_game, "binkw64.dll");
        Assert.Equal(BinkBytes, File.ReadAllBytes(G("binkw64.dll")));
        Assert.False(File.Exists(G("binkw64Hooked.dll")));
    }

    [Fact]
    public void BinkInstall_WithoutGameBink_WritesSentinel_AndUninstallDeletes()
    {
        Assert.True(_svc.Install(_game, "bink2w64.dll"));
        Assert.Equal(0, new FileInfo(G("bink2w64.dll.original")).Length);
        _svc.Uninstall(_game, "bink2w64.dll");
        Assert.False(File.Exists(G("bink2w64.dll")));
        Assert.False(File.Exists(G("bink2w64.dll.original")));
    }

    [Fact]
    public void BrokenBinkInstall_IsRepairedOnUpdatePass()
    {
        // What older RHI versions left: the mod at the name, the game's Bink in .original, no Hooked copy
        File.WriteAllBytes(G("binkw64.dll"), ModBytes);
        File.WriteAllBytes(G("binkw64.dll.original"), BinkBytes);

        _svc.UpdateInstalledGames(new[] { (_game, "binkw64.dll") });

        Assert.Equal(BinkBytes, File.ReadAllBytes(G("binkw64Hooked.dll")));
        Assert.False(File.Exists(G("binkw64.dll.original")));
        Assert.Equal(ModBytes, File.ReadAllBytes(G("binkw64.dll")));
    }

    [Fact]
    public void BrokenBinkInstall_OlderModBuild_IsRepairedByTheUpdate()
    {
        File.WriteAllBytes(G("binkw64.dll"), new byte[] { 4, 4, 4, 4, 4, 4, 4 }); // an older build of the mod
        File.WriteAllBytes(G("binkw64.dll.original"), BinkBytes);
        _svc.UpdateInstalledGames(new[] { (_game, "binkw64.dll") });
        Assert.Equal(BinkBytes, File.ReadAllBytes(G("binkw64Hooked.dll")));
        Assert.Equal(ModBytes, File.ReadAllBytes(G("binkw64.dll")));
        Assert.False(File.Exists(G("binkw64.dll.original")));
    }

    [Fact]
    public void BinkInstall_MovedIntoOptiScaler_RestoresGameBink()
    {
        File.WriteAllBytes(G("binkw64.dll"), BinkBytes);
        Assert.True(_svc.Install(_game, "binkw64.dll"));

        Assert.Equal(Rtx40MfgService.PluginRelPath, _svc.MoveToOptiScalerPlugin(_game, "binkw64.dll"));
        Assert.Equal(BinkBytes, File.ReadAllBytes(G("binkw64.dll")));
        Assert.False(File.Exists(G("binkw64Hooked.dll")));

        // Back out: the remembered Bink name is used again, with the game's file kept as Hooked
        Assert.Equal("binkw64.dll", _svc.MoveOutOfOptiScalerPlugin(_game));
        Assert.Equal(ModBytes, File.ReadAllBytes(G("binkw64.dll")));
        Assert.Equal(BinkBytes, File.ReadAllBytes(G("binkw64Hooked.dll")));
    }

    // ── RTX Hair deploy rule ──────────────────────────────────────────────────

    [Theory]
    [InlineData(true, false, true, GpuFamily.Rtx40, true)]
    [InlineData(true, false, true, GpuFamily.Rtx30, true)]
    [InlineData(true, false, true, null, true)]               // unknown card: deploy
    [InlineData(true, false, true, GpuFamily.Rtx50, false)]   // RTX 50 has RTX Hair itself
    [InlineData(true, false, true, GpuFamily.Newer, false)]
    [InlineData(true, false, true, GpuFamily.Rtx20, false)]
    [InlineData(true, false, false, GpuFamily.Rtx40, false)]  // not DirectX 12 (Vulkan needs none)
    [InlineData(false, false, true, GpuFamily.Rtx40, false)]  // mod not installed
    [InlineData(true, true, true, GpuFamily.Rtx40, false)]    // HSF's helper copy
    public void RtxHairFilesWanted(bool installed, bool hsfMods, bool dx12, GpuFamily? gpu, bool expected)
        => Assert.Equal(expected, Rtx40MfgService.RtxHairFilesWanted(installed, hsfMods, dx12, gpu));

    [Theory]
    [InlineData("dxc_2025_07_14.zip", true)]
    [InlineData("linux_dxc_2025_07_14.x86_64.tar.gz", false)]
    [InlineData("Source code (zip)", false)]
    public void DxcAssetName(string name, bool expected) => Assert.Equal(expected, Rtx40MfgService.IsDxcWindowsZip(name));

    // ── RTX Hair files ────────────────────────────────────────────────────────

    [Fact]
    public void DxcFiles_DeployedAndRemoved_GameCopiesNeverTouched()
    {
        var gameDxil = new byte[] { 7, 7, 7, 7 };
        File.WriteAllBytes(G("dxil.dll"), gameDxil); // the game ships its own

        Assert.True(_svc.DeployDxcFiles(_game, recreateRemoved: false));
        Assert.Equal(new byte[] { 1, 1, 1 }, File.ReadAllBytes(G("dxcompiler.dll")));
        Assert.Equal(gameDxil, File.ReadAllBytes(G("dxil.dll")));
        Assert.Equal(new[] { "dxcompiler.dll" }, Rtx40MfgService.DxcFilesOwnedIn(_game));

        _svc.RemoveDxcFiles(_game);
        Assert.False(File.Exists(G("dxcompiler.dll")));
        Assert.Equal(gameDxil, File.ReadAllBytes(G("dxil.dll")));
        Assert.False(File.Exists(G(Rtx40MfgService.DxcMarkerFile)));
    }

    [Fact]
    public void DxcFiles_RefreshedWhenStagingChanges_UserRemovedNotRecreated()
    {
        Assert.True(_svc.DeployDxcFiles(_game, recreateRemoved: false));
        File.Delete(G("dxil.dll")); // user removed it

        File.WriteAllBytes(Path.Combine(_dxc, "dxcompiler.dll"), new byte[] { 3, 3, 3, 3 }); // new staged version
        Assert.True(_svc.DeployDxcFiles(_game, recreateRemoved: false));
        Assert.Equal(new byte[] { 3, 3, 3, 3 }, File.ReadAllBytes(G("dxcompiler.dll")));
        Assert.False(File.Exists(G("dxil.dll")));

        // An install the user asks for puts it back
        Assert.True(_svc.DeployDxcFiles(_game, recreateRemoved: true));
        Assert.True(File.Exists(G("dxil.dll")));
    }

    [Fact]
    public void DxcFiles_ReplacedByGame_AreKeptOnRemoval()
    {
        Assert.True(_svc.DeployDxcFiles(_game, recreateRemoved: false));
        File.WriteAllBytes(G("dxcompiler.dll"), new byte[] { 8, 8, 8, 8, 8 }); // a game update put its own there
        _svc.RemoveDxcFiles(_game);
        Assert.Equal(new byte[] { 8, 8, 8, 8, 8 }, File.ReadAllBytes(G("dxcompiler.dll")));
        Assert.False(File.Exists(G("dxil.dll")));
    }

    // ── RTXMFG.RtxHair.ini ────────────────────────────────────────────────────

    [Fact]
    public void RtxHairIni_OffThenOn_KeepsOtherKeys()
    {
        var ini = G(Rtx40MfgService.RtxHairIniName);
        File.WriteAllLines(ini, new[] { "[RtxHair]", "DumpSpirv=1" });

        Assert.True(Rtx40MfgService.SetRtxHairOn(_game, false));
        Assert.False(Rtx40MfgService.IsRtxHairOn(_game));
        Assert.Contains("Universal=0", File.ReadAllLines(ini));
        Assert.Contains("DumpSpirv=1", File.ReadAllLines(ini));

        Assert.True(Rtx40MfgService.SetRtxHairOn(_game, true));
        Assert.True(Rtx40MfgService.IsRtxHairOn(_game));
        Assert.Equal(new[] { "[RtxHair]", "DumpSpirv=1" }, File.ReadAllLines(ini));
    }

    [Fact]
    public void RtxHairIni_CreatedForOff_DeletedForOn()
    {
        Assert.True(Rtx40MfgService.IsRtxHairOn(_game));
        Assert.True(Rtx40MfgService.SetRtxHairOn(_game, false));
        Assert.Equal(new[] { "[RtxHair]", "Universal=0" }, File.ReadAllLines(G(Rtx40MfgService.RtxHairIniName)));
        Assert.True(Rtx40MfgService.SetRtxHairOn(_game, true));
        Assert.False(File.Exists(G(Rtx40MfgService.RtxHairIniName)));
    }

    [Fact]
    public void SetIniValue_AddsKeyToExistingSectionBeforeTheNextOne()
    {
        var result = Rtx40MfgService.SetIniValue(new[] { "[RtxHair]", "DumpSpirv=1", "[Other]", "A=1" }, "RtxHair", "Universal", "0");
        Assert.Equal(new[] { "[RtxHair]", "DumpSpirv=1", "Universal=0", "[Other]", "A=1" }, result);
        Assert.Equal("0", Rtx40MfgService.GetIniValue(result, "RtxHair", "Universal"));
        Assert.Null(Rtx40MfgService.GetIniValue(result, "Other", "Universal"));
    }
}
