using System.Diagnostics;
using System.Text;
using System.Text.Json;
using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Services;

/// <summary>
/// Manages OptiScaler lifecycle: download, staging, install, uninstall,
/// update detection, INI management, and ReShade coexistence.
/// </summary>
public class OptiScalerService : IOptiScalerService
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

    // ── Backing fields ────────────────────────────────────────────────────────
    private bool _hasUpdate;
    private bool _firstTimeWarningAcknowledged;

    public OptiScalerService(
        HttpClient http,
        IAuxInstallService auxInstaller,
        IDllOverrideService dllOverrideService)
    {
        _http = http;
        _auxInstaller = auxInstaller;
        _dllOverrideService = dllOverrideService;
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

    // ── Staging and update ────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task EnsureStagingAsync(IProgress<(string message, double percent)>? progress = null)
    {
        try
        {
            // ── 1. Skip if staging is already valid ──────────────────────────────
            if (IsStagingReady)
            {
                CrashReporter.Log("[OptiScalerService.EnsureStagingAsync] Staging already valid — skipping download");
                progress?.Report(("OptiScaler staging ready", 100));
                return;
            }

            progress?.Report(("Checking OptiScaler release...", 5));

            // ── 2. Fetch latest release metadata from GitHub API ─────────────────
            string json;
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Get, GitHubReleasesApi);
                req.Headers.Add("User-Agent", "RHI");
                req.Headers.Add("Accept", "application/vnd.github+json");
                var resp = await _http.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                {
                    CrashReporter.Log($"[OptiScalerService.EnsureStagingAsync] GitHub API returned {resp.StatusCode}");
                    return;
                }
                json = await resp.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[OptiScalerService.EnsureStagingAsync] GitHub API request failed — {ex.Message}");
                return;
            }

            // ── 3. Parse release — find the .7z asset and tag ────────────────────
            string? tagName = null;
            string? assetName = null;
            string? downloadUrl = null;
            try
            {
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("tag_name", out var tagEl))
                    tagName = tagEl.GetString();

                if (doc.RootElement.TryGetProperty("assets", out var assets))
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        var name = asset.GetProperty("name").GetString() ?? "";
                        if (name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
                        {
                            assetName = name;
                            downloadUrl = asset.GetProperty("browser_download_url").GetString();
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[OptiScalerService.EnsureStagingAsync] Failed to parse GitHub response — {ex.Message}");
                return;
            }

            if (assetName == null || downloadUrl == null)
            {
                CrashReporter.Log("[OptiScalerService.EnsureStagingAsync] No .7z asset found in latest release");
                return;
            }

            // ── 4. Check if already up to date ──────────────────────────────────
            var cachedVersion = StagedVersion;
            if (cachedVersion != null
                && string.Equals(cachedVersion, tagName, StringComparison.Ordinal)
                && IsStagingReady)
            {
                CrashReporter.Log($"[OptiScalerService.EnsureStagingAsync] Already up to date ({tagName})");
                progress?.Report(("OptiScaler up to date", 100));
                return;
            }

            progress?.Report(($"Downloading OptiScaler ({assetName})...", 10));
            CrashReporter.Log($"[OptiScalerService.EnsureStagingAsync] Downloading {assetName} from {downloadUrl}");

            // ── 5. Download the .7z archive to a temp file ──────────────────────
            Directory.CreateDirectory(StagingDir);
            var tempArchive = Path.Combine(StagingDir, assetName + ".tmp");

            try
            {
                var dlResp = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                if (!dlResp.IsSuccessStatusCode)
                {
                    CrashReporter.Log($"[OptiScalerService.EnsureStagingAsync] Download failed ({dlResp.StatusCode})");
                    return;
                }

                var total = dlResp.Content.Headers.ContentLength ?? -1L;
                long downloaded = 0;
                var buf = new byte[1024 * 1024]; // 1 MB

                using (var net = await dlResp.Content.ReadAsStreamAsync())
                using (var file = new FileStream(tempArchive, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1024 * 1024, useAsync: true))
                {
                    int read;
                    while ((read = await net.ReadAsync(buf)) > 0)
                    {
                        await file.WriteAsync(buf.AsMemory(0, read));
                        downloaded += read;
                        if (total > 0)
                        {
                            var pct = 10 + (double)downloaded / total * 60; // 10–70%
                            progress?.Report(($"Downloading OptiScaler... {downloaded / 1024} KB / {total / 1024} KB", pct));
                        }
                    }
                }

                CrashReporter.Log($"[OptiScalerService.EnsureStagingAsync] Downloaded {downloaded} bytes");
            }
            catch (Exception ex)
            {
                if (File.Exists(tempArchive)) try { File.Delete(tempArchive); } catch { }
                CrashReporter.Log($"[OptiScalerService.EnsureStagingAsync] Download exception — {ex.Message}");
                return;
            }

            // ── 6. Extract the .7z archive to staging using bundled 7z.exe ──────
            progress?.Report(("Extracting OptiScaler...", 75));
            try
            {
                var sevenZipExe = Find7ZipExe();
                if (sevenZipExe == null)
                {
                    CrashReporter.Log("[OptiScalerService.EnsureStagingAsync] 7-Zip not found — cannot extract archive");
                    if (File.Exists(tempArchive)) try { File.Delete(tempArchive); } catch { }
                    return;
                }

                // Extract to a temp directory first, then move contents to staging
                var tempExtractDir = Path.Combine(Path.GetTempPath(), $"RHI_optiscaler_{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempExtractDir);

                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = sevenZipExe,
                        Arguments = $"x \"{tempArchive}\" -o\"{tempExtractDir}\" -y",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                    };

                    CrashReporter.Log($"[OptiScalerService.EnsureStagingAsync] Running {psi.FileName} {psi.Arguments}");

                    using var proc = Process.Start(psi);
                    if (proc == null)
                    {
                        CrashReporter.Log("[OptiScalerService.EnsureStagingAsync] Failed to start 7z process");
                        return;
                    }

                    var stdoutTask = proc.StandardOutput.ReadToEndAsync();
                    var stderrTask = proc.StandardError.ReadToEndAsync();
                    proc.WaitForExit(120_000); // 120 second timeout for ~53 MB archive

                    var stderr = await stderrTask;
                    if (!string.IsNullOrWhiteSpace(stderr))
                        CrashReporter.Log($"[OptiScalerService.EnsureStagingAsync] 7z stderr: {stderr}");

                    if (proc.ExitCode != 0)
                    {
                        CrashReporter.Log($"[OptiScalerService.EnsureStagingAsync] 7z exit code {proc.ExitCode}");
                        return;
                    }

                    // The archive may contain a top-level folder — find where OptiScaler.dll lives
                    var dllCandidates = Directory.GetFiles(tempExtractDir, "OptiScaler.dll", SearchOption.AllDirectories);
                    if (dllCandidates.Length == 0)
                    {
                        CrashReporter.Log("[OptiScalerService.EnsureStagingAsync] OptiScaler.dll not found in extracted archive");
                        return;
                    }

                    var sourceDir = Path.GetDirectoryName(dllCandidates[0])!;

                    // Clear existing staging contents before copying new files
                    foreach (var existingFile in Directory.GetFiles(StagingDir))
                    {
                        try { File.Delete(existingFile); } catch { }
                    }

                    // Copy all files from the source directory to staging
                    foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
                    {
                        var relativePath = Path.GetRelativePath(sourceDir, file);
                        var destPath = Path.Combine(StagingDir, relativePath);
                        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                        File.Copy(file, destPath, overwrite: true);
                    }

                    CrashReporter.Log($"[OptiScalerService.EnsureStagingAsync] Extracted to staging from {sourceDir}");
                }
                finally
                {
                    try { Directory.Delete(tempExtractDir, recursive: true); } catch (Exception ex) { CrashReporter.Log($"[OptiScalerService.EnsureStagingAsync] Failed to clean up temp dir — {ex.Message}"); }
                }
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[OptiScalerService.EnsureStagingAsync] Extraction failed — {ex.Message}");
                return;
            }
            finally
            {
                // Clean up the downloaded archive
                if (File.Exists(tempArchive)) try { File.Delete(tempArchive); } catch { }
            }

            // ── 7. Write version tag to version.txt ─────────────────────────────
            try
            {
                File.WriteAllText(VersionFilePath, tagName ?? "unknown");
                CrashReporter.Log($"[OptiScalerService.EnsureStagingAsync] Version tag written: {tagName}");
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[OptiScalerService.EnsureStagingAsync] Failed to write version file — {ex.Message}");
            }

            progress?.Report(("OptiScaler staging ready", 100));
            CrashReporter.Log("[OptiScalerService.EnsureStagingAsync] Staging complete");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[OptiScalerService.EnsureStagingAsync] Unexpected error — {ex.Message}");
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Finds 7z.exe on the system. Checks the bundled copy next to the app exe first,
    /// then common install locations, then PATH.
    /// </summary>
    private static string? Find7ZipExe()
    {
        // Check bundled 7z.exe next to the app exe first
        var bundled = Path.Combine(AppContext.BaseDirectory, "7z.exe");
        if (File.Exists(bundled))
            return bundled;

        // Check common install locations
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "7-Zip", "7z.exe"),
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
                return path;
        }

        // Check PATH
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "7z.exe",
                Arguments = "--help",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                proc.WaitForExit(5000);
                return "7z.exe";
            }
        }
        catch { }

        return null;
    }

    /// <inheritdoc />
    public async Task CheckForUpdateAsync()
    {
        try
        {
            // ── 1. Fetch latest release tag from GitHub API ──────────────────
            string json;
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Get, GitHubReleasesApi);
                req.Headers.Add("User-Agent", "RHI");
                req.Headers.Add("Accept", "application/vnd.github+json");
                var resp = await _http.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                {
                    CrashReporter.Log($"[OptiScalerService.CheckForUpdateAsync] GitHub API returned {resp.StatusCode}");
                    return;
                }
                json = await resp.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[OptiScalerService.CheckForUpdateAsync] GitHub API request failed — {ex.Message}");
                return;
            }

            // ── 2. Extract tag_name from the response ────────────────────────
            string? remoteTag = null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("tag_name", out var tagEl))
                    remoteTag = tagEl.GetString();
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[OptiScalerService.CheckForUpdateAsync] Failed to parse GitHub response — {ex.Message}");
                return;
            }

            if (string.IsNullOrEmpty(remoteTag))
            {
                CrashReporter.Log("[OptiScalerService.CheckForUpdateAsync] No tag_name found in latest release");
                return;
            }

            // ── 3. Compare with cached version tag (case-sensitive) ──────────
            var cachedTag = StagedVersion;
            HasUpdate = !string.Equals(cachedTag, remoteTag, StringComparison.Ordinal);

            CrashReporter.Log($"[OptiScalerService.CheckForUpdateAsync] Cached={cachedTag ?? "(none)"}, Remote={remoteTag}, HasUpdate={HasUpdate}");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[OptiScalerService.CheckForUpdateAsync] Unexpected error — {ex.Message}");
        }
    }

    /// <inheritdoc />
    public void ClearStaging()
    {
        try
        {
            if (!Directory.Exists(StagingDir))
                return;

            // Delete all files in the staging directory
            foreach (var file in Directory.GetFiles(StagingDir, "*", SearchOption.AllDirectories))
            {
                try { File.Delete(file); }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[OptiScalerService.ClearStaging] Failed to delete file '{file}' — {ex.Message}");
                }
            }

            // Delete all subdirectories in the staging directory
            foreach (var dir in Directory.GetDirectories(StagingDir))
            {
                try { Directory.Delete(dir, recursive: true); }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[OptiScalerService.ClearStaging] Failed to delete directory '{dir}' — {ex.Message}");
                }
            }

            CrashReporter.Log("[OptiScalerService.ClearStaging] Staging folder cleared");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[OptiScalerService.ClearStaging] Unexpected error — {ex.Message}");
        }
    }

    // ── OptiPatcher staging and update ────────────────────────────────────────

    /// <summary>
    /// Downloads the latest OptiPatcher.asi from the rolling release to the staging folder.
    /// No-op if staging is already valid and up to date.
    /// </summary>
    public async Task EnsureOptiPatcherStagingAsync(IProgress<(string message, double percent)>? progress = null)
    {
        try
        {
            progress?.Report(("Checking OptiPatcher release...", 0));

            // ── 1. Fetch rolling release metadata from GitHub API ────────────
            string json;
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Get, OptiPatcherReleasesApi);
                req.Headers.Add("User-Agent", "RHI");
                req.Headers.Add("Accept", "application/vnd.github+json");
                var resp = await _http.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                {
                    CrashReporter.Log($"[OptiScalerService.EnsureOptiPatcherStagingAsync] GitHub API returned {resp.StatusCode}");
                    return;
                }
                json = await resp.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[OptiScalerService.EnsureOptiPatcherStagingAsync] GitHub API request failed — {ex.Message}");
                return;
            }

            // ── 2. Parse release — extract version from body and find .asi asset ─
            string? version = null;
            string? downloadUrl = null;
            try
            {
                using var doc = JsonDocument.Parse(json);

                // Extract version from body text: "Base version: vX.XX"
                if (doc.RootElement.TryGetProperty("body", out var bodyEl))
                {
                    var body = bodyEl.GetString() ?? "";
                    var match = System.Text.RegularExpressions.Regex.Match(body, @"Base version:\s*v?([\d.]+)");
                    if (match.Success)
                        version = "v" + match.Groups[1].Value;
                }

                if (doc.RootElement.TryGetProperty("assets", out var assets))
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        var name = asset.GetProperty("name").GetString() ?? "";
                        if (name.Equals(OptiPatcherFileName, StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = asset.GetProperty("browser_download_url").GetString();
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[OptiScalerService.EnsureOptiPatcherStagingAsync] Failed to parse GitHub response — {ex.Message}");
                return;
            }

            if (downloadUrl == null)
            {
                CrashReporter.Log("[OptiScalerService.EnsureOptiPatcherStagingAsync] No OptiPatcher.asi asset found in rolling release");
                return;
            }

            version ??= "unknown";

            // ── 3. Check if already up to date ──────────────────────────────
            var cachedVersion = File.Exists(OptiPatcherVersionPath)
                ? File.ReadAllText(OptiPatcherVersionPath).Trim()
                : null;
            var stagedAsiPath = Path.Combine(OptiPatcherStagingDir, OptiPatcherFileName);

            if (cachedVersion != null
                && string.Equals(cachedVersion, version, StringComparison.Ordinal)
                && File.Exists(stagedAsiPath))
            {
                CrashReporter.Log($"[OptiScalerService.EnsureOptiPatcherStagingAsync] Already up to date ({version})");
                progress?.Report(("OptiPatcher up to date", 100));
                return;
            }

            progress?.Report(($"Downloading OptiPatcher ({version})...", 30));
            CrashReporter.Log($"[OptiScalerService.EnsureOptiPatcherStagingAsync] Downloading {OptiPatcherFileName} from {downloadUrl}");

            // ── 4. Download the .asi file directly ──────────────────────────
            Directory.CreateDirectory(OptiPatcherStagingDir);
            try
            {
                var dlResp = await _http.GetAsync(downloadUrl);
                if (!dlResp.IsSuccessStatusCode)
                {
                    CrashReporter.Log($"[OptiScalerService.EnsureOptiPatcherStagingAsync] Download failed ({dlResp.StatusCode})");
                    return;
                }

                var bytes = await dlResp.Content.ReadAsByteArrayAsync();
                await File.WriteAllBytesAsync(stagedAsiPath, bytes);
                CrashReporter.Log($"[OptiScalerService.EnsureOptiPatcherStagingAsync] Downloaded {bytes.Length} bytes");
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[OptiScalerService.EnsureOptiPatcherStagingAsync] Download exception — {ex.Message}");
                return;
            }

            // ── 5. Write version to version.txt ─────────────────────────────
            try
            {
                File.WriteAllText(OptiPatcherVersionPath, version);
                CrashReporter.Log($"[OptiScalerService.EnsureOptiPatcherStagingAsync] Version tag written: {version}");
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[OptiScalerService.EnsureOptiPatcherStagingAsync] Failed to write version file — {ex.Message}");
            }

            progress?.Report(("OptiPatcher staging ready", 100));
            CrashReporter.Log("[OptiScalerService.EnsureOptiPatcherStagingAsync] Staging complete");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[OptiScalerService.EnsureOptiPatcherStagingAsync] Unexpected error — {ex.Message}");
        }
    }

    /// <summary>
    /// Checks the GitHub releases API for a newer OptiPatcher version than the staged one.
    /// </summary>
    public async Task<bool> CheckOptiPatcherUpdateAsync()
    {
        try
        {
            string json;
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Get, OptiPatcherReleasesApi);
                req.Headers.Add("User-Agent", "RHI");
                req.Headers.Add("Accept", "application/vnd.github+json");
                var resp = await _http.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                {
                    CrashReporter.Log($"[OptiScalerService.CheckOptiPatcherUpdateAsync] GitHub API returned {resp.StatusCode}");
                    return false;
                }
                json = await resp.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[OptiScalerService.CheckOptiPatcherUpdateAsync] GitHub API request failed — {ex.Message}");
                return false;
            }

            string? remoteVersion = null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("body", out var bodyEl))
                {
                    var body = bodyEl.GetString() ?? "";
                    var match = System.Text.RegularExpressions.Regex.Match(body, @"Base version:\s*v?([\d.]+)");
                    if (match.Success)
                        remoteVersion = "v" + match.Groups[1].Value;
                }
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[OptiScalerService.CheckOptiPatcherUpdateAsync] Failed to parse GitHub response — {ex.Message}");
                return false;
            }

            if (string.IsNullOrEmpty(remoteVersion))
            {
                CrashReporter.Log("[OptiScalerService.CheckOptiPatcherUpdateAsync] No version found in rolling release body");
                return false;
            }

            var cachedVersion = File.Exists(OptiPatcherVersionPath)
                ? File.ReadAllText(OptiPatcherVersionPath).Trim()
                : null;
            var hasUpdate = !string.Equals(cachedVersion, remoteVersion, StringComparison.Ordinal);

            CrashReporter.Log($"[OptiScalerService.CheckOptiPatcherUpdateAsync] Cached={cachedVersion ?? "(none)"}, Remote={remoteVersion}, HasUpdate={hasUpdate}");
            return hasUpdate;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[OptiScalerService.CheckOptiPatcherUpdateAsync] Unexpected error — {ex.Message}");
            return false;
        }
    }

    // ── Install / Uninstall / Update ──────────────────────────────────────────

    /// <inheritdoc />
    /// <summary>
    /// Returns the path to the bundled OptiScaler INI template that matches
    /// the current GPU type and DLSS input settings.
    /// </summary>
    public static string GetBundledIniPath(string gpuType, bool dlssInputs)
    {
        var fileName = gpuType.Equals("NVIDIA", StringComparison.OrdinalIgnoreCase)
            ? "OptiScaler.nvidia.ini"
            : dlssInputs
                ? "OptiScaler.amd-dlss.ini"
                : "OptiScaler.amd-nodlss.ini";
        return Path.Combine(AppContext.BaseDirectory, fileName);
    }

    public async Task<AuxInstalledRecord?> InstallAsync(
        GameCardViewModel card,
        IProgress<(string message, double percent)>? progress = null,
        string gpuType = "NVIDIA",
        bool dlssInputs = true,
        string? hotkey = null)
    {
        try
        {
            // ── 1. First-time warning check ──────────────────────────────────
            if (!FirstTimeWarningAcknowledged)
            {
                // The actual dialog is wired in the UI layer; here we just
                // record the acknowledgement so it is only shown once.
                FirstTimeWarningAcknowledged = true;
            }

            progress?.Report(("Preparing OptiScaler install...", 5));

            // ── 2. Validate staging ──────────────────────────────────────────
            if (!IsStagingReady)
            {
                CrashReporter.Log("[OptiScalerService.InstallAsync] Staging not ready — attempting download");
                await EnsureStagingAsync(progress);
                if (!IsStagingReady)
                {
                    CrashReporter.Log("[OptiScalerService.InstallAsync] Staging still not ready after download attempt — aborting");
                    progress?.Report(("OptiScaler staging not available", 0));
                    return null;
                }
            }

            // ── 3. Resolve effective DLL name ────────────────────────────────
            var effectiveDllName = _dllOverrideService.GetEffectiveOsName(card.GameName);
            CrashReporter.Log($"[OptiScalerService.InstallAsync] {card.GameName}: effective DLL name = {effectiveDllName}");

            progress?.Report(("Copying OptiScaler files...", 20));

            // ── 4. ReShade coexistence — rename RS DLL to ReShade64.dll BEFORE deploying files ──
            // This MUST happen before file deployment because OptiScaler may use the same
            // DLL name as ReShade (e.g. dxgi.dll). If we deploy first, OptiScaler overwrites
            // ReShade, and the backup saves the game's original dxgi.dll instead of ReShade.
            try
            {
                // Check tracking record first
                var rsRecord = _auxInstaller.FindRecord(card.GameName, card.InstallPath, AuxInstallService.TypeReShade)
                            ?? _auxInstaller.FindRecord(card.GameName, card.InstallPath, AuxInstallService.TypeReShadeNormal);

                string? rsFilePath = null;
                if (rsRecord != null)
                {
                    var candidatePath = Path.Combine(card.InstallPath, rsRecord.InstalledAs);
                    if (File.Exists(candidatePath))
                        rsFilePath = candidatePath;
                }

                // If no record or file not found, scan for known ReShade DLL names
                if (rsFilePath == null)
                {
                    foreach (var dllName in DllOverrideConstants.CommonDllNames)
                    {
                        var candidatePath = Path.Combine(card.InstallPath, dllName);
                        if (File.Exists(candidatePath) && _auxInstaller is IAuxFileService auxFile && auxFile.IsReShadeFile(candidatePath))
                        {
                            rsFilePath = candidatePath;
                            break;
                        }
                    }
                }

                if (rsFilePath != null)
                {
                    var rsCurrentName = Path.GetFileName(rsFilePath);
                    var rsDestPath = Path.Combine(card.InstallPath, ReShadeCoexistName);

                    // Only rename if not already named ReShade64.dll
                    if (!rsCurrentName.Equals(ReShadeCoexistName, StringComparison.OrdinalIgnoreCase))
                    {
                        // If a file already exists at the destination, delete it first (stale leftover)
                        if (File.Exists(rsDestPath))
                            File.Delete(rsDestPath);

                        File.Move(rsFilePath, rsDestPath);
                        CrashReporter.Log($"[OptiScalerService.InstallAsync] Renamed ReShade '{rsCurrentName}' → '{ReShadeCoexistName}'");
                    }

                    // Update ReShade tracking record
                    if (rsRecord != null)
                    {
                        rsRecord.InstalledAs = ReShadeCoexistName;
                        _auxInstaller.SaveAuxRecord(rsRecord);
                        CrashReporter.Log($"[OptiScalerService.InstallAsync] Updated ReShade record InstalledAs → '{ReShadeCoexistName}'");
                    }

                    // Update card RS state
                    card.RsInstalledFile = ReShadeCoexistName;
                }
            }
            catch (Exception rsEx)
            {
                CrashReporter.Log($"[OptiScalerService.InstallAsync] ReShade rename failed — {rsEx.Message}");
            }

            // ── 5. Deploy all files from staging to game folder ──────────────
            // OptiScaler.dll is renamed to the effective DLL name.
            // All other files are copied with their original names.
            // Game-owned originals are backed up to <filename>.original before overwriting.
            var stagingFiles = Directory.GetFiles(StagingDir, "*", SearchOption.TopDirectoryOnly);
            foreach (var stagingFile in stagingFiles)
            {
                var fileName = Path.GetFileName(stagingFile);

                // Skip version.txt — it's RHI's staging metadata, not an OptiScaler file
                if (fileName.Equals("version.txt", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Skip installer scripts, READMEs, and license files — not needed in game folder
                if (fileName.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)
                    || fileName.EndsWith(".sh", StringComparison.OrdinalIgnoreCase)
                    || fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    continue;

                string destPath;
                if (fileName.Equals("OptiScaler.dll", StringComparison.OrdinalIgnoreCase))
                {
                    // OptiScaler.dll gets renamed to the effective DLL name
                    destPath = Path.Combine(card.InstallPath, effectiveDllName);
                }
                else
                {
                    destPath = Path.Combine(card.InstallPath, fileName);
                }

                // Skip OptiScaler.ini here — it's handled separately in step 5 with INI seeding logic
                if (fileName.Equals(IniFileName, StringComparison.OrdinalIgnoreCase))
                    continue;

                BackupOriginalIfExists(destPath);
                File.Copy(stagingFile, destPath, overwrite: true);
                CrashReporter.Log($"[OptiScalerService.InstallAsync] Deployed {fileName}" +
                    (fileName.Equals("OptiScaler.dll", StringComparison.OrdinalIgnoreCase) ? $" as {effectiveDllName}" : ""));
            }

            // ── Deploy subdirectories from staging (e.g. D3D12_Optiscaler) ──
            foreach (var stagingSubDir in Directory.GetDirectories(StagingDir))
            {
                var dirName = Path.GetFileName(stagingSubDir);

                // Skip Licenses folder — not needed in game folder
                if (dirName.Equals("Licenses", StringComparison.OrdinalIgnoreCase))
                    continue;

                var destSubDir = Path.Combine(card.InstallPath, dirName);
                Directory.CreateDirectory(destSubDir);

                foreach (var subFile in Directory.GetFiles(stagingSubDir, "*", SearchOption.AllDirectories))
                {
                    var relativePath = Path.GetRelativePath(stagingSubDir, subFile);
                    var destPath = Path.Combine(destSubDir, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    BackupOriginalIfExists(destPath);
                    File.Copy(subFile, destPath, overwrite: true);
                    CrashReporter.Log($"[OptiScalerService.InstallAsync] Deployed {dirName}/{relativePath}");
                }
            }

            progress?.Report(("Configuring OptiScaler INI...", 60));

            // ── 5. INI seeding and deployment ────────────────────────────────
            Directory.CreateDirectory(AuxInstallService.InisDir);

            var userIniPath = OsIniPath; // %LOCALAPPDATA%\RHI\inis\OptiScaler.ini
            var gameIniPath = Path.Combine(card.InstallPath, IniFileName);

            // Always update the INIs_Folder with the correct bundled INI for the current GPU settings.
            // This ensures the template stays in sync when the user changes GPU type in Settings.
            var bundledIniPath = GetBundledIniPath(gpuType, dlssInputs);
            if (File.Exists(bundledIniPath))
            {
                File.Copy(bundledIniPath, userIniPath, overwrite: true);
                CrashReporter.Log($"[OptiScalerService.InstallAsync] Updated INIs folder with {Path.GetFileName(bundledIniPath)}");
            }

            // Deploy INI to game folder only if one doesn't already exist there
            if (!File.Exists(gameIniPath))
            {
                if (File.Exists(userIniPath))
                {
                    File.Copy(userIniPath, gameIniPath, overwrite: false);
                    CrashReporter.Log("[OptiScalerService.InstallAsync] Deployed OptiScaler.ini to game folder");
                }
            }
            else
            {
                CrashReporter.Log("[OptiScalerService.InstallAsync] OptiScaler.ini already exists in game folder — preserved");
            }

            // Always enforce LoadReshade=true in the deployed INI
            if (File.Exists(gameIniPath))
            {
                EnforceLoadReshade(gameIniPath);
                CrashReporter.Log("[OptiScalerService.InstallAsync] Enforced LoadReshade=true in deployed INI");

                // Apply the user's configured hotkey
                if (!string.IsNullOrEmpty(hotkey))
                {
                    WriteShortcutKey(gameIniPath, hotkey);
                    CrashReporter.Log($"[OptiScalerService.InstallAsync] Set ShortcutKey={hotkey} in deployed INI");
                }

                // Always enforce LoadAsiPlugins=true so OptiPatcher can load when present
                EnforceLoadAsiPlugins(gameIniPath);
                CrashReporter.Log("[OptiScalerService.InstallAsync] Enforced LoadAsiPlugins=true in deployed INI");
            }

            progress?.Report(("Saving install record...", 80));

            // ── 6. Create/update AuxInstalledRecord ──────────────────────────
            var record = new AuxInstalledRecord
            {
                GameName       = card.GameName,
                InstallPath    = card.InstallPath,
                AddonType      = AddonType,
                InstalledAs    = effectiveDllName,
                SourceUrl      = null,
                RemoteFileSize = null,
                InstalledAt    = DateTime.UtcNow,
            };
            _auxInstaller.SaveAuxRecord(record);
            CrashReporter.Log($"[OptiScalerService.InstallAsync] Saved tracking record for {card.GameName}");

            // ── 7. Deploy OptiPatcher for AMD/Intel GPUs ─────────────────────
            if (gpuType.Equals("AMD", StringComparison.OrdinalIgnoreCase)
                || gpuType.Equals("Intel", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    progress?.Report(("Downloading OptiPatcher...", 85));
                    await EnsureOptiPatcherStagingAsync(progress);

                    var stagedAsi = Path.Combine(OptiPatcherStagingDir, OptiPatcherFileName);
                    if (File.Exists(stagedAsi))
                    {
                        var pluginsDir = Path.Combine(card.InstallPath, "plugins");
                        Directory.CreateDirectory(pluginsDir);
                        var destAsi = Path.Combine(pluginsDir, OptiPatcherFileName);
                        File.Copy(stagedAsi, destAsi, overwrite: true);
                        CrashReporter.Log($"[OptiScalerService.InstallAsync] Deployed OptiPatcher.asi to plugins folder");
                        progress?.Report(("OptiPatcher deployed", 90));
                    }
                    else
                    {
                        CrashReporter.Log("[OptiScalerService.InstallAsync] OptiPatcher staging not available — skipping deployment");
                    }
                }
                catch (Exception opEx)
                {
                    CrashReporter.Log($"[OptiScalerService.InstallAsync] OptiPatcher deployment failed — {opEx.Message}");
                }
            }

            // ── 8. Update card VM properties ─────────────────────────────────
            card.OsInstalledFile = effectiveDllName;
            card.OsInstalledVersion = StagedVersion;
            card.OsStatus = GameStatus.Installed;

            progress?.Report(("OptiScaler installed!", 100));
            CrashReporter.Log($"[OptiScalerService.InstallAsync] Install complete for {card.GameName}");

            return record;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[OptiScalerService.InstallAsync] {card.GameName} — {ex.Message}");
            progress?.Report(($"Install failed: {ex.Message}", 0));
            return null;
        }
    }

    /// <inheritdoc />
    public void Uninstall(GameCardViewModel card)
    {
        try
        {
            var gameDir = card.InstallPath;

            // ── 1. Delete all OptiScaler files and restore originals ─────────
            // Determine which files were deployed by checking the staging folder
            var stagingFiles = IsStagingReady ? Directory.GetFiles(StagingDir, "*", SearchOption.TopDirectoryOnly) : Array.Empty<string>();
            var stagingDirs = IsStagingReady ? Directory.GetDirectories(StagingDir) : Array.Empty<string>();
            var deployedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var stagingFile in stagingFiles)
            {
                var fileName = Path.GetFileName(stagingFile);
                if (fileName.Equals("version.txt", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (fileName.Equals("OptiScaler.dll", StringComparison.OrdinalIgnoreCase))
                    continue; // handled separately below (renamed on deploy)
                if (fileName.Equals(IniFileName, StringComparison.OrdinalIgnoreCase))
                    continue; // handled separately below
                deployedFileNames.Add(fileName);
            }

            // Delete the renamed OptiScaler DLL
            var installedDll = card.OsInstalledFile;
            if (string.IsNullOrEmpty(installedDll))
            {
                var record = _auxInstaller.FindRecord(card.GameName, gameDir, AddonType);
                installedDll = record?.InstalledAs;
            }
            if (!string.IsNullOrEmpty(installedDll))
            {
                var dllPath = Path.Combine(gameDir, installedDll);
                if (File.Exists(dllPath))
                {
                    File.Delete(dllPath);
                    CrashReporter.Log($"[OptiScalerService.Uninstall] Deleted OptiScaler DLL: {dllPath}");
                    RestoreOriginalIfExists(dllPath);
                }
            }

            // ── 2. Delete OptiScaler.ini from game folder ────────────────────
            var iniPath = Path.Combine(gameDir, IniFileName);
            if (File.Exists(iniPath))
            {
                File.Delete(iniPath);
                CrashReporter.Log($"[OptiScalerService.Uninstall] Deleted {IniFileName}");
            }

            // ── 3. Delete all other deployed files ───────────────────────────
            foreach (var fileName in deployedFileNames)
            {
                var filePath = Path.Combine(gameDir, fileName);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    CrashReporter.Log($"[OptiScalerService.Uninstall] Deleted {fileName}");
                    RestoreOriginalIfExists(filePath);
                }
            }

            // ── 3b. Clean up deployed subdirectories ─────────────────────────
            foreach (var stagingSubDir in stagingDirs)
            {
                var dirName = Path.GetFileName(stagingSubDir);
                if (dirName.Equals("Licenses", StringComparison.OrdinalIgnoreCase))
                    continue;

                var gameSubDir = Path.Combine(gameDir, dirName);
                if (!Directory.Exists(gameSubDir)) continue;

                foreach (var subFile in Directory.GetFiles(gameSubDir, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        File.Delete(subFile);
                        RestoreOriginalIfExists(subFile);
                    }
                    catch (Exception ex)
                    {
                        CrashReporter.Log($"[OptiScalerService.Uninstall] Failed to delete {subFile} — {ex.Message}");
                    }
                }

                // Remove the subdirectory if empty
                try
                {
                    if (Directory.Exists(gameSubDir) && !Directory.EnumerateFileSystemEntries(gameSubDir).Any())
                        Directory.Delete(gameSubDir, recursive: true);
                }
                catch { }
            }

            // ── 4. Remove AuxInstalledRecord ─────────────────────────────────
            var existingRecord = _auxInstaller.FindRecord(card.GameName, gameDir, AddonType);
            if (existingRecord != null)
            {
                _auxInstaller.RemoveRecord(existingRecord);
                CrashReporter.Log($"[OptiScalerService.Uninstall] Removed tracking record for {card.GameName}");
            }

            // ── 4b. Clean up OptiPatcher ─────────────────────────────────────
            try
            {
                var optiPatcherPath = Path.Combine(gameDir, "plugins", OptiPatcherFileName);
                if (File.Exists(optiPatcherPath))
                {
                    File.Delete(optiPatcherPath);
                    CrashReporter.Log("[OptiScalerService.Uninstall] Deleted OptiPatcher.asi from plugins folder");
                }

                var pluginsDir = Path.Combine(gameDir, "plugins");
                if (Directory.Exists(pluginsDir) && !Directory.EnumerateFileSystemEntries(pluginsDir).Any())
                {
                    Directory.Delete(pluginsDir);
                    CrashReporter.Log("[OptiScalerService.Uninstall] Removed empty plugins folder");
                }
            }
            catch (Exception opEx)
            {
                CrashReporter.Log($"[OptiScalerService.Uninstall] OptiPatcher cleanup failed — {opEx.Message}");
            }

            // ── 5. ReShade coexistence — restore ReShade64.dll to correct name ──
            try
            {
                var rsCoexistPath = Path.Combine(gameDir, ReShadeCoexistName);
                if (File.Exists(rsCoexistPath))
                {
                    var resolvedName = ResolveReShadeFilename(card);
                    var resolvedPath = Path.Combine(gameDir, resolvedName);

                    if (!resolvedName.Equals(ReShadeCoexistName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (File.Exists(resolvedPath))
                        {
                            // Target filename is occupied — keep as ReShade64.dll
                            CrashReporter.Log($"[OptiScalerService.Uninstall] Target '{resolvedName}' occupied — keeping ReShade as '{ReShadeCoexistName}'");
                        }
                        else
                        {
                            File.Move(rsCoexistPath, resolvedPath);
                            CrashReporter.Log($"[OptiScalerService.Uninstall] Renamed ReShade '{ReShadeCoexistName}' → '{resolvedName}'");
                        }
                    }

                    // Update ReShade tracking record
                    var rsRecord = _auxInstaller.FindRecord(card.GameName, gameDir, AuxInstallService.TypeReShade)
                                ?? _auxInstaller.FindRecord(card.GameName, gameDir, AuxInstallService.TypeReShadeNormal);
                    if (rsRecord != null)
                    {
                        var actualName = File.Exists(resolvedPath) ? resolvedName : ReShadeCoexistName;
                        rsRecord.InstalledAs = actualName;
                        _auxInstaller.SaveAuxRecord(rsRecord);
                        CrashReporter.Log($"[OptiScalerService.Uninstall] Updated ReShade record InstalledAs → '{actualName}'");
                    }

                    // Update card RS state
                    card.RsInstalledFile = File.Exists(resolvedPath) ? resolvedName : ReShadeCoexistName;
                }
            }
            catch (Exception rsEx)
            {
                CrashReporter.Log($"[OptiScalerService.Uninstall] ReShade restore failed — {rsEx.Message}");
            }

            // ── 6. Update card VM ────────────────────────────────────────────
            card.OsStatus = GameStatus.NotInstalled;
            card.OsInstalledFile = null;
            card.OsInstalledVersion = null;

            CrashReporter.Log($"[OptiScalerService.Uninstall] Uninstall complete for {card.GameName}");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[OptiScalerService.Uninstall] {card.GameName} — {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task UpdateAsync(
        GameCardViewModel card,
        IProgress<(string message, double percent)>? progress = null)
    {
        try
        {
            progress?.Report(("Preparing OptiScaler update...", 5));

            // ── 1. Validate staging is ready ─────────────────────────────────
            if (!IsStagingReady)
            {
                CrashReporter.Log("[OptiScalerService.UpdateAsync] Staging not ready — attempting download");
                await EnsureStagingAsync(progress);
                if (!IsStagingReady)
                {
                    CrashReporter.Log("[OptiScalerService.UpdateAsync] Staging still not ready after download attempt — aborting");
                    progress?.Report(("OptiScaler staging not available", 0));
                    return;
                }
            }

            var gameDir = card.InstallPath;

            // ── 2. Get the installed DLL filename from tracking record ───────
            var record = _auxInstaller.FindRecord(card.GameName, gameDir, AddonType);
            var installedDll = record?.InstalledAs ?? card.OsInstalledFile;
            if (string.IsNullOrEmpty(installedDll))
            {
                CrashReporter.Log($"[OptiScalerService.UpdateAsync] No installed DLL filename found for {card.GameName} — aborting");
                progress?.Report(("Update failed: no installed DLL found", 0));
                return;
            }

            progress?.Report(("Updating OptiScaler files...", 20));

            // ── 3. Deploy all files from staging, overwriting OptiScaler files ──
            // Originals were already backed up during initial install.
            var stagingFiles = Directory.GetFiles(StagingDir, "*", SearchOption.TopDirectoryOnly);
            foreach (var stagingFile in stagingFiles)
            {
                var fileName = Path.GetFileName(stagingFile);

                if (fileName.Equals("version.txt", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Skip installer scripts, READMEs, and license files — not needed in game folder
                if (fileName.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)
                    || fileName.EndsWith(".sh", StringComparison.OrdinalIgnoreCase)
                    || fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (fileName.Equals(IniFileName, StringComparison.OrdinalIgnoreCase))
                    continue; // INI is preserved — do not overwrite

                string destPath;
                if (fileName.Equals("OptiScaler.dll", StringComparison.OrdinalIgnoreCase))
                    destPath = Path.Combine(gameDir, installedDll);
                else
                    destPath = Path.Combine(gameDir, fileName);

                // Backup any new game files that weren't present during initial install
                // (e.g. if a new OptiScaler version adds files that collide with game files)
                BackupOriginalIfExists(destPath);
                File.Copy(stagingFile, destPath, overwrite: true);
                CrashReporter.Log($"[OptiScalerService.UpdateAsync] Replaced {fileName}");
            }

            // ── Deploy subdirectories from staging (e.g. D3D12_Optiscaler) ──
            foreach (var stagingSubDir in Directory.GetDirectories(StagingDir))
            {
                var dirName = Path.GetFileName(stagingSubDir);

                // Skip Licenses folder — not needed in game folder
                if (dirName.Equals("Licenses", StringComparison.OrdinalIgnoreCase))
                    continue;

                var destSubDir = Path.Combine(gameDir, dirName);
                Directory.CreateDirectory(destSubDir);

                foreach (var subFile in Directory.GetFiles(stagingSubDir, "*", SearchOption.AllDirectories))
                {
                    var relativePath = Path.GetRelativePath(stagingSubDir, subFile);
                    var destPath = Path.Combine(destSubDir, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    BackupOriginalIfExists(destPath);
                    File.Copy(subFile, destPath, overwrite: true);
                    CrashReporter.Log($"[OptiScalerService.UpdateAsync] Deployed {dirName}/{relativePath}");
                }
            }

            // ── 4. Do NOT overwrite OptiScaler.ini in the game folder ────────
            // INI is intentionally preserved — no copy operation here
            CrashReporter.Log("[OptiScalerService.UpdateAsync] Preserved existing OptiScaler.ini in game folder");

            progress?.Report(("Updating tracking record...", 80));

            // ── 5. Update tracking record with new version ───────────────────
            if (record != null)
            {
                record.InstalledAt = DateTime.UtcNow;
                _auxInstaller.SaveAuxRecord(record);
                CrashReporter.Log($"[OptiScalerService.UpdateAsync] Updated tracking record for {card.GameName}");
            }

            // ── 6. Update card VM properties ─────────────────────────────────
            card.OsInstalledVersion = StagedVersion;
            card.OsStatus = GameStatus.Installed;

            progress?.Report(("OptiScaler updated!", 100));
            CrashReporter.Log($"[OptiScalerService.UpdateAsync] Update complete for {card.GameName}");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[OptiScalerService.UpdateAsync] {card.GameName} — {ex.Message}");
            progress?.Report(($"Update failed: {ex.Message}", 0));
        }
    }

    // ── ReShade coexistence helpers ───────────────────────────────────────────

    /// <summary>
    /// Resolves the correct ReShade filename to restore when OptiScaler is uninstalled.
    /// Priority: user DLL override > manifest override > auto-detected API > dxgi.dll default.
    /// </summary>
    internal string ResolveReShadeFilename(GameCardViewModel card)
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
            if (card.DetectedApis.Contains(Models.GraphicsApiType.DirectX9))
                return "d3d9.dll";
            if (card.DetectedApis.Contains(Models.GraphicsApiType.OpenGL))
                return "opengl32.dll";
            // DX11/DX12/Vulkan all use dxgi.dll
            if (card.DetectedApis.Contains(Models.GraphicsApiType.DirectX11)
                || card.DetectedApis.Contains(Models.GraphicsApiType.DirectX12))
                return "dxgi.dll";
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
        if (!string.IsNullOrWhiteSpace(userOverride))
            return userOverride!;
        if (!string.IsNullOrWhiteSpace(manifestOverride))
            return manifestOverride!;
        if (!string.IsNullOrWhiteSpace(detectedApi))
        {
            return detectedApi!.ToLowerInvariant() switch
            {
                "dx9" or "directx9" => "d3d9.dll",
                "opengl" => "opengl32.dll",
                "dx11" or "dx12" or "directx11" or "directx12" => "dxgi.dll",
                _ => AuxInstallService.RsNormalName,
            };
        }
        return AuxInstallService.RsNormalName; // dxgi.dll
    }

    // ── INI management ────────────────────────────────────────────────────────

    /// <inheritdoc />
    public void CopyIniToGame(GameCardViewModel card)
    {
        if (string.IsNullOrEmpty(card.InstallPath)) return;

        var sourceIni = OsIniPath; // %LOCALAPPDATA%\RHI\inis\OptiScaler.ini
        if (!File.Exists(sourceIni))
        {
            CrashReporter.Log("[OptiScalerService.CopyIniToGame] No OptiScaler.ini in INIs folder — aborting copy.");
            return;
        }

        var destIni = Path.Combine(card.InstallPath, IniFileName);
        File.Copy(sourceIni, destIni, overwrite: true);
        EnforceLoadReshade(destIni);
        CrashReporter.Log($"[OptiScalerService.CopyIniToGame] Copied OptiScaler.ini to '{card.InstallPath}' with LoadReshade=true enforced.");
    }

    /// <summary>
    /// Before copying an OptiScaler file to the game folder, checks if a game-owned
    /// original already exists at the destination. If so, renames it to
    /// &lt;filename&gt;.original so it can be restored on uninstall.
    /// Skips backup if a .original already exists (from a previous install).
    /// </summary>
    private static void BackupOriginalIfExists(string destPath)
    {
        if (!File.Exists(destPath)) return;
        var backupPath = destPath + ".original";
        if (File.Exists(backupPath)) return; // already backed up from a previous install
        try
        {
            File.Move(destPath, backupPath);
            CrashReporter.Log($"[OptiScalerService] Backed up original: {Path.GetFileName(destPath)} → {Path.GetFileName(backupPath)}");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[OptiScalerService] Failed to back up '{Path.GetFileName(destPath)}' — {ex.Message}");
        }
    }

    /// <summary>
    /// After removing an OptiScaler file from the game folder, checks if a
    /// &lt;filename&gt;.original backup exists. If so, restores it to the original name.
    /// </summary>
    private static void RestoreOriginalIfExists(string filePath)
    {
        var backupPath = filePath + ".original";
        if (!File.Exists(backupPath)) return;
        try
        {
            // If the OptiScaler file wasn't deleted (e.g. in-use), don't overwrite it
            if (File.Exists(filePath))
            {
                CrashReporter.Log($"[OptiScalerService] Cannot restore '{Path.GetFileName(filePath)}' — file still exists");
                return;
            }
            File.Move(backupPath, filePath);
            CrashReporter.Log($"[OptiScalerService] Restored original: {Path.GetFileName(backupPath)} → {Path.GetFileName(filePath)}");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[OptiScalerService] Failed to restore '{Path.GetFileName(filePath)}' — {ex.Message}");
        }
    }

    /// <summary>
    /// Reads the INI file at <paramref name="iniPath"/>, finds the <c>LoadReshade=</c> line
    /// (case-insensitive), replaces it with <c>LoadReshade=true</c>, or appends the line
    /// if it is missing. Writes the result back to the file.
    /// </summary>
    public static void EnforceLoadReshade(string iniPath)
    {
        var lines = File.ReadAllLines(iniPath).ToList();
        bool found = false;
        for (int i = 0; i < lines.Count; /* manual increment */)
        {
            if (lines[i].TrimStart().StartsWith("LoadReshade=", StringComparison.OrdinalIgnoreCase))
            {
                if (!found)
                {
                    // First occurrence — replace with enforced value
                    lines[i] = "LoadReshade=true";
                    found = true;
                    i++;
                }
                else
                {
                    // Duplicate — remove it
                    lines.RemoveAt(i);
                }
            }
            else
            {
                i++;
            }
        }
        if (!found)
            lines.Add("LoadReshade=true");
        File.WriteAllLines(iniPath, lines);
    }

    /// <summary>
    /// Reads the INI file at <paramref name="iniPath"/>, finds the <c>LoadAsiPlugins=</c> line
    /// (case-insensitive), replaces it with <c>LoadAsiPlugins=true</c>, or appends the line
    /// if it is missing. Writes the result back to the file.
    /// </summary>
    public static void EnforceLoadAsiPlugins(string iniPath)
    {
        var lines = File.ReadAllLines(iniPath).ToList();
        bool found = false;
        for (int i = 0; i < lines.Count; /* manual increment */)
        {
            if (lines[i].TrimStart().StartsWith("LoadAsiPlugins=", StringComparison.OrdinalIgnoreCase))
            {
                if (!found)
                {
                    lines[i] = "LoadAsiPlugins=true";
                    found = true;
                    i++;
                }
                else
                {
                    // Duplicate — remove it
                    lines.RemoveAt(i);
                }
            }
            else
            {
                i++;
            }
        }
        if (!found)
            lines.Add("LoadAsiPlugins=true");
        File.WriteAllLines(iniPath, lines);
    }

    // ── Detection ─────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public string? DetectInstallation(string installPath)
    {
        try
        {
            if (!Directory.Exists(installPath)) return null;

            // Scan all DLL files in the game folder for OptiScaler binary signatures
            foreach (var dllPath in Directory.GetFiles(installPath, "*.dll"))
            {
                if (IsOptiScalerFile(dllPath))
                    return Path.GetFileName(dllPath);
            }

            // Secondary marker: check for OptiScaler.ini presence
            var iniPath = Path.Combine(installPath, IniFileName);
            if (File.Exists(iniPath))
            {
                // INI exists but no DLL matched — check supported DLL names
                // in case the binary signature scan missed it
                foreach (var dllName in SupportedDllNames)
                {
                    var candidatePath = Path.Combine(installPath, dllName);
                    if (File.Exists(candidatePath))
                    {
                        CrashReporter.Log($"[OptiScalerService.DetectInstallation] OptiScaler.ini found with candidate DLL '{dllName}' in '{installPath}'");
                        return dllName;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[OptiScalerService.DetectInstallation] Error scanning '{installPath}' — {ex.Message}");
        }
        return null;
    }

    /// <inheritdoc />
    public bool IsOptiScalerFile(string filePath)
    {
        return IsOptiScalerFileStatic(filePath);
    }

    /// <summary>
    /// Static version of <see cref="IsOptiScalerFile"/> for use by the foreign DLL
    /// protection system (<see cref="AuxInstallService.IdentifyDxgiFile"/>).
    /// Reads the first ~2 MB of a DLL file and scans for OptiScaler binary signatures.
    /// </summary>
    public static bool IsOptiScalerFileStatic(string filePath)
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

            foreach (var signature in OptiScalerSignatures)
            {
                if (ContainsSequence(buffer, totalRead, signature))
                    return true;
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[OptiScalerService.IsOptiScalerFile] Error scanning '{filePath}' — {ex.Message}");
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

    // ── Tracking records ──────────────────────────────────────────────────────

    /// <inheritdoc />
    public List<AuxInstalledRecord> LoadAllRecords()
    {
        try
        {
            return _auxInstaller.LoadAll()
                .Where(r => r.AddonType.Equals(AddonType, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[OptiScalerService.LoadAllRecords] Failed to load records — {ex.Message}");
            return new List<AuxInstalledRecord>();
        }
    }

    /// <inheritdoc />
    public AuxInstalledRecord? FindRecord(string gameName, string installPath)
    {
        try
        {
            return _auxInstaller.FindRecord(gameName, installPath, AddonType);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[OptiScalerService.FindRecord] Failed to find record for '{gameName}' — {ex.Message}");
            return null;
        }
    }

    // ── DLL naming ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public string GetEffectiveOsDllName(string gameName)
    {
        throw new NotImplementedException();
    }

    // ── Hotkey ────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public void SetHotkey(string hotkeyValue)
    {
        try
        {
            Directory.CreateDirectory(AuxInstallService.InisDir);
            var iniPath = OsIniPath;
            WriteShortcutKey(iniPath, hotkeyValue);
            CrashReporter.Log($"[OptiScalerService.SetHotkey] Wrote ShortcutKey={hotkeyValue} to INIs folder");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[OptiScalerService.SetHotkey] Failed — {ex.Message}");
        }
    }

    /// <inheritdoc />
    public void ApplyHotkeyToAllGames(string hotkeyValue)
    {
        try
        {
            var records = LoadAllRecords();
            int updatedCount = 0;
            foreach (var record in records)
            {
                var gameIniPath = Path.Combine(record.InstallPath, IniFileName);
                if (!File.Exists(gameIniPath)) continue;

                try
                {
                    WriteShortcutKey(gameIniPath, hotkeyValue);
                    updatedCount++;
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[OptiScalerService.ApplyHotkeyToAllGames] Failed for '{record.GameName}' — {ex.Message}");
                }
            }
            CrashReporter.Log($"[OptiScalerService.ApplyHotkeyToAllGames] Updated {updatedCount} game(s) with ShortcutKey={hotkeyValue}");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[OptiScalerService.ApplyHotkeyToAllGames] Failed — {ex.Message}");
        }
    }

    /// <summary>
    /// Reads the INI file at <paramref name="iniPath"/>, finds the <c>ShortcutKey=</c> line
    /// (case-insensitive), replaces it with <c>ShortcutKey=&lt;value&gt;</c>, or appends the line
    /// if it is missing. Writes the result back to the file. If the file does not exist,
    /// creates it with just the ShortcutKey line.
    /// </summary>
    public static void WriteShortcutKey(string iniPath, string hotkeyValue)
    {
        // Convert friendly name (e.g. "Delete") to VK code (e.g. "0x2E") for OptiScaler
        var vkValue = ResolveHotkeyToVkCode(hotkeyValue);

        var lines = File.Exists(iniPath)
            ? File.ReadAllLines(iniPath).ToList()
            : new List<string>();

        bool found = false;
        for (int i = 0; i < lines.Count; /* manual increment */)
        {
            if (lines[i].TrimStart().StartsWith("ShortcutKey=", StringComparison.OrdinalIgnoreCase))
            {
                if (!found)
                {
                    lines[i] = $"ShortcutKey={vkValue}";
                    found = true;
                    i++;
                }
                else
                {
                    // Duplicate — remove it
                    lines.RemoveAt(i);
                }
            }
            else
            {
                i++;
            }
        }
        if (!found)
            lines.Add($"ShortcutKey={vkValue}");
        File.WriteAllLines(iniPath, lines);
    }

    /// <summary>
    /// Reads the <c>ShortcutKey=</c> value from the given INI file.
    /// Returns null if the file does not exist or the key is not found.
    /// </summary>
    public static string? ReadShortcutKey(string iniPath)
    {
        if (!File.Exists(iniPath)) return null;
        foreach (var line in File.ReadAllLines(iniPath))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("ShortcutKey=", StringComparison.OrdinalIgnoreCase))
                return trimmed.Substring("ShortcutKey=".Length);
        }
        return null;
    }
}
