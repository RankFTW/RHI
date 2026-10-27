using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    // ── Install helpers ───────────────────────────────────────────────────────

    private async Task InstallDlss5ToolAsync(
        GameCardViewModel card,
        Button statusBtn,
        ComboBox addonVersionCombo,
        ComboBox nrVersionCombo,
        Renodx5AddonService rdx5Svc,
        IDlssStreamlineService dlssSvc,
        IAddonPackService addonSvc)
    {
        var installPath = card.InstallPath!;
        var gameName    = card.GameName;
        var store       = card.Source ?? "";

        // Resolve requested addon version
        var requestedVersion = await DispatchAsync<string?>(_window.DispatcherQueue!,
            () => addonVersionCombo.SelectedItem as string).ConfigureAwait(false);
        bool useLatest = string.IsNullOrEmpty(requestedVersion) || requestedVersion.StartsWith("Latest");

        string addonSourcePath;
        if (useLatest)
        {
            // Use the flat staging file (latest) — same as before
            _window.DispatcherQueue?.TryEnqueue(() => statusBtn.Content = "Staging DLSS5 Tool...");
            await rdx5Svc.EnsureStagingAsync().ConfigureAwait(false);
            if (!rdx5Svc.IsStagingReady)
                throw new InvalidOperationException("DLSS5 Tool staging not ready");
            addonSourcePath = rdx5Svc.StagedFilePath;
            // Persist "Latest" (clears any pinned version)
            _window.ViewModel.SetNrAddonVersion(gameName, null, store);
        }
        else
        {
            // Ensure the specific version is staged
            _window.DispatcherQueue?.TryEnqueue(() => statusBtn.Content = $"Staging DLSS5 Tool v{requestedVersion}...");
            var staged = await rdx5Svc.EnsureVersionStagedAsync("dlss5tool", requestedVersion!).ConfigureAwait(false);
            if (!staged)
            {
                CrashReporter.Log($"[NeuralRendering] Could not stage DLSS5 Tool v{requestedVersion} — falling back to latest");
                await rdx5Svc.EnsureStagingAsync().ConfigureAwait(false);
                if (!rdx5Svc.IsStagingReady)
                    throw new InvalidOperationException("DLSS5 Tool staging not ready");
                addonSourcePath = rdx5Svc.StagedFilePath;
                _window.ViewModel.SetNrAddonVersion(gameName, null, store);
            }
            else
            {
                addonSourcePath = rdx5Svc.GetVersionedStagedFilePath("dlss5tool", requestedVersion!)!;
                _window.ViewModel.SetNrAddonVersion(gameName, requestedVersion, store);
            }
        }

        // Deploy the addon
        await Task.Run(() =>
        {
            var deployDir = ModInstallService.GetAddonDeployPath(installPath);
            Directory.CreateDirectory(deployDir);
            File.Copy(addonSourcePath, Path.Combine(deployDir, "renodx-dlss5.addon64"), overwrite: true);
            CrashReporter.Log($"[NeuralRendering] Deployed renodx-dlss5.addon64 (v{(useLatest ? "latest" : requestedVersion)}) to '{deployDir}'");
            // Note: intentionally NOT calling TrackAddonDeployment — NR-managed files are not
            // tracked by AddonPackService to prevent the stale-cleanup pass from removing them.
        }).ConfigureAwait(false);

        // Upgrade all DLSS DLLs to latest (SR/RR/FG + NR)
        _window.DispatcherQueue?.TryEnqueue(() => statusBtn.Content = "Upgrading DLSS DLLs...");
        await UpgradeDlssDllsAsync(card, dlssSvc, nrVersionCombo).ConfigureAwait(false);

        // Append the addon file to the Dlss5Tool component record (DLLs recorded in UpgradeDlssDllsAsync)
        var d5Files = Models.RhiInstallManifest.GetComponentFiles(installPath, "Dlss5Tool").ToList();
        if (!d5Files.Contains("renodx-dlss5.addon64", StringComparer.OrdinalIgnoreCase))
            d5Files.Insert(0, "renodx-dlss5.addon64");
        Models.RhiInstallManifest.SetComponent(installPath, "Dlss5Tool", d5Files);
    }

    /// <summary>
    /// Deploys the newest DLSS SR/RR/FG/NR DLLs to the game using detected paths + sentinel pattern.
    /// Used by DLSS5 Tool and DLSS5 Tool + Bridge installs.
    /// </summary>
    private async Task UpgradeDlssDllsAsync(GameCardViewModel card, IDlssStreamlineService dlssSvc, ComboBox? nrVersionCombo = null)
    {
        var installPath = card.InstallPath!;
        var detection   = card.DlssDetection;

        // Fetch newest cached DLLs
        var cachedSr = await dlssSvc.EnsureNewestDlssCachedAsync().ConfigureAwait(false);
        var cachedRr = await dlssSvc.EnsureNewestDlssdCachedAsync().ConfigureAwait(false);
        var cachedFg = await dlssSvc.EnsureNewestDlssgCachedAsync().ConfigureAwait(false);

        // NR DLL — use selected version or newest
        string? nrSelectedVersion = null;
        if (nrVersionCombo != null)
        {
            nrSelectedVersion = await DispatchAsync<string?>(_window.DispatcherQueue!,
                () => nrVersionCombo.SelectedItem as string).ConfigureAwait(false);
        }
        string? cachedNr;
        if (string.IsNullOrEmpty(nrSelectedVersion) || nrSelectedVersion == "Latest")
            cachedNr = await dlssSvc.EnsureNewestDlssnrCachedAsync().ConfigureAwait(false);
        else
        {
            var nrDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RHI", "DLSS-NR", StripVersionSuffix(nrSelectedVersion));
            cachedNr = ResolveCachedNrDllPath(nrSelectedVersion) ?? Path.Combine(nrDir, "nvngx_dlssnr.dll");
            if (!File.Exists(cachedNr))
                cachedNr = await dlssSvc.EnsureSpecificDlssnrCachedAsync(nrSelectedVersion).ConfigureAwait(false)
                        ?? await dlssSvc.EnsureNewestDlssnrCachedAsync().ConfigureAwait(false);
        }

        await Task.Run(() =>
        {
            // SR
            if (cachedSr != null)
            {
                var dest = detection?.DlssPath ?? Path.Combine(installPath, "nvngx_dlss.dll");
                DeployWithSentinel(cachedSr, dest, "NeuralRendering.UpgradeSR");
                RhiInstallManifest.AddSharedFileOwner(installPath, "nvngx_dlss.dll", "Dlss5Tool");
            }
            // RR
            if (cachedRr != null)
            {
                var dest = detection?.DlssdPath ?? Path.Combine(installPath, "nvngx_dlssd.dll");
                DeployWithSentinel(cachedRr, dest, "NeuralRendering.UpgradeRR");
                RhiInstallManifest.AddSharedFileOwner(installPath, "nvngx_dlssd.dll", "Dlss5Tool");
            }
            // FG
            if (cachedFg != null)
            {
                var dest = detection?.DlssgPath ?? Path.Combine(installPath, "nvngx_dlssg.dll");
                DeployWithSentinel(cachedFg, dest, "NeuralRendering.UpgradeFG");
                RhiInstallManifest.AddSharedFileOwner(installPath, "nvngx_dlssg.dll", "Dlss5Tool");
            }
            // NR
            if (cachedNr != null)
            {
                var dest = detection?.DlssnrPath ?? Path.Combine(installPath, "nvngx_dlssnr.dll");
                DeployNrDllSentinel(installPath, cachedNr); // uses the sentinel helper
                RhiInstallManifest.AddSharedFileOwner(installPath, "nvngx_dlssnr.dll", "Dlss5Tool");
            }
        }).ConfigureAwait(false);

        // Record DLSS5 Tool's deployed DLSS DLLs in rhi_install.txt
        // The addon file (renodx-dlss5.addon64) is added by InstallDlss5ToolAsync after this returns.
        var dlss5Files = new List<string>();
        if (cachedSr  != null) dlss5Files.Add("nvngx_dlss.dll");
        if (cachedRr  != null) dlss5Files.Add("nvngx_dlssd.dll");
        if (cachedFg  != null) dlss5Files.Add("nvngx_dlssg.dll");
        if (cachedNr  != null) dlss5Files.Add("nvngx_dlssnr.dll");
        Models.RhiInstallManifest.SetComponent(installPath, "Dlss5Tool", dlss5Files);

        // Re-detect DLSS so the card and Nvidia Profile section see the newly deployed DLLs.
        // Without this, DlssDetection still has the pre-install state and the profile panel shows "None".
        var newDetection = dlssSvc.Detect(installPath);
        if (newDetection.HasAny)
        {
            dlssSvc.RecordDlssFound(card.GameName);
            dlssSvc.RecordTrustedPath(card.GameName, newDetection);
        }
        _window.DispatcherQueue?.TryEnqueue(() =>
        {
            card.DlssDetection = newDetection;
            card.ApplyDlssDetection(newDetection);
            card.RefreshDlssVersions(dlssSvc);
        });
    }

    /// <summary>Deploys src → dest with sentinel backup. If dest exists, backs up the original. If dest doesn't exist, writes a 0-byte sentinel so uninstall knows to delete it entirely.</summary>
    private static void DeployWithSentinel(string src, string dest, string logCtx)
    {
        try
        {
            var sentinel = dest + ".original";
            if (File.Exists(dest))
            {
                if (!File.Exists(sentinel))
                    File.Copy(dest, sentinel); // backup game original
            }
            else
            {
                if (!File.Exists(sentinel))
                    File.WriteAllBytes(sentinel, Array.Empty<byte>()); // 0-byte sentinel — RHI placed this
            }
            File.Copy(src, dest, overwrite: true);
            CrashReporter.Log($"[{logCtx}] Deployed to '{dest}'");
        }
        catch (Exception ex) { CrashReporter.Log($"[{logCtx}] Failed '{dest}' — {ex.Message}"); }
    }

    /// <summary>
    /// Restores or deletes DLSS SR/RR/FG/NR DLLs deployed by UpgradeDlssDllsAsync,
    /// using the sentinel pattern: 0-byte sentinel = delete entirely, non-zero = restore original.
    /// </summary>
    private static void RestoreWithSentinel(string dest, string logCtx)
    {
        try
        {
            var sentinel = dest + ".original";
            if (!File.Exists(sentinel)) return; // not placed by RHI — leave untouched
            var info = new FileInfo(sentinel);
            if (info.Length == 0)
            {
                // RHI placed this from scratch — delete both
                try { if (File.Exists(dest)) File.Delete(dest); } catch (Exception ex) { CrashReporter.Log($"[DetailPanelBuilder.RestoreWithSentinel] Failed to delete '{dest}' — {ex.Message}"); }
                try { File.Delete(sentinel); } catch (Exception ex) { CrashReporter.Log($"[DetailPanelBuilder.RestoreWithSentinel] Failed to delete sentinel '{sentinel}' — {ex.Message}"); }
                CrashReporter.Log($"[{logCtx}] Deleted '{dest}' (RHI-placed)");
            }
            else
            {
                // Restore game original
                File.Copy(sentinel, dest, overwrite: true);
                File.Delete(sentinel);
                CrashReporter.Log($"[{logCtx}] Restored '{dest}' from backup");
            }
        }
        catch (Exception ex) { CrashReporter.Log($"[{logCtx}] Restore failed '{dest}' — {ex.Message}"); }
    }

    /// <summary>Restores all DLSS DLLs (SR/RR/FG/NR) deployed by UpgradeDlssDllsAsync using their sentinels.</summary>
    private static void RestoreDlssDllsWithSentinel(GameCardViewModel card, IDlssStreamlineService dlssSvc)
    {
        var installPath = card.InstallPath!;
        var det = card.DlssDetection;
        // Only restore each file if Dlss5Tool is the last owner
        if (RhiInstallManifest.RemoveSharedFileOwner(installPath, "nvngx_dlss.dll",   "Dlss5Tool"))
        {
            RestoreWithSentinel(det?.DlssPath   ?? Path.Combine(installPath, "nvngx_dlss.dll"),   "NeuralRendering.RestoreSR");
            // Also clean up OptiScaler's root copy if it was placed there (different path from plugin path)
            if (det?.DlssPath != null && !det.DlssPath.Equals(Path.Combine(installPath, "nvngx_dlss.dll"), StringComparison.OrdinalIgnoreCase))
                RestoreWithSentinel(Path.Combine(installPath, "nvngx_dlss.dll"), "NeuralRendering.RestoreSR.Root");
        }
        if (RhiInstallManifest.RemoveSharedFileOwner(installPath, "nvngx_dlssd.dll",  "Dlss5Tool"))
        {
            RestoreWithSentinel(det?.DlssdPath  ?? Path.Combine(installPath, "nvngx_dlssd.dll"),  "NeuralRendering.RestoreRR");
            if (det?.DlssdPath != null && !det.DlssdPath.Equals(Path.Combine(installPath, "nvngx_dlssd.dll"), StringComparison.OrdinalIgnoreCase))
                RestoreWithSentinel(Path.Combine(installPath, "nvngx_dlssd.dll"), "NeuralRendering.RestoreRR.Root");
        }
        if (RhiInstallManifest.RemoveSharedFileOwner(installPath, "nvngx_dlssg.dll",  "Dlss5Tool"))
        {
            RestoreWithSentinel(det?.DlssgPath  ?? Path.Combine(installPath, "nvngx_dlssg.dll"),  "NeuralRendering.RestoreFG");
            if (det?.DlssgPath != null && !det.DlssgPath.Equals(Path.Combine(installPath, "nvngx_dlssg.dll"), StringComparison.OrdinalIgnoreCase))
                RestoreWithSentinel(Path.Combine(installPath, "nvngx_dlssg.dll"), "NeuralRendering.RestoreFG.Root");
        }
        if (RhiInstallManifest.RemoveSharedFileOwner(installPath, "nvngx_dlssnr.dll", "Dlss5Tool"))
            RestoreWithSentinel(det?.DlssnrPath ?? Path.Combine(installPath, "nvngx_dlssnr.dll"), "NeuralRendering.RestoreNR");
    }

    private async Task InstallBridgeAddonAsync(
        GameCardViewModel card,
        Button statusBtn,
        IAddonPackService addonSvc,
        ComboBox? packVersionCombo = null)
    {
        var installPath = card.InstallPath!;
        var gameName    = card.GameName;
        var store       = card.Source ?? "";
        var rdx5Svc     = App.Services.GetRequiredService<Renodx5AddonService>();

        // Resolve requested Bridge version
        string? requestedBridgeVersion = null;
        if (packVersionCombo != null)
        {
            requestedBridgeVersion = await DispatchAsync<string?>(_window.DispatcherQueue!,
                () => packVersionCombo.SelectedItem as string).ConfigureAwait(false);
        }
        bool useLatestBridge = string.IsNullOrEmpty(requestedBridgeVersion) || requestedBridgeVersion.StartsWith("Latest");

        _window.DispatcherQueue?.TryEnqueue(() => statusBtn.Content = "Downloading DX11 Bridge...");

        // Resolve Bridge staged path — versioned or latest via AddonPackService
        string? bridgeSourcePath = null;
        if (!useLatestBridge && requestedBridgeVersion != null)
        {
            var staged = await rdx5Svc.EnsureVersionStagedAsync(Renodx5AddonService.BridgeSubDir, requestedBridgeVersion).ConfigureAwait(false);
            if (staged)
            {
                bridgeSourcePath = rdx5Svc.GetVersionedStagedFilePath(Renodx5AddonService.BridgeSubDir, requestedBridgeVersion);
                _window.ViewModel.SetNrPackVersion(gameName, requestedBridgeVersion, store);
                CrashReporter.Log($"[NeuralRendering] Using Bridge v{requestedBridgeVersion} from versioned staging");
            }
            else
            {
                CrashReporter.Log($"[NeuralRendering] Could not stage Bridge v{requestedBridgeVersion} — falling back to latest");
            }
        }

        if (bridgeSourcePath == null)
        {
            // Fall back to AddonPackService (always latest) — also force re-download to clear stale cache
            var entry = addonSvc.AvailablePacks.FirstOrDefault(p =>
                p.PackageName.Equals(BridgePackageName, StringComparison.OrdinalIgnoreCase));
            if (entry != null)
            {
                addonSvc.RemoveAddon(BridgePackageName);
                await addonSvc.DownloadAddonAsync(entry).ConfigureAwait(false);
            }
            bridgeSourcePath = FindStagedAddon(BridgePackageName, ".addon64");
            _window.ViewModel.SetNrPackVersion(gameName, null, store);
        }

        // Deploy — Bridge goes in the game root (next to ReShade / exe), not reshade-addons
        _window.DispatcherQueue?.TryEnqueue(() => statusBtn.Content = "Deploying DX11 Bridge...");
        await Task.Run(() =>
        {
            if (bridgeSourcePath == null || !File.Exists(bridgeSourcePath))
            {
                CrashReporter.Log($"[NeuralRendering] Bridge staging file not found");
                return;
            }
            var dest = Path.Combine(installPath, BridgeDeployFile);
            File.Copy(bridgeSourcePath, dest, overwrite: true);
            CrashReporter.Log($"[NeuralRendering] Deployed {BridgeDeployFile} to '{installPath}'");
        }).ConfigureAwait(false);
    }

    private async Task InstallShortFuseAsync(
        GameCardViewModel card,
        Button statusBtn,
        ComboBox addonVersionCombo,
        ComboBox nrVersionCombo,
        Renodx5AddonService rdx5Svc,
        IDlssStreamlineService dlssSvc)
    {
        var installPath = card.InstallPath!;
        var gameName    = card.GameName;
        var store       = card.Source ?? "";

        // Resolve requested addon version
        var requestedVersion = await DispatchAsync<string?>(_window.DispatcherQueue!,
            () => addonVersionCombo.SelectedItem as string).ConfigureAwait(false);
        bool useLatest = string.IsNullOrEmpty(requestedVersion) || requestedVersion.StartsWith("Latest");

        string sfSourcePath;
        if (useLatest)
        {
            _window.DispatcherQueue?.TryEnqueue(() => statusBtn.Content = "Staging ShortFuse...");
            await rdx5Svc.EnsureSfStagingAsync().ConfigureAwait(false);
            if (!rdx5Svc.IsSfStagingReady)
                throw new InvalidOperationException("ShortFuse staging not ready");
            sfSourcePath = rdx5Svc.SfStagedFilePath;
            _window.ViewModel.SetNrAddonVersion(gameName, null, store);
        }
        else
        {
            _window.DispatcherQueue?.TryEnqueue(() => statusBtn.Content = $"Staging ShortFuse v{requestedVersion}...");
            var staged = await rdx5Svc.EnsureVersionStagedAsync("dlsstool", requestedVersion!).ConfigureAwait(false);
            if (!staged)
            {
                CrashReporter.Log($"[NeuralRendering] Could not stage ShortFuse v{requestedVersion} — falling back to latest");
                await rdx5Svc.EnsureSfStagingAsync().ConfigureAwait(false);
                if (!rdx5Svc.IsSfStagingReady)
                    throw new InvalidOperationException("ShortFuse staging not ready");
                sfSourcePath = rdx5Svc.SfStagedFilePath;
                _window.ViewModel.SetNrAddonVersion(gameName, null, store);
            }
            else
            {
                sfSourcePath = rdx5Svc.GetVersionedStagedFilePath("dlsstool", requestedVersion!)!;
                _window.ViewModel.SetNrAddonVersion(gameName, requestedVersion, store);
            }
        }

        _window.DispatcherQueue?.TryEnqueue(() => statusBtn.Content = "Installing DLSS stack...");

        // Deploy the SF addon from the resolved source path
        try
        {
            var deployDir = ModInstallService.GetAddonDeployPath(installPath);
            Directory.CreateDirectory(deployDir);
            bool sfZzzMode = _window.ViewModel.GetSfZzzMode(gameName, store);
            await Task.Run(() =>
            {
                var normalDest = Path.Combine(deployDir, "renodx-dlss.addon64");
                var zzzDest    = Path.Combine(deployDir, Renodx5AddonService.SfZzzDeployFileName);
                File.Copy(sfSourcePath, normalDest, overwrite: true);
                CrashReporter.Log($"[NeuralRendering] Deployed renodx-dlss.addon64 (v{(useLatest ? "latest" : requestedVersion)}) to '{deployDir}'");
                if (sfZzzMode)
                {
                    if (File.Exists(zzzDest)) File.Delete(zzzDest);
                    File.Move(normalDest, zzzDest);
                    CrashReporter.Log($"[NeuralRendering] Renamed to {Renodx5AddonService.SfZzzDeployFileName} (zzz mode)");
                }
                // Remove DLSS5 Tool addon if present (mutual exclusivity)
                var dlss5InDeploy = Path.Combine(deployDir, "renodx-dlss5.addon64");
                if (File.Exists(dlss5InDeploy)) { File.Delete(dlss5InDeploy); CrashReporter.Log("[NeuralRendering] Removed renodx-dlss5.addon64 (mutual exclusivity)"); }
            }).ConfigureAwait(false);
        }
        catch (Exception ex) { CrashReporter.Log($"[NeuralRendering] SF addon deploy failed — {ex.Message}"); }

        // Co-deploy DLSS/Streamline DLLs using sentinel .original pattern
        var detection = dlssSvc.Detect(installPath);
        await rdx5Svc.InstallSfDllsOnlyAsync(installPath, detection.HasAny ? detection : null).ConfigureAwait(false);

        // Deploy NR DLL at requested version (or latest)
        _window.DispatcherQueue?.TryEnqueue(() => statusBtn.Content = "Deploying NR DLL...");
        var nrSelectedVersion = await DispatchAsync<string?>(_window.DispatcherQueue!,
            () => nrVersionCombo.SelectedItem as string).ConfigureAwait(false);
        bool nrUseLatest = string.IsNullOrEmpty(nrSelectedVersion) || nrSelectedVersion.StartsWith("Latest");
        var nrDestPath = Path.Combine(installPath, "nvngx_dlssnr.dll");
        try
        {
            string? cachedNr;
            if (nrUseLatest)
                cachedNr = await dlssSvc.EnsureNewestDlssnrCachedAsync().ConfigureAwait(false);
            else
            {
                var nrDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RHI", "DLSS-NR", nrSelectedVersion!);
                cachedNr = ResolveCachedNrDllPath(nrSelectedVersion!) ?? Path.Combine(nrDir, "nvngx_dlssnr.dll");
                if (!File.Exists(cachedNr))
                    cachedNr = await dlssSvc.EnsureSpecificDlssnrCachedAsync(nrSelectedVersion!).ConfigureAwait(false)
                            ?? await dlssSvc.EnsureNewestDlssnrCachedAsync().ConfigureAwait(false);
            }

            if (cachedNr != null)
            {
                var backup = nrDestPath + ".original";
                if (File.Exists(nrDestPath) && !File.Exists(backup))
                    File.Copy(nrDestPath, backup);   // back up pre-existing game file
                else if (!File.Exists(nrDestPath) && !File.Exists(backup))
                    File.WriteAllBytes(backup, Array.Empty<byte>()); // 0-byte sentinel — RHI placed this from scratch
                File.Copy(cachedNr, nrDestPath, overwrite: true);
                CrashReporter.Log($"[NeuralRendering] Deployed nvngx_dlssnr.dll (v{(nrUseLatest ? "latest" : nrSelectedVersion)}) to '{installPath}'");
            }
            else
                CrashReporter.Log($"[NeuralRendering] NR DLL not available — skipping");
        }
        catch (Exception ex) { CrashReporter.Log($"[NeuralRendering] NR DLL deploy failed — {ex.Message}"); }

        // Update DLSS detection cache
        var newDetection = dlssSvc.Detect(installPath);
        if (newDetection.HasAny)
        {
            dlssSvc.RecordDlssFound(card.GameName);
            dlssSvc.RecordTrustedPath(card.GameName, newDetection);
        }
        _window.DispatcherQueue?.TryEnqueue(() =>
        {
            card.DlssDetection = newDetection;
            card.ApplyDlssDetection(newDetection);
            card.RefreshDlssVersions(dlssSvc);
        });

        // Apply auto-config (rename ReShade, install UAL, write reshade.ini [INSTALL] keys)
        if (_window.ViewModel.GetSfAutoConfigEnabled(card.GameName, card.Source ?? ""))
        {
            _window.DispatcherQueue?.TryEnqueue(() => statusBtn.Content = "Configuring ReShade...");
            await _window.ViewModel.ApplySfAutoConfigAsync(card).ConfigureAwait(false);
        }
    }
}
