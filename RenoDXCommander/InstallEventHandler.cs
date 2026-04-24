using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

/// <summary>
/// Encapsulates detail-panel install/uninstall button click handlers and related path-picking logic.
/// Extracted from MainWindow code-behind to reduce file size.
/// </summary>
public class InstallEventHandler
{
    private readonly MainWindow _window;
    private readonly Func<string?, Task<string?>> _pickFolderAsync;

    public InstallEventHandler(MainWindow window, Func<string?, Task<string?>> pickFolderAsync)
    {
        _window = window;
        _pickFolderAsync = pickFolderAsync;
    }

    private MainViewModel ViewModel => _window.ViewModel;

    public async void CombinedInstallButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not GameCardViewModel card) return;
        if (string.IsNullOrEmpty(card.InstallPath) || !System.IO.Directory.Exists(card.InstallPath))
        {
            var folder = await _pickFolderAsync(null);
            if (folder == null) return;
            card.InstallPath = folder;
            ViewModel.SaveLibraryPublic();
        }
        // Chain: RenoDX → RE Framework → ReShade (skip components that are N/A)
        if (card.Mod?.SnapshotUrl != null)
            await ViewModel.InstallModCommand.ExecuteAsync(card);
        if (card.RefRowVisibility == Visibility.Visible)
            await ViewModel.InstallREFrameworkCommand.ExecuteAsync(card);
        if (card.ReShadeRowVisibility == Visibility.Visible)
            await ViewModel.InstallReShadeCommand.ExecuteAsync(card);
    }

    public async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        // If this is an external-only game, open the external URL instead
        var checkCard = GetCardFromSender(sender);
        if (checkCard?.IsExternalOnly == true)
        {
            _window.ExternalLink_Click(sender, e);
            return;
        }
        if (sender is not Button btn || btn.Tag is not GameCardViewModel card) return;
        await EnsurePathAndInstall(card, () => ViewModel.InstallModCommand.ExecuteAsync(card));
    }

    public async void Install64Button_Click(object sender, RoutedEventArgs e)
    {
        var card = GetCardFromSender(sender);
        if (card == null) return;
        await EnsurePathAndInstall(card, () => ViewModel.InstallModCommand.ExecuteAsync(card));
    }

    public async void Install32Button_Click(object sender, RoutedEventArgs e)
    {
        var card = GetCardFromSender(sender);
        if (card == null) return;
        await EnsurePathAndInstall(card, () => ViewModel.InstallMod32Command.ExecuteAsync(card));
    }

    public async Task EnsurePathAndInstall(GameCardViewModel card, Func<Task> installAction)
    {
        if (string.IsNullOrEmpty(card.InstallPath) || !System.IO.Directory.Exists(card.InstallPath))
        {
            var folder = await _pickFolderAsync(null);
            if (folder == null) return;
            card.InstallPath = folder;
        }
        await installAction();
    }

    public void UninstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetCardFromSender(sender) is { } card)
            ViewModel.UninstallModCommand.Execute(card);
    }

    public async void InstallRsButton_Click(object sender, RoutedEventArgs e)
    {
        var card = (sender as FrameworkElement)?.Tag as GameCardViewModel;
        if (card == null) return;
        if (string.IsNullOrEmpty(card.InstallPath) || !System.IO.Directory.Exists(card.InstallPath))
        {
            var folder = await _pickFolderAsync(null);
            if (folder == null) return;
            card.InstallPath = folder;
            ViewModel.SaveLibraryPublic();
        }
        await ViewModel.InstallReShadeCommand.ExecuteAsync(card);
    }

    public void UninstallRsButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is GameCardViewModel card)
        {
            if (card.RequiresVulkanInstall)
                ViewModel.UninstallVulkanReShadeCommand.Execute(card);
            else
                ViewModel.UninstallReShadeCommand.Execute(card);
        }
    }

    public async void InstallUlButton_Click(object sender, RoutedEventArgs e)
    {
        var card = (sender as FrameworkElement)?.Tag as GameCardViewModel;
        if (card == null) return;
        if (string.IsNullOrEmpty(card.InstallPath) || !System.IO.Directory.Exists(card.InstallPath))
        {
            var folder = await _pickFolderAsync(null);
            if (folder == null) return;
            card.InstallPath = folder;
            ViewModel.SaveLibraryPublic();
        }
        await ViewModel.InstallUlAsync(card);
    }

    public void UninstallUlButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is GameCardViewModel card)
            ViewModel.UninstallUl(card);
    }

    public async void InstallDcButton_Click(object sender, RoutedEventArgs e)
    {
        var card = (sender as FrameworkElement)?.Tag as GameCardViewModel;
        if (card == null) return;
        if (string.IsNullOrEmpty(card.InstallPath) || !System.IO.Directory.Exists(card.InstallPath))
        {
            var folder = await _pickFolderAsync(null);
            if (folder == null) return;
            card.InstallPath = folder;
            ViewModel.SaveLibraryPublic();
        }
        await ViewModel.InstallDcAsync(card);
    }

    public void UninstallDcButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is GameCardViewModel card)
            ViewModel.UninstallDc(card);
    }

    public async void InstallOsButton_Click(object sender, RoutedEventArgs e)
    {
        var card = (sender as FrameworkElement)?.Tag as GameCardViewModel;
        if (card == null) return;
        if (string.IsNullOrEmpty(card.InstallPath) || !System.IO.Directory.Exists(card.InstallPath))
        {
            var folder = await _pickFolderAsync(null);
            if (folder == null) return;
            card.InstallPath = folder;
            ViewModel.SaveLibraryPublic();
        }

        // ── First-time OptiScaler warning ──────────────────────────────
        if (!ViewModel.Settings.OsFirstTimeWarningDismissed)
        {
            var xamlRoot = (sender as FrameworkElement)?.XamlRoot;
            if (xamlRoot != null)
            {
                var warningDialog = new ContentDialog
                {
                    Title = "⚠ OptiScaler Setup",
                    Content = "Before installing OptiScaler, please configure your GPU type and DLSS input settings in the OptiScaler Settings section on the Settings page.\n\nThis ensures OptiScaler is configured correctly for your hardware.",
                    PrimaryButtonText = "Continue",
                    CloseButtonText = "Cancel",
                    XamlRoot = xamlRoot,
                    RequestedTheme = ElementTheme.Dark,
                };

                var result = await DialogService.ShowSafeAsync(warningDialog);
                if (result != ContentDialogResult.Primary) return;

                ViewModel.Settings.OsFirstTimeWarningDismissed = true;
                ViewModel.SaveSettingsPublic();
            }
        }

        // ── Read GPU/DLSS settings from persisted preferences ──────────
        var gpuType = ViewModel.Settings.OsGpuType;
        var useDlssInputs = ViewModel.Settings.OsDlssInputs;

        card.OsIsInstalling = true;
        card.OsActionMessage = "Installing OptiScaler...";
        card.OsProgress = 0;
        try
        {
            await ViewModel.OptiScalerServiceInstance.InstallAsync(card,
                new Progress<(string message, double percent)>(p =>
                {
                    card.OsActionMessage = p.message;
                    card.OsProgress = p.percent;
                }),
                gpuType,
                useDlssInputs,
                ViewModel.Settings.OsHotkey);

            // ── PD-Upscaler REFramework swap for compatible RE Engine games ──
            if (ViewModel.Manifest?.PdUpscalerGames != null
                && ViewModel.Manifest.PdUpscalerGames.TryGetValue(card.GameName, out var pdArtifact)
                && File.Exists(Path.Combine(card.InstallPath, "dinput8.dll")))
            {
                try
                {
                    card.OsActionMessage = "Installing PD-Upscaler REFramework...";
                    await ViewModel.REFrameworkServiceInstance.InstallPdUpscalerAsync(
                        card.GameName, card.InstallPath, pdArtifact,
                        new Progress<(string message, double percent)>(p =>
                        {
                            card.OsActionMessage = p.message;
                            card.OsProgress = p.percent;
                        }));
                    card.RefInstalledVersion = "PD-Upscaler";
                    card.NotifyAll();
                }
                catch (Exception pdEx)
                {
                    // PD-Upscaler failure is non-fatal — OptiScaler is already installed
                    CrashReporter.Log($"[InstallEventHandler] PD-Upscaler install failed (non-fatal): {pdEx.Message}");
                }
            }

            card.OsActionMessage = "✅ OptiScaler installed!";
            card.NotifyAll();
            card.FadeMessage(m => card.OsActionMessage = m, card.OsActionMessage);
        }
        catch (Exception ex)
        {
            card.OsActionMessage = $"❌ Install failed: {ex.Message}";
        }
        finally
        {
            card.OsIsInstalling = false;
        }
    }

    public void UninstallOsButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not GameCardViewModel card) return;
        try
        {
            // ── Restore standard REFramework if pd-upscaler was swapped in ──
            if (ViewModel.Manifest?.PdUpscalerGames != null
                && ViewModel.Manifest.PdUpscalerGames.ContainsKey(card.GameName))
            {
                ViewModel.REFrameworkServiceInstance.RestoreStandardREFramework(
                    card.GameName, card.InstallPath);
                // Restore the version display to the standard REFramework version
                if (card.RefRecord != null)
                    card.RefInstalledVersion = card.RefRecord.InstalledVersion;
                card.NotifyAll();
            }

            ViewModel.OptiScalerServiceInstance.Uninstall(card);
            card.OsActionMessage = "✖ OptiScaler removed.";
            card.NotifyAll();
            card.FadeMessage(m => card.OsActionMessage = m, card.OsActionMessage);
        }
        catch (Exception ex)
        {
            card.OsActionMessage = $"❌ Uninstall failed: {ex.Message}";
        }
    }

    public void CopyOsIniButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not GameCardViewModel card) return;
        if (string.IsNullOrEmpty(card.InstallPath)) return;
        try
        {
            if (!File.Exists(Services.OptiScalerService.OsIniPath))
            {
                card.OsActionMessage = "❌ No OptiScaler.ini found in INIs folder.";
                return;
            }
            ViewModel.OptiScalerServiceInstance.CopyIniToGame(card);
            card.OsActionMessage = "✅ OptiScaler.ini copied to game folder.";
            card.FadeMessage(m => card.OsActionMessage = m, card.OsActionMessage);
        }
        catch (Exception ex)
        {
            card.OsActionMessage = $"❌ {ex.Message}";
        }
    }

    public async void InstallRefButton_Click(object sender, RoutedEventArgs e)
    {
        var card = (sender as FrameworkElement)?.Tag as GameCardViewModel;
        if (card == null) return;
        if (string.IsNullOrEmpty(card.InstallPath) || !System.IO.Directory.Exists(card.InstallPath))
        {
            var folder = await _pickFolderAsync(null);
            if (folder == null) return;
            card.InstallPath = folder;
            ViewModel.SaveLibraryPublic();
        }
        await ViewModel.InstallREFrameworkCommand.ExecuteAsync(card);
    }

    public void UninstallRefButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is GameCardViewModel card)
            ViewModel.UninstallREFrameworkCommand.Execute(card);
    }

    public async void InstallLumaButton_Click(object sender, RoutedEventArgs e)
    {
        var card = (sender as FrameworkElement)?.Tag as GameCardViewModel;
        if (card != null) await ViewModel.InstallLumaAsync(card);
    }

    public void UninstallLumaButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is GameCardViewModel card)
            ViewModel.UninstallLumaCommand.Execute(card);
    }

    public async void ChooseShadersButton_Click(object sender, RoutedEventArgs e)
    {
        var result = await ShaderPopupHelper.ShowAsync(
            _window.Content.XamlRoot,
            ViewModel.ShaderPackServiceInstance,
            ViewModel.Settings.SelectedShaderPacks,
            ShaderPopupHelper.PopupContext.Global);

        if (result != null)
        {
            ViewModel.Settings.SelectedShaderPacks = result;
            ViewModel.SaveSettingsPublic();
            ViewModel.DeployAllShaders();
        }
    }

    public async void ChooseAddonsButton_Click(object sender, RoutedEventArgs e)
    {
        var addonService = ViewModel.AddonPackServiceInstance;
        var currentSelection = addonService.DownloadedAddonNames.ToList();

        var result = await AddonPopupHelper.ShowAsync(
            _window.Content.XamlRoot,
            addonService,
            currentSelection,
            AddonPopupHelper.PopupContext.Global);

        if (result != null)
        {
            ViewModel.DeployAllAddons();
        }
    }

    public void LumaToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_window.DetailPanelBuilderInstance.CurrentDetailCard != null)
            ViewModel.ToggleLumaMode(_window.DetailPanelBuilderInstance.CurrentDetailCard);
    }

    public void SwitchToLumaButton_Click(object sender, RoutedEventArgs e)
    {
        var card = (sender as FrameworkElement)?.Tag as GameCardViewModel;
        if (card != null) ViewModel.ToggleLumaMode(card);
    }

    public void UeExtendedFlyoutItem_Click(object sender, RoutedEventArgs e)
    {
        var card = (sender as FrameworkElement)?.Tag as GameCardViewModel;
        if (card == null) return;

        ViewModel.ToggleUeExtended(card);

        // Directly update the badge text based on the new state
        string newLabel = card.UseUeExtended ? "UE Extended" : "Generic UE";
        _window.DetailGenericText.Text = newLabel;

        // Update the UE button styling
        if (card.UseUeExtended)
        {
            _window.DetailUeExtendedBtn.Background = Brush(ResourceKeys.AccentGreenBgBrush);
            _window.DetailUeExtendedBtn.Foreground = Brush(ResourceKeys.AccentGreenBrush);
            _window.DetailUeExtendedBtn.BorderBrush = Brush(ResourceKeys.AccentGreenBorderBrush);
        }
        else
        {
            _window.DetailUeExtendedBtn.Background = Brush(ResourceKeys.SurfaceOverlayBrush);
            _window.DetailUeExtendedBtn.Foreground = Brush(ResourceKeys.TextSecondaryBrush);
            _window.DetailUeExtendedBtn.BorderBrush = Brush(ResourceKeys.BorderStrongBrush);
        }

        // Update tooltip
        ToolTipService.SetToolTip(_window.DetailUeExtendedBtn,
            card.UseUeExtended ? "Disable UE Extended" : "Enable UE Extended");

        // Show inline message or warning dialog
        if (card.UseUeExtended)
        {
            _window.DetailRsMessage.Text = "⚡ UE-Extended enabled — check Discord to confirm this game is compatible.";
            _window.DetailRsMessage.Foreground = Brush(ResourceKeys.AccentPurpleBrush);
            _window.DetailRsMessage.Visibility = Visibility.Visible;
            // Show compatibility warning dialog
            _ = _window.ShowUeExtendedWarningAsync(card);
        }
        else
        {
            _window.DetailRsMessage.Text = "UE-Extended disabled.";
            _window.DetailRsMessage.Foreground = Brush(ResourceKeys.TextTertiaryBrush);
            _window.DetailRsMessage.Visibility = Visibility.Visible;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static GameCardViewModel? GetCardFromSender(object sender) => sender switch
    {
        Button btn          when btn.Tag  is GameCardViewModel c => c,
        MenuFlyoutItem item when item.Tag is GameCardViewModel c => c,
        _ => null
    };

    /// <summary>Looks up a SolidColorBrush from the merged theme resource dictionaries.</summary>
    private static SolidColorBrush Brush(string key) =>
        (SolidColorBrush)Application.Current.Resources[key];
}
