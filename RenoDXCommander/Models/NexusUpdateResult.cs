namespace RenoDXCommander.Models;

public record NexusUpdateResult(
    string GameDomain,
    int ModId,
    NexusFileInfo LatestFile,
    bool UpdateAvailable,
    DateTimeOffset CheckedAt);
