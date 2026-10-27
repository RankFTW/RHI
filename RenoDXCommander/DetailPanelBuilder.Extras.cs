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
                : osVariant == "F5"
                ? "https://github.com/janblade/OptiScaler-F5-DLSSNR-Multipass/releases"
                : osVariant == "Nightly"
                ? "https://github.com/optiscaler/OptiScaler-nightly/releases"
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
        var osVariantForInfo = _window.ViewModel.GetOsVariant(card.GameName, card.Source ?? "");
        if (osVariantForInfo == "DlssNr" || osVariantForInfo == "F5" || osVariantForInfo == "Nightly")
        {
            var forkUrl = osVariantForInfo == "F5"
                ? "https://github.com/janblade/OptiScaler-F5-DLSSNR-Multipass"
                : osVariantForInfo == "DlssNr"
                ? "https://github.com/wilsjo2/OptiScaler-DLSSNR-PreSR-Multipass"
                : "https://github.com/optiscaler/OptiScaler-nightly";
            var forkLabel = osVariantForInfo == "F5" ? "F5 DLSSNR" : osVariantForInfo == "DlssNr" ? "DLSS NR" : "OptiScaler Nightly";
            ToolTipService.SetToolTip(infoBtn, $"Open {forkLabel} GitHub page");
            infoBtn.Click += (s, e) => _ = Windows.System.Launcher.LaunchUriAsync(new Uri(forkUrl));
        }
        else
        {
            infoBtn.Click += (s, e) => _window.InfoButton_Click(s, e);
        }
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
}
