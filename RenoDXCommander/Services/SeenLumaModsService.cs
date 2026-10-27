// SeenLumaModsService.cs — Tracks which Luma completed mods the user has "seen" so we can highlight new ones.

using System.Text.Json;

namespace RenoDXCommander.Services;

/// <summary>
/// Persists the set of Luma completed mod names the user has acknowledged ("seen").
/// On refresh, any mods not in this set are considered "new" and can be highlighted in the UI.
/// </summary>
public class SeenLumaModsService
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "seen_luma_mods.json");

    private HashSet<string> _seenMods = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;

    public void Load()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var list = JsonSerializer.Deserialize<List<string>>(json);
                if (list != null)
                    _seenMods = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch { _seenMods = new HashSet<string>(StringComparer.OrdinalIgnoreCase); }
    }

    public HashSet<string> GetSeenMods() { Load(); return _seenMods; }

    public void MarkAsSeen(IEnumerable<string> modNames)
    {
        Load();
        foreach (var name in modNames) _seenMods.Add(name);
        Save();
    }

    public void SeedIfEmpty(IEnumerable<string> allModNames)
    {
        Load();
        if (_seenMods.Count == 0)
        {
            foreach (var name in allModNames) _seenMods.Add(name);
            Save();
        }
    }

    public List<string> GetNewMods(IEnumerable<string> currentModNames)
    {
        _loaded = false;
        Load();
        return currentModNames
            .Where(name => !_seenMods.Contains(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_seenMods.ToList(), new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { CrashReporter.Log($"[SeenLumaModsService.Save] Failed to save seen Luma mods — {ex.Message}"); }
    }
}
