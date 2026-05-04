using System.IO;

namespace RenoDXCommander.Services;

/// <summary>
/// Watches the user's Downloads folder for new .addon64/.addon32 files
/// and archive files (.zip, .7z, .rar) containing renodx addons.
/// </summary>
public sealed class AddonFileWatcher : IDisposable
{
    private FileSystemWatcher? _watcher;
    private readonly ICrashReporter _crashReporter;
    private string _watchPath;

    /// <summary>
    /// Tracks recently processed file paths to prevent duplicate installs.
    /// Browser downloads trigger both Created and Renamed events for the same file.
    /// </summary>
    private readonly Dictionary<string, DateTime> _recentFiles = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan DedupeWindow = TimeSpan.FromSeconds(5);

    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".7z", ".rar"
    };

    /// <summary>Raised on the thread-pool when a new addon file is detected.</summary>
    public event Action<string>? AddonFileDetected;

    /// <summary>Raised on the thread-pool when an archive containing renodx files is detected.</summary>
    public event Action<string>? ArchiveFileDetected;

    public AddonFileWatcher(ICrashReporter crashReporter)
    {
        _crashReporter = crashReporter;
        _watchPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    public void SetWatchPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        _watchPath = path;
        Stop();
        Start();
    }

    private void Stop()
    {
        if (_watcher != null)
        {
            _watcher.Created -= OnFileEvent;
            _watcher.Renamed -= OnFileRenamed;
            _watcher.Dispose();
            _watcher = null;
        }
    }

    public void Start()
    {
        if (!Directory.Exists(_watchPath))
        {
            _crashReporter.Log($"[AddonFileWatcher] Watch folder not found: {_watchPath}");
            return;
        }

        try
        {
            _watcher = new FileSystemWatcher(_watchPath)
            {
                NotifyFilter = NotifyFilters.FileName,
                EnableRaisingEvents = true,
                IncludeSubdirectories = false,
            };

            // Created catches direct saves; Renamed catches browser temp→final renames
            _watcher.Created += OnFileEvent;
            _watcher.Renamed += OnFileRenamed;

            _crashReporter.Log($"[AddonFileWatcher] Watching '{_watchPath}' for addon files");
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[AddonFileWatcher] Failed to start — {ex.Message}");
        }
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e) => ScheduleCheck(e.FullPath);
    private void OnFileRenamed(object sender, RenamedEventArgs e) => ScheduleCheck(e.FullPath);

    private void ScheduleCheck(string path)
    {
        var ext = Path.GetExtension(path);

        // Deduplicate: ignore if we've already seen this file recently.
        // Browser downloads trigger both Created and Renamed events.
        lock (_recentFiles)
        {
            var now = DateTime.UtcNow;
            // Clean up old entries
            var stale = _recentFiles.Where(kv => now - kv.Value > DedupeWindow).Select(kv => kv.Key).ToList();
            foreach (var key in stale) _recentFiles.Remove(key);

            if (_recentFiles.ContainsKey(path))
                return; // already processing this file
            _recentFiles[path] = now;
        }

        // Check for addon files
        if (string.Equals(ext, ".addon64", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".addon32", StringComparison.OrdinalIgnoreCase))
        {
            var fileName = Path.GetFileName(path);
            if (!fileName.StartsWith("renodx-", StringComparison.OrdinalIgnoreCase))
                return;

            _crashReporter.Log($"[AddonFileWatcher] Detected addon file: {Path.GetFileName(path)}");
            WaitAndRaise(path, AddonFileDetected);
            return;
        }

        // Check for archive files containing "renodx" in the name
        if (ArchiveExtensions.Contains(ext))
        {
            var fileName = Path.GetFileName(path);
            if (!fileName.Contains("renodx", StringComparison.OrdinalIgnoreCase))
                return;

            _crashReporter.Log($"[AddonFileWatcher] Detected archive with renodx: {fileName}");
            WaitAndRaise(path, ArchiveFileDetected);
            return;
        }
    }

    private void WaitAndRaise(string path, Action<string>? handler)
    {
        if (handler == null) return;

        // Wait for the file to exist and be unlocked (browser may still be writing)
        _ = Task.Run(async () =>
        {
            for (int i = 0; i < 20; i++) // up to 10 seconds
            {
                await Task.Delay(500);
                try
                {
                    if (!File.Exists(path)) continue;
                    using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    _crashReporter.Log($"[AddonFileWatcher] File ready: {Path.GetFileName(path)}");
                    handler.Invoke(path);
                    return;
                }
                catch (IOException) { /* still locked, retry */ }
                catch (UnauthorizedAccessException) { /* still locked, retry */ }
            }
            _crashReporter.Log($"[AddonFileWatcher] Timed out waiting for file: {Path.GetFileName(path)}");
        });
    }

    public void Dispose() => Stop();
}
