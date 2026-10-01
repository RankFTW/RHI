using System.Globalization;
using System.Text.Json;
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
// Split like upstream's OptiScalerService: this file holds state, the INI and Engine.ini; see
// OptiScaler.Staging.cs, .Install.cs, .Coexist.cs and .Streamline.cs for the rest.
public sealed partial class OptiScaler(HttpClient http, Downloads downloads, DlssCatalog dlss)
{
    public const string Component = "OptiScaler", IniName = "OptiScaler.ini", DefaultDll = "dxgi.dll", OptiPatcherPath = "plugins/OptiPatcher.asi";
    public const string Description = "OptiScaler replaces or adds upscalers (DLSS, FSR, XeSS) and frame generation in games that support any one of them. " +
        "It loads ReShade itself when both are installed, so RHI renames ReShade to ReShade64.dll if they would share a DLL name.";
    public static readonly string[] DllNames = ["dxgi.dll", "winmm.dll", "d3d11.dll", "d3d12.dll", "dbghelp.dll", "version.dll", "wininet.dll", "winhttp.dll"];
    public static readonly Dictionary<string, string> Hotkeys = OptiScalerPolicy.Hotkeys;

    private static string Root => Path.Combine(LinuxPaths.Cache, "optiscaler");
    // User-editable copies of RHI's INI templates, seeded once like %LocalAppData%\RHI\inis on Windows.
    public static string InisDirectory => Path.Combine(LinuxPaths.Data, "inis");

    public static string DetectGpu() => Directory.Exists("/sys/module/nvidia") || File.Exists("/proc/driver/nvidia/version") ? "NVIDIA" : "AMD";

    public static string Label(string variant, string version) => variant switch
    {
        OsVariant.Nightly => "Nightly " + version, OsVariant.DlssNr => "DLSS NR " + version, _ => version
    };

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
        return OptiScalerPolicy.TemplateName(gpu, dlssInputs, variant);
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
