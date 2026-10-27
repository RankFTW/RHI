// ShaderPackService.Deploy.cs — Shader deployment, sync, pruning, marker/ownership
using System.Text.Json;

namespace RenoDXCommander.Services;

public partial class ShaderPackService
{
    // ── Pack filtering ────────────────────────────────────────────────────────────

    /// <summary>
    /// Expands a set of pack IDs to include all transitive dependencies (Requires).
    /// Prevents infinite loops via visited-set tracking.
    /// </summary>
    public IEnumerable<string> ExpandPackDependencies(IEnumerable<string> packIds)
        => ExpandDependencies(packIds);

    /// <summary>
    /// Expands a set of pack IDs to include all transitive dependencies (Requires).
    /// Prevents infinite loops via visited-set tracking.
    /// </summary>
    private IEnumerable<string> ExpandDependencies(IEnumerable<string> packIds)
    {
        var result = new HashSet<string>(packIds, StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(result);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            var pack = _packs.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (pack?.Requires == null) continue;
            foreach (var dep in pack.Requires)
            {
                if (result.Add(dep))
                    queue.Enqueue(dep);
            }
        }
        return result;
    }

    /// <summary>
    /// Returns packs whose Id is in the given set. Unknown IDs are silently ignored.
    /// </summary>
    private IEnumerable<ShaderPack> PacksForIds(IEnumerable<string> packIds)
    {
        var idSet = new HashSet<string>(packIds, StringComparer.OrdinalIgnoreCase);
        return _packs.Where(p => idSet.Contains(p.Id));
    }

    /// <summary>
    /// Deploys only the packs matching the given <paramref name="packIds"/>,
    /// plus any transitive dependencies declared via <see cref="ShaderPack.Requires"/>.
    /// Used to deploy the user's chosen subset of shader packs.
    /// </summary>
    private void DeployPacksIfAbsent(IEnumerable<string> packIds, string destShadersDir, string destTexturesDir,
        Dictionary<string, HashSet<string>>? fileExclusions = null)
    {
        var expandedIds = ExpandDependencies(packIds);
        var shadersFiles = new List<string>();
        var texturesFiles = new List<string>();

        foreach (var pack in PacksForIds(expandedIds))
        {
            try
            {
                var d = ReadSettings();
                if (d == null || !d.TryGetValue(FileListKey(pack.Id), out var json) || string.IsNullOrEmpty(json))
                    continue;
                var files = JsonSerializer.Deserialize<List<string>>(json) ?? new();
                // Build exclusion set for this pack (leaf filenames)
                HashSet<string>? excluded = null;
                fileExclusions?.TryGetValue(pack.Id, out excluded);
                foreach (var rel in files)
                {
                    if (rel.StartsWith("Shaders" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    {
                        var relSub = rel.Substring("Shaders".Length + 1);
                        if (excluded != null && excluded.Contains(Path.GetFileName(relSub))) continue;
                        shadersFiles.Add(relSub);
                    }
                    else if (rel.StartsWith("Textures" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        texturesFiles.Add(rel.Substring("Textures".Length + 1));
                }
            }
            catch (Exception ex) { CrashReporter.Log($"[ShaderPackService] Failed to read pack record — {ex.Message}"); }
        }

        bool hasRecords = shadersFiles.Count > 0 || texturesFiles.Count > 0;

        if (hasRecords)
        {
            EnsureReShadeHeaders(shadersFiles);
            DeployFileListIfAbsent(ShadersDir, destShadersDir, shadersFiles);
            DeployFileListIfAbsent(TexturesDir, destTexturesDir, texturesFiles);
        }
    }

    /// <summary>
    /// Deploys all known packs to the destination directories.
    /// Fallback used by <see cref="DeployToGameFolder"/> when no specific pack IDs are provided.
    /// </summary>
    private void DeployAllPacksIfAbsent(string destShadersDir, string destTexturesDir,
        Dictionary<string, HashSet<string>>? fileExclusions = null)
    {
        DeployPacksIfAbsent(_packs.Select(p => p.Id), destShadersDir, destTexturesDir, fileExclusions);
    }

    /// <summary>
    /// ReShade framework headers that all shader packs depend on.
    /// These must always be deployed alongside any pack so that shaders can compile.
    /// </summary>
    private static readonly string[] ReShadeHeaders = { "ReShade.fxh", "ReShadeUI.fxh" };

    /// <summary>
    /// Ensures the ReShade framework headers (reshade.fxh, reshadeui.fxh) are included
    /// in the deploy list whenever any shader pack files are being deployed.
    /// These headers live in the staging Shaders folder but aren't tracked per-pack.
    /// </summary>
    private static void EnsureReShadeHeaders(List<string> shadersFiles)
    {
        foreach (var header in ReShadeHeaders)
        {
            if (!shadersFiles.Contains(header, StringComparer.OrdinalIgnoreCase))
                shadersFiles.Add(header);
        }
    }

    // ── Marker / ownership helpers ────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the reshade-shaders folder in <paramref name="gameDir"/>
    /// was placed there by RDXC (contains our marker file).
    /// </summary>
    public bool IsManagedByRdxc(string gameDir)
    {
        var marker = Path.Combine(gameDir, GameReShadeShaders, ManagedMarkerFileName);
        return File.Exists(marker);
    }

    /// <summary>
    /// Writes the RHI ownership marker into the reshade-shaders folder.
    /// Call after creating the folder so future runs recognise it as ours.
    /// </summary>
    private void WriteMarker(string gameDir)
    {
        try
        {
            var rsDir = Path.Combine(gameDir, GameReShadeShaders);
            var marker = Path.Combine(rsDir, ManagedMarkerFileName);
            Directory.CreateDirectory(rsDir);
            // Skip the write when the marker is already correct (this runs on every sync).
            if (File.Exists(marker) && File.ReadAllText(marker) == ManagedMarkerContent) return;
            File.WriteAllText(marker, ManagedMarkerContent);
        }
        catch (Exception ex)
        { CrashReporter.Log($"[ShaderPackService.WriteMarkerFile] Failed to write marker — {ex.Message}"); }
    }

    /// <summary>
    /// If a user-owned reshade-shaders folder was previously renamed to
    /// reshade-shaders-original, rename it back. Called on RS/DC uninstall.
    /// </summary>
    public void RestoreOriginalIfPresent(string gameDir)
    {
        var orig = Path.Combine(gameDir, GameReShadeOriginal);
        var current = Path.Combine(gameDir, GameReShadeShaders);
        if (!Directory.Exists(orig)) return;
        // Only restore if our managed copy is gone
        if (!Directory.Exists(current))
        {
            try { Directory.Move(orig, current); }
            catch (Exception ex)
            { CrashReporter.Log($"[ShaderPackService.RestoreOriginalShaders] Failed to restore original — {ex.Message}"); }
        }
    }

    // ── Game folder deployment ────────────────────────────────────────────────────

    /// <summary>
    /// Deploys staging shaders to <c>gameDir\reshade-shaders\</c>.
    /// If a pre-existing non-RDXC reshade-shaders folder is found, it is renamed
    /// to reshade-shaders-original before creating our managed folder.
    /// When <paramref name="packIds"/> is null, all packs are deployed (fallback for LumaService).
    /// </summary>
    public void DeployToGameFolder(string gameDir, IEnumerable<string>? packIds = null,
        Dictionary<string, HashSet<string>>? fileExclusions = null)
    {
        var rsDir = Path.Combine(gameDir, GameReShadeShaders);

        // If an existing reshade-shaders folder is NOT ours, preserve it
        if (Directory.Exists(rsDir) && !IsManagedByRdxc(gameDir))
        {
            var origDir = Path.Combine(gameDir, GameReShadeOriginal);
            try
            {
                if (!Directory.Exists(origDir))
                    Directory.Move(rsDir, origDir);
                else
                    CrashReporter.Log($"[ShaderPackService.DeployToGameFolder] reshade-shaders-original already exists in {gameDir}; skipping rename");
            }
            catch (Exception ex)
            { CrashReporter.Log($"[ShaderPackService.DeployToGameFolder] Failed to rename existing reshade-shaders — {ex.Message}"); }
        }

        if (packIds != null)
            DeployPacksIfAbsent(packIds, Path.Combine(rsDir, "Shaders"), Path.Combine(rsDir, "Textures"), fileExclusions);
        else
            DeployAllPacksIfAbsent(Path.Combine(rsDir, "Shaders"), Path.Combine(rsDir, "Textures"), fileExclusions);
        WriteMarker(gameDir);
    }

    /// <summary>
    /// Removes the RDXC-managed reshade-shaders folder (only if our marker is present).
    /// If a pre-existing folder was renamed to reshade-shaders-original, it is left alone;
    /// RestoreOriginalIfPresent() handles restoring it on RS/DC uninstall.
    /// If the folder has no marker (user-owned), rename it to reshade-shaders-original.
    /// Called when DC is installed to the same folder (ReShade uses DC global path).
    /// </summary>
    public void RemoveFromGameFolder(string gameDir)
    {
        var rsDir = Path.Combine(gameDir, GameReShadeShaders);
        if (!Directory.Exists(rsDir)) return;

        if (IsManagedByRdxc(gameDir))
        {
            try { Directory.Delete(rsDir, recursive: true); }
            catch (Exception ex) { CrashReporter.Log($"[ShaderPackService.RemoveFromGameFolder] Failed to remove managed reshade-shaders — {ex.Message}"); }
        }
        else
        {
            // User-owned folder — rename to preserve it
            var origDir = Path.Combine(gameDir, GameReShadeOriginal);
            if (!Directory.Exists(origDir))
                try { Directory.Move(rsDir, origDir); }
                catch (Exception ex) { CrashReporter.Log($"[ShaderPackService.RemoveFromGameFolder] Failed to rename user reshade-shaders — {ex.Message}"); }
            else
                CrashReporter.Log($"[ShaderPackService.RemoveFromGameFolder] reshade-shaders-original already exists in {gameDir}; skipping rename of user folder");
        }
    }

    // ── Global sync (called on Refresh) ──────────────────────────────────────────

    /// <summary>
    /// Synchronises the game-local reshade-shaders folder to match the current selection.
    /// Null/empty selection → remove managed shaders and restore originals.
    /// Custom shader sentinel → deploy from user-managed custom directories.
    /// Non-empty selection → prune unselected pack files and deploy selected packs.
    /// </summary>
    public void SyncGameFolder(string gameDir, IEnumerable<string>? selectedPackIds = null,
        Dictionary<string, HashSet<string>>? fileExclusions = null)
    {
        // Null/empty selection → remove managed shaders and restore originals
        if (selectedPackIds == null || !selectedPackIds.Any())
        {
            CrashReporter.Log($"[ShaderPackService.SyncGameFolder] No packs selected → removing managed shaders for {gameDir}");
            if (IsManagedByRdxc(gameDir))
                RemoveFromGameFolder(gameDir);
            RestoreOriginalIfPresent(gameDir);
            return;
        }

        var rsDir = Path.Combine(gameDir, GameReShadeShaders);

        // First time RHI manages this folder: preserve a user's own reshade-shaders as
        // reshade-shaders-original (checked BEFORE the marker is written — WriteMarker creates
        // the directory, which would otherwise make a clean install look like a user folder).
        if (!IsManagedByRdxc(gameDir) && Directory.Exists(rsDir))
        {
            var origDir = Path.Combine(gameDir, GameReShadeOriginal);
            try
            {
                if (!Directory.Exists(origDir))
                    Directory.Move(rsDir, origDir);
                else
                    CrashReporter.Log($"[ShaderPackService.SyncGameFolder] reshade-shaders-original already exists in {gameDir}; skipping rename");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { CrashReporter.Log($"[ShaderPackService.SyncGameFolder] Failed to rename existing reshade-shaders — {ex.Message}"); }
        }

        // Work out exactly which files the folder should hold, then mirror it (copy missing or
        // changed files, delete the rest). Same result as the old wipe-and-redeploy, but no
        // writes when nothing changed.
        var plan = new ShaderFolderPlan();
        if (selectedPackIds.Contains(CustomShaderSentinel))
        {
            // Custom shader source: the user's Custom\Shaders and Custom\Textures folders.
            Directory.CreateDirectory(CustomShadersDir);
            Directory.CreateDirectory(CustomTexturesDir);
            AddCustomFiles(plan, excludedLeafNames: null);
        }
        else
        {
            bool IsCustomFilePack(string id) => id.Equals(CustomFilePackId, StringComparison.OrdinalIgnoreCase)
                                             || id.StartsWith(CustomFilePackId + "_", StringComparison.OrdinalIgnoreCase);
            var normalPacks = selectedPackIds.Where(id => !IsCustomFilePack(id)).ToList();
            if (normalPacks.Count > 0)
                AddPackFiles(plan, normalPacks, fileExclusions);
            if (selectedPackIds.Any(IsCustomFilePack))
                AddCustomFiles(plan, CustomFileExclusions(fileExclusions));
        }

        if (CrashReporter.VerboseLogging)
            CrashReporter.Log($"[ShaderPackService.SyncGameFolder] gameDir={gameDir}, {plan.Count} files planned");

        MirrorManagedFolder(gameDir, plan);
    }

    // ── Private copy helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the staged source file differs from the deployed destination file.
    /// Compares file size first (fast), then falls back to byte-level comparison if sizes match
    /// but the staging file is newer — covers edge cases where content changes without size change.
    /// </summary>
    private bool IsFileStale(string srcPath, string destPath)
    {
        try
        {
            var srcInfo  = new FileInfo(srcPath);
            var destInfo = new FileInfo(destPath);

            // Different size → definitely stale
            if (srcInfo.Length != destInfo.Length) return true;

            // Same size but source is newer → compare bytes
            if (srcInfo.LastWriteTimeUtc > destInfo.LastWriteTimeUtc)
            {
                // Quick byte comparison (read in 8 KB chunks)
                using var fs1 = File.OpenRead(srcPath);
                using var fs2 = File.OpenRead(destPath);
                var buf1 = new byte[8192];
                var buf2 = new byte[8192];
                int read1;
                while ((read1 = fs1.Read(buf1, 0, buf1.Length)) > 0)
                {
                    var read2 = fs2.Read(buf2, 0, buf2.Length);
                    if (read1 != read2) return true;
                    if (!buf1.AsSpan(0, read1).SequenceEqual(buf2.AsSpan(0, read2))) return true;
                }
                return false; // Identical bytes
            }

            return false; // Same size and destination is same age or newer
        }
        catch
        {
            return false; // On error, don't overwrite
        }
    }

    private void DeployFileListIfAbsent(string sourceDir, string destDir, List<string> relPaths)
    {
        if (!Directory.Exists(sourceDir)) return;
        Directory.CreateDirectory(destDir);
        foreach (var rel in relPaths)
        {
            var src = Path.Combine(sourceDir, rel);
            if (!File.Exists(src)) continue;
            var dest = Path.Combine(destDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

            if (File.Exists(dest))
            {
                // Overwrite if the staged file differs from the deployed file
                if (!IsFileStale(src, dest)) continue;
                try { File.Copy(src, dest, overwrite: true); }
                catch (Exception ex) { CrashReporter.Log($"[ShaderPackService.DeployFileListIfAbsent] Update '{rel}' failed — {ex.Message}"); }
            }
            else
            {
                File.Copy(src, dest, overwrite: false);
            }
        }
    }
}
