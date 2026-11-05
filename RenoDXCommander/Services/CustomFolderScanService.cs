// CustomFolderScanService.cs -- scans the user's custom game folders (Settings › Tools & Maintenance ›
// Custom game folders) for DRM-free / non-store games. Each direct subfolder is one candidate (or the
// folder itself when it is a game). Candidates already in the library, ignored, removed from RHI, or
// without an exe are left out. Nothing is added here: MainViewModel offers the results to the user.

using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public sealed class CustomFolderScanService : ICustomFolderScanService
{
    private readonly IGameDetectionService _detection;

    public CustomFolderScanService(IGameDetectionService detection) => _detection = detection;

    public Task<IReadOnlyList<CustomGameCandidate>> ScanAsync(CustomFolderScanRequest request, CancellationToken ct)
        => Task.Run(() => Scan(request, ct), ct);

    private IReadOnlyList<CustomGameCandidate> Scan(CustomFolderScanRequest req, CancellationToken ct)
    {
        var found = new List<CustomGameCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var libPaths = req.LibraryPaths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(NormalizePath)
            .Where(p => p.Length > 3) // never let a drive-root install path hide everything
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        foreach (var rawFolder in req.Folders)
        {
            ct.ThrowIfCancellationRequested();
            var folder = NormalizePath(rawFolder);
            try
            {
                if (!Directory.Exists(folder))
                {
                    CrashReporter.Log($"[CustomFolderScanService.Scan] Folder not found, skipped: '{folder}'");
                    continue;
                }
                foreach (var root in GetCandidateRoots(folder))
                {
                    ct.ThrowIfCancellationRequested();
                    if (!seen.Add(root)) continue;
                    try
                    {
                        var c = Examine(root, folder, req, libPaths, ct);
                        if (c != null) found.Add(c);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (UnauthorizedAccessException ex)
                    {
                        CrashReporter.Log($"[CustomFolderScanService.Scan] No access to '{root}' — {ex.Message}");
                    }
                    catch (IOException ex)
                    {
                        CrashReporter.Log($"[CustomFolderScanService.Scan] Can't read '{root}' — {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (UnauthorizedAccessException ex)
            {
                CrashReporter.Log($"[CustomFolderScanService.Scan] No access to '{folder}' — {ex.Message}");
            }
            catch (IOException ex)
            {
                CrashReporter.Log($"[CustomFolderScanService.Scan] Can't read '{folder}' — {ex.Message}");
            }
        }

        CrashReporter.Log($"[CustomFolderScanService.Scan] {req.Folders.Count} folder(s), {seen.Count} candidate folder(s) checked, {found.Count} new game(s) in {sw.ElapsedMilliseconds} ms");
        return found;
    }

    /// <summary>The folder itself when it's a game, otherwise its direct subfolders.</summary>
    internal static List<string> GetCandidateRoots(string folder)
    {
        if (IsSingleGameFolder(folder)) return new List<string> { folder };
        var list = new List<string>();
        foreach (var sub in Directory.EnumerateDirectories(folder))
        {
            var name = Path.GetFileName(sub);
            if (CustomGameExeScorer.IsSkippedFolderName(name)) continue;
            try
            {
                var attr = File.GetAttributes(sub);
                if ((attr & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) != 0) continue;
            }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }
            list.Add(NormalizePath(sub));
        }
        return list;
    }

    /// <summary>
    /// True when the added folder is one game rather than a folder of games: it has store/engine markers,
    /// or it has its own exe and at most one subfolder that looks like a game.
    /// </summary>
    internal static bool IsSingleGameFolder(string folder)
    {
        if (DragDropHandler.LooksLikeGameRoot(folder)) return true;
        bool topExe = Directory.EnumerateFiles(folder, "*.exe")
            .Any(f => !CustomGameExeScorer.IsExcludedExeName(Path.GetFileName(f)));
        if (!topExe) return false;
        int gameLike = 0;
        foreach (var sub in Directory.EnumerateDirectories(folder))
        {
            try
            {
                if (DragDropHandler.LooksLikeGameRoot(sub)
                    || Directory.EnumerateFiles(sub, "*.exe").Any(f => !CustomGameExeScorer.IsExcludedExeName(Path.GetFileName(f))))
                    gameLike++;
            }
            catch (IOException) { /* unreadable subfolder: not counted */ }
            catch (UnauthorizedAccessException) { /* same */ }
            if (gameLike >= 2) return false;
        }
        return true;
    }

    private CustomGameCandidate? Examine(string root, string customFolder, CustomFolderScanRequest req,
        List<string> libPaths, CancellationToken ct)
    {
        if (req.IgnoredFolders.Contains(root)) return null;
        foreach (var lib in libPaths)
            if (IsSameOrInside(lib, root) || IsSameOrInside(root, lib)) return null;

        var folderName = Path.GetFileName(root);
        var cleaned = CustomGameNameCleaner.Clean(folderName);
        if (req.KnownNames.Contains(CustomGameExeScorer.Norm(cleaned))) return null;

        var ranking = CustomGameExeScorer.Rank(root, cleaned, ct: ct);
        if (ranking.Exes.Count == 0) return null;
        var top = ranking.Exes[0];

        var (_, engine) = _detection.DetectEngineAndPath(root);

        // Name: folder name first; for meaningless folder names use the exe / Unreal project name
        var name = cleaned;
        if (CustomGameNameCleaner.IsGenericFolderName(cleaned))
            name = DragDropHandler.InferGameName(top.FullPath, root, engine);
        string? canonical = req.CanonicalTitle?.Invoke(name);
        if (canonical == null)
        {
            var exeName = DragDropHandler.CleanFolderName(
                DragDropHandler.CleanUnrealExeName(Path.GetFileNameWithoutExtension(top.FullPath)));
            if (CustomGameExeScorer.Norm(exeName).Length >= 4 && !CustomGameNameCleaner.IsGenericFolderName(exeName))
                canonical = req.CanonicalTitle?.Invoke(exeName);
        }
        if (!string.IsNullOrWhiteSpace(canonical)) name = canonical;
        if (string.IsNullOrWhiteSpace(name)) name = folderName;
        if (req.KnownNames.Contains(CustomGameExeScorer.Norm(name))) return null;

        return new CustomGameCandidate
        {
            FolderPath = root,
            CustomFolder = customFolder,
            SuggestedName = name,
            Engine = engine,
            Exes = ranking.Exes,
            IsAmbiguous = ranking.IsAmbiguous,
        };
    }

    // ── Path helpers (public for the settings UI and tests) ──────────────────

    /// <summary>Full path, '/' → '\', no trailing separator (except a drive root like "D:\").</summary>
    public static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        var p = path.Trim().Replace('/', '\\');
        try { p = Path.GetFullPath(p); }
        catch (ArgumentException) { /* keep as typed */ }
        catch (NotSupportedException) { /* same */ }
        catch (PathTooLongException) { /* same */ }
        var trimmed = p.TrimEnd('\\');
        return trimmed.Length == 2 && trimmed[1] == ':' ? trimmed + "\\" : trimmed;
    }

    /// <summary>True when <paramref name="path"/> is <paramref name="parent"/> or somewhere inside it.</summary>
    public static bool IsSameOrInside(string path, string parent)
    {
        var a = NormalizePath(path);
        var b = NormalizePath(parent);
        if (a.Length == 0 || b.Length == 0) return false;
        if (a.Equals(b, StringComparison.OrdinalIgnoreCase)) return true;
        var prefix = b.EndsWith('\\') ? b : b + "\\";
        return a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The candidate folder (as the scan would offer it) that holds <paramref name="gamePath"/>, if the
    /// path is inside one of <paramref name="customFolders"/>. Null when it isn't under any of them.
    /// </summary>
    public static string? FindCandidateRoot(string gamePath, IEnumerable<string> customFolders)
    {
        var p = NormalizePath(gamePath);
        foreach (var raw in customFolders)
        {
            var f = NormalizePath(raw);
            if (!IsSameOrInside(p, f)) continue;
            if (p.Equals(f, StringComparison.OrdinalIgnoreCase)) return f;
            var rel = p.Substring(f.EndsWith('\\') ? f.Length : f.Length + 1);
            var first = rel.Split('\\', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            bool single;
            try { single = Directory.Exists(f) && IsSingleGameFolder(f); }
            catch (IOException) { single = false; }
            catch (UnauthorizedAccessException) { single = false; }
            return single || string.IsNullOrEmpty(first) ? f : Path.Combine(f, first);
        }
        return null;
    }

    /// <summary>
    /// Why a folder can't be added as a custom game folder, or null when it's fine.
    /// <paramref name="existing"/> is the current list.
    /// </summary>
    public static string? ValidateFolder(string path, IEnumerable<string> existing)
    {
        var p = NormalizePath(path);
        if (p.Length == 0) return "No folder was chosen.";
        if (!Directory.Exists(p)) return $"The folder \"{p}\" doesn't exist.";
        if (p.Length <= 3) return "Pick the folder that holds your games, not a whole drive.";

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrEmpty(windows) && IsSameOrInside(p, windows))
            return "The Windows folder can't be used as a game folder.";

        var segments = p.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(s => s.Equals("WindowsApps", StringComparison.OrdinalIgnoreCase)))
            return "Game Pass / Microsoft Store folders (WindowsApps) can't be used. RHI finds those games on its own.";
        if (segments.Any(s => s.Equals("steamapps", StringComparison.OrdinalIgnoreCase)))
            return "Steam library folders can't be used. RHI finds Steam games on its own.";

        foreach (var sf in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86,
                                   Environment.SpecialFolder.UserProfile })
        {
            var sp = Environment.GetFolderPath(sf);
            if (!string.IsNullOrEmpty(sp) && p.Equals(NormalizePath(sp), StringComparison.OrdinalIgnoreCase))
                return $"\"{p}\" holds far more than games. Pick the folder that holds your games.";
        }

        foreach (var raw in existing)
        {
            var e = NormalizePath(raw);
            if (e.Length == 0) continue;
            if (p.Equals(e, StringComparison.OrdinalIgnoreCase)) return "That folder is already on the list.";
            if (IsSameOrInside(p, e)) return $"That folder is inside \"{e}\", which is already on the list.";
            if (IsSameOrInside(e, p)) return $"That folder contains \"{e}\", which is already on the list. Remove it first.";
        }
        return null;
    }
}
