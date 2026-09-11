using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Models;
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
    private readonly IOptiScalerService _optiScalerService;
    private readonly IREFrameworkService _reFrameworkService;
    private readonly IShaderPackService _shaderPackService;
    private readonly IDlssStreamlineService _dlssStreamlineService;

    public InstallEventHandler(MainWindow window, Func<string?, Task<string?>> pickFolderAsync)
    {
        _window = window;
        _pickFolderAsync = pickFolderAsync;
        _optiScalerService = App.Services.GetRequiredService<IOptiScalerService>();
        _reFrameworkService = App.Services.GetRequiredService<IREFrameworkService>();
        _shaderPackService = App.Services.GetRequiredService<IShaderPackService>();
        _dlssStreamlineService = App.Services.GetRequiredService<IDlssStreamlineService>();
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

        // If RTX HDR is active, open the RTX HDR configuration dialog
        if (checkCard?.IsRtxHdrEnabled == true)
        {
            _window.RtxHdrConfigButton_Click(sender, e);
            return;
        }

        if (sender is not Button btn || btn.Tag is not GameCardViewModel card) return;

        // Warn when installing RenoDX alongside an already-installed Luma mod
        if (card.IsLumaInstalled && !ViewModel.Settings.LumaRenodxCombinedWarningDismissed)
        {
            if (!await ShowLumaRenodxCombinedWarning(sender)) return;
        }

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
        if (!await ViewModel.CheckInstallWarningAsync(card.GameName, "optiscaler")) return;

        if (!ViewModel.Settings.OsFirstTimeWarningDismissed)
        {
            var xamlRoot = (sender as FrameworkElement)?.XamlRoot;
            if (xamlRoot != null)
            {
                var warningDialog = new ContentDialog
                {
                    Title = "⚠ OptiScaler Setup",
                    Content = "Before installing OptiScaler, please configure your GPU type and DLSS input (AMD/Intel only) settings in the OptiScaler Settings section on the Settings page.\n\nThis ensures OptiScaler is configured correctly for your hardware.",
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
        var osVariant = ViewModel.GetOsVariant(card.GameName, card.Source ?? "");

        card.OsIsInstalling = true;
        card.OsActionMessage = "Installing OptiScaler...";
        card.OsProgress = 0;
        try
        {
            await _optiScalerService.InstallAsync(card,
                new Progress<(string message, double percent)>(p =>
                {
                    card.OsActionMessage = p.message;
                    card.OsProgress = p.percent;
                }),
                gpuType,
                useDlssInputs,
                ViewModel.Settings.OsHotkey,
                osVariant);

            // ── PD-Upscaler REFramework swap for compatible RE Engine games ──
            if (ViewModel.Manifest?.PdUpscalerGames != null
                && ViewModel.Manifest.PdUpscalerGames.TryGetValue(card.GameName, out var pdArtifact)
                && File.Exists(Path.Combine(card.InstallPath, "dinput8.dll")))
            {
                try
                {
                    card.OsActionMessage = "Installing PD-Upscaler REFramework...";
                    await _reFrameworkService.InstallPdUpscalerAsync(
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
            card.OsStatus = GameStatus.Installed;
            card.NotifyAll();
            card.FadeMessage(m => card.OsActionMessage = m, card.OsActionMessage);

            // Clear the DLSS skip cache for this game — OptiScaler just deployed nvngx_dlss.dll
            // and/or Streamline, so the next Refresh must scan it rather than skipping it.
            _dlssStreamlineService.RecordDlssFound(card.GameName);

            // ── Post-install: Deploy Streamline and DLSS Enabler if pre-enabled ──
            if ((osVariant == "Nightly" || osVariant == "DlssNr") && !string.IsNullOrEmpty(card.InstallPath))
            {
                if (ViewModel.GetOsDeployStreamline(card.GameName, card.Source ?? ""))
                {
                    try
                    {
                        // Deploy directly to the correct version — no swap needed, no .original backups
                        var selectedSlVersion = ViewModel.GetOsStreamlineVersion(card.GameName, card.Source ?? "");
                        _optiScalerService.DeployStreamlineToGame(card.InstallPath, selectedSlVersion);
                    }
                    catch (Exception ex) { CrashReporter.Log($"[InstallEventHandler] Streamline post-install deploy failed — {ex.Message}"); }
                }
                if (ViewModel.GetOsDeployDlssEnabler(card.GameName, card.Source ?? ""))
                {
                    try
                    {
                        var dlssEnablerService = App.Services.GetRequiredService<DlssEnablerService>();
                        var optiScalerDir = Path.Combine(card.InstallPath, "OptiScaler");
                        _ = dlssEnablerService.InstallAsync(optiScalerDir);
                    }
                    catch (Exception ex) { CrashReporter.Log($"[InstallEventHandler] DLSS Enabler post-install deploy failed — {ex.Message}"); }
                }

                // Apply persisted FG settings to the newly deployed OptiScaler.ini
                var fgInput2 = ViewModel.GetOsFgInput(card.GameName, card.Source ?? "");
                var fgOutput2 = ViewModel.GetOsFgOutput(card.GameName, card.Source ?? "");
                var fgNvngx2 = ViewModel.GetOsFgNvngxReplacement(card.GameName, card.Source ?? "");
                if (fgInput2 != "auto" || fgOutput2 != "auto")
                    OptiScalerService.ApplyFgSettings(card.InstallPath, fgInput2, fgOutput2, fgNvngx2);
            }
        }
        catch (Exception ex)
        {
            card.OsActionMessage = $"❌ Install failed: {ex.Message}";
        }
        finally
        {
            card.OsIsInstalling = false;
            _window.DispatcherQueue?.TryEnqueue(() =>
            {
                card.NotifyAll();
                _window.PopulateDetailPanel(card);
            });
        }
    }

    public void UninstallOsButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not GameCardViewModel card) return;
        UninstallOptiScaler(card);
    }

    public void UninstallOptiScaler(GameCardViewModel card)
    {
        try
        {
            // ── Restore standard REFramework if pd-upscaler was swapped in ──
            if (ViewModel.Manifest?.PdUpscalerGames != null
                && ViewModel.Manifest.PdUpscalerGames.ContainsKey(card.GameName))
            {
                _reFrameworkService.RestoreStandardREFramework(
                    card.GameName, card.InstallPath);
                // Restore the version display to the standard REFramework version
                if (card.RefRecord != null)
                    card.RefInstalledVersion = card.RefRecord.InstalledVersion;
                card.NotifyAll();
            }

            _optiScalerService.Uninstall(card);

            // Clear all per-game OptiScaler cog settings so they reset to defaults on next open
            try
            {
                var gn = card.GameName; var st = card.Source ?? "";
                // Remove Engine.ini keys written by cog settings (UE games)
                if (!string.IsNullOrEmpty(card.InstallPath))
                {
                    if (ViewModel.GetOsDilatedMotionVectorsOff(gn, st))
                        try { AuxInstallService.RemoveEngineIniCustomKeys(card.InstallPath, new[] { "r.NGX.DLSS.DilateMotionVectors", "r.Streamline.DilateMotionVectors" }, card.EngineIniProjectOverride, gn, card.Source); } catch { }
                    var fsrFix = ViewModel.GetOsFsrCrashFix(gn, st);
                    if (!string.IsNullOrEmpty(fsrFix) && fsrFix != "None")
                        try { AuxInstallService.RemoveEngineIniCustomKeys(card.InstallPath, new[] { "r.FidelityFX.FSR2.UseNativeDX12", "r.FidelityFX.FSR3.UseNativeDX12", "r.FidelityFX.FSR3.UseRHI" }, card.EngineIniProjectOverride, gn, card.Source); } catch { }
                    if (ViewModel.GetOsFsrFgSwapchain(gn, st))
                        try { AuxInstallService.RemoveEngineIniCustomKeys(card.InstallPath, new[] { "r.FidelityFX.FI.OverrideSwapChainDX12" }, card.EngineIniProjectOverride, gn, card.Source); } catch { }
                    if (ViewModel.GetOsUpscalerPlugin(gn, st))
                        try { AuxInstallService.RemoveEngineIniCustomKeys(card.InstallPath, new[] { "r.AntiAliasingMethod", "r.TemporalAA.Upscaler" }, card.EngineIniProjectOverride, gn, card.Source); } catch { }
                }
                // Clear persisted per-game settings
                ViewModel.SetOsDeployStreamline(gn, false, st);
                ViewModel.SetOsDeployDlssEnabler(gn, false, st);
                ViewModel.SetOsFgInput(gn, null, st);
                ViewModel.SetOsFgOutput(gn, null, st);
                ViewModel.SetOsFgNvngxReplacement(gn, null, st);
                ViewModel.SetOsDilatedMotionVectorsOff(gn, false, st);
                ViewModel.SetOsFsrCrashFix(gn, null, st);
                ViewModel.SetOsFsrFgSwapchain(gn, false, st);
                ViewModel.SetOsUpscalerPlugin(gn, false, st);
                ViewModel.SetOsStreamlineVersion(gn, null, st);
            }
            catch (Exception cleanEx) { CrashReporter.Log($"[InstallEventHandler.UninstallOptiScaler] Settings cleanup failed — {cleanEx.Message}"); }

            card.OsActionMessage = "✖ OptiScaler removed.";
            card.OsStatus = GameStatus.Available;
            card.NotifyAll();
            card.FadeMessage(m => card.OsActionMessage = m, card.OsActionMessage);
        }
        catch (Exception ex)
        {
            card.OsActionMessage = $"❌ Uninstall failed: {ex.Message}";
        }

        _window.DispatcherQueue?.TryEnqueue(() =>
        {
            card.NotifyAll();
            _window.PopulateDetailPanel(card);
        });
    }

    public void CopyOsIniButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not GameCardViewModel card) return;
        if (string.IsNullOrEmpty(card.InstallPath)) return;
        try
        {
            _optiScalerService.CopyIniToGame(card, ViewModel.Settings.OsHotkey);
            // Apply persisted FG settings after copying the INI
            var fgInput = ViewModel.GetOsFgInput(card.GameName, card.Source ?? "");
            var fgOutput = ViewModel.GetOsFgOutput(card.GameName, card.Source ?? "");
            var fgNvngx = ViewModel.GetOsFgNvngxReplacement(card.GameName, card.Source ?? "");
            OptiScalerService.ApplyFgSettings(card.InstallPath, fgInput, fgOutput, fgNvngx);
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
        if (card == null) return;

        // Warn when installing Luma alongside an already-installed RenoDX mod
        if (card.IsRdxInstalled && !ViewModel.Settings.LumaRenodxCombinedWarningDismissed)
        {
            if (!await ShowLumaRenodxCombinedWarning(sender)) return;
        }

        await ViewModel.InstallLumaAsync(card);
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
            _shaderPackService,
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
        {
            var card = _window.DetailPanelBuilderInstance.CurrentDetailCard;
            ViewModel.ToggleLumaMode(card);
            // Rebuild detail panel to update author badges
            _window.PopulateDetailPanel(card);
            _window.BuildOverridesPanel(card);
        }
    }

    public void SwitchToLumaButton_Click(object sender, RoutedEventArgs e)
    {
        var card = (sender as FrameworkElement)?.Tag as GameCardViewModel;
        if (card != null)
        {
            ViewModel.ToggleLumaMode(card);
            // Rebuild detail panel to update author badges
            _window.PopulateDetailPanel(card);
            _window.BuildOverridesPanel(card);
        }
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
            // Show compatibility warning dialog
            _ = _window.ShowUeExtendedWarningAsync(card);
            // Force a full panel rebuild to show UE-Extended state
            _window.PopulateDetailPanel(card);
            _window.BuildOverridesPanel(card);
        }
        else
        {
            // Force a full panel rebuild to restore the correct mod info (name, author, badges)
            _window.PopulateDetailPanel(card);
            _window.BuildOverridesPanel(card);
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

    /// <summary>
    /// Shows the "Installing both RenoDX and Luma" compatibility warning dialog.
    /// Returns true if the user chose to continue, false if cancelled.
    /// Persists dismissal if the user checks "Don't show again".
    /// </summary>
    private async Task<bool> ShowLumaRenodxCombinedWarning(object sender)
    {
        var xamlRoot = (sender as FrameworkElement)?.XamlRoot ?? _window.Content.XamlRoot;
        if (xamlRoot == null) return true;

        var dontShowCheck = new CheckBox
        {
            Content = "Don't show this again",
            FontSize = 12,
            Margin = new Thickness(0, 12, 0, 0),
        };

        var messageText = new TextBlock
        {
            Text = "Heads up — you're installing both RenoDX and Luma on this game.\n\n" +
                   "There's no guarantee they'll work well together. If you're using RenoDX for HDR and just want Luma for DLAA, make sure to disable HDR in the Luma mod settings to avoid conflicts.\n\n" +
                   "If something doesn't look right, uninstalling one of them is the first thing to try. We can't offer support for issues that come from running both together.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            LineHeight = 22,
            Foreground = Brush(ResourceKeys.TextPrimaryBrush),
        };

        var content = new StackPanel { Spacing = 4 };
        content.Children.Add(messageText);
        content.Children.Add(dontShowCheck);

        var dialog = new ContentDialog
        {
            Title = "Installing both RenoDX and Luma",
            Content = content,
            PrimaryButtonText = "Continue",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };

        var result = await DialogService.ShowSafeAsync(dialog);

        if (dontShowCheck.IsChecked == true)
        {
            ViewModel.Settings.LumaRenodxCombinedWarningDismissed = true;
            ViewModel.SaveSettingsPublic();
        }

        return result == ContentDialogResult.Primary;
    }
}
