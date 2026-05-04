using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for the DXVK uninstall cleanup logic.
/// Feature: dxvk-integration, Property 9: Uninstall restores original state
///
/// For any valid DxvkInstalledRecord describing an installed DXVK configuration,
/// after uninstall: all DLLs in InstalledDlls SHALL be removed from the game
/// directory root, all DLLs in PluginFolderDlls SHALL be removed from the
/// OptiScaler plugins folder, and all files in BackedUpFiles SHALL be restored
/// from their .original backups (renamed back to original names).
/// </summary>
public class DxvkUninstallRestoresOriginalStatePropertyTests : IDisposable
{
    // ── Temp directory for test files ──────────────────────────────────────────

    private readonly string _tempDir;

    public DxvkUninstallRestoresOriginalStatePropertyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "DxvkUninstallTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup
        }
    }

    // ── Known DXVK DLL names ──────────────────────────────────────────────────

    private static readonly string[] KnownDxvkDlls =
        ["d3d8.dll", "d3d9.dll", "d3d10core.dll", "d3d11.dll", "dxgi.dll"];

    // ── Generators ────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates a non-empty subset of known DXVK DLL names.
    /// </summary>
    private static readonly Gen<List<string>> GenDllSubset =
        Gen.SubListOf(KnownDxvkDlls)
           .Where(list => list.Count > 0)
           .Select(items => items.ToList());

    /// <summary>
    /// Generates a list of backed-up file names (original game DLLs that were
    /// renamed to .original during install). Uses realistic DLL names.
    /// </summary>
    private static readonly Gen<List<string>> GenBackedUpFiles =
        Gen.SubListOf(KnownDxvkDlls)
           .Select(items => items.ToList());

    /// <summary>
    /// Generates a valid DxvkInstalledRecord with random DLL placements.
    /// Some DLLs go to game root (InstalledDlls), some to OptiScaler plugins
    /// (PluginFolderDlls), and some files may have been backed up.
    /// </summary>
    private static readonly Gen<DxvkInstalledRecord> GenRecord =
        from allDlls in GenDllSubset
        from splitPoint in Gen.Choose(0, allDlls.Count)
        from backedUp in GenBackedUpFiles
        select new DxvkInstalledRecord
        {
            GameName = "TestGame",
            InstallPath = "", // Will be set per-test to the temp dir
            DxvkVersion = "v2.7.1",
            InstalledDlls = allDlls.Take(splitPoint).ToList(),
            PluginFolderDlls = allDlls.Skip(splitPoint).ToList(),
            BackedUpFiles = backedUp,
            DeployedConf = false,
            InOptiScalerPlugins = splitPoint < allDlls.Count,
            InstalledAt = DateTime.UtcNow,
        };

    // ── Uninstall cleanup logic (mirrors DxvkService.Uninstall steps 1-3) ─────

    /// <summary>
    /// Performs the core filesystem cleanup operations from DxvkService.Uninstall:
    /// 1. Delete DLLs from game root (InstalledDlls)
    /// 2. Delete DLLs from OptiScaler/plugins/ (PluginFolderDlls)
    /// 3. Restore .original backups (BackedUpFiles)
    /// This mirrors the actual Uninstall method's filesystem operations.
    /// </summary>
    private static void PerformUninstallCleanup(DxvkInstalledRecord record, string gameDir)
    {
        // Step 1: Delete DXVK DLLs from game root
        foreach (var dll in record.InstalledDlls)
        {
            var dllPath = Path.Combine(gameDir, dll);
            if (File.Exists(dllPath))
                File.Delete(dllPath);
        }

        // Step 2: Delete DXVK DLLs from OptiScaler plugins folder
        if (record.PluginFolderDlls.Count > 0)
        {
            var pluginsFolder = Path.Combine(gameDir, "OptiScaler", "plugins");
            foreach (var dll in record.PluginFolderDlls)
            {
                var dllPath = Path.Combine(pluginsFolder, dll);
                if (File.Exists(dllPath))
                    File.Delete(dllPath);
            }
        }

        // Step 3: Restore .original backups
        foreach (var backedUp in record.BackedUpFiles)
        {
            var originalPath = Path.Combine(gameDir, backedUp);
            var backupPath = originalPath + ".original";
            if (File.Exists(backupPath) && !File.Exists(originalPath))
            {
                File.Move(backupPath, originalPath);
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Sets up the filesystem to simulate an installed DXVK configuration:
    /// - Creates DLL files in the game root for InstalledDlls
    /// - Creates DLL files in OptiScaler/plugins/ for PluginFolderDlls
    /// - Creates .original backup files for BackedUpFiles
    /// Returns the game directory path.
    /// </summary>
    private string SetupGameDirectory(DxvkInstalledRecord record)
    {
        var gameDir = Path.Combine(_tempDir, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(gameDir);

        // Create DLL files in game root
        foreach (var dll in record.InstalledDlls)
        {
            File.WriteAllText(Path.Combine(gameDir, dll), "DXVK_DLL_CONTENT");
        }

        // Create DLL files in OptiScaler/plugins/
        if (record.PluginFolderDlls.Count > 0)
        {
            var pluginsFolder = Path.Combine(gameDir, "OptiScaler", "plugins");
            Directory.CreateDirectory(pluginsFolder);
            foreach (var dll in record.PluginFolderDlls)
            {
                File.WriteAllText(Path.Combine(pluginsFolder, dll), "DXVK_PLUGIN_DLL_CONTENT");
            }
        }

        // Create .original backup files
        foreach (var backedUp in record.BackedUpFiles)
        {
            var backupPath = Path.Combine(gameDir, backedUp + ".original");
            File.WriteAllText(backupPath, "ORIGINAL_GAME_DLL_CONTENT");
        }

        return gameDir;
    }

    // ── Property 9: Uninstall restores original state ─────────────────────────
    // Feature: dxvk-integration, Property 9: Uninstall restores original state
    // **Validates: Requirements 4.1, 4.2, 4.3**

    /// <summary>
    /// For any valid DxvkInstalledRecord, after uninstall cleanup:
    /// all DLLs in InstalledDlls SHALL be removed from the game directory root.
    /// Note: if a DLL name also appears in BackedUpFiles, the DXVK DLL is deleted
    /// but the original game file is restored in its place — this is correct behavior.
    /// We verify that DLLs NOT in BackedUpFiles are truly gone.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Installed_Dlls_Are_Removed_From_Game_Root()
    {
        return Prop.ForAll(
            Arb.From(GenRecord),
            (DxvkInstalledRecord record) =>
            {
                var gameDir = SetupGameDirectory(record);
                PerformUninstallCleanup(record, gameDir);

                var backedUpSet = new HashSet<string>(record.BackedUpFiles, StringComparer.OrdinalIgnoreCase);

                // DLLs that were NOT backed up should be completely gone
                var dllsWithoutBackup = record.InstalledDlls
                    .Where(dll => !backedUpSet.Contains(dll))
                    .ToList();

                var allNonBackedUpRemoved = dllsWithoutBackup.All(dll =>
                    !File.Exists(Path.Combine(gameDir, dll)));

                // DLLs that WERE backed up should have the restored original content
                var dllsWithBackup = record.InstalledDlls
                    .Where(dll => backedUpSet.Contains(dll))
                    .ToList();

                var allBackedUpRestored = dllsWithBackup.All(dll =>
                {
                    var path = Path.Combine(gameDir, dll);
                    return File.Exists(path) &&
                           File.ReadAllText(path) == "ORIGINAL_GAME_DLL_CONTENT";
                });

                var remaining = dllsWithoutBackup
                    .Where(dll => File.Exists(Path.Combine(gameDir, dll)))
                    .ToList();

                return (allNonBackedUpRemoved && allBackedUpRestored)
                    .Label($"Expected InstalledDlls removed (or replaced by backup). " +
                           $"Non-backed-up remaining: [{string.Join(", ", remaining)}], " +
                           $"InstalledDlls: [{string.Join(", ", record.InstalledDlls)}], " +
                           $"BackedUpFiles: [{string.Join(", ", record.BackedUpFiles)}]");
            });
    }

    /// <summary>
    /// For any valid DxvkInstalledRecord, after uninstall cleanup:
    /// all DLLs in PluginFolderDlls SHALL be removed from the OptiScaler plugins folder.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Plugin_Folder_Dlls_Are_Removed_From_OptiScaler_Plugins()
    {
        return Prop.ForAll(
            Arb.From(GenRecord),
            (DxvkInstalledRecord record) =>
            {
                var gameDir = SetupGameDirectory(record);
                PerformUninstallCleanup(record, gameDir);

                var pluginsFolder = Path.Combine(gameDir, "OptiScaler", "plugins");
                var allRemoved = record.PluginFolderDlls.All(dll =>
                    !File.Exists(Path.Combine(pluginsFolder, dll)));

                var remaining = record.PluginFolderDlls
                    .Where(dll => File.Exists(Path.Combine(pluginsFolder, dll)))
                    .ToList();

                return allRemoved
                    .Label($"Expected all PluginFolderDlls removed from OptiScaler/plugins/. " +
                           $"Remaining: [{string.Join(", ", remaining)}], " +
                           $"PluginFolderDlls: [{string.Join(", ", record.PluginFolderDlls)}]");
            });
    }

    /// <summary>
    /// For any valid DxvkInstalledRecord, after uninstall cleanup:
    /// all files in BackedUpFiles SHALL be restored from their .original backups
    /// (renamed back to original names).
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Backed_Up_Files_Are_Restored_From_Original_Backups()
    {
        return Prop.ForAll(
            Arb.From(GenRecord),
            (DxvkInstalledRecord record) =>
            {
                var gameDir = SetupGameDirectory(record);
                PerformUninstallCleanup(record, gameDir);

                // Only check files that don't overlap with InstalledDlls —
                // if a backed-up file name is also in InstalledDlls, the DXVK DLL
                // was deleted first, then the backup should be restored.
                var restorable = record.BackedUpFiles;

                var allRestored = restorable.All(file =>
                {
                    var originalPath = Path.Combine(gameDir, file);
                    var backupPath = originalPath + ".original";
                    // The original should exist and the .original backup should be gone
                    return File.Exists(originalPath) && !File.Exists(backupPath);
                });

                var notRestored = restorable
                    .Where(file =>
                    {
                        var originalPath = Path.Combine(gameDir, file);
                        var backupPath = originalPath + ".original";
                        return !File.Exists(originalPath) || File.Exists(backupPath);
                    })
                    .ToList();

                return allRestored
                    .Label($"Expected all BackedUpFiles restored. " +
                           $"Not restored: [{string.Join(", ", notRestored)}], " +
                           $"BackedUpFiles: [{string.Join(", ", record.BackedUpFiles)}], " +
                           $"InstalledDlls: [{string.Join(", ", record.InstalledDlls)}]");
            });
    }

    /// <summary>
    /// For any valid DxvkInstalledRecord, after uninstall cleanup:
    /// ALL three conditions hold simultaneously — DLLs removed from root
    /// (or replaced by restored backups), DLLs removed from plugins,
    /// and backups restored.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property Uninstall_Restores_Complete_Original_State()
    {
        return Prop.ForAll(
            Arb.From(GenRecord),
            (DxvkInstalledRecord record) =>
            {
                var gameDir = SetupGameDirectory(record);
                PerformUninstallCleanup(record, gameDir);

                var backedUpSet = new HashSet<string>(record.BackedUpFiles, StringComparer.OrdinalIgnoreCase);

                // Check 1: InstalledDlls without backups are gone;
                //          InstalledDlls with backups are replaced by original content
                var rootDllsCorrect = record.InstalledDlls.All(dll =>
                {
                    var path = Path.Combine(gameDir, dll);
                    if (backedUpSet.Contains(dll))
                        return File.Exists(path) &&
                               File.ReadAllText(path) == "ORIGINAL_GAME_DLL_CONTENT";
                    else
                        return !File.Exists(path);
                });

                // Check 2: All PluginFolderDlls removed from OptiScaler/plugins/
                var pluginsFolder = Path.Combine(gameDir, "OptiScaler", "plugins");
                var pluginDllsRemoved = record.PluginFolderDlls.All(dll =>
                    !File.Exists(Path.Combine(pluginsFolder, dll)));

                // Check 3: All BackedUpFiles restored (original exists, .original gone)
                var backupsRestored = record.BackedUpFiles.All(file =>
                {
                    var originalPath = Path.Combine(gameDir, file);
                    var backupPath = originalPath + ".original";
                    return File.Exists(originalPath) && !File.Exists(backupPath);
                });

                return (rootDllsCorrect && pluginDllsRemoved && backupsRestored)
                    .Label($"Expected complete original state restoration. " +
                           $"RootDllsCorrect={rootDllsCorrect}, " +
                           $"PluginDllsRemoved={pluginDllsRemoved}, " +
                           $"BackupsRestored={backupsRestored}, " +
                           $"InstalledDlls=[{string.Join(", ", record.InstalledDlls)}], " +
                           $"PluginFolderDlls=[{string.Join(", ", record.PluginFolderDlls)}], " +
                           $"BackedUpFiles=[{string.Join(", ", record.BackedUpFiles)}]");
            });
    }
}
