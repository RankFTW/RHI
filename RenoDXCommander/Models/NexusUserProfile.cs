namespace RenoDXCommander.Models;

public record NexusUserProfile(
    string Name,
    string Email,
    string ProfileUrl,
    bool IsPremium,
    bool IsSupporter);
