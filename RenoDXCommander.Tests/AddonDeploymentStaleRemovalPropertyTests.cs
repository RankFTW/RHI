using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Services;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based test for deployment removing stale addon files.
/// Feature: reshade-addons, Property 11: Deployment removes stale addon files
/// **Validates: Requirements 7.6**
/// </summary>
public class AddonDeploymentStaleRemovalPropertyTests
{
    // ── Pure deployment logic (replicated from AddonDeploymentBitnessPropertyTests) ──

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

    private static readonly Gen<string> GenPackageName =
        from len in Gen.Choose(1, 20)
        from chars in Gen.ArrayOf(len, Gen.Elements(
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 -_".ToCharArray()))
        select new string(chars);

    /// <summary>
    /// Generates a scenario with previous (stale) addon names and new (active) addon names,
    /// plus a game bitness flag.
    /// </summary>
    private static readonly Gen<(List<string> previousNames, List<string> newNames, bool is32Bit)> GenStaleScenario =
        from prevCount in Gen.Choose(1, 5)
        from newCount in Gen.Choose(0, 5)
        from previousNames in Gen.ListOf(prevCount, GenPackageName)
        from newNames in Gen.ListOf(newCount, GenPackageName)
        from is32Bit in Arb.Default.Bool().Generator
        select (previousNames.ToList(), newNames.ToList(), is32Bit);

    // ── Property 11 ───────────────────────────────────────────────────────────────
    // Feature: reshade-addons, Property 11: Deployment removes stale addon files
    // **Validates: Requirements 7.6**

    /// <summary>
    /// For any game's addon deploy path, after a deployment operation, the only addon
    /// files present should be those in the current active selection. Any previously
    /// deployed addon files not in the new selection should be removed.
    /// </summary>
    [Property(MaxTest = 20)]
    public Property StaleAddonFiles_AreRemoved_AfterDeployment()
    {
        return Prop.ForAll(
            Arb.From(GenStaleScenario),
            scenario =>
            {
                var (previousNames, newNames, is32Bit) = scenario;
                var bitnessExt = is32Bit ? ".addon32" : ".addon64";

                // Deduplicate previous names by sanitized form
                var seenPrev = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var uniquePrevious = new List<string>();
                foreach (var name in previousNames)
                {
                    var safe = AddonPackService.SanitizeFileName(name);
                    if (seenPrev.Add(safe))
                        uniquePrevious.Add(name);
                }

                // Deduplicate new names by sanitized form
                var seenNew = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var uniqueNew = new List<string>();
                foreach (var name in newNames)
                {
                    var safe = AddonPackService.SanitizeFileName(name);
                    if (seenNew.Add(safe))
                        uniqueNew.Add(name);
                }

                var tempBase = Path.Combine(Path.GetTempPath(),
                    "rhi_stale_test_" + Guid.NewGuid().ToString("N"));
                var stagingDir = Path.Combine(tempBase, "staging");
                var deployDir = Path.Combine(tempBase, "deploy");

                try
                {
                    Directory.CreateDirectory(stagingDir);
                    Directory.CreateDirectory(deployDir);

                    // 1. Pre-populate deploy directory with "stale" files from previous deployment
                    var staleFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var name in uniquePrevious)
                    {
                        var safeName = AddonPackService.SanitizeFileName(name);
                        var fileName = safeName + bitnessExt;
                        File.WriteAllBytes(Path.Combine(deployDir, fileName), new byte[] { 0xDE, 0xAD });
                        staleFileNames.Add(fileName);
                    }

                    // 2. Set up staging with the new selection's files
                    foreach (var name in uniqueNew)
                    {
                        var safeName = AddonPackService.SanitizeFileName(name);
                        File.WriteAllBytes(
                            Path.Combine(stagingDir, safeName + bitnessExt), new byte[] { 0xCA, 0xFE });
                    }

                    // 3. Run deployment with the new selection
                    DeployAddonsToDir(stagingDir, deployDir, uniqueNew, is32Bit);

                    // 4. Verify: only the new selection's files remain
                    var remainingFiles = Directory.EnumerateFiles(deployDir)
                        .Where(f =>
                        {
                            var ext = Path.GetExtension(f);
                            return ext.Equals(".addon32", StringComparison.OrdinalIgnoreCase)
                                || ext.Equals(".addon64", StringComparison.OrdinalIgnoreCase);
                        })
                        .Select(f => Path.GetFileName(f))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    // Compute expected: only new selection addons that were staged
                    var expectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var name in uniqueNew)
                    {
                        var safeName = AddonPackService.SanitizeFileName(name);
                        var fileName = safeName + bitnessExt;
                        if (File.Exists(Path.Combine(stagingDir, safeName + bitnessExt)))
                            expectedFiles.Add(fileName);
                    }

                    // Check: remaining files match expected exactly
                    if (!expectedFiles.SetEquals(remainingFiles))
                        return false.Label(
                            $"Files mismatch. Expected: [{string.Join(", ", expectedFiles)}], " +
                            $"Remaining: [{string.Join(", ", remainingFiles)}]");

                    // Check: stale-only files (in previous but not in new) are gone
                    var newSafeNames = new HashSet<string>(
                        uniqueNew.Select(n => AddonPackService.SanitizeFileName(n) + bitnessExt),
                        StringComparer.OrdinalIgnoreCase);

                    foreach (var staleFile in staleFileNames)
                    {
                        if (!newSafeNames.Contains(staleFile) && remainingFiles.Contains(staleFile))
                            return false.Label(
                                $"Stale file '{staleFile}' was NOT removed after deployment.");
                    }

                    return true.Label(
                        $"OK: {uniquePrevious.Count} stale, {uniqueNew.Count} new, " +
                        $"is32Bit={is32Bit}, remaining={remainingFiles.Count}");
                }
                finally
                {
                    if (Directory.Exists(tempBase))
                        Directory.Delete(tempBase, recursive: true);
                }
            });
    }
}
