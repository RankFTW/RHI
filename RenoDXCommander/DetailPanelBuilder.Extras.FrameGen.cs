// DetailPanelBuilder.Extras.FrameGen.cs — Extras section: DLSSG 20/30 and Universal MFG Unlocker rows with their DLL pickers.

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

    /// <summary>
    /// Warns that XeMFG standalone doesn't start while ReShade is installed. Returns true to continue.
    /// "Don't show this again" is saved even when the user cancels.
    /// </summary>
    private async System.Threading.Tasks.Task<bool> ShowUmfgReShadeWarningAsync(XamlRoot? xamlRoot)
    {
        xamlRoot ??= _window.Content.XamlRoot;
        if (xamlRoot == null) return true;

        var dontShowCheck = new CheckBox
        {
            Content = "Don't show this again",
            FontSize = 12,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var content = new StackPanel { Spacing = 4 };
        content.Children.Add(new TextBlock
        {
            Text = "ReShade is installed in this game. Universal MFG Unlocker's XeSS frame generation (\"XeMFG standalone\" in its menu) "
                 + "doesn't start while ReShade is loaded: the mod's menu shows \"Waiting menu_factory_not_dxgi\". "
                 + "This happens whether ReShade is installed as a DLL or as an ASI.\n\n"
                 + "DLSS multi frame generation works as normal. To use XeMFG with ReShade, install OptiScaler (nightly, F5 or DLSS NR) "
                 + "and set FG Output to XeFG in OptiScaler's settings; RHI then installs the mod as an OptiScaler plugin.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            LineHeight = 22,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
        });
        content.Children.Add(dontShowCheck);

        var dialog = new ContentDialog
        {
            Title = "XeMFG and ReShade",
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
            _window.ViewModel.Settings.UmfgReShadeWarningDismissed = true;
            _window.ViewModel.SaveSettingsPublic();
        }
        return result == ContentDialogResult.Primary;
    }

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
        ToolTipService.SetToolTip(label, "20/30 FG Unlock — enables DLSS Frame Generation (up to 6X) on RTX 20 and 30 series GPUs. D3D12. Needs NVIDIA driver R580 or newer. No ASI Loader or ReShade required.");
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
            AttachLinkCursor(statusBlock);
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
                : "Install 20/30 FG Unlock — deploys the proxy DLL you choose, dlssg_sm86.ini and the dlssg_sm86 folder");
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
                // Only one proxy may be in the folder: if reinstalling under a different name,
                // put the old slot back first (the ini and dlssg_sm86\ folder are reused)
                if (isInstalled && !string.IsNullOrEmpty(currentDllName)
                    && !currentDllName.Equals(chosen, StringComparison.OrdinalIgnoreCase))
                    AuxInstallService.SentinelRestore(Path.Combine(installPath, currentDllName));

                bool ok = await Task.Run(async () =>
                {
                    if (!svc.IsStagingReady || svc.HasUpdate)
                        await svc.EnsureStagingAsync().ConfigureAwait(false);
                    return svc.Install(installPath, chosen);
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
        var cogBtn = CogDialog.MakeCogButton("20/30 FG Unlock settings");
        cogBtn.Click += async (s, e) =>
        {
            try { await ShowDlssg2030SettingsAsync(installPath, isInstalled); }
            catch (Exception ex) { CrashReporter.Log($"[BuildDlssg2030Row] Settings dialog failed — {ex.Message}"); }
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
                        Text = "Choose which proxy DLL the game loads. Start with version.dll; if frame generation doesn't appear in the game, reinstall with the next name in the list. dxgi.dll and d3d12.dll are a last resort and don't work in Vulkan games.",
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
        bool isPluginInstall = isInstalled && Rtx40MfgService.IsPluginInstall(currentDllName); // loaded by OptiScaler

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
            Text = "Universal MFG Unlocker",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap, // long name; wraps instead of clipping in the 120 px column
        };
        ToolTipService.SetToolTip(label, "Universal MFG Unlocker (formerly RTX 40 MFG Unlock) — DLSS Multi Frame Generation beyond 2x (up to 6x) on RTX 40 and RTX 30 Series GPUs (RTX 20 experimental), plus XeSS frame generation (XeMFG) on any GPU. Standalone DLL, no ASI Loader required. RHI also installs Intel's XeSS files that XeMFG needs.");
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
                _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/dashdogy/MFG-Unlocker/releases"));
            AttachLinkCursor(statusBlock);
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
        ToolTipService.SetToolTip(infoBtn, "Open the Universal MFG Unlocker GitHub page");
        infoBtn.Click += (s, e) =>
            _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/dashdogy/MFG-Unlocker"));
        Grid.SetColumn(infoBtn, 2);
        row.Children.Add(infoBtn);

        // Col 3 — Install button
        var installBtn = new Button
        {
            Content = isInstalled ? "↺  Reinstall Universal MFG Unlocker" : "⬇  Install Universal MFG Unlocker",
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
            ToolTipService.SetToolTip(installBtn, "MFG Ada Unlock (addon) is already installed and conflicts with Universal MFG Unlocker. Remove it from the addon picker first.");
        }
        else
        {
            // Cheap check for the tooltip only; the click handler checks OptiScaler's install record
            bool osPluginLikely = card.IsOsInstalled && !string.IsNullOrEmpty(installPath)
                && System.IO.Directory.Exists(System.IO.Path.Combine(installPath, "OptiScaler"));
            ToolTipService.SetToolTip(installBtn,
                isPluginInstall ? $"Reinstall Universal MFG Unlocker (loaded by OptiScaler from {currentDllName})"
                : osPluginLikely ? (isInstalled
                    ? $"Reinstall Universal MFG Unlocker. OptiScaler is installed, so it moves from {currentDllName} into OptiScaler\\plugins."
                    : "Install Universal MFG Unlocker as an OptiScaler plugin (OptiScaler\\plugins\\MFGUnlocker.asi), as its author recommends with OptiScaler.")
                : isInstalled ? $"Reinstall Universal MFG Unlocker (currently deployed as {currentDllName})"
                : "Install Universal MFG Unlocker — deploys the unlock DLL under a name you choose");
        }

        installBtn.Click += async (s, e) =>
        {
            if (string.IsNullOrEmpty(installPath)) return;
            var vm = _window.ViewModel;

            // With an OptiScaler 10 build installed by RHI the mod goes in OptiScaler\plugins (no name to pick).
            // Read the live location too: it can have moved since this panel was built.
            bool asPlugin    = await Task.Run(() => vm.IsOptiScalerPluginHost(card));
            var liveDllName  = vm.GetRtx40MfgInstalledAs(gameName, store);
            bool liveInstalled = !string.IsNullOrEmpty(liveDllName) && mfgSvc.IsInstalledIn(installPath, liveDllName);

            // XeMFG standalone can't start while ReShade hooks DXGI (the mod reports menu_factory_not_dxgi).
            // Through OptiScaler it works, so no warning there.
            if (!asPlugin && card.IsRsInstalled && !card.RequiresVulkanInstall
                && !vm.Settings.UmfgReShadeWarningDismissed
                && !await ShowUmfgReShadeWarningAsync(installBtn.XamlRoot))
                return;

            string? chosen;
            if (asPlugin)
                chosen = Rtx40MfgService.PluginRelPath;
            else
            {
                chosen = await ShowMfgDllPickerAsync(card, Rtx40MfgService.IsPluginInstall(liveDllName) ? null : liveDllName);
                if (chosen == null) return;
            }

            installBtn.IsEnabled = false;
            installBtn.Content   = "Installing...";
            try
            {
                // Proxy → plugin: move it so the old proxy name is remembered for when OptiScaler is removed
                if (asPlugin && liveInstalled && !Rtx40MfgService.IsPluginInstall(liveDllName))
                {
                    var from  = liveDllName!;
                    var moved = await Task.Run(() => mfgSvc.MoveToOptiScalerPlugin(installPath, from));
                    if (moved != null) liveDllName = moved;
                }

                // If reinstalling under a different name, remove the old one first
                if (liveInstalled && !string.IsNullOrEmpty(liveDllName)
                    && !liveDllName.Equals(chosen, StringComparison.OrdinalIgnoreCase))
                    mfgSvc.Uninstall(installPath, liveDllName);

                bool ok = await Task.Run(async () =>
                {
                    if (!mfgSvc.IsStagingReady || mfgSvc.HasUpdate)
                        await mfgSvc.EnsureStagingAsync().ConfigureAwait(false);
                    if (!asPlugin) // standalone XeMFG; through OptiScaler it uses OptiScaler's XeSS files
                        await mfgSvc.EnsureXessStagingAsync().ConfigureAwait(false);
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
        var cogBtn = CogDialog.MakeCogButton("Universal MFG Unlocker settings");
        cogBtn.Click += async (s, e) =>
        {
            try
            {
                var cog = new CogDialog("Universal MFG Unlocker", _window.Content.XamlRoot);
                cog.AddIntro("Press Backspace in-game to open the Universal MFG Unlocker menu. It offers these modes:");
                cog.AddNote("• Follow game: uses the game's own MFG setting");
                cog.AddNote("• Fixed 2x to 6x: forces a specific multiplier");
                cog.AddNote("• Dynamic: targets the display refresh rate or a custom FPS value");
                cog.AddIntro("If frames freeze above 2x, set the Frame Generation preset to B in this game's DLSS / Streamline section.");
                if (isInstalled)
                    cog.AddNote($"Currently installed as: {currentDllName}");

                if (isPluginInstall)
                {
                    cog.AddSection("Loaded by OptiScaler");
                    cog.AddIntro("OptiScaler loads the mod as a plugin, as its author recommends. Frame generation is chosen in OptiScaler, not here: "
                        + "open OptiScaler's ⚙ and set FG Output to DLSSG for DLSS multi frame generation, or XeFG for XeMFG. "
                        + "OptiScaler's default (Auto) runs no frame generation.");
                    cog.AddNote("The mod's own menu still opens beside OptiScaler's for the DLSS presets and DLSS 5. If OptiScaler is removed, RHI moves the mod back next to the game's exe.");
                }

                cog.AddSection("XeSS frame generation (XeMFG)");
                if (isPluginInstall)
                    cog.AddIntro("Through OptiScaler, XeMFG is OptiScaler's FG Output set to XeFG. It uses the XeSS files OptiScaler installs, so nothing else is needed.");
                else
                    cog.AddIntro("The mod can also use Intel's XeSS frame generation, 2x to 6x on any GPU (experimental). Tick \"XeMFG standalone\" in the in-game menu and restart the game. "
                        + "It needs Intel's libxess_fg.dll and libxell.dll in an RTXMFG-XeSS folder next to the mod; RHI installs them with the mod.");
                if (isInstalled && !isPluginInstall && !string.IsNullOrEmpty(installPath))
                {
                    var xessDir = System.IO.Path.Combine(installPath, Rtx40MfgService.XessFolderName);
                    bool xessPresent = System.IO.File.Exists(System.IO.Path.Combine(xessDir, "libxess_fg.dll"))
                                    && System.IO.File.Exists(System.IO.Path.Combine(xessDir, "libxell.dll"));
                    if (xessPresent)
                        cog.AddNote("XeSS files: installed.");
                    else
                        cog.AddWarning("XeSS files: missing. Reinstall the mod to add them.");
                }
                if (string.Equals(card.RsInstalledFile, "dxgi.dll", StringComparison.OrdinalIgnoreCase))
                    cog.AddWarning("ReShade is installed as dxgi.dll in this game. With XeMFG at 5x and 6x, some generated frames may not reach the screen. "
                        + "If that happens, give ReShade a different DLL name in Game Overrides › DLL naming overrides.");
                cog.AddButtons(cog.MakeButton("Open GitHub page", CogButtonKind.Secondary,
                    async () => { await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/dashdogy/MFG-Unlocker")); }));
                await cog.ShowAsync();
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[BuildRtx40MfgRow] Settings dialog failed — {ex.Message}");
            }
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
        ToolTipService.SetToolTip(removeBtn, "Remove Universal MFG Unlocker from this game");
        removeBtn.Click += (s, e) =>
        {
            if (string.IsNullOrEmpty(installPath)) return;
            // Live location: it may have moved in or out of OptiScaler\plugins since the panel was built
            mfgSvc.Uninstall(installPath, _window.ViewModel.GetRtx40MfgInstalledAs(gameName, store) ?? currentDllName);
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
            Title = "Choose Universal MFG Unlocker DLL name",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Select the filename to deploy the Universal MFG Unlocker DLL as. Choose a name the game loads early, and avoid names already used by other mods.",
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
