using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>OptiScaler.ini "All Settings": reading options from the comments and writing edits back.</summary>
public class OsIniEditorTests
{
    private static readonly string[] Sample =
    {
        "; -------------------------------------------------------",
        "[Upscalers]",
        "; -------------------------------------------------------",
        "; Select Upscaler for Dx11 games",
        "; fsr22 (native DX11), xess (native DX11, Arc only), fsr31_12 (dx11on12, FSR4), dlss - Default (auto) is fsr22",
        "Dx11Upscaler=auto",
        "",
        "[FrameGen]",
        "; Enables Frame Generation",
        "; true or false - Default (auto) is false",
        "Enabled=auto",
        "",
        "; Selected FG Output",
        "; nofg, fsrfg, xefg, nukems - Default (auto) is nofg",
        "FGOutput=F5flow",
        "",
        "; Frametime source",
        "; 0: Input | 1: Time between Present calls | 2: Zero (Only for XeFG)",
        "; true or false - Default (auto) is 0",
        "FTInput=auto",
        "",
        "; Defines FG rectangle",
        "; integer value - Default (auto) is whole screen",
        "RectLeft=auto",
        "RectTop=auto",
        "",
        "; 1 to 3 - Default (auto) is 1",
        "AllowedFrameAhead=auto",
        "",
        "; 0.0 - 1.0 - Default (auto) is 0.3",
        "FPTVarianceFactor=auto",
    };

    private static OsIniSetting Get(List<OsIniSetting> list, string key) => list.Single(s => s.Key == key);

    [Fact]
    public void Parse_ReadsOptionsFromComments()
    {
        var list = OsIniEditor.Parse(Sample);

        var up = Get(list, "Dx11Upscaler");
        Assert.Equal(OsIniKind.Choice, up.Kind);
        Assert.Equal(new[] { "auto", "fsr22", "xess", "fsr31_12", "dlss" }, up.Options.Select(o => o.Value));
        Assert.Equal("xess (native DX11, Arc only)", up.Options[2].Label); // comma inside brackets isn't a split
        Assert.Equal("fsr22", up.Default);

        var en = Get(list, "Enabled");
        Assert.Equal(OsIniKind.Bool, en.Kind);
        Assert.Equal(new[] { "auto", "true", "false" }, en.Options.Select(o => o.Value));

        var fg = Get(list, "FGOutput");
        Assert.Equal("F5flow", fg.Value); // value the build doesn't list is kept as is
        Assert.Equal("FrameGen", fg.Section);
    }

    [Fact]
    public void Parse_NumberedLineWinsOverWrongBoolLine()
    {
        var ft = Get(OsIniEditor.Parse(Sample), "FTInput");
        Assert.Equal(OsIniKind.Choice, ft.Kind);
        Assert.Equal(new[] { "auto", "0", "1", "2" }, ft.Options.Select(o => o.Value));
    }

    [Fact]
    public void Parse_RangesTextAndSharedComments()
    {
        var list = OsIniEditor.Parse(Sample);
        Assert.Equal(new[] { "auto", "1", "2", "3" }, Get(list, "AllowedFrameAhead").Options.Select(o => o.Value));
        Assert.Equal(OsIniKind.Text, Get(list, "FPTVarianceFactor").Kind);
        Assert.Equal(OsIniKind.Text, Get(list, "RectLeft").Kind);
        Assert.Equal(Get(list, "RectLeft").Help, Get(list, "RectTop").Help); // keys in a row share the comment
    }

    [Fact]
    public void Merge_UsesBuildOptionsAndAddsMissingKeys()
    {
        var game = OsIniEditor.Parse(new[] { "[FrameGen]", "; old comment", "FGOutput=fsrfg" });
        var build = OsIniEditor.Parse(new[]
        {
            "[FrameGen]", "; fsrfg, F5flow - Default (auto) is fsrfg", "FGOutput=auto",
            "; true or false - Default (auto) is false", "NewKey=auto",
        });
        var merged = OsIniEditor.Merge(game, build);

        var fg = Get(merged, "FGOutput");
        Assert.Equal("fsrfg", fg.Value);
        Assert.Contains(fg.Options, o => o.Value == "F5flow");
        var added = Get(merged, "NewKey");
        Assert.False(added.InFile);
        Assert.Equal("auto", added.Value);
    }

    [Fact]
    public void ApplyValues_ChangesOnlyThoseLines()
    {
        var lines = new[] { "[FrameGen]", "; comment", "Enabled=auto", "FGOutput = auto", "", "[Other]", "X=1" };
        var result = OsIniEditor.ApplyValues(lines, new[]
        {
            ("FrameGen", "fgoutput", "F5flow"),  // key matched ignoring case, file's spelling kept
            ("FrameGen", "NewKey", "true"),      // added after the section's last key
            ("Missing", "K", "v"),               // new section at the end
        });

        Assert.Equal(new[]
        {
            "[FrameGen]", "; comment", "Enabled=auto", "FGOutput =F5flow", "NewKey=true", "", "[Other]", "X=1",
            "", "[Missing]", "K=v",
        }, result);
    }

    [Theory]
    [InlineData("  F5 super ini  ", "F5 super ini")]
    [InlineData("a/b:c*?", "abc")]
    [InlineData("name...", "name")]
    [InlineData("   ", null)]
    [InlineData("<>|", null)]
    public void LibraryCleanName(string input, string? expected) =>
        Assert.Equal(expected, OsIniLibrary.CleanName(input));
}
