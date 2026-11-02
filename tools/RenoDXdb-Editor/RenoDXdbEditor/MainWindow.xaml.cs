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

    // True while the editor is being filled from an entry (field events are not edits)
    private bool    _populating;
    // The entry open in the editor has edits not yet written back to it
    private bool    _editorEdited;

    // Sync results (cached so selector cards stay accurate after sync)
    private string? _namedModsRemoteContent;
    private string? _unrealRemoteContent;

    // Baseline content at load time — used as the "before" side of the push diff
    private string? _fileBaselineContent;

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
        SetSyncStatus("Syncing from GitHub…", RhiTools.Theme.Ui.CautionHex);

        var namedTask  = DbSyncService.FetchAsync(DbType.NamedMods, _githubToken);
        var unrealTask = DbSyncService.FetchAsync(DbType.Unreal,    _githubToken);
        var unityTask  = DbSyncService.FetchAsync(DbType.Unity,     _githubToken);
        var sourceTask = DbSyncService.FetchAsync(DbType.Source,    _githubToken);
        await Task.WhenAll(namedTask, unrealTask, unityTask, sourceTask);

        var named  = namedTask.Result;
        var unreal = unrealTask.Result;
        var unity  = unityTask.Result;
        var source = sourceTask.Result;

        _namedModsRemoteContent = named.Success  ? named.RemoteContent  : null;
        _unrealRemoteContent    = unreal.Success ? unreal.RemoteContent : null;

        if (named.Success)
        {
            await DbSyncService.SaveLocalAsync(DbType.NamedMods, named.RemoteContent!);
        }
        if (unreal.Success)
        {
            await DbSyncService.SaveLocalAsync(DbType.Unreal, unreal.RemoteContent!);
        }
        if (unity.Success)
        {
            await DbSyncService.SaveLocalAsync(DbType.Unity, unity.RemoteContent!);
        }
        if (source.Success)
        {
            await DbSyncService.SaveLocalAsync(DbType.Source, source.RemoteContent!);
        }

        Dispatcher.Invoke(() => UpdateSelectorCards(named, unreal, unity, source));

        if (!isStartup)
        {
            if (named.Success  && named.HasDiff)  Dispatcher.Invoke(() => HandleDiff(DbType.NamedMods, named));
            if (unreal.Success && unreal.HasDiff) Dispatcher.Invoke(() => HandleDiff(DbType.Unreal,    unreal));
            if (unity.Success  && unity.HasDiff)  Dispatcher.Invoke(() => HandleDiff(DbType.Unity,     unity));
            if (source.Success && source.HasDiff) Dispatcher.Invoke(() => HandleDiff(DbType.Source,    source));
        }

        if (!named.Success && !unreal.Success && !unity.Success)
            SetSyncStatus($"Sync failed — {named.Error ?? unreal.Error ?? unity.Error}", RhiTools.Theme.Ui.CriticalHex);
        else if (!named.Success || !unreal.Success || !unity.Success || (!source.Success && !IsNotFound(source)))
            SetSyncStatus("Sync partially failed — using local copies where available", RhiTools.Theme.Ui.CautionHex);
        else
            SetSyncStatus($"Synced at {DateTime.Now:HH:mm:ss}", RhiTools.Theme.Ui.SuccessHex);
    }

    /// <summary>True when the fetch failed only because the file isn't in rhi-repo yet.</summary>
    private static bool IsNotFound(SyncResult r) =>
        !r.Success && (r.Error?.Contains("404") ?? false);

    /// <summary>Serialises the open Unreal/Unity/Source list in that DB's own shape.</summary>
    private string SerializeEngineDb(JsonSerializerOptions opts) => _activeDb switch
    {
        DbType.Unity  => JsonSerializer.Serialize(
            _allUnreal.Select(e => new UnityEntry(e.Name, e.Status, e.Upgrades, e.Comments)).ToList(), opts),
        DbType.Source => JsonSerializer.Serialize(
            _allUnreal.Select(e => new SourceEntry(e.Name, e.Status, e.Bitness, e.Comments)).ToList(), opts),
        _             => JsonSerializer.Serialize(_allUnreal.ToList(), opts),
    };

    private void SetSyncStatus(string text, string hex)
    {
        Dispatcher.Invoke(() =>
        {
            SyncStatusLabel.Text      = text;
            SyncStatusLabel.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(hex));
        });
    }

    private void UpdateSelectorCards(SyncResult named, SyncResult unreal, SyncResult unity, SyncResult source)
    {
        if (named.Success)
        {
            NamedModsStatusLabel.Text       = named.HasDiff ? "⚠ Remote differs from local — open to review" : "✓ Up to date";
            NamedModsStatusLabel.Foreground = named.HasDiff ? RhiTools.Theme.Ui.Caution : RhiTools.Theme.Ui.Success;
        }
        else
        {
            NamedModsStatusLabel.Text       = $"✗ Fetch failed — {named.Error}";
            NamedModsStatusLabel.Foreground = RhiTools.Theme.Ui.Critical;
        }

        if (unreal.Success)
        {
            UnrealStatusLabel.Text       = unreal.HasDiff ? "⚠ Remote differs from local — open to review" : "✓ Up to date";
            UnrealStatusLabel.Foreground = unreal.HasDiff ? RhiTools.Theme.Ui.Caution : RhiTools.Theme.Ui.Success;
        }
        else
        {
            UnrealStatusLabel.Text       = $"✗ Fetch failed — {unreal.Error}";
            UnrealStatusLabel.Foreground = RhiTools.Theme.Ui.Critical;
        }

        if (UnityStatusLabel != null)
        {
            if (unity.Success)
            {
                UnityStatusLabel.Text       = unity.HasDiff ? "⚠ Remote differs from local — open to review" : "✓ Up to date";
                UnityStatusLabel.Foreground = unity.HasDiff ? RhiTools.Theme.Ui.Caution : RhiTools.Theme.Ui.Success;
            }
            else
            {
                UnityStatusLabel.Text       = $"✗ Fetch failed — {unity.Error}";
                UnityStatusLabel.Foreground = RhiTools.Theme.Ui.Critical;
            }
        }

        if (source.Success)
        {
            SourceStatusLabel.Text       = source.HasDiff ? "⚠ Remote differs from local — open to review" : "✓ Up to date";
            SourceStatusLabel.Foreground = source.HasDiff ? RhiTools.Theme.Ui.Caution : RhiTools.Theme.Ui.Success;
        }
        else if (IsNotFound(source))
        {
            SourceStatusLabel.Text       = "Not on GitHub yet — open to create, then Push";
            SourceStatusLabel.Foreground = RhiTools.Theme.Ui.Caution;
        }
        else
        {
            SourceStatusLabel.Text       = $"✗ Fetch failed — {source.Error}";
            SourceStatusLabel.Foreground = RhiTools.Theme.Ui.Critical;
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

        // Auto-apply any pending editor changes before pushing
        if (HasSelection)
        {
            if (_isUnrealMode) ApplyUnrealChanges();
            else               ApplyModChanges();
        }

        // Save to disk first so the file is up to date
        if (_isDirty)
            SaveToPath(_currentFilePath);

        // Build in-memory JSON of what we're about to push
        var opts = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        string newContent;
        if (_isUnrealMode)
        {
            newContent = SerializeEngineDb(opts);
        }
        else
            newContent = JsonSerializer.Serialize(_allMods.ToList(), opts);

        // Use the file content as it was when loaded as the diff baseline
        string oldContent = _fileBaselineContent ?? "";

        // Show preview diff — user must confirm before we push
        var dbName = Path.GetFileName(_currentFilePath);
        var preview = new DiffWindow(dbName, oldContent, newContent, DiffWindow.DiffMode.PreviewPush)
        { Owner = this };
        preview.ShowDialog();
        if (!preview.UserChoseOverwrite) return;

        var msg = $"Update {dbName} via RenoDXdb-Editor";

        StatusBar.Text = "Pushing to GitHub…";
        PushBtn.IsEnabled = false;
        try
        {
            var (success, error) = await DbSyncService.PushAsync(_activeDb, newContent, _githubToken, msg);
            StatusBar.Text = success
                ? $"✓ Pushed successfully at {DateTime.Now:HH:mm:ss}"
                : $"✗ Push failed: {error}";
            if (success)
            {
                // Update baseline and cached remote so future diffs/selector cards are correct
                _fileBaselineContent = newContent;
                if (_activeDb == DbType.NamedMods) _namedModsRemoteContent = newContent;
                else if (_activeDb == DbType.Unreal) _unrealRemoteContent = newContent;
            }
            else
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
        if (_isDirty && !ConfirmDiscard()) return;
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
        if (_isDirty && !ConfirmDiscard()) return;
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
        if (_isDirty && !ConfirmDiscard()) return;
        _activeDb = DbType.Unity;
        var local = DbSyncService.LocalCachePath(DbType.Unity);

        if (File.Exists(local))
            LoadFile(local, DbType.Unity);
        else
            MessageBox.Show("No local cache found for Unity DB.\n\nUse Sync to download it, or use Open JSON to load a local file.",
                "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void OpenSource_Click(object sender, RoutedEventArgs e)
    {
        if (_isDirty && !ConfirmDiscard()) return;
        _activeDb = DbType.Source;
        var local = DbSyncService.LocalCachePath(DbType.Source);

        // The Source DB is new: start an empty one locally; the first Push creates it in rhi-repo
        if (!File.Exists(local))
        {
            try { File.WriteAllText(local, "[]"); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show($"Couldn't create {Path.GetFileName(local)}:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }
        LoadFile(local, DbType.Source);
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
        else if (fn.Contains("source", StringComparison.OrdinalIgnoreCase))
            db = DbType.Source;
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
            _fileBaselineContent = json; // snapshot for push diff

            if (db == DbType.Unreal || db == DbType.Unity || db == DbType.Source)
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
                : db == DbType.Source ? "Source Game Details"
                : _isUnrealMode ? "Unreal Game Details"
                : "Game Details";

            // Method is Unreal-only; Upgrades are Unreal/Unity; Bitness is Source-only
            bool showMethod = db == DbType.Unreal;
            bool isSource   = db == DbType.Source;
            UMethodLabel.Visibility    = showMethod ? Visibility.Visible : Visibility.Collapsed;
            UMethodCombo.Visibility    = showMethod ? Visibility.Visible : Visibility.Collapsed;
            UpgradesSection.Visibility = isSource ? Visibility.Collapsed : Visibility.Visible;
            UBitnessLabel.Visibility   = isSource ? Visibility.Visible : Visibility.Collapsed;
            UBitnessCombo.Visibility   = isSource ? Visibility.Visible : Visibility.Collapsed;

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
        // Auto-apply any pending editor changes before saving
        if (HasSelection)
        {
            if (_isUnrealMode) ApplyUnrealChanges();
            else               ApplyModChanges();
        }
        if (_currentFilePath == null) { SaveFileAs_Click(sender, e); return; }
        SaveToPath(_currentFilePath);
    }

    private void SaveFileAs_Click(object sender, RoutedEventArgs e)
    {
        // Auto-apply any pending editor changes before saving
        if (HasSelection)
        {
            if (_isUnrealMode) ApplyUnrealChanges();
            else               ApplyModChanges();
        }
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
                json = SerializeEngineDb(opts);
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
            _editorEdited = false;   // don't write the editor into the entry being deleted
            _allUnreal.Remove(entry);
        }
        else
        {
            if (GameList.SelectedItem is not GameMod mod) return;
            if (MessageBox.Show($"Delete \"{mod.Name}\"?", "Confirm Delete",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            _editorEdited = false;
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
        // Keep edits made to the entry we're leaving (they used to be applied only on Save)
        if (_editorEdited && e.RemovedItems.Count > 0)
            CommitEditsTo(e.RemovedItems[0]);
        _editorEdited = false;

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

    /// <summary>
    /// Writes the editor fields into an entry the user just moved away from.
    /// Skips entries no longer in the open DB (deleted, or a different DB was opened).
    /// </summary>
    private void CommitEditsTo(object? item)
    {
        if (_isUnrealMode && item is UnrealEntry ue && _allUnreal.Contains(ue))
        {
            if (WriteEditorToUnreal(ue))
                Dispatcher.BeginInvoke(() =>
                {
                    if (!ReferenceEquals(GameList.SelectedItem, ue) && _allUnreal.Remove(ue))
                        InsertUnrealAlpha(ue);
                });
        }
        else if (!_isUnrealMode && item is GameMod mod && _allMods.Contains(mod))
        {
            if (WriteEditorToMod(mod))
                Dispatcher.BeginInvoke(() =>
                {
                    if (!ReferenceEquals(GameList.SelectedItem, mod) && _allMods.Remove(mod))
                        InsertModAlpha(mod);
                });
        }
        else return;

        _isDirty = true;
        UpdateStatusBar();
    }

    private void PopulateModEditor(GameMod mod)
    {
        _populating = true;
        try { FillModEditor(mod); }
        finally { _populating = false; }
        _editorEdited = false;
    }

    private void FillModEditor(GameMod mod)
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
        _populating = true;
        try { FillUnrealEditor(entry); }
        finally { _populating = false; }
        _editorEdited = false;
    }

    private void FillUnrealEditor(UnrealEntry entry)
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

        SelectBitness(entry.Bitness);

        UCommentsBox.Text = entry.Comments ?? "";
        StatusLabel.Text  = "";
    }

    private void SelectBitness(string? bitness)
    {
        foreach (ComboBoxItem item in UBitnessCombo.Items)
        {
            if (string.Equals(item.Tag as string, bitness ?? "", StringComparison.OrdinalIgnoreCase))
            {
                UBitnessCombo.SelectedItem = item;
                return;
            }
        }
        UBitnessCombo.SelectedIndex = 0;
    }

    private void ClearEditor()
    {
        _populating = true;
        try { ClearEditorFields(); }
        finally { _populating = false; }
        _editorEdited = false;
    }

    private void ClearEditorFields()
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
        UBitnessCombo.SelectedIndex = 0;
        UCommentsBox.Text = "";
        StatusLabel.Text  = "";
    }

    private void Field_Changed(object sender, RoutedEventArgs e)
    {
        if (_populating || !HasSelection) return;
        _editorEdited = true;
        _isDirty = true;
        UpdateStatusBar();
    }

    private void ApplyModChanges()
    {
        if (GameList.SelectedItem is not GameMod mod) return;

        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            ShowStatus("⚠ Game name is required.", isError: true);
            return;
        }

        _editorEdited = false;
        bool nameChanged = WriteEditorToMod(mod);

        if (nameChanged)
        {
            _allMods.Remove(mod);
            InsertModAlpha(mod);
            GameList.SelectedItem = mod;
            GameList.ScrollIntoView(mod);
        }

        _isDirty = true;
        UpdateStatusBar();
    }

    private void ApplyUnrealChanges()
    {
        if (GameList.SelectedItem is not UnrealEntry entry) return;

        if (string.IsNullOrWhiteSpace(UNameBox.Text))
        {
            ShowStatus("⚠ Game name is required.", isError: true);
            return;
        }

        _editorEdited = false;
        bool nameChanged = WriteEditorToUnreal(entry);

        if (nameChanged)
        {
            _allUnreal.Remove(entry);
            InsertUnrealAlpha(entry);
            GameList.SelectedItem = entry;
            GameList.ScrollIntoView(entry);
        }

        _isDirty = true;
        UpdateStatusBar();
    }

    /// <summary>Copies the editor fields into a named mod. A blank name keeps the old one. Returns true if the name changed.</summary>
    private bool WriteEditorToMod(GameMod mod)
    {
        var newName = NameBox.Text.Trim();
        if (newName.Length == 0) newName = mod.Name;

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
        return nameChanged;
    }

    /// <summary>Copies the editor fields into an Unreal/Unity/Source entry. A blank name keeps the old one. Returns true if the name changed.</summary>
    private bool WriteEditorToUnreal(UnrealEntry entry)
    {
        var newName = UNameBox.Text.Trim();
        if (newName.Length == 0) newName = entry.Name;

        bool nameChanged = !string.Equals(entry.Name, newName, StringComparison.Ordinal);
        entry.Name   = newName;
        entry.Status = (UStatusCombo.SelectedItem as ComboBoxItem)?.Content as string ?? "WIP";

        // Method: ComboBoxItem tag or content
        var methodItem = UMethodCombo.SelectedItem as ComboBoxItem;
        var methodVal  = (methodItem?.Tag as string ?? methodItem?.Content as string ?? "").Trim();
        entry.Method   = string.IsNullOrEmpty(methodVal) ? null : methodVal;

        // Upgrades: collect rows
        entry.Upgrades = CollectUpgrades();

        if (_activeDb == DbType.Source)
        {
            var tag = (UBitnessCombo.SelectedItem as ComboBoxItem)?.Tag as string;
            entry.Bitness  = string.IsNullOrEmpty(tag) ? null : tag;
            entry.Method   = null;
            entry.Upgrades = null;
        }

        entry.Comments = NullIfEmpty(UCommentsBox.Text);
        return nameChanged;
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
        Field_Changed(sender, e);
    }

    private void AddUpgradeRowWithValues(string format, string size)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
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
            foreach (var (label, val) in UnrealEntry.SizeValues)
                sizeCombo.Items.Add(new ComboBoxItem { Content = label, Tag = val });
        }
        SelectComboByTag(sizeCombo, size);
        Grid.SetColumn(sizeCombo, 2);

        // Remove button
        var removeBtn = new Button
        {
            ToolTip    = "Remove this upgrade row",
            Style      = (Style)FindResource("RemoveButton"),
        };
        removeBtn.Click += (s, e) =>
        {
            UpgradesPanel.Children.Remove(grid);
            Field_Changed(s, e);
        };
        Grid.SetColumn(removeBtn, 3);

        // Hooked after the initial selection so filling the row isn't an edit
        fmtCombo.SelectionChanged  += Field_Changed;
        sizeCombo.SelectionChanged += Field_Changed;

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
            ? RhiTools.Theme.Ui.Critical
            : RhiTools.Theme.Ui.Success;
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
        if (_isUnrealMode && _activeDb == DbType.Source)
        {
            await WikiCheckSourceAsync();
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

    // ── Wiki check — Source ───────────────────────────────────────────────────

    private async Task WikiCheckSourceAsync()
    {
        WikiCheckBtn.IsEnabled = false;
        StatusBar.Text = "Fetching RenoDX wiki (Source Engine section)…";

        List<WikiSourceEntry> wikiEntries;
        try
        {
            wikiEntries = await WikiSourceScrapeService.FetchSourceAllAsync(_githubToken);
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
            StatusBar.Text = $"Wiki check complete — no new Source games found ({wikiEntries.Count} checked).";
            MessageBox.Show(
                $"All {wikiEntries.Count} wiki entries are already in the DB.\n\nNothing to review.",
                "Up to Date", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Source rows are simple (name, status, bitness, comments), so list them and add in one go;
        // anything that needs fixing can be edited in the normal editor afterwards.
        static string Bits(string? b) => b switch { "32" => "32-bit", "64" => "64-bit", "both" => "32+64-bit", _ => "bitness unknown" };
        var list = string.Join("\n", newEntries.Select(w => $"• {w.Name}  [{w.Status}, {Bits(w.Bitness)}]"));
        var answer = MessageBox.Show(
            $"Found {newEntries.Count} new Source game(s) on the wiki:\n\n{list}\n\nAdd them all? You can edit or delete any of them afterwards.",
            "New Source Games", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            StatusBar.Text = "Wiki check closed — no entries added.";
            return;
        }

        foreach (var w in newEntries)
            InsertUnrealAlpha(new UnrealEntry
            {
                Name     = w.Name,
                Status   = w.Status,
                Bitness  = w.Bitness,
                Comments = w.Comments,
            });

        _isDirty = true;
        UpdateStatusBar();
        StatusBar.Text = $"Added {newEntries.Count} Source game(s) from wiki. Save to persist.";
    }
}
