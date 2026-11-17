using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// 20/30 FG Unlock: ini keys kept across mod updates and the HSF conflict pair.
/// </summary>
public class Dlssg2030Tests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rhi-dlssg2030-" + Guid.NewGuid().ToString("N"));

    public Dlssg2030Tests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private const string PackagedIni =
        "[General]\r\nEnabled=1\r\n\r\n[FrameGeneration]\r\nEngine=DLSS\r\nMaxGeneratedFrames=3\r\n\r\n" +
        "[XeSSFG]\r\nFlowResolution=Middle\r\nUseHudless=Auto\r\n\r\n[Logging]\r\nLevel=1\r\nDirectory=dlssg_sm86\\logs\r\n";

    private (string Packaged, string Game) Write(string gameIni)
    {
        var packaged = Path.Combine(_root, "packaged.ini");
        var game     = Path.Combine(_root, "dlssg_sm86.ini");
        File.WriteAllText(packaged, PackagedIni);
        File.WriteAllText(game, gameIni);
        return (packaged, game);
    }

    [Fact]
    public void Update_KeepsUsersXessAndLoggingValues()
    {
        var (packaged, game) = Write(
            "[XeSSFG]\r\nFlowResolution=CoarseAt4K\r\nUseHudless=1\r\n[Logging]\r\nLevel=2\r\nFrameTiming=1\r\n");
        Dlssg20_30Service.DeployIniFrom(packaged, game);
        Assert.Equal("CoarseAt4K", Dlssg20_30Service.ReadIniValue(game, "XeSSFG", "FlowResolution"));
        Assert.Equal("1",          Dlssg20_30Service.ReadIniValue(game, "XeSSFG", "UseHudless"));
        Assert.Equal("2",          Dlssg20_30Service.ReadIniValue(game, "Logging", "Level"));
        Assert.Equal("1",          Dlssg20_30Service.ReadIniValue(game, "Logging", "FrameTiming"));
        // Not a managed key: comes from the new package
        Assert.Equal(@"dlssg_sm86\logs", Dlssg20_30Service.ReadIniValue(game, "Logging", "Directory"));
    }

    [Fact]
    public void Update_DropsInvalidValues()
    {
        var (packaged, game) = Write("[XeSSFG]\r\nFlowResolution=Ultra\r\n[Logging]\r\nLevel=7\r\nFrameTiming=yes\r\n");
        Dlssg20_30Service.DeployIniFrom(packaged, game);
        Assert.Equal("Middle", Dlssg20_30Service.ReadIniValue(game, "XeSSFG", "FlowResolution"));
        Assert.Equal("1",      Dlssg20_30Service.ReadIniValue(game, "Logging", "Level"));
        Assert.Null(Dlssg20_30Service.ReadIniValue(game, "Logging", "FrameTiming"));
    }

    [Fact]
    public void HsfAndDlssg2030_AreExclusive()
        => Assert.Contains(DetailPanelBuilder.ExclusiveExtras, p =>
            (p.A == ExtraKind.Hsf && p.B == ExtraKind.Dlssg2030) || (p.A == ExtraKind.Dlssg2030 && p.B == ExtraKind.Hsf));
}
