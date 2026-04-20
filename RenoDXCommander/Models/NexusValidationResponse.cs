namespace RenoDXCommander.Models;

public record NexusValidationResponse(
    string Name,
    string Email,
    string ProfileUrl,
    bool IsPremium,
    bool IsSupporter,
    string Key);
