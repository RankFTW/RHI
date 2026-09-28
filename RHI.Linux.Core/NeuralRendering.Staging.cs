using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using RenoDXCommander.Services;

namespace RHI.Linux.Core;

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

public sealed partial class NeuralRenderingSetup
{
    public AddonReleases Releases => releases;
    public DlssCatalog Dlss => dlss;
}
