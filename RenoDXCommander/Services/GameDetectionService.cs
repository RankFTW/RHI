// GameDetectionService.cs — Class declaration, shared helpers, engine detection, and game matching
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public partial class GameDetectionService : IGameDetectionService
{
    /// <summary>
    /// Maximum directory depth for file system scans when searching for game executables.
    /// Prevents scanning deeply nested folders that are unlikely to contain game binaries.
    /// </summary>
    private const int MaxScanDepth = 4;

    /// <summary>
    /// Caches engine detection results keyed by root install path to avoid redundant file system scans
    /// on subsequent refreshes. Thread-safe for parallel detection across platforms.
    /// </summary>
    private static readonly ConcurrentDictionary<string, (string installPath, EngineType engine)> _engineCache = new(StringComparer.OrdinalIgnoreCase);

    // ── Engine + path detection ───────────────────────────────────────────────────

    public (string installPath, EngineType engine) DetectEngineAndPath(string rootPath)
    {
        // Check cache first to avoid redundant file system scans
        if (_engineCache.TryGetValue(rootPath, out var cached))
            return cached;

        var result = DetectEngineAndPathCore(rootPath);
        _engineCache.TryAdd(rootPath, result);
        return result;
    }

    private (string installPath, EngineType engine) DetectEngineAndPathCore(string rootPath)
    {
        // --- Unreal Engine (UE4/5) ---
        // Find Binaries\Win64 or Binaries\WinGDK that is NOT inside an Engine folder.
        // This is where ReShade and the .addon64 must be placed.
        var uePath = FindUEBinariesFolder(rootPath);
        if (uePath != null)
        {
            // Distinguish UE4/5 (has *Shipping*.exe) from UE3 (plain exe only).
            // RenoDX addon support requires UE4+; UE3 games cannot use it.
            if (IsUnrealLegacy(rootPath))
                return (uePath, EngineType.UnrealLegacy);
            return (uePath, EngineType.Unreal);
        }

        // Also catch UE3 games whose Binaries may not match UE4 layout (Binaries\Win32 only)
        if (IsUnrealLegacy(rootPath))
        {
            var exeFolderLegacy = FindShallowExeFolder(rootPath);
            return (exeFolderLegacy ?? rootPath, EngineType.UnrealLegacy);
        }

        // --- Unity ---
        // UnityPlayer.dll is always next to the exe (root or 1 level deep)
        var unityPlayer = FindFileShallow(rootPath, "UnityPlayer.dll", maxDepth: MaxScanDepth / 2);
        if (unityPlayer != null)
            return (Path.GetDirectoryName(unityPlayer)!, EngineType.Unity);

        // Additional Unity detection: Mono folder, MonoBleedingEdge folder, il2cpp file, or GameAssembly.dll
        // Some Unity games (especially IL2CPP builds) may not have UnityPlayer.dll in the base folder
        if (IsUnityGame(rootPath))
        {
            var unityExeFolder = FindShallowExeFolder(rootPath);
            return (unityExeFolder ?? rootPath, EngineType.Unity);
        }

        // --- RE Engine ---
        // re_chunk_000.pak is the signature file for all RE Engine games
        var reChunk = FindFileShallow(rootPath, "re_chunk_000.pak", maxDepth: MaxScanDepth / 2);
        if (reChunk != null)
        {
            var reExeFolder = FindShallowExeFolder(rootPath);
            return (reExeFolder ?? rootPath, EngineType.REEngine);
        }

        // --- Generic fallback ---
        var exeFolder = FindShallowExeFolder(rootPath);
        return (exeFolder ?? rootPath, EngineType.Unknown);
    }

    /// <summary>
    /// Returns true if the game folder contains Unity-specific markers other than UnityPlayer.dll.
    /// Covers IL2CPP builds and other Unity configurations that don't ship UnityPlayer.dll in the root.
    /// Checks for: Mono folder, MonoBleedingEdge folder, il2cpp folder/file, GameAssembly.dll
    /// </summary>
    private bool IsUnityGame(string root)
    {
        try
        {
            // Check for Mono folder (classic Unity Mono scripting backend)
            if (Directory.Exists(Path.Combine(root, "Mono"))) return true;
            // Check for MonoBleedingEdge folder (Unity 2017+)
            if (Directory.Exists(Path.Combine(root, "MonoBleedingEdge"))) return true;
            // Check for il2cpp folder (Unity IL2CPP build)
            if (Directory.Exists(Path.Combine(root, "il2cpp"))) return true;
            // Check for GameAssembly.dll (Unity IL2CPP build marker)
            if (FindFileShallow(root, "GameAssembly.dll", maxDepth: MaxScanDepth / 2) != null) return true;
            // Check one level deep for these markers (game data subfolder pattern)
            foreach (var sub in Directory.GetDirectories(root))
            {
                if (IsSkippedFolder(sub)) continue;
                var name = Path.GetFileName(sub);
                // Unity data folders are typically named "<GameName>_Data"
                if (!name.EndsWith("_Data", StringComparison.OrdinalIgnoreCase)) continue;
                if (Directory.Exists(Path.Combine(sub, "Mono"))) return true;
                if (Directory.Exists(Path.Combine(sub, "MonoBleedingEdge"))) return true;
                if (Directory.Exists(Path.Combine(sub, "il2cpp"))) return true;
                if (File.Exists(Path.Combine(sub, "Resources", "unity_builtin_extra"))) return true;
            }
        }
        catch (Exception ex) { CrashReporter.Log($"[GameDetectionService] Scan error — {ex.Message}"); }
        return false;
    }

    /// <summary>
    /// Returns true if this looks like a UE3 or older Unreal game.
    /// Heuristics (any one is sufficient):
    ///   • Has .u or .upk files (UE3 package format) anywhere shallow
    ///   • Has Engine\Config\BaseEngine.ini but NO *Shipping*.exe anywhere
    ///   • Has Binaries\Win32 or Binaries\Win64 with a plain exe but no Shipping exe,
    ///     AND an Engine folder with classic UE3 sub-layout
    /// </summary>
    private bool IsUnrealLegacy(string root)
    {
        try
        {
            // ── Cheap checks first ────────────────────────────────────────────────
            // Rocket League specific: has TAGame folder (UE3 codename)
            if (Directory.Exists(Path.Combine(root, "TAGame"))) return true;

            // Classic UE3 marker: Engine\Config\BaseEngine.ini
            var baseEnginePath = Path.Combine(root, "Engine", "Config", "BaseEngine.ini");
            bool hasBaseEngine = File.Exists(baseEnginePath);

            if (hasBaseEngine)
            {
                // Confirm Binaries exists (cheap check) and no Shipping exe
                bool hasBinaries = Directory.Exists(Path.Combine(root, "Binaries"));
                bool hasShipping = false;
                if (hasBinaries)
                {
                    try { hasShipping = HasFileShallow(root, "*Shipping*.exe", MaxScanDepth); }
                    catch (Exception ex) { CrashReporter.Log($"[GameDetectionService] Scan error — {ex.Message}"); }
                }
                if (hasBinaries && !hasShipping) return true;
            }

            // ── Depth-limited .u / .upk search ───────────────────────────────────
            foreach (var ext in new[] { "*.u", "*.upk" })
            {
                if (HasFileShallow(root, ext, MaxScanDepth - 1)) return true;
            }
        }
        catch (Exception ex) { CrashReporter.Log($"[GameDetectionService] Scan error — {ex.Message}"); }
        return false;
    }

    /// <summary>
    /// Depth-limited file existence check. Returns true if any file matching
    /// <paramref name="pattern"/> exists within <paramref name="maxDepth"/> levels.
    /// </summary>
    private bool HasFileShallow(string dir, string pattern, int maxDepth)
    {
        if (maxDepth < 0 || !Directory.Exists(dir)) return false;
        try
        {
            if (Directory.GetFiles(dir, pattern).Length > 0) return true;
            if (maxDepth > 0)
                foreach (var sub in Directory.GetDirectories(dir))
                {
                    if (IsSkippedFolder(sub)) continue;
                    if (HasFileShallow(sub, pattern, maxDepth - 1)) return true;
                }
        }
        catch (Exception ex) { CrashReporter.Log($"[GameDetectionService] Scan error — {ex.Message}"); }
        return false;
    }

    /// <summary>
    /// Finds the Binaries\Win64 or Binaries\WinGDK folder that is NOT inside
    /// an "Engine" directory. UE games have the structure:
    ///   GameRoot\
    ///     GameName\        ← or a codename folder
    ///       Binaries\
    ///         Win64\       ← THIS is where ReShade + .addon64 goes
    ///     Engine\          ← SKIP this entirely
    /// </summary>
    private string? FindUEBinariesFolder(string root)
    {
        var candidates = new List<string>();
        CollectUEBinaries(root, 0, candidates);
        if (candidates.Count == 0) return null;

        // Prefer folders that contain a Shipping exe
        var withShipping = candidates.FirstOrDefault(c =>
            Directory.GetFiles(c, "*Shipping.exe").Length > 0 ||
            Directory.GetFiles(c, "*.exe").Any(f =>
                Path.GetFileName(f).Contains("Shipping", StringComparison.OrdinalIgnoreCase)));

        return withShipping ?? candidates[0];
    }

    private void CollectUEBinaries(string dir, int depth, List<string> results)
    {
        if (depth > MaxScanDepth + 1) return;
        try
        {
            foreach (var sub in Directory.GetDirectories(dir))
            {
                var name = Path.GetFileName(sub);

                // Skip Engine folder — its Binaries are for the engine, not the game
                if (name.Equals("Engine", StringComparison.OrdinalIgnoreCase)) continue;

                // Skip bonus-content folders (artbook viewers, soundtrack players, etc.)
                if (IsSkippedFolder(sub)) continue;

                // Found a Binaries folder — look inside for Win64/WinGDK
                if (name.Equals("Binaries", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var binSub in Directory.GetDirectories(sub))
                    {
                        var binName = Path.GetFileName(binSub);
                        bool isTarget = binName.Equals("Win64",  StringComparison.OrdinalIgnoreCase)
                                     || binName.Equals("Win32",  StringComparison.OrdinalIgnoreCase)
                                     || binName.Equals("WinGDK", StringComparison.OrdinalIgnoreCase);
                        if (isTarget && Directory.GetFiles(binSub, "*.exe").Length > 0)
                            results.Add(binSub);
                    }
                    // Don't recurse further into Binaries
                    continue;
                }

                // Recurse into non-Engine, non-Binaries subfolders
                CollectUEBinaries(sub, depth + 1, results);
            }
        }
        catch (Exception ex) { CrashReporter.Log($"[GameDetectionService] Scan error — {ex.Message}"); }
    }

    /// <summary>Folder names to skip during engine/exe heuristic searches.
    /// These contain bonus content (artbook viewers, soundtrack players, etc.)
    /// that can carry their own engine DLLs and exes.</summary>
    private readonly HashSet<string> _skipFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "artbook", "art book", "artbooks", "digitalartbook",
        "soundtrack", "ost", "music",
        "manual", "manuals", "docs", "documentation",
        "bonus", "bonuscontent", "bonus content", "extras",
        "wallpapers", "wallpaper",
    };

    private bool IsSkippedFolder(string path)
    {
        var name = Path.GetFileName(path);
        return _skipFolders.Contains(name);
    }

    private string? FindFileShallow(string dir, string pattern, int maxDepth)
    {
        if (maxDepth < 0 || !Directory.Exists(dir)) return null;
        try
        {
            var found = Directory.GetFiles(dir, pattern);
            if (found.Length > 0) return found[0];
            if (maxDepth > 0)
                foreach (var sub in Directory.GetDirectories(dir))
                {
                    if (IsSkippedFolder(sub)) continue;
                    var r = FindFileShallow(sub, pattern, maxDepth - 1);
                    if (r != null) return r;
                }
        }
        catch (Exception ex) { CrashReporter.Log($"[GameDetectionService] Scan error — {ex.Message}"); }
        return null;
    }

    private string? FindShallowExeFolder(string root)
    {
        var queue = new Queue<(string path, int depth)>();
        queue.Enqueue((root, 0));
        while (queue.Count > 0)
        {
            var (dir, depth) = queue.Dequeue();
            if (depth > MaxScanDepth) continue;
            try
            {
                if (Directory.GetFiles(dir, "*.exe").Length > 0) return dir;
                foreach (var sub in Directory.GetDirectories(dir))
                {
                    if (IsSkippedFolder(sub)) continue;
                    queue.Enqueue((sub, depth + 1));
                }
            }
            catch (Exception ex) { CrashReporter.Log($"[GameDetectionService] Scan error — {ex.Message}"); }
        }
        return null;
    }

    // ── Matching ──────────────────────────────────────────────────────────────────

    public GameMod? MatchGame(
        DetectedGame game,
        IEnumerable<GameMod> mods,
        Dictionary<string, string>? nameMappings = null)
    {
        var modList = mods.Select(m => (mod: m, norm: NormalizeName(m.Name))).ToList();
        var name = NormalizeName(game.Name);

        // 0. User-defined name mappings take absolute priority.
        //    The mapping stores detectedName → wikiName (exact strings as typed).
        //    We try both exact and normalised key comparison.
        if (nameMappings != null && nameMappings.Count > 0)
        {
            // Try direct key lookup first (exact stored name)
            if (nameMappings.TryGetValue(game.Name, out var mapped) && !string.IsNullOrEmpty(mapped))
            {
                var mappedNorm = NormalizeName(mapped);
                var byMapped = modList.FirstOrDefault(t => t.norm == mappedNorm);
                if (byMapped.mod != null) return byMapped.mod;
                // Target wiki name not found in mod list — fall through to auto-match
            }
            // Try normalised key comparison (handles minor capitalisation/spacing differences)
            var gameNormForMap = NormalizeName(game.Name);
            foreach (var kv in nameMappings)
            {
                if (NormalizeName(kv.Key) == gameNormForMap && !string.IsNullOrEmpty(kv.Value))
                {
                    var mappedNorm = NormalizeName(kv.Value);
                    var byMapped = modList.FirstOrDefault(t => t.norm == mappedNorm);
                    if (byMapped.mod != null) return byMapped.mod;
                }
            }
        }

        // 1. Exact normalised match — covers diacritics, punctuation, TM symbols.
        var exact = modList.FirstOrDefault(t => t.norm == name);
        if (exact.mod != null) return exact.mod;

        // 2. Game name contains the mod name — e.g. detected "Code Vein GOTY" matches wiki "Code Vein".
        //    Pick the longest (most specific) mod name that fits inside the game name.
        var containedBy = modList
            .Where(t => name.Contains(t.norm))
            .OrderByDescending(t => t.norm.Length)
            .FirstOrDefault();
        if (containedBy.mod != null) return containedBy.mod;

        // 3. Mod name contains the game name — abbreviated detected name.
        //    Only match when game name appears at the START of the mod name.
        //    Reject if the extra suffix is a sequel indicator (II, III, 2, 3…).
        foreach (var t in modList.Where(t => t.norm.StartsWith(name)).OrderBy(t => t.norm.Length))
        {
            var suffix = t.norm.Substring(name.Length);
            if (string.IsNullOrEmpty(suffix)) return t.mod;
            var isSequel = System.Text.RegularExpressions.Regex.IsMatch(suffix, @"^[ivxlcdm0-9]+$");
            if (!isSequel) return t.mod;
        }

        CrashReporter.Log($"[GameDetectionService.MatchGame] No match for '{game.Name}' (norm='{name}') against {modList.Count} wiki mods");
        // Log close matches for diagnostics
        var closeMatches = modList
            .Where(t => t.norm.Contains(name) || name.Contains(t.norm))
            .Select(t => $"'{t.mod.Name}'→'{t.norm}'")
            .Take(5);
        if (closeMatches.Any())
            CrashReporter.Log($"[GameDetectionService.MatchGame] Close matches: [{string.Join(", ", closeMatches)}]");
        return null;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private string? ExtractVdfValue(string content, string key) =>
        Regex.Match(content, $@"""{Regex.Escape(key)}""\s+""([^""]+)""", RegexOptions.IgnoreCase)
            is { Success: true } m ? m.Groups[1].Value : null;

    private string? ExtractJsonString(string json, string key) =>
        Regex.Match(json, $@"""{Regex.Escape(key)}""\s*:\s*""([^""\\]*(\\.[^""\\]*)*)""")
            is { Success: true } m ? m.Groups[1].Value.Replace("\\\\", "\\").Replace("\\/", "/") : null;

    private string? ReadRegistry(string keyPath, string valueName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath)
                         ?? Registry.LocalMachine.OpenSubKey(keyPath);
            return key?.GetValue(valueName) as string;
        }
        catch (Exception ex) { CrashReporter.Log($"[GameDetectionService.ReadRegistry] Failed to read registry '{keyPath}\\{valueName}' — {ex.Message}"); return null; }
    }

    /// <summary>
    /// Normalise a game name for matching: strip diacritics (ö→o, é→e, ñ→n),
    /// trademark symbols, and every character that isn't a-z or 0-9.
    /// "God of War Ragnarök" and "God of War Ragnarok" both become "godofwarragnarok".
    /// "NieR:Automata" and "STAR WARS™ Jedi: Fallen Order" are handled correctly too.
    /// </summary>
    public string NormalizeName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";

        // 1. Strip common trademark/copyright symbols that are never part of the title
        name = name.Replace("™", "").Replace("®", "").Replace("©", "");

        // 2. Decompose unicode into base character + combining marks,
        //    then drop the combining marks (diacritics).
        //    e.g. ö (U+00F6) → o (U+006F) + ̈ (U+0308) → o
        var decomposed = name.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }

        // 3. Lower-case, then strip everything that isn't a letter or digit.
        //    This handles ':', '-', ''', '!', spaces, etc. uniformly.
        return Regex.Replace(sb.ToString().ToLowerInvariant(), @"[^a-z0-9]", "");
    }
}
