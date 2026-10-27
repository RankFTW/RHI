// ShaderPackService.Mirror.cs — keeps an RHI-managed reshade-shaders folder in sync without
// rewriting it. SyncGameFolder runs for every ReShade game on each launch and Refresh; it used to
// delete the whole folder and copy everything back (~67 files per game, hundreds of syncs per
// session), which also pulled shaders out from under running games. The mirror produces the same
// end result but only copies missing/changed files and deletes files that no longer belong.

using System.Text.Json;

namespace RenoDXCommander.Services;

public partial class ShaderPackService
{
    /// <summary>
    /// Desired contents of a managed reshade-shaders folder:
    /// relative path inside reshade-shaders ("Shaders\x.fx", "Textures\y.png") → staged source file.
    /// </summary>
    private sealed class ShaderFolderPlan : Dictionary<string, string>
    {
        public ShaderFolderPlan() : base(StringComparer.OrdinalIgnoreCase) { }

        public void Add(string subFolder, string rel, string source)
        {
            if (!File.Exists(source)) return;
            this[Path.Combine(subFolder, rel.Replace('/', '\\'))] = source;
        }
    }

    /// <summary>Same file set DeployPacksIfAbsent copies: recorded pack files minus exclusions, plus ReShade headers.</summary>
    private void AddPackFiles(ShaderFolderPlan plan, IEnumerable<string> packIds,
        Dictionary<string, HashSet<string>>? fileExclusions)
    {
        var shaders = new List<string>();
        var textures = new List<string>();
        var d = ReadSettings();

        foreach (var pack in PacksForIds(ExpandDependencies(packIds)))
        {
            try
            {
                if (!d.TryGetValue(FileListKey(pack.Id), out var json) || string.IsNullOrEmpty(json)) continue;
                var files = JsonSerializer.Deserialize<List<string>>(json) ?? new();
                HashSet<string>? excluded = null;
                fileExclusions?.TryGetValue(pack.Id, out excluded);
                foreach (var rel in files)
                {
                    if (rel.StartsWith("Shaders" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    {
                        var relSub = rel.Substring("Shaders".Length + 1);
                        if (excluded != null && excluded.Contains(Path.GetFileName(relSub))) continue;
                        shaders.Add(relSub);
                    }
                    else if (rel.StartsWith("Textures" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        textures.Add(rel.Substring("Textures".Length + 1));
                }
            }
            catch (JsonException ex) { CrashReporter.Log($"[ShaderPackService.AddPackFiles] Bad file record for '{pack.Id}' — {ex.Message}"); }
        }

        if (shaders.Count == 0 && textures.Count == 0) return;
        EnsureReShadeHeaders(shaders);
        foreach (var rel in shaders)  plan.Add("Shaders",  rel, Path.Combine(ShadersDir, rel));
        foreach (var rel in textures) plan.Add("Textures", rel, Path.Combine(TexturesDir, rel));
    }

    /// <summary>Files from the user's Custom\Shaders and Custom\Textures folders, minus excluded leaf names.</summary>
    private void AddCustomFiles(ShaderFolderPlan plan, HashSet<string>? excludedLeafNames)
    {
        void AddDir(string srcDir, string subFolder)
        {
            if (!Directory.Exists(srcDir)) return;
            foreach (var file in Directory.EnumerateFiles(srcDir, "*", SearchOption.AllDirectories))
            {
                if (excludedLeafNames != null && excludedLeafNames.Contains(Path.GetFileName(file))) continue;
                plan.Add(subFolder, Path.GetRelativePath(srcDir, file), file);
            }
        }
        AddDir(CustomShadersDir, "Shaders");
        AddDir(CustomTexturesDir, "Textures");
    }

    /// <summary>Excluded leaf names for the custom-files pack (base key plus per-folder keys).</summary>
    private static HashSet<string> CustomFileExclusions(Dictionary<string, HashSet<string>>? fileExclusions)
    {
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (fileExclusions == null) return excluded;
        foreach (var kv in fileExclusions)
            if (kv.Key.Equals(CustomFilePackId, StringComparison.OrdinalIgnoreCase)
                || kv.Key.StartsWith(CustomFilePackId + "_", StringComparison.OrdinalIgnoreCase))
                excluded.UnionWith(kv.Value);
        return excluded;
    }

    /// <summary>
    /// Makes gameDir\reshade-shaders match <paramref name="plan"/>: copies missing or changed files,
    /// deletes anything else (the folder is RHI-managed, so everything in it is ours), keeps the marker.
    /// No writes when the folder already matches.
    /// </summary>
    private void MirrorManagedFolder(string gameDir, ShaderFolderPlan plan)
    {
        var root = Path.Combine(gameDir, GameReShadeShaders);
        int copied = 0, removed = 0, failed = 0;

        foreach (var (rel, src) in plan)
        {
            var dest = FileHelper.SafeCombine(root, rel);
            if (dest == null) { CrashReporter.Log($"[ShaderPackService.Mirror] Skipping unsafe path '{rel}'"); continue; }
            try
            {
                if (File.Exists(dest) && !IsFileStale(src, dest)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(src, dest, overwrite: true);
                copied++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed++;
                CrashReporter.Log($"[ShaderPackService.Mirror] Copy '{rel}' to '{gameDir}' failed — {ex.Message}");
            }
        }

        if (Directory.Exists(root))
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToList())
            {
                var rel = Path.GetRelativePath(root, file);
                if (rel.Equals(ManagedMarkerFileName, StringComparison.OrdinalIgnoreCase) || plan.ContainsKey(rel))
                    continue;
                try { File.Delete(file); removed++; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    failed++;
                    CrashReporter.Log($"[ShaderPackService.Mirror] Remove '{rel}' from '{gameDir}' failed — {ex.Message}");
                }
            }
            RemoveEmptySubfolders(root);
        }

        Directory.CreateDirectory(Path.Combine(root, "Shaders"));
        Directory.CreateDirectory(Path.Combine(root, "Textures"));
        WriteMarker(gameDir);

        if (copied > 0 || removed > 0 || failed > 0)
            CrashReporter.Log($"[ShaderPackService.Mirror] '{gameDir}': {copied} copied, {removed} removed, {failed} failed ({plan.Count} files wanted)");
    }

    // Bottom-up; keeps Shaders and Textures themselves.
    private static void RemoveEmptySubfolders(string root)
    {
        var dirs = Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
            .OrderByDescending(d => d.Length).ToList();
        foreach (var dir in dirs)
        {
            var rel = Path.GetRelativePath(root, dir);
            if (rel.Equals("Shaders", StringComparison.OrdinalIgnoreCase) || rel.Equals("Textures", StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                CrashReporter.Log($"[ShaderPackService.Mirror] Couldn't remove empty folder '{dir}' — {ex.Message}");
            }
        }
    }
}
