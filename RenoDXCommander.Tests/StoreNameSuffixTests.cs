using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>Store platform suffixes stripped for name lookups (artwork, PCGamingWiki data).</summary>
public class StoreNameSuffixTests
{
    [Theory]
    [InlineData("Banishers: Ghosts of New Eden - Windows", "Banishers: Ghosts of New Eden")]
    [InlineData("Some Game (Windows)", "Some Game")]
    [InlineData("Some Game (Windows 10)", "Some Game")]
    [InlineData("Some Game (Win10)", "Some Game")]
    [InlineData("Some Game (PC)", "Some Game")]
    [InlineData("Some Game [PC]", "Some Game")]
    [InlineData("Some Game for Windows", "Some Game")]
    [InlineData("Some Game for Windows 10", "Some Game")]
    [InlineData("Some Game – PC", "Some Game")]
    [InlineData("Some Game Windows 10", "Some Game")]
    [InlineData("Some Game (Microsoft Store Edition)", "Some Game")]
    public void StripPlatform_RemovesTrailingPlatform(string name, string expected) =>
        Assert.Equal(expected, StoreNameSuffix.StripPlatform(name));

    [Theory]
    [InlineData("Banishers: Ghosts of New Eden")]
    [InlineData("Windows")]                         // nothing left: keep
    [InlineData("Microsoft Flight Simulator 2024")]
    [InlineData("Half-Life 2")]
    [InlineData("Windows 98 Simulator")]            // platform word not at the end
    [InlineData("PC Building Simulator")]
    [InlineData("")]
    [InlineData(null)]
    public void StripPlatform_LeavesOtherNames(string? name) =>
        Assert.Null(StoreNameSuffix.StripPlatform(name));

    [Theory]
    [InlineData("Hela: Of Mice & Magic Demo", "Hela: Of Mice & Magic")]
    [InlineData("Some Game (Demo)", "Some Game")]
    [InlineData("Some Game - Demo (PC)", "Some Game")]
    [InlineData("Some Game Playtest", "Some Game")]
    public void StripTrial_RemovesDemoAndPlaytest(string name, string expected) =>
        Assert.Equal(expected, StoreNameSuffix.StripTrial(name));

    [Theory]
    [InlineData("Demonologist")]
    [InlineData("Some Game")]
    public void StripTrial_LeavesOtherNames(string name) =>
        Assert.Null(StoreNameSuffix.StripTrial(name));
}
