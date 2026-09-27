using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RHI.Linux.Core;

public static class NrMethod
{
    public const string ShortFuse = "ShortFuse", Dlss5Tool = "DLSS5Tool", Bridge = "DLSS5ToolBridge", Feeder = "Feeder";
    public static readonly string[] All = [ShortFuse, Dlss5Tool, Bridge, Feeder];
    public static string Name(string method) => method switch
    {
        ShortFuse => "ShortFuse DLSS Tool", Dlss5Tool => "DLSS5 Tool", Bridge => "DLSS5 Tool + DX11 Bridge", Feeder => "DLSS5 Feeder", _ => method
    };
    public static string Short(string method) => method switch
    {
        ShortFuse => "DLSS Tool (SF)", Dlss5Tool => "DLSS5 Tool", Bridge => "DLSS5 Tool + Bridge", Feeder => "Feeder", _ => method
    };
    // Same rules as the Windows method picker: every method is listed, inapplicable ones are disabled.
    public static bool Available(string method, bool is32Bit, GraphicsApiType api, bool hasDlss) => method switch
    {
        ShortFuse => !is32Bit && api != GraphicsApiType.OpenGL,
        Dlss5Tool => hasDlss && !is32Bit,
        Bridge => hasDlss && api is GraphicsApiType.DirectX11 or GraphicsApiType.Vulkan && !is32Bit,
        _ => true
    };
    public static string Recommended(bool is32Bit, GraphicsApiType api, bool hasDlss) =>
        is32Bit || api == GraphicsApiType.OpenGL || !hasDlss ? Feeder : api is GraphicsApiType.DirectX11 or GraphicsApiType.Vulkan ? Bridge : ShortFuse;
    public static string Description(string method, bool hasDlss, bool is32Bit) => method switch
    {
        Dlss5Tool => hasDlss
            ? "For DX12 games with native DLSS. Deploys the DLSS5 Tool ReShade addon and nvngx_dlssnr.dll. Lighter alternative to ShortFuse DLSS Tool when you don't need the full Streamline stack."
            : "For DX12 games with native DLSS. This game has no detected DLSS — consider ShortFuse DLSS Tool instead.",
        Bridge => "For DX11 and Vulkan games with native DLSS. The bridge mirrors the game's DLSS onto a private DX12 session so the NR addon can hook it.",
        ShortFuse => "Recommended for most games with native DLSS. Deploys the full DLSS SR/RR/FG/NR stack and Streamline alongside the ReShade addon. Supports DX12, DX11, DX9, and Vulkan.",
        _ => is32Bit
            ? "For 32-bit games. Feeds a synthetic DLSS contract from ReShade depth and motion vectors. Deploys the Feeder addon, DLSS5 Tool (neural consumer), NR DLL, DLSS SR DLL, and the required shaders (DLSS5_Feed.fx + LumeniteFX)."
            : "For games with no native DLSS (DX11, DX12, Vulkan, OpenGL). Feeds a synthetic DLSS contract from ReShade depth and motion vectors. Deploys the Feeder addon, DLSS5 Tool (neural consumer), NR DLL, DLSS SR DLL, and required shaders."
    };
    public static (string Label, string Url) Link(string method) => method switch
    {
        Dlss5Tool => ("DLSS5 Tool info →", "https://discord.com/channels/1408098019194310818/1543802634991968366"),
        Bridge => ("DX11 Bridge info →", "https://github.com/NIGos/dlss5-bridge"),
        ShortFuse => ("ShortFuse DLSS Tool info →", "https://discord.com/channels/1408098019194310818/1543975158937821315"),
        _ => ("Feeder setup guide →", "https://github.com/jlrouzies-fr/DLSS5-Feeder")
    };
}

public static class NrFiles
{
    public const string Dlss5Addon = "renodx-dlss5.addon64", SfAddon = "renodx-dlss.addon64", Bridge = "dlss5-bridge.addon64";
    public const string Feeder64 = "dlss5-feed.addon64", Feeder32 = "dlss5-feed.addon32", HostExe = "dlss5-feed-host64.exe";
    public const string FeedFx = "DLSS5_Feed.fx", LumeniteFx = "lumenite_Kernel.fx";
    public const string CostProxy = "nvngx_dlssnr.dll", CostReal = "nvngx_dlssnr_real.dll", CostIni = "nvngx_dlssnr.ini", CostAddon = "dlssnr-companion.addon64";
    public const string Host64 = "host64", DgVoodooDll = "D3D9.dll", DgVoodooConf = "dgVoodoo.conf";
    public const string ShadersDir = "reshade-shaders/Shaders", TexturesDir = "reshade-shaders/Textures";
}

public sealed record NrRelease(string Version, string Url);

// Release lists for the NR addons. The DLSS5 Tool and ShortFuse builds come from RHI's
// release repo (as on Windows); the Feeder, Bridge and Cost Scaler from their authors' repos.
public sealed class AddonReleases
{
    public const string Dlss5Tool = "dlss5tool", ShortFuse = "dlsstool", Feeder = "feeder", Bridge = "bridge", CostScaler = "costscaler";
    private const string RhiRepo = "https://api.github.com/repos/RankFTW/rhi-repo/releases?per_page=100";
    private static readonly Dictionary<string, string> AuthorRepos = new()
    {
        [Feeder] = "https://api.github.com/repos/jlrouzies-fr/DLSS5-Feeder/releases?per_page=100",
        [Bridge] = "https://api.github.com/repos/NIGos/dlss5-bridge/releases?per_page=100",
        [CostScaler] = "https://api.github.com/repos/xenmods/DLSSNR-Cost-Scaler/releases?per_page=30",
    };
    public const string LumeniteUrl = "https://github.com/umar-afzaal/LumeniteFX/archive/refs/heads/mainline.zip";
    private static readonly Dictionary<string, string[]> Contents = new()
    {
        [Dlss5Tool] = [NrFiles.Dlss5Addon], [ShortFuse] = [NrFiles.SfAddon], [Bridge] = [NrFiles.Bridge],
        [Feeder] = [NrFiles.Feeder64, NrFiles.Feeder32, NrFiles.FeedFx, NrFiles.HostExe],
        [CostScaler] = [NrFiles.CostProxy, NrFiles.CostIni, NrFiles.CostAddon],
    };

    private readonly HttpClient _http;
    private readonly Downloads _downloads;
    private Dictionary<string, List<NrRelease>> _lists = [];
    public string Status { get; private set; } = "Neural Rendering versions have not been checked.";
    private static string Root => Path.Combine(LinuxPaths.Cache, "neural-rendering");
    private static string CacheFile => Path.Combine(Root, "available_versions.json");

    public AddonReleases(HttpClient http, Downloads downloads)
    {
        _http = http; _downloads = downloads;
        try { if (File.Exists(CacheFile)) { _lists = JsonSerializer.Deserialize<Dictionary<string, List<NrRelease>>>(File.ReadAllText(CacheFile), LinuxPaths.Json) ?? []; Status = "Using cached Neural Rendering versions."; } }
        catch (Exception ex) when (ex is JsonException or IOException) { _lists = []; }
    }

    public IReadOnlyList<string> Versions(string type) => _lists.GetValueOrDefault(type)?.Select(r => r.Version).ToList() ?? [];
    public string? Latest(string type) => Versions(type).FirstOrDefault();

    public async Task Refresh(bool force = false)
    {
        if (!force && File.Exists(CacheFile) && DateTime.UtcNow - File.GetLastWriteTimeUtc(CacheFile) < TimeSpan.FromHours(1) && _lists.Count >= 4) return;
        var lists = new Dictionary<string, List<NrRelease>>(_lists);
        var failures = new List<string>();
        try
        {
            var releases = await Releases(RhiRepo);
            lists[Dlss5Tool] = Sort(Parse(releases, "renodx-dlss5-", NrFiles.Dlss5Addon));
            lists[ShortFuse] = Sort(Parse(releases, "renodx-dlss-SF-", NrFiles.SfAddon));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or TaskCanceledException) { failures.Add("RHI releases: " + ex.Message); }
        foreach (var (type, url) in AuthorRepos)
        {
            try { lists[type] = Sort(Parse(await Releases(url), "", Contents[type][0])); }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or TaskCanceledException) { failures.Add(type + ": " + ex.Message); }
        }
        _lists = lists;
        LinuxPaths.WriteJson(CacheFile, _lists);
        Status = failures.Count == 0 ? $"Neural Rendering versions checked {DateTime.Now:t}." : "Some release lists could not be checked: " + string.Join("; ", failures);
        if (failures.Count > 0 && _lists.Count == 0) throw new IOException(Status);
    }

    private async Task<JsonElement> Releases(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode) throw new IOException($"GitHub returned {(int)response.StatusCode} for {new Uri(url).AbsolutePath}.");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    public static List<NrRelease> Parse(JsonElement releases, string prefix, string fileName)
    {
        var result = new List<NrRelease>();
        if (releases.ValueKind != JsonValueKind.Array) return result;
        foreach (var release in releases.EnumerateArray())
        {
            var tag = release.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            if (string.IsNullOrEmpty(tag) || !tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (release.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) continue;
            string? exact = null, zip = null, loose = null;
            foreach (var asset in release.TryGetProperty("assets", out var a) ? a.EnumerateArray() : default)
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                if (url == null) continue;
                if (name.Equals(fileName, StringComparison.OrdinalIgnoreCase)) exact ??= url;
                else if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) zip ??= url;
                else if (name.EndsWith(Path.GetExtension(fileName), StringComparison.OrdinalIgnoreCase)) loose ??= url;
            }
            if ((exact ?? zip ?? loose) is { } download) result.Add(new(tag[prefix.Length..], download));
        }
        return result;
    }

    // Newest first: numeric core descending, a final release above its pre-releases, rc10 above rc9.
    public static List<NrRelease> Sort(IEnumerable<NrRelease> releases) => releases.OrderByDescending(r => r.Version, VersionComparer.Instance).ToList();

    public sealed class VersionComparer : IComparer<string>
    {
        public static readonly VersionComparer Instance = new();
        public int Compare(string? x, string? y)
        {
            var (xCore, xPre) = Split(x ?? ""); var (yCore, yPre) = Split(y ?? "");
            for (var i = 0; i < Math.Max(xCore.Length, yCore.Length); i++)
            {
                var c = (i < xCore.Length ? xCore[i] : 0).CompareTo(i < yCore.Length ? yCore[i] : 0);
                if (c != 0) return c;
            }
            if (xPre.Length == 0 || yPre.Length == 0) return (xPre.Length == 0).CompareTo(yPre.Length == 0);
            var xs = Regex.Split(xPre, "(\\d+)"); var ys = Regex.Split(yPre, "(\\d+)");
            for (var i = 0; i < Math.Min(xs.Length, ys.Length); i++)
            {
                var c = long.TryParse(xs[i], out var xn) && long.TryParse(ys[i], out var yn) ? xn.CompareTo(yn) : string.Compare(xs[i], ys[i], StringComparison.OrdinalIgnoreCase);
                if (c != 0) return c;
            }
            return xs.Length.CompareTo(ys.Length);
        }
        private static (long[] Core, string Pre) Split(string version)
        {
            version = version.TrimStart('v', 'V');
            var match = Regex.Match(version, "^([0-9]+(?:\\.[0-9]+)*)(.*)$");
            if (!match.Success) return ([], version);
            return (match.Groups[1].Value.Split('.').Select(p => long.TryParse(p, out var n) ? n : 0).ToArray(), match.Groups[2].Value.TrimStart('-', '_', '.'));
        }
    }

    // Downloads and extracts one release into a versioned cache folder; null = newest.
    public async Task<(string Version, string Directory)> Stage(string type, string? version, IProgress<string>? progress = null)
    {
        if (Versions(type).Count == 0 || version != null && !Versions(type).Contains(version)) await Refresh(true);
        var release = (version == null ? _lists.GetValueOrDefault(type)?.FirstOrDefault() : _lists.GetValueOrDefault(type)?.FirstOrDefault(r => r.Version == version))
            ?? throw new IOException($"Could not find {(version == null ? "a release" : "version " + version)} of {Title(type)}. Check your connection and try again.");
        var directory = Path.Combine(Root, type, Regex.Replace(release.Version, "[^A-Za-z0-9._-]+", "_"));
        if (File.Exists(Path.Combine(directory, ".complete"))) return (release.Version, directory);
        progress?.Report($"Downloading {Title(type)} {release.Version}…");
        var download = await _downloads.Fetch(release.Url, progress);
        var staging = directory + ".rhi-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(staging);
            var wanted = Contents[type];
            if (release.Url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var archive = ZipFile.OpenRead(download);
                foreach (var name in wanted)
                {
                    // Prefer the exact name; the DLSS5 Tool zips occasionally rename their single addon.
                    var entry = archive.Entries.FirstOrDefault(e => DlssCatalog.EntryName(e).Equals(name, StringComparison.OrdinalIgnoreCase))
                        ?? (wanted.Length == 1 ? archive.Entries.FirstOrDefault(e => DlssCatalog.EntryName(e).EndsWith(Path.GetExtension(name), StringComparison.OrdinalIgnoreCase)) : null);
                    if (entry != null) DlssCatalog.Extract(entry, Path.Combine(staging, name));
                }
            }
            else File.Copy(download, Path.Combine(staging, wanted[0]));
            if (!File.Exists(Path.Combine(staging, wanted[0]))) throw new IOException($"{Title(type)} {release.Version} does not contain {wanted[0]}.");
            foreach (var file in Directory.EnumerateFiles(staging))
            {
                var extension = Path.GetExtension(file).ToLowerInvariant();
                if (extension is ".addon64" or ".dll" or ".exe") Downloads.ValidatePe(file, MachineType.x64);
                else if (extension == ".addon32") Downloads.ValidatePe(file, MachineType.I386);
            }
            File.WriteAllText(Path.Combine(staging, ".complete"), release.Url);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
            Directory.Move(staging, directory);
            return (release.Version, directory);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
    }

    // LumeniteFX: only lumenite_Kernel.fx and what it includes are deployed for the Feeder.
    public async Task<List<Payload>> Lumenite(IProgress<string>? progress = null)
    {
        var marker = Path.Combine(Root, "lumenite.refreshed");
        var refresh = !File.Exists(marker) || DateTime.UtcNow - File.GetLastWriteTimeUtc(marker) > TimeSpan.FromDays(1);
        var zip = await _downloads.Fetch(LumeniteUrl, progress, refresh);
        Directory.CreateDirectory(Root); File.WriteAllText(marker, "");
        var result = new List<Payload>();
        using var archive = ZipFile.OpenRead(zip);
        foreach (var entry in archive.Entries)
        {
            var parts = DlssCatalog.EntryPath(entry).Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (entry.Name.Length == 0 || parts.Any(p => p is "." or "..")) continue;
            var index = Array.FindIndex(parts, p => p is "Shaders" or "Textures");
            if (index < 0) continue;
            var rest = string.Join('/', parts.Skip(index + 1));
            var keep = parts[index] == "Shaders"
                ? rest.Equals(NrFiles.LumeniteFx, StringComparison.OrdinalIgnoreCase) || rest.StartsWith("include/", StringComparison.OrdinalIgnoreCase) && rest.EndsWith(".fxh", StringComparison.OrdinalIgnoreCase)
                : Path.GetFileName(rest).StartsWith("lumenite_", StringComparison.OrdinalIgnoreCase);
            if (!keep) continue;
            using var memory = new MemoryStream();
            using (var stream = entry.Open()) await stream.CopyToAsync(memory);
            result.Add(new((parts[index] == "Shaders" ? NrFiles.ShadersDir : NrFiles.TexturesDir) + "/" + rest, memory.ToArray()));
        }
        if (!result.Any(p => p.RelativePath.EndsWith(NrFiles.LumeniteFx, StringComparison.OrdinalIgnoreCase))) throw new IOException("The LumeniteFX download does not contain lumenite_Kernel.fx.");
        return result;
    }

    public static string Title(string type) => type switch
    {
        Dlss5Tool => "DLSS5 Tool", ShortFuse => "ShortFuse DLSS Tool", Feeder => "DLSS5 Feeder", Bridge => "DLSS5 DX11 Bridge", CostScaler => "DLSS NR Cost Scaler", _ => type
    };
}

// What RHI deployed for Neural Rendering, kept with the game so removal restores exactly that.
public sealed class NrRecord
{
    public string Method { get; set; } = "";
    public string? AddonVersion { get; set; }
    public string? PackVersion { get; set; }
    public string? NrVersion { get; set; }
    public bool CostScaler { get; set; }
    public bool DgVoodoo { get; set; }
    public bool Host64 { get; set; }
    public List<string> Dlls { get; set; } = [];
    public DateTimeOffset Installed { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class NrState
{
    public NrRecord? Record { get; init; }
    public bool Dlss5Tool { get; init; }
    public bool ShortFuse { get; init; }
    public bool Bridge { get; init; }
    public bool Feeder { get; init; }
    public bool HostExe { get; init; }
    public bool FeedFx { get; init; }
    public bool Lumenite { get; init; }
    public bool DgVoodoo { get; init; }
    public bool CostScaler { get; init; }
    public bool NrOwned { get; init; }
    public bool Is32Bit { get; init; }
    // The Feeder uses the SR DLL beside the executable, not a copy deeper in the game.
    public string? RootSrVersion { get; init; }
    public bool RootSr { get; init; }
    public DlssDetection Detection { get; init; } = new();
    public string? Root { get; init; }
    // Inferred from files for installs made by the Windows app on a shared library.
    public string? Method => Record?.Method ?? (ShortFuse ? NrMethod.ShortFuse : Dlss5Tool && Bridge ? NrMethod.Bridge : Feeder ? NrMethod.Feeder : Dlss5Tool ? NrMethod.Dlss5Tool : null);
    public bool AnyInstalled => Method != null;
    public bool Installed(string method) => method switch
    {
        NrMethod.Dlss5Tool => Dlss5Tool || NrOwned && Method == NrMethod.Dlss5Tool,
        NrMethod.Bridge => Dlss5Tool || Bridge,
        NrMethod.ShortFuse => ShortFuse,
        _ => Feeder
    };

    public IEnumerable<(string Text, bool Ok)> Tags(string method, bool reShade)
    {
        (string, bool) Tag(string label, bool ok, string? version = null) => (ok ? $"✓ {label}{(version == null ? "" : " " + version)}" : "✗ " + label, ok);
        (string, bool) Dll(DlssKind kind, string label) => Tag(label, Detection.Has(kind), Detection.Version(kind));
        yield return Tag("ReShade", reShade);
        switch (method)
        {
            case NrMethod.Dlss5Tool or NrMethod.Bridge:
                yield return Tag("DLSS5 Tool", Dlss5Tool);
                if (method == NrMethod.Bridge) yield return Tag("DX11 Bridge", Bridge);
                if (Detection.Has(DlssKind.SR) || Detection.Has(DlssKind.RR)) { yield return Dll(DlssKind.SR, "DLSS SR"); yield return Dll(DlssKind.RR, "DLSS RR"); yield return Dll(DlssKind.FG, "DLSS FG"); }
                yield return Dll(DlssKind.NR, "NR DLL");
                break;
            case NrMethod.ShortFuse:
                yield return Tag("ShortFuse DLSS Tool", ShortFuse);
                yield return Dll(DlssKind.SR, "DLSS SR"); yield return Dll(DlssKind.RR, "DLSS RR"); yield return Dll(DlssKind.FG, "DLSS FG"); yield return Dll(DlssKind.NR, "NR DLL");
                yield return Tag("Streamline", Detection.Has(DlssKind.Streamline), Detection.Version(DlssKind.Streamline));
                break;
            default:
                yield return Tag("Feeder Addon", Feeder);
                yield return Tag(Is32Bit ? "DLSS5 Tool (host64)" : "DLSS5 Tool", Dlss5Tool);
                if (Is32Bit) yield return Tag("host64.exe", HostExe);
                yield return Tag("DLSS SR", RootSr, RootSrVersion); yield return Dll(DlssKind.NR, "NR DLL");
                yield return Tag("Feed.fx", FeedFx); yield return Tag("LumeniteFX", Lumenite);
                if (DgVoodoo || Record?.DgVoodoo == true) yield return Tag("dgVoodoo2", DgVoodoo);
                break;
        }
    }
}

public sealed class NeuralRenderingSetup(Downloads downloads, DlssCatalog dlss, AddonReleases releases, Catalog catalog)
{
    public const string Component = "Neural Rendering", IniOwner = "neural-rendering";
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

    public AddonReleases Releases => releases;
    public DlssCatalog Dlss => dlss;

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

    public static bool IsDx9(Game game, GraphicsApiType api) => api == GraphicsApiType.DirectX9;

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

    private async Task<List<Payload>> DgVoodoo(bool is32, IProgress<string>? progress)
    {
        var versions = catalog.ManifestRoot("dgVoodooVersions");
        var url = versions is { ValueKind: JsonValueKind.Object } list ? list.EnumerateObject().Select(p => p.Value.GetString()).FirstOrDefault(u => u != null) : null;
        url ??= "https://github.com/dege-diosg/dgVoodoo2/releases/download/v2.87.3/dgVoodoo2_87_3.zip";
        progress?.Report("Downloading dgVoodoo2…");
        var zip = await downloads.Fetch(url, progress);
        using var archive = ZipFile.OpenRead(zip);
        var name = is32 ? "MS/x86/D3D9.dll" : "MS/x64/D3D9.dll";
        var entry = archive.Entries.FirstOrDefault(e => DlssCatalog.EntryPath(e).Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new IOException("The dgVoodoo2 download does not contain " + name + ".");
        using var memory = new MemoryStream();
        using (var stream = entry.Open()) await stream.CopyToAsync(memory);
        // Same configuration as Windows RHI: translate D3D9 to D3D11 so the Feeder can hook it.
        const string conf = "; dgVoodoo2 configuration — managed by RHI\n\n[General]\nOutputAPI = d3d11_fl11_0\n\n[DirectX]\nDisableAndPassThru = false\nVideoCard = geforce_9800_gt\nVRAM = 1024\ndgVoodooWatermark = false\n";
        return [new(NrFiles.DgVoodooDll, memory.ToArray()), new(NrFiles.DgVoodooConf, System.Text.Encoding.UTF8.GetBytes(conf))];
    }

    private static void InstallCostScaler(string dir, string staged)
    {
        var proxy = At(dir, NrFiles.CostProxy); var real = At(dir, NrFiles.CostReal);
        if (File.Exists(proxy) && !File.Exists(real)) File.Move(proxy, real);
        Sentinel.Copy(Path.Combine(staged, NrFiles.CostProxy), proxy);
    }

    private static void UninstallCostScaler(string dir, bool recorded)
    {
        var proxy = At(dir, NrFiles.CostProxy); var real = At(dir, NrFiles.CostReal);
        if (File.Exists(real)) File.Move(real, proxy, true);
        else if (recorded && File.Exists(proxy) && !Sentinel.Placed(proxy)) File.Delete(proxy);
    }

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
