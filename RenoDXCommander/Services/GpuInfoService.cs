// GpuInfoService.cs — which NVIDIA graphics card(s) the PC has, read once from Windows (WMI
// Win32_VideoController: only devices that are present, no admin needed) and cached.
// Used to tell whether DLSS multi frame generation needs Universal MFG Unlocker (RTX 20 / 30 / 40)
// or works on its own (RTX 50 and newer). The name parsing is pure (GpuFamily.FromName) and tested.

using System.Management;
using System.Text.RegularExpressions;

namespace RenoDXCommander.Services;

/// <summary>NVIDIA GPU generations, as far as DLSS frame generation is concerned.</summary>
public enum GpuFamily
{
    /// <summary>Not an RTX card (GTX, Quadro without RTX, or unrecognised): no DLSS frame generation.</summary>
    NotRtx,
    Rtx20,
    Rtx30,
    Rtx40,
    Rtx50,
    /// <summary>A generation newer than RTX 50: assumed to have multi frame generation of its own.</summary>
    Newer,
}

/// <summary>One NVIDIA card Windows reports.</summary>
public sealed record NvidiaGpu(string Name, GpuFamily Family)
{
    /// <summary>Short label for the UI, e.g. "RTX 40".</summary>
    public string FamilyLabel => GpuInfoService.FamilyLabel(Family);
}

public static class GpuInfoService
{
    private static readonly SemaphoreSlim Lock = new(1, 1);
    private static IReadOnlyList<NvidiaGpu>? _cached;

    // Desktop and laptop GeForce: "RTX 4070", "RTX 4070 Laptop GPU", "RTX 5090 D", "RTX 3050 Ti"
    private static readonly Regex GeForceNumber = new(@"\bRTX\s*(\d{2})(\d{2})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// The generation for a card name from Windows. Workstation names say their architecture
    /// ("RTX 6000 Ada Generation", "RTX PRO 6000 Blackwell", "RTX A4000" = Ampere, "Quadro RTX 5000" /
    /// "TITAN RTX" = Turing), GeForce names their series number.
    /// </summary>
    public static GpuFamily FromName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return GpuFamily.NotRtx;
        var n = name.Trim();
        bool Has(string s) => n.Contains(s, StringComparison.OrdinalIgnoreCase);

        if (!Has("RTX")) return GpuFamily.NotRtx;              // GTX, MX, older Quadro, other brands
        if (Has("Blackwell")) return GpuFamily.Rtx50;
        if (Has(" Ada")) return GpuFamily.Rtx40;               // "RTX 2000 Ada Generation"
        if (Regex.IsMatch(n, @"\bRTX\s*A\d{3,4}\b", RegexOptions.IgnoreCase)) return GpuFamily.Rtx30; // Ampere workstation
        if (Has("Quadro RTX") || Has("TITAN RTX")) return GpuFamily.Rtx20;

        var m = GeForceNumber.Match(n);
        if (!m.Success) return GpuFamily.NotRtx;
        return int.Parse(m.Groups[1].Value) switch
        {
            20 => GpuFamily.Rtx20,
            30 => GpuFamily.Rtx30,
            40 => GpuFamily.Rtx40,
            50 => GpuFamily.Rtx50,
            > 50 and < 100 => GpuFamily.Newer,
            _ => GpuFamily.NotRtx,
        };
    }

    public static string FamilyLabel(GpuFamily f) => f switch
    {
        GpuFamily.Rtx20 => "RTX 20",
        GpuFamily.Rtx30 => "RTX 30",
        GpuFamily.Rtx40 => "RTX 40",
        GpuFamily.Rtx50 => "RTX 50",
        GpuFamily.Newer => "newer than RTX 50",
        _ => "not RTX",
    };

    /// <summary>True for cards that need Universal MFG Unlocker for frame generation beyond 2x (RTX 40) or at all (RTX 20 / 30).</summary>
    public static bool NeedsMfgUnlocker(GpuFamily f) => f is GpuFamily.Rtx20 or GpuFamily.Rtx30 or GpuFamily.Rtx40;

    /// <summary>
    /// The card that matters when there are several (the newest RTX), or null when no NVIDIA card was found.
    /// </summary>
    public static NvidiaGpu? Primary(IReadOnlyList<NvidiaGpu> gpus) =>
        gpus.OrderByDescending(g => g.Family).FirstOrDefault();

    /// <summary>The NVIDIA cards Windows reports (cached after the first call). Empty when there are none or WMI fails.</summary>
    public static async Task<IReadOnlyList<NvidiaGpu>> GetNvidiaGpusAsync()
    {
        if (_cached != null) return _cached;
        await Lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_cached != null) return _cached;
            _cached = DevOverride() ?? await Task.Run(Query).ConfigureAwait(false);
            CrashReporter.Log(_cached.Count == 0
                ? "[GpuInfoService] No NVIDIA graphics card found"
                : "[GpuInfoService] NVIDIA: " + string.Join(", ", _cached.Select(g => $"{g.Name} ({g.FamilyLabel})")));
            return _cached;
        }
        finally { Lock.Release(); }
    }

    /// <summary>The cached result, or null before <see cref="GetNvidiaGpusAsync"/> has finished once.</summary>
    public static IReadOnlyList<NvidiaGpu>? Cached => _cached;

    /// <summary>
    /// Developer testing only (needs unlock.txt): %LocalAppData%\RHI\gpu_override.txt, one card name per line
    /// (e.g. "NVIDIA GeForce RTX 4090"), is used instead of the real cards. Read once per launch. Null when absent.
    /// </summary>
    private static IReadOnlyList<NvidiaGpu>? DevOverride()
    {
        if (!DevUnlockService.IsUnlocked) return null;
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "gpu_override.txt");
        try
        {
            if (!File.Exists(path)) return null;
            var names = File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();
            if (names.Count == 0) return null;
            CrashReporter.Log($"[GpuInfoService] Dev override from gpu_override.txt: {string.Join(", ", names)}");
            return names.Select(n => new NvidiaGpu(n, FromName(n))).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[GpuInfoService.DevOverride] Couldn't read '{path}' — {ex.Message}");
            return null;
        }
    }

    private static IReadOnlyList<NvidiaGpu> Query()
    {
        var list = new List<NvidiaGpu>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, AdapterCompatibility, PNPDeviceID FROM Win32_VideoController");
            foreach (var obj in searcher.Get())
            {
                using (obj)
                {
                    var name = obj["Name"] as string;
                    var vendor = obj["AdapterCompatibility"] as string;
                    var pnp = obj["PNPDeviceID"] as string;
                    // NVIDIA's PCI vendor id is 10DE; the vendor string is the fallback
                    bool nvidia = (pnp?.Contains("VEN_10DE", StringComparison.OrdinalIgnoreCase) ?? false)
                               || (vendor?.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ?? false);
                    if (!nvidia || string.IsNullOrWhiteSpace(name)) continue;
                    list.Add(new NvidiaGpu(name.Trim(), FromName(name)));
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or System.Runtime.InteropServices.COMException
                                      or UnauthorizedAccessException or TypeInitializationException)
        {
            CrashReporter.Log($"[GpuInfoService.Query] WMI failed — {ex.Message}");
        }
        return list;
    }
}
