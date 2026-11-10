// GameNameService.DisplayModes.cs -- Display modes (Monitor / TV / ...) saved in settings.json as "DisplayModes"
// (DisplayModeStore.Snapshot: the modes with their defaults, the default list of swapped items, and each game's state
// keyed "GameName|Store"). RenameGame moves a game's state to its new name (DisplayModeStore.MigrateGameRename).
// The on/off switch for the feature is SettingsViewModel.DisplayModesEnabled.

using System.Text.Json;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public partial class GameNameService
{
    private readonly DisplayModeStore _displayModes = new();

    public DisplayModeStore DisplayModes => _displayModes;

    private void LoadDisplayModes(Dictionary<string, string> s)
    {
        DisplayModeData? saved = null;
        try
        {
            if (s.TryGetValue("DisplayModes", out var json) && !string.IsNullOrEmpty(json))
                saved = JsonSerializer.Deserialize<DisplayModeData>(json);
        }
        catch (JsonException ex)
        {
            CrashReporter.Log($"[GameNameService.LoadDisplayModes] Couldn't read DisplayModes — {ex.Message}");
        }
        _displayModes.Load(saved);
        if (saved != null)
            CrashReporter.Log($"[GameNameService.LoadDisplayModes] {_displayModes.Modes.Count} mode(s), {saved.Games?.Count ?? 0} game(s)");
    }

    private void SaveDisplayModes(Dictionary<string, string> s)
        => s["DisplayModes"] = JsonSerializer.Serialize(_displayModes.Snapshot());
}
