// DetailPanelBuilder.Overrides.DriverSettings.cs — Driver Profile Settings (VSync, Latency, Smooth Motion, Power/CPU, ReBAR).

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    // Set by BuildNvidiaProfileSection so BuildDriverProfileSection appends to the body, not the panel root
    private StackPanel? _nvBodyPanel;

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
        var nvidiaPresetService = _dlssPresetService;
        if (!nvidiaPresetService.IsSupported)
        {
            // Just add the admin notice
            bool elevated = VulkanLayerService.IsRunningAsAdmin();
            (_nvBodyPanel ?? _window.NvidiaProfilePanel).Children.Add(new TextBlock
            {
                Text = elevated
                    ? "✓ Запущено от администратора — все настройки профиля доступны для записи."
                    : "⚠ Для записи настроек профиля драйвера нужны права администратора. Включите режим администратора в настройках или перезапустите RHI от его имени.",
                FontSize = 10,
                Foreground = UIFactory.Brush(elevated ? ResourceKeys.TextTertiaryBrush : ResourceKeys.AccentAmberDimBrush),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0),
            });
            return;
        }

        // Capture stable references before going off-thread
        var gameName = card.GameName;
        var installPath = card.InstallPath ?? "";
        var gameSource = card.Source ?? "";
        var targetCard = card;
        var svc = nvidiaPresetService;
        var nvBodySnapshot = _nvBodyPanel;

        // Create a dedicated container for the driver profile section.
        // This lets us replace only the driver content when the live scan completes,
        // without touching the DLSS rows above it in nvBodySnapshot.
        var driverContainer = new StackPanel();
        nvBodySnapshot?.Children.Add(driverContainer);

        // Fetch all NVAPI values off the UI thread, then build UI synchronously on dispatcher
        var scanToken = _panelScanCts.Token;
        _ = Task.Run(async () =>
        {
            // Skip if the user navigated away before we even acquire the semaphore —
            // the cached render is already showing; no need to do the NVAPI scan.
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
                data = new DriverProfileData(
                    VSyncMode:              svc.GetVSyncMode(gameName, installPath),
                    GlobalVSyncMode:        svc.GetGlobalVSyncMode(),
                    VSyncTearControl:       svc.GetVSyncTearControl(gameName, installPath),
                    LowLatencyMode:         svc.GetLowLatencyMode(gameName, installPath),
                    SmoothMotionEnable:     svc.GetSmoothMotionEnable(gameName, installPath),
                    SmoothMotionApis:       svc.GetSmoothMotionApis(gameName, installPath),
                    SmoothMotionFlipPacingFs: svc.GetSmoothMotionFlipPacingFs(gameName, installPath),
                    PowerManagementMode:    svc.GetPowerManagementMode(gameName, installPath),
                    PerGameGSyncEnabled:    svc.GetPerGameGSyncEnabled(gameName, installPath),
                    ReBarSizeLimit:         svc.GetReBarSizeLimit(gameName, installPath),
                    ReBarEnableMode:        svc.GetReBarEnableMode(gameName, installPath),
                    ReBarMode:              svc.GetReBarMode(gameName, installPath),
                    GlobalReBarSizeLimit:   svc.GetGlobalReBarSizeLimit(),
                    IsAdmin:                VulkanLayerService.IsRunningAsAdmin());
            }
            finally
            {
                CrashReporter.Log($"[BuildDriverProfileSection] Semaphore releasing: '{gameName}'");
                _panelScanSemaphore.Release();
            }

            _window.DispatcherQueue?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                // Bail if a newer BuildNvidiaProfileSection call has started (e.g. user changed a preset)
                if (scanToken.IsCancellationRequested) return;

                // Guard: if the user has navigated away, the body panel may have been replaced
                var currentCard = _window.ViewModel.SelectedGame;
                if (currentCard == null || !currentCard.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase)
                    || currentCard.Source != gameSource)
                    return;

                var sw = System.Diagnostics.Stopwatch.StartNew();
                // Build into a throwaway container first, then swap atomically.
                var tempDriver = new StackPanel();
                BuildDriverProfileSectionWithData(targetCard, capturedName, svc, tempDriver, data);
                driverContainer.Children.Clear();
                driverContainer.Children.Add(tempDriver);
                sw.Stop();
                if (sw.ElapsedMilliseconds > 50)
                    CrashReporter.Log($"[BuildDriverProfileSectionWithData] SLOW: '{gameName}' took {sw.ElapsedMilliseconds}ms on UI thread");
            });
        });
    }

    private void BuildDriverProfileSectionWithData(GameCardViewModel card, string capturedName,
        DlssPresetService nvidiaPresetService, StackPanel? nvBody, DriverProfileData d)
    {
        // ══════════════════════════════════════════════════════════════════════
        // Nvidia Profile Settings — VSync, Latency, Smooth Motion, Power/CPU, ReBAR
        // ══════════════════════════════════════════════════════════════════════
        if (nvidiaPresetService.IsSupported)
        {
            bool isAdmin = d.IsAdmin;

            (nvBody ?? _window.NvidiaProfilePanel).Children.Add(UIFactory.MakeSeparator());

            var nvidiaGrid = new Grid { ColumnSpacing = 12, Opacity = isAdmin ? 1.0 : 0.4, IsHitTestVisible = isAdmin };
            // 4 columns with dividers between: col0 | div1 | col2 | div3 | col4 | div5 | col6
            nvidiaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            nvidiaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            nvidiaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            nvidiaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            nvidiaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            nvidiaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            nvidiaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var installPathSafe = card.InstallPath ?? "";

            // ── Column 0: VSync ──
            var vsyncCol = new StackPanel { Spacing = 4 };
            var vsyncLabel = new TextBlock { Text = "VSync", FontSize = 11, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush) };
            ToolTipService.SetToolTip(vsyncLabel, "Настройки вертикальной синхронизации — как драйвер согласует рендеринг кадров с частотой обновления дисплея.");
            vsyncCol.Children.Add(vsyncLabel);

            // VSync Mode
            {
                vsyncCol.Children.Add(new TextBlock { Text = "Режим", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) });
                var options = DlssPresetService.VSyncModeOptions;
                uint current = d.VSyncMode;
                var globalVSync = d.GlobalVSyncMode;

                var itemsList = new List<string>();
                if (globalVSync.HasValue)
                {
                    var globalName = options.FirstOrDefault(o => o.Value == globalVSync.Value).Name ?? "Управляется игрой";
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
                };
                ToolTipService.SetToolTip(combo, globalVSync.HasValue
                    ? "Глобально — наследовать общую настройку. Управляется игрой: решает сама игра. Принудительно выкл: отключает VSync. Принудительно вкл: привязка к частоте обновления. Fast Sync: рендер без ограничений, выводится последний завершённый кадр."
                    : "Режим VSync — управление игрой: решает сама игра. Принудительно выкл: полностью отключает VSync. Принудительно вкл: привязка к частоте обновления. Fast Sync: рендер без ограничений, выводится последний завершённый кадр.");
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
                vsyncCol.Children.Add(new TextBlock { Text = "Управление разрывами", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) });
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
                };
                ToolTipService.SetToolTip(combo, "Управление разрывами VSync — стандартно: обычное поведение VSync. Адаптивно: VSync включён при FPS ≥ частоты обновления, выключен ниже (меньше фризов при низком FPS).");
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
                    IsEnabled = !latencyLocked,
                    Opacity = latencyLocked ? 0.4 : 1.0,
                };
                ToolTipService.SetToolTip(combo2, latencyLocked
                    ? "Пока включён Smooth Motion, Low Latency зафиксирован на Ultra. Выключите Smooth Motion, чтобы изменить настройку."
                    : "Режим Low Latency — выкл: очередью кадров управляет игра. Вкл: не более 1 предрендеренного кадра (ниже задержка). Ultra: отправка кадров точно ко времени (минимальная задержка, может слегка снизить FPS).");
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

            // ── Column 4: Smooth Motion ──
            var smoothCol = new StackPanel { Spacing = 4 };
            var smoothLabel = new TextBlock { Text = "Smooth Motion", FontSize = 11, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush) };
            ToolTipService.SetToolTip(smoothLabel, "NVIDIA Smooth Motion — генерация кадров на уровне драйвера. Добавляет интерполированные кадры для более плавной картинки. Требуется RTX 40-й серии и новее.");
            smoothCol.Children.Add(smoothLabel);

            // Enable
            bool smoothMotionEnabled;
            {
                smoothCol.Children.Add(new TextBlock { Text = "Включить", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) });
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
                };
                ToolTipService.SetToolTip(combo, "Включение Smooth Motion — выкл: отключено. Вкл: включает генерацию кадров на уровне драйвера (только RTX 40-й серии и новее).");
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
                    _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(card));
                };
                smoothCol.Children.Add(combo);
                init = false;
            }

            // APIs
            {
                smoothCol.Children.Add(new TextBlock { Text = "Разрешённые API", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) });
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
                    IsEnabled = smoothMotionEnabled,
                    Opacity = smoothMotionEnabled ? 1.0 : 0.4,
                };
                ToolTipService.SetToolTip(combo, "API Smooth Motion — какие графические API может подхватывать Smooth Motion. «Нет» — отключено для всех API.");
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
                    IsEnabled = smoothMotionEnabled,
                    Opacity = smoothMotionEnabled ? 1.0 : 0.4,
                };
                ToolTipService.SetToolTip(combo, "Flip Pacing — выкл: приоритет низкой задержки. Вкл: приоритет плавности вывода кадров. Устанавливается сразу для полноэкранного и оконного режимов.");
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

            // ── Column 6: Other (Power, G-Sync, Restore) ──
            var powerCol = new StackPanel { Spacing = 4 };
            var powerLabel = new TextBlock { Text = "Другое", FontSize = 11, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush) };
            ToolTipService.SetToolTip(powerLabel, "Управление питанием, контроль G-Sync и сброс профиля.");
            powerCol.Children.Add(powerLabel);

            // Power Management Mode
            {
                powerCol.Children.Add(new TextBlock { Text = "Режим питания", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) });
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
                };
                ToolTipService.SetToolTip(combo, "Управление питанием — Adaptive: GPU снижает частоты в простое. Maximum: фиксирует максимальные частоты. Optimal: баланс (рекомендация NVIDIA).");
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
                };
                ToolTipService.SetToolTip(gsyncCombo, "Управление G-Sync для отдельной игры. «Отключено» принудительно выключает G-Sync для игры независимо от общей настройки.");
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
                Content = "Восстановить значения по умолчанию",
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
                "Вернуть профиль драйвера NVIDIA этой игры к заводским значениям. Удаляет все пользовательские настройки (пресеты, масштаб рендеринга, MFG, переопределения драйвера). Действие необратимо.");
            restoreProfileBtn.Click += async (s, ev) =>
            {
                var xamlRoot = (s as FrameworkElement)?.XamlRoot ?? _window.Content.XamlRoot;
                var warningDialog = new ContentDialog
                {
                    Title = "Восстановить настройки драйвера?",
                    Content = new TextBlock
                    {
                        Text = $"Настройки драйвера для {capturedName} будут возвращены к заводским, а DLL DLSS/Streamline — к исходным версиям. Действие необратимо.",
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 13,
                    },
                    PrimaryButtonText = "Восстановить",
                    CloseButtonText = "Отмена",
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

            // ── Column 8: ReBAR ──
            var rebarCol = new StackPanel { Spacing = 4 };
            var rebarLabel = new TextBlock { Text = "ReBAR", FontSize = 11, Foreground = UIFactory.Brush(ResourceKeys.TextPrimaryBrush) };
            ToolTipService.SetToolTip(rebarLabel, "Resizable BAR — позволяет CPU обращаться ко всей памяти GPU сразу. Может поднять производительность на 5–10% в отдельных играх. Нужны RTX 30+ и поддержка в BIOS.");
            rebarCol.Children.Add(rebarLabel);

            bool rebarEnabled = false; // set inside Enable block below
            ulong rebarSizeLimit = d.ReBarSizeLimit;

            // Enable — Auto (Default) / Off / On using new 0x000BFA21 setting
            {
                rebarCol.Children.Add(new TextBlock { Text = "Включить", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) });

                uint rebarEnableMode = d.ReBarEnableMode;
                // 0=Off→index 1, 1=Auto→index 0, 2=On→index 2
                int enableIdx = rebarEnableMode == 0 ? 1 : rebarEnableMode == 2 ? 2 : 0;

                var rebarEnableCombo = new ComboBox
                {
                    ItemsSource = new[] { "Авто (по умолчанию)", "Off", "On" },
                    SelectedIndex = enableIdx,
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    CornerRadius = new CornerRadius(6),
                };
                ToolTipService.SetToolTip(rebarEnableCombo, "Авто — решает драйвер. Вкл — принудительно включить ReBAR. Выкл — принудительно выключить ReBAR.");
                var rebarComboInit = true;
                rebarEnableCombo.SelectionChanged += (s, ev) =>
                {
                    if (rebarComboInit) return;
                    // index 0=Auto(1), 1=Off(0), 2=On(2)
                    uint mode = rebarEnableCombo.SelectedIndex switch { 1 => 0u, 2 => 2u, _ => 1u };
                    _ = Task.Run(() => nvidiaPresetService.SetReBarEnableMode(capturedName, installPathSafe, mode));
                    _window.DispatcherQueue?.TryEnqueue(() => BuildOverridesPanel(card));
                };
                rebarCol.Children.Add(rebarEnableCombo);
                rebarComboInit = false;

                // Derive enabled state for Mode/Size combos: only On enables them, Auto and Off grey them
                rebarEnabled = rebarEnableMode == 2;
            }

            // Mode
            {
                rebarCol.Children.Add(new TextBlock { Text = "Режим", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) });
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
                    IsEnabled = rebarEnabled,
                    Opacity = rebarEnabled ? 1.0 : 0.4,
                };
                ToolTipService.SetToolTip(rebarModeCombo, "Standard — консервативно. Optimized — агрессивное планирование драйвера (используется для игр из белого списка NVIDIA).");
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
                rebarCol.Children.Add(new TextBlock { Text = "Лимит размера", FontSize = 10, Foreground = UIFactory.Brush(ResourceKeys.TextTertiaryBrush), Margin = new Thickness(0, 2, 0, 0) });

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
                    IsEnabled = rebarEnabled,
                    Opacity = rebarEnabled ? 1.0 : 0.4,
                };
                ToolTipService.SetToolTip(rebarSizeCombo, "1 ГБ оптимален для большинства игр. Уменьшите до 512 МБ, если возникают фризы, связанные с ReBAR.");
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

            (nvBody ?? _window.NvidiaProfilePanel).Children.Add(nvidiaGrid);
        }

        // Admin notice at the bottom of the Nvidia Profile section
        bool isElevated = d.IsAdmin;
        (nvBody ?? _window.NvidiaProfilePanel).Children.Add(new TextBlock
        {
            Text = isElevated
                ? "✓ Запущено от администратора — все настройки профиля доступны для записи."
                : "⚠ Для записи настроек профиля драйвера нужны права администратора. Включите режим администратора в настройках или перезапустите RHI от его имени.",
            FontSize = 10,
            Foreground = UIFactory.Brush(isElevated ? ResourceKeys.TextTertiaryBrush : ResourceKeys.AccentAmberDimBrush),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        });

    }
}
