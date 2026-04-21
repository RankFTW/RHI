// MainWindow.xaml.cs — Constructor, field declarations, window lifecycle,
// addon file handling, and game list selection.

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
    private CompactViewBuilder? _compactViewBuilder;

    /// <summary>Exposes the detail panel builder for extracted handler classes.</summary>
    internal DetailPanelBuilder DetailPanelBuilderInstance => _detailPanelBuilder;

    private string? _pendingReselect;

    public MainWindow(MainViewModel viewModel, ICrashReporter crashReporter)
    {
        ViewModel = viewModel;
        _crashReporter = crashReporter;
        InitializeComponent();
        InitializeSkeletons();
        _cardBuilder = new CardBuilder(this);
        _detailPanelBuilder = new DetailPanelBuilder(this);
        _compactViewBuilder = new CompactViewBuilder(this);
        _overridesFlyoutBuilder = new OverridesFlyoutBuilder(this, crashReporter);
        _dialogService = new DialogService(this);
        _settingsHandler = new SettingsHandler(this);
        _installEventHandler = new InstallEventHandler(this, PickFolderAsync);
        AuxInstallService.EnsureInisDir();       // create inis folder on first run
        AuxInstallService.EnsureReShadeStaging(); // create staging dir (DLLs downloaded by ReShadeUpdateService)
        Title = "RHI";
        // Fire-and-forget: check/download Lilium HDR shaders in the background
        var shaderTask = ViewModel.ShaderPackServiceInstance.EnsureLatestAsync();
        shaderTask.SafeFireAndForget("MainWindow.ShaderPack");
        ViewModel.SetShaderPackReadyTask(shaderTask);
        // Fire-and-forget: fetch addon list and check for updates in the background
        Task.Run(async () =>
        {
            try
            {
                await ViewModel.AddonPackServiceInstance.EnsureLatestAsync();
                await ViewModel.AddonPackServiceInstance.CheckAndUpdateAllAsync();
            }
            catch (Exception ex) { crashReporter.Log($"[MainWindow] Addon pack init failed — {ex.Message}"); }
        }).SafeFireAndForget("MainWindow.AddonPack");
        _crashReporter.Log("[MainWindow.MainWindow] InitializeComponent complete");
        // Set a sensible default size immediately so the window isn't huge on first launch.
        // TryRestoreWindowBounds (called on Activated) will then override this with the
        // saved size+position from the previous session, if one exists.
        if (ViewModel.CurrentViewLayout != ViewLayout.Compact)
            AppWindow.Resize(new Windows.Graphics.SizeInt32(DefaultWidth, DefaultHeight));
        // For compact mode, sizing is handled entirely by ApplyCompactSize in the
        // Activated handler using SetWindowPos, which avoids the size mismatch between
        // AppWindow.Resize (client area) and SetWindowPos (full window frame).

        // Enforce minimum window size and enable Win32 drag-and-drop via WindowStateManager
        var hwnd = WindowNative.GetWindowHandle(this);
        NativeInterop.EnableDarkTitleBar(hwnd);
        _dragDropHandler = new DragDropHandler(this, _crashReporter);
        _windowStateManager = new WindowStateManager(this, hwnd, _dragDropHandler, _crashReporter);
        _windowStateManager.InstallWndProcSubclass();
        _windowStateManager.EnableDragAccept();

        // Apply compact size and lock immediately in the constructor.
        // There may be a tiny WinUI layout adjustment on first render, but the lock
        // prevents the user from resizing the window freely.
        if (ViewModel.CurrentViewLayout == ViewLayout.Compact)
        {
            _windowStateManager.TryRestoreWindowBounds(positionOnly: true);
            _windowStateManager.ApplyCompactSize();
            _windowStateManager.SetSizeLocked(true);
        }

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
        ViewModel.ShowShaderSelectionPicker = async (current) =>
            await ShaderPopupHelper.ShowAsync(Content.XamlRoot, ViewModel.ShaderPackServiceInstance, current, ShaderPopupHelper.PopupContext.Global);
        ViewModel.ShowPerGameShaderSelectionPicker = async (gameName, current) =>
            await ShaderPopupHelper.ShowAsync(Content.XamlRoot, ViewModel.ShaderPackServiceInstance, current, ShaderPopupHelper.PopupContext.PerGame);
        ViewModel.PropertyChanged += OnViewModelChanged;
        GameList.ItemsSource = ViewModel.DisplayedGames;
        // Apply initial visibility
        UpdatePageVisibility();
        // Show version in status bar
        StatusBarVersionText.Text = $"v{Services.CrashReporter.AppVersion}";
        // Always show the ✕ clear button on search box
        SearchBox.Loaded += (_, _) => VisualStateManager.GoToState(SearchBox, "ButtonVisible", false);
        ViewModel.InitializeAsync().SafeFireAndForget("MainWindow.Init");
        // Rebuild custom filter chips when the collection changes
        ViewModel.Filter.CustomFilters.CollectionChanged += (_, _) =>
            DispatcherQueue.TryEnqueue(RebuildCustomFilterChips);
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
        _addonFileWatcher.ArchiveFileDetected += path =>
            DispatcherQueue.TryEnqueue(() => HandleArchiveFile(path));
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

            if (ViewModel.CurrentViewLayout == ViewLayout.Compact)
            {
                // Compact mode: restore position only, then apply the fixed compact size and lock.
                _windowStateManager.TryRestoreWindowBounds(positionOnly: true);
                _windowStateManager.ApplyCompactSize();
                _windowStateManager.SetSizeLocked(true);
            }
            else
            {
                _windowStateManager.TryRestoreWindowBounds();
            }
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
        _windowStateManager.CleanupOleDragDrop();
        SingleInstanceService.Stop();
        ViewModel.SaveSettingsPublic(); // persist GridLayout and other settings
        ViewModel.SaveLibraryPublic();  // persist LastSelectedGame and other library state
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

    internal async void HandleArchiveFile(string filePath)
    {
        try
        {
            _crashReporter.Log($"[MainWindow.HandleArchiveFile] Processing '{Path.GetFileName(filePath)}'");

            while (ViewModel.IsLoading)
                await Task.Delay(200);

            NativeInterop.SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));

            await _dragDropHandler.ProcessDroppedArchive(filePath);
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainWindow.HandleArchiveFile] Failed — {ex.Message}");
        }
    }

    // ── Game list selection ──────────────────────────────────────────────────────

    private void GameList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GameList.SelectedItem is GameCardViewModel card)
        {
            ViewModel.SelectedGame = card;

            switch (ViewModel.CurrentViewLayout)
            {
                case ViewLayout.Grid:
                    // In grid mode, scroll to and highlight the selected card
                    ScrollToCard(card);
                    break;
                case ViewLayout.Detail:
                    // In detail mode, populate the detail panel as before
                    PopulateDetailPanel(card);
                    DetailPanel.Visibility = Visibility.Visible;
                    BuildOverridesPanel(card);
                    OverridesContainer.Visibility = Visibility.Visible;
                    ManagementContainer.Visibility = Visibility.Visible;
                    break;
                case ViewLayout.Compact:
                    // Rebuild current compact page for newly selected game, retaining page index
                    _compactViewBuilder?.RebuildCurrentPage(
                        card, ViewModel.CompactPageIndex);
                    break;
            }
        }
        else
        {
            ViewModel.SelectedGame = null;

            switch (ViewModel.CurrentViewLayout)
            {
                case ViewLayout.Grid:
                    // Clear highlight from all cards
                    foreach (var child in CardGridPanel.Children)
                    {
                        if (child is Border b && b.Tag is GameCardViewModel c)
                            c.CardHighlighted = false;
                    }
                    break;
                case ViewLayout.Detail:
                    DetailPanel.Visibility = Visibility.Collapsed;
                    OverridesPanel.Children.Clear();
                    OverridesContainer.Visibility = Visibility.Collapsed;
                    ManagementPanel.Children.Clear();
                    ManagementContainer.Visibility = Visibility.Collapsed;
                    break;
                case ViewLayout.Compact:
                    // Hide detail panel content when no game is selected
                    DetailPanel.Visibility = Visibility.Collapsed;
                    OverridesPanel.Children.Clear();
                    OverridesContainer.Visibility = Visibility.Collapsed;
                    ManagementPanel.Children.Clear();
                    ManagementContainer.Visibility = Visibility.Collapsed;
                    break;
            }
        }
    }
}
