using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Services;

public partial class DxvkService
{
    // ── Default dxvk.conf template content ────────────────────────────
    public const string DefaultDxvkConfContent =
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
    public async Task<bool> InstallAsync(
        GameCardViewModel card,
        IProgress<(string message, double percent)>? progress = null,
        string? screenshotSavePath = null,
        string? overlayHotkey = null,
        string? screenshotHotkey = null)
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
                    progress?.Report((AntivirusGuard.IsBlocked(AntivirusComponentName)
                        ? "DXVK couldn't be downloaded: your antivirus removed its files. Add an exclusion for %LocalAppData%\\RHI and try again."
                        : "DXVK staging not available", 0));
                    return false;
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
                return false;
            }

            if (!Directory.Exists(card.InstallPath))
            {
                var msg = $"Game directory does not exist: '{card.InstallPath}'";
                CrashReporter.Log($"[DxvkService.InstallAsync] {msg}");
                progress?.Report((msg, 0));
                return false;
            }

            // ── 4. Validate DLL architecture matches game bitness ────────
            var sampleDll = Path.Combine(StagingDir, archFolder, dllNames[0]);
            if (!File.Exists(sampleDll))
            {
                var msg = $"DXVK's {archFolder}\\{dllNames[0]} is missing from RHI's download folder. "
                        + "If your antivirus removed it, add an exclusion for %LocalAppData%\\RHI and install DXVK again.";
                CrashReporter.Log($"[DxvkService.InstallAsync] {msg}");
                progress?.Report((msg, 0));
                return false;
            }

            progress?.Report(("Resolving deployment paths...", 15));

            // ── DX8/DX9 Direct Mode ─────────────────────────────────────
            // All variants deploy DXVK as d3d9.dll directly for DX8/DX9 games.
            // ReShade switches to the global Vulkan implicit layer (same as DX10/DX11).
            // Lilium HDR additionally sets NVIDIA profile present method for HDR routing.
            bool isDx9DirectMode = IsProxyModeApi(card.GraphicsApi);

            if (isDx9DirectMode)
            {
                progress?.Report(("Deploying DXVK as d3d9.dll...", 35));

                var srcDll = Path.Combine(StagingDir, archFolder, "d3d9.dll");
                if (!File.Exists(srcDll))
                {
                    CrashReporter.Log($"[DxvkService.InstallAsync] Direct DX9: staged d3d9.dll not found at '{srcDll}'");
                    progress?.Report(("DXVK d3d9.dll not found in staging", 0));
                    return false;
                }

                // Step 1: Remove existing legacy DXVK proxy if present (migration from old installs)
                var existingProxy = Path.Combine(card.InstallPath, ProxyDxvkDllName);
                if (File.Exists(existingProxy))
                {
                    File.Delete(existingProxy);
                    CrashReporter.Log("[DxvkService.InstallAsync] Direct DX9: removed legacy dxgi_dxvk.dll proxy");
                }
                RemoveProxySectionFromReShadeIni(card.InstallPath);

                // Step 2: Move whatever is at d3d9.dll out of the way (ReShade → .reshade_backup,
                // anything else → .original; DXVK's own DLL from an earlier install is just replaced).
                // "Own" = RHI's DXVK record lists d3d9.dll: a hand-installed or another mod's DXVK is a foreign file.
                var localReshade = Path.Combine(card.InstallPath, "d3d9.dll");
                bool recordOwnsD3d9 = RecordOwnsD3d9(FindRecord(card.GameName, card.InstallPath));
                bool rsBackupExisted = File.Exists(localReshade + ".reshade_backup");
                var refused = PrepareDx9Target(localReshade, p => recordOwnsD3d9 && IsDxvkFileStatic(p), AuxInstallService.IsReShadeFile, out bool foreignBackedUp);
                if (refused != null)
                {
                    CrashReporter.Log($"[DxvkService.InstallAsync] Direct DX9: {refused}");
                    progress?.Report((refused, 0));
                    return false;
                }
                bool reshadeMovedAside = !foreignBackedUp && !rsBackupExisted && File.Exists(localReshade + ".reshade_backup");
                bool dx9RecordSaved = false;
                DxvkInstalledRecord dx9Record;
                try
                {
                    // Step 3: Deploy DXVK as d3d9.dll
                    progress?.Report(("Deploying DXVK as d3d9.dll...", 50));
                    CopyFileWithLockHandling(srcDll, localReshade, "d3d9.dll", card.GameName);
                    CrashReporter.Log($"[DxvkService.InstallAsync] Direct DX9: deployed DXVK as d3d9.dll (variant={_selectedVariant})");

                    // Step 4: Deploy dxvk.conf
                    progress?.Report(("Configuring dxvk.conf...", 60));
                    var dx9ConfPath = Path.Combine(card.InstallPath, "dxvk.conf");
                    bool dx9DeployedConf = false;
                    try
                    {
                        var confContent = _selectedVariant == DxvkVariant.LiliumHdr
                            ? GetLiliumD3d9ConfContent(_liliumPresetIndex)
                            : DefaultDxvkConfContent;
                        File.WriteAllText(dx9ConfPath, confContent);
                        dx9DeployedConf = true;
                        CrashReporter.Log($"[DxvkService.InstallAsync] Direct DX9: deployed dxvk.conf (variant={_selectedVariant})");
                    }
                    catch (Exception ex)
                    {
                        CrashReporter.Log($"[DxvkService.InstallAsync] Direct DX9: dxvk.conf deploy failed — {ex.Message}");
                    }

                    // Step 5: Deploy Vulkan reshade.ini + footprint (no [PROXY] section)
                    progress?.Report(("Configuring Vulkan ReShade...", 70));
                    AuxInstallService.MergeRsVulkanIni(card.InstallPath, card.GameName, screenshotSavePath, overlayHotkey, screenshotHotkey);
                    VulkanFootprintService.Create(card.InstallPath);
                    CrashReporter.Log("[DxvkService.InstallAsync] Direct DX9: deployed Vulkan reshade.ini + footprint");

                    // Step 5b: Lilium HDR only — set NVIDIA profile settings for HDR present mode
                    if (_selectedVariant == DxvkVariant.LiliumHdr)
                    {
                        progress?.Report(("Setting NVIDIA profile...", 78));
                        try
                        {
                            var presetSvc = App.Services.GetRequiredService<DlssPresetService>();
                            presetSvc.SetLiliumPresentMethod(card.GameName, card.InstallPath);
                            CrashReporter.Log("[DxvkService.InstallAsync] Lilium HDR: set NVIDIA present method settings");
                        }
                        catch (Exception ex)
                        {
                            CrashReporter.Log($"[DxvkService.InstallAsync] Lilium HDR: NVIDIA profile settings failed — {ex.Message}");
                        }
                    }

                    // Step 6: Save tracking record (a reinstall keeps the earlier install's backups)
                    progress?.Report(("Saving install record...", 85));
                    var dx9BackedUp = MergeBackedUpFiles(
                        foreignBackedUp ? new List<string> { "d3d9.dll" } : new List<string>(),
                        FindRecord(card.GameName, card.InstallPath)?.BackedUpFiles);
                    dx9Record = new DxvkInstalledRecord
                    {
                        GameName = card.GameName,
                        InstallPath = card.InstallPath,
                        Store = card.Source ?? "",
                        DxvkVersion = FormatVersionForDisplay(StagedVersion),
                        InstalledDlls = new List<string> { "d3d9.dll" },
                        PluginFolderDlls = new List<string>(),
                        BackedUpFiles = dx9BackedUp,
                        DeployedConf = dx9DeployedConf,
                        InOptiScalerPlugins = false,
                        IsProxyMode = false,
                        IsLiliumHdrMode = _selectedVariant == DxvkVariant.LiliumHdr,
                        InstalledAt = DateTime.UtcNow,
                    };
                    SaveRecord(dx9Record);
                    dx9RecordSaved = true;
                }
                finally
                {
                    // Failed before the record was saved: put back what step 2 moved aside, so the game's d3d9.dll
                    // (or ReShade) isn't left in an untracked .original / .reshade_backup
                    if (!dx9RecordSaved) UndoDx9Target(localReshade, foreignBackedUp, reshadeMovedAside);
                }

                card.DxvkInstalledVersion = dx9Record.DxvkVersion;
                card.DxvkStatus = GameStatus.Installed;
                card.DxvkRecord = dx9Record;
                card.VulkanRenderingPath = "Vulkan";

                // Update DetectedApis to include both the original native API and Vulkan
                // so the badge shows "DX9 / VLK" and searches for either API find this game.
                // Keep original API in DetectedApis if present; seed DX9 if empty.
                if (!card.DetectedApis.Any(a => a is GraphicsApiType.DirectX8 or GraphicsApiType.DirectX9 or GraphicsApiType.DirectX10))
                    card.DetectedApis.Add(GraphicsApiType.DirectX9);
                card.DetectedApis.Add(GraphicsApiType.Vulkan);
                card.GraphicsApi = GraphicsApiType.Vulkan;
                HasUpdate = false;

                // Update RS status to reflect Vulkan layer
                var vulkanVersion = AuxInstallService.ReadInstalledVersion(
                    VulkanLayerService.LayerDirectory, VulkanLayerService.LayerDllName);
                card.RsInstalledVersion = vulkanVersion;
                card.RsStatus = GameStatus.Installed;
                card.RefreshBackupState(); // populate VulkanRsIniExists so panel shows correctly
                card.NotifyAll();

                progress?.Report(("DXVK installed!", 100));
                CrashReporter.Log($"[DxvkService.InstallAsync] Direct DX9 install complete for {card.GameName} (variant={_selectedVariant})");
                return true;
            }

            // ── Standard Mode (DX10/DX11) ────────────────────────────────

            // ── 5. Resolve OptiScaler coexistence paths ──────────────────
            var (rootDlls, pluginDlls) = ResolveDeploymentPaths(dllNames, card.InstallPath);
            CrashReporter.Log($"[DxvkService.InstallAsync] Root DLLs: [{string.Join(", ", rootDlls)}], Plugin DLLs: [{string.Join(", ", pluginDlls)}]");

            // DLLs that clash with OptiScaler's name go where the installed OptiScaler build loads them:
            // OptiScaler\plugins on OptiScaler 10 builds, <game>\plugins on 0.9 (never create OptiScaler\ there,
            // it would mark the game as an OptiScaler 10 install). The root plugins folder can be the game's,
            // so a file there that isn't DXVK is never replaced: refused before anything changes.
            var pluginsFolder = OptiScalerService.GetPluginsDir(card.InstallPath);
            foreach (var dll in pluginDlls)
            {
                var dest = FileHelper.SafeCombine(pluginsFolder, dll);
                if (dest != null && CanWritePluginCopy(dest)) continue;
                var msg = $"DXVK's {dll} has to go in OptiScaler's plugins folder, but '{Path.GetRelativePath(card.InstallPath, dest ?? pluginsFolder)}' already holds another file. Move it out of the way and install DXVK again.";
                CrashReporter.Log($"[DxvkService.InstallAsync] {card.GameName}: {msg}");
                progress?.Report((msg, 0));
                return false;
            }

            // ── 6. Switch ReShade BEFORE deploying DXVK DLLs ────────────
            // ReShade mode switch must happen first because:
            // - ReShade may be installed as dxgi.dll (DX proxy mode)
            // - DXVK also needs to deploy dxgi.dll for DX10/DX11 games
            // - If we deploy DXVK's dxgi.dll first, then the ReShade uninstall
            //   deletes it thinking it's removing the old ReShade DLL
            progress?.Report(("Switching ReShade mode...", 25));
            await SwitchReShadeForDxvkAsync(card, dxvkEnabled: true, screenshotSavePath, overlayHotkey, screenshotHotkey);

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

            // ── 8. Deploy DLLs to OptiScaler's plugins folder ───────────
            if (pluginDlls.Count > 0)
            {
                Directory.CreateDirectory(pluginsFolder);

                foreach (var dll in pluginDlls)
                {
                    var srcPath = Path.Combine(StagingDir, archFolder, dll);
                    var destPath = Path.Combine(pluginsFolder, dll);

                    // No backup: checked above that nothing but an earlier DXVK copy is there
                    CopyFileWithLockHandling(srcPath, destPath, dll, card.GameName);
                    CrashReporter.Log($"[DxvkService.InstallAsync] Deployed {dll} to {Path.GetRelativePath(card.InstallPath, pluginsFolder)}");
                }
            }

            progress?.Report(("Configuring dxvk.conf...", 65));

            // ── 9. Deploy default dxvk.conf if none exists ───────────────
            var confPath = Path.Combine(card.InstallPath, "dxvk.conf");
            bool deployedConf = false;

            // Always (re)write dxvk.conf to match the current variant
            try
            {
                var confContent = DefaultDxvkConfContent;

                CrashReporter.Log($"[DxvkService.InstallAsync] Writing dxvk.conf — _selectedVariant={_selectedVariant}, api={card.GraphicsApi}");

                // Lilium HDR uses its own complete conf (no base lines)
                if (_selectedVariant == DxvkVariant.LiliumHdr)
                {
                    // Determine original API: check if this is a proxy-mode-eligible game (DX8/DX9)
                    // card.GraphicsApi may be Vulkan from Lilium HDR persistence — use IsProxyModeApi on the original API
                    var confApi = (card.GraphicsApi == GraphicsApiType.Vulkan || IsProxyModeApi(card.GraphicsApi))
                        ? GraphicsApiType.DirectX9 : card.GraphicsApi;
                    if (confApi is GraphicsApiType.DirectX8 or GraphicsApiType.DirectX9)
                        confContent = GetLiliumD3d9ConfContent(_liliumPresetIndex);
                    else
                        confContent = GetLiliumD3d11ConfContent(_liliumPresetIndex);
                    CrashReporter.Log($"[DxvkService.InstallAsync] Using Lilium HDR conf (confApi={confApi})");
                }

                File.WriteAllText(confPath, confContent);
                deployedConf = true;
                CrashReporter.Log($"[DxvkService.InstallAsync] Deployed dxvk.conf ({confContent.Length} chars, variant={_selectedVariant}, api={card.GraphicsApi})");
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[DxvkService.InstallAsync] Failed to deploy dxvk.conf — {ex.Message}");
            }

            progress?.Report(("Saving install record...", 80));

            // ── 10. Create and persist DxvkInstalledRecord ───────────────
            // A reinstall finds its own DLLs (no new backup), so keep the backups the first install
            // made, or uninstall would leave the game's own DLL as .original.
            backedUpFiles = MergeBackedUpFiles(backedUpFiles, FindRecord(card.GameName, card.InstallPath)?.BackedUpFiles);
            var record = new DxvkInstalledRecord
            {
                GameName = card.GameName,
                InstallPath = card.InstallPath,
                Store = card.Source ?? "",
                DxvkVersion = FormatVersionForDisplay(StagedVersion),
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

            // DX10/DX11 DXVK installs Vulkan via the global layer (same as DX9).
            // Set VulkanRenderingPath so ReShade uses the Vulkan layer and the
            // RS record is re-checked for the Vulkan layer version.
            // Do NOT flip GraphicsApi to Vulkan for DX11 — unlike DX9 where the game
            // genuinely runs as Vulkan, DX11+DXVK is a translation layer and the card
            // must keep GraphicsApi=DirectX11 so SwitchReShadeForDxvk, uninstall, and
            // all existing guards continue to work correctly.
            if (card.GraphicsApi is not GraphicsApiType.DirectX8
                                 and not GraphicsApiType.DirectX9)
            {
                card.VulkanRenderingPath = "Vulkan";

                // Clear any stale DX-proxy RS record so the Vulkan RS re-check fires
                if (card.RsRecord != null
                    && record.InstalledDlls.Any(d => d.Equals(card.RsRecord.InstalledAs, StringComparison.OrdinalIgnoreCase)))
                {
                    card.RsRecord = null;
                    card.RsStatus = GameStatus.NotInstalled;
                    card.RsInstalledFile = null;
                }

                // Update RS status to reflect the Vulkan layer now in use
                var vulkanVersion = AuxInstallService.ReadInstalledVersion(
                    VulkanLayerService.LayerDirectory, VulkanLayerService.LayerDllName);
                card.RsInstalledVersion = vulkanVersion;
                card.RsStatus = GameStatus.Installed;
                card.RefreshBackupState();
            }
            card.NotifyAll();

            progress?.Report(("DXVK installed!", 100));
            CrashReporter.Log($"[DxvkService.InstallAsync] Install complete for {card.GameName}");
            return true;
        }
        catch (IOException ioEx) when (IsFileLockedException(ioEx))
        {
            var msg = $"File is in use — close {card.GameName} and retry.";
            CrashReporter.Log($"[DxvkService.InstallAsync] {msg} — {ioEx.Message}");
            progress?.Report(($"❌ {msg}", 0));
            return false;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DxvkService.InstallAsync] {card.GameName} — {ex.Message}");
            CrashReporter.WriteCrashReport("DxvkService.InstallAsync", ex,
                note: $"Game: {card.GameName}, Path: {card.InstallPath}");
            progress?.Report(($"❌ Install failed: {ex.Message}", 0));
            return false;
        }
    }

    // ── Uninstall ─────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<string?> UninstallAsync(GameCardViewModel card)
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
                return null;
            }

            var gameDir = card.InstallPath;

            // ── 0. Preflight: a running game holds DXVK's DLLs. Deleting would fail half-way,
            //       and the DX9 path would then delete the ReShade backup and the record.
            var blocked = UninstallPreflight(gameDir, record, card.GameName);
            if (blocked != null)
            {
                CrashReporter.Log($"[DxvkService.Uninstall] '{card.GameName}': {blocked} — nothing changed, record kept");
                return blocked;
            }

            // ── 1. Delete DXVK DLLs from game root ──────────────────────
            foreach (var dll in record.InstalledDlls)
            {
                var dllPath = FileHelper.SafeCombine(gameDir, dll);
                if (dllPath == null) { CrashReporter.Log($"[DxvkService.Uninstall] Skipping unsafe record path '{dll}'"); continue; }
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

            // ── 2. Delete DXVK DLLs from OptiScaler's plugins folder ────
            // OptiScaler\plugins (10 builds) or <game>\plugins (0.9; only a DXVK file there is deleted)
            if (record.PluginFolderDlls.Count > 0)
            {
                foreach (var dll in record.PluginFolderDlls)
                {
                    if (FileHelper.SafeCombine(gameDir, dll) == null) { CrashReporter.Log($"[DxvkService.Uninstall] Skipping unsafe record path '{dll}'"); continue; }
                    var dllPath = FindPluginCopy(gameDir, dll);
                    if (dllPath == null) { CrashReporter.Log($"[DxvkService.Uninstall] DXVK '{dll}' isn't in OptiScaler's plugins folder — nothing to delete"); continue; }
                    try
                    {
                        File.Delete(dllPath);
                        CrashReporter.Log($"[DxvkService.Uninstall] Deleted {dll} from {Path.GetRelativePath(gameDir, Path.GetDirectoryName(dllPath)!)}");
                        RemoveEmptyRootPlugins(gameDir, dllPath);
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
                var originalPath = FileHelper.SafeCombine(gameDir, backedUp);
                if (originalPath == null) { CrashReporter.Log($"[DxvkService.Uninstall] Skipping unsafe record path '{backedUp}'"); continue; }
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
            if (record.IsLiliumHdrMode || record.InstalledDlls.Contains("d3d9.dll"))
            {
                // Direct DX9 mode (all variants): DXVK was d3d9.dll, ReShade was on Vulkan layer.
                // Restore ReShade as local d3d9.dll and remove Vulkan footprint.
                var reshadeBackup = Path.Combine(gameDir, "d3d9.dll.reshade_backup");
                var d3d9Path = Path.Combine(gameDir, "d3d9.dll");
                // Older RHI versions overwrote the backup with DXVK's own DLL on a reinstall: never restore that
                if (File.Exists(reshadeBackup) && IsDxvkFileStatic(reshadeBackup))
                {
                    try
                    {
                        File.Delete(reshadeBackup);
                        CrashReporter.Log("[DxvkService.Uninstall] Direct DX9: d3d9.dll.reshade_backup was DXVK, not ReShade — deleted");
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        CrashReporter.Log($"[DxvkService.Uninstall] Failed to delete DXVK copy in .reshade_backup — {ex.Message}");
                    }
                }
                if (File.Exists(reshadeBackup) && !File.Exists(d3d9Path))
                {
                    File.Move(reshadeBackup, d3d9Path);
                    CrashReporter.Log("[DxvkService.Uninstall] Direct DX9: restored ReShade d3d9.dll from backup");
                }
                else if (!File.Exists(d3d9Path))
                {
                    // No backup — reinstall ReShade from staging
                    var stagedRs = card.Is32Bit ? AuxInstallService.RsStagedPath32 : AuxInstallService.RsStagedPath64;
                    if (File.Exists(stagedRs))
                    {
                        File.Copy(stagedRs, d3d9Path);
                        CrashReporter.Log("[DxvkService.Uninstall] Direct DX9: reinstalled ReShade d3d9.dll from staging");
                    }
                }
                // Clean up backup file if d3d9.dll was restored
                if (File.Exists(reshadeBackup) && File.Exists(d3d9Path))
                {
                    try { File.Delete(reshadeBackup); } catch (Exception ex) { CrashReporter.Log($"[DxvkService.UninstallAsync] Failed to delete ReShade backup — {ex.Message}"); }
                }
                // Remove Vulkan footprint
                VulkanFootprintService.Delete(gameDir);
                // d3d9.dll can be the game's own file that step 3 just put back (no ReShade then)
                bool localRs = File.Exists(d3d9Path) && AuxInstallService.IsReShadeFile(d3d9Path);
                // Delete the Vulkan reshade.ini; a fresh standard DX one only when ReShade is there to use it
                var iniPath = Path.Combine(gameDir, "reshade.ini");
                if (File.Exists(iniPath)) try { File.Delete(iniPath); } catch (Exception ex) { CrashReporter.Log($"[DxvkService.UninstallAsync] Failed to delete Vulkan reshade.ini — {ex.Message}"); }
                if (localRs) AuxInstallService.MergeRsIni(gameDir, gameName: card.GameName);
                // Reset card to non-Vulkan — re-apply API from detection/override
                card.VulkanRenderingPath = "DirectX";
                card.GraphicsApi = GraphicsApiType.DirectX9; // Safe default for DX8/DX9 games
                // Restore DetectedApis: remove Vulkan, keep DX9
                card.DetectedApis.Remove(GraphicsApiType.Vulkan);
                // Update RS version to reflect the restored local DLL
                var restoredRsVersion = localRs ? AuxInstallService.ReadInstalledVersion(gameDir, "d3d9.dll") : null;
                if (restoredRsVersion != null)
                    card.RsInstalledVersion = restoredRsVersion;
                if (!localRs && File.Exists(d3d9Path))
                {
                    // Saved ReShade records for d3d9.dll go too, or the next launch shows ReShade installed again
                    foreach (var rsType in new[] { AuxInstallService.TypeReShade, AuxInstallService.TypeReShadeNormal })
                    {
                        var stale = _auxInstaller.FindRecord(card.GameName, gameDir, rsType);
                        if (stale != null && string.Equals(stale.InstalledAs, "d3d9.dll", StringComparison.OrdinalIgnoreCase))
                        {
                            _auxInstaller.RemoveRecord(stale);
                            CrashReporter.Log($"[DxvkService.Uninstall] Direct DX9: removed {rsType} record for d3d9.dll");
                        }
                    }
                    card.RsRecord = null;
                    card.RsInstalledFile = null;
                    card.RsInstalledVersion = null;
                    card.RsStatus = GameStatus.NotInstalled;
                    CrashReporter.Log("[DxvkService.Uninstall] Direct DX9: d3d9.dll is the game's own file again — no local ReShade, no reshade.ini written");
                }
                // Restore the RsRecord so UninstallReShade can find it (was cleared by DXVK install/CacheLoad)
                if (localRs)
                {
                    var restoredRecord = new AuxInstalledRecord
                    {
                        GameName    = card.GameName,
                        InstallPath = gameDir,
                        Store       = card.Source ?? "",
                        AddonType   = AuxInstallService.TypeReShade,
                        InstalledAs = "d3d9.dll",
                        InstalledAt = File.GetLastWriteTimeUtc(d3d9Path),
                    };
                    _auxInstaller.SaveAuxRecord(restoredRecord);
                    card.RsRecord      = restoredRecord;
                    card.RsInstalledFile = "d3d9.dll";
                    CrashReporter.Log("[DxvkService.Uninstall] Direct DX9: restored RsRecord for d3d9.dll");
                }
                // Lilium HDR only: clear NVIDIA profile present method settings
                if (record.IsLiliumHdrMode)
                {
                    try
                    {
                        var presetSvc = App.Services.GetRequiredService<DlssPresetService>();
                        presetSvc.ClearLiliumPresentMethod(card.GameName, card.InstallPath);
                    }
                    catch (Exception ex) { CrashReporter.Log($"[DxvkService.UninstallAsync] Failed to clear Lilium present method — {ex.Message}"); }
                }
                card.NotifyAll();
                CrashReporter.Log("[DxvkService.Uninstall] Direct DX9: switched back to local ReShade + standard reshade.ini");
            }
            else if (record.IsProxyMode)
            {
                // Legacy proxy mode (pre-migration installs): remove proxy DLL + [PROXY] section,
                // then do the same Vulkan cleanup in case the proxy was partially migrated
                var proxyDll = Path.Combine(gameDir, ProxyDxvkDllName);
                if (File.Exists(proxyDll))
                {
                    try { File.Delete(proxyDll); } catch (Exception ex) { CrashReporter.Log($"[DxvkService.UninstallAsync] Failed to delete legacy proxy DLL — {ex.Message}"); }
                }
                RemoveProxySectionFromReShadeIni(gameDir);
                CrashReporter.Log($"[DxvkService.Uninstall] Legacy proxy mode: removed proxy DLL + [PROXY] section");
            }
            else
            {
                // Standard mode (DX10/DX11): delete Vulkan footprint unconditionally,
                // then switch ReShade from Vulkan layer back to DX proxy if RS is installed.
                VulkanFootprintService.Delete(gameDir);
                CrashReporter.Log($"[DxvkService.Uninstall] Deleted Vulkan footprint");
                try
                {
                    await SwitchReShadeForDxvkAsync(card, dxvkEnabled: false);
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
            return null;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DxvkService.Uninstall] {card.GameName} — {ex.Message}");
            CrashReporter.WriteCrashReport("DxvkService.Uninstall", ex,
                note: $"Game: {card.GameName}, Path: {card.InstallPath}");
            return $"Uninstall failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Null when every file the uninstall touches can be opened exclusively, else a message for the user saying
    /// which one can't and why: in use (a running game keeps its DLLs open) or access denied. Nothing is changed.
    /// </summary>
    internal static string? UninstallPreflight(string gameDir, DxvkInstalledRecord record, string gameName = "the game")
    {
        var paths = new List<string?>();
        foreach (var dll in record.InstalledDlls) paths.Add(FileHelper.SafeCombine(gameDir, dll));
        // Both folders a coexistence copy can be in (OptiScaler\plugins on 10 builds, <game>\plugins on 0.9).
        // No DXVK check here: a running game can hold the file, and a locked file must block, not be skipped.
        foreach (var dll in record.PluginFolderDlls)
            foreach (var dir in PluginCopyDirs(gameDir))
                paths.Add(FileHelper.SafeCombine(dir, dll));
        foreach (var f in record.BackedUpFiles)
        {
            var p = FileHelper.SafeCombine(gameDir, f);
            if (p != null) paths.Add(p + ".original");
        }
        paths.Add(Path.Combine(gameDir, "d3d9.dll.reshade_backup"));
        if (record.DeployedConf) paths.Add(Path.Combine(gameDir, "dxvk.conf"));

        foreach (var path in paths)
        {
            if (path == null || !File.Exists(path)) continue;
            var err = InstallFolderMover.TryOpenExclusiveError(path);
            if (err == null) continue;
            CrashReporter.Log($"[DxvkService.UninstallPreflight] '{path}' — {err.Message}");
            return err is UnauthorizedAccessException
                ? $"RHI isn't allowed to change '{Path.GetFileName(path)}' (access denied). Check the game folder's permissions, or turn on Admin Mode, and try again."
                : $"'{Path.GetFileName(path)}' is in use. Close {gameName} and try again.";
        }
        return null;
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
                    progress?.Report((AntivirusGuard.IsBlocked(AntivirusComponentName)
                        ? "DXVK couldn't be downloaded: your antivirus removed its files. Add an exclusion for %LocalAppData%\\RHI and try again."
                        : "DXVK staging not available", 0));
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

            // ── 2. Determine arch folder for staging ─────────────────────
            // For direct DX9 mode (any variant), card.GraphicsApi is Vulkan (runtime override) — use the
            // original API from the record to determine the correct source folder.
            var isDx9DirectInstall = existingRecord.InstalledDlls.Contains("d3d9.dll");
            var updateApi = isDx9DirectInstall ? GraphicsApiType.DirectX9 : card.GraphicsApi;
            var (archFolder, dllNames) = DetermineRequiredDlls(updateApi, card.Is32Bit);

            // ── 3. Re-deploy DLLs to game root (overwrite existing DXVK DLLs) ──
            foreach (var dll in existingRecord.InstalledDlls)
            {
                var srcPath = Path.Combine(StagingDir, archFolder, dll);
                var destPath = FileHelper.SafeCombine(card.InstallPath, dll);
                if (destPath == null) { CrashReporter.Log($"[DxvkService.UpdateAsync] Skipping unsafe record path '{dll}'"); continue; }
                if (File.Exists(srcPath))
                {
                    CopyFileWithLockHandling(srcPath, destPath, dll, card.GameName);
                    CrashReporter.Log($"[DxvkService.UpdateAsync] Updated {dll} in game root");
                }
            }

            // ── 4. Re-deploy DLLs to OptiScaler's plugins folder ────────
            // Where the copy is now; if it's gone, the installed OptiScaler build's folder (OptiScaler\plugins
            // on 10 builds, <game>\plugins on 0.9). Never over a file there that isn't DXVK.
            if (existingRecord.PluginFolderDlls.Count > 0)
            {
                var pluginsFolder = OptiScalerService.GetPluginsDir(card.InstallPath);
                foreach (var dll in existingRecord.PluginFolderDlls)
                {
                    var srcPath = Path.Combine(StagingDir, archFolder, dll);
                    var destPath = FindPluginCopy(card.InstallPath, dll) ?? FileHelper.SafeCombine(pluginsFolder, dll);
                    if (destPath == null) { CrashReporter.Log($"[DxvkService.UpdateAsync] Skipping unsafe record path '{dll}'"); continue; }
                    if (!CanWritePluginCopy(destPath))
                    {
                        CrashReporter.Log($"[DxvkService.UpdateAsync] Skipping {dll}: '{destPath}' isn't a DXVK file");
                        continue;
                    }
                    if (File.Exists(srcPath))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                        CopyFileWithLockHandling(srcPath, destPath, dll, card.GameName);
                        CrashReporter.Log($"[DxvkService.UpdateAsync] Updated {dll} in {Path.GetRelativePath(card.InstallPath, Path.GetDirectoryName(destPath)!)}");
                    }
                }
            }

            progress?.Report(("Saving updated record...", 80));

            // ── 5. Rewrite dxvk.conf for direct DX9 installs ────────────
            if (isDx9DirectInstall)
            {
                var confPath = Path.Combine(card.InstallPath, "dxvk.conf");
                if (existingRecord.IsLiliumHdrMode)
                {
                    // Lilium HDR: determine DX9 vs DX11 from the installed DLLs
                    var isDx9 = existingRecord.InstalledDlls.Any(d => d.Equals("d3d9.dll", StringComparison.OrdinalIgnoreCase));
                    var confContent = isDx9
                        ? GetLiliumD3d9ConfContent(_liliumPresetIndex)
                        : GetLiliumD3d11ConfContent(_liliumPresetIndex);
                    File.WriteAllText(confPath, confContent);
                    CrashReporter.Log($"[DxvkService.UpdateAsync] Rewrote dxvk.conf with {(isDx9 ? "DX9" : "DX11")} Lilium preset {_liliumPresetIndex}");
                }
                else
                {
                    // Dev/Stable DX9: rewrite default conf
                    File.WriteAllText(confPath, DefaultDxvkConfContent);
                    CrashReporter.Log("[DxvkService.UpdateAsync] Rewrote dxvk.conf with default content (direct DX9 mode)");
                }
            }

            // ── 6. Update tracking record version ────────────────────────
            existingRecord.DxvkVersion = FormatVersionForDisplay(StagedVersion);
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
    public string? CopyConfToGame(GameCardViewModel card)
    {
        try
        {
            if (string.IsNullOrEmpty(card.InstallPath) || !Directory.Exists(card.InstallPath))
            {
                CrashReporter.Log($"[DxvkService.CopyConfToGame] Invalid install path for '{card.GameName}'");
                return "The game folder wasn't found.";
            }

            var destPath = Path.Combine(card.InstallPath, "dxvk.conf");

            // For Lilium HDR variant, deploy the preset-specific conf content
            var record = FindRecord(card.GameName, card.InstallPath);
            bool isLilium = record?.IsLiliumHdrMode == true;
            if (isLilium)
            {
                bool isDx9 = card.GraphicsApi == GraphicsApiType.DirectX9 ||
                             (record?.InstalledDlls.Contains("d3d9.dll") == true);
                int presetIdx = LiliumPresetIndex; // currently set for this card
                var content = isDx9
                    ? GetLiliumD3d9ConfContent(presetIdx)
                    : GetLiliumD3d11ConfContent(presetIdx);
                File.WriteAllText(destPath, content);
                CrashReporter.Log($"[DxvkService.CopyConfToGame] Wrote Lilium HDR dxvk.conf (preset {presetIdx}, {(isDx9 ? "DX9" : "DX11")}) to '{card.GameName}'");
                return null;
            }

            // Non-Lilium: copy from the user's INI template
            if (File.Exists(ConfTemplatePath))
            {
                File.Copy(ConfTemplatePath, destPath, overwrite: true);
                CrashReporter.Log($"[DxvkService.CopyConfToGame] Copied dxvk.conf template to {card.GameName}");
            }
            else
            {
                File.WriteAllText(destPath, DefaultDxvkConfContent);
                CrashReporter.Log($"[DxvkService.CopyConfToGame] Wrote default dxvk.conf to {card.GameName}");
            }
            return null;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DxvkService.CopyConfToGame] {card.GameName} — {ex.Message}");
            return ex.Message;
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

            // Also check OptiScaler's plugins folder (OptiScaler\plugins on 10 builds, <game>\plugins on 0.9)
            foreach (var pluginsFolder in PluginCopyDirs(installPath))
            {
                if (!Directory.Exists(pluginsFolder)) continue;
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

    /// <summary>This install's backups plus the ones an earlier install of the same game recorded (no duplicates).</summary>
    internal static List<string> MergeBackedUpFiles(List<string> current, IEnumerable<string>? prior)
    {
        var merged = new List<string>(current);
        if (prior != null)
            foreach (var f in prior)
                if (!merged.Contains(f, StringComparer.OrdinalIgnoreCase)) merged.Add(f);
        return merged;
    }

    /// <summary>
    /// Clears <c>d3d9.dll</c> for a direct-mode DX9 DXVK install without losing anything:
    /// absent → nothing to do; DXVK (earlier install / reinstall) → replaced, backups untouched;
    /// ReShade → moved to <c>.reshade_backup</c> (an existing backup is never replaced, the current
    /// ReShade copy is then just removed); anything else (the game's own file, another wrapper) →
    /// moved to <c>.original</c>, <paramref name="foreignBackedUp"/> = true. Returns a message when the
    /// install must not go ahead (a foreign file whose <c>.original</c> slot is taken). Locked files
    /// throw <see cref="IOException"/> before anything is changed.
    /// </summary>
    internal static string? PrepareDx9Target(string d3d9Path, Func<string, bool> isDxvk, Func<string, bool> isReShade, out bool foreignBackedUp)
    {
        foreignBackedUp = false;
        if (!File.Exists(d3d9Path)) return null;

        if (isDxvk(d3d9Path))
        {
            CrashReporter.Log("[DxvkService.PrepareDx9Target] d3d9.dll is already DXVK — replacing it, backups kept");
            return null;
        }

        if (isReShade(d3d9Path))
        {
            var rsBackup = d3d9Path + ".reshade_backup";
            if (!File.Exists(rsBackup))
            {
                File.Move(d3d9Path, rsBackup);
                CrashReporter.Log("[DxvkService.PrepareDx9Target] Moved ReShade d3d9.dll to .reshade_backup");
            }
            else
            {
                File.Delete(d3d9Path);
                CrashReporter.Log("[DxvkService.PrepareDx9Target] ReShade d3d9.dll removed; existing .reshade_backup kept");
            }
            return null;
        }

        var original = d3d9Path + ".original";
        if (File.Exists(original))
            return "d3d9.dll in this game belongs to another mod or RHI component, and its backup slot (d3d9.dll.original) is already used. Uninstall that mod first, then install DXVK.";

        File.Move(d3d9Path, original);
        foreignBackedUp = true;
        CrashReporter.Log("[DxvkService.PrepareDx9Target] Backed up the game's d3d9.dll as d3d9.dll.original");
        return null;
    }

    /// <summary>
    /// True when RHI's DXVK record says it put <c>d3d9.dll</c> in the game (DX9 direct mode). Only then is a
    /// DXVK <c>d3d9.dll</c> there RHI's own copy; without it the file is a hand-installed or another mod's DXVK.
    /// </summary>
    internal static bool RecordOwnsD3d9(DxvkInstalledRecord? record)
        => record?.InstalledDlls.Contains("d3d9.dll", StringComparer.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Undoes <see cref="PrepareDx9Target"/> after a DX9 install failed before its record was saved: the game's
    /// file goes back from <c>.original</c> (or ReShade from a <c>.reshade_backup</c> that install made). The name
    /// was free once step 2 had moved it, so whatever is there now is this install's DXVK copy and is replaced.
    /// </summary>
    internal static void UndoDx9Target(string d3d9Path, bool foreignBackedUp, bool reshadeMovedAside)
    {
        var from = foreignBackedUp ? d3d9Path + ".original"
                 : reshadeMovedAside ? d3d9Path + ".reshade_backup"
                 : null;
        if (from == null || !File.Exists(from)) return;
        try
        {
            File.Move(from, d3d9Path, overwrite: true);
            CrashReporter.Log($"[DxvkService.UndoDx9Target] Install failed — put '{Path.GetFileName(from)}' back as d3d9.dll");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[DxvkService.UndoDx9Target] Couldn't put '{Path.GetFileName(from)}' back — {ex.Message}");
        }
    }

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
    private async Task SwitchReShadeForDxvkAsync(GameCardViewModel card, bool dxvkEnabled,
        string? screenshotSavePath = null, string? overlayHotkey = null, string? screenshotHotkey = null)
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
                AuxInstallService.MergeRsVulkanIni(card.InstallPath, card.GameName, screenshotSavePath, overlayHotkey, screenshotHotkey);
                CrashReporter.Log($"[DxvkService.SwitchReShadeForDxvk] {card.GameName}: deployed Vulkan reshade.ini");

                // 3. Create the Vulkan footprint marker
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
                    filenameOverride: rsFilename,
                    store: card.Source);
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
