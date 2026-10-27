using System.Text.Json;
using System.Text.Json.Serialization;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Records component update events (downloads of new versions) and persists them to disk.
/// Entries are capped at 200. Writes are debounced by 1 second.
/// </summary>
public class UpdateLogService : IUpdateLogService
{
    private const int MaxEntries = 200;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "update_log.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented  = true,
        Converters     = { new JsonStringEnumConverter() },
    };

    private readonly List<UpdateLogEntry> _entries = new();
    private readonly object               _lock    = new();
    private System.Threading.Timer?       _debounce;

    public UpdateLogService()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json   = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<List<UpdateLogEntry>>(json, JsonOptions);
                if (loaded != null)
                {
                    // newest-first in memory; cap at max
                    _entries.AddRange(loaded.OrderByDescending(e => e.Timestamp).Take(MaxEntries));
                }
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[UpdateLogService.ctor] Load failed — {ex.Message}");
        }
    }

    // ── Public API ────────────────────────────────────────────────────────

    public void Record(UpdateLogEntry entry)
    {
        lock (_lock)
        {
            // Deduplicate: skip if the most recent entry for this component already has the same new version
            var existing = _entries.FirstOrDefault(e =>
                string.Equals(e.ComponentName, entry.ComponentName, StringComparison.OrdinalIgnoreCase));
            if (existing != null &&
                string.Equals(existing.NewVersion, entry.NewVersion, StringComparison.OrdinalIgnoreCase))
                return;

            _entries.Insert(0, entry);

            // Trim to cap
            if (_entries.Count > MaxEntries)
                _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
        }

        ScheduleSave();
    }

    public IReadOnlyList<UpdateLogEntry> GetAll()
    {
        lock (_lock)
            return _entries.ToList();
    }

    public void Clear()
    {
        lock (_lock)
            _entries.Clear();

        try
        {
            if (File.Exists(FilePath))
                File.Delete(FilePath);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[UpdateLogService.Clear] {ex.Message}");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private void ScheduleSave()
    {
        // Debounce: restart the 1-second timer on every Record call
        _debounce?.Change(Timeout.Infinite, Timeout.Infinite);
        _debounce = new System.Threading.Timer(_ => Save(), null, 1000, Timeout.Infinite);
    }

    private void Save()
    {
        List<UpdateLogEntry> snapshot;
        lock (_lock)
            snapshot = _entries.ToList();

        try
        {
            var dir = Path.GetDirectoryName(FilePath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            File.WriteAllText(FilePath, json);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[UpdateLogService.Save] {ex.Message}");
        }
    }
}
