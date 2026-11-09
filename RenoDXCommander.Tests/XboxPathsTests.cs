using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>Game Pass games use their real folder (D:\xbox\Game\Content), not the WindowsApps junction.</summary>
public class XboxPathsTests
{
    private const string Pkg = @"C:\Program Files\WindowsApps\LyricalGamesLLC.ValorMortis_1.3.5124.0_x64__72fag5ekhk96y";
    private const string Real = @"D:\xbox\Valor Mortis\Content";

    private static string? Resolve(string physical) =>
        string.Equals(physical, Pkg, StringComparison.OrdinalIgnoreCase) ? Real : null;

    [Theory]
    [InlineData("CN=C32D4B9A-1AEC-416A-8124-789B35AD6753", "72fag5ekhk96y")]
    [InlineData("CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US", "8wekyb3d8bbwe")]
    public void PublisherId_MatchesWindows(string publisher, string expected)
    {
        Assert.Equal(expected, XboxPaths.PublisherId(publisher));
    }

    [Fact]
    public void Rewrite_JsonPath_KeepsSubfolder()
    {
        var json = """{"InstallPath": "C:\\Program Files\\WindowsApps\\LyricalGamesLLC.ValorMortis_1.3.5124.0_x64__72fag5ekhk96y\\ValorMortis\\Binaries\\Win64"}""";
        var result = XboxPaths.RewriteSavedPaths(json, Resolve, out var count);
        Assert.Equal(1, count);
        Assert.Equal("""{"InstallPath": "D:\\xbox\\Valor Mortis\\Content\\ValorMortis\\Binaries\\Win64"}""", result);
    }

    [Fact]
    public void Rewrite_JsonInsideJsonString_KeepsDoubleEscaping()
    {
        var json = """{"LaunchExeOverrides":"{\"Valor Mortis|\":\"C:\\\\Program Files\\\\WindowsApps\\\\LyricalGamesLLC.ValorMortis_1.3.5124.0_x64__72fag5ekhk96y\\\\gamelaunchhelper.exe\"}"}""";
        var result = XboxPaths.RewriteSavedPaths(json, Resolve, out var count);
        Assert.Equal(1, count);
        Assert.Contains("""D:\\\\xbox\\\\Valor Mortis\\\\Content\\\\gamelaunchhelper.exe""", result);
    }

    [Fact]
    public void Rewrite_LowerCaseCacheKey_StaysLowerCase()
    {
        var json = """{"c:\\program files\\windowsapps\\lyricalgamesllc.valormortis_1.3.5124.0_x64__72fag5ekhk96y\\valormortis\\binaries\\win64": true}""";
        var result = XboxPaths.RewriteSavedPaths(json, Resolve, out _);
        Assert.Equal("""{"d:\\xbox\\valor mortis\\content\\valormortis\\binaries\\win64": true}""", result);
    }

    [Fact]
    public void Rewrite_OlderVersionOrUnknownPackage_Unchanged()
    {
        // An older version's folder no longer exists, so it doesn't resolve; normal path reconciliation handles it
        var json = """["C:\\Program Files\\WindowsApps\\LyricalGamesLLC.ValorMortis_1.2.0.0_x64__72fag5ekhk96y\\x", "C:\\Program Files\\WindowsApps\\SupergiantGamesLLC.HadesII_1.14.296.0_x64_137573_q53c1yqmx7pha", "%PROGRAMFILES%\\WindowsApps\\A278AB0D.AsphaltXtreme_1.7.3.8_x86__h6adky7gbf63m", "D:\\xbox\\Valor Mortis\\Content"]""";
        var result = XboxPaths.RewriteSavedPaths(json, Resolve, out var count);
        Assert.Equal(0, count);
        Assert.Equal(json, result);
    }

    [Fact]
    public void Rewrite_PassesThePhysicalPackageFolder()
    {
        var seen = new List<string>();
        XboxPaths.RewriteSavedPaths("""{"a":"C:\\Program Files\\WindowsApps\\SupergiantGamesLLC.HadesII_1.14.296.0_x64_137573_q53c1yqmx7pha\\Ship"}""",
            p => { seen.Add(p); return null; }, out _);
        Assert.Equal(@"C:\Program Files\WindowsApps\SupergiantGamesLLC.HadesII_1.14.296.0_x64_137573_q53c1yqmx7pha", Assert.Single(seen));
    }

    [Fact]
    public void ReadGameConfig_GivesNameAndLaunchId()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rhi-xbox-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "MicrosoftGame.config"), """
                <?xml version="1.0" encoding="utf-8"?>
                <Game configVersion="1">
                  <Identity Name="Microsoft.198377053870B" Publisher="CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US" Version="1.112.1610.0" />
                  <ExecutableList>
                    <Executable Name="Meteorite/Binaries/WinGDK/HaloCampaignEvolved.exe" Id="AppHaloCampaignEvolvedShipping" OverrideDisplayName="ms-resource:AppDisplayName" TargetDeviceFamily="PC" />
                  </ExecutableList>
                  <ShellVisuals DefaultDisplayName="Halo: Campaign Evolved" PublisherDisplayName="Microsoft Studios" />
                </Game>
                """);
            var (name, aumid) = XboxPaths.ReadGameConfig(dir);
            Assert.Equal("Halo: Campaign Evolved", name);
            Assert.Equal("Microsoft.198377053870B_8wekyb3d8bbwe!AppHaloCampaignEvolvedShipping", aumid);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
