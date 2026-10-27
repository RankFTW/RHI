// DetailPanelBuilder.Extras.AsiLoaders.cs — Extras section: Ultimate ASI Loader and RTX Encore rows with their DLL pickers.

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
    private void BuildUalRow(GameCardViewModel card, StackPanel body)
    {
        _window.ViewModel.SetLastUiAction($"BuildUalRow({card.GameName})");
        var ualSvc    = _window.ViewModel.UalServiceInstance;
        var gameName  = card.GameName;
        var store     = card.Source ?? "";
        var installPath = card.InstallPath ?? "";

        // Detect current install state
        var ualRecord   = string.IsNullOrEmpty(installPath) ? null
            : _auxInstallService.FindRecord(gameName, installPath, UltimateAsiLoaderService.AddonType);
        bool isInstalled = ualRecord != null;
        string? installedAs = ualRecord?.InstalledAs;

        // Consistency guard: if the dict says installed but FindRecord finds no record,
        // the aux record was lost (e.g. Game Pass path version change). Clear the stale dict entry
        // so the collapsed header doesn't show "ASI Loader winmm.dll" when nothing is actually installed.
        if (!isInstalled && !string.IsNullOrEmpty(_window.ViewModel.GetUalInstalledAs(gameName, store)))
        {
            CrashReporter.Log($"[BuildUalRow] Clearing stale UalInstalledAs entry for '{gameName}' — dict said installed but no aux record found");
            _window.ViewModel.SetUalInstalledAs(gameName, null, store);
        }

        // Status text
        string statusText;
        string statusColor;
        if (isInstalled)
        {
            var staged = card.Is32Bit ? ualSvc.StagedVersion32 : ualSvc.StagedVersion64;
            statusText  = staged ?? "Installed";
            statusColor = "#5ECB7D";
        }
        else
        {
            statusText  = "Ready";
            statusColor = "#A0AABB";
        }

        // ── Row grid matching Components section exactly ───────────────────────
        // Col 0: label (120)  Col 1: status (80)  Col 2: Info (36)
        // Col 3: install (*)  Col 4: cog (36)     Col 5: delete (36)
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
            Text = "ASI Loader",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(label, "Ultimate ASI Loader — proxy DLL that loads .asi plugins into game processes.");
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
            ToolTipService.SetToolTip(statusBlock, $"Installed as: {installedAs}\nClick to open GitHub releases");
            statusBlock.PointerPressed += (s, e) =>
                _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/ThirteenAG/Ultimate-ASI-Loader/releases"));
        }
        Grid.SetColumn(statusBlock, 1);
        row.Children.Add(statusBlock);

        // Col 2 — Info button (matches Components style)
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
        ToolTipService.SetToolTip(infoBtn, "Open Ultimate ASI Loader GitHub releases page");
        infoBtn.Click += (s, e) =>
            _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/ThirteenAG/Ultimate-ASI-Loader/releases"));
        Grid.SetColumn(infoBtn, 2);
        row.Children.Add(infoBtn);

        // Col 3 — Install button
        var installBtn = new Button
        {
            Content = isInstalled ? "↺  Reinstall ASI Loader" : "⬇  Install ASI Loader",
            FontSize = 12,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
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
        ToolTipService.SetToolTip(installBtn, isInstalled
            ? $"Reinstall Ultimate ASI Loader (currently '{installedAs}')"
            : "Install Ultimate ASI Loader — choose which DLL name to use");
        installBtn.Click += async (s, e) =>
        {
            if (string.IsNullOrEmpty(installPath)) return;
            var chosen = await ShowUalDllPickerAsync(card, ualRecord?.InstalledAs);
            if (chosen == null) return;

            installBtn.IsEnabled = false;
            installBtn.Content = "Installing...";
            try
            {
                var (success, hookedOriginal) = await ualSvc.InstallAsync(card, chosen);
                if (success)
                {
                    _window.ViewModel.SetUalInstalledAs(gameName, chosen, store);
                    if (hookedOriginal != null)
                    {
                        _ = DialogService.ShowSafeAsync(new ContentDialog
                        {
                            Title = "Original DLL chained",
                            Content = $"The existing '{chosen}' was renamed to '{hookedOriginal}' so ASI Loader can chain-load it automatically.",
                            CloseButtonText = "OK",
                            XamlRoot = _window.Content.XamlRoot,
                        });
                    }
                    RequestExtrasRebuild(card);
                }
                else
                {
                    installBtn.Content = "❌ Install failed";
                }
            }
            finally { installBtn.IsEnabled = true; }
        };
        Grid.SetColumn(installBtn, 3);
        row.Children.Add(installBtn);

        // Col 4 — Cog (empty for now, matches Components cog size/position)
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
            Tag = card,
        };
        ToolTipService.SetToolTip(cogBtn, "ASI Loader settings (coming soon)");
        cogBtn.Click += async (s, e) =>
        {
            // Placeholder — settings dialog will be added later
            var dlg = new ContentDialog
            {
                Title = "ASI Loader Settings",
                Content = new TextBlock { Text = "No settings available yet.", FontSize = 12 },
                CloseButtonText = "Close",
                XamlRoot = _window.Content.XamlRoot,
            };
            await DialogService.ShowSafeAsync(dlg);
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
        ToolTipService.SetToolTip(removeBtn, "Remove Ultimate ASI Loader from this game");
        removeBtn.Click += (s, e) =>
        {
            if (string.IsNullOrEmpty(installPath)) return;
            ualSvc.Uninstall(card);
            _window.ViewModel.SetUalInstalledAs(gameName, null, store);
            RequestExtrasRebuild(card);
        };
        Grid.SetColumn(removeBtn, 5);
        row.Children.Add(removeBtn);

        body.Children.Add(row);
    }

    private async Task<string?> ShowUalDllPickerAsync(GameCardViewModel card, string? currentDllName)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return null;

        var names = card.Is32Bit
            ? UltimateAsiLoaderService.Win32Names
            : UltimateAsiLoaderService.Win64Names;

        // Collect files already in the game folder
        var existingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (Directory.Exists(card.InstallPath))
                foreach (var f in Directory.GetFiles(card.InstallPath, "*.dll"))
                    existingFiles.Add(Path.GetFileName(f));
        }
        catch (Exception ex) { CrashReporter.Log($"[DetailPanelBuilder.ShowUalDllPickerAsync] Failed to list DLLs in '{card.InstallPath}' — {ex.Message}"); }

        // RHI-managed filenames to flag as conflict
        var rhiOwned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(card.RsInstalledFile))  rhiOwned.Add(card.RsInstalledFile);
        if (!string.IsNullOrEmpty(card.OsInstalledFile))  rhiOwned.Add(card.OsInstalledFile);
        if (!string.IsNullOrEmpty(card.DcInstalledFile))  rhiOwned.Add(card.DcInstalledFile);

        string? chosen = null;

        var listPanel = new StackPanel { Spacing = 4 };
        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 420,
            Content = listPanel,
        };

        foreach (var name in names)
        {
            bool isRecommended = UltimateAsiLoaderService.RecommendedNames.Contains(name, StringComparer.OrdinalIgnoreCase);
            bool isRhiConflict = UltimateAsiLoaderService.RhiConflictNames.Contains(name, StringComparer.OrdinalIgnoreCase);
            bool isTaken       = existingFiles.Contains(name) && !rhiOwned.Contains(name) && name != currentDllName;
            bool isRhiOwned    = rhiOwned.Contains(name);
            bool isCurrent     = string.Equals(name, currentDllName, StringComparison.OrdinalIgnoreCase);

            var btn = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10, 6, 10, 6),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                IsEnabled = !isRhiOwned,
                Opacity = isRhiOwned ? 0.4 : 1.0,
            };

            // Styling
            if (isCurrent)
            {
                btn.Background   = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush);
                btn.BorderBrush  = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush);
            }
            else
            {
                btn.Background  = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush);
                btn.BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush);
            }

            // Content: name + badges
            var contentRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            contentRow.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = 12,
                Foreground = isTaken || isRhiOwned
                    ? UIFactory.Brush(ResourceKeys.TextTertiaryBrush)
                    : UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
                VerticalAlignment = VerticalAlignment.Center,
            });

            if (isRecommended)
                contentRow.Children.Add(MakeBadge("Recommended", "#1A3A20", "#6AE87A", "#2A5A30"));
            if (isTaken)
                contentRow.Children.Add(MakeBadge("In use", "#2A1818", "#CC6666", "#5A2828"));
            if (isRhiOwned)
                contentRow.Children.Add(MakeBadge("Used by RHI", "#2A1818", "#CC6666", "#5A2828"));
            if (isRhiConflict && !isRhiOwned)
                contentRow.Children.Add(MakeBadge("May conflict with ReShade/OS", "#2A1A10", "#CC9955", "#5A3A18"));
            if (isCurrent)
                contentRow.Children.Add(MakeBadge("Current", "#182840", "#7AACDD", "#2A4468"));

            btn.Content = contentRow;

            // Tooltip for taken files
            if (isTaken)
                ToolTipService.SetToolTip(btn, $"'{name}' already exists in the game folder. Selecting it will rename the existing file to '{Path.GetFileNameWithoutExtension(name)}Hooked.dll' so ASI Loader can chain-load it.");
            else if (isRhiOwned)
                ToolTipService.SetToolTip(btn, "This filename is already used by an RHI-managed component (ReShade, OptiScaler, or DC). Choose a different name.");

            btn.Tag = name;
            btn.Click += (s, ev) =>
            {
                chosen = (s as Button)?.Tag as string;
                // Close the dialog by finding and closing it
                if (s is FrameworkElement fe)
                {
                    var dialog = FindParentContentDialog(fe);
                    dialog?.Hide();
                }
            };

            listPanel.Children.Add(btn);
        }

        var dialog = new ContentDialog
        {
            Title = "Choose ASI Loader DLL name",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Select the filename for ASI Loader. Most games work with version.dll or winmm.dll.",
                        FontSize = 11,
                        Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
                        TextWrapping = TextWrapping.Wrap,
                    },
                    scrollViewer,
                }
            },
            CloseButtonText = "Cancel",
            XamlRoot = _window.Content.XamlRoot,
        };

        await DialogService.ShowSafeAsync(dialog);
        return chosen;
    }

    // ── RTX Encore ────────────────────────────────────────────────────────────

    private void BuildRtxEncoreRow(GameCardViewModel card, StackPanel body)
    {
        _window.ViewModel.SetLastUiAction($"BuildRtxEncoreRow({card.GameName})");
        var svc         = App.Services.GetRequiredService<RtxEncoreService>();
        var gameName    = card.GameName;
        var store       = card.Source ?? "";
        var installPath = card.InstallPath ?? "";

        var currentDllName = _window.ViewModel.GetRtxEncoreInstalledAs(gameName, store);
        bool isInstalled   = svc.IsInstalledIn(installPath, currentDllName);
        bool nrDeployed    = _window.ViewModel.GetRtxEncoreNrDllDeployed(gameName, store);

        string statusText  = isInstalled ? (svc.StagedVersion ?? "Installed") : "Ready";
        string statusColor = isInstalled ? "#5ECB7D" : "#A0AABB";

        _window.ViewModel.SetLastUiAction($"BuildRtxEncoreRow:BuildGrid({card.GameName})");

        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(PanelColW(1, 0, 350, _window.ExtrasContainer.ActualWidth)) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

        var label = new TextBlock { Text = "RTX Encore (Beta)", FontSize = 12, Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush), VerticalAlignment = VerticalAlignment.Center };
        ToolTipService.SetToolTip(label, "RTX Encore — frame generation X2-X6, Smooth Motion and Neural Rendering for RTX 20/30/40.");
        Grid.SetColumn(label, 0); row.Children.Add(label);

        var statusBlock = new TextBlock { Text = statusText, FontSize = 12, Foreground = UIFactory.GetBrush(statusColor), VerticalAlignment = VerticalAlignment.Center, HorizontalTextAlignment = Microsoft.UI.Xaml.TextAlignment.Center, TextDecorations = isInstalled ? Windows.UI.Text.TextDecorations.Underline : Windows.UI.Text.TextDecorations.None };
        if (isInstalled) { ToolTipService.SetToolTip(statusBlock, $"Installed as: {currentDllName}\nClick to open GitHub releases"); statusBlock.PointerPressed += (s, e) => _ = Windows.System.Launcher.LaunchUriAsync(new Uri(RtxEncoreService.RepoUrl + "/releases")); }
        Grid.SetColumn(statusBlock, 1); row.Children.Add(statusBlock);

        var infoBtn = new Button { Content = "Info", FontSize = 11, Padding = new Thickness(6, 2, 6, 2), Width = 36, Height = 32, Background = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush), Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush), BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8) };
        ToolTipService.SetToolTip(infoBtn, "Open RTX Encore GitHub page");
        infoBtn.Click += (s, e) => _ = Windows.System.Launcher.LaunchUriAsync(new Uri(RtxEncoreService.RepoUrl));
        Grid.SetColumn(infoBtn, 2); row.Children.Add(infoBtn);

        string installBtnLabel = isInstalled ? "↺  Reinstall RTX Encore (Beta)" : "⬇  Install RTX Encore (Beta)";
        var installBtn = new Button { Content = installBtnLabel, FontSize = 12, Height = 32, HorizontalAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(8), Background = isInstalled ? UIFactory.GetBrush("#182840") : UIFactory.Brush(ResourceKeys.AccentBlueBgBrush), Foreground = isInstalled ? UIFactory.GetBrush("#7AACDD") : UIFactory.Brush(ResourceKeys.AccentBlueBrush), BorderBrush = isInstalled ? UIFactory.GetBrush("#2A4468") : UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush), BorderThickness = new Thickness(1) };
        ToolTipService.SetToolTip(installBtn, isInstalled ? $"Reinstall RTX Encore (currently '{currentDllName}')" : "Install RTX Encore — choose which DLL name to use");
        installBtn.Click += async (s, e) =>
        {
            if (string.IsNullOrEmpty(installPath)) return;
            var chosen = await ShowRtxEncoreDllPickerAsync(card, currentDllName);
            if (chosen == null) return;
            installBtn.IsEnabled = false; installBtn.Content = "Installing...";
            try
            {
                bool ok = await svc.InstallAsync(installPath, chosen, currentDllName);
                if (ok)
                {
                    _window.ViewModel.SetRtxEncoreInstalledAs(gameName, chosen, store);

                    // If the user had NR DLL deployment enabled in the cog, deploy it now
                    if (_window.ViewModel.GetRtxEncoreNrDllDeployed(gameName, store))
                    {
                        // Ensure 310.8.0 is cached — try with (50xx) suffix first (manifest key), then bare version
                        var dlssS = App.Services.GetRequiredService<IDlssStreamlineService>();
                        var cachedNr = svc.GetNrDllCachedPath()
                                    ?? await dlssS.EnsureSpecificDlssnrCachedAsync(RtxEncoreService.NrRequiredVer + " (50xx)").ConfigureAwait(false)
                                    ?? await dlssS.EnsureSpecificDlssnrCachedAsync(RtxEncoreService.NrRequiredVer).ConfigureAwait(false);
                        if (cachedNr != null)
                            svc.DeployNrDll(installPath);
                        else
                            CrashReporter.Log("[RtxEncoreInstall] NR DLL 310.8.0 not available — skipping NR deploy");
                    }

                    RequestExtrasRebuild(card);
                }
                else { installBtn.Content = "Install failed"; installBtn.IsEnabled = true; }
            }
            catch (Exception ex) { CrashReporter.Log($"[BuildRtxEncoreRow] Install failed — {ex.Message}"); installBtn.Content = "Install failed"; installBtn.IsEnabled = true; }
        };
        Grid.SetColumn(installBtn, 3); row.Children.Add(installBtn);

        var cogBtn = new Button { Width = 36, Height = 32, Padding = new Thickness(0), Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush), Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush), BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Content = new TextBlock { Text = "⚙", FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center } };
        ToolTipService.SetToolTip(cogBtn, "RTX Encore settings");
        cogBtn.Click += async (s, e) =>
        {
            var content = new StackPanel { Spacing = 8 };
            content.Children.Add(new TextBlock { Text = "Neural Rendering (nvngx_dlssnr.dll)", FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush) });
            content.Children.Add(new TextBlock { Text = "RTX Encore requires nvngx_dlssnr.dll version 310.8.0 exactly. Other versions are refused.", FontSize = 11, Foreground = UIFactory.Brush(ResourceKeys.InlineDescriptionBrush), TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap });
            var nrCachedPath = svc.GetNrDllCachedPath();
            bool nrAvailable = nrCachedPath != null;

            var nrRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 4, 0, 0) };
            nrRow.Children.Add(new TextBlock { Text = "Deploy NR DLL", FontSize = 11, Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush), VerticalAlignment = VerticalAlignment.Center });
            var nrCombo = new ComboBox { FontSize = 11, MinWidth = 80 };
            nrCombo.Items.Add("No"); nrCombo.Items.Add("Yes");
            nrCombo.SelectedIndex = nrDeployed ? 1 : 0;
            ToolTipService.SetToolTip(nrCombo, nrAvailable
                ? "Deploy nvngx_dlssnr.dll 310.8.0 alongside RTX Encore for Neural Rendering support."
                : "nvngx_dlssnr.dll 310.8.0 not yet cached — selecting Yes will download it automatically.");
            nrRow.Children.Add(nrCombo); content.Children.Add(nrRow);
            if (nrAvailable)
                content.Children.Add(new TextBlock { Text = $"Source: {nrCachedPath}", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap });
            else
                content.Children.Add(new TextBlock { Text = "310.8.0 not yet downloaded — will download on Apply.", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap });
            var cogDlg = new ContentDialog { Title = "RTX Encore Settings", Content = new ScrollViewer { Content = content, MaxHeight = 480, Padding = new Thickness(0, 0, 16, 0) }, PrimaryButtonText = "Apply", CloseButtonText = "Cancel", XamlRoot = _window.Content.XamlRoot, RequestedTheme = ElementTheme.Dark };
            var dlgResult = await DialogService.ShowSafeAsync(cogDlg);

            if (dlgResult != ContentDialogResult.Primary) return;
            bool enableNr = nrCombo.SelectedIndex == 1;

            // Always persist the preference, regardless of install state
            if (enableNr != nrDeployed)
                _window.ViewModel.SetRtxEncoreNrDllDeployed(gameName, enableNr, store);

            if (!isInstalled) return; // can't deploy to disk yet — preference is saved, deploy happens at install

            if (enableNr && !nrDeployed)
            {
                // Ensure 310.8.0 is cached — try exact match first, then with (50xx) suffix
                var dlssSvc = App.Services.GetRequiredService<IDlssStreamlineService>();
                var cachedNrPath = svc.GetNrDllCachedPath()
                                ?? await dlssSvc.EnsureSpecificDlssnrCachedAsync(RtxEncoreService.NrRequiredVer + " (50xx)").ConfigureAwait(false)
                                ?? await dlssSvc.EnsureSpecificDlssnrCachedAsync(RtxEncoreService.NrRequiredVer).ConfigureAwait(false);
                if (cachedNrPath == null)
                {
                    CrashReporter.Log("[RtxEncoreCog] 310.8.0 not available and download failed — NR DLL not deployed");
                    return;
                }
                bool ok = svc.DeployNrDll(installPath);
                if (!ok) { _window.ViewModel.SetRtxEncoreNrDllDeployed(gameName, false, store); } // revert if deploy failed
                RequestExtrasRebuild(card);
            }
            else if (!enableNr && nrDeployed) { svc.RemoveNrDll(installPath); RequestExtrasRebuild(card); }
        };
        Grid.SetColumn(cogBtn, 4); row.Children.Add(cogBtn);

        var removeBtn2 = new Button { Width = 36, Height = 32, Padding = new Thickness(0), Background = UIFactory.Brush(ResourceKeys.AccentRedBgBrush), Foreground = UIFactory.Brush(ResourceKeys.AccentRedBrush), BorderBrush = UIFactory.Brush(ResourceKeys.AccentPurpleBorderBrush), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Content = new TextBlock { Text = "✕", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Foreground = UIFactory.Brush(ResourceKeys.AccentRedBrush) }, Opacity = isInstalled ? 1.0 : 0, IsHitTestVisible = isInstalled };
        ToolTipService.SetToolTip(removeBtn2, "Remove RTX Encore from this game");
        removeBtn2.Click += (s, e) =>
        {
            if (string.IsNullOrEmpty(installPath) || string.IsNullOrEmpty(currentDllName)) return;
            svc.Uninstall(installPath, currentDllName, removeNrDll: nrDeployed);
            _window.ViewModel.SetRtxEncoreInstalledAs(gameName, null, store);
            if (nrDeployed) _window.ViewModel.SetRtxEncoreNrDllDeployed(gameName, false, store);
            RequestExtrasRebuild(card);
        };
        Grid.SetColumn(removeBtn2, 5); row.Children.Add(removeBtn2);

        body.Children.Add(row);
        _window.ViewModel.SetLastUiAction($"BuildRtxEncoreRow:Done({card.GameName})");
    }

    private async Task<string?> ShowRtxEncoreDllPickerAsync(GameCardViewModel card, string? currentDllName)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return null;
        var rhiConflicts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(card.RsInstalledFile)) rhiConflicts[card.RsInstalledFile] = "ReShade";
        if (!string.IsNullOrEmpty(card.OsInstalledFile)) rhiConflicts[card.OsInstalledFile] = "OptiScaler";
        if (!string.IsNullOrEmpty(card.DcInstalledFile)) rhiConflicts[card.DcInstalledFile] = "Display Commander";
        var ualName = _window.ViewModel.GetUalInstalledAs(card.GameName, card.Source ?? "");
        if (!string.IsNullOrEmpty(ualName)) rhiConflicts[ualName] = "ASI Loader";
        var mfgName = _window.ViewModel.GetRtx40MfgInstalledAs(card.GameName, card.Source ?? "");
        if (!string.IsNullOrEmpty(mfgName)) rhiConflicts[mfgName] = "RTX 40 MFG";
        var fgName = _window.ViewModel.GetDlssg2030InstalledAs(card.GameName, card.Source ?? "");
        if (!string.IsNullOrEmpty(fgName)) rhiConflicts[fgName] = "20/30 FG Unlock";
        if (!string.IsNullOrEmpty(currentDllName)) rhiConflicts.Remove(currentDllName);

        var onDiskConflicts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(card.InstallPath))
        {
            foreach (var name in RtxEncoreService.KnownProxyNames)
            {
                if (rhiConflicts.ContainsKey(name)) continue;
                if (string.Equals(name, currentDllName, StringComparison.OrdinalIgnoreCase)) continue;
                var diskPath = name.EndsWith(".asi", StringComparison.OrdinalIgnoreCase)
                    ? System.IO.Path.Combine(card.InstallPath, "plugins", name)
                    : System.IO.Path.Combine(card.InstallPath, name);
                if (File.Exists(diskPath))
                {
                    try { var fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(diskPath); onDiskConflicts[name] = !string.IsNullOrEmpty(fvi.FileDescription) ? fvi.FileDescription : !string.IsNullOrEmpty(fvi.ProductName) ? fvi.ProductName : "existing file"; }
                    catch { onDiskConflicts[name] = "existing file"; }
                }
            }
        }

        string? chosen = null;
        var listPanel = new StackPanel { Spacing = 4 };
        var scrollViewer = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 480, Content = listPanel };

        foreach (var name in RtxEncoreService.KnownProxyNames)
        {
            bool isRecommended = string.Equals(name, "version.dll", StringComparison.OrdinalIgnoreCase);
            bool isAsi         = name.EndsWith(".asi", StringComparison.OrdinalIgnoreCase);
            bool isCurrent     = string.Equals(name, currentDllName, StringComparison.OrdinalIgnoreCase);
            bool isRhiOwned    = rhiConflicts.ContainsKey(name);
            bool isOnDisk      = onDiskConflicts.ContainsKey(name);
            var btn = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 6, 10, 6), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), IsEnabled = !isRhiOwned, Opacity = isRhiOwned ? 0.4 : 1.0, Background = isCurrent ? UIFactory.Brush(ResourceKeys.AccentBlueBgBrush) : UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush), BorderBrush = isCurrent ? UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush) : UIFactory.Brush(ResourceKeys.BorderDefaultBrush) };
            var contentRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            contentRow.Children.Add(new TextBlock { Text = name, FontSize = 12, Foreground = isRhiOwned ? UIFactory.Brush(ResourceKeys.TextTertiaryBrush) : UIFactory.Brush(ResourceKeys.TextPrimaryBrush), VerticalAlignment = VerticalAlignment.Center });
            if (isRecommended) contentRow.Children.Add(MakeBadge("Recommended", "#1A3A20", "#6AE87A", "#2A5A30"));
            if (isAsi)         contentRow.Children.Add(MakeBadge("ASI plugin",  "#1A2A3A", "#7AACDD", "#2A4A6A"));
            if (isRhiOwned)    contentRow.Children.Add(MakeBadge($"In use by {rhiConflicts[name]}", "#2A1818", "#CC6666", "#5A2828"));
            else if (isOnDisk) contentRow.Children.Add(MakeBadge($"On disk: {onDiskConflicts[name]}", "#2A1A10", "#CC9955", "#5A3A18"));
            if (isCurrent)     contentRow.Children.Add(MakeBadge("Current", "#182840", "#7AACDD", "#2A4468"));
            btn.Content = contentRow;
            if (isRhiOwned)    ToolTipService.SetToolTip(btn, $"In use by {rhiConflicts[name]}. Choose a different name.");
            else if (isOnDisk) ToolTipService.SetToolTip(btn, $"A '{name}' already exists ({onDiskConflicts[name]}). RHI will back it up and restore it on uninstall.");
            btn.Tag = name;
            btn.Click += (s, ev) => { chosen = (s as Button)?.Tag as string; if (s is FrameworkElement fe) FindParentContentDialog(fe)?.Hide(); };
            listPanel.Children.Add(btn);
        }

        var pickerDialog = new ContentDialog
        {
            Title = "Choose RTX Encore DLL name",
            Content = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = "Select the filename for RTX Encore. Names in use by RHI components are disabled. Amber names already have a file on disk — RHI will back it up and restore on uninstall.", FontSize = 11, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), TextWrapping = TextWrapping.Wrap }, scrollViewer } },
            CloseButtonText = "Cancel", XamlRoot = _window.Content.XamlRoot, RequestedTheme = ElementTheme.Dark,
        };
        await DialogService.ShowSafeAsync(pickerDialog);
        return chosen;
    }
}
