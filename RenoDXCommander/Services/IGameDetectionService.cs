using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Detects installed games from various launchers and identifies game engines.
/// </summary>
public interface IGameDetectionService
{
    List<DetectedGame> FindSteamGames();

    List<DetectedGame> FindGogGames();

    List<DetectedGame> FindEpicGames();

    List<DetectedGame> FindEaGames();

    List<DetectedGame> FindXboxGames();

    List<DetectedGame> FindUbisoftGames();

    List<DetectedGame> FindBattleNetGames();

    List<DetectedGame> FindRockstarGames();

    (string installPath, EngineType engine) DetectEngineAndPath(string rootPath);

    GameMod? MatchGame(
        DetectedGame game,
        IEnumerable<GameMod> mods,
        Dictionary<string, string>? nameMappings = null);

    string NormalizeName(string name);
}
