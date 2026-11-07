// GameNameService.Collections.cs -- the user's game collections, saved in settings.json beside
// FavouriteGames ("GameCollections" = ordered list of { Name, Games["GameName|Store"] },
// "ActiveCollection" = the collection shown in the game list). RenameGame moves memberships to the
// new name (GameCollectionStore.MigrateGameRename). Games that are no longer detected stay in them.

using System.Text.Json;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public partial class GameNameService
{
    private readonly GameCollectionStore _collections = new();

    public GameCollectionStore Collections => _collections;

    private void LoadGameCollections(Dictionary<string, string> s)
    {
        List<GameCollection>? saved = null;
        try
        {
            if (s.TryGetValue("GameCollections", out var json) && !string.IsNullOrEmpty(json))
                saved = JsonSerializer.Deserialize<List<GameCollection>>(json);
        }
        catch (JsonException ex)
        {
            CrashReporter.Log($"[GameNameService.LoadGameCollections] Couldn't read GameCollections — {ex.Message}");
        }
        s.TryGetValue("ActiveCollection", out var active);
        _collections.Load(saved, string.IsNullOrWhiteSpace(active) ? null : active);
        if (_collections.Count > 0)
            CrashReporter.Log($"[GameNameService.LoadGameCollections] {_collections.Count} collection(s), active '{_collections.ActiveName ?? "none"}'");
    }

    private void SaveGameCollections(Dictionary<string, string> s)
    {
        var snapshot = _collections.Snapshot();
        if (snapshot.Count > 0) s["GameCollections"] = JsonSerializer.Serialize(snapshot);
        else s.Remove("GameCollections");
        var active = _collections.ActiveName;
        if (!string.IsNullOrEmpty(active)) s["ActiveCollection"] = active;
        else s.Remove("ActiveCollection");
    }
}
