using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Game state used by OptiScaler installation. No UI toolkit or dispatcher is required.
/// Platform adapters retain ownership of notifications and game-file transactions.
/// </summary>
public interface IOptiScalerGame
{
    string GameName { get; }
    string InstallPath { get; }
    string Source { get; }
    GraphicsApiType GraphicsApi { get; }
    HashSet<GraphicsApiType> DetectedApis { get; }
    AuxInstalledRecord? RsRecord { get; }
    string? RsInstalledFile { get; set; }
    string? OsInstalledFile { get; set; }
    string? OsInstalledVersion { get; set; }
    GameStatus OsStatus { get; set; }
}
