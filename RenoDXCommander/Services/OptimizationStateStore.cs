using System.Text.Json;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>What RHI remembers about one game between "Optimize" and "Undo Optimization".</summary>
public sealed class OptimizationRecord
{
    /// <summary>State captured immediately before the most recent optimize (null when there is nothing to undo).</summary>
    public OptimizationRestorePoint? RestorePoint { get; set; }
    /// <summary>Fingerprint of the defaults applied by the most recent optimize ("Defaults changed" detection).</summary>
    public string? LastAppliedFingerprint { get; set; }
    public DateTime? LastAppliedUtc { get; set; }
}

/// <summary>
/// Persists <see cref="OptimizationRecord"/>s in %LocalAppData%\RHI\optimization_state.json — a small, separate file so
/// game_library.json and settings.json are never touched. Only the most recent restore point per game is kept.
/// </summary>
public sealed class OptimizationStateStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private readonly string _path;
    private readonly object _gate = new();

    public OptimizationStateStore() : this(DefaultPath) { }
    public OptimizationStateStore(string path) => _path = path;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "optimization_state.json");

    private Dictionary<string, OptimizationRecord> Load()
    {
        try
        {
            if (!File.Exists(_path)) return new(StringComparer.OrdinalIgnoreCase);
            var d = JsonSerializer.Deserialize<Dictionary<string, OptimizationRecord>>(File.ReadAllText(_path));
            return d == null ? new(StringComparer.OrdinalIgnoreCase) : new(d, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[OptimizationStateStore] Could not read '{_path}' — {ex.Message}");
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void Save(Dictionary<string, OptimizationRecord> all)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(all, JsonOpts));
        File.Move(tmp, _path, overwrite: true);   // atomic swap: a crash never leaves a half-written file
    }

    public OptimizationRecord? Get(string gameKey)
    {
        lock (_gate) return Load().TryGetValue(gameKey, out var r) ? r : null;
    }

    public void Set(string gameKey, OptimizationRecord record)
    {
        lock (_gate)
        {
            var all = Load();
            all[gameKey] = record;
            Save(all);
        }
    }

    public void Remove(string gameKey)
    {
        lock (_gate)
        {
            var all = Load();
            if (all.Remove(gameKey)) Save(all);
        }
    }
}
