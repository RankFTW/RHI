using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>HDR Gaming Database README parsing (HTML table since Nov 2026, Markdown list before).</summary>
public class HdrDatabaseTests
{
    private const string Base = "https://github.com/KoKlusz/HDR-Gaming-Database/discussions/";

    [Fact]
    public void Parse_HtmlTable()
    {
        var readme = "<table>\n"
            + $"<tr><td><a href=\"{Base}144\">007 First Light</a></td><td align=\"center\"></td></tr>\n"
            + $"<tr><td><a href=\"{Base}12\">Alan Wake 2</a></td><td align=\"center\">⭐</td></tr>\n"
            + $"<tr><td><a href=\"{Base}77\">Ratchet &amp; Clank Rift Apart</a></td><td align=\"center\"></td></tr>\n"
            + "</table>\n";
        var db = HdrDatabaseService.Parse(readme);
        Assert.Equal(3, db.Count);
        Assert.Equal(Base + "12", db["Alan Wake 2"]);
        Assert.Equal(Base + "77", db["Ratchet & Clank Rift Apart"]);
    }

    [Fact]
    public void Parse_MarkdownList_StillWorks()
    {
        var readme = $"* [Cyberpunk 2077]({Base}5) ⭐\n  * [Marvel’s Spider-Man™]({Base}9)\n";
        var db = HdrDatabaseService.Parse(readme);
        Assert.Equal(Base + "5", db["Cyberpunk 2077"]);
        Assert.Equal(Base + "9", db["Marvel's Spider-Man"]); // quotes straightened, ™ removed
    }

    [Fact]
    public void Parse_IgnoresOtherLinks_AndKeepsFirstDuplicate()
    {
        var readme = "<a href=\"https://github.com/KoKlusz/HDR-Gaming-Database\">Repo</a>\n"
            + $"<tr><td><a href=\"{Base}1\">Game</a></td></tr>\n"
            + $"<tr><td><a href=\"{Base}2\">GAME</a></td></tr>\n";
        var db = HdrDatabaseService.Parse(readme);
        Assert.Single(db);
        Assert.Equal(Base + "1", db["game"]);
    }

    [Fact]
    public void Parse_Empty() => Assert.Empty(HdrDatabaseService.Parse(""));
}
