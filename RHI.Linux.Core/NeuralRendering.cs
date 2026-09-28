using System.Text.Json;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RHI.Linux.Core;

// Linux Neural Rendering setup. Upstream has no single NR service (the logic lives in DgVoodooService.cs,
// DlssNrCostScalerService.cs and DetailPanelBuilder.NeuralRendering.cs), so this is split into partials
// that line up with it:
//   NeuralRendering.cs            - install record, state detection, Proton launch extras
//   NeuralRendering.Types.cs      - NrMethod, NrFiles, NrRecord, NrState
//   NeuralRendering.Staging.cs    - NrRelease, AddonReleases (release lists, downloads, staging)
//   NeuralRendering.Install.cs    - install / method swap / remove, ReShade ini + preset
//   NeuralRendering.DgVoodoo.cs   - dgVoodoo2 for DX9 Feeder installs (upstream DgVoodooService)
//   NeuralRendering.CostScaler.cs - DLSS NR Cost Scaler proxy swap (upstream DlssNrCostScalerService)

public sealed partial class NeuralRenderingSetup(Downloads downloads, DlssCatalog dlss, AddonReleases releases, Catalog catalog)
{
    public const string Component = "Neural Rendering", IniOwner = "neural-rendering";

    private static string Meta(string installDirectory) => LinuxPaths.ResolveCase(installDirectory, ".rhi-linux");
    private static string RecordFile(string installDirectory) => Path.Combine(Meta(installDirectory), "neural-rendering.json");
    public static NrRecord? LoadRecord(string installDirectory)
    {
        var file = RecordFile(installDirectory);
        try { return File.Exists(file) ? JsonSerializer.Deserialize<NrRecord>(File.ReadAllText(file), LinuxPaths.Json) : null; }
        catch (JsonException) { return null; }
    }
    private static void SaveRecord(string installDirectory, NrRecord? record)
    {
        var file = RecordFile(installDirectory);
        if (record == null) { if (File.Exists(file)) File.Delete(file); }
        else LinuxPaths.WriteJson(file, record);
    }
    private static string At(string directory, string relative) => LinuxPaths.ResolveCase(directory, relative);

    public static NrState Read(Game game)
    {
        if (game.Executable == null) return new();
        var dir = game.InstallDirectory; var is32 = game.Architecture == MachineType.I386;
        var host = At(dir, NrFiles.Host64);
        bool Exists(string relative) => File.Exists(At(dir, relative));
        var shaders = At(dir, NrFiles.ShadersDir);
        bool Shader(string name) => Directory.Exists(shaders) && Directory.EnumerateFiles(shaders, "*", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 4, IgnoreInaccessible = true })
            .Any(f => Path.GetFileName(f).Equals(name, StringComparison.OrdinalIgnoreCase));
        var record = LoadRecord(dir);
        return new()
        {
            Record = record, Is32Bit = is32, Root = dir,
            Dlss5Tool = is32 ? File.Exists(At(host, NrFiles.Dlss5Addon)) : Exists(NrFiles.Dlss5Addon),
            ShortFuse = Exists(NrFiles.SfAddon), Bridge = Exists(NrFiles.Bridge), Feeder = Exists(is32 ? NrFiles.Feeder32 : NrFiles.Feeder64),
            HostExe = File.Exists(At(host, NrFiles.HostExe)), FeedFx = Shader(NrFiles.FeedFx), Lumenite = Shader(NrFiles.LumeniteFx),
            DgVoodoo = record?.DgVoodoo == true ? Exists(NrFiles.DgVoodooDll) : Sentinel.Placed(At(dir, NrFiles.DgVoodooDll)),
            CostScaler = Exists(NrFiles.CostReal), NrOwned = Sentinel.Placed(At(dir, DlssFiles.Nr)),
            RootSr = Exists(DlssFiles.Sr), RootSrVersion = PeVersion.Read(At(dir, DlssFiles.Sr)) is { } sr ? PeVersion.Format(sr) : null,
            Detection = DlssScanner.Detect(game.Root),
        };
    }

    // DLL overrides and environment Proton needs for the installed Neural Rendering files.
    public static LaunchExtras Extras(Game game, GamePreferences prefs)
    {
        var record = game.Executable == null ? null : LoadRecord(game.InstallDirectory);
        var dlls = new List<string>();
        if (record?.DgVoodoo == true) dlls.Add("d3d9");
        if (record?.Host64 == true) dlls.Add("dxgi");
        return DlssProfile.Extras(prefs, record != null, dlls);
    }
}
