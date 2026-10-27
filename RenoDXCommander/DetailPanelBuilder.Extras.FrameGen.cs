// DetailPanelBuilder.Extras.FrameGen.cs — Extras section: DLSSG 20/30 and RTX 40 MFG rows with their DLL pickers.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Windows.Foundation;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    private static readonly string[] MfgDllNames =
    {
        "version.dll", "dinput8.dll", "winmm.dll", "d3d9.dll", "d3d10.dll",
        "d3d11.dll", "d3d12.dll", "dxgi.dll", "dsound.dll", "wininet.dll",
        "winhttp.dll", "binkw64.dll", "bink2w64.dll", "xinput1_1.dll",
        "xinput1_2.dll", "xinput1_3.dll", "xinput1_4.dll", "xinput9_1_0.dll",
        "xinputuap.dll",
    };

    private void BuildDlssg2030Row(GameCardViewModel card, StackPanel body)
    {
        _window.ViewModel.SetLastUiAction($"BuildDlssg2030Row({card.GameName})");
        var svc         = App.Services.GetRequiredService<Dlssg20_30Service>();
        var gameName    = card.GameName;
        var store       = card.Source ?? "";
        var installPath = card.InstallPath ?? "";

        var currentDllName = _window.ViewModel.GetDlssg2030InstalledAs(gameName, store);
        bool isInstalled   = svc.IsInstalledIn(installPath, currentDllName);
        // Use cached state from card instead of File.Exists on UI thread
        bool addonConflict = card.MfgAdaInstalled;

        string statusText  = isInstalled ? (svc.StagedVersion ?? "Installed") : "Ready";
        string statusColor = isInstalled ? "#5ECB7D" : "#A0AABB";

        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(PanelColW(1, 0, 350, _window.ExtrasContainer.ActualWidth)) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

        // Col 0 — label
        var label = new TextBlock
        {
            Text = "20/30 FG Unlock",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(label, "20/30 FG Unlock — enables DLSS Frame Generation on RTX 20 and 30 series GPUs. D3D12 only. No ASI Loader or ReShade required.");
        Grid.SetColumn(label, 0);
        row.Children.Add(label);

        // Col 1 — status (version SHA when installed)
        var statusBlock = new TextBlock
        {
            Text = statusText,
            FontSize = 12,
            Foreground = UIFactory.GetBrush(statusColor),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalTextAlignment = Microsoft.UI.Xaml.TextAlignment.Center,
            TextDecorations = isInstalled ? Windows.UI.Text.TextDecorations.Underline : Windows.UI.Text.TextDecorations.None,
        };
        if (isInstalled)
        {
            ToolTipService.SetToolTip(statusBlock, $"Installed as: {currentDllName}\nClick to open GitHub releases");
            statusBlock.PointerPressed += (s, e) =>
                _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/sdli1995/dlssg_for_sm86"));
        }
        Grid.SetColumn(statusBlock, 1);
        row.Children.Add(statusBlock);

        // Col 2 — Info button
        var infoBtn = new Button
        {
            Content = "Info",
            FontSize = 11,
            Padding = new Thickness(6, 2, 6, 2),
            Width = 36,
            Height = 32,
            Background = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush),
            Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
        };
        ToolTipService.SetToolTip(infoBtn, "Open 20/30 FG Unlock GitHub page");
        infoBtn.Click += (s, e) =>
            _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/sdli1995/dlssg_for_sm86"));
        Grid.SetColumn(infoBtn, 2);
        row.Children.Add(infoBtn);

        // Col 3 — Install button
        var installBtn = new Button
        {
            Content = isInstalled ? "↺  Reinstall 20/30 FG" : "⬇  Install 20/30 FG",
            FontSize = 12,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(8),
            Background = isInstalled ? UIFactory.GetBrush("#182840") : UIFactory.Brush(ResourceKeys.AccentBlueBgBrush),
            Foreground = isInstalled ? UIFactory.GetBrush("#7AACDD") : UIFactory.Brush(ResourceKeys.AccentBlueBrush),
            BorderBrush = isInstalled ? UIFactory.GetBrush("#2A4468") : UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush),
            BorderThickness = new Thickness(1),
        };

        if (addonConflict)
        {
            installBtn.IsEnabled = false;
            installBtn.Opacity   = 0.35;
            installBtn.Content   = "Uninstall MFG Ada Unlock first";
            ToolTipService.SetToolTip(installBtn, "MFG Ada Unlock (addon) is installed and conflicts. Remove it from the addon picker first.");
        }
        else
        {
            ToolTipService.SetToolTip(installBtn, isInstalled
                ? $"Reinstall 20/30 FG Unlock (currently deployed as {currentDllName})"
                : "Install 20/30 FG Unlock — deploys version.dll under a name you choose");
        }

        installBtn.Click += async (s, e) =>
        {
            if (string.IsNullOrEmpty(installPath)) return;

            var chosen = await ShowDlssg2030DllPickerAsync(card, currentDllName);
            if (chosen == null) return;

            installBtn.IsEnabled = false;
            installBtn.Content   = "Installing...";
            try
            {
                // If reinstalling with a different name, remove the old one first
                if (isInstalled && !string.IsNullOrEmpty(currentDllName)
                    && !currentDllName.Equals(chosen, StringComparison.OrdinalIgnoreCase))
                    svc.Uninstall(installPath, currentDllName);

                var gpuGen = _window.ViewModel.GetDlssg2030GpuGen(gameName, store);
                bool ok = await Task.Run(async () =>
                {
                    if (!svc.IsStagingReady || svc.HasUpdate)
                        await svc.EnsureStagingAsync().ConfigureAwait(false);
                    return svc.Install(installPath, chosen, gpuGen);
                });

                if (ok)
                {
                    _window.ViewModel.SetDlssg2030InstalledAs(gameName, chosen, store);
                    RequestExtrasRebuild(card);
                }
                else
                {
                    installBtn.Content   = "Download failed — try again";
                    installBtn.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[BuildDlssg2030Row] Install failed — {ex.Message}");
                installBtn.Content   = "Install failed";
                installBtn.IsEnabled = true;
            }
        };
        Grid.SetColumn(installBtn, 3);
        row.Children.Add(installBtn);

        // Col 4 — Cog button
        var cogBtn = new Button
        {
            Width = 36, Height = 32, Padding = new Thickness(0),
            Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Content = new TextBlock { Text = "⚙", FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center },
        };
        ToolTipService.SetToolTip(cogBtn, "Select GPU generation");
        cogBtn.Click += async (s, e) =>
        {
            var currentGen = _window.ViewModel.GetDlssg2030GpuGen(gameName, store);
            var combo = new ComboBox
            {
                ItemsSource = new[] { Dlssg20_30Service.GpuGenRtx30, Dlssg20_30Service.GpuGenRtx20 },
                SelectedItem = currentGen,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MaxDropDownHeight = 300,
            };
            var dlg = new ContentDialog
            {
                Title = "20/30 FG Unlock — GPU Generation",
                Content = new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Select your GPU generation. This controls which rendering path is used.\n\n" +
                                   "RTX 30 Series (SM86) — Ampere\n" +
                                   "RTX 20 Series (SM75) — Turing\n\n" +
                                   "Reinstall after changing to apply the new setting.",
                            FontSize = 12,
                            TextWrapping = TextWrapping.Wrap,
                        },
                        combo,
                    },
                },
                PrimaryButtonText = "Save",
                CloseButtonText   = "Cancel",
                XamlRoot          = _window.Content.XamlRoot,
                RequestedTheme    = ElementTheme.Dark,
            };
            var result = await DialogService.ShowSafeAsync(dlg);
            if (result == ContentDialogResult.Primary && combo.SelectedItem is string selected)
            {
                _window.ViewModel.SetDlssg2030GpuGen(gameName, selected, store);
                // If installed, update the INI immediately
                if (isInstalled && !string.IsNullOrEmpty(currentDllName)
                    && !string.IsNullOrEmpty(installPath))
                {
                    var iniPath = System.IO.Path.Combine(installPath, Dlssg20_30Service.IniFileName);
                    if (System.IO.File.Exists(iniPath))
                        Dlssg20_30Service.ApplyRouterToIniPublic(iniPath, selected);
                }
                RequestExtrasRebuild(card);
            }
        };
        Grid.SetColumn(cogBtn, 4);
        row.Children.Add(cogBtn);

        // Col 5 — Remove button
        var removeBtn = new Button
        {
            Width = 36, Height = 32, Padding = new Thickness(0),
            Background = UIFactory.Brush(ResourceKeys.AccentRedBgBrush),
            Foreground = UIFactory.Brush(ResourceKeys.AccentRedBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.AccentPurpleBorderBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Content = new TextBlock { Text = "✕", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Foreground = UIFactory.Brush(ResourceKeys.AccentRedBrush) },
            Opacity = isInstalled ? 1.0 : 0,
            IsHitTestVisible = isInstalled,
        };
        ToolTipService.SetToolTip(removeBtn, "Remove 20/30 FG Unlock from this game");
        removeBtn.Click += (s, e) =>
        {
            if (string.IsNullOrEmpty(installPath)) return;
            svc.Uninstall(installPath, currentDllName);
            _window.ViewModel.SetDlssg2030InstalledAs(gameName, null, store);
            RequestExtrasRebuild(card);
        };
        Grid.SetColumn(removeBtn, 5);
        row.Children.Add(removeBtn);

        body.Children.Add(row);
    }

    private async Task<string?> ShowDlssg2030DllPickerAsync(GameCardViewModel card, string? currentDllName)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return null;

        var rhiOwned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(card.RsInstalledFile))  rhiOwned.Add(card.RsInstalledFile);
        if (!string.IsNullOrEmpty(card.OsInstalledFile))  rhiOwned.Add(card.OsInstalledFile);
        if (!string.IsNullOrEmpty(card.DcInstalledFile))  rhiOwned.Add(card.DcInstalledFile);

        string? chosen = null;

        var listPanel    = new StackPanel { Spacing = 4 };
        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 360,
            Content   = listPanel,
        };

        foreach (var name in Dlssg20_30Service.ProxyNames)
        {
            bool isRecommended  = string.Equals(name, "version.dll", StringComparison.OrdinalIgnoreCase);
            bool isRhiOwned     = rhiOwned.Contains(name);
            bool isCurrent      = string.Equals(name, currentDllName, StringComparison.OrdinalIgnoreCase);

            var btn = new Button
            {
                HorizontalAlignment        = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding         = new Thickness(10, 6, 10, 6),
                CornerRadius    = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                IsEnabled       = !isRhiOwned,
                Opacity         = isRhiOwned ? 0.4 : 1.0,
                Background  = isCurrent ? UIFactory.Brush(ResourceKeys.AccentBlueBgBrush) : UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
                BorderBrush = isCurrent ? UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush) : UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            };

            var contentRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            contentRow.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = 12,
                Foreground = isRhiOwned
                    ? UIFactory.Brush(ResourceKeys.TextTertiaryBrush)
                    : UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
                VerticalAlignment = VerticalAlignment.Center,
            });
            if (isRecommended)
                contentRow.Children.Add(MakeBadge("Recommended", "#1A3A20", "#6AE87A", "#2A5A30"));
            if (isRhiOwned)
                contentRow.Children.Add(MakeBadge("Used by RHI", "#2A1818", "#CC6666", "#5A2828"));
            if (isCurrent)
                contentRow.Children.Add(MakeBadge("Current", "#182840", "#7AACDD", "#2A4468"));

            btn.Content = contentRow;
            if (isRhiOwned)
                ToolTipService.SetToolTip(btn, "This filename is already used by an RHI-managed component. Choose a different name.");

            btn.Tag    = name;
            btn.Click += (s, ev) =>
            {
                chosen = (s as Button)?.Tag as string;
                if (s is FrameworkElement fe)
                {
                    var dialog = FindParentContentDialog(fe);
                    dialog?.Hide();
                }
            };
            listPanel.Children.Add(btn);
        }

        var pickerDialog = new ContentDialog
        {
            Title = "Choose 20/30 FG Unlock DLL name",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Select your GPU generation in the cog (⚙) before installing. Then choose the filename to deploy the DLL as.",
                        FontSize = 11,
                        Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
                        TextWrapping = TextWrapping.Wrap,
                    },
                    scrollViewer,
                }
            },
            CloseButtonText = "Cancel",
            XamlRoot        = _window.Content.XamlRoot,
        };

        await DialogService.ShowSafeAsync(pickerDialog);
        return chosen;
    }

    private void BuildRtx40MfgRow(GameCardViewModel card, StackPanel body)
    {
        _window.ViewModel.SetLastUiAction($"BuildRtx40MfgRow({card.GameName})");
        var mfgSvc       = App.Services.GetRequiredService<Rtx40MfgService>();
        var gameName     = card.GameName;
        var store        = card.Source ?? "";
        var installPath  = card.InstallPath ?? "";

        // Use cached state from card instead of File.Exists on UI thread
        bool isInstalled   = card.Rtx40MfgInstalled;
        bool addonConflict = card.MfgAdaInstalled;
        var currentDllName = _window.ViewModel.GetRtx40MfgInstalledAs(gameName, store);

        // Status
        string statusText  = isInstalled ? (mfgSvc.StagedVersion ?? "Installed") : "Ready";
        string statusColor = isInstalled ? "#5ECB7D" : "#A0AABB";

        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(PanelColW(1, 0, 350, _window.ExtrasContainer.ActualWidth)) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

        // Col 0 — label
        var label = new TextBlock
        {
            Text = "RTX 40 MFG",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(label, "RTX 40 MFG Unlock — enables DLSS Multi Frame Generation multipliers beyond 2x (up to 6x) on RTX 40 Series GPUs. Standalone DLL, no ASI Loader required.");
        Grid.SetColumn(label, 0);
        row.Children.Add(label);

        // Col 1 — status
        var statusBlock = new TextBlock
        {
            Text = statusText,
            FontSize = 12,
            Foreground = UIFactory.GetBrush(statusColor),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalTextAlignment = Microsoft.UI.Xaml.TextAlignment.Center,
            TextDecorations = isInstalled ? Windows.UI.Text.TextDecorations.Underline : Windows.UI.Text.TextDecorations.None,
        };
        if (isInstalled)
        {
            ToolTipService.SetToolTip(statusBlock, $"Installed as: {currentDllName}\nClick to open GitHub releases page");
            statusBlock.PointerPressed += (s, e) =>
                _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/dashdogy/RTX40MFG-Unlock/releases"));
        }
        Grid.SetColumn(statusBlock, 1);
        row.Children.Add(statusBlock);

        // Col 2 — Info button
        var infoBtn = new Button
        {
            Content = "Info",
            FontSize = 11,
            Padding = new Thickness(6, 2, 6, 2),
            Width = 36,
            Height = 32,
            Background = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush),
            Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
        };
        ToolTipService.SetToolTip(infoBtn, "Open RTX 40 MFG Unlock GitHub page");
        infoBtn.Click += (s, e) =>
            _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/dashdogy/RTX40MFG-Unlock"));
        Grid.SetColumn(infoBtn, 2);
        row.Children.Add(infoBtn);

        // Col 3 — Install button
        var installBtn = new Button
        {
            Content = isInstalled ? "↺  Reinstall RTX 40 MFG" : "⬇  Install RTX 40 MFG",
            FontSize = 12,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(8),
            Background = isInstalled
                ? UIFactory.GetBrush("#182840")
                : UIFactory.Brush(ResourceKeys.AccentBlueBgBrush),
            Foreground = isInstalled
                ? UIFactory.GetBrush("#7AACDD")
                : UIFactory.Brush(ResourceKeys.AccentBlueBrush),
            BorderBrush = isInstalled
                ? UIFactory.GetBrush("#2A4468")
                : UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush),
            BorderThickness = new Thickness(1),
        };

        if (addonConflict)
        {
            installBtn.IsEnabled = false;
            installBtn.Opacity   = 0.35;
            installBtn.Content   = "Uninstall MFG Ada Unlock first";
            ToolTipService.SetToolTip(installBtn, "MFG Ada Unlock (addon) is already installed and conflicts with RTX 40 MFG Unlock. Remove it from the addon picker first.");
        }
        else
        {
            ToolTipService.SetToolTip(installBtn, isInstalled
                ? $"Reinstall RTX 40 MFG Unlock (currently deployed as {currentDllName})"
                : "Install RTX 40 MFG Unlock — deploys RTXMFG.dll under a name you choose");
        }

        installBtn.Click += async (s, e) =>
        {
            if (string.IsNullOrEmpty(installPath)) return;

            var chosen = await ShowMfgDllPickerAsync(card, currentDllName);
            if (chosen == null) return;

            installBtn.IsEnabled = false;
            installBtn.Content   = "Installing...";
            try
            {
                // If reinstalling with a different name, remove the old one first
                if (isInstalled && !string.IsNullOrEmpty(currentDllName)
                    && !currentDllName.Equals(chosen, StringComparison.OrdinalIgnoreCase))
                    mfgSvc.Uninstall(installPath, currentDllName);

                bool ok = await Task.Run(async () =>
                {
                    if (!mfgSvc.IsStagingReady || mfgSvc.HasUpdate)
                        await mfgSvc.EnsureStagingAsync().ConfigureAwait(false);
                    return mfgSvc.Install(installPath, chosen);
                });

                if (ok)
                {
                    _window.ViewModel.SetRtx40MfgInstalledAs(gameName, chosen, store);
                    card.SetMfgState(mfgAdaInstalled: card.MfgAdaInstalled, mfgAdaRtx40Conflict: card.MfgAdaInstalled, rtx40MfgInstalled: true);
                    RequestExtrasRebuild(card);
                }
                else
                {
                    installBtn.Content   = "Download failed — try again";
                    installBtn.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[BuildRtx40MfgRow] Install failed — {ex.Message}");
                installBtn.Content   = "Install failed";
                installBtn.IsEnabled = true;
            }
        };
        Grid.SetColumn(installBtn, 3);
        row.Children.Add(installBtn);

        // Col 4 — Cog button
        var cogBtn = new Button
        {
            Width = 36,
            Height = 32,
            Padding = new Thickness(0),
            Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Content = new TextBlock { Text = "⚙", FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center },
        };
        ToolTipService.SetToolTip(cogBtn, "RTX 40 MFG settings — configure multiplier mode");
        cogBtn.Click += async (s, e) =>
        {
            var installedNote = isInstalled ? $"\n\nCurrently installed as: {currentDllName}" : "";
            var dlg = new ContentDialog
            {
                Title = "RTX 40 MFG Settings",
                Content = new TextBlock
                {
                    Text = "Press Backspace in-game to open the RTX 40 MFG menu.\n\n" +
                           "• Follow game — uses the game's own MFG setting\n" +
                           "• Fixed 2x–6x — forces a specific multiplier\n" +
                           "• Dynamic — targets the display refresh rate or a custom FPS value\n\n" +
                           "If frames freeze above 2x, try setting Frame Generation to Preset B in the NVIDIA App." +
                           installedNote,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                },
                PrimaryButtonText = "Open GitHub",
                CloseButtonText   = "Close",
                XamlRoot          = _window.Content.XamlRoot,
                RequestedTheme    = ElementTheme.Dark,
            };
            var result = await DialogService.ShowSafeAsync(dlg);
            if (result == ContentDialogResult.Primary)
                _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/dashdogy/RTX40MFG-Unlock"));
        };
        Grid.SetColumn(cogBtn, 4);
        row.Children.Add(cogBtn);

        // Col 5 — Remove button
        var removeBtn = new Button
        {
            Width = 36,
            Height = 32,
            Padding = new Thickness(0),
            Background = UIFactory.Brush(ResourceKeys.AccentRedBgBrush),
            Foreground = UIFactory.Brush(ResourceKeys.AccentRedBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.AccentPurpleBorderBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Content = new TextBlock { Text = "✕", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Foreground = UIFactory.Brush(ResourceKeys.AccentRedBrush) },
            Opacity = isInstalled ? 1.0 : 0,
            IsHitTestVisible = isInstalled,
        };
        ToolTipService.SetToolTip(removeBtn, "Remove RTX 40 MFG Unlock from this game");
        removeBtn.Click += (s, e) =>
        {
            if (string.IsNullOrEmpty(installPath)) return;
            mfgSvc.Uninstall(installPath, currentDllName);
            _window.ViewModel.SetRtx40MfgInstalledAs(gameName, null, store);
            card.SetMfgState(mfgAdaInstalled: card.MfgAdaInstalled, mfgAdaRtx40Conflict: false, rtx40MfgInstalled: false);
            RequestExtrasRebuild(card);
        };
        Grid.SetColumn(removeBtn, 5);
        row.Children.Add(removeBtn);

        body.Children.Add(row);
    }

    private async Task<string?> ShowMfgDllPickerAsync(GameCardViewModel card, string? currentDllName)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return null;

        var rhiOwned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(card.RsInstalledFile))  rhiOwned.Add(card.RsInstalledFile);
        if (!string.IsNullOrEmpty(card.OsInstalledFile))  rhiOwned.Add(card.OsInstalledFile);
        if (!string.IsNullOrEmpty(card.DcInstalledFile))  rhiOwned.Add(card.DcInstalledFile);

        string? chosen = null;

        var listPanel    = new StackPanel { Spacing = 4 };
        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 420,
            Content   = listPanel,
        };

        foreach (var name in MfgDllNames)
        {
            bool isRecommended  = string.Equals(name, "version.dll", StringComparison.OrdinalIgnoreCase);
            bool isRhiOwned     = rhiOwned.Contains(name);
            bool isDxgiConflict = string.Equals(name, "dxgi.dll", StringComparison.OrdinalIgnoreCase)
                               && !string.IsNullOrEmpty(card.RsInstalledFile);
            bool isCurrent      = string.Equals(name, currentDllName, StringComparison.OrdinalIgnoreCase);

            var btn = new Button
            {
                HorizontalAlignment        = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding         = new Thickness(10, 6, 10, 6),
                CornerRadius    = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                IsEnabled       = !isRhiOwned,
                Opacity         = isRhiOwned ? 0.4 : 1.0,
                Background  = isCurrent ? UIFactory.Brush(ResourceKeys.AccentBlueBgBrush) : UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
                BorderBrush = isCurrent ? UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush) : UIFactory.Brush(ResourceKeys.BorderDefaultBrush),
            };

            var contentRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            contentRow.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = 12,
                Foreground = isRhiOwned
                    ? UIFactory.Brush(ResourceKeys.TextTertiaryBrush)
                    : UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
                VerticalAlignment = VerticalAlignment.Center,
            });
            if (isRecommended)
                contentRow.Children.Add(MakeBadge("Recommended", "#1A3A20", "#6AE87A", "#2A5A30"));
            if (isRhiOwned)
                contentRow.Children.Add(MakeBadge("Used by RHI", "#2A1818", "#CC6666", "#5A2828"));
            else if (isDxgiConflict)
                contentRow.Children.Add(MakeBadge("May conflict with ReShade/OS", "#2A1A10", "#CC9955", "#5A3A18"));
            if (isCurrent)
                contentRow.Children.Add(MakeBadge("Current", "#182840", "#7AACDD", "#2A4468"));

            btn.Content = contentRow;

            if (isRhiOwned)
                ToolTipService.SetToolTip(btn, "This filename is already used by an RHI-managed component. Choose a different name.");
            else if (isDxgiConflict)
                ToolTipService.SetToolTip(btn, "dxgi.dll may conflict with ReShade or OptiScaler if they also use this name.");

            btn.Tag    = name;
            btn.Click += (s, ev) =>
            {
                chosen = (s as Button)?.Tag as string;
                if (s is FrameworkElement fe)
                {
                    var dialog = FindParentContentDialog(fe);
                    dialog?.Hide();
                }
            };

            listPanel.Children.Add(btn);
        }

        var pickerDialog = new ContentDialog
        {
            Title = "Choose RTX 40 MFG DLL name",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Select the filename to deploy RTXMFG.dll as. Choose a name the game loads early, and avoid names already used by other mods.",
                        FontSize = 11,
                        Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
                        TextWrapping = TextWrapping.Wrap,
                    },
                    scrollViewer,
                }
            },
            CloseButtonText = "Cancel",
            XamlRoot        = _window.Content.XamlRoot,
        };

        await DialogService.ShowSafeAsync(pickerDialog);
        return chosen;
    }
}
