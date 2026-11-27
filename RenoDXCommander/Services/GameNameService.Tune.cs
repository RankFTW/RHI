// GameNameService.Tune.cs -- Tune presets and tuned games saved in settings.json as "TunePresets" (TunePresetStore.Snapshot).
// RenameGame moves a tuned game's record to its new name (TunePresetStore.MigrateGameRename).
// The on/off switch for the game page button is SettingsViewModel.TunePresetsEnabled.

using System.Text.Json;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public partial class GameNameService
{
    private readonly TunePresetStore _tunePresets = new();

    public TunePresetStore TunePresets => _tunePresets;

    private void LoadTunePresets(Dictionary<string, string> s)
    {
        TuneData? saved = null;
        try
        {
            if (s.TryGetValue("TunePresets", out var json) && !string.IsNullOrEmpty(json))
                saved = JsonSerializer.Deserialize<TuneData>(json);
        }
        catch (JsonException ex)
        {
            CrashReporter.Log($"[GameNameService.LoadTunePresets] Couldn't read TunePresets — {ex.Message}");
        }
        _tunePresets.Load(saved);
        if (saved != null)
            CrashReporter.Log($"[GameNameService.LoadTunePresets] {_tunePresets.Presets.Count} preset(s), {saved.Games?.Count ?? 0} tuned game(s)");
    }

    private void SaveTunePresets(Dictionary<string, string> s)
        => s["TunePresets"] = JsonSerializer.Serialize(_tunePresets.Snapshot());
}
