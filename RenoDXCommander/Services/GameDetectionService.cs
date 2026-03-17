using System.Collections.Concurrent;
using System.Globalization;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public class GameDetectionService : IGameDetectionService
{
    /// <summary>
    /// Maximum directory depth for file system scans when searching for game executables.
    /// Prevents scanning deeply nested folders that are unlikely to contain game binaries.
    /// </summary>
    private const int MaxScanDepth = 4;

    /// <summary>
    /// Caches engine detection results keyed by root install path to avoid redundant file system scans
    /// on subsequent refreshes. Thread-safe for parallel detection across platforms.
    /// </summary>
    private static readonly ConcurrentDictionary<string, (string installPath, EngineType engine)> _engineCache = new(StringComparer.OrdinalIgnoreCase);

    // ── Steam ─────────────────────────────────────────────────────────────────────

    public List<DetectedGame> FindSteamGames()
    {
        var games = new List<DetectedGame>();
        var steamPath = ReadRegistry(@"SOFTWARE\Valve\Steam", "SteamPath")
                     ?? ReadRegistry(@"SOFTWARE\WOW6432Node\Valve\Steam", "SteamPath");
        if (steamPath == null) return games;
        steamPath = steamPath.Replace('/', '\\');

        foreach (var library in FindSteamLibraries(steamPath))
        {
            var steamapps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamapps)) continue;
            foreach (var acf in Directory.GetFiles(steamapps, "appmanifest_*.acf"))
            {
                try
                {
                    var content = File.ReadAllText(acf);
                    var name       = ExtractVdfValue(content, "name");
                    var installDir = ExtractVdfValue(content, "installdir");
                    if (name == null || installDir == null) continue;
                    var rootPath = Path.Combine(steamapps, "common", installDir);
                    if (!Directory.Exists(rootPath)) continue;
                    games.Add(new DetectedGame { Name = name, InstallPath = rootPath, Source = "Steam" });
                }
                catch (Exception ex) { CrashReporter.Log($"[GameDetectionService.FindSteamGames] Failed to parse ACF '{acf}' — {ex.Message}"); }
            }
        }
        return games;
    }

    // ── GOG ───────────────────────────────────────────────────────────────────────

    public List<DetectedGame> FindGogGames()
    {
        var games = new List<DetectedGame>();
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\GOG.com\Games")
                         ?? Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games");
            if (key == null) return games;
            foreach (var sub in key.GetSubKeyNames())
            {
                using var gameKey = key.OpenSubKey(sub);
                if (gameKey == null) continue;
                var name = gameKey.GetValue("GAMENAME") as string ?? gameKey.GetValue("GameName") as string;
                var path = gameKey.GetValue("PATH") as string ?? gameKey.GetValue("path") as string;
                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(path) && Directory.Exists(path))
                    games.Add(new DetectedGame { Name = name, InstallPath = path, Source = "GOG" });
            }
        }
        catch (SecurityException) { }
        return games;
    }

    // ── Epic ──────────────────────────────────────────────────────────────────────

    public List<DetectedGame> FindEpicGames()
    {
        var games = new List<DetectedGame>();
        var manifestDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(manifestDir)) return games;
        foreach (var file in Directory.GetFiles(manifestDir, "*.item"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var name = ExtractJsonString(json, "DisplayName");
                var path = ExtractJsonString(json, "InstallLocation");
                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(path) && Directory.Exists(path))
                    games.Add(new DetectedGame { Name = name, InstallPath = path, Source = "Epic" });
            }
            catch (Exception ex) { CrashReporter.Log($"[GameDetectionService.FindEpicGames] Failed to parse manifest '{file}' — {ex.Message}"); }
        }
        return games;
    }

    // ── EA App ────────────────────────────────────────────────────────────────────

    public List<DetectedGame> FindEaGames()
    {
        var games = new List<DetectedGame>();
        var seen  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ── Method 1: ProgramData\EA\content manifests (legacy Origin / some EA App) ──
        var contentDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EA", "content");
        if (Directory.Exists(contentDir))
        {
            foreach (var dir in Directory.GetDirectories(contentDir))
            {
                var manifestFile = Path.Combine(dir, "__Installer", "installerdata.xml");
                if (!File.Exists(manifestFile)) continue;
                try
                {
                    var xml  = File.ReadAllText(manifestFile);
                    var name = Regex.Match(xml, @"<gameName>([^<]+)</gameName>").Groups[1].Value;
                    var path = Regex.Match(xml, @"<defaultInstallPath>([^<]+)</defaultInstallPath>").Groups[1].Value;
                    if (!string.IsNullOrEmpty(name) && Directory.Exists(path) && seen.Add(path))
                        games.Add(new DetectedGame { Name = name, InstallPath = path, Source = "EA App" });
                }
                catch (Exception ex) { CrashReporter.Log($"[GameDetectionService] Scan error — {ex.Message}"); }
            }
        }

        // ── Method 2: Registry (Origin Games / EA App entries) ─────────────────────
        // EA App and Origin both write to HKLM\Software\Wow6432Node\Origin Games\{contentID}
        // with InstallDir and DisplayName values.
        try
        {
            using var baseKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Wow6432Node\Origin Games");
            if (baseKey != null)
            {
                foreach (var subKeyName in baseKey.GetSubKeyNames())
                {
                    try
                    {
                        using var gameKey = baseKey.OpenSubKey(subKeyName);
                        if (gameKey == null) continue;
                        var installDir = gameKey.GetValue("InstallDir") as string;
                        if (string.IsNullOrEmpty(installDir) || !Directory.Exists(installDir)) continue;
                        if (!seen.Add(installDir)) continue; // already found via manifest

                        var displayName = gameKey.GetValue("DisplayName") as string;
                        if (string.IsNullOrEmpty(displayName))
                        {
                            // Use folder name as fallback
                            displayName = Path.GetFileName(installDir.TrimEnd('\\', '/'));
                        }
                        games.Add(new DetectedGame { Name = displayName, InstallPath = installDir, Source = "EA App" });
                    }
                    catch (Exception ex) { CrashReporter.Log($"[GameDetectionService] Scan error — {ex.Message}"); }
                }
            }
        }
        catch (Exception ex) { CrashReporter.Log($"[GameDetectionService] Scan error — {ex.Message}"); }

        // ── Method 3: Broader registry scan ────────────────────────────────────────
        // Many EA games write their own registry keys with an "Install Dir" value
        // under various publisher paths. Scan common known parent keys.
        var registryPaths = new[]
        {
            @"SOFTWARE\EA Games",
            @"SOFTWARE\Wow6432Node\EA Games",
            @"SOFTWARE\Electronic Arts",
            @"SOFTWARE\Wow6432Node\Electronic Arts",
            @"SOFTWARE\Criterion Games",
            @"SOFTWARE\Wow6432Node\Criterion Games",
            @"SOFTWARE\Respawn",
            @"SOFTWARE\Wow6432Node\Respawn",
            @"SOFTWARE\BioWare",
            @"SOFTWARE\Wow6432Node\BioWare",
            @"SOFTWARE\DICE",
            @"SOFTWARE\Wow6432Node\DICE",
            @"SOFTWARE\PopCap",
            @"SOFTWARE\Wow6432Node\PopCap",
            @"SOFTWARE\Ghost Games",
            @"SOFTWARE\Wow6432Node\Ghost Games",
        };
        foreach (var regPath in registryPaths)
        {
            try
            {
                using var parentKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(regPath);
                if (parentKey == null) continue;
                foreach (var subName in parentKey.GetSubKeyNames())
                {
                    try
                    {
                        using var gameKey = parentKey.OpenSubKey(subName);
                        if (gameKey == null) continue;
                        var installDir = gameKey.GetValue("Install Dir") as string
                                      ?? gameKey.GetValue("InstallDir") as string
                                      ?? gameKey.GetValue("Install Directory") as string;
                        if (string.IsNullOrEmpty(installDir) || !Directory.Exists(installDir)) continue;
                        if (!seen.Add(installDir)) continue;

                        // Use the subkey name as the game name (usually the game title)
                        games.Add(new DetectedGame { Name = subName, InstallPath = installDir, Source = "EA App" });
                    }
                    catch (Exception ex) { CrashReporter.Log($"[GameDetectionService] Scan error — {ex.Message}"); }
                }
            }
            catch (Exception ex) { CrashReporter.Log($"[GameDetectionService] Scan error — {ex.Message}"); }
        }

        // ── Method 4: Scan default EA Games folder ─────────────────────────────────
        // Games installed to the default location may not be in the registry if the
        // EA App is not running or was reinstalled. Scan common default paths.
        var defaultDirs = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "EA Games"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "EA Games"),
        };
        foreach (var eaDir in defaultDirs)
        {
            if (!Directory.Exists(eaDir)) continue;
            foreach (var gameDir in Directory.GetDirectories(eaDir))
            {
                if (!seen.Add(gameDir)) continue;
                // Only add if it contains an exe (to avoid empty/DLC folders)
                try
                {
                    if (!Directory.EnumerateFiles(gameDir, "*.exe", SearchOption.TopDirectoryOnly).Any()) continue;
                }
                catch { continue; }
                var name = Path.GetFileName(gameDir);
                games.Add(new DetectedGame { Name = name, InstallPath = gameDir, Source = "EA App" });
            }
        }

        // ── Method 5: Scan __Installer\installerdata.xml on all drives ─────────────
        // EA games installed to custom locations (e.g. E:\BurnoutPR) contain an
        // __Installer\installerdata.xml manifest inside the game folder.
        // We scan the EA Desktop encrypted IS file to discover install paths,
        // but since we can't decrypt it, we look at the EA App's local config
        // to find configured install directories.
        try
        {
            var eaDesktopLocal = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Electronic Arts", "EA Desktop");
            if (Directory.Exists(eaDesktopLocal))
            {
                // EA Desktop stores download paths in various ini/json config files
                foreach (var file in Directory.GetFiles(eaDesktopLocal, "*.ini", SearchOption.AllDirectories)
                    .Concat(Directory.GetFiles(eaDesktopLocal, "*.json", SearchOption.AllDirectories)))
                {
                    try
                    {
                        var content = File.ReadAllText(file);
                        // Look for paths like E:\Games or D:\EA Games
                        var pathMatches = Regex.Matches(content, @"[A-Z]:\\[^""'\r\n,}{]+");
                        foreach (Match m in pathMatches)
                        {
                            var candidate = m.Value.TrimEnd('\\', '/');
                            if (!Directory.Exists(candidate)) continue;
                            // Scan subdirectories for __Installer\installerdata.xml
                            foreach (var subDir in Directory.GetDirectories(candidate))
                            {
                                var manifest = Path.Combine(subDir, "__Installer", "installerdata.xml");
                                if (!File.Exists(manifest)) continue;
                                if (!seen.Add(subDir)) continue;
                                try
                                {
                                    var xml = File.ReadAllText(manifest);
                                    var gameName = Regex.Match(xml, @"<gameName>([^<]+)</gameName>").Groups[1].Value;
                                    if (string.IsNullOrEmpty(gameName))
                                        gameName = Path.GetFileName(subDir);
                                    games.Add(new DetectedGame { Name = gameName, InstallPath = subDir, Source = "EA App" });
                                }
                                catch (Exception ex) { CrashReporter.Log($"[GameDetectionService] Scan error — {ex.Message}"); }
                            }
                        }
                    }
                    catch (Exception ex) { CrashReporter.Log($"[GameDetectionService] Scan error — {ex.Message}"); }
                }
            }
        }
        catch (Exception ex) { CrashReporter.Log($"[GameDetectionService] Scan error — {ex.Message}"); }

        // ── Method 6: Scan ProgramData\EA Desktop for __Installer in installed paths ─
        // The EA Desktop encrypted IS file is at ProgramData\EA Desktop\<hash>\IS
        // but each installed game also has an __Installer dir. Additionally, some
        // EA games place an installerdata.xml directly in the game folder.
        // Scan all drives for game folders that contain __Installer\installerdata.xml.
        try
        {
            var eaProgramData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "EA Desktop");
            if (Directory.Exists(eaProgramData))
            {
                // Look for install paths in any decryptable/readable files
                foreach (var subDir in Directory.GetDirectories(eaProgramData))
                {
                    // Each subfolder may contain install info
                    foreach (var file in Directory.GetFiles(subDir))
                    {
                        try
                        {
                            // Try reading as text — some files are plain text with paths
                            var content = File.ReadAllText(file);
                            var pathMatches = Regex.Matches(content, @"[A-Z]:\\[^\x00-\x1F""']+?\\");
                            foreach (Match m in pathMatches)
                            {
                                var candidate = m.Value.TrimEnd('\\');
                                if (!Directory.Exists(candidate)) continue;
                                if (!seen.Add(candidate)) continue;
                                var manifest = Path.Combine(candidate, "__Installer", "installerdata.xml");
                                if (File.Exists(manifest))
                                {
                                    try
                                    {
                                        var xml = File.ReadAllText(manifest);
                                        var gameName = Regex.Match(xml, @"<gameName>([^<]+)</gameName>").Groups[1].Value;
                                        if (string.IsNullOrEmpty(gameName))
                                            gameName = Path.GetFileName(candidate);
                                        games.Add(new DetectedGame { Name = gameName, InstallPath = candidate, Source = "EA App" });
                                    }
                                    catch (Exception ex) { CrashReporter.Log($"[GameDetectionService] Scan error — {ex.Message}"); }
                                }
                                else
                                {
                                    // No manifest — check for exe
                                    try
                                    {
                                        if (Directory.EnumerateFiles(candidate, "*.exe", SearchOption.TopDirectoryOnly).Any())
                                            games.Add(new DetectedGame { Name = Path.GetFileName(candidate), InstallPath = candidate, Source = "EA App" });
                                    }
                                    catch (Exception ex) { CrashReporter.Log($"[GameDetectionService] Scan error — {ex.Message}"); }
                                }
                            }
                        }
                        catch (Exception ex) { CrashReporter.Log($"[GameDetectionService.FindEaGames] Failed to read EA Desktop file — {ex.Message}"); }
                    }
                }
            }
        }
        catch (Exception ex) { CrashReporter.Log($"[GameDetectionService] Scan error — {ex.Message}"); }

        return games;
    }

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
                    catch
                    {
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
                    catch
                    {
                        displayName = package.Id?.Name ?? "Unknown";
                    }

                    if (string.IsNullOrEmpty(displayName))
                        displayName = package.Id?.Name ?? "Unknown";

                    // Resolve the actual game root for GDK games:
                    // Some GDK games have their files directly in InstallLocation,
                    // others have a Content\{InternalName} subfolder structure
                    var gameRoot = ResolveXboxGameRoot(installLocation);

                    games.Add(new DetectedGame
                    {
                        Name        = displayName,
                        InstallPath = gameRoot,
                        Source      = "Xbox",
                    });
                }
                catch
                {
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

    // ── Ubisoft Connect ───────────────────────────────────────────────────────────

    /// <summary>
    /// Detects games installed via Ubisoft Connect (formerly Uplay).
    /// Uses three approaches:
    ///   1. Registry — each installed game has an InstallDir value under
    ///      HKLM\SOFTWARE\Ubisoft\Launcher\Installs\{id}.
    ///   2. Configuration file — the launcher's settings.yml stores the
    ///      game_installation_path which may differ from the default.
    ///   3. Default install folders — scan Program Files\Ubisoft\Ubisoft Game Launcher\games.
    /// </summary>
    public List<DetectedGame> FindUbisoftGames()
    {
        var games = new List<DetectedGame>();
        var seen  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ── Method 1: Registry — HKLM\SOFTWARE\Ubisoft\Launcher\Installs\{id} ────
        // Each game has an InstallDir value pointing to the game folder.
        var registryPaths = new[]
        {
            @"SOFTWARE\Ubisoft\Launcher\Installs",
            @"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs",
        };
        foreach (var regPath in registryPaths)
        {
            try
            {
                using var baseKey = Registry.LocalMachine.OpenSubKey(regPath);
                if (baseKey == null) continue;
                foreach (var subName in baseKey.GetSubKeyNames())
                {
                    try
                    {
                        using var gameKey = baseKey.OpenSubKey(subName);
                        if (gameKey == null) continue;
                        var installDir = (gameKey.GetValue("InstallDir") as string)?.Trim().TrimEnd('\\', '/');
                        if (string.IsNullOrEmpty(installDir)) continue;
                        if (!Directory.Exists(installDir)) continue;
                        if (!seen.Add(installDir)) continue;

                        // Use the folder name as the game name — Ubisoft registry keys are
                        // numeric IDs with no display name. The folder name is typically the
                        // game title (e.g. "Assassin's Creed Valhalla", "Far Cry 6").
                        var name = Path.GetFileName(installDir);
                        if (string.IsNullOrEmpty(name)) continue;

                        games.Add(new DetectedGame { Name = name, InstallPath = installDir, Source = "Ubisoft" });
                    }
                    catch (Exception ex) { CrashReporter.Log($"[GameDetectionService] Scan error — {ex.Message}"); }
                }
            }
            catch (Exception ex) { CrashReporter.Log($"[GameDetectionService] Scan error — {ex.Message}"); }
        }

        // ── Method 2: Launcher config — settings.yml ──────────────────────────────
        // Ubisoft Connect stores its config at:
        //   %LOCALAPPDATA%\Ubisoft Game Launcher\settings.yml
        // The game_installation_path key tells us where games are installed.
        try
        {
            var launcherLocal = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Ubisoft Game Launcher");
            var settingsFile = Path.Combine(launcherLocal, "settings.yml");
            if (File.Exists(settingsFile))
            {
                var content = File.ReadAllText(settingsFile);
                // Simple YAML parse — look for game_installation_path: "C:\..."
                var pathMatch = Regex.Match(content,
                    @"game_installation_path\s*:\s*""?([A-Z]:\\[^""\r\n]+?)""?\s*$",
                    RegexOptions.Multiline | RegexOptions.IgnoreCase);
                if (pathMatch.Success)
                {
                    var gamesRoot = pathMatch.Groups[1].Value.TrimEnd('\\', '/');
                    if (Directory.Exists(gamesRoot))
                    {
                        foreach (var gameDir in Directory.GetDirectories(gamesRoot))
                        {
                            if (!seen.Add(gameDir)) continue;
                            // Only include if it has an exe (skip DLC/empty folders)
                            try
                            {
                                if (!HasFileShallow(gameDir, "*.exe", MaxScanDepth / 2)) continue;
                            }
                            catch { continue; }
                            var name = Path.GetFileName(gameDir);
                            if (!string.IsNullOrEmpty(name))
                                games.Add(new DetectedGame { Name = name, InstallPath = gameDir, Source = "Ubisoft" });
                        }
                    }
                }
            }
        }
        catch (Exception ex) { CrashReporter.Log($"[GameDetectionService] Scan error — {ex.Message}"); }

        // ── Method 3: Default install folders ─────────────────────────────────────
        var defaultDirs = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Ubisoft", "Ubisoft Game Launcher", "games"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Ubisoft", "Ubisoft Game Launcher", "games"),
        };
        foreach (var ubiDir in defaultDirs)
        {
            if (!Directory.Exists(ubiDir)) continue;
            foreach (var gameDir in Directory.GetDirectories(ubiDir))
            {
                if (!seen.Add(gameDir)) continue;
                try
                {
                    if (!HasFileShallow(gameDir, "*.exe", MaxScanDepth / 2)) continue;
                }
                catch { continue; }
                var name = Path.GetFileName(gameDir);
                if (!string.IsNullOrEmpty(name))
                    games.Add(new DetectedGame { Name = name, InstallPath = gameDir, Source = "Ubisoft" });
            }
        }

        CrashReporter.Log($"[GameDetectionService.FindUbisoftGames] Found {games.Count} game(s)");
        return games;
    }

    // ── Battle.net ────────────────────────────────────────────────────────────────

    public List<DetectedGame> FindBattleNetGames()
    {
        var games = new List<DetectedGame>();
        var seen  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ── Method 1: Registry — Uninstall entries ────────────────────────────────
        // Battle.net games register under HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall
        // or the Wow6432Node equivalent. Keys often have product codes like "Diablo IV" or
        // publisher-specific IDs. Look for InstallLocation values.
        var uninstallPaths = new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        };
        var blizzardPublishers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Blizzard Entertainment",
            "Blizzard Entertainment, Inc.",
            "Activision",
            "Activision Blizzard",
        };
        foreach (var regPath in uninstallPaths)
        {
            try
            {
                using var baseKey = Registry.LocalMachine.OpenSubKey(regPath);
                if (baseKey == null) continue;
                foreach (var subName in baseKey.GetSubKeyNames())
                {
                    try
                    {
                        using var gameKey = baseKey.OpenSubKey(subName);
                        if (gameKey == null) continue;

                        var publisher = gameKey.GetValue("Publisher") as string ?? "";
                        if (!blizzardPublishers.Contains(publisher.Trim())) continue;

                        var installDir = (gameKey.GetValue("InstallLocation") as string)?.Trim().TrimEnd('\\', '/');
                        if (string.IsNullOrEmpty(installDir) || !Directory.Exists(installDir)) continue;
                        if (!seen.Add(installDir)) continue;

                        var displayName = gameKey.GetValue("DisplayName") as string;
                        if (string.IsNullOrEmpty(displayName)) continue;

                        // Skip the launcher itself
                        if (displayName.Contains("Battle.net", StringComparison.OrdinalIgnoreCase)
                            && !displayName.Contains("game", StringComparison.OrdinalIgnoreCase))
                            continue;

                        games.Add(new DetectedGame { Name = displayName.Trim(), InstallPath = installDir, Source = "Battle.net" });
                    }
                    catch (Exception ex) { CrashReporter.Log($"[GameDetectionService] Scan error — {ex.Message}"); }
                }
            }
            catch (Exception ex) { CrashReporter.Log($"[GameDetectionService] Scan error — {ex.Message}"); }
        }

        // ── Method 2: Battle.net config — %APPDATA%\Battle.net\Battle.net.config ──
        // This JSON file has "Client.Install.DefaultInstallPath" which is where
        // games are typically installed. Scan subdirectories for game folders.
        try
        {
            var configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Battle.net", "Battle.net.config");
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                // Simple extraction — look for DefaultInstallPath
                var match = Regex.Match(json,
                    @"""Client\.Install\.DefaultInstallPath""\s*:\s*""([^""]+)""",
                    RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var gamesRoot = match.Groups[1].Value
                        .Replace("\\\\", "\\").Replace("\\/", "\\").TrimEnd('\\', '/');
                    if (Directory.Exists(gamesRoot))
                    {
                        foreach (var gameDir in Directory.GetDirectories(gamesRoot))
                        {
                            if (!seen.Add(gameDir)) continue;
                            try { if (!HasFileShallow(gameDir, "*.exe", MaxScanDepth / 2)) continue; } catch { continue; }
                            var name = Path.GetFileName(gameDir);
                            if (!string.IsNullOrEmpty(name))
                                games.Add(new DetectedGame { Name = name, InstallPath = gameDir, Source = "Battle.net" });
                        }
                    }
                }
            }
        }
        catch (Exception ex) { CrashReporter.Log($"[GameDetectionService] Scan error — {ex.Message}"); }

        // ── Method 3: Default install folders ─────────────────────────────────────
        var defaultDirs = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Battle.net"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Battle.net"),
            // Some games install under Program Files directly
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Blizzard Entertainment"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Blizzard Entertainment"),
        };
        foreach (var dir in defaultDirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var gameDir in Directory.GetDirectories(dir))
            {
                if (!seen.Add(gameDir)) continue;
                var dirName = Path.GetFileName(gameDir);
                // Skip the launcher folder itself
                if (string.IsNullOrEmpty(dirName)
                    || dirName.Equals("Battle.net", StringComparison.OrdinalIgnoreCase)
                    || dirName.Equals("Agent", StringComparison.OrdinalIgnoreCase))
                    continue;
                try { if (!HasFileShallow(gameDir, "*.exe", MaxScanDepth / 2)) continue; } catch { continue; }
                games.Add(new DetectedGame { Name = dirName, InstallPath = gameDir, Source = "Battle.net" });
            }
        }

        CrashReporter.Log($"[GameDetectionService.FindBattleNetGames] Found {games.Count} game(s)");
        return games;
    }

    // ── Rockstar Games Launcher ───────────────────────────────────────────────────

    public List<DetectedGame> FindRockstarGames()
    {
        var games = new List<DetectedGame>();
        var seen  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ── Method 1: Registry — Uninstall entries ────────────────────────────────
        // Rockstar games register under the standard Uninstall key with
        // "Rockstar Games" as publisher. Look for InstallLocation.
        var uninstallPaths = new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        };
        foreach (var regPath in uninstallPaths)
        {
            try
            {
                using var baseKey = Registry.LocalMachine.OpenSubKey(regPath);
                if (baseKey == null) continue;
                foreach (var subName in baseKey.GetSubKeyNames())
                {
                    try
                    {
                        using var gameKey = baseKey.OpenSubKey(subName);
                        if (gameKey == null) continue;

                        var publisher = gameKey.GetValue("Publisher") as string ?? "";
                        if (!publisher.Contains("Rockstar", StringComparison.OrdinalIgnoreCase)) continue;

                        var installDir = (gameKey.GetValue("InstallLocation") as string)?.Trim().TrimEnd('\\', '/');
                        if (string.IsNullOrEmpty(installDir) || !Directory.Exists(installDir)) continue;
                        if (!seen.Add(installDir)) continue;

                        var displayName = gameKey.GetValue("DisplayName") as string;
                        if (string.IsNullOrEmpty(displayName)) continue;

                        // Skip the launcher itself
                        if (displayName.Contains("Launcher", StringComparison.OrdinalIgnoreCase)
                            && displayName.Contains("Rockstar", StringComparison.OrdinalIgnoreCase)
                            && !displayName.Contains("Grand", StringComparison.OrdinalIgnoreCase))
                            continue;

                        games.Add(new DetectedGame { Name = displayName.Trim(), InstallPath = installDir, Source = "Rockstar" });
                    }
                    catch (Exception ex) { CrashReporter.Log($"[GameDetectionService] Scan error — {ex.Message}"); }
                }
            }
            catch (Exception ex) { CrashReporter.Log($"[GameDetectionService] Scan error — {ex.Message}"); }
        }

        // ── Method 2: Rockstar Launcher titles.dat ────────────────────────────────
        // The launcher stores a titles.dat file in its install directory containing
        // JSON-like data about installed games with InstallFolder paths.
        try
        {
            var launcherPath = ReadRegistry(@"SOFTWARE\WOW6432Node\Rockstar Games\Launcher", "InstallFolder")
                            ?? ReadRegistry(@"SOFTWARE\Rockstar Games\Launcher", "InstallFolder");
            if (!string.IsNullOrEmpty(launcherPath))
            {
                var titlesFile = Path.Combine(launcherPath, "titles.dat");
                if (File.Exists(titlesFile))
                {
                    var content = File.ReadAllText(titlesFile);
                    // Extract InstallFolder values — titles.dat has lines like:
                    // "InstallFolder":"C:\\Program Files\\Rockstar Games\\Grand Theft Auto V"
                    foreach (Match m in Regex.Matches(content,
                        @"""InstallFolder""\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase))
                    {
                        var installDir = m.Groups[1].Value
                            .Replace("\\\\", "\\").TrimEnd('\\', '/');
                        if (!Directory.Exists(installDir)) continue;
                        if (!seen.Add(installDir)) continue;
                        var name = Path.GetFileName(installDir);
                        if (!string.IsNullOrEmpty(name))
                            games.Add(new DetectedGame { Name = name, InstallPath = installDir, Source = "Rockstar" });
                    }
                }
            }
        }
        catch (Exception ex) { CrashReporter.Log($"[GameDetectionService] Scan error — {ex.Message}"); }

        // ── Method 3: Default install folders ─────────────────────────────────────
        var defaultDirs = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Rockstar Games"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Rockstar Games"),
        };
        foreach (var dir in defaultDirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var gameDir in Directory.GetDirectories(dir))
            {
                if (!seen.Add(gameDir)) continue;
                var dirName = Path.GetFileName(gameDir);
                // Skip the launcher folder
                if (string.IsNullOrEmpty(dirName)
                    || dirName.Equals("Launcher", StringComparison.OrdinalIgnoreCase)
                    || dirName.Equals("Social Club", StringComparison.OrdinalIgnoreCase))
                    continue;
                try { if (!HasFileShallow(gameDir, "*.exe", MaxScanDepth / 2)) continue; } catch { continue; }
                games.Add(new DetectedGame { Name = dirName, InstallPath = gameDir, Source = "Rockstar" });
            }
        }

        CrashReporter.Log($"[GameDetectionService.FindRockstarGames] Found {games.Count} game(s)");
        return games;
    }

    // ── Engine + path detection ───────────────────────────────────────────────────

    public (string installPath, EngineType engine) DetectEngineAndPath(string rootPath)
    {
        // Check cache first to avoid redundant file system scans
        if (_engineCache.TryGetValue(rootPath, out var cached))
            return cached;

        var result = DetectEngineAndPathCore(rootPath);
        _engineCache.TryAdd(rootPath, result);
        return result;
    }

    private (string installPath, EngineType engine) DetectEngineAndPathCore(string rootPath)
    {
        // --- Unreal Engine (UE4/5) ---
        // Find Binaries\Win64 or Binaries\WinGDK that is NOT inside an Engine folder.
        // This is where ReShade and the .addon64 must be placed.
        var uePath = FindUEBinariesFolder(rootPath);
        if (uePath != null)
        {
            // Distinguish UE4/5 (has *Shipping*.exe) from UE3 (plain exe only).
            // RenoDX addon support requires UE4+; UE3 games cannot use it.
            if (IsUnrealLegacy(rootPath))
                return (uePath, EngineType.UnrealLegacy);
            return (uePath, EngineType.Unreal);
        }

        // Also catch UE3 games whose Binaries may not match UE4 layout (Binaries\Win32 only)
        if (IsUnrealLegacy(rootPath))
        {
            var exeFolderLegacy = FindShallowExeFolder(rootPath);
            return (exeFolderLegacy ?? rootPath, EngineType.UnrealLegacy);
        }

        // --- Unity ---
        // UnityPlayer.dll is always next to the exe (root or 1 level deep)
        var unityPlayer = FindFileShallow(rootPath, "UnityPlayer.dll", maxDepth: MaxScanDepth / 2);
        if (unityPlayer != null)
            return (Path.GetDirectoryName(unityPlayer)!, EngineType.Unity);

        // Additional Unity detection: Mono folder, MonoBleedingEdge folder, il2cpp file, or GameAssembly.dll
        // Some Unity games (especially IL2CPP builds) may not have UnityPlayer.dll in the base folder
        if (IsUnityGame(rootPath))
        {
            var unityExeFolder = FindShallowExeFolder(rootPath);
            return (unityExeFolder ?? rootPath, EngineType.Unity);
        }

        // --- Generic fallback ---
        var exeFolder = FindShallowExeFolder(rootPath);
        return (exeFolder ?? rootPath, EngineType.Unknown);
    }

    /// <summary>
    /// Returns true if the game folder contains Unity-specific markers other than UnityPlayer.dll.
    /// Covers IL2CPP builds and other Unity configurations that don't ship UnityPlayer.dll in the root.
    /// Checks for: Mono folder, MonoBleedingEdge folder, il2cpp folder/file, GameAssembly.dll
    /// </summary>
    private bool IsUnityGame(string root)
    {
        try
        {
            // Check for Mono folder (classic Unity Mono scripting backend)
            if (Directory.Exists(Path.Combine(root, "Mono"))) return true;
            // Check for MonoBleedingEdge folder (Unity 2017+)
            if (Directory.Exists(Path.Combine(root, "MonoBleedingEdge"))) return true;
            // Check for il2cpp folder (Unity IL2CPP build)
            if (Directory.Exists(Path.Combine(root, "il2cpp"))) return true;
            // Check for GameAssembly.dll (Unity IL2CPP build marker)
            if (FindFileShallow(root, "GameAssembly.dll", maxDepth: MaxScanDepth / 2) != null) return true;
            // Check one level deep for these markers (game data subfolder pattern)
            foreach (var sub in Directory.GetDirectories(root))
            {
                if (IsSkippedFolder(sub)) continue;
                var name = Path.GetFileName(sub);
                // Unity data folders are typically named "<GameName>_Data"
                if (!name.EndsWith("_Data", StringComparison.OrdinalIgnoreCase)) continue;
                if (Directory.Exists(Path.Combine(sub, "Mono"))) return true;
                if (Directory.Exists(Path.Combine(sub, "MonoBleedingEdge"))) return true;
                if (Directory.Exists(Path.Combine(sub, "il2cpp"))) return true;
                if (File.Exists(Path.Combine(sub, "Resources", "unity_builtin_extra"))) return true;
            }
        }
        catch (Exception ex) { CrashReporter.Log($"[GameDetectionService] Scan error — {ex.Message}"); }
        return false;
    }

    /// <summary>
    /// Returns true if this looks like a UE3 or older Unreal game.
    /// Heuristics (any one is sufficient):
    ///   • Has .u or .upk files (UE3 package format) anywhere shallow
    ///   • Has Engine\Config\BaseEngine.ini but NO *Shipping*.exe anywhere
    ///   • Has Binaries\Win32 or Binaries\Win64 with a plain exe but no Shipping exe,
    ///     AND an Engine folder with classic UE3 sub-layout
    /// </summary>
    private bool IsUnrealLegacy(string root)
    {
        try
        {
            // ── Cheap checks first ────────────────────────────────────────────────
            // Rocket League specific: has TAGame folder (UE3 codename)
            if (Directory.Exists(Path.Combine(root, "TAGame"))) return true;

            // Classic UE3 marker: Engine\Config\BaseEngine.ini
            var baseEnginePath = Path.Combine(root, "Engine", "Config", "BaseEngine.ini");
            bool hasBaseEngine = File.Exists(baseEnginePath);

            if (hasBaseEngine)
            {
                // Confirm Binaries exists (cheap check) and no Shipping exe
                bool hasBinaries = Directory.Exists(Path.Combine(root, "Binaries"));
                bool hasShipping = false;
                if (hasBinaries)
                {
                    try { hasShipping = HasFileShallow(root, "*Shipping*.exe", MaxScanDepth); }
                    catch (Exception ex) { CrashReporter.Log($"[GameDetectionService] Scan error — {ex.Message}"); }
                }
                if (hasBinaries && !hasShipping) return true;
            }

            // ── Depth-limited .u / .upk search ───────────────────────────────────
            // UE3 cooked content files are always near the root (typically 1-3 levels).
            // Using a depth-limited search instead of AllDirectories avoids traversing
            // the entire game folder tree for non-UE games.
            foreach (var ext in new[] { "*.u", "*.upk" })
            {
                if (HasFileShallow(root, ext, MaxScanDepth - 1)) return true;
            }
        }
        catch (Exception ex) { CrashReporter.Log($"[GameDetectionService] Scan error — {ex.Message}"); }
        return false;
    }

    /// <summary>
    /// Depth-limited file existence check. Returns true if any file matching
    /// <paramref name="pattern"/> exists within <paramref name="maxDepth"/> levels.
    /// </summary>
    private bool HasFileShallow(string dir, string pattern, int maxDepth)
    {
        if (maxDepth < 0 || !Directory.Exists(dir)) return false;
        try
        {
            if (Directory.GetFiles(dir, pattern).Length > 0) return true;
            if (maxDepth > 0)
                foreach (var sub in Directory.GetDirectories(dir))
                {
                    if (IsSkippedFolder(sub)) continue;
                    if (HasFileShallow(sub, pattern, maxDepth - 1)) return true;
                }
        }
        catch (Exception ex) { CrashReporter.Log($"[GameDetectionService] Scan error — {ex.Message}"); }
        return false;
    }

    /// <summary>
    /// Finds the Binaries\Win64 or Binaries\WinGDK folder that is NOT inside
    /// an "Engine" directory. UE games have the structure:
    ///   GameRoot\
    ///     GameName\        ← or a codename folder
    ///       Binaries\
    ///         Win64\       ← THIS is where ReShade + .addon64 goes
    ///     Engine\          ← SKIP this entirely
    /// </summary>
    private string? FindUEBinariesFolder(string root)
    {
        var candidates = new List<string>();
        CollectUEBinaries(root, 0, candidates);
        if (candidates.Count == 0) return null;

        // Prefer folders that contain a Shipping exe
        var withShipping = candidates.FirstOrDefault(c =>
            Directory.GetFiles(c, "*Shipping.exe").Length > 0 ||
            Directory.GetFiles(c, "*.exe").Any(f =>
                Path.GetFileName(f).Contains("Shipping", StringComparison.OrdinalIgnoreCase)));

        return withShipping ?? candidates[0];
    }

    private void CollectUEBinaries(string dir, int depth, List<string> results)
    {
        if (depth > MaxScanDepth + 1) return;
        try
        {
            foreach (var sub in Directory.GetDirectories(dir))
            {
                var name = Path.GetFileName(sub);

                // Skip Engine folder — its Binaries are for the engine, not the game
                if (name.Equals("Engine", StringComparison.OrdinalIgnoreCase)) continue;

                // Skip bonus-content folders (artbook viewers, soundtrack players, etc.)
                if (IsSkippedFolder(sub)) continue;

                // Found a Binaries folder — look inside for Win64/WinGDK
                if (name.Equals("Binaries", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var binSub in Directory.GetDirectories(sub))
                    {
                        var binName = Path.GetFileName(binSub);
                        bool isTarget = binName.Equals("Win64",  StringComparison.OrdinalIgnoreCase)
                                     || binName.Equals("Win32",  StringComparison.OrdinalIgnoreCase)
                                     || binName.Equals("WinGDK", StringComparison.OrdinalIgnoreCase);
                        if (isTarget && Directory.GetFiles(binSub, "*.exe").Length > 0)
                            results.Add(binSub);
                    }
                    // Don't recurse further into Binaries
                    continue;
                }

                // Recurse into non-Engine, non-Binaries subfolders
                CollectUEBinaries(sub, depth + 1, results);
            }
        }
        catch (Exception ex) { CrashReporter.Log($"[GameDetectionService] Scan error — {ex.Message}"); }
    }

    /// <summary>Folder names to skip during engine/exe heuristic searches.
    /// These contain bonus content (artbook viewers, soundtrack players, etc.)
    /// that can carry their own engine DLLs and exes.</summary>
    private readonly HashSet<string> _skipFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "artbook", "art book", "artbooks", "digitalartbook",
        "soundtrack", "ost", "music",
        "manual", "manuals", "docs", "documentation",
        "bonus", "bonuscontent", "bonus content", "extras",
        "wallpapers", "wallpaper",
    };

    private bool IsSkippedFolder(string path)
    {
        var name = Path.GetFileName(path);
        return _skipFolders.Contains(name);
    }

    private string? FindFileShallow(string dir, string pattern, int maxDepth)
    {
        if (maxDepth < 0 || !Directory.Exists(dir)) return null;
        try
        {
            var found = Directory.GetFiles(dir, pattern);
            if (found.Length > 0) return found[0];
            if (maxDepth > 0)
                foreach (var sub in Directory.GetDirectories(dir))
                {
                    if (IsSkippedFolder(sub)) continue;
                    var r = FindFileShallow(sub, pattern, maxDepth - 1);
                    if (r != null) return r;
                }
        }
        catch (Exception ex) { CrashReporter.Log($"[GameDetectionService] Scan error — {ex.Message}"); }
        return null;
    }

    private string? FindShallowExeFolder(string root)
    {
        var queue = new Queue<(string path, int depth)>();
        queue.Enqueue((root, 0));
        while (queue.Count > 0)
        {
            var (dir, depth) = queue.Dequeue();
            if (depth > MaxScanDepth) continue;
            try
            {
                if (Directory.GetFiles(dir, "*.exe").Length > 0) return dir;
                foreach (var sub in Directory.GetDirectories(dir))
                {
                    if (IsSkippedFolder(sub)) continue;
                    queue.Enqueue((sub, depth + 1));
                }
            }
            catch (Exception ex) { CrashReporter.Log($"[GameDetectionService] Scan error — {ex.Message}"); }
        }
        return null;
    }

    // ── Matching ──────────────────────────────────────────────────────────────────

    public GameMod? MatchGame(
        DetectedGame game,
        IEnumerable<GameMod> mods,
        Dictionary<string, string>? nameMappings = null)
    {
        var modList = mods.Select(m => (mod: m, norm: NormalizeName(m.Name))).ToList();
        var name = NormalizeName(game.Name);

        // 0. User-defined name mappings take absolute priority.
        //    The mapping stores detectedName → wikiName (exact strings as typed).
        //    We try both exact and normalised key comparison.
        if (nameMappings != null && nameMappings.Count > 0)
        {
            // Try direct key lookup first (exact stored name)
            if (nameMappings.TryGetValue(game.Name, out var mapped) && !string.IsNullOrEmpty(mapped))
            {
                var mappedNorm = NormalizeName(mapped);
                var byMapped = modList.FirstOrDefault(t => t.norm == mappedNorm);
                if (byMapped.mod != null) return byMapped.mod;
                // Target wiki name not found in mod list — fall through to auto-match
            }
            // Try normalised key comparison (handles minor capitalisation/spacing differences)
            var gameNormForMap = NormalizeName(game.Name);
            foreach (var kv in nameMappings)
            {
                if (NormalizeName(kv.Key) == gameNormForMap && !string.IsNullOrEmpty(kv.Value))
                {
                    var mappedNorm = NormalizeName(kv.Value);
                    var byMapped = modList.FirstOrDefault(t => t.norm == mappedNorm);
                    if (byMapped.mod != null) return byMapped.mod;
                }
            }
        }

        // 1. Exact normalised match — covers diacritics, punctuation, TM symbols.
        var exact = modList.FirstOrDefault(t => t.norm == name);
        if (exact.mod != null) return exact.mod;

        // 2. Game name contains the mod name — e.g. detected "Code Vein GOTY" matches wiki "Code Vein".
        //    Pick the longest (most specific) mod name that fits inside the game name.
        var containedBy = modList
            .Where(t => name.Contains(t.norm))
            .OrderByDescending(t => t.norm.Length)
            .FirstOrDefault();
        if (containedBy.mod != null) return containedBy.mod;

        // 3. Mod name contains the game name — abbreviated detected name.
        //    Only match when game name appears at the START of the mod name.
        //    Reject if the extra suffix is a sequel indicator (II, III, 2, 3…).
        foreach (var t in modList.Where(t => t.norm.StartsWith(name)).OrderBy(t => t.norm.Length))
        {
            var suffix = t.norm.Substring(name.Length);
            if (string.IsNullOrEmpty(suffix)) return t.mod;
            var isSequel = System.Text.RegularExpressions.Regex.IsMatch(suffix, @"^[ivxlcdm0-9]+$");
            if (!isSequel) return t.mod;
        }

        CrashReporter.Log($"[GameDetectionService.MatchGame] No match for '{game.Name}' (norm='{name}') against {modList.Count} wiki mods");
        // Log close matches for diagnostics
        var closeMatches = modList
            .Where(t => t.norm.Contains(name) || name.Contains(t.norm))
            .Select(t => $"'{t.mod.Name}'→'{t.norm}'")
            .Take(5);
        if (closeMatches.Any())
            CrashReporter.Log($"[GameDetectionService.MatchGame] Close matches: [{string.Join(", ", closeMatches)}]");
        return null;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private List<string> FindSteamLibraries(string steamPath)
    {
        var libraries = new List<string> { steamPath };
        var vdfPath   = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdfPath)) return libraries;
        foreach (Match m in Regex.Matches(File.ReadAllText(vdfPath), @"""path""\s+""([^""]+)"""))
        {
            var lib = m.Groups[1].Value.Replace(@"\\", @"\");
            if (Directory.Exists(lib) && !libraries.Contains(lib)) libraries.Add(lib);
        }
        return libraries;
    }

    private string? ExtractVdfValue(string content, string key) =>
        Regex.Match(content, $@"""{Regex.Escape(key)}""\s+""([^""]+)""", RegexOptions.IgnoreCase)
            is { Success: true } m ? m.Groups[1].Value : null;

    private string? ExtractJsonString(string json, string key) =>
        Regex.Match(json, $@"""{Regex.Escape(key)}""\s*:\s*""([^""\\]*(\\.[^""\\]*)*)""")
            is { Success: true } m ? m.Groups[1].Value.Replace("\\\\", "\\").Replace("\\/", "/") : null;

    private string? ReadRegistry(string keyPath, string valueName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath)
                         ?? Registry.LocalMachine.OpenSubKey(keyPath);
            return key?.GetValue(valueName) as string;
        }
        catch (Exception ex) { CrashReporter.Log($"[GameDetectionService.ReadRegistry] Failed to read registry '{keyPath}\\{valueName}' — {ex.Message}"); return null; }
    }

    /// <summary>
    /// Normalise a game name for matching: strip diacritics (ö→o, é→e, ñ→n),
    /// trademark symbols, and every character that isn't a-z or 0-9.
    /// "God of War Ragnarök" and "God of War Ragnarok" both become "godofwarragnarok".
    /// "NieR:Automata" and "STAR WARS™ Jedi: Fallen Order" are handled correctly too.
    /// </summary>
    public string NormalizeName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";

        // 1. Strip common trademark/copyright symbols that are never part of the title
        name = name.Replace("™", "").Replace("®", "").Replace("©", "");

        // 2. Decompose unicode into base character + combining marks,
        //    then drop the combining marks (diacritics).
        //    e.g. ö (U+00F6) → o (U+006F) + ̈ (U+0308) → o
        var decomposed = name.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }

        // 3. Lower-case, then strip everything that isn't a letter or digit.
        //    This handles ':', '-', ''', '!', spaces, etc. uniformly.
        return Regex.Replace(sb.ToString().ToLowerInvariant(), @"[^a-z0-9]", "");
    }
}
