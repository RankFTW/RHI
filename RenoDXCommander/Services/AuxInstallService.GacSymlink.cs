// AuxInstallService.GacSymlink.cs — GAC symlink install/uninstall for XNA Framework games (e.g. Terraria)
namespace RenoDXCommander.Services;

public partial class AuxInstallService
{
    /// <summary>
    /// Installs ReShade for an XNA Framework game by creating symbolic links in the
    /// GAC directory. The DLL and reshade.ini are staged in the game's install folder
    /// and symlinked into the GAC path where XNA loads its graphics DLL from.
    /// The reshade.ini gets [INSTALL] BasePath set to the game's install directory
    /// so ReShade finds shaders/presets relative to the game folder.
    /// Requires admin privileges for symlink creation.
    /// </summary>
    public static void InstallGacSymlink(
        string gameInstallPath,
        string gacDirectory,
        string dllFileName,
        bool use32Bit,
        string? screenshotSavePath = null,
        string? overlayHotkey = null,
        string? screenshotHotkey = null)
    {
        if (!Directory.Exists(gacDirectory))
            throw new DirectoryNotFoundException($"GAC directory not found: {gacDirectory}");

        // ── Stage the DLL in the game folder ──────────────────────────────────
        var stagedDllPath = Path.Combine(gameInstallPath, dllFileName);
        var rsStagedPath = use32Bit ? RsStagedPath32 : RsStagedPath64;

        if (!File.Exists(rsStagedPath))
            throw new FileNotFoundException(
                $"ReShade DLL not found in staging directory.\nExpected: {rsStagedPath}");

        File.Copy(rsStagedPath, stagedDllPath, overwrite: true);

        // ── Merge reshade.ini in the game folder with BasePath ────────────────
        if (File.Exists(RsIniPath))
            MergeRsIni(gameInstallPath, screenshotSavePath, overlayHotkey, screenshotHotkey);

        var stagedIniPath = Path.Combine(gameInstallPath, "reshade.ini");

        // Write [INSTALL] BasePath pointing to the game folder
        ApplyIniBasePath(stagedIniPath, gameInstallPath);

        // ── Create symlinks in the GAC directory ──────────────────────────────
        var gacDllLink = Path.Combine(gacDirectory, dllFileName);
        var gacIniLink = Path.Combine(gacDirectory, "reshade.ini");

        // Remove existing symlinks or files before creating new ones
        RemoveIfExists(gacDllLink);
        RemoveIfExists(gacIniLink);

        File.CreateSymbolicLink(gacDllLink, stagedDllPath);
        CrashReporter.Log($"[AuxInstallService.InstallGacSymlink] Symlink: {gacDllLink} → {stagedDllPath}");

        File.CreateSymbolicLink(gacIniLink, stagedIniPath);
        CrashReporter.Log($"[AuxInstallService.InstallGacSymlink] Symlink: {gacIniLink} → {stagedIniPath}");
    }

    /// <summary>
    /// Removes GAC symlinks and the staged DLL/INI from the game folder.
    /// </summary>
    public static void UninstallGacSymlink(
        string gameInstallPath,
        string gacDirectory,
        string dllFileName)
    {
        // Remove symlinks from GAC directory
        var gacDllLink = Path.Combine(gacDirectory, dllFileName);
        var gacIniLink = Path.Combine(gacDirectory, "reshade.ini");

        RemoveIfExists(gacDllLink);
        RemoveIfExists(gacIniLink);

        CrashReporter.Log($"[AuxInstallService.UninstallGacSymlink] Removed GAC symlinks from {gacDirectory}");

        // Remove staged DLL from game folder
        var stagedDllPath = Path.Combine(gameInstallPath, dllFileName);
        RemoveIfExists(stagedDllPath);
    }

    /// <summary>
    /// Checks if GAC symlinks are currently installed for a game.
    /// </summary>
    public static bool IsGacSymlinkInstalled(string gacDirectory, string dllFileName)
    {
        var gacDllLink = Path.Combine(gacDirectory, dllFileName);
        return File.Exists(gacDllLink);
    }

    /// <summary>
    /// Writes or updates the [INSTALL] section in the given reshade.ini file,
    /// setting BasePath to the specified game directory.
    /// </summary>
    private static void ApplyIniBasePath(string iniFilePath, string basePath)
    {
        var ini = File.Exists(iniFilePath)
            ? ParseIni(File.ReadAllLines(iniFilePath))
            : new Dictionary<string, OrderedDict>(StringComparer.OrdinalIgnoreCase);

        const string section = "INSTALL";

        if (!ini.ContainsKey(section))
            ini[section] = new OrderedDict();

        ini[section]["BasePath"] = basePath;

        WriteIni(iniFilePath, ini);
    }

    /// <summary>
    /// Removes a file or symlink if it exists. Handles both regular files and
    /// dangling symbolic links (where the target no longer exists).
    /// </summary>
    private static void RemoveIfExists(string path)
    {
        try
        {
            // FileInfo.Exists follows symlinks and returns false for dangling links.
            // Use the file attributes directly to detect the link itself.
            var fi = new FileInfo(path);
            if (fi.Exists)
            {
                fi.Delete();
                return;
            }

            // Check for dangling symlink: the link file exists on disk but its
            // target is gone, so FileInfo.Exists returns false.
            // LinkTarget is non-null only if the path is a symbolic link.
            if (fi.LinkTarget != null)
            {
                fi.Delete();
                CrashReporter.Log($"[AuxInstallService.RemoveIfExists] Removed dangling symlink '{path}' → '{fi.LinkTarget}'");
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[AuxInstallService.RemoveIfExists] Failed to remove '{path}' — {ex.Message}");
        }
    }
}
