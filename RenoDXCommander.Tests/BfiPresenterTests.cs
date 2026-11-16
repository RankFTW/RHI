using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>BFI Presenter: release tags, the tested-games list parser and name matching, the per-game record.</summary>
public class BfiPresenterTests
{
    [Theory]
    [InlineData("v.1.0.41", "1.0.41")]
    [InlineData("v1.0.41", "1.0.41")]
    [InlineData("1.0.41", "1.0.41")]
    public void VersionFromTag_StripsPrefix(string tag, string version) =>
        Assert.Equal(version, BfiPresenterService.VersionFromTag(tag));

    [Theory]
    [InlineData("BFIPresenter.addon64", BfiRoute.Dx)]
    [InlineData(@"bin\BFIPresenter.addon32", BfiRoute.Dx)]
    [InlineData(@"Binaries\Win64\BFIPresenter.ini", BfiRoute.Vulkan)]
    public void RouteOf_FromRecord(string rel, BfiRoute route) =>
        Assert.Equal(route, BfiPresenterService.RouteOf(rel));

    private const string Sample =
        "## Compatibility\n\n" +
        "| Game | Status | Frame generation with the presenter | Min. version | Key requirement |\n" +
        "| --- | --- | --- | --- | --- |\n" +
        "| Alan Wake 2 | Working | Works (2×, 3×) | v0.19 | Standard setup |\n" +
        "| Alan Wake | Working (DX9, 32-bit) | — | v1.0.3 | `BFIPresenter.addon32` |\n" +
        "| Onimusha | Working | Works | v0.27 | Resize the game's swap chain to start BFI (`PresenterResize=2`, set by the Manager) |\n" +
        "| God of War Ragnarök | Working | Runs | v0.24 | **Standard setup**; RTSS limiter off |\n" +
        "| Control | Working | Works | v0.2 | Standard setup |\n" +
        "| Hades | Working | Not tested | v0.78 | without RenoDX, BFI starts only with Resize the game's swap chain to start BFI (`PresenterResize`, set by the Manager) |\n" +
        "\n### More games tested\n\n" +
        "| Game | Notes |\n| --- | --- |\n" +
        "| Hades II | `PresenterResize=1` (the Manager sets it) |\n" +
        "\n### Tested by users\n\n" +
        "| Game | Tested by | Notes |\n| --- | --- | --- |\n" +
        "| Megabonk | [@HighPriest](https://github.com/HighPriest) ([#59](https://example)) | Standard setup |\n";

    [Fact]
    public void ParseTestedGames_ReadsAllThreeTables()
    {
        var list = BfiPresenterService.ParseTestedGames(Sample);
        Assert.Equal(8, list.Count);
        var oni = list.Single(e => e.Name == "Onimusha");
        Assert.Equal(2, oni.PresenterResize);
        Assert.False(oni.PresenterResizeOnlyWithoutRenoDx);
        var hades = list.Single(e => e.Name == "Hades");
        Assert.Equal(1, hades.PresenterResize);              // bare key = 1
        Assert.True(hades.PresenterResizeOnlyWithoutRenoDx);
        Assert.Null(list.Single(e => e.Name == "Control").PresenterResize);
        Assert.Equal("Working", oni.Status);
        Assert.Equal(1, list.Single(e => e.Name == "Hades II").PresenterResize);
        var mega = list.Single(e => e.Name == "Megabonk");
        Assert.Equal("Reported working by @HighPriest (#59)", mega.Status);
        Assert.Contains("Standard setup", list.Single(e => e.Name == "God of War Ragnarök").Notes);
        Assert.DoesNotContain("**", list.Single(e => e.Name == "God of War Ragnarök").Notes);
    }

    [Theory]
    [InlineData("Alan Wake 2", "Alan Wake 2")]
    [InlineData("Alan Wake", "Alan Wake")]
    [InlineData("God of War Ragnarok", "God of War Ragnarök")]   // accent
    [InlineData("Control Ultimate Edition", "Control")]          // store edition after the name
    [InlineData("Hades II", "Hades II")]
    public void FindTested_Matches(string detected, string expected)
    {
        var list = BfiPresenterService.ParseTestedGames(Sample);
        Assert.Equal(expected, BfiPresenterService.FindTested(list, detected)?.Name);
    }

    [Theory]
    [InlineData("Alan Wake 3")]      // a number after a listed name is another game
    [InlineData("Controller Tester")] // not at a word boundary
    [InlineData("")]
    public void FindTested_NoFalseMatch(string detected)
    {
        var list = BfiPresenterService.ParseTestedGames(Sample);
        Assert.Null(BfiPresenterService.FindTested(list, detected));
    }

    [Fact]
    public void NormalizeName_DropsMarksAndParentheses() =>
        Assert.Equal("resident evil 4", BfiPresenterService.NormalizeName("Resident Evil™ 4 (2023)"));
}
