// DetailPanelBuilder.Extras.cs — Extras section: Ultimate ASI Loader and future extras.

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
    public void BuildExtrasSection(GameCardViewModel card)
    {
        _window.ViewModel.SetLastUiAction($"BuildExtrasSection({card.GameName})");
        var __exSw = System.Diagnostics.Stopwatch.StartNew();
        _window.ExtrasPanel.Children.Clear();
        _window.ExtrasContainer.Visibility = Visibility.Visible;

        // ── Collapsible header ────────────────────────────────────────────────
        const string extrasSectionKey = "Extras";
        var exSettings   = _window.ViewModel.Settings;
        bool exCollapsed = exSettings.CollapsedDetailSections.Contains(extrasSectionKey);

        var exArrow = new TextBlock
        {
            Text      = exCollapsed ? "▶" : "▼",
            FontSize  = 10,
            Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
            VerticalAlignment = VerticalAlignment.Center,
            Margin    = new Thickness(0, 0, 6, 0),
        };
        var exTitle = new TextBlock
        {
            Text       = "Extras",
            FontSize   = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var exHeaderRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
        exHeaderRow.Children.Add(MakeDragHandle(_window.ExtrasContainer));
        exHeaderRow.Children.Add(exArrow);
        exHeaderRow.Children.Add(exTitle);
        _window.ExtrasPanel.Children.Add(exHeaderRow);

        var exBody = new StackPanel { Spacing = 10, Visibility = exCollapsed ? Visibility.Collapsed : Visibility.Visible };
        _window.ExtrasPanel.Children.Add(exBody);

        exHeaderRow.PointerEntered += (s, e) => exTitle.Foreground = UIFactory.Brush(ResourceKeys.AccentTealBrush);
        exHeaderRow.PointerExited  += (s, e) => exTitle.Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush);
        var exHandCursor  = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
        var exArrowCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Arrow);
        var exCursorProp  = DetailPanelBuilder.CursorProp;
        exHeaderRow.PointerEntered += (s, e) => exCursorProp?.SetValue(exHeaderRow, exHandCursor);
        exHeaderRow.PointerExited  += (s, e) => exCursorProp?.SetValue(exHeaderRow, exArrowCursor);

        // ── Collapsed summary (dict lookups only — no FS calls) ──────────────
        var vm = _window.ViewModel;
        var gn = card.GameName;
        var gs = card.Source ?? "";
        var installPath = card.InstallPath ?? "";
        var dgSvc = App.Services.GetRequiredService<DgVoodooService>();
        bool dgvInstalled = !string.IsNullOrEmpty(installPath) && dgSvc.IsDeployed(installPath);
        var exSummaryEntries = new List<(string, string?)>();
        if (!string.IsNullOrEmpty(vm.GetUalInstalledAs(gn, gs)))                           exSummaryEntries.Add(("ASI Loader", vm.GetUalInstalledAs(gn, gs)));
        if (vm.GetRtx40MfgInstalled(gn, gs))                                               exSummaryEntries.Add(("RTX 40 MFG", "On"));
        if (card.MfgAdaInstalled)                                                          exSummaryEntries.Add(("MFG Ada", "On"));
        if (vm.GetDlssg2030Installed(gn, gs))                                              exSummaryEntries.Add(("20/30 FG", "On"));
        if (card.IsOsInstalled)                                                             exSummaryEntries.Add(("OptiScaler", card.OsInstalledVersion));
        if (!string.IsNullOrEmpty(vm.GetDeInstalledAs(gn, gs)))                            exSummaryEntries.Add(("DLSS Enabler", "On"));
        if (card.IsDxvkInstalled)                                                           exSummaryEntries.Add(("DXVK", card.DxvkInstalledVersion ?? "On"));
        if (dgvInstalled)                                                                   exSummaryEntries.Add(("dgVoodoo2", vm.GetDgVoodooVersion(gn, gs) ?? _window.ViewModel.Manifest?.DgVoodooVersions?.Keys.FirstOrDefault()));
        var exSummary = DetailPanelBuilder.MakeSectionSummaryInlines(exSummaryEntries);
        if (exSummary != null)
        {
            exSummary.Visibility = exCollapsed ? Visibility.Visible : Visibility.Collapsed;
            exHeaderRow.Children.Add(exSummary);
        }

        exHeaderRow.PointerPressed += (s, e) =>
        {
            bool nowCollapsed = exBody.Visibility == Visibility.Visible;
            exBody.Visibility = nowCollapsed ? Visibility.Collapsed : Visibility.Visible;
            exArrow.Text = nowCollapsed ? "▶" : "▼";
            if (exSummary != null)
                exSummary.Visibility = nowCollapsed ? Visibility.Visible : Visibility.Collapsed;
            if (nowCollapsed) exSettings.CollapsedDetailSections.Add(extrasSectionKey);
            else              exSettings.CollapsedDetailSections.Remove(extrasSectionKey);
            _window.ViewModel.SaveSettingsPublic();
        };

        // ── Ultimate ASI Loader row ───────────────────────────────────────────
        var __t0 = __exSw.ElapsedMilliseconds;
        BuildUalRow(card, exBody);
        CrashReporter.Log($"[BuildExtrasSection] UalRow: {__exSw.ElapsedMilliseconds - __t0}ms '{card.GameName}'");

        // ── OptiScaler row ────────────────────────────────────────────────────
        __t0 = __exSw.ElapsedMilliseconds;
        BuildOsRow(card, exBody);
        CrashReporter.Log($"[BuildExtrasSection] OsRow: {__exSw.ElapsedMilliseconds - __t0}ms '{card.GameName}'");

        // ── MFG Unlocks separator ─────────────────────────────────────────────
        exBody.Children.Add(MakeExtrasSeparator("MFG Unlocks"));

        // ── RTX Encore row ────────────────────────────────────────────────────
        __t0 = __exSw.ElapsedMilliseconds;
        BuildRtxEncoreRow(card, exBody);
        CrashReporter.Log($"[BuildExtrasSection] RtxEncoreRow: {__exSw.ElapsedMilliseconds - __t0}ms '{card.GameName}'");

        // ── DLSS Enabler (standalone) row ─────────────────────────────────────
        __t0 = __exSw.ElapsedMilliseconds;
        BuildDlssEnablerRow(card, exBody);
        CrashReporter.Log($"[BuildExtrasSection] DlssEnablerRow: {__exSw.ElapsedMilliseconds - __t0}ms '{card.GameName}'");

        // ── 20/30 FG Unlock row ───────────────────────────────────────────────
        __t0 = __exSw.ElapsedMilliseconds;
        BuildDlssg2030Row(card, exBody);
        CrashReporter.Log($"[BuildExtrasSection] Dlssg2030Row: {__exSw.ElapsedMilliseconds - __t0}ms '{card.GameName}'");

        // ── RTX 40 MFG Unlock row ─────────────────────────────────────────────
        __t0 = __exSw.ElapsedMilliseconds;
        BuildRtx40MfgRow(card, exBody);
        CrashReporter.Log($"[BuildExtrasSection] Rtx40MfgRow: {__exSw.ElapsedMilliseconds - __t0}ms '{card.GameName}'");

        // ── MFG Ada Unlock row ────────────────────────────────────────────────
        __t0 = __exSw.ElapsedMilliseconds;
        BuildMfgAdaUnlockRow(card, exBody);
        CrashReporter.Log($"[BuildExtrasSection] MfgAdaRow: {__exSw.ElapsedMilliseconds - __t0}ms '{card.GameName}'");

        // ── API Upgrades sub-header + DXVK row ────────────────────────────────
        if (card.IsDxvkToggleVisible)
        {
            exBody.Children.Add(MakeExtrasSeparator("API Upgrades"));
            __t0 = __exSw.ElapsedMilliseconds;
            BuildDxvkRow(card, exBody);
            CrashReporter.Log($"[BuildExtrasSection] DxvkRow: {__exSw.ElapsedMilliseconds - __t0}ms '{card.GameName}'");
        }

        // ── dgVoodoo2 standalone row — DX9 games or already installed ─────────
        bool isDx9ForDgv = card.GraphicsApi == GraphicsApiType.DirectX9;
        // dgvInstalled already computed above for the summary
        if (isDx9ForDgv || dgvInstalled)
        {
            if (!card.IsDxvkToggleVisible) // Show separator only if DXVK row wasn't shown
                exBody.Children.Add(MakeExtrasSeparator("API Upgrades"));
            __t0 = __exSw.ElapsedMilliseconds;
            BuildDgVoodooRow(card, exBody);
            CrashReporter.Log($"[BuildExtrasSection] DgVoodooRow: {__exSw.ElapsedMilliseconds - __t0}ms '{card.GameName}'");
        }

        UpdateOsFeedback(card);
        __exSw.Stop();
        CrashReporter.Log($"[BuildExtrasSection] Total: {__exSw.ElapsedMilliseconds}ms '{card.GameName}'");
    }

    
    public void OnExtrasCardPropertyChanged(GameCardViewModel card, string? propertyName)
    {
        UpdateOsFeedback(card);

        if (propertyName is "IsOsInstalled" or "OsActionLabel" or "OsStatusText"
            or "OsStatusColor" or "OsDeleteVisibility" or "OsRowVisibility"
            or "OsInstallEnabled" or "OsBtnBackground" or "OsInstalledFile" or "Is32Bit")
        {
            RequestExtrasRebuild(card);
        }
    }

    
    public void UpdateOsFeedback(GameCardViewModel card)
    {
        _window.DetailOsProgress.Visibility = card.OsRowVisibility == Visibility.Visible ? card.OsProgressVisibility : Visibility.Collapsed;
        _window.DetailOsProgress.Value = card.OsProgress;
        _window.DetailOsMessage.Visibility = card.OsRowVisibility == Visibility.Visible ? card.OsMessageVisibility : Visibility.Collapsed;
        _window.DetailOsMessage.Text = card.OsActionMessage;
        _window.DetailOsMessage.Foreground = UIFactory.GetBrush(GetMessageColor(card.OsActionMessage));
    }

    
    private bool _extrasRebuildPending;

    public void RequestExtrasRebuild(GameCardViewModel card)
    {
        if (_extrasRebuildPending) return;
        _extrasRebuildPending = true;
        _window.DispatcherQueue.TryEnqueue(() =>
        {
            _extrasRebuildPending = false;
            if (_currentDetailCard != card) return;
            BuildExtrasSection(card);
        });
    }

    private static Grid MakeExtrasSeparator(string label)
    {
        var grid = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 4, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(350) }); // fixed — separator text is centred, exact width doesn't matter
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

        var text = new TextBlock
        {
            Text = $"———  {label}  ———",
            FontSize = 11,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(text, 3);
        grid.Children.Add(text);
        return grid;
    }

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

    private void BuildMfgAdaUnlockRow(GameCardViewModel card, StackPanel body)
    {
        _window.ViewModel.SetLastUiAction($"BuildMfgAdaUnlockRow({card.GameName})");
        var gameName    = card.GameName;
        var store       = card.Source ?? "";
        var installPath = card.InstallPath ?? "";

        const string DeployFileName  = "renodx-mfgunlock.addon64";
        var stagedPath  = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RHI", "addons", "MFG Ada Unlock.addon64");

        // Use cached state from card instead of File.Exists on UI thread
        bool isInstalled   = card.MfgAdaInstalled;
        bool rsInstalled   = card.IsRsInstalled;
        bool rtx40Conflict = card.Rtx40MfgInstalled;
        // Use AddonPackService.IsDownloaded instead of File.Exists
        bool staged        = _window.ViewModel.AddonPackServiceInstance.IsDownloaded("MFG Ada Unlock");

        var   addonVersion = AddonPackService.LoadAddonVersion("MFG Ada Unlock");
        string statusText  = isInstalled ? (string.IsNullOrEmpty(addonVersion) ? "Installed" : $"v{addonVersion}") : "Ready";
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
            Text = "MFG Ada Unlock",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(label, "MFG Ada Unlock — unlocks DLSS Multi Frame Generation (3x/4x+) on RTX 40-series GPUs. Requires ReShade. In-memory only, no files modified.");
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
            ToolTipService.SetToolTip(statusBlock, "Click to open GitHub releases");
            statusBlock.PointerPressed += (s, e) =>
                _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/mavismmg/MFGAdaUnlock-RenoDx/releases"));
        }
        Grid.SetColumn(statusBlock, 1);
        row.Children.Add(statusBlock);

        // Col 2 — Info button (always blue)
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
        ToolTipService.SetToolTip(infoBtn, "Open MFG Ada Unlock GitHub page");
        infoBtn.Click += (s, e) =>
            _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/mavismmg/MFGAdaUnlock-RenoDx"));
        Grid.SetColumn(infoBtn, 2);
        row.Children.Add(infoBtn);

        // Col 3 — Install button
        string btnLabel;
        bool btnEnabled = true;
        if (!rsInstalled)
        {
            btnLabel   = "⚠  ReShade required";
            btnEnabled = false;
        }
        else if (rtx40Conflict)
        {
            btnLabel   = "⚠  RTX 40 MFG installed";
            btnEnabled = false;
        }
        else if (!staged)
        {
            btnLabel   = "⬇  Install MFG Ada Unlock";
            btnEnabled = true; // will download on demand when clicked
        }
        else
        {
            btnLabel = isInstalled ? "↺  Reinstall MFG Ada Unlock" : "⬇  Install MFG Ada Unlock";
        }

        var installBtn = new Button
        {
            Content = btnLabel,
            FontSize = 12,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(8),
            Background = isInstalled ? UIFactory.GetBrush("#182840") : UIFactory.Brush(ResourceKeys.AccentBlueBgBrush),
            Foreground = isInstalled ? UIFactory.GetBrush("#7AACDD") : UIFactory.Brush(ResourceKeys.AccentBlueBrush),
            BorderBrush = isInstalled ? UIFactory.GetBrush("#2A4468") : UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush),
            BorderThickness = new Thickness(1),
            IsEnabled = btnEnabled,
            Opacity = btnEnabled ? 1.0 : 0.35,
        };

        if (!rsInstalled)
            ToolTipService.SetToolTip(installBtn, "Install ReShade first — MFG Ada Unlock requires it");
        else if (rtx40Conflict)
            ToolTipService.SetToolTip(installBtn, "RTX 40 MFG Unlock (ASI version) is already installed and conflicts. Remove it first.");

        installBtn.Click += async (s, e) =>
        {
            if (string.IsNullOrEmpty(installPath)) return;
            installBtn.IsEnabled = false;
            installBtn.Content   = "Downloading...";
            try
            {
                // Download on demand if not yet staged
                if (!File.Exists(stagedPath))
                {
                    var addonSvc = _window.ViewModel.AddonPackServiceInstance;
                    var entry = addonSvc.AvailablePacks.FirstOrDefault(p =>
                        p.PackageName.Equals("MFG Ada Unlock", StringComparison.OrdinalIgnoreCase));
                    if (entry != null)
                        await addonSvc.DownloadAddonAsync(entry).ConfigureAwait(false);
                }

                if (!File.Exists(stagedPath))
                {
                    CrashReporter.Log("[BuildMfgAdaUnlockRow] Staged file still not found after download attempt");
                    _window.DispatcherQueue?.TryEnqueue(() => installBtn.Content = "Download failed");
                    return;
                }

                var dest = Path.Combine(installPath, DeployFileName);
                File.Copy(stagedPath, dest, overwrite: true);
                CrashReporter.Log($"[BuildMfgAdaUnlockRow] Installed '{DeployFileName}' to '{installPath}'");
                _window.DispatcherQueue?.TryEnqueue(() =>
                {
                    card.SetMfgState(mfgAdaInstalled: true, mfgAdaRtx40Conflict: card.Rtx40MfgInstalled, rtx40MfgInstalled: card.Rtx40MfgInstalled);
                    RequestExtrasRebuild(card);
                });
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[BuildMfgAdaUnlockRow] Install failed — {ex.Message}");
                _window.DispatcherQueue?.TryEnqueue(() => { installBtn.IsEnabled = true; installBtn.Content = btnLabel; });
            }
        };
        Grid.SetColumn(installBtn, 3);
        row.Children.Add(installBtn);

        // Col 4 — Cog (placeholder for consistency)
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
        ToolTipService.SetToolTip(cogBtn, "MFG Ada Unlock settings");
        cogBtn.Click += async (s, e) =>
        {
            var dlg = new ContentDialog
            {
                Title = "MFG Ada Unlock",
                Content = new TextBlock
                {
                    Text = "MFG Ada Unlock unlocks DLSS Multi Frame Generation (3x/4x and above) on RTX 40-series GPUs.\n\n" +
                           "Configure via the ReShade overlay in-game.",
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                },
                PrimaryButtonText = "Open GitHub",
                CloseButtonText = "Close",
                XamlRoot = _window.Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            };
            var result = await DialogService.ShowSafeAsync(dlg);
            if (result == ContentDialogResult.Primary)
                _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/mavismmg/MFGAdaUnlock-RenoDx"));
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
        ToolTipService.SetToolTip(removeBtn, "Remove MFG Ada Unlock from this game");
        removeBtn.Click += (s, e) =>
        {
            if (string.IsNullOrEmpty(installPath)) return;
            try
            {
                var dest = Path.Combine(installPath, DeployFileName);
                if (File.Exists(dest)) File.Delete(dest);
                CrashReporter.Log($"[BuildMfgAdaUnlockRow] Removed '{DeployFileName}' from '{installPath}'");

                // Also remove from addon selections so SyncGameFolder doesn't redeploy it.
                // MFG Ada Unlock installed via Extras greys out the picker toggle — the user
                // can't deselect it there, so we must clean up the selection here.
                const string PackName = "MFG Ada Unlock";

                // Global selection
                var globalAddons = _window.ViewModel.Settings.EnabledGlobalAddons;
                if (globalAddons.Remove(PackName))
                {
                    _window.ViewModel.SaveSettingsPublic();
                    CrashReporter.Log($"[BuildMfgAdaUnlockRow] Removed '{PackName}' from global addon selection");
                }

                // Per-game selection (composite key first, then legacy name-only)
                var gameNameService = _window.ViewModel.GameNameServiceInstance;
                var compositeKey = Models.GameKey.FromCard(card.GameName, card.Source).ToKey();
                bool perGameChanged = false;
                if (gameNameService.PerGameAddonSelection.TryGetValue(compositeKey, out var perGame))
                    perGameChanged = perGame.Remove(PackName);
                if (!perGameChanged && gameNameService.PerGameAddonSelection.TryGetValue(card.GameName, out var perGameLegacy))
                    perGameChanged = perGameLegacy.Remove(PackName);
                if (perGameChanged)
                {
                    _window.ViewModel.SaveSettingsPublic();
                    CrashReporter.Log($"[BuildMfgAdaUnlockRow] Removed '{PackName}' from per-game addon selection for '{card.GameName}'");
                }

                card.SetMfgState(mfgAdaInstalled: false, mfgAdaRtx40Conflict: card.Rtx40MfgInstalled, rtx40MfgInstalled: card.Rtx40MfgInstalled);
                RequestExtrasRebuild(card);
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[BuildMfgAdaUnlockRow] Remove failed — {ex.Message}");
            }
        };
        Grid.SetColumn(removeBtn, 5);
        row.Children.Add(removeBtn);

        body.Children.Add(row);
    }

    private void BuildOsRow(GameCardViewModel card, StackPanel body)
    {
        _window.ViewModel.SetLastUiAction($"BuildOsRow({card.GameName})");
        // Only add the row when it should be visible
        if (card.OsRowVisibility != Visibility.Visible) return;

        bool osGreyed = card.Is32Bit;

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
            Text = "OptiScaler",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            VerticalAlignment = VerticalAlignment.Center,
            TextDecorations = osGreyed ? Windows.UI.Text.TextDecorations.Strikethrough : Windows.UI.Text.TextDecorations.None,
            Opacity = osGreyed ? 0.35 : 1.0,
            Tag = card,
        };
        Grid.SetColumn(label, 0);
        row.Children.Add(label);

        // Col 1 — status
        var statusBlock = new TextBlock
        {
            Text = card.OsStatusText,
            FontSize = 12,
            Foreground = UIFactory.GetBrush(card.OsStatusColor),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalTextAlignment = Microsoft.UI.Xaml.TextAlignment.Center,
            TextDecorations = osGreyed
                ? Windows.UI.Text.TextDecorations.Strikethrough
                : (card.IsOsInstalled ? Windows.UI.Text.TextDecorations.Underline : Windows.UI.Text.TextDecorations.None),
            Opacity = osGreyed ? 0.35 : 1.0,
        };
        if (card.IsOsInstalled && !osGreyed)
        {
            var osVariant = _window.ViewModel.GetOsVariant(card.GameName, card.Source ?? "");
            var osWikiUrl = osVariant == "DlssNr"
                ? "https://github.com/wilsjo2/OptiScaler-DLSSNR-PreSR-Multipass/releases"
                : "https://github.com/optiscaler/OptiScaler/wiki";
            ToolTipService.SetToolTip(statusBlock, "Click to open OptiScaler releases");
            statusBlock.PointerPressed += async (s, e) =>
                await Windows.System.Launcher.LaunchUriAsync(new Uri(osWikiUrl));
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
            CornerRadius = new CornerRadius(8),
            Tag = card,
            DataContext = AddonType.OptiScaler,
        };
        ApplyInfoButtonStyle(infoBtn, card, AddonType.OptiScaler);
        infoBtn.Click += (s, e) => _window.InfoButton_Click(s, e);
        Grid.SetColumn(infoBtn, 2);
        row.Children.Add(infoBtn);

        // Col 3 — Install button
        var installBtn = new Button
        {
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            CornerRadius = new CornerRadius(8),
            FontSize = 12,
            Background = UIFactory.GetBrush(card.OsBtnBackground),
            Foreground = UIFactory.GetBrush(card.OsBtnForeground),
            BorderBrush = UIFactory.GetBrush(card.OsBtnBorderBrush),
            BorderThickness = new Thickness(1),
            Tag = card,
            IsEnabled = card.OsInstallEnabled && !osGreyed,
            Opacity = osGreyed ? 0.35 : 1.0,
            IsHitTestVisible = !osGreyed,
        };
        installBtn.Content = WithInfoArrow(card.OsActionLabel, HasRealInfoContent(card, AddonType.OptiScaler), card.OsStatus == GameStatus.UpdateAvailable, installBtn);
        installBtn.Click += (s, e) => _window.InstallOsButton_Click(s, e);
        Grid.SetColumn(installBtn, 3);
        row.Children.Add(installBtn);

        // Col 4 — Cog (⚙) button
        var cogBtn = new Button
        {
            Width = 36,
            Height = 32,
            Padding = new Thickness(0),
            Background = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush),
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.BorderStrongBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Content = new TextBlock { Text = "⚙", FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center },
            Tag = card,
            IsEnabled = !osGreyed,
            Opacity = osGreyed ? 0.35 : 1.0,
        };
        ToolTipService.SetToolTip(cogBtn, "OptiScaler Settings");
        cogBtn.Click += (s, e) => _window.OsCogButton_ClickInternal(s, e);
        Grid.SetColumn(cogBtn, 4);
        row.Children.Add(cogBtn);

        // Col 5 — Delete (✕) button
        bool osShow = card.OsDeleteVisibility == Visibility.Visible;
        var deleteBtn = new Button
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
            Tag = card,
            Opacity = (osGreyed || !osShow) ? 0 : 1.0,
            IsHitTestVisible = osShow && !osGreyed,
        };
        ToolTipService.SetToolTip(deleteBtn, "Remove OptiScaler");
        deleteBtn.Click += (s, e) => _window.UninstallOsButton_Click(s, e);
        Grid.SetColumn(deleteBtn, 5);
        row.Children.Add(deleteBtn);

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
        catch { }

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

    /// <summary>Walks up the visual tree to find the parent ContentDialog.</summary>
    private static ContentDialog? FindParentContentDialog(DependencyObject element)
    {
        var parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element);
        while (parent != null)
        {
            if (parent is ContentDialog d) return d;
            parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(parent);
        }
        return null;
    }

    private static Border MakeBadge(string text, string bg, string fg, string border)
    {
        return new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(5, 1, 5, 1),
            Background = UIFactory.GetBrush(bg),
            BorderBrush = UIFactory.GetBrush(border),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = text,
                FontSize = 10,
                Foreground = UIFactory.GetBrush(fg),
            },
        };
    }

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

    private static readonly string[] DeDllNames =
        { "version.dll", "dxgi.dll", "winmm.dll", "dbghelp.dll", "psapi.dll", "winhttp.dll" };

    private void BuildDlssEnablerRow(GameCardViewModel card, StackPanel body)
    {
        _window.ViewModel.SetLastUiAction($"BuildDlssEnablerRow({card.GameName})");
        var deSvc       = App.Services.GetRequiredService<DlssEnablerService>();
        var gameName    = card.GameName;
        var store       = card.Source ?? "";
        var installPath = card.InstallPath ?? "";

        var currentDllName = _window.ViewModel.GetDeInstalledAs(gameName, store);
        _window.ViewModel.SetLastUiAction($"BuildDlssEnablerRow:IsInstalled({card.GameName})");
        bool isInstalled   = deSvc.IsStandaloneInstalledIn(installPath, currentDllName);

        // Mutual exclusivity with OptiScaler
        bool osConflict = card.IsOsInstalled;

        _window.ViewModel.SetLastUiAction($"BuildDlssEnablerRow:StagedVersion({card.GameName})");
        // Status text
        string statusText  = isInstalled ? (deSvc.StagedVersion ?? "Installed") : "Ready";
        string statusColor = isInstalled ? "#5ECB7D" : "#A0AABB";

        _window.ViewModel.SetLastUiAction($"BuildDlssEnablerRow:BuildGrid({card.GameName})");
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
            Text = "DLSS Enabler",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(label, "DLSS Enabler — standalone proxy DLL that enables DLSS in games that don't natively support it.");
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
            ToolTipService.SetToolTip(statusBlock, $"Installed as: {currentDllName}\nClick to open GitHub releases");
            statusBlock.PointerPressed += (s, e) =>
                _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/RankFTW/rhi-repo/releases"));
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
        ToolTipService.SetToolTip(infoBtn, "Open DLSS Enabler Nexus page");
        infoBtn.Click += (s, e) =>
            _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://www.nexusmods.com/site/mods/757"));
        Grid.SetColumn(infoBtn, 2);
        row.Children.Add(infoBtn);

        // Col 3 — Install button
        string installBtnLabel;
        if (osConflict)
            installBtnLabel = "Installed via OptiScaler";
        else
            installBtnLabel = isInstalled ? "↺  Reinstall DLSS Enabler" : "⬇  Install DLSS Enabler";

        var installBtn = new Button
        {
            Content = installBtnLabel,
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

        if (osConflict)
        {
            installBtn.IsEnabled = false;
            installBtn.IsHitTestVisible = false;
            installBtn.Opacity = 0.35;
            ToolTipService.SetToolTip(installBtn, "Cannot install alongside OptiScaler — DLSS Enabler is already included within OptiScaler");
        }
        else
        {
            ToolTipService.SetToolTip(installBtn, isInstalled
                ? $"Reinstall DLSS Enabler (currently '{currentDllName}')"
                : "Install DLSS Enabler — choose which DLL name to use");

            installBtn.Click += async (s, e) =>
            {
                if (string.IsNullOrEmpty(installPath)) return;
                var chosen = await ShowDeDllPickerAsync(card, currentDllName);
                if (chosen == null) return;

                installBtn.IsEnabled = false;
                installBtn.Content   = "Installing...";
                try
                {
                    bool ok = await deSvc.InstallStandaloneAsync(gameName, installPath, store, chosen, currentDllName);
                    if (ok)
                    {
                        _window.ViewModel.SetDeInstalledAs(gameName, chosen, store);
                        RequestExtrasRebuild(card);
                    }
                    else
                    {
                        installBtn.Content   = "Install failed";
                        installBtn.IsEnabled = true;
                    }
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[BuildDlssEnablerRow] Install failed — {ex.Message}");
                    installBtn.Content   = "Install failed";
                    installBtn.IsEnabled = true;
                }
            };
        }
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
            IsEnabled = !osConflict,
            IsHitTestVisible = !osConflict,
            Opacity = osConflict ? 0.35 : 1.0,
        };
        ToolTipService.SetToolTip(cogBtn, osConflict ? "Cannot install alongside OptiScaler" : "DLSS Enabler settings");
        cogBtn.Click += async (s, e) =>
        {
            var dlg = new ContentDialog
            {
                Title   = "DLSS Enabler Settings",
                Content = new TextBlock { Text = "No settings available.", FontSize = 12 },
                CloseButtonText   = "Close",
                XamlRoot          = _window.Content.XamlRoot,
                RequestedTheme    = ElementTheme.Dark,
            };
            await DialogService.ShowSafeAsync(dlg);
        };
        Grid.SetColumn(cogBtn, 4);
        row.Children.Add(cogBtn);

        // Col 5 — Remove button (hidden when not installed)
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
        ToolTipService.SetToolTip(removeBtn, "Remove standalone DLSS Enabler from this game");
        removeBtn.Click += (s, e) =>
        {
            if (string.IsNullOrEmpty(installPath) || string.IsNullOrEmpty(currentDllName)) return;
            deSvc.UninstallStandalone(installPath, currentDllName);
            _window.ViewModel.SetDeInstalledAs(gameName, null, store);
            RequestExtrasRebuild(card);
        };
        Grid.SetColumn(removeBtn, 5);
        row.Children.Add(removeBtn);

        body.Children.Add(row);
        _window.ViewModel.SetLastUiAction($"BuildDlssEnablerRow:Done({card.GameName})");
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

    private async Task<string?> ShowDeDllPickerAsync(GameCardViewModel card, string? currentDllName)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return null;

        // RHI-managed filenames to flag as conflict
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

        foreach (var name in DeDllNames)
        {
            bool isRecommended = string.Equals(name, "version.dll", StringComparison.OrdinalIgnoreCase);
            bool isRhiOwned    = rhiOwned.Contains(name);
            bool isDxgiConflict = string.Equals(name, "dxgi.dll", StringComparison.OrdinalIgnoreCase)
                               && !string.IsNullOrEmpty(card.RsInstalledFile);
            bool isCurrent     = string.Equals(name, currentDllName, StringComparison.OrdinalIgnoreCase);

            var btn = new Button
            {
                HorizontalAlignment        = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding         = new Thickness(10, 6, 10, 6),
                CornerRadius    = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                IsEnabled       = !isRhiOwned,
                Opacity         = isRhiOwned ? 0.4 : 1.0,
            };

            if (isCurrent)
            {
                btn.Background  = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush);
                btn.BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush);
            }
            else
            {
                btn.Background  = UIFactory.Brush(ResourceKeys.SurfaceOverlayBrush);
                btn.BorderBrush = UIFactory.Brush(ResourceKeys.BorderDefaultBrush);
            }

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
                ToolTipService.SetToolTip(btn, "This filename is already used by an RHI-managed component (ReShade, OptiScaler, or DC). Choose a different name.");
            else if (isDxgiConflict)
                ToolTipService.SetToolTip(btn, "dxgi.dll may conflict with ReShade or OS components if they also use this name.");

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
            Title = "Choose DLSS Enabler DLL name",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Select the filename for the standalone DLSS Enabler DLL. Most games work with version.dll.",
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

    private void BuildDxvkRow(GameCardViewModel card, StackPanel body)
    {
        _window.ViewModel.SetLastUiAction($"BuildDxvkRow({card.GameName})");
        // Col 0: label (120)  Col 1: status (80)  Col 2: Info (36)
        // Col 3: install (*)  Col 4: cog (36)     Col 5: delete (36)
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(PanelColW(1, 0, 350, _window.ExtrasContainer.ActualWidth)) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

        bool isDisabled = !card.IsDxvkToggleEnabled;

        // Col 0 — label
        var label = new TextBlock
        {
            Text = "DXVK",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = isDisabled ? 0.35 : 1.0,
            Tag = card,
        };
        if (card.DxvkToggleTooltip != null)
            ToolTipService.SetToolTip(label, card.DxvkToggleTooltip);
        Grid.SetColumn(label, 0);
        row.Children.Add(label);

        // Col 1 — status
        var statusBlock = new TextBlock
        {
            Text = card.DxvkStatusText,
            FontSize = 12,
            Foreground = UIFactory.GetBrush(card.DxvkStatusColor),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalTextAlignment = Microsoft.UI.Xaml.TextAlignment.Center,
            TextDecorations = card.IsDxvkInstalled
                ? Windows.UI.Text.TextDecorations.Underline
                : Windows.UI.Text.TextDecorations.None,
            Opacity = isDisabled ? 0.35 : 1.0,
        };
        if (card.IsDxvkInstalled && !isDisabled)
        {
            ToolTipService.SetToolTip(statusBlock, "Click to open DXVK releases");
            statusBlock.PointerPressed += (s, e) => _window.DetailDxvkStatus_PointerPressed(s, e);
            statusBlock.PointerEntered += (s, e) => _window.LinkText_PointerEntered(s, e);
            statusBlock.PointerExited  += (s, e) => _window.LinkText_PointerExited(s, e);
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
            CornerRadius = new CornerRadius(8),
            Background = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush),
            Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush),
            BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush),
            BorderThickness = new Thickness(1),
            Tag = card,
            Opacity = isDisabled ? 0.35 : 1.0,
        };
        infoBtn.Click += (s, e) => _window.DxvkInfoButton_Click(s, e);
        Grid.SetColumn(infoBtn, 2);
        row.Children.Add(infoBtn);

        // Col 3 — Install button
        var installBtn = new Button
        {
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            CornerRadius = new CornerRadius(8),
            FontSize = 12,
            Background = UIFactory.GetBrush(card.DxvkBtnBackground),
            Foreground = UIFactory.GetBrush(card.DxvkBtnForeground),
            BorderBrush = UIFactory.GetBrush(card.DxvkBtnBorderBrush),
            BorderThickness = new Thickness(1),
            Tag = card,
            IsEnabled = card.DxvkInstallEnabled && !isDisabled,
            Opacity = isDisabled ? 0.35 : 1.0,
            IsHitTestVisible = !isDisabled,
        };
        installBtn.Content = card.DxvkActionLabel;
        installBtn.Click += (s, e) => _window.InstallDxvkButton_Click(s, e);
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
            BorderBrush = UIFactory.Brush(ResourceKeys.BorderStrongBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Content = new TextBlock { Text = "⚙", FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center },
            Tag = card,
            IsEnabled = !isDisabled,
            Opacity = isDisabled ? 0.35 : 1.0,
        };
        ToolTipService.SetToolTip(cogBtn, "DXVK Settings");
        cogBtn.Click += (s, e) => _window.DxvkCogButton_Click(s, e);
        Grid.SetColumn(cogBtn, 4);
        row.Children.Add(cogBtn);

        // Col 5 — Delete button
        bool showDelete = card.DxvkDeleteVisibility == Visibility.Visible;
        var deleteBtn = new Button
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
            Tag = card,
            Opacity = showDelete ? 1.0 : 0.0,
            IsHitTestVisible = showDelete,
        };
        ToolTipService.SetToolTip(deleteBtn, "Remove DXVK");
        deleteBtn.Click += (s, e) => _window.UninstallDxvkButton_Click(s, e);
        Grid.SetColumn(deleteBtn, 5);
        row.Children.Add(deleteBtn);

        body.Children.Add(row);
    }

    private void BuildDgVoodooRow(GameCardViewModel card, StackPanel body)
    {
        _window.ViewModel.SetLastUiAction($"BuildDgVoodooRow({card.GameName})");
        var gameName    = card.GameName;
        var store       = card.Source ?? "";
        var installPath = card.InstallPath ?? "";

        var dgSvc   = App.Services.GetRequiredService<DgVoodooService>();
        var manifest = _window.ViewModel.Manifest;

        bool isInstalled = dgSvc.IsDeployed(installPath);

        // Status: show deployed version — prefer stored per-game override, else latest from manifest
        string? latestVersion = manifest?.DgVoodooVersions?.Keys.FirstOrDefault();
        var activeVersion = _window.ViewModel.GetDgVoodooVersion(gameName, store) ?? latestVersion;
        string statusText  = isInstalled ? (activeVersion != null ? $"v{activeVersion}" : "Installed") : "Ready";
        string statusColor = isInstalled ? "#5ECB7D" : "#A0AABB";

        // ── Row grid ──────────────────────────────────────────────────────────
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
            Text = "dgVoodoo2",
            FontSize = 12,
            Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(label,
            "dgVoodoo2 — translates DX9 calls to DX11, enabling ReShade compute shaders and DLSS5 Feeder on DX9 games.\nDeploys D3D9.dll + dgVoodoo.conf to the game folder.");
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
            ToolTipService.SetToolTip(statusBlock, "Click to open dgVoodoo2 releases page");
            statusBlock.PointerPressed += (s, e) =>
                _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/dege-diosg/dgVoodoo2/releases"));
            statusBlock.PointerEntered += (s, e) => _window.LinkText_PointerEntered(s, e);
            statusBlock.PointerExited  += (s, e) => _window.LinkText_PointerExited(s, e);
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
        ToolTipService.SetToolTip(infoBtn, "Open dgVoodoo2 releases page");
        infoBtn.Click += (s, e) =>
            _ = Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/dege-diosg/dgVoodoo2/releases"));
        Grid.SetColumn(infoBtn, 2);
        row.Children.Add(infoBtn);

        // Col 3 — Install button
        var installBtn = new Button
        {
            Content = isInstalled ? "↺  Redeploy dgVoodoo2" : "⬇  Install dgVoodoo2",
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
            IsEnabled = manifest?.DgVoodooVersions?.Count > 0,
        };
        ToolTipService.SetToolTip(installBtn,
            isInstalled ? "Redeploy dgVoodoo2 — refreshes D3D9.dll and dgVoodoo.conf from staged version"
                        : "Install dgVoodoo2 — deploys D3D9.dll and dgVoodoo.conf to the game folder");

        installBtn.Click += async (s, ev) =>
        {
            if (string.IsNullOrEmpty(installPath) || manifest?.DgVoodooVersions == null) return;
            installBtn.IsEnabled = false;
            installBtn.Content   = "Installing...";
            try
            {
                // Pick version: prefer stored per-game override, then latest from manifest
                KeyValuePair<string, string> versionEntry;
                var storedVersion = _window.ViewModel.GetDgVoodooVersion(gameName, store);
                if (!string.IsNullOrEmpty(storedVersion)
                    && manifest.DgVoodooVersions.TryGetValue(storedVersion, out var storedUrl))
                    versionEntry = new KeyValuePair<string, string>(storedVersion, storedUrl);
                else
                    versionEntry = manifest.DgVoodooVersions.First();

                await dgSvc.EnsureStagedAsync(versionEntry.Key, versionEntry.Value).ConfigureAwait(false);
                var deployed = await Task.Run(() =>
                    dgSvc.DeployToGame(installPath, versionEntry.Key, is64Bit: !card.Is32Bit));

                if (deployed.Count > 0)
                {
                    _window.ViewModel.SetDgVoodooStandalone(gameName, true, store);
                    CrashReporter.Log($"[BuildDgVoodooRow] dgVoodoo2 v{versionEntry.Key} deployed standalone for '{gameName}'");

                    // If ReShade was at d3d9.dll, dgVoodoo2's SentinelBackup has already saved it
                    // as d3d9.dll.original. Copy it to dxgi.dll so ReShade hooks dgVoodoo2's DX11 output.
                    // Do NOT move d3d9.dll (that's now dgVoodoo2).
                    if (card.IsRsInstalled
                        && card.RsInstalledFile?.Equals("d3d9.dll", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        CrashReporter.Log($"[BuildDgVoodooRow] Copying ReShade from D3D9.dll.original → dxgi.dll for dgVoodoo2 coexistence on '{gameName}'");
                        try
                        {
                            // D3D9.dll.original is the ReShade DLL backed up by SentinelBackup during deploy.
                            // Copy it to dxgi.dll so ReShade hooks dgVoodoo2's DX11 output.
                            // No sentinel needed on dxgi.dll — this is our file, not a foreign DLL.
                            var sentinelPath = Path.Combine(installPath, "d3d9.dll.original");
                            var dxgiPath     = Path.Combine(installPath, "dxgi.dll");
                            if (File.Exists(sentinelPath) && new FileInfo(sentinelPath).Length > 0)
                            {
                                File.Copy(sentinelPath, dxgiPath, overwrite: true);
                                // Update ReShade tracking record
                                if (card.RsRecord != null)
                                {
                                    card.RsRecord.InstalledAs = "dxgi.dll";
                                    _auxInstallService.SaveAuxRecord(card.RsRecord);
                                }
                                card.RsInstalledFile = "dxgi.dll";
                                CrashReporter.Log($"[BuildDgVoodooRow] ReShade copied to dxgi.dll for '{gameName}'");
                            }
                            else
                            {
                                CrashReporter.Log($"[BuildDgVoodooRow] D3D9.dll.original not found or is empty sentinel — skipping coexistence copy for '{gameName}'");
                            }
                        }
                        catch (Exception rsEx)
                        {
                            CrashReporter.Log($"[BuildDgVoodooRow] Failed to copy ReShade to dxgi.dll for '{gameName}' — {rsEx.Message}");
                        }
                    }

                    // Rebuild after everything (including ReShade reinstall) is complete
                    RequestExtrasRebuild(card);
                }
                else
                {
                    installBtn.Content = "❌ Deploy failed";
                    installBtn.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[BuildDgVoodooRow] Install failed for '{gameName}' — {ex.Message}");
                installBtn.Content   = "❌ Failed";
                installBtn.IsEnabled = true;
            }
        };
        Grid.SetColumn(installBtn, 3);
        row.Children.Add(installBtn);

        // Col 4 — Cog
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
        ToolTipService.SetToolTip(cogBtn, "dgVoodoo2 Settings");
        cogBtn.Click += async (s, ev) =>
        {
            // Build version list from manifest
            var versions = manifest?.DgVoodooVersions?.Keys.ToList() ?? new List<string>();
            if (versions.Count == 0)
            {
                var noVerDlg = new ContentDialog
                {
                    Title = "dgVoodoo2 Settings",
                    Content = new TextBlock { Text = "No versions available in manifest.", FontSize = 12, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush) },
                    CloseButtonText = "Close",
                    XamlRoot = _window.Content.XamlRoot,
                    RequestedTheme = ElementTheme.Dark,
                };
                await DialogService.ShowSafeAsync(noVerDlg);
                return;
            }

            var currentVersion = _window.ViewModel.GetDgVoodooVersion(gameName, store) ?? versions[0];

            var versionStack = new StackPanel { Spacing = 6 };
            versionStack.Children.Add(new TextBlock
            {
                Text = "Version",
                FontSize = 12,
                Foreground = UIFactory.Brush(ResourceKeys.TextSecondaryBrush),
            });
            var versionCombo = new ComboBox
            {
                ItemsSource = versions,
                SelectedItem = versions.Contains(currentVersion) ? currentVersion : versions[0],
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MaxDropDownHeight = 300,
            };
            ToolTipService.SetToolTip(versionCombo,
                "dgVoodoo2 version to deploy. Older versions may work better with certain games (e.g. 2.87.3 for Mass Effect).");
            versionStack.Children.Add(versionCombo);

            versionStack.Children.Add(new TextBlock
            {
                Text = "Changing version will immediately redeploy dgVoodoo2 to the game folder.",
                FontSize = 11,
                Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
            });

            var cogDlg = new ContentDialog
            {
                Title = "dgVoodoo2 Settings",
                Content = versionStack,
                PrimaryButtonText = "Apply",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = _window.Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            };

            var result = await DialogService.ShowSafeAsync(cogDlg);
            if (result != ContentDialogResult.Primary) return;

            var selectedVersion = versionCombo.SelectedItem as string;
            if (string.IsNullOrEmpty(selectedVersion)) return;

            _window.ViewModel.SetDgVoodooVersion(gameName, selectedVersion, store);

            // Redeploy if already installed
            if (isInstalled && manifest?.DgVoodooVersions?.TryGetValue(selectedVersion, out var versionUrl) == true)
            {
                try
                {
                    await dgSvc.EnsureStagedAsync(selectedVersion, versionUrl).ConfigureAwait(false);
                    var redeployed = await Task.Run(() => dgSvc.DeployToGame(installPath, selectedVersion, is64Bit: !card.Is32Bit));
                    if (redeployed.Count > 0)
                        CrashReporter.Log($"[BuildDgVoodooRow] Redeployed dgVoodoo2 v{selectedVersion} for '{gameName}'");
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[BuildDgVoodooRow] Redeploy failed — {ex.Message}");
                }
                RequestExtrasRebuild(card);
            }
        };
        Grid.SetColumn(cogBtn, 4);
        row.Children.Add(cogBtn);

        // Col 5 — Remove button (visible only when installed)
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
            Opacity = isInstalled ? 1.0 : 0.0,
            IsHitTestVisible = isInstalled,
        };
        ToolTipService.SetToolTip(removeBtn, "Remove dgVoodoo2 from this game");
        removeBtn.Click += async (s, ev) =>
        {
            if (string.IsNullOrEmpty(installPath)) return;
            // Coexistence guard: don't remove if Luma or Feeder also needs dgVoodoo2
            bool lumaNeeds    = card.LumaStatus == GameStatus.Installed;
            bool feederNeeds  = File.Exists(Path.Combine(installPath, "dlss5-feed.addon32"))
                             || File.Exists(Path.Combine(installPath, "dlss5-feed.addon64"));
            if (lumaNeeds || feederNeeds)
            {
                CrashReporter.Log($"[BuildDgVoodooRow] {(lumaNeeds ? "Luma" : "Feeder")} still installed — only clearing standalone flag for '{gameName}'");
                _window.ViewModel.SetDgVoodooStandalone(gameName, false, store);
                RequestExtrasRebuild(card);
                return;
            }
            dgSvc.RemoveFromGame(installPath);
            _window.ViewModel.SetDgVoodooStandalone(gameName, false, store);
            CrashReporter.Log($"[BuildDgVoodooRow] dgVoodoo2 removed for '{gameName}'");

            // If ReShade was coexisting as dxgi.dll, dgVoodoo2's SentinelRestore already put
            // ReShade back at D3D9.dll (from D3D9.dll.original). Just delete the dxgi.dll copy
            // and update the tracking record — no full reinstall needed.
            if (card.IsRsInstalled
                && card.RsInstalledFile?.Equals("dxgi.dll", StringComparison.OrdinalIgnoreCase) == true)
            {
                try
                {
                    var dxgiPath = Path.Combine(installPath, "dxgi.dll");
                    if (File.Exists(dxgiPath)) File.Delete(dxgiPath);
                    // Also clean up any stale dxgi.dll.original left from a previous failed attempt
                    var dxgiOrigPath = Path.Combine(installPath, "dxgi.dll.original");
                    if (File.Exists(dxgiOrigPath)) File.Delete(dxgiOrigPath);
                    if (card.RsRecord != null)
                    {
                        card.RsRecord.InstalledAs = "d3d9.dll";
                        _auxInstallService.SaveAuxRecord(card.RsRecord);
                    }
                    card.RsInstalledFile = "d3d9.dll";
                    CrashReporter.Log($"[BuildDgVoodooRow] Cleaned up coexistence dxgi.dll, updated ReShade record to d3d9.dll for '{gameName}'");
                }
                catch (Exception rsEx)
                {
                    CrashReporter.Log($"[BuildDgVoodooRow] Failed to clean up dxgi.dll for '{gameName}' — {rsEx.Message}");
                }
            }

            RequestExtrasRebuild(card);
        };
        Grid.SetColumn(removeBtn, 5);
        row.Children.Add(removeBtn);

        body.Children.Add(row);
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
