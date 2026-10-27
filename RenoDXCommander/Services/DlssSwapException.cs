namespace RenoDXCommander.Services;

/// <summary>
/// Thrown when a DLSS / Streamline DLL can't be written into a game folder.
/// <see cref="Exception.Message"/> is a plain-English explanation that can be shown
/// to the user as-is; the original exception is kept in <see cref="Exception.InnerException"/>.
/// </summary>
public sealed class DlssSwapException : Exception
{
    public string TargetPath { get; }

    private DlssSwapException(string targetPath, string message, Exception inner)
        : base(message, inner)
    {
        TargetPath = targetPath;
    }

    /// <summary>Builds a user-facing exception from a file-system error.</summary>
    public static DlssSwapException From(string targetPath, Exception inner)
        => new(targetPath, Describe(targetPath, inner), inner);

    internal static bool IsWindowsAppsPath(string path)
        => path.Replace('/', '\\').Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase);

    internal static string Describe(string targetPath, Exception inner)
    {
        bool windowsApps = IsWindowsAppsPath(targetPath);
        return inner switch
        {
            DirectoryNotFoundException when windowsApps =>
                "This Game Pass game's folder has changed, most likely because the game updated. " +
                "Click Refresh in RHI, then try again.",
            DirectoryNotFoundException =>
                "The game folder no longer exists at this location. The game may have been moved, updated or uninstalled. " +
                "Click Refresh in RHI, then try again.",
            UnauthorizedAccessException when windowsApps =>
                "Windows blocks changes to Game Pass games installed in the protected WindowsApps folder, " +
                "so RHI can't change DLSS files for this game.",
            UnauthorizedAccessException =>
                "Windows denied access to the game folder. Try running RHI as administrator.",
            IOException =>
                "The DLSS file is in use. Close the game and try again.",
            _ => inner.Message,
        };
    }
}
