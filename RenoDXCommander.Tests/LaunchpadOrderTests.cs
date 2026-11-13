using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// iMMERSE Launchpad must run before Uplift and HSF (they read its motion the same frame). HSF switches it on
/// itself and ReShade lists it last (Lies of P, Nov 2026); RHI moves it to the top without changing what's on.
/// </summary>
public class LaunchpadOrderTests
{
    private const string Lp = UpliftService.LaunchpadToken;

    [Fact]
    public void MovesLaunchpadFirst_InBothLines()
    {
        var text = "Techniques=HSF_Depth@HSF.fx,HSF_Motion@HSF.fx," + Lp + "\r\n"
                 + "TechniqueSorting=HSF_Depth@HSF.fx,HSF_Motion@HSF.fx,Uplift@Uplift.fx," + Lp + "\r\n\r\n"
                 + "[MartysMods_LAUNCHPAD.fx]\r\nOPTICAL_FLOW_Q=1\r\n";
        var (result, changed) = UpliftService.MoveTechniqueFirst(text, Lp);

        Assert.True(changed);
        Assert.Equal("Techniques=" + Lp + ",HSF_Depth@HSF.fx,HSF_Motion@HSF.fx\r\n"
                   + "TechniqueSorting=" + Lp + ",HSF_Depth@HSF.fx,HSF_Motion@HSF.fx,Uplift@Uplift.fx\r\n\r\n"
                   + "[MartysMods_LAUNCHPAD.fx]\r\nOPTICAL_FLOW_Q=1\r\n", result);
    }

    [Theory]
    [InlineData("Techniques=" + Lp + ",Uplift@Uplift.fx\r\n")]            // already first
    [InlineData("Techniques=HSF_Depth@HSF.fx,HSF_Motion@HSF.fx\r\n")]     // not switched on: never added
    [InlineData("")]
    public void NothingToMove(string text)
    {
        var (result, changed) = UpliftService.MoveTechniqueFirst(text, Lp);
        Assert.False(changed);
        Assert.Equal(text, result);
    }

    [Fact]
    public void OnlyInSorting_StillMovedThere_TechniquesUntouched()
    {
        // Ticked state lives in Techniques: Launchpad unticked stays unticked
        var text = "Techniques=HSF_Motion@HSF.fx\nTechniqueSorting=HSF_Motion@HSF.fx," + Lp + "\n";
        var (result, changed) = UpliftService.MoveTechniqueFirst(text, Lp);
        Assert.True(changed);
        Assert.Equal("Techniques=HSF_Motion@HSF.fx\nTechniqueSorting=" + Lp + ",HSF_Motion@HSF.fx\n", result);
    }

    [Fact]
    public void KeepLaunchpadFirst_RewritesThePresetFile()
    {
        var game = Path.Combine(Path.GetTempPath(), "rhi-lp-order-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(game);
            var preset = Path.Combine(game, "ReShadePreset.ini");
            File.WriteAllText(preset, "Techniques=HSF_Motion@HSF.fx," + Lp + "\r\n");
            Assert.True(UpliftService.KeepLaunchpadFirst(game));
            Assert.Equal("Techniques=" + Lp + ",HSF_Motion@HSF.fx\r\n", File.ReadAllText(preset));
            Assert.False(UpliftService.KeepLaunchpadFirst(game));   // second time: already first
        }
        finally { Directory.Delete(game, recursive: true); }
    }
}
