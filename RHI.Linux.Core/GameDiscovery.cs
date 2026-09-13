using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using RenoDXCommander.Services;

namespace RHI.Linux.Core;

public sealed class GameDiscovery
{
    public List<string> Warnings { get; } = [];
    public static string NormalizeName(string name) => Regex.Replace(string.Concat(name.Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)).ToLowerInvariant(), "[^a-z0-9]", "");

    public static IEnumerable<string> DefaultSteamRoots(string home) => new[]
    {
        Path.Combine(home, ".local/share/Steam"), Path.Combine(home, ".steam/steam"), Path.Combine(home, ".steam/root"),
        Path.Combine(home, ".var/app/com.valvesoftware.Steam/.local/share/Steam"),
        Path.Combine(home, ".var/app/com.valvesoftware.Steam/.steam/steam")
    }.Concat(Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { } data ? [Path.Combine(data, "Steam")] : []);

    public List<Game> Scan(IEnumerable<string> steamRoots, Settings? settings = null, Catalog? catalog = null)
    {
        Warnings.Clear();
        var games = new List<Game>();
        var roots = steamRoots.Where(Directory.Exists).Select(LinuxPaths.Canonical).Distinct().ToList();
        var libraries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var root in roots)
        {
            libraries.TryAdd(root, root);
            var file = Path.Combine(root, "steamapps/libraryfolders.vdf");
            if (!File.Exists(file)) file = Path.Combine(root, "config/libraryfolders.vdf");
            if (!File.Exists(file)) continue;
            try
            {
                foreach (var node in Vdf.Parse(File.ReadAllText(file)).Child("libraryfolders")?.Children ?? [])
                {
                    var path = node.Text("path") ?? (int.TryParse(node.Key, out _) ? node.Value : null);
                    if (path != null && Directory.Exists(path)) libraries.TryAdd(LinuxPaths.Canonical(path), root);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
            { Warnings.Add($"{file}: {ex.Message}"); }
        }
        foreach (var (library, steamRoot) in libraries)
        {
            var apps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(apps)) continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
                {
                    try
                    {
                        var app = Vdf.Parse(File.ReadAllText(file)).Child("AppState");
                        var name = app?.Text("name"); var dir = app?.Text("installdir"); var id = app?.Text("appid");
                        if (name is null || dir is null || !uint.TryParse(id, out _) || IsTool(name)) continue;
                        var root = LinuxPaths.ResolveCase(Path.Combine(apps, "common"), dir);
                        if (!Directory.Exists(root)) continue;
                        var game = new Game { Name = name, AppId = id, Root = root, SteamRoot = steamRoot, Source = steamRoot.Contains("com.valvesoftware.Steam") ? "Steam Flatpak" : "Steam" };
                        game.Prefix = libraries.Keys.Prepend(library).Distinct()
                            .Select(l => Path.Combine(l, "steamapps/compatdata", id!, "pfx")).FirstOrDefault(Directory.Exists);
                        PopulateExecutables(game, catalog);
                        if (settings != null) ApplyPreferences(game, settings);
                        games.Add(game);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
                    { Warnings.Add($"{file}: {ex.Message}"); }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Warnings.Add($"{apps}: {ex.Message}"); }
        }
        foreach (var game in settings?.ManualGames ?? [])
        {
            if (!Directory.Exists(game.Root)) { Warnings.Add($"Missing manual game: {game.Root}"); continue; }
            PopulateExecutables(game, catalog);
            ApplyPreferences(game, settings!);
            games.Add(game);
        }
        return games.DistinctBy(g => g.Id).OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsTool(string name) => name.StartsWith("Proton", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Steam Linux Runtime", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Steamworks", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("Steam Controller", StringComparison.OrdinalIgnoreCase);

    public static void ApplyPreferences(Game game, Settings settings)
    {
        var p = settings.For(game);
        if (p.Executable != null && File.Exists(p.Executable) && LinuxPaths.IsWithin(game.Root, p.Executable)) game.Executable = p.Executable;
        if (p.Prefix != null) game.Prefix = p.Prefix;
    }

    public static void PopulateExecutables(Game game, Catalog? catalog = null)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint, MaxRecursionDepth = 12 };
        game.Executables = Directory.EnumerateFiles(game.Root, "*", options)
            .Where(p => p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            .Where(p => !Regex.IsMatch(Path.GetRelativePath(game.Root, p), @"(?i)(^|[/\\])(_commonredist|redist|easyanticheat|battleye|dotnet|crashreport[^/\\]*)([/\\]|$)"))
            .Where(p => !Regex.IsMatch(Path.GetFileName(p), @"(?i)(crash|unins|vcredist|dxsetup|unitycrash|reporter|vc_redist|setup)"))
            .OrderByDescending(p => p.Contains("shipping", StringComparison.OrdinalIgnoreCase))
            .ThenBy(p => Path.GetFileName(p).Contains("launcher", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(p => new FileInfo(p).Length).ToList();
        game.Executable = game.Executables.FirstOrDefault();
        var exeOverride = catalog?.ManifestString("launchExeOverrides", game.Name)?.Replace('\\', '/');
        var folderOverride = catalog?.ManifestString("installPathOverrides", game.Name)?.Replace('\\', '/');
        if (exeOverride != null)
            game.Executable = game.Executables.FirstOrDefault(p => Path.GetFileName(p).Equals(Path.GetFileName(exeOverride), StringComparison.OrdinalIgnoreCase)) ?? game.Executable;
        else if (folderOverride != null)
        {
            var folder = LinuxPaths.ResolveCase(game.Root, folderOverride);
            game.Executable = game.Executables.FirstOrDefault(p => Path.GetDirectoryName(p) == folder) ?? game.Executable;
        }
    }
}
