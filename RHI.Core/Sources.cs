namespace RHI.Core;

// Download sources shared by the platform adapters. Shared services may own their own sources;
// game-specific URLs and current release selections come from the live manifests.
public static class Sources
{
    public const string Project = "https://github.com/TwoToneEddy/RHI";
    public const string UserAgent = "RHI-Linux/0.1 (+" + Project + ")";
    public const string Manifest = "https://raw.githubusercontent.com/RankFTW/RHI/main/manifest.json";
    public const string DlssManifest = "https://raw.githubusercontent.com/RankFTW/RHI/main/dlss_manifest.json";
    public const string Wiki = "https://github.com/clshortfuse/renodx/wiki/Mods";
    public const string REFrameworkDownloadBase = "https://github.com/praydog/REFramework-nightly/releases/latest/download/";
    public const string REFrameworkReleases = "https://api.github.com/repos/praydog/REFramework-nightly/releases";
    public const string ReShade = "https://reshade.me/";
    public static string ReShadeNightly(int bits) => $"https://nightly.link/crosire/reshade/workflows/build/main/ReShade%20({bits}-bit).zip";
    public const string CompilerArchiveVersion = "62.0.3";
    public static string CompilerArchive(int bits) => $"https://download-installer.cdn.mozilla.net/pub/firefox/releases/{CompilerArchiveVersion}/win{bits}/ach/Firefox%20Setup%20{CompilerArchiveVersion}.exe";
    public static string CompilerArchiveSha256(int bits) => bits == 32
        ? "d6edb4ff0a713f417ebd19baedfe07527c6e45e84a6c73ed8c66a33377cc0aca"
        : "721977f36c008af2b637aedd3f1b529f3cfed6feb10f68ebe17469acb1934986";
    public const string DgVoodooFallback = "https://github.com/dege-diosg/dgVoodoo2/releases/download/v2.87.3/dgVoodoo2_87_3.zip";
    public const string RhiAddonReleases = "https://api.github.com/repos/RankFTW/rhi-repo/releases?per_page=100";
    public const string FeederReleases = "https://api.github.com/repos/jlrouzies-fr/DLSS5-Feeder/releases?per_page=100";
    public const string BridgeReleases = "https://api.github.com/repos/NIGos/dlss5-bridge/releases?per_page=100";
    public const string CostScalerReleaseRoot = "https://api.github.com/repos/xenmods/DLSSNR-Cost-Scaler/releases";
    public const string CostScalerReleases = CostScalerReleaseRoot + "?per_page=30";
    public const string CostScalerLatest = CostScalerReleaseRoot + "/latest";
    public const string Lumenite = "https://github.com/umar-afzaal/LumeniteFX/archive/refs/heads/mainline.zip";
    public const string OptiScalerStable = "https://api.github.com/repos/optiscaler/OptiScaler/releases/latest";
    public const string OptiScalerNightlyReleases = "https://api.github.com/repos/optiscaler/OptiScaler-nightly/releases";
    public const string OptiScalerNightly = OptiScalerNightlyReleases + "?per_page=5";
    public const string OptiScalerDlssNrReleases = "https://api.github.com/repos/wilsjo2/OptiScaler-DLSSNR-PreSR-Multipass/releases";
    public const string OptiScalerDlssNr = OptiScalerDlssNrReleases + "?per_page=5";
    public const string OptiPatcher = "https://github.com/optiscaler/OptiPatcher/releases/download/rolling/OptiPatcher.asi";
}
