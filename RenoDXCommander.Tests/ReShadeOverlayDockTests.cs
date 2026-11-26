using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Settings › ReShade › ReShade menu position: the side ReShade's menu is docked on, written into
/// reshade.ini's [OVERLAY] Docking line. The layouts below are from real reshade.ini files.
/// </summary>
public class ReShadeOverlayDockTests
{
    // ReShade's default: the menu's windows (node 1) first = left, the game's part (central node) second
    private const string LeftDocking =
        "Docking=[Docking][Data],DockSpace   ID=0xB0DF600F Window=0xCC18005E Pos=8,,8 Size=3424,,1424 Split=X,"
      + "  DockNode  ID=0x00000001 Parent=0xB0DF600F SizeRef=1203,,1440 Selected=0x495DD1F4,"
      + "  DockNode  ID=0x00000002 Parent=0xB0DF600F SizeRef=2235,,1440 CentralNode=1";

    // The same layout after dragging the menu to the right in game
    private const string RightDocking =
        "Docking=[Docking][Data],DockSpace   ID=0xB0DF600F Window=0xCC18005E Pos=8,,8 Size=3424,,1424 Split=X,"
      + "  DockNode  ID=0x00000002 Parent=0xB0DF600F SizeRef=2235,,1440 CentralNode=1,"
      + "  DockNode  ID=0x00000001 Parent=0xB0DF600F SizeRef=1203,,1440 Selected=0x495DD1F4";

    private const string WindowLine =
        "Window=[Window][###home],Pos=8,,8,Size=1203,,1424,Collapsed=0,DockId=0x00000001,,0,"
      + "[Window][RenoDX],Pos=8,,8,Size=1203,,1424,Collapsed=0,DockId=0x00000001,,7,"
      + "[Window][###editor],Collapsed=0,DockId=0x00000002";

    private static string Ini(params string[] overlayLines) =>
        "[GENERAL]\r\nEffectSearchPaths=.\\reshade-shaders\\Shaders\\**\r\n\r\n[OVERLAY]\r\nClockFormat=0\r\n"
        + string.Join("\r\n", overlayLines) + "\r\nTutorialProgress=4\r\n\r\n[SCREENSHOT]\r\nFileFormat=1\r\n";

    private static string Line(string ini, string key) =>
        ini.Replace("\r\n", "\n").Split('\n').Single(l => l.StartsWith(key + "="));

    // ── An existing layout is turned round ───────────────────────────────────

    [Fact]
    public void Left_ToRight_SwapsTheTwoHalves()
    {
        var result = ReShadeOverlayDock.SetSideInText(Ini(LeftDocking, WindowLine), "Right");

        Assert.Equal(RightDocking, Line(result, "Docking"));
    }

    [Fact]
    public void Right_ToLeft_SwapsThemBack()
    {
        var result = ReShadeOverlayDock.SetSideInText(Ini(RightDocking, WindowLine), "Left");

        Assert.Equal(LeftDocking, Line(result, "Docking"));
    }

    [Fact]
    public void OnlyTheDockingLineChanges()
    {
        var before = Ini(LeftDocking, WindowLine);
        var after = ReShadeOverlayDock.SetSideInText(before, "Right");

        var b = before.Split("\r\n");
        var a = after.Split("\r\n");
        Assert.Equal(b.Length, a.Length);
        for (int i = 0; i < b.Length; i++)
            if (!b[i].StartsWith("Docking=")) Assert.Equal(b[i], a[i]);
    }

    [Theory]
    [InlineData("Left")]
    [InlineData("Right")]
    public void AlreadyOnThatSide_IsUnchanged(string side)
    {
        var ini = Ini(side == "Left" ? LeftDocking : RightDocking, WindowLine);

        Assert.Same(ini, ReShadeOverlayDock.SetSideInText(ini, side));
    }

    [Theory]
    [InlineData("Default")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Sideways")]
    public void Default_OrAnUnknownValue_NeverChangesAFile(string? side)
    {
        var ini = Ini(LeftDocking, WindowLine);

        Assert.Same(ini, ReShadeOverlayDock.SetSideInText(ini, side));
    }

    [Fact]
    public void LineEndings_AreKept()
    {
        var lf = Ini(LeftDocking, WindowLine).Replace("\r\n", "\n");

        var result = ReShadeOverlayDock.SetSideInText(lf, "Right");

        Assert.DoesNotContain("\r", result);
        Assert.Equal(RightDocking, Line(result, "Docking"));
    }

    [Fact]
    public void AHalfThatIsSplitAgain_MovesAsAWhole()
    {
        // The menu half split top / bottom by the user: its two children go with it
        var nested =
            "Docking=[Docking][Data],DockSpace     ID=0xB0DF600F Pos=8,,8 Size=3424,,1424 Split=X,"
          + "  DockNode    ID=0x00000001 Parent=0xB0DF600F SizeRef=1203,,1440 Split=Y,"
          + "    DockNode  ID=0x00000003 Parent=0x00000001 SizeRef=1203,,700 Selected=0x495DD1F4,"
          + "    DockNode  ID=0x00000004 Parent=0x00000001 SizeRef=1203,,738 Selected=0x327798EA,"
          + "  DockNode    ID=0x00000002 Parent=0xB0DF600F SizeRef=2235,,1440 CentralNode=1";

        var result = Line(ReShadeOverlayDock.SetSideInText(Ini(nested), "Right"), "Docking");

        Assert.Equal(
            "Docking=[Docking][Data],DockSpace     ID=0xB0DF600F Pos=8,,8 Size=3424,,1424 Split=X,"
          + "  DockNode    ID=0x00000002 Parent=0xB0DF600F SizeRef=2235,,1440 CentralNode=1,"
          + "  DockNode    ID=0x00000001 Parent=0xB0DF600F SizeRef=1203,,1440 Split=Y,"
          + "    DockNode  ID=0x00000003 Parent=0x00000001 SizeRef=1203,,700 Selected=0x495DD1F4,"
          + "    DockNode  ID=0x00000004 Parent=0x00000001 SizeRef=1203,,738 Selected=0x327798EA",
            result);
    }

    // ── Layouts the user arranged another way are left alone ─────────────────

    [Theory]
    // The menu undocked into a loose window: no split at all
    [InlineData("Docking=[Docking][Data],DockNode  ID=0x00000001 Pos=120,,90 Size=900,,700 Selected=0x495DD1F4,DockSpace ID=0xB0DF600F Window=0xCC18005E Pos=8,,8 Size=3424,,1424 CentralNode=1")]
    // The menu docked along the top: a top / bottom split
    [InlineData("Docking=[Docking][Data],DockSpace   ID=0xB0DF600F Pos=8,,8 Size=3424,,1424 Split=Y,  DockNode  ID=0x00000001 Parent=0xB0DF600F SizeRef=3424,,400 Selected=0x495DD1F4,  DockNode  ID=0x00000002 Parent=0xB0DF600F SizeRef=3424,,1024 CentralNode=1")]
    public void OtherLayouts_AreNotTouched(string docking)
    {
        var ini = Ini(docking, WindowLine);

        Assert.Same(ini, ReShadeOverlayDock.SetSideInText(ini, "Right"));
        Assert.Same(ini, ReShadeOverlayDock.SetSideInText(ini, "Left"));
    }

    // ── A file with no layout yet ────────────────────────────────────────────

    [Theory]
    [InlineData(false)] // a new reshade.ini from RHI's template: no Docking line
    [InlineData(true)]  // ReShade ran but its menu was never opened: "Docking=[Docking][Data]"
    public void NoLayout_Left_IsUnchanged_BecauseReShadeStartsOnTheLeft(bool emptyDockingLine)
    {
        var ini = emptyDockingLine ? Ini("Docking=[Docking][Data]") : Ini();

        Assert.Same(ini, ReShadeOverlayDock.SetSideInText(ini, "Left"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoLayout_Right_WritesTheLayout_WithTheGameHalfFirst(bool emptyDockingLine)
    {
        var ini = emptyDockingLine ? Ini("Docking=[Docking][Data]") : Ini();

        var result = ReShadeOverlayDock.SetSideInText(ini, "Right");
        var nodes = ReShadeOverlayDock.SplitList(Line(result, "Docking")["Docking=".Length..]);

        Assert.Equal("[Docking][Data]", nodes[0]);
        Assert.StartsWith("DockSpace", nodes[1]);
        Assert.Contains("ID=0xB0DF600F", nodes[1]);
        Assert.Contains("Pos=", nodes[1]);  // ImGui ignores a root dock space without Pos and Size
        Assert.Contains("Size=", nodes[1]);
        Assert.Contains("Split=X", nodes[1]);
        Assert.Contains("CentralNode=1", nodes[2]);                       // game half on the left
        Assert.Contains("ID=0x00000001 Parent=0xB0DF600F", nodes[3]);     // menu half on the right
        Assert.Equal(4, nodes.Count);
    }

    [Fact]
    public void NoLayout_Right_DocksReShadesWindowsAndKnownAddons_AsReShadesDefaultDoes()
    {
        var result = ReShadeOverlayDock.SetSideInText(Ini(), "Right");
        var window = Line(result, "Window");

        foreach (var name in new[] { "###home", "###addons", "###settings", "###statistics", "###log", "###about", "RenoDX", "ReLimiter" })
            Assert.Contains($"[Window][{name}],Collapsed=0,DockId=0x00000001,,", window);
        Assert.Contains("[Window][###editor],Collapsed=0,DockId=0x00000002", window);
    }

    [Fact]
    public void NoLayout_Right_KeepsWindowsAlreadySaved()
    {
        // ReLimiter's on-screen display position, saved before the menu was ever opened
        var saved = "Window=[Window][##limiter_osd],Pos=1564,,0,Size=356,,168,Collapsed=0";

        var window = Line(ReShadeOverlayDock.SetSideInText(Ini("Docking=[Docking][Data]", saved), "Right"), "Window");

        Assert.StartsWith(saved + ",", window);
        Assert.Contains("[Window][###home],Collapsed=0,DockId=0x00000001,,0", window);
    }

    [Fact]
    public void NoLayout_Right_ThenReadBack_IsStable()
    {
        var once = ReShadeOverlayDock.SetSideInText(Ini(), "Right");

        Assert.Same(once, ReShadeOverlayDock.SetSideInText(once, "Right"));
        // and Left turns the written layout round like any other
        Assert.Contains("CentralNode=1", ReShadeOverlayDock.SplitList(Line(ReShadeOverlayDock.SetSideInText(once, "Left"), "Docking"))[^1]);
    }

    [Fact]
    public void NoOverlaySection_Right_AddsOne()
    {
        var ini = "[GENERAL]\r\nEffectSearchPaths=.\r\n";

        var result = ReShadeOverlayDock.SetSideInText(ini, "Right");

        Assert.StartsWith("[GENERAL]\r\nEffectSearchPaths=.\r\n", result);
        Assert.Contains("\r\n[OVERLAY]\r\nDocking=[Docking][Data],DockSpace", result);
        Assert.Contains("\r\nWindow=[Window][###home]", result);
    }

    // ── The list format ──────────────────────────────────────────────────────

    [Fact]
    public void SplitList_TreatsADoubledCommaAsPartOfTheItem()
    {
        var items = ReShadeOverlayDock.SplitList("[Window][###home],Pos=8,,8,Size=1203,,1424,Collapsed=0");

        Assert.Equal(new[] { "[Window][###home]", "Pos=8,,8", "Size=1203,,1424", "Collapsed=0" }, items);
    }

    [Theory]
    [InlineData("left", "Left")]
    [InlineData("RIGHT", "Right")]
    [InlineData("", "Default")]
    [InlineData(null, "Default")]
    [InlineData("Centre", "Default")]
    public void Normalize(string? value, string expected)
        => Assert.Equal(expected, ReShadeOverlayDock.Normalize(value));

    [Fact]
    public void Setting_IsSavedAndLoaded()
    {
        var vm = new RenoDXCommander.ViewModels.SettingsViewModel();
        Assert.Equal("Default", vm.RsOverlaySide);

        vm.LoadSettingsFromDict(new Dictionary<string, string> { ["RsOverlaySide"] = "right" });
        Assert.Equal("Right", vm.RsOverlaySide);
    }
}
