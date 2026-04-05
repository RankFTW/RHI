using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Services;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based test for missing bitness variant skipping addon deployment.
/// Feature: reshade-addons, Property 16: Missing bitness variant skips addon deployment
/// **Validates: Requirements 7.3, 7.4**
/// </summary>
public class AddonMissingBitnessSkipPropertyTests
{
    // ── Pure deployment logic under test ───────────────────────────────────────────

    /// <summary>
    /// Replicates the core deployment selection logic from AddonPackService.DeployAddonsForGame.
    /// Given a staging directory, deploy directory, active selection, and bitness,
    /// deploys the correct files and removes stale ones.
    /// </summary>
    private static HashSet<string> DeployAddonsToDir(
        string stagingDir, string deployDir, List<string> activeSelection, bool is32Bit)
    {
        var bitnessExt = is32Bit ? ".addon32" : ".addon64";
        Directory.CreateDirectory(deployDir);

        var deployedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var packageName in activeSelection)
        {
            var safeName = AddonPackService.SanitizeFileName(packageName);
            var stagingFile = Path.Combine(stagingDir, safeName + bitnessExt);

            if (!File.Exists(stagingFile))
                continue;

            var destFile = Path.Combine(deployDir, safeName + bitnessExt);
            File.Copy(stagingFile, destFile, overwrite: true);
            deployedFileNames.Add(safeName + bitnessExt);
        }

        // Remove stale addon files
        foreach (var file in Directory.EnumerateFiles(deployDir))
        {
            var ext = Path.GetExtension(file);
            if (!ext.Equals(".addon32", StringComparison.OrdinalIgnoreCase) &&
                !ext.Equals(".addon64", StringComparison.OrdinalIgnoreCase))
                continue;

            var fileName = Path.GetFileName(file);
            if (!deployedFileNames.Contains(fileName))
                File.Delete(file);
        }

        return deployedFileNames;
    }

    // ── Generators ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates addon package names using safe characters.
    /// </summary>
    private static readonly Gen<string> GenPackageName =
        from len in Gen.Choose(1, 20)
        from chars in Gen.ArrayOf(len, Gen.Elements(
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 -_".ToCharArray()))
        select new string(chars);

    /// <summary>
    /// Staging variant: which bitness files are present for an addon.
    /// "none"     = no files staged
    /// "correct"  = only the matching bitness variant staged
    /// "wrong"    = only the opposite bitness variant staged
    /// "both"     = both variants staged
    /// </summary>
    private static readonly Gen<string> GenStagingVariant =
        Gen.Elements("none", "correct", "wrong", "both");

    /// <summary>
    /// Generates a test scenario: a list of (packageName, stagingVariant) pairs and a game bitness.
    /// </summary>
    private static readonly Gen<(List<(string name, string variant)> addons, bool is32Bit)> GenScenario =
        from count in Gen.Choose(1, 8)
        from addons in Gen.ListOf(count,
            from name in GenPackageName
            from variant in GenStagingVariant
            select (name, variant))
        from is32Bit in Arb.Default.Bool().Generator
        select (addons.ToList(), is32Bit);

    // ── Property 16 ───────────────────────────────────────────────────────────────
    // Feature: reshade-addons, Property 16: Missing bitness variant skips addon deployment
    // **Validates: Requirements 7.3, 7.4**

    /// <summary>
    /// For any addon in the active selection and any game bitness, if the addon does not
    /// have the required bitness variant file (.addon32 for 32-bit, .addon64 for 64-bit)
    /// in the staging directory, that addon should not be deployed to the game's folder.
    /// No fallback variant should be used.
    /// </summary>
    [Property(MaxTest = 20)]
    public Property MissingBitnessVariant_SkipsAddonDeployment()
    {
        return Prop.ForAll(
            Arb.From(GenScenario),
            scenario =>
            {
                var (addons, is32Bit) = scenario;
                var correctExt = is32Bit ? ".addon32" : ".addon64";
                var wrongExt = is32Bit ? ".addon64" : ".addon32";

                // Deduplicate by sanitized name to avoid collisions
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var uniqueAddons = new List<(string name, string variant)>();
                foreach (var addon in addons)
                {
                    var safeName = AddonPackService.SanitizeFileName(addon.name);
                    if (seen.Add(safeName))
                        uniqueAddons.Add(addon);
                }

                var tempBase = Path.Combine(Path.GetTempPath(),
                    "rhi_missing_bitness_test_" + Guid.NewGuid().ToString("N"));
                var stagingDir = Path.Combine(tempBase, "staging");
                var deployDir = Path.Combine(tempBase, "deploy");

                try
                {
                    Directory.CreateDirectory(stagingDir);
                    Directory.CreateDirectory(deployDir);

                    // Track which addons have the correct variant staged
                    var addonsWithCorrectVariant = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    // Set up staging files based on generated variant
                    foreach (var (name, variant) in uniqueAddons)
                    {
                        var safeName = AddonPackService.SanitizeFileName(name);

                        switch (variant)
                        {
                            case "correct":
                                File.WriteAllBytes(
                                    Path.Combine(stagingDir, safeName + correctExt),
                                    Array.Empty<byte>());
                                addonsWithCorrectVariant.Add(safeName);
                                break;
                            case "wrong":
                                // Only the wrong variant — should NOT be deployed
                                File.WriteAllBytes(
                                    Path.Combine(stagingDir, safeName + wrongExt),
                                    Array.Empty<byte>());
                                break;
                            case "both":
                                File.WriteAllBytes(
                                    Path.Combine(stagingDir, safeName + correctExt),
                                    Array.Empty<byte>());
                                File.WriteAllBytes(
                                    Path.Combine(stagingDir, safeName + wrongExt),
                                    Array.Empty<byte>());
                                addonsWithCorrectVariant.Add(safeName);
                                break;
                            case "none":
                                // Nothing staged
                                break;
                        }
                    }

                    // Active selection = all addon names
                    var activeSelection = uniqueAddons.Select(a => a.name).ToList();

                    // Execute deployment
                    var deployed = DeployAddonsToDir(stagingDir, deployDir, activeSelection, is32Bit);

                    // Verify actual files on disk
                    var actualFiles = Directory.EnumerateFiles(deployDir)
                        .Where(f =>
                        {
                            var ext = Path.GetExtension(f);
                            return ext.Equals(".addon32", StringComparison.OrdinalIgnoreCase)
                                || ext.Equals(".addon64", StringComparison.OrdinalIgnoreCase);
                        })
                        .Select(f => Path.GetFileName(f))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    // Check: addons with ONLY the wrong variant must NOT be deployed
                    foreach (var (name, variant) in uniqueAddons)
                    {
                        var safeName = AddonPackService.SanitizeFileName(name);

                        if (variant == "wrong" || variant == "none")
                        {
                            // Should NOT be deployed at all
                            var correctFile = safeName + correctExt;
                            var wrongFile = safeName + wrongExt;

                            if (actualFiles.Contains(correctFile))
                                return false.Label(
                                    $"Addon '{name}' (variant={variant}) should not be deployed " +
                                    $"but '{correctFile}' found in deploy dir");

                            if (actualFiles.Contains(wrongFile))
                                return false.Label(
                                    $"Addon '{name}' (variant={variant}) used fallback — " +
                                    $"'{wrongFile}' found in deploy dir (no fallback allowed)");
                        }
                        else
                        {
                            // "correct" or "both" — should be deployed with correct extension only
                            var correctFile = safeName + correctExt;
                            var wrongFile = safeName + wrongExt;

                            if (!actualFiles.Contains(correctFile))
                                return false.Label(
                                    $"Addon '{name}' (variant={variant}) should be deployed " +
                                    $"but '{correctFile}' not found in deploy dir");

                            if (actualFiles.Contains(wrongFile))
                                return false.Label(
                                    $"Addon '{name}' (variant={variant}) deployed wrong variant — " +
                                    $"'{wrongFile}' should not be in deploy dir");
                        }
                    }

                    // Check: deployed count matches addons with correct variant
                    if (deployed.Count != addonsWithCorrectVariant.Count)
                        return false.Label(
                            $"Deployed count mismatch. Expected {addonsWithCorrectVariant.Count}, " +
                            $"got {deployed.Count}");

                    return true.Label(
                        $"OK: {uniqueAddons.Count} addons, is32Bit={is32Bit}, " +
                        $"deployed={deployed.Count}/{uniqueAddons.Count}");
                }
                finally
                {
                    if (Directory.Exists(tempBase))
                        Directory.Delete(tempBase, recursive: true);
                }
            });
    }
}
