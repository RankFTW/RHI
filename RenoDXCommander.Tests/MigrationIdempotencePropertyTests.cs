// Feature: downloads-folder-reorganisation, Property 3: Migration idempotence
using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Services;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests verifying that <see cref="DownloadsMigrationService.RunOnce(string)"/>
/// does not overwrite files that already exist at their classified destination.
///
/// **Validates: Requirements 10.1**
///
/// Uses the internal <c>RunOnce(string rootPath)</c> overload with temporary
/// directories so tests are fully isolated from the real filesystem.
/// </summary>
public class MigrationIdempotencePropertyTests
{
    // ── Generator ───────────────────────────────────────────────────────

    /// <summary>
    /// Represents a file to place in the downloads root, optionally pre-placed
    /// at its classified destination with sentinel content.
    /// </summary>
    internal sealed record TestFile(string FileName, bool PrePlaceAtDestination);

    /// <summary>
    /// Generates non-empty lists of <see cref="TestFile"/> entries where at least
    /// one file is pre-placed at its destination (to exercise the idempotence path).
    /// Filenames are constrained to valid filesystem characters.
    /// </summary>
    private static Arbitrary<TestFile[]> TestFileSetArbitrary()
    {
        var safeChar = Gen.Elements(
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_-".ToCharArray());
        var shortName = Gen.Choose(1, 12)
            .SelectMany(len => Gen.ArrayOf(len, safeChar))
            .Select(chars => new string(chars));

        // Category-specific filename generators
        var renodxGen = Gen.Elements(".addon64", ".addon32")
            .SelectMany(ext => shortName.Select(mid => $"renodx-{mid}{ext}"));
        var relimiterGen = shortName.Select(s => $"relimiter.{s}");
        var dcLiteGen = shortName.Select(s => $"dc_lite.{s}");
        var metaGen = Gen.Elements("ul_meta.json", "dc_meta.json");
        var lumaGen = shortName.Select(s => $"luma_{s}");
        var shaderGen = Gen.Elements(".7z", ".zip")
            .SelectMany(ext => shortName.Select(name => $"{name}{ext}"));
        var miscExts = Gen.Elements(".exe", ".dll", ".txt", ".dat", ".cfg");
        var miscGen = miscExts.SelectMany(ext =>
            shortName.Where(s =>
                !s.StartsWith("renodx-", StringComparison.OrdinalIgnoreCase) &&
                !s.StartsWith("relimiter.", StringComparison.OrdinalIgnoreCase) &&
                !s.StartsWith("dc_lite.", StringComparison.OrdinalIgnoreCase) &&
                !s.StartsWith("luma_", StringComparison.OrdinalIgnoreCase))
            .Select(name => $"{name}{ext}"));

        var fileNameGen = Gen.Frequency(
            Tuple.Create(2, renodxGen),
            Tuple.Create(2, Gen.OneOf(relimiterGen, dcLiteGen, metaGen)),
            Tuple.Create(2, lumaGen),
            Tuple.Create(2, shaderGen),
            Tuple.Create(2, miscGen));

        var prePlaceGen = Gen.Elements(true, false);

        var testFileGen = fileNameGen.SelectMany(name =>
            prePlaceGen.Select(pre => new TestFile(name, pre)));

        // Generate 1-10 files, ensure at least one is pre-placed
        var setGen = Gen.Choose(1, 10)
            .SelectMany(count => Gen.ArrayOf(count, testFileGen))
            .Select(files =>
            {
                // Deduplicate by filename (case-insensitive)
                var unique = files
                    .GroupBy(f => f.FileName, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToArray();
                // Ensure at least one file is pre-placed
                if (unique.Length > 0 && !unique.Any(f => f.PrePlaceAtDestination))
                    unique[0] = unique[0] with { PrePlaceAtDestination = true };
                return unique;
            })
            .Where(files => files.Length > 0);

        return setGen.ToArbitrary();
    }

    // ── Property ────────────────────────────────────────────────────────

    /// <summary>
    /// Feature: downloads-folder-reorganisation, Property 3: Migration idempotence
    ///
    /// **Validates: Requirements 10.1**
    ///
    /// For any set of files in the downloads root where some files already exist
    /// at their classified destination, running the migration shall not overwrite
    /// existing destination files and shall not throw errors.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Migration_Skips_Existing_Destination_Files_Without_Error()
    {
        return Prop.ForAll(TestFileSetArbitrary(), (TestFile[] testFiles) =>
        {
            var root = Path.Combine(Path.GetTempPath(), "MigIdempotence_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(root);
            var sentinelContent = "SENTINEL_ORIGINAL_CONTENT";

            try
            {
                // Create all subdirectories
                var subfolderNames = new[] { "shaders", "renodx", "framelimiter", "misc", "luma" };
                foreach (var sub in subfolderNames)
                    Directory.CreateDirectory(Path.Combine(root, sub));

                // Place files in root and optionally pre-place at destination
                var preplacedFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var tf in testFiles)
                {
                    var rootFilePath = Path.Combine(root, tf.FileName);
                    File.WriteAllText(rootFilePath, $"root_{tf.FileName}");

                    if (tf.PrePlaceAtDestination)
                    {
                        var subfolder = DownloadsMigrationService.ClassifySubfolder(tf.FileName);
                        var destPath = Path.Combine(root, subfolder, tf.FileName);
                        File.WriteAllText(destPath, sentinelContent);
                        preplacedFiles[tf.FileName] = destPath;
                    }
                }

                // Run migration — should not throw
                DownloadsMigrationService.RunOnce(root);

                // Assert: pre-placed files were NOT overwritten
                foreach (var (fileName, destPath) in preplacedFiles)
                {
                    if (!File.Exists(destPath))
                        return false.Label($"Pre-placed file disappeared: {fileName}");

                    var content = File.ReadAllText(destPath);
                    if (content != sentinelContent)
                        return false.Label(
                            $"Pre-placed file was overwritten: {fileName}, " +
                            $"expected=\"{sentinelContent}\", actual=\"{content}\"");
                }

                // Assert: non-pre-placed files were moved to destination
                foreach (var tf in testFiles)
                {
                    if (tf.PrePlaceAtDestination) continue;

                    var subfolder = DownloadsMigrationService.ClassifySubfolder(tf.FileName);
                    var destPath = Path.Combine(root, subfolder, tf.FileName);
                    if (!File.Exists(destPath))
                        return false.Label($"Non-pre-placed file was not moved: {tf.FileName}");
                }

                return true.Label("All pre-placed files preserved, migration completed without error");
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    try { Directory.Delete(root, recursive: true); }
                    catch { /* best effort cleanup */ }
                }
            }
        });
    }
}
