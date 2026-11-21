using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>"Changed since …" in What's New: finding a version's section and the lines added since an earlier copy.</summary>
public class PatchNotesChangesTests
{
    private const string Beta2Notes =
        "Token tip\n\n" +
        "## v2.9.6 Beta 2\n\n" +
        "### New\n- Artwork from Steam.\n\n" +
        "### Changes\n- ReLimiter v4 in every game.\n\n" +
        "### Bug Fixes\n- Fixed art for Ubisoft names.\n\n" +
        "## v2.9.5\n\n### New\n- Backup and restore.\n";

    private const string Beta3Notes =
        "Token tip\n\n" +
        "## v2.9.6 Beta 3\n\n" +
        "### New\n- Artwork from Steam.\n\n" +
        "### Changes\n**General**\n- ReLimiter v4 in every game.\n- ReShade cog has Game overrides.\n\n" +
        "### Bug Fixes\n- Fixed art for Ubisoft names.\n- Fixed the DC DLL name not clearing.\n\n" +
        "## v2.9.5\n\n### New\n- Backup and restore.\n";

    [Fact]
    public void FindSection_Matches_By_Number_Whatever_The_Beta()
    {
        var s = PatchNotesChanges.FindSection(Beta3Notes, "2.9.6");
        Assert.NotNull(s);
        Assert.Equal("v2.9.6 Beta 3", s!.Value.Heading);
        Assert.Contains("Game overrides", s.Value.Body);
        Assert.DoesNotContain("Backup and restore", s.Value.Body);
    }

    [Fact]
    public void FindSection_Does_Not_Match_A_Hotfix_Number()
    {
        const string notes = "## v2.9.6.1\n- Hotfix.\n\n## v2.9.6\n- Release.\n";
        var s = PatchNotesChanges.FindSection(notes, "2.9.6");
        Assert.Equal("v2.9.6", s!.Value.Heading);
        Assert.Equal("v2.9.6.1", PatchNotesChanges.FindSection(notes, "2.9.6.1")!.Value.Heading);
        Assert.Null(PatchNotesChanges.FindSection(notes, "2.9.7"));
    }

    [Fact]
    public void NewLines_Lists_Only_Added_Lines_Under_Their_Heading()
    {
        var before = PatchNotesChanges.FindSection(Beta2Notes, "2.9.6")!.Value.Body;
        var now = PatchNotesChanges.FindSection(Beta3Notes, "2.9.6")!.Value.Body;
        var lines = PatchNotesChanges.NewLines(before, now);
        Assert.Equal(2, lines.Count);
        Assert.Equal(("Changes", "- ReShade cog has Game overrides."), lines[0]);
        Assert.Equal(("Bug Fixes", "- Fixed the DC DLL name not clearing."), lines[1]);
    }

    [Fact]
    public void NewLines_Ignores_Spacing_And_Lists_Reworded_Lines()
    {
        var lines = PatchNotesChanges.NewLines("- Old  wording.\n- Same.\n", "-  Old wording.\n- Same.\n- Same, reworded.\n");
        Assert.Single(lines);
        Assert.Equal("- Same, reworded.", lines[0].Line);
    }

    [Fact]
    public void BuildBlock_Groups_Lines_And_Is_Null_When_Nothing_Changed()
    {
        Assert.Null(PatchNotesChanges.BuildBlock(new List<(string?, string)>(), "2.9.6 beta 2"));
        var block = PatchNotesChanges.BuildBlock(new List<(string?, string)>
        {
            ("Changes", "- A."), ("Changes", "- B."), ("Bug Fixes", "- C."),
        }, "2.9.6 beta 2")!;
        Assert.StartsWith("### Changed since 2.9.6 beta 2", block);
        Assert.Equal(1, CountOf(block, "**Changes**"));
        Assert.Contains("**Bug Fixes**\n\n- C.", block);
    }

    [Fact]
    public void InsertBlock_Goes_Under_The_Version_Heading()
    {
        var result = PatchNotesChanges.InsertBlock("## v2.9.6 Beta 3\n\n### New\n- X.", "### Changed since 2.9.6 beta 2\n\n- Y.");
        Assert.StartsWith("## v2.9.6 Beta 3\n\n### Changed since 2.9.6 beta 2", result);
        Assert.True(result.IndexOf("---") < result.IndexOf("### New"));
    }

    private static int CountOf(string text, string part)
    {
        int n = 0, i = 0;
        while ((i = text.IndexOf(part, i, StringComparison.Ordinal)) >= 0) { n++; i += part.Length; }
        return n;
    }
}
