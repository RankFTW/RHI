// StoreNameSuffix.cs — platform words some stores (mostly the Microsoft Store / Xbox app) add to a game's
// name, e.g. "Banishers: Ghosts of New Eden - Windows", "Game (Windows 10)", "Game (PC)", "Game for Windows".
// Used as a fallback when a name lookup (PCGamingWiki data → Steam AppID, artwork) finds nothing for the
// full name. The detected name itself is never changed. Pure; tests in StoreNameSuffixTests.

using System.Text.RegularExpressions;

namespace RenoDXCommander.Services;

public static class StoreNameSuffix
{
    private const string Platform = @"(?:Windows(?:\s*(?:10|11))?|Win\s*(?:10|11)|PC|Microsoft\s+Store(?:\s+Edition)?)";

    // " - Windows", " – PC", ": Windows 10", " (Windows)", " [PC]", " for Windows 10", " Windows 10" at the very end
    private static readonly Regex PlatformSuffix = new(
        @"\s*(?:[-–—:]\s*" + Platform + @"|\(\s*" + Platform + @"\s*\)|\[\s*" + Platform + @"\s*\]|\s+for\s+Windows(?:\s*(?:10|11))?|\s+Windows\s*(?:10|11))\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

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

    /// <summary>The name without a trailing "Demo" / "Playtest" (after any platform suffix), or null when it has none.</summary>
    public static string? StripTrial(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var baseName = StripPlatform(name) ?? name.Trim();
        var stripped = TrialSuffix.Replace(baseName, "").Trim();
        return stripped.Length > 0 && !stripped.Equals(baseName, StringComparison.Ordinal) ? stripped : null;
    }
}
