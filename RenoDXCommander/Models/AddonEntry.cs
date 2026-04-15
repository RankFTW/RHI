namespace RenoDXCommander.Models;

public record AddonEntry(
    string SectionId,
    string PackageName,
    string? PackageDescription,
    string? DownloadUrl,
    string? DownloadUrl32,
    string? DownloadUrl64,
    string? RepositoryUrl,
    string? EffectInstallPath,
    string? DeployFileName = null);
