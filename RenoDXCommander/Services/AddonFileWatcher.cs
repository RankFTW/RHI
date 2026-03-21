using System.IO;

namespace RenoDXCommander.Services;

/// <summary>
/// Watches the user's Downloads folder for new .addon64/.addon32 files
/// and raises an event when one appears.
/// </summary>
public sealed class AddonFileWatcher : IDisposable
{
    private FileSystemWatcher? _watcher;
    private readonly ICrashReporter _crashReporter;
    private string _watchPath;

    /// <summary>Raised on the thread-pool when a new addon file is detected.</summary>
    public event Action<string>? AddonFileDetected;

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
        if (!string.Equals(ext, ".addon64", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(ext, ".addon32", StringComparison.OrdinalIgnoreCase))
            return;

        var fileName = Path.GetFileName(path);
        if (!fileName.StartsWith("renodx-", StringComparison.OrdinalIgnoreCase))
            return;

        _crashReporter.Log($"[AddonFileWatcher] Detected addon file: {Path.GetFileName(path)}");

        // Wait for the file to exist and be unlocked (browser may still be writing)
        _ = Task.Run(async () =>
        {
            for (int i = 0; i < 20; i++) // up to 10 seconds
            {
                await Task.Delay(500);
                try
                {
                    if (!File.Exists(path)) continue;
                    // Try opening to confirm it's not locked
                    using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    _crashReporter.Log($"[AddonFileWatcher] File ready: {Path.GetFileName(path)}");
                    AddonFileDetected?.Invoke(path);
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
