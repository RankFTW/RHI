// GameDetectionService.Xbox.cs — Xbox / Game Pass platform game detection
using System.Text;
using Microsoft.Win32;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public partial class GameDetectionService
{
    // ── Xbox / Game Pass ──────────────────────────────────────────────────────

    private const string GamingServicesRootKey = @"SOFTWARE\Microsoft\GamingServices\PackageRepository\Root";

    /// <summary>
    /// Detects Xbox / Game Pass games using the Windows PackageManager API.
    /// A package counts as a game only if Xbox Gaming Services has registered it
    /// (HKLM\...\GamingServices\PackageRepository\Root, the list SKIF uses) or it ships
    /// a MicrosoftGame.config (GDK games). Regular Store apps are in neither.
    /// </summary>
    public List<DetectedGame> FindXboxGames()
    {
        var games = new List<DetectedGame>();

        try
        {
            var registered = ReadGamingServicesPackages();
            var registeredNames = new HashSet<string>(registered.Select(r => r.PackageFullName), StringComparer.OrdinalIgnoreCase);
            CrashReporter.Log($"[GameDetectionService.FindXboxGames] Gaming Services lists {registeredNames.Count} package(s)");

            var packageManager = new Windows.Management.Deployment.PackageManager();

            // FindPackagesForUser("") returns packages for the current user
            var packages = packageManager.FindPackagesForUser("");

            foreach (var package in packages)
            {
                try
                {
                    // Skip frameworks, resource packs, bundles, and optional packages
                    if (package.IsFramework || package.IsResourcePackage || package.IsBundle)
                        continue;

                    // Only want Store-signed packages (not dev mode, not system)
                    if (package.SignatureKind != Windows.ApplicationModel.PackageSignatureKind.Store)
                        continue;

                    // Get the install location
                    string? installLocation;
                    try
                    {
                        installLocation = package.InstalledLocation?.Path;
                    }
                    catch (Exception ex)
                    {
                        CrashReporter.Log($"[GameDetectionService.FindXboxGames] Package '{package.Id?.Name}' — InstalledLocation inaccessible — {ex.Message}");
                        continue; // Some packages throw on accessing InstalledLocation
                    }

                    if (string.IsNullOrEmpty(installLocation)) continue;

                    // Games installed to an Xbox games folder: use the real folder (e.g. D:\xbox\<Game>\Content)
                    // instead of the WindowsApps junction, whose name changes with every game update.
                    if (XboxPaths.RealPackageFolder(installLocation) is { } realLocation)
                        installLocation = realLocation;

                    if (!Directory.Exists(installLocation))
                        continue;

                    // Filter: must be a game, not a regular app
                    bool isGame = registeredNames.Contains(package.Id?.FullName ?? "")
                               || File.Exists(Path.Combine(installLocation, "MicrosoftGame.config"));
                    if (!isGame) continue;

                    // Get the display name — prefer the DisplayName property, fall back to Id.Name
                    string displayName;
                    try
                    {
                        displayName = package.DisplayName;
                    }
                    catch (Exception ex)
                    {
                        CrashReporter.Log($"[GameDetectionService.FindXboxGames] Package '{package.Id?.Name}' — DisplayName inaccessible — {ex.Message}");
                        displayName = package.Id?.Name ?? "Unknown";
                    }

                    if (string.IsNullOrEmpty(displayName))
                        displayName = package.Id?.Name ?? "Unknown";

                    // Resolve the actual game root for GDK games
                    var gameRoot = ResolveXboxGameRoot(installLocation);

                    // Capture AUMID for shell:AppsFolder launch (required for Game Pass games)
                    string? aumid = null;
                    try
                    {
                        var appEntries = package.GetAppListEntries();
                        aumid = appEntries?.FirstOrDefault()?.AppUserModelId;
                    }
                    catch (Exception ex)
                    {
                        CrashReporter.Log($"[GameDetectionService.FindXboxGames] Package '{package.Id?.Name}' — AUMID inaccessible — {ex.Message}");
                    }

                    games.Add(new DetectedGame
                    {
                        Name        = displayName,
                        InstallPath = gameRoot,
                        Source      = "Xbox",
                        XboxAumid   = aumid,
                    });
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[GameDetectionService.FindXboxGames] Package '{package.Id?.Name}' error — {ex.Message}");
                    // Individual package errors should not stop enumeration
                }
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[GameDetectionService.FindXboxGames] PackageManager enumeration failed — {ex.Message}");

            // Fallback: try the filesystem-based approach if PackageManager fails
            var fallbackGames = FindXboxGamesFallback();
            games.AddRange(fallbackGames);
        }

        CrashReporter.Log($"[GameDetectionService.FindXboxGames] Found {games.Count} game(s)");
        return games;
    }

    /// <summary>
    /// Filesystem-based fallback for Xbox game detection.
    /// Scans .GamingRoot files, registry, and common folder names.
    /// </summary>
    private List<DetectedGame> FindXboxGamesFallback()
    {
        var games = new List<DetectedGame>();
        var searchRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // .GamingRoot files on every fixed drive
        try
        {
            foreach (var drive in DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
            {
                var gamingRootPath = Path.Combine(drive.RootDirectory.FullName, ".GamingRoot");
                if (!File.Exists(gamingRootPath)) continue;
                try
                {
                    var paths = ParseGamingRootFile(gamingRootPath);
                    foreach (var relPath in paths)
                    {
                        var fullPath = Path.Combine(drive.RootDirectory.FullName, relPath);
                        if (Directory.Exists(fullPath))
                            searchRoots.Add(fullPath);
                    }
                }
                catch (Exception ex) { CrashReporter.Log($"[GameDetectionService] Scan error — {ex.Message}"); }
            }
        }
        catch (Exception ex) { CrashReporter.Log($"[GameDetectionService] Scan error — {ex.Message}"); }

        // Games registered with Gaming Services: each Root is a game folder itself, not a parent of games
        var foundPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (packageFullName, root) in ReadGamingServicesPackages())
        {
            try
            {
                var installPath = XboxPaths.RealPackageFolder(root) ?? root;
                if (!Directory.Exists(installPath)) continue;
                installPath = ResolveXboxGameRoot(installPath);
                if (!foundPaths.Add(installPath)) continue;

                var (configName, aumid) = XboxPaths.ReadGameConfig(installPath);
                games.Add(new DetectedGame
                {
                    Name        = configName ?? packageFullName.Split('_')[0],
                    InstallPath = installPath,
                    Source      = "Xbox",
                    XboxAumid   = aumid,
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                CrashReporter.Log($"[GameDetectionService.FindXboxGamesFallback] '{packageFullName}' — {ex.Message}");
            }
        }

        // Common folder names
        try
        {
            foreach (var drive in DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
                foreach (var name in new[] { "XboxGames", "Xbox Games" })
                {
                    var dir = Path.Combine(drive.RootDirectory.FullName, name);
                    if (Directory.Exists(dir)) searchRoots.Add(dir);
                }
        }
        catch (Exception ex) { CrashReporter.Log($"[GameDetectionService] Scan error — {ex.Message}"); }

        // ModifiableWindowsApps
        try
        {
            var mod = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "ModifiableWindowsApps");
            if (Directory.Exists(mod)) searchRoots.Add(mod);
        }
        catch (Exception ex) { CrashReporter.Log($"[GameDetectionService] Scan error — {ex.Message}"); }

        CrashReporter.Log($"[GameDetectionService.FindXboxGamesFallback] Searching {searchRoots.Count} root(s)");

        foreach (var root in searchRoots)
        {
            try
            {
                foreach (var gameDir in Directory.GetDirectories(root))
                {
                    var gameName = Path.GetFileName(gameDir);
                    if (string.IsNullOrEmpty(gameName) ||
                        gameName.StartsWith(".") ||
                        gameName.Equals("GamingServices", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var installPath = ResolveXboxGameRoot(gameDir);
                    if (!foundPaths.Add(installPath)) continue;

                    bool hasExe = false;
                    try { hasExe = HasFileShallow(installPath, "*.exe", MaxScanDepth - 1); }
                    catch (Exception ex) { CrashReporter.Log($"[GameDetectionService] Scan error — {ex.Message}"); }
                    if (!hasExe) continue;

                    // Same name and launch id as the package list gives (folder names drop ':' etc.)
                    var (configName, aumid) = XboxPaths.ReadGameConfig(installPath);
                    games.Add(new DetectedGame
                    {
                        Name        = configName ?? gameName,
                        InstallPath = installPath,
                        Source      = "Xbox",
                        XboxAumid   = aumid,
                    });
                }
            }
            catch (Exception ex) { CrashReporter.Log($"[GameDetectionService] Scan error — {ex.Message}"); }
        }

        return games;
    }

    /// <summary>
    /// Packages Xbox Gaming Services has registered as games: Root\&lt;GUID&gt;\&lt;entry&gt; with string values
    /// Package (package full name) and Root (install folder, \\?\-prefixed). Empty if Gaming Services
    /// isn't installed or the key can't be read.
    /// </summary>
    private static List<(string PackageFullName, string Root)> ReadGamingServicesPackages()
    {
        var result = new List<(string, string)>();
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var rootKey = hklm.OpenSubKey(GamingServicesRootKey);
            if (rootKey == null) return result;

            foreach (var guid in rootKey.GetSubKeyNames())
            {
                using var guidKey = rootKey.OpenSubKey(guid);
                if (guidKey == null) continue;
                foreach (var entry in guidKey.GetSubKeyNames())
                {
                    using var entryKey = guidKey.OpenSubKey(entry);
                    if (entryKey?.GetValue("Package") is not string package || string.IsNullOrWhiteSpace(package)) continue;
                    var root = entryKey.GetValue("Root") as string ?? "";
                    if (root.StartsWith(@"\\?\", StringComparison.Ordinal)) root = root[4..];
                    root = root.TrimEnd('\\');
                    result.Add((package, root));
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            CrashReporter.Log($"[GameDetectionService.ReadGamingServicesPackages] Registry read failed — {ex.Message}");
        }
        return result;
    }

    /// <summary>
    /// Parses a .GamingRoot binary file and extracts the relative folder path(s).
    /// </summary>
    private List<string> ParseGamingRootFile(string filePath)
    {
        var paths = new List<string>();
        var bytes = File.ReadAllBytes(filePath);
        if (bytes.Length < 8) return paths;

        // Structured format: "RGBX" header + count + UTF-16LE null-terminated paths
        if (bytes[0] == 'R' && bytes[1] == 'G' && bytes[2] == 'B' && bytes[3] == 'X')
        {
            var count = BitConverter.ToInt32(bytes, 4);
            var offset = 8;
            for (int i = 0; i < count && offset < bytes.Length; i++)
            {
                int start = offset;
                while (offset + 1 < bytes.Length)
                {
                    if (bytes[offset] == 0 && bytes[offset + 1] == 0) { offset += 2; break; }
                    offset += 2;
                }
                var str = Encoding.Unicode.GetString(bytes, start, offset - start).TrimEnd('\0').Trim('\\', '/');
                if (!string.IsNullOrWhiteSpace(str)) paths.Add(str);
            }
        }

        // Fallback: read as UTF-16LE, skip non-text header
        if (paths.Count == 0)
        {
            int textStart = 0;
            for (int i = 0; i + 1 < bytes.Length; i += 2)
            {
                char c = (char)(bytes[i] | (bytes[i + 1] << 8));
                if (char.IsLetterOrDigit(c) || c == '\\' || c == '/') { textStart = i; break; }
            }
            var raw = Encoding.Unicode.GetString(bytes, textStart, bytes.Length - textStart).TrimEnd('\0').Trim('\\', '/');
            if (!string.IsNullOrWhiteSpace(raw))
                foreach (var p in raw.Split('\0', StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = p.Trim('\\', '/');
                    if (!string.IsNullOrWhiteSpace(trimmed)) paths.Add(trimmed);
                }
        }

        return paths;
    }

    /// <summary>
    /// The game root of an Xbox games folder entry (&lt;Game&gt;\Content for GDK games): the same folder the
    /// package list reports, so both detection paths give the same install path. Not a subfolder of Content:
    /// the first one is usually Engine.
    /// </summary>
    private static string ResolveXboxGameRoot(string gameDir)
    {
        var contentDir = Path.Combine(gameDir, "Content");
        return Directory.Exists(contentDir) ? contentDir : gameDir;
    }
}
