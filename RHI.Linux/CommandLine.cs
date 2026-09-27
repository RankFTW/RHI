using System.Text.Json;
using RHI.Linux.Core;
using RenoDXCommander.Services;

namespace RHI.Linux;

internal static class CommandLine
{
    public static async Task<int> Run(string[] args)
    {
        using var http = Downloads.CreateClient();
        var catalog = new Catalog(http);
        if (args[0] == "--scan")
        {
            var settings = Settings.Load();
            var discovery = new GameDiscovery();
            var games = discovery.Scan(GameDiscovery.DefaultSteamRoots(LinuxPaths.Home).Concat(settings.SteamRoots), settings, catalog);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                Games = games.Select(g => new { g.Name, g.Source, g.AppId, g.Root, g.Executable, g.Prefix,
                    Architecture = g.Architecture.ToString(), Api = g.Api.ToString(), RenoDX = catalog.Match(g)?.Name }),
                discovery.Warnings
            }, LinuxPaths.Json));
            return 0;
        }
        if (args[0] == "--catalog-check")
        {
            await catalog.Refresh(); Console.WriteLine(catalog.Status); return 0;
        }
        if (args[0] == "--smoke-test") return await SmokeTest(http, catalog);
        if (args[0] == "--nr-smoke-test") return await NeuralRenderingSmokeTest(http, catalog);
        if (args[0] == "--prepare" && args.Length >= 2) return await Prepare(args[1], args.Contains("--ue-hdr"), args.Contains("--nightly"), http, catalog);
        if (args[0] == "--save-launch-options" && args.Length == 2)
        {
            var game = FindGame(args[1], catalog);
            var proxy = new Installation(game.InstallDirectory).ReadState().Proxy ?? throw new IOException("Install ReShade first.");
            var configs = Proton.LocalConfigs(game).ToList();
            if (configs.Count != 1) throw new IOException("Select the Steam user account in the desktop app to save launch options.");
            var options = Proton.LaunchOptions(Proton.ReadOptions(configs[0], args[1]) ?? "%command%", proxy, NeuralRenderingSetup.Extras(game, Settings.Load().For(game)));
            Console.WriteLine("Saved. Backup: " + Proton.SaveOptions(configs[0], args[1], options));
            Console.WriteLine(options); return 0;
        }
        if (args[0] == "--restore-hdr" && args.Length == 2)
        {
            var game = FindGame(args[1], catalog);
            foreach (var ini in IniSettings.FindEngineInis(game)) IniSettings.Restore(ini);
            IniSettings.Restore(LinuxPaths.ResolveCase(game.InstallDirectory, "ReShade.ini"));
            Console.WriteLine("Previous HDR settings and file permissions restored."); return 0;
        }
        Console.WriteLine("RHI Linux\n  (no arguments)    Open the desktop app\n  --scan            Print detected games, executable paths and Proton prefixes as JSON\n  --catalog-check   Fetch and validate the live RenoDX catalogue\n  --smoke-test      Test real downloads, install/update/remove in an isolated temporary directory\n  --nr-smoke-test   Install, swap and remove every Neural Rendering (DLSS 5) method in a temporary game\n  --prepare APPID [--ue-hdr] [--nightly]  Install ReShade using the saved channel, matched RenoDX and shaders\n  --save-launch-options APPID  Save its DLL override with Steam fully closed\n  --restore-hdr APPID  Restore previous HDR settings and Engine.ini permissions\n");
        return args[0] is "--help" or "-h" ? 0 : 2;
    }

    private static Game FindGame(string appId, Catalog catalog)
    {
        if (!uint.TryParse(appId, out _)) throw new ArgumentException("Expected a Steam app ID.");
        var settings = Settings.Load();
        var matches = new GameDiscovery().Scan(GameDiscovery.DefaultSteamRoots(LinuxPaths.Home).Concat(settings.SteamRoots), settings, catalog).Where(g => g.AppId == appId).ToList();
        if (matches.Count != 1) throw new IOException("Expected one installed game with this app ID; select the library in the desktop app instead.");
        return matches[0];
    }

    private static async Task<int> Prepare(string appId, bool hdr, bool nightly, HttpClient http, Catalog catalog)
    {
        var progress = new Progress<string>(Console.WriteLine);
        await catalog.Refresh(progress);
        var game = FindGame(appId, catalog);
        var settings = Settings.Load(); var prefs = settings.For(game);
        var channel = nightly ? "Nightly" : prefs.Channel;
        var mod = catalog.Match(game) ?? throw new IOException("No exact RenoDX catalogue match. Select a mod in the desktop app.");
        var url = catalog.AddonUrl(game, mod) ?? throw new IOException("No direct addon download for this game and architecture.");
        var api = Enum.TryParse<RenoDXCommander.Models.GraphicsApiType>(catalog.ManifestString("graphicsApiOverrides", game.Name)?.Replace("DX", "DirectX"), out var known) ? known : game.Api;
        var proxy = Installation.ProxyFor(api);
        var engineInis = hdr ? IniSettings.FindEngineInis(game) : [];
        if (hdr && (!url.Contains("ue-extended") || engineInis.Count != 1)) throw new IOException("Automatic HDR configuration requires UE Extended and exactly one Engine.ini. Select the file in the desktop app instead.");
        Console.WriteLine($"Preparing {game.Name}\nExecutable: {game.Executable}\nPrefix: {game.Prefix}\nAddon: {url}");
        var downloads = new Downloads(http);
        var (reshade, version) = await downloads.ReShade(channel, game.Architecture, progress);
        try
        {
            var addon = await downloads.Fetch(url, progress, true);
            Downloads.ValidatePe(addon, game.Architecture);
            var shaderPayloads = new Dictionary<string, List<Payload>>();
            foreach (var pack in new[] { "Standard", "Lilium HDR" }) shaderPayloads[pack] = await downloads.Shaders(pack, progress);
            var install = new Installation(game.InstallDirectory);
            var compiler = await downloads.ShaderCompiler(game.Architecture, progress);
            install.Install("ReShade", version, [new(proxy, File.ReadAllBytes(reshade)), Installation.DefaultIni(), compiler], proxy: proxy);
            install.Install("RenoDX", mod.Name + " • " + DateTime.UtcNow.ToString("yyyy-MM-dd"), [new(Path.GetFileName(new Uri(url).AbsolutePath), File.ReadAllBytes(addon))]);
            foreach (var (pack, payload) in shaderPayloads) install.Install("Shaders: " + pack, DateTime.UtcNow.ToString("yyyy-MM-dd"), payload);
            if (hdr)
            {
                IniSettings.Apply(engineInis[0], IniSettings.UnrealHdr, readOnly: true);
                IniSettings.Apply(LinuxPaths.ResolveCase(game.InstallDirectory, "ReShade.ini"), IniSettings.RenoDxHdr);
            }
            prefs.Executable = game.Executable; prefs.Prefix = game.Prefix; prefs.ModName = mod.Name; prefs.Api = api.ToString(); prefs.Channel = channel; settings.LastGameId = game.Id; settings.Save();
            var options = Proton.LaunchOptions("%command%", proxy);
            LinuxPaths.WriteJson(Path.Combine(LinuxPaths.Data, "prepared-" + appId + ".json"), new
            {
                game.Name, game.AppId, game.Executable, game.Prefix, EngineIni = engineInis.FirstOrDefault(),
                LaunchOptions = options, State = install.ReadState(), PreparedAt = DateTimeOffset.UtcNow
            });
            Console.WriteLine($"Installed ReShade {version}, {mod.Name} RenoDX and both shader packs.\nLaunch options: {options}\nUse --save-launch-options {appId} with Steam closed, or copy the options into Steam.");
            return 0;
        }
        finally { File.Delete(reshade); }
    }

    private static async Task<int> SmokeTest(HttpClient http, Catalog catalog)
    {
        var temp = Path.Combine(Path.GetTempPath(), "rhi-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var progress = new Progress<string>(Console.WriteLine);
        var downloads = new Downloads(http);
        try
        {
            await catalog.Refresh(progress);
            Console.WriteLine(catalog.Status);
            foreach (var arch in new[] { MachineType.I386, MachineType.x64 })
            {
                var (path, version) = await downloads.ReShade("Stable", arch, progress);
                try
                {
                    var directory = Path.Combine(temp, arch.ToString()); Directory.CreateDirectory(directory);
                    File.WriteAllText(Path.Combine(directory, "DXGI.DLL"), "original proxy");
                    var install = new Installation(directory);
                    var payload = File.ReadAllBytes(path);
                    var compiler = await downloads.ShaderCompiler(arch, progress);
                    install.Install("ReShade", version, [new("dxgi.dll", payload), Installation.DefaultIni(), compiler], true, "dxgi.dll");
                    install.Install("ReShade", version, [new("dxgi.dll", payload), Installation.DefaultIni(), compiler], false, "dxgi.dll");
                    if (Installation.Hash(File.ReadAllBytes(Path.Combine(directory, "DXGI.DLL"))) != Installation.Hash(payload)) throw new Exception("Installed ReShade hash mismatch.");
                    if (arch == MachineType.x64)
                    {
                        var mod = catalog.Mods.First(m => m.Name == "Generic Unreal Engine");
                        var addon = await downloads.Fetch(mod.SnapshotUrl!, progress, true);
                        Downloads.ValidatePe(addon, arch);
                        install.Install("RenoDX", "Generic Unreal Engine", [new("renodx-unrealengine.addon64", File.ReadAllBytes(addon))]);
                        foreach (var pack in new[] { "Standard", "Lilium HDR" })
                        {
                            var shaders = await downloads.Shaders(pack, progress);
                            install.Install("Shaders: " + pack, "smoke", shaders);
                            Console.WriteLine($"{pack}: {shaders.Count} shader / texture files verified.");
                        }
                        File.AppendAllText(Path.Combine(directory, "ReShade.ini"), "\n; User preference\n");
                    }
                    install.Remove();
                    if (File.ReadAllText(Path.Combine(directory, "DXGI.DLL")) != "original proxy") throw new Exception("Original proxy was not restored.");
                    if (install.ReadState().Components.Count != 0) throw new Exception("Uninstall retained components.");
                    if (arch == MachineType.x64 && !File.ReadAllText(Path.Combine(directory, "ReShade.ini")).Contains("User preference")) throw new Exception("User settings were not retained.");
                    Console.WriteLine($"PASS {arch}: ReShade {version}, install, update, case handling, uninstall and original restoration.");
                }
                finally { File.Delete(path); }
            }
            var (nightly, nightlyVersion) = await downloads.ReShade("Nightly", MachineType.x64, progress);
            File.Delete(nightly);
            Console.WriteLine("PASS: " + nightlyVersion + " x64 download and PE validation.");
            Console.WriteLine("PASS: online installation smoke test. No installed games were modified.");
            return 0;
        }
        finally { Directory.Delete(temp, true); }
    }
    // Real downloads, installed into disposable fake games: every Neural Rendering method must
    // install, swap versions in place, and remove back to the exact original files.
    private static async Task<int> NeuralRenderingSmokeTest(HttpClient http, Catalog catalog)
    {
        var temp = Path.Combine(Path.GetTempPath(), "rhi-nr-smoke-" + Guid.NewGuid().ToString("N"));
        // Keep INI backup records out of the user's data folder; downloads still use the shared cache.
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", Path.Combine(temp, "data"));
        var progress = new Progress<string>(Console.WriteLine);
        var downloads = new Downloads(http);
        var dlss = new DlssCatalog(http, downloads); var releases = new AddonReleases(http, downloads);
        var setup = new NeuralRenderingSetup(downloads, dlss, releases, catalog);
        await dlss.Refresh(); await releases.Refresh(true);
        Console.WriteLine(dlss.Status + " " + releases.Status);
        static byte[] Pe(MachineType machine)
        {
            var bytes = new byte[512]; bytes[0] = (byte)'M'; bytes[1] = (byte)'Z'; BitConverter.GetBytes(128).CopyTo(bytes, 0x3c);
            bytes[128] = (byte)'P'; bytes[129] = (byte)'E'; BitConverter.GetBytes((ushort)machine).CopyTo(bytes, 132); return bytes;
        }
        static Dictionary<string, string> Snapshot(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => !Path.GetRelativePath(root, f).StartsWith(".rhi-linux")).ToDictionary(f => Path.GetRelativePath(root, f), f => Installation.Hash(File.ReadAllBytes(f)));
        var cases = new (string Name, string Method, MachineType Machine, RenoDXCommander.Models.GraphicsApiType Api)[]
        {
            ("ShortFuse", NrMethod.ShortFuse, MachineType.x64, RenoDXCommander.Models.GraphicsApiType.DirectX12),
            ("Dlss5Tool", NrMethod.Dlss5Tool, MachineType.x64, RenoDXCommander.Models.GraphicsApiType.DirectX12),
            ("Bridge", NrMethod.Bridge, MachineType.x64, RenoDXCommander.Models.GraphicsApiType.DirectX11),
            ("Feeder", NrMethod.Feeder, MachineType.x64, RenoDXCommander.Models.GraphicsApiType.DirectX11),
            ("FeederDx9", NrMethod.Feeder, MachineType.x64, RenoDXCommander.Models.GraphicsApiType.DirectX9),
            ("Feeder32", NrMethod.Feeder, MachineType.I386, RenoDXCommander.Models.GraphicsApiType.DirectX9),
        };
        try
        {
            foreach (var (name, method, machine, api) in cases)
            {
                var root = Path.Combine(temp, name); var binaries = Path.Combine(root, "Game/Binaries/Win64"); Directory.CreateDirectory(binaries);
                var exe = Path.Combine(binaries, "Game.exe"); File.WriteAllBytes(exe, Pe(machine));
                // A game-owned DLSS DLL in an Unreal plugin folder must be backed up and restored.
                var plugin = Path.Combine(root, "Engine/Plugins/Runtime/Nvidia/DLSS/Binaries/ThirdParty/Win64"); Directory.CreateDirectory(plugin);
                File.WriteAllText(Path.Combine(plugin, "nvngx_dlss.dll"), "game original");
                File.WriteAllText(Path.Combine(binaries, "ReShadePreset.ini"), "Techniques=Existing@Existing.fx\n\n[Existing.fx]\nValue=1\n");
                var game = new Game { Name = "NR " + name, Root = root, Executable = exe, Executables = [exe] };
                var install = new Installation(binaries);
                install.Install("ReShade", "smoke", [new("dxgi.dll", Pe(machine)), Installation.DefaultIni()], proxy: "dxgi.dll");
                var before = Snapshot(root);
                var prefs = new GamePreferences { NrCostScaler = method == NrMethod.Dlss5Tool, SfAutoConfig = method == NrMethod.ShortFuse };
                await setup.Install(game, prefs, method, api, "Stable", progress);
                var state = NeuralRenderingSetup.Read(game);
                var tags = state.Tags(method, true).ToList();
                Console.WriteLine($"{name}: " + string.Join("  ", tags.Select(t => t.Text)));
                if (!state.Installed(method) || state.Method != method) throw new Exception(name + ": method not detected after install.");
                var missing = tags.Where(t => !t.Ok).Select(t => t.Text).ToList();
                if (missing.Count > 0) throw new Exception(name + ": missing " + string.Join(", ", missing));
                if (state.Detection.Version(DlssKind.NR) is null or "Unknown") throw new Exception(name + ": NR DLL version unreadable.");
                if (method != NrMethod.Feeder && state.Detection.Path(DlssKind.SR) != Path.Combine(plugin, "nvngx_dlss.dll")) throw new Exception(name + ": SR was not deployed to the game's plugin copy.");
                if (method == NrMethod.Dlss5Tool && !state.CostScaler) throw new Exception(name + ": Cost Scaler not installed.");
                var extras = NeuralRenderingSetup.Extras(game, prefs);
                Console.WriteLine($"{name}: launch options: " + Proton.LaunchOptions("%command%", "dxgi.dll", extras));
                // Swap to the previous addon and NR DLL release in place.
                var addonType = method == NrMethod.ShortFuse ? AddonReleases.ShortFuse : AddonReleases.Dlss5Tool;
                prefs.NrAddonVersion = releases.Versions(addonType).Skip(1).FirstOrDefault();
                prefs.NrDllVersion = dlss.Versions(DlssKind.NR).Skip(1).FirstOrDefault();
                await setup.Install(game, prefs, method, api, "Stable", progress);
                var swapped = NeuralRenderingSetup.LoadRecord(binaries)!;
                if (swapped.AddonVersion != prefs.NrAddonVersion) throw new Exception(name + ": addon swap did not apply.");
                Console.WriteLine($"{name}: swapped to addon {swapped.AddonVersion}, NR {NeuralRenderingSetup.Read(game).Detection.Version(DlssKind.NR)}");
                await setup.Remove(game);
                var after = Snapshot(root);
                var changed = before.Keys.Union(after.Keys).Where(k => before.GetValueOrDefault(k) != after.GetValueOrDefault(k)).ToList();
                if (changed.Count > 0) throw new Exception(name + ": removal left differences: " + string.Join(", ", changed));
                Console.WriteLine($"PASS {name}: install, status, launch settings, in-place swap and exact removal.");
            }
            Console.WriteLine("PASS: Neural Rendering smoke test. No installed games were modified.");
            return 0;
        }
        finally { Directory.Delete(temp, true); }
    }
}

