using RenoDXCommander.Models;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Custom game folders: the exe picker (CustomGameExeScorer), the folder-name cleaner and the
/// folder validation / candidate-root helpers. Exes are fake files (not PE), so the PE probe reports
/// no graphics imports and not console — the layouts alone must pick the right exe.
/// </summary>
public class CustomGameFolderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rhi-cgf-" + Guid.NewGuid().ToString("N"));

    public CustomGameFolderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* temp folder; ignore */ }
    }

    private string Game(string name) { var d = Path.Combine(_root, name); Directory.CreateDirectory(d); return d; }

    private static void File(string dir, string rel, int kb)
    {
        var p = Path.Combine(dir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        System.IO.File.WriteAllBytes(p, new byte[kb * 1024]);
    }

    private static ExeRanking Rank(string dir) => CustomGameExeScorer.Rank(dir, probe: _ => new ExeProbe(false, false));

    [Fact]
    public void Unreal_ShippingExeBeatsRootStub()
    {
        var g = Game("MyGame");
        File(g, "MyGame.exe", 300);
        File(g, @"MyGame\Binaries\Win64\MyGame-Win64-Shipping.exe", 4000);
        File(g, @"Engine\Binaries\Win64\CrashReportClient.exe", 2000);
        var r = Rank(g);
        Assert.EndsWith("MyGame-Win64-Shipping.exe", r.Exes[0].FullPath);
        Assert.False(r.IsAmbiguous);
        Assert.DoesNotContain(r.Exes, e => e.FullPath.EndsWith("CrashReportClient.exe"));
    }

    [Fact]
    public void Unity_CrashHandlerExcluded()
    {
        var g = Game("Cool Unity Game");
        File(g, "Cool Unity Game.exe", 650);
        File(g, "UnityPlayer.dll", 1);
        File(g, "UnityCrashHandler64.exe", 1200);
        var r = Rank(g);
        Assert.Single(r.Exes);
        Assert.EndsWith("Cool Unity Game.exe", r.Exes[0].FullPath);
    }

    [Fact]
    public void LauncherLosesToGame()
    {
        var g = Game("Space Thing");
        File(g, "launcher.exe", 3000);
        File(g, "game.exe", 2000);
        var r = Rank(g);
        Assert.EndsWith("game.exe", r.Exes[0].FullPath);
    }

    [Fact]
    public void Dx12Variant_IsAmbiguous()
    {
        var g = Game("Game");
        File(g, "Game.exe", 5000);
        File(g, "Game_DX12.exe", 5000);
        var r = Rank(g);
        Assert.Equal(2, r.Exes.Count);
        Assert.True(r.IsAmbiguous);
    }

    [Fact]
    public void InstallersAndRedistsExcluded()
    {
        var g = Game("Quiet Game");
        File(g, "QuietGame.exe", 2000);
        File(g, "unins000.exe", 1000);
        File(g, "vc_redist.x64.exe", 1000);
        File(g, @"_CommonRedist\DirectX\DXSETUP.exe", 500);
        File(g, @"redist\something.exe", 500);
        var r = Rank(g);
        Assert.Single(r.Exes);
        Assert.EndsWith("QuietGame.exe", r.Exes[0].FullPath);
    }

    [Fact]
    public void NoExe_NoCandidates()
    {
        var g = Game("Soundtrack Only");
        File(g, "track01.flac", 10);
        Assert.Empty(Rank(g).Exes);
    }

    [Fact]
    public void ConsoleAndGraphicsProbeAffectScore()
    {
        var g = Game("Probe");
        File(g, "a.exe", 1000);
        File(g, "b.exe", 1000);
        var r = CustomGameExeScorer.Rank(g, probe: p => p.EndsWith("a.exe")
            ? new ExeProbe(false, true) : new ExeProbe(true, false));
        Assert.EndsWith("b.exe", r.Exes[0].FullPath);
        Assert.False(r.IsAmbiguous);
    }

    [Theory]
    [InlineData("unins000.exe", true)]
    [InlineData("vc_redist.x64.exe", true)]
    [InlineData("UnityCrashHandler64.exe", true)]
    [InlineData("UE4PrereqSetup_x64.exe", true)]
    [InlineData("EasyAntiCheat_EOS_Setup.exe", true)]
    [InlineData("Game.exe", false)]
    [InlineData("Cyberpunk2077.exe", false)]
    public void ExcludedNames(string name, bool excluded)
        => Assert.Equal(excluded, CustomGameExeScorer.IsExcludedExeName(name));

    [Theory]
    [InlineData("Hollow Knight [GOG]", "Hollow Knight")]
    [InlineData("Cyberpunk.2077.v2.1-GOG", "Cyberpunk 2077")]
    [InlineData("Hades (v1.38290)", "Hades")]
    [InlineData("Hollow_Knight_v1.5.78.11833", "Hollow Knight")]
    [InlineData("Stardew Valley Build 12345 Repack", "Stardew Valley")]
    [InlineData("The Witcher 3 Wild Hunt GOTY", "The Witcher 3 Wild Hunt GOTY")]
    [InlineData("Some.Game-CODEX", "Some Game")]
    [InlineData("S.T.A.L.K.E.R. Shadow of Chernobyl", "S.T.A.L.K.E.R. Shadow of Chernobyl")]
    [InlineData("Disco Elysium - The Final Cut (DRM Free)", "Disco Elysium The Final Cut")]
    public void NameCleaner(string folder, string expected)
        => Assert.Equal(expected, CustomGameNameCleaner.Clean(folder));

    [Fact]
    public void Validate_RefusesBadFolders()
    {
        var a = Game("Lib");
        var inner = Game(@"Lib\Sub");
        Assert.Null(CustomFolderScanService.ValidateFolder(a, Array.Empty<string>()));
        Assert.NotNull(CustomFolderScanService.ValidateFolder(a, new[] { a }));
        Assert.NotNull(CustomFolderScanService.ValidateFolder(inner, new[] { a }));
        Assert.NotNull(CustomFolderScanService.ValidateFolder(a, new[] { inner }));
        Assert.NotNull(CustomFolderScanService.ValidateFolder(Environment.GetFolderPath(Environment.SpecialFolder.Windows), Array.Empty<string>()));
        var steam = Game(@"steamapps\common");
        Assert.NotNull(CustomFolderScanService.ValidateFolder(steam, Array.Empty<string>()));
        Assert.NotNull(CustomFolderScanService.ValidateFolder(Path.Combine(_root, "missing"), Array.Empty<string>()));
    }

    [Fact]
    public void CandidateRoots_LibraryVsSingleGame()
    {
        var lib = Game("Library");
        File(lib, @"One\one.exe", 500);
        File(lib, @"Two\two.exe", 500);
        Assert.Equal(2, CustomFolderScanService.GetCandidateRoots(lib).Count);
        Assert.Equal(Path.Combine(lib, "Two"),
            CustomFolderScanService.FindCandidateRoot(Path.Combine(lib, "Two", "bin"), new[] { lib }));

        var single = Game("SingleGame");
        File(single, "single.exe", 500);
        File(single, @"data\readme.txt", 1);
        Assert.Equal(new[] { CustomFolderScanService.NormalizePath(single) }, CustomFolderScanService.GetCandidateRoots(single));
    }
}
