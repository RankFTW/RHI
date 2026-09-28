using RenoDXCommander.Models;

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
