// GameDetectionService.Platform.cs — GOG, Epic, EA, Ubisoft, Battle.net, and Rockstar platform game detection
using System.Security;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public partial class GameDetectionService
{
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
        catch (SecurityException ex) { CrashReporter.Log($"[GameDetectionService.FindGogGames] Registry access denied — {ex.Message}"); }
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
                catch (Exception ex) { CrashReporter.Log($"[GameDetectionService.FindEaGames] Exe check failed for '{gameDir}' — {ex.Message}"); continue; }
                var name = Path.GetFileName(gameDir);
                games.Add(new DetectedGame { Name = name, InstallPath = gameDir, Source = "EA App" });
            }
        }

        // ── Method 5: Scan __Installer\installerdata.xml on all drives ─────────────
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
                            catch (Exception ex) { CrashReporter.Log($"[GameDetectionService.FindUbisoftGames] Exe check failed for '{gameDir}' — {ex.Message}"); continue; }
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
                catch (Exception ex) { CrashReporter.Log($"[GameDetectionService.FindUbisoftGames] Exe check failed for '{gameDir}' — {ex.Message}"); continue; }
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
                            try { if (!HasFileShallow(gameDir, "*.exe", MaxScanDepth / 2)) continue; } catch (Exception ex) { CrashReporter.Log($"[GameDetectionService.FindBattleNetGames] Exe check failed for '{gameDir}' — {ex.Message}"); continue; }
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
                try { if (!HasFileShallow(gameDir, "*.exe", MaxScanDepth / 2)) continue; } catch (Exception ex) { CrashReporter.Log($"[GameDetectionService.FindBattleNetGames] Exe check failed for '{gameDir}' — {ex.Message}"); continue; }
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
                    // Extract InstallFolder values
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
                try { if (!HasFileShallow(gameDir, "*.exe", MaxScanDepth / 2)) continue; } catch (Exception ex) { CrashReporter.Log($"[GameDetectionService.FindRockstarGames] Exe check failed for '{gameDir}' — {ex.Message}"); continue; }
                games.Add(new DetectedGame { Name = dirName, InstallPath = gameDir, Source = "Rockstar" });
            }
        }

        CrashReporter.Log($"[GameDetectionService.FindRockstarGames] Found {games.Count} game(s)");
        return games;
    }
}
