// ShaderPackService.Deploy.cs — Shader deployment, sync, pruning, marker/ownership
using System.Text.Json;

namespace RenoDXCommander.Services;

public partial class ShaderPackService
{
    // ── Pack filtering ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns packs whose Id is in the given set. Unknown IDs are silently ignored.
    /// </summary>
    private IEnumerable<ShaderPack> PacksForIds(IEnumerable<string> packIds)
    {
        var idSet = new HashSet<string>(packIds, StringComparer.OrdinalIgnoreCase);
        return Packs.Where(p => idSet.Contains(p.Id));
    }

    /// <summary>
    /// Deploys only the packs matching the given <paramref name="packIds"/>.
    /// Used to deploy the user's chosen subset of shader packs.
    /// </summary>
    private void DeployPacksIfAbsent(IEnumerable<string> packIds, string destShadersDir, string destTexturesDir)
    {
        var shadersFiles = new List<string>();
        var texturesFiles = new List<string>();

        foreach (var pack in PacksForIds(packIds))
        {
            try
            {
                if (!File.Exists(SettingsPath)) continue;
                var d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(SettingsPath));
                if (d == null || !d.TryGetValue(FileListKey(pack.Id), out var json) || string.IsNullOrEmpty(json))
                    continue;
                var files = JsonSerializer.Deserialize<List<string>>(json) ?? new();
                foreach (var rel in files)
                {
                    if (rel.StartsWith("Shaders" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        shadersFiles.Add(rel.Substring("Shaders".Length + 1));
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
    private void DeployAllPacksIfAbsent(string destShadersDir, string destTexturesDir)
    {
        DeployPacksIfAbsent(Packs.Select(p => p.Id), destShadersDir, destTexturesDir);
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

    /// <summary>
    /// Collects relative paths for all files belonging to packs matching the given <paramref name="packIds"/>.
    /// </summary>
    private (List<string> shaders, List<string> textures) FilesForIds(IEnumerable<string> packIds)
    {
        var shaders = new List<string>();
        var textures = new List<string>();
        try
        {
            if (!File.Exists(SettingsPath)) return (shaders, textures);
            var d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(SettingsPath));
            if (d == null) return (shaders, textures);
            foreach (var pack in PacksForIds(packIds))
            {
                if (!d.TryGetValue(FileListKey(pack.Id), out var json) || string.IsNullOrEmpty(json)) continue;
                var files = JsonSerializer.Deserialize<List<string>>(json) ?? new();
                foreach (var rel in files)
                {
                    if (rel.StartsWith("Shaders" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        shaders.Add(rel.Substring("Shaders".Length + 1));
                    else if (rel.StartsWith("Textures" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        textures.Add(rel.Substring("Textures".Length + 1));
                }
            }
        }
        catch (Exception ex) { CrashReporter.Log($"[ShaderPackService] Operation failed — {ex.Message}"); }
        return (shaders, textures);
    }

    // ── Marker / ownership helpers ────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the reshade-shaders folder in <paramref name="gameDir"/>
    /// was placed there by RDXC (contains our marker file).
    /// </summary>
    public bool IsManagedByRdxc(string gameDir)
    {
        var marker = Path.Combine(gameDir, GameReShadeShaders, ManagedMarkerFile);
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
            var marker = Path.Combine(rsDir, ManagedMarkerFile);
            Directory.CreateDirectory(rsDir);
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
    public void DeployToGameFolder(string gameDir, IEnumerable<string>? packIds = null)
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
            DeployPacksIfAbsent(packIds, Path.Combine(rsDir, "Shaders"), Path.Combine(rsDir, "Textures"));
        else
            DeployAllPacksIfAbsent(Path.Combine(rsDir, "Shaders"), Path.Combine(rsDir, "Textures"));
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
    /// Builds the set of relative paths (relative to ShadersDir / TexturesDir)
    /// that were ever deployed by RDXC for ANY pack — used to identify stale files
    /// during sync.
    /// </summary>
    private (HashSet<string> shaders, HashSet<string> textures) AllKnownPackFiles()
    {
        var shaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var textures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(SettingsPath)) return (shaders, textures);
            var d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(SettingsPath));
            if (d == null) return (shaders, textures);
            foreach (var pack in Packs)
            {
                if (!d.TryGetValue(FileListKey(pack.Id), out var json) || string.IsNullOrEmpty(json)) continue;
                var files = JsonSerializer.Deserialize<List<string>>(json) ?? new();
                foreach (var rel in files)
                {
                    if (rel.StartsWith("Shaders" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        shaders.Add(rel.Substring("Shaders".Length + 1));
                    else if (rel.StartsWith("Textures" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        textures.Add(rel.Substring("Textures".Length + 1));
                }
            }
        }
        catch (Exception ex) { CrashReporter.Log($"[ShaderPackService] Operation failed — {ex.Message}"); }
        return (shaders, textures);
    }

    /// <summary>
    /// Removes from <paramref name="destDir"/> every file in <paramref name="knownFiles"/>
    /// that is NOT in <paramref name="keepFiles"/>. Leaves directories in place.
    /// </summary>
    private void PruneFiles(string destDir, IEnumerable<string> knownFiles, IEnumerable<string> keepFiles)
    {
        if (!Directory.Exists(destDir)) return;
        var keepSet = new HashSet<string>(keepFiles, StringComparer.OrdinalIgnoreCase);
        foreach (var rel in knownFiles)
        {
            if (keepSet.Contains(rel)) continue;
            var path = Path.Combine(destDir, rel);
            if (!File.Exists(path)) continue;
            try { File.Delete(path); }
            catch (Exception ex) { CrashReporter.Log($"[ShaderPackService.PruneFiles] Failed for '{path}' — {ex.Message}"); }
        }
    }

    /// <summary>
    /// Synchronises the game-local reshade-shaders folder to match the current selection.
    /// Null/empty selection → remove managed shaders and restore originals.
    /// Custom shader sentinel → deploy from user-managed custom directories.
    /// Non-empty selection → prune unselected pack files and deploy selected packs.
    /// </summary>
    public void SyncGameFolder(string gameDir, IEnumerable<string>? selectedPackIds = null)
    {
        var rsShaders = Path.Combine(gameDir, GameReShadeShaders, "Shaders");
        var rsTextures = Path.Combine(gameDir, GameReShadeShaders, "Textures");

        var (allKnownShaders, allKnownTextures) = AllKnownPackFiles();

        // Null/empty selection → remove managed shaders and restore originals
        if (selectedPackIds == null || !selectedPackIds.Any())
        {
            CrashReporter.Log($"[ShaderPackService.SyncGameFolder] No packs selected → removing managed shaders for {gameDir}");
            if (IsManagedByRdxc(gameDir))
                RemoveFromGameFolder(gameDir);
            RestoreOriginalIfPresent(gameDir);
            return;
        }

        // ── Custom shader sentinel → deploy from user-managed directories ─────────
        if (selectedPackIds.Contains(CustomShaderSentinel))
        {
            if (CrashReporter.VerboseLogging)
                CrashReporter.Log($"[ShaderPackService.SyncGameFolder] Effective shader source: Custom directories (gameDir={gameDir})");

            // Ensure custom directories exist (create if missing)
            Directory.CreateDirectory(CustomShadersDir);
            Directory.CreateDirectory(CustomTexturesDir);

            // Wipe existing managed shaders completely before deploying custom content.
            // PruneFiles only knows about pack files — custom files from a previous
            // deployment would linger.  A full remove + fresh deploy is the clean path.
            RemoveFromGameFolder(gameDir);

            // Copy custom shaders and textures into the game's reshade-shaders folder
            DeployFolderIfAbsent(CustomShadersDir, rsShaders);
            DeployFolderIfAbsent(CustomTexturesDir, rsTextures);
            WriteMarker(gameDir);
            return;
        }

        // Non-empty selection → prune unselected and deploy selected
        if (CrashReporter.VerboseLogging)
            CrashReporter.Log($"[ShaderPackService.SyncGameFolder] Effective shader source: Pack-based (gameDir={gameDir})");

        CrashReporter.Log($"[ShaderPackService.SyncGameFolder] gameDir={gameDir}, managed={IsManagedByRdxc(gameDir)}");

        if (IsManagedByRdxc(gameDir))
        {
            // Full wipe then redeploy — PruneFiles only knows about pack filenames,
            // so custom shader files from a previous custom-mode deployment would
            // linger.  A clean remove + fresh deploy handles the transition cleanly.
            RemoveFromGameFolder(gameDir);
            DeployPacksIfAbsent(selectedPackIds, rsShaders, rsTextures);
            WriteMarker(gameDir);
        }
        else
        {
            // Not yet managed — handle rename + marker, then deploy selected packs
            var rsDir = Path.Combine(gameDir, GameReShadeShaders);
            if (Directory.Exists(rsDir))
            {
                var origDir = Path.Combine(gameDir, GameReShadeOriginal);
                try
                {
                    if (!Directory.Exists(origDir))
                        Directory.Move(rsDir, origDir);
                    else
                        CrashReporter.Log($"[ShaderPackService.SyncGameFolder] reshade-shaders-original already exists in {gameDir}; skipping rename");
                }
                catch (Exception ex)
                { CrashReporter.Log($"[ShaderPackService.SyncGameFolder] Failed to rename existing reshade-shaders — {ex.Message}"); }
            }

            DeployPacksIfAbsent(selectedPackIds, Path.Combine(rsDir, "Shaders"), Path.Combine(rsDir, "Textures"));
            WriteMarker(gameDir);
        }
    }

    /// <summary>
    /// Synchronises shaders to every game that has ReShade installed.
    /// Called after ↻ Refresh so selection changes take effect immediately everywhere.
    /// Per-game overrides are resolved from <paramref name="locations"/> shaderModeOverride;
    /// otherwise the passed-in <paramref name="selectedPackIds"/> is used.
    /// </summary>
    public void SyncShadersToAllLocations(
        IEnumerable<(string installPath, bool rsInstalled, string? shaderModeOverride)> locations,
        IEnumerable<string>? selectedPackIds = null)
    {
        foreach (var loc in locations)
        {
            if (string.IsNullOrEmpty(loc.installPath) || !Directory.Exists(loc.installPath))
                continue;

            if (!loc.rsInstalled)
                continue;

            SyncGameFolder(loc.installPath, selectedPackIds);
        }
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

    /// <summary>
    /// Full-folder copy fallback: copies all files from sourceDir → destDir,
    /// skipping any that already exist. Used when per-pack file records are absent.
    /// </summary>
    private void DeployFolderIfAbsent(string sourceDir, string destDir)
    {
        if (!Directory.Exists(sourceDir)) return;
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceDir, file);
            var destFile = Path.Combine(destDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);

            if (File.Exists(destFile))
            {
                if (!IsFileStale(file, destFile)) continue;
                try { File.Copy(file, destFile, overwrite: true); }
                catch (Exception ex) { CrashReporter.Log($"[ShaderPackService.DeployFolderIfAbsent] Update '{rel}' failed — {ex.Message}"); }
            }
            else
            {
                File.Copy(file, destFile, overwrite: false);
            }
        }
    }
}
