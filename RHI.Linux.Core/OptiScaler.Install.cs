using System.Text.Json;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RHI.Linux.Core;

public sealed partial class OptiScaler
{
    // ── Install / update / remove ────────────────────────────────────────────
    public async Task Install(Game game, GamePreferences prefs, OptiScalerSettings settings, GraphicsApiType api, IProgress<string>? progress = null)
    {
        GameSetup.RequireClosed(game);
        if (game.Architecture != MachineType.x64) throw new IOException("OptiScaler supports 64-bit games only.");
        var dir = game.InstallDirectory;
        var variant = OsVariant.Of(prefs);
        var dllName = DllFor(prefs, api);
        try { await Refresh(); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or JsonException) { CrashReporter.Log("OptiScaler releases: " + ex.Message); }

        // Download everything before touching the game folder.
        var (version, staged) = await Stage(variant, progress);
        var payloads = Payloads(staged, dllName);
        try { payloads.Add(new(OptiPatcherPath, await File.ReadAllBytesAsync(await OptiPatcher(progress)))); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException) { CrashReporter.Log("OptiPatcher: " + ex.Message); }
        var dlls = new List<(string Source, string Name)>();
        foreach (var kind in new[] { DlssKind.SR, DlssKind.RR, DlssKind.FG })
        {
            try { dlls.Add((await dlss.Fetch(kind, null, progress), DlssFiles.DllName(kind))); }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException) { CrashReporter.Log($"OptiScaler {DlssFiles.Label(kind)}: " + ex.Message); }
        }
        if (variant == OsVariant.DlssNr) dlls.Add((await dlss.Fetch(DlssKind.NR, prefs.OsNrRuntime, progress), DlssFiles.Nr));
        string? streamline = null;
        if (OsVariant.Advanced(variant) && prefs.OsDeployStreamline) streamline = await dlss.Fetch(DlssKind.Streamline, prefs.OsStreamlineVersion, progress);
        var template = Template(settings.EffectiveGpu, settings.DlssInputs, variant);
        var stagedIni = Path.Combine(staged, IniName);

        GameSetup.RequireClosed(game);
        progress?.Report($"Installing OptiScaler {Label(variant, version)}…");
        await Task.Run(() =>
        {
            if (FromWindows(game)) RemoveWindows(game);
            var previous = LoadRecord(dir);
            var installation = new Installation(dir);
            MoveReShadeAside(installation, dir, dllName);
            installation.Install(Component, Label(variant, version), payloads, replaceForeign: true);
            RestoreReShade(installation, dir);

            var record = new OptiScalerRecord
            {
                Variant = variant, Version = version, DllName = dllName, Dlls = previous?.Dlls ?? [],
                Folders = payloads.Select(p => p.RelativePath.Split('/')).Where(p => p.Length > 1).Select(p => p[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            };
            // A reinstall keeps the game's INI; an update merges the user's changes into the new release's INI;
            // a first install or a variant change starts from RHI's template.
            var ini = LinuxPaths.ResolveCase(dir, IniName);
            var ours = previous?.Ini == true && File.Exists(ini) && Sentinel.Placed(ini);
            string text;
            if (ours && previous!.Variant == variant)
                text = previous.Version != version && File.Exists(stagedIni) ? MergeIni(File.ReadAllText(ini), File.ReadAllText(stagedIni)) : File.ReadAllText(ini);
            else text = File.ReadAllText(template ?? stagedIni);
            WriteIni(dir, Configure(text, settings, prefs, variant));
            record.Ini = true;

            // The Neural Rendering section keeps the DLSS DLLs it placed; OptiScaler places the rest.
            var nrOwned = NeuralRenderingSetup.LoadRecord(dir)?.Dlls ?? [];
            foreach (var (source, name) in dlls)
            {
                var destination = LinuxPaths.ResolveCase(dir, name);
                if (nrOwned.Contains(destination)) continue;
                Sentinel.Deploy(source, destination);
                if (!record.Dlls.Contains(destination)) record.Dlls.Add(destination);
            }
            DeployStreamline(dir, streamline);
            SaveRecord(dir, record);
        });
    }

    // OptiScaler settings → NR Runtime: swaps nvngx_dlssnr.dll for the DLSS NR build.
    public async Task ApplyNrRuntime(Game game, GamePreferences prefs, IProgress<string>? progress = null)
    {
        GameSetup.RequireClosed(game);
        var dir = game.InstallDirectory;
        if (Record(game) is not { Variant: OsVariant.DlssNr } record) return;
        var destination = LinuxPaths.ResolveCase(dir, DlssFiles.Nr);
        if (NeuralRenderingSetup.LoadRecord(dir)?.Dlls.Contains(destination) == true) throw new IOException("The Neural Rendering section manages this game's NR DLL. Change its version there.");
        var source = await dlss.Fetch(DlssKind.NR, prefs.OsNrRuntime, progress);
        GameSetup.RequireClosed(game);
        Sentinel.Deploy(source, destination);
        if (!record.Dlls.Contains(destination)) { record.Dlls.Add(destination); SaveRecord(dir, record); }
    }

    public static async Task Remove(Game game)
    {
        GameSetup.RequireClosed(game);
        await Task.Run(() =>
        {
            var dir = game.InstallDirectory;
            if (FromWindows(game)) { RemoveWindows(game); return; }
            var record = LoadRecord(dir);
            DeployStreamline(dir, null);
            var installation = new Installation(dir);
            if (installation.ReadState().Components.ContainsKey(Component)) installation.Remove(Component);
            RestoreReShade(installation, dir);
            if (record != null)
            {
                if (record.Ini) Sentinel.Restore(LinuxPaths.ResolveCase(dir, IniName));
                var nrOwned = NeuralRenderingSetup.LoadRecord(dir)?.Dlls ?? [];
                foreach (var dll in Enumerable.Reverse(record.Dlls)) if (!nrOwned.Contains(dll)) Sentinel.Restore(dll);
                foreach (var folder in record.Folders.Append("plugins")) RemoveEmpty(LinuxPaths.ResolveCase(dir, folder));
            }
            RestoreEngineIni(game);
            SaveRecord(dir, null);
        });
    }

    private static void RemoveEmpty(string folder)
    {
        if (!Directory.Exists(folder) || new DirectoryInfo(folder).LinkTarget != null) return;
        foreach (var sub in Directory.EnumerateDirectories(folder)) RemoveEmpty(sub);
        if (!Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
    }

    // As Windows RHI's Uninstall, driven by its rhi_install.txt: delete what it deployed, restore each
    // ".original", and give ReShade64.dll its dxgi.dll name back.
    private static void RemoveWindows(Game game)
    {
        var dir = game.InstallDirectory;
        var manifest = WindowsManifest(game);
        if (manifest == null) return;
        string At(string relative) => LinuxPaths.ResolveCase(dir, relative);
        bool Shared(string name) => manifest.SharedFiles.TryGetValue(name, out var owners) && owners.Any(o => !o.Equals(Component, StringComparison.OrdinalIgnoreCase));
        var reshade = At(Installation.ReShadeBesideOptiScaler);
        var reshadeReturns = File.Exists(reshade) && manifest.InstalledAs.Equals(DefaultDll, StringComparison.OrdinalIgnoreCase);
        var dll = At(manifest.InstalledAs);
        if (File.Exists(dll)) File.Delete(dll);
        if (reshadeReturns) { if (File.Exists(Sentinel.BackupOf(dll))) File.Delete(Sentinel.BackupOf(dll)); }
        else Sentinel.Restore(dll);
        var ini = At(IniName);
        if (File.Exists(ini)) File.Delete(ini);
        Sentinel.Restore(ini);
        foreach (var name in manifest.Files)
        {
            if (name.Equals(IniName, StringComparison.OrdinalIgnoreCase) || name.Equals(manifest.InstalledAs, StringComparison.OrdinalIgnoreCase) || Shared(name)) continue;
            if (name.Equals(DlssFiles.Nr, StringComparison.OrdinalIgnoreCase) && (manifest.Variant != OsVariant.DlssNr || !string.IsNullOrEmpty(manifest.NrMethod))) continue;
            var path = At(name);
            if (File.Exists(path)) File.Delete(path);
            Sentinel.Restore(path);
        }
        foreach (var folder in manifest.Folders.Append("plugins").Append("OptiScaler"))
        {
            var path = At(folder);
            if (!Directory.Exists(path) || new DirectoryInfo(path).LinkTarget != null) continue;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Where(f => !f.EndsWith(Sentinel.Suffix, StringComparison.Ordinal)).ToList())
            { File.Delete(file); Sentinel.Restore(file); }
            RemoveEmpty(path);
        }
        if (reshadeReturns && !File.Exists(dll)) File.Move(reshade, dll);
        File.Delete(At("rhi_install.txt"));
    }
}
