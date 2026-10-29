using System.Text.RegularExpressions;

namespace RHI.Linux.Core;

public sealed record ComponentStatus(string Name, string? Version, bool Installed, bool Damaged, bool Applied)
{
    public string Label => Damaged ? "Needs repair" : Applied ? "Applied" : Installed ? "Installed" : "Not installed";
    public string? Channel => Name != "ReShade" || Version == null ? null : Version.StartsWith("Nightly") ? "Nightly" : Version == "Local" ? "Local" : "Stable";
}

public sealed class InstallationStatus
{
    public InstallState State { get; init; } = new();
    public Dictionary<string, ComponentStatus> Components { get; init; } = [];
    public bool LaunchConfigured { get; init; }
    public bool HdrConfigured { get; init; }
    // DLSS / Neural Rendering launch settings (environment and extra DLL overrides).
    public bool DlssLaunchConfigured { get; init; } = true;
    public string? Error { get; init; }
    public ComponentStatus Get(string component) => Components.GetValueOrDefault(component) ?? new(component, null, false, false, false);

    public static bool HasExtras(string? options, LaunchExtras extras)
    {
        if (options == null) return extras.Dlls.Count == 0 && Proton.HasEnvironment("", extras);
        var value = Proton.ReadVariable(options, "WINEDLLOVERRIDES") ?? "";
        var entries = value.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(e => e.Split('=', 2)).Where(p => p.Length == 2)
            .SelectMany(p => p[0].Split(',').Select(k => (Key: k.Trim(), Mode: p[1].Split(',')[0].Trim()))).ToList();
        return extras.Dlls.All(d => entries.Any(e => e.Key.Equals(d, StringComparison.OrdinalIgnoreCase) && e.Mode == "n")) && Proton.HasEnvironment(options, extras);
    }

    public static bool HasLaunchOverrides(string? options, string proxy, LaunchExtras? extras = null)
    {
        extras ??= LaunchExtras.None;
        if (options == null || !options.Contains("%command%")) return false;
        var matches = Regex.Matches(options, "(?<!\\S)WINEDLLOVERRIDES=(?:\"([^\"]*)\"|'([^']*)'|([^\\s]+))");
        if (matches.Count != 1) return false;
        var value = matches[0].Groups.Cast<Group>().Skip(1).First(g => g.Success).Value;
        var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in value.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = entry.Split('=', 2);
            if (pair.Length != 2) return false;
            foreach (var key in pair[0].Split(',')) entries[key.Trim()] = pair[1].Trim();
        }
        return new[] { Path.GetFileNameWithoutExtension(proxy), "d3dcompiler_47" }.Concat(extras.Dlls)
            .All(k => entries.TryGetValue(k, out var mode) && mode.Split(',')[0].Trim() == "n") && Proton.HasEnvironment(options, extras);
    }

    public static InstallationStatus Read(Game game, string? steamConfig = null, LaunchExtras? extras = null)
    {
        if (game.Executable == null) return new();
        try
        {
            var state = new Installation(game.InstallDirectory).ReadState();
            var components = new Dictionary<string, ComponentStatus>();
            var logPath = LinuxPaths.ResolveCase(game.InstallDirectory, "ReShade.log");
            var log = File.Exists(logPath) ? File.ReadAllText(logPath) : "";
            var logTime = File.Exists(logPath) ? File.GetLastWriteTimeUtc(logPath) : DateTime.MinValue;
            var latestPayload = state.Files.Where(f => !f.Mutable).Select(f => LinuxPaths.ResolveCase(game.InstallDirectory, f.Path))
                .Where(File.Exists).Select(File.GetLastWriteTimeUtc).DefaultIfEmpty(DateTime.MaxValue).Max();
            var fromThisGame = log.Contains(Path.GetFileName(game.Executable), StringComparison.OrdinalIgnoreCase) && logTime >= latestPayload;
            foreach (var (name, version) in state.Components)
            {
                var files = state.Files.Where(f => f.Component == name).ToList();
                var valid = files.Count > 0 && files.All(f =>
                {
                    var path = LinuxPaths.ResolveCase(game.InstallDirectory, f.Path);
                    return File.Exists(path) && (f.Mutable || Installation.Hash(File.ReadAllBytes(path)) == f.Hash);
                });
                var applied = valid && fromThisGame && (name == "ReShade"
                    ? log.Contains("Initializing crosire's ReShade") && (log.Contains("Created runtime environment") || log.Contains("Recreated runtime environment"))
                    : name == "RenoDX" && log.Contains("Registered add-on \"RenoDX\""));
                components[name] = new(name, version, valid, !valid, applied);
            }
            var configs = Proton.LocalConfigs(game).ToList();
            var selectedConfig = steamConfig != null && configs.Contains(steamConfig) ? steamConfig : configs.Count == 1 ? configs[0] : null;
            var configured = selectedConfig != null && HasLaunchOverrides(Proton.ReadOptions(selectedConfig, game.AppId!), state.Proxy ?? "dxgi.dll", extras);
            var ini = LinuxPaths.ResolveCase(game.InstallDirectory, "ReShade.ini");
            var hdr = File.Exists(ini) && IniSettings.Get(File.ReadAllText(ini), "renodx", "Set_Path") == "0" &&
                IniSettings.FindEngineInis(game).Any(p => IniSettings.UnrealHdr.All(k => IniSettings.Get(File.ReadAllText(p), k.Section, k.Key) == k.Value));
            var dlssConfigured = extras == null || selectedConfig == null ? extras == null || HasExtras(null, extras) : HasExtras(Proton.ReadOptions(selectedConfig, game.AppId!), extras);
            return new() { State = state, Components = components, LaunchConfigured = configured, HdrConfigured = hdr, DlssLaunchConfigured = dlssConfigured };
        }
        catch (Exception ex) { return new() { Error = ex.Message }; }
    }
}
