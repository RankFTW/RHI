namespace RenoDXCommander.Services;

/// <summary>
/// Single source of truth for all download subdirectory paths.
/// Replaces the scattered DownloadCacheDir references across services.
/// </summary>
public static class DownloadPaths
{
    public static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "downloads");

    public static readonly string Shaders      = Path.Combine(Root, "shaders");
    public static readonly string RenoDX       = Path.Combine(Root, "renodx");
    public static readonly string FrameLimiter = Path.Combine(Root, "framelimiter");
    public static readonly string Misc         = Path.Combine(Root, "misc");
    public static readonly string Luma         = Path.Combine(Root, "luma");

    /// <summary>All category subdirectories, used by migration and directory creation.</summary>
    public static readonly string[] AllSubdirectories = [Shaders, RenoDX, FrameLimiter, Misc, Luma];
}
