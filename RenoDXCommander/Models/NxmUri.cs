namespace RenoDXCommander.Models;

public record NxmUri(
    string GameDomain,
    int ModId,
    int FileId,
    string Key,
    long Expires);
