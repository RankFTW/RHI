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

    /// <summary>Exposes the detail panel builder for extracted handler classes.</summary>
    internal DetailPanelBuilder DetailPanelBuilderInstance => _detailPanelBuilder;

    private string? _pendingReselect;

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
        Title = "RHI";
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
        ViewModel.ShowShaderSelectionPicker = async (current) =>
            await ShaderPopupHelper.ShowAsync(Content.XamlRoot, ViewModel.ShaderPackServiceInstance, current, ShaderPopupHelper.PopupContext.Global);
        ViewModel.ShowPerGameShaderSelectionPicker = async (gameName, current) =>
            await ShaderPopupHelper.ShowAsync(Content.XamlRoot, ViewModel.ShaderPackServiceInstance, current, ShaderPopupHelper.PopupContext.PerGame);
        ViewModel.PropertyChanged += OnViewModelChanged;
        GameList.ItemsSource = ViewModel.DisplayedGames;
        // Apply initial visibility
        UpdatePageVisibility();
        // Always show the ✕ clear button on search box
        SearchBox.Loaded += (_, _) => VisualStateManager.GoToState(SearchBox, "ButtonVisible", false);
        ViewModel.InitializeAsync().SafeFireAndForget("MainWindow.Init");
        // Silent update check — runs in background, shows dialog only if update found
        CheckForAppUpdateAsync().SafeFireAndForget("MainWindow.UpdateCheck");
        // Show patch notes on first launch after update
        ShowPatchNotesIfNewVersionAsync().SafeFireAndForget("MainWindow.PatchNotes");
        // One-time DC removal warning (independent of patch notes)
        ShowDcRemovalWarningAsync().SafeFireAndForget("MainWindow.DcRemovalWarning");
        // One-time legacy Program Files cleanup
        ShowLegacyProgramFilesCleanupAsync().SafeFireAndForget("MainWindow.LegacyCleanup");
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
        _windowStateManager.CleanupOleDragDrop();
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

    // ── Game list selection ──────────────────────────────────────────────────────

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
}
