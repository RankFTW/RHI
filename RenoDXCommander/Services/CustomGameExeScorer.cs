// CustomGameExeScorer.cs -- picks the game's exe in a folder found by the custom game folders scan.
// Pure static logic so it can be unit tested on temp folders: enumerate exes (bounded depth/count,
// redist/installer/bonus folders skipped, helper exes excluded by name), score each one, rank.

using System.Text.RegularExpressions;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>What the PE reader found in an exe. Fake / unreadable files give (false, false).</summary>
public readonly record struct ExeProbe(bool HasGraphicsImports, bool IsConsole);

/// <summary>Facts about one exe that the score is computed from (no disk access).</summary>
public sealed record ExeFacts(
    string FileName,
    string RelativePath,
    long SizeBytes,
    bool NextToUnityPlayer,
    bool HasGraphicsImports,
    bool IsConsole,
    string FolderName);

/// <summary>Ranked exes for a folder; <see cref="IsAmbiguous"/> when the top two are close.</summary>
public sealed record ExeRanking(IReadOnlyList<ScoredExe> Exes, bool IsAmbiguous);

public static class CustomGameExeScorer
{
    public const int MaxDepth = 4;
    public const int MaxDirectories = 1500;
    public const int MaxExes = 300;

    /// <summary>Top two within this many points = ambiguous ("Check the exe").</summary>
    public const int AmbiguityMargin = 15;

    /// <summary>Folders that never hold the game's exe (installers, redistributables, anti-cheat, tools).</summary>
    private static readonly HashSet<string> s_skipFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "redist", "_redist", "__redist", "redists", "_commonredist", "commonredist", "redistributables", "redistributable",
        "directx", "directx9", "dxsetup", "vcredist", "vc_redist", "support", "_support", "__support",
        "__installer", "_installer", "installer", "installers", "install", "setup", "prerequisites", "prereqs", "prereq",
        "easyanticheat", "battleye", "physx", "dotnet", "dotnetfx", "netfx", "thirdparty", "crashreporter", "crashpad",
        "monobleedingedge", "uninstall", "_uninstall", "__macosx", "dlc_installers",
    };

    /// <summary>Helper / installer exes that are never the game (matched on the name without ".exe").</summary>
    private static readonly Regex s_excludedName = new(
        @"^(unins\d*|uninst.*|uninstall.*|setup.*|.*_setup|vc_?redist.*|vcredist.*|dxsetup|dxwebsetup|directx.*|" +
        @"unitycrashhandler.*|crashreportclient.*|crashreporter.*|crashpad_handler|crashsender.*|crashhandler.*|bugsplat.*|" +
        @"easyanticheat.*|eac_?launcher|eacsetup|beservice.*|battleye.*|be_?launcher|" +
        @"ue4prereqsetup.*|ueprereqsetup.*|ue\dprereqsetup.*|dotnetfx.*|ndp\d.*|netfx.*|.*redist.*|" +
        @"quicksfv|7z|7za|7zg|7zfm|7zr|unrar|rar|winrar|unarc|isdone|oalinst|physx.*|" +
        @"epiconlineservices.*|epicwebhelper|ubisoftconnectinstaller|upc|uplaywebcore|socialclub.*|" +
        @"steamerrorreporter.*|steamservice|steam_?install.*|cefsharp\.browsersubprocess|cefprocess|qtwebengineprocess|" +
        @"zfgamebrowser|ffmpeg|ffprobe|python\w*|wermgr|dxdiag|installer.*|install|touchup|cleanup|dowser|" +
        @"gogalaxy.*|galaxyclient.*|vulkan.*runtime.*|vulkanrt.*|openal.*|xnafx.*|xnafx\d+_redist)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Exe names that are usually not the game itself, but can be (penalised, not excluded).</summary>
    private static readonly string[] s_unlikelyWords =
    {
        "server", "editor", "config", "settings", "benchmark", "crash", "report", "helper", "updater",
        "dedicated", "modmanager", "tool", "patcher", "sdk", "console", "debug",
    };

    /// <summary>Trailing tags stripped from exe names before comparing them with the folder name.</summary>
    private static readonly Regex s_exeTagStrip = new(
        @"([_\-\s.]?(shipping|win64|wingdk|win32|x64|x86|dx\d+|d3d\d+|vulkan|vk|steam|gog|epic|egs|retail|64bit|32bit|64|32))+$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsSkippedFolderName(string name)
        => !string.IsNullOrEmpty(name)
           && (name.StartsWith('.') || name.StartsWith('$')
               || s_skipFolders.Contains(name) || GameDetectionService.IsSkippedFolderName(name));

    /// <summary>True for helper / installer exe file names (with or without ".exe").</summary>
    public static bool IsExcludedExeName(string fileName)
    {
        var stem = fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? fileName[..^4] : fileName;
        return s_excludedName.IsMatch(stem);
    }

    /// <summary>
    /// Exe files under <paramref name="root"/>, up to <see cref="MaxDepth"/> folders down, skipping
    /// redist/installer/bonus folders and excluded helper exes. Bounded so a huge folder can't hang.
    /// </summary>
    public static List<string> EnumerateExes(string root, CancellationToken ct = default)
    {
        var result = new List<string>();
        var queue = new Queue<(string dir, int depth)>();
        queue.Enqueue((root, 0));
        int dirs = 0;
        while (queue.Count > 0 && dirs < MaxDirectories && result.Count < MaxExes)
        {
            ct.ThrowIfCancellationRequested();
            var (dir, depth) = queue.Dequeue();
            dirs++;
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir, "*.exe"))
                {
                    if (IsExcludedExeName(Path.GetFileName(f))) continue;
                    result.Add(f);
                    if (result.Count >= MaxExes) break;
                }
                if (depth >= MaxDepth) continue;
                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    var name = Path.GetFileName(sub);
                    if (IsSkippedFolderName(name)) continue;
                    try
                    {
                        // Don't follow junctions / symlinks (loops, other drives)
                        if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0) continue;
                    }
                    catch (IOException) { continue; }
                    catch (UnauthorizedAccessException) { continue; }
                    queue.Enqueue((sub, depth + 1));
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                CrashReporter.Log($"[CustomGameExeScorer.EnumerateExes] No access to '{dir}' — {ex.Message}");
            }
            catch (IOException ex)
            {
                CrashReporter.Log($"[CustomGameExeScorer.EnumerateExes] Can't read '{dir}' — {ex.Message}");
            }
        }
        return result;
    }

    /// <summary>Lower-case letters and digits only.</summary>
    internal static string Norm(string s) => Regex.Replace(s.ToLowerInvariant(), "[^a-z0-9]", "");

    /// <summary>Scores one exe. Higher = more likely the game. Returns the score and a short reason list.</summary>
    public static (int Score, string Reasons) Score(ExeFacts f)
    {
        int score = 0;
        var why = new List<string>();
        var stem = Path.GetFileNameWithoutExtension(f.FileName);
        var rel = f.RelativePath.Replace('/', '\\');
        var parts = rel.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        int depth = parts.Length - 1;

        bool inBinaries = parts.Length >= 3
            && parts[^3].Equals("Binaries", StringComparison.OrdinalIgnoreCase)
            && (parts[^2].Equals("Win64", StringComparison.OrdinalIgnoreCase)
             || parts[^2].Equals("WinGDK", StringComparison.OrdinalIgnoreCase)
             || parts[^2].Equals("Win32", StringComparison.OrdinalIgnoreCase));
        bool inEngineFolder = parts.Length > 1 && parts[0].Equals("Engine", StringComparison.OrdinalIgnoreCase);

        if (stem.EndsWith("Shipping", StringComparison.OrdinalIgnoreCase)) { score += 100; why.Add("Unreal shipping exe"); }
        if (inBinaries && !inEngineFolder) { score += 60; why.Add("Binaries folder"); }
        if (f.NextToUnityPlayer) { score += 80; why.Add("Unity player"); }
        if (f.HasGraphicsImports) { score += 40; why.Add("uses a graphics API"); }

        var exeNorm = Norm(s_exeTagStrip.Replace(stem, ""));
        var folderNorm = Norm(f.FolderName);
        if (exeNorm.Length > 0 && folderNorm.Length > 0)
        {
            if (exeNorm == folderNorm) { score += 40; why.Add("name matches folder"); }
            else if (exeNorm.Length >= 3 && folderNorm.Length >= 3
                     && (folderNorm.Contains(exeNorm) || exeNorm.Contains(folderNorm)))
            { score += 20; why.Add("name like folder"); }
        }

        var stemNorm = Norm(stem);
        if (stemNorm.Contains("launcher")) { score -= 60; why.Add("launcher"); }
        else if (s_unlikelyWords.Any(w => stemNorm.Contains(w))) { score -= 30; why.Add("helper-like name"); }
        if (f.IsConsole) { score -= 50; why.Add("console program"); }
        if (inEngineFolder) { score -= 40; why.Add("inside Engine folder"); }
        if (f.SizeBytes < 150 * 1024) { score -= 30; why.Add("very small"); }

        // Deeper exes are less likely, except the known engine layouts
        if (!inBinaries && !f.NextToUnityPlayer && depth > 0) score -= 4 * depth;

        // File size: tie-breaker only (0..10)
        double mb = f.SizeBytes / (1024.0 * 1024.0);
        score += (int)Math.Min(10, Math.Round(Math.Log2(1 + mb) * 1.5));

        return (score, string.Join(", ", why));
    }

    /// <summary>Reads what the scorer needs from a real exe; tolerant of non-PE files.</summary>
    public static ExeProbe ProbeExe(string path)
    {
        bool console = false, graphics = false;
        try { console = IsConsoleSubsystem(path); }
        catch (IOException ex) { CrashReporter.Log($"[CustomGameExeScorer.ProbeExe] '{path}' — {ex.Message}"); }
        catch (UnauthorizedAccessException ex) { CrashReporter.Log($"[CustomGameExeScorer.ProbeExe] '{path}' — {ex.Message}"); }
        try { graphics = GraphicsApiDetector.DetectAllApis(path).Count > 0; }
        catch (Exception ex) { CrashReporter.Log($"[CustomGameExeScorer.ProbeExe] Import read failed '{path}' — {ex.Message}"); }
        return new ExeProbe(graphics, console);
    }

    /// <summary>True when the PE optional header says IMAGE_SUBSYSTEM_WINDOWS_CUI. False for non-PE files.</summary>
    public static bool IsConsoleSubsystem(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var dos = new byte[64];
        if (fs.Read(dos, 0, 64) < 64 || dos[0] != 'M' || dos[1] != 'Z') return false;
        int peOffset = BitConverter.ToInt32(dos, 0x3C);
        if (peOffset <= 0 || peOffset > fs.Length - 100) return false;
        fs.Seek(peOffset, SeekOrigin.Begin);
        var hdr = new byte[24 + 70];
        if (fs.Read(hdr, 0, hdr.Length) < hdr.Length) return false;
        if (hdr[0] != 'P' || hdr[1] != 'E' || hdr[2] != 0 || hdr[3] != 0) return false;
        // Subsystem sits at offset 68 of the optional header in both PE32 and PE32+
        ushort subsystem = BitConverter.ToUInt16(hdr, 24 + 68);
        return subsystem == 3;
    }

    /// <summary>
    /// Ranks the exes in <paramref name="root"/>, best first. <paramref name="folderName"/> is the
    /// (cleaned) game name the exe names are compared with; defaults to the folder's own name.
    /// <paramref name="probe"/> reads PE details; defaults to <see cref="ProbeExe"/>.
    /// </summary>
    public static ExeRanking Rank(string root, string? folderName = null,
        Func<string, ExeProbe>? probe = null, CancellationToken ct = default)
    {
        probe ??= ProbeExe;
        var name = folderName ?? Path.GetFileName(root.TrimEnd('\\', '/'));
        var list = new List<ScoredExe>();
        foreach (var exe in EnumerateExes(root, ct))
        {
            ct.ThrowIfCancellationRequested();
            long size = 0;
            try { size = new FileInfo(exe).Length; }
            catch (IOException) { /* size stays 0: scored as very small */ }
            catch (UnauthorizedAccessException) { /* same */ }
            var dir = Path.GetDirectoryName(exe) ?? root;
            bool unity = File.Exists(Path.Combine(dir, "UnityPlayer.dll")) || File.Exists(Path.Combine(dir, "GameAssembly.dll"));
            var p = probe(exe);
            var rel = Path.GetRelativePath(root, exe);
            var (score, reasons) = Score(new ExeFacts(Path.GetFileName(exe), rel, size, unity, p.HasGraphicsImports, p.IsConsole, name));
            list.Add(new ScoredExe(exe, rel, size, score, reasons));
        }
        var ranked = list
            .OrderByDescending(e => e.Score)
            .ThenByDescending(e => e.SizeBytes)
            .ThenBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        bool ambiguous = ranked.Count >= 2 && ranked[0].Score - ranked[1].Score < AmbiguityMargin;
        return new ExeRanking(ranked, ambiguous);
    }
}
