using System.Text;

namespace RenoDXCommander.Services;

/// <summary>
/// Manages OptiScaler lifecycle: download, staging, install, uninstall,
/// update detection, INI management, and ReShade coexistence.
/// </summary>
public partial class OptiScalerService : IOptiScalerService
{
    // ── Constants ─────────────────────────────────────────────────────────────
    public const string DefaultDllName = "dxgi.dll";
    public const string IniFileName = "OptiScaler.ini";
    public const string ReShadeCoexistName = "ReShade64.dll";
    public const string AddonType = "OptiScaler";

    public static readonly string[] SupportedDllNames =
    [
        "dxgi.dll", "winmm.dll", "d3d11.dll", "d3d12.dll", "dbghelp.dll",
        "version.dll", "wininet.dll", "winhttp.dll"
    ];

    public static readonly string[] CompanionFiles =
    [
        "fakenvapi.dll",
        "dlssg_to_fsr3.dll",
        // FFX SDK DLLs (exact names resolved from staging folder at runtime)
    ];

    private static readonly string StagingDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "optiscaler");

    private static readonly string VersionFilePath = Path.Combine(StagingDir, "version.txt");

    public static readonly string OsIniPath = Path.Combine(
        AuxInstallService.InisDir, IniFileName);

    /// <summary>
    /// Returns all 6 possible user-editable INI paths in the inis folder.
    /// Used for operations that apply to all variants/GPU configs (e.g. hotkey).
    /// </summary>
    public static IEnumerable<string> AllUserIniPaths()
    {
        var dir = AuxInstallService.InisDir;
        yield return Path.Combine(dir, "OptiScaler.nvidia.ini");
        yield return Path.Combine(dir, "OptiScaler.amd-dlss.ini");
        yield return Path.Combine(dir, "OptiScaler.amd-nodlss.ini");
        yield return Path.Combine(dir, "OptiScaler_nightly.nvidia.ini");
        yield return Path.Combine(dir, "OptiScaler_nightly.amd-dlss.ini");
        yield return Path.Combine(dir, "OptiScaler_nightly.amd-nodlss.ini");
        yield return Path.Combine(dir, "OptiScaler_dlssnr.nvidia.ini");
        yield return Path.Combine(dir, "OptiScaler_dlssnr.amd-dlss.ini");
        yield return Path.Combine(dir, "OptiScaler_dlssnr.amd-nodlss.ini");
    }

    /// <summary>
    /// Returns the user-editable INI path in the inis folder for the given GPU type, DLSS setting, and variant.
    /// </summary>
    public static string GetUserIniPath(string gpuType, bool dlssInputs, string variant = "Stable")
    {
        var suffix = variant switch {
            "Nightly" => "_nightly",
            "DlssNr"  => "_dlssnr",
            _         => ""
        };
        var fileName = gpuType.Equals("NVIDIA", StringComparison.OrdinalIgnoreCase)
            ? $"OptiScaler{suffix}.nvidia.ini"
            : dlssInputs
                ? $"OptiScaler{suffix}.amd-dlss.ini"
                : $"OptiScaler{suffix}.amd-nodlss.ini";
        return Path.Combine(AuxInstallService.InisDir, fileName);
    }

    private static readonly string GitHubReleasesApi =
        "https://api.github.com/repos/optiscaler/OptiScaler/releases/latest";

    private static readonly string NightlyStagingDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "optiscaler-nightly");
    private static readonly string NightlyVersionFilePath = Path.Combine(NightlyStagingDir, "version.txt");
    private const string NightlyReleasesApi =
        "https://api.github.com/repos/optiscaler/OptiScaler-nightly/releases";

    // ── OptiPatcher constants ─────────────────────────────────────────────────
    private static readonly string OptiPatcherReleasesApi =
        "https://api.github.com/repos/optiscaler/OptiPatcher/releases/tags/rolling";
    private static readonly string OptiPatcherStagingDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "optipatcher");
    private static readonly string OptiPatcherVersionPath = Path.Combine(OptiPatcherStagingDir, "version.txt");
    private static readonly string OptiPatcherFileName = "OptiPatcher.asi";

    // ── DLSS DLL staging constants ────────────────────────────────────────────
    /// <summary>
    /// DLSS Swapper manifest hosted on GitHub — contains structured records with
    /// direct download URLs for every known DLSS DLL version.
    /// </summary>
    private const string DlssManifestUrl =
        "https://raw.githubusercontent.com/beeradmoore/dlss-swapper-manifest-builder/main/manifest.json";
    private static readonly string DlssStagingDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "dlss");
    private static readonly string DlssVersionPath = Path.Combine(DlssStagingDir, "version.txt");
    private static readonly string DlssdVersionPath = Path.Combine(DlssStagingDir, "version_dlssd.txt");
    private static readonly string DlssgVersionPath = Path.Combine(DlssStagingDir, "version_dlssg.txt");
    private const string DlssDllFileName = "nvngx_dlss.dll";
    private const string DlssdDllFileName = "nvngx_dlssd.dll";
    private const string DlssgDllFileName = "nvngx_dlssg.dll";

    /// <summary>
    /// Maps friendly key names (used in the Settings UI) to Windows Virtual Key Code
    /// hex strings (used by OptiScaler's ShortcutKey= INI setting).
    /// See: https://learn.microsoft.com/en-us/windows/win32/inputdev/virtual-key-codes
    /// </summary>
    public static readonly Dictionary<string, string> HotkeyNameToVkCode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Insert"]    = "0x2D",
        ["Delete"]    = "0x2E",
        ["Home"]      = "0x24",
        ["End"]       = "0x23",
        ["Page Up"]   = "0x21",
        ["Page Down"] = "0x22",
        ["F1"]        = "0x70",
        ["F2"]        = "0x71",
        ["F3"]        = "0x72",
        ["F4"]        = "0x73",
        ["F5"]        = "0x74",
        ["F6"]        = "0x75",
        ["F7"]        = "0x76",
        ["F8"]        = "0x77",
        ["F9"]        = "0x78",
        ["F10"]       = "0x79",
        ["F11"]       = "0x7A",
        ["F12"]       = "0x7B",
    };

    /// <summary>
    /// Converts a friendly key name to the VK code hex string for OptiScaler's INI.
    /// Returns the input unchanged if no mapping exists (allows raw hex codes to pass through).
    /// </summary>
    public static string ResolveHotkeyToVkCode(string friendlyName)
    {
        return HotkeyNameToVkCode.TryGetValue(friendlyName, out var vkCode) ? vkCode : friendlyName;
    }

    // ── Binary signature markers ──────────────────────────────────────────────
    // Unique strings embedded in OptiScaler.dll that distinguish it from ReShade
    // and other proxy DLLs. Checked via binary scan of the first ~2 MB.
    private static readonly byte[][] OptiScalerSignatures =
    [
        Encoding.ASCII.GetBytes("OptiScaler"),
    ];

    // ── Dependencies (injected via DI) ────────────────────────────────────────
    private readonly HttpClient _http;
    private readonly IAuxInstallService _auxInstaller;
    private readonly IDllOverrideService _dllOverrideService;
    private readonly GitHubETagCache _etagCache;
    private readonly Lazy<IDxvkService> _dxvkServiceLazy;
    private readonly Lazy<IDlssStreamlineService> _dlssStreamlineServiceLazy;

    // ── Backing fields ────────────────────────────────────────────────────────
    private bool _hasUpdate;
    private bool _hasUpdateNightly;
    private bool _firstTimeWarningAcknowledged;

    public OptiScalerService(
        HttpClient http,
        IAuxInstallService auxInstaller,
        IDllOverrideService dllOverrideService,
        GitHubETagCache etagCache,
        Lazy<IDxvkService> dxvkServiceLazy,
        Lazy<IDlssStreamlineService> dlssStreamlineServiceLazy)
    {
        _http = http;
        _auxInstaller = auxInstaller;
        _dllOverrideService = dllOverrideService;
        _etagCache = etagCache;
        _dxvkServiceLazy = dxvkServiceLazy;
        _dlssStreamlineServiceLazy = dlssStreamlineServiceLazy;
    }

    // ── Properties ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public bool IsStagingReady =>
        Directory.Exists(StagingDir)
        && File.Exists(VersionFilePath)
        && File.Exists(Path.Combine(StagingDir, "OptiScaler.dll"));

    /// <inheritdoc />
    public bool HasUpdate
    {
        get => _hasUpdate;
        private set => _hasUpdate = value;
    }

    /// <inheritdoc />
    public string? StagedVersion
    {
        get
        {
            try
            {
                return File.Exists(VersionFilePath)
                    ? File.ReadAllText(VersionFilePath).Trim()
                    : null;
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[OptiScalerService.StagedVersion] Failed to read version file — {ex.Message}");
                return null;
            }
        }
    }

    /// <inheritdoc />
    public bool FirstTimeWarningAcknowledged
    {
        get => _firstTimeWarningAcknowledged;
        set => _firstTimeWarningAcknowledged = value;
    }

    /// <inheritdoc />
    public bool IsStagingReadyNightly =>
        Directory.Exists(NightlyStagingDir)
        && File.Exists(NightlyVersionFilePath)
        && File.Exists(Path.Combine(NightlyStagingDir, "OptiScaler.dll"));

    /// <inheritdoc />
    public bool HasUpdateNightly
    {
        get => _hasUpdateNightly;
        private set => _hasUpdateNightly = value;
    }

    /// <inheritdoc />
    public string? StagedVersionNightly
    {
        get
        {
            try { return File.Exists(NightlyVersionFilePath) ? File.ReadAllText(NightlyVersionFilePath).Trim() : null; }
            catch { return null; }
        }
    }

    // ── DLSS NR variant ───────────────────────────────────────────────────────

    private static readonly string DlssNrStagingDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "optiscaler-dlssnr");
    private static readonly string DlssNrVersionFilePath = Path.Combine(DlssNrStagingDir, "version.txt");

    /// <summary>GitHub releases list URL for the DLSS NR OptiScaler fork.</summary>
    private const string DlssNrReleasesApi =
        "https://api.github.com/repos/wilsjo2/OptiScaler-DLSSNR-PreSR-Multipass/releases";

    /// <summary>The NR forwarder DLL that must be deployed alongside OptiScaler.dll for the DlssNr variant.</summary>
    public const string DlssNrForwarderName = "nvngx.dll_dlssnr.dll";

    /// <inheritdoc />
    public bool IsStagingReadyDlssNr =>
        Directory.Exists(DlssNrStagingDir)
        && File.Exists(DlssNrVersionFilePath)
        && File.Exists(Path.Combine(DlssNrStagingDir, "OptiScaler.dll"));

    private bool _hasUpdateDlssNr;

    /// <inheritdoc />
    public bool HasUpdateDlssNr
    {
        get => _hasUpdateDlssNr;
        private set => _hasUpdateDlssNr = value;
    }

    /// <inheritdoc />
    public string? StagedVersionDlssNr
    {
        get
        {
            try { return File.Exists(DlssNrVersionFilePath) ? File.ReadAllText(DlssNrVersionFilePath).Trim() : null; }
            catch { return null; }
        }
    }

}
