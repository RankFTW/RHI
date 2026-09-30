using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Services;

/// <summary>
/// Applies the user's configured DLSS / Streamline / Neural Rendering defaults to ONE game: driver "NVIDIA Override" flags,
/// DLL version swaps (skipped for overridden components), presets and render scales.
/// This is the logic that used to live inside the "Quick Apply" button handler; it moved here unchanged so the button and
/// One-Click Optimize share a single implementation.
/// </summary>
public sealed class DlssDefaultsApplier
{
    private readonly IDlssStreamlineService svc;
    private readonly INvidiaGameProfile pSvc;

    public DlssDefaultsApplier(IDlssStreamlineService dlssService, INvidiaGameProfile profile)
    {
        svc = dlssService;
        pSvc = profile;
    }

    public async Task ApplyAsync(GameCardViewModel targetCard, SettingsViewModel settings)
    {
        if (targetCard.DlssDetection == null) return;
        var installPath = targetCard.InstallPath ?? "";


        // Check driver override state — skip DLL swaps for overridden components
        bool srOverride = pSvc.IsSupported && pSvc.IsSrDriverOverrideActive(targetCard.GameName, installPath);
        bool rrOverride = pSvc.IsSupported && pSvc.IsRrDriverOverrideActive(targetCard.GameName, installPath);
        bool fgOverride = pSvc.IsSupported && pSvc.IsFgDriverOverrideActive(targetCard.GameName, installPath);

        // Apply driver override defaults if configured — takes priority over version defaults
        if (pSvc.IsSupported && settings.DefaultSrDriverOverride && targetCard.HasDlss)
            pSvc.SetSrDriverOverride(targetCard.GameName, installPath, true);
        if (pSvc.IsSupported && settings.DefaultRrDriverOverride && targetCard.HasDlssd)
            pSvc.SetRrDriverOverride(targetCard.GameName, installPath, true);
        if (pSvc.IsSupported && settings.DefaultFgDriverOverride && targetCard.HasDlssg)
            pSvc.SetFgDriverOverride(targetCard.GameName, installPath, true);

        // Re-read override state after applying defaults (may have just been enabled above)
        srOverride = pSvc.IsSupported && (srOverride || settings.DefaultSrDriverOverride);
        rrOverride = pSvc.IsSupported && (rrOverride || settings.DefaultRrDriverOverride);
        fgOverride = pSvc.IsSupported && (fgOverride || settings.DefaultFgDriverOverride);

        if (!string.IsNullOrEmpty(settings.DefaultDlssVersion) && targetCard.HasDlss && targetCard.DlssDetection.DlssPath != null
            && !(targetCard.DlssInstalledVersion?.StartsWith("1.") == true) && !srOverride)
        {
            if (settings.DefaultDlssVersion.Equals("Custom", StringComparison.OrdinalIgnoreCase))
                await svc.SwapDlssCustomAsync(targetCard.DlssDetection.DlssPath);
            else
                await svc.SwapDlssAsync(targetCard.DlssDetection.DlssPath, settings.DefaultDlssVersion);
        }
        if (!string.IsNullOrEmpty(settings.DefaultDlssdVersion) && targetCard.HasDlssd && targetCard.DlssDetection.DlssdPath != null
            && !(targetCard.DlssdInstalledVersion?.StartsWith("1.") == true) && !rrOverride)
        {
            if (settings.DefaultDlssdVersion.Equals("Custom", StringComparison.OrdinalIgnoreCase))
                await svc.SwapDlssCustomAsync(targetCard.DlssDetection.DlssdPath);
            else
                await svc.SwapDlssdAsync(targetCard.DlssDetection.DlssdPath, settings.DefaultDlssdVersion);
        }
        if (!string.IsNullOrEmpty(settings.DefaultDlssgVersion) && targetCard.HasDlssg && targetCard.DlssDetection.DlssgPath != null
            && !fgOverride)
        {
            if (settings.DefaultDlssgVersion.Equals("Custom", StringComparison.OrdinalIgnoreCase))
                await svc.SwapDlssCustomAsync(targetCard.DlssDetection.DlssgPath);
            else
                await svc.SwapDlssgAsync(targetCard.DlssDetection.DlssgPath, settings.DefaultDlssgVersion);
        }
        if (!string.IsNullOrEmpty(settings.DefaultStreamlineVersion) && targetCard.HasStreamline && targetCard.DlssDetection.StreamlineFolder != null
            && !(targetCard.StreamlineInstalledVersion?.StartsWith("1.") == true))
        {
            if (settings.DefaultStreamlineVersion.Equals("Custom", StringComparison.OrdinalIgnoreCase))
                await svc.SwapStreamlineCustomAsync(targetCard.DlssDetection.StreamlineFolder);
            else
                await svc.SwapStreamlineAsync(targetCard.DlssDetection.StreamlineFolder, settings.DefaultStreamlineVersion);
        }

        if (settings.DefaultSrPreset != 0 && targetCard.HasDlss && !(targetCard.DlssInstalledVersion?.StartsWith("1.") == true))
            pSvc.SetSrPreset(targetCard.GameName, installPath, settings.DefaultSrPreset);
        if (settings.DefaultRrPreset != 0 && targetCard.HasDlssd && !(targetCard.DlssdInstalledVersion?.StartsWith("1.") == true))
            pSvc.SetRrPreset(targetCard.GameName, installPath, settings.DefaultRrPreset);
        if (settings.DefaultFgPreset != 0 && targetCard.HasDlssg)
            pSvc.SetFgPreset(targetCard.GameName, installPath, settings.DefaultFgPreset);

        if (FeatureFlags.DlssNr)
        {
            if (!string.IsNullOrEmpty(settings.DefaultDlssnrVersion) && targetCard.HasDlssnr && targetCard.DlssDetection?.DlssnrPath != null)
                await svc.SwapDlssnrAsync(targetCard.DlssDetection.DlssnrPath, settings.DefaultDlssnrVersion);
            if (settings.DefaultNrPreset != 0 && targetCard.HasDlssnr)
                pSvc.SetNrPreset(targetCard.GameName, installPath, settings.DefaultNrPreset);
        }

        if (settings.DefaultSrRenderScale != 0 && targetCard.HasDlss && !(targetCard.DlssInstalledVersion?.StartsWith("1.") == true))
            pSvc.SetSrRenderScale(targetCard.GameName, installPath, settings.DefaultSrRenderScale);
        if (settings.DefaultRrRenderScale != 0 && targetCard.HasDlssd && !(targetCard.DlssdInstalledVersion?.StartsWith("1.") == true))
            pSvc.SetRrRenderScale(targetCard.GameName, installPath, settings.DefaultRrRenderScale);
    }
}
