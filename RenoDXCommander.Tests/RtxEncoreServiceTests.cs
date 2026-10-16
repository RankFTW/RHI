using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

public class RtxEncoreServiceTests
{
    [Theory]
    [InlineData("Euro Truck Simulator 2")]
    [InlineData("euro truck simulator 2")]
    [InlineData("EURO TRUCK SIMULATOR 2")]
    [InlineData(" Euro Truck Simulator 2 ")]
    public void GetRecommendedProxyName_EuroTruckSimulator2_ReturnsWinmm(string gameName)
    {
        Assert.Equal("winmm.dll", RtxEncoreService.GetRecommendedProxyName(gameName));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Cyberpunk 2077")]
    [InlineData("Euro Truck Simulator")]
    [InlineData("Euro Truck Simulator 2 Demo")]
    [InlineData("American Truck Simulator")]
    public void GetRecommendedProxyName_OtherGames_ReturnsVersion(string? gameName)
    {
        Assert.Equal("version.dll", RtxEncoreService.GetRecommendedProxyName(gameName));
    }

    [Theory]
    [InlineData("Euro Truck Simulator 2")]
    [InlineData("Cyberpunk 2077")]
    public void GetRecommendedProxyName_ReturnsKnownProxyName(string gameName)
    {
        Assert.Contains(
            RtxEncoreService.GetRecommendedProxyName(gameName),
            RtxEncoreService.KnownProxyNames);
    }
}