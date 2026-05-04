using System.Text;
using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Services;

/// <summary>
/// Manages DXVK lifecycle: download, staging, install, uninstall,
/// update detection, dxvk.conf management, binary signature detection,
/// OptiScaler coexistence, and variant selection.
/// Implemented as a partial class — staging, install, and tracking
/// logic live in separate partial files.
/// </summary>
public partial class DxvkService : IDxvkService
{
    // ── Constants ──────────────────────────────────────────────────────

    private static readonly string StagingDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "dxvk");

    private static readonly string VersionFilePath =
        Path.Combine(StagingDir, "version.txt");

    private static readonly string DbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "dxvk_installed.json");

    private static readonly string ConfTemplatePath = Path.Combine(
        AuxInstallService.InisDir, "dxvk.conf");

    private static readonly string NightlyLinkUrl =
        "https://nightly.link/doitsujin/dxvk/workflows/artifacts/master";

    private static readonly string StandardGitHubApi =
        "https://api.github.com/repos/doitsujin/dxvk/releases/latest";

    // ── Binary signature markers ──────────────────────────────────────
    // Unique strings embedded in DXVK DLLs that distinguish them from
    // other proxy DLLs. Checked via binary scan of the first ~2 MB.
    private static readonly byte[][] DxvkSignatures =
    [
        Encoding.ASCII.GetBytes("dxvk"),
        Encoding.ASCII.GetBytes("DXVK_"),
    ];

    // ── Dependencies (injected via DI) ────────────────────────────────
    private readonly HttpClient _http;
    private readonly IAuxInstallService _auxInstaller;
    private readonly IOptiScalerService _optiScalerService;
    private readonly GitHubETagCache _etagCache;

    // ── Backing fields ────────────────────────────────────────────────
    private bool _hasUpdate;
    private bool _firstTimeWarningAcknowledged;
    private DxvkVariant _selectedVariant = DxvkVariant.Development;

    public DxvkService(
        HttpClient http,
        IAuxInstallService auxInstaller,
        IOptiScalerService optiScalerService,
        GitHubETagCache etagCache)
    {
        _http = http;
        _auxInstaller = auxInstaller;
        _optiScalerService = optiScalerService;
        _etagCache = etagCache;
    }

    // ── Properties ────────────────────────────────────────────────────

    /// <inheritdoc />
    public bool IsStagingReady =>
        CheckStagingReady(StagingDir, VersionFilePath, Path.Combine(StagingDir, "x64", "d3d9.dll"));

    /// <summary>
    /// Pure logic for <see cref="IsStagingReady"/>: returns true only when
    /// the staging directory exists, the version file exists, and the
    /// reference DLL file exists. Extracted as a static helper for testability.
    /// </summary>
    internal static bool CheckStagingReady(string stagingDir, string versionFilePath, string dllCheckPath) =>
        Directory.Exists(stagingDir)
        && File.Exists(versionFilePath)
        && File.Exists(dllCheckPath);

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
                CrashReporter.Log($"[DxvkService.StagedVersion] Failed to read version file — {ex.Message}");
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
    public DxvkVariant SelectedVariant
    {
        get => _selectedVariant;
        set => _selectedVariant = value;
    }

    // ── Pure helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Returns the architecture subfolder and the list of DXVK DLL filenames
    /// required for the given graphics API and bitness.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="api"/> is not one of DX8/DX9/DX10/DX11.
    /// </exception>
    public static (string archFolder, List<string> dllNames) DetermineRequiredDlls(
        GraphicsApiType api, bool is32Bit)
    {
        var arch = is32Bit ? "x32" : "x64";
        var dlls = api switch
        {
            GraphicsApiType.DirectX8  => new List<string> { "d3d8.dll" },
            GraphicsApiType.DirectX9  => new List<string> { "d3d9.dll" },
            GraphicsApiType.DirectX10 => new List<string> { "d3d10core.dll", "dxgi.dll" },
            GraphicsApiType.DirectX11 => new List<string> { "d3d11.dll", "dxgi.dll" },
            _ => throw new InvalidOperationException(
                $"DXVK does not support {api}. Only DX8/9/10/11 are supported.")
        };
        return (arch, dlls);
    }

    /// <summary>
    /// Validates that the given path is a safe deployment target for DXVK DLLs.
    /// Rejects paths under protected system directories:
    /// <c>%SystemRoot%</c>, <c>%SystemRoot%\System32</c>,
    /// <c>%SystemRoot%\SysWOW64</c>, and <c>%ProgramFiles%\WindowsApps</c>.
    /// </summary>
    /// <param name="path">The candidate deployment path to validate.</param>
    /// <returns>
    /// <c>true</c> if the path is a valid game directory outside protected
    /// locations; <c>false</c> if the path is null, empty, or under a
    /// protected system directory.
    /// </returns>
    public static bool IsValidDeploymentPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        string normalizedPath;
        try
        {
            // Expand any environment variables (e.g. %SystemRoot%) and normalize
            var expanded = Environment.ExpandEnvironmentVariables(path);
            normalizedPath = Path.GetFullPath(expanded).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            // Invalid path characters or other path parsing failures
            return false;
        }

        // Build the list of protected directory prefixes
        var systemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        var protectedPaths = new[]
        {
            systemRoot,                                                     // %SystemRoot%
            Path.Combine(systemRoot, "System32"),                           // %SystemRoot%\System32
            Path.Combine(systemRoot, "SysWOW64"),                           // %SystemRoot%\SysWOW64
            Path.Combine(programFiles, "WindowsApps"),                      // %ProgramFiles%\WindowsApps
        };

        foreach (var protectedDir in protectedPaths)
        {
            if (string.IsNullOrEmpty(protectedDir))
                continue;

            var normalizedProtected = protectedDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // Reject if the path IS the protected directory or is a subdirectory of it
            if (string.Equals(normalizedPath, normalizedProtected, StringComparison.OrdinalIgnoreCase))
                return false;

            if (normalizedPath.StartsWith(normalizedProtected + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    // ── OptiScaler coexistence ────────────────────────────────────────

    /// <summary>
    /// Partitions the required DXVK DLLs into those that go to the game
    /// root directory and those that must be routed to the OptiScaler
    /// plugins folder due to a filename conflict with OptiScaler.
    /// </summary>
    /// <param name="requiredDlls">DLL filenames that DXVK needs to deploy.</param>
    /// <param name="installPath">The game's resolved install directory.</param>
    /// <returns>
    /// A tuple of (rootDlls, pluginDlls) where rootDlls go to the game
    /// directory and pluginDlls go to OptiScaler/plugins/.
    /// </returns>
    internal (List<string> rootDlls, List<string> pluginDlls) ResolveDeploymentPaths(
        List<string> requiredDlls, string installPath)
    {
        var rootDlls = new List<string>();
        var pluginDlls = new List<string>();

        // Check if OptiScaler is installed and get its filename
        var osInstalledFile = _optiScalerService.DetectInstallation(installPath);

        foreach (var dll in requiredDlls)
        {
            if (osInstalledFile != null &&
                string.Equals(dll, osInstalledFile, StringComparison.OrdinalIgnoreCase))
            {
                // Filename conflict — route to OptiScaler plugins folder
                pluginDlls.Add(dll);
            }
            else
            {
                rootDlls.Add(dll);
            }
        }

        return (rootDlls, pluginDlls);
    }

    // ── Stub methods (implemented in partial files) ───────────────────
    // Real implementations live in:
    //   DxvkService.Staging.cs   — EnsureStagingAsync, CheckForUpdateAsync, ClearStaging
    //   DxvkService.Install.cs   — InstallAsync, Uninstall, UpdateAsync, CopyConfToGame, DetectInstallation
    //   DxvkService.Tracking.cs  — LoadAllRecords, FindRecord, SaveRecord, RemoveRecord

    /// <inheritdoc />
    public bool IsDxvkFile(string filePath)
    {
        return IsDxvkFileStatic(filePath);
    }

    /// <summary>
    /// Static version of <see cref="IsDxvkFile"/> for use by the foreign DLL
    /// protection system.
    /// Reads the first ~2 MB of a DLL file and scans for DXVK binary signatures.
    /// </summary>
    public static bool IsDxvkFileStatic(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return false;

            using var stream = File.OpenRead(filePath);
            var bufferSize = (int)Math.Min(stream.Length, 2 * 1024 * 1024);
            var buffer = new byte[bufferSize];
            int totalRead = 0;
            while (totalRead < bufferSize)
            {
                int read = stream.Read(buffer, totalRead, bufferSize - totalRead);
                if (read == 0) break;
                totalRead += read;
            }

            foreach (var signature in DxvkSignatures)
            {
                if (ContainsSequence(buffer, totalRead, signature))
                    return true;
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DxvkService.IsDxvkFile] Error scanning '{filePath}' — {ex.Message}");
        }
        return false;
    }

    /// <summary>
    /// Searches for a byte sequence within a buffer using a simple sliding-window scan.
    /// </summary>
    private static bool ContainsSequence(byte[] buffer, int bufferLength, byte[] sequence)
    {
        if (sequence.Length == 0 || bufferLength < sequence.Length) return false;
        var limit = bufferLength - sequence.Length;
        for (int i = 0; i <= limit; i++)
        {
            bool match = true;
            for (int j = 0; j < sequence.Length; j++)
            {
                if (buffer[i + j] != sequence[j])
                {
                    match = false;
                    break;
                }
            }
            if (match) return true;
        }
        return false;
    }
}
