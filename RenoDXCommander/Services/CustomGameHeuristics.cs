using System.Text.RegularExpressions;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>An executable found inside a candidate game folder.</summary>
public sealed record GameExeInfo(string Path, long Size, ExecutableKind Kind, int Depth);

/// <summary>
/// File-system heuristics used by <see cref="CustomFolderScanService"/>: which executable is the
/// real game, what to call the game, and how confident we are that a folder is a game at all.
/// Engine detection itself is NOT re-implemented here — the scanner asks
/// <see cref="IGameDetectionService.DetectEngineAndPath"/> and passes the result in.
/// </summary>
public static class CustomGameHeuristics
{
    private const int MaxExecutablesPerGame = 400;
    private const long MinPlausibleExeBytes = 200 * 1024;

    private static readonly EnumerationOptions SafeEnumeration = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        // Junctions / symlinks can loop back into the tree; system files are never game content.
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
    };

    private static readonly string[] DataFolderHints =
    {
        "data", "assets", "content", "resources", "paks", "media", "movies", "videos", "sound", "sounds",
        "audio", "textures", "levels", "maps", "localization", "shaders", "streamingassets",
    };

    private static readonly HashSet<string> DataFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pak", ".dat", ".bik", ".bk2", ".wem", ".bank", ".assets", ".pck", ".vpk", ".wad", ".arc",
        ".big", ".bsa", ".ba2", ".forge", ".cpk", ".rpf", ".ucas", ".utoc",
    };

    // ── Enumeration helpers ───────────────────────────────────────────────────────

    /// <summary>Child directories, skipping reparse points and anything unreadable. Never throws.</summary>
    public static IEnumerable<string> SafeDirectories(string dir)
    {
        try { return Directory.EnumerateDirectories(dir, "*", SafeEnumeration).ToList(); }
        catch (Exception) { return Array.Empty<string>(); }
    }

    /// <summary>Files matching <paramref name="pattern"/> directly inside <paramref name="dir"/>. Never throws.</summary>
    public static IEnumerable<string> SafeFiles(string dir, string pattern)
    {
        try { return Directory.EnumerateFiles(dir, pattern, SafeEnumeration).ToList(); }
        catch (Exception) { return Array.Empty<string>(); }
    }

    /// <summary>
    /// Collects executables below <paramref name="gameRoot"/> (up to <see cref="GameDetectionService.MaxScanDepth"/>
    /// levels), skipping redistributable / Engine / anti-cheat / bonus-content folders.
    /// </summary>
    public static List<GameExeInfo> CollectExecutables(string gameRoot, CancellationToken ct = default)
    {
        var result = new List<GameExeInfo>();
        var queue = new Queue<(string dir, int depth)>();
        queue.Enqueue((gameRoot, 0));
        while (queue.Count > 0 && result.Count < MaxExecutablesPerGame)
        {
            ct.ThrowIfCancellationRequested();
            var (dir, depth) = queue.Dequeue();
            foreach (var file in SafeFiles(dir, "*.exe"))
            {
                long size = 0;
                try { size = new FileInfo(file).Length; } catch (Exception) { }
                result.Add(new GameExeInfo(file, size, GameExecutableFilter.Classify(file), depth));
            }
            if (depth >= GameDetectionService.MaxScanDepth) continue;
            foreach (var sub in SafeDirectories(dir))
            {
                var name = System.IO.Path.GetFileName(sub);
                if (GameExecutableFilter.IsExcludedDirectory(name) || GameExecutableFilter.IsSystemLikeDirectory(name)) continue;
                queue.Enqueue((sub, depth + 1));
            }
        }
        return result;
    }

    // ── Choosing the real executable ──────────────────────────────────────────────

    /// <summary>
    /// Picks the executable that is most likely the game itself.
    /// Order of preference: Unreal *-Win64-Shipping.exe / *-Shipping.exe → the exe sitting next to
    /// UnityPlayer.dll (or a matching *_Data folder) → the exe in the folder engine detection resolved →
    /// exe named like the game folder → shallower, then larger. Launchers and dedicated servers only
    /// win when nothing better exists. Returns null when there is no usable executable at all.
    /// </summary>
    public static GameExeInfo? PickExecutable(string gameRoot, IReadOnlyList<GameExeInfo> exes, string? engineResolvedFolder = null)
    {
        var usable = exes.Where(e => e.Kind != ExecutableKind.NonGame).ToList();
        if (usable.Count == 0) return null;

        // 1. Unreal shipping executables (client builds first, then anything shipping).
        var shipping = usable.Where(e => GameExecutableFilter.IsUnrealShippingExe(e.Path)).ToList();
        if (shipping.Count > 0)
        {
            return shipping
                .OrderBy(e => (int)e.Kind)
                .ThenBy(e => ShippingRank(e.Path))
                .ThenByDescending(e => e.Size)
                .First();
        }

        // 2. Unity: exe next to UnityPlayer.dll or with a sibling <name>_Data folder.
        var unity = usable.Where(e => IsUnityExe(e.Path)).ToList();
        if (unity.Count > 0)
        {
            return unity
                .OrderBy(e => (int)e.Kind)
                .ThenByDescending(e => HasDataFolderFor(e.Path))
                .ThenByDescending(e => e.Size)
                .First();
        }

        // 3. Generic ranking.
        var folderName = System.IO.Path.GetFileName(gameRoot.TrimEnd('\\', '/')) ?? "";
        // Tiny executables are stubs (Control.exe next to Control_DX12.exe, dlc-toggler.exe next to Game\Bin\TS4_x64.exe),
        // so they rank last; among real executables a name match wins, then the biggest, then the shallowest.
        return usable
            .OrderBy(e => (int)e.Kind)
            .ThenByDescending(e => engineResolvedFolder != null &&
                CustomFolderPaths.AreSame(System.IO.Path.GetDirectoryName(e.Path), engineResolvedFolder))
            .ThenBy(e => e.Size < MinPlausibleExeBytes)
            .ThenByDescending(e => NameMatches(e.Path, folderName))
            .ThenByDescending(e => e.Size)
            .ThenBy(e => e.Depth)
            .First();
    }

    private static int ShippingRank(string path)
    {
        var n = System.IO.Path.GetFileNameWithoutExtension(path);
        if (n.EndsWith("-Win64-Shipping", StringComparison.OrdinalIgnoreCase)) return 0;
        if (n.EndsWith("-WinGDK-Shipping", StringComparison.OrdinalIgnoreCase)) return 1;
        return 2;
    }

    /// <summary>True when the executable sits next to UnityPlayer.dll or a matching &lt;name&gt;_Data folder.</summary>
    public static bool IsUnityExe(string exePath)
    {
        var dir = System.IO.Path.GetDirectoryName(exePath);
        if (string.IsNullOrEmpty(dir)) return false;
        try { return File.Exists(System.IO.Path.Combine(dir, "UnityPlayer.dll")) || HasDataFolderFor(exePath); }
        catch (Exception) { return false; }
    }

    private static bool HasDataFolderFor(string exePath)
    {
        var dir = System.IO.Path.GetDirectoryName(exePath);
        if (string.IsNullOrEmpty(dir)) return false;
        try { return Directory.Exists(System.IO.Path.Combine(dir, System.IO.Path.GetFileNameWithoutExtension(exePath) + "_Data")); }
        catch (Exception) { return false; }
    }

    /// <summary>True when the executable's name and the folder name contain one another (ignoring case/punctuation, min 3 chars).</summary>
    public static bool NameMatches(string exePath, string folderName)
    {
        var e = GameExecutableFilter.Compact(exePath);
        var f = GameExecutableFilter.Compact(folderName);
        return e.Length >= 3 && f.Length >= 3 && (e == f || f.Contains(e) || e.Contains(f));
    }

    // ── Wrapper folders ───────────────────────────────────────────────────────────

    /// <summary>
    /// Many games are laid out as "Game Name\Sources\Game.exe" or "Game Name\windows_content\Game Name.exe".
    /// When the executable is named after a parent folder rather than the folder it sits in, the parent
    /// (still inside the configured folder, never the configured folder itself) is the real game root.
    /// </summary>
    public static string LiftToWrapperFolder(string gameRoot, string scanRoot, string exePath)
    {
        var root = gameRoot;
        while (true)
        {
            var parent = System.IO.Path.GetDirectoryName(root.TrimEnd('\\', '/'));
            if (string.IsNullOrEmpty(parent)
                || CustomFolderPaths.AreSame(parent, scanRoot)
                || !CustomFolderPaths.IsSameOrUnder(parent, scanRoot))
                break;

            var parentName = System.IO.Path.GetFileName(parent) ?? "";
            var rootName = System.IO.Path.GetFileName(root.TrimEnd('\\', '/')) ?? "";
            if (NameMatches(exePath, parentName) && !NameMatches(exePath, rootName))
                root = parent;
            else
                break;
        }
        return root;
    }

    // ── Naming ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Display name from the folder name. A generic binary folder (bin, Win64…) borrows its parent's
    /// name; underscores become spaces when the name has no spaces; trailing [tags] are dropped.
    /// </summary>
    public static string InferName(string gameRoot, string? exePath = null)
    {
        var dir = gameRoot.TrimEnd('\\', '/');
        var name = System.IO.Path.GetFileName(dir);
        for (int i = 0; i < 3 && !string.IsNullOrEmpty(name) && GameExecutableFilter.IsGenericBinaryFolder(name); i++)
        {
            var parent = System.IO.Path.GetDirectoryName(dir);
            if (string.IsNullOrEmpty(parent)) break;
            dir = parent;
            name = System.IO.Path.GetFileName(dir);
        }
        if (string.IsNullOrWhiteSpace(name))
            name = exePath != null ? System.IO.Path.GetFileNameWithoutExtension(exePath) : dir;

        name = StripSceneReleaseName(name);
        if (!name.Contains(' ') && name.Contains('_')) name = name.Replace('_', ' ');
        name = Regex.Replace(name, @"\s*\[[^\]]*\]\s*$", "");
        name = Regex.Replace(name, @"\s+", " ").Trim(' ', '-', '.', '_');
        return name.Length > 0 ? name : (System.IO.Path.GetFileName(gameRoot.TrimEnd('\\', '/')) ?? gameRoot);
    }

    // Release-group tags that scene/repack folders end with ("Title.Words-GROUP"). Only these are stripped when the
    // name has no dots, so real hyphenated titles ("Half-Life", "X-COM") are never touched.
    private static readonly HashSet<string> ReleaseGroups = new(StringComparer.OrdinalIgnoreCase)
    {
        "InsaneRamZes", "GoldBerg", "CODEX", "SKIDROW", "PLAZA", "TENOKE", "RUNE", "FLT", "DARKSiDERS", "RAZOR",
        "RAZOR1911", "HOODLUM", "EMPRESS", "DODI", "ElAmigos", "TiNYiSO", "KaOs", "P2P", "Chronos", "ANOMALY",
    };

    private static readonly Regex DottedReleaseName = new(@"^(?<title>[A-Za-z0-9']+(?:\.[A-Za-z0-9']+)+)-(?<group>[A-Za-z0-9]+)$", RegexOptions.Compiled);
    private static readonly Regex HyphenReleaseName = new(@"^(?<title>[A-Za-z0-9']+)-(?<group>[A-Za-z0-9]+)$", RegexOptions.Compiled);

    /// <summary>
    /// "Gears.of.War.Reloaded-InsaneRamZes" → "Gears of War Reloaded", "inZOI-InsaneRamZes" → "inZOI".
    /// Names in the wiki/manifest are matched by title, so leaving the release tag in would break that match.
    /// </summary>
    public static string StripSceneReleaseName(string name)
    {
        var dotted = DottedReleaseName.Match(name);
        if (dotted.Success) return dotted.Groups["title"].Value.Replace('.', ' ');

        var hyphen = HyphenReleaseName.Match(name);
        if (hyphen.Success && ReleaseGroups.Contains(hyphen.Groups["group"].Value)) return hyphen.Groups["title"].Value;
        return name;
    }

    // ── Confidence ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Rates how likely the folder is a real game. Engine markers (from the existing engine detection,
    /// or Unreal shipping / Unity layout) are decisive; otherwise supporting signals must add up
    /// (exe size, exe named like the folder, store SDK DLLs, typical game data files).
    /// </summary>
    public static (CustomCandidateConfidence confidence, string reason) Rate(
        string gameRoot, EngineType engine, GameExeInfo? exe)
    {
        if (exe == null) return (CustomCandidateConfidence.Low, "no usable executable found");
        if (exe.Kind == ExecutableKind.Launcher) return (CustomCandidateConfidence.Low, "only a launcher-style executable found");
        if (exe.Kind == ExecutableKind.Server) return (CustomCandidateConfidence.Low, "only a dedicated-server executable found");
        if (exe.Size < MinPlausibleExeBytes) return (CustomCandidateConfidence.Low, "executable is very small");

        if (engine is EngineType.Unreal or EngineType.UnrealLegacy or EngineType.Unity or EngineType.REEngine)
            return (CustomCandidateConfidence.High, $"{engine} engine detected");
        if (GameExecutableFilter.IsUnrealShippingExe(exe.Path)) return (CustomCandidateConfidence.High, "Unreal shipping executable");
        if (IsUnityExe(exe.Path)) return (CustomCandidateConfidence.High, "Unity layout");

        int score = 0;
        var why = new List<string>();
        if (exe.Size >= 1024 * 1024) { score += 1; why.Add("exe >= 1 MB"); }
        if (exe.Size >= 20L * 1024 * 1024) { score += 1; why.Add("exe >= 20 MB"); }
        if (exe.Size >= 40L * 1024 * 1024) { score += 1; why.Add("exe >= 40 MB"); }   // utilities are rarely this big
        if (exe.Size >= 60L * 1024 * 1024) { score += 1; why.Add("exe >= 60 MB"); }
        if (NameMatches(exe.Path, System.IO.Path.GetFileName(gameRoot.TrimEnd('\\', '/')) ?? "")) { score += 2; why.Add("exe named like folder"); }
        if (HasGameRuntimeDll(System.IO.Path.GetDirectoryName(exe.Path)) || HasGameRuntimeDll(gameRoot)) { score += 2; why.Add("store SDK / game middleware dll"); }
        if (CountDataHints(gameRoot) >= 2) { score += 1; why.Add("game data files"); }

        return score >= 3
            ? (CustomCandidateConfidence.High, string.Join(", ", why))
            : (CustomCandidateConfidence.Low, why.Count > 0 ? "weak signals: " + string.Join(", ", why) : "no supporting signals");
    }

    // DLLs that ship with games and almost never with other software: store SDKs (Steam/GOG/Epic/Discord) and
    // common game middleware (Bink video, FMOD audio, AMD AGS / NVIDIA NGX / GFSDK graphics SDKs, PhysX, OpenAL).
    private static readonly string[] GameRuntimeDllPatterns =
    {
        "steam_api*.dll", "Galaxy*.dll", "EOSSDK*.dll", "discord_game_sdk.dll",
        "bink*.dll", "fmod*.dll", "amd_ags_*.dll", "nvngx_*.dll", "GFSDK_*.dll", "PhysX*.dll", "OpenAL32.dll",
    };

    private static bool HasGameRuntimeDll(string? dir)
    {
        if (string.IsNullOrEmpty(dir)) return false;
        foreach (var pattern in GameRuntimeDllPatterns)
            if (SafeFiles(dir, pattern).Any()) return true;
        return false;
    }

    private static int CountDataHints(string root)
    {
        int hits = 0;
        foreach (var sub in SafeDirectories(root))
            if (DataFolderHints.Contains(System.IO.Path.GetFileName(sub), StringComparer.OrdinalIgnoreCase)) hits++;
        int seen = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(root, "*", SafeEnumeration))
            {
                if (++seen > 2000) break;
                if (DataFileExtensions.Contains(System.IO.Path.GetExtension(f))) hits++;
            }
        }
        catch (Exception) { }
        return hits;
    }
}
