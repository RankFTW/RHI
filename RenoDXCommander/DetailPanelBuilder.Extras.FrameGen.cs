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
        // Extras that can't be installed together (DetailPanelBuilder.Extras.Conflicts.cs)
        var blocker = ExtraBlocker(card, ExtraKind.Dlssg2030);

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
        ToolTipService.SetToolTip(label, "20/30 FG Unlock — turns on a game's greyed-out DLSS Frame Generation on RTX 20 and 30 (up to 6x where the game allows it), with NVIDIA's or Intel's XeSS frame generation model.\n"
            + "GPU: RTX 30 and RTX 20 (RTX 20 is less tested). RTX 40 and 50 work too: frame generation then runs the mod's own kernels instead of NVIDIA's. Needs NVIDIA driver R580 or newer and Hardware-accelerated GPU scheduling turned on in Windows.\n"
            + "Game: must already have DLSS Frame Generation. Games with an old DLSS Frame Generation (3.8 or older, for example The Witcher 3 next-gen) don't get frame generation; games whose own frame generation stops at 4x stay at 4x.\n"
            + "API: DirectX 12 only. No ASI Loader or ReShade needed. Not for online games with anti-cheat. Use one frame generation mod per game (not with OptiScaler's frame generation, DLSS Enabler or HSF).");
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

        if (blocker is { } fgBlocker)
        {
            ApplyExtraBlock(installBtn, label, ExtraKind.Dlssg2030, fgBlocker);
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
            try
            {
                // Fresh installs only: anti-cheat warning, closed-source notice, OptiScaler warning
                var xamlRoot = installBtn.XamlRoot ?? _window.Content.XamlRoot;
                var st = _window.ViewModel.Settings;
                if (!isInstalled)
                {
                    if (!await ConfirmAntiCheatAsync(card, "20/30 FG Unlock", AntiCheatNoticeKey.Dlssg2030, xamlRoot)) return;
                    if (!await ShowClosedSourceNoticeAsync("20/30 FG Unlock", "sdli1995",
                            () => st.Dlssg2030ClosedSourceDismissed, () => st.Dlssg2030ClosedSourceDismissed = true, xamlRoot))
                        return;
                    if (card.IsOsInstalled && !await ConfirmDlssg2030WithOptiScalerAsync(xamlRoot)) return;
                }

                var chosen = await ShowDlssg2030DllPickerAsync(card, currentDllName);
                if (chosen == null) return;

                installBtn.IsEnabled = false;
                installBtn.Content   = "Installing...";

                // Only one proxy may be in the folder: if reinstalling under a different name,
                // put the old slot back first (the ini and dlssg_sm86\ folder are reused)
                if (isInstalled && !string.IsNullOrEmpty(currentDllName)
                    && !currentDllName.Equals(chosen, StringComparison.OrdinalIgnoreCase))
                    AuxInstallService.SentinelRestore(Path.Combine(installPath, currentDllName));

                var (staged, ok) = await Task.Run(async () =>
                {
                    if (!svc.IsStagingReady || svc.HasUpdate)
                        await svc.EnsureStagingAsync().ConfigureAwait(false);
                    if (!svc.IsStagingReady) return (false, false);
                    // Same name as the current install: the file there is RHI's own proxy
                    return (true, svc.Install(installPath, chosen,
                        replacesOwnCopy: isInstalled && string.Equals(currentDllName, chosen, StringComparison.OrdinalIgnoreCase)));
                });

                if (ok)
                {
                    _window.ViewModel.SetDlssg2030InstalledAs(gameName, chosen, store);
                    RequestExtrasRebuild(card);
                }
                else
                {
                    CrashReporter.Log($"[BuildDlssg2030Row] '{gameName}': {(staged ? "install" : "download")} failed");
                    installBtn.Content   = staged ? "Install failed — see the log" : "Download failed — try again";
                    installBtn.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[BuildDlssg2030Row] Install failed — {ex.Message}");
                installBtn.Content   = "Install failed — see the log";
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

        string? chosen = null;

        var listPanel    = new StackPanel { Spacing = 4 };
        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 360,
            Content   = listPanel,
        };

        foreach (var option in GetExtraDllOptions(card, Dlssg20_30Service.ProxyNames, currentDllName))
        {
            var name            = option.Name;
            bool isRecommended  = option.IsRecommended;
            bool isRhiOwned     = option.BlockedBy != null;
            bool isCurrent      = option.IsCurrent;

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
            AddExtraDllBadges(contentRow, btn, option, "20/30 FG Unlock");
            if (isCurrent)
                contentRow.Children.Add(MakeBadge("Current", "#182840", "#7AACDD", "#2A4468"));

            btn.Content = contentRow;

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
                        Text = "Choose which proxy DLL the game loads. Start with version.dll; if frame generation doesn't appear in the game, reinstall with the next name in the list. dxgi.dll and d3d12.dll are a last resort and don't work in Vulkan games. "
                             + "To check it loaded, set Log detail to Details in the ⚙ and look for the game's exe name in the mod's log.",
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
        var  blocker       = ExtraBlocker(card, ExtraKind.UniversalMfg);
        var currentDllName = _window.ViewModel.GetRtx40MfgInstalledAs(gameName, store);
        bool isPluginInstall = isInstalled && Rtx40MfgService.IsPluginInstall(currentDllName); // loaded by OptiScaler
        bool isHsfMods       = isInstalled && Rtx40MfgService.IsHsfModsInstall(currentDllName); // loaded by HSF's helper
        bool hasHsf          = !string.IsNullOrEmpty(installPath)
            && (HsfService.IsInstalledIn(installPath, false) || HsfService.IsInstalledIn(installPath, true));

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
        ToolTipService.SetToolTip(label, "Universal MFG Unlocker (formerly RTX 40 MFG Unlock) — DLSS Multi Frame Generation (up to 6x) on RTX 20, 30 and 40, plus XeSS frame generation (XeMFG) on any GPU.\n"
            + "GPU: RTX 20, 30 and 40 for DLSS MFG (RTX 50 has it natively). XeMFG on NVIDIA and AMD, Intel Arc experimental.\n"
            + "Game: DLSS MFG needs the game's own DLSS Frame Generation. XeMFG standalone (experimental) also works with the game's DLSS, XeSS or FSR 3.1 upscaling. Not for games with anti-cheat.\n"
            + "API: DirectX 12 and Vulkan (DLSS MFG in Vulkan is experimental, without Dynamic MFG). In DirectX 11 games only its menu and DLSS 5 run.\n"
            + "No ReShade or ASI Loader needed. RHI also installs the XeSS files XeMFG needs.");
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

        if (blocker is { } mfgBlocker)
        {
            ApplyExtraBlock(installBtn, label, ExtraKind.UniversalMfg, mfgBlocker);
        }
        else
        {
            // Cheap check for the tooltip only; the click handler checks OptiScaler's install record
            bool osPluginLikely = card.IsOsInstalled && !string.IsNullOrEmpty(installPath)
                && System.IO.Directory.Exists(System.IO.Path.Combine(installPath, "OptiScaler"));
            ToolTipService.SetToolTip(installBtn,
                isHsfMods ? "Reinstall Universal MFG Unlocker in HSF_streamline\\mods (loaded by HSF's helper)"
                : isPluginInstall ? $"Reinstall Universal MFG Unlocker (loaded by OptiScaler from {currentDllName})"
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

            // Games on RHI's anti-cheat list: warn before a new install (not on reinstall / update)
            if (!liveInstalled && !await ConfirmAntiCheatAsync(card, "Universal MFG Unlocker",
                    AntiCheatNoticeKey.UniversalMfg, installBtn.XamlRoot))
                return;

            // Installed for HSF's helper (HSF_streamline\mods), or a 32-bit game with HSF (HSF's guide: never next to
            // a 32-bit exe): into HSF_streamline\mods, no name to pick
            if ((liveInstalled && Rtx40MfgService.IsHsfModsInstall(liveDllName)) || (hasHsf && card.Is32Bit))
            {
                installBtn.IsEnabled = false;
                installBtn.Content   = "Installing...";
                var hsfError = await vm.InstallMfgForHsfAsync(card, null);
                if (hsfError != null) CrashReporter.Log($"[BuildRtx40MfgRow] HSF mods reinstall failed — {hsfError}");
                RequestExtrasRebuild(card);
                return;
            }

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
                {
                    var oldName = liveDllName;
                    await Task.Run(() => mfgSvc.Uninstall(installPath, oldName)); // hashes RHI's RTX Hair files
                }

                bool ok = await Task.Run(async () =>
                {
                    if (!mfgSvc.IsStagingReady || mfgSvc.HasUpdate)
                        await mfgSvc.EnsureStagingAsync().ConfigureAwait(false);
                    if (!asPlugin) // standalone XeMFG; through OptiScaler it uses OptiScaler's XeSS files
                        await mfgSvc.EnsureXessStagingAsync().ConfigureAwait(false);
                    // dxcompiler.dll / dxil.dll for RTX Hair (DirectX 12 games on RTX 30 / 40)
                    await mfgSvc.PrepareRtxHairStagingAsync(installPath, chosen).ConfigureAwait(false);
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
                cog.AddNote("• Dynamic: targets the display refresh rate or a custom FPS value (not in Vulkan games)");
                cog.AddIntro("If the picture freezes above 2x, try Frame Generation preset B, in the mod's menu or in this game's DLSS / Streamline section "
                    + "(on RTX 30, restart the game after changing it).");
                if (isInstalled)
                    cog.AddNote($"Currently installed as: {currentDllName}");
                if (isInstalled && Rtx40MfgService.IsBinkName(currentDllName))
                    cog.AddNote($"The game's own {currentDllName} is kept beside it as {System.IO.Path.GetFileNameWithoutExtension(currentDllName)}Hooked.dll, as the mod needs. Removing the mod puts it back.");

                // Stable OptiScaler (0.9) beside a proxy install: the mod doesn't support it
                if (isInstalled && !isPluginInstall && !isHsfMods && card.IsOsInstalled && !string.IsNullOrEmpty(installPath)
                    && !System.IO.Directory.Exists(System.IO.Path.Combine(installPath, "OptiScaler")))
                    cog.AddWarning("Stable OptiScaler (0.9) isn't supported by the mod. Use OptiScaler nightly, F5 or DLSS NR.");

                if (isPluginInstall)
                {
                    cog.AddSection("Loaded by OptiScaler");
                    cog.AddIntro("OptiScaler loads the mod as a plugin, as its author recommends. Frame generation is chosen in OptiScaler, not here: "
                        + "open OptiScaler's ⚙ and set FG Output to DLSSG for DLSS multi frame generation, or XeFG for XeMFG. "
                        + "OptiScaler's default (Auto) runs no frame generation.");
                    cog.AddNote("With a recent OptiScaler nightly (4 November 2026 or newer) the DLSS presets and DLSS 5 are in an \"MFG Unlocker\" tab of OptiScaler's menu; "
                        + "older builds show the mod's menu beside OptiScaler's. If OptiScaler is removed, RHI moves the mod back next to the game's exe.");
                    cog.AddNote("On RTX 30, DLSS multi frame generation through OptiScaler is experimental; if it doesn't start, use XeFG.");
                    cog.AddNote("Through OptiScaler, XeFG works in DirectX 12 games. Vulkan games need the mod author's own OptiScaler build.");
                    cog.AddNote("OptiScaler's frame limit counts generated frames, so raise or remove it at high multipliers.");
                    cog.AddNote("XeMFG can also run as OptiScaler's DLSS frame generation, but only with the mod author's OptiScaler build. RHI doesn't install that build.");
                    cog.AddButtons(cog.MakeButton("Author's OptiScaler build", CogButtonKind.Secondary,
                        async () => { await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/dashdogy/OptiScaler/releases")); },
                        "Open the releases page of the mod author's OptiScaler build"));
                }

                if (isHsfMods || hasHsf)
                {
                    cog.AddSection("Used with HSF");
                    cog.AddIntro(isHsfMods
                        ? "Installed in HSF_streamline\\mods: HSF's helper loads the unlocker from there (32-bit games). "
                          + "Pick the multiplier in HSF's tab in ReShade's overlay. Removing HSF removes this copy too."
                        : "HSF is installed in this game. Pick the multiplier in HSF's tab in ReShade's overlay.");
                    cog.AddNote("No RTXMFG-XeSS folder: HSF's frame generation is DLSS, so XeMFG isn't used.");
                }

                cog.AddSection("XeSS frame generation (XeMFG)");
                if (isHsfMods || hasHsf)
                    cog.AddIntro("Not installed in games with HSF. If you want XeMFG here instead, remove HSF and reinstall the mod.");
                else if (isPluginInstall)
                    cog.AddIntro("Through OptiScaler, XeMFG is OptiScaler's FG Output set to XeFG. It uses the XeSS files OptiScaler installs, so nothing else is needed.");
                else
                {
                    cog.AddIntro("The mod can also run Intel's XeSS frame generation itself, 2x to 6x on any GPU (experimental, DirectX 12 and Vulkan). "
                        + "Tick XeMFG standalone in the in-game menu and pick the MFG mode. In games with DLSS Frame Generation, turn that on in the game; XeMFG replaces it. "
                        + "Other games need DLSS, XeSS or FSR 3.1 upscaling on (Vulkan: DLSS only), with the game's own FSR or XeSS frame generation off.");
                    cog.AddNote("It needs Intel's libxess_fg.dll and libxell.dll in an RTXMFG-XeSS folder next to the mod; RHI installs them with the mod.");
                }
                if (isInstalled && !isPluginInstall && !isHsfMods && !hasHsf && !string.IsNullOrEmpty(installPath))
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
                        + "If that happens, give ReShade a different DLL name in the ReShade ⚙ › ReShade DLL name.");

                AddUmfgRtxHairSection(cog, card, mfgSvc, isInstalled, isHsfMods);
                AddUmfgDlss5Section(cog, card, mfgSvc, isInstalled, isHsfMods);

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
        removeBtn.Click += async (s, e) =>
        {
            try
            {
                if (string.IsNullOrEmpty(installPath)) return;
                removeBtn.IsEnabled = false;
                // Live location: it may have moved in or out of OptiScaler\plugins since the panel was built
                var liveName = _window.ViewModel.GetRtx40MfgInstalledAs(gameName, store) ?? currentDllName;
                await Task.Run(() =>
                {
                    mfgSvc.Uninstall(installPath, liveName); // hashes RHI's RTX Hair files
                    mfgSvc.RemoveNrDll(installPath);          // the DLSS 5 NR DLL, if the mod installed it
                });
                _window.ViewModel.SetRtx40MfgInstalledAs(gameName, null, store);
                card.SetMfgState(mfgAdaInstalled: card.MfgAdaInstalled, mfgAdaRtx40Conflict: false, rtx40MfgInstalled: false);
                RequestExtrasRebuild(card);
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[BuildRtx40MfgRow] Remove failed — {ex.Message}");
                removeBtn.IsEnabled = true;
            }
        };
        Grid.SetColumn(removeBtn, 5);
        row.Children.Add(removeBtn);

        body.Children.Add(row);
    }

    private async Task<string?> ShowMfgDllPickerAsync(GameCardViewModel card, string? currentDllName)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return null;

        string? chosen = null;

        var listPanel    = new StackPanel { Spacing = 4 };
        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 420,
            Content   = listPanel,
        };

        // Bink names keep the game's own Bink DLL as <name>Hooked.dll, so they get no ASI Loader hooked option, and
        // they need the game's Bink DLL: without one they're disabled
        var installDir = card.InstallPath;
        var binkMissing = await Task.Run(() => MfgDllNames.Where(Rtx40MfgService.IsBinkName)
            .Where(n => !string.Equals(n, currentDllName, StringComparison.OrdinalIgnoreCase)
                && (!File.Exists(Path.Combine(installDir, n))
                    || File.Exists(Rtx40MfgService.BinkHookedPath(Path.Combine(installDir, n))))) // already another mod's Bink slot
            .ToHashSet(StringComparer.OrdinalIgnoreCase));
        foreach (var option in GetExtraDllOptions(card, MfgDllNames, currentDllName, noHooked: Rtx40MfgService.IsBinkName))
        {
            var name            = option.Name;
            bool isRecommended  = option.IsRecommended;
            bool noGameBink     = option.BlockedBy == null && binkMissing.Contains(name);
            bool isRhiOwned     = option.BlockedBy != null || noGameBink;
            bool isDxgiConflict = string.Equals(name, "dxgi.dll", StringComparison.OrdinalIgnoreCase)
                               && !string.IsNullOrEmpty(card.RsInstalledFile);
            bool isCurrent      = option.IsCurrent;

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
            AddExtraDllBadges(contentRow, btn, option, "Universal MFG Unlocker");
            if (!isRhiOwned && isDxgiConflict)
                contentRow.Children.Add(MakeBadge("May conflict with ReShade/OS", "#2A1A10", "#CC9955", "#5A3A18"));
            if (isCurrent)
                contentRow.Children.Add(MakeBadge("Current", "#182840", "#7AACDD", "#2A4468"));

            btn.Content = contentRow;

            if (noGameBink)
            {
                contentRow.Children.Add(MakeBadge("Game has no Bink DLL", "#2A1818", "#CC6666", "#5A2828"));
                ToolTipService.SetToolTip(btn, File.Exists(Rtx40MfgService.BinkHookedPath(Path.Combine(installDir, name)))
                    ? $"{System.IO.Path.GetFileNameWithoutExtension(name)}Hooked.dll is already there: another mod uses this Bink name."
                    : $"This game has no {name}. The mod's Bink route keeps the game's own {name} beside it as {System.IO.Path.GetFileNameWithoutExtension(name)}Hooked.dll, so it only works in games that ship one.");
            }
            else if (!isRhiOwned && Rtx40MfgService.IsBinkName(name) && !isCurrent)
                ToolTipService.SetToolTip(btn, $"RHI renames the game's {name} to {System.IO.Path.GetFileNameWithoutExtension(name)}Hooked.dll and keeps both, as the mod needs. Removing the mod puts it back.");
            if (!isRhiOwned && isDxgiConflict)
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
                        Text = "Choose a name the game loads early. Start with version.dll, then winmm.dll; if the mod's menu (Backspace) doesn't appear, reinstall with the next name. "
                             + "Vulkan games may not load DirectX names such as dxgi.dll or d3d12.dll.",
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
