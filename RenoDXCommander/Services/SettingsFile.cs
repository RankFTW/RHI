// SettingsFile.cs — the one place that reads and writes %LocalAppData%\RHI\settings.json.
// Several services store keys in the same file, so every read-modify-write must go through
// Update() under one lock, or one writer overwrites another's keys with stale values.
// Crash safety (.tmp + .bak, corrupt-file recovery, no save over an unreadable file) comes
// from SafeJsonFile.

namespace RenoDXCommander.Services;

public static class SettingsFile
{
    public static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "settings.json");

    private static object Lock => SafeJsonFile.LockFor(FilePath);

    // Read-only snapshot for frequent readers, reused while the file is unchanged.
    private static Dictionary<string, string>? _snapshot;
    private static DateTime _snapshotWriteTime;
    private static long _snapshotLength;

    /// <summary>Loads a fresh, mutable copy of the settings.</summary>
    public static Dictionary<string, string> Load() => Load(out _);

    /// <summary>Loads a fresh, mutable copy of the settings and reports how the load went.</summary>
    public static Dictionary<string, string> Load(out SafeJsonFile.LoadStatus status) =>
        SafeJsonFile.Read<Dictionary<string, string>>(FilePath, out status) ?? NewDict();

    /// <summary>
    /// Read-only view for frequent lookups (e.g. per-pack, per-game loops). Re-reads the file
    /// only when it changed on disk. Don't cast and modify it; use <see cref="Update"/>.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Snapshot()
    {
        lock (Lock)
        {
            try
            {
                var fi = new FileInfo(FilePath);
                if (_snapshot != null && fi.Exists
                    && fi.LastWriteTimeUtc == _snapshotWriteTime && fi.Length == _snapshotLength)
                    return _snapshot;

                var d = Load(out _);
                fi.Refresh();
                _snapshot = d;
                _snapshotWriteTime = fi.Exists ? fi.LastWriteTimeUtc : default;
                _snapshotLength = fi.Exists ? fi.Length : -1;
                return d;
            }
            catch (IOException ex)
            {
                CrashReporter.Log($"[SettingsFile.Snapshot] Failed to stat settings — {ex.Message}");
                return _snapshot ?? NewDict();
            }
        }
    }

    /// <summary>
    /// Read-modify-write under the shared lock. Skips the write (returns false) if the existing
    /// file couldn't be read, so a locked file is never replaced with a partial copy.
    /// Exceptions thrown by <paramref name="mutate"/> propagate to the caller.
    /// </summary>
    public static bool Update(Action<Dictionary<string, string>> mutate, string callerTag)
    {
        lock (Lock)
        {
            var ok = SafeJsonFile.Update<Dictionary<string, string>>(FilePath, mutate, callerTag);
            _snapshot = null;
            return ok;
        }
    }

    private static Dictionary<string, string> NewDict() => new(StringComparer.OrdinalIgnoreCase);
}
