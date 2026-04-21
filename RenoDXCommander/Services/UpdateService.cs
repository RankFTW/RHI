using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RenoDXCommander.Services;

/// <summary>
/// Checks GitHub Releases for a newer version of RHI and downloads the installer if requested.
/// </summary>
public class UpdateService : IUpdateService
{
    private readonly HttpClient _http;
    private readonly GitHubETagCache _etagCache;

    public UpdateService(HttpClient http, GitHubETagCache etagCache)
    {
        _http = http;
        _etagCache = etagCache;
    }
    // GitHub API endpoint for the latest release (new per-version tags like "RHI 1.6.7").
    // This is the primary check — uses /releases/latest to find the newest release.
    private const string LatestReleaseApiUrl =
        "https://api.github.com/repos/RankFTW/RHI/releases/latest";

    // Fallback: legacy tag-based endpoints for older releases.
    private const string ReleaseApiUrl =
        "https://api.github.com/repos/RankFTW/RHI/releases/tags/RHI";
    private const string LegacyReleaseApiUrl =
        "https://api.github.com/repos/RankFTW/RenoDXChecker/releases/tags/RDXC";

    // GitHub API endpoint for the "RDXC-BETA" release tag.
    private const string BetaReleaseApiUrl =
        "https://api.github.com/repos/RankFTW/RenoDXChecker/releases/tags/RDXC-BETA";

    // The asset filenames to look for when updating (checked in order).
    private static readonly string[] InstallerFileNames = ["RHI-Setup.exe", "RDXC-Setup.exe"];

    /// <summary>
    /// Returns the current app version from the assembly metadata (set via .csproj AssemblyVersion).
    /// </summary>
    public Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);

    /// <summary>
    /// Silently checks GitHub for a newer release. When <paramref name="betaOptIn"/> is true,
    /// queries both the stable and beta endpoints and uses <see cref="VersionResolver"/> to
    /// pick the winning version. Returns the parsed remote version and the download URL for
    /// the installer asset, or null if no update is available or the check fails.
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdateAsync(bool betaOptIn = false)
    {
        try
        {
            // Always fetch the stable release — try /releases/latest first, then tag-based fallbacks
            var stable = await FetchReleaseAsync(LatestReleaseApiUrl).ConfigureAwait(false);
            if (stable == null)
            {
                CrashReporter.Log("[UpdateService.CheckForUpdateAsync] /releases/latest returned nothing, trying RHI tag...");
                stable = await FetchReleaseAsync(ReleaseApiUrl).ConfigureAwait(false);
            }
            if (stable == null)
            {
                CrashReporter.Log("[UpdateService.CheckForUpdateAsync] RHI tag returned nothing, trying legacy RDXC...");
                stable = await FetchReleaseAsync(LegacyReleaseApiUrl).ConfigureAwait(false);
            }

            // Fetch beta release only when opted in; failure is non-fatal
            (RdxcVersion version, string downloadUrl)? beta = null;
            if (betaOptIn)
            {
                try
                {
                    beta = await FetchReleaseAsync(BetaReleaseApiUrl).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[UpdateService.CheckForUpdateAsync] Beta endpoint failed — {ex.Message}");
                    // Continue with stable-only
                }
            }

            // Build the current version as an RdxcVersion for the resolver
            var current = CurrentVersion;
            var betaNum = current.Revision > 0 ? (int?)current.Revision : null;
            var currentRdxc = new RdxcVersion(current.Major, current.Minor, current.Build, betaNum);

            // Use VersionResolver to determine which update (if any) to offer
            RdxcVersion? winner = VersionResolver.Resolve(
                currentRdxc,
                stable?.version,
                beta?.version);

            if (winner == null)
            {
                CrashReporter.Log($"[UpdateService.CheckForUpdateAsync] Up to date (local={currentRdxc.ToDisplayString()})");
                return null;
            }

            // Determine the download URL from the winning source
            string? winnerDownloadUrl = null;
            if (stable.HasValue && winner.Value == stable.Value.version)
                winnerDownloadUrl = stable.Value.downloadUrl;
            else if (beta.HasValue && winner.Value == beta.Value.version)
                winnerDownloadUrl = beta.Value.downloadUrl;

            if (string.IsNullOrEmpty(winnerDownloadUrl))
            {
                CrashReporter.Log($"[UpdateService.CheckForUpdateAsync] Winner {winner.Value.ToDisplayString()} has no download URL");
                return null;
            }

            var cmp = new Version(current.Major, current.Minor, current.Build);
            var rmt = new Version(winner.Value.Major, winner.Value.Minor, winner.Value.Build);

            CrashReporter.Log($"[UpdateService.CheckForUpdateAsync] Update available {currentRdxc.ToDisplayString()} → {winner.Value.ToDisplayString()}");
            return new UpdateInfo
            {
                CurrentVersion = cmp,
                RemoteVersion  = rmt,
                DownloadUrl    = winnerDownloadUrl,
                DisplayVersion = winner.Value.ToDisplayString(),
            };
        }
        catch (Exception ex)
        {
            // Update check should never crash the app — swallow and log
            CrashReporter.Log($"[UpdateService.CheckForUpdateAsync] Check failed — {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Fetches a single GitHub release endpoint, parses the version with <see cref="RdxcVersion.TryParse"/>,
    /// and extracts the installer download URL. Returns null if the endpoint fails or the version cannot be parsed.
    /// </summary>
    private async Task<(RdxcVersion version, string downloadUrl)?> FetchReleaseAsync(string apiUrl)
    {
        var json = await _etagCache.GetWithETagAsync(_http, apiUrl, $"RHI/{CurrentVersion}").ConfigureAwait(false);
        if (json == null)
        {
            CrashReporter.Log($"[UpdateService.FetchReleaseAsync] GitHub API returned error for {apiUrl}");
            return null;
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Parse version from release name or tag_name using RdxcVersion
        var releaseName = root.TryGetProperty("name", out var name) ? name.GetString() : null;
        var tagName = root.TryGetProperty("tag_name", out var tag) ? tag.GetString() : null;

        if (!RdxcVersion.TryParse(releaseName, out var version) &&
            !RdxcVersion.TryParse(tagName, out version))
        {
            CrashReporter.Log($"[UpdateService.FetchReleaseAsync] Could not parse version from tag='{tagName}' name='{releaseName}'");
            return null;
        }

        // Find the installer asset download URL (check RHI-Setup.exe first, then legacy RDXC)
        string? downloadUrl = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var installerName in InstallerFileNames)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var assetName = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                    if (string.Equals(assetName, installerName, StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = asset.TryGetProperty("browser_download_url", out var url)
                            ? url.GetString() : null;
                        break;
                    }
                }
                if (!string.IsNullOrEmpty(downloadUrl)) break;
            }
        }

        if (string.IsNullOrEmpty(downloadUrl))
        {
            CrashReporter.Log($"[UpdateService.FetchReleaseAsync] Version {version.ToDisplayString()} found but no installer asset (tried {string.Join(", ", InstallerFileNames)})");
            return null;
        }

        return (version, downloadUrl);
    }

    /// <summary>
    /// Downloads the installer to a temp file and returns the file path.
    /// Reports progress via the optional callback.
    /// </summary>
    public async Task<string?> DownloadInstallerAsync(
        string downloadUrl,
        IProgress<(string msg, double pct)>? progress = null)
    {
        try
        {
            progress?.Report(("Downloading update...", 0));

            var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("RHI", CurrentVersion.ToString()));

            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            // Derive the installer filename from the download URL, falling back to RHI-Setup.exe
            var fileName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
            if (string.IsNullOrEmpty(fileName)) fileName = InstallerFileNames[0];
            var tempPath = Path.Combine(Path.GetTempPath(), fileName);

            await using var contentStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 1024 * 1024, useAsync: true);

            var buffer = new byte[1024 * 1024]; // 1 MB
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer).ConfigureAwait(false)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead)).ConfigureAwait(false);
                totalRead += bytesRead;

                if (totalBytes > 0)
                {
                    var pct = (double)totalRead / totalBytes * 100;
                    progress?.Report(($"Downloading update... {pct:F0}%", pct));
                }
            }

            progress?.Report(("Download complete.", 100));
            CrashReporter.Log($"[UpdateService.DownloadInstallerAsync] Downloaded installer to {tempPath} ({totalRead:N0} bytes)");
            return tempPath;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[UpdateService.DownloadInstallerAsync] Download failed — {ex.Message}");
            progress?.Report(($"Download failed: {ex.Message}", 0));
            return null;
        }
    }

    /// <summary>
    /// Launches the downloaded installer and requests the app to close.
    /// </summary>
    public void LaunchInstallerAndExit(string installerPath, Action closeApp)
    {
        try
        {
            CrashReporter.Log($"[UpdateService.LaunchInstaller] Launching installer {installerPath}");
            Process.Start(new ProcessStartInfo
            {
                FileName        = installerPath,
                UseShellExecute = true,
            });

            // Give the installer a moment to start, then close the app
            closeApp();
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[UpdateService.LaunchInstaller] Failed to launch installer — {ex.Message}");
        }
    }

    /// <summary>
    /// Parses a version string from a release tag or name.
    /// Handles formats like: "1.2.2", "v1.2.2", "RHI-1.2.2", "RHI 1.2.2", "RHI v1.2.2" etc.
    /// </summary>
    private Version? ParseVersion(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        // Match the first occurrence of a version-like pattern (digits.digits.digits[.digits])
        var match = Regex.Match(input, @"(\d+\.\d+\.\d+(?:\.\d+)?)");
        if (!match.Success) return null;

        return Version.TryParse(match.Groups[1].Value, out var ver) ? ver : null;
    }
}

/// <summary>
/// Contains information about an available update.
/// </summary>
public class UpdateInfo
{
    public required Version CurrentVersion { get; init; }
    public required Version RemoteVersion  { get; init; }
    public required string  DownloadUrl    { get; init; }
    public string? DisplayVersion { get; init; }
}

/// <summary>
/// Represents a RHI version with optional beta suffix.
/// Examples: "1.4.8", "1.4.8-beta1", "1.4.8 beta 1"
/// </summary>
public readonly record struct RdxcVersion(int Major, int Minor, int Build, int? BetaNumber = null)
    : IComparable<RdxcVersion>
{
    /// <summary>True when this version has a beta suffix.</summary>
    public bool IsBeta => BetaNumber.HasValue;

    /// <summary>Base version tuple without beta suffix, used for priority comparison.</summary>
    public (int, int, int) BaseVersion => (Major, Minor, Build);

    /// <summary>
    /// Compares by (Major, Minor, Build) first, then by beta status:
    /// stable (no beta) is greater than any beta at the same base version,
    /// and a higher beta number is greater than a lower one.
    /// </summary>
    public int CompareTo(RdxcVersion other)
    {
        int cmp = Major.CompareTo(other.Major);
        if (cmp != 0) return cmp;

        cmp = Minor.CompareTo(other.Minor);
        if (cmp != 0) return cmp;

        cmp = Build.CompareTo(other.Build);
        if (cmp != 0) return cmp;

        // Same base version — stable > beta, higher beta > lower beta
        if (!IsBeta && !other.IsBeta) return 0;
        if (!IsBeta && other.IsBeta) return 1;   // stable > beta
        if (IsBeta && !other.IsBeta) return -1;  // beta < stable

        // Both are beta — compare beta numbers
        return BetaNumber!.Value.CompareTo(other.BetaNumber!.Value);
    }

    // Regex: optional leading text (e.g. "HDRX-", "v"), then Major.Minor.Build, optional beta suffix
    private static readonly Regex VersionPattern = new(
        @"(\d+)\.(\d+)\.(\d+)(?:[\s-]*beta[\s-]*(\d+))?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Parses version strings such as "1.4.8", "1.4.8-beta1", "1.4.8 beta 1",
    /// "v1.4.8-beta2", "RHI-1.4.8-beta1". Returns false and logs when input is invalid.
    /// </summary>
    public static bool TryParse(string? input, out RdxcVersion version)
    {
        version = default;

        if (string.IsNullOrWhiteSpace(input))
        {
            CrashReporter.Log($"[RdxcVersion.TryParse] Cannot parse null or empty version string");
            return false;
        }

        var match = VersionPattern.Match(input);
        if (!match.Success)
        {
            CrashReporter.Log($"[RdxcVersion.TryParse] Cannot parse version from '{input}'");
            return false;
        }

        int major = int.Parse(match.Groups[1].Value);
        int minor = int.Parse(match.Groups[2].Value);
        int build = int.Parse(match.Groups[3].Value);
        int? betaNumber = match.Groups[4].Success ? int.Parse(match.Groups[4].Value) : null;

        version = new RdxcVersion(major, minor, build, betaNumber);
        return true;
    }

    /// <summary>
    /// Formats to display string: "1.4.8" for stable, "1.4.8 beta 1" for beta.
    /// </summary>
    public string ToDisplayString() =>
        IsBeta ? $"{Major}.{Minor}.{Build} beta {BetaNumber}" : $"{Major}.{Minor}.{Build}";
}

/// <summary>
/// Determines which update (if any) to offer based on current version,
/// latest stable, and latest beta.
/// </summary>
internal static class VersionResolver
{
    /// <summary>
    /// Returns the version to offer, or null if up to date.
    /// Rules:
    ///   - Stable always wins over beta at same or higher base version
    ///   - Beta only offered when its base version exceeds both latest stable's
    ///     and current version's base version
    ///   - Only offer if candidate's base version > current base version
    /// </summary>
    public static RdxcVersion? Resolve(
        RdxcVersion current,
        RdxcVersion? latestStable,
        RdxcVersion? latestBeta)
    {
        var currentBase = current.BaseVersion;

        // Determine if stable is a valid candidate (base version > current base version)
        bool stableValid = latestStable.HasValue
            && latestStable.Value.BaseVersion.CompareTo(currentBase) > 0;

        // Determine if beta is a valid candidate:
        //   - base version > current base version, OR
        //   - same base version but current is a beta with a lower beta number
        //   - base version > latest stable base version (beta only wins when ahead of stable)
        bool betaValid = latestBeta.HasValue
            && (latestBeta.Value.BaseVersion.CompareTo(currentBase) > 0
                || (latestBeta.Value.BaseVersion == currentBase
                    && current.IsBeta
                    && latestBeta.Value.CompareTo(current) > 0))
            && (!latestStable.HasValue
                || latestBeta.Value.BaseVersion.CompareTo(latestStable.Value.BaseVersion) > 0);

        // Beta takes priority when it's ahead of both stable and current
        if (betaValid)
            return latestBeta;

        if (stableValid)
            return latestStable;

        return null;
    }
}
