// SeenModsStore.cs — Tracks which mods the user has "seen" so new ones can be highlighted.
// One store per list (wiki, Ultra+, Luma); each is a one-line subclass naming its file.

using System.Text.Json;

namespace RenoDXCommander.Services;

/// <summary>
/// Persists the set of mod names the user has acknowledged ("seen") in a JSON list under
/// <c>%LocalAppData%\RHI</c>. On refresh, any mods not in this set are "new" and can be highlighted.
/// Names match without regard to case.
/// </summary>
public abstract class SeenModsStore
{
    private readonly string _filePath;
    private HashSet<string> _seenMods = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;

    /// <param name="fileName">File name in RHI's data folder, e.g. <c>seen_wiki_mods.json</c>.</param>
    protected SeenModsStore(string fileName) : this(fileName, null) { }

    /// <param name="fileName">File name of the list.</param>
    /// <param name="directory">Folder to keep it in; null = RHI's data folder (tests pass a temp folder).</param>
    protected SeenModsStore(string fileName, string? directory)
    {
        _filePath = Path.Combine(directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI"), fileName);
    }

    private string LogName => GetType().Name;

    /// <summary>Loads the seen set from disk. Safe to call multiple times (no-op after the first load).</summary>
    public void Load()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (File.Exists(_filePath))
            {
                var list = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_filePath));
                if (list != null)
                    _seenMods = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A corrupt or unreadable file loads as empty (start fresh)
            CrashReporter.Log($"[{LogName}.Load] Couldn't read '{Path.GetFileName(_filePath)}' — starting fresh — {ex.Message}");
            _seenMods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>Returns the set of mod names the user has already seen.</summary>
    public HashSet<string> GetSeenMods()
    {
        Load();
        return _seenMods;
    }

    /// <summary>Marks the given mod names as "seen" and saves.</summary>
    public void MarkAsSeen(IEnumerable<string> modNames)
    {
        Load();
        foreach (var name in modNames)
            _seenMods.Add(name);
        Save();
    }

    /// <summary>Seeds the seen set with all current mods (first launch, so nothing shows as "new").</summary>
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
    /// Given the current mod list, returns the names of mods not yet seen.
    /// Reloads from disk to pick up any manual edits to the seen file.
    /// </summary>
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
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(_seenMods.ToList(), new JsonSerializerOptions { WriteIndented = true });
            FileHelper.WriteAllTextAtomic(_filePath, json, $"{LogName}.Save");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not critical: the mods show as new again next time
            CrashReporter.Log($"[{LogName}.Save] Failed to save '{Path.GetFileName(_filePath)}' — {ex.Message}");
        }
    }
}

/// <summary>Wiki mods the user has seen (<c>seen_wiki_mods.json</c>).</summary>
public class SeenWikiModsService() : SeenModsStore("seen_wiki_mods.json");

/// <summary>Ultra+ mods the user has seen (<c>seen_ultraplus_mods.json</c>).</summary>
public class SeenUltraPlusModsService() : SeenModsStore("seen_ultraplus_mods.json");

/// <summary>Luma completed mods the user has seen (<c>seen_luma_mods.json</c>).</summary>
public class SeenLumaModsService() : SeenModsStore("seen_luma_mods.json");
