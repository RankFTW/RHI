// CustomDllService.Update.cs — keeping custom DLL copies up to date (a new file with the same name in the folder is a
// new version), the folder watcher that triggers it, the ⚙'s per-DLL states, and record moves for game renames and
// install-folder moves.

using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public partial class CustomDllService
{
    // ── States (⚙) ────────────────────────────────────────────────────────────

    public IReadOnlyList<(CustomDllRecord Record, CustomDllState State)> GetStates(string gameName, string store)
    {
        var result = new List<(CustomDllRecord, CustomDllState)>();
        foreach (var rec in GetRecords(gameName, store))
            result.Add((rec, StateOf(rec)));
        return result;
    }

    private CustomDllState StateOf(CustomDllRecord rec)
    {
        var game = FileHelper.SafeCombine(rec.InstallPath, rec.InstalledAs);
        var src = FileHelper.SafeCombine(_folder, rec.Source);
        try
        {
            if (game == null || !File.Exists(game)) return CustomDllState.Missing;
            var gameHash = Sha256Of(game);
            if (src == null || !File.Exists(src))
                return string.Equals(gameHash, rec.Sha256, StringComparison.OrdinalIgnoreCase)
                    ? CustomDllState.SourceMissing : CustomDllState.ChangedOutsideRhi;
            var srcHash = Sha256Of(src);
            if (string.Equals(gameHash, srcHash, StringComparison.OrdinalIgnoreCase)) return CustomDllState.UpToDate;
            return string.Equals(gameHash, rec.Sha256, StringComparison.OrdinalIgnoreCase)
                ? CustomDllState.UpdatePending : CustomDllState.ChangedOutsideRhi;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[CustomDllService.StateOf] {rec.Source} — {ex.Message}");
            return CustomDllState.ChangedOutsideRhi;
        }
    }

    // ── Redeploy pass ─────────────────────────────────────────────────────────

    private readonly object _redeployLock = new();

    public CustomDllRedeployResult CheckAndRedeploy(IEnumerable<CustomDllGame> games, string caller)
    {
        const string tag = "[CustomDllService]";
        lock (_redeployLock)
        {
            int checkedGames = 0, updated = 0, failed = 0, dropped = 0;
            var byKey = new Dictionary<string, CustomDllGame>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in games) byKey.TryAdd(g.Key, g);

            lock (DbLock)
            {
                var db = LoadDb();
                if (db.Count == 0) return new CustomDllRedeployResult(0, 0, 0, 0);
                bool changed = false;
                var srcHashes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                string? SourceHash(string source)
                {
                    if (srcHashes.TryGetValue(source, out var h)) return h;
                    var p = FileHelper.SafeCombine(_folder, source);
                    try { h = p != null && File.Exists(p) ? Sha256Of(p) : null; }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        CrashReporter.Log($"{tag} Couldn't read {source} in the custom DLL folder — {ex.Message}");
                        h = null;
                    }
                    srcHashes[source] = h;
                    return h;
                }

                foreach (var (key, list) in db)
                {
                    if (!byKey.TryGetValue(key, out var game)) continue; // game not in the library now: leave it
                    if (game.IsHidden || game.IsRunning) continue;        // hidden: left alone; running: retried next time
                    checkedGames++;
                    foreach (var rec in list.ToList())
                    {
                        var dir = string.IsNullOrEmpty(rec.InstallPath) ? game.InstallPath : rec.InstallPath;
                        if (string.IsNullOrEmpty(dir) || XboxPaths.IsWindowsApps(dir) || !Directory.Exists(dir)) continue;
                        var dest = FileHelper.SafeCombine(dir, rec.InstalledAs);
                        if (dest == null) continue;

                        if (!File.Exists(dest))
                        {
                            // The user removed it: never put it back. The game's own file comes back from .original.
                            AuxInstallService.SentinelRestore(dest);
                            RhiInstallManifest.RemoveComponent(dir, ComponentPrefix + rec.Source);
                            list.Remove(rec);
                            changed = true;
                            dropped++;
                            continue;
                        }

                        var srcHash = SourceHash(rec.Source);
                        if (srcHash == null) continue; // source missing: shown in the ⚙

                        string gameHash;
                        try { gameHash = Sha256Of(dest); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            CrashReporter.Log($"{tag} '{game.GameName}': couldn't read {rec.InstalledAs} — {ex.Message}");
                            failed++;
                            continue;
                        }
                        if (string.Equals(gameHash, srcHash, StringComparison.OrdinalIgnoreCase))
                        {
                            if (!string.Equals(rec.Sha256, gameHash, StringComparison.OrdinalIgnoreCase))
                            { rec.Sha256 = gameHash; changed = true; }
                            continue;
                        }
                        if (!string.Equals(gameHash, rec.Sha256, StringComparison.OrdinalIgnoreCase))
                            continue; // changed by hand: left alone, shown in the ⚙

                        var src = FileHelper.SafeCombine(_folder, rec.Source)!;
                        var tmp = dest + ".rhi-tmp";
                        try
                        {
                            File.Copy(src, tmp, overwrite: true);
                            File.Move(tmp, dest, overwrite: true);
                            rec.Sha256 = Sha256Of(dest);
                            rec.UpdatedUtc = DateTime.UtcNow;
                            changed = true;
                            updated++;
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            CrashReporter.Log($"{tag} '{game.GameName}': updating {rec.InstalledAs} failed — {ex.Message}");
                            TryDelete(tmp, tag);
                            failed++;
                        }
                    }
                }
                if (changed) SaveDb(db, "CustomDllService.CheckAndRedeploy");
            }

            CrashReporter.Log($"{tag} Redeploy ({caller}): {checkedGames} games checked, {updated} updated"
                + (failed > 0 ? $", {failed} failed" : "") + (dropped > 0 ? $", {dropped} removed by the user (records dropped)" : ""));
            return new CustomDllRedeployResult(checkedGames, updated, failed, dropped);
        }
    }

    // ── Folder watcher ────────────────────────────────────────────────────────

    private FileSystemWatcher? _watcher;
    private Timer? _debounce;
    private Action? _onChanged;

    public void StartWatching(Action onChanged)
    {
        lock (_redeployLock)
        {
            _onChanged = onChanged;
            if (_watcher != null) return;
            try
            {
                _debounce = new Timer(_ =>
                {
                    try { _onChanged?.Invoke(); }
                    catch (Exception ex) { CrashReporter.Log($"[CustomDllService.Watcher] Redeploy failed — {ex.GetType().Name}: {ex.Message}"); }
                }, null, Timeout.Infinite, Timeout.Infinite);
                _watcher = new FileSystemWatcher(Folder, "*.dll")
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                };
                void Kick(object? s, FileSystemEventArgs e) => _debounce?.Change(2000, Timeout.Infinite);
                _watcher.Created += Kick;
                _watcher.Changed += Kick;
                _watcher.Renamed += (s, e) => Kick(s, e);
                _watcher.EnableRaisingEvents = true;
                CrashReporter.Log($"[CustomDllService.StartWatching] Watching '{_folder}'");
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
            {
                CrashReporter.Log($"[CustomDllService.StartWatching] Couldn't watch '{_folder}' — {ex.Message}");
                _watcher?.Dispose();
                _watcher = null;
            }
        }
    }

    // ── Rename / folder move ──────────────────────────────────────────────────

    private sealed class Row
    {
        public string Name = "";
        public string Store = "";
        public CustomDllRecord Rec = new();
    }

    public int RenameRecords(string oldName, string newName, string? installPath = null)
    {
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName)
            || oldName.Equals(newName, StringComparison.OrdinalIgnoreCase)) return 0;
        lock (DbLock)
        {
            var db = LoadDb();
            var rows = new List<Row>();
            foreach (var (key, list) in db)
            {
                var gk = GameKey.Parse(key);
                rows.AddRange(list.Select(r => new Row { Name = gk.Name, Store = gk.Store, Rec = r }));
            }
            int n = RecordRename.Apply(rows, oldName, newName, installPath,
                r => r.Name, (r, v) => r.Name = v, r => r.Rec.InstallPath,
                (a, b) => a.Store.Equals(b.Store, StringComparison.OrdinalIgnoreCase)
                          && a.Rec.Source.Equals(b.Rec.Source, StringComparison.OrdinalIgnoreCase));
            if (n == 0) return 0;
            var next = new Dictionary<string, List<CustomDllRecord>>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rows)
            {
                var key = GameKey.From(r.Name, r.Store).ToKey();
                if (!next.TryGetValue(key, out var l)) next[key] = l = new List<CustomDllRecord>();
                l.Add(r.Rec);
            }
            SaveDb(next, "CustomDllService.RenameRecords");
            CrashReporter.Log($"[CustomDllService.RenameRecords] '{oldName}' → '{newName}': {n} record(s)");
            return n;
        }
    }

    public int RepointRecords(string gameName, string oldPath, string newPath)
    {
        lock (DbLock)
        {
            var db = LoadDb();
            int n = 0;
            foreach (var (key, list) in db)
            {
                if (!GameKey.Parse(key).MatchesName(gameName)) continue;
                foreach (var r in list.Where(r => RecordRename.SamePath(r.InstallPath, oldPath)))
                {
                    r.InstallPath = newPath;
                    n++;
                }
            }
            if (n > 0)
            {
                SaveDb(db, "CustomDllService.RepointRecords");
                CrashReporter.Log($"[CustomDllService.RepointRecords] '{gameName}': {n} record(s) → '{newPath}'");
            }
            return n;
        }
    }
}
