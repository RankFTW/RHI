using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;

namespace RenoDXdbEditor;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    // ── State ─────────────────────────────────────────────────────────────────

    private DbType _activeDb = DbType.NamedMods;
    private bool   _isUnrealMode;

    // Named-mods data
    private ObservableCollection<GameMod>      _allMods    = new();
    private ICollectionView?                   _modsView;

    // Unreal data
    private ObservableCollection<UnrealEntry>  _allUnreal  = new();
    private ICollectionView?                   _unrealView;

    private string? _currentFilePath;
    private bool    _isDirty;
    private string  _githubToken = "";

    // Sync results (cached so selector cards stay accurate after sync)
    private string? _namedModsRemoteContent;
    private string? _unrealRemoteContent;

    // ── Bindable properties ───────────────────────────────────────────────────

    private bool _hasFile;
    public bool HasFile
    {
        get => _hasFile;
        set
        {
            _hasFile = value;
            OnPropertyChanged(nameof(HasFile));
            OnPropertyChanged(nameof(CanCheckWiki));
        }
    }

    private bool _hasSelection;
    public bool HasSelection
    {
        get => _hasSelection;
        set { _hasSelection = value; OnPropertyChanged(nameof(HasSelection)); }
    }

    /// <summary>True when a DB is open that supports wiki checking.</summary>
    public bool CanCheckWiki => _hasFile;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // ── Init ──────────────────────────────────────────────────────────────────

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        LoadTokenFromDisk();
        // Defer auto-sync until after window is shown and controls are ready
        Loaded += (_, _) => _ = SyncBothAsync(isStartup: true);
    }

    // ── Sync logic ────────────────────────────────────────────────────────────

    private async Task SyncBothAsync(bool isStartup)
    {
        SetSyncStatus("Syncing from GitHub…", "#FFCC44");

        var namedTask  = DbSyncService.FetchAsync(DbType.NamedMods, _githubToken);
        var unrealTask = DbSyncService.FetchAsync(DbType.Unreal,    _githubToken);
        var unityTask  = DbSyncService.FetchAsync(DbType.Unity,     _githubToken);
        await Task.WhenAll(namedTask, unrealTask, unityTask);

        var named  = namedTask.Result;
        var unreal = unrealTask.Result;
        var unity  = unityTask.Result;

        _namedModsRemoteContent = named.Success  ? named.RemoteContent  : null;
        _unrealRemoteContent    = unreal.Success ? unreal.RemoteContent : null;

        if (named.Success)
        {
            if (!File.Exists(DbSyncService.LocalCachePath(DbType.NamedMods)) || !named.HasDiff)
                await DbSyncService.SaveLocalAsync(DbType.NamedMods, named.RemoteContent!);
        }
        if (unreal.Success)
        {
            if (!File.Exists(DbSyncService.LocalCachePath(DbType.Unreal)) || !unreal.HasDiff)
                await DbSyncService.SaveLocalAsync(DbType.Unreal, unreal.RemoteContent!);
        }
        if (unity.Success)
        {
            if (!File.Exists(DbSyncService.LocalCachePath(DbType.Unity)) || !unity.HasDiff)
                await DbSyncService.SaveLocalAsync(DbType.Unity, unity.RemoteContent!);
        }

        Dispatcher.Invoke(() => UpdateSelectorCards(named, unreal, unity));

        if (!isStartup)
        {
            if (named.Success  && named.HasDiff)  Dispatcher.Invoke(() => HandleDiff(DbType.NamedMods, named));
            if (unreal.Success && unreal.HasDiff) Dispatcher.Invoke(() => HandleDiff(DbType.Unreal,    unreal));
            if (unity.Success  && unity.HasDiff)  Dispatcher.Invoke(() => HandleDiff(DbType.Unity,     unity));
        }

        if (!named.Success && !unreal.Success && !unity.Success)
            SetSyncStatus($"Sync failed — {named.Error ?? unreal.Error ?? unity.Error}", "#EE5555");
        else if (!named.Success || !unreal.Success || !unity.Success)
            SetSyncStatus("Sync partially failed — using local copies where available", "#FFCC44");
        else
            SetSyncStatus($"Synced at {DateTime.Now:HH:mm:ss}", "#55CC77");
    }

    private void SetSyncStatus(string text, string hex)
    {
        Dispatcher.Invoke(() =>
        {
            SyncStatusLabel.Text      = text;
            SyncStatusLabel.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(hex));
        });
    }

    private void UpdateSelectorCards(SyncResult named, SyncResult unreal, SyncResult unity)
    {
        if (named.Success)
        {
            NamedModsStatusLabel.Text       = named.HasDiff ? "⚠ Remote differs from local — open to review" : "✓ Up to date";
            NamedModsStatusLabel.Foreground = named.HasDiff ? new SolidColorBrush(Colors.Orange) : new SolidColorBrush(Color.FromRgb(0x55, 0xCC, 0x77));
        }
        else
        {
            NamedModsStatusLabel.Text       = $"✗ Fetch failed — {named.Error}";
            NamedModsStatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(0xEE, 0x55, 0x55));
        }

        if (unreal.Success)
        {
            UnrealStatusLabel.Text       = unreal.HasDiff ? "⚠ Remote differs from local — open to review" : "✓ Up to date";
            UnrealStatusLabel.Foreground = unreal.HasDiff ? new SolidColorBrush(Colors.Orange) : new SolidColorBrush(Color.FromRgb(0x55, 0xCC, 0x77));
        }
        else
        {
            UnrealStatusLabel.Text       = $"✗ Fetch failed — {unreal.Error}";
            UnrealStatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(0xEE, 0x55, 0x55));
        }

        if (UnityStatusLabel != null)
        {
            if (unity.Success)
            {
                UnityStatusLabel.Text       = unity.HasDiff ? "⚠ Remote differs from local — open to review" : "✓ Up to date";
                UnityStatusLabel.Foreground = unity.HasDiff ? new SolidColorBrush(Colors.Orange) : new SolidColorBrush(Color.FromRgb(0x55, 0xCC, 0x77));
            }
            else
            {
                UnityStatusLabel.Text       = $"✗ Fetch failed — {unity.Error}";
                UnityStatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(0xEE, 0x55, 0x55));
            }
        }
    }

    private void HandleDiff(DbType db, SyncResult result)
    {
        if (result.RemoteContent == null) return;
        var fileName = Path.GetFileName(DbSyncService.LocalCachePath(db));
        var diff = new DiffWindow(fileName, result.LocalContent, result.RemoteContent)
        {
            Owner = this
        };
        diff.ShowDialog();
        if (diff.UserChoseOverwrite)
        {
            _ = DbSyncService.SaveLocalAsync(db, result.RemoteContent);
            // If this DB is currently open, reload it
            if (_currentFilePath != null &&
                Path.GetFileName(_currentFilePath).Equals(fileName, StringComparison.OrdinalIgnoreCase))
            {
                LoadFile(_currentFilePath);
                StatusBar.Text = "Reloaded from updated local cache.";
            }
        }
    }

    // ── Sync toolbar button ───────────────────────────────────────────────────

    private void Sync_Click(object sender, RoutedEventArgs e)
    {
        _ = SyncBothAsync(isStartup: false);
    }

    // ── Push ──────────────────────────────────────────────────────────────────

    private async void Push_Click(object sender, RoutedEventArgs e)
    {
        if (!HasFile || _currentFilePath == null) return;
        if (string.IsNullOrWhiteSpace(_githubToken))
        {
            MessageBox.Show("No GitHub token set.\n\nClick 🔑 Token to configure one.",
                "Token Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (_isDirty)
        {
            var save = MessageBox.Show("You have unsaved changes. Save before pushing?",
                "Unsaved Changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (save == MessageBoxResult.Cancel) return;
            if (save == MessageBoxResult.Yes) SaveToPath(_currentFilePath);
        }

        var dbName = Path.GetFileName(_currentFilePath);
        var msg = $"Update {dbName} via RenoDXdb-Editor";
        var content = await File.ReadAllTextAsync(_currentFilePath);

        StatusBar.Text = "Pushing to GitHub…";
        PushBtn.IsEnabled = false;
        try
        {
            var (success, error) = await DbSyncService.PushAsync(_activeDb, content, _githubToken, msg);
            StatusBar.Text = success
                ? $"✓ Pushed successfully at {DateTime.Now:HH:mm:ss}"
                : $"✗ Push failed: {error}";
            if (!success)
                MessageBox.Show($"Push failed:\n{error}", "Push Failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            PushBtn.IsEnabled = HasFile;
        }
    }

    // ── Token ─────────────────────────────────────────────────────────────────

    private void Token_Click(object sender, RoutedEventArgs e)
    {
        var win = new TokenWindow(_githubToken) { Owner = this };
        if (win.ShowDialog() == true)
        {
            _githubToken = win.Token;
            SaveTokenToDisk();
        }
    }

    private void LoadTokenFromDisk()
    {
        var path = TokenPath();
        if (File.Exists(path))
            _githubToken = File.ReadAllText(path).Trim();
    }

    private void SaveTokenToDisk()
    {
        File.WriteAllText(TokenPath(), _githubToken);
    }

    private static string TokenPath()
    {
        var dir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        return Path.Combine(dir, "github_token.txt");
    }

    // ── Selector ──────────────────────────────────────────────────────────────

    private void OpenNamedMods_Click(object sender, RoutedEventArgs e)
    {
        _activeDb = DbType.NamedMods;
        var local = DbSyncService.LocalCachePath(DbType.NamedMods);

        // If remote differed, show diff before opening
        if (_namedModsRemoteContent != null)
        {
            var localContent = File.Exists(local) ? File.ReadAllText(local) : null;
            static string? Norm(string? s) => s?.Replace("\r\n", "\n").TrimEnd();
            if (Norm(localContent) != Norm(_namedModsRemoteContent))
            {
                var diff = new DiffWindow(Path.GetFileName(local), localContent, _namedModsRemoteContent)
                { Owner = this };
                diff.ShowDialog();
                if (diff.UserChoseOverwrite)
                    _ = DbSyncService.SaveLocalAsync(DbType.NamedMods, _namedModsRemoteContent);
            }
        }

        if (File.Exists(local))
            LoadFile(local, DbType.NamedMods);
        else
            MessageBox.Show("No local cache available.\nCheck your internet connection and try Sync.",
                "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void OpenUnreal_Click(object sender, RoutedEventArgs e)
    {
        _activeDb = DbType.Unreal;
        var local = DbSyncService.LocalCachePath(DbType.Unreal);

        if (_unrealRemoteContent != null)
        {
            var localContent = File.Exists(local) ? File.ReadAllText(local) : null;
            var normR = _unrealRemoteContent.Replace("\r\n", "\n").TrimEnd();
            var normL = localContent?.Replace("\r\n", "\n").TrimEnd();
            if (normL != normR)
            {
                var diff = new DiffWindow(Path.GetFileName(local), localContent, _unrealRemoteContent)
                { Owner = this };
                diff.ShowDialog();
                if (diff.UserChoseOverwrite)
                    _ = DbSyncService.SaveLocalAsync(DbType.Unreal, _unrealRemoteContent);
            }
        }

        if (File.Exists(local))
            LoadFile(local, DbType.Unreal);
        else
            MessageBox.Show("No local cache available.\nCheck your internet connection and try Sync.",
                "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void OpenUnity_Click(object sender, RoutedEventArgs e)
    {
        _activeDb = DbType.Unity;
        var local = DbSyncService.LocalCachePath(DbType.Unity);

        if (File.Exists(local))
            LoadFile(local, DbType.Unity);
        else
            MessageBox.Show("No local cache found for Unity DB.\n\nUse Sync to download it, or use Open JSON to load a local file.",
                "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void BackToSelector_Click(object sender, RoutedEventArgs e)
    {
        if (_isDirty && !ConfirmDiscard()) return;
        ShowSelector();
    }

    private void ShowSelector()
    {
        SelectorPanel.Visibility = Visibility.Visible;
        EditorPanel.Visibility   = Visibility.Collapsed;
        HasFile = false;
        HasSelection = false;
        _currentFilePath = null;
        _isDirty = false;
        StatusBar.Text = "Select a database above to get started";
    }

    // ── File operations ───────────────────────────────────────────────────────

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (_isDirty && !ConfirmDiscard()) return;
        var dlg = new OpenFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            Title  = "Open RenoDXdb JSON",
        };
        if (dlg.ShowDialog() != true) return;

        // Infer DB type from filename
        var fn = Path.GetFileName(dlg.FileName);
        DbType db;
        if (fn.Contains("unity", StringComparison.OrdinalIgnoreCase))
            db = DbType.Unity;
        else if (fn.Contains("unreal", StringComparison.OrdinalIgnoreCase))
            db = DbType.Unreal;
        else
            db = DbType.NamedMods;
        LoadFile(dlg.FileName, db);
    }

    private void LoadFile(string path, DbType? dbOverride = null)
    {
        try
        {
            var json = File.ReadAllText(path);
            var db   = dbOverride ?? _activeDb;
            _activeDb = db;

            if (db == DbType.Unreal || db == DbType.Unity)
            {
                var entries = JsonSerializer.Deserialize<List<UnrealEntry>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? new List<UnrealEntry>();

                _allUnreal = new ObservableCollection<UnrealEntry>(
                    entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase));
                _unrealView = CollectionViewSource.GetDefaultView(_allUnreal);
                _unrealView.Filter = FilterEntry;
                GameList.ItemsSource = _unrealView;
                _isUnrealMode = true;
            }
            else
            {
                var mods = JsonSerializer.Deserialize<List<GameMod>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? new List<GameMod>();

                _allMods = new ObservableCollection<GameMod>(
                    mods.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase));
                _modsView = CollectionViewSource.GetDefaultView(_allMods);
                _modsView.Filter = FilterEntry;
                GameList.ItemsSource = _modsView;
                _isUnrealMode = false;
            }

            // Show the right fields
            NamedModsFields.Visibility = _isUnrealMode ? Visibility.Collapsed : Visibility.Visible;
            UnrealFields.Visibility    = _isUnrealMode ? Visibility.Visible   : Visibility.Collapsed;
            EditorTitle.Text = db == DbType.Unity ? "Unity Game Details"
                : _isUnrealMode ? "Unreal Game Details"
                : "Game Details";

            // Hide Method field for Unity — it has no Method
            UMethodLabel.Visibility = db == DbType.Unity ? Visibility.Collapsed : Visibility.Visible;
            UMethodCombo.Visibility  = db == DbType.Unity ? Visibility.Collapsed : Visibility.Visible;

            _currentFilePath = path;
            _isDirty         = false;
            HasFile          = true;   // also notifies CanCheckWiki
            OnPropertyChanged(nameof(CanCheckWiki));
            SelectorPanel.Visibility = Visibility.Collapsed;
            EditorPanel.Visibility   = Visibility.Visible;
            ClearEditor();
            UpdateStatusBar();
            UpdateCount();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open file:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveFile_Click(object sender, RoutedEventArgs e)
    {
        if (_currentFilePath == null) { SaveFileAs_Click(sender, e); return; }
        SaveToPath(_currentFilePath);
    }

    private void SaveFileAs_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter   = "JSON files (*.json)|*.json",
            Title    = "Save RenoDXdb JSON",
            FileName = Path.GetFileName(_currentFilePath) ?? "RenoDXdb.json",
        };
        if (dlg.ShowDialog() != true) return;
        _currentFilePath = dlg.FileName;
        SaveToPath(_currentFilePath);
    }

    private void SaveToPath(string path)
    {
        try
        {
            var opts = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            };
            string json;
            if (_isUnrealMode)
            {
                json = JsonSerializer.Serialize(_allUnreal.ToList(), opts);
                // Unity DB: serialize without the Method field using a dedicated model
                if (_activeDb == DbType.Unity)
                    json = JsonSerializer.Serialize(
                        _allUnreal.Select(e => new UnityEntry(e.Name, e.Status, e.Upgrades, e.Comments)).ToList(),
                        opts);
            }
            else
                json = JsonSerializer.Serialize(_allMods.ToList(), opts);

            File.WriteAllText(path, json);
            _isDirty = false;
            StatusBar.Text = $"Saved → {path}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Entry list operations ─────────────────────────────────────────────────

    private void NewEntry_Click(object sender, RoutedEventArgs e)
    {
        if (_isUnrealMode)
        {
            var entry = new UnrealEntry { Name = "New Game", Status = "WIP" };
            InsertUnrealAlpha(entry);
            GameList.SelectedItem = entry;
            GameList.ScrollIntoView(entry);
        }
        else
        {
            var mod = new GameMod { Name = "New Game", Status = "Done" };
            InsertModAlpha(mod);
            GameList.SelectedItem = mod;
            GameList.ScrollIntoView(mod);
        }
        UNameBox.Focus(); UNameBox.SelectAll();
        NameBox.Focus();  NameBox.SelectAll();
        _isDirty = true;
    }

    private void DeleteEntry_Click(object sender, RoutedEventArgs e)
    {
        if (_isUnrealMode)
        {
            if (GameList.SelectedItem is not UnrealEntry entry) return;
            if (MessageBox.Show($"Delete \"{entry.Name}\"?", "Confirm Delete",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            _allUnreal.Remove(entry);
        }
        else
        {
            if (GameList.SelectedItem is not GameMod mod) return;
            if (MessageBox.Show($"Delete \"{mod.Name}\"?", "Confirm Delete",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            _allMods.Remove(mod);
        }
        ClearEditor();
        HasSelection = false;
        _isDirty = true;
        UpdateCount();
    }

    private void InsertModAlpha(GameMod mod)
    {
        int i = 0;
        while (i < _allMods.Count &&
               string.Compare(_allMods[i].Name, mod.Name, StringComparison.OrdinalIgnoreCase) < 0)
            i++;
        _allMods.Insert(i, mod);
        UpdateCount();
    }

    private void InsertUnrealAlpha(UnrealEntry entry)
    {
        int i = 0;
        while (i < _allUnreal.Count &&
               string.Compare(_allUnreal[i].Name, entry.Name, StringComparison.OrdinalIgnoreCase) < 0)
            i++;
        _allUnreal.Insert(i, entry);
        UpdateCount();
    }

    // ── Selection & editor ────────────────────────────────────────────────────

    private void GameList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUnrealMode && GameList.SelectedItem is UnrealEntry ue)
        {
            HasSelection = true;
            PopulateUnrealEditor(ue);
        }
        else if (!_isUnrealMode && GameList.SelectedItem is GameMod mod)
        {
            HasSelection = true;
            PopulateModEditor(mod);
        }
        else
        {
            HasSelection = false;
            ClearEditor();
        }
    }

    private void PopulateModEditor(GameMod mod)
    {
        NameBox.Text          = mod.Name;
        StatusCombo.SelectedIndex = mod.Status == "WIP" ? 1 : 0;
        AuthorBox.Text        = mod.Author;
        SnapshotUrlBox.Text   = mod.SnapshotUrl  ?? "";
        SnapshotUrl32Box.Text = mod.SnapshotUrl32 ?? "";
        NexusUrlBox.Text      = mod.NexusUrl      ?? "";
        DiscordUrlBox.Text    = mod.DiscordUrl     ?? "";
        DiscussionUrlBox.Text = mod.DiscussionUrl  ?? "";
        NotesBox.Text         = mod.Notes          ?? "";
        StatusLabel.Text = "";
    }

    private void PopulateUnrealEditor(UnrealEntry entry)
    {
        UNameBox.Text = entry.Name;
        UStatusCombo.SelectedIndex = entry.Status == "Done" ? 0 : 1;

        // Method combo — match by Content or (none)
        UMethodCombo.SelectedIndex = entry.Method switch
        {
            "native"  => 1,
            "ini"     => 2,
            "upgrade" => 3,
            _         => 0,   // (none)
        };

        // Upgrades
        BuildUpgradeRows(entry.ParseUpgrades());

        UCommentsBox.Text = entry.Comments ?? "";
        StatusLabel.Text  = "";
    }

    private void ClearEditor()
    {
        // Named mods
        NameBox.Text          = "";
        StatusCombo.SelectedIndex = 0;
        AuthorBox.Text        = "";
        SnapshotUrlBox.Text   = "";
        SnapshotUrl32Box.Text = "";
        NexusUrlBox.Text      = "";
        DiscordUrlBox.Text    = "";
        DiscussionUrlBox.Text = "";
        NotesBox.Text         = "";
        // Unreal
        UNameBox.Text = "";
        UStatusCombo.SelectedIndex  = 0;
        UMethodCombo.SelectedIndex  = 0;
        UpgradesPanel.Children.Clear();
        UCommentsBox.Text = "";
        StatusLabel.Text  = "";
    }

    private void Field_Changed(object sender, RoutedEventArgs e) { /* applied on button */ }

    private void ApplyChanges_Click(object sender, RoutedEventArgs e)
    {
        if (_isUnrealMode)   ApplyUnrealChanges();
        else                 ApplyModChanges();
    }

    private void ApplyModChanges()
    {
        if (GameList.SelectedItem is not GameMod mod) return;

        var newName = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(newName))
        {
            ShowStatus("⚠ Game name is required.", isError: true);
            return;
        }

        bool nameChanged = !string.Equals(mod.Name, newName, StringComparison.Ordinal);
        mod.Name         = newName;
        mod.Status       = (StatusCombo.SelectedItem as ComboBoxItem)?.Content as string ?? "Done";
        mod.Author       = AuthorBox.Text.Trim();
        mod.SnapshotUrl  = NullIfEmpty(SnapshotUrlBox.Text);
        mod.SnapshotUrl32 = NullIfEmpty(SnapshotUrl32Box.Text);
        mod.NexusUrl     = NullIfEmpty(NexusUrlBox.Text);
        mod.DiscordUrl   = NullIfEmpty(DiscordUrlBox.Text);
        mod.DiscussionUrl = NullIfEmpty(DiscussionUrlBox.Text);
        mod.Notes        = NullIfEmpty(NotesBox.Text);

        if (nameChanged)
        {
            _allMods.Remove(mod);
            InsertModAlpha(mod);
            GameList.SelectedItem = mod;
            GameList.ScrollIntoView(mod);
        }

        _isDirty = true;
        ShowStatus("✓ Changes applied.");
        UpdateStatusBar();
    }

    private void ApplyUnrealChanges()
    {
        if (GameList.SelectedItem is not UnrealEntry entry) return;

        var newName = UNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(newName))
        {
            ShowStatus("⚠ Game name is required.", isError: true);
            return;
        }

        bool nameChanged = !string.Equals(entry.Name, newName, StringComparison.Ordinal);
        entry.Name   = newName;
        entry.Status = (UStatusCombo.SelectedItem as ComboBoxItem)?.Content as string ?? "WIP";

        // Method: ComboBoxItem tag or content
        var methodItem = UMethodCombo.SelectedItem as ComboBoxItem;
        var methodVal  = (methodItem?.Tag as string ?? methodItem?.Content as string ?? "").Trim();
        entry.Method   = string.IsNullOrEmpty(methodVal) ? null : methodVal;

        // Upgrades: collect rows
        entry.Upgrades = CollectUpgrades();

        entry.Comments = NullIfEmpty(UCommentsBox.Text);

        if (nameChanged)
        {
            _allUnreal.Remove(entry);
            InsertUnrealAlpha(entry);
            GameList.SelectedItem = entry;
            GameList.ScrollIntoView(entry);
        }

        _isDirty = true;
        ShowStatus("✓ Changes applied.");
        UpdateStatusBar();
    }

    // ── Upgrades two-column UI ────────────────────────────────────────────────

    private void BuildUpgradeRows(List<(string Format, string Size)> pairs)
    {
        UpgradesPanel.Children.Clear();

        if (pairs.Count == 0)
            AddUpgradeRowWithValues("", "");  // always show at least one blank row
        else
            foreach (var (fmt, sz) in pairs)
                AddUpgradeRowWithValues(fmt, sz);
    }

    private void AddUpgradeRow_Click(object sender, RoutedEventArgs e)
    {
        AddUpgradeRowWithValues("", "");
    }

    private void AddUpgradeRowWithValues(string format, string size)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

        // Format combo — use Unity keys when Unity DB is open
        var fmtCombo = new ComboBox { Margin = new Thickness(0) };
        fmtCombo.Items.Add(new ComboBoxItem { Content = "(none)", Tag = "" });
        var fmtValues = _activeDb == DbType.Unity ? UnrealEntry.UnityFormatValues : UnrealEntry.FormatValues;
        foreach (var v in fmtValues)
            fmtCombo.Items.Add(new ComboBoxItem { Content = v, Tag = v });
        SelectComboByTag(fmtCombo, format);
        Grid.SetColumn(fmtCombo, 0);

        // Size combo — use Unity values when Unity DB is open
        var sizeCombo = new ComboBox { Margin = new Thickness(0) };
        sizeCombo.Items.Add(new ComboBoxItem { Content = "(none)", Tag = "" });
        if (_activeDb == DbType.Unity)
        {
            foreach (var (label, val) in UnrealEntry.UnitySizeValuePairs)
                sizeCombo.Items.Add(new ComboBoxItem { Content = label, Tag = val });
        }
        else
        {
            foreach (var v in UnrealEntry.SizeValues)
                sizeCombo.Items.Add(new ComboBoxItem { Content = v, Tag = v });
        }
        SelectComboByTag(sizeCombo, size);
        Grid.SetColumn(sizeCombo, 2);

        // Remove button
        var removeBtn = new Button
        {
            Content    = "✕",
            Padding    = new Thickness(0),
            Width      = 28,
            Height     = 28,
            ToolTip    = "Remove this upgrade row",
            Style      = (Style)FindResource("DangerButton"),
        };
        removeBtn.Click += (_, _) => UpgradesPanel.Children.Remove(grid);
        Grid.SetColumn(removeBtn, 3);

        grid.Children.Add(fmtCombo);
        grid.Children.Add(sizeCombo);
        grid.Children.Add(removeBtn);
        UpgradesPanel.Children.Add(grid);
    }

    private static void SelectComboByTag(ComboBox combo, string tag)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase)
             || string.Equals(item.Content as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
        combo.SelectedIndex = 0; // (none)
    }

    private string? CollectUpgrades()
    {
        var pairs = new List<(string Format, string Size)>();
        foreach (Grid row in UpgradesPanel.Children.OfType<Grid>())
        {
            var cols = row.Children.OfType<ComboBox>().ToList();
            if (cols.Count < 2) continue;
            var fmt  = (cols[0].SelectedItem as ComboBoxItem)?.Tag  as string ?? "";
            var size = (cols[1].SelectedItem as ComboBoxItem)?.Tag  as string
                    ?? (cols[1].SelectedItem as ComboBoxItem)?.Content as string ?? "";
            pairs.Add((fmt, size));
        }
        return UnrealEntry.SerialiseUpgrades(pairs);
    }

    // ── Search ────────────────────────────────────────────────────────────────

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        (_isUnrealMode ? _unrealView : _modsView)?.Refresh();
        UpdateCount();
    }

    private bool FilterEntry(object obj)
    {
        var q = SearchBox.Text.Trim();
        if (string.IsNullOrEmpty(q)) return true;
        return obj switch
        {
            GameMod m    => m.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                         || m.Author.Contains(q, StringComparison.OrdinalIgnoreCase),
            UnrealEntry u => u.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                         || (u.Comments?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false),
            _            => false,
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string? NullIfEmpty(string s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private void ShowStatus(string msg, bool isError = false)
    {
        StatusLabel.Foreground = isError
            ? new SolidColorBrush(Colors.Tomato)
            : new SolidColorBrush(Color.FromRgb(0x55, 0xCC, 0x77));
        StatusLabel.Text = msg;
    }

    private void UpdateStatusBar()
    {
        if (_currentFilePath == null) { StatusBar.Text = "No file open"; return; }
        int count = _isUnrealMode ? _allUnreal.Count : _allMods.Count;
        StatusBar.Text = $"{_currentFilePath}  ({count} entries){(_isDirty ? "  •  Unsaved changes" : "")}";
    }

    private void UpdateCount()
    {
        var view    = _isUnrealMode ? _unrealView : _modsView;
        int visible = view?.Cast<object>().Count() ?? 0;
        int total   = _isUnrealMode ? _allUnreal.Count : _allMods.Count;
        CountLabel.Text = $"{visible} / {total}";
    }

    private bool ConfirmDiscard()
    {
        if (!_isDirty) return true;
        return MessageBox.Show("You have unsaved changes. Discard them?", "Unsaved Changes",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (_isDirty && !ConfirmDiscard()) e.Cancel = true;
        base.OnClosing(e);
    }

    // ── Wiki check ────────────────────────────────────────────────────────────

    private async void WikiCheck_Click(object sender, RoutedEventArgs e)
    {
        if (!HasFile) return;

        // Route to the correct wiki check based on which DB is open
        if (_isUnrealMode && _activeDb == DbType.Unity)
        {
            await WikiCheckUnityAsync();
            return;
        }
        if (_isUnrealMode)
        {
            await WikiCheckUeExtendedAsync();
            return;
        }

        WikiCheckBtn.IsEnabled = false;
        StatusBar.Text = "Fetching RenoDX wiki…";

        List<WikiMod> wikiMods;
        try
        {
            wikiMods = await WikiScrapeService.FetchNamedDoneModsAsync(_githubToken);
        }
        catch (Exception ex)
        {
            StatusBar.Text = $"Wiki fetch failed: {ex.Message}";
            WikiCheckBtn.IsEnabled = true;
            MessageBox.Show($"Failed to fetch the wiki:\n\n{ex.Message}",
                "Wiki Fetch Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        finally
        {
            WikiCheckBtn.IsEnabled = CanCheckWiki;
        }

        // Build a set of normalised names already in the DB
        var inDb = new HashSet<string>(
            _allMods.Select(m => NormaliseModName(m.Name)),
            StringComparer.Ordinal);

        // Keep only wiki mods whose normalised name isn't in the DB yet
        var newMods = wikiMods
            .Where(w => !inDb.Contains(NormaliseModName(w.Name)))
            .OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (newMods.Count == 0)
        {
            StatusBar.Text = $"Wiki check complete — no new mods found ({wikiMods.Count} checked).";
            MessageBox.Show(
                $"All {wikiMods.Count} completed wiki mods are already in the DB.\n\nNothing to review.",
                "Up to Date", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        StatusBar.Text = $"Found {newMods.Count} new mod(s) — opening review…";

        var review = new WikiReviewWindow(newMods) { Owner = this };
        review.ShowDialog();

        if (review.AcceptedMods.Count == 0)
        {
            StatusBar.Text = $"Wiki review closed — no mods accepted.";
            return;
        }

        foreach (var mod in review.AcceptedMods)
            InsertModAlpha(mod);

        _isDirty = true;
        UpdateStatusBar();
        StatusBar.Text = $"Added {review.AcceptedMods.Count} mod(s) from wiki. Save to persist.";
    }

    // ── Wiki check — UE-Extended ──────────────────────────────────────────────

    private async Task WikiCheckUeExtendedAsync()
    {
        WikiCheckBtn.IsEnabled = false;
        StatusBar.Text = "Fetching RenoDX wiki (UE-Extended section)…";

        List<WikiUeExtEntry> wikiEntries;
        try
        {
            wikiEntries = await WikiUeExtendedScrapeService.FetchUeExtendedDoneAsync(_githubToken);
        }
        catch (Exception ex)
        {
            StatusBar.Text = $"Wiki fetch failed: {ex.Message}";
            MessageBox.Show($"Failed to fetch the wiki:\n\n{ex.Message}",
                "Wiki Fetch Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        finally
        {
            WikiCheckBtn.IsEnabled = CanCheckWiki;
        }

        // Compare against the current Unreal DB by normalised name
        var inDb = new HashSet<string>(
            _allUnreal.Select(u => NormaliseModName(u.Name)),
            StringComparer.Ordinal);

        var newEntries = wikiEntries
            .Where(w => !inDb.Contains(NormaliseModName(w.Name)))
            .OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (newEntries.Count == 0)
        {
            StatusBar.Text = $"Wiki check complete — no new UE-Extended games found ({wikiEntries.Count} checked).";
            MessageBox.Show(
                $"All {wikiEntries.Count} completed wiki entries are already in the DB.\n\nNothing to review.",
                "Up to Date", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        StatusBar.Text = $"Found {newEntries.Count} new UE-Extended game(s) — opening review…";

        var review = new UeExtReviewWindow(newEntries) { Owner = this };
        review.ShowDialog();

        if (review.AcceptedEntries.Count == 0)
        {
            StatusBar.Text = "Wiki review closed — no entries accepted.";
            return;
        }

        foreach (var entry in review.AcceptedEntries)
            InsertUnrealAlpha(entry);

        _isDirty = true;
        UpdateStatusBar();
        StatusBar.Text = $"Added {review.AcceptedEntries.Count} UE-Extended game(s) from wiki. Save to persist.";
    }

    /// <summary>
    /// Normalises a mod name for duplicate detection:
    /// decodes HTML entities, strips trademark/copyright symbols, lowercases,
    /// removes all non-alphanumeric/non-space characters, collapses whitespace.
    /// Mirrors NormalizeName in the app.
    /// </summary>
    private static string NormaliseModName(string name)
    {
        // Decode HTML entities (handles &middot; &amp; &apos; etc.)
        var s = System.Web.HttpUtility.HtmlDecode(name);
        // Strip common trademark/copyright symbols
        s = s.Replace("™", "").Replace("®", "").Replace("©", "");
        // Lowercase and strip all non-alphanumeric (except spaces)
        s = System.Text.RegularExpressions.Regex.Replace(s.ToLowerInvariant(), @"[^\w\s]", "");
        // Collapse whitespace
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();
        return s;
    }

    // ── Wiki check — Unity ────────────────────────────────────────────────────

    private async Task WikiCheckUnityAsync()
    {
        WikiCheckBtn.IsEnabled = false;
        StatusBar.Text = "Fetching RenoDX wiki (Unity Engine section)…";

        List<WikiUnityEntry> wikiEntries;
        try
        {
            wikiEntries = await WikiUnityScrapeService.FetchUnityAllAsync(_githubToken);
        }
        catch (Exception ex)
        {
            StatusBar.Text = $"Wiki fetch failed: {ex.Message}";
            MessageBox.Show($"Failed to fetch the wiki:\n\n{ex.Message}",
                "Wiki Fetch Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        finally
        {
            WikiCheckBtn.IsEnabled = CanCheckWiki;
        }

        var inDb = new HashSet<string>(
            _allUnreal.Select(u => NormaliseModName(u.Name)),
            StringComparer.Ordinal);

        var newEntries = wikiEntries
            .Where(w => !inDb.Contains(NormaliseModName(w.Name)))
            .OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (newEntries.Count == 0)
        {
            StatusBar.Text = $"Wiki check complete — no new Unity games found ({wikiEntries.Count} checked).";
            MessageBox.Show(
                $"All {wikiEntries.Count} completed wiki entries are already in the DB.\n\nNothing to review.",
                "Up to Date", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        StatusBar.Text = $"Found {newEntries.Count} new Unity game(s) — opening review…";

        var review = new UnityReviewWindow(newEntries) { Owner = this };
        review.ShowDialog();

        if (review.AcceptedEntries.Count == 0)
        {
            StatusBar.Text = "Wiki review closed — no entries accepted.";
            return;
        }

        foreach (var entry in review.AcceptedEntries)
            InsertUnrealAlpha(entry);

        _isDirty = true;
        UpdateStatusBar();
        StatusBar.Text = $"Added {review.AcceptedEntries.Count} Unity game(s) from wiki. Save to persist.";
    }
}
