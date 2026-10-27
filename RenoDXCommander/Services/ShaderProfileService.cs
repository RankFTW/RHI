using System.IO.Compression;
using System.Text.Json;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Loads, saves, and exports shader profiles from/to local app data.
/// Profiles are global (not per-game).
/// </summary>
public static class ShaderProfileService
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "shader_profiles.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Loads all saved shader profiles from disk.
    /// Returns an empty list on missing or corrupt file.
    /// </summary>
    public static List<ShaderProfile> Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<List<ShaderProfile>>(json);
                if (loaded != null)
                {
                    CrashReporter.Log($"[ShaderProfileService.Load] Loaded {loaded.Count} profile(s)");
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[ShaderProfileService.Load] Failed to load profiles: {ex.Message}");
        }
        return new List<ShaderProfile>();
    }

    /// <summary>
    /// Saves all shader profiles to disk. Creates the directory if needed.
    /// </summary>
    public static void Save(List<ShaderProfile> profiles)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(profiles, JsonOptions);
            File.WriteAllText(FilePath, json);
            CrashReporter.Log($"[ShaderProfileService.Save] Saved {profiles.Count} profile(s)");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[ShaderProfileService.Save] Failed to save profiles: {ex.Message}");
        }
    }

    /// <summary>
    /// Builds a zip of the staged .fx/.fxh/texture files for the given selection (respecting exclusions).
    /// Returns the path to the temp zip file.
    /// When a profile is provided, it is serialized as shader_import.json and added to the zip.
    /// </summary>
    public static string BuildExportZip(
        List<string> selectedPackIds,
        Dictionary<string, HashSet<string>> fileExclusions,
        IShaderPackService shaderPackService,
        ShaderProfile? profile = null)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var zipPath = Path.Combine(Path.GetTempPath(), $"RHI_Shaders_{timestamp}.zip");

        if (File.Exists(zipPath)) File.Delete(zipPath);

        // Build a temp staging dir of selected files, then zip it
        var tempDir = Path.Combine(Path.GetTempPath(), $"RHI_Shaders_stage_{timestamp}");
        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        Directory.CreateDirectory(tempDir);

        try
        {
            var shadersOut = Path.Combine(tempDir, "Shaders");
            var texturesOut = Path.Combine(tempDir, "Textures");
            Directory.CreateDirectory(shadersOut);
            Directory.CreateDirectory(texturesOut);

            var shadersDir  = ShaderPackService.ShadersDir;
            var texturesDir = ShaderPackService.TexturesDir;

            foreach (var packId in selectedPackIds)
            {
                fileExclusions.TryGetValue(packId, out var excluded);

                // Shaders: walk the pack's subfolder in staging, preserving relative paths
                var packShadersDir = Path.Combine(shadersDir, packId);
                if (Directory.Exists(packShadersDir))
                {
                    foreach (var srcPath in Directory.EnumerateFiles(packShadersDir, "*", SearchOption.AllDirectories))
                    {
                        var leafName = Path.GetFileName(srcPath);
                        if (excluded != null && excluded.Contains(leafName)) continue;

                        // Preserve subfolder structure relative to the pack root
                        var relPath = Path.GetRelativePath(packShadersDir, srcPath);
                        var destPath = Path.Combine(shadersOut, packId, relPath);
                        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                        File.Copy(srcPath, destPath, overwrite: true);
                    }
                }

                // Textures: same pattern
                var packTexturesDir = Path.Combine(texturesDir, packId);
                if (Directory.Exists(packTexturesDir))
                {
                    foreach (var srcPath in Directory.EnumerateFiles(packTexturesDir, "*", SearchOption.AllDirectories))
                    {
                        var relPath  = Path.GetRelativePath(packTexturesDir, srcPath);
                        var destPath = Path.Combine(texturesOut, packId, relPath);
                        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                        File.Copy(srcPath, destPath, overwrite: true);
                    }
                }
            }

            // Also include the ReShade framework headers from the staging root (ReShade.fxh, ReShadeUI.fxh)
            foreach (var header in Directory.EnumerateFiles(shadersDir, "*.fxh", SearchOption.TopDirectoryOnly))
            {
                var destPath = Path.Combine(shadersOut, Path.GetFileName(header));
                File.Copy(header, destPath, overwrite: true);
            }

            ZipFile.CreateFromDirectory(tempDir, zipPath);
            CrashReporter.Log($"[ShaderProfileService.BuildExportZip] Created zip: {zipPath}");

            // Add shader_import.json to the existing zip
            if (profile != null)
            {
                using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Update);
                var entry = archive.CreateEntry("shader_import.json");
                using var writer = new StreamWriter(entry.Open());
                writer.Write(JsonSerializer.Serialize(profile, JsonOptions));
            }
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best-effort cleanup */ }
        }

        return zipPath;
    }

    /// <summary>
    /// Imports a shader profile from a zip archive created by BuildExportZip.
    /// Returns (profile, packIdsWithExtractedFiles) on success, or null on failure.
    /// packIdsWithExtractedFiles is the set of pack IDs whose files were extracted from the zip.
    /// </summary>
    public static (ShaderProfile profile, HashSet<string> extractedPackIds)? ImportFromZip(
        string zipPath,
        IShaderPackService shaderPackService)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);

            // 1. Find and deserialize the profile entry
            var profileEntry = zip.GetEntry("shader_import.json");
            if (profileEntry == null)
            {
                CrashReporter.Log($"[ShaderProfileService.ImportFromZip] Not a valid RHI shader profile archive: {zipPath}");
                return null;
            }

            ShaderProfile? profile;
            try
            {
                using var reader = new StreamReader(profileEntry.Open());
                var json = reader.ReadToEnd();
                profile = JsonSerializer.Deserialize<ShaderProfile>(json, JsonOptions);
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[ShaderProfileService.ImportFromZip] Failed to deserialize profile: {ex.Message}");
                return null;
            }

            if (profile == null)
            {
                CrashReporter.Log($"[ShaderProfileService.ImportFromZip] Profile is null or corrupt");
                return null;
            }

            var shadersDir  = ShaderPackService.ShadersDir;
            var texturesDir = ShaderPackService.TexturesDir;
            Directory.CreateDirectory(shadersDir);
            Directory.CreateDirectory(texturesDir);

            var extractedPackIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 2. Extract files for each selected pack not already cached
            foreach (var packId in profile.SelectedPacks)
            {
                if (shaderPackService.IsPackCached(packId))
                    continue; // already have it

                // packId comes from the imported file; it must be a plain folder name
                if (string.IsNullOrWhiteSpace(packId)
                    || packId != Path.GetFileName(packId)
                    || packId is "." or ".."
                    || packId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    CrashReporter.Log($"[ShaderProfileService.ImportFromZip] Skipping invalid pack id '{packId}'");
                    continue;
                }

                var packShadersPrefix   = $"Shaders/{packId}/";
                var packTexturesPrefix  = $"Textures/{packId}/";

                bool extractedAny = false;
                foreach (var entry in zip.Entries)
                {
                    var entryName = entry.FullName.Replace('\\', '/');

                    string? destRoot = null;
                    string? relPath  = null;

                    if (entryName.StartsWith(packShadersPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        destRoot = Path.Combine(shadersDir, packId);
                        relPath  = entryName.Substring(packShadersPrefix.Length);
                    }
                    else if (entryName.StartsWith(packTexturesPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        destRoot = Path.Combine(texturesDir, packId);
                        relPath  = entryName.Substring(packTexturesPrefix.Length);
                    }

                    if (destRoot == null || string.IsNullOrEmpty(relPath) || entry.Name == "")
                        continue;

                    var destPath = FileHelper.SafeCombine(destRoot, relPath);
                    if (destPath == null)
                    {
                        CrashReporter.Log($"[ShaderProfileService.ImportFromZip] Skipping entry outside the pack folder: '{entry.FullName}'");
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    using var src  = entry.Open();
                    using var dest = File.Create(destPath);
                    src.CopyTo(dest);
                    extractedAny = true;
                }

                if (extractedAny)
                {
                    shaderPackService.RecordExtractedFilesFromDir(packId);
                    extractedPackIds.Add(packId);
                }
            }

            // 3. Extract root-level .fxh files from Shaders/ (e.g. ReShade.fxh)
            foreach (var entry in zip.Entries)
            {
                var entryName = entry.FullName.Replace('\\', '/');
                // Must be directly under Shaders/ with no subdirectory
                if (!entryName.StartsWith("Shaders/", StringComparison.OrdinalIgnoreCase)) continue;
                var rel = entryName.Substring("Shaders/".Length);
                if (rel.Contains('/')) continue; // skip pack subfolders
                if (!rel.EndsWith(".fxh", StringComparison.OrdinalIgnoreCase)) continue;

                var destPath = Path.Combine(shadersDir, rel);
                using var src  = entry.Open();
                using var dest = File.Create(destPath);
                src.CopyTo(dest);
            }

            // 4. Invalidate include cache
            shaderPackService.ClearIncludeCache();

            CrashReporter.Log($"[ShaderProfileService.ImportFromZip] Imported profile '{profile.Name}', extracted {extractedPackIds.Count} pack(s)");
            return (profile, extractedPackIds);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[ShaderProfileService.ImportFromZip] Failed: {ex.Message}");
            return null;
        }
    }
}
