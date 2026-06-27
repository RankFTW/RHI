using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace RenoDXCommander.Services;

/// <summary>
/// Manages the Universal UE DOF Fix addon — download, staging, install, uninstall, and update detection.
/// Targets UE 5.0–5.6 games only. Hosted on RankFTW/rhi-repo GitHub releases.
/// </summary>
public class DofFixService : IDofFixService
{
    private const string AddonFileName = "renodx-universal_ue_dof_fix.addon64";
    private const string TagPrefix = "ue-dof-fix-";
    private const string GitHubApiUrl = "https://api.github.com/repos/RankFTW/rhi-repo/releases";
    private const string DefaultDownloadBaseUrl = "https://github.com/RankFTW/rhi-repo/releases/download";

    private readonly HttpClient _http;
    private readonly ICrashReporter _crashReporter;
    private readonly string _stagingDir;
    private readonly string _versionFile;

    /// <summary>Games to skip DOF Fix eligibility for (set from manifest).</summary>
    private List<string>? _skipGames;

    /// <summary>Games to force-enable DOF Fix eligibility regardless of engine detection.</summary>
    private List<string>? _forceGames;

    public DofFixService(HttpClient http, ICrashReporter crashReporter)
    {
        _http = http;
        _crashReporter = crashReporter;
        _stagingDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RHI", "ue-dof-fix");
        _versionFile = Path.Combine(_stagingDir, "version.txt");
    }

    /// <summary>The addon filename deployed to game folders.</summary>
    public static string FileName => AddonFileName;

    /// <summary>The currently staged version (from version.txt), or null if not staged.</summary>
    public string? StagedVersion => File.Exists(_versionFile) ? File.ReadAllText(_versionFile).Trim() : null;

    /// <summary>Whether the staging directory has the addon ready for deployment.</summary>
    public bool IsStagingReady => File.Exists(Path.Combine(_stagingDir, AddonFileName));

    /// <summary>Whether an update is available (set after CheckForUpdateAsync).</summary>
    public bool HasUpdate { get; private set; }

    /// <summary>The latest remote version tag (set after CheckForUpdateAsync).</summary>
    public string? LatestVersion { get; private set; }

    /// <summary>The release body/notes from the latest version (set after CheckForUpdateAsync or FetchReleaseNotes).</summary>
    public string? ReleaseNotes { get; private set; }

    /// <summary>Optional manifest URL override.</summary>
    public string? ManifestUrlOverride { get; set; }

    /// <summary>
    /// Sets the skip list from manifest data.
    /// </summary>
    public void SetSkipGames(List<string>? skipGames) => _skipGames = skipGames;

    /// <summary>Sets the force-eligible list from manifest data.</summary>
    public void SetForceGames(List<string>? forceGames) => _forceGames = forceGames;

    /// <summary>
    /// Ensures the addon is staged (downloaded). Downloads if not present or if an update is available.
    /// </summary>
    public async Task EnsureStagingAsync(IProgress<(string message, double percent)>? progress = null)
    {
        if (IsStagingReady && !HasUpdate)
        {
            _crashReporter.Log("[DofFixService.EnsureStagingAsync] Staging already valid — skipping download");
            return;
        }

        Directory.CreateDirectory(_stagingDir);
        progress?.Report(("Downloading DOF Fix...", 10));

        var (version, downloadUrl, body) = await FetchLatestReleaseInfoAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(version) || string.IsNullOrEmpty(downloadUrl))
        {
            _crashReporter.Log("[DofFixService.EnsureStagingAsync] Could not resolve latest release");
            return;
        }

        progress?.Report(("Downloading DOF Fix...", 30));

        try
        {
            var bytes = await _http.GetByteArrayAsync(downloadUrl).ConfigureAwait(false);
            var destPath = Path.Combine(_stagingDir, AddonFileName);
            await File.WriteAllBytesAsync(destPath, bytes).ConfigureAwait(false);
            File.WriteAllText(_versionFile, version);
            ReleaseNotes = body;
            HasUpdate = false;
            _crashReporter.Log($"[DofFixService.EnsureStagingAsync] Downloaded v{version} ({bytes.Length} bytes)");
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[DofFixService.EnsureStagingAsync] Download failed — {ex.Message}");
        }

        progress?.Report(("DOF Fix ready", 100));
    }

    /// <summary>
    /// Checks GitHub for a newer version than what's currently staged.
    /// </summary>
    public async Task CheckForUpdateAsync()
    {
        var (version, _, body) = await FetchLatestReleaseInfoAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(version))
        {
            _crashReporter.Log("[DofFixService.CheckForUpdateAsync] Could not resolve latest version");
            return;
        }

        LatestVersion = version;
        ReleaseNotes = body;
        var current = StagedVersion;
        HasUpdate = !string.Equals(current, version, StringComparison.OrdinalIgnoreCase);
        _crashReporter.Log($"[DofFixService.CheckForUpdateAsync] Cached={current ?? "(none)"}, Remote={version}, HasUpdate={HasUpdate}");
    }

    /// <summary>
    /// Installs the DOF Fix addon to a game folder.
    /// </summary>
    public async Task<bool> InstallAsync(string installPath, IProgress<(string message, double percent)>? progress = null)
    {
        if (string.IsNullOrEmpty(installPath)) return false;

        await EnsureStagingAsync(progress).ConfigureAwait(false);
        if (!IsStagingReady) return false;

        progress?.Report(("Deploying DOF Fix...", 70));

        var src = Path.Combine(_stagingDir, AddonFileName);
        var dest = Path.Combine(installPath, AddonFileName);
        try
        {
            File.Copy(src, dest, overwrite: true);
            _crashReporter.Log($"[DofFixService.InstallAsync] Deployed to {installPath}");
            progress?.Report(("DOF Fix installed!", 100));
            return true;
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[DofFixService.InstallAsync] Deploy failed — {ex.Message}");
            progress?.Report(($"❌ {ex.Message}", 0));
            return false;
        }
    }

    /// <summary>
    /// Uninstalls the DOF Fix addon from a game folder.
    /// </summary>
    public bool Uninstall(string installPath)
    {
        if (string.IsNullOrEmpty(installPath)) return false;
        var filePath = Path.Combine(installPath, AddonFileName);
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _crashReporter.Log($"[DofFixService.Uninstall] Removed from {installPath}");
            }
            return true;
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[DofFixService.Uninstall] Failed — {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Detects if the DOF Fix is installed in a game folder.
    /// </summary>
    public bool IsInstalledIn(string installPath)
        => !string.IsNullOrEmpty(installPath) && File.Exists(Path.Combine(installPath, AddonFileName));

    /// <summary>
    /// Detects if the DOF Fix is installed in a game folder (static helper).
    /// </summary>
    public static bool IsInstalled(string installPath)
        => !string.IsNullOrEmpty(installPath) && File.Exists(Path.Combine(installPath, AddonFileName));

    /// <summary>
    /// Returns the release page URL for a given version tag.
    /// </summary>
    public string GetReleaseUrl(string version)
        => $"https://github.com/RankFTW/rhi-repo/releases/tag/{TagPrefix}{version}";

    /// <summary>
    /// Determines if a game is eligible for DOF Fix based on engine hint.
    /// Must be Unreal Engine 5.0–5.6 (not 5.7+, not UE4, not non-UE), 64-bit, and not in skip list.
    /// </summary>
    public bool IsGameEligible(string? engineHint, bool is32Bit, string? gameName)
    {
        if (is32Bit) return false;

        // Force-eligible games bypass all engine detection
        if (!string.IsNullOrEmpty(gameName) && _forceGames != null
            && _forceGames.Contains(gameName, StringComparer.OrdinalIgnoreCase))
            return true;

        if (string.IsNullOrEmpty(engineHint)) return false;
        if (!engineHint.Contains("Unreal Engine 5")) return false;

        // Check skip list
        if (!string.IsNullOrEmpty(gameName) && _skipGames != null
            && _skipGames.Contains(gameName, StringComparer.OrdinalIgnoreCase))
            return false;

        // Engine hint format: "Unreal Engine 5.x.y" or "Unreal Engine 5.x"
        // Extract the minor version after "5."
        var idx = engineHint.IndexOf("5.", StringComparison.Ordinal);
        if (idx < 0) return false;
        var afterFive = engineHint.Substring(idx + 2);
        // Get the minor version number
        var dotIdx = afterFive.IndexOf('.');
        var minorStr = dotIdx >= 0 ? afterFive.Substring(0, dotIdx) : afterFive.TrimEnd(' ', ')');
        if (int.TryParse(minorStr, out var minor))
            return minor >= 0 && minor <= 6; // 5.0 through 5.6
        // If we can't parse (just "Unreal Engine 5" with no minor), assume eligible
        return true;
    }

    /// <summary>
    /// Determines if a game is eligible for DOF Fix based on engine hint (static, no skip list).
    /// </summary>
    public static bool IsEligible(string? engineHint, bool is32Bit)
    {
        if (is32Bit) return false;
        if (string.IsNullOrEmpty(engineHint)) return false;
        if (!engineHint.Contains("Unreal Engine 5")) return false;
        var idx = engineHint.IndexOf("5.", StringComparison.Ordinal);
        if (idx < 0) return false;
        var afterFive = engineHint.Substring(idx + 2);
        var dotIdx = afterFive.IndexOf('.');
        var minorStr = dotIdx >= 0 ? afterFive.Substring(0, dotIdx) : afterFive.TrimEnd(' ', ')');
        if (int.TryParse(minorStr, out var minor))
            return minor >= 0 && minor <= 6;
        return true;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<(string? version, string? downloadUrl, string? body)> FetchLatestReleaseInfoAsync()
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, GitHubApiUrl);
            request.Headers.Add("User-Agent", "RHI");
            request.Headers.Add("Accept", "application/vnd.github+json");

            using var response = await _http.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _crashReporter.Log($"[DofFixService] GitHub API returned {response.StatusCode}");
                return (null, null, null);
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            // Find the latest release with the ue-dof-fix- tag prefix
            foreach (var release in doc.RootElement.EnumerateArray())
            {
                if (!release.TryGetProperty("tag_name", out var tagEl)) continue;
                var tag = tagEl.GetString();
                if (tag == null || !tag.StartsWith(TagPrefix, StringComparison.OrdinalIgnoreCase)) continue;

                var version = tag.Substring(TagPrefix.Length);
                var body = release.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() : null;

                // Find the addon asset download URL
                string? downloadUrl = null;
                if (release.TryGetProperty("assets", out var assets))
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        if (asset.TryGetProperty("name", out var nameEl)
                            && nameEl.GetString() == AddonFileName
                            && asset.TryGetProperty("browser_download_url", out var urlEl))
                        {
                            downloadUrl = urlEl.GetString();
                            break;
                        }
                    }
                }

                // Use manifest URL override or construct default
                if (!string.IsNullOrEmpty(ManifestUrlOverride))
                    downloadUrl = ManifestUrlOverride.Replace("{version}", version);
                else if (downloadUrl == null)
                    downloadUrl = $"{DefaultDownloadBaseUrl}/{tag}/{AddonFileName}";

                return (version, downloadUrl, body);
            }

            _crashReporter.Log("[DofFixService] No release found with ue-dof-fix- tag");
            return (null, null, null);
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[DofFixService] FetchLatestReleaseInfo failed — {ex.Message}");
            return (null, null, null);
        }
    }
}
