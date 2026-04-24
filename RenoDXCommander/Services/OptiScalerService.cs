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
        "dxgi.dll", "winmm.dll", "d3d12.dll", "dbghelp.dll",
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

    private static readonly string GitHubReleasesApi =
        "https://api.github.com/repos/optiscaler/OptiScaler/releases/latest";

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

    // ── Backing fields ────────────────────────────────────────────────────────
    private bool _hasUpdate;
    private bool _firstTimeWarningAcknowledged;

    public OptiScalerService(
        HttpClient http,
        IAuxInstallService auxInstaller,
        IDllOverrideService dllOverrideService,
        GitHubETagCache etagCache)
    {
        _http = http;
        _auxInstaller = auxInstaller;
        _dllOverrideService = dllOverrideService;
        _etagCache = etagCache;
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

}
