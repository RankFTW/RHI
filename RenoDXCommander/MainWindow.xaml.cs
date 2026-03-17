using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using RenoDXCommander.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;
using Windows.Storage.Pickers;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using WinRT.Interop;

namespace RenoDXCommander;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    // Sensible default — used on first launch before any saved size exists
    private const int DefaultWidth  = 1280;
    private const int DefaultHeight = 1000;

    private readonly CardBuilder _cardBuilder;
    private readonly DetailPanelBuilder _detailPanelBuilder;

    public MainWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        _cardBuilder = new CardBuilder(this);
        _detailPanelBuilder = new DetailPanelBuilder(this);
        AuxInstallService.EnsureInisDir();       // create inis folder on first run
        AuxInstallService.EnsureReShadeStaging(); // create staging dir (DLLs downloaded by ReShadeUpdateService)
        Title = "RDXC - RenoDXCommander";
        // Fire-and-forget: check/download Lilium HDR shaders in the background
        ViewModel.ShaderPackServiceInstance.EnsureLatestAsync().SafeFireAndForget("MainWindow.ShaderPack");
        CrashReporter.Log("[MainWindow.MainWindow] InitializeComponent complete");
        // Set a sensible default size immediately so the window isn't huge on first launch.
        // TryRestoreWindowBounds (called on Activated) will then override this with the
        // saved size+position from the previous session, if one exists.
        AppWindow.Resize(new Windows.Graphics.SizeInt32(DefaultWidth, DefaultHeight));

        // Enforce minimum window size via Win32 subclass
        _hwnd = WindowNative.GetWindowHandle(this);
        _origWndProc = NativeInterop.GetWindowLongPtr(_hwnd, NativeInterop.GWLP_WNDPROC);
        _wndProcDelegate = new NativeInterop.WndProcDelegate(WndProc);
        NativeInterop.SetWindowLongPtr(_hwnd, NativeInterop.GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));

        // Enable Win32 drag-and-drop (WM_DROPFILES) — required for unpackaged WinUI 3 apps
        // where the OLE-based AllowDrop/DragOver/Drop events don't fire reliably.
        NativeInterop.DragAcceptFiles(_hwnd, true);
        // Allow drag messages through UIPI when running as admin
        NativeInterop.ChangeWindowMessageFilterEx(_hwnd, NativeInterop.WM_DROPFILES, NativeInterop.MSGFLT_ALLOW, IntPtr.Zero);
        NativeInterop.ChangeWindowMessageFilterEx(_hwnd, NativeInterop.WM_COPYGLOBALDATA, NativeInterop.MSGFLT_ALLOW, IntPtr.Zero);

        // Set the title bar icon (unpackaged apps need this explicitly)
        AppWindow.SetIcon("icon.ico");

        // Dark title bar — match our theme
        if (AppWindow.TitleBar is { } titleBar)
        {
            titleBar.BackgroundColor = Windows.UI.Color.FromArgb(0xFF, 0x0F, 0x13, 0x18);           // SurfaceHeader
            titleBar.ForegroundColor = Windows.UI.Color.FromArgb(0xFF, 0xE8, 0xEC, 0xF2);           // TextPrimary
            titleBar.InactiveBackgroundColor = Windows.UI.Color.FromArgb(0xFF, 0x0F, 0x13, 0x18);
            titleBar.InactiveForegroundColor = Windows.UI.Color.FromArgb(0xFF, 0x6B, 0x7A, 0x8E);   // TextTertiary
            titleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(0xFF, 0x0F, 0x13, 0x18);
            titleBar.ButtonForegroundColor = Windows.UI.Color.FromArgb(0xFF, 0xA0, 0xAA, 0xBB);     // TextSecondary
            titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(0xFF, 0x1E, 0x24, 0x2C); // SurfaceOverlay
            titleBar.ButtonHoverForegroundColor = Windows.UI.Color.FromArgb(0xFF, 0xE8, 0xEC, 0xF2);
            titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(0xFF, 0x28, 0x32, 0x40); // BorderDefault
            titleBar.ButtonPressedForegroundColor = Windows.UI.Color.FromArgb(0xFF, 0xE8, 0xEC, 0xF2);
            titleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(0xFF, 0x0F, 0x13, 0x18);
            titleBar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(0xFF, 0x40, 0x48, 0x58); // TextDisabled
        }
        // Restore window size & position after activation (ensure HWND is ready)
        this.Activated += MainWindow_Activated;
        ViewModel.SetDispatcher(DispatcherQueue);
        ViewModel.ConfirmForeignDxgiOverwrite = ShowForeignDxgiConfirmDialogAsync;
        ViewModel.ConfirmForeignWinmmOverwrite = ShowForeignWinmmConfirmDialogAsync;
        ViewModel.ShowShaderSelectionPicker = ShowShaderPickerDialogAsync;
        ViewModel.ShowPerGameShaderSelectionPicker = async (gameName, current) => await ShowShaderPickerDialogAsync(current);
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
        this.Closed += MainWindow_Closed;
    }


    private void MainWindow_Activated(object? sender, WindowActivatedEventArgs e)
    {
        try
        {
            // Only restore once
            this.Activated -= MainWindow_Activated;
            TryRestoreWindowBounds();
        }
        catch { }
    }

    private void MainWindow_Closed(object? sender, WindowEventArgs e)
    {
        // Unsubscribe from ViewModel property changes to avoid leaks (Requirement 8.5)
        ViewModel.PropertyChanged -= OnViewModelChanged;

        // Unsubscribe detail panel builder from current card's PropertyChanged
        if (_detailPanelBuilder.CurrentDetailCard != null)
            _detailPanelBuilder.CurrentDetailCard.PropertyChanged -= _detailPanelBuilder.DetailCard_PropertyChanged;

        ViewModel.SaveSettingsPublic(); // persist GridLayout and other settings
        SaveWindowBounds();
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
                    // Don't disturb the Settings panel if it's currently visible
                    if (ViewModel.CurrentPage != AppPage.Settings)
                    {
                        LoadingPanel.Visibility = loading ? Visibility.Visible  : Visibility.Collapsed;
                        GameViewPanel.Visibility = loading ? Visibility.Collapsed : Visibility.Visible;
                    }
                    LoadingRing.IsActive = loading;
                    RefreshBtn.IsEnabled = !loading;
                    StatusDot.Fill = new SolidColorBrush(loading
                        ? ((SolidColorBrush)Application.Current.Resources[ResourceKeys.AccentAmberBrush]).Color
                        : ((SolidColorBrush)Application.Current.Resources[ResourceKeys.AccentGreenBrush]).Color);
                    if (!loading) TryRestoreSelection();
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
            }
        });
    }

    // ── Header buttons ────────────────────────────────────────────────────────────

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        CrashReporter.Log("[MainWindow.RefreshButton_Click] User clicked Refresh");
        _ = RefreshWithScrollRestore();
    }

    private void FullRefreshButton_Click(object sender, RoutedEventArgs e)
    {
        CrashReporter.Log("[MainWindow.FullRefreshButton_Click] User clicked Full Refresh");
        _ = FullRefreshWithScrollRestore();
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

    private void SupportButton_Click(object sender, RoutedEventArgs e)
    {
        _ = Windows.System.Launcher.LaunchUriAsync(
            new Uri("https://discordapp.com/channels/1296187754979528747/1475173660686815374"));
    }

    private async Task<bool> ShowForeignDxgiConfirmDialogAsync(GameCardViewModel card, string dxgiPath)
    {
        var fileSize = new System.IO.FileInfo(dxgiPath).Length;
        var sizeKB   = fileSize / 1024.0;

        var dlg = new ContentDialog
        {
            Title               = "⚠ Unknown dxgi.dll Detected",
            Content             = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground   = Brush(ResourceKeys.AccentAmberBrush),
                FontSize     = 13,
                Text         = $"A dxgi.dll file was found in:\n{card.InstallPath}\n\n" +
                               $"File size: {sizeKB:N0} KB\n\n" +
                               "RDXC cannot identify this file as ReShade or Display Commander. " +
                               "It may belong to another mod (e.g. DXVK, Special K, ENB).\n\n" +
                               "Overwriting it may break the existing mod. Do you want to proceed?",
            },
            PrimaryButtonText   = "Overwrite",
            CloseButtonText     = "Cancel",
            XamlRoot            = Content.XamlRoot,
            Background          = Brush(ResourceKeys.SurfaceOverlayBrush),
        };

        var result = await dlg.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    private async Task<bool> ShowForeignWinmmConfirmDialogAsync(GameCardViewModel card, string winmmPath)
    {
        var fileSize = new System.IO.FileInfo(winmmPath).Length;
        var sizeKB   = fileSize / 1024.0;

        var dlg = new ContentDialog
        {
            Title               = "⚠ Unknown winmm.dll Detected",
            Content             = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground   = Brush(ResourceKeys.AccentAmberBrush),
                FontSize     = 13,
                Text         = $"A winmm.dll file was found in:\n{card.InstallPath}\n\n" +
                               $"File size: {sizeKB:N0} KB\n\n" +
                               "RDXC cannot identify this file as Display Commander. " +
                               "It may belong to another mod or DLL injector.\n\n" +
                               "Overwriting it may break the existing mod. Do you want to proceed?",
            },
            PrimaryButtonText   = "Overwrite",
            CloseButtonText     = "Cancel",
            XamlRoot            = Content.XamlRoot,
            Background          = Brush(ResourceKeys.SurfaceOverlayBrush),
        };

        var result = await dlg.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    // ── Auto-Update ────────────────────────────────────────────────────────────

    private async Task CheckForAppUpdateAsync()
    {
        try
        {
            if (ViewModel.SkipUpdateCheck)
            {
                CrashReporter.Log("[MainWindow.CheckForAppUpdateAsync] Update check skipped (disabled in settings)");
                return;
            }

            // Wait until the XamlRoot is available (window needs to be fully loaded for dialogs)
            while (Content.XamlRoot == null)
                await Task.Delay(200);

            var updateInfo = await ViewModel.UpdateServiceInstance.CheckForUpdateAsync(ViewModel.BetaOptIn);
            if (updateInfo == null) return; // up to date or check failed

            // Show update dialog on UI thread
            DispatcherQueue.TryEnqueue(async () =>
            {
                await ShowUpdateDialogAsync(updateInfo);
            });
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.CheckForAppUpdateAsync] Update check error — {ex.Message}");
        }
    }

    private async Task ShowUpdateDialogAsync(UpdateInfo updateInfo)
    {
        var dlg = new ContentDialog
        {
            Title   = "🔄 Update Available",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        Foreground   = Brush(ResourceKeys.TextSecondaryBrush),
                        FontSize     = 14,
                        Text         = $"A new version of RDXC is available!\n\n" +
                                       $"Installed:  v{updateInfo.CurrentVersion}\n" +
                                       $"Available:  v{updateInfo.DisplayVersion ?? updateInfo.RemoteVersion.ToString()}\n\n" +
                                       "Would you like to update now?",
                    },
                },
            },
            PrimaryButtonText   = "Update Now",
            CloseButtonText     = "Later",
            XamlRoot            = Content.XamlRoot,
            Background          = Brush(ResourceKeys.SurfaceRaisedBrush),
        };

        var result = await dlg.ShowAsync();
        if (result != ContentDialogResult.Primary) return; // user chose "Later"

        // User chose "Update Now" — show downloading dialog
        await DownloadAndInstallUpdateAsync(updateInfo);
    }

    private async Task DownloadAndInstallUpdateAsync(UpdateInfo updateInfo)
    {
        // Create a non-dismissable progress dialog
        var progressText = new TextBlock
        {
            Text         = "Starting download...",
            TextWrapping = TextWrapping.Wrap,
            Foreground   = Brush(ResourceKeys.TextSecondaryBrush),
            FontSize     = 13,
        };
        var progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value   = 0,
            Height  = 6,
            IsIndeterminate = false,
        };
        var downloadDlg = new ContentDialog
        {
            Title   = "⬇ Downloading Update",
            Content = new StackPanel
            {
                Spacing = 12,
                Children = { progressText, progressBar },
            },
            XamlRoot   = Content.XamlRoot,
            Background = Brush(ResourceKeys.SurfaceRaisedBrush),
            // No buttons — dialog will be closed programmatically when download completes
        };

        // Show dialog non-blocking
        var dialogTask = downloadDlg.ShowAsync();

        var progress = new Progress<(string msg, double pct)>(p =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                progressText.Text = p.msg;
                progressBar.Value = p.pct;
            });
        });

        var installerPath = await ViewModel.UpdateServiceInstance.DownloadInstallerAsync(
            updateInfo.DownloadUrl, progress);

        if (string.IsNullOrEmpty(installerPath))
        {
            // Download failed — update dialog to show error with a Close button
            DispatcherQueue.TryEnqueue(() =>
            {
                progressText.Text = "❌ Download failed. Please try again later or download manually from GitHub.";
                progressBar.Value = 0;
                downloadDlg.CloseButtonText = "Close";
            });
            return;
        }

        // Close the progress dialog
        downloadDlg.Hide();

        // Launch installer and close RDXC
        ViewModel.UpdateServiceInstance.LaunchInstallerAndExit(installerPath, () =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                this.Close();
            });
        });
    }

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

    private void RebuildCardGrid()
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
                CrashReporter.Log($"[MainWindow.RebuildCardGrid] Skipped card '{card.GameName}' — {ex.Message}");
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
    {
        var gameName = card.GameName;
        bool isLumaMode = ViewModel.IsLumaEnabled(gameName);

        var panel = new StackPanel { Spacing = 8, Width = 420 };

        // ── Title ──
        panel.Children.Add(new TextBlock
        {
            Text = "Overrides",
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = Brush(ResourceKeys.TextPrimaryBrush),
        });

        // ── Game name + Wiki name ──
        var gameNameBox = new TextBox
        {
            Header = "Game name (editable)",
            Text = gameName,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x1A, 0x20, 0x30)),
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xE2, 0xE8, 0xFF)),
            BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x28, 0x32, 0x40)),
            Padding = new Thickness(8, 4, 8, 4),
        };
        var wikiNameBox = new TextBox
        {
            Header = "Wiki mod name",
            PlaceholderText = "Exact wiki name",
            Text = ViewModel.GetNameMapping(gameName) ?? "",
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x1A, 0x20, 0x30)),
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xE2, 0xE8, 0xFF)),
            BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x28, 0x32, 0x40)),
            Padding = new Thickness(8, 4, 8, 4),
        };
        var originalStoreName = ViewModel.GetOriginalStoreName(gameName);
        var nameResetBtn = new Button
        {
            Content = "↩ Reset",
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Bottom,
            Padding = new Thickness(8, 4, 8, 4),
            Background = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x1A, 0x20, 0x30)),
            Foreground = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x6B, 0x7A, 0x8E)),
            BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x28, 0x32, 0x40)),
        };
        ToolTipService.SetToolTip(nameResetBtn,
            "Reset game name back to auto-detected and clear wiki name mapping.");
        nameResetBtn.Click += (s, ev) =>
        {
            gameNameBox.Text = originalStoreName ?? gameName;
            wikiNameBox.Text = "";
        };

        var nameGrid = new Grid { ColumnSpacing = 8 };
        nameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        nameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        nameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(gameNameBox, 0);
        Grid.SetColumn(wikiNameBox, 1);
        Grid.SetColumn(nameResetBtn, 2);
        nameGrid.Children.Add(gameNameBox);
        nameGrid.Children.Add(wikiNameBox);
        nameGrid.Children.Add(nameResetBtn);
        panel.Children.Add(nameGrid);
        panel.Children.Add(MakeSeparator());

        // ── DC Mode + Shader Mode (side by side) ──
        int? currentDcMode = ViewModel.GetPerGameDcModeOverride(gameName);
        var globalDcLabel = ViewModel.DcModeLevel switch { 1 => "DC Mode 1", 2 => "DC Mode 2", _ => "Off" };
        var dcModeOptions = new[] { $"Global ({globalDcLabel})", "Exclude (Off)", "DC Mode 1", "DC Mode 2" };
        var dcModeCombo = new ComboBox
        {
            ItemsSource = dcModeOptions,
            SelectedIndex = currentDcMode switch { null => 0, 0 => 1, 1 => 2, 2 => 3, _ => 0 },
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Header = "DC Mode",
        };
        ToolTipService.SetToolTip(dcModeCombo,
            "Global = use the Settings DC Mode. Exclude (Off) = always use normal naming. " +
            "DC Mode 1 = force dxgi.dll proxy. DC Mode 2 = force winmm.dll proxy.");

        string currentShaderMode = ViewModel.GetPerGameShaderMode(gameName);
        var globalShaderLabel = ViewModel.ShaderDeployMode.ToString();
        var shaderModeValues = new[] { "Global", "Off", "Minimum", "All", "User", "Select" };
        var shaderModeDisplay = new[] { $"Global ({globalShaderLabel})", "Off", "Minimum", "All", "User", "Select" };
        int shaderSelectedIdx = Array.IndexOf(shaderModeValues, currentShaderMode);
        if (shaderSelectedIdx < 0) shaderSelectedIdx = 0;
        var shaderModeCombo = new ComboBox
        {
            ItemsSource = shaderModeDisplay,
            SelectedIndex = shaderSelectedIdx,
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Header = "Shader Mode",
        };
        ToolTipService.SetToolTip(shaderModeCombo,
            "Global = follow the Settings toggle. Off = no shaders. Minimum = Lilium only. All = all packs. User = custom folder only. Select = pick specific packs.\n" +
            "Note: Per-game shader mode only applies when ReShade is used standalone (DC Mode OFF). " +
            "When DC Mode is ON, all DC-mode games share the DC global shader folder.");

        // Track previous shader mode index so we can revert on picker cancel
        int previousShaderIdx = shaderSelectedIdx;
        shaderModeCombo.SelectionChanged += async (s, ev) =>
        {
            var idx = shaderModeCombo.SelectedIndex;
            if (idx >= 0 && idx < shaderModeValues.Length && shaderModeValues[idx] == "Select")
            {
                // Trigger the per-game shader selection picker
                if (ViewModel.ShowPerGameShaderSelectionPicker != null)
                {
                    // Pre-populate with existing per-game selection, or fall back to global
                    List<string>? current = ViewModel.GameNameServiceInstance.PerGameShaderSelection.TryGetValue(gameName, out var existing)
                        ? existing
                        : ViewModel.Settings.SelectedShaderPacks;
                    var result = await ViewModel.ShowPerGameShaderSelectionPicker(gameName, current);
                    if (result != null)
                    {
                        // Store the per-game selection
                        ViewModel.GameNameServiceInstance.PerGameShaderSelection[gameName] = result;
                        previousShaderIdx = idx;
                    }
                    else
                    {
                        // User cancelled — revert ComboBox to previous value
                        shaderModeCombo.SelectedIndex = previousShaderIdx;
                    }
                }
            }
            else
            {
                previousShaderIdx = idx;
            }
        };

        var modeGrid = new Grid { ColumnSpacing = 8 };
        modeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        modeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(dcModeCombo, 0);
        Grid.SetColumn(shaderModeCombo, 1);
        modeGrid.Children.Add(dcModeCombo);
        modeGrid.Children.Add(shaderModeCombo);
        panel.Children.Add(modeGrid);
        panel.Children.Add(MakeSeparator());

        // ── DLL naming override ──
        bool isDllOverride = ViewModel.HasDllOverride(gameName);
        var existingCfg = ViewModel.GetDllOverride(gameName);
        bool is32Bit = card.Is32Bit;
        var defaultRsName = is32Bit ? "ReShade32.dll" : "ReShade64.dll";
        var defaultDcName = is32Bit ? "zzz_display_commander.addon32" : "zzz_display_commander.addon64";

        var dllOverrideToggle = new ToggleSwitch
        {
            Header = "DLL naming override",
            IsOn = isDllOverride,
            IsEnabled = !isLumaMode,
            OnContent = "Custom filenames enabled",
            OffContent = "Using default filenames",
            Foreground = Brush(ResourceKeys.TextSecondaryBrush),
            FontSize = 12,
        };
        ToolTipService.SetToolTip(dllOverrideToggle,
            "Override the filenames ReShade and Display Commander are installed as. " +
            "When enabled, existing RS/DC files are renamed to the custom filenames. " +
            "The game is automatically excluded from DC Mode, Update All, and global shaders.");
        var existingRsName = existingCfg?.ReShadeFileName ?? "";
        var existingDcName = existingCfg?.DcFileName ?? "";
        var rsNameBox = new ComboBox
        {
            IsEditable = true,
            PlaceholderText = defaultRsName,
            Header = "ReShade filename",
            FontSize = 12,
            IsEnabled = isDllOverride,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = DllOverrideConstants.CommonDllNames,
        };
        if (!string.IsNullOrEmpty(existingRsName))
        {
            if (DllOverrideConstants.CommonDllNames.Contains(existingRsName, StringComparer.OrdinalIgnoreCase))
                rsNameBox.SelectedItem = DllOverrideConstants.CommonDllNames.First(n => n.Equals(existingRsName, StringComparison.OrdinalIgnoreCase));
            else
            {
                var capturedRs = existingRsName;
                rsNameBox.Loaded += (s, e) => rsNameBox.Text = capturedRs;
            }
        }
        var dcNameBox = new ComboBox
        {
            IsEditable = true,
            PlaceholderText = defaultDcName,
            Header = "DC filename",
            FontSize = 12,
            IsEnabled = isDllOverride,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = DllOverrideConstants.CommonDllNames,
        };
        if (!string.IsNullOrEmpty(existingDcName))
        {
            if (DllOverrideConstants.CommonDllNames.Contains(existingDcName, StringComparer.OrdinalIgnoreCase))
                dcNameBox.SelectedItem = DllOverrideConstants.CommonDllNames.First(n => n.Equals(existingDcName, StringComparison.OrdinalIgnoreCase));
            else
            {
                var capturedDc = existingDcName;
                dcNameBox.Loaded += (s, e) => dcNameBox.Text = capturedDc;
            }
        }
        dllOverrideToggle.Toggled += (s, ev) =>
        {
            rsNameBox.IsEnabled = dllOverrideToggle.IsOn;
            dcNameBox.IsEnabled = dllOverrideToggle.IsOn;
        };

        var dllNameGrid = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        dllNameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        dllNameGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(rsNameBox, 0);
        Grid.SetColumn(dcNameBox, 1);
        dllNameGrid.Children.Add(rsNameBox);
        dllNameGrid.Children.Add(dcNameBox);

        var dllGroupPanel = new StackPanel { Spacing = 4 };
        dllGroupPanel.Children.Add(dllOverrideToggle);
        dllGroupPanel.Children.Add(dllNameGrid);
        var dllGroupBorder = new Border
        {
            Child = dllGroupPanel,
            Background = Brush(ResourceKeys.SurfaceOverlayBrush),
            BorderBrush = Brush(ResourceKeys.BorderSubtleBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 12),
        };
        panel.Children.Add(dllGroupBorder);
        panel.Children.Add(MakeSeparator());

        // ── Global update inclusion (3 ToggleSwitches) ──
        panel.Children.Add(new TextBlock
        {
            Text = "Global update inclusion",
            FontSize = 12,
            Foreground = Brush(ResourceKeys.TextSecondaryBrush),
            Margin = new Thickness(0, 0, 0, 8),
        });

        var rsToggle = new ToggleSwitch
        {
            Header = "ReShade",
            IsOn = !ViewModel.IsUpdateAllExcludedReShade(gameName),
            OnContent = "Yes",
            OffContent = "No",
            Foreground = Brush(ResourceKeys.TextSecondaryBrush),
            FontSize = 11,
            MinWidth = 0,
        };
        var dcToggle = new ToggleSwitch
        {
            Header = "DC",
            IsOn = !ViewModel.IsUpdateAllExcludedDc(gameName),
            OnContent = "Yes",
            OffContent = "No",
            Foreground = Brush(ResourceKeys.TextSecondaryBrush),
            FontSize = 11,
            MinWidth = 0,
        };
        var rdxToggle = new ToggleSwitch
        {
            Header = "RenoDX",
            IsOn = !ViewModel.IsUpdateAllExcludedRenoDx(gameName),
            OnContent = "Yes",
            OffContent = "No",
            Foreground = Brush(ResourceKeys.TextSecondaryBrush),
            FontSize = 11,
            MinWidth = 0,
        };

        var rsBorder = new Border
        {
            Child = rsToggle,
            BorderBrush = Brush(ResourceKeys.BorderDefaultBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 6, 8, 6),
        };
        var dcBorder = new Border
        {
            Child = dcToggle,
            BorderBrush = Brush(ResourceKeys.BorderDefaultBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 6, 8, 6),
        };
        var rdxBorder = new Border
        {
            Child = rdxToggle,
            BorderBrush = Brush(ResourceKeys.BorderDefaultBrush),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 6, 8, 6),
        };

        var toggleRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
        };
        toggleRow.Children.Add(rsBorder);
        toggleRow.Children.Add(dcBorder);
        toggleRow.Children.Add(rdxBorder);
        panel.Children.Add(toggleRow);
        panel.Children.Add(MakeSeparator());

        // ── Wiki exclusion ──
        var wikiExcludeToggle = new ToggleSwitch
        {
            Header = "Wiki exclusion",
            IsOn = ViewModel.IsWikiExcluded(gameName),
            OnContent = "Excluded from wiki lookups",
            OffContent = "Included in wiki lookups",
            Foreground = Brush(ResourceKeys.TextSecondaryBrush),
            FontSize = 12,
        };
        ToolTipService.SetToolTip(wikiExcludeToggle,
            "When enabled, this game will not be looked up on the RenoDX wiki. " +
            "Useful for games that share a name with an unrelated wiki entry.");
        panel.Children.Add(wikiExcludeToggle);

        // ── Reset Overrides button ──
        var resetOverridesBtn = new Button
        {
            Content = "Reset Overrides",
            FontSize = 12,
            Padding = new Thickness(16, 8, 16, 8),
            Background = Brush(ResourceKeys.SurfaceOverlayBrush),
            Foreground = Brush(ResourceKeys.TextSecondaryBrush),
            BorderBrush = Brush(ResourceKeys.BorderDefaultBrush),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 8, 0, 0),
        };
        resetOverridesBtn.Click += (s, ev) =>
        {
            gameNameBox.Text = originalStoreName ?? gameName;
            wikiNameBox.Text = "";
            dcModeCombo.SelectedIndex = 0;
            shaderModeCombo.SelectedIndex = 0;
            dllOverrideToggle.IsOn = false;
            rsToggle.IsOn = true;
            dcToggle.IsOn = true;
            rdxToggle.IsOn = true;
            wikiExcludeToggle.IsOn = false;
        };
        panel.Children.Add(resetOverridesBtn);

        // Wrap in a ScrollViewer for long content
        var scrollViewer = new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 500,
        };

        var flyout = new Flyout
        {
            Content = scrollViewer,
            Placement = FlyoutPlacementMode.BottomEdgeAlignedRight,
        };

        // On flyout closed, save all overrides and refresh the card
        var capturedName = gameName;
        flyout.Closed += (s, ev) =>
        {
            bool anyChanged = false;

            // ── Handle game rename ──
            var det = gameNameBox.Text?.Trim();
            var effectiveName = capturedName;

            if (!string.IsNullOrEmpty(capturedName) && !string.IsNullOrEmpty(det)
                && !det.Equals(capturedName, StringComparison.OrdinalIgnoreCase))
            {
                ViewModel.RenameGame(capturedName, det);
                effectiveName = det;
                anyChanged = true;
            }

            // ── Name mapping ──
            var wikiKey = wikiNameBox.Text?.Trim();
            var existingMapping = ViewModel.GetNameMapping(effectiveName);
            if (!string.IsNullOrEmpty(effectiveName) && !string.IsNullOrEmpty(wikiKey))
            {
                if (!string.Equals(wikiKey, existingMapping, StringComparison.OrdinalIgnoreCase))
                {
                    ViewModel.AddNameMapping(effectiveName, wikiKey);
                    anyChanged = true;
                }
            }
            else if (!string.IsNullOrEmpty(effectiveName) && string.IsNullOrEmpty(wikiKey) && !string.IsNullOrEmpty(existingMapping))
            {
                ViewModel.RemoveNameMapping(effectiveName);
                anyChanged = true;
            }

            // DC Mode override
            int? newDcMode = dcModeCombo.SelectedIndex switch { 1 => 0, 2 => 1, 3 => 2, _ => null };
            if (newDcMode != ViewModel.GetPerGameDcModeOverride(effectiveName))
            {
                ViewModel.SetPerGameDcModeOverride(effectiveName, newDcMode);
                ViewModel.ApplyDcModeSwitchForCard(effectiveName);
                anyChanged = true;
            }

            // Shader mode
            var shaderModeIdx = shaderModeCombo.SelectedIndex;
            var newShaderMode = shaderModeIdx >= 0 && shaderModeIdx < shaderModeValues.Length
                ? shaderModeValues[shaderModeIdx] : "Global";
            if (newShaderMode != ViewModel.GetPerGameShaderMode(effectiveName))
            {
                ViewModel.SetPerGameShaderMode(effectiveName, newShaderMode);
                ViewModel.DeployShadersForCard(effectiveName);
                anyChanged = true;
            }

            // DLL naming override
            bool nowDllOverride = dllOverrideToggle.IsOn;
            bool wasDllOverride = ViewModel.HasDllOverride(effectiveName);
            var targetCard = ViewModel.AllCards.FirstOrDefault(c =>
                c.GameName.Equals(effectiveName, StringComparison.OrdinalIgnoreCase));

            if (nowDllOverride && !wasDllOverride && targetCard != null)
            {
                var rsText = rsNameBox.SelectedItem as string ?? rsNameBox.Text;
                var rsName = !string.IsNullOrWhiteSpace(rsText) ? rsText.Trim() : rsNameBox.PlaceholderText;
                var dcText = dcNameBox.SelectedItem as string ?? dcNameBox.Text;
                var dcName = !string.IsNullOrWhiteSpace(dcText) ? dcText.Trim() : dcNameBox.PlaceholderText;
                ViewModel.EnableDllOverride(targetCard, rsName, dcName);
                anyChanged = true;
            }
            else if (nowDllOverride && wasDllOverride && targetCard != null)
            {
                var rsText = rsNameBox.SelectedItem as string ?? rsNameBox.Text;
                var rsName = !string.IsNullOrWhiteSpace(rsText) ? rsText.Trim() : rsNameBox.PlaceholderText;
                var dcText = dcNameBox.SelectedItem as string ?? dcNameBox.Text;
                var dcName = !string.IsNullOrWhiteSpace(dcText) ? dcText.Trim() : dcNameBox.PlaceholderText;
                ViewModel.UpdateDllOverrideNames(targetCard, rsName, dcName);
                anyChanged = true;
            }
            else if (!nowDllOverride && wasDllOverride && targetCard != null)
            {
                ViewModel.DisableDllOverride(targetCard);
                anyChanged = true;
            }

            // Per-component Update All
            bool nowRsExcluded = !rsToggle.IsOn;
            if (nowRsExcluded != ViewModel.IsUpdateAllExcludedReShade(effectiveName))
            {
                ViewModel.ToggleUpdateAllExclusionReShade(effectiveName);
                anyChanged = true;
            }

            bool nowDcExcluded = !dcToggle.IsOn;
            if (nowDcExcluded != ViewModel.IsUpdateAllExcludedDc(effectiveName))
            {
                ViewModel.ToggleUpdateAllExclusionDc(effectiveName);
                anyChanged = true;
            }

            bool nowRdxExcluded = !rdxToggle.IsOn;
            if (nowRdxExcluded != ViewModel.IsUpdateAllExcludedRenoDx(effectiveName))
            {
                ViewModel.ToggleUpdateAllExclusionRenoDx(effectiveName);
                anyChanged = true;
            }

            // Wiki exclusion
            if (wikiExcludeToggle.IsOn != ViewModel.IsWikiExcluded(effectiveName))
            {
                ViewModel.ToggleWikiExclusion(effectiveName);
                anyChanged = true;
            }

            if (!anyChanged) return;

            CrashReporter.Log($"[MainWindow.OpenOverridesFlyout] Flyout overrides saved for: {effectiveName}");

            // Trigger pending reselect and restore selection (mirrors BuildOverridesPanel save logic)
            _pendingReselect = effectiveName;
            DispatcherQueue.TryEnqueue(TryRestoreSelection);

            // Refresh the card's status indicators
            card.NotifyAll();

            // Also rebuild the card in the grid if we're in grid mode
            if (ViewModel.IsGridLayout)
                RebuildCardGrid();
        };

        flyout.ShowAt(anchor);
    }

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
    {
        ViewModel.NavigateToSettingsCommand.Execute(null);
        GameViewPanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Visible;
        LoadingPanel.Visibility = Visibility.Collapsed;
        // Sync toggle state with ViewModel
        SkipUpdateToggle.IsOn = ViewModel.SkipUpdateCheck;
        BetaOptInToggle.IsOn = ViewModel.BetaOptIn;
        VerboseLoggingToggle.IsOn = ViewModel.VerboseLogging;
    }

    private void SkipUpdateToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle)
        {
            ViewModel.SkipUpdateCheck = toggle.IsOn;
            ViewModel.SaveSettingsPublic();
        }
    }

    private void BetaOptInToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle)
        {
            ViewModel.BetaOptIn = toggle.IsOn;
            ViewModel.SaveSettingsPublic();
        }
    }

    private void VerboseLoggingToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle)
        {
            ViewModel.VerboseLogging = toggle.IsOn;
            ViewModel.SaveSettingsPublic();
        }
    }

    private static readonly string PatchNotesDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RenoDXCommander");

    private async Task ShowPatchNotesIfNewVersionAsync()
    {
        try
        {
            // Wait until XamlRoot is ready
            while (Content.XamlRoot == null)
                await Task.Delay(200);

            // Wait for UI to settle and any update dialog to finish
            await Task.Delay(1500);

            var current = ViewModel.UpdateServiceInstance.CurrentVersion;
            var versionStr = $"{current.Major}.{current.Minor}.{current.Build}";
            var markerFile = Path.Combine(PatchNotesDir, $"PatchNotes-{versionStr}.txt");

            // Clean up markers from older versions
            try
            {
                Directory.CreateDirectory(PatchNotesDir);
                foreach (var old in Directory.EnumerateFiles(PatchNotesDir, "PatchNotes-*.txt"))
                {
                    if (!old.Equals(markerFile, StringComparison.OrdinalIgnoreCase))
                    {
                        try { File.Delete(old); } catch { }
                    }
                }
            }
            catch { }

            // If marker exists, this version's notes have already been shown
            if (File.Exists(markerFile)) return;

            // Write the marker file FIRST — ensures we never show again
            try
            {
                Directory.CreateDirectory(PatchNotesDir);
                File.WriteAllText(markerFile, $"Patch notes shown for v{versionStr}");
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[MainWindow.ShowPatchNotesIfNewVersionAsync] Failed to write patch notes marker — {ex.Message}");
            }

            DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    await ShowPatchNotesDialogAsync();
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[MainWindow.ShowPatchNotesIfNewVersionAsync] Patch notes dialog failed — {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.ShowPatchNotesIfNewVersionAsync] Patch notes check error — {ex.Message}");
        }
    }

    private async Task ShowPatchNotesDialogAsync()
    {
        var notes = ViewModels.MainViewModel.GetRecentPatchNotes(3);

        var markdown = new CommunityToolkit.WinUI.Controls.MarkdownTextBlock
        {
            Text = notes,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Foreground = Brush(ResourceKeys.TextSecondaryBrush),
            FontSize = 12,
            UseEmphasisExtras = true,
            UseListExtras = true,
            UseTaskLists = true,
        };

        var scrollViewer = new ScrollViewer
        {
            MaxHeight = 500,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = markdown,
        };

        var dlg = new ContentDialog
        {
            Title              = "📋 Patch Notes — What's New",
            Content            = scrollViewer,
            CloseButtonText    = "Close",
            XamlRoot           = Content.XamlRoot,
            Background         = Brush(ResourceKeys.SurfaceToolbarBrush),
        };

        await dlg.ShowAsync();
    }

    private async void PatchNotesLink_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await ShowPatchNotesDialogAsync();
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.PatchNotesLink_Click] Patch notes dialog error — {ex.Message}");
        }
    }

    private void OpenLogsFolder_Click(object sender, RoutedEventArgs e)
    {
        var logsDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RenoDXCommander", "logs");
        System.IO.Directory.CreateDirectory(logsDir);
        CrashReporter.Log("[MainWindow.OpenLogsFolder_Click] User opened logs folder from About panel");
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(logsDir) { UseShellExecute = true });
    }

    private void OpenDownloadsFolder_Click(object sender, RoutedEventArgs e)
    {
        System.IO.Directory.CreateDirectory(ModInstallService.DownloadCacheDir);
        CrashReporter.Log("[MainWindow.OpenDownloadsFolder_Click] User opened downloads cache folder from About panel");
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ModInstallService.DownloadCacheDir) { UseShellExecute = true });
    }

    private void SettingsBack_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.NavigateToGameViewCommand.Execute(null);
        SettingsPanel.Visibility = Visibility.Collapsed;
        // Restore whichever panel was showing before Settings was opened
        if (ViewModel.IsLoading)
            LoadingPanel.Visibility = Visibility.Visible;
        else
            GameViewPanel.Visibility = Visibility.Visible;
    }

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
        CrashReporter.Log($"[MainWindow.AddGameButton_Click] Adding game: {gameName}");

        // Pick the game folder
        var folder = await PickFolderAsync();
        if (folder == null) return;

        var game = new DetectedGame
        {
            Name = gameName, InstallPath = folder, Source = "Manual", IsManuallyAdded = true
        };
        ViewModel.AddManualGameCommand.Execute(game);
    }

    // ── Drag-and-drop game add ────────────────────────────────────────────────

    private void Grid_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Drop to add game, install addon, or extract archive";
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.IsGlyphVisible = true;
        }
    }

    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".7z", ".rar", ".tar", ".gz", ".bz2", ".xz", ".tar.gz", ".tgz", ".tar.bz2", ".tar.xz",
    };

    private async void Grid_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            return;

        var items = await e.DataView.GetStorageItemsAsync();
        foreach (var item in items)
        {
            if (item is not Windows.Storage.StorageFile file) continue;

            var ext = file.FileType?.ToLowerInvariant() ?? "";

            // Handle .addon64 / .addon32 files — install RenoDX addon to a game
            if (ext is ".addon64" or ".addon32")
            {
                try
                {
                    await ProcessDroppedAddon(file.Path);
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[MainWindow.Grid_Drop] DragDrop addon error processing '{file.Path}' — {ex.Message}");
                }
                continue;
            }

            // Handle archive files — extract and look for .addon64/.addon32 inside
            if (ArchiveExtensions.Contains(ext))
            {
                try
                {
                    await ProcessDroppedArchive(file.Path);
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[MainWindow.Grid_Drop] DragDrop archive error processing '{file.Path}' — {ex.Message}");
                }
                continue;
            }

            // Handle .exe files — add game
            if (!ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)) continue;

            var exePath = file.Path;
            CrashReporter.Log($"[MainWindow.Grid_Drop] Received exe '{exePath}'");

            try
            {
                await ProcessDroppedExe(exePath);
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[MainWindow.Grid_Drop] Error processing '{exePath}' — {ex.Message}");
            }
        }
    }

    private async Task ProcessDroppedExe(string exePath)
    {
        var exeDir  = Path.GetDirectoryName(exePath)!;
        var exeName = Path.GetFileNameWithoutExtension(exePath);

        // ── Determine the game root folder ────────────────────────────────────
        // Walk up from the exe to find the likely game root.
        // For Unreal: the exe is usually in GameRoot\Binaries\Win64 or \WinGDK
        // For Unity: the exe is usually in the game root next to UnityPlayer.dll
        // For others: the exe folder or its parent is the game root
        var gameRoot = InferGameRoot(exeDir);
        CrashReporter.Log($"[MainWindow.ProcessDroppedExe] Inferred game root '{gameRoot}' from exe dir '{exeDir}'");

        // ── Detect engine and correct install path ────────────────────────────
        var (installPath, engine) = ViewModel.GameDetectionServiceInstance.DetectEngineAndPath(gameRoot);

        // ── Infer game name ───────────────────────────────────────────────────
        var gameName = InferGameName(exePath, gameRoot, engine);
        CrashReporter.Log($"[MainWindow.ProcessDroppedExe] Inferred name '{gameName}', engine={engine}");

        // ── Check for duplicates (by install path or normalized name) ─────────
        var normName = ViewModel.GameDetectionServiceInstance.NormalizeName(gameName);
        var normInstall = installPath.TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();

        var existingCard = ViewModel.AllCards.FirstOrDefault(c =>
            ViewModel.GameDetectionServiceInstance.NormalizeName(c.GameName) == normName
            || (!string.IsNullOrEmpty(c.InstallPath)
                && c.InstallPath.TrimEnd(Path.DirectorySeparatorChar)
                    .Equals(normInstall, StringComparison.OrdinalIgnoreCase)));

        if (existingCard != null)
        {
            var dupDialog = new ContentDialog
            {
                Title           = "Game Already Exists",
                Content         = $"\"{existingCard.GameName}\" is already in your library at:\n{existingCard.InstallPath}",
                CloseButtonText = "OK",
                XamlRoot        = Content.XamlRoot,
                Background      = Brush(ResourceKeys.SurfaceToolbarBrush),
            };
            await dupDialog.ShowAsync();
            return;
        }

        // ── Confirm with user (allow name edit) ──────────────────────────────
        var nameBox = new TextBox { Text = gameName, Width = 380 };
        var engineLabel = engine switch
        {
            EngineType.Unreal       => "Unreal Engine",
            EngineType.UnrealLegacy => "Unreal Engine (Legacy)",
            EngineType.Unity        => "Unity",
            _                       => "Unknown"
        };

        var confirmPanel = new StackPanel { Spacing = 8 };
        confirmPanel.Children.Add(new TextBlock
        {
            Text = "Game name:", Foreground = Brush(ResourceKeys.TextSecondaryBrush),
        });
        confirmPanel.Children.Add(nameBox);
        confirmPanel.Children.Add(new TextBlock
        {
            Text = $"Engine: {engineLabel}\nInstall path: {installPath}",
            TextWrapping = TextWrapping.Wrap,
            Foreground   = Brush(ResourceKeys.TextTertiaryBrush),
            FontSize     = 12, Margin = new Thickness(0, 6, 0, 0),
        });

        var confirmDialog = new ContentDialog
        {
            Title             = "➕ Add Dropped Game",
            Content           = confirmPanel,
            PrimaryButtonText = "Add Game",
            CloseButtonText   = "Cancel",
            XamlRoot          = Content.XamlRoot,
            Background        = Brush(ResourceKeys.SurfaceToolbarBrush),
        };
        var result = await confirmDialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        var finalName = nameBox.Text.Trim();
        if (string.IsNullOrEmpty(finalName)) return;

        CrashReporter.Log($"[MainWindow.ProcessDroppedExe] Adding game '{finalName}' at '{installPath}'");
        var game = new DetectedGame
        {
            Name = finalName, InstallPath = gameRoot, Source = "Manual", IsManuallyAdded = true
        };
        ViewModel.AddManualGameCommand.Execute(game);
    }

    /// <summary>
    /// <summary>
    /// Handles a dropped archive file (.zip, .7z, .rar, etc.) — extracts it using 7-Zip,
    /// looks for .addon64/.addon32 files inside, and passes them to ProcessDroppedAddon.
    /// </summary>
    private async Task ProcessDroppedArchive(string archivePath)
    {
        var archiveName = Path.GetFileName(archivePath);
        CrashReporter.Log($"[MainWindow.ProcessDroppedArchive] Received '{archiveName}'");

        var sevenZipExe = App.Services.GetRequiredService<ISevenZipExtractor>().Find7ZipExe();
        if (sevenZipExe == null)
        {
            var errDialog = new ContentDialog
            {
                Title = "7-Zip Not Found",
                Content = "Cannot extract archive — 7-Zip was not found. Please reinstall RDXC.",
                CloseButtonText = "OK",
                XamlRoot = this.Content.XamlRoot,
            };
            await errDialog.ShowAsync();
            return;
        }

        // Extract entire archive to a temp directory
        var tempDir = Path.Combine(Path.GetTempPath(), $"rdxc_archive_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = sevenZipExe,
                Arguments = $"x \"{archivePath}\" -o\"{tempDir}\" -y",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            CrashReporter.Log($"[MainWindow.ProcessDroppedArchive] Extracting with {psi.FileName} {psi.Arguments}");

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null)
            {
                CrashReporter.Log("[MainWindow.ProcessDroppedArchive] Failed to start 7z process");
                return;
            }

            // Read output asynchronously to prevent deadlock
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();
            proc.WaitForExit(60_000); // 60 second timeout for large archives

            var stderr = await stderrTask;
            if (!string.IsNullOrWhiteSpace(stderr))
                CrashReporter.Log($"[MainWindow.ProcessDroppedArchive] 7z stderr: {stderr}");

            if (proc.ExitCode != 0)
            {
                CrashReporter.Log($"[MainWindow.ProcessDroppedArchive] 7z exit code {proc.ExitCode}");
                var failDialog = new ContentDialog
                {
                    Title = "Archive Extraction Failed",
                    Content = $"Failed to extract '{archiveName}'. The file may be corrupt or in an unsupported format.",
                    CloseButtonText = "OK",
                    XamlRoot = this.Content.XamlRoot,
                };
                await failDialog.ShowAsync();
                return;
            }

            // Search for .addon64 and .addon32 files in the extracted contents
            var addonFiles = Directory.GetFiles(tempDir, "*.addon64", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(tempDir, "*.addon32", SearchOption.AllDirectories))
                .ToList();

            if (addonFiles.Count == 0)
            {
                CrashReporter.Log($"[MainWindow.ProcessDroppedArchive] No addon files found in '{archiveName}'");
                var noAddonDialog = new ContentDialog
                {
                    Title = "No Addon Found",
                    Content = $"No .addon64 or .addon32 files were found inside '{archiveName}'.",
                    CloseButtonText = "OK",
                    XamlRoot = this.Content.XamlRoot,
                };
                await noAddonDialog.ShowAsync();
                return;
            }

            CrashReporter.Log($"[MainWindow.ProcessDroppedArchive] Found {addonFiles.Count} addon file(s): [{string.Join(", ", addonFiles.Select(Path.GetFileName))}]");

            // If multiple addons found, let the user pick; otherwise use the single one
            string addonToInstall;
            if (addonFiles.Count == 1)
            {
                addonToInstall = addonFiles[0];
            }
            else
            {
                // Show a picker dialog for multiple addons
                var combo = new ComboBox
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    PlaceholderText = "Select addon to install...",
                };
                foreach (var af in addonFiles)
                    combo.Items.Add(new ComboBoxItem { Content = Path.GetFileName(af), Tag = af });
                combo.SelectedIndex = 0;

                var pickDialog = new ContentDialog
                {
                    Title = $"Multiple Addons in '{archiveName}'",
                    Content = combo,
                    PrimaryButtonText = "Install",
                    CloseButtonText = "Cancel",
                    XamlRoot = this.Content.XamlRoot,
                };
                if (await pickDialog.ShowAsync() != ContentDialogResult.Primary) return;
                addonToInstall = (combo.SelectedItem as ComboBoxItem)?.Tag as string ?? addonFiles[0];
            }

            // Pass the extracted addon to the existing install flow
            await ProcessDroppedAddon(addonToInstall);
        }
        finally
        {
            // Clean up temp directory
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Handles a dropped .addon64/.addon32 file — prompts the user to pick a game
    /// and installs the addon to that game's folder after confirmation.
    /// </summary>
    private async Task ProcessDroppedAddon(string addonPath)
    {
        var addonFileName = Path.GetFileName(addonPath);
        CrashReporter.Log($"[MainWindow.ProcessDroppedAddon] Received '{addonFileName}'");

        // Build a list of all detected games to choose from
        var cards = ViewModel.AllCards?.ToList() ?? new();
        if (cards.Count == 0)
        {
            var noGamesDialog = new ContentDialog
            {
                Title = "No Games Available",
                Content = "No games are currently detected. Add a game first.",
                CloseButtonText = "OK",
                XamlRoot = this.Content.XamlRoot,
            };
            await noGamesDialog.ShowAsync();
            return;
        }

        // Build a ComboBox for game selection
        var combo = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = "Select a game...",
        };

        // Sort alphabetically and populate
        var sortedCards = cards.OrderBy(c => c.GameName, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var card in sortedCards)
            combo.Items.Add(new ComboBoxItem { Content = card.GameName, Tag = card });

        // Try to auto-select a game by matching addon filename to game names
        // e.g. "renodx-re9requiem.addon64" might fuzzy-match a game with "requiem" in the name
        var addonNameLower = Path.GetFileNameWithoutExtension(addonFileName).ToLowerInvariant();
        for (int i = 0; i < sortedCards.Count; i++)
        {
            // Check if the addon name contains a significant part of the game name
            string[] gameWords = sortedCards[i].GameName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (gameWords.Length >= 2)
            {
                bool matched = false;
                foreach (var w in gameWords)
                {
                    if (w.Length > 3 && addonNameLower.Contains(w.ToLowerInvariant()))
                    {
                        matched = true;
                        break;
                    }
                }
                if (matched)
                {
                    combo.SelectedIndex = i;
                    break;
                }
            }
        }

        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = $"Install {addonFileName} to a game folder.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            Foreground = Brush(ResourceKeys.TextSecondaryBrush),
        });
        panel.Children.Add(combo);

        var pickDialog = new ContentDialog
        {
            Title = "📦 Install RenoDX Addon",
            Content = panel,
            PrimaryButtonText = "Next",
            CloseButtonText = "Cancel",
            XamlRoot = this.Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };

        var pickResult = await pickDialog.ShowAsync();
        if (pickResult != ContentDialogResult.Primary) return;

        if (combo.SelectedItem is not ComboBoxItem selected || selected.Tag is not GameCardViewModel targetCard)
        {
            var noSelection = new ContentDialog
            {
                Title = "No Game Selected",
                Content = "Please select a game to install the addon to.",
                CloseButtonText = "OK",
                XamlRoot = this.Content.XamlRoot,
            };
            await noSelection.ShowAsync();
            return;
        }

        var gameName = targetCard.GameName;
        var installPath = targetCard.InstallPath;

        // Check for existing RenoDX addon files in the game folder
        string? existingAddon = null;
        try
        {
            var existing = Directory.GetFiles(installPath, "*.addon64")
                .Concat(Directory.GetFiles(installPath, "*.addon32"))
                .Where(f => !Path.GetFileName(f).StartsWith("zzz_display_commander", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (existing.Count > 0)
                existingAddon = string.Join(", ", existing.Select(Path.GetFileName));
        }
        catch { }

        // Confirmation dialog
        var warningText = $"Are you sure you want to install {addonFileName} for {gameName}?";
        if (!string.IsNullOrEmpty(existingAddon))
            warningText += $"\n\nThis will replace the existing addon: {existingAddon}";
        warningText += $"\n\nInstall path: {installPath}";

        var confirmDialog = new ContentDialog
        {
            Title = "⚠ Confirm Addon Install",
            Content = new TextBlock
            {
                Text = warningText,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
            },
            PrimaryButtonText = "Install",
            CloseButtonText = "Cancel",
            XamlRoot = this.Content.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
        };

        var confirmResult = await confirmDialog.ShowAsync();
        if (confirmResult != ContentDialogResult.Primary) return;

        // Remove existing RenoDX addon files (not DC addons)
        try
        {
            var toRemove = Directory.GetFiles(installPath, "*.addon64")
                .Concat(Directory.GetFiles(installPath, "*.addon32"))
                .Where(f => !Path.GetFileName(f).StartsWith("zzz_display_commander", StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var f in toRemove)
            {
                CrashReporter.Log($"[MainWindow.ProcessDroppedAddon] Removing existing '{Path.GetFileName(f)}'");
                File.Delete(f);
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.ProcessDroppedAddon] Failed to remove existing addons — {ex.Message}");
        }

        // Copy the addon file to the game folder
        var destPath = Path.Combine(installPath, addonFileName);
        try
        {
            File.Copy(addonPath, destPath, overwrite: true);
            CrashReporter.Log($"[MainWindow.ProcessDroppedAddon] Installed '{addonFileName}' to '{installPath}'");

            // Update card status
            targetCard.Status = GameStatus.Installed;
            targetCard.InstalledAddonFileName = addonFileName;
            targetCard.NotifyAll();

            var successDialog = new ContentDialog
            {
                Title = "✅ Addon Installed",
                Content = $"{addonFileName} has been installed for {gameName}.",
                CloseButtonText = "OK",
                XamlRoot = this.Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            };
            await successDialog.ShowAsync();
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.ProcessDroppedAddon] Install failed — {ex.Message}");
            var errDialog = new ContentDialog
            {
                Title = "❌ Install Failed",
                Content = $"Failed to install addon: {ex.Message}",
                CloseButtonText = "OK",
                XamlRoot = this.Content.XamlRoot,
            };
            await errDialog.ShowAsync();
        }
    }

    /// <summary>
    /// Walk up from the exe directory to find the game root.
    /// Stops when we find a directory that looks like a game root.
    /// For Unreal: recognises Binaries\Win64 structure (2 levels up).
    /// For other games: checks for store markers (Steam, GOG, Epic, EA, Xbox)
    /// and defaults to the exe's own directory if no markers are found.
    /// </summary>
    private static string InferGameRoot(string exeDir)
    {
        var dir = exeDir;

        // If the exe is inside Binaries\Win64, Binaries\WinGDK, or Binaries\Win32,
        // the game root is two levels up.
        var dirName   = Path.GetFileName(dir) ?? "";
        var parentDir = Path.GetDirectoryName(dir);
        var parentName = parentDir != null ? Path.GetFileName(parentDir) ?? "" : "";

        if (parentName.Equals("Binaries", StringComparison.OrdinalIgnoreCase)
            && (dirName.Equals("Win64", StringComparison.OrdinalIgnoreCase)
             || dirName.Equals("WinGDK", StringComparison.OrdinalIgnoreCase)
             || dirName.Equals("Win32", StringComparison.OrdinalIgnoreCase)))
        {
            var grandparent = Path.GetDirectoryName(parentDir);
            if (grandparent != null) return grandparent;
        }

        // Walk up looking for game root markers (max 3 levels).
        // Check the exe's own directory first — most non-Unreal games have
        // the exe right in the game root alongside store markers.
        var current = dir;
        for (int i = 0; i < 3 && current != null; i++)
        {
            if (LooksLikeGameRoot(current))
                return current;
            current = Path.GetDirectoryName(current);
        }

        // No markers found at all — the exe directory itself is the safest bet.
        // Don't walk up further, as that risks hitting a library root or drive root.
        return dir;
    }

    /// <summary>
    /// Returns true if a directory looks like a game root based on store markers
    /// or engine files. This is intentionally broad to catch Steam, GOG, Epic,
    /// EA, Xbox, Ubisoft, Unity, and Unreal games.
    /// </summary>
    private static bool LooksLikeGameRoot(string dirPath)
    {
        try
        {
            // Steam markers
            if (File.Exists(Path.Combine(dirPath, "steam_appid.txt"))
             || File.Exists(Path.Combine(dirPath, "steam_api64.dll"))
             || File.Exists(Path.Combine(dirPath, "steam_api.dll")))
                return true;

            // GOG markers — GOG games have goggame-*.dll, goglog.ini, gog.ico, etc.
            if (File.Exists(Path.Combine(dirPath, "goglog.ini"))
             || File.Exists(Path.Combine(dirPath, "gog.ico"))
             || File.Exists(Path.Combine(dirPath, "goggame.sdb")))
                return true;
            // Also check for goggame-*.dll pattern
            if (Directory.GetFiles(dirPath, "goggame-*.dll").Length > 0)
                return true;

            // Epic markers
            if (Directory.Exists(Path.Combine(dirPath, ".egstore")))
                return true;

            // EA markers
            if (File.Exists(Path.Combine(dirPath, "installerdata.xml"))
             || File.Exists(Path.Combine(dirPath, "__Installer")))
                return true;

            // Xbox / Game Pass markers
            if (File.Exists(Path.Combine(dirPath, "MicrosoftGame.config"))
             || File.Exists(Path.Combine(dirPath, "appxmanifest.xml")))
                return true;

            // Ubisoft Connect markers
            if (File.Exists(Path.Combine(dirPath, "uplay_install.state"))
             || File.Exists(Path.Combine(dirPath, "upc.exe"))
             || Directory.GetFiles(dirPath, "uplay_*.dll").Length > 0)
                return true;

            // Battle.net / Blizzard markers
            if (File.Exists(Path.Combine(dirPath, ".build.info"))
             || File.Exists(Path.Combine(dirPath, ".product.db"))
             || File.Exists(Path.Combine(dirPath, "Blizzard Launcher.exe")))
                return true;

            // Rockstar Games Launcher markers
            if (File.Exists(Path.Combine(dirPath, "PlayGTAV.exe"))
             || File.Exists(Path.Combine(dirPath, "RockstarService.exe"))
             || Directory.GetFiles(dirPath, "socialclub*.dll").Length > 0)
                return true;

            // Unity marker
            if (File.Exists(Path.Combine(dirPath, "UnityPlayer.dll")))
                return true;

            // Unreal markers
            if (Directory.Exists(Path.Combine(dirPath, "Binaries"))
             || Directory.Exists(Path.Combine(dirPath, "Engine")))
                return true;
        }
        catch { /* permission issues — skip silently */ }

        return false;
    }

    /// <summary>
    /// Infer the game name from the exe and folder structure.
    /// Priority:
    ///   1. For Unreal: use the top-level folder name under game root (the "project" name)
    ///   2. For Unity: use the exe name (typically matches the game name)
    ///   3. Use the game root folder name
    ///   4. Fallback to exe filename
    /// Cleans up common suffixes like "-Win64-Shipping", "Shipping", etc.
    /// </summary>
    private static string InferGameName(string exePath, string gameRoot, EngineType engine)
    {
        var exeName     = Path.GetFileNameWithoutExtension(exePath);
        var rootDirName = Path.GetFileName(gameRoot) ?? exeName;

        if (engine == EngineType.Unreal || engine == EngineType.UnrealLegacy)
        {
            // Unreal games: the exe is often "GameName-Win64-Shipping.exe"
            // Strip the suffix to get the clean name
            var cleanExe = CleanUnrealExeName(exeName);

            // Sometimes the root folder is better (e.g. "Avowed" vs "Michigan-Win64-Shipping")
            // Prefer root folder name if it looks like a proper name (has spaces or is short)
            if (rootDirName.Contains(' ') || rootDirName.Contains('-'))
                return CleanFolderName(rootDirName);

            // Check for a content/game subfolder one level into root (common Xbox pattern)
            try
            {
                var subdirs = Directory.GetDirectories(gameRoot)
                    .Select(Path.GetFileName)
                    .Where(d => d != null
                        && !d.Equals("Binaries", StringComparison.OrdinalIgnoreCase)
                        && !d.Equals("Engine", StringComparison.OrdinalIgnoreCase)
                        && !d.Equals("Content", StringComparison.OrdinalIgnoreCase)
                        && !d.StartsWith(".", StringComparison.Ordinal))
                    .ToList();

                // If there's one or two content folders, the first might be the game name
                if (subdirs.Count > 0 && subdirs.Count <= 3)
                {
                    var candidate = subdirs.FirstOrDefault(d =>
                        !string.IsNullOrEmpty(d)
                        && !d.Equals("Saved", StringComparison.OrdinalIgnoreCase)
                        && !d.Equals("Plugins", StringComparison.OrdinalIgnoreCase)
                        && !d.Equals("Intermediate", StringComparison.OrdinalIgnoreCase));

                    if (candidate != null && candidate.Length > 2)
                        return CleanFolderName(candidate);
                }
            }
            catch { }

            return !string.IsNullOrEmpty(cleanExe) ? cleanExe : CleanFolderName(rootDirName);
        }

        if (engine == EngineType.Unity)
        {
            // Unity: exe name typically IS the game name
            return CleanFolderName(exeName);
        }

        // Unknown engine: prefer root folder name, fall back to exe name
        return CleanFolderName(rootDirName);
    }

    /// <summary>Strips common Unreal exe suffixes to get a clean game name.</summary>
    private static string CleanUnrealExeName(string exeName)
    {
        // Common patterns: "GameName-Win64-Shipping", "GameName-WinGDK-Shipping",
        // "GameNameShipping", "GameName-Win64-Test"
        var cleaned = Regex.Replace(exeName, @"[_-]?(Win64|WinGDK|Win32)[_-]?Shipping$", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"[_-]?Shipping$", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"[_-]?(Win64|WinGDK|Win32)[_-]?Test$", "", RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned, @"[_-]?(Win64|WinGDK|Win32)$", "", RegexOptions.IgnoreCase);
        return cleaned.Trim('-', '_', ' ');
    }

    /// <summary>
    /// Cleans a folder or exe name into a presentable game name.
    /// Replaces underscores and camelCase boundaries with spaces.
    /// </summary>
    private static string CleanFolderName(string name)
    {
        // Replace underscores and hyphens with spaces
        var cleaned = name.Replace('_', ' ').Replace('-', ' ');
        // Insert spaces before uppercase letters in camelCase (e.g. "HighOnLife" → "High On Life")
        // but not for consecutive caps (e.g. "AFOP" stays "AFOP")
        cleaned = Regex.Replace(cleaned, @"(?<=[a-z])(?=[A-Z])", " ");
        // Collapse multiple spaces
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        return cleaned;
    }

    // ── Filter tabs ───────────────────────────────────────────────────────────────

    private void Filter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        ViewModel.SetFilterCommand.Execute(btn.Tag as string ?? "Detected");

        // Style buttons based on active filter set
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

    private async void CombinedInstallButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not GameCardViewModel card) return;
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
        if (card.DcRowVisibility == Microsoft.UI.Xaml.Visibility.Visible)
            await ViewModel.InstallDcCommand.ExecuteAsync(card);
        if (card.ReShadeRowVisibility == Microsoft.UI.Xaml.Visibility.Visible)
            await ViewModel.InstallReShadeCommand.ExecuteAsync(card);
    }

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {

        // If this is an external-only game, open the external URL instead
        var checkCard = GetCardFromSender(sender);
        if (checkCard?.IsExternalOnly == true)
        {
            ExternalLink_Click(sender, e);
            return;
        }
        if (sender is not Button btn || btn.Tag is not GameCardViewModel card) return;
        await EnsurePathAndInstall(card, () => ViewModel.InstallModCommand.ExecuteAsync(card));
    }

    private async void Install64Button_Click(object sender, RoutedEventArgs e)
    {
        var card = GetCardFromSender(sender);
        if (card == null) return;
        await EnsurePathAndInstall(card, () => ViewModel.InstallModCommand.ExecuteAsync(card));
    }

    private async void Install32Button_Click(object sender, RoutedEventArgs e)
    {
        var card = GetCardFromSender(sender);
        if (card == null) return;
        await EnsurePathAndInstall(card, () => ViewModel.InstallMod32Command.ExecuteAsync(card));
    }

    private async Task EnsurePathAndInstall(GameCardViewModel card, Func<Task> installAction)
    {
        if (string.IsNullOrEmpty(card.InstallPath) || !System.IO.Directory.Exists(card.InstallPath))
        {
            var folder = await PickFolderAsync();
            if (folder == null) return;
            card.InstallPath = folder;
        }
        await installAction();
    }

    private void UninstallButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetCardFromSender(sender) is { } card)
            ViewModel.UninstallModCommand.Execute(card);
    }

    private async void InstallRsButton_Click(object sender, RoutedEventArgs e)
    {
        var card = (sender as FrameworkElement)?.Tag as GameCardViewModel;
        if (card == null) return;
        if (string.IsNullOrEmpty(card.InstallPath) || !System.IO.Directory.Exists(card.InstallPath))
        {
            var folder = await PickFolderAsync();
            if (folder == null) return;
            card.InstallPath = folder;
            ViewModel.SaveLibraryPublic();
        }
        await ViewModel.InstallReShadeCommand.ExecuteAsync(card);
    }

    private void UninstallRsButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is GameCardViewModel card)
        {
            if (card.RequiresVulkanInstall)
                ViewModel.UninstallVulkanReShadeCommand.Execute(card);
            else
                ViewModel.UninstallReShadeCommand.Execute(card);
        }
    }

    // ── Shaders mode cycle handler ──────────────────────────────────────────

    private async void ShadersModeButton_Click(object sender, RoutedEventArgs e)
    {
        // If already in Select mode, re-open the picker instead of cycling
        if (ViewModel.ShaderDeployMode == ShaderPackService.DeployMode.Select)
        {
            var result = await ShowShaderPickerDialogAsync(ViewModel.Settings.SelectedShaderPacks);
            if (result != null)
            {
                ViewModel.Settings.SelectedShaderPacks = result;
                ViewModel.DeployAllShaders();
            }
            ViewModel.CycleShaderDeployMode(); // Select → Off
            return;
        }

        var newMode = ViewModel.CycleShaderDeployMode();

        // When cycling into Select mode, immediately show the picker
        if (newMode == ShaderPackService.DeployMode.Select)
        {
            var result = await ShowShaderPickerDialogAsync(ViewModel.Settings.SelectedShaderPacks);
            if (result != null)
            {
                ViewModel.Settings.SelectedShaderPacks = result;
                ViewModel.DeployAllShaders();
            }
            else
            {
                // User cancelled — revert to previous mode (cycle back)
                ViewModel.CycleShaderDeployMode(); // Select → Off
            }
        }
    }

    /// <summary>
    /// Shows a modal ContentDialog with checkboxes for each shader pack.
    /// Pre-populated from <paramref name="currentSelection"/>.
    /// Returns the confirmed selection, or null if the user cancelled.
    /// </summary>
    private async Task<List<string>?> ShowShaderPickerDialogAsync(List<string>? currentSelection)
    {
        var packs = ViewModel.ShaderPackServiceInstance.AvailablePacks;
        var selected = new HashSet<string>(currentSelection ?? [], StringComparer.OrdinalIgnoreCase);

        var panel = new StackPanel { Spacing = 6 };
        var checkBoxes = new List<(string Id, CheckBox Box)>();

        foreach (var (id, displayName) in packs)
        {
            var cb = new CheckBox
            {
                Content    = displayName,
                IsChecked  = selected.Contains(id),
                FontSize   = 13,
                Foreground = Brush(ResourceKeys.TextPrimaryBrush),
            };
            checkBoxes.Add((id, cb));
            panel.Children.Add(cb);
        }

        var dlg = new ContentDialog
        {
            Title             = "Select Shader Packs",
            Content           = panel,
            PrimaryButtonText = "OK",
            CloseButtonText   = "Cancel",
            XamlRoot          = Content.XamlRoot,
            Background        = Brush(ResourceKeys.SurfaceOverlayBrush),
        };

        var dialogResult = await dlg.ShowAsync();
        if (dialogResult != ContentDialogResult.Primary)
            return null; // cancelled — caller should revert

        // Build the confirmed selection from ticked checkboxes
        var confirmed = new List<string>();
        foreach (var (id, box) in checkBoxes)
        {
            if (box.IsChecked == true)
                confirmed.Add(id);
        }
        return confirmed;
    }

    private async void DeployShadersButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ContentDialog
        {
            Title             = "Deploy Shaders",
            Content           = "Deploy the current shader mode to all installed games?",
            PrimaryButtonText = "Continue",
            CloseButtonText   = "Cancel",
            XamlRoot          = Content.XamlRoot,
        };
        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
            ViewModel.DeployAllShaders();
    }

    private void DcModeButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CycleDcMode();
    }

    private async void DeployDcModeButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ContentDialog
        {
            Title             = "Deploy DC Mode",
            Content           = "Apply DC Mode file changes across all installed games?",
            PrimaryButtonText = "Continue",
            CloseButtonText   = "Cancel",
            XamlRoot          = Content.XamlRoot,
        };
        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
            ViewModel.ApplyDcModeSwitch();
    }

    // ── Update All handlers ──────────────────────────────────────────────────

    private void UpdateAllButton_Click(object sender, RoutedEventArgs e)
    {
        // The button opens its flyout automatically; nothing extra needed here.
        // (WinUI Button.Flyout opens on click.)
    }

    private async void UpdateAllRenoDx_Click(object sender, RoutedEventArgs e)
        => await ViewModel.UpdateAllRenoDxAsync();

    private async void UpdateAllReShade_Click(object sender, RoutedEventArgs e)
        => await ViewModel.UpdateAllReShadeAsync();

    private async void UpdateAllDc_Click(object sender, RoutedEventArgs e)
        => await ViewModel.UpdateAllDcAsync();

    private async void InstallDcButton_Click(object sender, RoutedEventArgs e)
    {
        var card = (sender as FrameworkElement)?.Tag as GameCardViewModel;
        if (card == null) return;
        if (string.IsNullOrEmpty(card.InstallPath) || !System.IO.Directory.Exists(card.InstallPath))
        {
            var folder = await PickFolderAsync();
            if (folder == null) return;
            card.InstallPath = folder;
            ViewModel.SaveLibraryPublic();
        }
        await ViewModel.InstallDcCommand.ExecuteAsync(card);
    }

    private void UninstallDcButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is GameCardViewModel card)
            ViewModel.UninstallDcCommand.Execute(card);
    }

    private void LumaToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_detailPanelBuilder.CurrentDetailCard != null) ViewModel.ToggleLumaMode(_detailPanelBuilder.CurrentDetailCard);
    }

    private void SwitchToLumaButton_Click(object sender, RoutedEventArgs e)
    {
        var card = (sender as FrameworkElement)?.Tag as GameCardViewModel;
        if (card != null) ViewModel.ToggleLumaMode(card);
    }

    private async void InstallLumaButton_Click(object sender, RoutedEventArgs e)
    {
        var card = (sender as FrameworkElement)?.Tag as GameCardViewModel;
        if (card != null) await ViewModel.InstallLumaAsync(card);
    }

    private void UninstallLumaButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is GameCardViewModel card)
            ViewModel.UninstallLumaCommand.Execute(card);
    }

    private void UeExtendedFlyoutItem_Click(object sender, RoutedEventArgs e)
    {
        var card = (sender as FrameworkElement)?.Tag as GameCardViewModel;
        if (card == null) return;

        ViewModel.ToggleUeExtended(card);

        // Directly update the badge text based on the new state
        string newLabel = card.UseUeExtended ? "UE Extended" : "Generic UE";
        DetailGenericText.Text = newLabel;

        // Update the UE button styling
        if (card.UseUeExtended)
        {
            DetailUeExtendedBtn.Background = Brush(ResourceKeys.AccentGreenBgBrush);
            DetailUeExtendedBtn.Foreground = Brush(ResourceKeys.AccentGreenBrush);
            DetailUeExtendedBtn.BorderBrush = Brush(ResourceKeys.AccentGreenBorderBrush);
        }
        else
        {
            DetailUeExtendedBtn.Background = Brush(ResourceKeys.SurfaceOverlayBrush);
            DetailUeExtendedBtn.Foreground = Brush(ResourceKeys.TextSecondaryBrush);
            DetailUeExtendedBtn.BorderBrush = Brush(ResourceKeys.BorderStrongBrush);
        }

        // Update tooltip
        ToolTipService.SetToolTip(DetailUeExtendedBtn,
            card.UseUeExtended ? "Disable UE Extended" : "Enable UE Extended");

        // Show inline message or warning dialog
        if (card.UseUeExtended)
        {
            DetailRsMessage.Text = "⚡ UE-Extended enabled — check Discord to confirm this game is compatible.";
            DetailRsMessage.Foreground = Brush(ResourceKeys.AccentPurpleBrush);
            DetailRsMessage.Visibility = Visibility.Visible;
            // Show compatibility warning dialog
            _ = ShowUeExtendedWarningAsync(card);
        }
        else
        {
            DetailRsMessage.Text = "UE-Extended disabled.";
            DetailRsMessage.Foreground = Brush(ResourceKeys.TextTertiaryBrush);
            DetailRsMessage.Visibility = Visibility.Visible;
        }
    }

    private async Task ShowUeExtendedWarningAsync(GameCardViewModel card)
    {
        try
        {
            while (Content.XamlRoot == null)
                await Task.Delay(100);

            var hasNotes = !string.IsNullOrWhiteSpace(card.Notes);
            var notesHint = hasNotes
                ? "\n\nCheck the Notes section for any additional compatibility information for this game."
                : "\n\nNo specific notes are available for this game — check the RDXC Discord for community reports.";

            var dlg = new ContentDialog
            {
                Title               = "⚠ UE-Extended Compatibility Warning",
                Content             = new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    FontSize     = 13,
                    Text         = "Not all Unreal Engine games are compatible with UE-Extended.\n\n" +
                                   "UE-Extended uses a different injection method that works better " +
                                   "with some games but may cause crashes or issues with others." +
                                   notesHint,
                },
                PrimaryButtonText   = "OK, I understand",
                XamlRoot            = Content.XamlRoot,
                Background          = Brush(ResourceKeys.SurfaceOverlayBrush),
            };

            await dlg.ShowAsync();
        }
        catch { }
    }

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

    private async void ExternalLink_Click(object sender, RoutedEventArgs e)
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
            catch (Exception ex) { CrashReporter.Log($"[MainWindow.CardInfoLink_Click] Failed — {ex.Message}"); }
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
        catch { /* card may have been removed from visual tree */ }
    }

    private async void NotesButton_Click(object sender, RoutedEventArgs e)
    {
        var card = GetCardFromSender(sender);
        if (card == null) return;

        var textColour = Brush(ResourceKeys.TextSecondaryBrush);
        var linkColour = Brush(ResourceKeys.AccentBlueBrush);
        var dimColour  = Brush(ResourceKeys.TextTertiaryBrush);

        var outerPanel = new StackPanel { Spacing = 10 };

        // ── Wiki status badge at top-left ─────────────────────────────────────────
        var statusBg     = card.WikiStatusBadgeBackground;
        var statusBorder = card.WikiStatusBadgeBorderBrush;
        var statusFg     = card.WikiStatusBadgeForeground;
        var statusBadge = new Border
        {
            CornerRadius    = new CornerRadius(6),
            Padding         = new Thickness(10, 4, 10, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background      = new SolidColorBrush(ParseColor(statusBg)),
            BorderBrush     = new SolidColorBrush(ParseColor(statusBorder)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text       = card.WikiStatusLabel,
                FontSize   = 12,
                Foreground = new SolidColorBrush(ParseColor(statusFg)),
            }
        };
        outerPanel.Children.Add(statusBadge);

        // ── Luma info (when in Luma mode) ───────────────────────────────────────────
        if (card.IsLumaMode && (card.LumaMod != null || !string.IsNullOrWhiteSpace(card.LumaNotes)))
        {
            var lumaLabel = card.LumaMod != null
                ? $"Luma — {card.LumaMod.Status} {card.LumaMod.Author}"
                : "Luma mode";
            var lumaBadge = new Border
            {
                CornerRadius    = new CornerRadius(6),
                Padding         = new Thickness(10, 4, 10, 4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background      = Brush(ResourceKeys.AccentGreenBgBrush),
                BorderBrush     = Brush(ResourceKeys.AccentGreenBorderBrush),
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text       = lumaLabel,
                    FontSize   = 12,
                    Foreground = Brush(ResourceKeys.AccentGreenBrush),
                }
            };
            outerPanel.Children.Add(lumaBadge);

            var lumaNotesText = "";
            if (card.LumaMod != null)
            {
                if (!string.IsNullOrWhiteSpace(card.LumaMod.SpecialNotes))
                    lumaNotesText += card.LumaMod.SpecialNotes;
                if (!string.IsNullOrWhiteSpace(card.LumaMod.FeatureNotes))
                {
                    if (lumaNotesText.Length > 0) lumaNotesText += "\n\n";
                    lumaNotesText += card.LumaMod.FeatureNotes;
                }
            }

            if (!string.IsNullOrWhiteSpace(lumaNotesText))
            {
                outerPanel.Children.Add(new TextBlock
                {
                    Text         = lumaNotesText,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground   = textColour,
                    FontSize     = 13,
                    LineHeight   = 22,
                });
            }

            // ── Manifest Luma notes (supplement wiki notes) ──────────────────────
            if (!string.IsNullOrWhiteSpace(card.LumaNotes))
            {
                if (!string.IsNullOrEmpty(card.LumaNotesUrl))
                {
                    var para = new Microsoft.UI.Xaml.Documents.Paragraph();
                    para.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
                    {
                        Text       = card.LumaNotes,
                        Foreground = textColour,
                        FontSize   = 13,
                    });
                    para.Inlines.Add(new Microsoft.UI.Xaml.Documents.LineBreak());
                    var link = new Microsoft.UI.Xaml.Documents.Hyperlink
                    {
                        NavigateUri = new Uri(card.LumaNotesUrl),
                        Foreground  = linkColour,
                    };
                    link.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
                    {
                        Text     = card.LumaNotesUrlLabel ?? card.LumaNotesUrl,
                        FontSize = 13,
                    });
                    para.Inlines.Add(link);
                    var rtb = new RichTextBlock { IsTextSelectionEnabled = true };
                    rtb.Blocks.Add(para);
                    outerPanel.Children.Add(rtb);
                }
                else
                {
                    outerPanel.Children.Add(new TextBlock
                    {
                        Text         = card.LumaNotes,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground   = textColour,
                        FontSize     = 13,
                        LineHeight   = 22,
                    });
                }
            }

            // Fallback if neither wiki nor manifest provided notes
            if (string.IsNullOrWhiteSpace(lumaNotesText) && string.IsNullOrWhiteSpace(card.LumaNotes))
            {
                outerPanel.Children.Add(new TextBlock
                {
                    Text       = "No additional Luma notes for this game.",
                    Foreground = dimColour,
                    FontSize   = 13,
                });
            }
        }
        // ── Standard RenoDX notes ───────────────────────────────────────────────────
        else if (!string.IsNullOrWhiteSpace(card.Notes))
        {
            if (!string.IsNullOrEmpty(card.NotesUrl))
            {
                var para = new Microsoft.UI.Xaml.Documents.Paragraph();
                para.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
                {
                    Text       = card.Notes,
                    Foreground = textColour,
                    FontSize   = 13,
                });
                para.Inlines.Add(new Microsoft.UI.Xaml.Documents.LineBreak());
                var link = new Microsoft.UI.Xaml.Documents.Hyperlink
                {
                    NavigateUri = new Uri(card.NotesUrl),
                    Foreground  = linkColour,
                };
                link.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
                {
                    Text     = card.NotesUrlLabel ?? card.NotesUrl,
                    FontSize = 13,
                });
                para.Inlines.Add(link);

                var rtb = new RichTextBlock { IsTextSelectionEnabled = true };
                rtb.Blocks.Add(para);
                outerPanel.Children.Add(rtb);
            }
            else
            {
                outerPanel.Children.Add(new TextBlock
                {
                    Text         = card.Notes,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground   = textColour,
                    FontSize     = 13,
                    LineHeight   = 22,
                });
            }
        }
        else
        {
            outerPanel.Children.Add(new TextBlock
            {
                Text       = "No additional notes for this game.",
                Foreground = dimColour,
                FontSize   = 13,
            });
        }

        var scrollContent = new ScrollViewer
        {
            Content   = outerPanel,
            MaxHeight = 440,
            Padding   = new Thickness(0, 4, 12, 0),
        };

        var dialog = new ContentDialog
        {
            Title           = $"ℹ  {card.GameName}",
            Content         = scrollContent,
            CloseButtonText = "Close",
            XamlRoot        = Content.XamlRoot,
            Background      = Brush(ResourceKeys.SurfaceToolbarBrush),
        };
        await dialog.ShowAsync();
    }

    /// <summary>Creates a thin horizontal separator line for dialogs.</summary>
    private static Border MakeSeparator() => new()
    {
        Height = 1,
        Background = (SolidColorBrush)Application.Current.Resources[ResourceKeys.BorderSubtleBrush],
        Margin = new Thickness(0, 2, 0, 2),
    };


    /// <summary>Looks up a SolidColorBrush from the merged theme resource dictionaries.</summary>
    private SolidColorBrush Brush(string key) =>
        (SolidColorBrush)Application.Current.Resources[key];

    /// <summary>Parses a hex colour string like "#1C2848" into a Windows.UI.Color.</summary>
    private static Windows.UI.Color ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        byte a = 255;
        int offset = 0;
        if (hex.Length == 8) { a = Convert.ToByte(hex[..2], 16); offset = 2; }
        byte r = Convert.ToByte(hex.Substring(offset, 2), 16);
        byte g = Convert.ToByte(hex.Substring(offset + 2, 2), 16);
        byte b = Convert.ToByte(hex.Substring(offset + 4, 2), 16);
        return Windows.UI.Color.FromArgb(a, r, g, b);
    }

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

    // COM interop definitions moved to NativeInterop.cs

    // Window persistence P/Invoke declarations moved to NativeInterop.cs

    // WndProc subclass P/Invoke declarations moved to NativeInterop.cs

    private IntPtr _hwnd;
    private IntPtr _origWndProc;
    private NativeInterop.WndProcDelegate? _wndProcDelegate; // prevent GC

    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == NativeInterop.WM_GETMINMAXINFO)
        {
            var dpi = NativeInterop.GetDpiForWindow(hWnd);
            var scale = dpi / 96.0;
            var mmi = Marshal.PtrToStructure<NativeInterop.MINMAXINFO>(lParam);
            mmi.ptMinTrackSize = new System.Drawing.Point(
                (int)(NativeInterop.MinWindowWidth * scale),
                (int)(NativeInterop.MinWindowHeight * scale));
            Marshal.StructureToPtr(mmi, lParam, false);
            return IntPtr.Zero;
        }

        if (msg == NativeInterop.WM_DROPFILES)
        {
            HandleWin32Drop(wParam);
            return IntPtr.Zero;
        }

        return NativeInterop.CallWindowProc(_origWndProc, hWnd, msg, wParam, lParam);
    }

    /// <summary>
    /// Handles WM_DROPFILES from Win32 shell drag-and-drop.
    /// Extracts file paths and routes them to the existing processing methods.
    /// </summary>
    private void HandleWin32Drop(IntPtr hDrop)
    {
        try
        {
            uint fileCount = NativeInterop.DragQueryFile(hDrop, 0xFFFFFFFF, null, 0);
            var paths = new List<string>();

            for (uint i = 0; i < fileCount; i++)
            {
                uint size = NativeInterop.DragQueryFile(hDrop, i, null, 0) + 1;
                var buffer = new char[size];
                NativeInterop.DragQueryFile(hDrop, i, buffer, size);
                paths.Add(new string(buffer, 0, (int)(size - 1)));
            }

            NativeInterop.DragFinish(hDrop);

            // Process on the UI thread
            DispatcherQueue.TryEnqueue(async () =>
            {
                foreach (var path in paths)
                {
                    var ext = Path.GetExtension(path)?.ToLowerInvariant() ?? "";

                    if (ext is ".addon64" or ".addon32")
                    {
                        try { await ProcessDroppedAddon(path); }
                        catch (Exception ex) { CrashReporter.Log($"[MainWindow.HandleWin32Drop] Addon error — {ex.Message}"); }
                        continue;
                    }

                    if (ArchiveExtensions.Contains(ext))
                    {
                        try { await ProcessDroppedArchive(path); }
                        catch (Exception ex) { CrashReporter.Log($"[MainWindow.HandleWin32Drop] Archive error — {ex.Message}"); }
                        continue;
                    }

                    if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        try { await ProcessDroppedExe(path); }
                        catch (Exception ex) { CrashReporter.Log($"[MainWindow.HandleWin32Drop] Exe error — {ex.Message}"); }
                    }
                }
            });
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.HandleWin32Drop] Failed — {ex.Message}");
        }
    }

    // GetDpiForWindow moved to NativeInterop.cs

    // ── Window persistence (JSON-based, works for unpackaged WinUI 3 apps) ────────
    // ApplicationData.Current.LocalSettings requires package identity and throws in
    // unpackaged apps — so we use a plain JSON file in %LocalAppData% instead.
    // Stores window bounds so the window remembers its last size/position.

    private static readonly string _windowSettingsPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RenoDXCommander", "window_main.json");

    // In-memory cache of window bounds (populated from file on first restore)
    private (int X, int Y, int W, int H)? _windowBounds;

    private void TryRestoreWindowBounds()
    {
        try
        {
            if (!System.IO.File.Exists(_windowSettingsPath)) return;
            var json = System.IO.File.ReadAllText(_windowSettingsPath);
            var doc  = System.Text.Json.JsonDocument.Parse(json).RootElement;

            // Try unified bounds first (new format)
            if (doc.TryGetProperty("X", out var ux) && doc.TryGetProperty("Y", out var uy) &&
                doc.TryGetProperty("W", out var uw) && doc.TryGetProperty("H", out var uh))
                _windowBounds = (ux.GetInt32(), uy.GetInt32(), uw.GetInt32(), uh.GetInt32());

            // Legacy migration: old format stored FullX/FullY or CompactX/CompactY — prefer Compact (closest to new layout)
            if (_windowBounds == null &&
                doc.TryGetProperty("CompactX", out var cx) && doc.TryGetProperty("CompactY", out var cy) &&
                doc.TryGetProperty("CompactW", out var cw) && doc.TryGetProperty("CompactH", out var ch))
                _windowBounds = (cx.GetInt32(), cy.GetInt32(), cw.GetInt32(), ch.GetInt32());

            if (_windowBounds == null &&
                doc.TryGetProperty("FullX", out var fx) && doc.TryGetProperty("FullY", out var fy) &&
                doc.TryGetProperty("FullW", out var fw) && doc.TryGetProperty("FullH", out var fh))
                _windowBounds = (fx.GetInt32(), fy.GetInt32(), fw.GetInt32(), fh.GetInt32());

            // Apply the bounds
            if (_windowBounds is var (x, y, w, h) && w >= 400 && h >= 300 && w <= 7680 && h <= 4320)
            {
                var hwnd = WindowNative.GetWindowHandle(this);
                NativeInterop.SetWindowPos(hwnd, IntPtr.Zero, x, y, w, h, 0x0040 /* SWP_NOZORDER */);
            }
        }
        catch { }
    }

    /// <summary>Captures the current window rect into the in-memory cache.</summary>
    private void CaptureCurrentBounds()
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            if (!NativeInterop.GetWindowRect(hwnd, out var r)) return;
            var w = r.Right - r.Left;
            var h = r.Bottom - r.Top;
            if (w < 100 || h < 100) return;
            _windowBounds = (r.Left, r.Top, w, h);
        }
        catch { }
    }

    /// <summary>Restores the cached window bounds, if available.</summary>
    private void RestoreWindowBounds()
    {
        try
        {
            if (_windowBounds is var (x, y, w, h) && w >= 400 && h >= 300 && w <= 7680 && h <= 4320)
            {
                var hwnd = WindowNative.GetWindowHandle(this);
                NativeInterop.SetWindowPos(hwnd, IntPtr.Zero, x, y, w, h, 0x0040 /* SWP_NOZORDER */);
            }
        }
        catch { }
    }

    private void SaveWindowBounds()
    {
        try
        {
            // Capture final bounds
            CaptureCurrentBounds();

            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_windowSettingsPath)!);
            var data = new Dictionary<string, int>();
            if (_windowBounds is var (x, y, w, h))
            {
                data["X"] = x; data["Y"] = y; data["W"] = w; data["H"] = h;
            }
            var json = System.Text.Json.JsonSerializer.Serialize(data);
            System.IO.File.WriteAllText(_windowSettingsPath, json);
        }
        catch { }
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
