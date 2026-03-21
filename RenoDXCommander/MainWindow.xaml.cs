using Microsoft.UI;
using RenoDXCommander.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace RenoDXCommander;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    public string[] DcDllPickerNames => DllOverrideConstants.DcDllPickerNames;

    // Sensible default — used on first launch before any saved size exists
    private const int DefaultWidth  = 1280;
    private const int DefaultHeight = 1000;

    private readonly ICrashReporter _crashReporter;
    private readonly CardBuilder _cardBuilder;
    private readonly DetailPanelBuilder _detailPanelBuilder;
    private readonly OverridesFlyoutBuilder _overridesFlyoutBuilder;
    private readonly DialogService _dialogService;
    private readonly SettingsHandler _settingsHandler;
    private readonly InstallEventHandler _installEventHandler;
    private readonly WindowStateManager _windowStateManager;
    private readonly DragDropHandler _dragDropHandler;
    private readonly AddonFileWatcher _addonFileWatcher;

    /// <summary>Exposes the detail panel builder for extracted handler classes.</summary>
    internal DetailPanelBuilder DetailPanelBuilderInstance => _detailPanelBuilder;

    public MainWindow(MainViewModel viewModel, ICrashReporter crashReporter)
    {
        ViewModel = viewModel;
        _crashReporter = crashReporter;
        InitializeComponent();
        _cardBuilder = new CardBuilder(this);
        _detailPanelBuilder = new DetailPanelBuilder(this);
        _overridesFlyoutBuilder = new OverridesFlyoutBuilder(this, crashReporter);
        _dialogService = new DialogService(this);
        _settingsHandler = new SettingsHandler(this);
        _installEventHandler = new InstallEventHandler(this, PickFolderAsync);
        AuxInstallService.EnsureInisDir();       // create inis folder on first run
        AuxInstallService.EnsureReShadeStaging(); // create staging dir (DLLs downloaded by ReShadeUpdateService)
        Title = "RDXC - RenoDXCommander";
        // Fire-and-forget: check/download Lilium HDR shaders in the background
        var shaderTask = ViewModel.ShaderPackServiceInstance.EnsureLatestAsync();
        shaderTask.SafeFireAndForget("MainWindow.ShaderPack");
        ViewModel.SetShaderPackReadyTask(shaderTask);
        _crashReporter.Log("[MainWindow.MainWindow] InitializeComponent complete");
        // Set a sensible default size immediately so the window isn't huge on first launch.
        // TryRestoreWindowBounds (called on Activated) will then override this with the
        // saved size+position from the previous session, if one exists.
        AppWindow.Resize(new Windows.Graphics.SizeInt32(DefaultWidth, DefaultHeight));

        // Enforce minimum window size and enable Win32 drag-and-drop via WindowStateManager
        var hwnd = WindowNative.GetWindowHandle(this);
        _dragDropHandler = new DragDropHandler(this, _crashReporter);
        _windowStateManager = new WindowStateManager(this, hwnd, _dragDropHandler, _crashReporter);
        _windowStateManager.InstallWndProcSubclass();
        _windowStateManager.EnableDragAccept();

        // Set the title bar icon (unpackaged apps need this explicitly)
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        AppWindow.SetIcon(Path.Combine(exeDir, "icon.ico"));

        // Dark title bar — match our theme
        if (AppWindow.TitleBar is { } titleBar)
        {
            var res = Application.Current.Resources;
            titleBar.BackgroundColor              = (Windows.UI.Color)res["TitleBarBackground"];
            titleBar.ForegroundColor              = (Windows.UI.Color)res["TitleBarForeground"];
            titleBar.InactiveBackgroundColor      = (Windows.UI.Color)res["TitleBarInactiveBackground"];
            titleBar.InactiveForegroundColor      = (Windows.UI.Color)res["TitleBarInactiveForeground"];
            titleBar.ButtonBackgroundColor        = (Windows.UI.Color)res["TitleBarButtonBackground"];
            titleBar.ButtonForegroundColor        = (Windows.UI.Color)res["TitleBarButtonForeground"];
            titleBar.ButtonHoverBackgroundColor   = (Windows.UI.Color)res["TitleBarButtonHoverBackground"];
            titleBar.ButtonHoverForegroundColor   = (Windows.UI.Color)res["TitleBarButtonHoverForeground"];
            titleBar.ButtonPressedBackgroundColor = (Windows.UI.Color)res["TitleBarButtonPressedBackground"];
            titleBar.ButtonPressedForegroundColor = (Windows.UI.Color)res["TitleBarButtonPressedForeground"];
            titleBar.ButtonInactiveBackgroundColor = (Windows.UI.Color)res["TitleBarButtonInactiveBackground"];
            titleBar.ButtonInactiveForegroundColor = (Windows.UI.Color)res["TitleBarButtonInactiveForeground"];
        }
        // Restore window size & position after activation (ensure HWND is ready)
        this.Activated += MainWindow_Activated;
        ViewModel.SetDispatcher(DispatcherQueue);
        ViewModel.ConfirmForeignDxgiOverwrite = _dialogService.ShowForeignDxgiConfirmDialogAsync;
        ViewModel.ConfirmForeignWinmmOverwrite = _dialogService.ShowForeignWinmmConfirmDialogAsync;
        ViewModel.ShowShaderSelectionPicker = async (current) =>
            await ShaderPopupHelper.ShowAsync(Content.XamlRoot, ViewModel.ShaderPackServiceInstance, current, ShaderPopupHelper.PopupContext.Global);
        ViewModel.ShowPerGameShaderSelectionPicker = async (gameName, current) =>
            await ShaderPopupHelper.ShowAsync(Content.XamlRoot, ViewModel.ShaderPackServiceInstance, current, ShaderPopupHelper.PopupContext.PerGame);
        ViewModel.PropertyChanged += OnViewModelChanged;
        GameList.ItemsSource = ViewModel.DisplayedGames;
        // Apply initial visibility
        UpdatePageVisibility();
        // Sync DcDllPicker text once the ComboBox is loaded
        DcDllPicker.Loaded += (_, _) => SyncDcDllPickerText();
        // Always show the ✕ clear button on search box
        SearchBox.Loaded += (_, _) => VisualStateManager.GoToState(SearchBox, "ButtonVisible", false);
        ViewModel.InitializeAsync().SafeFireAndForget("MainWindow.Init");
        // Silent update check — runs in background, shows dialog only if update found
        CheckForAppUpdateAsync().SafeFireAndForget("MainWindow.UpdateCheck");
        // Show patch notes on first launch after update
        ShowPatchNotesIfNewVersionAsync().SafeFireAndForget("MainWindow.PatchNotes");
        // Register .addon64/.addon32 file associations (per-user, no admin)
        FileAssociationService.Register(crashReporter);
        // Watch Downloads folder for addon files
        _addonFileWatcher = new AddonFileWatcher(crashReporter);
        _addonFileWatcher.AddonFileDetected += path =>
            DispatcherQueue.TryEnqueue(() => HandleAddonFile(path));
        // Apply saved watch folder if configured
        var savedFolder = ViewModel.Settings.AddonWatchFolder;
        if (!string.IsNullOrWhiteSpace(savedFolder))
            _addonFileWatcher.SetWatchPath(savedFolder);
        else
            _addonFileWatcher.Start();
        this.Closed += MainWindow_Closed;
    }


    private void MainWindow_Activated(object? sender, WindowActivatedEventArgs e)
    {
        try
        {
            // Only restore once
            this.Activated -= MainWindow_Activated;
            _windowStateManager.TryRestoreWindowBounds();
        }
        catch (Exception ex) { _crashReporter.Log($"[MainWindow.MainWindow_Activated] Failed to restore window bounds — {ex.Message}"); }
    }

    private void MainWindow_Closed(object? sender, WindowEventArgs e)
    {
        // Unsubscribe from ViewModel property changes to avoid leaks (Requirement 8.5)
        ViewModel.PropertyChanged -= OnViewModelChanged;

        // Unsubscribe detail panel builder from current card's PropertyChanged
        if (_detailPanelBuilder.CurrentDetailCard != null)
            _detailPanelBuilder.CurrentDetailCard.PropertyChanged -= _detailPanelBuilder.DetailCard_PropertyChanged;

        _addonFileWatcher.Dispose();
        SingleInstanceService.Stop();
        ViewModel.SaveSettingsPublic(); // persist GridLayout and other settings
        _windowStateManager.SaveWindowBounds();
    }

    // ── Addon file handling (Downloads watcher + file association) ───────────────

    /// <summary>
    /// Handles an addon file detected by the Downloads watcher or passed via command-line.
    /// Waits for initialization to complete, then delegates to the drag-drop handler.
    /// </summary>
    internal async void HandleAddonFile(string filePath)
    {
        try
        {
            _crashReporter.Log($"[MainWindow.HandleAddonFile] Processing '{Path.GetFileName(filePath)}'");

            // Wait for game list to be populated before showing the picker
            while (ViewModel.IsLoading)
                await Task.Delay(200);

            // Bring window to front
            NativeInterop.SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));

            await _dragDropHandler.ProcessDroppedAddon(filePath);
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainWindow.HandleAddonFile] Failed — {ex.Message}");
        }
    }

    // ── ViewModel → UI sync ───────────────────────────────────────────────────────

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(ViewModel.IsLoading):
                    var loading = ViewModel.IsLoading;
                    // After initial boot, keep the game view visible during refreshes
                    bool silent = ViewModel.HasInitialized;
                    if (ViewModel.CurrentPage != AppPage.Settings && !silent)
                    {
                        LoadingPanel.Visibility = loading ? Visibility.Visible  : Visibility.Collapsed;
                        GameViewPanel.Visibility = loading ? Visibility.Collapsed : Visibility.Visible;
                    }
                    if (!silent) LoadingRing.IsActive = loading;
                    RefreshBtn.IsEnabled = !loading;
                    if (!silent) StatusDot.Fill = new SolidColorBrush(loading
                        ? ((SolidColorBrush)Application.Current.Resources[ResourceKeys.AccentAmberBrush]).Color
                        : ((SolidColorBrush)Application.Current.Resources[ResourceKeys.AccentGreenBrush]).Color);
                    if (!loading)
                    {
                        if (!silent) ViewModel.MarkInitialized();
                        TryRestoreSelection();
                        RefreshFilterButtonStyles();
                    }
                    break;
                case nameof(ViewModel.StatusText):
                case nameof(ViewModel.SubStatusText):
                    LoadingTitle.Text    = ViewModel.StatusText;
                    LoadingSubtitle.Text = ViewModel.SubStatusText;
                    StatusBarText.Text   = ViewModel.StatusText
                        + (string.IsNullOrEmpty(ViewModel.SubStatusText) ? "" : $"  —  {ViewModel.SubStatusText}");
                    break;
                case nameof(ViewModel.InstalledCount):
                    InstalledCountText.Text = $"{ViewModel.InstalledCount} installed";
                    break;
                case nameof(ViewModel.TotalGames):
                    GameCountText.Text = $"{ViewModel.TotalGames} shown";
                    if (ViewModel.IsGridLayout) RebuildCardGrid();
                    break;
                case nameof(ViewModel.HiddenCount):
                    HiddenCountText.Text = ViewModel.HiddenCount > 0
                        ? $"· {ViewModel.HiddenCount} hidden" : "";
                    break;
                case nameof(ViewModel.DcDllFileName):
                    SyncDcDllPickerText();
                    break;
                case nameof(ViewModel.FilterMode):
                    RefreshFilterButtonStyles();
                    break;
                case nameof(ViewModel.AnyUpdateAvailable):
                    UpdateBtn.Background  = UIFactory.GetBrush(ViewModel.UpdateAllBtnBackground);
                    UpdateBtn.Foreground  = UIFactory.GetBrush(ViewModel.UpdateAllBtnForeground);
                    UpdateBtn.BorderBrush = UIFactory.GetBrush(ViewModel.UpdateAllBtnBorder);
                    break;
            }
        });
    }

    // ── Header buttons ────────────────────────────────────────────────────────────

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        _crashReporter.Log("[MainWindow.RefreshButton_Click] User clicked Refresh");
        _ = RefreshWithScrollRestore();
    }

    private void FullRefreshButton_Click(object sender, RoutedEventArgs e)
    {
        _crashReporter.Log("[MainWindow.FullRefreshButton_Click] User clicked Full Refresh");
        _ = FullRefreshWithScrollRestore();
    }

    private async void BrowseAddonWatchFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folderPath = await PickFolderAsync();
            if (!string.IsNullOrEmpty(folderPath))
            {
                AddonWatchFolderBox.Text = folderPath;
                ViewModel.Settings.AddonWatchFolder = folderPath;
                _addonFileWatcher.SetWatchPath(folderPath);
                ViewModel.SaveSettingsPublic();
                _crashReporter.Log($"[MainWindow] Addon watch folder set to: {folderPath}");
            }
        }
        catch (Exception ex) { _crashReporter.Log($"[MainWindow.BrowseAddonWatchFolder] {ex.Message}"); }
    }

    private void ResetAddonWatchFolder_Click(object sender, RoutedEventArgs e)
    {
        AddonWatchFolderBox.Text = "";
        ViewModel.Settings.AddonWatchFolder = "";
        var defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        _addonFileWatcher.SetWatchPath(defaultPath);
        ViewModel.SaveSettingsPublic();
        _crashReporter.Log("[MainWindow] Addon watch folder reset to default Downloads");
    }

    private async Task RefreshWithScrollRestore()
    {
        var selectedName = (GameList.SelectedItem as GameCardViewModel)?.GameName;

        await ViewModel.RefreshAsync();

        RestoreScrollAndSelection(selectedName);
    }

    private async Task FullRefreshWithScrollRestore()
    {
        var selectedName = (GameList.SelectedItem as GameCardViewModel)?.GameName;

        await ViewModel.FullRefreshAsync();

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

    private string? _pendingReselect;

    private void RsIniButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;
        if (string.IsNullOrEmpty(card.InstallPath)) return;
        try
        {
            if (card.RequiresVulkanInstall)
            {
                AuxInstallService.MergeRsVulkanIni(card.InstallPath);
                VulkanFootprintService.Create(card.InstallPath);
                // Deploy shaders for Vulkan games (no DLL install, so shaders go with INI)
                ViewModel.DeployShadersForCard(card.GameName);
            }
            else
                AuxInstallService.MergeRsIni(card.InstallPath);
            AuxInstallService.CopyRsPresetIniIfPresent(card.InstallPath);
            bool presetDeployed = File.Exists(AuxInstallService.RsPresetIniPath);
            card.RsActionMessage = presetDeployed
                ? "✅ reshade.ini merged & ReShadePreset.ini copied."
                : "✅ reshade.ini merged into game folder.";
        }
        catch (Exception ex)
        {
            card.RsActionMessage = $"❌ {ex.Message}";
        }
    }

    private void DcIniButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;
        if (string.IsNullOrEmpty(card.InstallPath)) return;
        try
        {
            AuxInstallService.CopyDcIni(card.InstallPath);
            card.DcActionMessage = "✅ DisplayCommander.toml copied to game folder.";
        }
        catch (Exception ex)
        {
            card.DcActionMessage = $"❌ {ex.Message}";
        }
    }

    private void SupportDiscord_Click(object sender, RoutedEventArgs e)
    {
        _ = Windows.System.Launcher.LaunchUriAsync(
            new Uri("https://discordapp.com/channels/1296187754979528747/1475173660686815374"));
    }

    private void SupportGuide_Click(object sender, RoutedEventArgs e)
    {
        _ = Windows.System.Launcher.LaunchUriAsync(
            new Uri("https://github.com/RankFTW/rdxc-manifest?tab=readme-ov-file#renodx-commander--detailed-guide"));
    }

    private async Task<bool> ShowForeignDxgiConfirmDialogAsync(GameCardViewModel card, string dxgiPath)
        => await _dialogService.ShowForeignDxgiConfirmDialogAsync(card, dxgiPath);

    private async Task<bool> ShowForeignWinmmConfirmDialogAsync(GameCardViewModel card, string winmmPath)
        => await _dialogService.ShowForeignWinmmConfirmDialogAsync(card, winmmPath);

    // ── Auto-Update ────────────────────────────────────────────────────────────

    private async Task CheckForAppUpdateAsync()
        => await _dialogService.CheckForAppUpdateAsync();

    private async Task ShowUpdateDialogAsync(UpdateInfo updateInfo)
        => await _dialogService.ShowUpdateDialogAsync(updateInfo);

    private async Task DownloadAndInstallUpdateAsync(UpdateInfo updateInfo)
        => await _dialogService.DownloadAndInstallUpdateAsync(updateInfo);

    private void LayoutToggle_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.IsGridLayout = !ViewModel.IsGridLayout;
        ViewModel.SaveSettingsPublic(); // persist the chosen layout
        if (ViewModel.IsGridLayout)
        {
            RebuildCardGrid();
        }
        else
        {
            // Switching to detail mode — repopulate detail panel for selected game if any
            if (ViewModel.SelectedGame is { } card)
            {
                PopulateDetailPanel(card);
                DetailPanel.Visibility = Visibility.Visible;
                BuildOverridesPanel(card);
                OverridesContainer.Visibility = Visibility.Visible;
            }
        }
    }

    // ── Card Grid rendering (Tasks 6.2–6.4) ──────────────────────────────────────

    internal void RebuildCardGrid()
    {
        CardGridPanel.Children.Clear();
        foreach (var card in ViewModel.DisplayedGames)
        {
            try
            {
                CardGridPanel.Children.Add(BuildGameCard(card));
            }
            catch (Exception ex)
            {
                _crashReporter.Log($"[MainWindow.RebuildCardGrid] Skipped card '{card.GameName}' — {ex.Message}");
            }
        }
        // If the selected game is in the displayed list, scroll to it
        if (ViewModel.SelectedGame is { } sel && ViewModel.DisplayedGames.Contains(sel))
            ScrollToCard(sel);
    }

    private Border BuildGameCard(GameCardViewModel card) => _cardBuilder.BuildGameCard(card);

    /// <summary>
    /// Handler for the install flyout opening — builds the flyout content and attaches it.
    /// Called when the install button's flyout is about to open.
    /// </summary>
    internal void CardInstallFlyout_Opening(object? sender, object e)
    {
        if (sender is not Flyout flyout) return;
        if (flyout.Target is not FrameworkElement { Tag: GameCardViewModel card }) return;

        var content = _cardBuilder.BuildInstallFlyoutContent(card);

        var scrollViewer = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 400,
        };

        flyout.Content = scrollViewer;

        // Unsubscribe from PropertyChanged when flyout closes
        flyout.Closed += FlyoutClosed;

        void FlyoutClosed(object? s, object ev)
        {
            flyout.Closed -= FlyoutClosed;
            if (content.Tag is (GameCardViewModel c, System.ComponentModel.PropertyChangedEventHandler h))
            {
                c.PropertyChanged -= h;
            }
        }
    }

    // ── Per-component install flyout click handlers ──

    internal async void CardComponentInstall_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not GameCardViewModel card) return;
        var component = btn.DataContext as string;

        // Ensure install path exists (same pattern as CardInstallButton_Click)
        if (string.IsNullOrEmpty(card.InstallPath) || !System.IO.Directory.Exists(card.InstallPath))
        {
            var folder = await PickFolderAsync();
            if (folder == null) return;
            card.InstallPath = folder;
            ViewModel.SaveLibraryPublic();
        }

        switch (component)
        {
            case "RDX":
                await ViewModel.InstallModCommand.ExecuteAsync(card);
                break;
            case "RS":
                await ViewModel.InstallReShadeCommand.ExecuteAsync(card);
                break;
            case "DC":
                await ViewModel.InstallDcCommand.ExecuteAsync(card);
                break;
            case "Luma":
                await ViewModel.InstallLumaAsync(card);
                break;
        }
    }

    internal void CardComponentUninstall_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not GameCardViewModel card) return;
        var component = btn.DataContext as string;

        switch (component)
        {
            case "RDX":
                ViewModel.UninstallModCommand.Execute(card);
                break;
            case "RS":
                if (card.RequiresVulkanInstall)
                    ViewModel.UninstallVulkanReShadeCommand.Execute(card);
                else
                    ViewModel.UninstallReShadeCommand.Execute(card);
                break;
            case "DC":
                ViewModel.UninstallDcCommand.Execute(card);
                break;
            case "Luma":
                ViewModel.UninstallLumaCommand.Execute(card);
                break;
        }
    }

    internal void CardCopyRsIni_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;
        if (string.IsNullOrEmpty(card.InstallPath)) return;
        try
        {
            if (card.RequiresVulkanInstall)
            {
                AuxInstallService.MergeRsVulkanIni(card.InstallPath);
                VulkanFootprintService.Create(card.InstallPath);
                // Deploy shaders for Vulkan games (no DLL install, so shaders go with INI)
                ViewModel.DeployShadersForCard(card.GameName);
            }
            else
                AuxInstallService.MergeRsIni(card.InstallPath);
            card.RsActionMessage = "✅ reshade.ini merged into game folder.";
        }
        catch (Exception ex)
        {
            card.RsActionMessage = $"❌ {ex.Message}";
        }
    }

    internal void CardCopyDcToml_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: GameCardViewModel card }) return;
        if (string.IsNullOrEmpty(card.InstallPath)) return;
        try
        {
            AuxInstallService.CopyDcIni(card.InstallPath);
            card.DcActionMessage = "✅ DisplayCommander.toml copied to game folder.";
        }
        catch (Exception ex)
        {
            card.DcActionMessage = $"❌ {ex.Message}";
        }
    }

    // ── Card action button handlers (Task 6.4) ───────────────────────────────────

    internal async void CardInstallButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not GameCardViewModel card) return;

        // Route to Luma install if in Luma mode, otherwise RenoDX combined install
        if (card.LumaFeatureEnabled && card.IsLumaMode && card.LumaMod != null)
        {
            await ViewModel.InstallLumaAsync(card);
        }
        else
        {
            // Ensure install path exists
            if (string.IsNullOrEmpty(card.InstallPath) || !System.IO.Directory.Exists(card.InstallPath))
            {
                var folder = await PickFolderAsync();
                if (folder == null) return;
                card.InstallPath = folder;
                ViewModel.SaveLibraryPublic();
            }
            // Chain: RenoDX → DC → ReShade (skip components that are N/A)
            if (card.Mod?.SnapshotUrl != null)
                await ViewModel.InstallModCommand.ExecuteAsync(card);
            if (card.DcRowVisibility == Visibility.Visible)
                await ViewModel.InstallDcCommand.ExecuteAsync(card);
            if (card.ReShadeRowVisibility == Visibility.Visible)
                await ViewModel.InstallReShadeCommand.ExecuteAsync(card);
        }
    }

    internal void CardFavouriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not GameCardViewModel card) return;
        ViewModel.ToggleFavouriteCommand.Execute(card);
        btn.Content = card.IsFavourite ? "⭐" : "☆";

        // Also refresh the detail panel icon if this is the selected game
        if (card == ViewModel.SelectedGame)
        {
            DetailFavIcon.Text = card.IsFavourite ? "⭐" : "☆";
            DetailFavIcon.Foreground = new SolidColorBrush(card.IsFavourite
                ? ((SolidColorBrush)Application.Current.Resources[ResourceKeys.AccentAmberBrush]).Color
                : ((SolidColorBrush)Application.Current.Resources[ResourceKeys.TextDisabledBrush]).Color);
        }
    }

    private void CardOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var card = GetCardFromSender(sender);
        if (card == null || string.IsNullOrEmpty(card.InstallPath)) return;
        if (System.IO.Directory.Exists(card.InstallPath))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(card.InstallPath) { UseShellExecute = true });
    }

    internal void CardOverridesButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement anchor && anchor.Tag is GameCardViewModel card)
        {
            ViewModel.SelectedGame = card;
            OpenOverridesFlyout(card, anchor);
        }
    }

    internal void CardMoreMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement anchor || anchor.Tag is not GameCardViewModel card)
            return;

        ViewModel.SelectedGame = card;

        var menu = new MenuFlyout
        {
            Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight,
        };

        // ── Open Folder ──
        var openFolderItem = new MenuFlyoutItem
        {
            Text = "📂 Open Folder",
            Tag = card,
        };
        openFolderItem.Click += CardOpenFolder_Click;
        menu.Items.Add(openFolderItem);

        // ── Hide / Show ──
        var hideItem = new MenuFlyoutItem
        {
            Text = card.HideButtonLabel,
            Tag = card,
        };
        hideItem.Click += (s, ev) => ViewModel.ToggleHideGameCommand.Execute(card);
        menu.Items.Add(hideItem);

        // ── Luma toggle (conditional — only when Luma is available for this game) ──
        if (card.LumaFeatureEnabled && card.IsLumaAvailable)
        {
            var lumaLabel = card.IsLumaMode ? "🟢 Luma Enabled" : "⚫ Enable Luma";
            var lumaItem = new MenuFlyoutItem
            {
                Text = lumaLabel,
                Tag = card,
            };
            lumaItem.Click += (s, ev) => ViewModel.ToggleLumaMode(card);
            menu.Items.Add(lumaItem);
        }

        menu.Items.Add(new MenuFlyoutSeparator());

        // ── Discussion / Instructions (conditional) ──
        if (card.HasNameUrl)
        {
            var discussionItem = new MenuFlyoutItem
            {
                Text = "ℹ Discussion / Instructions",
                Tag = card,
            };
            discussionItem.Click += async (s, ev) =>
            {
                if (card.NameUrl != null)
                    await Windows.System.Launcher.LaunchUriAsync(new Uri(card.NameUrl));
            };
            menu.Items.Add(discussionItem);
        }

        // ── View Notes (conditional) ──
        if (card.HasNotes)
        {
            var notesItem = new MenuFlyoutItem
            {
                Text = "💬 View Notes",
                Tag = card,
            };
            notesItem.Click += NotesButton_Click;
            menu.Items.Add(notesItem);
        }

        menu.ShowAt(anchor);
    }

    private void OpenOverridesFlyout(GameCardViewModel card, FrameworkElement anchor)
        => _overridesFlyoutBuilder.OpenOverridesFlyout(card, anchor);



    /// <summary>Scrolls the card grid to bring the given card into view and highlights it.</summary>
    private void ScrollToCard(GameCardViewModel target)
    {
        foreach (var child in CardGridPanel.Children)
        {
            if (child is Border b && b.Tag is GameCardViewModel c)
            {
                bool isTarget = c == target;
                c.CardHighlighted = isTarget;
                if (isTarget)
                    b.StartBringIntoView();
            }
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
        => _settingsHandler.SettingsButton_Click(sender, e);

    private void SkipUpdateToggle_Toggled(object sender, RoutedEventArgs e)
        => _settingsHandler.SkipUpdateToggle_Toggled(sender, e);

    private void BetaOptInToggle_Toggled(object sender, RoutedEventArgs e)
        => _settingsHandler.BetaOptInToggle_Toggled(sender, e);

    private void VerboseLoggingToggle_Toggled(object sender, RoutedEventArgs e)
        => _settingsHandler.VerboseLoggingToggle_Toggled(sender, e);

    private async Task ShowPatchNotesIfNewVersionAsync()
        => await _dialogService.ShowPatchNotesIfNewVersionAsync();

    private async Task ShowPatchNotesDialogAsync()
        => await _dialogService.ShowPatchNotesDialogAsync();

    private async void PatchNotesLink_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await ShowPatchNotesDialogAsync();
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainWindow.PatchNotesLink_Click] Patch notes dialog error — {ex.Message}");
        }
    }

    private void OpenLogsFolder_Click(object sender, RoutedEventArgs e)
        => _settingsHandler.OpenLogsFolder_Click(sender, e);

    private void OpenDownloadsFolder_Click(object sender, RoutedEventArgs e)
        => _settingsHandler.OpenDownloadsFolder_Click(sender, e);

    private void SettingsBack_Click(object sender, RoutedEventArgs e)
        => _settingsHandler.SettingsBack_Click(sender, e);

    private void DetailScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        const double maxWidth = 850;
        const double padding = 48; // 24 left + 24 right
        var available = e.NewSize.Width - padding;
        DetailPanel.Width = available > maxWidth ? maxWidth : (available > 0 ? available : double.NaN);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ViewModel.SearchQuery = SearchBox.Text;
        // Always show the clear (✕) button
        VisualStateManager.GoToState(SearchBox, "ButtonVisible", true);
    }

    // ShowHidden toggle removed; Hidden tab shows hidden games by default.

    // ── Manual add game ───────────────────────────────────────────────────────────

    private async void AddGameButton_Click(object sender, RoutedEventArgs e)
    {
        // Ask for game name
        var nameBox = new TextBox { PlaceholderText = "Game name (e.g. Cyberpunk 2077)", Width = 350 };
        var nameDialog = new ContentDialog
        {
            Title           = "➕ Add Game Manually",
            Content         = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = "Enter the game name exactly as it appears on the wiki mod list:", TextWrapping = TextWrapping.Wrap, Foreground = Brush(ResourceKeys.TextSecondaryBrush) },
                    nameBox
                }
            },
            PrimaryButtonText   = "Pick Folder →",
            CloseButtonText     = "Cancel",
            XamlRoot            = Content.XamlRoot,
            Background          = Brush(ResourceKeys.SurfaceToolbarBrush),
        };
        var result = await nameDialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        var gameName = nameBox.Text.Trim();
        if (string.IsNullOrEmpty(gameName)) return;
        _crashReporter.Log($"[MainWindow.AddGameButton_Click] Adding game: {gameName}");

        // Pick the game folder
        var folder = await PickFolderAsync();
        if (folder == null) return;

        var game = new DetectedGame
        {
            Name = gameName, InstallPath = folder, Source = "Manual", IsManuallyAdded = true
        };
        ViewModel.AddManualGameCommand.Execute(game);
    }

    // ── Drag-and-drop (delegated to DragDropHandler) ────────────────────────────

    private void Grid_DragOver(object sender, DragEventArgs e)
        => _dragDropHandler.Grid_DragOver(sender, e);

    private void Grid_Drop(object sender, DragEventArgs e)
        => _dragDropHandler.Grid_Drop(sender, e);


    // ── Filter tabs ───────────────────────────────────────────────────────────────

    private void Filter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        ViewModel.SetFilterCommand.Execute(btn.Tag as string ?? "Detected");
        RefreshFilterButtonStyles();
    }

    /// <summary>
    /// Syncs filter tab button styles to match the current ActiveFilters set.
    /// Called after SetFilter and also when FilterMode changes (e.g. on restore).
    /// </summary>
    private void RefreshFilterButtonStyles()
    {
        var active   = ((SolidColorBrush)Application.Current.Resources[ResourceKeys.ChipActiveBrush]).Color;
        var inactive = ((SolidColorBrush)Application.Current.Resources[ResourceKeys.ChipDefaultBrush]).Color;
        var activeFg   = ((SolidColorBrush)Application.Current.Resources[ResourceKeys.TextPrimaryBrush]).Color;
        var inactiveFg = ((SolidColorBrush)Application.Current.Resources[ResourceKeys.ChipTextBrush]).Color;

        foreach (var b in new[] { FilterFavourites, FilterInstalled, FilterDetected, FilterUnreal, FilterUnity, FilterOther, FilterRenoDX, FilterLuma, FilterHidden })
        {
            bool isActive = ViewModel.ActiveFilters.Contains(b.Tag as string ?? "");
            b.Background  = new SolidColorBrush(isActive ? active   : inactive);
            b.Foreground  = new SolidColorBrush(isActive ? activeFg : inactiveFg);
        }
    }

    private void FavouriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not GameCardViewModel card) return;
        ViewModel.ToggleFavouriteCommand.Execute(card);

        // Refresh the detail panel icon to reflect the new state
        DetailFavIcon.Text = card.IsFavourite ? "⭐" : "☆";
        DetailFavIcon.Foreground = new SolidColorBrush(card.IsFavourite
            ? ((SolidColorBrush)Application.Current.Resources[ResourceKeys.AccentAmberBrush]).Color
            : ((SolidColorBrush)Application.Current.Resources[ResourceKeys.TextDisabledBrush]).Color);
    }

    // ── Card handlers ─────────────────────────────────────────────────────────────

    private void ExpandComponents_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is GameCardViewModel card)
            card.ComponentExpanded = !card.ComponentExpanded;
    }

    private void CombinedInstallButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.CombinedInstallButton_Click(sender, e);

    private void InstallButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.InstallButton_Click(sender, e);

    private void Install64Button_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.Install64Button_Click(sender, e);

    private void Install32Button_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.Install32Button_Click(sender, e);

    private async Task EnsurePathAndInstall(GameCardViewModel card, Func<Task> installAction)
        => await _installEventHandler.EnsurePathAndInstall(card, installAction);

    private void UninstallButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.UninstallButton_Click(sender, e);

    private void InstallRsButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.InstallRsButton_Click(sender, e);

    private void UninstallRsButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.UninstallRsButton_Click(sender, e);

    // ── Shaders mode cycle handler ──────────────────────────────────────────

    private void ChooseShadersButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.ChooseShadersButton_Click(sender, e);

    private void DeployDcModeButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.DeployDcModeButton_Click(sender, e);

    private bool _dcDllPickerIsTyping;

    /// <summary>
    /// Syncs the DcDllPicker ComboBox text/selection to match ViewModel.DcDllFileName.
    /// Called on init and when the property changes from external sources.
    /// </summary>
    private void SyncDcDllPickerText()
    {
        var value = ViewModel.DcDllFileName;
        _dcDllPickerIsTyping = true; // suppress SelectionChanged while we sync
        // If the value matches a dropdown item, select it; otherwise clear selection and set text
        var match = DcDllPickerNames.FirstOrDefault(n => n.Equals(value, StringComparison.OrdinalIgnoreCase));
        if (match != null)
            DcDllPicker.SelectedItem = match;
        else
        {
            DcDllPicker.SelectedItem = null;
            DcDllPicker.Text = value;
        }
        _dcDllPickerIsTyping = false;
    }

    private void DcDllPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_dcDllPickerIsTyping) return;
        if (sender is ComboBox combo && combo.SelectedItem is string selected && !string.IsNullOrWhiteSpace(selected))
            ViewModel.DcDllFileName = selected;
    }

    private void DcDllPicker_TextSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs e)
    {
        var typed = e.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(typed))
        {
            _dcDllPickerIsTyping = true; // suppress SelectionChanged side-effects
            ViewModel.DcDllFileName = typed;
            sender.SelectedItem = null; // clear stale selection so ComboBox doesn't revert
            _dcDllPickerIsTyping = false;
        }
        e.Handled = true;
    }

    // ── Update All handlers ──────────────────────────────────────────────────

    private async void UpdateAllButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.UpdateAllReShadeAsync();
        await ViewModel.UpdateAllDcAsync();
        await ViewModel.UpdateAllRenoDxAsync();
    }

    private async void UpdateAllRenoDx_Click(object sender, RoutedEventArgs e)
        => await ViewModel.UpdateAllRenoDxAsync();

    private async void UpdateAllReShade_Click(object sender, RoutedEventArgs e)
        => await ViewModel.UpdateAllReShadeAsync();

    private async void UpdateAllDc_Click(object sender, RoutedEventArgs e)
        => await ViewModel.UpdateAllDcAsync();

    private void InstallDcButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.InstallDcButton_Click(sender, e);

    private void UninstallDcButton_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.UninstallDcButton_Click(sender, e);

    private void LumaToggle_Click(object sender, RoutedEventArgs e)
        => _installEventHandler.LumaToggle_Click(sender, e);

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

    private void HideButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetCardFromSender(sender) is { } card)
            ViewModel.ToggleHideGameCommand.Execute(card);
    }

    private async void BrowseFolder_Click(object sender, RoutedEventArgs e)
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

    private void RemoveManualGame_Click(object sender, RoutedEventArgs e)
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

    internal async void ExternalLink_Click(object sender, RoutedEventArgs e)
    {
        var card = GetCardFromSender(sender);
        if (card == null) return;
        // When IsExternalOnly the ExternalUrl has already been resolved correctly
        // (e.g. forced to Discord by ApplyCardOverrides). Use it directly so a
        // NexusUrl on the underlying mod can't override the intended destination.
        var url = card.IsExternalOnly ? card.ExternalUrl : (card.NexusUrl ?? card.DiscordUrl ?? card.ExternalUrl);
        if (!string.IsNullOrEmpty(url))
            await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
    }


    private async void NameLink_Click(object sender, RoutedEventArgs e)
    {
        var card = GetCardFromSender(sender);
        if (card?.NameUrl != null)
            await Windows.System.Launcher.LaunchUriAsync(new Uri(card.NameUrl));
    }

    internal async void CardInfoLink_Click(object sender, RoutedEventArgs e)
    {
        var card = GetCardFromSender(sender);
        if (card?.NameUrl != null)
        {
            try { await Windows.System.Launcher.LaunchUriAsync(new Uri(card.NameUrl)); }
            catch (Exception ex) { _crashReporter.Log($"[MainWindow.CardInfoLink_Click] Failed — {ex.Message}"); }
        }
    }

    internal void CardNotesButton_Click(object sender, RoutedEventArgs e)
    {
        NotesButton_Click(sender, e);
    }

    internal void Card_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        try
        {
            if (sender is not Border b || b.Tag is not GameCardViewModel card) return;

            foreach (var c in ViewModel.DisplayedGames)
                c.CardHighlighted = false;

            card.CardHighlighted = true;
            ViewModel.SelectedGame = card;
        }
        catch (Exception ex) { _crashReporter.Log($"[MainWindow.Card_PointerPressed] Error selecting card — {ex.Message}"); }
    }

    private void NotesButton_Click(object sender, RoutedEventArgs e)
        => _dialogService.NotesButton_Click(sender, e);

    /// <summary>Looks up a SolidColorBrush from the merged theme resource dictionaries.</summary>
    private SolidColorBrush Brush(string key) =>
        (SolidColorBrush)Application.Current.Resources[key];

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static GameCardViewModel? GetCardFromSender(object sender) => sender switch
    {
        Button btn          when btn.Tag  is GameCardViewModel c => c,
        MenuFlyoutItem item when item.Tag is GameCardViewModel c => c,
        _ => null
    };

    private async Task<string?> PickFolderAsync(string? suggestedPath = null)
    {
        // WinUI 3 unpackaged FolderPicker ignores SuggestedStartLocation for arbitrary paths.
        // Use IFileOpenDialog via COM directly so we can call SetFolder() with the game directory.
        if (!string.IsNullOrEmpty(suggestedPath) && Directory.Exists(suggestedPath))
        {
            try
            {
                return await Task.Run(() =>
                {
                    var dialog = (NativeInterop.IFileOpenDialog)new NativeInterop.FileOpenDialogClass();
                    dialog.SetOptions(NativeInterop.FOS.FOS_PICKFOLDERS | NativeInterop.FOS.FOS_FORCEFILESYSTEM);

                    // Set the initial folder to the game directory
                    int hr = NativeInterop.SHCreateItemFromParsingName(suggestedPath, IntPtr.Zero,
                        ref NativeInterop.IID_IShellItem, out NativeInterop.IShellItem startFolder);
                    if (hr == 0 && startFolder != null)
                        dialog.SetFolder(startFolder);

                    var hwnd = WindowNative.GetWindowHandle(this);
                    hr = dialog.Show(hwnd);
                    if (hr != 0) return null; // user cancelled (HRESULT_FROM_WIN32(ERROR_CANCELLED))

                    dialog.GetResult(out NativeInterop.IShellItem result);
                    result.GetDisplayName(NativeInterop.SIGDN.SIGDN_FILESYSPATH, out string path);
                    return path;
                });
            }
            catch
            {
                // Fall through to standard picker on any COM failure
            }
        }

        // Standard picker for the no-suggested-path case (Add Game, etc.)
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");
        var hwnd2 = WindowNative.GetWindowHandle(this);
        InitializeWithWindow.Initialize(picker, hwnd2);
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    // ── Page visibility management ──────────────────────────────────────────────

    private void UpdatePageVisibility()
    {
        // Show the correct panel based on current page and loading state
        if (ViewModel.CurrentPage == AppPage.Settings)
        {
            GameViewPanel.Visibility = Visibility.Collapsed;
            SettingsPanel.Visibility = Visibility.Visible;
            LoadingPanel.Visibility = Visibility.Collapsed;
        }
        else if (ViewModel.IsLoading)
        {
            GameViewPanel.Visibility = Visibility.Collapsed;
            SettingsPanel.Visibility = Visibility.Collapsed;
            // LoadingPanel is already Visible by default
        }
        else
        {
            GameViewPanel.Visibility = Visibility.Visible;
            SettingsPanel.Visibility = Visibility.Collapsed;
            LoadingPanel.Visibility = Visibility.Collapsed;
        }

        // Auto-select first game if nothing is selected
        if (GameList.SelectedItem == null && ViewModel.DisplayedGames.Count > 0)
        {
            GameList.SelectedItem = ViewModel.DisplayedGames[0];
        }
    }

    private void GameList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GameList.SelectedItem is GameCardViewModel card)
            {
                ViewModel.SelectedGame = card;

                if (ViewModel.IsGridLayout)
                {
                    // In grid mode, scroll to and highlight the selected card
                    ScrollToCard(card);
                }
                else
                {
                    // In detail mode, populate the detail panel as before
                    PopulateDetailPanel(card);
                    DetailPanel.Visibility = Visibility.Visible;
                    BuildOverridesPanel(card);
                    OverridesContainer.Visibility = Visibility.Visible;
                }
            }
            else
            {
                ViewModel.SelectedGame = null;

                if (ViewModel.IsGridLayout)
                {
                    // Clear highlight from all cards
                    foreach (var child in CardGridPanel.Children)
                    {
                        if (child is Border b && b.Tag is GameCardViewModel c)
                            c.CardHighlighted = false;
                    }
                }
                else
                {
                    DetailPanel.Visibility = Visibility.Collapsed;
                    OverridesPanel.Children.Clear();
                    OverridesContainer.Visibility = Visibility.Collapsed;
                }
            }
        }



    private void PopulateDetailPanel(GameCardViewModel card) => _detailPanelBuilder.PopulateDetailPanel(card);

    private void UpdateDetailComponentRows(GameCardViewModel card) => _detailPanelBuilder.UpdateDetailComponentRows(card);

    private void DetailCard_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => _detailPanelBuilder.DetailCard_PropertyChanged(sender, e);

    internal void UpdateLumaToggleStyle(bool isLumaMode)
    {
        DetailLumaToggleText.Text = isLumaMode ? "Luma Enabled" : "Luma Disabled";
        if (isLumaMode)
        {
            DetailLumaToggle.Background = Brush(ResourceKeys.AccentGreenBgBrush);
            DetailLumaToggle.Foreground = Brush(ResourceKeys.AccentGreenBrush);
            DetailLumaToggle.BorderBrush = Brush(ResourceKeys.AccentGreenBorderBrush);
        }
        else
        {
            DetailLumaToggle.Background = Brush(ResourceKeys.SurfaceOverlayBrush);
            DetailLumaToggle.Foreground = Brush(ResourceKeys.TextTertiaryBrush);
            DetailLumaToggle.BorderBrush = Brush(ResourceKeys.BorderStrongBrush);
        }
    }

    private void BuildOverridesPanel(GameCardViewModel card) => _detailPanelBuilder.BuildOverridesPanel(card);

    /// <summary>Allows DetailPanelBuilder to trigger a pending reselect after overrides save.</summary>
    internal void RequestReselect(string? name)
    {
        _pendingReselect = name;
        DispatcherQueue.TryEnqueue(TryRestoreSelection);
    }


    private void TryRestoreSelection()
    {
        if (_pendingReselect != null)
        {
            var name = _pendingReselect;
            var match = ViewModel.DisplayedGames.FirstOrDefault(c =>
                c.GameName.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                GameList.SelectedItem = match;
                GameList.ScrollIntoView(match);
                _pendingReselect = null;
                return;
            }
        }

        // Auto-select first game if nothing is selected
        if (GameList.SelectedItem == null && ViewModel.DisplayedGames.Count > 0)
        {
            GameList.SelectedItem = ViewModel.DisplayedGames[0];
        }
    }
}
