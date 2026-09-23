using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Scans the user's explicitly configured custom game folders for games.
///
/// Approach (no parallel detection system — the existing services do the heavy lifting):
///  1. Walk each configured folder top-down (max <see cref="GameDetectionService.MaxScanDepth"/> levels) and stop
///     at the first folder that "looks like a game root": it holds a non-installer executable, an Unreal
///     Binaries\Win64 layout, or an executable inside a bin/Win64-style subfolder.
///     Grouping by folder means each game is one candidate no matter how many exes it ships.
///  2. Run <see cref="IGameDetectionService.DetectEngineAndPath"/> on each game root (same call the normal
///     pipeline uses), pick the real executable with <see cref="CustomGameHeuristics"/>, and read graphics API
///     and bitness with the existing <see cref="GraphicsApiDetector"/> / <see cref="IPeHeaderService"/>.
///  3. Drop anything that overlaps a game RHI already knows (store, manual or earlier custom import) and
///     de-duplicate candidates against each other, by path — never by display name alone.
/// The scan never leaves the configured folders, skips system locations, junctions and unreadable folders,
/// and honours cancellation.
/// </summary>
public sealed class CustomFolderScanService : ICustomFolderScanService
{
    /// <summary>Safety cap on directories visited per configured folder (a stray drive-root pick can't hang RHI).</summary>
    internal const int MaxDirectoriesPerRoot = 20_000;

    private readonly IGameDetectionService _detection;
    private readonly IPeHeaderService _peHeader;

    public CustomFolderScanService(IGameDetectionService detection, IPeHeaderService peHeader)
    {
        _detection = detection;
        _peHeader = peHeader;
    }

    public Task<CustomFolderScanResult> ScanAsync(
        IReadOnlyList<string> roots,
        IReadOnlyCollection<string> existingPaths,
        IReadOnlyCollection<string> reservedNames,
        CancellationToken cancellationToken = default)
        => Task.Run(() => Scan(roots, existingPaths, reservedNames, cancellationToken), cancellationToken);

    /// <summary>Synchronous scan (used directly by tests). Throws <see cref="OperationCanceledException"/> when cancelled.</summary>
    public CustomFolderScanResult Scan(
        IReadOnlyList<string> roots,
        IReadOnlyCollection<string> existingPaths,
        IReadOnlyCollection<string> reservedNames,
        CancellationToken ct = default)
    {
        var result = new CustomFolderScanResult();
        var found = new List<CustomGameCandidate>();
        var normalizedRoots = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var configured in roots)
        {
            ct.ThrowIfCancellationRequested();
            var root = CustomFolderPaths.Normalize(configured);
            if (root == null || !seen.Add(root)) continue;

            if (CustomFolderPaths.IsUnsafeRoot(root))
            {
                CrashReporter.Log($"[CustomFolderScanService] Skipping system location '{root}'");
                result.SkippedRoots.Add(root);
                continue;
            }
            if (!CustomFolderPaths.IsAvailable(root))
            {
                // Offline drive / missing folder: report it, keep it configured, scan nothing.
                CrashReporter.Log($"[CustomFolderScanService] Folder unavailable, skipped (kept in list): '{root}'");
                result.UnavailableRoots.Add(root);
                continue;
            }

            normalizedRoots.Add(root);
            var budget = new Budget();
            var gameRoots = new List<string>();
            FindGameRoots(root, 0, gameRoots, budget, ct);
            // The configured folder is only itself a game when it contains no games below it,
            // so a stray exe next to your game folders can't swallow the whole library.
            if (gameRoots.Count == 0 && LooksLikeGameRoot(root)) gameRoots.Add(root);
            if (budget.Exhausted)
            {
                result.Truncated = true;
                CrashReporter.Log($"[CustomFolderScanService] Directory cap reached while scanning '{root}'");
            }

            foreach (var gameRoot in gameRoots)
            {
                ct.ThrowIfCancellationRequested();
                // Cheap, path-only check first: engine/API detection is the expensive part, and a library that is
                // already imported must not pay for it again on every Refresh.
                if (OverlapsKnownGame(gameRoot, existingPaths, normalizedRoots)) continue;
                var candidate = BuildCandidate(gameRoot, root, existingPaths, normalizedRoots, ct);
                if (candidate != null) found.Add(candidate);
            }
        }

        result.Candidates.AddRange(Finalize(found, normalizedRoots, existingPaths, reservedNames));
        CrashReporter.Log($"[CustomFolderScanService] Scanned {normalizedRoots.Count} folder(s): " +
            $"{result.Candidates.Count(c => c.Confidence == CustomCandidateConfidence.High)} high-confidence, " +
            $"{result.Candidates.Count(c => c.Confidence == CustomCandidateConfidence.Low)} need review, " +
            $"{result.UnavailableRoots.Count} unavailable");
        return result;
    }

    // ── Discovery ─────────────────────────────────────────────────────────────────

    private sealed class Budget
    {
        private int _visited;
        public bool Exhausted { get; private set; }
        public bool TryVisit()
        {
            if (++_visited > MaxDirectoriesPerRoot) { Exhausted = true; return false; }
            return true;
        }
    }

    private static void FindGameRoots(string dir, int depth, List<string> found, Budget budget, CancellationToken ct)
    {
        foreach (var child in CustomGameHeuristics.SafeDirectories(dir))
        {
            ct.ThrowIfCancellationRequested();
            if (!budget.TryVisit()) return;

            var name = Path.GetFileName(child);
            if (GameExecutableFilter.IsSystemLikeDirectory(name) || GameExecutableFilter.IsExcludedDirectory(name)) continue;

            if (LooksLikeGameRoot(child)) { found.Add(child); continue; }   // game found: don't look inside it
            if (depth + 1 >= GameDetectionService.MaxScanDepth) continue;

            if (depth == 0)
            {
                // A game normally lives in its own top-level folder ("<configured folder>\<Game>\...\Game.exe").
                // When a top-level folder holds exactly ONE game somewhere below it, that folder is the game
                // (so "Lost in Play\LostInPlay" and "Some Game\ph_ft\work\bin\Game.exe" are rooted correctly).
                // A folder with several games below it is a container (publisher / genre) and stays split.
                var below = new List<string>();
                FindGameRoots(child, depth + 1, below, budget, ct);
                if (below.Count == 1) found.Add(child);
                else found.AddRange(below);
            }
            else
            {
                FindGameRoots(child, depth + 1, found, budget, ct);
            }
        }
    }

    /// <summary>
    /// A folder is a game root when a non-installer executable sits in it, in an Unreal
    /// "Project\Binaries\Win64" layout beside an Engine folder, or in a bin/Win64/x64-style subfolder.
    /// </summary>
    internal static bool LooksLikeGameRoot(string dir)
    {
        try
        {
            if (HasUsableExe(dir)) return true;

            var subs = CustomGameHeuristics.SafeDirectories(dir).ToList();

            // Unreal: <root>\Engine + <root>\<Project>\Binaries\Win64\*.exe
            if (subs.Any(s => Path.GetFileName(s).Equals("Engine", StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var project in subs)
                {
                    if (Path.GetFileName(project).Equals("Engine", StringComparison.OrdinalIgnoreCase)) continue;
                    var binaries = Path.Combine(project, "Binaries");
                    foreach (var platform in CustomGameHeuristics.SafeDirectories(binaries))
                        if (HasUsableExe(platform)) return true;
                }
            }

            // Executable one or two levels down in a generic binary folder (bin, Binaries\Win64, bin\x64 …).
            foreach (var sub in subs)
            {
                if (!GameExecutableFilter.IsGenericBinaryFolder(Path.GetFileName(sub))) continue;
                if (HasUsableExe(sub)) return true;
                foreach (var sub2 in CustomGameHeuristics.SafeDirectories(sub))
                    if (GameExecutableFilter.IsGenericBinaryFolder(Path.GetFileName(sub2)) && HasUsableExe(sub2)) return true;
            }
        }
        catch (Exception) { /* unreadable folder: not a game root */ }
        return false;
    }

    private static bool HasUsableExe(string dir)
        => CustomGameHeuristics.SafeFiles(dir, "*.exe").Any(GameExecutableFilter.IsUsable);

    // ── Candidate construction ────────────────────────────────────────────────────

    private CustomGameCandidate? BuildCandidate(
        string gameRoot, string scanRoot, IReadOnlyCollection<string> existingPaths, IReadOnlyList<string> scanRoots, CancellationToken ct)
    {
        var exes = CustomGameHeuristics.CollectExecutables(gameRoot, ct);
        var first = CustomGameHeuristics.PickExecutable(gameRoot, exes);
        if (first == null) return null;   // only installers / helpers in there — not a game

        // "Game Name\Sources\Game.exe": the executable is named after a parent folder, so that parent is the game.
        var lifted = CustomGameHeuristics.LiftToWrapperFolder(gameRoot, scanRoot, first.Path);
        if (!CustomFolderPaths.AreSame(lifted, gameRoot))
        {
            if (OverlapsKnownGame(lifted, existingPaths, scanRoots)) return null;
            gameRoot = lifted;
            exes = CustomGameHeuristics.CollectExecutables(gameRoot, ct);
        }

        var engine = EngineType.Unknown;
        string? resolvedFolder = null;
        try { (resolvedFolder, engine) = _detection.DetectEngineAndPath(gameRoot); }
        catch (Exception ex) { CrashReporter.Log($"[CustomFolderScanService] Engine detection failed for '{gameRoot}' — {ex.Message}"); }

        // The folder engine detection resolved is only evidence when an engine was actually identified; for an
        // unknown engine RHI just returns the first folder holding any exe, which must not outrank a real game exe.
        var exe = CustomGameHeuristics.PickExecutable(gameRoot, exes, engine != EngineType.Unknown ? resolvedFolder : null) ?? first;

        var (confidence, reason) = CustomGameHeuristics.Rate(gameRoot, engine, exe);

        var api = GraphicsApiType.Unknown;
        try
        {
            api = GraphicsApiDetector.Detect(exe.Path);
            // Unity executables import no graphics DLLs; RHI reads the API from boot.config instead.
            if (api == GraphicsApiType.Unknown && engine == EngineType.Unity)
                api = GraphicsApiDetector.DetectUnityFromBootConfig(Path.GetDirectoryName(exe.Path) ?? gameRoot);
        }
        catch (Exception ex) { CrashReporter.Log($"[CustomFolderScanService] API detection failed for '{exe.Path}' — {ex.Message}"); }

        var machine = MachineType.Native;
        try { machine = _peHeader.DetectArchitecture(exe.Path); }
        catch (Exception ex) { CrashReporter.Log($"[CustomFolderScanService] Bitness detection failed for '{exe.Path}' — {ex.Message}"); }

        return new CustomGameCandidate
        {
            Name = CustomGameHeuristics.InferName(gameRoot, exe.Path),
            InstallPath = gameRoot,
            ScanRoot = scanRoot,
            ExePath = exe.Path,
            Engine = engine,
            GraphicsApi = api,
            Bitness = machine,
            Confidence = confidence,
            Reason = reason,
        };
    }

    // ── De-duplication ────────────────────────────────────────────────────────────

    private List<CustomGameCandidate> Finalize(
        List<CustomGameCandidate> found,
        IReadOnlyList<string> normalizedRoots,
        IReadOnlyCollection<string> existingPaths,
        IReadOnlyCollection<string> reservedNames)
    {
        // 1. Skip games RHI already has (store / manual / earlier custom import) — by path, not by name.
        var fresh = found.Where(c => !OverlapsKnownGame(c.InstallPath, existingPaths, normalizedRoots)).ToList();

        // 2. Custom/custom: overlapping configured folders can find the same game twice. The candidate found under the
        //    more specific configured folder wins (the user pointed at that folder as "a folder of games"); otherwise
        //    the shallowest path wins.
        var kept = new List<CustomGameCandidate>();
        foreach (var c in fresh.OrderByDescending(c => c.ScanRoot.Length).ThenBy(c => c.InstallPath.Length))
            if (!kept.Any(k => CustomFolderPaths.Overlaps(k.InstallPath, c.InstallPath)))
                kept.Add(c);

        // 3. Unique display names. A name collision with a manual/custom game would silently hide one of
        //    them (the pipeline keys manual games by name), so disambiguate with the parent folder.
        var taken = new HashSet<string>(reservedNames.Select(_detection.NormalizeName), StringComparer.Ordinal);
        foreach (var c in kept.OrderBy(c => c.InstallPath, StringComparer.OrdinalIgnoreCase))
        {
            var unique = c.Name;
            if (taken.Contains(_detection.NormalizeName(unique)))
            {
                var parent = Path.GetFileName(Path.GetDirectoryName(c.InstallPath.TrimEnd('\\', '/')) ?? "");
                unique = !string.IsNullOrEmpty(parent) ? $"{c.Name} ({parent})" : c.Name;
                for (int n = 2; taken.Contains(_detection.NormalizeName(unique)); n++)
                    unique = $"{c.Name} ({n})";
            }
            c.Name = unique;
            taken.Add(_detection.NormalizeName(unique));
        }

        return kept
            .OrderBy(c => c.Confidence)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// True when <paramref name="candidatePath"/> is the same folder as a known game, contains one, or lies inside one.
    /// A known path that is an ancestor of a scanned folder is ignored for the "inside" case — it can't be a game,
    /// it is a broad location (e.g. a manually added parent folder) and must not hide everything below it.
    /// </summary>
    internal static bool OverlapsKnownGame(string candidatePath, IEnumerable<string> existingPaths, IReadOnlyList<string> scanRoots)
    {
        foreach (var existing in existingPaths)
        {
            if (string.IsNullOrWhiteSpace(existing)) continue;
            if (CustomFolderPaths.AreSame(candidatePath, existing)) return true;
            if (CustomFolderPaths.IsSameOrUnder(existing, candidatePath)) return true;
            if (CustomFolderPaths.IsSameOrUnder(candidatePath, existing)
                && !scanRoots.Any(r => CustomFolderPaths.IsSameOrUnder(r, existing)))
                return true;
        }
        return false;
    }
}
