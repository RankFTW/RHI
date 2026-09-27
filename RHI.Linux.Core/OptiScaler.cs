using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RHI.Linux.Core;

public static class OsVariant
{
    public const string Stable = "Stable", Nightly = "Nightly", DlssNr = "DlssNr";
    public static readonly string[] All = [Stable, Nightly, DlssNr];
    public static string Name(string variant) => variant == DlssNr ? "DLSS NR" : variant;
    public static string Of(GamePreferences prefs) => prefs.OsVariant is { } v && All.Contains(v) ? v : Stable;
    // Frame generation, DLSS presets and the NR settings are Nightly / DLSS NR options, as on Windows.
    public static bool Advanced(string variant) => variant != Stable;
    public static string ReleasesUrl(string variant) => variant == DlssNr
        ? "https://github.com/wilsjo2/OptiScaler-DLSSNR-PreSR-Multipass/releases" : "https://github.com/optiscaler/OptiScaler/wiki";
}

// What RHI deployed for OptiScaler, kept with the game so updates and removal know exactly that.
public sealed class OptiScalerRecord
{
    public string Variant { get; set; } = OsVariant.Stable;
    public string Version { get; set; } = "";
    public string DllName { get; set; } = OptiScaler.DefaultDll;
    public bool Ini { get; set; }
    public List<string> Dlls { get; set; } = [];
    public List<string> Folders { get; set; } = [];
    public DateTimeOffset Installed { get; set; } = DateTimeOffset.UtcNow;
}

// The Windows app's rhi_install.txt, read to remove an OptiScaler install made on a dual-boot library.
public sealed class WindowsOsManifest
{
    public string Component { get; set; } = "";
    public string Variant { get; set; } = OsVariant.Stable;
    public string Version { get; set; } = "";
    public string InstalledAs { get; set; } = "";
    public List<string> Files { get; set; } = [];
    public List<string> Folders { get; set; } = [];
    public Dictionary<string, List<string>> SharedFiles { get; set; } = [];
    public string? NrMethod { get; set; }
}

// OptiScaler as installed by Windows RHI: the Stable, Nightly and DLSS NR builds, OptiPatcher,
// RHI's per-GPU OptiScaler.ini templates and the latest DLSS DLLs beside the executable. Proton
// needs a native override for the DLL name OptiScaler is installed as.
public sealed class OptiScaler(HttpClient http, Downloads downloads, DlssCatalog dlss)
{
    public const string Component = "OptiScaler", IniName = "OptiScaler.ini", DefaultDll = "dxgi.dll", OptiPatcherPath = "plugins/OptiPatcher.asi";
    public const string Description = "OptiScaler replaces or adds upscalers (DLSS, FSR, XeSS) and frame generation in games that support any one of them. " +
        "It loads ReShade itself when both are installed, so RHI renames ReShade to ReShade64.dll if they would share a DLL name.";
    public static readonly string[] DllNames = ["dxgi.dll", "winmm.dll", "d3d11.dll", "d3d12.dll", "dbghelp.dll", "version.dll", "wininet.dll", "winhttp.dll"];
    public static readonly Dictionary<string, string> Hotkeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Insert"] = "0x2D", ["Delete"] = "0x2E", ["Home"] = "0x24", ["End"] = "0x23", ["Page Up"] = "0x21", ["Page Down"] = "0x22",
        ["F1"] = "0x70", ["F2"] = "0x71", ["F3"] = "0x72", ["F4"] = "0x73", ["F5"] = "0x74", ["F6"] = "0x75",
        ["F7"] = "0x76", ["F8"] = "0x77", ["F9"] = "0x78", ["F10"] = "0x79", ["F11"] = "0x7A", ["F12"] = "0x7B",
    };
    private static readonly Dictionary<string, string> ReleaseApis = new()
    {
        [OsVariant.Stable] = "https://api.github.com/repos/optiscaler/OptiScaler/releases/latest",
        [OsVariant.Nightly] = "https://api.github.com/repos/optiscaler/OptiScaler-nightly/releases?per_page=5",
        [OsVariant.DlssNr] = "https://api.github.com/repos/wilsjo2/OptiScaler-DLSSNR-PreSR-Multipass/releases?per_page=5",
    };
    private const string OptiPatcherUrl = "https://github.com/optiscaler/OptiPatcher/releases/download/rolling/OptiPatcher.asi";
    private static readonly string[] SkippedExtensions = [".bat", ".sh", ".ps1", ".txt", ".md", ".exe", ".pdb"];
    private static readonly string[] SkippedFolders = ["Licenses", "redist", "docs", "images", "tests"];

    private static string Root => Path.Combine(LinuxPaths.Cache, "optiscaler");
    private static string CacheFile => Path.Combine(Root, "latest.json");
    // User-editable copies of RHI's INI templates, seeded once like %LocalAppData%\RHI\inis on Windows.
    public static string InisDirectory => Path.Combine(LinuxPaths.Data, "inis");
    private Dictionary<string, NrRelease> _latest = Load();
    public string Status { get; private set; } = "OptiScaler versions have not been checked.";

    private static Dictionary<string, NrRelease> Load()
    {
        try { return File.Exists(CacheFile) ? JsonSerializer.Deserialize<Dictionary<string, NrRelease>>(File.ReadAllText(CacheFile), LinuxPaths.Json) ?? [] : []; }
        catch (Exception ex) when (ex is JsonException or IOException) { return []; }
    }

    public NrRelease? Latest(string variant) => _latest.GetValueOrDefault(variant);

    public static string DetectGpu() => Directory.Exists("/sys/module/nvidia") || File.Exists("/proc/driver/nvidia/version") ? "NVIDIA" : "AMD";

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

    public static string Label(string variant, string version) => variant switch
    {
        OsVariant.Nightly => "Nightly " + version, OsVariant.DlssNr => "DLSS NR " + version, _ => version
    };

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
        var start = new ProcessStartInfo("7z") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
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

    // ── Records and state ────────────────────────────────────────────────────
    private static string RecordFile(string dir) => Path.Combine(LinuxPaths.ResolveCase(dir, ".rhi-linux"), "optiscaler.json");
    public static OptiScalerRecord? LoadRecord(string dir)
    {
        try { var file = RecordFile(dir); return File.Exists(file) ? JsonSerializer.Deserialize<OptiScalerRecord>(File.ReadAllText(file), LinuxPaths.Json) : null; }
        catch (Exception ex) when (ex is JsonException or IOException) { return null; }
    }
    private static void SaveRecord(string dir, OptiScalerRecord? record)
    {
        var file = RecordFile(dir);
        if (record != null) LinuxPaths.WriteJson(file, record);
        else if (File.Exists(file)) File.Delete(file);
    }
    public static OptiScalerRecord? Record(Game game) => game.Executable == null ? null : LoadRecord(game.InstallDirectory);

    private static bool Managed(Game game)
    {
        try { return new Installation(game.InstallDirectory).ReadState().Components.ContainsKey(Component); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return false; }
    }

    public static WindowsOsManifest? WindowsManifest(Game game)
    {
        if (game.Executable == null) return null;
        try
        {
            var path = LinuxPaths.ResolveCase(game.InstallDirectory, "rhi_install.txt");
            var manifest = File.Exists(path) ? JsonSerializer.Deserialize<WindowsOsManifest>(File.ReadAllText(path), LinuxPaths.Json) : null;
            return manifest?.Component == Component && manifest.InstalledAs.Length > 0 ? manifest : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException) { return null; }
    }
    // Installed by Windows RHI on a shared library: its rhi_install.txt and the renamed DLL.
    public static bool FromWindows(Game game) => !Managed(game) && WindowsManifest(game) is { } m
        && File.Exists(LinuxPaths.ResolveCase(game.InstallDirectory, m.InstalledAs));
    public static bool Installed(Game game) => game.Executable != null && (Managed(game) || FromWindows(game));

    public static ComponentStatus StatusOf(Game game, InstallationStatus state)
    {
        var status = state.Get(Component);
        return status.Version == null && FromWindows(game) ? new(Component, "Windows RHI", true, false, false) : status;
    }

    public bool UpdateAvailable(Game game) => Record(game) is { } record && Latest(record.Variant) is { } latest && latest.Version != record.Version;

    public static string DllFor(GamePreferences prefs, GraphicsApiType api) =>
        prefs.OsDllName is { } name && DllNames.Contains(name, StringComparer.OrdinalIgnoreCase) ? name : api == GraphicsApiType.Vulkan ? "winmm.dll" : DefaultDll;

    // Proton loads Wine's builtin DLL unless the renamed OptiScaler DLL is marked native.
    public static IEnumerable<string> LaunchDlls(Game game)
    {
        if (game.Executable == null) return [];
        var name = Managed(game) ? Record(game)?.DllName : FromWindows(game) ? WindowsManifest(game)!.InstalledAs : null;
        return name == null ? [] : [Path.GetFileNameWithoutExtension(name)];
    }

    // ── OptiScaler.ini ───────────────────────────────────────────────────────
    public static string TemplateName(string gpu, bool dlssInputs, string variant)
    {
        var suffix = variant switch { OsVariant.Nightly => "_nightly", OsVariant.DlssNr => "_dlssnr", _ => "" };
        return $"OptiScaler{suffix}." + (gpu.Equals("NVIDIA", StringComparison.OrdinalIgnoreCase) ? "nvidia" : dlssInputs ? "amd-dlss" : "amd-nodlss") + ".ini";
    }

    // The user's copy in the inis folder, seeded from the bundled template the first time.
    public static string? Template(string gpu, bool dlssInputs, string variant)
    {
        var name = TemplateName(gpu, dlssInputs, variant);
        var user = Path.Combine(InisDirectory, name);
        var bundled = Path.Combine(AppContext.BaseDirectory, name);
        if (!File.Exists(user) && File.Exists(bundled)) { Directory.CreateDirectory(InisDirectory); File.Copy(bundled, user); }
        return File.Exists(user) ? user : null;
    }

    public static string IniPath(Game game) => LinuxPaths.ResolveCase(game.InstallDirectory, IniName);

    public static string? IniGet(string text, string section, string key)
    {
        var current = "";
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim().TrimStart('﻿');
            if (line.StartsWith('[') && line.EndsWith(']')) { current = line[1..^1].Trim(); continue; }
            if (line.StartsWith(';') || line.StartsWith('#') || !current.Equals(section, StringComparison.OrdinalIgnoreCase)) continue;
            var pair = line.Split('=', 2);
            if (pair.Length == 2 && pair[0].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) return pair[1].Trim();
        }
        return null;
    }

    // As Windows' SetOptiScalerIniValue: replace the key in its section, or add it at the section's end.
    public static string IniSet(string text, string section, string key, string value)
    {
        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        int? sectionEnd = null; var inSection = false;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim().TrimStart('﻿');
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                if (inSection) { sectionEnd = i; break; }
                inSection = line[1..^1].Trim().Equals(section, StringComparison.OrdinalIgnoreCase);
                if (inSection) sectionEnd = lines.Count;
                continue;
            }
            if (!inSection || line.StartsWith(';') || line.StartsWith('#')) continue;
            var pair = line.Split('=', 2);
            if (pair.Length == 2 && pair[0].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) { lines[i] = key + "=" + value; return string.Join(newline, lines); }
        }
        if (sectionEnd is { } end)
        {
            while (end > 0 && lines[end - 1].Trim().Length == 0) end--;
            lines.Insert(end, key + "=" + value);
        }
        else
        {
            if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
            lines.Add("[" + section + "]"); lines.Add(key + "=" + value); lines.Add("");
        }
        return string.Join(newline, lines);
    }

    // As Windows' EnforceLoadReshade/LoadAsiPlugins/WriteShortcutKey: the first key=, wherever it is.
    public static string IniForce(string text, string key, string value)
    {
        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        var found = false;
        for (var i = 0; i < lines.Count;)
        {
            if (!lines[i].TrimStart().StartsWith(key + "=", StringComparison.OrdinalIgnoreCase)) { i++; continue; }
            if (found) { lines.RemoveAt(i); continue; }
            lines[i] = key + "=" + value; found = true; i++;
        }
        if (!found)
        {
            if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
            lines.Add(key + "=" + value); lines.Add("");
        }
        return string.Join(newline, lines);
    }

    // Updates keep the user's changes: every value differing from the new release's default is re-applied to it.
    public static string MergeIni(string user, string staged)
    {
        var defaults = Sections(staged); var result = staged;
        foreach (var (section, keys) in Sections(user))
            foreach (var (key, value) in keys)
                if (!(defaults.TryGetValue(section, out var d) && d.TryGetValue(key, out var v) && v.Equals(value, StringComparison.OrdinalIgnoreCase)))
                    result = IniSet(result, section, key, value);
        return result;
    }

    private static Dictionary<string, Dictionary<string, string>> Sections(string text)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase); var current = "";
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim().TrimStart('﻿');
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']')) { current = line[1..^1].Trim(); continue; }
            var pair = line.Split('=', 2);
            if (pair.Length != 2 || pair[0].Trim().Length == 0) continue;
            if (!result.TryGetValue(current, out var keys)) result[current] = keys = new(StringComparer.OrdinalIgnoreCase);
            keys[pair[0].Trim()] = pair[1].Trim();
        }
        return result;
    }

    public static string? GetSetting(Game game, string section, string key)
    {
        var ini = IniPath(game);
        return File.Exists(ini) ? IniGet(File.ReadAllText(ini), section, key) : null;
    }
    public static void SetSetting(Game game, string section, string key, string value)
    {
        var ini = IniPath(game);
        if (!File.Exists(ini)) throw new IOException("Install OptiScaler first.");
        File.WriteAllText(ini, IniSet(File.ReadAllText(ini), section, key, value));
    }

    private static string Configure(string text, OptiScalerSettings settings, GamePreferences prefs, string variant)
    {
        text = IniForce(text, "LoadReshade", "true");
        text = IniForce(text, "LoadAsiPlugins", "true");
        text = IniForce(text, "ShortcutKey", Hotkeys.GetValueOrDefault(settings.Hotkey, settings.Hotkey));
        return OsVariant.Advanced(variant) ? FrameGen(text, prefs) : text;
    }

    // Re-applies the saved frame generation choices, as Windows does after installing or deploying the INI.
    public static string FrameGen(string text, GamePreferences prefs)
    {
        var input = prefs.OsFgInput ?? "auto"; var output = prefs.OsFgOutput ?? "auto";
        if (input == "auto" && output == "auto") return text;
        text = IniSet(text, "FrameGen", "FGInput", input);
        text = IniSet(text, "FrameGen", "FGOutput", output);
        return output == "dlssg" ? IniSet(text, "FrameGen", "FGNvngxReplacement", prefs.OsFgNvngx ?? "None") : text;
    }

    public static void ApplyHotkey(string iniPath, string hotkey)
    {
        if (File.Exists(iniPath)) File.WriteAllText(iniPath, IniForce(File.ReadAllText(iniPath), "ShortcutKey", Hotkeys.GetValueOrDefault(hotkey, hotkey)));
    }

    // "Deploy OptiScaler.ini": replaces the game's INI with the current template.
    public static void DeployIni(Game game, GamePreferences prefs, OptiScalerSettings settings)
    {
        GameSetup.RequireClosed(game);
        var record = Record(game) ?? throw new IOException("Install OptiScaler first.");
        var template = Template(settings.EffectiveGpu, settings.DlssInputs, record.Variant) ?? throw new IOException("The OptiScaler INI template is missing from this RHI build.");
        WriteIni(game.InstallDirectory, Configure(File.ReadAllText(template), settings, prefs, record.Variant));
        record.Ini = true; SaveRecord(game.InstallDirectory, record);
    }

    private static void WriteIni(string dir, string text)
    {
        var ini = LinuxPaths.ResolveCase(dir, IniName);
        var temp = Path.Combine(LinuxPaths.Cache, "optiscaler-" + Guid.NewGuid().ToString("N") + ".ini");
        try
        {
            Directory.CreateDirectory(LinuxPaths.Cache);
            File.WriteAllText(temp, text);
            Sentinel.Deploy(temp, ini);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

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
            // OptiScaler takes ReShade's proxy name and loads it as ReShade64.dll instead (LoadReshade=true).
            var target = LinuxPaths.ResolveCase(dir, dllName);
            if (installation.ReadState().Files.FirstOrDefault(f => f.Component == "ReShade" && LinuxPaths.ResolveCase(dir, f.Path) == target) is { } reshade)
                installation.Move("ReShade", reshade.Path, Installation.ReShadeBesideOptiScaler);
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

    // ReShade returns to its proxy name once OptiScaler no longer uses it.
    private static void RestoreReShade(Installation installation, string dir)
    {
        var state = installation.ReadState();
        if (state.Proxy == null || state.Files.All(f => f.Component != "ReShade" || !f.Path.Equals(Installation.ReShadeBesideOptiScaler, StringComparison.OrdinalIgnoreCase))) return;
        var proxy = LinuxPaths.ResolveCase(dir, state.Proxy);
        if (File.Exists(proxy) || state.Files.Any(f => LinuxPaths.ResolveCase(dir, f.Path) == proxy)) return;
        installation.Move("ReShade", Installation.ReShadeBesideOptiScaler, state.Proxy);
    }

    private static void DeployStreamline(string dir, string? source)
    {
        var folder = Path.Combine(LinuxPaths.ResolveCase(dir, "OptiScaler"), "Streamline");
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
        if (source == null) return;
        Directory.CreateDirectory(folder);
        foreach (var file in Directory.EnumerateFiles(source, "*.dll")) File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
    }

    // OptiScaler settings → Streamline: deploys (or removes) the selected Streamline in OptiScaler/Streamline.
    public async Task ApplyStreamline(Game game, GamePreferences prefs, IProgress<string>? progress = null)
    {
        GameSetup.RequireClosed(game);
        if (Record(game) == null) return;
        var source = prefs.OsDeployStreamline ? await dlss.Fetch(DlssKind.Streamline, prefs.OsStreamlineVersion, progress) : null;
        await Task.Run(() => DeployStreamline(game.InstallDirectory, source));
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

    // ── Engine.ini settings (Unreal Engine) ──────────────────────────────────
    public static readonly Dictionary<string, IniKey[]> EngineSettings = new()
    {
        ["DilateMotionVectors"] = [new("SystemSettings", "r.NGX.DLSS.DilateMotionVectors", "0"), new("SystemSettings", "r.Streamline.DilateMotionVectors", "0")],
        ["FSR2"] = [new("SystemSettings", "r.FidelityFX.FSR2.UseNativeDX12", "1")],
        ["FSR3"] = [new("SystemSettings", "r.FidelityFX.FSR3.UseNativeDX12", "1")],
        ["FSR3.1"] = [new("SystemSettings", "r.FidelityFX.FSR3.UseNativeDX12", "1"), new("SystemSettings", "r.FidelityFX.FSR3.UseRHI", "0")],
        ["FsrFgSwapchain"] = [new("SystemSettings", "r.FidelityFX.FI.OverrideSwapChainDX12", "1")],
        ["UpscalerPlugin"] = [new("SystemSettings", "r.AntiAliasingMethod", "4"), new("SystemSettings", "r.TemporalAA.Upscaler", "1")],
    };
    private static readonly string[] EngineGroups = ["DilateMotionVectors", "FsrCrashFix", "FsrFgSwapchain", "UpscalerPlugin"];
    private static string Owner(string group) => "OptiScaler:" + group;

    public static string? EngineIni(Game game) => IniSettings.FindEngineInis(game) is [var only] ? only : null;
    public static bool EngineApplied(Game game, string group) => EngineIni(game) is { } ini && IniSettings.HasRecord(ini, Owner(group));

    // Each setting is its own owner, so turning one back to Default restores only its keys.
    public static void SetEngine(Game game, string group, IniKey[]? keys)
    {
        GameSetup.RequireClosed(game);
        var ini = EngineIni(game) ?? throw new IOException("Launch the game once to create its Engine.ini. If there are several, choose the right one in Advanced settings.");
        IniSettings.Restore(ini, Owner(group));
        var readOnly = OperatingSystem.IsLinux() && File.Exists(ini) && !File.GetUnixFileMode(ini).HasFlag(UnixFileMode.UserWrite);
        if (keys != null) IniSettings.Apply(ini, keys, readOnly, Owner(group));
    }

    public static void RestoreEngineIni(Game game)
    {
        foreach (var ini in IniSettings.FindEngineInis(game))
            foreach (var group in EngineGroups) IniSettings.Restore(ini, Owner(group));
    }

    public static string Number(float value) => value.ToString("0.000000", CultureInfo.InvariantCulture);
}
