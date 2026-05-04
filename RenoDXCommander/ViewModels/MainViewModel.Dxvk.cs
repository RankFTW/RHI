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

        // ── DXVK warning (shown unless user has opted out via checkbox) ─────────
        if (xamlRoot != null && !_dxvkService.FirstTimeWarningAcknowledged)
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
                _dxvkService.FirstTimeWarningAcknowledged = true;
        }

        // ── Install ──────────────────────────────────────────────────────
        card.DxvkIsInstalling = true;
        card.DxvkActionMessage = "Installing DXVK...";
        card.DxvkProgress = 0;
        try
        {
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
        }
        catch (Exception ex)
        {
            card.DxvkActionMessage = $"❌ Install failed: {ex.Message}";
            _crashReporter.WriteCrashReport("InstallDxvk", ex, note: $"Game: {card.GameName}");
        }
        finally
        {
            card.DxvkIsInstalling = false;
        }
    }

    // ── DXVK Uninstall ────────────────────────────────────────────────────────────

    /// <summary>
    /// Uninstalls DXVK from the given game card.
    /// Removes deployed DLLs, restores backups, and cleans up the tracking record.
    /// </summary>
    public void UninstallDxvk(GameCardViewModel card)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return;

        card.DxvkIsInstalling = true;
        card.DxvkActionMessage = "Removing DXVK...";
        try
        {
            _dxvkService.Uninstall(card);
            card.DxvkActionMessage = "✖ DXVK removed.";
            card.NotifyAll();
            card.FadeMessage(m => card.DxvkActionMessage = m, card.DxvkActionMessage);
            SaveLibrary();
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
            await _dxvkService.UpdateAsync(card,
                new Progress<(string message, double percent)>(p =>
                {
                    card.DxvkActionMessage = p.message;
                    card.DxvkProgress = p.percent;
                }));

            card.DxvkActionMessage = "✅ DXVK updated!";
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
