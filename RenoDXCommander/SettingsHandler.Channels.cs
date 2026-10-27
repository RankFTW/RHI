using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class SettingsHandler
{
    // ── DXVK Variant Selector ─────────────────────────────────────────────────

    /// <summary>
    /// Handles the DXVK variant ComboBox selection change.
    /// Persists the variant, clears the staging cache, and prompts about re-deployment.
    /// </summary>
    public async void DxvkVariantCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (ViewModel.Settings.IsLoadingSettings) return;
            if (sender is not ComboBox combo || combo.SelectedItem is not string selected) return;

            var newVariant = selected switch
            {
                var s when s.Contains("Stable", StringComparison.OrdinalIgnoreCase) => DxvkVariant.Stable,
                var s when s.Contains("Lilium", StringComparison.OrdinalIgnoreCase) => DxvkVariant.LiliumHdr,
                _ => DxvkVariant.Development,
            };

            var currentVariant = _dxvkService.SelectedVariant;
            if (newVariant == currentVariant) return;

            // Persist the variant preference
            ViewModel.Settings.DxvkVariant = newVariant switch
            {
                DxvkVariant.Stable => "Stable",
                DxvkVariant.LiliumHdr => "LiliumHdr",
                _ => "Development"
            };
            ViewModel.SaveSettingsPublic();

            // Update the service
            _dxvkService.SelectedVariant = newVariant;

            // Ensure the new variant's staging is ready (download if needed)
            _ = Task.Run(async () =>
            {
                try { await _dxvkService.EnsureStagingAsync(); }
                catch (Exception ex) { CrashReporter.Log($"[SettingsHandler.DxvkVariantCombo] Staging download failed — {ex.Message}"); }
            });

            // Auto-reinstall DXVK on all affected games (those without per-game override)
            var gamesWithDxvk = ViewModel.AllCards
                .Where(c => c.DxvkStatus == GameStatus.Installed || c.DxvkStatus == GameStatus.UpdateAvailable)
                .Where(c => ViewModel.GetDxvkVariantOverride(c.GameName, c.Source) == null) // Only games using global default
                .ToList();

            if (gamesWithDxvk.Count > 0)
            {
                foreach (var card in gamesWithDxvk)
                {
                    _ = ViewModel.InstallDxvkAsync(card);
                }
            }

            var variantLabel = newVariant switch
            {
                DxvkVariant.Stable => "Stable",
                DxvkVariant.LiliumHdr => "Lilium HDR",
                _ => "Development",
            };

            var dialog = new ContentDialog
            {
                Title = "DXVK Variant Changed",
                Content = $"DXVK variant changed to {variantLabel}."
                    + (gamesWithDxvk.Count > 0
                        ? $"\n\nSwitching {gamesWithDxvk.Count} game(s) to the {variantLabel} build."
                        : "\n\nNo games currently have DXVK installed."),
                CloseButtonText = "OK",
                XamlRoot = _window.Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            };
            await DialogService.ShowSafeAsync(dialog);
        }
        catch (Exception unhandledEx)
        {
            CrashReporter.Log($"[SettingsHandler.DxvkVariantCombo_SelectionChanged] Unhandled error — {unhandledEx.Message}\n{unhandledEx.StackTrace}");
        }
    }

    // ── ReShade Build Channel Selector ────────────────────────────────────────

    /// <summary>
    /// Handles the ReShade build channel ComboBox selection change.
    /// Persists the channel, clears the addon ReShade staging cache, downloads
    /// from the new source, and flags all installed ReShade games as UpdateAvailable.
    /// </summary>
    public async void ReShadeChannelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (ViewModel.Settings.IsLoadingSettings) return;
            if (sender is not ComboBox combo || combo.SelectedItem is not string selected) return;

            var newChannel = selected.Contains("Nightly", StringComparison.OrdinalIgnoreCase)
                ? "Nightly" : "Stable";

            if (string.Equals(newChannel, ViewModel.Settings.ReShadeChannel, StringComparison.OrdinalIgnoreCase))
                return;

            // Persist the channel preference
            ViewModel.Settings.ReShadeChannel = newChannel;
            ViewModel.SaveSettingsPublic();

            // Download from the new source and update Vulkan layer when done
            _ = Task.Run(async () =>
            {
                try
                {
                    // Ensure both variants are available
                    var stableTask = _reShadeUpdateService.EnsureLatestAsync();
                    var nightlyTask = _reShadeNightlyService.EnsureLatestAsync();
                    await Task.WhenAll(stableTask, nightlyTask);

                    // Update the global Vulkan layer DLLs if they exist
                    // Only update if no per-game Vulkan override is active
                    var hasVulkanOverride = ViewModel.AllCards
                        .Any(c => c.RequiresVulkanInstall
                            && ViewModel.GetReShadeChannelOverride(c.GameName, c.Source) != null);

                    if (!hasVulkanOverride)
                    {
                        try
                        {
                            var layerDir = VulkanLayerService.LayerDirectory;
                            var stagedPath64 = AuxInstallService.GetStagedPathForChannel(newChannel, false);
                            var stagedPath32 = AuxInstallService.GetStagedPathForChannel(newChannel, true);

                            // 64-bit
                            var layer64 = Path.Combine(layerDir, VulkanLayerService.LayerDllName);
                            if (File.Exists(stagedPath64)
                                && new FileInfo(stagedPath64).Length > AuxInstallService.MinReShadeSize
                                && File.Exists(layer64))
                            {
                                try
                                {
                                    File.Copy(stagedPath64, layer64, overwrite: true);
                                    CrashReporter.Log($"[SettingsHandler.ReShadeChannelCombo] Updated Vulkan layer 64-bit DLL to {newChannel} build");
                                }
                                catch (UnauthorizedAccessException)
                                {
                                    CrashReporter.Log("[SettingsHandler.ReShadeChannelCombo] Direct copy denied, attempting elevated copy...");
                                    try
                                    {
                                        await ElevatedFileCopyAsync(stagedPath64, layer64);
                                        CrashReporter.Log($"[SettingsHandler.ReShadeChannelCombo] Updated Vulkan layer 64-bit DLL via elevated copy to {newChannel} build");
                                    }
                                    catch (Exception elevEx)
                                    {
                                        CrashReporter.Log($"[SettingsHandler.ReShadeChannelCombo] Elevated copy failed — {elevEx.Message}");
                                    }
                                }
                                catch (IOException ioEx)
                                {
                                    CrashReporter.Log($"[SettingsHandler.ReShadeChannelCombo] Vulkan layer 64-bit copy failed (file locked?) — {ioEx.Message}");
                                }
                            }

                            // 32-bit
                            var layer32 = Path.Combine(layerDir, "ReShade32.dll");
                            if (File.Exists(stagedPath32)
                                && new FileInfo(stagedPath32).Length > AuxInstallService.MinReShadeSize
                                && File.Exists(layer32))
                            {
                                try
                                {
                                    File.Copy(stagedPath32, layer32, overwrite: true);
                                    CrashReporter.Log($"[SettingsHandler.ReShadeChannelCombo] Updated Vulkan layer 32-bit DLL to {newChannel} build");
                                }
                                catch (UnauthorizedAccessException)
                                {
                                    try
                                    {
                                        await ElevatedFileCopyAsync(stagedPath32, layer32);
                                        CrashReporter.Log($"[SettingsHandler.ReShadeChannelCombo] Updated Vulkan layer 32-bit DLL via elevated copy to {newChannel} build");
                                    }
                                    catch (Exception elevEx)
                                    {
                                        CrashReporter.Log($"[SettingsHandler.ReShadeChannelCombo] 32-bit elevated copy failed — {elevEx.Message}");
                                    }
                                }
                                catch (Exception ex32)
                                {
                                    CrashReporter.Log($"[SettingsHandler.ReShadeChannelCombo] 32-bit Vulkan layer copy failed — {ex32.Message}");
                                }
                            }
                        }
                        catch (UnauthorizedAccessException)
                        {
                            CrashReporter.Log("[SettingsHandler.ReShadeChannelCombo] Cannot update Vulkan layer — admin privileges required");
                        }
                        catch (Exception ex)
                        {
                            CrashReporter.Log($"[SettingsHandler.ReShadeChannelCombo] Failed to update Vulkan layer — {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[SettingsHandler.ReShadeChannelCombo] Background download failed — {ex.Message}");
                }
            });

            // Auto-reinstall ReShade on all affected games (those without a per-game override)
            // Games with a per-game override keep their override channel — only "Global" games switch.
            var gamesWithRs = ViewModel.AllCards
                .Where(c => c.RsStatus == GameStatus.Installed || c.RsStatus == GameStatus.UpdateAvailable)
                .Where(c => !c.RequiresVulkanInstall) // Vulkan handled above via layer copy
                .Where(c => ViewModel.GetReShadeChannelOverride(c.GameName, c.Source) == null) // Only games using global default
                .Where(c => !c.UseNormalReShade) // Normal ReShade games are unaffected
                .ToList();

            foreach (var card in gamesWithRs)
            {
                _ = ViewModel.InstallReShadeCommand.ExecuteAsync(card);
            }

            var totalCount = gamesWithRs.Count;
            var vulkanCount = ViewModel.AllCards.Count(c => c.RequiresVulkanInstall && c.IsRsInstalled);
            var channelLabel = string.Equals(newChannel, "Nightly", StringComparison.OrdinalIgnoreCase)
                ? "Nightly" : "Stable";

            var dialog = new ContentDialog
            {
                Title = "ReShade Build Channel Changed",
                Content = $"ReShade build channel changed to {channelLabel}.\n\n"
                    + (totalCount > 0
                        ? $"Switching {totalCount} game(s) to the {channelLabel} build."
                          + (vulkanCount > 0 ? $"\n{vulkanCount} Vulkan game(s) updated via global layer." : "")
                        : "No games currently have ReShade installed."),
                CloseButtonText = "OK",
                XamlRoot = _window.Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            };
            await DialogService.ShowSafeAsync(dialog);
        }
        catch (Exception unhandledEx)
        {
            CrashReporter.Log($"[SettingsHandler.ReShadeChannelCombo_SelectionChanged] Unhandled error — {unhandledEx.Message}\n{unhandledEx.StackTrace}");
        }
    }

    /// <summary>
    /// Copies a file using an elevated cmd.exe process (UAC prompt).
    /// Used when direct File.Copy fails due to permissions on C:\ProgramData\ReShade.
    /// </summary>
    private static async Task ElevatedFileCopyAsync(string source, string destination)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c copy /y \"{source}\" \"{destination}\"",
            Verb = "runas",
            UseShellExecute = true,
            CreateNoWindow = true,
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
        };
        using var proc = System.Diagnostics.Process.Start(psi);
        if (proc == null) throw new IOException("Failed to start elevated copy process");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await proc.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            proc.Kill();
            throw new IOException("Elevated copy timed out after 10 seconds");
        }
        if (proc.ExitCode != 0)
            throw new IOException($"Elevated copy exited with code {proc.ExitCode}");
    }
}
