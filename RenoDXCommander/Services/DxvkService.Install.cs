using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Services;

public partial class DxvkService
{
    // ── Default dxvk.conf template content ────────────────────────────
    private const string DefaultDxvkConfContent =
        """
        dxgi.enableHDR = True
        dxvk.allowFse = False
        dxvk.latencySleep = Auto
        d3d9.dpiAware = True
        """;

    // ── Proxy mode constants (DX8/DX9) ───────────────────────────────
    /// <summary>
    /// Filename for the DXVK DLL when using proxy chain mode.
    /// ReShade's [PROXY] section points to this file.
    /// </summary>
    private const string ProxyDxvkDllName = "dxgi_dxvk.dll";

    /// <summary>Returns true when the API should use proxy chain mode instead of Vulkan layer.</summary>
    private static bool IsProxyModeApi(GraphicsApiType api) =>
        api is GraphicsApiType.DirectX8 or GraphicsApiType.DirectX9;

    // ── Install ───────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task InstallAsync(
        GameCardViewModel card,
        IProgress<(string message, double percent)>? progress = null)
    {
        try
        {
            progress?.Report(("Preparing DXVK install...", 5));

            // ── 1. Ensure staging is ready ───────────────────────────────
            if (!IsStagingReady)
            {
                CrashReporter.Log("[DxvkService.InstallAsync] Staging not ready — downloading");
                await EnsureStagingAsync(progress);
                if (!IsStagingReady)
                {
                    CrashReporter.Log("[DxvkService.InstallAsync] Staging still not ready — aborting");
                    progress?.Report(("DXVK staging not available", 0));
                    return;
                }
            }

            // ── 2. Determine required DLLs ───────────────────────────────
            var (archFolder, dllNames) = DetermineRequiredDlls(card.GraphicsApi, card.Is32Bit);
            CrashReporter.Log($"[DxvkService.InstallAsync] {card.GameName}: API={card.GraphicsApi}, Is32Bit={card.Is32Bit}, arch={archFolder}, DLLs=[{string.Join(", ", dllNames)}]");

            // ── 3. Validate deployment path ──────────────────────────────
            if (!IsValidDeploymentPath(card.InstallPath))
            {
                var msg = $"Cannot deploy DXVK to '{card.InstallPath}' — path is under a protected system directory.";
                CrashReporter.Log($"[DxvkService.InstallAsync] {msg}");
                progress?.Report((msg, 0));
                return;
            }

            if (!Directory.Exists(card.InstallPath))
            {
                var msg = $"Game directory does not exist: '{card.InstallPath}'";
                CrashReporter.Log($"[DxvkService.InstallAsync] {msg}");
                progress?.Report((msg, 0));
                return;
            }

            // ── 4. Validate DLL architecture matches game bitness ────────
            var sampleDll = Path.Combine(StagingDir, archFolder, dllNames[0]);
            if (!File.Exists(sampleDll))
            {
                var msg = $"Staged DLL not found: '{sampleDll}' — try clearing the DXVK cache in Settings.";
                CrashReporter.Log($"[DxvkService.InstallAsync] {msg}");
                progress?.Report((msg, 0));
                return;
            }

            progress?.Report(("Resolving deployment paths...", 15));

            // ── DX8/DX9 Proxy Mode ──────────────────────────────────────
            // For DX8/DX9 games, use the ReShade proxy chain method:
            // ReShade stays as d3d9.dll (DX proxy), DXVK is deployed as
            // dxgi_dxvk.dll, and ReShade's [PROXY] section chains to it.
            // No Vulkan layer needed.
            bool useProxyMode = IsProxyModeApi(card.GraphicsApi);

            if (useProxyMode)
            {
                progress?.Report(("Deploying DXVK (proxy mode)...", 35));

                // The source DLL is DXVK's d3d9.dll from staging
                var srcDll = Path.Combine(StagingDir, archFolder, "d3d9.dll");
                if (!File.Exists(srcDll))
                {
                    CrashReporter.Log($"[DxvkService.InstallAsync] Proxy mode: staged d3d9.dll not found at '{srcDll}'");
                    progress?.Report(("DXVK d3d9.dll not found in staging", 0));
                    return;
                }

                // Deploy as dxgi_dxvk.dll in the game folder
                var destDll = Path.Combine(card.InstallPath, ProxyDxvkDllName);
                CopyFileWithLockHandling(srcDll, destDll, ProxyDxvkDllName, card.GameName);
                CrashReporter.Log($"[DxvkService.InstallAsync] Proxy mode: deployed {ProxyDxvkDllName} to game root");

                // Add [PROXY] section to reshade.ini
                progress?.Report(("Configuring ReShade proxy chain...", 50));
                AddProxySectionToReShadeIni(card.InstallPath);

                progress?.Report(("Configuring dxvk.conf...", 65));

                // Deploy default dxvk.conf if none exists
                var proxyConfPath = Path.Combine(card.InstallPath, "dxvk.conf");
                bool proxyDeployedConf = false;
                if (!File.Exists(proxyConfPath))
                {
                    try
                    {
                        File.WriteAllText(proxyConfPath, DefaultDxvkConfContent);
                        proxyDeployedConf = true;
                        CrashReporter.Log("[DxvkService.InstallAsync] Deployed default dxvk.conf");
                    }
                    catch (Exception ex)
                    {
                        CrashReporter.Log($"[DxvkService.InstallAsync] Failed to deploy dxvk.conf — {ex.Message}");
                    }
                }

                progress?.Report(("Saving install record...", 80));

                var proxyRecord = new DxvkInstalledRecord
                {
                    GameName = card.GameName,
                    InstallPath = card.InstallPath,
                    DxvkVersion = StagedVersion ?? "unknown",
                    InstalledDlls = new List<string> { ProxyDxvkDllName },
                    PluginFolderDlls = new List<string>(),
                    BackedUpFiles = new List<string>(),
                    DeployedConf = proxyDeployedConf,
                    InOptiScalerPlugins = false,
                    IsProxyMode = true,
                    InstalledAt = DateTime.UtcNow,
                };
                SaveRecord(proxyRecord);

                card.DxvkInstalledVersion = proxyRecord.DxvkVersion;
                card.DxvkStatus = GameStatus.Installed;
                card.DxvkRecord = proxyRecord;
                HasUpdate = false;

                progress?.Report(("DXVK installed (proxy mode)!", 100));
                CrashReporter.Log($"[DxvkService.InstallAsync] Proxy mode install complete for {card.GameName}");
                return;
            }

            // ── Standard Mode (DX10/DX11) ────────────────────────────────

            // ── 5. Resolve OptiScaler coexistence paths ──────────────────
            var (rootDlls, pluginDlls) = ResolveDeploymentPaths(dllNames, card.InstallPath);
            CrashReporter.Log($"[DxvkService.InstallAsync] Root DLLs: [{string.Join(", ", rootDlls)}], Plugin DLLs: [{string.Join(", ", pluginDlls)}]");

            // ── 6. Switch ReShade BEFORE deploying DXVK DLLs ────────────
            // ReShade mode switch must happen first because:
            // - ReShade may be installed as dxgi.dll (DX proxy mode)
            // - DXVK also needs to deploy dxgi.dll for DX10/DX11 games
            // - If we deploy DXVK's dxgi.dll first, then the ReShade uninstall
            //   deletes it thinking it's removing the old ReShade DLL
            progress?.Report(("Switching ReShade mode...", 25));
            await SwitchReShadeForDxvkAsync(card, dxvkEnabled: true);

            progress?.Report(("Deploying DXVK DLLs...", 35));

            var backedUpFiles = new List<string>();

            // ── 7. Deploy DLLs to game root ──────────────────────────────
            foreach (var dll in rootDlls)
            {
                var srcPath = Path.Combine(StagingDir, archFolder, dll);
                var destPath = Path.Combine(card.InstallPath, dll);

                BackupIfNeeded(destPath, backedUpFiles);
                CopyFileWithLockHandling(srcPath, destPath, dll, card.GameName);
                CrashReporter.Log($"[DxvkService.InstallAsync] Deployed {dll} to game root");
            }

            // ── 8. Deploy DLLs to OptiScaler plugins folder ─────────────
            if (pluginDlls.Count > 0)
            {
                var pluginsFolder = Path.Combine(card.InstallPath, "OptiScaler", "plugins");
                Directory.CreateDirectory(pluginsFolder);

                foreach (var dll in pluginDlls)
                {
                    var srcPath = Path.Combine(StagingDir, archFolder, dll);
                    var destPath = Path.Combine(pluginsFolder, dll);

                    // No backup needed for plugins folder — it's RHI-managed
                    CopyFileWithLockHandling(srcPath, destPath, dll, card.GameName);
                    CrashReporter.Log($"[DxvkService.InstallAsync] Deployed {dll} to OptiScaler/plugins/");
                }
            }

            progress?.Report(("Configuring dxvk.conf...", 65));

            // ── 9. Deploy default dxvk.conf if none exists ───────────────
            var confPath = Path.Combine(card.InstallPath, "dxvk.conf");
            bool deployedConf = false;
            if (!File.Exists(confPath))
            {
                try
                {
                    File.WriteAllText(confPath, DefaultDxvkConfContent);
                    deployedConf = true;
                    CrashReporter.Log("[DxvkService.InstallAsync] Deployed default dxvk.conf");
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[DxvkService.InstallAsync] Failed to deploy dxvk.conf — {ex.Message}");
                }
            }
            else
            {
                CrashReporter.Log("[DxvkService.InstallAsync] dxvk.conf already exists — preserved");
            }

            progress?.Report(("Saving install record...", 80));

            // ── 10. Create and persist DxvkInstalledRecord ───────────────
            var record = new DxvkInstalledRecord
            {
                GameName = card.GameName,
                InstallPath = card.InstallPath,
                DxvkVersion = StagedVersion ?? "unknown",
                InstalledDlls = new List<string>(rootDlls),
                PluginFolderDlls = new List<string>(pluginDlls),
                BackedUpFiles = backedUpFiles,
                DeployedConf = deployedConf,
                InOptiScalerPlugins = pluginDlls.Count > 0,
                InstalledAt = DateTime.UtcNow,
            };
            SaveRecord(record);
            CrashReporter.Log($"[DxvkService.InstallAsync] Saved tracking record for {card.GameName}");

            // ── 11. Update card properties ───────────────────────────────
            card.DxvkInstalledVersion = record.DxvkVersion;
            card.DxvkStatus = GameStatus.Installed;
            card.DxvkRecord = record;
            HasUpdate = false;

            progress?.Report(("DXVK installed!", 100));
            CrashReporter.Log($"[DxvkService.InstallAsync] Install complete for {card.GameName}");
        }
        catch (IOException ioEx) when (IsFileLockedException(ioEx))
        {
            var msg = $"File is in use — close {card.GameName} and retry.";
            CrashReporter.Log($"[DxvkService.InstallAsync] {msg} — {ioEx.Message}");
            progress?.Report(($"❌ {msg}", 0));
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DxvkService.InstallAsync] {card.GameName} — {ex.Message}");
            CrashReporter.WriteCrashReport("DxvkService.InstallAsync", ex,
                note: $"Game: {card.GameName}, Path: {card.InstallPath}");
            progress?.Report(($"❌ Install failed: {ex.Message}", 0));
        }
    }

    // ── Uninstall ─────────────────────────────────────────────────────

    /// <inheritdoc />
    public void Uninstall(GameCardViewModel card)
    {
        try
        {
            var record = FindRecord(card.GameName, card.InstallPath);
            if (record == null)
            {
                CrashReporter.Log($"[DxvkService.Uninstall] No tracking record found for '{card.GameName}' — nothing to uninstall");
                card.DxvkStatus = GameStatus.NotInstalled;
                card.DxvkInstalledVersion = null;
                card.DxvkRecord = null;
                return;
            }

            var gameDir = card.InstallPath;

            // ── 1. Delete DXVK DLLs from game root ──────────────────────
            foreach (var dll in record.InstalledDlls)
            {
                var dllPath = Path.Combine(gameDir, dll);
                try
                {
                    if (File.Exists(dllPath))
                    {
                        File.Delete(dllPath);
                        CrashReporter.Log($"[DxvkService.Uninstall] Deleted {dll} from game root");
                    }
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[DxvkService.Uninstall] Failed to delete '{dll}' — {ex.Message}");
                }
            }

            // ── 2. Delete DXVK DLLs from OptiScaler plugins folder ──────
            if (record.PluginFolderDlls.Count > 0)
            {
                var pluginsFolder = Path.Combine(gameDir, "OptiScaler", "plugins");
                foreach (var dll in record.PluginFolderDlls)
                {
                    var dllPath = Path.Combine(pluginsFolder, dll);
                    try
                    {
                        if (File.Exists(dllPath))
                        {
                            File.Delete(dllPath);
                            CrashReporter.Log($"[DxvkService.Uninstall] Deleted {dll} from OptiScaler/plugins/");
                        }
                    }
                    catch (Exception ex)
                    {
                        CrashReporter.Log($"[DxvkService.Uninstall] Failed to delete plugin '{dll}' — {ex.Message}");
                    }
                }
            }

            // ── 3. Restore .original backups ─────────────────────────────
            foreach (var backedUp in record.BackedUpFiles)
            {
                var originalPath = Path.Combine(gameDir, backedUp);
                var backupPath = originalPath + ".original";
                try
                {
                    if (File.Exists(backupPath))
                    {
                        if (!File.Exists(originalPath))
                        {
                            File.Move(backupPath, originalPath);
                            CrashReporter.Log($"[DxvkService.Uninstall] Restored {backedUp} from backup");
                        }
                        else
                        {
                            CrashReporter.Log($"[DxvkService.Uninstall] Cannot restore '{backedUp}' — file still exists");
                        }
                    }
                    else
                    {
                        CrashReporter.Log($"[DxvkService.Uninstall] Backup not found for '{backedUp}' — skipping restore");
                    }
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[DxvkService.Uninstall] Failed to restore '{backedUp}' — {ex.Message}");
                }
            }

            // ── 4. Delete dxvk.conf if we deployed it ────────────────────
            if (record.DeployedConf)
            {
                var confPath = Path.Combine(gameDir, "dxvk.conf");
                try
                {
                    if (File.Exists(confPath))
                    {
                        File.Delete(confPath);
                        CrashReporter.Log("[DxvkService.Uninstall] Deleted dxvk.conf");
                    }
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[DxvkService.Uninstall] Failed to delete dxvk.conf — {ex.Message}");
                }
            }

            // ── 5. Coordinate ReShade mode switching ─────────────────────
            if (record.IsProxyMode)
            {
                // Proxy mode: remove [PROXY] section from reshade.ini
                RemoveProxySectionFromReShadeIni(gameDir);
                CrashReporter.Log($"[DxvkService.Uninstall] Removed [PROXY] section from reshade.ini (proxy mode)");
            }
            else
            {
                // Standard mode: switch Vulkan layer → DX proxy
                // Fire-and-forget since Uninstall is synchronous; the actual switching
                // is lightweight (file operations only, no network).
                try
                {
                    SwitchReShadeForDxvkAsync(card, dxvkEnabled: false).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[DxvkService.Uninstall] ReShade mode switch failed — {ex.Message}");
                }
            }

            // ── 6. Remove tracking record ────────────────────────────────
            RemoveRecord(card.GameName, card.InstallPath);
            CrashReporter.Log($"[DxvkService.Uninstall] Removed tracking record for {card.GameName}");

            // ── 7. Update card properties ────────────────────────────────
            card.DxvkStatus = GameStatus.NotInstalled;
            card.DxvkInstalledVersion = null;
            card.DxvkRecord = null;

            CrashReporter.Log($"[DxvkService.Uninstall] Uninstall complete for {card.GameName}");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DxvkService.Uninstall] {card.GameName} — {ex.Message}");
            CrashReporter.WriteCrashReport("DxvkService.Uninstall", ex,
                note: $"Game: {card.GameName}, Path: {card.InstallPath}");
        }
    }

    // ── Update ────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task UpdateAsync(
        GameCardViewModel card,
        IProgress<(string message, double percent)>? progress = null)
    {
        try
        {
            progress?.Report(("Preparing DXVK update...", 5));

            // ── 1. Re-stage latest DXVK if needed ────────────────────────
            if (HasUpdate)
            {
                CrashReporter.Log("[DxvkService.UpdateAsync] Update available — clearing staging for fresh download");
                ClearStaging();
            }

            if (!IsStagingReady)
            {
                await EnsureStagingAsync(progress);
                if (!IsStagingReady)
                {
                    CrashReporter.Log("[DxvkService.UpdateAsync] Staging not ready after download — aborting");
                    progress?.Report(("DXVK staging not available", 0));
                    return;
                }
            }

            var existingRecord = FindRecord(card.GameName, card.InstallPath);
            if (existingRecord == null)
            {
                CrashReporter.Log($"[DxvkService.UpdateAsync] No existing record for '{card.GameName}' — performing fresh install");
                await InstallAsync(card, progress);
                return;
            }

            progress?.Report(("Updating DXVK DLLs...", 30));

            // ── 2. Determine required DLLs ───────────────────────────────
            var (archFolder, dllNames) = DetermineRequiredDlls(card.GraphicsApi, card.Is32Bit);

            // ── 3. Re-deploy DLLs to game root (overwrite existing DXVK DLLs) ──
            foreach (var dll in existingRecord.InstalledDlls)
            {
                var srcPath = Path.Combine(StagingDir, archFolder, dll);
                var destPath = Path.Combine(card.InstallPath, dll);
                if (File.Exists(srcPath))
                {
                    CopyFileWithLockHandling(srcPath, destPath, dll, card.GameName);
                    CrashReporter.Log($"[DxvkService.UpdateAsync] Updated {dll} in game root");
                }
            }

            // ── 4. Re-deploy DLLs to OptiScaler plugins folder ──────────
            if (existingRecord.PluginFolderDlls.Count > 0)
            {
                var pluginsFolder = Path.Combine(card.InstallPath, "OptiScaler", "plugins");
                Directory.CreateDirectory(pluginsFolder);

                foreach (var dll in existingRecord.PluginFolderDlls)
                {
                    var srcPath = Path.Combine(StagingDir, archFolder, dll);
                    var destPath = Path.Combine(pluginsFolder, dll);
                    if (File.Exists(srcPath))
                    {
                        CopyFileWithLockHandling(srcPath, destPath, dll, card.GameName);
                        CrashReporter.Log($"[DxvkService.UpdateAsync] Updated {dll} in OptiScaler/plugins/");
                    }
                }
            }

            progress?.Report(("Saving updated record...", 80));

            // ── 5. Update tracking record version ────────────────────────
            existingRecord.DxvkVersion = StagedVersion ?? "unknown";
            existingRecord.InstalledAt = DateTime.UtcNow;
            SaveRecord(existingRecord);

            // ── 6. Update card properties ────────────────────────────────
            card.DxvkInstalledVersion = existingRecord.DxvkVersion;
            card.DxvkStatus = GameStatus.Installed;
            card.DxvkRecord = existingRecord;
            HasUpdate = false;

            progress?.Report(("DXVK updated!", 100));
            CrashReporter.Log($"[DxvkService.UpdateAsync] Update complete for {card.GameName}");
        }
        catch (IOException ioEx) when (IsFileLockedException(ioEx))
        {
            var msg = $"File is in use — close {card.GameName} and retry.";
            CrashReporter.Log($"[DxvkService.UpdateAsync] {msg} — {ioEx.Message}");
            progress?.Report(($"❌ {msg}", 0));
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DxvkService.UpdateAsync] {card.GameName} — {ex.Message}");
            CrashReporter.WriteCrashReport("DxvkService.UpdateAsync", ex,
                note: $"Game: {card.GameName}, Path: {card.InstallPath}");
            progress?.Report(($"❌ Update failed: {ex.Message}", 0));
        }
    }

    // ── CopyConfToGame ────────────────────────────────────────────────

    /// <inheritdoc />
    public void CopyConfToGame(GameCardViewModel card)
    {
        try
        {
            if (string.IsNullOrEmpty(card.InstallPath) || !Directory.Exists(card.InstallPath))
            {
                CrashReporter.Log($"[DxvkService.CopyConfToGame] Invalid install path for '{card.GameName}'");
                return;
            }

            var destPath = Path.Combine(card.InstallPath, "dxvk.conf");

            // Try to copy from the INI presets directory first
            if (File.Exists(ConfTemplatePath))
            {
                File.Copy(ConfTemplatePath, destPath, overwrite: true);
                CrashReporter.Log($"[DxvkService.CopyConfToGame] Copied dxvk.conf template to {card.GameName}");
            }
            else
            {
                // Fall back to writing the default template content
                File.WriteAllText(destPath, DefaultDxvkConfContent);
                CrashReporter.Log($"[DxvkService.CopyConfToGame] Wrote default dxvk.conf to {card.GameName}");
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DxvkService.CopyConfToGame] {card.GameName} — {ex.Message}");
        }
    }

    // ── DetectInstallation ────────────────────────────────────────────

    /// <inheritdoc />
    public string? DetectInstallation(string installPath, GraphicsApiType api)
    {
        try
        {
            if (string.IsNullOrEmpty(installPath) || !Directory.Exists(installPath))
                return null;

            // Determine which DLL filenames to scan based on the game's API
            List<string> candidateDlls;
            try
            {
                var (_, dlls) = DetermineRequiredDlls(api, false); // bitness doesn't matter for filename detection
                candidateDlls = dlls;
            }
            catch (InvalidOperationException)
            {
                // Unsupported API — scan all known DXVK DLL names
                candidateDlls = new List<string> { "d3d8.dll", "d3d9.dll", "d3d10core.dll", "d3d11.dll", "dxgi.dll" };
            }

            foreach (var dll in candidateDlls)
            {
                var dllPath = Path.Combine(installPath, dll);
                if (File.Exists(dllPath) && IsDxvkFile(dllPath))
                {
                    // Found a DXVK DLL — check if we have a managed record
                    var record = FindRecord(Path.GetFileName(installPath), installPath);
                    if (record != null)
                    {
                        return record.DxvkVersion;
                    }

                    // Unmanaged DXVK detected
                    return "Detected (unmanaged)";
                }
            }

            // Also check OptiScaler plugins folder
            var pluginsFolder = Path.Combine(installPath, "OptiScaler", "plugins");
            if (Directory.Exists(pluginsFolder))
            {
                foreach (var dll in candidateDlls)
                {
                    var dllPath = Path.Combine(pluginsFolder, dll);
                    if (File.Exists(dllPath) && IsDxvkFile(dllPath))
                    {
                        var record = FindRecord(Path.GetFileName(installPath), installPath);
                        return record?.DxvkVersion ?? "Detected (unmanaged)";
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DxvkService.DetectInstallation] Error scanning '{installPath}' — {ex.Message}");
            return null;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────

    /// <summary>
    /// Backs up an existing file at <paramref name="destPath"/> with a .original
    /// extension. Always backs up game-original files. For RHI-managed DLLs
    /// (previous DXVK install), overwrites without backup since the .original
    /// from the first install is the one we want to preserve.
    /// </summary>
    private void BackupIfNeeded(string destPath, List<string> backedUpFiles)
    {
        if (!File.Exists(destPath)) return;

        var backupPath = destPath + ".original";

        // If a .original backup already exists, the game's original is already preserved.
        // The current file is either a previous DXVK install or another RHI-managed DLL.
        // Don't create a second backup — just overwrite.
        if (File.Exists(backupPath))
        {
            CrashReporter.Log($"[DxvkService.BackupIfNeeded] Backup already exists for {Path.GetFileName(destPath)} — will overwrite current file");
            return;
        }

        // Skip backup for known RHI-managed DLLs (previous DXVK, OptiScaler, ReShade)
        // — these are not game originals, no need to preserve them
        if (IsDxvkFile(destPath))
        {
            CrashReporter.Log($"[DxvkService.BackupIfNeeded] Skipping backup for DXVK file: {Path.GetFileName(destPath)}");
            return;
        }

        if (_optiScalerService.IsOptiScalerFile(destPath))
        {
            CrashReporter.Log($"[DxvkService.BackupIfNeeded] Skipping backup for OptiScaler file: {Path.GetFileName(destPath)}");
            return;
        }

        if (AuxInstallService.IsReShadeFile(destPath))
        {
            CrashReporter.Log($"[DxvkService.BackupIfNeeded] Skipping backup for ReShade file: {Path.GetFileName(destPath)}");
            return;
        }

        // This is a game-original file — back it up
        try
        {
            File.Move(destPath, backupPath);
            backedUpFiles.Add(Path.GetFileName(destPath));
            CrashReporter.Log($"[DxvkService.BackupIfNeeded] Backed up original: {Path.GetFileName(destPath)} → {Path.GetFileName(backupPath)}");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DxvkService.BackupIfNeeded] Failed to back up '{Path.GetFileName(destPath)}' — {ex.Message}");
        }
    }

    /// <summary>
    /// Copies a file with specific handling for file-locked errors.
    /// </summary>
    private static void CopyFileWithLockHandling(string srcPath, string destPath, string dllName, string gameName)
    {
        try
        {
            File.Copy(srcPath, destPath, overwrite: true);
        }
        catch (IOException ioEx) when (IsFileLockedException(ioEx))
        {
            throw new IOException(
                $"Cannot copy '{dllName}' — the file is in use. Close {gameName} and retry.", ioEx);
        }
    }

    /// <summary>
    /// Determines if an IOException is caused by a file being locked/in-use.
    /// </summary>
    private static bool IsFileLockedException(IOException ex)
    {
        // HResult 0x80070020 = ERROR_SHARING_VIOLATION
        // HResult 0x80070021 = ERROR_LOCK_VIOLATION
        const int sharingViolation = unchecked((int)0x80070020);
        const int lockViolation = unchecked((int)0x80070021);
        return ex.HResult == sharingViolation || ex.HResult == lockViolation;
    }

    /// <summary>
    /// Coordinates ReShade mode switching when DXVK is enabled or disabled.
    /// When DXVK is enabled, ReShade must switch from DX proxy DLL to the global
    /// Vulkan implicit layer (no per-game DLL — the layer is system-wide).
    /// When DXVK is disabled, ReShade switches back from Vulkan layer to DX proxy.
    /// </summary>
    private async Task SwitchReShadeForDxvkAsync(GameCardViewModel card, bool dxvkEnabled)
    {
        try
        {
            // Skip if game is already a native Vulkan game or dual-API on Vulkan path.
            // Do NOT check RequiresVulkanInstall here — it includes DxvkEnabled,
            // which is the reason we're being called in the first place.
            if (card.IsVulkanOnly || (card.IsDualApiGame && card.VulkanRenderingPath == "Vulkan"))
            {
                CrashReporter.Log($"[DxvkService.SwitchReShadeForDxvk] {card.GameName}: native Vulkan — skipping mode switch");
                return;
            }

            // Skip if ReShade is not installed
            if (card.RsStatus == GameStatus.NotInstalled)
            {
                CrashReporter.Log($"[DxvkService.SwitchReShadeForDxvk] {card.GameName}: ReShade not installed — skipping mode switch");
                return;
            }

            if (dxvkEnabled)
            {
                // ── Switch DX proxy → Vulkan layer ──────────────────────────

                // 1. Remove the DX proxy ReShade DLL (e.g. dxgi.dll)
                if (card.RsRecord != null)
                {
                    _auxInstaller.Uninstall(card.RsRecord);
                    card.RsRecord = null;
                    card.RsInstalledFile = null;
                    CrashReporter.Log($"[DxvkService.SwitchReShadeForDxvk] {card.GameName}: uninstalled DX proxy ReShade");
                }

                // 2. Deploy reshade.vulkan.ini (as reshade.ini) to the game folder
                AuxInstallService.MergeRsVulkanIni(card.InstallPath, card.GameName);
                CrashReporter.Log($"[DxvkService.SwitchReShadeForDxvk] {card.GameName}: deployed Vulkan reshade.ini");

                // 3. Deploy ReShadePreset.ini if present
                AuxInstallService.CopyRsPresetIniIfPresent(card.InstallPath);

                // 4. Create the Vulkan footprint marker
                VulkanFootprintService.Create(card.InstallPath);
                CrashReporter.Log($"[DxvkService.SwitchReShadeForDxvk] {card.GameName}: created Vulkan footprint");

                // 5. Verify the global Vulkan layer is installed — if not, log a warning.
                //    The layer requires admin elevation to install, which we can't do from
                //    this service. The user may need to install ReShade via the normal UI
                //    flow first (which handles admin elevation and VulkanLayerService.InstallLayer).
                if (!VulkanLayerService.IsLayerInstalled())
                {
                    CrashReporter.Log(
                        $"[DxvkService.SwitchReShadeForDxvk] WARNING: {card.GameName}: " +
                        "Global Vulkan ReShade layer is not installed. DXVK games present as Vulkan " +
                        "and require the layer. Install ReShade on a Vulkan game via the UI to set up the layer.");
                }

                CrashReporter.Log($"[DxvkService.SwitchReShadeForDxvk] {card.GameName}: switched ReShade to Vulkan layer mode");
            }
            else
            {
                // ── Switch Vulkan layer → DX proxy ──────────────────────────

                // 1. Delete the Vulkan footprint marker
                VulkanFootprintService.Delete(card.InstallPath);
                CrashReporter.Log($"[DxvkService.SwitchReShadeForDxvk] {card.GameName}: deleted Vulkan footprint");

                // 2. Delete reshade.vulkan.ini (deployed as reshade.ini) from the game folder
                var vulkanIniPath = Path.Combine(card.InstallPath, "reshade.ini");
                try
                {
                    if (File.Exists(vulkanIniPath))
                    {
                        File.Delete(vulkanIniPath);
                        CrashReporter.Log($"[DxvkService.SwitchReShadeForDxvk] {card.GameName}: deleted Vulkan reshade.ini");
                    }
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[DxvkService.SwitchReShadeForDxvk] {card.GameName}: failed to delete Vulkan reshade.ini — {ex.Message}");
                }

                // 3. Reinstall ReShade as DX proxy DLL with the correct API-specific filename.
                // DX9 games need d3d9.dll, OpenGL needs opengl32.dll, DX11/DX12 use dxgi.dll.
                var rsFilename = card.GraphicsApi switch
                {
                    GraphicsApiType.DirectX9  => "d3d9.dll",
                    GraphicsApiType.DirectX8  => "d3d9.dll",  // DX8 games use d3d9 proxy
                    GraphicsApiType.OpenGL    => "opengl32.dll",
                    _ => (string?)null,  // null = default dxgi.dll
                };
                var record = await _auxInstaller.InstallReShadeAsync(
                    card.GameName,
                    card.InstallPath,
                    shaderModeOverride: card.ShaderModeOverride,
                    use32Bit: card.Is32Bit,
                    filenameOverride: rsFilename);
                card.RsRecord = record;
                card.RsInstalledFile = record.InstalledAs;
                card.RsStatus = GameStatus.Installed;
                CrashReporter.Log($"[DxvkService.SwitchReShadeForDxvk] {card.GameName}: reinstalled ReShade as DX proxy");
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DxvkService.SwitchReShadeForDxvk] {card.GameName}: ReShade mode switch failed — {ex.Message}");
        }
    }

    // ── ReShade INI proxy section helpers (DX8/DX9 proxy mode) ────────

    /// <summary>
    /// Adds or updates the [PROXY] section in reshade.ini to chain to the DXVK DLL.
    /// Creates reshade.ini if it doesn't exist.
    /// </summary>
    private static void AddProxySectionToReShadeIni(string gameDir)
    {
        var iniPath = Path.Combine(gameDir, "reshade.ini");
        try
        {
            var lines = File.Exists(iniPath)
                ? new List<string>(File.ReadAllLines(iniPath))
                : new List<string>();

            // Remove existing [PROXY] section if present
            RemoveSection(lines, "PROXY");

            // Append the new [PROXY] section
            if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
                lines.Add(""); // blank line separator
            lines.Add("[PROXY]");
            lines.Add("EnableProxyLibrary=1");
            lines.Add($"ProxyLibrary={ProxyDxvkDllName}");

            File.WriteAllLines(iniPath, lines);
            CrashReporter.Log($"[DxvkService] Added [PROXY] section to reshade.ini → {ProxyDxvkDllName}");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DxvkService.AddProxySectionToReShadeIni] Failed — {ex.Message}");
        }
    }

    /// <summary>
    /// Removes the [PROXY] section from reshade.ini (or sets EnableProxyLibrary=0).
    /// </summary>
    private static void RemoveProxySectionFromReShadeIni(string gameDir)
    {
        var iniPath = Path.Combine(gameDir, "reshade.ini");
        if (!File.Exists(iniPath)) return;
        try
        {
            var lines = new List<string>(File.ReadAllLines(iniPath));
            if (RemoveSection(lines, "PROXY"))
            {
                File.WriteAllLines(iniPath, lines);
                CrashReporter.Log("[DxvkService] Removed [PROXY] section from reshade.ini");
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DxvkService.RemoveProxySectionFromReShadeIni] Failed — {ex.Message}");
        }
    }

    /// <summary>
    /// Removes all lines belonging to the given INI section (header + keys until next section or EOF).
    /// Returns true if the section was found and removed.
    /// </summary>
    private static bool RemoveSection(List<string> lines, string sectionName)
    {
        var header = $"[{sectionName}]";
        int start = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Trim().Equals(header, StringComparison.OrdinalIgnoreCase))
            {
                start = i;
                break;
            }
        }
        if (start < 0) return false;

        // Find the end of the section (next [Section] header or EOF)
        int end = start + 1;
        while (end < lines.Count && !lines[end].TrimStart().StartsWith('['))
            end++;

        // Remove trailing blank lines before the section too
        while (start > 0 && string.IsNullOrWhiteSpace(lines[start - 1]))
            start--;

        lines.RemoveRange(start, end - start);
        return true;
    }
}
