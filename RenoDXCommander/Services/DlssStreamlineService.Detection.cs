using System.Diagnostics;

namespace RenoDXCommander.Services;

public partial class DlssStreamlineService
{
    // ── Detection ─────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public DlssDetectionResult Detect(string installPath)
    {
        var result = new DlssDetectionResult();

        if (string.IsNullOrEmpty(installPath) || !Directory.Exists(installPath))
            return result;

        // For WindowsApps packages, the installPath points to a deep subfolder (e.g. ...\Binaries\WinGDK)
        // but DLSS DLLs are often in sibling paths (e.g. ...\Engine\Plugins\...).
        // Search from the package root instead.
        var searchRoot = ResolveSearchRoot(installPath);

        try
        {
            // Walk the directory tree manually to handle access-denied subdirectories gracefully.
            // This is critical for WindowsApps and other restricted folders.
            SearchDirectory(searchRoot, result);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DlssStreamlineService.Detect] Error scanning '{searchRoot}' — {ex.Message}");
        }

        // Fall back to OptiScaler directory copies if no deeper game copy was found
        if (result.DlssPath == null && result._optiScalerDlssPath != null)
            result.DlssPath = result._optiScalerDlssPath;
        if (result.DlssdPath == null && result._optiScalerDlssdPath != null)
            result.DlssdPath = result._optiScalerDlssdPath;
        if (result.DlssgPath == null && result._optiScalerDlssgPath != null)
            result.DlssgPath = result._optiScalerDlssgPath;

        // Read versions for found DLLs
        if (result.DlssPath != null)
            result.DlssVersion = GetFileVersion(result.DlssPath);
        if (result.DlssdPath != null)
            result.DlssdVersion = GetFileVersion(result.DlssdPath);
        if (result.DlssgPath != null)
            result.DlssgVersion = GetFileVersion(result.DlssgPath);
        if (result.StreamlineInterposerPath != null)
            result.StreamlineVersion = GetFileVersion(result.StreamlineInterposerPath);

        // Enumerate which Streamline files are actually present
        if (result.StreamlineFolder != null)
        {
            foreach (var slDll in KnownStreamlineDlls)
            {
                if (File.Exists(Path.Combine(result.StreamlineFolder, slDll)))
                    result.StreamlineFiles.Add(slDll);
            }
        }

        return result;
    }

    /// <summary>
    /// Resolves the search root for DLSS detection. Handles cases where the installPath
    /// points to a specific subfolder but DLLs are in sibling directories:
    /// - WindowsApps: goes up to the package root
    /// - Unreal Engine / CryEngine / other: if path is inside a Bin\ subfolder, goes up to the game root
    /// </summary>
    private static string ResolveSearchRoot(string installPath)
    {
        // Check if this is a WindowsApps path — go up to the package root
        if (installPath.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase)
            || installPath.Contains(@"/WindowsApps/", StringComparison.OrdinalIgnoreCase))
        {
            var parts = installPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (string.Equals(parts[i], "WindowsApps", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < parts.Length)
                    {
                        var packageRoot = string.Join(Path.DirectorySeparatorChar.ToString(), parts[..(i + 2)]);
                        if (Directory.Exists(packageRoot))
                            return packageRoot;
                    }
                    break;
                }
            }
        }

        // Check if the path is inside a "Bin" or "Binaries" folder.
        // - "Binaries" (Unreal Engine): {GameRoot}\{ContentFolder}\Binaries\{Platform}
        //   Need grandparent of Binaries to reach {GameRoot} where Engine\ lives.
        //   But only if grandparent isn't a store library folder (common, steamapps, etc.)
        // - "Bin" (CryEngine, etc.): {GameRoot}\Bin\{Platform}
        //   Need parent of Bin to reach {GameRoot} where sibling Bin folders live.
        var normalized = installPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var current = normalized;

        while (!string.IsNullOrEmpty(current))
        {
            var dirName = Path.GetFileName(current);
            if (string.Equals(dirName, "Binaries", StringComparison.OrdinalIgnoreCase))
            {
                var parent = Path.GetDirectoryName(current);
                var grandparent = parent != null ? Path.GetDirectoryName(parent) : null;

                // Only use grandparent if it's safe (not a store library root or drive root)
                if (grandparent != null && Directory.Exists(grandparent) && !IsStoreLibraryFolder(grandparent))
                    return grandparent;
                // Fallback to parent (the content folder or game root)
                if (parent != null && Directory.Exists(parent))
                    return parent;
                break;
            }
            else if (string.Equals(dirName, "Bin", StringComparison.OrdinalIgnoreCase))
            {
                var parent = Path.GetDirectoryName(current);
                if (parent != null && Directory.Exists(parent))
                    return parent;
                break;
            }

            var up = Path.GetDirectoryName(current);
            if (up == current) break;
            current = up;
        }

        return installPath;
    }

    /// <summary>
    /// Returns true if the given path is a known store library folder that should NOT
    /// be used as a search root (would scan all games in the library).
    /// </summary>
    private static bool IsStoreLibraryFolder(string path)
    {
        var name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(name)) return true; // drive root

        // Check for known store library folder names
        if (string.Equals(name, "common", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "steamapps", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "SteamLibrary", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "EpicGames", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "GOG Games", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "WindowsApps", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Program Files", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Program Files (x86)", StringComparison.OrdinalIgnoreCase))
            return true;

        // Drive root check (e.g. "D:\")
        if (Path.GetDirectoryName(path) == null)
            return true;

        return false;
    }

    /// <summary>
    /// Recursively searches directories for DLSS/Streamline DLLs,
    /// gracefully skipping directories that throw access-denied exceptions.
    /// Skips DLSS DLLs in directories containing OptiScaler.ini (those are
    /// OptiScaler's copies, not the game's originals).
    /// </summary>
    private void SearchDirectory(string directory, DlssDetectionResult result)
    {
        bool hasOptiScalerIni = File.Exists(Path.Combine(directory, "OptiScaler.ini"));

        // Check files in the current directory
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.dll"))
            {
                var fileName = Path.GetFileName(file);

                if (string.Equals(fileName, DlssDllName, StringComparison.OrdinalIgnoreCase))
                {
                    // Skip OptiScaler's copies — prefer the game's deeper copy
                    if (!hasOptiScalerIni && result.DlssPath == null)
                        result.DlssPath = file;
                    else if (hasOptiScalerIni && result.DlssPath == null)
                        result._optiScalerDlssPath = file; // stash as fallback
                }
                else if (string.Equals(fileName, DlssdDllName, StringComparison.OrdinalIgnoreCase))
                {
                    if (!hasOptiScalerIni && result.DlssdPath == null)
                        result.DlssdPath = file;
                    else if (hasOptiScalerIni && result.DlssdPath == null)
                        result._optiScalerDlssdPath = file;
                }
                else if (string.Equals(fileName, DlssgDllName, StringComparison.OrdinalIgnoreCase))
                {
                    if (!hasOptiScalerIni && result.DlssgPath == null)
                        result.DlssgPath = file;
                    else if (hasOptiScalerIni && result.DlssgPath == null)
                        result._optiScalerDlssgPath = file;
                }
                else if (string.Equals(fileName, StreamlineIndicator, StringComparison.OrdinalIgnoreCase))
                {
                    if (result.StreamlineInterposerPath == null)
                    {
                        result.StreamlineInterposerPath = file;
                        result.StreamlineFolder = Path.GetDirectoryName(file);
                    }
                }
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (DirectoryNotFoundException) { }

        // Recurse into subdirectories, skipping any that are inaccessible
        try
        {
            foreach (var subDir in Directory.EnumerateDirectories(directory))
            {
                SearchDirectory(subDir, result);
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (DirectoryNotFoundException) { }
    }
}
