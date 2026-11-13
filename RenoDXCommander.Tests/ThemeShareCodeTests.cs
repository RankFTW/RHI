using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>Theme share codes ("RHI-THEME:RRGGBB-…"): Share copies one, Import reads one.</summary>
public class ThemeShareCodeTests
{
    private const string Code = "RHI-THEME:180213-23141B-9BE1F2-C5ACE6-D2ACDD-3CCB85-B898E8-D4A856-E07070";

    [Fact]
    public void Code_HasNineColoursInFixedOrder()
    {
        var p = ThemePalette.ParseCustom(
            "Background=#180213;Panel=#23141B;Text=#9BE1F2;Accent=#C5ACE6;Buttons=#D2ACDD;Installed=#3CCB85;Update=#B898E8;Warning=#D4A856;Error=#E07070",
            ThemePalette.Default);
        Assert.Equal(Code, p.ToShareCode());
    }

    [Fact]
    public void RoundTrip_GivesTheSamePalette()
    {
        Assert.True(ThemePalette.TryParseShareCode(Code, out var p));
        Assert.Equal(Code, p.ToShareCode());
        // Border and muted text are worked out from the picked colours, as when picking them by hand
        Assert.Equal(ThemePalette.ParseCustom(p.ToCustomString(), ThemePalette.Default), p);
        Assert.Equal("#180213", ThemePalette.ToHex(p.Background));
        Assert.Equal("#E07070", ThemePalette.ToHex(p.Error));
    }

    [Theory]
    [InlineData("Try my theme! " + Code + " enjoy")]                                          // pasted with a message
    [InlineData("  rhi-theme:180213-23141b-9be1f2-c5ace6-d2acdd-3ccb85-b898e8-d4a856-e07070\r\n")] // case, spaces
    [InlineData("RHI-THEME: #180213-#23141B-#9BE1F2-#C5ACE6-#D2ACDD-#3CCB85-#B898E8-#D4A856-#E07070")] // # and a space
    public void Import_IsForgiving(string pasted)
    {
        Assert.True(ThemePalette.TryParseShareCode(pasted, out var p));
        Assert.Equal(Code, p.ToShareCode());
    }

    [Theory]
    [InlineData("")]
    [InlineData("180213-23141B-9BE1F2-C5ACE6-D2ACDD-3CCB85-B898E8-D4A856-E07070")]  // no prefix
    [InlineData("RHI-THEME:180213-23141B-9BE1F2-C5ACE6-D2ACDD-3CCB85-B898E8-D4A856")] // eight colours
    [InlineData("RHI-THEME:180213-23141B-9BE1F2-C5ACE6-D2ACDD-3CCB85-B898E8-D4A856-E07070-FFFFFF")] // ten
    [InlineData("RHI-THEME:180213-23141B-9BE1F2-C5ACE6-D2ACDD-3CCB85-B898E8-D4A856-E0707")]  // short colour
    [InlineData("RHI-THEME:180213-23141B-9BE1F2-C5ACE6-D2ACDD-3CCB85-B898E8-D4A856-GG7070")] // not hex
    [InlineData("RHI-THEME:FF180213-23141B-9BE1F2-C5ACE6-D2ACDD-3CCB85-B898E8-D4A856-E07070")] // alpha not allowed
    public void Import_RefusesBadCodes(string pasted)
    {
        Assert.False(ThemePalette.TryParseShareCode(pasted, out _));
    }
}
