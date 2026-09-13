using System.Diagnostics;
using System.Text.RegularExpressions;

namespace RHI.Linux.Core;

public static class Proton
{
    public static string MergeOverrides(string existing, IEnumerable<string> required)
    {
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in existing.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = entry.Split('=', 2);
            if (pair.Length != 2) throw new FormatException("Malformed WINEDLLOVERRIDES entry: " + entry);
            foreach (var name in pair[0].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) overrides[name] = pair[1];
        }
        foreach (var dll in required) overrides[dll] = "n,b";
        return string.Join(';', overrides.Select(p => $"{p.Key}={p.Value}"));
    }

    public static string LaunchOptions(string existing, string proxy)
    {
        var dll = Path.GetFileNameWithoutExtension(proxy);
        if (dll is not "dxgi" and not "d3d9" and not "opengl32") throw new ArgumentException("Unsupported ReShade proxy.");
        if (existing.Contains('\n') || existing.Contains('\r')) throw new FormatException("Launch options must be on one line.");
        // Steam also accepts plain game arguments without %command%. Keep them after
        // the inserted command when adding the environment-variable prefix.
        if (!existing.Contains("%command%") && existing.TrimStart().StartsWith('-') && existing.IndexOfAny([';', '|', '&', '`', '$', '<', '>']) < 0)
            existing = "%command% " + existing;
        var regex = new Regex("(?<!\\S)WINEDLLOVERRIDES=(?:\"([^\"]*)\"|'([^']*)'|([^\\s]+))");
        var matches = regex.Matches(existing);
        if (matches.Count > 1) throw new FormatException("Multiple WINEDLLOVERRIDES assignments. Combine them before applying launch options.");
        if (matches.Count == 1)
        {
            var m = matches[0];
            var value = m.Groups.Cast<Group>().Skip(1).First(g => g.Success).Value;
            if (value.IndexOfAny(['$', '`', '\\', '"', '\'']) >= 0) throw new FormatException("Dynamic WINEDLLOVERRIDES cannot be merged automatically. Enter literal DLL overrides.");
            var merged = "WINEDLLOVERRIDES='" + MergeOverrides(value, [dll, "d3dcompiler_47"]) + "'";
            existing = existing[..m.Index] + merged + existing[(m.Index + m.Length)..];
        }
        else
        {
            if (existing.Contains("WINEDLLOVERRIDES", StringComparison.Ordinal)) throw new FormatException("Could not safely parse the existing DLL overrides.");
            existing = "WINEDLLOVERRIDES='" + MergeOverrides("", [dll, "d3dcompiler_47"]) + "' " + (string.IsNullOrWhiteSpace(existing) ? "%command%" : existing);
        }
        if (!existing.Contains("%command%", StringComparison.Ordinal))
            throw new FormatException("Add %command% where Steam should insert the game command, then generate again.");
        return existing;
    }

    public static IEnumerable<string> LocalConfigs(Game game)
    {
        if (game.SteamRoot is null || game.AppId is null) return [];
        var users = Path.Combine(game.SteamRoot, "userdata");
        return Directory.Exists(users) ? Directory.EnumerateDirectories(users)
            .Select(p => Path.Combine(p, "config/localconfig.vdf")).Where(File.Exists).ToArray() : [];
    }

    private static VdfNode? AppNode(string content, string appId) => Vdf.Parse(content)
        .At("UserLocalConfigStore", "Software", "Valve", "Steam", "apps", appId);

    public static string? ReadOptions(string config, string appId) => AppNode(File.ReadAllText(config), appId)?.Text("LaunchOptions");

    public static string EditOptions(string content, string appId, string options)
    {
        if (!uint.TryParse(appId, out _)) throw new ArgumentException("Invalid Steam App ID.");
        var root = Vdf.Parse(content);
        var apps = root.At("UserLocalConfigStore", "Software", "Valve", "Steam", "apps")
            ?? throw new IOException("Steam apps settings were not found. Set launch options through Steam instead.");
        var app = apps.Child(appId);
        var value = app?.Child("LaunchOptions");
        if (value != null) return content[..value.ValueStart] + Vdf.Quote(options) + content[value.ValueEnd..];
        if (app != null) return content.Insert(app.CloseOffset, "\n\t\t\t\t\t\t\"LaunchOptions\"\t\t" + Vdf.Quote(options) + "\n\t\t\t\t\t");
        return content.Insert(apps.CloseOffset, $"\n\t\t\t\t\t{Vdf.Quote(appId)}\n\t\t\t\t\t{{\n\t\t\t\t\t\t\"LaunchOptions\"\t\t{Vdf.Quote(options)}\n\t\t\t\t\t}}\n\t\t\t\t");
    }

    public static bool SteamRunning() => Process.GetProcessesByName("steam").Any(p => { p.Dispose(); return true; });

    public static string SaveOptions(string config, string appId, string options)
    {
        if (SteamRunning()) throw new IOException("Exit Steam completely before saving, so it cannot overwrite this change. Alternatively copy the options into Steam → Properties → Launch Options while Steam is open.");
        var original = File.ReadAllText(config);
        var edited = EditOptions(original, appId, options);
        var backup = config + ".rhi-backup-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        File.Copy(config, backup);
        var temp = config + ".rhi-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temp, edited);
            if (SteamRunning() || File.ReadAllText(config) != original) throw new IOException("Steam settings changed while saving; retry after exiting Steam.");
            File.Move(temp, config, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        return backup;
    }

    public static string LocalAppData(string prefix)
    {
        var users = Path.Combine(prefix, "drive_c/users");
        if (!Directory.Exists(users)) throw new DirectoryNotFoundException("This Proton prefix has not been created. Launch the game once through Steam first.");
        var user = Directory.EnumerateDirectories(users).FirstOrDefault(p => Path.GetFileName(p).Equals("steamuser", StringComparison.OrdinalIgnoreCase))
            ?? Directory.EnumerateDirectories(users).FirstOrDefault(p => !new[] { "Public", "Default", "All Users" }.Contains(Path.GetFileName(p), StringComparer.OrdinalIgnoreCase))
            ?? throw new DirectoryNotFoundException("No Wine user found in the prefix.");
        return LinuxPaths.ResolveCase(user, "AppData/Local");
    }

    public static void Open(string pathOrUri)
    {
        var start = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
        start.ArgumentList.Add(pathOrUri);
        Process.Start(start)?.Dispose();
    }
}
