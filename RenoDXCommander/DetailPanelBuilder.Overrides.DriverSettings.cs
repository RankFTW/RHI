// DetailPanelBuilder.Overrides.DriverSettings.cs — Driver Settings section (independent panel).

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    // Used by BuildNvidiaProfileBody to update the DLSS collapsed summary (still needed)
    private StackPanel? _nvBodyPanel;
    private StackPanel? _nvHeaderRow;

    // Separate header row reference for the Driver Settings section summary
    private StackPanel? _driverHeaderRow;

    // Snapshot of all NVAPI values needed to build the driver profile section — fetched off the UI thread.
    private sealed record DriverProfileData(
        uint VSyncMode, uint? GlobalVSyncMode,
        uint VSyncTearControl,
        uint LowLatencyMode,
        uint SmoothMotionEnable,
        uint SmoothMotionApis,
        uint SmoothMotionFlipPacingFs,
        uint PowerManagementMode,
        bool PerGameGSyncEnabled,
        ulong ReBarSizeLimit,
        uint ReBarEnableMode,
        uint ReBarMode,
        ulong GlobalReBarSizeLimit,
        bool IsAdmin);

    private void BuildDriverProfileSection(GameCardViewModel card, string capturedName)
    {
        // ══════════════════════════════════════════════════════════════════════
        // Driver Settings — independent collapsible section
        // VSync, Low Latency, Smooth Motion, Power/G-Sync, ReBAR
        // ══════════════════════════════════════════════════════════════════════

        _window.NvidiaProfileDriverPanel.Children.Clear();

        const string driverSectionKey = "NvidiaProfileDriver";
        var driverSettings  = _window.ViewModel.Settings;
        bool driverCollapsed = driverSettings.CollapsedDetailSections.Contains(driverSectionKey);

        var driverVer = _dlssPresetService.DriverVersionString;
        var driverHeaderText = string.IsNullOrEmpty(driverVer)
            ? "Driver Settings"
            : $"Driver Settings — Driver {driverVer}";

        var driverArrow = new TextBlock
        {
            Text              = driverCollapsed ? "▶" : "▼",
            FontSize          = 10,
            Foreground        = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = new Thickness(0, 0, 6, 0),
        };
        var driverTitle = new TextBlock
        {
            Text              = driverHeaderText,
            FontSize          = 13,
            FontWeight        = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground        = UIFactory.Brush(ResourceKeys.TextPrimaryBrush),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var driverHeaderRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
        driverHeaderRow.Children.Add(MakeDragHandle(_window.NvidiaProfileDriverContainer));
        driverHeaderRow.Children.Add(driverArrow);
        driverHeaderRow.Children.Add(driverTitle);
        _window.NvidiaProfileDriverPanel.Children.Add(driverHeaderRow);

        var driverBody = new StackPanel { Spacing = 6, Visibility = driverCollapsed ? Visibility.Collapsed : Visibility.Visible };
        _window.NvidiaProfileDriverPanel.Children.Add(driverBody);

        driverHeaderRow.PointerEntered += (s, e) => driverTitle.Foreground = UIFactory.Brush(ResourceKeys.AccentTealBrush);
        driverHeaderRow.PointerExited  += (s, e) => driverTitle.Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush);
        var driverHandCursor  = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
        var driverArrowCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Arrow);
        var driverCursorProp  = DetailPanelBuilder.CursorProp;
        driverHeaderRow.PointerEntered += (s, e) => driverCursorProp?.SetValue(driverHeaderRow, driverHandCursor);
        driverHeaderRow.PointerExited  += (s, e) => driverCursorProp?.SetValue(driverHeaderRow, driverArrowCursor);

        driverHeaderRow.PointerPressed += (s, e) =>
        {
            bool nowCollapsed = driverBody.Visibility == Visibility.Visible;
            driverBody.Visibility = nowCollapsed ? Visibility.Collapsed : Visibility.Visible;
            driverArrow.Text = nowCollapsed ? "▶" : "▼";
            // Show/hide the summary TextBlock at index 3 (appended after data is loaded)
            if (_driverHeaderRow != null && _driverHeaderRow.Children.Count > 3
                && _driverHeaderRow.Children[3] is TextBlock driverSummaryTb)
                driverSummaryTb.Visibility = nowCollapsed ? Visibility.Visible : Visibility.Collapsed;
            if (nowCollapsed) driverSettings.CollapsedDetailSections.Add(driverSectionKey);
            else              driverSettings.CollapsedDetailSections.Remove(driverSectionKey);
            _window.ViewModel.SaveSettingsPublic();
        };

        _driverHeaderRow = driverHeaderRow;

        if (!_dlssPresetService.IsSupported)
        {
            bool elevated = VulkanLayerService.IsRunningAsAdmin();
            driverBody.Children.Add(new TextBlock
            {
                Text = elevated
                    ? "✓ Running as admin — all driver profile settings are writable."
                    : "⚠ Admin rights required to write driver profile settings. Enable Admin Mode in Settings or restart as admin.",
                FontSize     = 10,
                Foreground   = UIFactory.Brush(elevated ? ResourceKeys.TextTertiaryBrush : ResourceKeys.AccentAmberDimBrush),
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(0, 4, 0, 0),
            });
            return;
        }

        var gameName    = card.GameName;
        var installPath = card.InstallPath ?? "";
        var gameSource  = card.Source ?? "";
        var targetCard  = card;
        var svc         = _dlssPresetService;

        // Dedicated container for the driver grid — swapped atomically by TryEnqueue
        var driverContainer = new StackPanel();
        driverBody.Children.Add(driverContainer);

        var scanToken = _panelScanCts.Token;
        _ = Task.Run(async () =>
        {
            if (_window.ViewModel.SelectedGame?.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase) != true
                || _window.ViewModel.SelectedGame?.Source != gameSource)
                return;

            CrashReporter.Log($"[BuildDriverProfileSection] Waiting for semaphore: '{gameName}'");
            try { await _panelScanSemaphore.WaitAsync(scanToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { CrashReporter.Log($"[BuildDriverProfileSection] Semaphore cancelled: '{gameName}'"); return; }
            CrashReporter.Log($"[BuildDriverProfileSection] Semaphore acquired, reading NVAPI: '{gameName}'");
            DriverProfileData? data = null;
            try
            {
                using var scanCts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
                var scanCt = scanCts.Token;
                var nvapiTask = Task.Run(() =>
                {
                    svc.PrimeProfileCache(gameName, installPath, scanCt);
                    return new DriverProfileData(
                        VSyncMode:                svc.GetVSyncMode(gameName, installPath),
                        GlobalVSyncMode:          svc.GetGlobalVSyncMode(),
                        VSyncTearControl:         svc.GetVSyncTearControl(gameName, installPath),
                        LowLatencyMode:           svc.GetLowLatencyMode(gameName, installPath),
                        SmoothMotionEnable:       svc.GetSmoothMotionEnable(gameName, installPath),
                        SmoothMotionApis:         svc.GetSmoothMotionApis(gameName, installPath),
                        SmoothMotionFlipPacingFs: svc.GetSmoothMotionFlipPacingFs(gameName, installPath),
                        PowerManagementMode:      svc.GetPowerManagementMode(gameName, installPath),
                        PerGameGSyncEnabled:      svc.GetPerGameGSyncEnabled(gameName, installPath),
                        ReBarSizeLimit:           svc.GetReBarSizeLimit(gameName, installPath),
                        ReBarEnableMode:          svc.GetReBarEnableMode(gameName, installPath),
                        ReBarMode:                svc.GetReBarMode(gameName, installPath),
                        GlobalReBarSizeLimit:     svc.GetGlobalReBarSizeLimit(),
                        IsAdmin:                  VulkanLayerService.IsRunningAsAdmin());
                }, scanCt);
                using var delayCts = new CancellationTokenSource();
                var delayTask = Task.Delay(5000, delayCts.Token);
                var completed = await Task.WhenAny(nvapiTask, delayTask).ConfigureAwait(false);
                delayCts.Cancel();
                if (completed == nvapiTask)
                    data = await nvapiTask.ConfigureAwait(false);
                else
                    CrashReporter.Log($"[BuildDriverProfileSection] NVAPI reads timed out for '{gameName}' — using defaults");
            }
            finally
            {
                CrashReporter.Log($"[BuildDriverProfileSection] Semaphore releasing: '{gameName}'");
                _panelScanSemaphore.Release();
            }

            _window.DispatcherQueue?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (scanToken.IsCancellationRequested) return;
                if (_window.SettingsPanel.Visibility == Microsoft.UI.Xaml.Visibility.Visible || DialogService.IsDialogOpen)
                {
                    CrashReporter.Log($"[BuildDriverProfileSectionWithData] Skipped AddToTree for '{gameName}' — Settings panel is open");
                    return;
                }
                var currentCard = _window.ViewModel.SelectedGame;
                if (currentCard == null || !currentCard.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase)
                    || currentCard.Source != gameSource)
                    return;

                _window.ViewModel.SetLastUiAction($"BuildDriverProfileSectionWithData({gameName})");
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var availW = driverContainer.ActualWidth > 0 ? driverContainer.ActualWidth : _window.NvidiaProfileDriverPanel.ActualWidth;
                var tempDriver = new StackPanel();
                BuildDriverProfileSectionWithData(targetCard, capturedName, svc, tempDriver, data, availW);
                sw.Stop();
                if (sw.ElapsedMilliseconds > 50)
                    CrashReporter.Log($"[BuildDriverProfileSectionWithData] SLOW build: '{gameName}' took {sw.ElapsedMilliseconds}ms");
                _window.ViewModel.SetLastUiAction($"BuildDriverProfileSectionWithData:AddToTree({gameName})");
                var sw2 = System.Diagnostics.Stopwatch.StartNew();
                driverContainer.Children.Clear();
                driverContainer.Children.Add(tempDriver);
                sw2.Stop();
                if (sw2.ElapsedMilliseconds > 50)
                    CrashReporter.Log($"[BuildDriverProfileSectionWithData] SLOW AddToTree: '{gameName}' took {sw2.ElapsedMilliseconds}ms on UI thread");

                // Update collapsed summary — appended at index 3 of driverHeaderRow
                if (_driverHeaderRow != null && data != null)
                {
                    while (_driverHeaderRow.Children.Count > 3)
                        _driverHeaderRow.Children.RemoveAt(3);

                    var vsyncName = DlssPresetService.VSyncModeOptions
                        .FirstOrDefault(o => o.Value == data.VSyncMode).Name ?? "Default";
                    var smoothName = data.SmoothMotionEnable != 0 ? "On" : null;
                    // ReBarEnableMode: 0=Off, 1=Auto (default), 2=On
                    var rebarName = data.ReBarEnableMode == 2 ? "On"
                                  : data.ReBarEnableMode == 0 ? "Off"
                                  : null; // Auto = default, omit from summary

                    var summaryEntries = new System.Collections.Generic.List<(string, string?)>
                    {
                        ("VSync", vsyncName),
                    };
                    if (smoothName != null)
                        summaryEntries.Add(("Smooth", smoothName));
                    if (rebarName != null)
                        summaryEntries.Add(("ReBAR", rebarName));
                    var summaryTb = DetailPanelBuilder.MakeSectionSummaryInlines(summaryEntries);
                    if (summaryTb != null)
                    {
                        summaryTb.Visibility = driverCollapsed ? Visibility.Visible : Visibility.Collapsed;
                        _driverHeaderRow.Children.Add(summaryTb);
                    }
                }
            });
        });
    }

    private void BuildDriverProfileSectionWithData(GameCardViewModel card, string capturedName,
        DlssPresetService nvidiaPresetService, StackPanel? nvBody, DriverProfileData d,
        double containerWidth = 0)
    {
        // ══════════════════════════════════════════════════════════════════════
        // Driver Settings — VSync, Latency, Smooth Motion, Power/CPU, ReBAR
        // ══════════════════════════════════════════════════════════════════════
        if (nvidiaPresetService.IsSupported)
        {
            bool isAdmin = d.IsAdmin;

            var nvidiaGrid = new Grid { ColumnSpacing = 12, Opacity = isAdmin ? 1.0 : 0.4, IsHitTestVisible = isAdmin };
            // Use fixed-pixel column widths to avoid the WinUI infinite layout loop.
            // Star columns inside a StackPanel inside ScrollViewer cause an infinite measurement cycle.
            // We compute equal column widths from the container width: 4 columns + 3 x 1px dividers.
            // Fall back to 200px per column if container width isn't available yet.
            const double DividerWidth = 1.0;
            const int DividerCount   = 3;
            const int ColCount       = 4;
            const double ColSpacing  = 12.0; // nvidiaGrid.ColumnSpacing
            // Total width consumed: ColumnSpacing between all 7 columns (6 gaps) + 3 divider columns
            double overhead = (ColCount + DividerCount - 1) * ColSpacing + DividerCount * DividerWidth;
            double colW = containerWidth > overhead
                ? (containerWidth - overhead) / ColCount
                : 200.0;

            nvidiaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(colW) });
            nvidiaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(DividerWidth) });
            nvidiaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(colW) });
            nvidiaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(DividerWidth) });
            nvidiaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(colW) });
            nvidiaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(DividerWidth) });
            nvidiaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(colW) });

            var installPathSafe = card.InstallPath ?? "";

            // ── Column 0: VSync ──
            var vsyncCol = new StackPanel { Spacing = 4 };
            var vsyncLabel = new TextBlock { Text = "VSync", FontSize = 11, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush) };
            ToolTipService.SetToolTip(vsyncLabel, "Vertical Sync settings — controls how the driver synchronizes frame rendering with your display's refresh rate.");
            vsyncCol.Children.Add(vsyncLabel);

            // VSync Mode
            {
                vsyncCol.Children.Add(new TextBlock { Text = "Mode", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) });
                var options = DlssPresetService.VSyncModeOptions;
                uint current = d.VSyncMode;
                var globalVSync = d.GlobalVSyncMode;

                var itemsList = new List<string>();
                if (globalVSync.HasValue)
                {
                    var globalName = options.FirstOrDefault(o => o.Value == globalVSync.Value).Name ?? "App Controlled";
                    itemsList.Add($"Global ({globalName})");
                }
                itemsList.AddRange(options.Select(o => o.Name));
                var items = itemsList.ToArray();

                // Determine selected index
                int idx;
                if (globalVSync.HasValue)
                {
                    bool perGameMatchesGlobal = current == globalVSync.Value;
                    var perGameIdx = Array.FindIndex(options, o => o.Value == current);
                    idx = perGameMatchesGlobal ? 0 : (perGameIdx >= 0 ? perGameIdx + 1 : 0);
                }
                else
                {
                    idx = Array.FindIndex(options, o => o.Value == current);
                    if (idx < 0) idx = 0;
                }

                var combo = new ComboBox
                {
                    ItemsSource = items,
                    SelectedIndex = idx,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    CornerRadius = new CornerRadius(6),
                    MaxDropDownHeight = 300,
                };
                ToolTipService.SetToolTip(combo, globalVSync.HasValue
                    ? "Global = inherit from global setting. App Controlled: let the game decide. Force Off: disables VSync. Force On: locks to refresh rate. Fast Sync: renders freely, displays latest complete frame."
                    : "VSync Mode — App Controlled: let the game decide. Force Off: disables VSync entirely. Force On: locks to refresh rate. Fast Sync: renders freely, displays latest complete frame.");
                var init = true;
                combo.SelectionChanged += (s, ev) =>
                {
                    if (init) return;
                    int i = combo.SelectedIndex;
                    if (i < 0 || i >= items.Length) return;
                    var selected = items[i];
                    if (selected.StartsWith("Global"))
                    {
                        // Inherit from global — write the global value
                        var valueToWrite = globalVSync ?? options[0].Value;
                        _ = Task.Run(() => nvidiaPresetService.SetVSyncMode(capturedName, installPathSafe, valueToWrite));
                    }
                    else
                    {
                        int optIdx = globalVSync.HasValue ? i - 1 : i;
                        if (optIdx >= 0 && optIdx < options.Length)
                        {
                            var valueToWrite = options[optIdx].Value;
                            _ = Task.Run(() => nvidiaPresetService.SetVSyncMode(capturedName, installPathSafe, valueToWrite));
                        }
                    }
                };
                vsyncCol.Children.Add(combo);
                init = false;
            }

            // VSync Tear Control
            {
                vsyncCol.Children.Add(new TextBlock { Text = "Tear Control", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) });
                var options = DlssPresetService.VSyncTearControlOptions;
                uint current = d.VSyncTearControl;
                var items = options.Select(o => o.Name).ToArray();
                int idx = Array.FindIndex(options, o => o.Value == current);
                if (idx < 0) idx = 0;
                var combo = new ComboBox
                {
                    ItemsSource = items,
                    SelectedIndex = idx,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    CornerRadius = new CornerRadius(6),
                    MaxDropDownHeight = 300,
                };
                ToolTipService.SetToolTip(combo, "VSync Tear Control — Standard: normal VSync behavior. Adaptive: VSync on when FPS ≥ refresh rate, off when below (reduces stuttering at low FPS).");
                var init = true;
                combo.SelectionChanged += (s, ev) =>
                {
                    if (init) return;
                    int i = combo.SelectedIndex;
                    if (i < 0 || i >= options.Length) return;
                    var valueToWrite = options[i].Value;
                    _ = Task.Run(() => nvidiaPresetService.SetVSyncTearControl(capturedName, installPathSafe, valueToWrite));
                };
                vsyncCol.Children.Add(combo);
                init = false;
            }

            // Low Latency Mode (in VSync column)
            {
                vsyncCol.Children.Add(new TextBlock { Text = "Low Latency", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) });
                var options = DlssPresetService.LowLatencyModeOptions;
                uint current = d.LowLatencyMode;
                var items = options.Select(o => o.Name).ToArray();
                int idx = Array.FindIndex(options, o => o.Value == current);
                if (idx < 0) idx = 0;
                // Locked to Ultra while Smooth Motion is enabled — must turn off Smooth Motion first
                bool smoothOn = d.SmoothMotionEnable != 0;
                bool latencyLocked = smoothOn;
                var combo2 = new ComboBox
                {
                    ItemsSource = items,
                    SelectedIndex = idx,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    CornerRadius = new CornerRadius(6),
                    MaxDropDownHeight = 300,
                    IsEnabled = !latencyLocked,
                    Opacity = latencyLocked ? 0.4 : 1.0,
                };
                ToolTipService.SetToolTip(combo2, latencyLocked
                    ? "Low Latency is locked to Ultra while Smooth Motion is enabled. Turn off Smooth Motion to change this setting."
                    : "Low Latency Mode — Off: game controls frame queue. On: limits pre-rendered frames to 1 (lower latency). Ultra: just-in-time frame submission (lowest latency, may reduce FPS slightly).");
                var init2 = true;
                combo2.SelectionChanged += (s, ev) =>
                {
                    if (init2) return;
                    int i = combo2.SelectedIndex;
                    if (i < 0 || i >= options.Length) return;
                    var valueToWrite = options[i].Value;
                    _ = Task.Run(() => nvidiaPresetService.SetLowLatencyMode(capturedName, installPathSafe, valueToWrite));
                };
                vsyncCol.Children.Add(combo2);
                init2 = false;
            }

            Grid.SetColumn(vsyncCol, 0);
            nvidiaGrid.Children.Add(vsyncCol);
            nvidiaGrid.Children.Add(MakeDlssDivider(1));
            _window.ViewModel.SetLastUiAction($"BuildDriverProfileSectionWithData:VSync done({capturedName})");

            // ── Column 4: Smooth Motion ──
            var smoothCol = new StackPanel { Spacing = 4 };
            var smoothLabel = new TextBlock { Text = "Smooth Motion", FontSize = 11, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush) };
            ToolTipService.SetToolTip(smoothLabel, "NVIDIA Smooth Motion — driver-level frame generation. Adds interpolated frames for smoother visuals. RTX 40 Series+ required.");
            smoothCol.Children.Add(smoothLabel);

            // Enable
            bool smoothMotionEnabled;
            {
                smoothCol.Children.Add(new TextBlock { Text = "Enable", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) });
                var options = DlssPresetService.SmoothMotionEnableOptions;
                uint current = d.SmoothMotionEnable;
                smoothMotionEnabled = current != 0;
                var items = options.Select(o => o.Name).ToArray();
                int idx = Array.FindIndex(options, o => o.Value == current);
                if (idx < 0) idx = 0;
                var combo = new ComboBox
                {
                    ItemsSource = items,
                    SelectedIndex = idx,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    CornerRadius = new CornerRadius(6),
                    MaxDropDownHeight = 300,
                };
                ToolTipService.SetToolTip(combo, "Smooth Motion Enable — Off: disabled. On: enables driver-level frame generation (RTX 40 Series+ only).");
                var init = true;
                combo.SelectionChanged += (s, ev) =>
                {
                    if (init) return;
                    int i = combo.SelectedIndex;
                    if (i < 0 || i >= options.Length) return;
                    var selectedValue = options[i].Value;
                    bool enabling = selectedValue != 0;
                    const uint LowLatencyOff   = 0x00000000;
                    const uint LowLatencyUltra = 0x00000002;
                    _ = Task.Run(() =>
                    {
                        nvidiaPresetService.SetSmoothMotionEnable(capturedName, installPathSafe, selectedValue);
                        // Cascade: set APIs to All when enabling, None when disabling
                        nvidiaPresetService.SetSmoothMotionApis(capturedName, installPathSafe, enabling ? 0x00000007u : 0x00000000u);
                        // Cascade Low Latency: Ultra when enabling, restore previous value when disabling
                        uint? savedLatency = null;
                        if (enabling)
                        {
                            uint prevLatency = nvidiaPresetService.GetLowLatencyMode(capturedName, installPathSafe);
                            savedLatency = prevLatency != LowLatencyUltra ? prevLatency : (uint?)null;
                            nvidiaPresetService.SetLowLatencyMode(capturedName, installPathSafe, LowLatencyUltra);
                        }
                        else
                        {
                            uint restoreValue = card.PreSmoothMotionLowLatency ?? LowLatencyOff;
                            nvidiaPresetService.SetLowLatencyMode(capturedName, installPathSafe, restoreValue);
                        }
                        // Marshal card state mutation back to UI thread
                        _window.DispatcherQueue?.TryEnqueue(() =>
                        {
                            card.PreSmoothMotionLowLatency = enabling ? savedLatency : null;
                        });
                    });
                    // Rebuild only the driver profile section — Smooth Motion affects the Low Latency
                    // row's enabled state and the APIs combo. Rebuilding the full panel is unnecessary.
                    _window.DispatcherQueue?.TryEnqueue(() => BuildDriverProfileSection(card, card.GameName));
                };
                smoothCol.Children.Add(combo);
                init = false;
            }

            // APIs
            {
                smoothCol.Children.Add(new TextBlock { Text = "Allowed APIs", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) });
                var options = DlssPresetService.SmoothMotionApisOptions;
                uint current = d.SmoothMotionApis;
                var items = options.Select(o => o.Name).ToArray();
                int idx = Array.FindIndex(options, o => o.Value == current);
                if (idx < 0) idx = 0;
                var combo = new ComboBox
                {
                    ItemsSource = items,
                    SelectedIndex = idx,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    CornerRadius = new CornerRadius(6),
                    MaxDropDownHeight = 300,
                    IsEnabled = smoothMotionEnabled,
                    Opacity = smoothMotionEnabled ? 1.0 : 0.4,
                };
                ToolTipService.SetToolTip(combo, "Smooth Motion APIs — which graphics APIs Smooth Motion is allowed to hook. None = disabled for all APIs.");
                var init = true;
                combo.SelectionChanged += (s, ev) =>
                {
                    if (init) return;
                    int i = combo.SelectedIndex;
                    if (i < 0 || i >= options.Length) return;
                    var valueToWrite = options[i].Value;
                    _ = Task.Run(() => nvidiaPresetService.SetSmoothMotionApis(capturedName, installPathSafe, valueToWrite));
                };
                smoothCol.Children.Add(combo);
                init = false;
            }

            // Flip Pacing (combined — sets both Fullscreen and Windowed together)
            {
                smoothCol.Children.Add(new TextBlock { Text = "Flip Pacing", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) });
                var options = DlssPresetService.SmoothMotionFlipPacingFsOptions;
                uint current = d.SmoothMotionFlipPacingFs;
                var items = options.Select(o => o.Name).ToArray();
                int idx = Array.FindIndex(options, o => o.Value == current);
                if (idx < 0) idx = 0;
                var combo = new ComboBox
                {
                    ItemsSource = items,
                    SelectedIndex = idx,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    CornerRadius = new CornerRadius(6),
                    MaxDropDownHeight = 300,
                    IsEnabled = smoothMotionEnabled,
                    Opacity = smoothMotionEnabled ? 1.0 : 0.4,
                };
                ToolTipService.SetToolTip(combo, "Flip Pacing — Off: prioritize lower latency. On: prioritize smoother frame pacing. Sets both fullscreen and windowed modes together.");
                var init = true;
                combo.SelectionChanged += (s, ev) =>
                {
                    if (init) return;
                    int i = combo.SelectedIndex;
                    if (i < 0 || i >= options.Length) return;
                    var fsValue = options[i].Value;
                    // Also set windowed pacing to the same value (use 0x00000001 for "On" instead of 0xFFFFFFFF)
                    var winValue = fsValue == 0xFFFFFFFF ? 0x00000001u : fsValue;
                    _ = Task.Run(() =>
                    {
                        nvidiaPresetService.SetSmoothMotionFlipPacingFs(capturedName, installPathSafe, fsValue);
                        nvidiaPresetService.SetSmoothMotionFlipPacingWin(capturedName, installPathSafe, winValue);
                    });
                };
                smoothCol.Children.Add(combo);
                init = false;
            }

            Grid.SetColumn(smoothCol, 4);
            nvidiaGrid.Children.Add(smoothCol);
            nvidiaGrid.Children.Add(MakeDlssDivider(5));
            _window.ViewModel.SetLastUiAction($"BuildDriverProfileSectionWithData:SmoothMotion done({capturedName})");

            // ── Column 6: Other (Power, G-Sync, Restore) ──
            var powerCol = new StackPanel { Spacing = 4 };
            var powerLabel = new TextBlock { Text = "Other", FontSize = 11, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush) };
            ToolTipService.SetToolTip(powerLabel, "Power management, G-Sync control, and profile reset.");
            powerCol.Children.Add(powerLabel);

            // Power Management Mode
            {
                powerCol.Children.Add(new TextBlock { Text = "Power Mode", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) });
                var options = DlssPresetService.PowerManagementOptions;
                uint current = d.PowerManagementMode;
                var items = options.Select(o => o.Name).ToArray();
                int idx = Array.FindIndex(options, o => o.Value == current);
                if (idx < 0) idx = 0;
                var combo = new ComboBox
                {
                    ItemsSource = items,
                    SelectedIndex = idx,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    CornerRadius = new CornerRadius(6),
                    MaxDropDownHeight = 300,
                };
                ToolTipService.SetToolTip(combo, "Power Management — Adaptive: GPU clocks down at idle. Maximum: locks GPU to highest clocks. Optimal: balanced (NVIDIA recommended).");
                var init = true;
                combo.SelectionChanged += (s, ev) =>
                {
                    if (init) return;
                    int i = combo.SelectedIndex;
                    if (i < 0 || i >= options.Length) return;
                    var valueToWrite = options[i].Value;
                    _ = Task.Run(() => nvidiaPresetService.SetPowerManagementMode(capturedName, installPathSafe, valueToWrite));
                };
                powerCol.Children.Add(combo);
                init = false;
            }

            // G-Sync per-game toggle
            {
                powerCol.Children.Add(new TextBlock { Text = "G-Sync", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) });
                bool gsyncEnabled = d.PerGameGSyncEnabled;
                var gsyncCombo = new ComboBox
                {
                    ItemsSource = new[] { "Enabled", "Disabled" },
                    SelectedIndex = gsyncEnabled ? 0 : 1,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    CornerRadius = new CornerRadius(6),
                    MaxDropDownHeight = 300,
                };
                ToolTipService.SetToolTip(gsyncCombo, "Per-game G-Sync control. Disabled forces G-Sync off for this game regardless of global setting.");
                var gsyncInit = true;
                gsyncCombo.SelectionChanged += (s, ev) =>
                {
                    if (gsyncInit) return;
                    bool enabled = gsyncCombo.SelectedIndex == 0;
                    _ = Task.Run(() => nvidiaPresetService.SetPerGameGSyncEnabled(capturedName, installPathSafe, enabled));
                };
                powerCol.Children.Add(gsyncCombo);
                gsyncInit = false;
            }

            // Restore Profile Defaults button (label spacer to align with 3rd row combos)
            powerCol.Children.Add(new TextBlock { Text = " ", FontSize = 10, Margin = new Thickness(0, 2, 0, 0) });
            var restoreProfileBtn = new Button
            {
                Content = "Restore Defaults",
                FontSize = 11,
                Height = 32,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = UIFactory.Brush(ResourceKeys.AccentBlueBgBrush),
                Foreground = UIFactory.Brush(ResourceKeys.AccentBlueBrush),
                BorderBrush = UIFactory.Brush(ResourceKeys.AccentBlueBorderBrush),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                IsEnabled = nvidiaPresetService.IsSupported,
            };
            ToolTipService.SetToolTip(restoreProfileBtn,
                "Restore this game's NVIDIA driver profile to factory defaults. Removes all custom settings (presets, render scale, MFG, driver overrides). This action is irreversible.");
            restoreProfileBtn.Click += async (s, ev) =>
            {
                var xamlRoot = (s as FrameworkElement)?.XamlRoot ?? _window.Content.XamlRoot;
                var warningDialog = new ContentDialog
                {
                    Title = "Restore driver settings?",
                    Content = new TextBlock
                    {
                        Text = $"This will restore driver settings for {capturedName} back to the factory default and restore DLSS/Streamline DLLs to their original versions. This action is irreversible.",
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 13,
                    },
                    PrimaryButtonText = "Restore",
                    CloseButtonText = "Cancel",
                    XamlRoot = xamlRoot,
                    RequestedTheme = ElementTheme.Dark,
                };

                var result = await DialogService.ShowSafeAsync(warningDialog);
                if (result != ContentDialogResult.Primary) return;

                var success = nvidiaPresetService.RestoreProfileDefaults(capturedName, installPathSafe);
                if (success)
                {
                    var refreshCard = _window.ViewModel.AllCards.FirstOrDefault(c =>
                        c.GameName.Equals(capturedName, StringComparison.OrdinalIgnoreCase));
                    if (refreshCard != null)
                    {
                        // Also restore DLSS/Streamline DLLs to originals
                        if (refreshCard.DlssDetection != null)
                        {
                            var dlssSvc = _dlssStreamlineService;
                            dlssSvc.RestoreAll(refreshCard.DlssDetection);
                            refreshCard.RefreshDlssVersions(dlssSvc);
                        }
                        _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(refreshCard));
                    }
                }
            };
            powerCol.Children.Add(restoreProfileBtn);

            Grid.SetColumn(powerCol, 6);
            nvidiaGrid.Children.Add(powerCol);
            _window.ViewModel.SetLastUiAction($"BuildDriverProfileSectionWithData:Power done({capturedName})");

            // ── Column 8: ReBAR ──
            var rebarCol = new StackPanel { Spacing = 4 };
            var rebarLabel = new TextBlock { Text = "ReBAR", FontSize = 11, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush) };
            ToolTipService.SetToolTip(rebarLabel, "Resizable BAR — allows the CPU to access full GPU VRAM at once. Can improve performance by 5-10% in some titles. RTX 30+ and BIOS support required.");
            rebarCol.Children.Add(rebarLabel);

            bool rebarEnabled = false; // set inside Enable block below
            ulong rebarSizeLimit = d.ReBarSizeLimit;

            // Enable — Auto (Default) / Off / On using new 0x000BFA21 setting
            {
                rebarCol.Children.Add(new TextBlock { Text = "Enable", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) });

                uint rebarEnableMode = d.ReBarEnableMode;
                // 0=Off→index 1, 1=Auto→index 0, 2=On→index 2
                int enableIdx = rebarEnableMode == 0 ? 1 : rebarEnableMode == 2 ? 2 : 0;

                var rebarEnableCombo = new ComboBox
                {
                    ItemsSource = new[] { "Auto (Default)", "Off", "On" },
                    SelectedIndex = enableIdx,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    CornerRadius = new CornerRadius(6),
                    MaxDropDownHeight = 300,
                };
                ToolTipService.SetToolTip(rebarEnableCombo, "Auto = driver decides. On = force-enable ReBAR. Off = force-disable ReBAR.");
                var rebarComboInit = true;
                rebarEnableCombo.SelectionChanged += (s, ev) =>
                {
                    if (rebarComboInit) return;
                    // index 0=Auto(1), 1=Off(0), 2=On(2)
                    uint mode = rebarEnableCombo.SelectedIndex switch { 1 => 0u, 2 => 2u, _ => 1u };
                    _ = Task.Run(() => nvidiaPresetService.SetReBarEnableMode(capturedName, installPathSafe, mode));
                    // Rebuild only the driver profile section — ReBAR Enable affects whether the
                    // ReBAR Size combo is enabled. Rebuilding the full panel is unnecessary.
                    _window.DispatcherQueue?.TryEnqueue(() => BuildDriverProfileSection(card, card.GameName));
                };
                rebarCol.Children.Add(rebarEnableCombo);
                rebarComboInit = false;

                // Derive enabled state for Mode/Size combos: only On enables them, Auto and Off grey them
                rebarEnabled = rebarEnableMode == 2;
            }

            // Mode
            {
                rebarCol.Children.Add(new TextBlock { Text = "Mode", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) });
                uint rebarMode = d.ReBarMode;
                var modeItems = DlssPresetService.ReBarModes.Select(m => m.Name).ToList();

                // Select current effective mode: per-game value, or default to Standard (index 0)
                int modeIdx = Array.FindIndex(DlssPresetService.ReBarModes, m => m.Value == rebarMode);
                if (modeIdx < 0) modeIdx = 0;

                var rebarModeCombo = new ComboBox
                {
                    ItemsSource = modeItems,
                    SelectedIndex = modeIdx,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    CornerRadius = new CornerRadius(6),
                    MaxDropDownHeight = 300,
                    IsEnabled = rebarEnabled,
                    Opacity = rebarEnabled ? 1.0 : 0.4,
                };
                ToolTipService.SetToolTip(rebarModeCombo, "Standard = conservative. Optimized = aggressive driver scheduling (used by NVIDIA-whitelisted titles).");
                var modeComboInit = true;
                rebarModeCombo.SelectionChanged += (s, ev) =>
                {
                    if (modeComboInit) return;
                    int idx = rebarModeCombo.SelectedIndex;
                    if (idx < 0) return;
                    uint newMode = DlssPresetService.ReBarModes[idx].Value;
                    _ = Task.Run(() => nvidiaPresetService.SetReBarMode(capturedName, installPathSafe, newMode));
                };
                rebarCol.Children.Add(rebarModeCombo);
                modeComboInit = false;
            }

            // Size Limit — always shows actual size values (no Global option)
            {
                rebarCol.Children.Add(new TextBlock { Text = "Size Limit", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) });

                var sizeItems = new List<string>();
                var sizeValues = new List<ulong>();
                foreach (var sl in DlssPresetService.ReBarSizeLimits)
                {
                    sizeItems.Add(sl.Name);
                    sizeValues.Add(sl.Value);
                }

                // Select the current effective size: per-game override, or global, or 1GB default
                ulong globalSize = d.GlobalReBarSizeLimit;
                ulong effectiveSize = rebarSizeLimit != 0 ? rebarSizeLimit : (globalSize != 0 ? globalSize : 0x0000000040000000);
                int sizeIdx;
                var matchIdx = sizeValues.IndexOf(effectiveSize);
                sizeIdx = matchIdx >= 0 ? matchIdx : 1; // Default: 1GB (index 1)

                var rebarSizeCombo = new ComboBox
                {
                    ItemsSource = sizeItems,
                    SelectedIndex = sizeIdx,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    CornerRadius = new CornerRadius(6),
                    MaxDropDownHeight = 300,
                    IsEnabled = rebarEnabled,
                    Opacity = rebarEnabled ? 1.0 : 0.4,
                };
                ToolTipService.SetToolTip(rebarSizeCombo, "1GB is optimal for most games. Decrease to 512MB if experiencing ReBAR-related stutters.");
                var sizeComboInit = true;
                rebarSizeCombo.SelectionChanged += (s, ev) =>
                {
                    if (sizeComboInit) return;
                    int idx = rebarSizeCombo.SelectedIndex;
                    if (idx < 0) return;
                    ulong newSize = sizeValues[idx];
                    _ = Task.Run(() => nvidiaPresetService.SetReBarSizeLimit(capturedName, installPathSafe, newSize));
                };
                rebarCol.Children.Add(rebarSizeCombo);
                sizeComboInit = false;
            }

            Grid.SetColumn(rebarCol, 2);
            nvidiaGrid.Children.Add(rebarCol);
            nvidiaGrid.Children.Add(MakeDlssDivider(3));
            _window.ViewModel.SetLastUiAction($"BuildDriverProfileSectionWithData:ReBAR done({capturedName})");

            // Defer adding the fully-built driver grid to the visual tree so WinUI can
            // finish any queued layout work from the DLSS columns above before committing
            // another large grid. Prevents the UI freeze seen on games with all five
            // DLSS components (S.T.A.L.K.E.R. 2 and similar).
            var targetPanel = nvBody ?? _window.NvidiaProfileDriverPanel;
            var isElevatedCapture = d.IsAdmin;
            _window.ViewModel.SetLastUiAction($"BuildDriverProfileSectionWithData:AddToTree({capturedName})");
            var enqueued = _window.DispatcherQueue?.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () =>
                {
                    // Guard: user navigated away between the outer callback and this deferred grid addition.
                    if (_window.ViewModel.SelectedGame != card) return;
                    _window.ViewModel.SetLastUiAction($"BuildDriverProfileSectionWithData:AddingGrid({capturedName})");
                    targetPanel.Children.Add(nvidiaGrid);
                    _window.ViewModel.SetLastUiAction($"BuildDriverProfileSectionWithData:AddingNotice({capturedName})");

                    // Admin notice appended after the grid so it stays at the bottom
                    targetPanel.Children.Add(new TextBlock
                    {
                        Text = isElevatedCapture
                            ? "✓ Running as admin — all driver profile settings are writable."
                            : "⚠ Admin rights required to write driver profile settings. Enable Admin Mode in Settings or restart as admin.",
                        FontSize = 10,
                        Foreground = UIFactory.Brush(isElevatedCapture ? ResourceKeys.TextTertiaryBrush : ResourceKeys.AccentAmberDimBrush),
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 8, 0, 0),
                    });
                    _window.ViewModel.SetLastUiAction($"BuildDriverProfileSectionWithData:Done({capturedName})");
                });
            CrashReporter.Log($"[BuildDriverProfileSectionWithData] TryEnqueue(Low) for grid add returned {enqueued?.ToString() ?? "null (no dispatcher)"} for '{capturedName}'");
        }
        // Admin notice is now added inside the deferred TryEnqueue above when nvidiaPresetService.IsSupported.
        // When not supported we still need to add it immediately.
        if (!nvidiaPresetService.IsSupported)
        {
            bool isElevated = d.IsAdmin;
            (nvBody ?? _window.NvidiaProfileDriverPanel).Children.Add(new TextBlock
            {
                Text = isElevated
                    ? "✓ Running as admin — all driver profile settings are writable."
                    : "⚠ Admin rights required to write driver profile settings. Enable Admin Mode in Settings or restart as admin.",
                FontSize = 10,
                Foreground = UIFactory.Brush(isElevated ? ResourceKeys.TextTertiaryBrush : ResourceKeys.AccentAmberDimBrush),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0),
            });
        }
    }
}

// v2.7.7 fix: cancel Task.Delay on NVAPI completion to prevent thread pool starvation
