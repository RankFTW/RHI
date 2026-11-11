using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// UpliftService enables the "Uplift" technique (needed on OpenGL for multisampled back buffers)
/// in a game's ReShade preset without touching anything else, and removes only that again.
/// </summary>
public class UpliftPresetTests
{
    private const string Token = UpliftService.TechniqueToken;

    private const string OpenMw =
        "Techniques=lilium__rcas_hdr@lilium__rcas_hdr.fx\r\n" +
        "TechniqueSorting=lilium__blue_noise_dithering@lilium__blue_noise_dithering.fx,lilium__rcas_hdr@lilium__rcas_hdr.fx\r\n" +
        "\r\n" +
        "[INPUT]\r\n" +
        "KeyOverlay=36,0,0,0\r\n";

    [Fact]
    public void Adds_first_to_both_lists_and_keeps_the_rest()
    {
        var (text, alreadyOn, changed) = UpliftService.AddTechniqueToPreset(OpenMw);
        Assert.True(changed);
        Assert.False(alreadyOn);
        Assert.StartsWith($"Techniques={Token},lilium__rcas_hdr@lilium__rcas_hdr.fx\r\n", text);
        Assert.Contains($"TechniqueSorting={Token},lilium__blue_noise_dithering@", text);
        Assert.EndsWith("[INPUT]\r\nKeyOverlay=36,0,0,0\r\n", text);
        Assert.DoesNotContain("\n\n\n", text.Replace("\r", ""));
    }

    [Fact]
    public void Already_enabled_is_left_alone()
    {
        var (once, _, _) = UpliftService.AddTechniqueToPreset(OpenMw);
        var (twice, alreadyOn, changed) = UpliftService.AddTechniqueToPreset(once);
        Assert.True(alreadyOn);
        Assert.False(changed);
        Assert.Equal(once, twice);
    }

    [Fact]
    public void Empty_or_missing_lines_are_created_at_the_top()
    {
        var (fresh, _, changed) = UpliftService.AddTechniqueToPreset("");
        Assert.True(changed);
        Assert.Equal($"Techniques={Token}\nTechniqueSorting={Token}\n", fresh);

        var (noLists, _, _) = UpliftService.AddTechniqueToPreset("[INPUT]\nKeyOverlay=36,0,0,0\n");
        Assert.StartsWith($"Techniques={Token}\nTechniqueSorting={Token}\n[INPUT]", noLists);

        var (emptyValue, _, _) = UpliftService.AddTechniqueToPreset("Techniques=\nTechniqueSorting=\n");
        Assert.StartsWith($"Techniques={Token}\nTechniqueSorting={Token}\n", emptyValue);
    }

    [Fact]
    public void Remove_takes_out_only_uplift()
    {
        var (added, _, _) = UpliftService.AddTechniqueToPreset(OpenMw);
        var (removed, changed) = UpliftService.RemoveTechniqueFromPreset(added);
        Assert.True(changed);
        Assert.Equal(OpenMw, removed);

        var (untouched, changedAgain) = UpliftService.RemoveTechniqueFromPreset(OpenMw);
        Assert.False(changedAgain);
        Assert.Equal(OpenMw, untouched);
    }

    private const string Lp = UpliftService.LaunchpadToken;

    [Fact]
    public void Launchpad_goes_just_above_uplift()
    {
        var text = "Techniques=A@a.fx,Uplift@Uplift.fx\nTechniqueSorting=A@a.fx,Uplift@Uplift.fx,B@b.fx\n";
        var (result, alreadyOn, changed) = UpliftService.AddTechniqueToPreset(text, Lp, Token);
        Assert.False(alreadyOn);
        Assert.True(changed);
        Assert.Equal($"Techniques=A@a.fx,{Lp},Uplift@Uplift.fx\nTechniqueSorting=A@a.fx,{Lp},Uplift@Uplift.fx,B@b.fx\n", result);
    }

    [Fact]
    public void Launchpad_below_uplift_is_moved_above_it()
    {
        var text = $"Techniques=Uplift@Uplift.fx,{Lp}\nTechniqueSorting=Uplift@Uplift.fx,B@b.fx,{Lp}\n";
        var (result, alreadyOn, changed) = UpliftService.AddTechniqueToPreset(text, Lp, Token);
        Assert.True(alreadyOn);
        Assert.True(changed);
        Assert.Equal($"Techniques=Uplift@Uplift.fx,{Lp}\nTechniqueSorting={Lp},Uplift@Uplift.fx,B@b.fx\n", result);

        var (again, _, changedAgain) = UpliftService.AddTechniqueToPreset(result, Lp, Token);
        Assert.False(changedAgain);
        Assert.Equal(result, again);
    }

    [Fact]
    public void Remove_launchpad_keeps_uplift()
    {
        var (withUplift, _, _) = UpliftService.AddTechniqueToPreset(OpenMw);
        var (withBoth, _, _) = UpliftService.AddTechniqueToPreset(withUplift, Lp, Token);
        var (removed, changed) = UpliftService.RemoveTechniqueFromPreset(withBoth, Lp);
        Assert.True(changed);
        Assert.Equal(withUplift, removed);
    }
}
