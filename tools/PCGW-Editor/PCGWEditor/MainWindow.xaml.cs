using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;

namespace PCGWEditor;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    // ── State ─────────────────────────────────────────────────────────────────

    private PcgwDataFile? _data;
    private string?       _currentFilePath;
    private bool          _isDirty;
    private string        _githubToken = "";
    private CancellationTokenSource? _fetchCts;

    // Games tab
    private ObservableCollection<PcgwEntryVm> _allGames  = new();
    private ICollectionView?                  _gamesView;
    private bool                              _gameEditorLoading;

    // Mappings tab
    private ObservableCollection<NameMappingVm> _allMappings = new();
    private bool                                 _mappingEditorLoading;

    // ── INotifyPropertyChanged ────────────────────────────────────────────────

    private bool _hasFile;
    public bool HasFile
    {
        get => _hasFile;
        set { _hasFile = value; OnPropertyChanged(nameof(HasFile)); RefreshToolbarState(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // ── Init ──────────────────────────────────────────────────────────────────

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        _githubToken = PcgwDataService.LoadToken();
        RefreshToolbarState();
        Loaded += (_, _) => _ = AutoSyncAsync();
    }

    private async Task AutoSyncAsync()
    {
        SetSyncStatus("Syncing from GitHub…", "#FFCC44");
        var result = await PcgwDataService.FetchAsync(_githubToken);
        if (result.Success)
        {
            if (!File.Exists(PcgwDataService.LocalPath) || !result.HasDiff)
                await PcgwDataService.SaveLocalAsync(result.RemoteContent!);

            SetSyncStatus(result.HasDiff
                ? "⚠ Remote differs from local"
                : $"✓ Synced ({_allGames.Count:N0} games)", result.HasDiff ? "#FFAA44" : "#55CC77");

            // Auto-load local file if nothing is open
            if (!HasFile && File.Exists(PcgwDataService.LocalPath))
                LoadFile(PcgwDataService.LocalPath);
        }
        else
        {
            SetSyncStatus($"Sync failed — {result.Error}", "#EE5555");
            if (File.Exists(PcgwDataService.LocalPath))
                LoadFile(PcgwDataService.LocalPath);
        }
    }

    // ── File operations ───────────────────────────────────────────────────────

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (_isDirty && !ConfirmDiscard()) return;
        var dlg = new OpenFileDialog { Filter = "JSON files (*.json)|*.json", Title = "Open pcgw_data.json" };
        if (dlg.ShowDialog() != true) return;
        LoadFile(dlg.FileName);
    }

    private void LoadFile(string path)
    {
        var data = PcgwDataService.Load(path);
        if (data == null)
        {
            MessageBox.Show("Failed to load file. It may be corrupt or an unexpected format.",
                "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        _data            = data;
        _currentFilePath = path;
        _isDirty         = false;
        HasFile          = true;

        // Populate games list
        _allGames = new ObservableCollection<PcgwEntryVm>(
            data.Games
                .Select(kv => PcgwEntryVm.FromRaw(kv.Key, kv.Value))
                .OrderBy(v => v.PageName, StringComparer.OrdinalIgnoreCase));

        _gamesView = CollectionViewSource.GetDefaultView(_allGames);
        _gamesView.Filter = FilterGame;
        GameList.ItemsSource = _gamesView;

        // Populate mappings list
        _allMappings = new ObservableCollection<NameMappingVm>(
            data.NameOverrides
                .Select(kv => new NameMappingVm { DetectedName = kv.Key, PcgwPageName = kv.Value })
                .OrderBy(m => m.DetectedName, StringComparer.OrdinalIgnoreCase));
        MappingList.ItemsSource = _allMappings;

        ClearGameEditor();
        ClearMappingEditor();
        UpdateCount();
        UpdateStatusBar();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_currentFilePath == null) { SaveAs_Click(sender, e); return; }
        SaveToPath(_currentFilePath);
    }

    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter   = "JSON files (*.json)|*.json",
            Title    = "Save pcgw_data.json",
            FileName = Path.GetFileName(_currentFilePath) ?? "pcgw_data.json",
        };
        if (dlg.ShowDialog() != true) return;
        _currentFilePath = dlg.FileName;
        SaveToPath(_currentFilePath);
    }

    private void SaveToPath(string path)
    {
        if (_data == null) return;
        try
        {
            FlushDataFromUi();
            PcgwDataService.Save(_data, path);
            _isDirty = false;
            StatusBar.Text = $"Saved → {path}";
            RefreshToolbarState();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Toolbar ───────────────────────────────────────────────────────────────

    private void Sync_Click(object sender, RoutedEventArgs e) => _ = SyncAndReloadAsync();

    private async Task SyncAndReloadAsync()
    {
        if (_isDirty && !ConfirmDiscard()) return;
        SetSyncStatus("Syncing…", "#FFCC44");
        var result = await PcgwDataService.FetchAsync(_githubToken);
        if (result.Success)
        {
            await PcgwDataService.SaveLocalAsync(result.RemoteContent!);
            LoadFile(PcgwDataService.LocalPath);
            SetSyncStatus($"✓ Synced at {DateTime.Now:HH:mm:ss}", "#55CC77");
        }
        else
        {
            SetSyncStatus($"✗ Sync failed — {result.Error}", "#EE5555");
            MessageBox.Show($"Sync failed:\n{result.Error}", "Sync Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void Push_Click(object sender, RoutedEventArgs e)
    {
        if (!HasFile || _data == null) return;
        if (string.IsNullOrWhiteSpace(_githubToken))
        {
            MessageBox.Show("No GitHub token set. Click 🔑 Token to configure one.",
                "Token Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (_isDirty)
        {
            var r = MessageBox.Show("You have unsaved changes. Save before pushing?",
                "Unsaved Changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) return;
            if (r == MessageBoxResult.Yes) SaveToPath(_currentFilePath!);
        }

        StatusBar.Text = "Pushing to GitHub…";
        PushBtn.IsEnabled = false;
        try
        {
            var content = File.ReadAllText(_currentFilePath!);
            var msg     = $"Update pcgw_data.json via PCGW-Editor ({_data.GameCount:N0} games)";
            var (success, error) = await PcgwDataService.PushAsync(content, _githubToken, msg);
            StatusBar.Text = success
                ? $"✓ Pushed at {DateTime.Now:HH:mm:ss}"
                : $"✗ Push failed: {error}";
            if (!success)
                MessageBox.Show($"Push failed:\n{error}", "Push Failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { PushBtn.IsEnabled = HasFile; }
    }

    private void Token_Click(object sender, RoutedEventArgs e)
    {
        var win = new TokenWindow(_githubToken) { Owner = this };
        if (win.ShowDialog() == true)
        {
            _githubToken = win.Token;
            PcgwDataService.SaveToken(_githubToken);
        }
    }

    // ── Games tab ─────────────────────────────────────────────────────────────

    private void GameList_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (GameList.SelectedItem is PcgwEntryVm vm)
        {
            GameEditorPanel.IsEnabled = true;
            PopulateGameEditor(vm);
        }
        else
        {
            ClearGameEditor();
        }
    }

    private void PopulateGameEditor(PcgwEntryVm vm)
    {
        _gameEditorLoading  = true;
        PageNameBox.Text    = vm.PageName;
        SteamAppIdBox.Text  = vm.SteamAppId > 0 ? vm.SteamAppId.ToString() : "";
        Dx9Check.IsChecked  = vm.Dx9;
        Dx10Check.IsChecked = vm.Dx10;
        Dx11Check.IsChecked = vm.Dx11;
        Dx12Check.IsChecked = vm.Dx12;
        VulkanCheck.IsChecked = vm.Vulkan;
        OpenGLCheck.IsChecked = vm.OpenGL;
        ConfigPathBox.Text     = vm.ConfigPath     ?? "";
        ConfigPathXboxBox.Text = vm.ConfigPathXbox ?? "";

        WikiUrlBlock.Text = $"https://www.pcgamingwiki.com/wiki/{vm.PageName.Replace(" ", "_")}";
        GameStatusLabel.Text = "";
        _gameEditorLoading = false;
    }

    private void ClearGameEditor()
    {
        _gameEditorLoading       = true;
        GameEditorPanel.IsEnabled = false;
        PageNameBox.Text         = "";
        SteamAppIdBox.Text       = "";
        Dx9Check.IsChecked = Dx10Check.IsChecked = Dx11Check.IsChecked =
        Dx12Check.IsChecked = VulkanCheck.IsChecked = OpenGLCheck.IsChecked = false;
        ConfigPathBox.Text     = "";
        ConfigPathXboxBox.Text = "";
        WikiUrlBlock.Text      = "";
        GameStatusLabel.Text   = "";
        _gameEditorLoading     = false;
    }

    private void GameField_Changed(object sender, RoutedEventArgs e)
    {
        // no-op — only applied on button click
    }

    private void ApplyGame_Click(object sender, RoutedEventArgs e)
    {
        if (GameList.SelectedItem is not PcgwEntryVm vm || _data == null) return;

        if (!int.TryParse(SteamAppIdBox.Text.Trim(), out int appId) &&
            !string.IsNullOrWhiteSpace(SteamAppIdBox.Text.Trim()))
        {
            GameStatusLabel.Foreground = new SolidColorBrush(Colors.Tomato);
            GameStatusLabel.Text = "⚠ Steam AppID must be a number.";
            return;
        }

        vm.SteamAppId    = appId;
        vm.Dx9           = Dx9Check.IsChecked  == true;
        vm.Dx10          = Dx10Check.IsChecked == true;
        vm.Dx11          = Dx11Check.IsChecked == true;
        vm.Dx12          = Dx12Check.IsChecked == true;
        vm.Vulkan        = VulkanCheck.IsChecked == true;
        vm.OpenGL        = OpenGLCheck.IsChecked == true;
        vm.ConfigPath    = NullIfEmpty(ConfigPathBox.Text);
        vm.ConfigPathXbox = NullIfEmpty(ConfigPathXboxBox.Text);

        // Sync back to _data
        _data.Games[vm.PageName] = vm.ToRaw();
        MarkDirty();

        GameStatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0xCC, 0x77));
        GameStatusLabel.Text = "✓ Changes applied.";

        // Refresh the list item display name
        _gamesView?.Refresh();
    }

    private void WikiUrl_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var url = WikiUrlBlock.Text;
        if (!string.IsNullOrEmpty(url))
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    // ── Mappings tab ──────────────────────────────────────────────────────────

    private void MappingList_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (MappingList.SelectedItem is NameMappingVm vm)
        {
            MappingEditorPanel.IsEnabled = true;
            DeleteMappingBtn.IsEnabled   = true;
            PopulateMappingEditor(vm);
        }
        else
        {
            ClearMappingEditor();
            DeleteMappingBtn.IsEnabled = false;
        }
    }

    private void PopulateMappingEditor(NameMappingVm vm)
    {
        _mappingEditorLoading   = true;
        DetectedNameBox.Text    = vm.DetectedName;
        PcgwPageNameBox.Text    = vm.PcgwPageName;
        MappingUrlBlock.Text    = vm.DerivedUrl;
        MappingStatusLabel.Text = "";
        _mappingEditorLoading   = false;
    }

    private void ClearMappingEditor()
    {
        _mappingEditorLoading        = true;
        MappingEditorPanel.IsEnabled = false;
        DetectedNameBox.Text         = "";
        PcgwPageNameBox.Text         = "";
        MappingUrlBlock.Text         = "";
        MappingStatusLabel.Text      = "";
        _mappingEditorLoading        = false;
    }

    private void MappingField_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_mappingEditorLoading) return;
        // Live-update derived URL preview
        var page = PcgwPageNameBox.Text.Trim();
        MappingUrlBlock.Text = string.IsNullOrWhiteSpace(page)
            ? ""
            : $"https://www.pcgamingwiki.com/wiki/{page.Replace(" ", "_")}";
    }

    private void AddMapping_Click(object sender, RoutedEventArgs e)
    {
        var vm = new NameMappingVm { DetectedName = "New Mapping", PcgwPageName = "" };
        InsertMappingAlpha(vm);
        MappingList.SelectedItem = vm;
        MappingList.ScrollIntoView(vm);
        DetectedNameBox.Focus();
        DetectedNameBox.SelectAll();
        MarkDirty();
    }

    private void DeleteMapping_Click(object sender, RoutedEventArgs e)
    {
        if (MappingList.SelectedItem is not NameMappingVm vm) return;
        if (MessageBox.Show($"Delete mapping for \"{vm.DetectedName}\"?", "Confirm Delete",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        _allMappings.Remove(vm);
        _data?.NameOverrides.Remove(vm.DetectedName);
        ClearMappingEditor();
        DeleteMappingBtn.IsEnabled = false;
        MarkDirty();
    }

    private void ApplyMapping_Click(object sender, RoutedEventArgs e)
    {
        if (MappingList.SelectedItem is not NameMappingVm vm || _data == null) return;

        var detectedName = DetectedNameBox.Text.Trim();
        var pcgwPage     = PcgwPageNameBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(detectedName) || string.IsNullOrWhiteSpace(pcgwPage))
        {
            MappingStatusLabel.Foreground = new SolidColorBrush(Colors.Tomato);
            MappingStatusLabel.Text = "⚠ Both fields are required.";
            return;
        }

        // Remove old key if name changed
        if (!string.Equals(vm.DetectedName, detectedName, StringComparison.Ordinal))
            _data.NameOverrides.Remove(vm.DetectedName);

        vm.DetectedName = detectedName;
        vm.PcgwPageName = pcgwPage;
        _data.NameOverrides[detectedName] = pcgwPage;

        // Re-sort
        var sorted = _allMappings
            .OrderBy(m => m.DetectedName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _allMappings.Clear();
        foreach (var m in sorted) _allMappings.Add(m);
        MappingList.SelectedItem = vm;

        MarkDirty();
        MappingStatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0xCC, 0x77));
        MappingStatusLabel.Text = "✓ Mapping saved.";
    }

    private void MappingUrl_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var url = MappingUrlBlock.Text;
        if (!string.IsNullOrEmpty(url))
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void InsertMappingAlpha(NameMappingVm vm)
    {
        int i = 0;
        while (i < _allMappings.Count &&
               string.Compare(_allMappings[i].DetectedName, vm.DetectedName,
                              StringComparison.OrdinalIgnoreCase) < 0)
            i++;
        _allMappings.Insert(i, vm);
    }

    // ── Search / filter ───────────────────────────────────────────────────────

    private void Search_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _gamesView?.Refresh();
        UpdateCount();
    }

    private bool FilterGame(object obj)
    {
        var q = SearchBox.Text.Trim();
        if (string.IsNullOrEmpty(q)) return true;
        if (obj is not PcgwEntryVm vm) return false;
        return vm.PageName.Contains(q, StringComparison.OrdinalIgnoreCase)
            || (vm.SteamAppId > 0 && vm.SteamAppId.ToString().Contains(q));
    }

    // ── Tab switch ────────────────────────────────────────────────────────────

    private void Tabs_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // Reset search when switching tabs so the list shows all entries
        // (search only applies to Games tab)
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void FlushDataFromUi()
    {
        if (_data == null) return;

        // Flush all game VMs → _data.Games
        _data.Games.Clear();
        foreach (var vm in _allGames)
            _data.Games[vm.PageName] = vm.ToRaw();

        // Flush all mapping VMs → _data.NameOverrides
        _data.NameOverrides.Clear();
        foreach (var vm in _allMappings)
            if (!string.IsNullOrWhiteSpace(vm.DetectedName) && !string.IsNullOrWhiteSpace(vm.PcgwPageName))
                _data.NameOverrides[vm.DetectedName] = vm.PcgwPageName;
    }

    private void MarkDirty()
    {
        _isDirty = true;
        UpdateStatusBar();
        RefreshToolbarState();
    }

    private void RefreshToolbarState()
    {
        SaveBtn.IsEnabled   = HasFile;
        SaveAsBtn.IsEnabled = HasFile;
        PushBtn.IsEnabled   = HasFile;
    }

    private void UpdateStatusBar()
    {
        if (_currentFilePath == null) { StatusBar.Text = "No file open."; return; }
        int games    = _allGames.Count;
        int mappings = _allMappings.Count;
        StatusBar.Text = $"{_currentFilePath}  ({games:N0} games, {mappings} name mappings)" +
                         (_isDirty ? "  •  Unsaved changes" : "");
    }

    private void UpdateCount()
    {
        var visible = _gamesView?.Cast<object>().Count() ?? 0;
        CountLabel.Text = $"{visible:N0} / {_allGames.Count:N0}";
    }

    private void SetSyncStatus(string text, string hex)
    {
        Dispatcher.Invoke(() =>
        {
            SyncStatusLabel.Text       = text;
            SyncStatusLabel.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(hex));
        });
    }

    private bool ConfirmDiscard()
    {
        if (!_isDirty) return true;
        return MessageBox.Show("You have unsaved changes. Discard them?", "Unsaved Changes",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private static string? NullIfEmpty(string s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_isDirty && !ConfirmDiscard()) e.Cancel = true;
        base.OnClosing(e);
    }

    // ── PCGW fetch ────────────────────────────────────────────────────────────

    private void PcgwCreds_Click(object sender, RoutedEventArgs e)
    {
        var (u, p) = PcgwFetchService.LoadCredentials();
        var win = new PcgwCredentialsWindow(u, p) { Owner = this };
        if (win.ShowDialog() == true)
            PcgwFetchService.SaveCredentials(win.Username, win.Password);
    }

    private void FetchUpdates_Click(object sender, RoutedEventArgs e) => _ = RunFetchAsync(fullRefresh: false);
    private void FetchAll_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            "Full Refresh fetches all 55,000+ games from PCGW and takes ~5–8 minutes.\n\nProceed?",
            "Full Refresh", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes)
            _ = RunFetchAsync(fullRefresh: true);
    }

    private void CancelFetch_Click(object sender, RoutedEventArgs e)
    {
        _fetchCts?.Cancel();
    }

    private async Task RunFetchAsync(bool fullRefresh)
    {
        // Check credentials
        var (username, password) = PcgwFetchService.LoadCredentials();
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            MessageBox.Show(
                "No PCGW bot credentials set.\nClick 🔑 PCGW to configure them.",
                "Credentials Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Ensure a file is open (load local cache if needed)
        if (!HasFile)
        {
            if (System.IO.File.Exists(PcgwDataService.LocalPath))
                LoadFile(PcgwDataService.LocalPath);
            else
            {
                MessageBox.Show(
                    "No pcgw_data.json loaded. Click Sync first to download the current file.",
                    "No File", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        // Disable buttons, show cancel
        SetFetchUiActive(true);
        _fetchCts = new CancellationTokenSource();
        var svc   = new PcgwFetchService(_fetchCts.Token);

        var progress = new Progress<FetchProgress>(fp =>
            Dispatcher.Invoke(() => StatusBar.Text = fp.Message));

        FetchResult result;
        try
        {
            if (fullRefresh)
            {
                result = await Task.Run(() => svc.FetchAllAsync(
                    username, password, _data!.Games, progress));
            }
            else
            {
                var since = _data!.Generated;
                if (string.IsNullOrEmpty(since))
                {
                    MessageBox.Show(
                        "The current file has no 'generated' timestamp — cannot determine what's new.\nRun a Full Refresh instead.",
                        "No Timestamp", MessageBoxButton.OK, MessageBoxImage.Warning);
                    SetFetchUiActive(false);
                    return;
                }
                result = await Task.Run(() => svc.FetchUpdatesAsync(
                    username, password, _data!.Games, since, progress));
            }
        }
        finally
        {
            SetFetchUiActive(false);
        }

        if (!result.Success)
        {
            if (result.Error != "Cancelled")
                MessageBox.Show($"Fetch failed:\n{result.Error}", "Fetch Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            StatusBar.Text = $"Fetch {(result.Error == "Cancelled" ? "cancelled" : "failed")}.";
            return;
        }

        // Update timestamp
        _data!.Generated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        // Reload the view models from the updated _data.Games
        _allGames = new System.Collections.ObjectModel.ObservableCollection<PcgwEntryVm>(
            _data.Games
                .Select(kv => PcgwEntryVm.FromRaw(kv.Key, kv.Value))
                .OrderBy(v => v.PageName, StringComparer.OrdinalIgnoreCase));
        _gamesView = CollectionViewSource.GetDefaultView(_allGames);
        _gamesView.Filter = FilterGame;
        GameList.ItemsSource = _gamesView;

        MarkDirty();
        UpdateCount();

        var summary = fullRefresh
            ? $"Full refresh complete — {result.Total:N0} games."
            : $"Fetch updates complete — {result.Added} new, {result.Updated} updated ({result.Total:N0} total).";
        StatusBar.Text = summary;

        // Auto-save to local path
        SaveToPath(_currentFilePath ?? PcgwDataService.LocalPath);
    }

    private void SetFetchUiActive(bool active)
    {
        FetchUpdatesBtn.IsEnabled = !active;
        FetchAllBtn.IsEnabled     = !active;
        SyncBtn.IsEnabled         = !active;
        PushBtn.IsEnabled         = !active && HasFile;
        CancelFetchBtn.Visibility = active
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
    }
}
