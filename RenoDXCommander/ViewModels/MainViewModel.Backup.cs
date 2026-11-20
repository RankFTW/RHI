// MainViewModel.Backup.cs -- makes .rhibackup files from the current library (Back up now, automatic backups).
// The file format and restore live in Services/BackupArchive.cs; the restore review is MainWindow.Restore.cs.
// Plan: docs/backup-restore-plan.md.

using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    internal static string RhiDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI");

    /// <summary>How many backups automatic backups keep in the backup folder.</summary>
    private const int AutoBackupsKept = 5;

    private readonly SemaphoreSlim _backupLock = new(1, 1);

    /// <summary>Raised on the UI thread after a backup was written (Settings card refreshes its "last backup" line).</summary>
    public event Action? BackupWritten;

    /// <summary>
    /// Raised (UI thread, low priority) once a store scan's cards are in the list: after the background scan merge
    /// on a cached start, after BuildCards on a full scan / Refresh. IsLoading turns false after the cache phase,
    /// before the store scan, so it can't be used for "every game is known" (the restore review matched only the
    /// 4 cached games of a fresh install that way).
    /// </summary>
    public event Action? LibraryScanned;

    private void RaiseLibraryScanned(string source)
    {
        CrashReporter.Log($"[MainViewModel.RaiseLibraryScanned] {source}: {_allCards.Count} cards");
        DispatcherQueue?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => LibraryScanned?.Invoke());
    }

    /// <summary>
    /// The components RHI installed in a game, in install order. Must run on the UI thread (reads card state).
    /// </summary>
    internal static List<BackupComponent> InstalledComponentsFor(GameCardViewModel card)
    {
        var list = new List<BackupComponent>();
        void Add(string id, string? detail) => list.Add(new BackupComponent { Id = id, Detail = string.IsNullOrWhiteSpace(detail) ? null : detail });

        // Luma installs its own ReShade, so ReShade isn't listed separately for Luma games
        if (!card.IsLumaInstalled && card.RsStatus is GameStatus.Installed or GameStatus.UpdateAvailable)
            Add(BackupComponents.ReShade, card.RsInstalledVersion);
        if (card.IsDxvkInstalled) Add(BackupComponents.Dxvk, null);
        if (card.IsRefInstalled) Add(BackupComponents.ReFramework, card.RefInstalledVersion);
        if (card.IsRdxInstalled) Add(BackupComponents.RenoDx, card.InstalledRecord?.AddonFileName ?? card.Mod?.Name);
        if (card.IsLumaInstalled) Add(BackupComponents.Luma, null);
        if (card.IsUlInstalled) Add(BackupComponents.ReLimiter, card.UlInstalledVersion);
        if (card.IsDcInstalled) Add(BackupComponents.DisplayCommander, card.DcInstalledVersion);
        if (card.IsDofFixInstalled) Add(BackupComponents.DofFix, null);
        if (card.IsOsInstalled) Add(BackupComponents.OptiScaler, card.OsInstalledVersion);
        return list;
    }

    /// <summary>
    /// Snapshot of the library for a backup: every game with an install folder and either something RHI installed
    /// or config files worth keeping. Card state is read here (UI thread); file checks are cheap (a few File.Exists).
    /// </summary>
    private (BackupManifest Manifest, Dictionary<int, string> Paths) BuildBackupSnapshot()
    {
        var manifest = new BackupManifest
        {
            RhiVersion = AppVersionInfo.Display,
            CreatedUtc = DateTime.UtcNow,
        };
        var paths = new Dictionary<int, string>();
        foreach (var card in _allCards.ToList())
        {
            if (string.IsNullOrEmpty(card.InstallPath)) continue;
            var components = InstalledComponentsFor(card);
            var game = new BackupGame
            {
                Name = card.GameName,
                Store = card.Source ?? "",
                InstallPath = card.InstallPath,
                Is32Bit = card.Is32Bit,
                Components = components,
            };
            if (components.Count == 0) continue; // nothing of RHI's in this game
            int index = manifest.Games.Count;
            game.ConfigFolder = $"games/{index}";
            manifest.Games.Add(game);
            paths[index] = card.InstallPath;
        }
        return (manifest, paths);
    }

    /// <summary>Config files are found off the UI thread (they need folder reads).</summary>
    private static void FillConfigFiles(BackupManifest manifest, Dictionary<int, string> paths)
    {
        for (int i = 0; i < manifest.Games.Count; i++)
        {
            if (!paths.TryGetValue(i, out var installPath)) continue;
            string? addonDir = null;
            try { addonDir = ModInstallService.GetAddonDeployPath(installPath); }
            catch (Exception ex) { CrashReporter.Log($"[MainViewModel.FillConfigFiles] Add-on folder for '{manifest.Games[i].Name}' — {ex.Message}"); }
            manifest.Games[i].ConfigFiles = BackupArchive.FindGameConfigFiles(installPath, addonDir);
        }
    }

    /// <summary>
    /// Writes a backup into <paramref name="folder"/>. Returns the file path, or null with an error message.
    /// <paramref name="onlyIfChanged"/> skips the write when nothing changed since the last backup (automatic backups).
    /// </summary>
    public async Task<(string? Path, string? Error)> CreateBackupAsync(string folder, bool onlyIfChanged, string reason)
    {
        if (string.IsNullOrWhiteSpace(folder)) return (null, "Choose a backup folder first.");
        if (!await _backupLock.WaitAsync(0)) return (null, "A backup is already being made.");
        try
        {
            var (manifest, paths) = BuildBackupSnapshot();
            var games = paths.Select(p => (manifest.Games[p.Key].Name, p.Value)).ToList();
            var lastHash = _settingsViewModel.LastBackupHash;
            var result = await Task.Run(() =>
            {
                FillConfigFiles(manifest, paths);
                var hash = BackupArchive.Fingerprint(RhiDataDir, manifest, paths);
                if (onlyIfChanged && string.Equals(hash, lastHash, StringComparison.Ordinal))
                    return (Path: (string?)null, Hash: hash, Skipped: true);

                // NVIDIA driver profile settings (empty on other cards or when the driver can't be read)
                string? nvidia = null;
                try
                {
                    var data = _dlssPresetService.ExportProfiles(games);
                    if (data.Count > 0)
                    {
                        nvidia = System.Text.Json.JsonSerializer.Serialize(data);
                        manifest.HasNvidia = true;
                    }
                }
                catch (Exception ex) { CrashReporter.Log($"[MainViewModel.CreateBackupAsync] NVIDIA profile export skipped — {ex.Message}"); }

                Directory.CreateDirectory(folder);
                var file = Path.Combine(folder, $"RHI-backup-{DateTime.Now:yyyyMMdd-HHmm}{BackupArchive.Extension}");
                BackupArchive.Write(file, manifest, RhiDataDir, paths, nvidia);
                return (Path: (string?)file, Hash: hash, Skipped: false);
            });

            if (result.Skipped)
            {
                CrashReporter.Log($"[MainViewModel.CreateBackupAsync] ({reason}) Nothing changed since the last backup — skipped");
                return (null, null);
            }

            _settingsViewModel.LastBackupUtc = DateTime.UtcNow.ToString("o");
            _settingsViewModel.LastBackupHash = result.Hash;
            SaveNameMappings();
            CrashReporter.Log($"[MainViewModel.CreateBackupAsync] ({reason}) Wrote '{result.Path}' — {manifest.Games.Count} games, NVIDIA={manifest.HasNvidia}");
            if (reason == "auto") PruneOldBackups(folder);
            DispatcherQueue?.TryEnqueue(() => BackupWritten?.Invoke());
            return (result.Path, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            CrashReporter.Log($"[MainViewModel.CreateBackupAsync] ({reason}) Failed — {ex.Message}");
            return (null, ex is UnauthorizedAccessException
                ? "RHI can't write to the backup folder. Choose another folder."
                : $"The backup couldn't be written: {ex.Message}");
        }
        finally
        {
            _backupLock.Release();
        }
    }

    /// <summary>Automatic backup: on, folder chosen, last one over a day ago, and something changed.</summary>
    public async Task MaybeAutoBackupAsync(string trigger)
    {
        try
        {
            if (!_settingsViewModel.AutoBackup || string.IsNullOrWhiteSpace(_settingsViewModel.BackupFolder)) return;
            if (IsLoading || _allCards.Count == 0) return;
            if (DateTime.TryParse(_settingsViewModel.LastBackupUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var last)
                && DateTime.UtcNow - last < TimeSpan.FromDays(1))
                return;
            CrashReporter.Log($"[MainViewModel.MaybeAutoBackupAsync] Automatic backup ({trigger})");
            await CreateBackupAsync(_settingsViewModel.BackupFolder, onlyIfChanged: true, "auto");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainViewModel.MaybeAutoBackupAsync] {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Keeps the newest automatic backups (RHI-backup-*.rhibackup) in the folder.</summary>
    private static void PruneOldBackups(string folder)
    {
        try
        {
            var old = new DirectoryInfo(folder).GetFiles("RHI-backup-*" + BackupArchive.Extension)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Skip(AutoBackupsKept);
            foreach (var f in old)
            {
                f.Delete();
                CrashReporter.Log($"[MainViewModel.PruneOldBackups] Removed '{f.Name}'");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[MainViewModel.PruneOldBackups] {ex.Message}");
        }
    }

    /// <summary>
    /// Downloads every add-on that's selected (globally or for a game) but not in staging. Add-ons are normally
    /// downloaded when ticked in the picker and CheckAndUpdateAllAsync only updates ones already downloaded, so
    /// after a restore (or Purge staging files) selected add-ons were never fetched and every game skipped them.
    /// Called after the add-on update check in both init paths.
    /// </summary>
    private async Task EnsureSelectedAddonsDownloadedAsync()
    {
        try
        {
            var selected = new HashSet<string>(_settingsViewModel.EnabledGlobalAddons ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            foreach (var list in _gameNameService.PerGameAddonSelection.Values.ToList())
                foreach (var name in list) selected.Add(name);
            foreach (var name in selected)
            {
                if (_backgroundStopped) return;
                if (_addonPackService.IsDownloaded(name)) continue;
                var entry = _addonPackService.AvailablePacks.FirstOrDefault(e => e.PackageName.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (entry == null) continue; // no longer offered, or staged by another service
                try
                {
                    CrashReporter.Log($"[MainViewModel.EnsureSelectedAddonsDownloadedAsync] '{name}' is selected but not downloaded — downloading");
                    await _addonPackService.DownloadAddonAsync(entry);
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
                {
                    CrashReporter.Log($"[MainViewModel.EnsureSelectedAddonsDownloadedAsync] '{name}' — {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainViewModel.EnsureSelectedAddonsDownloadedAsync] {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>The restore waiting for its review (restore\plan.json), or null.</summary>
    internal static RestorePlan? PendingRestorePlan() => BackupArchive.LoadPlan(RhiDataDir);
}
