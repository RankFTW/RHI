using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using RenoDXCommander.Services;

namespace RHI.Linux.Core;

public enum DlssKind { SR, RR, FG, NR, Streamline }

public sealed class DlssManifestEntry
{
    public string Version { get; set; } = "";
    public string Url { get; set; } = "";
}

public sealed class DlssManifest
{
    public List<DlssManifestEntry>? Dlss { get; set; }
    public List<DlssManifestEntry>? Dlssd { get; set; }
    public List<DlssManifestEntry>? Dlssg { get; set; }
    public List<DlssManifestEntry>? Dlssnr { get; set; }
    public List<DlssManifestEntry>? Streamline { get; set; }
}

public static class DlssFiles
{
    public const string Sr = DlssFileDiscovery.Dlss, Rr = DlssFileDiscovery.Dlssd, Fg = DlssFileDiscovery.Dlssg, Nr = DlssFileDiscovery.Dlssnr, StreamlineCommon = DlssFileDiscovery.StreamlineIndicator;
    public const string CustomMarker = ".rhi_custom";
    public static readonly string[] Streamline = DlssFileDiscovery.StreamlineDlls;
    public static readonly DlssKind[] Dlls = [DlssKind.SR, DlssKind.RR, DlssKind.FG, DlssKind.NR];
    public static string DllName(DlssKind kind) => kind switch
    {
        DlssKind.SR => Sr, DlssKind.RR => Rr, DlssKind.FG => Fg, DlssKind.NR => Nr,
        _ => StreamlineCommon
    };
    public static string Label(DlssKind kind) => kind switch
    {
        DlssKind.SR => "DLSS Super Resolution", DlssKind.RR => "Ray Reconstruction", DlssKind.FG => "Frame Generation",
        DlssKind.NR => "Neural Rendering", _ => "Streamline"
    };
    public static string Short(DlssKind kind) => kind switch { DlssKind.SR => "SR", DlssKind.RR => "RR", DlssKind.FG => "FG", DlssKind.NR => "NR", _ => "SL" };
    // Users drop their own builds here, as with %LocalAppData%\RHI\Custom on Windows.
    public static string CustomDirectory => Path.Combine(LinuxPaths.Data, "Custom", "DLSS");
    public static string CustomStreamlineDirectory => Path.Combine(LinuxPaths.Data, "Custom", "Streamline");
    public static string? CustomFile(DlssKind kind)
    {
        var dir = kind == DlssKind.Streamline ? CustomStreamlineDirectory : CustomDirectory;
        if (!Directory.Exists(dir)) return null;
        var path = LinuxPaths.ResolveCase(dir, DllName(kind));
        return File.Exists(path) ? path : null;
    }
}

// Deploys use the Windows app's ".original" convention so both apps agree on what RHI placed:
// a real backup restores the game's file, a 0-byte sentinel means RHI created the file.
public static class Sentinel
{
    public const string Suffix = SentinelFiles.Suffix;
    public static string BackupOf(string path) => SentinelFiles.BackupOf(path);
    public static bool Placed(string path) => SentinelFiles.Placed(path);
    public static void Deploy(string source, string destination) => SentinelFiles.Deploy(source, destination);
    public static bool DeployIfAbsent(string source, string destination) => SentinelFiles.DeployIfAbsent(source, destination);
    public static void Restore(string destination) => SentinelFiles.Restore(destination);
    public static void Copy(string source, string destination) => SentinelFiles.Copy(source, destination);
}

public sealed class DlssDetection
{
    public Dictionary<DlssKind, string> Paths { get; } = [];
    public string? StreamlineFolder { get; set; }
    public string? StreamlineVersionFile { get; set; }
    private readonly Dictionary<DlssKind, string?> _versions = [], _originals = [];
    public bool Has(DlssKind kind) => kind == DlssKind.Streamline ? StreamlineFolder != null : Paths.ContainsKey(kind);
    public bool HasAny => Paths.Count > 0 || StreamlineFolder != null;
    public bool HasDlss => Paths.Count > 0;
    public string? Path(DlssKind kind) => kind == DlssKind.Streamline ? StreamlineFolder : Paths.GetValueOrDefault(kind);
    public string? Version(DlssKind kind) => _versions.GetValueOrDefault(kind);
    public string? Original(DlssKind kind) => _originals.GetValueOrDefault(kind);
    public bool IsCustom(DlssKind kind) => kind == DlssKind.Streamline
        ? StreamlineFolder != null && File.Exists(System.IO.Path.Combine(StreamlineFolder, "sl" + DlssFiles.CustomMarker))
        : Path(kind) is { } p && File.Exists(p + DlssFiles.CustomMarker);
    public bool HasBackup => Paths.Values.Any(Sentinel.Placed) || StreamlineFolder != null
        && DlssFiles.Streamline.Any(f => Sentinel.Placed(System.IO.Path.Combine(StreamlineFolder, f)));

    internal void ReadVersions()
    {
        foreach (var (kind, path) in Paths)
        {
            // With the NR Cost Scaler installed the real runtime sits beside its proxy.
            var real = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, "nvngx_dlssnr_real.dll");
            _versions[kind] = Format(PeVersion.Read(kind == DlssKind.NR && File.Exists(real) ? real : path));
            _originals[kind] = Sentinel.Placed(path) ? new FileInfo(Sentinel.BackupOf(path)).Length == 0 ? null : Format(PeVersion.Read(Sentinel.BackupOf(path))) : _versions[kind];
        }
        if (StreamlineFolder == null) return;
        // Use the highest-versioned Streamline DLL, as sl.common.dll can lag behind the others.
        var best = DlssFiles.Streamline.Select(f => System.IO.Path.Combine(StreamlineFolder, f)).Where(File.Exists)
            .Select(f => (Path: f, Version: PeVersion.Read(f))).Where(v => v.Version != null)
            .OrderByDescending(v => System.Version.TryParse(v.Version, out var parsed) ? parsed : new System.Version()).FirstOrDefault();
        StreamlineVersionFile = best.Path;
        _versions[DlssKind.Streamline] = Format(best.Version);
        var backup = best.Path == null ? null : Sentinel.BackupOf(best.Path);
        _originals[DlssKind.Streamline] = backup != null && File.Exists(backup) && new FileInfo(backup).Length > 0 ? Format(PeVersion.Read(backup)) : _versions[DlssKind.Streamline];
    }
    private static string? Format(string? raw) => raw == null ? null : PeVersion.Format(raw);
}

public static class DlssScanner
{
    // Linux already supplies the game root. OptiScaler and helper copies are not native game DLSS.
    public static DlssDetection Detect(string root)
    {
        var result = new DlssDetection();
        var files = DlssFileDiscovery.Scan(root,
            name => name.Equals(".rhi-linux", StringComparison.OrdinalIgnoreCase) || name.Equals("host64", StringComparison.OrdinalIgnoreCase),
            preferStreamlineIndicator: false);
        foreach (var kind in DlssFiles.Dlls)
            if (files.GameDlls.TryGetValue(DlssFiles.DllName(kind), out var path)) result.Paths[kind] = path;
        result.StreamlineFolder = files.StreamlineFolder;
        result.ReadVersions();
        return result;
    }
}

public sealed class DlssCatalog
{
    // The Windows app reads the same live list, so Linux sees new DLSS builds without an update.
    public const string ManifestUrl = Sources.DlssManifest;
    private readonly HttpClient _http;
    private readonly Downloads _downloads;
    private DlssManifest _manifest = new();
    public string Status { get; private set; } = "Using the bundled DLSS version list.";
    private static string CacheFile => Path.Combine(LinuxPaths.Cache, "dlss", "dlss_manifest.json");
    public static string CacheDirectory => Path.Combine(LinuxPaths.Cache, "dlss");

    public DlssCatalog(HttpClient http, Downloads downloads)
    {
        _http = http; _downloads = downloads;
        foreach (var file in new[] { CacheFile, Path.Combine(AppContext.BaseDirectory, "dlss_manifest.json") })
        {
            try
            {
                if (!File.Exists(file)) continue;
                _manifest = Parse(File.ReadAllText(file));
                if (file == CacheFile) Status = "Using the cached DLSS version list.";
                break;
            }
            catch (Exception ex) when (ex is JsonException or IOException) { }
        }
    }

    public static DlssManifest Parse(string json) =>
        JsonSerializer.Deserialize<DlssManifest>(json, LinuxPaths.Json) ?? throw new JsonException("Empty DLSS manifest.");

    public async Task Refresh()
    {
        var json = await _http.GetStringAsync(ManifestUrl);
        var manifest = Parse(json);
        if ((manifest.Dlss?.Count ?? 0) == 0) throw new IOException("The DLSS version list is empty; the cached list has been kept.");
        _manifest = manifest;
        Directory.CreateDirectory(Path.GetDirectoryName(CacheFile)!);
        await File.WriteAllTextAsync(CacheFile, json);
        Status = $"DLSS version list refreshed {DateTime.Now:t}.";
    }

    private List<DlssManifestEntry> Entries(DlssKind kind) => (kind switch
    {
        DlssKind.SR => _manifest.Dlss, DlssKind.RR => _manifest.Dlssd, DlssKind.FG => _manifest.Dlssg,
        DlssKind.NR => _manifest.Dlssnr, _ => _manifest.Streamline
    }) ?? [];
    public IReadOnlyList<string> Versions(DlssKind kind) => Entries(kind).Select(e => PeVersion.Format(e.Version)).ToList();
    public string? Latest(DlssKind kind) => Versions(kind).FirstOrDefault();
    public DlssManifestEntry? Find(DlssKind kind, string version) => Entries(kind).FirstOrDefault(e =>
        PeVersion.Format(e.Version) == version || e.Version == version || StripSuffix(e.Version) == StripSuffix(version));

    // "310.8.0 (50xx)" → "310.8.0": the parenthetical is display-only.
    public static string StripSuffix(string version)
    {
        var index = version.IndexOf('(');
        return index > 0 ? version[..index].TrimEnd() : version;
    }
    private static string Folder(string version) => Regex.Replace(version, "[^A-Za-z0-9._-]+", "_").Trim('_');

    // Returns the cached DLL (or, for Streamline, the folder of sl.*.dll), downloading on demand.
    public async Task<string> Fetch(DlssKind kind, string? version = null, IProgress<string>? progress = null)
    {
        var entry = (version == null ? Entries(kind).FirstOrDefault() : Find(kind, version))
            ?? throw new IOException($"{DlssFiles.Label(kind)} {version ?? "latest"} is not in the DLSS version list. Refresh and try again.");
        var directory = Path.Combine(CacheDirectory, kind.ToString(), Folder(entry.Version));
        var target = kind == DlssKind.Streamline ? directory : Path.Combine(directory, DlssFiles.DllName(kind));
        if (kind == DlssKind.Streamline ? File.Exists(Path.Combine(directory, DlssFiles.StreamlineCommon)) : File.Exists(target)) return target;
        progress?.Report($"Downloading {DlssFiles.Label(kind)} {PeVersion.Format(entry.Version)}…");
        var zip = await _downloads.Fetch(entry.Url, progress);
        var staging = directory + ".rhi-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(staging);
            var wanted = kind == DlssKind.Streamline ? DlssFiles.Streamline : [DlssFiles.DllName(kind)];
            using (var archive = ZipFile.OpenRead(zip))
                foreach (var file in archive.Entries)
                {
                    var name = EntryName(file);
                    if (!wanted.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                    var output = Path.Combine(staging, name.ToLowerInvariant());
                    Extract(file, output);
                    Downloads.ValidatePe(output, MachineType.x64);
                }
            if (!Directory.EnumerateFiles(staging).Any()) throw new IOException($"The {DlssFiles.Label(kind)} download did not contain {string.Join(", ", wanted.Take(2))}.");
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
            Directory.CreateDirectory(Path.GetDirectoryName(directory)!);
            Directory.Move(staging, directory);
            return target;
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }

    // ExtractToFile applies the zip's Unix permission bits, which Windows-made zips leave empty
    // (an unreadable file), so copy the stream into a normally created file instead.
    public static void Extract(ZipArchiveEntry entry, string output) => DllArchive.Extract(entry, output);

    public static string EntryName(ZipArchiveEntry entry) => DllArchive.EntryName(entry);
    public static string EntryPath(ZipArchiveEntry entry) => DllArchive.EntryPath(entry);
}

// Per-DLL version swaps for the DLSS overrides section (Default / versions / Custom).
public sealed class DlssSwap(DlssCatalog catalog)
{
    public async Task Apply(DlssKind kind, string target, string selection, IProgress<string>? progress = null)
    {
        if (selection.StartsWith("Default", StringComparison.OrdinalIgnoreCase)) { Restore(kind, target); return; }
        if (kind == DlssKind.Streamline) { await ApplyStreamline(target, selection, progress); return; }
        string source;
        if (selection == "Custom")
            source = DlssFiles.CustomFile(kind) ?? throw new IOException($"Put your own {DlssFiles.DllName(kind)} in {DlssFiles.CustomDirectory} first.");
        else source = await catalog.Fetch(kind, selection, progress);
        Downloads.ValidatePe(source, MachineType.x64);
        Sentinel.Deploy(source, target);
        var marker = target + DlssFiles.CustomMarker;
        if (selection == "Custom") File.WriteAllText(marker, ""); else if (File.Exists(marker)) File.Delete(marker);
    }

    private async Task ApplyStreamline(string folder, string selection, IProgress<string>? progress)
    {
        var source = selection == "Custom" ? DlssFiles.CustomStreamlineDirectory : await catalog.Fetch(DlssKind.Streamline, selection, progress);
        var replaced = 0;
        foreach (var dll in DlssFiles.Streamline)
        {
            var game = LinuxPaths.ResolveCase(folder, dll);
            var cached = Directory.Exists(source) ? LinuxPaths.ResolveCase(source, dll) : "";
            if (!File.Exists(game) || !File.Exists(cached)) continue;
            Downloads.ValidatePe(cached, MachineType.x64);
            Sentinel.Deploy(cached, game); replaced++;
        }
        if (replaced == 0) throw new IOException(selection == "Custom" ? $"Put your Streamline DLLs in {DlssFiles.CustomStreamlineDirectory} first." : "The Streamline download has none of this game's DLLs.");
        var marker = Path.Combine(folder, "sl" + DlssFiles.CustomMarker);
        if (selection == "Custom") File.WriteAllText(marker, ""); else if (File.Exists(marker)) File.Delete(marker);
    }

    public static void Restore(DlssKind kind, string target)
    {
        if (kind == DlssKind.Streamline)
        {
            foreach (var dll in DlssFiles.Streamline) Sentinel.Restore(LinuxPaths.ResolveCase(target, dll));
            var marker = Path.Combine(target, "sl" + DlssFiles.CustomMarker);
            if (File.Exists(marker)) File.Delete(marker);
            return;
        }
        Sentinel.Restore(target);
        if (File.Exists(target + DlssFiles.CustomMarker)) File.Delete(target + DlssFiles.CustomMarker);
    }

    public static void RestoreAll(DlssDetection detection)
    {
        foreach (var kind in DlssFiles.Dlls.Append(DlssKind.Streamline))
            if (detection.Path(kind) is { } path) Restore(kind, path);
    }
}
