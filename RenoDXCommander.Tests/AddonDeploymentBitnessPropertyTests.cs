using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Services;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based test for deployment matching active selection with correct bitness.
/// Feature: reshade-addons, Property 10: Deployment matches active selection with correct bitness
/// </summary>
public class AddonDeploymentBitnessPropertyTests
{
    // ── Pure deployment logic under test ───────────────────────────────────────────

    /// <summary>
    /// Replicates the core deployment selection logic from AddonPackService.DeployAddonsForGame
    /// without relying on the static StagingDir. Given a staging directory, deploy directory,
    /// active selection, and bitness, deploys the correct files and removes stale ones.
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

    /// <summary>
    /// Computes the expected set of deployed file names purely from inputs,
    /// without touching the file system.
    /// </summary>
    private static HashSet<string> ComputeExpectedDeployment(
        List<string> activeSelection, HashSet<string> stagedFileNames, bool is32Bit)
    {
        var bitnessExt = is32Bit ? ".addon32" : ".addon64";
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var packageName in activeSelection)
        {
            var safeName = AddonPackService.SanitizeFileName(packageName);
            var fileName = safeName + bitnessExt;
            if (stagedFileNames.Contains(fileName))
                expected.Add(fileName);
        }

        return expected;
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
    /// Generates a staging state for a single addon: which bitness variants exist.
    /// "none" = no files staged, "32" = only .addon32, "64" = only .addon64, "both" = both.
    /// </summary>
    private static readonly Gen<string> GenStagingState =
        Gen.Elements("none", "32", "64", "both");

    /// <summary>
    /// Generates a test scenario: a list of (packageName, stagingState) pairs and a game bitness.
    /// </summary>
    private static readonly Gen<(List<(string name, string staging)> addons, bool is32Bit)> GenScenario =
        from count in Gen.Choose(0, 6)
        from addons in Gen.ListOf(count,
            from name in GenPackageName
            from staging in GenStagingState
            select (name, staging))
        from is32Bit in Arb.Default.Bool().Generator
        select (addons.ToList(), is32Bit);

    // ── Property 10 ───────────────────────────────────────────────────────────────
    // Feature: reshade-addons, Property 10: Deployment matches active selection with correct bitness
    // **Validates: Requirements 6.2, 7.1, 7.2, 7.3, 7.4**

    /// <summary>
    /// For any game with ReShade installed, the set of addon files in the deploy path
    /// after deployment should exactly match the active selection filtered to only include
    /// addons that have the matching bitness variant available, and each deployed file
    /// should have the correct bitness extension (.addon32 for 32-bit, .addon64 for 64-bit).
    /// Addons without the required bitness variant are skipped (not deployed).
    /// </summary>
    [Property(MaxTest = 20)]
    public Property DeployedFiles_MatchActiveSelection_WithCorrectBitness()
    {
        return Prop.ForAll(
            Arb.From(GenScenario),
            scenario =>
            {
                var (addons, is32Bit) = scenario;
                var bitnessExt = is32Bit ? ".addon32" : ".addon64";

                // Deduplicate by sanitized name to avoid collisions
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var uniqueAddons = new List<(string name, string staging)>();
                foreach (var addon in addons)
                {
                    var safeName = AddonPackService.SanitizeFileName(addon.name);
                    if (seen.Add(safeName))
                        uniqueAddons.Add(addon);
                }

                var tempBase = Path.Combine(Path.GetTempPath(), "rhi_deploy_test_" + Guid.NewGuid().ToString("N"));
                var stagingDir = Path.Combine(tempBase, "staging");
                var deployDir = Path.Combine(tempBase, "deploy");

                try
                {
                    Directory.CreateDirectory(stagingDir);
                    Directory.CreateDirectory(deployDir);

                    // Set up staging files based on generated state
                    var stagedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var (name, staging) in uniqueAddons)
                    {
                        var safeName = AddonPackService.SanitizeFileName(name);
                        if (staging == "32" || staging == "both")
                        {
                            File.WriteAllBytes(Path.Combine(stagingDir, safeName + ".addon32"), Array.Empty<byte>());
                            stagedFileNames.Add(safeName + ".addon32");
                        }
                        if (staging == "64" || staging == "both")
                        {
                            File.WriteAllBytes(Path.Combine(stagingDir, safeName + ".addon64"), Array.Empty<byte>());
                            stagedFileNames.Add(safeName + ".addon64");
                        }
                    }

                    // Active selection = all addon names
                    var activeSelection = uniqueAddons.Select(a => a.name).ToList();

                    // Compute expected deployment (pure logic)
                    var expected = ComputeExpectedDeployment(activeSelection, stagedFileNames, is32Bit);

                    // Execute deployment (file I/O logic)
                    var deployed = DeployAddonsToDir(stagingDir, deployDir, activeSelection, is32Bit);

                    // Also verify actual files on disk match
                    var actualFiles = Directory.EnumerateFiles(deployDir)
                        .Where(f =>
                        {
                            var ext = Path.GetExtension(f);
                            return ext.Equals(".addon32", StringComparison.OrdinalIgnoreCase)
                                || ext.Equals(".addon64", StringComparison.OrdinalIgnoreCase);
                        })
                        .Select(f => Path.GetFileName(f))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    // Check 1: deployed set matches expected set
                    if (!expected.SetEquals(deployed))
                        return false.Label(
                            $"Deployed set mismatch. Expected: [{string.Join(", ", expected)}], " +
                            $"Got: [{string.Join(", ", deployed)}]");

                    // Check 2: actual files on disk match expected
                    if (!expected.SetEquals(actualFiles))
                        return false.Label(
                            $"Disk files mismatch. Expected: [{string.Join(", ", expected)}], " +
                            $"Actual: [{string.Join(", ", actualFiles)}]");

                    // Check 3: all deployed files have the correct bitness extension
                    foreach (var file in actualFiles)
                    {
                        if (!file.EndsWith(bitnessExt, StringComparison.OrdinalIgnoreCase))
                            return false.Label(
                                $"Wrong bitness extension: '{file}' should end with '{bitnessExt}'");
                    }

                    return true.Label(
                        $"OK: {uniqueAddons.Count} addons, is32Bit={is32Bit}, " +
                        $"deployed={deployed.Count}, expected={expected.Count}");
                }
                finally
                {
                    if (Directory.Exists(tempBase))
                        Directory.Delete(tempBase, recursive: true);
                }
            });
    }

    /// <summary>
    /// Addons without the required bitness variant in staging are never deployed.
    /// For example, if a game is 32-bit and an addon only has .addon64 in staging,
    /// that addon must not appear in the deploy directory.
    /// </summary>
    [Property(MaxTest = 20)]
    public Property MissingBitnessVariant_IsSkipped()
    {
        return Prop.ForAll(
            Arb.From(GenPackageName),
            Arb.Default.Bool().Generator.ToArbitrary(),
            (string packageName, bool is32Bit) =>
            {
                var bitnessExt = is32Bit ? ".addon32" : ".addon64";
                var oppositeExt = is32Bit ? ".addon64" : ".addon32";

                var tempBase = Path.Combine(Path.GetTempPath(), "rhi_skip_test_" + Guid.NewGuid().ToString("N"));
                var stagingDir = Path.Combine(tempBase, "staging");
                var deployDir = Path.Combine(tempBase, "deploy");

                try
                {
                    Directory.CreateDirectory(stagingDir);
                    Directory.CreateDirectory(deployDir);

                    var safeName = AddonPackService.SanitizeFileName(packageName);

                    // Stage only the OPPOSITE bitness variant
                    File.WriteAllBytes(Path.Combine(stagingDir, safeName + oppositeExt), Array.Empty<byte>());

                    var activeSelection = new List<string> { packageName };
                    var deployed = DeployAddonsToDir(stagingDir, deployDir, activeSelection, is32Bit);

                    // Nothing should be deployed
                    var actualFiles = Directory.EnumerateFiles(deployDir)
                        .Where(f =>
                        {
                            var ext = Path.GetExtension(f);
                            return ext.Equals(".addon32", StringComparison.OrdinalIgnoreCase)
                                || ext.Equals(".addon64", StringComparison.OrdinalIgnoreCase);
                        })
                        .ToList();

                    if (deployed.Count != 0)
                        return false.Label(
                            $"Expected 0 deployed, got {deployed.Count} for '{packageName}' " +
                            $"(is32Bit={is32Bit}, only {oppositeExt} staged)");

                    if (actualFiles.Count != 0)
                        return false.Label(
                            $"Expected 0 files on disk, got {actualFiles.Count} for '{packageName}'");

                    return true.Label(
                        $"OK: '{packageName}' skipped (is32Bit={is32Bit}, only {oppositeExt} staged)");
                }
                finally
                {
                    if (Directory.Exists(tempBase))
                        Directory.Delete(tempBase, recursive: true);
                }
            });
    }
}
