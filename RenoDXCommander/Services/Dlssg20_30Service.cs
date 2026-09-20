using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace RenoDXCommander.Services;

/// <summary>
/// Manages the 20/30 FG Unlock component (dlssg_for_sm86).
/// GitHub: sdli1995/dlssg_for_sm86.
/// Deploys a single DLL (from the repo tree, not a release) plus dlssg_sm86.ini,
/// renamed to a user-chosen proxy name. Supports RTX 30 (SM86) and RTX 20 (SM75).
/// Version tracking uses the latest commit SHA on version.dll.
/// </summary>
public class Dlssg20_30Service
{
    // ── Constants ─────────────────────────────────────────────────────────────

    private const string RepoOwner     = "sdli1995";
    private const string RepoName      = "dlssg_for_sm86";
    private const string DllRawUrl     = $"https://raw.githubusercontent.com/{RepoOwner}/{RepoName}/main/version.dll";
    private const string IniRawUrl     = $"https://raw.githubusercontent.com/{RepoOwner}/{RepoName}/main/dlssg_sm86.ini";
    private const string ReadmeApiUrl  = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/readme";
    private const string CommitsApiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/commits?path=version.dll&per_page=1";

    public const string StagedDllName = "version.dll";
    public const string IniFileName   = "dlssg_sm86.ini";

    public const string GpuGenRtx30 = "Серия RTX 30";
    public const string GpuGenRtx20 = "Серия RTX 20";

    /// <summary>All valid proxy DLL names (version.dll default + altnative folder).</summary>
    public static readonly string[] ProxyNames =
    {
        "version.dll", "winmm.dll", "dinput8.dll", "winhttp.dll", "dxgi.dll",
    };

    // ── Instance state ─────────────────────────────────────────────────────────

    private readonly HttpClient     _http;
    private readonly ICrashReporter _crashReporter;
    private readonly string         _stagingDir;
    private readonly string         _versionFile;

    public Dlssg20_30Service(HttpClient http, ICrashReporter crashReporter)
    {
        _http          = http;
        _crashReporter = crashReporter;
        _stagingDir    = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RHI", "dlssg2030");
        _versionFile   = Path.Combine(_stagingDir, "version.txt");
    }

    public bool IsStagingReady =>
        File.Exists(Path.Combine(_stagingDir, StagedDllName)) &&
        File.Exists(Path.Combine(_stagingDir, IniFileName));

    /// <summary>Short (7-char) commit SHA of the staged DLL, or null if not staged.</summary>
    public string? StagedVersion => File.Exists(_versionFile)
        ? File.ReadAllText(_versionFile).Trim() : null;

    public bool     HasUpdate     { get; private set; }
    public string?  LatestVersion { get; private set; }

    // ── Detection ─────────────────────────────────────────────────────────────

    public bool IsInstalledIn(string installPath, string? installedAs)
        => !string.IsNullOrEmpty(installPath) && !string.IsNullOrEmpty(installedAs)
        && File.Exists(Path.Combine(installPath, installedAs));

    // ── Staging ───────────────────────────────────────────────────────────────

    public async Task CheckForUpdateAsync()
    {
        var sha = await FetchLatestVersionAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(sha)) return;

        LatestVersion = sha;
        var current = StagedVersion;
        HasUpdate = !string.Equals(current, sha, StringComparison.OrdinalIgnoreCase);
        _crashReporter.Log($"[Dlssg20_30Service.CheckForUpdate] Cached={current ?? "(none)"}, Remote={sha}, HasUpdate={HasUpdate}");
    }

    public async Task EnsureStagingAsync(IProgress<(string message, double percent)>? progress = null)
    {
        if (IsStagingReady && !HasUpdate)
        {
            _crashReporter.Log("[Dlssg20_30Service.EnsureStaging] Already up to date");
            return;
        }

        Directory.CreateDirectory(_stagingDir);

        var sha = LatestVersion ?? await FetchLatestVersionAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(sha))
        {
            _crashReporter.Log("[Dlssg20_30Service.EnsureStaging] Could not resolve latest commit SHA");
            return;
        }

        progress?.Report(("Загрузка DLL разблокировки FG 20/30...", 20));
        if (!await DownloadFileAsync(DllRawUrl, Path.Combine(_stagingDir, StagedDllName)).ConfigureAwait(false))
            return;

        progress?.Report(("Загрузка INI...", 60));
        if (!await DownloadFileAsync(IniRawUrl, Path.Combine(_stagingDir, IniFileName)).ConfigureAwait(false))
            return;

        File.WriteAllText(_versionFile, sha);
        LatestVersion = sha;
        HasUpdate = false;
        _crashReporter.Log($"[Dlssg20_30Service.EnsureStaging] Staged commit {sha}");
        progress?.Report(("Разблокировка FG 20/30 готова", 100));
    }

    // ── Install ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Deploys version.dll (renamed to dllName) and dlssg_sm86.ini to installPath.
    /// Writes Router=SM86/SM75 to the INI based on gpuGen.
    /// </summary>
    public bool Install(string installPath, string dllName, string gpuGen)
    {
        if (string.IsNullOrEmpty(installPath) || !IsStagingReady) return false;
        try
        {
            var dllDest = Path.Combine(installPath, dllName);
            var iniDest = Path.Combine(installPath, IniFileName);

            // Sentinel backup for the DLL slot
            AuxInstallService.SentinelBackup(dllDest);
            File.Copy(Path.Combine(_stagingDir, StagedDllName), dllDest, overwrite: true);
            _crashReporter.Log($"[Dlssg20_30Service.Install] Deployed as '{dllName}' to '{installPath}'");

            // Deploy INI then apply router setting
            File.Copy(Path.Combine(_stagingDir, IniFileName), iniDest, overwrite: true);
            ApplyRouterToIni(iniDest, gpuGen);
            _crashReporter.Log($"[Dlssg20_30Service.Install] Deployed {IniFileName} with Router for {gpuGen}");

            return true;
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[Dlssg20_30Service.Install] Failed — {ex.Message}");
            return false;
        }
    }

    // ── Update ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Updates an already-installed game: overwrites the DLL and merges the new INI
    /// (preserves user's Router/GPU gen setting while picking up any new upstream keys).
    /// </summary>
    public bool Update(string installPath, string installedAs, string gpuGen)
    {
        if (string.IsNullOrEmpty(installPath) || !IsStagingReady) return false;
        try
        {
            var dllDest = Path.Combine(installPath, installedAs);
            var iniDest = Path.Combine(installPath, IniFileName);

            // Overwrite DLL — no new sentinel (original sentinel from first install preserved)
            File.Copy(Path.Combine(_stagingDir, StagedDllName), dllDest, overwrite: true);
            _crashReporter.Log($"[Dlssg20_30Service.Update] Updated DLL '{installedAs}' in '{installPath}'");

            // Merge INI: deploy fresh staging INI (picks up new settings), then re-apply router
            File.Copy(Path.Combine(_stagingDir, IniFileName), iniDest, overwrite: true);
            ApplyRouterToIni(iniDest, gpuGen);
            _crashReporter.Log($"[Dlssg20_30Service.Update] Merged INI with Router for {gpuGen}");

            return true;
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[Dlssg20_30Service.Update] Failed — {ex.Message}");
            return false;
        }
    }

    // ── Uninstall ─────────────────────────────────────────────────────────────

    public void Uninstall(string installPath, string? installedAs)
    {
        if (string.IsNullOrEmpty(installPath)) return;
        try
        {
            if (!string.IsNullOrEmpty(installedAs))
                AuxInstallService.SentinelRestore(Path.Combine(installPath, installedAs));

            var iniPath = Path.Combine(installPath, IniFileName);
            try { if (File.Exists(iniPath)) File.Delete(iniPath); } catch { }

            _crashReporter.Log($"[Dlssg20_30Service.Uninstall] Removed from '{installPath}'");
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[Dlssg20_30Service.Uninstall] Failed — {ex.Message}");
        }
    }

    // ── INI helper ────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes the correct Router value to the [Compatibility] section of the deployed INI.
    /// RTX 30 → SM86, RTX 20 → SM75. Public so the Extras cog can call it immediately on GPU gen change.
    /// </summary>
    public static void ApplyRouterToIniPublic(string iniPath, string gpuGen)
        => ApplyRouterToIni(iniPath, gpuGen);

    private static void ApplyRouterToIni(string iniPath, string gpuGen)
    {
        if (!File.Exists(iniPath)) return;

        var router = gpuGen.Equals(GpuGenRtx20, StringComparison.OrdinalIgnoreCase) ? "SM75" : "SM86";

        try
        {
            var lines = File.ReadAllLines(iniPath).ToList();
            bool inCompatibility = false;
            bool written = false;

            for (int i = 0; i < lines.Count; i++)
            {
                var trimmed = lines[i].Trim();

                if (trimmed.StartsWith("[", StringComparison.Ordinal))
                    inCompatibility = trimmed.Equals("[Compatibility]", StringComparison.OrdinalIgnoreCase);

                if (inCompatibility && trimmed.StartsWith("Router=", StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = $"Router={router}";
                    written = true;
                    break;
                }
            }

            // If no Router key found, append it to [Compatibility] section
            if (!written)
            {
                var compatIdx = lines.FindIndex(l => l.Trim().Equals("[Compatibility]", StringComparison.OrdinalIgnoreCase));
                if (compatIdx >= 0)
                    lines.Insert(compatIdx + 1, $"Router={router}");
            }

            File.WriteAllLines(iniPath, lines);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[Dlssg20_30Service.ApplyRouterToIni] Failed — {ex.Message}");
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<string?> FetchLatestVersionAsync()
    {
        // Primary: parse version from README heading (e.g. "# DLSSG Native 0.2.4" → "0.2.4")
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, ReadmeApiUrl);
            req.Headers.Add("User-Agent", "RHI");
            req.Headers.Add("Accept", "application/vnd.github+json");
            using var resp = await _http.SendAsync(req).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("content", out var contentProp))
                {
                    var b64 = contentProp.GetString() ?? "";
                    var text = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(b64));
                    // First h1 line: "# DLSSG Native 0.2.4"
                    foreach (var line in text.Split('\n'))
                    {
                        var trimmed = line.TrimStart('#').Trim();
                        // Extract version number — last token that looks like x.y.z
                        var parts = trimmed.Split(' ');
                        foreach (var part in parts.Reverse())
                        {
                            if (System.Text.RegularExpressions.Regex.IsMatch(part, @"^\d+\.\d+"))
                                return part;
                        }
                        if (trimmed.Length > 0) break; // only check first heading
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[Dlssg20_30Service.FetchLatestVersion] README parse failed — {ex.Message}");
        }

        // Fallback: short commit SHA
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, CommitsApiUrl);
            req.Headers.Add("User-Agent", "RHI");
            req.Headers.Add("Accept", "application/vnd.github+json");
            using var resp = await _http.SendAsync(req).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                _crashReporter.Log($"[Dlssg20_30Service.FetchLatestVersion] HTTP {(int)resp.StatusCode}");
                return null;
            }
            var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var sha = doc.RootElement.EnumerateArray().FirstOrDefault().TryGetProperty("sha", out var shaProp)
                ? shaProp.GetString() : null;
            return sha?.Length >= 7 ? sha[..7] : sha;
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[Dlssg20_30Service.FetchLatestVersion] {ex.Message}");
            return null;
        }
    }

    private async Task<bool> DownloadFileAsync(string url, string destPath)
    {
        try
        {
            using var resp = await _http.GetAsync(url).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                _crashReporter.Log($"[Dlssg20_30Service.DownloadFile] HTTP {(int)resp.StatusCode} for {url}");
                return false;
            }
            var bytes = await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            await File.WriteAllBytesAsync(destPath, bytes).ConfigureAwait(false);
            _crashReporter.Log($"[Dlssg20_30Service.DownloadFile] Downloaded {Path.GetFileName(destPath)} ({bytes.Length} bytes)");
            return true;
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[Dlssg20_30Service.DownloadFile] Failed for {url} — {ex.Message}");
            return false;
        }
    }
}
