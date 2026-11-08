// ScreenshotLibraryService.cs -- finds a game's ReShade screenshots for the RHI overlay.
// Folder: the game's reshade.ini [SCREENSHOT] SavePath (relative paths are from the game folder), else the
// folder RHI would set (Settings › ReShade › Screenshot folder, per game when that's on), else the game folder.
// ReShade names screenshots "<exe name> <date> <time>", so a folder shared by several games is filtered by
// the game's exe name.
namespace RenoDXCommander.Services;

internal static class ScreenshotLibraryService
{
    internal static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".bmp", ".jxr" };

    /// <summary>The folder the game's ReShade screenshots go to, or null when none exists.</summary>
    internal static string? FindFolder(string? installPath, string? rhiFolder)
    {
        var candidates = new List<string?>();
        if (!string.IsNullOrEmpty(installPath))
            candidates.Add(ReadIniSavePath(installPath));
        candidates.Add(rhiFolder);
        candidates.Add(installPath);
        foreach (var c in candidates)
        {
            if (string.IsNullOrWhiteSpace(c)) continue;
            try { if (Directory.Exists(c)) return Path.GetFullPath(c); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException) { }
        }
        return null;
    }

    /// <summary>[SCREENSHOT] SavePath from the game's reshade.ini, made absolute; null when not set.</summary>
    internal static string? ReadIniSavePath(string gameDir)
    {
        try
        {
            var ini = Path.Combine(gameDir, "reshade.ini");
            if (!File.Exists(ini)) return null;
            var value = ParseSavePath(File.ReadAllLines(ini));
            if (string.IsNullOrWhiteSpace(value)) return null;
            value = value.Trim().Trim('"');
            return Path.IsPathRooted(value) ? value : Path.GetFullPath(Path.Combine(gameDir, value));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            CrashReporter.Log($"[ScreenshotLibraryService.ReadIniSavePath] {ex.Message}");
            return null;
        }
    }

    /// <summary>SavePath in the [SCREENSHOT] section of reshade.ini lines, or null.</summary>
    internal static string? ParseSavePath(IEnumerable<string> lines)
    {
        bool inSection = false;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith('[')) { inSection = line.Equals("[SCREENSHOT]", StringComparison.OrdinalIgnoreCase); continue; }
            if (!inSection) continue;
            int eq = line.IndexOf('=');
            if (eq > 0 && line.Substring(0, eq).Trim().Equals("SavePath", StringComparison.OrdinalIgnoreCase))
                return line.Substring(eq + 1).Trim();
        }
        return null;
    }

    /// <summary>
    /// The game's screenshots in <paramref name="folder"/>, newest first. Files starting with the exe name when
    /// there are any (ReShade's naming); otherwise every screenshot, but only in a folder that's the game's own.
    /// </summary>
    internal static List<FileInfo> List(string folder, string? exePath, bool folderIsGamesOwn)
    {
        var files = new List<FileInfo>();
        try
        {
            foreach (var f in new DirectoryInfo(folder).EnumerateFiles())
                if (Extensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase)) files.Add(f);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            CrashReporter.Log($"[ScreenshotLibraryService.List] {ex.Message}");
            return files;
        }
        return Filter(files, exePath, folderIsGamesOwn).OrderByDescending(f => f.LastWriteTimeUtc).ToList();
    }

    internal static IEnumerable<FileInfo> Filter(List<FileInfo> files, string? exePath, bool folderIsGamesOwn)
    {
        var exeName = string.IsNullOrEmpty(exePath) ? null : Path.GetFileNameWithoutExtension(exePath);
        if (!string.IsNullOrEmpty(exeName))
        {
            var mine = files.Where(f => MatchesExe(f.Name, exeName)).ToList();
            if (mine.Count > 0) return mine;
        }
        return folderIsGamesOwn ? files : Enumerable.Empty<FileInfo>();
    }

    /// <summary>"Game 2026-11-08 21-30-00.png" belongs to Game.exe (name, then a space or the end).</summary>
    internal static bool MatchesExe(string fileName, string exeName) =>
        fileName.StartsWith(exeName, StringComparison.OrdinalIgnoreCase)
        && (fileName.Length == exeName.Length || fileName[exeName.Length] is ' ' or '_' or '.' or '-');
}
