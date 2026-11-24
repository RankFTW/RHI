using RenoDXCommander;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>urti.ini editing (the URTI ⚙) and the ⚙'s value checks.</summary>
public class UrtiSettingsTests
{
    private const string Profile = "live=1\ncalibration_interval=120\ncapture_presents=8\nshadow_rays=2\nreflect_f0=0.04\nui_photo=0\nui_zone=0.012,0.69,0.16,0.97\njitter=0\nsun_measure=0\n";

    [Fact]
    public void Get_ReadsTheFirstLine_AndNullWhenMissing()
    {
        Assert.Equal("120", UrtiService.GetValueFromText(Profile, "calibration_interval"));
        Assert.Equal("0.012,0.69,0.16,0.97", UrtiService.GetValueFromText(Profile, "ui_zone"));
        Assert.Null(UrtiService.GetValueFromText(Profile, "refl_ms"));
        Assert.Null(UrtiService.GetValueFromText(Profile, "live_x")); // "live=" doesn't match a longer key
    }

    [Fact]
    public void Set_ReplacesInPlace_KeepsOtherLinesAndEndings()
    {
        var text = UrtiService.SetValueInText(Profile, "jitter", "2");
        Assert.Equal(Profile.Replace("jitter=0", "jitter=2"), text);
        var crlf = UrtiService.SetValueInText("a=1\r\nb=2\r\n", "b", "3");
        Assert.Equal("a=1\r\nb=3\r\n", crlf);
    }

    [Fact]
    public void Set_AddsMissingKey_RemovesOnNull_AndDropsDuplicates()
    {
        Assert.Equal("a=1\nrefl_ms=2\n", UrtiService.SetValueInText("a=1\n", "refl_ms", "2"));
        Assert.Equal("a=1\n", UrtiService.SetValueInText("a=1\nui_zone=0,0,1,1\n", "ui_zone", null));
        Assert.Equal("x=5\na=1\n", UrtiService.SetValueInText("x=1\na=1\nx=2\n", "x", "5"));
        Assert.Equal("k=1\n", UrtiService.SetValueInText("", "k", "1"));
    }

    [Theory]
    [InlineData("0.012,0.69,0.16,0.97", "0.012,0.69,0.16,0.97")]
    [InlineData(" 0, 0 ,1,1 ", "0,0,1,1")]
    [InlineData("", "")]
    public void Zone_AcceptsValid(string input, string expected) =>
        Assert.Equal(expected, DetailPanelBuilder.UrtiZone(input).Item1);

    [Theory]
    [InlineData("0.5,0.5,0.4,0.9")] // left after right
    [InlineData("0,0,1")]
    [InlineData("0,0,1,1.5")]
    [InlineData("a,b,c,d")]
    public void Zone_RejectsInvalid(string input) =>
        Assert.Null(DetailPanelBuilder.UrtiZone(input).Item1);

    [Theory]
    [InlineData(new[] { "0", "1" }, "1", 1)]
    [InlineData(new[] { "0", "1" }, "5", 1)]     // URTI: not 0 = on
    [InlineData(new[] { "0", "1" }, "0", 0)]
    [InlineData(new[] { "1", "2", "3", "4" }, "9", 3)] // shadow_rays clamped to 1-4
    [InlineData(new[] { "1", "2", "3", "4" }, "0", 0)]
    [InlineData(new[] { "0", "1", "2" }, "2", 2)]
    [InlineData(new[] { "0", "1", "2" }, "junk", 0)]
    public void ChoiceIndex_ReadsLikeUrti(string[] values, string value, int expected) =>
        Assert.Equal(expected, DetailPanelBuilder.UrtiChoiceIndex(values, value));
}
