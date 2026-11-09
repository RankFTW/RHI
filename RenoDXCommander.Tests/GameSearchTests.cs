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
    public void PunctuationIgnored(string name, string query)
    {
        Assert.True(FilterViewModel.NameMatchesLoosely(name, query));
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
