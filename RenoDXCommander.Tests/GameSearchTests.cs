using RenoDXCommander.ViewModels;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>Game search ignores punctuation and spaces in names, like Steam ("fear" finds F.E.A.R.).</summary>
public class GameSearchTests
{
    [Theory]
    [InlineData("F.E.A.R. 3", "fear")]
    [InlineData("F.E.A.R. 3", "FEAR 3")]
    [InlineData("F.E.A.R. 3", "fear3")]
    [InlineData("Half-Life 2", "half life")]
    [InlineData("Marvel's Spider-Man Remastered", "spiderman")]
    [InlineData("S.T.A.L.K.E.R. 2: Heart of Chornobyl", "stalker 2")]
    [InlineData("Borderlands® 4", "borderlands 4")]
    [InlineData("Tom Clancy's Rainbow Six® Siege", "tom clancys")]
    public void PunctuationIgnored(string name, string query)
    {
        Assert.True(FilterViewModel.NameMatchesLoosely(name, query));
    }

    [Theory]
    [InlineData("Grand Theft Auto III", "gta 3")]
    [InlineData("Grand Theft Auto III", "gta3")]
    [InlineData("Grand Theft Auto III", "grand theft auto 3")]
    [InlineData("Grand Theft Auto V", "gta5")]
    [InlineData("FINAL FANTASY VII REBIRTH", "final fantasy 7")]
    [InlineData("Final Fantasy XVI", "ff16")]
    public void RomanNumeralsMatchNumbers(string name, string query)
    {
        Assert.True(FilterViewModel.NameMatchesLoosely(name, query));
    }

    [Theory]
    [InlineData("Grand Theft Auto V", "gta")]
    [InlineData("Grand Theft Auto III", "gtaiii")]
    [InlineData("FINAL FANTASY VII REBIRTH", "ff7")]
    [InlineData("Final Fantasy X", "ffx")]
    [InlineData("Half-Life 2", "hl2")]
    [InlineData("Resident Evil 4", "re4")]
    [InlineData("Marvel's Spider-Man 2", "msm")]
    [InlineData("Red Dead Redemption 2", "rdr2")]
    public void InitialsMatch(string name, string query)
    {
        Assert.True(FilterViewModel.NameMatchesLoosely(name, query));
    }

    [Theory]
    [InlineData("Grand Theft Auto V", "gta4")]       // initials must match from the start, with the right number
    [InlineData("Red Dead Redemption 2", "dr2")]     // not from the middle of the initials
    [InlineData("I Am Bread", "1 am")]               // a lone "I" only counts as 1 at the end of a name
    public void InitialsAndNumbersDontOverMatch(string name, string query)
    {
        Assert.False(FilterViewModel.NameMatchesLoosely(name, query));
    }

    [Theory]
    [InlineData("F.E.A.R. 3", "fear 4")]
    [InlineData("Portal 2", "p")]      // one letter: too loose, the normal search covers it
    [InlineData("Portal 2", ".")]      // nothing left to match
    public void NoFalseMatches(string name, string query)
    {
        Assert.False(FilterViewModel.NameMatchesLoosely(name, query));
    }
}
