namespace RenoDXCommander.Services;

/// <summary>
/// One-time migration service that moves files from the flat downloads root
/// into categorised subdirectories. Runs at startup before any service accesses
/// the downloads folder.
/// </summary>
public static class DownloadsMigrationService
{
    private static readonly string MarkerPath = Path.Combine(DownloadPaths.Root, ".reorganised");

    /// <summary>
    /// Executes the one-time migration. If the marker file already exists,
    /// the migration is skipped entirely.
    /// </summary>
    public static void RunOnce() => RunOnce(DownloadPaths.Root);

    /// <summary>
    /// Internal overload that accepts a custom root path for testability.
    /// Migrates files from <paramref name="rootPath"/> into categorised subdirectories.
    /// </summary>
    internal static void RunOnce(string rootPath)
    {
        try
        {
            var markerPath = Path.Combine(rootPath, ".reorganised");

            if (File.Exists(markerPath))
                return;

            if (!Directory.Exists(rootPath))
                return;

            // Compute subdirectories relative to the provided root
            var subdirs = new[]
            {
                Path.Combine(rootPath, "shaders"),
                Path.Combine(rootPath, "renodx"),
                Path.Combine(rootPath, "framelimiter"),
                Path.Combine(rootPath, "misc"),
                Path.Combine(rootPath, "luma")
            };

            // Track which subdirectories were successfully created
            var createdDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var subdir in subdirs)
            {
                try
                {
                    Directory.CreateDirectory(subdir);
                    createdDirs.Add(subdir);
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[Migration] Failed to create directory {subdir}: {ex.Message}");
                }
            }

            // Enumerate only top-level files (not subdirectories)
            string[] files;
            try
            {
                files = Directory.GetFiles(rootPath);
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[Migration] Failed to enumerate files in {rootPath}: {ex.Message}");
                WriteMarker(markerPath);
                return;
            }

            foreach (var filePath in files)
            {
                var fileName = Path.GetFileName(filePath);

                // Skip the marker file itself
                if (fileName.Equals(".reorganised", StringComparison.OrdinalIgnoreCase))
                    continue;

                var targetSubfolder = ClassifySubfolder(fileName);
                var targetDir = Path.Combine(rootPath, targetSubfolder);

                // Skip if the target subdirectory could not be created
                if (!createdDirs.Contains(targetDir))
                {
                    CrashReporter.Log($"[Migration] Skipping {fileName}: target directory {targetDir} was not created");
                    continue;
                }

                var destPath = Path.Combine(targetDir, fileName);

                // Idempotence: skip if destination already exists
                if (File.Exists(destPath))
                {
                    CrashReporter.Log($"[Migration] Skipping {fileName}: already exists at destination");
                    continue;
                }

                try
                {
                    File.Move(filePath, destPath);
                    CrashReporter.Log($"[Migration] Moved {fileName} → {targetDir}");
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[Migration] Failed to move {fileName}: {ex.Message}");
                }
            }

            WriteMarker(markerPath);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[Migration] Unexpected error during migration: {ex.Message}");
        }
    }

    /// <summary>
    /// Classifies a filename into the appropriate download subdirectory path
    /// using priority-ordered pattern matching.
    /// </summary>
    internal static string ClassifyFile(string fileName)
    {
        var subfolder = ClassifySubfolder(fileName);
        return Path.Combine(DownloadPaths.Root, subfolder);
    }

    /// <summary>
    /// Returns the subfolder name (e.g. "shaders", "renodx") for a given filename.
    /// Used by the root-path overload to build paths relative to any root.
    /// </summary>
    internal static string ClassifySubfolder(string fileName)
    {
        // Priority 1: renodx-*.addon64 or renodx-*.addon32
        if (fileName.StartsWith("renodx-", StringComparison.OrdinalIgnoreCase) &&
            (fileName.EndsWith(".addon64", StringComparison.OrdinalIgnoreCase) ||
             fileName.EndsWith(".addon32", StringComparison.OrdinalIgnoreCase)))
            return "renodx";

        // Priority 2: relimiter.* or dc_lite.* or ul_meta.json or dc_meta.json
        if (fileName.StartsWith("relimiter.", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("dc_lite.", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("ul_meta.json", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("dc_meta.json", StringComparison.OrdinalIgnoreCase))
            return "framelimiter";

        // Priority 3: luma_*
        if (fileName.StartsWith("luma_", StringComparison.OrdinalIgnoreCase))
            return "luma";

        // Priority 4: *.7z or *.zip
        var ext = Path.GetExtension(fileName);
        if (ext.Equals(".7z", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            return "shaders";

        // Priority 5: everything else
        return "misc";
    }

    private static void WriteMarker(string markerPath)
    {
        try
        {
            File.WriteAllBytes(markerPath, []);
            CrashReporter.Log("[Migration] Marker file written");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[Migration] Failed to write marker file: {ex.Message}");
        }
    }
}
