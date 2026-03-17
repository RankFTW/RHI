namespace RenoDXCommander.Services;

/// <summary>
/// Downloads and maintains Lilium's HDR ReShade shaders from GitHub.
/// </summary>
public interface ILiliumShaderService
{
    Task EnsureLatestAsync(IProgress<string>? progress = null);

    void DeployToDcFolder();

    void DeployToGameFolder(string gameDir);

    void RemoveFromGameFolder(string gameDir);
}
