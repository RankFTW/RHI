// SeenWikiModsService.cs — Tracks which wiki mods the user has "seen" so we can highlight new ones.

using System.Text.Json;

namespace RenoDXCommander.Services;

/// <summary>
/// Persists the set of wiki mod names the user has acknowledged ("seen").
/// On refresh, any mods not in this set are considered "new" and can be highlighted in the UI.
/// </summary>
public class SeenWikiModsService
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "seen_wiki_mods.json");

    private HashSet<string> _seenMods = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;

    /// <summary>
    /// Loads the seen mods set from disk. Safe to call multiple times (no-op after first load).
    /// </summary>
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
        catch
        {
            // Ignore errors — start fresh
            _seenMods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Returns the set of mod names the user has already seen.
    /// </summary>
    public HashSet<string> GetSeenMods()
    {
        Load();
        return _seenMods;
    }

    /// <summary>
    /// Marks the given mod names as "seen" and persists to disk.
    /// </summary>
    public void MarkAsSeen(IEnumerable<string> modNames)
    {
        Load();
        foreach (var name in modNames)
            _seenMods.Add(name);
        Save();
    }

    /// <summary>
    /// Seeds the seen set with all current mods (used on first launch to avoid showing everything as "new").
    /// </summary>
    public void SeedIfEmpty(IEnumerable<string> allModNames)
    {
        Load();
        if (_seenMods.Count == 0)
        {
            foreach (var name in allModNames)
                _seenMods.Add(name);
            Save();
        }
    }

    /// <summary>
    /// Given the current wiki mod list, returns the names of mods not yet seen.
    /// Reloads from disk to pick up any manual edits to the seen file.
    /// </summary>
    public List<string> GetNewMods(IEnumerable<string> currentModNames)
    {
        // Force reload from disk so manual edits take effect
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

            var json = JsonSerializer.Serialize(_seenMods.ToList(), new JsonSerializerOptions { WriteIndented = true });
            FileHelper.WriteAllTextAtomic(FilePath, json, "SeenWikiModsService.Save");
        }
        catch
        {
            // Ignore save errors — not critical
        }
    }
}
