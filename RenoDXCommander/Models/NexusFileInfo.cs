namespace RenoDXCommander.Models;

public record NexusFileInfo(
    int FileId,
    string FileName,
    string Version,
    long SizeKb,
    string Category,
    DateTimeOffset UploadedAt);
