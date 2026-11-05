// GameNameService.CustomFolders.cs -- the global "Custom game folders" list and the candidate folders
// the user chose to ignore. Saved in settings.json ("CustomGameFolders", "IgnoredCustomGameFolders").
// Copy-on-write: the debounced save serializes on a timer thread while the UI may be changing the list.

namespace RenoDXCommander.Services;

public partial class GameNameService
{
    private volatile IReadOnlyList<string> _customGameFolders = Array.Empty<string>();
    private volatile IReadOnlySet<string> _ignoredCustomGameFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> CustomGameFolders => _customGameFolders;
    public IReadOnlySet<string> IgnoredCustomGameFolders => _ignoredCustomGameFolders;

    public void SetCustomGameFolders(IEnumerable<string> folders)
        => _customGameFolders = folders
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(CustomFolderScanService.NormalizePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public void AddIgnoredCustomGameFolders(IEnumerable<string> folders)
    {
        var set = new HashSet<string>(_ignoredCustomGameFolders, StringComparer.OrdinalIgnoreCase);
        foreach (var f in folders)
            if (!string.IsNullOrWhiteSpace(f)) set.Add(CustomFolderScanService.NormalizePath(f));
        _ignoredCustomGameFolders = set;
    }

    public void ClearIgnoredCustomGameFolders()
        => _ignoredCustomGameFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private void LoadCustomGameFolders(Func<string, List<string>> load)
    {
        SetCustomGameFolders(load("CustomGameFolders"));
        _ignoredCustomGameFolders = new HashSet<string>(
            load("IgnoredCustomGameFolders").Where(f => !string.IsNullOrWhiteSpace(f)).Select(CustomFolderScanService.NormalizePath),
            StringComparer.OrdinalIgnoreCase);
    }

    private void SaveCustomGameFolders(Dictionary<string, string> s)
    {
        var folders = _customGameFolders;
        var ignored = _ignoredCustomGameFolders;
        if (folders.Count > 0) s["CustomGameFolders"] = System.Text.Json.JsonSerializer.Serialize(folders.ToList());
        else s.Remove("CustomGameFolders");
        if (ignored.Count > 0) s["IgnoredCustomGameFolders"] = System.Text.Json.JsonSerializer.Serialize(ignored.ToList());
        else s.Remove("IgnoredCustomGameFolders");
    }
}
