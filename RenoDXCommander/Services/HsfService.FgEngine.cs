// HsfService.FgEngine.cs: HSF's frame generation engine (HSF.ini [HSF] FgEngine): dlssg = NVIDIA's DLSS-G
// (default), xemfg = Intel's XeSS frame generation run inside DLSS-G (experimental; from the XeMFG test build,
// Nov 2026). HSF applies it at the next launch; its own menu (FG ENGINE) writes the same key. XeMFG needs Intel's
// libxess_fg.dll 1.3.1.78 and libxell.dll beside HSF.ini (test build) or in HSF_streamline\ (as releases will ship
// them), and Universal MFG Unlocker for 3x to 6x on every RTX card. Without the unlocker it runs at 2x.

namespace RenoDXCommander.Services;

public partial class HsfService
{
    public const string FgEngineKey = "FgEngine";
    public const string EngineDlssg = "dlssg";
    public const string EngineXeMfg = "xemfg";
    internal static readonly string[] XeFgFiles = ["libxess_fg.dll", "libxell.dll"];

    private static readonly Dictionary<string, (long Size, DateTime Time, bool Has)> s_xemfgCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly byte[] s_xemfgMarker = System.Text.Encoding.ASCII.GetBytes("FgEngine=xemfg");

    /// <summary>The game's HSF add-on knows XeMFG (its code has the FgEngine=xemfg text). Cached by size + date.</summary>
    public static bool SupportsXeMfg(string installPath, bool is32Bit)
    {
        var path = Path.Combine(AddonDir(installPath), AddonFileFor(is32Bit));
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) return false;
            lock (s_xemfgCache)
                if (s_xemfgCache.TryGetValue(path, out var c) && c.Size == fi.Length && c.Time == fi.LastWriteTimeUtc) return c.Has;
            bool has = File.ReadAllBytes(path).AsSpan().IndexOf(s_xemfgMarker) >= 0;
            lock (s_xemfgCache) s_xemfgCache[path] = (fi.Length, fi.LastWriteTimeUtc, has);
            return has;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>Intel's XeSS frame generation files are next to HSF.ini or in HSF_streamline\.</summary>
    public static bool HasXeFgFiles(string installPath)
    {
        var dir = AddonDir(installPath);
        return XeFgFiles.All(f => File.Exists(Path.Combine(dir, f)) || File.Exists(Path.Combine(dir, StreamlineFolder, f)));
    }

    /// <summary>The game's engine (dlssg when unset or unknown).</summary>
    public static string ReadFgEngine(string installPath)
    {
        try
        {
            var ini = IniPath(installPath);
            if (!File.Exists(ini)) return EngineDlssg;
            bool inHsf = false;
            foreach (var raw in File.ReadAllLines(ini))
            {
                var t = raw.Trim();
                if (t.StartsWith('[') && t.EndsWith(']')) { inHsf = t[1..^1].Trim().Equals("HSF", StringComparison.OrdinalIgnoreCase); continue; }
                int eq = t.IndexOf('=');
                if (inHsf && eq > 0 && t[..eq].Trim().Equals(FgEngineKey, StringComparison.OrdinalIgnoreCase))
                    return t[(eq + 1)..].Trim().Equals(EngineXeMfg, StringComparison.OrdinalIgnoreCase) ? EngineXeMfg : EngineDlssg;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return EngineDlssg;
    }

    /// <summary>Sets [HSF] FgEngine (one line; the rest of HSF.ini untouched). False if it couldn't be written.</summary>
    public bool WriteFgEngine(string installPath, string engine)
    {
        var ini = IniPath(installPath);
        try
        {
            var lines = File.Exists(ini) ? File.ReadAllLines(ini).ToList() : new List<string>();
            IniSet(lines, "HSF", FgEngineKey, engine);
            File.WriteAllText(ini, string.Join("\r\n", lines) + "\r\n");
            _crashReporter.Log($"[HsfService.WriteFgEngine] {FgEngineKey}={engine} → '{ini}'");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"[HsfService.WriteFgEngine] '{ini}' — {ex.Message}");
            return false;
        }
    }
}
