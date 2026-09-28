using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RHI.Linux.Core;

public sealed partial class NeuralRenderingSetup
{
    private static readonly string[] FeederTechniques = ["Lumenite_Kernel@lumenite_Kernel.fx", "DLSS5_Feed@DLSS5_Feed.fx"];
    private static readonly string[] FeederSorting = ["DLSS5_Feed_Debug@DLSS5_Feed.fx", "Lumenite_Kernel@lumenite_Kernel.fx", "DLSS5_Feed@DLSS5_Feed.fx"];
    private static readonly IniKey[] FeederParameters = new (string Key, string Value)[]
    {
        ("DEBUG_VIEW", "0"), ("DEPTH_TOLERANCE", "0.100000"), ("GEOM_AGREE_PX", "-1.500000"), ("GEOM_DYNAMIC_MARGIN", "0.250000"), ("GEOM_ENABLE", "3"),
        ("GEOM_MASK_REJECTED", "0.350000"), ("GEOM_OUTLIER_PX", "4.000000"), ("GEOM_PARALLAX", "1.020000"), ("LUMA_TOLERANCE", "0.280000"),
        ("MASK_STRENGTH", "1.000000"), ("MV_CONSISTENCY", "1.400000"), ("MV_LOWRES_FILTER", "0"), ("MV_PROVIDER_INFO", "0"), ("MV_SCALE", "1.000000"),
        ("MV_SIGN", "1.000000,1.000000"), ("MV_VALIDATE", "1"), ("STATIC_BIAS", "0.150000"), ("STATIC_MIN_CONTRAST", "0.012000"), ("VALIDATE_DEPTH", "1"),
        ("VALIDATE_LUMA", "0"), ("VALIDATE_MV", "1"), ("VALIDATE_STATIC", "1"),
    }.Select(p => new IniKey("DLSS5_Feed.fx", p.Key, p.Value)).ToArray();


    public async Task Install(Game game, GamePreferences prefs, string method, GraphicsApiType api, string reShadeChannel, IProgress<string>? progress = null)
    {
        GameSetup.RequireClosed(game);
        var dir = game.InstallDirectory; var is32 = game.Architecture == MachineType.I386;
        if (!NrMethod.All.Contains(method)) throw new ArgumentException("Unknown Neural Rendering method.");
        var previous = LoadRecord(dir);
        if (previous != null && previous.Method != method) { await Remove(game); previous = null; }

        // Download everything before touching the game folder.
        var record = new NrRecord { Method = method, CostScaler = prefs.NrCostScaler };
        var payloads = new List<Payload>();
        string? costDir = null, nrDll;
        (string Version, string Directory) addon, pack = default;
        // Every method deploys an addon: ShortFuse its own, the others the DLSS5 Tool (for the Feeder, as neural consumer).
        addon = await releases.Stage(method == NrMethod.ShortFuse ? AddonReleases.ShortFuse : AddonReleases.Dlss5Tool, prefs.NrAddonVersion, progress);
        record.AddonVersion = addon.Version;
        if (method is NrMethod.Bridge or NrMethod.Feeder)
        {
            pack = await releases.Stage(method == NrMethod.Bridge ? AddonReleases.Bridge : AddonReleases.Feeder, prefs.NrPackVersion, progress);
            record.PackVersion = pack.Version;
        }
        nrDll = await dlss.Fetch(DlssKind.NR, prefs.NrDllVersion, progress);
        record.NrVersion = PeVersion.Format(PeVersion.Read(nrDll)) is var v && v != "Unknown" ? v : prefs.NrDllVersion ?? dlss.Latest(DlssKind.NR);
        if (prefs.NrCostScaler) { costDir = (await releases.Stage(AddonReleases.CostScaler, null, progress)).Directory; }
        byte[] Read(string directory, string name) => File.ReadAllBytes(Path.Combine(directory, name));

        var dllsToDeploy = new List<(string Source, string Destination, bool IfAbsent)>();
        var detection = DlssScanner.Detect(game.Root);
        string DllTarget(DlssKind kind) => detection.Path(kind) ?? At(dir, DlssFiles.DllName(kind));
        switch (method)
        {
            case NrMethod.Dlss5Tool or NrMethod.Bridge:
                payloads.Add(new(NrFiles.Dlss5Addon, Read(addon.Directory, NrFiles.Dlss5Addon)));
                if (method == NrMethod.Bridge) payloads.Add(new(NrFiles.Bridge, Read(pack.Directory!, NrFiles.Bridge)));
                foreach (var kind in new[] { DlssKind.SR, DlssKind.RR, DlssKind.FG })
                    dllsToDeploy.Add((await dlss.Fetch(kind, null, progress), DllTarget(kind), false));
                dllsToDeploy.Add((nrDll, At(dir, DlssFiles.Nr), true));
                break;
            case NrMethod.ShortFuse:
                payloads.Add(new(NrFiles.SfAddon, Read(addon.Directory, NrFiles.SfAddon)));
                foreach (var kind in new[] { DlssKind.SR, DlssKind.RR, DlssKind.FG })
                    dllsToDeploy.Add((await dlss.Fetch(kind, null, progress), DllTarget(kind), false));
                dllsToDeploy.Add((nrDll, At(dir, DlssFiles.Nr), false));
                var streamline = await dlss.Fetch(DlssKind.Streamline, null, progress);
                var slTarget = detection.StreamlineFolder ?? dir;
                foreach (var file in Directory.EnumerateFiles(streamline))
                    dllsToDeploy.Add((file, At(slTarget, Path.GetFileName(file)), false));
                break;
            default:
                var feeder = is32 ? NrFiles.Feeder32 : NrFiles.Feeder64;
                if (!File.Exists(Path.Combine(pack.Directory!, feeder))) throw new IOException($"Feeder {pack.Version} has no {feeder}. Choose another Feeder version.");
                payloads.Add(new(feeder, Read(pack.Directory!, feeder)));
                // The neural consumer is 64-bit; 32-bit games run it in the Feeder's host64 helper.
                payloads.Add(new((is32 ? NrFiles.Host64 + "/" : "") + NrFiles.Dlss5Addon, Read(addon.Directory, NrFiles.Dlss5Addon)));
                var sr = await dlss.Fetch(DlssKind.SR, null, progress);
                dllsToDeploy.Add((sr, At(dir, DlssFiles.Sr), false));
                dllsToDeploy.Add((nrDll, At(dir, DlssFiles.Nr), true));
                if (!File.Exists(Path.Combine(pack.Directory!, NrFiles.FeedFx))) throw new IOException($"Feeder {pack.Version} does not include {NrFiles.FeedFx}.");
                payloads.Add(new(NrFiles.ShadersDir + "/" + NrFiles.FeedFx, Read(pack.Directory!, NrFiles.FeedFx)));
                payloads.AddRange(await releases.Lumenite(progress));
                if (is32)
                {
                    if (!File.Exists(Path.Combine(pack.Directory!, NrFiles.HostExe))) throw new IOException($"Feeder {pack.Version} has no 64-bit host for 32-bit games.");
                    var (reshade64, _) = await downloads.ReShade(reShadeChannel, MachineType.x64, progress);
                    try { payloads.Add(new(NrFiles.Host64 + "/dxgi.dll", File.ReadAllBytes(reshade64))); } finally { File.Delete(reshade64); }
                    payloads.Add(new(NrFiles.Host64 + "/" + NrFiles.HostExe, Read(pack.Directory!, NrFiles.HostExe)));
                    payloads.Add(new(NrFiles.Host64 + "/" + DlssFiles.Nr, File.ReadAllBytes(nrDll)));
                    payloads.Add(new(NrFiles.Host64 + "/" + DlssFiles.Sr, File.ReadAllBytes(sr)));
                    record.Host64 = true;
                }
                if (IsDx9(game, api))
                {
                    payloads.AddRange(await DgVoodoo(is32, progress));
                    record.DgVoodoo = true;
                }
                break;
        }
        if (costDir != null)
        {
            payloads.Add(new(NrFiles.CostIni, Read(costDir, NrFiles.CostIni)));
            if (File.Exists(Path.Combine(costDir, NrFiles.CostAddon))) payloads.Add(new(NrFiles.CostAddon, Read(costDir, NrFiles.CostAddon)));
        }

        GameSetup.RequireClosed(game);
        progress?.Report("Installing " + NrMethod.Name(method) + "…");
        await Task.Run(() =>
        {
            // Undo the Cost Scaler first so the real NR DLL is back in place before it is updated.
            UninstallCostScaler(dir, previous?.CostScaler == true);
            new Installation(dir).Install(Component, $"{NrMethod.Name(method)} {record.AddonVersion}".Trim(), payloads, replaceForeign: true);
            record.Dlls = previous?.Dlls ?? [];
            foreach (var (source, destination, ifAbsent) in dllsToDeploy)
            {
                var deployed = ifAbsent ? Sentinel.DeployIfAbsent(source, destination) : Deploy(source, destination);
                if (deployed && !record.Dlls.Contains(destination)) record.Dlls.Add(destination);
            }
            if (costDir != null) InstallCostScaler(dir, costDir);
            ApplyIni(game, method, prefs.SfAutoConfig);
            SaveRecord(dir, record);
        });
        prefs.NrMethod = method;
    }

    private static bool Deploy(string source, string destination) { Sentinel.Deploy(source, destination); return true; }

    private static void ApplyIni(Game game, string method, bool sfAutoConfig)
    {
        var dir = game.InstallDirectory;
        var ini = At(dir, "ReShade.ini");
        if (!File.Exists(ini)) return;
        IniSettings.Restore(ini, IniOwner);
        if (method == NrMethod.ShortFuse && sfAutoConfig)
            IniSettings.Apply(ini, [new("INSTALL", "HookStreamline", "1"), new("INSTALL", "HookDirectX", "1")], owner: IniOwner);
        var preset = PresetFile(dir, ini);
        IniSettings.Restore(preset, IniOwner);
        if (method != NrMethod.Feeder) return;
        var defines = IniSettings.Get(File.ReadAllText(ini), "GENERAL", "PreprocessorDefinitions");
        if (defines?.Contains("DLSS5_MV_PROVIDER", StringComparison.OrdinalIgnoreCase) != true)
            IniSettings.Apply(ini, [new("GENERAL", "PreprocessorDefinitions", string.IsNullOrEmpty(defines) ? "DLSS5_MV_PROVIDER=3" : defines.TrimEnd(',') + ",DLSS5_MV_PROVIDER=3")], owner: IniOwner);
        // Enable the Feeder techniques in the existing preset instead of replacing the user's effects.
        var text = File.Exists(preset) ? File.ReadAllText(preset) : "";
        string Merge(string? existing, string[] add) => string.Join(',', (existing ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Concat(add).Distinct(StringComparer.OrdinalIgnoreCase));
        var keys = new List<IniKey>
        {
            new("", "Techniques", Merge(IniSettings.Get(text, "", "Techniques"), FeederTechniques)),
            new("", "TechniqueSorting", Merge(IniSettings.Get(text, "", "TechniqueSorting"), FeederSorting)),
        };
        keys.AddRange(FeederParameters.Where(k => IniSettings.Get(text, k.Section, k.Key) == null));
        IniSettings.Apply(preset, keys, owner: IniOwner);
    }

    private static string PresetFile(string dir, string ini)
    {
        var path = IniSettings.Get(File.ReadAllText(ini), "GENERAL", "PresetPath")?.Replace('\\', '/').Trim();
        if (string.IsNullOrEmpty(path) || Path.IsPathRooted(path) || path.Contains(':')) path = "ReShadePreset.ini";
        path = string.Join('/', path.Split('/').Where(p => p is not "." and not ""));
        try { return At(dir, path); } catch (IOException) { return At(dir, "ReShadePreset.ini"); }
    }

    public async Task Remove(Game game)
    {
        GameSetup.RequireClosed(game);
        var dir = game.InstallDirectory;
        var record = LoadRecord(dir);
        await Task.Run(() =>
        {
            UninstallCostScaler(dir, record?.CostScaler == true);
            // DLSS DLLs OptiScaler also placed stay for it; its own removal restores them.
            var osOwned = OptiScaler.LoadRecord(dir)?.Dlls ?? [];
            if (record != null) { foreach (var dll in Enumerable.Reverse(record.Dlls)) if (!osOwned.Contains(dll)) Sentinel.Restore(dll); }
            else RemoveLegacy(game);
            var install = new Installation(dir);
            if (install.ReadState().Components.ContainsKey(Component)) install.Remove(Component);
            var host = At(dir, NrFiles.Host64);
            if (Directory.Exists(host) && !Directory.EnumerateFileSystemEntries(host).Any()) Directory.Delete(host);
            var ini = At(dir, "ReShade.ini");
            IniSettings.Restore(ini, IniOwner);
            if (File.Exists(ini)) IniSettings.Restore(PresetFile(dir, ini), IniOwner);
            else IniSettings.Restore(At(dir, "ReShadePreset.ini"), IniOwner);
            SaveRecord(dir, null);
        });
    }

    // Files placed by the Windows app (dual-boot libraries) have no Linux record: remove them the
    // way Windows RHI does, restoring every ".original" backup it left.
    private static void RemoveLegacy(Game game)
    {
        var dir = game.InstallDirectory;
        foreach (var file in new[] { NrFiles.Dlss5Addon, NrFiles.SfAddon, NrFiles.Bridge, NrFiles.Feeder64, NrFiles.Feeder32 })
        {
            var path = At(dir, file);
            if (File.Exists(path) && !new Installation(dir).ReadState().Files.Any(f => f.Path.Equals(file, StringComparison.OrdinalIgnoreCase))) File.Delete(path);
        }
        var detection = DlssScanner.Detect(game.Root);
        foreach (var kind in DlssFiles.Dlls)
        {
            Sentinel.Restore(At(dir, DlssFiles.DllName(kind)));
            if (detection.Path(kind) is { } path) Sentinel.Restore(path);
        }
        var host = At(dir, NrFiles.Host64);
        if (Directory.Exists(host) && File.Exists(At(host, NrFiles.HostExe))) Directory.Delete(host, true);
    }
}
