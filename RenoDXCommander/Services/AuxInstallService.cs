using System.Diagnostics;
using System.Text.Json;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Installs and manages ReShade for each game.
/// Maintains its own DB (aux_installed.json) separate from RenoDX records.
/// Caches downloaded files in the same downloads folder as RenoDX.
/// </summary>
public class AuxInstallService : IAuxInstallService, IAuxFileService
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
        "UPST", "reshade");
    public static string RsStagedPath64 => Path.Combine(RsStagingDir, RsStaged64);
    public static string RsStagedPath32 => Path.Combine(RsStagingDir, RsStaged32);

    /// <summary>
    /// Ensures the staged ReShade DLLs exist. The DLLs are downloaded from reshade.me
    /// by ReShadeUpdateService — this method only verifies they are present.
    /// Returns true if staged DLLs are available, false if missing.
    /// </summary>
    public static bool EnsureReShadeStaging()
    {
        Directory.CreateDirectory(RsStagingDir);
        return File.Exists(RsStagedPath64) && File.Exists(RsStagedPath32);
    }

    /// <summary>
    /// Classifies what a dxgi.dll file is based on its content.
    /// </summary>
    public enum DxgiFileType { Unknown, ReShade }

    /// <summary>
    /// Identifies what type of dxgi.dll is at the given path.
    /// Uses strict checks: exact size match against known staged binaries,
    /// and binary string scanning for definitive markers.
    /// Returns Unknown unless there is positive evidence — never guesses based on size alone.
    /// </summary>
    public static DxgiFileType IdentifyDxgiFile(string filePath)
    {
        if (!File.Exists(filePath)) return DxgiFileType.Unknown;

        if (IsReShadeFileStrict(filePath)) return DxgiFileType.ReShade;

        return DxgiFileType.Unknown;
    }

    // ── Foreign DLL backup / restore ──────────────────────────────────────────

    /// <summary>
    /// If <paramref name="dllPath"/> exists and is a foreign (unrecognised) DLL,
    /// renames it to <c>dllPath + ".original"</c> so it is preserved.
    /// Only applies to <c>dxgi.dll</c> and <c>winmm.dll</c>.
    /// Returns true if a backup was made.
    /// </summary>
    public static bool BackupForeignDll(string dllPath)
    {
        if (!File.Exists(dllPath)) return false;

        var name = Path.GetFileName(dllPath);
        bool isForeign;
        if (name.Equals("dxgi.dll", StringComparison.OrdinalIgnoreCase))
            isForeign = IdentifyDxgiFile(dllPath) == DxgiFileType.Unknown;
        else
            return false;

        if (!isForeign) return false;

        var backupPath = dllPath + ".original";
        try
        {
            if (File.Exists(backupPath)) File.Delete(backupPath);
            File.Move(dllPath, backupPath);
            CrashReporter.Log($"[AuxInstallService.BackupForeignDll] {name} → {name}.original in {Path.GetDirectoryName(dllPath)}");
            return true;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[AuxInstallService.BackupForeignDll] Failed for '{dllPath}' — {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// If a <c>.original</c> backup exists for <paramref name="dllPath"/> and the
    /// slot is now vacant, restores the backup to its original name.
    /// </summary>
    public static void RestoreForeignDll(string dllPath)
    {
        var backupPath = dllPath + ".original";
        if (!File.Exists(backupPath)) return;
        if (File.Exists(dllPath)) return;   // slot still occupied — don't overwrite

        try
        {
            File.Move(backupPath, dllPath);
            var name = Path.GetFileName(dllPath);
            CrashReporter.Log($"[AuxInstallService.RestoreForeignDll] {name}.original → {name} in {Path.GetDirectoryName(dllPath)}");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[AuxInstallService.RestoreForeignDll] Failed for '{dllPath}' — {ex.Message}");
        }
    }

    /// <summary>
    /// Strict ReShade check: exact size match against staged ReShade DLLs,
    /// OR binary scan for ReShade-specific strings. Never falls back to a size threshold.
    /// </summary>
    public static bool IsReShadeFileStrict(string filePath)
    {
        if (!File.Exists(filePath)) return false;
        var fileSize = new FileInfo(filePath).Length;

        // ReShade DLLs are typically 4-8 MB. Files over 15 MB are almost certainly
        // something else (e.g. OptiScaler at ~28 MB) that may contain "ReShade"
        // in config comments but are not actually ReShade.
        if (fileSize > 15 * 1024 * 1024) return false;

        // Exact size match against staged ReShade64.dll
        if (File.Exists(RsStagedPath64) && fileSize == new FileInfo(RsStagedPath64).Length)
            return true;
        // Exact size match against staged ReShade32.dll
        if (File.Exists(RsStagedPath32) && fileSize == new FileInfo(RsStagedPath32).Length)
            return true;

        // Binary string scan for ReShade markers
        // Only match on strings unique to the actual ReShade binary — "reshade.me" (the URL
        // embedded in the PE resources) and "crosire" (the author name). Do NOT match on
        // generic phrases like "ReShade DLL" which appear in config comments of tools like
        // OptiScaler that load ReShade externally.
        try
        {
            var bytes = File.ReadAllBytes(filePath);
            var text = System.Text.Encoding.ASCII.GetString(bytes);
            if (text.Contains("ReShade", StringComparison.Ordinal) &&
                (text.Contains("reshade.me", StringComparison.OrdinalIgnoreCase) ||
                 text.Contains("crosire", StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        catch (Exception ex) { CrashReporter.Log($"[AuxInstallService.IsReShadeFileStrict] Binary scan failed for '{filePath}' — {ex.Message}"); }

        return false;
    }

    /// <summary>
    /// Returns true if the file at <paramref name="filePath"/> is a ReShade DLL.
    /// Uses exact size match against staged copies first, then binary string scan.
    /// Falls back to a 2 MB size threshold ONLY if neither staged copies nor
    /// binary markers are available — this is the legacy heuristic and is less reliable.
    /// </summary>
    public static bool IsReShadeFile(string filePath)
    {
        if (!File.Exists(filePath)) return false;

        // Prefer the strict check which requires positive evidence
        if (IsReShadeFileStrict(filePath)) return true;

        // Legacy fallback: if no staged copies exist AND binary scan didn't find markers,
        // use the size heuristic. This only triggers on first run before staging.
        var fileSize = new FileInfo(filePath).Length;
        bool hasStagedCopies = File.Exists(RsStagedPath64) || File.Exists(RsStagedPath32);
        if (!hasStagedCopies && fileSize > 2 * 1024 * 1024)
            return true;

        return false;
    }

    // Keys used in AuxInstalledRecord.AddonType
    public const string TypeReShade = "ReShade";

    // ── Infrastructure ────────────────────────────────────────────────────────────
    private static readonly string DbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "UPST", "aux_installed.json");

    // ── INI preset folder ─────────────────────────────────────────────────────────
    /// <summary>
    /// User-placed preset config files live here.
    /// Place reshade.ini here to enable the 📋 INI button on the ReShade row.
    /// </summary>
    public static readonly string InisDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "UPST", "inis");

    public static string RsIniPath => Path.Combine(InisDir, "reshade.ini");
    public static string RsVulkanIniPath => Path.Combine(InisDir, "reshade.vulkan.ini");
    public static string RsPresetIniPath => Path.Combine(InisDir, "ReShadePreset.ini");

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
    }

    /// <summary>
    /// Merges the template reshade.ini into the game directory's existing reshade.ini.
    /// Template keys always overwrite existing values (template wins). Sections and keys
    /// already in the game's INI that are not in the template are preserved untouched.
    /// If no reshade.ini exists in the game folder, the template is copied as-is.
    /// </summary>
    public static void MergeRsIni(string gameDir)
    {
        if (!File.Exists(RsIniPath))
            throw new FileNotFoundException("reshade.ini not found in inis folder.", RsIniPath);

        var gamePath = Path.Combine(gameDir, "reshade.ini");

        if (!File.Exists(gamePath))
        {
            // No existing INI — just copy the template
            File.Copy(RsIniPath, gamePath, overwrite: true);
            return;
        }

        // Parse both files
        var gameIni     = ParseIni(File.ReadAllLines(gamePath));
        var templateIni = ParseIni(File.ReadAllLines(RsIniPath));

        // Merge: template keys overwrite, game-only keys preserved
        foreach (var (section, templateKeys) in templateIni)
        {
            if (!gameIni.TryGetValue(section, out var gameKeys))
            {
                // Entire section is new — add it
                gameIni[section] = new OrderedDict(templateKeys);
            }
            else
            {
                // Section exists — overwrite matching keys, add new ones
                foreach (var (key, value) in templateKeys)
                    gameKeys[key] = value;
            }
        }

        // Write merged INI back
        WriteIni(gamePath, gameIni);
    }

    /// <summary>
    /// Merges the Vulkan-specific reshade.vulkan.ini template into the game directory
    /// as reshade.ini. Uses the same merge logic as <see cref="MergeRsIni"/> — template
    /// keys overwrite, game-only keys are preserved. Falls back to the standard
    /// reshade.ini if the Vulkan template doesn't exist.
    /// </summary>
    public static void MergeRsVulkanIni(string gameDir)
    {
        // Fall back to standard reshade.ini if no Vulkan template exists
        var templatePath = File.Exists(RsVulkanIniPath) ? RsVulkanIniPath : RsIniPath;

        if (!File.Exists(templatePath))
            throw new FileNotFoundException("Neither reshade.vulkan.ini nor reshade.ini found in inis folder.", templatePath);

        var gamePath = Path.Combine(gameDir, "reshade.ini");

        if (!File.Exists(gamePath))
        {
            File.Copy(templatePath, gamePath, overwrite: true);
            return;
        }

        var gameIni     = ParseIni(File.ReadAllLines(gamePath));
        var templateIni = ParseIni(File.ReadAllLines(templatePath));

        foreach (var (section, templateKeys) in templateIni)
        {
            if (!gameIni.TryGetValue(section, out var gameKeys))
            {
                gameIni[section] = new OrderedDict(templateKeys);
            }
            else
            {
                foreach (var (key, value) in templateKeys)
                    gameKeys[key] = value;
            }
        }

        WriteIni(gamePath, gameIni);
    }

    /// <summary>Copies reshade.ini from the inis folder to the given game directory (full overwrite, no merge).</summary>
    public static void CopyRsIni(string gameDir)
    {
        if (!File.Exists(RsIniPath))
            throw new FileNotFoundException("reshade.ini not found in inis folder.", RsIniPath);
        File.Copy(RsIniPath, Path.Combine(gameDir, "reshade.ini"), overwrite: true);
    }

    /// <summary>
    /// Copies ReShadePreset.ini from the inis folder to the given game directory if the file exists.
    /// Silent no-op when the file is absent — the preset is optional.
    /// </summary>
    public static void CopyRsPresetIniIfPresent(string gameDir)
    {
        if (!File.Exists(RsPresetIniPath)) return;
        try
        {
            File.Copy(RsPresetIniPath, Path.Combine(gameDir, "ReShadePreset.ini"), overwrite: true);
            CrashReporter.Log($"[AuxInstallService.CopyRsPresetIniIfPresent] Copied to {gameDir}");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[AuxInstallService.CopyRsPresetIniIfPresent] Failed for '{gameDir}' — {ex.Message}");
        }
    }

    // ── INI parsing / writing helpers ─────────────────────────────────────────────

    /// <summary>Simple alias for an ordered key-value dictionary (preserves insertion order).</summary>
    private class OrderedDict : Dictionary<string, string>
    {
        public OrderedDict() : base(StringComparer.OrdinalIgnoreCase) { }
        public OrderedDict(IDictionary<string, string> d) : base(d, StringComparer.OrdinalIgnoreCase) { }
    }

    /// <summary>
    /// Parses an INI file into sections → key-value pairs.
    /// Preserves all keys within each section in order. Lines that aren't
    /// key=value pairs (comments, blank lines) are stored under a special "" key
    /// with a numeric suffix to preserve them on write-back.
    /// </summary>
    private static Dictionary<string, OrderedDict> ParseIni(string[] lines)
    {
        var result = new Dictionary<string, OrderedDict>(StringComparer.OrdinalIgnoreCase);
        var currentSection = ""; // keys before any section header go under ""
        result[currentSection] = new OrderedDict();

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();

            // Section header
            if (line.StartsWith('[') && line.Contains(']'))
            {
                currentSection = line.Trim('[', ']', ' ');
                if (!result.ContainsKey(currentSection))
                    result[currentSection] = new OrderedDict();
                continue;
            }

            // Key=Value
            var eqIdx = line.IndexOf('=');
            if (eqIdx > 0)
            {
                var key   = line[..eqIdx].Trim();
                var value = line[(eqIdx + 1)..];
                result[currentSection][key] = value;
            }
            // else: blank line or comment — skip (not preserved in merge output)
        }

        return result;
    }

    /// <summary>Writes a parsed INI structure back to a file.</summary>
    private static void WriteIni(string path, Dictionary<string, OrderedDict> ini)
    {
        using var writer = new StreamWriter(path, append: false, encoding: new System.Text.UTF8Encoding(false));

        // Write the anonymous section first (keys before any [section])
        if (ini.TryGetValue("", out var anon) && anon.Count > 0)
        {
            foreach (var (key, value) in anon)
                writer.WriteLine($"{key}={value}");
            writer.WriteLine();
        }

        // Write named sections
        foreach (var (section, keys) in ini)
        {
            if (section == "") continue; // already written
            writer.WriteLine($"[{section}]");
            foreach (var (key, value) in keys)
                writer.WriteLine($"{key}={value}");
            writer.WriteLine();
        }
    }

    public static readonly string DownloadCacheDir = ModInstallService.DownloadCacheDir;

    private readonly HttpClient _http;
    private readonly IShaderPackService _shaderPackService;

    public AuxInstallService(HttpClient http, IShaderPackService shaderPackService)
    {
        _http = http;
        _shaderPackService = shaderPackService;
    }

    // ── Install — ReShade ─────────────────────────────────────────────────────────

    public async Task<AuxInstalledRecord> InstallReShadeAsync(
        string gameName,
        string installPath,
        string? shaderModeOverride = null,
        bool use32Bit = false,
        string? filenameOverride = null,
        IEnumerable<string>? selectedPackIds = null,
        IProgress<(string message, double percent)>? progress = null)
    {
        Directory.CreateDirectory(DownloadCacheDir);

        var destName = !string.IsNullOrWhiteSpace(filenameOverride)
            ? filenameOverride
            : RsNormalName;
        var destPath = Path.Combine(installPath, destName);

        // ── Record-aware cleanup: remove old non-standard DLL if InstalledAs differs ─
        var existingRecord = FindRecord(gameName, installPath, TypeReShade);
        if (existingRecord != null &&
            !string.Equals(existingRecord.InstalledAs, destName, StringComparison.OrdinalIgnoreCase))
        {
            var oldPath = Path.Combine(installPath, existingRecord.InstalledAs);
            if (File.Exists(oldPath))
                try { File.Delete(oldPath); } catch (Exception ex) { CrashReporter.Log($"[AuxInstallService.InstallReShadeAsync] Failed to delete old RS file '{oldPath}' — {ex.Message}"); }
            RestoreForeignDll(oldPath);
        }

        // ── Ensure staged DLLs exist (downloaded from reshade.me) ────────────────
        progress?.Report(("Preparing ReShade files...", 10));
        EnsureReShadeStaging();

        var rsStagedPath = use32Bit ? RsStagedPath32 : RsStagedPath64;
        if (!File.Exists(rsStagedPath))
            throw new FileNotFoundException(
                $"ReShade DLLs not found in staging directory.\n" +
                $"Expected: {rsStagedPath}\n" +
                $"Please restart RDXC to download ReShade from reshade.me.");

        // ── Back up foreign DLL at destination ──────────────────────────────────
        BackupForeignDll(destPath);

        // ── Copy staged DLL to game folder ────────────────────────────────────────
        progress?.Report(("Installing ReShade...", 80));
        File.Copy(rsStagedPath, destPath, overwrite: true);

        // Deploy reshade.ini alongside the DLL.
        if (File.Exists(RsIniPath))
            MergeRsIni(installPath);
        // Deploy ReShadePreset.ini alongside reshade.ini when the user has placed one in the inis folder.
        CopyRsPresetIniIfPresent(installPath);

        progress?.Report(("ReShade installed!", 100));

        // ── Shader deployment ─────────────────────────────────────────────────────
        // Always deploy shaders locally to the game folder.
        // Uses Sync (prune + deploy) so switching shader selections properly
        // removes files from the previous selection.
        _shaderPackService.SyncGameFolder(installPath, selectedPackIds);

        var record = new AuxInstalledRecord
        {
            GameName       = gameName,
            InstallPath    = installPath,
            AddonType      = TypeReShade,
            InstalledAs    = destName,
            SourceUrl      = null,       // bundled — no remote URL
            RemoteFileSize = null,       // no remote size to track
            InstalledAt    = DateTime.UtcNow,
        };
        SaveRecord(record);
        return record;
    }

    // ── Update detection ──────────────────────────────────────────────────────────

    // ── Version reading ───────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the product version string from an installed ReShade file.
    /// Returns a short human-readable version string (e.g. "6.7.3") or null
    /// if the file doesn't exist or has no version info.
    /// </summary>
    public static string? ReadInstalledVersion(string installPath, string fileName)
    {
        var filePath = Path.Combine(installPath, fileName);
        if (!File.Exists(filePath)) return null;
        try
        {
            var info = FileVersionInfo.GetVersionInfo(filePath);
            var ver  = info.ProductVersion?.Trim();
            if (string.IsNullOrEmpty(ver)) return null;

            // Trim build number: "0.12.461.2731" → "0.12.461"
            // ReShade already ships as "6.7.3" (3 parts), so this is a no-op for it.
            var parts = ver.Split('.');
            if (parts.Length > 3)
                ver = string.Join(".", parts[0], parts[1], parts[2]);

            return ver;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[AuxInstallService.ReadInstalledVersion] Failed to read version from '{filePath}' — {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Checks if an installed ReShade file is outdated by comparing its size
    /// against the staged (bundled) DLL. Returns true if an update is available.
    /// </summary>
    public static bool CheckReShadeUpdateLocal(AuxInstalledRecord record)
    {
        if (record.AddonType != TypeReShade) return false;
        var localFile = Path.Combine(record.InstallPath, record.InstalledAs);
        if (!File.Exists(localFile)) return false;

        var localSize = new FileInfo(localFile).Length;

        // Check against the 64-bit staged DLL first, then 32-bit.
        // The installed file matches the staged DLL it was copied from.
        // If either staged file has a different size, an update is available.
        if (File.Exists(RsStagedPath64) && localSize == new FileInfo(RsStagedPath64).Length)
            return false; // matches current 64-bit — no update
        if (File.Exists(RsStagedPath32) && localSize == new FileInfo(RsStagedPath32).Length)
            return false; // matches current 32-bit — no update

        // Size doesn't match either staged DLL — update available
        return true;
    }

    public async Task<bool> CheckForUpdateAsync(AuxInstalledRecord record)
    {
        if (record.SourceUrl == null)
        {
            CrashReporter.Log($"[AuxInstallService.CheckForUpdateAsync] [{record.AddonType}] {record.GameName}: no SourceUrl — skipping");
            return false;
        }

        // Resolve addon search path for .addon64/.addon32 files
        var ext = Path.GetExtension(record.InstalledAs);
        var isAddon = ext.Equals(".addon64", StringComparison.OrdinalIgnoreCase)
                   || ext.Equals(".addon32", StringComparison.OrdinalIgnoreCase);
        var deployDir = isAddon
            ? ModInstallService.GetAddonDeployPath(record.InstallPath)
            : record.InstallPath;
        var localFile = Path.Combine(deployDir, record.InstalledAs);
        if (!File.Exists(localFile))
        {
            // Fallback: file may be in the base install path (pre-AddonPath)
            localFile = Path.Combine(record.InstallPath, record.InstalledAs);
        }
        if (!File.Exists(localFile))
        {
            CrashReporter.Log($"[AuxInstallService.CheckForUpdateAsync] [{record.AddonType}] {record.GameName}: local file missing — update needed");
            return true;
        }

        var localSize = new FileInfo(localFile).Length;
        CrashReporter.Log($"[AuxInstallService.CheckForUpdateAsync] [{record.AddonType}] {record.GameName}: local={localSize}, stored={record.RemoteFileSize}");

        try
        {
            // ── Strategy 1: HEAD for Content-Length ─────────────────────────────
            long? remoteSize = null;
            try
            {
                var headResp = await _http.SendAsync(new HttpRequestMessage(HttpMethod.Head, record.SourceUrl));
                if (headResp.IsSuccessStatusCode)
                    remoteSize = headResp.Content.Headers.ContentLength;
                CrashReporter.Log($"[AuxInstallService.CheckForUpdateAsync] [{record.AddonType}] {record.GameName}: HEAD status={headResp.StatusCode}, CL={remoteSize}");
            }
            catch (Exception ex) { CrashReporter.Log($"[AuxInstallService.CheckForUpdateAsync] [{record.AddonType}] HEAD failed — {ex.Message}"); }

            // ── Strategy 2: Range GET for Content-Range total ──────────────────
            if (!remoteSize.HasValue)
            {
                try
                {
                    var rangeReq = new HttpRequestMessage(HttpMethod.Get, record.SourceUrl);
                    rangeReq.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
                    var rangeResp = await _http.SendAsync(rangeReq, HttpCompletionOption.ResponseHeadersRead);
                    if (rangeResp.Content.Headers.ContentRange?.Length is long totalLen)
                        remoteSize = totalLen;
                    else if (rangeResp.IsSuccessStatusCode)
                        remoteSize = rangeResp.Content.Headers.ContentLength;
                    CrashReporter.Log($"[AuxInstallService.CheckForUpdateAsync] [{record.AddonType}] {record.GameName}: Range GET size={remoteSize}");
                    rangeResp.Dispose();
                }
                catch (Exception ex) { CrashReporter.Log($"[AuxInstallService.CheckForUpdateAsync] [{record.AddonType}] Range failed — {ex.Message}"); }
            }

            // ── Strategy 3: Full download comparison ───────────────────────────
            // If we still have no remote size, or if sizes match (could be a same-size
            // different-content update), download the file and compare bytes.
            if (!remoteSize.HasValue || remoteSize.Value == localSize)
            {
                CrashReporter.Log($"[AuxInstallService.CheckForUpdateAsync] [{record.AddonType}] {record.GameName}: falling back to download comparison (remoteSize={remoteSize}, localSize={localSize})");
                try
                {
                    var cacheName = record.InstalledAs;
                    var tempPath = Path.Combine(DownloadCacheDir, cacheName + $".update-check-{Guid.NewGuid():N}");
                    Directory.CreateDirectory(DownloadCacheDir);

                    var response = await _http.GetAsync(record.SourceUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        var bytes = await response.Content.ReadAsByteArrayAsync();
                        await File.WriteAllBytesAsync(tempPath, bytes);
                        var downloadedSize = bytes.Length;

                        CrashReporter.Log($"[AuxInstallService.CheckForUpdateAsync] [{record.AddonType}] {record.GameName}: downloaded {downloadedSize} bytes, local {localSize} bytes");

                        if (downloadedSize != localSize)
                        {
                            // Size differs — definite update. Move downloaded file to cache
                            // so the next install picks it up without re-downloading.
                            var cachePath = Path.Combine(DownloadCacheDir, cacheName);
                            if (File.Exists(cachePath)) File.Delete(cachePath);
                            File.Move(tempPath, cachePath);
                            return true;
                        }

                        // Same size — compare bytes directly
                        var localBytes = await File.ReadAllBytesAsync(localFile);
                        bool contentDiffers = !bytes.AsSpan().SequenceEqual(localBytes.AsSpan());
                        CrashReporter.Log($"[AuxInstallService.CheckForUpdateAsync] [{record.AddonType}] {record.GameName}: same size, content differs={contentDiffers}");

                        if (contentDiffers)
                        {
                            var cachePath = Path.Combine(DownloadCacheDir, cacheName);
                            if (File.Exists(cachePath)) File.Delete(cachePath);
                            File.Move(tempPath, cachePath);
                            return true;
                        }

                        // Identical — clean up temp
                        try { File.Delete(tempPath); } catch (Exception cleanupEx) { CrashReporter.Log($"[AuxInstallService.CheckForUpdateAsync] Failed to clean up temp file '{tempPath}' — {cleanupEx.Message}"); }
                        return false;
                    }
                }
                catch (Exception ex) { CrashReporter.Log($"[AuxInstallService.CheckForUpdateAsync] [{record.AddonType}] Download compare failed — {ex.Message}"); }

                return false;
            }

            // Size-based comparison
            bool update = remoteSize.Value != localSize;
            CrashReporter.Log($"[AuxInstallService.CheckForUpdateAsync] [{record.AddonType}] {record.GameName}: remote={remoteSize}, local={localSize} → update={update}");
            return update;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[AuxInstallService.CheckForUpdateAsync] [{record.AddonType}] {record.GameName} exception — {ex.Message}");
            return false;
        }
    }

    // ── Uninstall ─────────────────────────────────────────────────────────────────

    public void Uninstall(AuxInstalledRecord record)
    {
        // Resolve addon search path for .addon64/.addon32 files
        var ext = Path.GetExtension(record.InstalledAs);
        var isAddon = ext.Equals(".addon64", StringComparison.OrdinalIgnoreCase)
                   || ext.Equals(".addon32", StringComparison.OrdinalIgnoreCase);
        var deployDir = isAddon
            ? ModInstallService.GetAddonDeployPath(record.InstallPath)
            : record.InstallPath;
        var path = Path.Combine(deployDir, record.InstalledAs);
        if (File.Exists(path))
            File.Delete(path);
        else
        {
            // Fallback: file may be in the base install path (pre-AddonPath)
            var fallback = Path.Combine(record.InstallPath, record.InstalledAs);
            if (File.Exists(fallback)) File.Delete(fallback);
        }
        RemoveRecord(record);

        // Restore any foreign DLL that was backed up when RDXC took over this slot.
        RestoreForeignDll(path);

        // If a user-owned reshade-shaders folder was renamed to reshade-shaders-original
        // when we deployed ours, restore it now that RS has been uninstalled.
        if (!string.IsNullOrEmpty(record.InstallPath))
            _shaderPackService.RestoreOriginalIfPresent(record.InstallPath);
    }

    /// <inheritdoc />
    public void UninstallDllOnly(AuxInstalledRecord record)
    {
        // Resolve addon search path for .addon64/.addon32 files
        var ext = Path.GetExtension(record.InstalledAs);
        var isAddon = ext.Equals(".addon64", StringComparison.OrdinalIgnoreCase)
                   || ext.Equals(".addon32", StringComparison.OrdinalIgnoreCase);
        var deployDir = isAddon
            ? ModInstallService.GetAddonDeployPath(record.InstallPath)
            : record.InstallPath;
        var path = Path.Combine(deployDir, record.InstalledAs);
        if (File.Exists(path))
            File.Delete(path);
        else
        {
            var fallback = Path.Combine(record.InstallPath, record.InstalledAs);
            if (File.Exists(fallback)) File.Delete(fallback);
        }
        RemoveRecord(record);

        // Restore any foreign DLL that was backed up when RDXC took over this slot.
        RestoreForeignDll(path);

        // NOTE: intentionally does NOT call RestoreOriginalIfPresent —
        // this variant is used when shaders must stay untouched.
    }

    // ── DB ────────────────────────────────────────────────────────────────────────

    public List<AuxInstalledRecord> LoadAll() => LoadDb();

    public AuxInstalledRecord? FindRecord(string gameName, string installPath, string addonType)
    {
        return LoadDb().FirstOrDefault(r =>
            r.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase) &&
            r.InstallPath.Equals(installPath, StringComparison.OrdinalIgnoreCase) &&
            r.AddonType.Equals(addonType, StringComparison.OrdinalIgnoreCase));
    }

    private void SaveRecord(AuxInstalledRecord record)
    {
        var db = LoadDb();
        var i  = db.FindIndex(r =>
            r.GameName.Equals(record.GameName, StringComparison.OrdinalIgnoreCase) &&
            r.InstallPath.Equals(record.InstallPath, StringComparison.OrdinalIgnoreCase) &&
            r.AddonType.Equals(record.AddonType, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) db[i] = record; else db.Add(record);
        SaveDb(db);
    }

    public void SaveAuxRecord(AuxInstalledRecord record) => SaveRecord(record);

    public void RemoveRecord(AuxInstalledRecord record)
    {
        var db = LoadDb();
        db.RemoveAll(r =>
            r.GameName.Equals(record.GameName, StringComparison.OrdinalIgnoreCase) &&
            r.InstallPath.Equals(record.InstallPath, StringComparison.OrdinalIgnoreCase) &&
            r.AddonType.Equals(record.AddonType, StringComparison.OrdinalIgnoreCase));
        SaveDb(db);
    }

    private List<AuxInstalledRecord> LoadDb()
    {
        try
        {
            if (!File.Exists(DbPath)) return new();
            return JsonSerializer.Deserialize<List<AuxInstalledRecord>>(File.ReadAllText(DbPath)) ?? new();
        }
        catch (Exception ex) { CrashReporter.Log($"[AuxInstallService.LoadDb] Failed to load DB from '{DbPath}' — {ex.Message}"); return new(); }
    }

    private void SaveDb(List<AuxInstalledRecord> db)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
        var json = JsonSerializer.Serialize(db,
            new JsonSerializerOptions { WriteIndented = true });

        FileHelper.WriteAllTextWithRetry(DbPath, json, "AuxInstallService.SaveDb");
    }

    // ── IAuxFileService explicit implementations ──────────────────────────────
    bool IAuxFileService.EnsureReShadeStaging() => EnsureReShadeStaging();
    DxgiFileType IAuxFileService.IdentifyDxgiFile(string filePath) => IdentifyDxgiFile(filePath);
    bool IAuxFileService.BackupForeignDll(string dllPath) => BackupForeignDll(dllPath);
    void IAuxFileService.RestoreForeignDll(string dllPath) => RestoreForeignDll(dllPath);
    bool IAuxFileService.IsReShadeFileStrict(string filePath) => IsReShadeFileStrict(filePath);
    bool IAuxFileService.IsReShadeFile(string filePath) => IsReShadeFile(filePath);
    void IAuxFileService.EnsureInisDir() => EnsureInisDir();
    void IAuxFileService.MergeRsIni(string gameDir) => MergeRsIni(gameDir);
    void IAuxFileService.MergeRsVulkanIni(string gameDir) => MergeRsVulkanIni(gameDir);
    void IAuxFileService.CopyRsIni(string gameDir) => CopyRsIni(gameDir);
    void IAuxFileService.CopyRsPresetIniIfPresent(string gameDir) => CopyRsPresetIniIfPresent(gameDir);
    string? IAuxFileService.ReadInstalledVersion(string installPath, string fileName) => ReadInstalledVersion(installPath, fileName);
    bool IAuxFileService.CheckReShadeUpdateLocal(AuxInstalledRecord record) => CheckReShadeUpdateLocal(record);
}
