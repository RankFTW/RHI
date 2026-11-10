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
    [InlineData("Grand Theft Auto V", "gta4")]       // the number has to be right
    [InlineData("Red Dead Redemption 2", "rr")]      // two characters only match initials from the start
    [InlineData("I Am Bread", "1 am")]               // a lone "I" only counts as 1 at the end of a name
    public void InitialsAndNumbersDontOverMatch(string name, string query)
    {
        Assert.False(FilterViewModel.NameMatchesLoosely(name, query));
    }

    [Theory]
    [InlineData("LEGO® Batman™: Legacy of the Dark Knight", "batman lego")]
    [InlineData("LEGO® Batman™: Legacy of the Dark Knight", "dark knight lego")]
    [InlineData("Resident Evil 4", "4 resident")]
    [InlineData("Grand Theft Auto V", "5 gta")]                 // initials work in any order too
    public void AnyWordOrder(string name, string query)
    {
        Assert.True(FilterViewModel.NameMatchesLoosely(name, query));
    }

    [Theory]
    [InlineData("Call of Duty®: Modern Warfare® III", "mw3")]   // part of the initials (codmw3)
    [InlineData("Call of Duty®: Modern Warfare® III", "mwiii")]
    [InlineData("Red Dead Redemption 2", "dr2")]
    public void PartialInitials(string name, string query)
    {
        Assert.True(FilterViewModel.NameMatchesLoosely(name, query));
    }

    [Theory]
    [InlineData("Resident Evil 4", "resident eviaa")]    // Steam's example: 2 letters off
    [InlineData("Resident Evil 4", "residentaa evilaa")] // 2 extra letters on each word
    [InlineData("Resident Evil 4", "residnt")]           // a missing letter while typing
    [InlineData("Cyberpunk 2077", "cyberpnuk")]          // swapped letters
    [InlineData("Hogwarts Legacy", "hogwarst legasy")]
    [InlineData("Hades II", "hdes")]                     // 4 letters, 1 missing
    public void TyposTolerated(string name, string query)
    {
        Assert.True(FilterViewModel.NameMatchesLoosely(name, query));
    }

    [Theory]
    [InlineData("Resident Evil 4", "resident eviaaa")]   // 3 letters off is too many
    [InlineData("Resident Evil 4", "resident dead")]     // every word has to match
    [InlineData("Lies of P", "lsei")]                    // 4 letters: only 1 letter off allowed
    [InlineData("The Witcher 3: Wild Hunt", "sea")]      // 3 letters or fewer: no typo slack ("the" isn't "sea")
    public void TyposDontOverMatch(string name, string query)
    {
        Assert.False(FilterViewModel.NameMatchesLoosely(name, query));
    }

    [Fact]
    public void FourLetterWord_OneSwapAllowed()
    {
        Assert.True(FilterViewModel.NameMatchesLoosely("Lies of P", "lise"));
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
