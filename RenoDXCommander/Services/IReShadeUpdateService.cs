namespace RenoDXCommander.Services;

/// <summary>
/// Downloads the latest ReShade with addon support and stages the DLLs.
/// </summary>
public interface IReShadeUpdateService
{
    Task<(string version, string url)?> CheckLatestVersionAsync();

    Task<bool> EnsureLatestAsync(IProgress<(string msg, double pct)>? progress = null);
}
