// SettingsHandler.Backup.cs -- "Backup and restore" card in Settings › Tools & Maintenance (after Game library).
// Backup folder, automatic backups, Back up now, Restore from a backup..., Open folder, last backup line.
// Built in C# (XAML constraint). Making backups: MainViewModel.Backup.cs; restoring: BackupRestoreHelper (staged,
// applied on the next start) and MainWindow.Restore.cs (install review). Plan: docs/backup-restore-plan.md.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Controls;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public partial class SettingsHandler
{
    private TextBlock? _backupFolderText;
    private TextBlock? _backupLastText;
    private TextBlock? _backupStatus;
    private ComboBox? _autoBackupCombo;
    private Button? _backupNowBtn;
    private Button? _backupOpenBtn;
    private bool _backupCardFailed;
    private bool _backupComboInit;

    private void EnsureBackupCard()
    {
        if (_backupFolderText != null) { RefreshBackupCard(); return; }
        if (_backupCardFailed) return;
        try
        {
            StackPanel? section = null;
            foreach (var child in _window.SettingsCardsPanel.Children)
                if (child is StackPanel sp && sp.Tag is string t && t == "Section:Tools") { section = sp; break; }
            if (section == null)
            {
                _backupCardFailed = true;
                CrashReporter.Log("[SettingsHandler.EnsureBackupCard] Tools section not found — card not added");
                return;
            }

            var card = new Border { Style = FindStyle("SettingsCardStyle") };
            var body = new StackPanel { Spacing = 10 };
            card.Child = body;

            var head = new StackPanel { Spacing = 3 };
            head.Children.Add(new TextBlock { Text = "Backup and restore", Style = FindStyle("SettingsCardTitleStyle") });
            head.Children.Add(new TextBlock
            {
                Text = "Keep a copy of your RHI setup for a reformat or a new PC: settings and per-game choices, what RHI "
                     + "installed in each game, their config files and your NVIDIA driver settings. Restoring brings it all back "
                     + "and offers to install everything again.",
                Style = FindStyle("SettingsCardDescStyle"),
                TextWrapping = TextWrapping.Wrap,
            });
            body.Children.Add(head);

            // Backup folder
            var folderRow = new Grid { ColumnSpacing = 10 };
            folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _backupFolderText = new TextBlock
            {
                Style = FindStyle("SettingsLabelStyle"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                IsTextSelectionEnabled = true,
            };
            folderRow.Children.Add(_backupFolderText);
            var chooseBtn = MakeCustomFoldersButton("Choose folder...", primary: false,
                "Where backups go. Pick a place a reformat won't wipe: another drive, OneDrive or a USB stick.");
            chooseBtn.Click += async (s, e) =>
            {
                try { await ChooseBackupFolderAsync(); }
                catch (Exception ex) { CrashReporter.Log($"[SettingsHandler.Backup] Choose folder failed — {ex.Message}"); }
            };
            Grid.SetColumn(chooseBtn, 1);
            folderRow.Children.Add(chooseBtn);
            body.Children.Add(folderRow);

            // Automatic backups
            var autoRow = new Grid { ColumnSpacing = 10 };
            autoRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            autoRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 210 });
            autoRow.Children.Add(new TextBlock { Text = "Automatic backups", Style = FindStyle("SettingsLabelStyle"), VerticalAlignment = VerticalAlignment.Center });
            _autoBackupCombo = new ComboBox { Style = FindStyle("SettingsComboStyle"), HorizontalAlignment = HorizontalAlignment.Stretch };
            _autoBackupCombo.Items.Add("Off");
            _autoBackupCombo.Items.Add("On");
            ToolTipService.SetToolTip(_autoBackupCombo,
                "Makes a backup in the backup folder at most once a day, only when something changed. The newest 5 are kept.");
            _autoBackupCombo.SelectionChanged += (s, e) =>
            {
                if (_backupComboInit) return;
                bool on = _autoBackupCombo.SelectedIndex == 1;
                if (on && string.IsNullOrWhiteSpace(ViewModel.Settings.BackupFolder))
                {
                    SetBackupStatus("Choose a backup folder first.", error: true);
                    _backupComboInit = true; _autoBackupCombo.SelectedIndex = 0; _backupComboInit = false;
                    return;
                }
                ViewModel.Settings.AutoBackup = on;
                ViewModel.SaveSettingsPublic();
                if (on) _ = ViewModel.MaybeAutoBackupAsync("turned on");
            };
            Grid.SetColumn(_autoBackupCombo, 1);
            autoRow.Children.Add(_autoBackupCombo);
            body.Children.Add(autoRow);

            _backupLastText = new TextBlock { Style = FindStyle("SettingsHelpStyle"), TextWrapping = TextWrapping.Wrap };
            body.Children.Add(_backupLastText);

            var buttons = new WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 8 };
            _backupNowBtn = MakeCustomFoldersButton("Back up now", primary: true, "Make a backup in the backup folder now.");
            _backupNowBtn.Click += async (s, e) =>
            {
                try { await BackUpNowAsync(); }
                catch (Exception ex) { CrashReporter.Log($"[SettingsHandler.Backup] Back up now failed — {ex.Message}"); }
            };
            buttons.Children.Add(_backupNowBtn);
            var restoreBtn = MakeCustomFoldersButton("Restore from a backup...", primary: false,
                "Choose a backup. RHI restarts, brings back its settings, and once your games are found offers to install everything again.");
            restoreBtn.Click += async (s, e) =>
            {
                try { await RestoreFromBackupAsync(); }
                catch (Exception ex) { CrashReporter.Log($"[SettingsHandler.Backup] Restore failed — {ex.Message}"); }
            };
            buttons.Children.Add(restoreBtn);
            _backupOpenBtn = MakeCustomFoldersButton("Open folder", primary: false, "Open the backup folder.");
            _backupOpenBtn.Click += (s, e) =>
            {
                try
                {
                    var folder = ViewModel.Settings.BackupFolder;
                    if (Directory.Exists(folder))
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
                }
                catch (Exception ex) { CrashReporter.Log($"[SettingsHandler.Backup] Open folder failed — {ex.Message}"); }
            };
            buttons.Children.Add(_backupOpenBtn);
            body.Children.Add(buttons);

            _backupStatus = new TextBlock
            {
                Style = FindStyle("SettingsNoteStyle"),
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed,
            };
            body.Children.Add(_backupStatus);

            body.Children.Add(new TextBlock
            {
                Text = "Not included: your GitHub token and API keys, downloads RHI can get again, and extras (HSF, RTX Encore, "
                     + "MFG unlockers and others), which you install again from Extras. Their settings are kept.",
                Style = FindStyle("SettingsHelpStyle"),
                TextWrapping = TextWrapping.Wrap,
            });

            // After the Game library card (heading at 0, Game library at 1 when it was added)
            int at = 1;
            for (int i = 0; i < section.Children.Count; i++)
                if (section.Children[i] is Border { Child: Panel p } && _customFoldersList != null && p.Children.Contains(_customFoldersList)) { at = i + 1; break; }
            section.Children.Insert(Math.Min(at, section.Children.Count), card);
            ViewModel.BackupWritten += () => { if (_backupFolderText != null) RefreshBackupCard(); };
            RefreshBackupCard();
        }
        catch (Exception ex)
        {
            _backupCardFailed = true;
            _backupFolderText = null;
            CrashReporter.Log($"[SettingsHandler.EnsureBackupCard] Failed — {ex.Message}");
        }
    }

    private void RefreshBackupCard()
    {
        if (_backupFolderText == null) return;
        try
        {
            var folder = ViewModel.Settings.BackupFolder;
            bool hasFolder = !string.IsNullOrWhiteSpace(folder);
            _backupFolderText.Text = hasFolder ? folder : "No backup folder chosen";
            ToolTipService.SetToolTip(_backupFolderText, hasFolder ? folder : null);
            if (_autoBackupCombo != null)
            {
                _backupComboInit = true;
                _autoBackupCombo.SelectedIndex = ViewModel.Settings.AutoBackup ? 1 : 0;
                _backupComboInit = false;
            }
            if (_backupNowBtn != null) _backupNowBtn.IsEnabled = hasFolder;
            if (_backupOpenBtn != null) _backupOpenBtn.IsEnabled = hasFolder && Directory.Exists(folder);
            if (_backupLastText != null)
            {
                _backupLastText.Text = DateTime.TryParse(ViewModel.Settings.LastBackupUtc, null,
                        System.Globalization.DateTimeStyles.RoundtripKind, out var last)
                    ? $"Last backup: {last.ToLocalTime():d MMMM yyyy, HH:mm}"
                    : "No backup made yet.";
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[SettingsHandler.RefreshBackupCard] {ex.Message}");
        }
    }

    private void SetBackupStatus(string text, bool error)
    {
        if (_backupStatus == null) return;
        _backupStatus.Text = text;
        _backupStatus.Foreground = UIFactory.Brush(error ? ResourceKeys.AccentRedBrush : ResourceKeys.TextSecondaryBrush);
        _backupStatus.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task ChooseBackupFolderAsync()
    {
        var current = ViewModel.Settings.BackupFolder;
        var folder = await _window.PickFolderAsync(string.IsNullOrWhiteSpace(current) ? null : current);
        if (string.IsNullOrEmpty(folder)) return;
        bool first = string.IsNullOrWhiteSpace(current);
        ViewModel.Settings.BackupFolder = folder;
        if (first) ViewModel.Settings.AutoBackup = true; // choosing a folder is the opt-in
        ViewModel.SaveSettingsPublic();
        RefreshBackupCard();
        if (folder.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows)[..3], StringComparison.OrdinalIgnoreCase)
            && !folder.Contains("OneDrive", StringComparison.OrdinalIgnoreCase))
            SetBackupStatus("This folder is on your Windows drive. A reformat will wipe it: copy your backups somewhere else before you reformat.", error: false);
        else
            SetBackupStatus(first ? "Automatic backups are on." : "", error: false);
        if (first) await BackUpNowAsync();
    }

    private async Task BackUpNowAsync()
    {
        var folder = ViewModel.Settings.BackupFolder;
        if (string.IsNullOrWhiteSpace(folder)) { SetBackupStatus("Choose a backup folder first.", error: true); return; }
        if (_backupNowBtn != null) { _backupNowBtn.IsEnabled = false; _backupNowBtn.Content = "Backing up..."; }
        try
        {
            var (path, error) = await ViewModel.CreateBackupAsync(folder, onlyIfChanged: false, "manual");
            if (error != null) SetBackupStatus(error, error: true);
            else if (path != null) SetBackupStatus($"Saved {Path.GetFileName(path)}.", error: false);
        }
        finally
        {
            if (_backupNowBtn != null) { _backupNowBtn.Content = "Back up now"; _backupNowBtn.IsEnabled = true; }
            RefreshBackupCard();
        }
    }

    private async Task RestoreFromBackupAsync()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        var path = await BackupRestoreHelper.PickBackupFileAsync(hwnd, ViewModel.Settings.BackupFolder);
        if (string.IsNullOrEmpty(path)) return;
        Services.BackupManifest manifest;
        try { manifest = await Task.Run(() => BackupArchive.ReadManifest(path)); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            SetBackupStatus(ex is InvalidDataException ? ex.Message : $"The file couldn't be read: {ex.Message}", error: true);
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Restore from a backup",
            Content = new TextBlock
            {
                Text = BackupRestoreHelper.Describe(manifest) + "\n\n"
                     + "RHI restarts and replaces its settings, per-game choices and presets with the ones in the backup "
                     + "(your current ones are kept in RHI's Backups folder). Once your games are found, RHI lists what was "
                     + "installed in each one so you can install it all again.",
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "Restore and restart",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = _window.Content?.XamlRoot,
        };
        if (await DialogService.ShowSafeAsync(dialog) != ContentDialogResult.Primary) return;
        try
        {
            await Task.Run(() => BackupRestoreHelper.StageRestore(path));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            SetBackupStatus($"The backup couldn't be restored: {ex.Message}", error: true);
            return;
        }
        BackupRestoreHelper.RestartRhi(_window);
    }
}
