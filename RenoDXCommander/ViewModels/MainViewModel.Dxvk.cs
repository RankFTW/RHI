using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.ViewModels;

/// <summary>
/// DXVK install, uninstall, update, and configuration command handlers.
/// Requirements: 10.1, 10.2, 10.3, 10.4, 9.3, 9.4, 22.1
/// </summary>
public partial class MainViewModel
{
    // ── DXVK Install ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Installs DXVK for the given game card.
    /// Shows a first-time warning dialog if not yet acknowledged this session.
    /// Sets <c>DxvkIsInstalling</c> during the operation to disable controls.
    /// </summary>
    public async Task InstallDxvkAsync(GameCardViewModel card, Microsoft.UI.Xaml.XamlRoot? xamlRoot = null)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return;

        // Check for manifest-driven install warning
        if (!await CheckInstallWarningAsync(card.GameName, "dxvk")) return;

        // ── DXVK warning (shown unless user has opted out via checkbox) ─────────
        if (xamlRoot != null && !_settingsViewModel.DxvkWarningDismissed)
        {
            var dontShowAgain = new CheckBox
            {
                Content = "Don't show this warning again",
                Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
                Margin = new Microsoft.UI.Xaml.Thickness(0, 8, 0, 0),
            };

            var contentPanel = new StackPanel();
            contentPanel.Children.Add(new TextBlock
            {
                Text = "⚠ ADVANCED FEATURE — USE AT YOUR OWN RISK\n\n"
                    + "DXVK is an unofficial DirectX-to-Vulkan translation layer.\n"
                    + "No support will be provided if a game is not compatible.\n\n"
                    + "WHO SHOULD USE THIS:\n"
                    + "• Primarily benefits older DX8/DX9 games (e.g. FFXIV, Morrowind)\n"
                    + "• Enables ReShade compute shaders on games that don't support them natively\n"
                    + "• Can reduce CPU-bound stuttering in older titles\n\n"
                    + "IMPORTANT WARNINGS:\n"
                    + "• Anti-cheat games may ban players using DXVK\n"
                    + "• Game overlays (Steam, NVIDIA, RTSS) may conflict or stop working\n"
                    + "• Exclusive fullscreen is blocked — use borderless windowed\n"
                    + "• First launch will be slow due to shader compilation (improves on subsequent runs)\n"
                    + "• Some games may crash or have graphical glitches with DXVK\n\n"
                    + "Do you want to continue?",
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
                FontSize = 13,
            });
            contentPanel.Children.Add(dontShowAgain);

            var warningDialog = new ContentDialog
            {
                Title = "⚠ DXVK Warning",
                Content = contentPanel,
                PrimaryButtonText = "Continue",
                CloseButtonText = "Cancel",
                XamlRoot = xamlRoot,
                RequestedTheme = Microsoft.UI.Xaml.ElementTheme.Dark,
            };

            var result = await DialogService.ShowSafeAsync(warningDialog);
            if (result != ContentDialogResult.Primary) return;

            if (dontShowAgain.IsChecked == true)
            {
                _settingsViewModel.DxvkWarningDismissed = true;
                SaveSettingsPublic();
            }
        }

        // ── Install ──────────────────────────────────────────────────────
        // Resolve the per-game DXVK variant and set it on the service before install
        var resolvedVariant = ResolveDxvkVariant(card.GameName, card.Source ?? "");
        var savedVariant = _dxvkService.SelectedVariant;
        _dxvkService.SelectedVariant = resolvedVariant;
        _dxvkService.LiliumPresetIndex = GetLiliumPreset(card.GameName, card.Source ?? "");

        card.DxvkIsInstalling = true;
        card.DxvkActionMessage = "Installing DXVK...";
        card.DxvkProgress = 0;
        try
        {
            // Ensure the resolved variant's staging is ready
            if (!_dxvkService.IsStagingReady)
                await _dxvkService.EnsureStagingAsync();

            await _dxvkService.InstallAsync(card,
                new Progress<(string message, double percent)>(p =>
                {
                    card.DxvkActionMessage = p.message;
                    card.DxvkProgress = p.percent;
                }));

            card.DxvkActionMessage = "✅ DXVK installed!";
            card.NotifyAll();
            card.FadeMessage(m => card.DxvkActionMessage = m, card.DxvkActionMessage);
            SaveLibrary();

            // Persist Vulkan rendering path if direct DX9 mode switched the game to Vulkan
            if (card.DxvkRecord?.InstalledDlls.Contains("d3d9.dll") == true && card.VulkanRenderingPath == "Vulkan")
                SetVulkanRenderingPath(card.GameName, "Vulkan", card.Source ?? "");

            // Deploy shaders — DXVK sets up a Vulkan ReShade environment so the
            // reshade-shaders folder is needed even when ReShade wasn't installed before.
            if (!string.IsNullOrEmpty(card.InstallPath))
                DeployShadersForCard(card.GameName);

            // Rebuild the detail panel — DXVK install changes GraphicsApi, RsStatus, and badge.
            // DetailPanelBuilder is imperative so NotifyAll alone won't update it.
            RequestDetailPanelRebuild?.Invoke(card);
        }
        catch (Exception ex)
        {
            card.DxvkActionMessage = $"❌ Install failed: {ex.Message}";
            _crashReporter.WriteCrashReport("InstallDxvk", ex, note: $"Game: {card.GameName}");
        }
        finally
        {
            card.DxvkIsInstalling = false;
            _dxvkService.SelectedVariant = savedVariant;
        }
    }

    // ── DXVK Uninstall ────────────────────────────────────────────────────────────

    /// <summary>
    /// Uninstalls DXVK from the given game card.
    /// Removes deployed DLLs, restores backups, and cleans up the tracking record.
    /// </summary>
    public async Task UninstallDxvkAsync(GameCardViewModel card)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return;

        card.DxvkIsInstalling = true;
        card.DxvkActionMessage = "Removing DXVK...";
        try
        {
            await _dxvkService.UninstallAsync(card);
            
            // Clear persisted Vulkan rendering path — Lilium HDR uninstall resets to DirectX
            SetVulkanRenderingPath(card.GameName, "DirectX", card.Source ?? "");

            // Update API cache — DXVK set it to Vulkan, now it needs to go back to DX9
            // so the next launch doesn't show a phantom VLK badge.
            if (!string.IsNullOrEmpty(card.InstallPath))
            {
                var dx9Set = new System.Collections.Generic.HashSet<GraphicsApiType>(card.DetectedApis);
                dx9Set.Remove(GraphicsApiType.Vulkan);
                if (dx9Set.Count == 0) dx9Set.Add(GraphicsApiType.DirectX9);
                CacheGameApi(card.InstallPath, GraphicsApiType.DirectX9, dx9Set);
                SaveGameApiCache();
            }
            
            card.DxvkActionMessage = "✖ DXVK removed.";
            card.NotifyAll();
            card.FadeMessage(m => card.DxvkActionMessage = m, card.DxvkActionMessage);
            SaveLibrary();

            // Rebuild the detail panel — uninstall reverts GraphicsApi and RS state.
            RequestDetailPanelRebuild?.Invoke(card);
        }
        catch (Exception ex)
        {
            card.DxvkActionMessage = $"❌ Uninstall failed: {ex.Message}";
            _crashReporter.WriteCrashReport("UninstallDxvk", ex, note: $"Game: {card.GameName}");
        }
        finally
        {
            card.DxvkIsInstalling = false;
        }
    }

    // Keep sync wrapper for callers that pass it as a delegate (bitness change handler etc.)
    public void UninstallDxvk(GameCardViewModel card) => _ = UninstallDxvkAsync(card);

    // ── DXVK Update ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Updates DXVK for the given game card.
    /// Re-stages the latest release if needed and re-deploys DLLs.
    /// </summary>
    public async Task UpdateDxvkAsync(GameCardViewModel card)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return;

        card.DxvkIsInstalling = true;
        card.DxvkActionMessage = "Updating DXVK...";
        card.DxvkProgress = 0;
        try
        {
            // Resolve per-game variant and switch before update
            var resolvedVariant = ResolveDxvkVariant(card.GameName, card.Source ?? "");
            var savedVariant = _dxvkService.SelectedVariant;
            _dxvkService.SelectedVariant = resolvedVariant;

            await _dxvkService.EnsureStagingAsync(new Progress<(string message, double percent)>(p =>
            {
                card.DxvkActionMessage = p.message;
                card.DxvkProgress = p.percent;
            }));

            await _dxvkService.UpdateAsync(card,
                new Progress<(string message, double percent)>(p =>
                {
                    card.DxvkActionMessage = p.message;
                    card.DxvkProgress = p.percent;
                }));

            _dxvkService.SelectedVariant = savedVariant;

            card.DxvkActionMessage = "✅ DXVK updated!";
            card.DxvkStatus = GameStatus.Installed;
            card.NotifyAll();
            card.FadeMessage(m => card.DxvkActionMessage = m, card.DxvkActionMessage);
            SaveLibrary();
        }
        catch (Exception ex)
        {
            card.DxvkActionMessage = $"❌ Update failed: {ex.Message}";
            _crashReporter.WriteCrashReport("UpdateDxvk", ex, note: $"Game: {card.GameName}");
        }
        finally
        {
            card.DxvkIsInstalling = false;
        }
    }

    // ── DXVK Copy dxvk.conf ───────────────────────────────────────────────────────

    /// <summary>
    /// Copies the dxvk.conf template to the game directory.
    /// </summary>
    public void CopyDxvkConf(GameCardViewModel card)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return;
        try
        {
            _dxvkService.CopyConfToGame(card);
            card.DxvkActionMessage = "✅ dxvk.conf copied to game folder.";
            card.FadeMessage(m => card.DxvkActionMessage = m, card.DxvkActionMessage);
        }
        catch (Exception ex)
        {
            card.DxvkActionMessage = $"❌ {ex.Message}";
        }
    }

    // ── DXVK Toggle Handler ───────────────────────────────────────────────────────

    /// <summary>
    /// Handles the DxvkEnabled toggle change.
    /// When toggled ON, triggers the DXVK install flow.
    /// When toggled OFF, triggers the DXVK uninstall flow.
    /// </summary>
    public async Task HandleDxvkToggleAsync(GameCardViewModel card, bool enabled, Microsoft.UI.Xaml.XamlRoot? xamlRoot = null)
    {
        card.DxvkEnabled = enabled;

        if (enabled)
        {
            await InstallDxvkAsync(card, xamlRoot);

            // If install failed (status didn't change to Installed), revert the toggle
            if (card.DxvkStatus != GameStatus.Installed && card.DxvkStatus != GameStatus.UpdateAvailable)
            {
                card.DxvkEnabled = false;
            }
        }
        else
        {
            UninstallDxvk(card);

            // Re-resolve Graphics API from manifest/user overrides after uninstall.
            // The DxvkService sets a hardcoded DX9 default for Lilium HDR uninstall,
            // but the proper value may differ based on manifest overrides.
            if (!string.IsNullOrEmpty(card.InstallPath))
            {
                card.DetectedApis = _DetectAllApisForCard(card.InstallPath, card.GameName, card.Source);
                card.IsDualApiGame = GraphicsApiDetector.IsDualApi(card.DetectedApis);
                card.GraphicsApi = DetectGraphicsApi(card.InstallPath, EngineType.Unknown, card.GameName, card.Source);
            }

            // Deploy shaders after ReShade is restored as DX proxy.
            // The DxvkService reinstalls ReShade but can't resolve shader packs,
            // so we deploy them here where we have access to the shader resolver.
            if (card.RsStatus == GameStatus.Installed && !string.IsNullOrEmpty(card.InstallPath))
            {
                DeployShadersForCard(card.GameName);
            }
        }

        SaveLibrary();

        // Rebuild the detail/overrides panel so the Update Inclusion section
        // picks up the new DxvkEnabled state immediately.
        card.NotifyAll();
    }
}
