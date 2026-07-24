// MainWindow.Events.Install.cs — Update All, DXVK, DOF Fix, engine badge, game launch, and scroll restore handlers.

using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    // ── Update All handlers ──────────────────────────────────────────────────

    private async void UpdateAllButton_Click(object sender, RoutedEventArgs e)
    {
        // Build the progress dialog
        var statusText = new TextBlock
        {
            Text = "Preparing...",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
        };
        var progressBar = new ProgressBar
        {
            IsIndeterminate = true,
            Height = 3,
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 8, 0, 0),
        };
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(statusText);
        panel.Children.Add(progressBar);

        var dialog = new ContentDialog
        {
            Title = "Updating All Components",
            Content = panel,
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };

        // Show dialog non-blocking (it stays open while updates run)
        var dialogTask = DialogService.ShowSafeAsync(dialog);

        try
        {
            if (!ViewModel.Settings.GlobalSkipRsUpdates)
            {
                statusText.Text = "Updating ReShade...";
                await ViewModel.UpdateAllReShadeAsync();
            }
            if (!ViewModel.Settings.GlobalSkipRdxUpdates)
            {
                statusText.Text = "Updating RenoDX...";
                await ViewModel.UpdateAllRenoDxAsync();
            }
            if (!ViewModel.Settings.GlobalSkipUlUpdates)
            {
                statusText.Text = "Updating ReLimiter...";
                await ViewModel.UpdateAllUlAsync();
            }
            if (!ViewModel.Settings.GlobalSkipDcUpdates)
            {
                statusText.Text = "Updating Display Commander...";
                await ViewModel.UpdateAllDcAsync();
            }
            if (!ViewModel.Settings.GlobalSkipOsUpdates)
            {
                statusText.Text = "Updating OptiScaler...";
                await ViewModel.UpdateAllOsAsync();
            }
            if (!ViewModel.Settings.GlobalSkipRefUpdates)
            {
                statusText.Text = "Updating RE Framework...";
                await ViewModel.UpdateAllRefAsync();
            }
            statusText.Text = "Updating DXVK...";
            await ViewModel.UpdateAllDxvkAsync();
            statusText.Text = "Updating Luma...";
            await ViewModel.UpdateAllLumaAsync();
            statusText.Text = "Updating DOF Fix...";
            await ViewModel.UpdateAllDofFixAsync();
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainWindow.UpdateAllButton_Click] Error during Update All — {ex.Message}");
        }

        // Close the dialog
        dialog.Hide();
        ViewModel.NotifyUpdateButtonChanged();
    }

    private async void UpdateAllRenoDx_Click(object sender, RoutedEventArgs e)
        => await ViewModel.UpdateAllRenoDxAsync();

    private async void UpdateAllReShade_Click(object sender, RoutedEventArgs e)
        => await ViewModel.UpdateAllReShadeAsync();

    internal void InstallUlButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.InstallUlButton_Click(sender, e);

    internal void UninstallUlButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.UninstallUlButton_Click(sender, e);

    internal void InstallDcButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.InstallDcButton_Click(sender, e);

    internal void UninstallDcButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.UninstallDcButton_Click(sender, e);

    internal void InstallOsButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.InstallOsButton_Click(sender, e);

    internal void UninstallOsButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.UninstallOsButton_Click(sender, e);

    internal void InstallRefButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.InstallRefButton_Click(sender, e);

    internal void UninstallRefButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.UninstallRefButton_Click(sender, e);

    private void UlIniButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;
        if (string.IsNullOrEmpty(card.InstallPath)) return;
        try
        {
            AuxInstallService.CopyUlIni(card.InstallPath);
            card.UlActionMessage = "✅ relimiter.ini copied to game folder.";
        }
        catch (Exception ex)
        {
            card.UlActionMessage = $"❌ {ex.Message}";
        }
    }

    private void DcIniButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;
        if (string.IsNullOrEmpty(card.InstallPath)) return;
        try
        {
            AuxInstallService.CopyDcIni(card.InstallPath);
            card.DcActionMessage = "✅ DisplayCommander.ini copied to game folder.";
            card.FadeMessage(m => card.DcActionMessage = m, card.DcActionMessage);
        }
        catch (Exception ex)
        {
            card.DcActionMessage = $"❌ {ex.Message}";
        }
    }

    private void CopyOsIniButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.CopyOsIniButton_Click(sender, e);

    // ── DXVK event handlers ──────────────────────────────────────────────────

    private async void InstallDxvkButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;
        if (card.DxvkIsInstalling) return;
        if (card.DxvkStatus == GameStatus.UpdateAvailable)
            await ViewModel.UpdateDxvkAsync(card);
        else if (card.DxvkStatus == GameStatus.Installed)
            await ViewModel.InstallDxvkAsync(card, Content.XamlRoot); // reinstall
        else
            await ViewModel.InstallDxvkAsync(card, Content.XamlRoot);
    }

    private void UninstallDxvkButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;
        ViewModel.UninstallDxvk(card);
    }

    private void CopyDxvkConfButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;
        ViewModel.CopyDxvkConf(card);
    }

    private async void DxvkInfoButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;

        // Build DXVK info content with game-specific notes from manifest
        var content = "DXVK translates DirectX 8/9/10/11 API calls into Vulkan.\n\n"
            + "Benefits:\n"
            + "• Enables ReShade compute shaders on older DX games\n"
            + "• May improve performance and reduce shader stutter\n"
            + "• Enables HDR output via dxvk.conf\n"
            + "• Borderless fullscreen recommended over exclusive fullscreen\n\n"
            + "⚠ Anti-cheat games may ban players using DXVK.\n"
            + "⚠ Game overlays (Steam, NVIDIA, RTSS) may conflict.";

        // Append game-specific notes from manifest
        var manifest = ViewModel.Manifest;
        if (manifest?.DxvkGameNotes != null
            && manifest.DxvkGameNotes.TryGetValue(card.GameName, out var noteEntry)
            && !string.IsNullOrWhiteSpace(noteEntry.Notes))
        {
            content += $"\n\n── Game Notes ──\n{noteEntry.Notes}";
        }

        var dialog = new ContentDialog
        {
            Title = "ℹ DXVK Info",
            Content = new TextBlock
            {
                Text = content,
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                FontSize = 13,
                Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            },
            CloseButtonText = "OK",
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        await DialogService.ShowSafeAsync(dialog);
    }

    private void DxvkVariantCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => _settingsHandler.DxvkVariantCombo_SelectionChanged(sender, e);

    private void ReShadeChannelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => _settingsHandler.ReShadeChannelCombo_SelectionChanged(sender, e);

    private async void DetailOsStatus_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var card = ViewModel.SelectedGame;
        if (card == null) return;

        if (card.IsOsInstalled)
            await Windows.System.Launcher.LaunchUriAsync(
                new Uri("https://github.com/optiscaler/OptiScaler/wiki"));
    }

    private async void DetailUlStatus_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var card = ViewModel.SelectedGame;
        if (card == null) return;

        if (card.UlStatus == Models.GameStatus.UpdateAvailable && ViewModel.LatestUlReleasePageUrl != null)
            await Windows.System.Launcher.LaunchUriAsync(new Uri(ViewModel.LatestUlReleasePageUrl));
        else if (card.UlStatus == Models.GameStatus.Installed || card.UlStatus == Models.GameStatus.UpdateAvailable)
            await Windows.System.Launcher.LaunchUriAsync(
                new Uri("https://github.com/RankFTW/ReLimiter?tab=readme-ov-file#relimiter--comprehensive-feature-guide"));
    }

    private async void DetailDcStatus_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var card = ViewModel.SelectedGame;
        if (card == null) return;

        if (card.IsDcInstalled)
            await Windows.System.Launcher.LaunchUriAsync(
                new Uri("https://github.com/pmnoxx/display-commander/releases/tag/latest_build"));
    }

    private async void DetailLumaStatus_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var card = ViewModel.SelectedGame;
        if (card == null) return;

        if (card.IsLumaInstalled)
            await Windows.System.Launcher.LaunchUriAsync(
                new Uri("https://github.com/Filoppi/Luma-Framework/releases"));
    }

    private async void DetailDxvkStatus_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var card = ViewModel.SelectedGame;
        if (card == null || !card.IsDxvkInstalled) return;

        var variant = ViewModel.ResolveDxvkVariant(card.GameName);
        var url = variant switch
        {
            Models.DxvkVariant.LiliumHdr => "https://github.com/EndlesslyFlowering/dxvk/releases",
            Models.DxvkVariant.Stable => "https://github.com/doitsujin/dxvk/releases",
            _ => "https://github.com/doitsujin/dxvk/releases", // Development doesn't have a stable release page
        };
        await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
    }

    // ── DOF Fix event handlers ───────────────────────────────────────────────

    private async void InstallDofFixButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;
        if (card.DofFixIsInstalling || string.IsNullOrEmpty(card.InstallPath)) return;

        card.DofFixIsInstalling = true;
        card.DofFixActionMessage = "Installing DOF Fix...";
        card.DofFixProgress = 0;
        try
        {
            var progress = new Progress<(string msg, double pct)>(p =>
            {
                card.DofFixActionMessage = p.msg;
                card.DofFixProgress = p.pct;
            });
            var success = await _dofFixService.InstallAsync(card.InstallPath, progress);
            if (success)
            {
                card.DofFixInstalledVersion = _dofFixService.StagedVersion;
                card.DofFixStatus = Models.GameStatus.Installed;
                card.DofFixActionMessage = "✅ DOF Fix installed!";
                card.NotifyAll();
                card.FadeMessage(m => card.DofFixActionMessage = m, card.DofFixActionMessage);
            }
            else
            {
                card.DofFixActionMessage = "❌ Install failed";
            }
        }
        catch (Exception ex)
        {
            card.DofFixActionMessage = $"❌ {ex.Message}";
        }
        finally
        {
            card.DofFixIsInstalling = false;
        }
    }

    private void UninstallDofFixButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;
        if (string.IsNullOrEmpty(card.InstallPath)) return;

        var success = _dofFixService.Uninstall(card.InstallPath);
        if (success)
        {
            card.DofFixStatus = Models.GameStatus.NotInstalled;
            card.DofFixInstalledVersion = null;
            card.DofFixActionMessage = "✖ DOF Fix removed.";
            card.NotifyAll();
            card.FadeMessage(m => card.DofFixActionMessage = m, card.DofFixActionMessage);
        }
        else
        {
            card.DofFixActionMessage = "❌ Uninstall failed";
        }
    }

    private async void DofFixInfoButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;

        var notes = _dofFixService.ReleaseNotes;
        if (string.IsNullOrEmpty(notes))
        {
            await _dofFixService.CheckForUpdateAsync();
            notes = _dofFixService.ReleaseNotes;
        }

        var dialog = new ContentDialog
        {
            Title = "UE DOF Fix — Release Notes",
            Content = new ScrollViewer
            {
                Content = new TextBlock
                {
                    Text = notes ?? "No release notes available.",
                    TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                    IsTextSelectionEnabled = true,
                },
                MaxHeight = 400,
            },
            CloseButtonText = "OK",
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        await DialogService.ShowSafeAsync(dialog);
    }

    private async void DofFixCogButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "DOF Fix Settings",
            Content = new TextBlock
            {
                Text = "No configurable settings available for this component.",
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
            },
            CloseButtonText = "OK",
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        await DialogService.ShowSafeAsync(dialog);
    }

    private async void DetailDofFixStatus_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var card = ViewModel.SelectedGame;
        if (card == null || !card.IsDofFixInstalled) return;

        var version = card.DofFixInstalledVersion;
        if (!string.IsNullOrEmpty(version))
        {
            var url = _dofFixService.GetReleaseUrl(version);
            await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
        }
    }

    private async void DetailRsStatus_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (ViewModel.SelectedGame?.RsStatus == Models.GameStatus.Installed)
            await Windows.System.Launcher.LaunchUriAsync(new Uri("https://reshade.me"));
    }

    private async void DetailRefStatus_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (ViewModel.SelectedGame?.IsRefInstalled == true)
            await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/praydog/REFramework-nightly/releases"));
    }

    private async void DetailRdxStatus_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var card = ViewModel.SelectedGame;
        if (card?.IsRdxInstalled == true)
        {
            var url = !string.IsNullOrEmpty(card.NameUrl)
                ? card.NameUrl
                : "https://github.com/clshortfuse/renodx/wiki/Mods";
            await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
        }
    }

    private void LumaToggle_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.LumaToggle_Click(sender, e);

    // ── Shared cursor handlers for clickable link text ────────────────────────────
    private static readonly Microsoft.UI.Input.InputCursor _handCursor =
        Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
    private static readonly Microsoft.UI.Input.InputCursor _arrowCursor =
        Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Arrow);

    private void LinkText_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is TextBlock tb && tb.TextDecorations == Windows.UI.Text.TextDecorations.Underline)
        {
            var prop = typeof(UIElement).GetProperty("ProtectedCursor",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            prop?.SetValue(tb, _handCursor);
        }
    }

    private void LinkText_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe)
        {
            var prop = typeof(UIElement).GetProperty("ProtectedCursor",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            prop?.SetValue(fe, _arrowCursor);
        }
    }

    // ── Engine badge click (toggle UE5 DOF Fix eligibility) ─────────────────

    private async void EngineBadge_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;

        // Games force-enabled via manifest cannot be toggled off
        if (_dofFixService.IsForceEligible(card.GameName))
            return;

        // Games with a specific engine version (from manifest or auto-detection) cannot be toggled.
        // The badge toggle is only for games with vague "Unreal Engine" (no version suffix).
        if (card.EngineHint != null && card.EngineHint != "Unreal Engine"
            && card.EngineHint != "Unreal Engine 5"
            && System.Text.RegularExpressions.Regex.IsMatch(card.EngineHint, @"Unreal Engine \d"))
            return;

        // Show first-time warning dialog
        if (!ViewModel.Settings.EngineBadgeWarningDismissed)
        {
            var dontShowCheck = new CheckBox
            {
                Content = "Don't show this again",
                FontSize = 12,
                Margin = new Thickness(0, 8, 0, 0),
            };
            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(new TextBlock
            {
                Text = "This toggles the engine version to Unreal Engine 5.0–5.6, making this game eligible for the DOF Fix addon.\n\nUse this when RHI cannot detect the UE version automatically (e.g. Game Pass games).",
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                FontSize = 12,
            });
            panel.Children.Add(dontShowCheck);

            var dialog = new ContentDialog
            {
                Title = "Engine Version Override",
                Content = panel,
                PrimaryButtonText = "Continue",
                CloseButtonText = "Cancel",
                XamlRoot = Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            };
            var result = await DialogService.ShowSafeAsync(dialog);

            if (dontShowCheck.IsChecked == true)
            {
                ViewModel.Settings.EngineBadgeWarningDismissed = true;
                ViewModel.SaveSettingsPublic();
            }

            if (result != ContentDialogResult.Primary) return;
        }

        // Toggle between "Unreal Engine" (no DOF Fix) and "Unreal Engine 5" (DOF Fix eligible)
        var current = card.EngineHint ?? "Unreal Engine";
        bool isCurrentlyUe5 = current.Contains("Unreal Engine 5", StringComparison.OrdinalIgnoreCase);
        var next = isCurrentlyUe5 ? "Unreal Engine" : "Unreal Engine 5";

        // Store or remove override
        if (next == "Unreal Engine")
        {
            _gameNameService.EngineVersionOverrides.Remove(card.GameName);

            // Uninstall DOF Fix addon if it was installed
            if (card.DofFixStatus == GameStatus.Installed && !string.IsNullOrEmpty(card.InstallPath))
            {
                _dofFixService.Uninstall(card.InstallPath);
                card.DofFixStatus = GameStatus.NotInstalled;
                card.DofFixInstalledVersion = null;
            }
        }
        else
            _gameNameService.EngineVersionOverrides[card.GameName] = next;

        // Update card
        card.EngineHint = next;
        card.IsDofFixEligible = _dofFixService.IsGameEligible(next, card.Is32Bit, card.GameName);
        card.NotifyAll();

        // Persist and rebuild panel
        ViewModel.SaveSettingsPublic();
        _detailPanelBuilder?.PopulateDetailPanel(card);
        _detailPanelBuilder?.UpdateDetailComponentRows(card);
    }

    private void EngineBadge_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is GameCardViewModel)
        {
            var prop = typeof(UIElement).GetProperty("ProtectedCursor",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            prop?.SetValue(fe, _handCursor);
        }
    }

    private void EngineBadge_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement fe)
        {
            var prop = typeof(UIElement).GetProperty("ProtectedCursor",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            prop?.SetValue(fe, _arrowCursor);
        }
    }

    private void SwitchToLumaButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.SwitchToLumaButton_Click(sender, e);

    private void InstallLumaButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.InstallLumaButton_Click(sender, e);

    private void UninstallLumaButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.UninstallLumaButton_Click(sender, e);

    private void UeExtendedFlyoutItem_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.UeExtendedFlyoutItem_Click(sender, e);

    internal async Task ShowUeExtendedWarningAsync(GameCardViewModel card)
        => await _dialogService.ShowUeExtendedWarningAsync(card);

    internal void HideButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetCardFromSender(sender) is { } card)
            ViewModel.ToggleHideGameCommand.Execute(card);
    }

    internal void LaunchGame_Click(object sender, RoutedEventArgs e)
    {
        var card = GetCardFromSender(sender) ?? ViewModel.SelectedGame;
        if (card == null) return;
        LaunchGame(card);
    }

    private void GameList_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        if (ViewModel.SelectedGame is { } card)
            LaunchGame(card);
    }

    internal void LaunchGame(GameCardViewModel card)
    {
        try
        {
            var gameName = card.GameName;

            // Track recent launches
            TrackRecentLaunch(gameName);
            var launchArgs = _gameNameService.LaunchArgsOverrides
                .TryGetValue(gameName, out var args) ? args : null;

            // ── HDR Auto-Toggle: resolve whether to enable ──
            var hdrOverride = _gameNameService.HdrToggleOverrides
                .TryGetValue(gameName, out var hov) ? hov : null;
            bool shouldToggleHdr = hdrOverride != null
                ? string.Equals(hdrOverride, "On", StringComparison.OrdinalIgnoreCase)
                : ViewModel.Settings.HdrAutoToggle;

            bool hdrWasAlreadyOn = false;
            List<uint>? hdrTargets = null;
            if (shouldToggleHdr)
            {
                hdrWasAlreadyOn = HdrToggleService.IsHdrEnabled();
                _crashReporter.Log($"[MainWindow.LaunchGame] HDR toggle: shouldToggle={shouldToggleHdr}, wasAlreadyOn={hdrWasAlreadyOn}, override='{hdrOverride}'");
                // Always attempt to enable — IsHdrEnabled can report false positives on some configs
                hdrTargets = ViewModel.Settings.HdrTargetDisplays;
                HdrToggleService.EnableHdr(hdrTargets.Count > 0 ? hdrTargets : null);
            }
            else
            {
                _crashReporter.Log($"[MainWindow.LaunchGame] HDR toggle: disabled for '{gameName}' (override='{hdrOverride}', global={ViewModel.Settings.HdrAutoToggle})");
            }

            // 1. User override (absolute path)
            if (_gameNameService.LaunchExeOverrides.TryGetValue(gameName, out var userExe)
                && !string.IsNullOrEmpty(userExe) && File.Exists(userExe))
            {
                _crashReporter.Log($"[MainWindow.LaunchGame] Launching '{gameName}' via user override: {userExe} {launchArgs}");
                var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(userExe)
                {
                    Arguments = launchArgs ?? "",
                    UseShellExecute = true,
                });
                MonitorProcessForHdr(proc, shouldToggleHdr, hdrWasAlreadyOn, gameName, card.InstallPath, hdrTargets);
                return;
            }

            // 2. Manifest override (relative path from InstallPath)
            if (ViewModel.Manifest?.LaunchExeOverrides != null
                && ViewModel.Manifest.LaunchExeOverrides.TryGetValue(gameName, out var manifestExe)
                && !string.IsNullOrEmpty(manifestExe) && !string.IsNullOrEmpty(card.InstallPath))
            {
                var fullPath = Path.Combine(card.InstallPath, manifestExe);
                if (File.Exists(fullPath))
                {
                    _crashReporter.Log($"[MainWindow.LaunchGame] Launching '{gameName}' via manifest override: {fullPath} {launchArgs}");
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(fullPath)
                    {
                        Arguments = launchArgs ?? "",
                        UseShellExecute = true,
                    });
                    return;
                }
            }

            // 3. Steam protocol (use -applaunch when args are set for reliable arg passing)
            var steamAppId = card.DetectedGame?.SteamAppId;
            if (steamAppId != null && steamAppId > 0)
            {
                if (!string.IsNullOrEmpty(launchArgs))
                {
                    var steamExe = GetSteamExePath();
                    if (steamExe != null)
                    {
                        _crashReporter.Log($"[MainWindow.LaunchGame] Launching '{gameName}' via Steam -applaunch {steamAppId} {launchArgs}");
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(steamExe)
                        {
                            Arguments = $"-applaunch {steamAppId} {launchArgs}",
                            UseShellExecute = true,
                        });
                    }
                    else
                    {
                        // Fallback: use URL protocol (args may not pass reliably)
                        var steamUri = $"steam://rungameid/{steamAppId}";
                        _crashReporter.Log($"[MainWindow.LaunchGame] Launching '{gameName}' via Steam URL (args may not apply): {steamUri}");
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(steamUri) { UseShellExecute = true });
                    }
                }
                else
                {
                    var steamUri = $"steam://rungameid/{steamAppId}";
                    _crashReporter.Log($"[MainWindow.LaunchGame] Launching '{gameName}' via Steam: {steamUri}");
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(steamUri) { UseShellExecute = true });
                }
                MonitorProcessForHdr(null, shouldToggleHdr, hdrWasAlreadyOn, gameName, card.InstallPath, hdrTargets);
                return;
            }

            // 4. Epic Games Store protocol (skip if args set — protocol doesn't support game args)
            if (!string.IsNullOrEmpty(card.DetectedGame?.EpicAppName) && string.IsNullOrEmpty(launchArgs))
            {
                var epicUri = $"com.epicgames.launcher://apps/{card.DetectedGame.EpicAppName}?action=launch&silent=true";
                _crashReporter.Log($"[MainWindow.LaunchGame] Launching '{gameName}' via Epic protocol: {epicUri}");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(epicUri) { UseShellExecute = true });
                MonitorProcessForHdr(null, shouldToggleHdr, hdrWasAlreadyOn, gameName, card.InstallPath, hdrTargets);
                return;
            }

            // 5. Direct exe — find the game exe in InstallPath
            if (!string.IsNullOrEmpty(card.InstallPath) && Directory.Exists(card.InstallPath))
            {
                var exes = Directory.GetFiles(card.InstallPath, "*.exe", SearchOption.TopDirectoryOnly);
                var excludeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    { "unins000", "UnityCrashHandler64", "UnityCrashHandler32", "CrashReporter", "launcher" };
                var gameExe = exes
                    .Where(e => !excludeNames.Contains(Path.GetFileNameWithoutExtension(e)))
                    .OrderByDescending(e => new FileInfo(e).Length)
                    .FirstOrDefault();

                if (gameExe != null)
                {
                    _crashReporter.Log($"[MainWindow.LaunchGame] Launching '{gameName}' via auto-detected exe: {gameExe} {launchArgs}");
                    var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(gameExe)
                    {
                        Arguments = launchArgs ?? "",
                        UseShellExecute = true,
                    });
                    MonitorProcessForHdr(proc, shouldToggleHdr, hdrWasAlreadyOn, gameName, card.InstallPath, hdrTargets);
                    return;
                }
            }

            _crashReporter.Log($"[MainWindow.LaunchGame] No launch method found for '{gameName}'");
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainWindow.LaunchGame] Failed to launch '{card.GameName}' — {ex.Message}");
        }
    }

    private void TrackRecentLaunch(string gameName)
    {
        var recent = ViewModel.Settings.RecentLaunches;
        recent.Remove(gameName);
        recent.Insert(0, gameName);
        if (recent.Count > 5) recent.RemoveRange(5, recent.Count - 5);
        ViewModel.Settings.RecentLaunches = recent;
        ViewModel.SaveSettingsPublic();

        TrayIconService.UpdateRecentGames(recent);
        if (ViewModel.Settings.RecentGamesMenu)
            TrayIconService.UpdateJumpList(recent);
    }

    /// <summary>
    /// Monitors a launched process and disables HDR when it exits (if we enabled it).
    /// For protocol launches (Steam/Epic URL) where proc is null, polls for the game exe by name.
    /// </summary>
    private void MonitorProcessForHdr(System.Diagnostics.Process? proc, bool shouldToggle, bool wasAlreadyOn, string gameName, string? installPath = null, List<uint>? hdrTargets = null)
    {
        // Find the card to set IsRunning
        var card = ViewModel.AllCards.FirstOrDefault(c =>
            c.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase));

        if (proc != null)
        {
            // Direct exe launch — monitor the process directly
            DispatcherQueue?.TryEnqueue(() => { if (card != null) card.IsRunning = true; });
            _ = Task.Run(async () =>
            {
                try
                {
                    await proc.WaitForExitAsync();

                    // The launched process exited — but it might be a wrapper/launcher (SKSE, MO2)
                    // that spawned the real game process. Check if any game process is still running
                    // from the same install folder before disabling HDR.
                    if (!string.IsNullOrEmpty(installPath))
                    {
                        var normalizedPath = Path.GetFullPath(installPath).TrimEnd('\\').ToLowerInvariant();
                        System.Diagnostics.Process? survivingProc = null;

                        // Give the spawned game process a moment to start (wrappers exit almost instantly)
                        for (int attempt = 0; attempt < 5; attempt++)
                        {
                            await Task.Delay(1000);
                            try
                            {
                                survivingProc = System.Diagnostics.Process.GetProcesses()
                                    .FirstOrDefault(p =>
                                    {
                                        try
                                        {
                                            var mainMod = p.MainModule?.FileName;
                                            return mainMod != null
                                                && Path.GetFullPath(mainMod).ToLowerInvariant().StartsWith(normalizedPath);
                                        }
                                        catch { return false; }
                                    });
                            }
                            catch { }

                            if (survivingProc != null) break;
                        }

                        if (survivingProc != null)
                        {
                            // A game process is still running — this was a wrapper launch.
                            // Monitor the real game process instead.
                            _crashReporter.Log($"[MainWindow.MonitorProcess] '{gameName}' wrapper exited, found surviving process '{survivingProc.ProcessName}' (PID {survivingProc.Id})");
                            await survivingProc.WaitForExitAsync();
                        }
                    }

                    if (shouldToggle) HdrToggleService.DisableHdr(hdrTargets);
                    DispatcherQueue?.TryEnqueue(() => { if (card != null) card.IsRunning = false; });
                    _crashReporter.Log($"[MainWindow.MonitorProcess] '{gameName}' exited{(shouldToggle ? " — HDR disabled" : "")}");
                }
                catch (Exception ex)
                {
                    DispatcherQueue?.TryEnqueue(() => { if (card != null) card.IsRunning = false; });
                    _crashReporter.Log($"[MainWindow.MonitorProcess] '{gameName}' monitoring failed — {ex.Message}");
                }
            });
        }
        else if (!string.IsNullOrEmpty(installPath))
        {
            // Protocol launch (Steam/Epic) — poll for any new process running from the install path
            _ = Task.Run(async () =>
            {
                try
                {
                    var normalizedPath = Path.GetFullPath(installPath).TrimEnd('\\').ToLowerInvariant();
                    _crashReporter.Log($"[MainWindow.MonitorProcess] Polling for game process in '{normalizedPath}'...");

                    // Wait up to 60 seconds for the game process to appear
                    System.Diagnostics.Process? gameProc = null;
                    for (int i = 0; i < 60; i++)
                    {
                        await Task.Delay(1000);
                        try
                        {
                            gameProc = System.Diagnostics.Process.GetProcesses()
                                .FirstOrDefault(p =>
                                {
                                    try
                                    {
                                        var mainMod = p.MainModule?.FileName;
                                        return mainMod != null
                                            && Path.GetFullPath(mainMod).ToLowerInvariant().StartsWith(normalizedPath);
                                    }
                                    catch { return false; }
                                });
                        }
                        catch { }

                        if (gameProc != null) break;
                    }

                    if (gameProc == null)
                    {
                        _crashReporter.Log($"[MainWindow.MonitorProcess] '{gameName}' — game process not found after 60s");
                        return;
                    }

                    DispatcherQueue?.TryEnqueue(() => { if (card != null) card.IsRunning = true; });
                    _crashReporter.Log($"[MainWindow.MonitorProcess] '{gameName}' — found process '{gameProc.ProcessName}' (PID {gameProc.Id}), waiting for exit...");
                    await gameProc.WaitForExitAsync();
                    if (shouldToggle) HdrToggleService.DisableHdr(hdrTargets);
                    DispatcherQueue?.TryEnqueue(() => { if (card != null) card.IsRunning = false; });
                    _crashReporter.Log($"[MainWindow.MonitorProcess] '{gameName}' exited{(shouldToggle ? " — HDR disabled" : "")}");
                }
                catch (Exception ex)
                {
                    DispatcherQueue?.TryEnqueue(() => { if (card != null) card.IsRunning = false; });
                    _crashReporter.Log($"[MainWindow.MonitorProcess] '{gameName}' poll monitoring failed — {ex.Message}");
                }
            });
        }
    }

    private static string? GetSteamExePath()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Valve\Steam")
                         ?? Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
            var installPath = key?.GetValue("InstallPath") as string;
            if (!string.IsNullOrEmpty(installPath))
            {
                var exe = Path.Combine(installPath, "steam.exe");
                if (File.Exists(exe)) return exe;
            }
        }
        catch { }
        return null;
    }

    internal async void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var card = GetCardFromSender(sender);
        if (card == null) return;
        var suggestedPath = card.InstallPath is { Length: > 0 } p && Directory.Exists(p) ? p
                          : card.DetectedGame?.InstallPath is { Length: > 0 } dp && Directory.Exists(dp) ? dp
                          : null;
        var folder = await PickFolderAsync(suggestedPath);
        if (folder != null)
        {
            card.InstallPath = folder;
            if (card.DetectedGame != null)
                card.DetectedGame.InstallPath = folder;
            // Persist the override so it survives Refresh / app restart
            ViewModel.SetFolderOverride(card.GameName, folder);
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var card = GetCardFromSender(sender);
        if (card == null || string.IsNullOrEmpty(card.InstallPath)) return;
        if (System.IO.Directory.Exists(card.InstallPath))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(card.InstallPath) { UseShellExecute = true });
    }

    private void OpenAppData_Click(object sender, RoutedEventArgs e)
    {
        var card = GetCardFromSender(sender);
        if (card == null || string.IsNullOrEmpty(card.InstallPath)) return;

        // Open the exact folder where Engine.ini is deployed
        var configDir = AuxInstallService.ResolveEngineIniDir(card.InstallPath, card.EngineIniProjectOverride, card.GameName);
        if (configDir != null && System.IO.Directory.Exists(configDir))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(configDir) { UseShellExecute = true });
            return;
        }

        // Fallback: open the project root in AppData
        var projectName = card.EngineIniProjectOverride
            ?? AuxInstallService.ResolveUeProjectName(card.InstallPath);

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(projectName))
        {
            var appDataDir = System.IO.Path.Combine(localAppData, projectName);
            if (System.IO.Directory.Exists(appDataDir))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(appDataDir) { UseShellExecute = true });
                return;
            }
        }
    }

    internal void RemoveManualGame_Click(object sender, RoutedEventArgs e)
    {
        var card = GetCardFromSender(sender);
        if (card == null) return;

        if (card.IsManuallyAdded)
        {
            // Manual game — remove it entirely
            ViewModel.RemoveManualGameCommand.Execute(card);
        }
        else
        {
            // Auto-detected game — reset folder to original detected path
            ViewModel.ResetFolderOverride(card);
        }
    }

    // ── Scroll restore helpers ────────────────────────────────────────────────────

    private async Task RefreshWithScrollRestore()
    {
        var selectedName = (GameList.SelectedItem as GameCardViewModel)?.GameName;

        await ViewModel.RefreshAsync();

        RestoreScrollAndSelection(selectedName);

        // If Settings page is visible, refresh the global NVIDIA driver settings
        if (ViewModel.CurrentPage == AppPage.Settings)
            _settingsHandler.RefreshGlobalNvidiaSettings();
    }

    private async Task FullRefreshWithScrollRestore()
    {
        var selectedName = (GameList.SelectedItem as GameCardViewModel)?.GameName;

        // Show progress dialog
        var progressPanel = new StackPanel { Spacing = 8 };
        var progressRow = new StackPanel { Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal, Spacing = 12 };
        var progressRing = new ProgressRing { IsActive = true, Width = 20, Height = 20 };
        var progressText = new TextBlock { Text = "Clearing caches...", FontSize = 13, Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush) };
        progressRow.Children.Add(progressRing);
        progressRow.Children.Add(progressText);
        progressPanel.Children.Add(progressRow);

        var progressDialog = new ContentDialog
        {
            Title = "Full Refresh",
            Content = progressPanel,
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };
        _ = DialogService.ShowSafeAsync(progressDialog);

        var uiProgress = new Progress<string>(msg => DispatcherQueue?.TryEnqueue(() => progressText.Text = msg));
        await ViewModel.FullRefreshAsync(uiProgress);

        progressDialog.Hide();
        RestoreScrollAndSelection(selectedName);
    }

    private void RestoreScrollAndSelection(string? selectedName)
    {
        // Restore game list selection
        if (!string.IsNullOrEmpty(selectedName))
        {
            _pendingReselect = selectedName;
            DispatcherQueue.TryEnqueue(TryRestoreSelection);
        }
    }
}
