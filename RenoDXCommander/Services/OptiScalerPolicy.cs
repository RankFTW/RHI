namespace RenoDXCommander.Services;

/// <summary>Platform-independent OptiScaler choices used by both desktop applications.</summary>
public static class OptiScalerPolicy
{
    public static string TemplateName(string gpuType, bool dlssInputs, string variant)
    {
        var suffix = variant switch
        {
            "Nightly" => "_nightly",
            "DlssNr" => "_dlssnr",
            _ => ""
        };
        var gpu = gpuType.Equals("NVIDIA", StringComparison.OrdinalIgnoreCase)
            ? "nvidia" : dlssInputs ? "amd-dlss" : "amd-nodlss";
        return $"OptiScaler{suffix}.{gpu}.ini";
    }

    public static readonly Dictionary<string, string> Hotkeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Insert"] = "0x2D", ["Delete"] = "0x2E", ["Home"] = "0x24", ["End"] = "0x23",
        ["Page Up"] = "0x21", ["Page Down"] = "0x22",
        ["F1"] = "0x70", ["F2"] = "0x71", ["F3"] = "0x72", ["F4"] = "0x73",
        ["F5"] = "0x74", ["F6"] = "0x75", ["F7"] = "0x76", ["F8"] = "0x77",
        ["F9"] = "0x78", ["F10"] = "0x79", ["F11"] = "0x7A", ["F12"] = "0x7B"
    };

    public static string ResolveHotkey(string name) => Hotkeys.GetValueOrDefault(name, name);

    public static string ResolveReShadeFilename(string? userOverride, string? manifestOverride, string? detectedApi)
    {
        if (!string.IsNullOrWhiteSpace(userOverride)) return userOverride;
        if (!string.IsNullOrWhiteSpace(manifestOverride)) return manifestOverride;
        return detectedApi?.ToLowerInvariant() switch
        {
            "dx9" or "directx9" => "d3d9.dll",
            "opengl" => "opengl32.dll",
            _ => "dxgi.dll"
        };
    }
}
