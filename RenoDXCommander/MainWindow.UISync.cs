// MainWindow.UISync.cs — ViewModel-to-UI synchronization, card grid rendering,
// detail panel delegation, drag-drop delegation, and dialog delegation methods.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
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
                    if (!loading && !silent && ViewModel.CurrentPage == AppPage.GameView)
                    {
                        RemoveSkeletons();
                        GameViewPanel.Visibility = Visibility.Visible; // already visible, but ensure
                    }
                    // LoadingPanel stays Collapsed always — skeleton replaces it
                    RefreshBtn.IsEnabled = !loading;
                    if (!silent) StatusDot.Fill = new SolidColorBrush(loading
                        ? ((SolidColorBrush)Application.Current.Resources[ResourceKeys.AccentAmberBrush]).Color
                        : ((SolidColorBrush)Application.Current.Resources[ResourceKeys.AccentGreenBrush]).Color);
                    if (!loading)
                    {
                        if (!silent) ViewModel.MarkInitialized();
                        TryRestoreSelection();
                        RefreshFilterButtonStyles();
                        RebuildCustomFilterChips();
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
                case nameof(ViewModel.FilterMode):
                    RefreshFilterButtonStyles();
                    break;
                case nameof(ViewModel.AnyUpdateAvailable):
                    UpdateBtn.Background  = UIFactory.GetBrush(ViewModel.UpdateAllBtnBackground);
                    UpdateBtn.Foreground  = UIFactory.GetBrush(ViewModel.UpdateAllBtnForeground);
                    UpdateBtn.BorderBrush = UIFactory.GetBrush(ViewModel.UpdateAllBtnBorder);
                    break;
                case nameof(ViewModel.CurrentPage):
                    UpdatePageVisibility();
                    break;
            }
        });
    }

    // ── Page visibility management ──────────────────────────────────────────────

    private void UpdatePageVisibility()
    {
        // Show the correct panel based on current page and loading state.
        // LoadingPanel stays Collapsed always — skeleton loading replaces it.
        GameViewPanel.Visibility = ViewModel.CurrentPage == AppPage.GameView ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = ViewModel.CurrentPage == AppPage.Settings ? Visibility.Visible : Visibility.Collapsed;
        AboutPanel.Visibility    = ViewModel.CurrentPage == AppPage.About    ? Visibility.Visible : Visibility.Collapsed;

        // Auto-select first game if nothing is selected
        if (GameList.SelectedItem == null && ViewModel.DisplayedGames.Count > 0)
        {
            GameList.SelectedItem = ViewModel.DisplayedGames[0];
        }
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

        // Restore last selected game from previous session
        if (GameList.SelectedItem == null && ViewModel.LastSelectedGameName != null)
        {
            var lastMatch = ViewModel.DisplayedGames.FirstOrDefault(c =>
                c.GameName.Equals(ViewModel.LastSelectedGameName, StringComparison.OrdinalIgnoreCase));
            if (lastMatch != null)
            {
                GameList.SelectedItem = lastMatch;
                GameList.ScrollIntoView(lastMatch);
                ViewModel.LastSelectedGameName = null; // consumed
                return;
            }
            // Game no longer available — clear and fall through to first-game default
            ViewModel.LastSelectedGameName = null;
        }

        // Auto-select first game if nothing is selected
        if (GameList.SelectedItem == null && ViewModel.DisplayedGames.Count > 0)
        {
            GameList.SelectedItem = ViewModel.DisplayedGames[0];
        }
    }

    // ── Card Grid rendering ───────────────────────────────────────────────────────

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

    // ── Detail panel delegation ───────────────────────────────────────────────────

    private void PopulateDetailPanel(GameCardViewModel card) => _detailPanelBuilder.PopulateDetailPanel(card);

    private void UpdateDetailComponentRows(GameCardViewModel card) => _detailPanelBuilder.UpdateDetailComponentRows(card);

    private void DetailCard_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => _detailPanelBuilder.DetailCard_PropertyChanged(sender, e);

    internal void BuildOverridesPanel(GameCardViewModel card) => _detailPanelBuilder.BuildOverridesPanel(card);

    private void OpenOverridesFlyout(GameCardViewModel card, FrameworkElement anchor)
        => _overridesFlyoutBuilder.OpenOverridesFlyout(card, anchor);

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

    /// <summary>Allows DetailPanelBuilder to trigger a pending reselect after overrides save.</summary>
    internal void RequestReselect(string? name)
    {
        _pendingReselect = name;
        DispatcherQueue.TryEnqueue(TryRestoreSelection);
    }

    // ── Dialog delegation ─────────────────────────────────────────────────────────

    private async Task<bool> ShowForeignDxgiConfirmDialogAsync(GameCardViewModel card, string dxgiPath)
        => await _dialogService.ShowForeignDxgiConfirmDialogAsync(card, dxgiPath);

    private async Task CheckForAppUpdateAsync()
        => await _dialogService.CheckForAppUpdateAsync();

    private async Task ShowUpdateDialogAsync(UpdateInfo updateInfo)
        => await _dialogService.ShowUpdateDialogAsync(updateInfo);

    private async Task DownloadAndInstallUpdateAsync(UpdateInfo updateInfo)
        => await _dialogService.DownloadAndInstallUpdateAsync(updateInfo);

    private async Task ShowPatchNotesIfNewVersionAsync()
        => await _dialogService.ShowPatchNotesIfNewVersionAsync();

    private async Task ShowPatchNotesDialogAsync()
        => await _dialogService.ShowPatchNotesDialogAsync();

    // ── Drag-and-drop delegation ────────────────────────────────────────────────

    private void Grid_DragOver(object sender, DragEventArgs e)
        => _dragDropHandler.Grid_DragOver(sender, e);

    private void Grid_Drop(object sender, DragEventArgs e)
        => _dragDropHandler.Grid_Drop(sender, e);

    // ── Helpers ───────────────────────────────────────────────────────────────────

    /// <summary>Looks up a SolidColorBrush from the merged theme resource dictionaries.</summary>
    private SolidColorBrush Brush(string key) =>
        (SolidColorBrush)Application.Current.Resources[key];

    /// <summary>
    /// Builds the effective screenshot save path for a game based on current settings.
    /// Returns null if no screenshot path is configured.
    /// </summary>
    internal string? BuildScreenshotSavePath(string gameName)
    {
        var basePath = ViewModel.Settings.ScreenshotPath;
        if (string.IsNullOrEmpty(basePath)) return null;
        if (!ViewModel.Settings.PerGameScreenshotFolders) return basePath;
        var sanitized = AuxInstallService.SanitizeDirectoryName(gameName);
        if (string.IsNullOrEmpty(sanitized)) return basePath;
        return basePath + @"\" + sanitized;
    }

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
        try
        {
            var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
            picker.FileTypeFilter.Add("*");
            var hwnd2 = WindowNative.GetWindowHandle(this);
            InitializeWithWindow.Initialize(picker, hwnd2);
            var folder = await picker.PickSingleFolderAsync();
            return folder?.Path;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.PickFolderAsync] Standard picker failed — {ex.Message}. Falling back to COM dialog.");
            // Fall back to COM dialog without a suggested path
            try
            {
                return await Task.Run(() =>
                {
                    var dialog = (NativeInterop.IFileOpenDialog)new NativeInterop.FileOpenDialogClass();
                    dialog.SetOptions(NativeInterop.FOS.FOS_PICKFOLDERS | NativeInterop.FOS.FOS_FORCEFILESYSTEM);
                    var hwnd = WindowNative.GetWindowHandle(this);
                    int hr = dialog.Show(hwnd);
                    if (hr != 0) return null;
                    dialog.GetResult(out NativeInterop.IShellItem result);
                    result.GetDisplayName(NativeInterop.SIGDN.SIGDN_FILESYSPATH, out string path);
                    return path;
                });
            }
            catch (Exception ex2)
            {
                CrashReporter.Log($"[MainWindow.PickFolderAsync] COM fallback also failed — {ex2.Message}");
                return null;
            }
        }
    }
}
