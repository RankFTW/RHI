// GameDetectionService.Xbox.cs — Xbox / Game Pass platform game detection
using System.Text;
using Microsoft.Win32;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public partial class GameDetectionService
{
    // ── Xbox / Game Pass ──────────────────────────────────────────────────────

    /// <summary>
    /// Detects Xbox / Game Pass games using the Windows PackageManager API.
    /// This enumerates all installed MSIX/UWP packages and filters for games
    /// by looking for a MicrosoftGame.config file (GDK games) or exe files
    /// in the package install location. This is the same approach used by
    /// Playnite and other game library managers.
    /// </summary>
    public List<DetectedGame> FindXboxGames()
    {
        var games = new List<DetectedGame>();

        try
        {
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
                    string installLocation;
                    try
                    {
                        installLocation = package.InstalledLocation?.Path;
                    }
                    catch (Exception ex)
                    {
                        CrashReporter.Log($"[GameDetectionService.FindXboxGames] Package '{package.Id?.Name}' — InstalledLocation inaccessible — {ex.Message}");
                        continue; // Some packages throw on accessing InstalledLocation
                    }

                    if (string.IsNullOrEmpty(installLocation) || !Directory.Exists(installLocation))
                        continue;

                    // Filter: must be a game, not a regular app
                    // GDK games have a MicrosoftGame.config file
                    bool isGame = File.Exists(Path.Combine(installLocation, "MicrosoftGame.config"));

                    // Some older UWP games don't have MicrosoftGame.config but do have
                    // exe files and are not Microsoft system apps
                    if (!isGame)
                    {
                        var packageName = package.Id?.Name ?? "";

                        // Skip known Microsoft system/utility packages
                        if (packageName.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) ||
                            packageName.StartsWith("Windows.", StringComparison.OrdinalIgnoreCase) ||
                            packageName.StartsWith("MicrosoftWindows.", StringComparison.OrdinalIgnoreCase) ||
                            packageName.StartsWith("Clipchamp.", StringComparison.OrdinalIgnoreCase) ||
                            packageName.StartsWith("Microsoft365.", StringComparison.OrdinalIgnoreCase) ||
                            packageName.StartsWith("NVIDIACorp.", StringComparison.OrdinalIgnoreCase) ||
                            packageName.StartsWith("RealtekSemiconductor", StringComparison.OrdinalIgnoreCase) ||
                            packageName.StartsWith("AppUp.", StringComparison.OrdinalIgnoreCase) ||
                            packageName.StartsWith("Disney.", StringComparison.OrdinalIgnoreCase) ||
                            packageName.StartsWith("SpotifyAB.", StringComparison.OrdinalIgnoreCase))
                            continue;

                        // Check for exe files — apps without them aren't games
                        bool hasExe = false;
                        try
                        {
                            hasExe = Directory.EnumerateFiles(installLocation, "*.exe",
                                SearchOption.TopDirectoryOnly).Any();
                        }
                        catch (Exception ex) { CrashReporter.Log($"[GameDetectionService] Scan error — {ex.Message}"); }
                        if (!hasExe) continue;

                        // Additional heuristic: must have a large exe (>10MB) typical of games
                        // This filters out small utility apps
                        bool hasLargeExe = false;
                        try
                        {
                            hasLargeExe = Directory.EnumerateFiles(installLocation, "*.exe",
                                SearchOption.TopDirectoryOnly)
                                .Any(f => new FileInfo(f).Length > 10 * 1024 * 1024);
                        }
                        catch (Exception ex) { CrashReporter.Log($"[GameDetectionService] Scan error — {ex.Message}"); }
                        if (!hasLargeExe) continue;

                        isGame = true;
                    }

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

                    games.Add(new DetectedGame
                    {
                        Name        = displayName,
                        InstallPath = gameRoot,
                        Source      = "Xbox",
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

        // Registry
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\GamingServices\PackageRepository\Root");
            if (key != null)
                foreach (var v in key.GetValueNames())
                {
                    var path = key.GetValue(v) as string;
                    if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                        searchRoots.Add(path);
                }
        }
        catch (Exception ex) { CrashReporter.Log($"[GameDetectionService] Scan error — {ex.Message}"); }

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

                    bool hasExe = false;
                    try { hasExe = HasFileShallow(installPath, "*.exe", MaxScanDepth - 1); }
                    catch (Exception ex) { CrashReporter.Log($"[GameDetectionService] Scan error — {ex.Message}"); }
                    if (!hasExe) continue;

                    games.Add(new DetectedGame { Name = gameName, InstallPath = installPath, Source = "Xbox" });
                }
            }
            catch (Exception ex) { CrashReporter.Log($"[GameDetectionService] Scan error — {ex.Message}"); }
        }

        return games;
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
    /// Resolves the actual game root from an Xbox game directory.
    /// Handles the Content\InternalName subfolder structure used by GDK games.
    /// </summary>
    private string ResolveXboxGameRoot(string gameDir)
    {
        var contentDir = Path.Combine(gameDir, "Content");
        if (!Directory.Exists(contentDir)) return gameDir;

        var innerDirs = Directory.GetDirectories(contentDir);
        return innerDirs.Length > 0 ? innerDirs[0] : contentDir;
    }
}
