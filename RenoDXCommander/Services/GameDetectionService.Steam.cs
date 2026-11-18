// GameDetectionService.Steam.cs — Steam platform game detection
using System.Text.RegularExpressions;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public partial class GameDetectionService
{
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
                    if (installDir == null) continue;
                    var acfAppId = ExtractAppIdFromAcfFilename(Path.GetFileName(acf));
                    // A placeholder name ("appid_244160") is replaced below from Steam's appinfo.vdf
                    if (name == null && acfAppId == null) continue;
                    name ??= "";
                    var rootPath = Path.Combine(steamapps, "common", installDir);
                    if (!Directory.Exists(rootPath)) continue;
                    var game = new DetectedGame { Name = name, InstallPath = rootPath, Source = "Steam" };
                    if (acfAppId.HasValue)
                        game.SteamAppId = acfAppId.Value;
                    games.Add(game);
                }
                catch (Exception ex) { CrashReporter.Log($"[GameDetectionService.FindSteamGames] Failed to parse ACF '{acf}' — {ex.Message}"); }
            }
        }
        FixPlaceholderNames(games, steamPath);
        return games;
    }

    /// <summary>
    /// Steam writes "appid_&lt;id&gt;" (or no name) into an app manifest when it installs a game before it has
    /// the app's details, and keeps it until the game updates; the Steam library shows the real name from
    /// appcacheppinfo.vdf. Use that name. Games whose name can't be found keep the placeholder (or the
    /// install folder's name when there is none at all).
    /// </summary>
    private static void FixPlaceholderNames(List<DetectedGame> games, string steamPath)
    {
        var placeholders = games.Where(g => SteamAppInfoReader.IsPlaceholderName(g.Name, g.SteamAppId)).ToList();
        if (placeholders.Count == 0) return;
        var ids = placeholders.Where(g => g.SteamAppId.HasValue).Select(g => g.SteamAppId!.Value).Distinct().ToList();
        var names = SteamAppInfoReader.ReadNames(Path.Combine(steamPath, "appcache", "appinfo.vdf"), ids);
        foreach (var g in placeholders)
        {
            var old = g.Name;
            if (g.SteamAppId is int id && names.TryGetValue(id, out var real))
                g.Name = real;
            else if (string.IsNullOrWhiteSpace(g.Name))
                g.Name = Path.GetFileName(g.InstallPath.TrimEnd(Path.DirectorySeparatorChar));
            CrashReporter.Log(g.Name == old
                ? $"[GameDetectionService.FixPlaceholderNames] '{old}': no name in Steam's appinfo.vdf, kept"
                : $"[GameDetectionService.FixPlaceholderNames] '{old}' -> '{g.Name}' (Steam's manifest has no real name)");
        }
    }

    /// <summary>
    /// Extracts the numeric Steam AppID from an ACF filename like "appmanifest_601150.acf".
    /// Returns null if the filename does not match the expected pattern.
    /// </summary>
    internal static int? ExtractAppIdFromAcfFilename(string filename)
    {
        var name = Path.GetFileNameWithoutExtension(filename);
        if (name != null
            && name.StartsWith("appmanifest_")
            && int.TryParse(name["appmanifest_".Length..], out var appId))
        {
            return appId;
        }
        return null;
    }

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
}
