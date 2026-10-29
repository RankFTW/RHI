using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RHI.Linux.Core;

public sealed partial class OptiScaler
{
    // ── Staging and update ───────────────────────────────────────────────────
    private static readonly Dictionary<string, string> ReleaseApis = new()
    {
        [OsVariant.Stable] = Sources.OptiScalerStable,
        [OsVariant.Nightly] = Sources.OptiScalerNightly,
        [OsVariant.DlssNr] = Sources.OptiScalerDlssNr,
    };
    private const string OptiPatcherUrl = Sources.OptiPatcher;
    private static readonly string[] SkippedExtensions = [".bat", ".sh", ".ps1", ".txt", ".md", ".exe", ".pdb"];
    private static readonly string[] SkippedFolders = ["Licenses", "redist", "docs", "images", "tests"];

    private static string CacheFile => Path.Combine(Root, "latest.json");
    private Dictionary<string, NrRelease> _latest = Load();
    public string Status { get; private set; } = "OptiScaler versions have not been checked.";

    private static Dictionary<string, NrRelease> Load()
    {
        try { return File.Exists(CacheFile) ? JsonSerializer.Deserialize<Dictionary<string, NrRelease>>(File.ReadAllText(CacheFile), LinuxPaths.Json) ?? [] : []; }
        catch (Exception ex) when (ex is JsonException or IOException) { return []; }
    }

    public NrRelease? Latest(string variant) => _latest.GetValueOrDefault(variant);

    public bool UpdateAvailable(Game game) => Record(game) is { } record && Latest(record.Variant) is { } latest && latest.Version != record.Version;

    public async Task Refresh(bool force = false)
    {
        if (!force && File.Exists(CacheFile) && DateTime.UtcNow - File.GetLastWriteTimeUtc(CacheFile) < TimeSpan.FromHours(1) && _latest.Count == ReleaseApis.Count) return;
        var latest = new Dictionary<string, NrRelease>(_latest);
        var failures = new List<string>();
        foreach (var (variant, url) in ReleaseApis)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                using var response = await http.SendAsync(request);
                if (!response.IsSuccessStatusCode) throw new IOException($"GitHub returned {(int)response.StatusCode}.");
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                if (Parse(document.RootElement, variant) is { } release) latest[variant] = release;
                else failures.Add(OsVariant.Name(variant) + ": no release with a download was found");
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or TaskCanceledException) { failures.Add(OsVariant.Name(variant) + ": " + ex.Message); }
        }
        _latest = latest;
        LinuxPaths.WriteJson(CacheFile, _latest);
        Status = failures.Count == 0 ? $"OptiScaler versions checked {DateTime.Now:t}." : "Some OptiScaler releases could not be checked: " + string.Join("; ", failures);
        if (_latest.Count == 0) throw new IOException(Status);
    }

    // Stable and Nightly ship a .7z; the DLSS NR fork ships zips, of which the RTX 40 MFG build is opt-in.
    public static NrRelease? Parse(JsonElement root, string variant)
    {
        var releases = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().ToList() : root.ValueKind == JsonValueKind.Object ? [root] : [];
        foreach (var release in releases)
        {
            if (release.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) continue;
            var tag = release.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            if (string.IsNullOrEmpty(tag)) continue;
            var assets = (release.TryGetProperty("assets", out var a) ? a.EnumerateArray() : default)
                .Select(x => (Name: x.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "", Url: x.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null))
                .Where(x => x.Url != null).ToList();
            var url = variant == OsVariant.DlssNr
                ? (assets.FirstOrDefault(x => x.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && !x.Name.Contains("rtx40", StringComparison.OrdinalIgnoreCase)).Url
                    ?? assets.FirstOrDefault(x => x.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).Url)
                : assets.FirstOrDefault(x => x.Name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase)).Url;
            if (url == null) continue;
            var version = variant switch { OsVariant.Nightly => tag.Replace("nightly-", ""), OsVariant.DlssNr => tag.TrimStart('v'), _ => tag };
            return new(version, url);
        }
        return null;
    }

    // Downloads and extracts one release into a versioned cache folder (the folder holding OptiScaler.dll).
    public async Task<(string Version, string Directory)> Stage(string variant, IProgress<string>? progress = null)
    {
        if (Latest(variant) == null) await Refresh(true);
        var release = Latest(variant) ?? throw new IOException($"Could not find an OptiScaler {OsVariant.Name(variant)} release. Check your connection and try again.");
        var directory = Path.Combine(Root, variant, Regex.Replace(release.Version, "[^A-Za-z0-9._-]+", "_"));
        if (File.Exists(Path.Combine(directory, ".complete"))) return (release.Version, directory);
        progress?.Report($"Downloading OptiScaler {Label(variant, release.Version)}…");
        var archive = await downloads.Fetch(release.Url, progress);
        var staging = directory + ".rhi-" + Guid.NewGuid().ToString("N");
        var extract = staging + "-extract";
        try
        {
            progress?.Report("Extracting OptiScaler…");
            Directory.CreateDirectory(extract);
            if (release.Url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) ExtractZip(archive, extract);
            else await Extract7z(archive, extract);
            var dll = Directory.EnumerateFiles(extract, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint })
                .Where(f => Path.GetFileName(f).Equals("OptiScaler.dll", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.Count(c => c == '/')).FirstOrDefault() ?? throw new IOException($"OptiScaler {release.Version} does not contain OptiScaler.dll.");
            Downloads.ValidatePe(dll, MachineType.x64);
            CopyTree(Path.GetDirectoryName(dll)!, staging);
            File.WriteAllText(Path.Combine(staging, ".complete"), release.Url);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
            Directory.CreateDirectory(Path.GetDirectoryName(directory)!);
            Directory.Move(staging, directory);
            return (release.Version, directory);
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            if (Directory.Exists(extract)) Directory.Delete(extract, true);
        }
    }

    private static async Task Extract7z(string archive, string output)
    {
        var start = new ProcessStartInfo(ArchiveTools.SevenZip) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in new[] { "x", "-y", "-snl-", "-o" + output, archive }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("Could not start 7z. Install 7zip to extract OptiScaler.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(); await stdout;
        if (process.ExitCode != 0) throw new IOException("Could not extract OptiScaler: " + (await error).Trim());
    }

    private static void ExtractZip(string archive, string output)
    {
        using var zip = ZipFile.OpenRead(archive);
        foreach (var entry in zip.Entries)
        {
            var parts = DlssCatalog.EntryPath(entry).Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (entry.Name.Length == 0 || parts.Length == 0) continue;
            if (parts.Any(p => p is "." or ".." || p.Contains(':'))) throw new IOException("Unsafe path in the OptiScaler archive.");
            var path = Path.Combine([output, .. parts]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            DlssCatalog.Extract(entry, path);
        }
    }

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    // Same filter as Windows: the renamed OptiScaler.dll, its companions and backend folders; no
    // scripts, documentation or licences. OptiScaler.ini is deployed separately.
    public static List<Payload> Payloads(string staged, string dllName)
    {
        bool Skip(string name) => name is ".complete" || SkippedExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase)
            || name.Equals("LICENSE", StringComparison.OrdinalIgnoreCase) || name.StartsWith("!!", StringComparison.Ordinal);
        var result = new List<Payload>();
        foreach (var file in Directory.EnumerateFiles(staged))
        {
            var name = Path.GetFileName(file);
            if (Skip(name) || name.Equals(IniName, StringComparison.OrdinalIgnoreCase)) continue;
            result.Add(new(name.Equals("OptiScaler.dll", StringComparison.OrdinalIgnoreCase) ? dllName : name, File.ReadAllBytes(file)));
        }
        foreach (var folder in Directory.EnumerateDirectories(staged))
        {
            if (SkippedFolders.Contains(Path.GetFileName(folder), StringComparer.OrdinalIgnoreCase)) continue;
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                if (!Skip(Path.GetFileName(file))) result.Add(new(Path.GetRelativePath(staged, file).Replace('\\', '/'), File.ReadAllBytes(file)));
        }
        return result;
    }

    public async Task<string> OptiPatcher(IProgress<string>? progress = null)
    {
        var marker = Path.Combine(Root, "optipatcher.refreshed");
        var refresh = !File.Exists(marker) || DateTime.UtcNow - File.GetLastWriteTimeUtc(marker) > TimeSpan.FromDays(1);
        var path = await downloads.Fetch(OptiPatcherUrl, progress, refresh);
        Downloads.ValidatePe(path, MachineType.x64);
        Directory.CreateDirectory(Root); File.WriteAllText(marker, "");
        return path;
    }
}
