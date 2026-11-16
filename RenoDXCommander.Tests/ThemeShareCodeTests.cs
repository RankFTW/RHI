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
    [InlineData("RHI-THEME:180213-23141B-9BE1F2-C5ACE6-D2ACDD-3CCB85-B898E8-D4A856-E07070-FFFFFF-FFFFFF-FFFFFF")] // twelve
    [InlineData("RHI-THEME:180213-23141B-9BE1F2-C5ACE6-D2ACDD-3CCB85-B898E8-D4A856-E0707")]  // short colour
    [InlineData("RHI-THEME:180213-23141B-9BE1F2-C5ACE6-D2ACDD-3CCB85-B898E8-D4A856-GG7070")] // not hex
    [InlineData("RHI-THEME:FF180213-23141B-9BE1F2-C5ACE6-D2ACDD-3CCB85-B898E8-D4A856-E07070")] // alpha not allowed
    public void Import_RefusesBadCodes(string pasted)
    {
        Assert.False(ThemePalette.TryParseShareCode(pasted, out _));
    }

    [Fact]
    public void ButtonText_IsAnOptionalTenthColour()
    {
        Assert.True(ThemePalette.TryParseShareCode(Code + "-FFEEDD", out var p));
        Assert.Equal("#FFEEDD", ThemePalette.ToHex(p.ButtonText!.Value));
        Assert.Equal(Code + "-FFEEDD", p.ToShareCode());
        Assert.Equal(p, ThemePalette.ParseCustom(p.ToCustomString(), ThemePalette.Default));
    }

    [Fact]
    public void FooterText_IsAnOptionalEleventhColour()
    {
        Assert.True(ThemePalette.TryParseShareCode(Code + "--AABBCC", out var p));
        Assert.Null(p.ButtonText);
        Assert.Equal("#AABBCC", ThemePalette.ToHex(p.FooterText!.Value));
        Assert.Equal(Code + "--AABBCC", p.ToShareCode());
        Assert.Equal(p, ThemePalette.ParseCustom(p.ToCustomString(), ThemePalette.Default));

        Assert.True(ThemePalette.TryParseShareCode(Code + "-FFEEDD-AABBCC", out var both));
        Assert.Equal(Code + "-FFEEDD-AABBCC", both.ToShareCode());
    }

    [Fact]
    public void FooterText_RecoloursOnlyTheFooter()
    {
        var ft = ThemePalette.Hex("#FFFF00");
        var m = new ThemeColorMapper(ThemePalette.Default.With("FooterText", ft));
        Assert.Equal(ft, m.Map(ThemePalette.FooterTextSource));
        // Other dim text (TextDisabled) is left alone
        Assert.Equal(ThemePalette.Hex("#404858"), m.Map(ThemePalette.Hex("#404858")));
        Assert.DoesNotContain("FooterText", ThemePalette.Default.ToCustomString());
    }

    [Fact]
    public void ButtonText_UnsetFollowsButtons()
    {
        Assert.True(ThemePalette.TryParseShareCode(Code, out var p));
        Assert.Null(p.ButtonText);
        Assert.Equal(p.Buttons, p.Get("ButtonText"));
        Assert.DoesNotContain("ButtonText", p.ToCustomString());
    }

    [Fact]
    public void ButtonText_RecoloursOnlyTheButtonTextColour()
    {
        var bt = ThemePalette.Hex("#FFFFFF");
        var withText = ThemePalette.Default.With("Buttons", ThemePalette.Hex("#E8A070")).With("ButtonText", bt);
        var without = ThemePalette.Default.With("Buttons", ThemePalette.Hex("#E8A070"));
        var m = new ThemeColorMapper(withText);
        var m0 = new ThemeColorMapper(without);
        Assert.Equal(bt, m.Map(ThemePalette.Hex("#7AACDD")));
        // Button background and border still follow Buttons
        Assert.Equal(m0.Map(ThemePalette.Hex("#182840")), m.Map(ThemePalette.Hex("#182840")));
        Assert.Equal(m0.Map(ThemePalette.Hex("#2A4468")), m.Map(ThemePalette.Hex("#2A4468")));
    }
}
