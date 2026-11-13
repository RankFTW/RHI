// StoreNameSuffix.cs — platform words some stores (mostly the Microsoft Store / Xbox app) add to a game's
// name, e.g. "Banishers: Ghosts of New Eden - Windows", "Game (Windows 10)", "Game (PC)", "Game (Windows PC)",
// "Game for Windows", "Game Windows", plus the edition names they keep ("Standard Edition", "Definitive Edition").
// Used as a fallback when a name lookup (PCGamingWiki data → Steam AppID, artwork) finds nothing for the
// full name. The detected name itself is never changed. Pure; tests in StoreNameSuffixTests.

using System.Text.RegularExpressions;

namespace RenoDXCommander.Services;

public static class StoreNameSuffix
{
    private const string Platform = @"(?:Windows(?:\s*(?:10|11|PC))?|Win\s*(?:10|11)|PC|Microsoft\s+Store(?:\s+Edition)?)";

    // " - Windows", " – PC", ": Windows 10", " (Windows)", " (Windows PC)", " [PC]", " for Windows 10",
    // " Windows 10" / " Windows" / " Windows PC" at the very end
    private static readonly Regex PlatformSuffix = new(
        @"\s*(?:[-–—:]\s*" + Platform + @"|\(\s*" + Platform + @"\s*\)|\[\s*" + Platform + @"\s*\]|\s+for\s+Windows(?:\s*(?:10|11))?|\s+Windows(?:\s*(?:10|11|PC))?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Edition names a store keeps in the title while PCGamingWiki files the game under its base name
    // ("Dishonored Definitive Edition" → "Dishonored", "NBA 2K26 Standard Edition" → "NBA 2K26")
    private static readonly Regex EditionSuffix = new(
        @"\s*(?:[-–—:]\s*)?(?:Standard|Definitive|Deluxe|Digital\s+Deluxe|Ultimate|Gold|Complete|Enhanced|Premium|Anniversary|Game\s+of\s+the\s+Year|GOTY)\s+Edition\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // "Resident Evil 3 (2020)": the year PCGamingWiki adds to tell same-named games apart
    private static readonly Regex YearTitle = new(@"^(?<base>.+?)\s+\((?<year>(?:19|20)\d{2})\)$", RegexOptions.CultureInvariant);

    private static readonly Regex TrialSuffix = new(
        @"\s*(?:[-–—:]\s*)?(?:\(\s*)?(?:Demo|Playtest|Prologue Demo)(?:\s*\))?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The name without a trailing platform suffix, or null when it has none.</summary>
    public static string? StripPlatform(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var stripped = PlatformSuffix.Replace(name, "").Trim();
        return stripped.Length > 0 && !stripped.Equals(name.Trim(), StringComparison.Ordinal) ? stripped : null;
    }

    /// <summary>The name without a trailing edition name ("Standard Edition", "Definitive Edition", …), or null when it has none.</summary>
    public static string? StripEdition(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var stripped = EditionSuffix.Replace(name, "").Trim().TrimEnd('-', '–', '—', ':').Trim();
        return stripped.Length > 0 && !stripped.Equals(name.Trim(), StringComparison.Ordinal) ? stripped : null;
    }

    /// <summary>For a title like "Resident Evil 3 (2020)": the name without the year, else null.</summary>
    public static string? BaseOfYearTitle(string title)
    {
        var m = YearTitle.Match(title);
        return m.Success ? m.Groups["base"].Value : null;
    }

    /// <summary>The name without a trailing "Demo" / "Playtest" (after any platform suffix), or null when it has none.</summary>
    public static string? StripTrial(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var baseName = StripPlatform(name) ?? name.Trim();
        var stripped = TrialSuffix.Replace(baseName, "").Trim();
        return stripped.Length > 0 && !stripped.Equals(baseName, StringComparison.Ordinal) ? stripped : null;
    }
}
