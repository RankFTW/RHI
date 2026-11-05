// CustomGameNameCleaner.cs -- turns a downloaded game's folder name ("Hades.v1.38290-GOG",
// "Hollow Knight [Repack]") into a presentable game name for the custom game folders scan.

using System.Text.RegularExpressions;

namespace RenoDXCommander.Services;

public static class CustomGameNameCleaner
{
    private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    /// <summary>Release groups / distribution words seen in folder names. Removed as whole words.</summary>
    private static readonly string[] s_tagWords =
    {
        "GOG", "GOG Galaxy", "Repack", "FitGirl", "FitGirl Repack", "DODI", "DODI Repack", "ElAmigos", "KaOs", "Xatab",
        "CODEX", "PLAZA", "SKIDROW", "RELOADED", "EMPRESS", "TENOKE", "RUNE", "FLT", "DARKSiDERS", "RAZOR1911",
        "CPY", "HOODLUM", "PROPHET", "TiNYiSO", "SiMPLEX", "DOGE", "ANOMALY", "GoldBerg", "P2P", "Chronos", "DINOByTES",
        "Portable", "DRM Free", "DRM-Free", "DRMFree", "Steam Rip", "SteamRip", "Pre-Installed", "PreInstalled",
        "Unlocked", "Cracked", "Crack", "x64", "x86", "Win64", "Win32", "Multi\\d+", "MULTi\\d+",
    };

    private static readonly Regex s_brackets = new(@"\[[^\]]*\]|\([^)]*\)|\{[^}]*\}", Opts);
    private static readonly Regex s_trailingGroup = new(
        @"[-_.](" + string.Join("|", s_tagWords.Select(w => w.Replace(" ", "[ ._-]?"))) + @")$", Opts);
    // Dotted abbreviations like S.T.A.L.K.E.R. keep their dots
    private static readonly Regex s_dottedAbbrev = new(@"\b(?:[A-Za-z]\.){2,}[A-Za-z]?\.?", Opts);
    private static readonly Regex s_version = new(@"(?<![A-Za-z0-9])v\s?\d+(?:[._]\d+)*[a-z]?(?![A-Za-z0-9])", Opts);
    private static readonly Regex s_bareVersion = new(@"(?<![A-Za-z0-9])\d+(?:\.\d+){2,}(?![A-Za-z0-9])", Opts);
    private static readonly Regex s_build = new(@"(?<![A-Za-z0-9])(?:build|b)[\s._-]?\d{3,}(?![A-Za-z0-9])", Opts);
    private static readonly Regex s_words = new(
        @"(?<![A-Za-z0-9])(" + string.Join("|", s_tagWords.Select(w => w.Replace(" ", "[ ._-]?"))) + @")(?![A-Za-z0-9])", Opts);

    /// <summary>Cleans a folder name into a game name. Returns the input trimmed if cleaning removes everything.</summary>
    public static string Clean(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName)) return "";
        var s = folderName.Trim();

        s = s_brackets.Replace(s, " ");
        // Repeated: "Game.v1.2-GOG" style names can end with several tags
        for (int i = 0; i < 3; i++)
        {
            var before = s;
            s = s_trailingGroup.Replace(s.Trim(), "");
            if (s == before) break;
        }

        s = s_dottedAbbrev.Replace(s, m => m.Value.Replace('.', '\u0001'));
        s = s_version.Replace(s, " ");
        s = s_bareVersion.Replace(s, " ");
        s = s_build.Replace(s, " ");
        s = s.Replace('.', ' ').Replace('_', ' ').Replace('\u0001', '.');
        s = s_words.Replace(s, " ");

        // Leftover separators ("Game - ", "Game -  Deluxe") and spacing
        s = Regex.Replace(s, @"\s+", " ").Trim();
        s = Regex.Replace(s, @"(\s[-–+&]\s?)+$", "").Trim();
        s = s.Trim('-', '–', '+', ' ', ',');

        s = DragDropHandler.CleanFolderName(s);
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return s.Length > 0 ? s : folderName.Trim();
    }

    /// <summary>Folder names that say nothing about the game (the scan falls back to the exe / Unreal project name).</summary>
    public static bool IsGenericFolderName(string name)
    {
        var n = CustomGameExeScorer.Norm(name);
        return n.Length < 2 || n is "game" or "games" or "bin" or "binaries" or "win64" or "win32" or "windows"
            or "windowsnoeditor" or "app" or "client" or "pc" or "data" or "install" or "files";
    }
}
