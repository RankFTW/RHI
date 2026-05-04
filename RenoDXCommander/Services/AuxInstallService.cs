// AuxInstallService.cs — Core scaffolding: constructor, constants, staging paths, INI/DB paths, and IAuxFileService implementations
using System.Diagnostics;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Installs and manages ReShade for each game.
/// Maintains its own DB (aux_installed.json) separate from RenoDX records.
/// Caches downloaded files in the same downloads folder as RenoDX.
/// </summary>
public partial class AuxInstallService : IAuxInstallService, IAuxFileService
{
    // ── URLs & filenames ──────────────────────────────────────────────────────────

    // ReShade is bundled alongside the app exe
    // On first install the DLLs are copied into the staging folder and used from there.
    // If the staging copies are deleted, they are restored from the app bundle automatically.
    public const string RsNormalName  = "dxgi.dll";       // standard install name
    public const string RsStaged64    = "ReShade64.dll";  // filename inside staging folder
    public const string RsStaged32    = "ReShade32.dll";

    // Staging folder: %LocalAppData%\RenoDXCommander\reshade\
    public static readonly string RsStagingDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "reshade");
    public static string RsStagedPath64 => Path.Combine(RsStagingDir, RsStaged64);
    public static string RsStagedPath32 => Path.Combine(RsStagingDir, RsStaged32);

    // Normal (non-addon) ReShade staging folder: %LocalAppData%\RHI\reshade-normal\
    public static readonly string RsNormalStagingDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "reshade-normal");
    public static string RsNormalStagedPath64 => Path.Combine(RsNormalStagingDir, RsStaged64);
    public static string RsNormalStagedPath32 => Path.Combine(RsNormalStagingDir, RsStaged32);

    /// <summary>
    /// Ensures the staged ReShade DLLs exist. The DLLs are downloaded from reshade.me
    /// by ReShadeUpdateService — this method only verifies they are present.
    /// Returns true if staged DLLs are available, false if missing.
    /// </summary>
    /// <summary>Minimum valid size for a ReShade DLL (1 MB). Real DLLs are 4-5 MB+.</summary>
    public const long MinReShadeSize = 1_000_000;

    public static bool EnsureReShadeStaging()
    {
        Directory.CreateDirectory(RsStagingDir);
        // Verify both DLLs exist AND are large enough to be valid.
        // A corrupted/truncated file (e.g. 2-3 KB) must not pass this check.
        return File.Exists(RsStagedPath64) && new FileInfo(RsStagedPath64).Length > MinReShadeSize
            && File.Exists(RsStagedPath32) && new FileInfo(RsStagedPath32).Length > MinReShadeSize;
    }

    // Keys used in AuxInstalledRecord.AddonType
    public const string TypeReShade = "ReShade";
    public const string TypeReShadeNormal = "ReShadeNormal";

    // ── Infrastructure ────────────────────────────────────────────────────────────
    private static readonly string DbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "aux_installed.json");

    // ── INI preset folder ─────────────────────────────────────────────────────────
    /// <summary>
    /// User-placed preset config files live here.
    /// Place reshade.ini here to enable the 📋 INI button on the ReShade row.
    /// </summary>
    public static readonly string InisDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "inis");

    public static string RsIniPath => Path.Combine(InisDir, "reshade.ini");
    public static string RsVulkanIniPath => Path.Combine(InisDir, "reshade.vulkan.ini");
    public static string RsPresetIniPath => Path.Combine(InisDir, "ReShadePreset.ini");
    public static string RsRdr2IniPath => Path.Combine(InisDir, "reshade.rdr2.ini");
    public static string UlIniPath => Path.Combine(InisDir, "relimiter.ini");
    public static string DcIniPath => Path.Combine(InisDir, "DisplayCommander.ini");
    public static string DxvkConfPath => Path.Combine(InisDir, "dxvk.conf");

    /// <summary>
    /// Ensures the inis directory exists and seeds the default reshade.ini if missing.
    /// The bundled ReShade.ini is copied from the app directory on first run or whenever
    /// the file is absent — an existing user-modified reshade.ini is never overwritten.
    /// </summary>
    public static void EnsureInisDir()
    {
        Directory.CreateDirectory(InisDir);

        // Seed bundled reshade.ini if the user doesn't already have one
        if (!File.Exists(RsIniPath))
        {
            var bundled = Path.Combine(AppContext.BaseDirectory, "ReShade.ini");
            if (File.Exists(bundled))
            {
                try
                {
                    File.Copy(bundled, RsIniPath, overwrite: false);
                    CrashReporter.Log("[AuxInstallService.EnsureInisDir] Seeded default reshade.ini from bundle");
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[AuxInstallService.EnsureInisDir] Failed to seed reshade.ini — {ex.Message}");
                }
            }
        }

        // Seed bundled reshade.vulkan.ini if the user doesn't already have one
        if (!File.Exists(RsVulkanIniPath))
        {
            var bundledVulkan = Path.Combine(AppContext.BaseDirectory, "reshade.vulkan.ini");
            if (File.Exists(bundledVulkan))
            {
                try
                {
                    File.Copy(bundledVulkan, RsVulkanIniPath, overwrite: false);
                    CrashReporter.Log("[AuxInstallService.EnsureInisDir] Seeded default reshade.vulkan.ini from bundle");
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[AuxInstallService.EnsureInisDir] Failed to seed reshade.vulkan.ini — {ex.Message}");
                }
            }
        }

        // Seed bundled reshade.rdr2.ini if the user doesn't already have one
        if (!File.Exists(RsRdr2IniPath))
        {
            var bundledRdr2 = Path.Combine(AppContext.BaseDirectory, "reshade.rdr2.ini");
            if (File.Exists(bundledRdr2))
            {
                try
                {
                    File.Copy(bundledRdr2, RsRdr2IniPath, overwrite: false);
                    CrashReporter.Log("[AuxInstallService.EnsureInisDir] Seeded default reshade.rdr2.ini from bundle");
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[AuxInstallService.EnsureInisDir] Failed to seed reshade.rdr2.ini — {ex.Message}");
                }
            }
        }

        // Seed bundled relimiter.ini if the user doesn't already have one
        if (!File.Exists(UlIniPath))
        {
            var bundledUl = Path.Combine(AppContext.BaseDirectory, "relimiter.ini");
            if (File.Exists(bundledUl))
            {
                try
                {
                    File.Copy(bundledUl, UlIniPath, overwrite: false);
                    CrashReporter.Log("[AuxInstallService.EnsureInisDir] Seeded default relimiter.ini from bundle");
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[AuxInstallService.EnsureInisDir] Failed to seed relimiter.ini — {ex.Message}");
                }
            }
        }

        // Seed bundled DisplayCommander.ini if the user doesn't already have one
        if (!File.Exists(DcIniPath))
        {
            var bundledDc = Path.Combine(AppContext.BaseDirectory, "DisplayCommander.ini");
            if (File.Exists(bundledDc))
            {
                try
                {
                    File.Copy(bundledDc, DcIniPath, overwrite: false);
                    CrashReporter.Log("[AuxInstallService.EnsureInisDir] Seeded default DisplayCommander.ini from bundle");
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[AuxInstallService.EnsureInisDir] Failed to seed DisplayCommander.ini — {ex.Message}");
                }
            }
        }

        // Seed bundled dxvk.conf if the user doesn't already have one
        if (!File.Exists(DxvkConfPath))
        {
            var bundledDxvk = Path.Combine(AppContext.BaseDirectory, "dxvk.conf");
            if (File.Exists(bundledDxvk))
            {
                try
                {
                    File.Copy(bundledDxvk, DxvkConfPath, overwrite: false);
                    CrashReporter.Log("[AuxInstallService.EnsureInisDir] Seeded default dxvk.conf from bundle");
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[AuxInstallService.EnsureInisDir] Failed to seed dxvk.conf — {ex.Message}");
                }
            }
        }
    }

    // ── Directory name sanitization ──────────────────────────────────────────────

    /// <summary>
    /// Strips characters that are invalid in Windows directory names from the input.
    /// Invalid chars: &lt; &gt; : " / | ? *
    /// </summary>
    public static string SanitizeDirectoryName(string name)
    {
        char[] invalid = { '<', '>', ':', '"', '/', '|', '?', '*' };
        return string.Concat(name.Where(c => !invalid.Contains(c)));
    }

    // ── Version reading ───────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the product version string from an installed file.
    /// For RenoDX addons (.addon64/.addon32), returns the full version with the
    /// leading "0." stripped (e.g. "0.12.461.2731" → "12.461.2731").
    /// For ReShade and other files, returns a short version (e.g. "6.7.3").
    /// Returns null if the file doesn't exist or has no version info.
    /// </summary>
    public static string? ReadInstalledVersion(string installPath, string fileName)
    {
        var filePath = Path.Combine(installPath, fileName);
        if (!File.Exists(filePath))
        {
            CrashReporter.Log($"[AuxInstallService.ReadInstalledVersion] File not found: '{filePath}'");
            return null;
        }
        try
        {
            var info = FileVersionInfo.GetVersionInfo(filePath);
            var ver  = info.ProductVersion?.Trim();
            if (string.IsNullOrEmpty(ver))
                ver = info.FileVersion?.Trim();
            if (string.IsNullOrEmpty(ver))
            {
                // No PE version resources — fall back to file last-modified date
                // formatted like RenoDX versions: "YY.MMDD.HHMM"
                var lastWrite = File.GetLastWriteTimeUtc(filePath);
                ver = $"{lastWrite:yy}.{lastWrite:MMdd}.{lastWrite:HHmm}";
                CrashReporter.Log($"[AuxInstallService.ReadInstalledVersion] No PE version in '{filePath}', using file date: {ver}");
                return ver;
            }

            var parts = ver.Split('.');
            var ext = Path.GetExtension(fileName);
            var isAddon = ext.Equals(".addon64", StringComparison.OrdinalIgnoreCase)
                       || ext.Equals(".addon32", StringComparison.OrdinalIgnoreCase);

            if (isAddon)
            {
                // RenoDX version format: "0.YYYY.MMDD.HHMM" — drop the leading "0.",
                // trim the year to 2 digits, keep month/day and hour/minute.
                // Example: "0.2026.0325.2215" → "26.0325.2215"
                if (parts.Length >= 2 && parts[0] == "0")
                {
                    var remaining = parts.Skip(1).ToArray();
                    // Trim 4-digit year to last 2 digits
                    if (remaining.Length > 0 && remaining[0].Length == 4)
                        remaining[0] = remaining[0].Substring(2);
                    ver = string.Join(".", remaining);
                }
            }
            else
            {
                // ReShade already ships as "6.7.3" (3 parts), so this is a no-op for it.
                // For other files with 4+ parts, trim to 3.
                if (parts.Length > 3)
                    ver = string.Join(".", parts[0], parts[1], parts[2]);

                // Nightly ReShade builds embed "UNOFFICIAL" in the PE version string.
                // Replace with "Nightly" for a cleaner display.
                if (ver.Contains("UNOFFICIAL", StringComparison.OrdinalIgnoreCase))
                    ver = ver.Replace("UNOFFICIAL", "Nightly", StringComparison.OrdinalIgnoreCase).Trim();
            }

            return ver;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[AuxInstallService.ReadInstalledVersion] Failed to read version from '{filePath}' — {ex.Message}");
            return null;
        }
    }

    private readonly HttpClient _http;
    private readonly IShaderPackService _shaderPackService;

    public AuxInstallService(HttpClient http, IShaderPackService shaderPackService)
    {
        _http = http;
        _shaderPackService = shaderPackService;
    }

    // ── IAuxFileService explicit implementations ──────────────────────────────
    bool IAuxFileService.EnsureReShadeStaging() => EnsureReShadeStaging();
    DxgiFileType IAuxFileService.IdentifyDxgiFile(string filePath) => IdentifyDxgiFile(filePath);
    bool IAuxFileService.BackupForeignDll(string dllPath) => BackupForeignDll(dllPath);
    void IAuxFileService.RestoreForeignDll(string dllPath) => RestoreForeignDll(dllPath);
    bool IAuxFileService.IsReShadeFileStrict(string filePath) => IsReShadeFileStrict(filePath);
    bool IAuxFileService.IsReShadeFile(string filePath) => IsReShadeFile(filePath);
    void IAuxFileService.EnsureInisDir() => EnsureInisDir();
    void IAuxFileService.MergeRsIni(string gameDir, string? screenshotSavePath, string? overlayHotkey, string? screenshotHotkey) => MergeRsIni(gameDir, screenshotSavePath, overlayHotkey, screenshotHotkey);
    void IAuxFileService.MergeRsVulkanIni(string gameDir, string? gameName, string? screenshotSavePath, string? overlayHotkey, string? screenshotHotkey) => MergeRsVulkanIni(gameDir, gameName, screenshotSavePath, overlayHotkey, screenshotHotkey);
    void IAuxFileService.CopyRsIni(string gameDir) => CopyRsIni(gameDir);
    void IAuxFileService.CopyRsPresetIniIfPresent(string gameDir) => CopyRsPresetIniIfPresent(gameDir);
    string? IAuxFileService.ReadInstalledVersion(string installPath, string fileName) => ReadInstalledVersion(installPath, fileName);
    bool IAuxFileService.CheckReShadeUpdateLocal(AuxInstalledRecord record) => CheckReShadeUpdateLocal(record);
}
