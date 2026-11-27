// MainViewModel.CacheLoad.Prefetch.cs -- File reads for the cache-phase card loop, done on a background thread first (audit D5).

using System.Collections.Concurrent;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    /// <summary>
    /// What the cache-phase card loop reads from disk, read beforehand off the UI thread: DLSS / Streamline file
    /// versions from the saved DLSS path cache and the Unreal version of games the cache marks as Unreal.
    /// Lookups that weren't prefetched (e.g. a manifest folder override changed the install folder) are read
    /// directly, exactly as the loop did before, so the result is always the same.
    /// </summary>
    private sealed class CacheFilePrefetch
    {
        private readonly IDlssStreamlineService _dlss;
        private readonly ConcurrentDictionary<string, string?> _versions = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, string?> _nrVersions = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, bool> _exists = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, string> _engineHints = new(StringComparer.OrdinalIgnoreCase);
        private int _misses;

        public CacheFilePrefetch(IDlssStreamlineService dlss) => _dlss = dlss;

        // ── Filled on the background thread ──────────────────────────────────
        public void ReadVersion(string path) => _versions[path] = _dlss.GetFileVersion(path);
        public void ReadNrVersion(string path) => _nrVersions[path] = ReadNr(path);
        public void ReadExists(string path) => _exists[path] = File.Exists(path);

        /// <summary>The loop's Streamline pick: sl.common.dll if present, else sl.interposer.dll; only that one's version.</summary>
        public void ReadStreamline(string folder)
        {
            var common     = Path.Combine(folder, "sl.common.dll");
            var interposer = Path.Combine(folder, "sl.interposer.dll");
            bool hasCommon = File.Exists(common);
            _exists[common] = hasCommon;
            if (hasCommon) { ReadVersion(common); return; }
            bool hasInterposer = File.Exists(interposer);
            _exists[interposer] = hasInterposer;
            if (hasInterposer) ReadVersion(interposer);
        }
        public void ReadEngineHint(string installPath) => _engineHints[installPath] = FormatEngineHint(EngineType.Unreal, installPath);

        // ── Looked up by the loop (UI thread) ────────────────────────────────
        /// <summary>File version, or null when the file is missing (same as File.Exists + GetFileVersion).</summary>
        public string? Version(string path)
        {
            if (_versions.TryGetValue(path, out var v)) return v;
            Interlocked.Increment(ref _misses);
            return _dlss.GetFileVersion(path);
        }

        /// <summary>The NR DLL's version, read from the real DLL behind an NR Cost Scaler proxy.</summary>
        public string? NrVersion(string path)
        {
            if (_nrVersions.TryGetValue(path, out var v)) return v;
            Interlocked.Increment(ref _misses);
            return ReadNr(path);
        }

        public bool Exists(string path)
        {
            if (_exists.TryGetValue(path, out var e)) return e;
            Interlocked.Increment(ref _misses);
            return File.Exists(path);
        }

        public string EngineHint(string installPath)
        {
            if (_engineHints.TryGetValue(installPath, out var h)) return h;
            Interlocked.Increment(ref _misses);
            return FormatEngineHint(EngineType.Unreal, installPath);
        }

        public string Summary =>
            $"{_versions.Count + _nrVersions.Count} DLL versions, {_engineHints.Count} Unreal versions, {Volatile.Read(ref _misses)} read in the loop";

        private string? ReadNr(string path) =>
            File.Exists(path) ? _dlss.GetFileVersion(DlssNrCostScalerService.ResolveRealNrDllPath(path)) : null;
    }

    /// <summary>
    /// Reads, on a background thread (4 at a time), the file versions and Unreal versions the cache-phase card
    /// loop needs. A failure leaves the rest to the loop, which reads anything missing itself.
    /// </summary>
    private async Task<CacheFilePrefetch> PrefetchCacheFileInfoAsync(SavedGameLibrary savedLib, IReadOnlyList<DetectedGame> allGames)
    {
        var prefetch = new CacheFilePrefetch(_dlssStreamlineService);

        // Collected here (UI thread, in-memory only): the same keys and folders the loop uses
        var versionPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nrPaths      = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var slFolders    = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unrealPaths  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var game in allGames)
        {
            if (savedLib.DlssPathsCache != null
                && savedLib.DlssPathsCache.TryGetValue(GameKey.FromCard(game.Name, game.Source).ToKey(), out var c))
            {
                if (c.DlssPath  != null) versionPaths.Add(c.DlssPath);
                if (c.DlssdPath != null) versionPaths.Add(c.DlssdPath);
                if (c.DlssgPath != null) versionPaths.Add(c.DlssgPath);
                if (c.DlssnrPath != null) nrPaths.Add(c.DlssnrPath);
                if (c.StreamlineFolder != null) slFolders.Add(c.StreamlineFolder);
            }

            if (string.IsNullOrEmpty(game.InstallPath)) continue;
            var rootKey = game.InstallPath.TrimEnd('\\', '/').ToLowerInvariant();
            if (_engineTypeCache.TryGetValue(rootKey, out var cachedEngine)
                && Enum.TryParse<EngineType>(cachedEngine, out var engine) && engine == EngineType.Unreal)
                unrealPaths.Add(_resolvedPathCache.TryGetValue(rootKey, out var resolved) ? resolved : game.InstallPath);
        }

        var work = new List<Action>(versionPaths.Count + nrPaths.Count + slFolders.Count + unrealPaths.Count);
        foreach (var p in versionPaths) work.Add(() => prefetch.ReadVersion(p));
        foreach (var p in nrPaths)      work.Add(() => prefetch.ReadNrVersion(p));
        foreach (var p in slFolders)    work.Add(() => prefetch.ReadStreamline(p));
        foreach (var p in unrealPaths)  work.Add(() => prefetch.ReadEngineHint(p));

        try
        {
            // Its own thread, not a thread-pool job: at startup the pool is busy with other startup work, and a
            // queued Task.Run waited for it (896 ms on a first launch, longer than reading on the UI thread).
            // Parallel.ForEach runs on that thread at once; pool threads join as they free up.
            await Task.Factory.StartNew(
                () => Parallel.ForEach(work, new ParallelOptions { MaxDegreeOfParallelism = 4 }, a => a()),
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
        catch (AggregateException ex)
        {
            // Whatever was read stays usable; the loop reads the rest itself
            _crashReporter.Log($"[MainViewModel.PrefetchCacheFileInfoAsync] Prefetch failed — {ex.InnerException?.Message ?? ex.Message}");
        }
        return prefetch;
    }
}
