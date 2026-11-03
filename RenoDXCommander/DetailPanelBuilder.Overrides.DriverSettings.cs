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
        uint? PerGameFpsLimit, uint GlobalFpsLimit,
        bool IsAdmin);

    private const string DriverSummaryTag = "DriverSummary";   // collapsed-only header summary

    /// <summary>
    /// Footer line under the driver settings: "Driver 617.42 · ✓ Running as admin — …".
    /// The driver version is shown here when expanded; when collapsed it's in the header summary.
    /// </summary>
    private TextBlock MakeDriverFooterText(bool elevated, Thickness margin)
    {
        var tb = new TextBlock
        {
            FontSize          = 10,
            TextWrapping      = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin            = margin,
        };
        var driverVer = _dlssPresetService.DriverVersionString;
        if (!string.IsNullOrEmpty(driverVer))
            tb.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
            {
                Text       = $"Driver {driverVer}  ·  ",
                Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush),
            });
        tb.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
        {
            Text = elevated
                ? "✓ Running as admin — all driver profile settings are writable."
                : "⚠ Admin rights required to write driver profile settings. Enable Admin Mode in Settings or restart as admin.",
            Foreground = UIFactory.Brush(elevated ? ResourceKeys.TextTertiaryBrush : ResourceKeys.AccentAmberDimBrush),
        });
        return tb;
    }

    private void BuildDriverProfileSection(GameCardViewModel card, string capturedName)
    {
        // ══════════════════════════════════════════════════════════════════════
        // Driver Settings — independent collapsible section
        // VSync, Low Latency, Smooth Motion, Power/G-Sync, ReBAR
        // ══════════════════════════════════════════════════════════════════════

        _window.NvidiaProfileDriverPanel.Children.Clear();

        const string driverSectionKey = "NvidiaProfileDriver";
        bool driverCollapsed = Layout.IsCollapsed(driverSectionKey);

        // Driver version goes in the collapsed summary, not the title, so the
        // title matches the other sections.
        var driverVer = _dlssPresetService.DriverVersionString;
        var driverHeaderText = "Driver Settings";

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
        ToolTipService.SetToolTip(driverArrow, SectionHeaderTooltip);
        ToolTipService.SetToolTip(driverTitle, SectionHeaderTooltip);
        driverHeaderRow.Children.Add(MakeDragHandle(_window.NvidiaProfileDriverContainer));
        driverHeaderRow.Children.Add(driverArrow);
        driverHeaderRow.Children.Add(driverTitle);
        _window.NvidiaProfileDriverPanel.Children.Add(driverHeaderRow);
        Layout.RegisterHeader(driverSectionKey, driverHeaderRow);

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
            // Summary shows only while collapsed
            if (_driverHeaderRow != null)
                foreach (var child in _driverHeaderRow.Children)
                    if (child is TextBlock { Tag: DriverSummaryTag } tb)
                        tb.Visibility = nowCollapsed ? Visibility.Visible : Visibility.Collapsed;
            Layout.SetCollapsed(driverSectionKey, nowCollapsed);
        };

        _driverHeaderRow = driverHeaderRow;

        if (!_dlssPresetService.IsSupported)
        {
            // No profile data to read: the summary is just the driver version
            if (!string.IsNullOrEmpty(driverVer)
                && DetailPanelBuilder.MakeSectionSummaryInlines(new[] { ("Driver", (string?)driverVer) },
                    "NVIDIA driver version.") is { } verTb)
            {
                verTb.Tag = DriverSummaryTag;
                verTb.Visibility = driverCollapsed ? Visibility.Visible : Visibility.Collapsed;
                driverHeaderRow.Children.Add(verTb);
            }

            bool elevated = VulkanLayerService.IsRunningAsAdmin();
            driverBody.Children.Add(MakeDriverFooterText(elevated, new Thickness(0, 4, 0, 0)));
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
                        PerGameFpsLimit:          svc.GetPerGameFpsLimit(gameName, installPath),
                        GlobalFpsLimit:           svc.GetGlobalFpsLimit(),
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
                    for (int i = _driverHeaderRow.Children.Count - 1; i >= 3; i--)
                        if (_driverHeaderRow.Children[i] is TextBlock { Tag: DriverSummaryTag })
                            _driverHeaderRow.Children.RemoveAt(i);

                    var vsyncName = DlssPresetService.VSyncModeOptions
                        .FirstOrDefault(o => o.Value == data.VSyncMode).Name ?? "Default";
                    var smoothName = data.SmoothMotionEnable != 0 ? "On" : null;
                    // ReBarEnableMode: 0=Off, 1=Auto (default), 2=On
                    var rebarName = data.ReBarEnableMode == 2 ? "On"
                                  : data.ReBarEnableMode == 0 ? "Off"
                                  : null; // Auto = default, omit from summary

                    var summaryEntries = new System.Collections.Generic.List<(string, string?)>();
                    if (!string.IsNullOrEmpty(driverVer))
                        summaryEntries.Add(("Driver", driverVer));
                    summaryEntries.Add(("VSync", vsyncName));
                    if (smoothName != null)
                        summaryEntries.Add(("Smooth", smoothName));
                    if (rebarName != null)
                        summaryEntries.Add(("ReBAR", rebarName));
                    if (data.PerGameFpsLimit is uint capFps)
                        summaryEntries.Add(("FPS Limit", capFps == 0 ? "Off" : $"{capFps}"));
                    var summaryTb = DetailPanelBuilder.MakeSectionSummaryInlines(summaryEntries,
                        "NVIDIA driver version and this game's driver profile settings.\n"
                        + "VSync is always shown. Smooth (Smooth Motion), ReBAR and FPS Limit appear only when they differ from the default.");
                    if (summaryTb != null)
                    {
                        summaryTb.Tag = DriverSummaryTag;
                        summaryTb.Visibility = driverBody.Visibility == Visibility.Collapsed ? Visibility.Visible : Visibility.Collapsed;
                        _driverHeaderRow.Children.Add(summaryTb);
                        Layout.SummaryChanged(); // Tabs: refresh the Overview tile
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
                var lowLatencyLabel = new TextBlock { Text = "Low Latency", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) };
                vsyncCol.Children.Add(lowLatencyLabel);
                var options = DlssPresetService.LowLatencyModeOptions;
                uint current = d.LowLatencyMode;
                var items = options.Select(o => o.Name).ToArray();
                int idx = Array.FindIndex(options, o => o.Value == current);
                if (idx < 0) idx = 0;
                // Locked to Ultra while Smooth Motion is enabled — must turn off Smooth Motion first
                bool smoothOn = d.SmoothMotionEnable != 0;
                bool latencyLocked = smoothOn;
                // The combo is disabled while locked and disabled controls show no tooltip,
                // so the reason goes on the visible label above it.
                if (latencyLocked)
                    ToolTipService.SetToolTip(lowLatencyLabel,
                        "Low Latency is locked to Ultra while Smooth Motion is on. Turn Smooth Motion Off to change this.");
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
                var apisLabel = new TextBlock { Text = "Allowed APIs", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) };
                if (!smoothMotionEnabled)
                    ToolTipService.SetToolTip(apisLabel, "Turn Smooth Motion On to change this.");
                smoothCol.Children.Add(apisLabel);
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
                var flipPacingLabel = new TextBlock { Text = "Flip Pacing", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) };
                if (!smoothMotionEnabled)
                    ToolTipService.SetToolTip(flipPacingLabel, "Turn Smooth Motion On to change this.");
                smoothCol.Children.Add(flipPacingLabel);
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

            // ── Column 6: Other (Power, G-Sync, FPS Limit) ──
            var powerCol = new StackPanel { Spacing = 4 };
            var powerLabel = new TextBlock { Text = "Other", FontSize = 11, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush) };
            ToolTipService.SetToolTip(powerLabel, "Power management, G-Sync control and the driver frame rate limit for this game.");
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

            // Per-game FPS limit (FPS Limiter V3). "Global" removes the game's own value so it follows the Settings page limit.
            {
                powerCol.Children.Add(new TextBlock { Text = "FPS Limit", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) });
                var presets = DlssPresetService.FpsLimiterPresets;
                var globalFps = d.GlobalFpsLimit;
                var globalItem = globalFps == 0 ? "Global (Off)" : $"Global ({globalFps} FPS)";

                // Builds the item list; a custom per-game value gets its own entry just above "Custom..."
                (string[] Items, int Index) BuildFpsItems(uint? perGame)
                {
                    var list = new List<string> { globalItem };
                    list.AddRange(presets.Select(o => o.Name));
                    if (perGame == null) return (list.ToArray(), 0);
                    int presetIdx = Array.FindIndex(presets, o => o.Value == perGame.Value && o.Value != uint.MaxValue);
                    if (presetIdx >= 0) return (list.ToArray(), presetIdx + 1);
                    list.Insert(list.Count - 1, $"{perGame.Value} FPS (Custom)");
                    return (list.ToArray(), list.Count - 2);
                }

                uint? currentFps = d.PerGameFpsLimit;
                var (fpsItems, fpsIdx) = BuildFpsItems(currentFps);
                var fpsCombo = new ComboBox
                {
                    ItemsSource = fpsItems,
                    SelectedIndex = fpsIdx,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    CornerRadius = new CornerRadius(6),
                    MaxDropDownHeight = 300,
                };
                bool hasFrameLimiter = card.UlStatus is GameStatus.Installed or GameStatus.UpdateAvailable
                                    || card.DcStatus is GameStatus.Installed or GameStatus.UpdateAvailable;
                var fpsTip = "NVIDIA driver frame rate limit for this game only. Global follows the limit set on the Settings page. Off turns the driver limit off for this game even when a global limit is set.";
                if (hasFrameLimiter)
                    fpsTip += "\n\nReLimiter or Display Commander is installed for this game, so RHI set the driver limit to Off. Using both limiters at once can cause stutter.";
                ToolTipService.SetToolTip(fpsCombo, fpsTip);

                var fpsInit = true;
                fpsCombo.SelectionChanged += async (s, ev) =>
                {
                    if (fpsInit) return;
                    try
                    {
                        var items = (string[])fpsCombo.ItemsSource;
                        int i = fpsCombo.SelectedIndex;
                        if (i < 0 || i >= items.Length) return;
                        var selected = items[i];

                        if (i == 0)
                        {
                            currentFps = null;
                            _ = Task.Run(() => nvidiaPresetService.ClearPerGameFpsLimit(capturedName, installPathSafe));
                            return;
                        }
                        if (selected.EndsWith("(Custom)", StringComparison.Ordinal))
                            return; // re-selected the existing custom value

                        int presetIdx = Array.FindIndex(presets, o => o.Name == selected);
                        if (presetIdx < 0) return;
                        if (presets[presetIdx].Value != uint.MaxValue)
                        {
                            var value = presets[presetIdx].Value;
                            currentFps = value;
                            _ = Task.Run(() => nvidiaPresetService.SetPerGameFpsLimit(capturedName, installPathSafe, value));
                            return;
                        }

                        // "Custom..." — ask for a value
                        var textBox = new TextBox { PlaceholderText = "20-1000", FontSize = 13 };
                        var dialog = new ContentDialog
                        {
                            Title = "Custom FPS Limit",
                            Content = textBox,
                            PrimaryButtonText = "Set",
                            CloseButtonText = "Cancel",
                            XamlRoot = fpsCombo.XamlRoot ?? _window.Content.XamlRoot,
                            RequestedTheme = ElementTheme.Dark,
                        };
                        var result = await DialogService.ShowSafeAsync(dialog);
                        uint? newFps = null;
                        if (result == ContentDialogResult.Primary && uint.TryParse(textBox.Text.Trim(), out var fps) && fps >= 20 && fps <= 1000)
                            newFps = fps;

                        if (newFps is uint chosen)
                        {
                            currentFps = chosen;
                            _ = Task.Run(() => nvidiaPresetService.SetPerGameFpsLimit(capturedName, installPathSafe, chosen));
                        }
                        // Rebuild the list (adds the custom entry, or reverts the selection on cancel)
                        var (newItems, newIdx) = BuildFpsItems(currentFps);
                        fpsInit = true;
                        fpsCombo.ItemsSource = newItems;
                        fpsCombo.SelectedIndex = newIdx;
                        fpsInit = false;
                    }
                    catch (Exception ex)
                    {
                        fpsInit = false;
                        CrashReporter.Log($"[DetailPanelBuilder.FpsLimitCombo] Error for '{capturedName}' — {ex.Message}");
                    }
                };
                powerCol.Children.Add(fpsCombo);
                fpsInit = false;
            }

            // Restore Profile Defaults button — sits in the footer row under this column
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
                var rebarModeLabel = new TextBlock { Text = "Mode", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) };
                if (!rebarEnabled)
                    ToolTipService.SetToolTip(rebarModeLabel, "Set ReBAR Enable to On to change this.");
                rebarCol.Children.Add(rebarModeLabel);
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
                var rebarSizeLabel = new TextBlock { Text = "Size Limit", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) };
                if (!rebarEnabled)
                    ToolTipService.SetToolTip(rebarSizeLabel, "Set ReBAR Enable to On to change this.");
                rebarCol.Children.Add(rebarSizeLabel);

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
            restoreProfileBtn.IsEnabled = isElevatedCapture && nvidiaPresetService.IsSupported;
            restoreProfileBtn.Opacity = isElevatedCapture ? 1.0 : 0.4;
            // Footer row: admin notice on the left, Restore Defaults under the Other column (fixed widths, see above)
            double gridW = ColCount * colW + overhead;
            var footerGrid = new Grid { ColumnSpacing = ColSpacing, Margin = new Thickness(0, 8, 0, 0) };
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, gridW - colW - ColSpacing)) });
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(colW) });
            Grid.SetColumn(restoreProfileBtn, 1);
            footerGrid.Children.Add(restoreProfileBtn);
            _window.ViewModel.SetLastUiAction($"BuildDriverProfileSectionWithData:AddToTree({capturedName})");
            var enqueued = _window.DispatcherQueue?.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () =>
                {
                    // Guard: user navigated away between the outer callback and this deferred grid addition.
                    if (_window.ViewModel.SelectedGame != card) return;
                    _window.ViewModel.SetLastUiAction($"BuildDriverProfileSectionWithData:AddingGrid({capturedName})");
                    // Footer (admin notice + Restore Defaults) goes after the grid so it stays at the bottom
                    footerGrid.Children.Add(MakeDriverFooterText(isElevatedCapture, new Thickness(0)));
                    if (Layout.IsTabs)
                    {
                        // Tabs (mockup A): the same controls as one card, arranged before it's shown
                        targetPanel.Children.Add(Layout.ArrangeDriver(nvidiaGrid, footerGrid, _dlssPresetService.DriverVersionString));
                    }
                    else
                    {
                        targetPanel.Children.Add(nvidiaGrid);
                        _window.ViewModel.SetLastUiAction($"BuildDriverProfileSectionWithData:AddingNotice({capturedName})");
                        targetPanel.Children.Add(footerGrid);
                    }
                    _window.ViewModel.SetLastUiAction($"BuildDriverProfileSectionWithData:Done({capturedName})");
                });
            CrashReporter.Log($"[BuildDriverProfileSectionWithData] TryEnqueue(Low) for grid add returned {enqueued?.ToString() ?? "null (no dispatcher)"} for '{capturedName}'");
        }
        // Admin notice is now added inside the deferred TryEnqueue above when nvidiaPresetService.IsSupported.
        // When not supported we still need to add it immediately.
        if (!nvidiaPresetService.IsSupported)
        {
            bool isElevated = d.IsAdmin;
            (nvBody ?? _window.NvidiaProfileDriverPanel).Children.Add(MakeDriverFooterText(isElevated, new Thickness(0, 8, 0, 0)));
        }
    }
}

// v2.7.7 fix: cancel Task.Delay on NVAPI completion to prevent thread pool starvation
