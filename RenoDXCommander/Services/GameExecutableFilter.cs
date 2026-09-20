using System.Text.RegularExpressions;

namespace RenoDXCommander.Services;

/// <summary>What an executable found inside a candidate game folder most likely is.</summary>
public enum ExecutableKind
{
    /// <summary>A plausible game executable.</summary>
    Game,
    /// <summary>A launcher / configuration front-end. Only used when nothing better exists.</summary>
    Launcher,
    /// <summary>A dedicated-server build. Only used when no client executable exists.</summary>
    Server,
    /// <summary>Installer, uninstaller, crash reporter, redistributable, updater, anti-cheat, helper, tool…</summary>
    NonGame,
}

/// <summary>
/// Classifies executables and folder names for the custom-folder scanner.
/// All rules are data (name fragments / folder names) so they are easy to extend:
/// add a fragment to the relevant list and it applies everywhere.
/// Names are compared in "compact" form: lower-case with everything except a-z/0-9 removed,
/// so "UnityCrashHandler64.exe", "unity_crash_handler.exe" and "Unity Crash Handler.exe" behave the same.
/// </summary>
public static class GameExecutableFilter
{
    // Fragments that identify executables which are never the game itself.
    // Chosen to be specific: e.g. "crash" alone is NOT listed because it would exclude "Crash Bandicoot".
    private static readonly string[] NonGameFragments =
    {
        // installers / uninstallers / redistributables
        "uninstall", "setup", "installer", "redist", "dxwebsetup", "oalinst", "dotnetfx", "netfx",
        "prereq", "vcredist", "physxinstall", "xnafx",
        // crash / error reporting
        "crashreport", "crashhandler", "crashpad", "crashsender", "crashdump", "crashrpt",
        "crashmonitor", "bugreport", "bugsplat", "errorreport", "createdump", "werfault",
        // updaters
        "updater", "autoupdate", "selfupdate", "patcher", "liveupdate",
        // anti-cheat services / installers
        "easyanticheat", "beservice", "battleye", "bedaisy", "punkbuster",
        // embedded browser / helper processes
        "helper", "cefsubprocess", "cefprocess", "cefsharp", "subprocess", "webview2",
        // mod managers and generic tools
        "modmanager", "modorganizer", "nexusmodmanager",
    };

    // Whole-name matches (compact form) for executables that are clearly not games.
    private static readonly HashSet<string> NonGameExact = new(StringComparer.OrdinalIgnoreCase)
    {
        "update", "patch", "install", "dxdiag", "7z", "7za", "7zg", "unrar", "ffmpeg", "ffprobe",
        "curl", "wget", "python", "pythonw", "notepad", "cmd", "powershell", "conhost", "msiexec",
        "rundll32", "regsvr32", "dotnet", "crashpad", "eac", "mo2",
    };

    // Prefixes that mark a system / runtime package rather than a game.
    private static readonly string[] NonGamePrefixes = { "unins", "directx", "dotnet", "windowsdesktopruntime", "vulkanrt" };

    // Fragments for launcher-style front-ends: valid only when no better executable exists.
    private static readonly string[] LauncherFragments =
        { "launcher", "bootstrap", "starter", "config", "settings", "options", "selector" };

    // Fragments for dedicated servers: valid only when no client executable exists.
    private static readonly string[] ServerFragments = { "dedicated", "server" };

    // Folder names never searched for game executables (redistributables, engine internals,
    // anti-cheat payloads, bonus content…). Compared case-insensitively against the folder name.
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "redist", "_redist", "redistributable", "redistributables", "_commonredist", "commonredist",
        "vcredist", "directx", "dxsetup", "dotnet", ".net", "physx", "prerequisites", "prereq", "prereqs",
        "installer", "installers", "__installer", "engine", "thirdparty", "3rdparty",
        "crashreport", "crashreportclient", "crashpad", "easyanticheat", "easyanticheat_eos",
        "battleye", "anticheat", "monobleedingedge", "il2cpp",
        // bonus content that ships its own executables (mirrors GameDetectionService's skip list)
        "artbook", "art book", "artbooks", "digitalartbook", "soundtrack", "ost", "music",
        "manual", "manuals", "docs", "documentation", "bonus", "bonuscontent", "bonus content",
        "extras", "wallpapers", "wallpaper",
        // debug builds of the same game
        "debug", "_dbg", "dbg",
    };

    // Folders that are never descended into while looking for games (OS / tooling folders).
    private static readonly HashSet<string> SystemLikeDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "windows", "$recycle.bin", "system volume information", "recovery", "config.msi", "msocache",
        "perflogs", "windowsapps", "programdata", "appdata", "node_modules", ".git", ".svn", "__macosx",
    };

    // Generic binary folders: a game whose executable lives in one of these is rooted one level up.
    private static readonly HashSet<string> GenericBinaryFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "bin32", "bin64", "binaries", "win64", "win32", "wingdk", "x64", "x86", "game", "release",
        "retail", "shipping", "app", "client", "windows", "build", "final", "pc", "win", "exe",
    };

    // Splits "S7GameUpdate", "PatchUpdate64", "game_update" and "Game Update" into words so a whole-word "update"
    // is found in every spelling, without excluding games that merely contain the letters (e.g. "Updated Realm").
    private static readonly Regex WordBoundary = new(
        @"[^A-Za-z0-9]+|(?<=[a-z])(?=[A-Z])|(?<=[A-Za-z])(?=[0-9])|(?<=[0-9])(?=[A-Za-z])", RegexOptions.Compiled);

    /// <summary>Lower-case name without extension, punctuation and spaces removed.</summary>
    public static string Compact(string fileNameOrPath)
    {
        var name = Path.GetFileNameWithoutExtension(fileNameOrPath) ?? "";
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var ch in name)
            if (char.IsAsciiLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString();
    }

    /// <summary>Classifies an executable by file name. Order matters: NonGame wins over Launcher over Server.</summary>
    public static ExecutableKind Classify(string fileNameOrPath)
    {
        var raw = Path.GetFileNameWithoutExtension(fileNameOrPath) ?? "";
        var c = Compact(fileNameOrPath);
        if (c.Length == 0) return ExecutableKind.NonGame;

        if (NonGameExact.Contains(c)) return ExecutableKind.NonGame;
        foreach (var p in NonGamePrefixes)
            if (c.StartsWith(p, StringComparison.Ordinal)) return ExecutableKind.NonGame;
        foreach (var f in NonGameFragments)
            if (c.Contains(f, StringComparison.Ordinal)) return ExecutableKind.NonGame;
        if (WordBoundary.Split(raw).Any(w => w.Equals("update", StringComparison.OrdinalIgnoreCase))) return ExecutableKind.NonGame;

        foreach (var f in LauncherFragments)
            if (c.Contains(f, StringComparison.Ordinal)) return ExecutableKind.Launcher;
        foreach (var f in ServerFragments)
            if (c.Contains(f, StringComparison.Ordinal)) return ExecutableKind.Server;

        return ExecutableKind.Game;
    }

    /// <summary>True for executables that can plausibly be launched as a game (Game, Launcher or Server kinds).</summary>
    public static bool IsUsable(string fileNameOrPath) => Classify(fileNameOrPath) != ExecutableKind.NonGame;

    /// <summary>Folder whose executables must be ignored (redistributables, Engine, anti-cheat, bonus content…).</summary>
    public static bool IsExcludedDirectory(string folderName) => ExcludedDirectories.Contains(folderName);

    /// <summary>Folder the scanner must not descend into at all (OS / tooling folders, dot-folders).</summary>
    public static bool IsSystemLikeDirectory(string folderName)
        => SystemLikeDirectories.Contains(folderName) || folderName.StartsWith('.');

    /// <summary>bin / Win64 / x64 / Game … — folders that are a game's binary folder rather than its name.</summary>
    public static bool IsGenericBinaryFolder(string folderName) => GenericBinaryFolders.Contains(folderName);

    /// <summary>True for Unreal-style shipping executables (Game-Win64-Shipping.exe, Game-Shipping.exe…).</summary>
    public static bool IsUnrealShippingExe(string fileNameOrPath)
        => (Path.GetFileNameWithoutExtension(fileNameOrPath) ?? "")
            .EndsWith("-Shipping", StringComparison.OrdinalIgnoreCase);
}
