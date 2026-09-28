using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public partial class OptiScalerService
{
    // ── ReShade coexistence helpers ───────────────────────────────────────────

    /// <summary>
    /// Resolves the correct ReShade filename to restore when OptiScaler is uninstalled.
    /// Priority: user DLL override > manifest override > auto-detected API > dxgi.dll default.
    /// </summary>
    internal string ResolveReShadeFilename(IOptiScalerGame card)
    {
        // 1. User DLL override for ReShade
        var userRsName = _dllOverrideService.GetEffectiveRsName(card.GameName);
        var cfg = _dllOverrideService.GetDllOverride(card.GameName);
        if (cfg != null && !string.IsNullOrWhiteSpace(cfg.ReShadeFileName))
            return cfg.ReShadeFileName;

        // 2. Manifest override — if GetEffectiveRsName returned something other than the default,
        //    it came from the manifest
        if (!userRsName.Equals(AuxInstallService.RsNormalName, StringComparison.OrdinalIgnoreCase))
            return userRsName;

        // 3. Auto-detected graphics API
        if (card.DetectedApis != null && card.DetectedApis.Count > 0)
        {
            // Pick the primary API for filename resolution
            // DX11/DX12 checked first — most modern games use these as primary
            if (card.DetectedApis.Contains(Models.GraphicsApiType.DirectX11)
                || card.DetectedApis.Contains(Models.GraphicsApiType.DirectX12))
                return "dxgi.dll";
            if (card.DetectedApis.Contains(Models.GraphicsApiType.DirectX9))
                return "d3d9.dll";
            if (card.DetectedApis.Contains(Models.GraphicsApiType.OpenGL))
                return "opengl32.dll";
        }

        // 4. Default
        return AuxInstallService.RsNormalName; // dxgi.dll
    }

    /// <summary>
    /// Resolves the correct ReShade filename using only override/API data (no card dependency).
    /// Priority: user DLL override > manifest override > API-based default > dxgi.dll.
    /// </summary>
    internal static string ResolveReShadeFilename(
        string? userOverride,
        string? manifestOverride,
        string? detectedApi)
    {
        return OptiScalerPolicy.ResolveReShadeFilename(userOverride, manifestOverride, detectedApi);
    }
}
