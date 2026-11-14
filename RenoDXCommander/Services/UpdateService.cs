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

    // Betas are published as normal (not pre-release) releases in RankFTW/rhi-beta, which holds
    // only release files; the code stays in RankFTW/RHI. /releases/latest skips pre-releases.
    private const string BetaReleaseApiUrl =
        "https://api.github.com/repos/RankFTW/rhi-beta/releases/latest";

    // Website (not API) addresses of the latest releases: redirect to /releases/tag/<tag>. Not rate limited.
    private const string LatestReleaseWebUrl = "https://github.com/RankFTW/RHI/releases/latest";
    private const string LatestBetaWebUrl    = "https://github.com/RankFTW/rhi-beta/releases/latest";

    // The asset filenames to look for when updating (checked in order).
    private static readonly string[] InstallerFileNames = ["RHI-Setup.exe", "RDXC-Setup.exe"];

    /// <summary>The assembly number (e.g. 2.9.5.0; the same for every beta of a version). See <see cref="AppVersionInfo"/>.</summary>
    public Version CurrentVersion => AppVersionInfo.Number;

    /// <summary>
    /// Checks GitHub for a newer RHI. Looks at the latest release in RankFTW/RHI and, when
    /// <paramref name="betaOptIn"/> is true, the latest beta in RankFTW/rhi-beta, and offers the newest
    /// one if it's newer than this build (<see cref="VersionResolver"/>). Null when up to date or the
    /// check fails.
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdateAsync(bool betaOptIn = false)
    {
        try
        {
            // Release: /releases/latest first, then the old tag-based endpoints
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
            // The API answer can be a stale cached copy (rate limited, expired token, GitHub error):
            // GitHubETagCache returns its saved body then. The website's redirect isn't rate limited,
            // so check it too and use whichever is newer.
            stable = Newer(stable, await FetchLatestViaWebAsync(LatestReleaseWebUrl).ConfigureAwait(false), "release");

            // Beta: only for users who turned on "Get beta updates"; failures fall back to releases only
            (RdxcVersion version, string downloadUrl)? beta = null;
            if (betaOptIn)
            {
                try
                {
                    beta = await FetchReleaseAsync(BetaReleaseApiUrl).ConfigureAwait(false);
                    beta = Newer(beta, await FetchLatestViaWebAsync(LatestBetaWebUrl).ConfigureAwait(false), "beta");
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException or InvalidOperationException)
                {
                    CrashReporter.Log($"[UpdateService.CheckForUpdateAsync] Beta check failed — {ex.Message}");
                }
            }

            var current = AppVersionInfo.Current;
            RdxcVersion? winner = VersionResolver.Resolve(current, stable?.version, beta?.version);

            if (winner == null)
            {
                CrashReporter.Log($"[UpdateService.CheckForUpdateAsync] Up to date (local={current.ToDisplayString()}, release={stable?.version.ToDisplayString() ?? "?"}{(betaOptIn ? $", beta={beta?.version.ToDisplayString() ?? "?"}" : "")})");
                return null;
            }

            // Download URL from the source that won (a beta can't equal a release: release > beta)
            string? winnerDownloadUrl =
                stable.HasValue && winner.Value == stable.Value.version ? stable.Value.downloadUrl
                : beta.HasValue && winner.Value == beta.Value.version ? beta.Value.downloadUrl
                : null;

            if (string.IsNullOrEmpty(winnerDownloadUrl))
            {
                CrashReporter.Log($"[UpdateService.CheckForUpdateAsync] Winner {winner.Value.ToDisplayString()} has no download URL");
                return null;
            }

            CrashReporter.Log($"[UpdateService.CheckForUpdateAsync] Update available {current.ToDisplayString()} → {winner.Value.ToDisplayString()}");
            return new UpdateInfo
            {
                CurrentVersion = new Version(current.Major, current.Minor, current.Build, current.Revision),
                RemoteVersion  = new Version(winner.Value.Major, winner.Value.Minor, winner.Value.Build, winner.Value.Revision),
                DownloadUrl    = winnerDownloadUrl,
                DisplayVersion = winner.Value.ToDisplayString(),
                CurrentDisplayVersion = current.ToDisplayString(),
                IsBeta         = winner.Value.IsBeta,
            };
        }
        catch (Exception ex)
        {
            // Update check should never crash the app — swallow and log
            CrashReporter.Log($"[UpdateService.CheckForUpdateAsync] Check failed — {ex.Message}");
            return null;
        }
    }

    /// <summary>The newer of the API answer and the website's; logs when the website's wins (API copy stale).</summary>
    private static (RdxcVersion version, string downloadUrl)? Newer(
        (RdxcVersion version, string downloadUrl)? api, (RdxcVersion version, string downloadUrl)? web, string what)
    {
        if (web == null) return api;
        if (api != null && web.Value.version.CompareTo(api.Value.version) <= 0) return api;
        if (api != null)
            CrashReporter.Log($"[UpdateService.CheckForUpdateAsync] API said {what} {api.Value.version.ToDisplayString()} (cached/stale), website says {web.Value.version.ToDisplayString()} — using the website's");
        return web;
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
    /// Finds the latest release of a repo (RHI or rhi-beta) from the website's redirect (no API call)
    /// and builds the installer URL from its tag. Returns null on failure or when the repo has no release.
    /// </summary>
    private async Task<(RdxcVersion version, string downloadUrl)?> FetchLatestViaWebAsync(string latestWebUrl)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var req = new HttpRequestMessage(HttpMethod.Get, latestWebUrl);
            req.Headers.TryAddWithoutValidation("User-Agent", $"RHI/{CurrentVersion}");
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            var finalUrl = resp.RequestMessage?.RequestUri?.AbsoluteUri ?? "";
            const string marker = "/releases/tag/";
            var idx = finalUrl.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (!resp.IsSuccessStatusCode || idx < 0)
            {
                CrashReporter.Log($"[UpdateService.FetchLatestViaWebAsync] No release tag (HTTP {(int)resp.StatusCode}, url '{finalUrl}')");
                return null;
            }
            var tag = Uri.UnescapeDataString(finalUrl[(idx + marker.Length)..].TrimEnd('/'));
            if (!RdxcVersion.TryParse(tag, out var version)) return null;
            // .../<owner>/<repo>/releases/latest → .../<owner>/<repo>/releases/download/<tag>/RHI-Setup.exe
            var repoBase = latestWebUrl[..latestWebUrl.LastIndexOf("/releases/", StringComparison.OrdinalIgnoreCase)];
            var downloadUrl = $"{repoBase}/releases/download/{Uri.EscapeDataString(tag)}/{InstallerFileNames[0]}";
            CrashReporter.Log($"[UpdateService.FetchLatestViaWebAsync] Latest on the website ({repoBase}): {tag}");
            return (version, downloadUrl);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            CrashReporter.Log($"[UpdateService.FetchLatestViaWebAsync] Failed — {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Downloads the installer to a temp file and returns the file path.
    /// Reports progress via the optional callback.
    /// </summary>
    public async Task<string?> DownloadInstallerAsync(
        string downloadUrl,
        IProgress<(string msg, double pct)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string? tempPath = null;
        try
        {
            // HttpClient.Timeout ends at the headers with ResponseHeadersRead.
            // Apply a deadline to the body reads/writes as well as shutdown cancellation.
            using var transfer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            transfer.CancelAfter(TimeSpan.FromMinutes(10));
            var token = transfer.Token;
            progress?.Report(("Downloading update...", 0));

            using var request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("RHI", CurrentVersion.ToString()));

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            // Derive the installer filename from the download URL, falling back to RHI-Setup.exe
            var fileName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
            if (string.IsNullOrEmpty(fileName)) fileName = InstallerFileNames[0];
            tempPath = Path.Combine(Path.GetTempPath(), fileName);

            await using var contentStream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 1024 * 1024, useAsync: true);

            var buffer = new byte[1024 * 1024]; // 1 MB
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), token).ConfigureAwait(false);
                totalRead += bytesRead;

                if (totalBytes > 0)
                {
                    var pct = (double)totalRead / totalBytes * 100;
                    progress?.Report(($"Downloading update... {pct:F0}%", pct));
                }
            }

            if (totalBytes >= 0 && totalRead != totalBytes)
                throw new IOException($"Incomplete installer download: expected {totalBytes} bytes, received {totalRead}.");
            progress?.Report(("Download complete.", 100));
            CrashReporter.Log($"[UpdateService.DownloadInstallerAsync] Downloaded installer to {tempPath} ({totalRead:N0} bytes)");
            return tempPath;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[UpdateService.DownloadInstallerAsync] Download failed — {ex.Message}");
            if (tempPath != null)
            {
                try { File.Delete(tempPath); }
                catch (Exception cleanupError) { CrashReporter.Log($"[UpdateService] Partial download cleanup failed — {cleanupError.Message}"); }
            }
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
    /// <summary>The installed version for people ("2.9.5 beta 1").</summary>
    public string? CurrentDisplayVersion { get; init; }
    /// <summary>True when the offered update is a beta.</summary>
    public bool IsBeta { get; init; }
}
