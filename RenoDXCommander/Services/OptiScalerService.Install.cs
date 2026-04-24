using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Services;

public partial class OptiScalerService
{
    // ── Install / Uninstall / Update ──────────────────────────────────────────

    /// <inheritdoc />
    /// <summary>
    /// Returns the path to the bundled OptiScaler INI template that matches
    /// the current GPU type and DLSS input settings.
    /// </summary>
    public static string GetBundledIniPath(string gpuType, bool dlssInputs)
    {
        var fileName = gpuType.Equals("NVIDIA", StringComparison.OrdinalIgnoreCase)
            ? "OptiScaler.nvidia.ini"
            : dlssInputs
                ? "OptiScaler.amd-dlss.ini"
                : "OptiScaler.amd-nodlss.ini";
        return Path.Combine(AppContext.BaseDirectory, fileName);
    }

    public async Task<AuxInstalledRecord?> InstallAsync(
        GameCardViewModel card,
        IProgress<(string message, double percent)>? progress = null,
        string gpuType = "NVIDIA",
        bool dlssInputs = true,
        string? hotkey = null)
    {
        try
        {
            // ── 1. First-time warning check ──────────────────────────────────
            if (!FirstTimeWarningAcknowledged)
            {
                // The actual dialog is wired in the UI layer; here we just
                // record the acknowledgement so it is only shown once.
                FirstTimeWarningAcknowledged = true;
            }

            progress?.Report(("Preparing OptiScaler install...", 5));

            // ── 2. Validate staging ──────────────────────────────────────────
            if (!IsStagingReady)
            {
                CrashReporter.Log("[OptiScalerService.InstallAsync] Staging not ready — attempting download");
                await EnsureStagingAsync(progress);
                if (!IsStagingReady)
                {
                    CrashReporter.Log("[OptiScalerService.InstallAsync] Staging still not ready after download attempt — aborting");
                    progress?.Report(("OptiScaler staging not available", 0));
                    return null;
                }
            }

            // ── 3. Resolve effective DLL name ────────────────────────────────
            var effectiveDllName = _dllOverrideService.GetEffectiveOsName(card.GameName);

            // For Vulkan games, OptiScaler must be named winmm.dll (dxgi.dll won't load).
            // Only override if no user/manifest override is already set.
            if (effectiveDllName == DefaultDllName
                && card.GraphicsApi == Models.GraphicsApiType.Vulkan)
            {
                effectiveDllName = "winmm.dll";
                CrashReporter.Log($"[OptiScalerService.InstallAsync] {card.GameName}: Vulkan game — auto-selected winmm.dll");
            }

            CrashReporter.Log($"[OptiScalerService.InstallAsync] {card.GameName}: effective DLL name = {effectiveDllName}");

            progress?.Report(("Copying OptiScaler files...", 20));

            // ── 4. ReShade coexistence — rename RS DLL to ReShade64.dll BEFORE deploying files ──
            // This MUST happen before file deployment because OptiScaler may use the same
            // DLL name as ReShade (e.g. dxgi.dll). If we deploy first, OptiScaler overwrites
            // ReShade, and the backup saves the game's original dxgi.dll instead of ReShade.
            try
            {
                // Check tracking record first
                var rsRecord = _auxInstaller.FindRecord(card.GameName, card.InstallPath, AuxInstallService.TypeReShade)
                            ?? _auxInstaller.FindRecord(card.GameName, card.InstallPath, AuxInstallService.TypeReShadeNormal);

                string? rsFilePath = null;
                if (rsRecord != null)
                {
                    var candidatePath = Path.Combine(card.InstallPath, rsRecord.InstalledAs);
                    if (File.Exists(candidatePath))
                        rsFilePath = candidatePath;
                }

                // If no record or file not found, scan for known ReShade DLL names
                if (rsFilePath == null)
                {
                    foreach (var dllName in DllOverrideConstants.CommonDllNames)
                    {
                        var candidatePath = Path.Combine(card.InstallPath, dllName);
                        if (File.Exists(candidatePath) && _auxInstaller is IAuxFileService auxFile && auxFile.IsReShadeFile(candidatePath))
                        {
                            rsFilePath = candidatePath;
                            break;
                        }
                    }
                }

                if (rsFilePath != null)
                {
                    var rsCurrentName = Path.GetFileName(rsFilePath);
                    var rsDestPath = Path.Combine(card.InstallPath, ReShadeCoexistName);

                    // Only rename if not already named ReShade64.dll
                    if (!rsCurrentName.Equals(ReShadeCoexistName, StringComparison.OrdinalIgnoreCase))
                    {
                        // If a file already exists at the destination, delete it first (stale leftover)
                        if (File.Exists(rsDestPath))
                            File.Delete(rsDestPath);

                        File.Move(rsFilePath, rsDestPath);
                        CrashReporter.Log($"[OptiScalerService.InstallAsync] Renamed ReShade '{rsCurrentName}' → '{ReShadeCoexistName}'");
                    }

                    // Update ReShade tracking record
                    if (rsRecord != null)
                    {
                        rsRecord.InstalledAs = ReShadeCoexistName;
                        _auxInstaller.SaveAuxRecord(rsRecord);
                        CrashReporter.Log($"[OptiScalerService.InstallAsync] Updated ReShade record InstalledAs → '{ReShadeCoexistName}'");
                    }

                    // Update card RS state
                    card.RsInstalledFile = ReShadeCoexistName;
                }
            }
            catch (Exception rsEx)
            {
                CrashReporter.Log($"[OptiScalerService.InstallAsync] ReShade rename failed — {rsEx.Message}");
            }

            // ── 5. Deploy all files from staging to game folder ──────────────
            // OptiScaler.dll is renamed to the effective DLL name.
            // All other files are copied with their original names.
            // Game-owned originals are backed up to <filename>.original before overwriting.
            var stagingFiles = Directory.GetFiles(StagingDir, "*", SearchOption.TopDirectoryOnly);
            foreach (var stagingFile in stagingFiles)
            {
                var fileName = Path.GetFileName(stagingFile);

                // Skip version.txt — it's RHI's staging metadata, not an OptiScaler file
                if (fileName.Equals("version.txt", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Skip installer scripts, READMEs, and license files — not needed in game folder
                if (fileName.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)
                    || fileName.EndsWith(".sh", StringComparison.OrdinalIgnoreCase)
                    || fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    continue;

                string destPath;
                if (fileName.Equals("OptiScaler.dll", StringComparison.OrdinalIgnoreCase))
                {
                    // OptiScaler.dll gets renamed to the effective DLL name
                    destPath = Path.Combine(card.InstallPath, effectiveDllName);
                }
                else
                {
                    destPath = Path.Combine(card.InstallPath, fileName);
                }

                // Skip OptiScaler.ini here — it's handled separately in step 5 with INI seeding logic
                if (fileName.Equals(IniFileName, StringComparison.OrdinalIgnoreCase))
                    continue;

                BackupOriginalIfExists(destPath);
                File.Copy(stagingFile, destPath, overwrite: true);
                CrashReporter.Log($"[OptiScalerService.InstallAsync] Deployed {fileName}" +
                    (fileName.Equals("OptiScaler.dll", StringComparison.OrdinalIgnoreCase) ? $" as {effectiveDllName}" : ""));
            }

            // ── Deploy subdirectories from staging (e.g. D3D12_Optiscaler) ──
            foreach (var stagingSubDir in Directory.GetDirectories(StagingDir))
            {
                var dirName = Path.GetFileName(stagingSubDir);

                // Skip Licenses folder — not needed in game folder
                if (dirName.Equals("Licenses", StringComparison.OrdinalIgnoreCase))
                    continue;

                var destSubDir = Path.Combine(card.InstallPath, dirName);
                Directory.CreateDirectory(destSubDir);

                foreach (var subFile in Directory.GetFiles(stagingSubDir, "*", SearchOption.AllDirectories))
                {
                    var relativePath = Path.GetRelativePath(stagingSubDir, subFile);
                    var destPath = Path.Combine(destSubDir, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    BackupOriginalIfExists(destPath);
                    File.Copy(subFile, destPath, overwrite: true);
                    CrashReporter.Log($"[OptiScalerService.InstallAsync] Deployed {dirName}/{relativePath}");
                }
            }

            progress?.Report(("Configuring OptiScaler INI...", 60));

            // ── 5. INI seeding and deployment ────────────────────────────────
            Directory.CreateDirectory(AuxInstallService.InisDir);

            var userIniPath = OsIniPath; // %LOCALAPPDATA%\RHI\inis\OptiScaler.ini
            var gameIniPath = Path.Combine(card.InstallPath, IniFileName);

            // Always update the INIs_Folder with the correct bundled INI for the current GPU settings.
            // This ensures the template stays in sync when the user changes GPU type in Settings.
            var bundledIniPath = GetBundledIniPath(gpuType, dlssInputs);
            if (File.Exists(bundledIniPath))
            {
                File.Copy(bundledIniPath, userIniPath, overwrite: true);
                CrashReporter.Log($"[OptiScalerService.InstallAsync] Updated INIs folder with {Path.GetFileName(bundledIniPath)}");
            }

            // Deploy INI to game folder only if one doesn't already exist there
            if (!File.Exists(gameIniPath))
            {
                if (File.Exists(userIniPath))
                {
                    File.Copy(userIniPath, gameIniPath, overwrite: false);
                    CrashReporter.Log("[OptiScalerService.InstallAsync] Deployed OptiScaler.ini to game folder");
                }
            }
            else
            {
                CrashReporter.Log("[OptiScalerService.InstallAsync] OptiScaler.ini already exists in game folder — preserved");
            }

            // Always enforce LoadReshade=true in the deployed INI
            if (File.Exists(gameIniPath))
            {
                EnforceLoadReshade(gameIniPath);
                CrashReporter.Log("[OptiScalerService.InstallAsync] Enforced LoadReshade=true in deployed INI");

                // Apply the user's configured hotkey
                if (!string.IsNullOrEmpty(hotkey))
                {
                    WriteShortcutKey(gameIniPath, hotkey);
                    CrashReporter.Log($"[OptiScalerService.InstallAsync] Set ShortcutKey={hotkey} in deployed INI");
                }

                // Always enforce LoadAsiPlugins=true so OptiPatcher can load when present
                EnforceLoadAsiPlugins(gameIniPath);
                CrashReporter.Log("[OptiScalerService.InstallAsync] Enforced LoadAsiPlugins=true in deployed INI");

                // Point OptiScaler to the staged DLSS DLL — deploy it directly to the game folder
                // since OptiScaler's NVNGX_DLSS_Path INI override doesn't work reliably.
                // The DLL must be physically present in the game directory.
                var stagedDlssPath = GetStagedDlssPath();
                if (stagedDlssPath != null)
                {
                    var gameDlssPath = Path.Combine(card.InstallPath, DlssDllFileName);
                    BackupOriginalIfExists(gameDlssPath);
                    File.Copy(stagedDlssPath, gameDlssPath, overwrite: true);
                    CrashReporter.Log($"[OptiScalerService.InstallAsync] Deployed {DlssDllFileName} ({new FileInfo(gameDlssPath).Length} bytes) to game folder");
                }

                // Deploy DLSS Ray Reconstruction DLL if staged
                var stagedDlssdPath = GetStagedDlssdPath();
                if (stagedDlssdPath != null)
                {
                    var gameDlssdPath = Path.Combine(card.InstallPath, DlssdDllFileName);
                    BackupOriginalIfExists(gameDlssdPath);
                    File.Copy(stagedDlssdPath, gameDlssdPath, overwrite: true);
                    CrashReporter.Log($"[OptiScalerService.InstallAsync] Deployed {DlssdDllFileName} ({new FileInfo(gameDlssdPath).Length} bytes) to game folder");
                }

                // Deploy DLSS Frame Generation DLL if staged
                var stagedDlssgPath = GetStagedDlssgPath();
                if (stagedDlssgPath != null)
                {
                    var gameDlssgPath = Path.Combine(card.InstallPath, DlssgDllFileName);
                    BackupOriginalIfExists(gameDlssgPath);
                    File.Copy(stagedDlssgPath, gameDlssgPath, overwrite: true);
                    CrashReporter.Log($"[OptiScalerService.InstallAsync] Deployed {DlssgDllFileName} ({new FileInfo(gameDlssgPath).Length} bytes) to game folder");
                }
            }

            progress?.Report(("Saving install record...", 80));

            // ── 6. Create/update AuxInstalledRecord ──────────────────────────
            var record = new AuxInstalledRecord
            {
                GameName       = card.GameName,
                InstallPath    = card.InstallPath,
                AddonType      = AddonType,
                InstalledAs    = effectiveDllName,
                SourceUrl      = null,
                RemoteFileSize = null,
                InstalledAt    = DateTime.UtcNow,
            };
            _auxInstaller.SaveAuxRecord(record);
            CrashReporter.Log($"[OptiScalerService.InstallAsync] Saved tracking record for {card.GameName}");

            // ── 7. Deploy OptiPatcher for AMD/Intel GPUs ─────────────────────
            if (gpuType.Equals("AMD", StringComparison.OrdinalIgnoreCase)
                || gpuType.Equals("Intel", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    progress?.Report(("Downloading OptiPatcher...", 85));
                    await EnsureOptiPatcherStagingAsync(progress);

                    var stagedAsi = Path.Combine(OptiPatcherStagingDir, OptiPatcherFileName);
                    if (File.Exists(stagedAsi))
                    {
                        var pluginsDir = Path.Combine(card.InstallPath, "plugins");
                        Directory.CreateDirectory(pluginsDir);
                        var destAsi = Path.Combine(pluginsDir, OptiPatcherFileName);
                        File.Copy(stagedAsi, destAsi, overwrite: true);
                        CrashReporter.Log($"[OptiScalerService.InstallAsync] Deployed OptiPatcher.asi to plugins folder");
                        progress?.Report(("OptiPatcher deployed", 90));
                    }
                    else
                    {
                        CrashReporter.Log("[OptiScalerService.InstallAsync] OptiPatcher staging not available — skipping deployment");
                    }
                }
                catch (Exception opEx)
                {
                    CrashReporter.Log($"[OptiScalerService.InstallAsync] OptiPatcher deployment failed — {opEx.Message}");
                }
            }

            // ── 8. Update card VM properties ─────────────────────────────────
            card.OsInstalledFile = effectiveDllName;
            card.OsInstalledVersion = StagedVersion;
            card.OsStatus = GameStatus.Installed;

            progress?.Report(("OptiScaler installed!", 100));
            CrashReporter.Log($"[OptiScalerService.InstallAsync] Install complete for {card.GameName}");

            return record;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[OptiScalerService.InstallAsync] {card.GameName} — {ex.Message}");
            progress?.Report(($"Install failed: {ex.Message}", 0));
            return null;
        }
    }

    /// <inheritdoc />
    public void Uninstall(GameCardViewModel card)
    {
        try
        {
            var gameDir = card.InstallPath;

            // ── 1. Delete all OptiScaler files and restore originals ─────────
            // Determine which files were deployed by checking the staging folder
            var stagingFiles = IsStagingReady ? Directory.GetFiles(StagingDir, "*", SearchOption.TopDirectoryOnly) : Array.Empty<string>();
            var stagingDirs = IsStagingReady ? Directory.GetDirectories(StagingDir) : Array.Empty<string>();
            var deployedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var stagingFile in stagingFiles)
            {
                var fileName = Path.GetFileName(stagingFile);
                if (fileName.Equals("version.txt", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (fileName.Equals("OptiScaler.dll", StringComparison.OrdinalIgnoreCase))
                    continue; // handled separately below (renamed on deploy)
                if (fileName.Equals(IniFileName, StringComparison.OrdinalIgnoreCase))
                    continue; // handled separately below
                deployedFileNames.Add(fileName);
            }

            // Delete the renamed OptiScaler DLL
            var installedDll = card.OsInstalledFile;
            if (string.IsNullOrEmpty(installedDll))
            {
                var record = _auxInstaller.FindRecord(card.GameName, gameDir, AddonType);
                installedDll = record?.InstalledAs;
            }

            // Determine if ReShade will be renamed to the same filename as the OptiScaler DLL.
            // If so, skip restoring the .original for that filename — ReShade will claim it.
            string? rsRestoreTarget = null;
            var rsCoexistCheck = Path.Combine(gameDir, ReShadeCoexistName);
            if (File.Exists(rsCoexistCheck))
            {
                rsRestoreTarget = ResolveReShadeFilename(card);
            }

            if (!string.IsNullOrEmpty(installedDll))
            {
                var dllPath = Path.Combine(gameDir, installedDll);
                if (File.Exists(dllPath))
                {
                    File.Delete(dllPath);
                    CrashReporter.Log($"[OptiScalerService.Uninstall] Deleted OptiScaler DLL: {dllPath}");

                    // Only restore the .original if ReShade won't be renamed to this filename
                    if (rsRestoreTarget == null
                        || !installedDll.Equals(rsRestoreTarget, StringComparison.OrdinalIgnoreCase))
                    {
                        RestoreOriginalIfExists(dllPath);
                    }
                    else
                    {
                        // Delete the .original backup so it doesn't get restored later
                        // when ReShade is uninstalled (AuxInstallService.RestoreForeignDll)
                        var originalPath = dllPath + ".original";
                        if (File.Exists(originalPath))
                        {
                            try
                            {
                                File.Delete(originalPath);
                                CrashReporter.Log($"[OptiScalerService.Uninstall] Deleted stale backup '{Path.GetFileName(originalPath)}' — ReShade will claim '{installedDll}'");
                            }
                            catch (Exception delEx)
                            {
                                CrashReporter.Log($"[OptiScalerService.Uninstall] Failed to delete backup — {delEx.Message}");
                            }
                        }
                        CrashReporter.Log($"[OptiScalerService.Uninstall] Skipping .original restore for '{installedDll}' — ReShade will claim this filename");
                    }
                }
            }

            // ── 2. Delete OptiScaler.ini from game folder ────────────────────
            var iniPath = Path.Combine(gameDir, IniFileName);
            if (File.Exists(iniPath))
            {
                File.Delete(iniPath);
                CrashReporter.Log($"[OptiScalerService.Uninstall] Deleted {IniFileName}");
            }

            // ── 2b. Delete deployed nvngx_dlss.dll and restore original ─────
            var gameDlssPath = Path.Combine(gameDir, DlssDllFileName);
            if (File.Exists(gameDlssPath))
            {
                File.Delete(gameDlssPath);
                CrashReporter.Log($"[OptiScalerService.Uninstall] Deleted {DlssDllFileName}");
                RestoreOriginalIfExists(gameDlssPath);
            }

            // ── 2c. Delete deployed nvngx_dlssd.dll and restore original ────
            var gameDlssdPath = Path.Combine(gameDir, DlssdDllFileName);
            if (File.Exists(gameDlssdPath))
            {
                File.Delete(gameDlssdPath);
                CrashReporter.Log($"[OptiScalerService.Uninstall] Deleted {DlssdDllFileName}");
                RestoreOriginalIfExists(gameDlssdPath);
            }

            // ── 2d. Delete deployed nvngx_dlssg.dll and restore original ────
            var gameDlssgPath = Path.Combine(gameDir, DlssgDllFileName);
            if (File.Exists(gameDlssgPath))
            {
                File.Delete(gameDlssgPath);
                CrashReporter.Log($"[OptiScalerService.Uninstall] Deleted {DlssgDllFileName}");
                RestoreOriginalIfExists(gameDlssgPath);
            }

            // ── 3. Delete all other deployed files ───────────────────────────
            foreach (var fileName in deployedFileNames)
            {
                var filePath = Path.Combine(gameDir, fileName);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    CrashReporter.Log($"[OptiScalerService.Uninstall] Deleted {fileName}");
                    RestoreOriginalIfExists(filePath);
                }
            }

            // ── 3b. Clean up deployed subdirectories ─────────────────────────
            foreach (var stagingSubDir in stagingDirs)
            {
                var dirName = Path.GetFileName(stagingSubDir);
                if (dirName.Equals("Licenses", StringComparison.OrdinalIgnoreCase))
                    continue;

                var gameSubDir = Path.Combine(gameDir, dirName);
                if (!Directory.Exists(gameSubDir)) continue;

                foreach (var subFile in Directory.GetFiles(gameSubDir, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        File.Delete(subFile);
                        RestoreOriginalIfExists(subFile);
                    }
                    catch (Exception ex)
                    {
                        CrashReporter.Log($"[OptiScalerService.Uninstall] Failed to delete {subFile} — {ex.Message}");
                    }
                }

                // Remove the subdirectory if empty
                try
                {
                    if (Directory.Exists(gameSubDir) && !Directory.EnumerateFileSystemEntries(gameSubDir).Any())
                        Directory.Delete(gameSubDir, recursive: true);
                }
                catch { }
            }

            // ── 4. Remove AuxInstalledRecord ─────────────────────────────────
            var existingRecord = _auxInstaller.FindRecord(card.GameName, gameDir, AddonType);
            if (existingRecord != null)
            {
                _auxInstaller.RemoveRecord(existingRecord);
                CrashReporter.Log($"[OptiScalerService.Uninstall] Removed tracking record for {card.GameName}");
            }

            // ── 4b. Clean up OptiPatcher ─────────────────────────────────────
            try
            {
                var optiPatcherPath = Path.Combine(gameDir, "plugins", OptiPatcherFileName);
                if (File.Exists(optiPatcherPath))
                {
                    File.Delete(optiPatcherPath);
                    CrashReporter.Log("[OptiScalerService.Uninstall] Deleted OptiPatcher.asi from plugins folder");
                }

                var pluginsDir = Path.Combine(gameDir, "plugins");
                if (Directory.Exists(pluginsDir) && !Directory.EnumerateFileSystemEntries(pluginsDir).Any())
                {
                    Directory.Delete(pluginsDir);
                    CrashReporter.Log("[OptiScalerService.Uninstall] Removed empty plugins folder");
                }
            }
            catch (Exception opEx)
            {
                CrashReporter.Log($"[OptiScalerService.Uninstall] OptiPatcher cleanup failed — {opEx.Message}");
            }

            // ── 5. ReShade coexistence — restore ReShade64.dll to correct name ──
            try
            {
                var rsCoexistPath = Path.Combine(gameDir, ReShadeCoexistName);
                if (File.Exists(rsCoexistPath))
                {
                    var resolvedName = ResolveReShadeFilename(card);
                    var resolvedPath = Path.Combine(gameDir, resolvedName);

                    if (!resolvedName.Equals(ReShadeCoexistName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (File.Exists(resolvedPath))
                        {
                            // Target filename is occupied — keep as ReShade64.dll
                            CrashReporter.Log($"[OptiScalerService.Uninstall] Target '{resolvedName}' occupied — keeping ReShade as '{ReShadeCoexistName}'");
                        }
                        else
                        {
                            File.Move(rsCoexistPath, resolvedPath);
                            CrashReporter.Log($"[OptiScalerService.Uninstall] Renamed ReShade '{ReShadeCoexistName}' → '{resolvedName}'");
                        }
                    }

                    // Update ReShade tracking record
                    var rsRecord = _auxInstaller.FindRecord(card.GameName, gameDir, AuxInstallService.TypeReShade)
                                ?? _auxInstaller.FindRecord(card.GameName, gameDir, AuxInstallService.TypeReShadeNormal);
                    if (rsRecord != null)
                    {
                        var actualName = File.Exists(resolvedPath) ? resolvedName : ReShadeCoexistName;
                        rsRecord.InstalledAs = actualName;
                        _auxInstaller.SaveAuxRecord(rsRecord);
                        CrashReporter.Log($"[OptiScalerService.Uninstall] Updated ReShade record InstalledAs → '{actualName}'");

                        // Also update the card's RsRecord reference so UninstallReShade finds the correct file
                        if (card.RsRecord != null)
                        {
                            card.RsRecord.InstalledAs = actualName;
                        }
                    }

                    // Update card RS state
                    card.RsInstalledFile = File.Exists(resolvedPath) ? resolvedName : ReShadeCoexistName;
                }
            }
            catch (Exception rsEx)
            {
                CrashReporter.Log($"[OptiScalerService.Uninstall] ReShade restore failed — {rsEx.Message}");
            }

            // ── 6. Update card VM ────────────────────────────────────────────
            card.OsStatus = GameStatus.NotInstalled;
            card.OsInstalledFile = null;
            card.OsInstalledVersion = null;

            CrashReporter.Log($"[OptiScalerService.Uninstall] Uninstall complete for {card.GameName}");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[OptiScalerService.Uninstall] {card.GameName} — {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task UpdateAsync(
        GameCardViewModel card,
        IProgress<(string message, double percent)>? progress = null)
    {
        try
        {
            progress?.Report(("Preparing OptiScaler update...", 5));

            // ── 1. Validate staging is ready ─────────────────────────────────
            if (!IsStagingReady)
            {
                CrashReporter.Log("[OptiScalerService.UpdateAsync] Staging not ready — attempting download");
                await EnsureStagingAsync(progress);
                if (!IsStagingReady)
                {
                    CrashReporter.Log("[OptiScalerService.UpdateAsync] Staging still not ready after download attempt — aborting");
                    progress?.Report(("OptiScaler staging not available", 0));
                    return;
                }
            }

            var gameDir = card.InstallPath;

            // ── 2. Get the installed DLL filename from tracking record ───────
            var record = _auxInstaller.FindRecord(card.GameName, gameDir, AddonType);
            var installedDll = record?.InstalledAs ?? card.OsInstalledFile;
            if (string.IsNullOrEmpty(installedDll))
            {
                CrashReporter.Log($"[OptiScalerService.UpdateAsync] No installed DLL filename found for {card.GameName} — aborting");
                progress?.Report(("Update failed: no installed DLL found", 0));
                return;
            }

            progress?.Report(("Updating OptiScaler files...", 20));

            // ── 3. Deploy all files from staging, overwriting OptiScaler files ──
            // Originals were already backed up during initial install.
            var stagingFiles = Directory.GetFiles(StagingDir, "*", SearchOption.TopDirectoryOnly);
            foreach (var stagingFile in stagingFiles)
            {
                var fileName = Path.GetFileName(stagingFile);

                if (fileName.Equals("version.txt", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Skip installer scripts, READMEs, and license files — not needed in game folder
                if (fileName.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)
                    || fileName.EndsWith(".sh", StringComparison.OrdinalIgnoreCase)
                    || fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (fileName.Equals(IniFileName, StringComparison.OrdinalIgnoreCase))
                    continue; // INI is preserved — do not overwrite

                string destPath;
                if (fileName.Equals("OptiScaler.dll", StringComparison.OrdinalIgnoreCase))
                    destPath = Path.Combine(gameDir, installedDll);
                else
                    destPath = Path.Combine(gameDir, fileName);

                // Backup any new game files that weren't present during initial install
                // (e.g. if a new OptiScaler version adds files that collide with game files)
                BackupOriginalIfExists(destPath);
                File.Copy(stagingFile, destPath, overwrite: true);
                CrashReporter.Log($"[OptiScalerService.UpdateAsync] Replaced {fileName}");
            }

            // ── Deploy subdirectories from staging (e.g. D3D12_Optiscaler) ──
            foreach (var stagingSubDir in Directory.GetDirectories(StagingDir))
            {
                var dirName = Path.GetFileName(stagingSubDir);

                // Skip Licenses folder — not needed in game folder
                if (dirName.Equals("Licenses", StringComparison.OrdinalIgnoreCase))
                    continue;

                var destSubDir = Path.Combine(gameDir, dirName);
                Directory.CreateDirectory(destSubDir);

                foreach (var subFile in Directory.GetFiles(stagingSubDir, "*", SearchOption.AllDirectories))
                {
                    var relativePath = Path.GetRelativePath(stagingSubDir, subFile);
                    var destPath = Path.Combine(destSubDir, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    BackupOriginalIfExists(destPath);
                    File.Copy(subFile, destPath, overwrite: true);
                    CrashReporter.Log($"[OptiScalerService.UpdateAsync] Deployed {dirName}/{relativePath}");
                }
            }

            // ── 4. Do NOT overwrite OptiScaler.ini in the game folder ────────
            // INI is intentionally preserved — no copy operation here
            CrashReporter.Log("[OptiScalerService.UpdateAsync] Preserved existing OptiScaler.ini in game folder");

            // ── 4b. Update nvngx_dlss.dll in game folder if staged ──────────
            var stagedDlssUpdate = GetStagedDlssPath();
            if (stagedDlssUpdate != null)
            {
                var gameDlssUpdate = Path.Combine(gameDir, DlssDllFileName);
                BackupOriginalIfExists(gameDlssUpdate);
                File.Copy(stagedDlssUpdate, gameDlssUpdate, overwrite: true);
                CrashReporter.Log($"[OptiScalerService.UpdateAsync] Updated {DlssDllFileName} in game folder");
            }

            // ── 4c. Update nvngx_dlssd.dll in game folder if staged ─────────
            var stagedDlssdUpdate = GetStagedDlssdPath();
            if (stagedDlssdUpdate != null)
            {
                var gameDlssdUpdate = Path.Combine(gameDir, DlssdDllFileName);
                BackupOriginalIfExists(gameDlssdUpdate);
                File.Copy(stagedDlssdUpdate, gameDlssdUpdate, overwrite: true);
                CrashReporter.Log($"[OptiScalerService.UpdateAsync] Updated {DlssdDllFileName} in game folder");
            }

            // ── 4d. Update nvngx_dlssg.dll in game folder if staged ─────────
            var stagedDlssgUpdate = GetStagedDlssgPath();
            if (stagedDlssgUpdate != null)
            {
                var gameDlssgUpdate = Path.Combine(gameDir, DlssgDllFileName);
                BackupOriginalIfExists(gameDlssgUpdate);
                File.Copy(stagedDlssgUpdate, gameDlssgUpdate, overwrite: true);
                CrashReporter.Log($"[OptiScalerService.UpdateAsync] Updated {DlssgDllFileName} in game folder");
            }

            progress?.Report(("Updating tracking record...", 80));

            // ── 5. Update tracking record with new version ───────────────────
            if (record != null)
            {
                record.InstalledAt = DateTime.UtcNow;
                _auxInstaller.SaveAuxRecord(record);
                CrashReporter.Log($"[OptiScalerService.UpdateAsync] Updated tracking record for {card.GameName}");
            }

            // ── 6. Update card VM properties ─────────────────────────────────
            card.OsInstalledVersion = StagedVersion;
            card.OsStatus = GameStatus.Installed;

            progress?.Report(("OptiScaler updated!", 100));
            CrashReporter.Log($"[OptiScalerService.UpdateAsync] Update complete for {card.GameName}");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[OptiScalerService.UpdateAsync] {card.GameName} — {ex.Message}");
            progress?.Report(($"Update failed: {ex.Message}", 0));
        }
    }

    // ── INI management ────────────────────────────────────────────────────────

    /// <inheritdoc />
    public void CopyIniToGame(GameCardViewModel card)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return;

        var sourceIni = OsIniPath; // %LOCALAPPDATA%\RHI\inis\OptiScaler.ini
        if (!File.Exists(sourceIni))
        {
            CrashReporter.Log("[OptiScalerService.CopyIniToGame] No OptiScaler.ini in INIs folder — aborting copy.");
            return;
        }

        var destIni = Path.Combine(card.InstallPath, IniFileName);
        File.Copy(sourceIni, destIni, overwrite: true);
        EnforceLoadReshade(destIni);
        CrashReporter.Log($"[OptiScalerService.CopyIniToGame] Copied OptiScaler.ini to '{card.InstallPath}' with LoadReshade=true enforced.");
    }

    /// <summary>
    /// Before copying an OptiScaler file to the game folder, checks if a game-owned
    /// original already exists at the destination. If so, renames it to
    /// &lt;filename&gt;.original so it can be restored on uninstall.
    /// Skips backup if a .original already exists (from a previous install).
    /// </summary>
    private static void BackupOriginalIfExists(string destPath)
    {
        if (!File.Exists(destPath)) return;
        var backupPath = destPath + ".original";
        if (File.Exists(backupPath)) return; // already backed up from a previous install
        try
        {
            File.Move(destPath, backupPath);
            CrashReporter.Log($"[OptiScalerService] Backed up original: {Path.GetFileName(destPath)} → {Path.GetFileName(backupPath)}");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[OptiScalerService] Failed to back up '{Path.GetFileName(destPath)}' — {ex.Message}");
        }
    }

    /// <summary>
    /// After removing an OptiScaler file from the game folder, checks if a
    /// &lt;filename&gt;.original backup exists. If so, restores it to the original name.
    /// </summary>
    private static void RestoreOriginalIfExists(string filePath)
    {
        var backupPath = filePath + ".original";
        if (!File.Exists(backupPath)) return;
        try
        {
            // If the OptiScaler file wasn't deleted (e.g. in-use), don't overwrite it
            if (File.Exists(filePath))
            {
                CrashReporter.Log($"[OptiScalerService] Cannot restore '{Path.GetFileName(filePath)}' — file still exists");
                return;
            }
            File.Move(backupPath, filePath);
            CrashReporter.Log($"[OptiScalerService] Restored original: {Path.GetFileName(backupPath)} → {Path.GetFileName(filePath)}");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[OptiScalerService] Failed to restore '{Path.GetFileName(filePath)}' — {ex.Message}");
        }
    }

    /// <summary>
    /// Reads the INI file at <paramref name="iniPath"/>, finds the <c>LoadReshade=</c> line
    /// (case-insensitive), replaces it with <c>LoadReshade=true</c>, or appends the line
    /// if it is missing. Writes the result back to the file.
    /// </summary>
    public static void EnforceLoadReshade(string iniPath)
    {
        var lines = File.ReadAllLines(iniPath).ToList();
        bool found = false;
        for (int i = 0; i < lines.Count; /* manual increment */)
        {
            if (lines[i].TrimStart().StartsWith("LoadReshade=", StringComparison.OrdinalIgnoreCase))
            {
                if (!found)
                {
                    // First occurrence — replace with enforced value
                    lines[i] = "LoadReshade=true";
                    found = true;
                    i++;
                }
                else
                {
                    // Duplicate — remove it
                    lines.RemoveAt(i);
                }
            }
            else
            {
                i++;
            }
        }
        if (!found)
            lines.Add("LoadReshade=true");
        File.WriteAllLines(iniPath, lines);
    }

    /// <summary>
    /// Reads the INI file at <paramref name="iniPath"/>, finds the <c>LoadAsiPlugins=</c> line
    /// (case-insensitive), replaces it with <c>LoadAsiPlugins=true</c>, or appends the line
    /// if it is missing. Writes the result back to the file.
    /// </summary>
    public static void EnforceLoadAsiPlugins(string iniPath)
    {
        var lines = File.ReadAllLines(iniPath).ToList();
        bool found = false;
        for (int i = 0; i < lines.Count; /* manual increment */)
        {
            if (lines[i].TrimStart().StartsWith("LoadAsiPlugins=", StringComparison.OrdinalIgnoreCase))
            {
                if (!found)
                {
                    lines[i] = "LoadAsiPlugins=true";
                    found = true;
                    i++;
                }
                else
                {
                    // Duplicate — remove it
                    lines.RemoveAt(i);
                }
            }
            else
            {
                i++;
            }
        }
        if (!found)
            lines.Add("LoadAsiPlugins=true");
        File.WriteAllLines(iniPath, lines);
    }

    /// <summary>
    /// Reads the INI file at <paramref name="iniPath"/>, finds the <c>NVNGX_DLSS_Path=</c> line
    /// (case-insensitive), replaces it with the given <paramref name="dlssFilePath"/> (full path
    /// to nvngx_dlss.dll), or appends the line if it is missing. OptiScaler expects the full
    /// file path including the filename, not just the directory.
    /// Safe to set for all games — OptiScaler auto-locates the game's own copy first.
    /// </summary>
    public static void EnforceNvngxDlssPath(string iniPath, string dlssFilePath)
    {
        var lines = File.ReadAllLines(iniPath).ToList();
        var value = $"NVNGX_DLSS_Path={dlssFilePath}";
        bool found = false;
        for (int i = 0; i < lines.Count; /* manual increment */)
        {
            if (lines[i].TrimStart().StartsWith("NVNGX_DLSS_Path=", StringComparison.OrdinalIgnoreCase))
            {
                if (!found)
                {
                    lines[i] = value;
                    found = true;
                    i++;
                }
                else
                {
                    lines.RemoveAt(i);
                }
            }
            else
            {
                i++;
            }
        }
        if (!found)
            lines.Add(value);
        File.WriteAllLines(iniPath, lines);
    }

    // ── Detection ─────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public string? DetectInstallation(string installPath)
    {
        try
        {
            if (!Directory.Exists(installPath)) return null;

            // Fast path: only check the known DLL names that OptiScaler can be installed as.
            // This avoids scanning every DLL in large game folders (e.g. Alan Wake 2 has hundreds).
            foreach (var dllName in SupportedDllNames)
            {
                var candidatePath = Path.Combine(installPath, dllName);
                if (File.Exists(candidatePath) && IsOptiScalerFile(candidatePath))
                    return dllName;
            }

            // Secondary marker: check for OptiScaler.ini presence
            var iniPath = Path.Combine(installPath, IniFileName);
            if (File.Exists(iniPath))
            {
                // INI exists but no supported DLL matched — check supported DLL names
                // by existence only (in case the binary signature scan missed it)
                foreach (var dllName in SupportedDllNames)
                {
                    var candidatePath = Path.Combine(installPath, dllName);
                    if (File.Exists(candidatePath))
                    {
                        CrashReporter.Log($"[OptiScalerService.DetectInstallation] OptiScaler.ini found with candidate DLL '{dllName}' in '{installPath}'");
                        return dllName;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[OptiScalerService.DetectInstallation] Error scanning '{installPath}' — {ex.Message}");
        }
        return null;
    }

    /// <inheritdoc />
    public bool IsOptiScalerFile(string filePath)
    {
        return IsOptiScalerFileStatic(filePath);
    }

    /// <summary>
    /// Static version of <see cref="IsOptiScalerFile"/> for use by the foreign DLL
    /// protection system (<see cref="AuxInstallService.IdentifyDxgiFile"/>).
    /// Reads the first ~2 MB of a DLL file and scans for OptiScaler binary signatures.
    /// </summary>
    public static bool IsOptiScalerFileStatic(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return false;

            using var stream = File.OpenRead(filePath);
            var bufferSize = (int)Math.Min(stream.Length, 8 * 1024 * 1024);
            var buffer = new byte[bufferSize];
            int totalRead = 0;
            while (totalRead < bufferSize)
            {
                int read = stream.Read(buffer, totalRead, bufferSize - totalRead);
                if (read == 0) break;
                totalRead += read;
            }

            foreach (var signature in OptiScalerSignatures)
            {
                if (ContainsSequence(buffer, totalRead, signature))
                    return true;
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[OptiScalerService.IsOptiScalerFile] Error scanning '{filePath}' — {ex.Message}");
        }
        return false;
    }

    /// <summary>
    /// Searches for a byte sequence within a buffer using a simple sliding-window scan.
    /// </summary>
    private static bool ContainsSequence(byte[] buffer, int bufferLength, byte[] sequence)
    {
        if (sequence.Length == 0 || bufferLength < sequence.Length) return false;
        var limit = bufferLength - sequence.Length;
        for (int i = 0; i <= limit; i++)
        {
            bool match = true;
            for (int j = 0; j < sequence.Length; j++)
            {
                if (buffer[i + j] != sequence[j])
                {
                    match = false;
                    break;
                }
            }
            if (match) return true;
        }
        return false;
    }

    // ── Tracking records ──────────────────────────────────────────────────────

    /// <inheritdoc />
    public List<AuxInstalledRecord> LoadAllRecords()
    {
        try
        {
            return _auxInstaller.LoadAll()
                .Where(r => r.AddonType.Equals(AddonType, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[OptiScalerService.LoadAllRecords] Failed to load records — {ex.Message}");
            return new List<AuxInstalledRecord>();
        }
    }

    /// <inheritdoc />
    public AuxInstalledRecord? FindRecord(string gameName, string installPath)
    {
        try
        {
            return _auxInstaller.FindRecord(gameName, installPath, AddonType);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[OptiScalerService.FindRecord] Failed to find record for '{gameName}' — {ex.Message}");
            return null;
        }
    }

    // ── DLL naming ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public string GetEffectiveOsDllName(string gameName)
    {
        throw new NotImplementedException();
    }

    // ── Hotkey ────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public void SetHotkey(string hotkeyValue)
    {
        try
        {
            Directory.CreateDirectory(AuxInstallService.InisDir);
            var iniPath = OsIniPath;
            WriteShortcutKey(iniPath, hotkeyValue);
            CrashReporter.Log($"[OptiScalerService.SetHotkey] Wrote ShortcutKey={hotkeyValue} to INIs folder");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[OptiScalerService.SetHotkey] Failed — {ex.Message}");
        }
    }

    /// <inheritdoc />
    public void ApplyHotkeyToAllGames(string hotkeyValue)
    {
        try
        {
            var records = LoadAllRecords();
            int updatedCount = 0;
            foreach (var record in records)
            {
                var gameIniPath = Path.Combine(record.InstallPath, IniFileName);
                if (!File.Exists(gameIniPath)) continue;

                try
                {
                    WriteShortcutKey(gameIniPath, hotkeyValue);
                    updatedCount++;
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[OptiScalerService.ApplyHotkeyToAllGames] Failed for '{record.GameName}' — {ex.Message}");
                }
            }
            CrashReporter.Log($"[OptiScalerService.ApplyHotkeyToAllGames] Updated {updatedCount} game(s) with ShortcutKey={hotkeyValue}");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[OptiScalerService.ApplyHotkeyToAllGames] Failed — {ex.Message}");
        }
    }

    /// <summary>
    /// Reads the INI file at <paramref name="iniPath"/>, finds the <c>ShortcutKey=</c> line
    /// (case-insensitive), replaces it with <c>ShortcutKey=&lt;value&gt;</c>, or appends the line
    /// if it is missing. Writes the result back to the file. If the file does not exist,
    /// creates it with just the ShortcutKey line.
    /// </summary>
    public static void WriteShortcutKey(string iniPath, string hotkeyValue)
    {
        // Convert friendly name (e.g. "Delete") to VK code (e.g. "0x2E") for OptiScaler
        var vkValue = ResolveHotkeyToVkCode(hotkeyValue);

        var lines = File.Exists(iniPath)
            ? File.ReadAllLines(iniPath).ToList()
            : new List<string>();

        bool found = false;
        for (int i = 0; i < lines.Count; /* manual increment */)
        {
            if (lines[i].TrimStart().StartsWith("ShortcutKey=", StringComparison.OrdinalIgnoreCase))
            {
                if (!found)
                {
                    lines[i] = $"ShortcutKey={vkValue}";
                    found = true;
                    i++;
                }
                else
                {
                    // Duplicate — remove it
                    lines.RemoveAt(i);
                }
            }
            else
            {
                i++;
            }
        }
        if (!found)
            lines.Add($"ShortcutKey={vkValue}");
        File.WriteAllLines(iniPath, lines);
    }

    /// <summary>
    /// Reads the <c>ShortcutKey=</c> value from the given INI file.
    /// Returns null if the file does not exist or the key is not found.
    /// </summary>
    public static string? ReadShortcutKey(string iniPath)
    {
        if (!File.Exists(iniPath)) return null;
        foreach (var line in File.ReadAllLines(iniPath))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("ShortcutKey=", StringComparison.OrdinalIgnoreCase))
                return trimmed.Substring("ShortcutKey=".Length);
        }
        return null;
    }
}
