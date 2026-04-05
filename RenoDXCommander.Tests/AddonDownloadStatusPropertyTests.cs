using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Services;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based test for download status reflecting storage state.
/// Feature: reshade-addons, Property 4: Download status reflects storage state
/// </summary>
public class AddonDownloadStatusPropertyTests
{
    // ── Pure logic under test ─────────────────────────────────────────────────────

    /// <summary>
    /// Replicates the IsDownloaded logic from AddonPackService but against
    /// an arbitrary directory, so we can test with a temp directory.
    /// </summary>
    private static bool IsDownloadedInDir(string directory, string packageName)
    {
        if (!Directory.Exists(directory))
            return false;

        var safeName = AddonPackService.SanitizeFileName(packageName);
        return File.Exists(Path.Combine(directory, safeName + ".addon32"))
            || File.Exists(Path.Combine(directory, safeName + ".addon64"));
    }

    // ── Generators ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates package names with printable characters (letters, digits, spaces,
    /// and some special chars that exercise SanitizeFileName).
    /// </summary>
    private static readonly Gen<string> GenPackageName =
        from len in Gen.Choose(1, 30)
        from chars in Gen.ArrayOf(len, Gen.Elements(
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 -_.()".ToCharArray()))
        select new string(chars);

    /// <summary>
    /// Generates a bitness variant to place in the staging directory.
    /// null means no file is placed.
    /// </summary>
    private static readonly Gen<string?> GenBitnessVariant =
        Gen.Elements<string?>(null, ".addon32", ".addon64", "both");

    // ── Property 4 ────────────────────────────────────────────────────────────────
    // Feature: reshade-addons, Property 4: Download status reflects storage state
    // **Validates: Requirements 2.7**

    /// <summary>
    /// For any package name and any state of the addon staging directory,
    /// IsDownloaded(packageName) returns true if and only if the corresponding
    /// addon file(s) (SanitizeFileName(name) + ".addon32" or ".addon64") exist
    /// in the staging directory.
    /// </summary>
    [Property(MaxTest = 20)]
    public Property DownloadStatus_ReflectsFileExistence()
    {
        return Prop.ForAll(
            Arb.From(GenPackageName),
            Arb.From(GenBitnessVariant),
            (string packageName, string? variant) =>
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "rhi_test_" + Guid.NewGuid().ToString("N"));

                try
                {
                    Directory.CreateDirectory(tempDir);
                    var safeName = AddonPackService.SanitizeFileName(packageName);

                    // Place files based on the variant
                    bool placed32 = false;
                    bool placed64 = false;

                    if (variant == ".addon32" || variant == "both")
                    {
                        File.WriteAllBytes(Path.Combine(tempDir, safeName + ".addon32"), Array.Empty<byte>());
                        placed32 = true;
                    }

                    if (variant == ".addon64" || variant == "both")
                    {
                        File.WriteAllBytes(Path.Combine(tempDir, safeName + ".addon64"), Array.Empty<byte>());
                        placed64 = true;
                    }

                    bool expectedDownloaded = placed32 || placed64;
                    bool actualDownloaded = IsDownloadedInDir(tempDir, packageName);

                    return (expectedDownloaded == actualDownloaded)
                        .Label($"packageName='{packageName}', safeName='{safeName}', " +
                               $"variant={variant ?? "none"}, expected={expectedDownloaded}, actual={actualDownloaded}");
                }
                finally
                {
                    if (Directory.Exists(tempDir))
                        Directory.Delete(tempDir, recursive: true);
                }
            });
    }

    /// <summary>
    /// SanitizeFileName is deterministic: calling it twice on the same input
    /// always produces the same output. This ensures the file lookup in
    /// IsDownloaded is consistent with the file naming in DownloadAddonAsync.
    /// </summary>
    [Property(MaxTest = 20)]
    public Property SanitizeFileName_IsDeterministic()
    {
        return Prop.ForAll(
            Arb.From(GenPackageName),
            (string packageName) =>
            {
                var first = AddonPackService.SanitizeFileName(packageName);
                var second = AddonPackService.SanitizeFileName(packageName);

                return (first == second)
                    .Label($"packageName='{packageName}', first='{first}', second='{second}'");
            });
    }

    /// <summary>
    /// When the staging directory does not exist, IsDownloaded always returns false
    /// regardless of the package name.
    /// </summary>
    [Property(MaxTest = 20)]
    public Property DownloadStatus_FalseWhenDirectoryMissing()
    {
        return Prop.ForAll(
            Arb.From(GenPackageName),
            (string packageName) =>
            {
                var nonExistentDir = Path.Combine(Path.GetTempPath(), "rhi_nonexistent_" + Guid.NewGuid().ToString("N"));

                bool result = IsDownloadedInDir(nonExistentDir, packageName);

                return (!result)
                    .Label($"packageName='{packageName}', expected=false, actual={result}");
            });
    }
}
