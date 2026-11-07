// MainViewModel.FolderMigration.cs -- moves a game's RHI installs when its install folder changes:
// automatically when the manifest moves it (installPathMigrations / installPathOverrides), and on
// request after Change install folder / Reset install folder (MainWindow.Events.Install.cs).

using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    private IInstallFolderMigrationService? _folderMigration;
    private IInstallFolderMigrationService FolderMigration
        => _folderMigration ??= App.Services.GetRequiredService<IInstallFolderMigrationService>();

    /// <summary>Games in the manifest's installPathMigrations (their DLSS search covers the game root).</summary>
    private readonly HashSet<string> _installPathMigrationGames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// "GameName|Store|old folder" for automatic moves that couldn't run this time (game running, file
    /// locked). BuildCards leaves those records alone instead of copying files across ("path
    /// reconciliation"), so the move can happen on a later launch with the evidence intact.
    /// </summary>
    private readonly HashSet<string> _deferredFolderMigrations = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _deferredFolderMigrationsLock = new();

    /// <summary>Notices from the last automatic pass, shown once the cards are built.</summary>
    private readonly List<(string GameName, string Store, string Message)> _folderMigrationNotices = new();

    /// <summary>Raised (background thread) with each finished automatic move: game name, store, notice text.</summary>
    public event Action<string, string, string>? FolderMigrated;

    private void NoteInstallPathMigrations(Dictionary<string, string>? migrations)
    {
        if (migrations == null) return;
        foreach (var name in migrations.Keys) _installPathMigrationGames.Add(name);
    }

    private static string DeferKey(string gameName, string? store, string oldDir)
        => $"{gameName}|{store ?? ""}|{InstallFolderMover.Norm(oldDir)}";

    /// <summary>True when BuildCards must not copy files / repoint records from <paramref name="oldPath"/> (a move is pending).</summary>
    internal bool ShouldDeferPathReconciliation(string gameName, string? store, string oldPath)
    {
        if (string.IsNullOrEmpty(oldPath)) return false;
        lock (_deferredFolderMigrationsLock)
        {
            if (_deferredFolderMigrations.Count == 0) return false;
            bool defer = _deferredFolderMigrations.Contains(DeferKey(gameName, store, oldPath));
            if (defer) _crashReporter.Log($"[MainViewModel.ShouldDeferPathReconciliation] '{gameName}': '{oldPath}' waits for the folder move (no copy)");
            return defer;
        }
    }

    /// <summary>True when an automatic move is waiting for this card's old folder (its records still point there).</summary>
    internal bool IsFolderMigrationPending(GameCardViewModel card)
    {
        lock (_deferredFolderMigrationsLock)
            return _deferredFolderMigrations.Any(k => k.StartsWith($"{card.GameName}|{card.Source ?? ""}|", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Both init paths, after the manifest is applied and before BuildCards: for each game whose folder
    /// comes from the manifest (installPathMigrations / installPathOverrides) and has no user folder
    /// override, moves RHI's installs from old folders (install records pointing elsewhere inside the
    /// game, or the folder detection picked before the override) to the new one. Records are repointed
    /// before BuildCards runs, so every component shows as installed in the new folder straight away.
    /// </summary>
    private async Task RunManifestFolderMigrationsAsync(List<DetectedGame> games)
    {
        if (_backgroundLifetime.IsCancellationRequested) return;
        try
        {
            await Task.Run(() => RunManifestFolderMigrations(games), _backgroundLifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _crashReporter.Log($"[MainViewModel.RunManifestFolderMigrationsAsync] Failed — {ex.Message}");
        }
    }

    private void RunManifestFolderMigrations(List<DetectedGame> games)
    {
        lock (_deferredFolderMigrationsLock) _deferredFolderMigrations.Clear();
        lock (_folderMigrationNotices) _folderMigrationNotices.Clear();
        if (_installPathOverrides.Count == 0) return;

        var candidates = games.Where(g => !g.IsManuallyAdded && _installPathOverrides.ContainsKey(g.Name)).ToList();
        if (candidates.Count == 0) return;

        // Folders the install records point at, per game (all record stores)
        var recordDirs = new List<(string Name, string Store, string Path)>();
        void Add(string name, string store, string path) { if (!string.IsNullOrEmpty(path)) recordDirs.Add((name, store ?? "", path)); }
        foreach (var r in _auxInstaller.LoadAll()) Add(r.GameName, r.Store, r.InstallPath);
        foreach (var r in _installer.LoadAll()) Add(r.GameName, r.Store, r.InstallPath);
        foreach (var r in LumaService.GetAllRecords()) Add(r.GameName, r.Store, r.InstallPath);
        foreach (var r in _refService.GetRecords()) Add(r.GameName, r.Store, r.InstallPath);
        foreach (var r in _dxvkService.LoadAllRecords()) Add(r.GameName, r.Store, r.InstallPath);

        foreach (var game in candidates)
        {
            if (_backgroundLifetime.IsCancellationRequested) return;
            try { MigrateManifestMovedGame(game, recordDirs); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                _crashReporter.Log($"[MainViewModel.RunManifestFolderMigrations] '{game.Name}' failed — {ex.Message}");
            }
        }
    }

    private void MigrateManifestMovedGame(DetectedGame game, List<(string Name, string Store, string Path)> recordDirs)
    {
        var store = game.Source ?? "";
        var key = GameKey.From(game.Name, store).ToKey();
        bool hidden = _hiddenGames.Contains(key);
        if (_folderOverrides.ContainsKey(key) || _folderOverrides.ContainsKey(game.Name)) return; // user picked the folder
        if (string.IsNullOrEmpty(game.InstallPath) || !Directory.Exists(game.InstallPath)) return;

        var newDir = ResolveOverrideFolder(game.InstallPath, _installPathOverrides[game.Name]);
        if (newDir == null || !Directory.Exists(newDir)) return;
        if (InstallFolderMover.IsWindowsApps(newDir)) return;                        // never written

        var root = InstallFolderMover.Norm(game.InstallPath);
        bool Usable(string dir) => !string.IsNullOrEmpty(dir)
            && !InstallFolderMover.SamePath(dir, newDir)
            && InstallFolderMover.IsInside(dir, root)
            && !InstallFolderMover.IsWindowsApps(dir)
            && Directory.Exists(dir);

        var oldDirs = recordDirs
            .Where(r => r.Name.Equals(game.Name, StringComparison.OrdinalIgnoreCase)
                     && (r.Store.Length == 0 || r.Store.Equals(store, StringComparison.OrdinalIgnoreCase))
                     && Usable(r.Path))
            .Select(r => InstallFolderMover.Norm(r.Path))
            .ToList();
        // The folder detection picked before the override: settings-keyed components (ReLimiter, NR
        // methods, extras) have no record, so it's checked for RHI's files too.
        var rootKey = game.InstallPath.TrimEnd('\\', '/').ToLowerInvariant();
        if (_resolvedPathCache.TryGetValue(rootKey, out var detected) && Usable(detected))
            oldDirs.Add(InstallFolderMover.Norm(detected));

        foreach (var oldDir in oldDirs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (_backgroundLifetime.IsCancellationRequested) return;
            var request = BuildFolderMigrationRequest(game.Name, store, oldDir, newDir, game.InstallPath);
            if (FolderMigration.BuildPlan(request).IsEmpty) continue;
            if (hidden)
            {
                // Hidden games are left alone, but BuildCards mustn't half-copy them either: moved once unhidden
                lock (_deferredFolderMigrationsLock) _deferredFolderMigrations.Add(DeferKey(game.Name, store, oldDir));
                _crashReporter.Log($"[MainViewModel.MigrateManifestMovedGame] '{game.Name}' is hidden — installs left in '{oldDir}'");
                continue;
            }

            _crashReporter.Log($"[MainViewModel.MigrateManifestMovedGame] '{game.Name}' install folder moved by the manifest: '{oldDir}' → '{newDir}'");
            var outcome = FolderMigration.Migrate(request);
            if (outcome.Success)
            {
                AfterFolderMigration(game.Name, store, oldDir, newDir, outcome);
                var shown = InstallFolderMover.IsInside(newDir, root) && !InstallFolderMover.SamePath(newDir, root)
                    ? Path.GetRelativePath(root, newDir) : newDir;
                var msg = $"Moved {game.Name}'s mods to {shown}";
                lock (_folderMigrationNotices) _folderMigrationNotices.Add((game.Name, store, msg));
            }
            else if (!outcome.NothingToMove)
            {
                lock (_deferredFolderMigrationsLock) _deferredFolderMigrations.Add(DeferKey(game.Name, store, oldDir));
                _crashReporter.Log($"[MainViewModel.MigrateManifestMovedGame] '{game.Name}' not moved ({outcome.Reason}) — will try again next launch");
            }
        }
    }

    /// <summary>The manifest's subfolder for a game: first candidate that exists (".." ones are taken as-is).</summary>
    private static string? ResolveOverrideFolder(string root, string subPath)
    {
        foreach (var candidate in subPath.Split('|'))
        {
            var trimmed = candidate.Trim();
            var full = Path.GetFullPath(Path.Combine(root, trimmed));
            if (trimmed.StartsWith("..") || Directory.Exists(full)) return full;
        }
        return null;
    }

    /// <summary>Raises a notice for each automatic move from the last pass (call after the cards are built / merged).</summary>
    private void PublishFolderMigrationNotices()
    {
        List<(string GameName, string Store, string Message)> notices;
        lock (_folderMigrationNotices)
        {
            notices = _folderMigrationNotices.ToList();
            _folderMigrationNotices.Clear();
        }
        foreach (var (name, store, msg) in notices)
        {
            try { FolderMigrated?.Invoke(name, store, msg); }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
            { _crashReporter.Log($"[MainViewModel.PublishFolderMigrationNotices] Handler failed — {ex.Message}"); }
        }
    }

    internal FolderMigrationRequest BuildFolderMigrationRequest(string gameName, string store, string oldDir, string newDir, string? gameRoot)
        => new()
        {
            GameName = gameName,
            Store = store,
            OldDir = oldDir,
            NewDir = newDir,
            GameRoot = gameRoot,
            SkipReShade = _manifest?.GacSymlinkGames?.ContainsKey(gameName) == true,
            IsEmulator = _manifest?.EmulatorGames?.ContainsKey(gameName) == true,
            RtxEncoreInstalledAs = GetRtxEncoreInstalledAs(gameName, store),
            RtxEncoreNrDllDeployed = GetRtxEncoreNrDllDeployed(gameName, store),
            Rtx40MfgInstalledAs = GetRtx40MfgInstalledAs(gameName, store),
            Dlssg2030InstalledAs = GetDlssg2030InstalledAs(gameName, store),
            DeInstalledAs = GetDeInstalledAs(gameName, store),
            DgVoodooStandalone = GetDgVoodooStandalone(gameName, store),
        };

    /// <summary>
    /// The install folder the scan picks for a game root (cached detection result, else a fresh
    /// detection, then the manifest's subfolder) — where Reset install folder goes back to.
    /// File-system work: call from a background thread.
    /// </summary>
    internal string ResolveDetectedInstallPath(string gameName, string root)
    {
        var rootKey = root.TrimEnd('\\', '/').ToLowerInvariant();
        string path = _resolvedPathCache.TryGetValue(rootKey, out var cached) && Directory.Exists(cached)
            ? cached
            : _gameDetectionService.DetectEngineAndPath(root).installPath;
        if (_installPathOverrides.TryGetValue(gameName, out var sub) && ResolveOverrideFolder(root, sub) is { } overridden)
            path = overridden;
        return path;
    }

    /// <summary>Change / Reset install folder: does the old folder hold this game's RHI installs? (Background thread.)</summary>
    internal bool HasInstallsToMove(GameCardViewModel card, string oldDir, string newDir)
        => FolderMigration.HasInstalls(BuildFolderMigrationRequest(card.GameName, card.Source ?? "", oldDir, newDir, card.DetectedGame?.InstallPath));

    /// <summary>Change / Reset install folder: moves the installs (background thread). Call <see cref="ApplyFolderMigrationToCard"/> after, on the UI thread.</summary>
    internal FolderMigrationOutcome MigrateCardInstalls(GameCardViewModel card, string oldDir, string newDir)
    {
        var store = card.Source ?? "";
        var outcome = FolderMigration.Migrate(BuildFolderMigrationRequest(card.GameName, store, oldDir, newDir, card.DetectedGame?.InstallPath));
        if (outcome.Success) AfterFolderMigration(card.GameName, store, oldDir, newDir, outcome);
        return outcome;
    }

    /// <summary>
    /// Points the card's in-memory install records at the new folder (FindRecord hands out fresh
    /// objects, so the card's own copies need updating too) and saves the library. UI thread.
    /// </summary>
    internal void ApplyFolderMigrationToCard(GameCardViewModel card, string oldDir, string newDir)
    {
        bool Old(string? p) => !string.IsNullOrEmpty(p) && InstallFolderMover.SamePath(p, oldDir);
        if (card.InstalledRecord != null && Old(card.InstalledRecord.InstallPath)) card.InstalledRecord.InstallPath = newDir;
        if (card.RsRecord != null && Old(card.RsRecord.InstallPath)) card.RsRecord.InstallPath = newDir;
        if (card.LumaRecord != null && Old(card.LumaRecord.InstallPath)) card.LumaRecord.InstallPath = newDir;
        if (card.RefRecord != null && Old(card.RefRecord.InstallPath)) card.RefRecord.InstallPath = newDir;
        if (card.DxvkRecord != null && Old(card.DxvkRecord.InstallPath)) card.DxvkRecord.InstallPath = newDir;
        card.NotifyAll();
        SaveLibrary();
    }

    /// <summary>Settings and caches that follow a successful move (both automatic and manual).</summary>
    private void AfterFolderMigration(string gameName, string store, string oldDir, string newDir, FolderMigrationOutcome outcome)
    {
        // ASI Loader's installed-as setting can be cleared by the Extras row while its record pointed
        // at the old folder; put it back from the (now moved) record.
        if (string.IsNullOrEmpty(GetUalInstalledAs(gameName, store)))
        {
            var ual = _auxInstaller.FindRecord(gameName, newDir, UltimateAsiLoaderService.AddonType);
            if (ual != null) SetUalInstalledAs(gameName, ual.InstalledAs, store);
        }

        // MFG Ada Unlock's XeSS files belong in the exe folder: give the old folder's back, deploy in the new one
        if (File.Exists(Path.Combine(oldDir, "MFGAdaUnlock.rhi-xess.txt")))
        {
            try
            {
                _rtx40MfgService.RemoveAdaXessFiles(oldDir);
                _rtx40MfgService.DeployAdaXessFiles(newDir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _crashReporter.Log($"[MainViewModel.AfterFolderMigration] '{gameName}' MFG Ada XeSS files — {ex.Message}");
            }
        }

        ForgetGameApi(oldDir);
        ForgetGameApi(newDir);
        // New folder: scans of the old one mustn't keep it on the DLSS skip list.
        _dlssStreamlineService.RecordDlssFound(gameName);
        _crashReporter.Log($"[MainViewModel.AfterFolderMigration] '{gameName}' moved to '{newDir}': {string.Join(", ", outcome.Components)}");
    }
}
