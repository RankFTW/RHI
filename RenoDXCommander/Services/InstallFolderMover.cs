// InstallFolderMover.cs -- moves RHI-owned files from a game's old install folder to its new one.
// Pure file-system work, driven by a plan (InstallFolderMigrationService builds it from each
// component's own tracking). All-or-nothing: every step is journalled and undone if a later step
// fails, and a preflight (folders writable, every file openable exclusively) runs first so a
// running game or a locked file leaves everything as it was.
//
// .original sentinel rules for a moved file F:
//   old folder: F.original non-empty → moved back to F (the game's own file returns);
//               F.original 0-byte    → deleted (RHI placed F from scratch).
//   new folder: a file already at F that isn't RHI's → kept as F.original (like an install would);
//               no file at F and the old copy had a sentinel/backup → 0-byte F.original sentinel.
//   new folder already has RHI's own copy (F + F.original there) → the new copy wins, the old one
//   is removed and its backup restored.

namespace RenoDXCommander.Services;

/// <summary>One file or folder to move, with absolute source and destination paths.</summary>
public sealed class FolderMoveEntry
{
    public required string Source { get; init; }
    public required string Dest { get; init; }
    public bool IsDirectory { get; init; }

    /// <summary>Backup beside the file using the sentinel pattern (".original"); null = no backup.</summary>
    public string? BackupSuffix { get; init; } = ".original";

    /// <summary>
    /// The game's own file is kept beside the proxy as &lt;name&gt;Hooked.dll (Bink names, ASI Loader).
    /// Restored in the old folder after the move; a game file already in the new folder is renamed to it.
    /// </summary>
    public bool HookedBackup { get; init; }

    /// <summary>Move only when a backup/sentinel sits beside the source (proof RHI placed it there).</summary>
    public bool RequireBackup { get; init; }

    /// <summary>
    /// RHI's reshade-shaders folder: a user/game folder of that name in the new folder is kept as
    /// reshade-shaders-original, and one in the old folder is renamed back after the move.
    /// </summary>
    public bool IsManagedShaders { get; init; }

    /// <summary>Marker files that make a reshade-shaders folder RHI's (for <see cref="IsManagedShaders"/>).</summary>
    public IReadOnlyList<string> ManagedMarkers { get; init; } = Array.Empty<string>();
}

/// <summary>A component's entries. <see cref="NewDirWins"/>: the new folder already has it, so the old copy is removed.</summary>
public sealed class FolderMoveGroup
{
    public required string Name { get; init; }
    public List<FolderMoveEntry> Entries { get; } = new();
    public bool NewDirWins { get; set; }
}

/// <summary>A file written (after the moves) as part of the same transaction, e.g. a merged rhi_install.txt.</summary>
public sealed record FolderMoveWrite(string Path, string Content);

public sealed class FolderMovePlan
{
    public required string OldDir { get; init; }
    public required string NewDir { get; init; }
    public List<FolderMoveGroup> Groups { get; } = new();
    public List<FolderMoveWrite> Writes { get; } = new();
    /// <summary>Files removed after the moves (old copies of tracking files that were rewritten in the new folder).</summary>
    public List<string> Deletes { get; } = new();

    public bool IsEmpty => Groups.All(g => g.Entries.Count == 0) && Writes.Count == 0 && Deletes.Count == 0;
}

public sealed class FolderMoveResult
{
    public bool Success { get; set; }
    /// <summary>Why nothing was moved (preflight) or why the move was undone.</summary>
    public string? AbortReason { get; set; }
    public List<string> Moved { get; } = new();
    public List<string> Restored { get; } = new();
    public List<string> Removed { get; } = new();
    public List<string> Skipped { get; } = new();
    /// <summary>Groups that had something moved or cleaned.</summary>
    public HashSet<string> GroupsTouched { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public static class InstallFolderMover
{
    private const string Tag = "[InstallFolderMover]";
    public const string ShadersFolder = "reshade-shaders";
    public const string ShadersOriginalFolder = "reshade-shaders-original";

    // ── Preflight ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns null when the plan can run, else the reason it can't (nothing is touched).
    /// Checks both folders exist and are writable and that every file the plan touches can be
    /// opened exclusively (a running game holds its DLLs open).
    /// </summary>
    public static string? Preflight(FolderMovePlan plan)
    {
        if (!Directory.Exists(plan.OldDir)) return $"old folder '{plan.OldDir}' no longer exists";
        if (!Directory.Exists(plan.NewDir)) return $"new folder '{plan.NewDir}' doesn't exist";
        if (IsWindowsApps(plan.NewDir)) return "the new folder is a protected WindowsApps folder";
        if (SamePath(plan.OldDir, plan.NewDir)) return "the old and new folders are the same";
        if (!CanWrite(plan.NewDir)) return $"RHI can't write to '{plan.NewDir}'";
        if (!CanWrite(plan.OldDir)) return $"RHI can't write to '{plan.OldDir}'";

        foreach (var path in FilesTouched(plan))
        {
            var err = TryOpenExclusive(path);
            if (err != null) return $"'{path}' is in use or locked ({err})";
        }
        return null;
    }

    private static IEnumerable<string> FilesTouched(FolderMovePlan plan)
    {
        foreach (var g in plan.Groups)
            foreach (var e in g.Entries)
            {
                if (e.IsDirectory)
                {
                    if (!Directory.Exists(e.Source)) continue;
                    IEnumerable<string> files;
                    try { files = Directory.EnumerateFiles(e.Source, "*", SearchOption.AllDirectories).ToList(); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    { files = new[] { e.Source + "\\?" }; } // reported as unopenable below
                    foreach (var f in files) yield return f;
                    continue;
                }
                if (File.Exists(e.Source)) yield return e.Source;
                if (File.Exists(e.Dest)) yield return e.Dest;
                if (e.BackupSuffix != null && File.Exists(e.Source + e.BackupSuffix)) yield return e.Source + e.BackupSuffix;
                if (e.HookedBackup && File.Exists(HookedPath(e.Source))) yield return HookedPath(e.Source);
            }
        foreach (var w in plan.Writes) if (File.Exists(w.Path)) yield return w.Path;
        foreach (var d in plan.Deletes) if (File.Exists(d)) yield return d;
    }

    private static string? TryOpenExclusive(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return null;
        }
        catch (UnauthorizedAccessException) when (IsReadOnly(path))
        {
            // Read-only files still move; check nothing else holds them.
            try { using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None); return null; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ex.Message; }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ex.Message; }
    }

    private static bool IsReadOnly(string path)
    {
        try { return File.Exists(path) && new FileInfo(path).IsReadOnly; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static bool CanWrite(string dir)
    {
        var probe = Path.Combine(dir, $".rhi-write-test-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllBytes(probe, Array.Empty<byte>());
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"{Tag} '{dir}' isn't writable — {ex.Message}");
            return false;
        }
    }

    // ── Execute ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs the plan. Preflight first; on any failure part-way every completed step is undone, so
    /// the files end up either all in the new folder or all where they were.
    /// </summary>
    public static FolderMoveResult Execute(FolderMovePlan plan)
    {
        var result = new FolderMoveResult();
        var reason = Preflight(plan);
        if (reason != null)
        {
            result.AbortReason = reason;
            CrashReporter.Log($"{Tag} Not moving '{plan.OldDir}' → '{plan.NewDir}': {reason}");
            return result;
        }

        var journal = new Journal(Path.Combine(plan.OldDir, $".rhi-move-{DateTime.UtcNow.Ticks}"));
        try
        {
            var dirSources = plan.Groups.SelectMany(g => g.Entries).Where(e => e.IsDirectory)
                .Select(e => Norm(e.Source)).ToList();
            var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in plan.Groups)
                foreach (var entry in group.Entries)
                {
                    var src = Norm(entry.Source);
                    if (!handled.Add(src)) continue;
                    // A file inside a folder that's moved as a whole goes with the folder
                    if (!entry.IsDirectory && dirSources.Any(d => IsInside(src, d))) continue;

                    bool touched = entry.IsDirectory
                        ? (group.NewDirWins ? CleanOldDir(entry, journal, result) : MoveDir(entry, journal, result))
                        : (group.NewDirWins ? CleanOldFile(entry, journal, result) : MoveFile(entry, journal, result));
                    if (touched) result.GroupsTouched.Add(group.Name);
                }

            foreach (var w in plan.Writes) journal.Write(w.Path, w.Content);
            foreach (var d in plan.Deletes) if (File.Exists(d)) { journal.Trash(d); result.Removed.Add(d); }

            journal.Commit();
            RemoveEmptyParents(journal.VacatedDirs, plan.OldDir);
            result.Success = true;
            CrashReporter.Log($"{Tag} Moved '{plan.OldDir}' → '{plan.NewDir}': {result.Moved.Count} moved, "
                + $"{result.Restored.Count} restored, {result.Removed.Count} removed, {result.Skipped.Count} skipped");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            result.AbortReason = $"move failed part-way and was undone — {ex.Message}";
            CrashReporter.Log($"{Tag} Failed moving '{plan.OldDir}' → '{plan.NewDir}' — {ex.Message}; undoing");
            journal.Rollback();
            result.Moved.Clear(); result.Restored.Clear(); result.Removed.Clear(); result.GroupsTouched.Clear();
        }
        return result;
    }

    // ── Files ─────────────────────────────────────────────────────────────────

    private static bool MoveFile(FolderMoveEntry e, Journal j, FolderMoveResult r)
    {
        var src = e.Source;
        var dst = e.Dest;
        if (!File.Exists(src)) return false;
        var suffix = e.BackupSuffix;
        bool srcHasBackup = suffix != null && File.Exists(src + suffix);
        if (e.RequireBackup && !srcHasBackup)
        {
            r.Skipped.Add($"{src} (no RHI backup beside it, not RHI's copy)");
            CrashReporter.Log($"{Tag} Left '{src}': no {suffix} beside it, so it isn't RHI's copy");
            return false;
        }
        if (SamePath(src, dst)) return false;

        j.EnsureDir(Path.GetDirectoryName(dst)!);
        if (File.Exists(dst) && suffix == null && !e.HookedBackup)
        {
            // Settings, logs and addons have no backup: the moved copy is the one in use, so it
            // replaces whatever is there (kept in the trash until the move commits).
            j.Trash(dst);
            r.Removed.Add(dst);
            CrashReporter.Log($"{Tag} Replaced '{dst}' with the moved copy");
        }
        else if (File.Exists(dst))
        {
            bool newIsRhis = (suffix != null && File.Exists(dst + suffix))
                          || (e.HookedBackup && File.Exists(HookedPath(dst)));
            if (newIsRhis)
            {
                CrashReporter.Log($"{Tag} '{dst}' is already RHI's copy in the new folder — keeping it, removing the old one");
                return CleanOldFile(e, j, r);
            }
            // A file of the same name that isn't RHI's: keep it the way an install would.
            var keepAs = e.HookedBackup ? HookedPath(dst) : dst + (suffix ?? ".original");
            if (File.Exists(keepAs))
            {
                CrashReporter.Log($"{Tag} '{dst}' and '{keepAs}' both exist in the new folder — keeping them, removing the old copy");
                return CleanOldFile(e, j, r);
            }
            j.Move(dst, keepAs);
            CrashReporter.Log($"{Tag} Kept the new folder's own '{Path.GetFileName(dst)}' as '{Path.GetFileName(keepAs)}'");
        }
        else if (srcHasBackup)
        {
            j.CreateEmpty(dst + suffix); // sentinel: RHI placed this file from scratch
        }

        j.Move(src, dst);
        r.Moved.Add(dst);
        CrashReporter.Log($"{Tag} Moved '{src}' → '{dst}'");
        RestoreOldBackup(e, j, r);
        return true;
    }

    /// <summary>New folder wins: remove the old copy and put the game's file back in the old folder.</summary>
    private static bool CleanOldFile(FolderMoveEntry e, Journal j, FolderMoveResult r)
    {
        if (!File.Exists(e.Source)) return false;
        if (e.RequireBackup && !(e.BackupSuffix != null && File.Exists(e.Source + e.BackupSuffix)))
        {
            r.Skipped.Add($"{e.Source} (no RHI backup beside it)");
            return false;
        }
        j.Trash(e.Source);
        r.Removed.Add(e.Source);
        CrashReporter.Log($"{Tag} Removed old copy '{e.Source}' (the new folder has its own)");
        RestoreOldBackup(e, j, r);
        return true;
    }

    private static void RestoreOldBackup(FolderMoveEntry e, Journal j, FolderMoveResult r)
    {
        var src = e.Source;
        if (e.BackupSuffix != null)
        {
            var bak = src + e.BackupSuffix;
            if (File.Exists(bak))
            {
                if (new FileInfo(bak).Length > 0 && !File.Exists(src))
                {
                    j.Move(bak, src);
                    r.Restored.Add(src);
                    CrashReporter.Log($"{Tag} Restored the game's own '{Path.GetFileName(src)}' in '{Path.GetDirectoryName(src)}'");
                    return;
                }
                if (new FileInfo(bak).Length == 0)
                {
                    j.DeleteEmpty(bak);
                    CrashReporter.Log($"{Tag} Removed sentinel '{bak}'");
                }
            }
        }
        if (e.HookedBackup)
        {
            var hooked = HookedPath(src);
            if (File.Exists(hooked) && !File.Exists(src))
            {
                j.Move(hooked, src);
                r.Restored.Add(src);
                CrashReporter.Log($"{Tag} Restored the game's own '{Path.GetFileName(src)}' from '{Path.GetFileName(hooked)}'");
            }
        }
    }

    // ── Folders ───────────────────────────────────────────────────────────────

    private static bool MoveDir(FolderMoveEntry e, Journal j, FolderMoveResult r)
    {
        var src = e.Source;
        var dst = e.Dest;
        if (!Directory.Exists(src) || SamePath(src, dst)) return false;

        if (e.IsManagedShaders && Directory.Exists(dst))
        {
            if (e.ManagedMarkers.Any(m => File.Exists(Path.Combine(dst, m))))
            {
                CrashReporter.Log($"{Tag} '{dst}' is already RHI's — keeping it, removing the old one");
                return CleanOldDir(e, j, r);
            }
            // The new folder's own reshade-shaders: keep it aside the way the shader deploy does
            var keepAs = Path.Combine(Path.GetDirectoryName(dst)!, ShadersOriginalFolder);
            if (Directory.Exists(keepAs))
            {
                r.Skipped.Add($"{src} (the new folder has its own {ShadersFolder} and {ShadersOriginalFolder})");
                CrashReporter.Log($"{Tag} Left '{src}': the new folder has its own '{ShadersFolder}' and '{ShadersOriginalFolder}'");
                return false;
            }
            j.Move(dst, keepAs, isDir: true);
            CrashReporter.Log($"{Tag} Kept the new folder's own '{ShadersFolder}' as '{ShadersOriginalFolder}'");
        }

        if (!Directory.Exists(dst))
        {
            j.EnsureDir(Path.GetDirectoryName(dst)!);
            j.Move(src, dst, isDir: true);
            r.Moved.Add(dst);
            CrashReporter.Log($"{Tag} Moved folder '{src}' → '{dst}'");
        }
        else
        {
            // Merge: the new folder's files win, the rest move across
            foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories).ToList())
            {
                var target = Path.Combine(dst, Path.GetRelativePath(src, file));
                if (File.Exists(target)) { j.Trash(file); r.Removed.Add(file); continue; }
                j.EnsureDir(Path.GetDirectoryName(target)!);
                j.Move(file, target);
                r.Moved.Add(target);
            }
            j.RemoveEmptyTree(src);
            CrashReporter.Log($"{Tag} Merged folder '{src}' into '{dst}'");
        }
        if (e.IsManagedShaders) RestoreShadersOriginal(src, j, r);
        return true;
    }

    private static bool CleanOldDir(FolderMoveEntry e, Journal j, FolderMoveResult r)
    {
        if (!Directory.Exists(e.Source)) return false;
        j.Trash(e.Source, isDir: true);
        r.Removed.Add(e.Source);
        CrashReporter.Log($"{Tag} Removed old folder '{e.Source}' (the new folder has its own)");
        if (e.IsManagedShaders) RestoreShadersOriginal(e.Source, j, r);
        return true;
    }

    private static void RestoreShadersOriginal(string shadersDir, Journal j, FolderMoveResult r)
    {
        var orig = Path.Combine(Path.GetDirectoryName(shadersDir)!, ShadersOriginalFolder);
        if (Directory.Exists(orig) && !Directory.Exists(shadersDir))
        {
            j.Move(orig, shadersDir, isDir: true);
            r.Restored.Add(shadersDir);
            CrashReporter.Log($"{Tag} Restored the old folder's own '{ShadersFolder}' from '{ShadersOriginalFolder}'");
        }
    }

    private static void RemoveEmptyParents(IEnumerable<string> dirs, string oldDir)
    {
        foreach (var dir in dirs.Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(d => d.Length))
        {
            var cur = dir;
            while (IsInside(cur, oldDir) && !SamePath(cur, oldDir))
            {
                try
                {
                    if (!Directory.Exists(cur) || Directory.EnumerateFileSystemEntries(cur).Any()) break;
                    Directory.Delete(cur);
                    CrashReporter.Log($"{Tag} Removed empty folder '{cur}'");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    CrashReporter.Log($"{Tag} Couldn't remove empty folder '{cur}' — {ex.Message}");
                    break;
                }
                cur = Path.GetDirectoryName(cur) ?? oldDir;
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Game Pass / Microsoft Store folder (C:\Program Files\WindowsApps\...): access-denied, never written.</summary>
    public static bool IsWindowsApps(string path)
        => (path.Replace('/', '\\') + "\\").Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase);

    /// <summary>binkw64.dll → binkw64Hooked.dll in the same folder.</summary>
    public static string HookedPath(string path)
        => Path.Combine(Path.GetDirectoryName(path) ?? "", HookedDllNames.ToHooked(Path.GetFileName(path)));

    public static string Norm(string path) => Path.GetFullPath(path.Replace('/', '\\')).TrimEnd('\\');

    public static bool SamePath(string a, string b) => string.Equals(Norm(a), Norm(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="path"/> is <paramref name="dir"/> or inside it.</summary>
    public static bool IsInside(string path, string dir)
    {
        var p = Norm(path);
        var d = Norm(dir);
        return p.Equals(d, StringComparison.OrdinalIgnoreCase)
            || p.StartsWith(d + "\\", StringComparison.OrdinalIgnoreCase);
    }

    // ── Journal (undo log) ────────────────────────────────────────────────────

    private sealed class Journal
    {
        private readonly List<(string What, Action Undo)> _undo = new();
        private readonly string _trashDir;
        private int _trashCount;
        public List<string> VacatedDirs { get; } = new();

        public Journal(string trashDir) { _trashDir = trashDir; }

        public void Move(string from, string to, bool isDir = false)
        {
            if (isDir) Directory.Move(from, to); else File.Move(from, to);
            _undo.Add(($"move '{from}' → '{to}'", () => { if (isDir) Directory.Move(to, from); else File.Move(to, from); }));
            var parent = Path.GetDirectoryName(from);
            if (parent != null) VacatedDirs.Add(parent);
        }

        public void CreateEmpty(string path)
        {
            File.WriteAllBytes(path, Array.Empty<byte>());
            _undo.Add(($"create '{path}'", () => File.Delete(path)));
        }

        public void DeleteEmpty(string path)
        {
            File.Delete(path);
            _undo.Add(($"delete '{path}'", () => File.WriteAllBytes(path, Array.Empty<byte>())));
        }

        public void EnsureDir(string dir)
        {
            var missing = new List<string>();
            for (var cur = dir; !string.IsNullOrEmpty(cur) && !Directory.Exists(cur); cur = Path.GetDirectoryName(cur))
                missing.Add(cur);
            for (int i = missing.Count - 1; i >= 0; i--)
            {
                var d = missing[i];
                Directory.CreateDirectory(d);
                _undo.Add(($"create folder '{d}'", () => { if (Directory.Exists(d) && !Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d); }));
            }
        }

        /// <summary>Moves a file/folder into the trash folder (deleted on commit, put back on rollback).</summary>
        public void Trash(string path, bool isDir = false)
        {
            EnsureDir(_trashDir);
            var target = Path.Combine(_trashDir, $"{_trashCount++}_{Path.GetFileName(path)}");
            Move(path, target, isDir);
        }

        public void Write(string path, string content)
        {
            byte[]? previous = File.Exists(path) ? File.ReadAllBytes(path) : null;
            EnsureDir(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            _undo.Add(($"write '{path}'", () => { if (previous != null) File.WriteAllBytes(path, previous); else File.Delete(path); }));
            CrashReporter.Log($"{Tag} Wrote '{path}'");
        }

        /// <summary>Removes the empty folders left under <paramref name="root"/> (and it) after a merge.</summary>
        public void RemoveEmptyTree(string root)
        {
            if (!Directory.Exists(root)) return;
            foreach (var d in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                         .OrderByDescending(d => d.Length).Append(root).ToList())
            {
                if (Directory.EnumerateFileSystemEntries(d).Any()) continue;
                Directory.Delete(d);
                var dir = d;
                _undo.Add(($"remove folder '{dir}'", () => Directory.CreateDirectory(dir)));
            }
        }

        public void Rollback()
        {
            for (int i = _undo.Count - 1; i >= 0; i--)
            {
                try { _undo[i].Undo(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { CrashReporter.Log($"{Tag} Undo failed for {_undo[i].What} — {ex.Message}"); }
            }
            _undo.Clear();
            // Only an empty trash folder goes: anything still in it couldn't be put back, so keep it
            try { if (Directory.Exists(_trashDir) && !Directory.EnumerateFileSystemEntries(_trashDir).Any()) Directory.Delete(_trashDir); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { CrashReporter.Log($"{Tag} Couldn't delete '{_trashDir}' — {ex.Message}"); }
        }

        public void Commit()
        {
            _undo.Clear();
            DeleteTrash();
        }

        private void DeleteTrash()
        {
            try { if (Directory.Exists(_trashDir)) Directory.Delete(_trashDir, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { CrashReporter.Log($"{Tag} Couldn't delete '{_trashDir}' — {ex.Message}"); }
        }
    }
}
