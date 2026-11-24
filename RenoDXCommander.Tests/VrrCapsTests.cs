using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

public class VrrCapsTests
{
    [Fact]
    public void Cap_MatchesReLimiterFormula()
    {
        Assert.Equal(224, VrrCaps.Cap(240));
        Assert.Equal(223, VrrCaps.Cap(239.96));
        Assert.Equal(188, VrrCaps.Cap(200));
        Assert.Equal(116, VrrCaps.Cap(120));
        Assert.Equal(0, VrrCaps.Cap(0));
    }

    [Fact]
    public void Presets_UseExactDisplayRate_WhenItRoundsToTheNominalRate()
    {
        var p = VrrCaps.Presets(new[] { 239.96 });
        Assert.Contains((223, 240), p);
        Assert.Contains((138, 144), p); // no display at 144: nominal rate
    }

    [Fact]
    public void Presets_LowestCapWins_WhenTwoDisplaysShareARate()
    {
        var p = VrrCaps.Presets(new[] { 240.0, 239.96 });
        Assert.Contains((223, 240), p);
    }

    [Fact]
    public void Presets_WithNoDisplays_UseNominalRates()
    {
        var p = VrrCaps.Presets(System.Array.Empty<double>());
        Assert.Contains((224, 240), p);
        Assert.Contains((430, 500), p);
        Assert.Equal(14, p.Count);
    }
}
