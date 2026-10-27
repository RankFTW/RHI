// SafeJsonFile.cs — crash-safe JSON persistence for RHI's own data files
// (settings.json and the *_installed.json record stores).
//
// - Writes go to a .tmp file, are flushed to disk, then swapped in; the previous good file
//   becomes .bak. A crash or power cut leaves either the old or the new file, never a partial one.
// - A corrupt file is renamed to .corrupt-<time> (kept for inspection) and the .bak is restored.
// - If a file exists but can't be read (locked, access denied), the next write to it is skipped:
//   that write was built from an empty list and would wipe the real data.
// - LockFor(path) returns the per-file lock. Hold it around load → modify → save sequences so two
//   threads can't each save their own copy and drop the other's change. Monitor locks are
//   re-entrant, so Read/Write can be called while holding it.

using System.Collections.Concurrent;
using System.Text.Json;

namespace RenoDXCommander.Services;

public static class SafeJsonFile
{
    public enum LoadStatus
    {
        /// <summary>File read and parsed.</summary>
        Ok,
        /// <summary>No file and no backup.</summary>
        Missing,
        /// <summary>File was corrupt or missing; restored from .bak.</summary>
        Recovered,
        /// <summary>File was corrupt and no usable backup exists. The bad file was kept as .corrupt-*.</summary>
        Corrupt,
        /// <summary>File exists but couldn't be read (locked or access denied).</summary>
        Unreadable,
    }

    private sealed class FileState
    {
        public readonly object Lock = new();
        /// <summary>The file on disk is known to be valid, so it's safe to rotate into .bak.</summary>
        public bool DiskValid;
        /// <summary>The last read failed; block writes until a read succeeds.</summary>
        public bool ReadFailed;
    }

    private static readonly ConcurrentDictionary<string, FileState> _states = new(StringComparer.OrdinalIgnoreCase);

    private static FileState StateFor(string path) => _states.GetOrAdd(Path.GetFullPath(path), _ => new FileState());

    /// <summary>The lock for <paramref name="path"/>. Hold it around load → modify → save.</summary>
    public static object LockFor(string path) => StateFor(path).Lock;

    public static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>
    /// Reads and parses <paramref name="path"/>, recovering from .bak if it's corrupt or missing.
    /// Returns null when there's nothing usable (see <paramref name="status"/>).
    /// </summary>
    public static T? Read<T>(string path, out LoadStatus status, JsonSerializerOptions? options = null) where T : class
    {
        var st = StateFor(path);
        lock (st.Lock)
        {
            var bak = path + ".bak";
            var name = Path.GetFileName(path);

            if (!File.Exists(path))
            {
                st.ReadFailed = false;
                if (TryParseFile<T>(bak, options, out var fromBak))
                {
                    TryRestoreBak(path, st, $"{name} missing");
                    status = LoadStatus.Recovered;
                    return fromBak;
                }
                status = LoadStatus.Missing;
                return null;
            }

            string json;
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    json = File.ReadAllText(path);
                    break;
                }
                catch (IOException ex) when (attempt < 4)
                {
                    CrashReporter.Log($"[SafeJsonFile.Read] {name} read attempt {attempt + 1} failed, retrying — {ex.Message}");
                    Thread.Sleep(50 * (attempt + 1));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    CrashReporter.Log($"[SafeJsonFile.Read] {name} unreadable, saves blocked until it can be read — {ex.GetType().Name}: {ex.Message}");
                    st.ReadFailed = true;
                    status = LoadStatus.Unreadable;
                    return null;
                }
            }

            st.ReadFailed = false;
            if (TryParse<T>(json, options, out var parsed))
            {
                st.DiskValid = true;
                status = LoadStatus.Ok;
                return parsed;
            }

            // Corrupt: keep the bad file for inspection, then fall back to the backup.
            st.DiskValid = false;
            CrashReporter.Log($"[SafeJsonFile.Read] {name} is corrupt ({json.Length} chars)");
            Quarantine(path);

            if (TryParseFile<T>(bak, options, out var recovered))
            {
                TryRestoreBak(path, st, $"{name} corrupt");
                status = LoadStatus.Recovered;
                return recovered;
            }

            CrashReporter.Log($"[SafeJsonFile.Read] No usable {name}.bak — starting empty");
            status = LoadStatus.Corrupt;
            return null;
        }
    }

    /// <summary>Reads a list file; returns an empty list when missing or unusable.</summary>
    public static List<T> ReadList<T>(string path, JsonSerializerOptions? options = null) =>
        Read<List<T>>(path, out _, options) ?? new List<T>();

    /// <summary>
    /// Writes <paramref name="value"/> atomically, keeping the previous good file as .bak.
    /// Returns false (and logs) if the write failed or was blocked by an earlier failed read.
    /// </summary>
    public static bool Write<T>(string path, T value, string callerTag, JsonSerializerOptions? options = null)
    {
        var st = StateFor(path);
        lock (st.Lock)
        {
            var name = Path.GetFileName(path);
            if (st.ReadFailed)
            {
                CrashReporter.Log($"[{callerTag}] {name} couldn't be read earlier — save skipped so it isn't overwritten");
                return false;
            }

            string json;
            try { json = JsonSerializer.Serialize(value, options); }
            catch (NotSupportedException ex)
            {
                CrashReporter.Log($"[{callerTag}] {name} serialize failed — {ex.Message}");
                return false;
            }

            var tmp = path + ".tmp";
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                    // Flush to disk before swapping, so a power cut can't leave an empty file.
                    using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var w = new StreamWriter(fs))
                    {
                        w.Write(json);
                        w.Flush();
                        fs.Flush(flushToDisk: true);
                    }

                    if (st.DiskValid && File.Exists(path))
                        File.Replace(tmp, path, path + ".bak", ignoreMetadataErrors: true);
                    else
                        File.Move(tmp, path, overwrite: true);

                    st.DiskValid = true;
                    return true;
                }
                catch (IOException ex) when (attempt < 2)
                {
                    CrashReporter.Log($"[{callerTag}] {name} write attempt {attempt + 1} failed, retrying — {ex.Message}");
                    Thread.Sleep(50 * (attempt + 1));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    CrashReporter.Log($"[{callerTag}] {name} write failed — {ex.GetType().Name}: {ex.Message}");
                    try { if (File.Exists(tmp)) File.Delete(tmp); }
                    catch (Exception cleanupEx) when (cleanupEx is IOException or UnauthorizedAccessException)
                    {
                        CrashReporter.Log($"[{callerTag}] Couldn't remove {name}.tmp — {cleanupEx.Message}");
                    }
                    return false;
                }
            }
        }
    }

    /// <summary>
    /// Load → modify → save under the file's lock. Skips the save if the file couldn't be read.
    /// Exceptions thrown by <paramref name="mutate"/> propagate to the caller.
    /// </summary>
    public static bool Update<T>(string path, Action<T> mutate, string callerTag, JsonSerializerOptions? options = null)
        where T : class, new()
    {
        lock (LockFor(path))
        {
            var value = Read<T>(path, out var status, options);
            if (status == LoadStatus.Unreadable)
            {
                CrashReporter.Log($"[{callerTag}] {Path.GetFileName(path)} unreadable — save skipped so it isn't overwritten");
                return false;
            }
            value ??= new T();
            mutate(value);
            return Write(path, value, callerTag, options);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static bool TryParse<T>(string json, JsonSerializerOptions? options, out T result) where T : class
    {
        try
        {
            var v = JsonSerializer.Deserialize<T>(json, options);
            if (v != null) { result = v; return true; }
        }
        catch (JsonException) { /* corrupt — caller handles */ }
        result = null!;
        return false;
    }

    private static bool TryParseFile<T>(string path, JsonSerializerOptions? options, out T result) where T : class
    {
        result = null!;
        try
        {
            return File.Exists(path) && TryParse(File.ReadAllText(path), options, out result);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[SafeJsonFile] Couldn't read {Path.GetFileName(path)} — {ex.Message}");
            return false;
        }
    }

    private static void Quarantine(string path)
    {
        var dest = $"{path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
        try
        {
            File.Move(path, dest, overwrite: true);
            CrashReporter.Log($"[SafeJsonFile] Corrupt file kept as {Path.GetFileName(dest)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[SafeJsonFile] Couldn't set aside corrupt {Path.GetFileName(path)} — {ex.Message}");
        }
    }

    private static void TryRestoreBak(string path, FileState st, string reason)
    {
        try
        {
            File.Copy(path + ".bak", path, overwrite: true);
            st.DiskValid = true;
            CrashReporter.Log($"[SafeJsonFile] {reason} — restored from .bak");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[SafeJsonFile] {reason} — loaded .bak but couldn't restore it — {ex.Message}");
        }
    }
}
