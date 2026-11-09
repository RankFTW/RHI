// SourceEngineSetup.cs -- files the RenoDX Source engine mod needs besides the addon (renodx wiki, Mods#source-engine):
// stable DXVK's d3d9.dll as dxgi_dxvk.dll beside ReShade (loaded through ReShade's [PROXY]), reshade.ini
// [GENERAL] Disable=1 + [PROXY], and dxvk.conf in the game's main folder. Also works out where those go:
// 32-bit games use bin\, 64-bit games the main folder, Garry's Mod both (bin\ and bin\win64\).

namespace RenoDXCommander.Services;

public static class SourceEngineSetup
{
    public const string ProxyDllName  = "dxgi_dxvk.dll";
    public const string ReShadeDll    = "d3d9.dll";
    public const string AddonName64   = "renodx-sourceengine.addon64";
    public const string AddonName32   = "renodx-sourceengine.addon32";
    public const string AddonUrl64    = "https://github.com/CommandGenius/renodx/releases/download/snapshot/" + AddonName64;
    public const string AddonUrl32    = "https://github.com/CommandGenius/renodx/releases/download/snapshot/" + AddonName32;
    public const string LaunchArg     = "-insecure";
    public const string DxvkConfName  = "dxvk.conf";

    /// <summary>Sentinel beside a file RHI placed: non-empty = the file it replaced (restore on removal), 0 bytes = RHI's own (delete).</summary>
    public const string SentinelSuffix = ".original";

    internal static readonly (string Key, string Value)[] DxvkConfSettings =
    {
        ("dxgi.deferSurfaceCreation", "True"),
        ("d3d9.deferSurfaceCreation", "True"),
    };

    /// <summary>A folder that gets ReShade + the DXVK proxy, and which build (32/64-bit) goes there.</summary>
    public sealed record Target(string Dir, bool Is64Bit);

    private const ushort MachineI386  = 0x014C;
    private const ushort MachineAmd64 = 0x8664;

    // Tools and launchers that sit beside the game exe and say nothing about the game's build
    private static readonly string[] IgnoredExeParts = { "unins", "crash", "setup", "redist", "vconsole", "launcher" };

    /// <summary>
    /// Where ReShade and the DXVK proxy go for a Source game, given its main folder (renodx wiki, Source Engine):
    /// <list type="bullet">
    /// <item>Garry's Mod: gmod_win64.exe and gmod.exe both in the main folder, engines in bin\win64\ (64-bit) and
    /// bin\ (32-bit) → bin\win64\ first (the game's detected build), then bin\.</item>
    /// <item>Other 64-bit games (64-bit exe in the main folder, engine in e.g. bin\x64\) → the main folder.</item>
    /// <item>32-bit games → bin\ when it holds the engine (else the main folder), plus bin\win64\ when that
    /// holds a 64-bit build.</item>
    /// </list>
    /// Empty when nothing matches. File-system work: call from a background thread.
    /// </summary>
    public static List<Target> DetectTargets(string root)
    {
        var list = new List<Target>();
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return list;

        var rootMachines = GameExes(root).Select(ReadMachine).ToList();
        bool has64 = rootMachines.Contains(MachineAmd64), has32 = rootMachines.Contains(MachineI386);
        var bin = Path.Combine(root, "bin");
        var win64 = Path.Combine(bin, "win64");

        if (has64 && IsEngineFolder(win64))
        {
            // The exe in the main folder runs the engine in bin\win64\: the DLLs load from there, not the main folder
            list.Add(new Target(win64, true));
            if (has32 && IsEngineFolder(bin)) list.Add(new Target(bin, false));
        }
        else if (has64)
            list.Add(new Target(root, true));
        else if (has32)
        {
            list.Add(new Target(IsEngineFolder(bin) ? bin : root, false));
            if (IsEngineFolder(win64) && GameExes(win64).Any(e => ReadMachine(e) == MachineAmd64))
                list.Add(new Target(win64, true));
        }
        return list;
    }

    /// <summary>
    /// The install folder relative to <paramref name="root"/> when the game's main target isn't the main
    /// folder (e.g. "bin"), else null.
    /// </summary>
    public static string? PrimarySubfolder(string root)
    {
        var first = DetectTargets(root).FirstOrDefault();
        if (first == null || SameDir(first.Dir, root)) return null;
        return Path.GetRelativePath(root, first.Dir);
    }

    private static bool IsEngineFolder(string dir)
        => File.Exists(Path.Combine(dir, "shaderapidx9.dll")) || File.Exists(Path.Combine(dir, "engine.dll"));

    private static IEnumerable<string> GameExes(string dir)
    {
        string[] files;
        try { files = Directory.GetFiles(dir, "*.exe"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[SourceEngineSetup.GameExes] Can't list '{dir}' — {ex.Message}");
            return Array.Empty<string>();
        }
        return files.Where(f =>
        {
            var n = Path.GetFileName(f);
            return !IgnoredExeParts.Any(p => n.Contains(p, StringComparison.OrdinalIgnoreCase));
        });
    }

    /// <summary>PE machine type of an exe (0 when it can't be read).</summary>
    internal static ushort ReadMachine(string exePath)
    {
        try
        {
            using var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var br = new BinaryReader(fs);
            if (fs.Length < 0x40) return 0;
            fs.Position = 0x3C;
            int pe = br.ReadInt32();
            if (pe <= 0 || pe + 6 > fs.Length) return 0;
            fs.Position = pe;
            if (br.ReadUInt32() != 0x00004550) return 0; // "PE\0\0"
            return br.ReadUInt16();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[SourceEngineSetup.ReadMachine] '{exePath}' — {ex.Message}");
            return 0;
        }
    }

    private static bool SameDir(string a, string b)
        => string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    // ── Files RHI places (sentinel pattern) ──────────────────────────────────

    /// <summary>
    /// Copies <paramref name="source"/> to dir\name. The first time, the file it replaces is kept in
    /// name.original (an empty name.original when there was none), so removal can put things back.
    /// </summary>
    public static void DeployOwnedFile(string dir, string name, string source)
    {
        var dest = Path.Combine(dir, name);
        var sentinel = dest + SentinelSuffix;
        if (!File.Exists(sentinel))
        {
            if (File.Exists(dest)) File.Copy(dest, sentinel);
            else File.WriteAllBytes(sentinel, Array.Empty<byte>());
        }
        File.Copy(source, dest, overwrite: true);
    }

    /// <summary>True when dir\name was placed by <see cref="DeployOwnedFile"/>.</summary>
    public static bool IsOwned(string dir, string name) => File.Exists(Path.Combine(dir, name) + SentinelSuffix);

    /// <summary>Undoes <see cref="DeployOwnedFile"/>: restores the replaced file, or deletes RHI's. No-op without a sentinel.</summary>
    public static void RemoveOwnedFile(string dir, string name)
    {
        var dest = Path.Combine(dir, name);
        var sentinel = dest + SentinelSuffix;
        if (!File.Exists(sentinel)) return;
        if (new FileInfo(sentinel).Length > 0) File.Copy(sentinel, dest, overwrite: true);
        else if (File.Exists(dest)) File.Delete(dest);
        File.Delete(sentinel);
    }

    // ── reshade.ini ──────────────────────────────────────────────────────────

    /// <summary>Adds [GENERAL] Disable=1 and the [PROXY] lines to dir\reshade.ini (created when missing).</summary>
    public static void ApplyReShadeIni(string dir)
    {
        var path = Path.Combine(dir, "reshade.ini");
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
        SetIniValue(lines, "GENERAL", "Disable", "1");
        SetIniValue(lines, "PROXY", "EnableProxyLibrary", "1");
        SetIniValue(lines, "PROXY", "ProxyLibrary", ProxyDllName);
        File.WriteAllLines(path, lines);
    }

    /// <summary>Removes what <see cref="ApplyReShadeIni"/> added (the [PROXY] section and [GENERAL] Disable=1).</summary>
    public static void RemoveReShadeIni(string dir)
    {
        var path = Path.Combine(dir, "reshade.ini");
        if (!File.Exists(path)) return;
        var lines = File.ReadAllLines(path).ToList();
        bool changed = RemoveIniSection(lines, "PROXY");
        changed |= RemoveIniValue(lines, "GENERAL", "Disable", "1");
        if (changed) File.WriteAllLines(path, lines);
    }

    /// <summary>
    /// ReShade's configs in a folder: reshade.ini, plus ReShade2.ini, ReShade3.ini ... which ReShade creates (as a
    /// copy of reshade.ini) for extra runtimes. The Source mod's DXVK proxy makes a second runtime, and that one
    /// draws the overlay and loads the shaders, so it reads ReShade2.ini.
    /// </summary>
    public static IEnumerable<string> ReShadeConfigs(string dir)
    {
        if (!Directory.Exists(dir)) yield break;
        foreach (var f in Directory.EnumerateFiles(dir, "reshade*.ini"))
        {
            var n = Path.GetFileNameWithoutExtension(f);
            if (n.Length == "reshade".Length || n["reshade".Length..].All(char.IsDigit)) yield return f;
        }
    }

    /// <summary>Config of the DXVK proxy's runtime (ReShade's second one), which draws the overlay and runs the shaders.</summary>
    public const string SecondRuntimeIni = "ReShade2.ini";

    /// <summary>
    /// Creates dir\ReShade2.ini from dir\reshade.ini before the game first runs, so the proxy's runtime starts
    /// with RHI's settings (shader paths, screenshot folder, keys, RenoDX presets) instead of a copy ReShade
    /// makes later. Without [PROXY] and [GENERAL] Disable=1: those are for the first runtime only, and
    /// Disable=1 here would turn the shaders off. An existing ReShade2.ini is kept. RHI-owned (empty sentinel).
    /// </summary>
    public static void EnsureSecondRuntimeIni(string dir)
    {
        var dest = Path.Combine(dir, SecondRuntimeIni);
        if (File.Exists(dest)) return;
        var source = Path.Combine(dir, "reshade.ini");
        var lines = File.Exists(source) ? File.ReadAllLines(source).ToList() : new List<string>();
        RemoveIniSection(lines, "PROXY");
        RemoveIniValue(lines, "GENERAL", "Disable", "1");
        File.WriteAllLines(dest, lines);
        var sentinel = dest + SentinelSuffix;
        if (!File.Exists(sentinel)) File.WriteAllBytes(sentinel, Array.Empty<byte>());
    }

    /// <summary>The extra runtime configs (ReShade2.ini ...), not reshade.ini.</summary>
    public static IEnumerable<string> ExtraRuntimeConfigs(string dir)
        => ReShadeConfigs(dir).Where(f => Path.GetFileNameWithoutExtension(f).Length > "reshade".Length);

    /// <summary>
    /// Points the shader / texture paths of every ReShade config in <paramref name="dir"/> at
    /// <paramref name="mainDir"/>'s reshade-shaders, so a second install (Garry's Mod's 32-bit bin\) shares the
    /// shaders RHI keeps in the main one (bin\win64\). Only the default paths change; paths a user set are kept.
    /// </summary>
    public static void ShareShaderPaths(string dir, string mainDir)
    {
        var rel = Path.GetRelativePath(dir, mainDir);
        foreach (var path in ReShadeConfigs(dir).ToList())
        {
            var lines = File.ReadAllLines(path).ToList();
            bool changed = false;
            foreach (var (key, sub) in new[] { ("EffectSearchPaths", "Shaders"), ("TextureSearchPaths", "Textures") })
            {
                var current = GetIniValue(lines, "GENERAL", key);
                if (current != null && !current.Equals($@".\reshade-shaders\{sub}\**", StringComparison.OrdinalIgnoreCase)) continue;
                SetIniValue(lines, "GENERAL", key, $@".\{rel}\reshade-shaders\{sub}\**");
                changed = true;
            }
            if (changed) File.WriteAllLines(path, lines);
        }
    }

    internal static string? GetIniValue(List<string> lines, string section, string key)
    {
        bool inSection = false;
        foreach (var line in lines)
        {
            var t = line.Trim();
            if (t.StartsWith('[')) { inSection = t.Equals($"[{section}]", StringComparison.OrdinalIgnoreCase); continue; }
            if (inSection && KeyOf(t) is { } k && k.Equals(key, StringComparison.OrdinalIgnoreCase))
                return t[(t.IndexOf('=') + 1)..].Trim();
        }
        return null;
    }

    internal static void SetIniValue(List<string> lines, string section, string key, string value)
    {
        int header = -1, lastInSection = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            var t = lines[i].Trim();
            if (t.StartsWith('['))
            {
                if (header >= 0) break; // end of our section
                if (t.Equals($"[{section}]", StringComparison.OrdinalIgnoreCase)) header = lastInSection = i;
                continue;
            }
            if (header < 0) continue;
            if (t.Length > 0) lastInSection = i;
            if (KeyOf(t) is { } k && k.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = $"{key}={value}";
                return;
            }
        }
        if (header >= 0)
        {
            // After the section's last line, before the blank line that separates it from the next
            lines.Insert(lastInSection + 1, $"{key}={value}");
            return;
        }
        if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1])) lines.Add("");
        lines.Add($"[{section}]");
        lines.Add($"{key}={value}");
    }

    internal static bool RemoveIniValue(List<string> lines, string section, string key, string onlyIfValue)
    {
        bool inSection = false;
        for (int i = 0; i < lines.Count; i++)
        {
            var t = lines[i].Trim();
            if (t.StartsWith('[')) { inSection = t.Equals($"[{section}]", StringComparison.OrdinalIgnoreCase); continue; }
            if (!inSection || KeyOf(t) is not { } k || !k.Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
            if (!t[(t.IndexOf('=') + 1)..].Trim().Equals(onlyIfValue, StringComparison.OrdinalIgnoreCase)) return false;
            lines.RemoveAt(i);
            return true;
        }
        return false;
    }

    internal static bool RemoveIniSection(List<string> lines, string section)
    {
        int start = lines.FindIndex(l => l.Trim().Equals($"[{section}]", StringComparison.OrdinalIgnoreCase));
        if (start < 0) return false;
        int end = start + 1;
        while (end < lines.Count && !lines[end].TrimStart().StartsWith('[')) end++;
        // Keep one blank line between the neighbours
        while (start > 0 && string.IsNullOrWhiteSpace(lines[start - 1])) start--;
        lines.RemoveRange(start, end - start);
        if (start > 0 && start < lines.Count) lines.Insert(start, "");
        return true;
    }

    private static string? KeyOf(string trimmedLine)
    {
        if (trimmedLine.Length == 0 || trimmedLine[0] is ';' or '#') return null;
        int eq = trimmedLine.IndexOf('=');
        return eq > 0 ? trimmedLine[..eq].Trim() : null;
    }

    // ── dxvk.conf ────────────────────────────────────────────────────────────

    /// <summary>Makes sure dir\dxvk.conf has the two deferSurfaceCreation lines (other lines are kept).</summary>
    public static void EnsureDxvkConf(string dir)
    {
        var path = Path.Combine(dir, DxvkConfName);
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
        foreach (var (key, value) in DxvkConfSettings)
        {
            int i = lines.FindIndex(l => KeyOf(l.Trim()) is { } k && k.Equals(key, StringComparison.OrdinalIgnoreCase));
            var line = $"{key} = {value}";
            if (i >= 0) lines[i] = line; else lines.Add(line);
        }
        File.WriteAllLines(path, lines);
    }

    /// <summary>Removes the deferSurfaceCreation lines; deletes dxvk.conf when nothing else is left in it.</summary>
    public static void RemoveDxvkConf(string dir)
    {
        var path = Path.Combine(dir, DxvkConfName);
        if (!File.Exists(path)) return;
        var lines = File.ReadAllLines(path).ToList();
        int removed = lines.RemoveAll(l => KeyOf(l.Trim()) is { } k
            && DxvkConfSettings.Any(s => s.Key.Equals(k, StringComparison.OrdinalIgnoreCase)));
        if (lines.All(string.IsNullOrWhiteSpace)) File.Delete(path);
        else if (removed > 0) File.WriteAllLines(path, lines);
    }

    // ── Launch argument ──────────────────────────────────────────────────────

    public static bool HasLaunchArg(string? args)
        => !string.IsNullOrWhiteSpace(args)
           && args.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(a => a.Equals(LaunchArg, StringComparison.OrdinalIgnoreCase));

    /// <summary>The launch arguments with -insecure added (unchanged when it's already there).</summary>
    public static string AddLaunchArg(string? args)
        => HasLaunchArg(args) ? args!.Trim() : $"{args?.Trim()} {LaunchArg}".Trim();

    /// <summary>The launch arguments without -insecure.</summary>
    public static string RemoveLaunchArg(string? args)
        => string.Join(' ', (args ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(a => !a.Equals(LaunchArg, StringComparison.OrdinalIgnoreCase)));
}
