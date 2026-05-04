using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public partial class DxvkService
{
    // ── Pure helpers (staging) ────────────────────────────────────────────────

    /// <summary>
    /// Pure logic for update detection: returns true if and only if the remote
    /// version tag differs from the cached version tag (case-sensitive, ordinal).
    /// Extracted as a static helper for testability.
    /// </summary>
    internal static bool CheckHasUpdate(string? cachedTag, string? remoteTag) =>
        !string.Equals(cachedTag, remoteTag, StringComparison.Ordinal);

    /// <summary>
    /// Regex to find the .zip download URL on the nightly.link page.
    /// Matches: href="https://nightly.link/doitsujin/dxvk/.../.zip"
    /// </summary>
    private static readonly Regex NightlyZipUrlRegex = new(
        @"href=""(https://nightly\.link/doitsujin/dxvk/[^""]*\.zip)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Regex to extract the commit hash from the nightly artifact filename.
    /// Matches: dxvk-master-{hash}.zip
    /// </summary>
    private static readonly Regex NightlyCommitHashRegex = new(
        @"dxvk-master-([0-9a-f]+)\.zip",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // ── Nightly.link helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Fetches the nightly.link page and extracts the .zip download URL and commit hash.
    /// Returns (downloadUrl, commitHash) or (null, null) on failure.
    /// </summary>
    private async Task<(string? downloadUrl, string? commitHash)> FetchNightlyInfoAsync()
    {
        try
        {
            var html = await _http.GetStringAsync(NightlyLinkUrl).ConfigureAwait(false);

            var urlMatch = NightlyZipUrlRegex.Match(html);
            if (!urlMatch.Success)
            {
                CrashReporter.Log("[DxvkService] No .zip URL found on nightly.link page");
                return (null, null);
            }

            var downloadUrl = urlMatch.Groups[1].Value;

            // Extract commit hash from the filename in the URL
            var hashMatch = NightlyCommitHashRegex.Match(downloadUrl);
            var commitHash = hashMatch.Success ? hashMatch.Groups[1].Value : null;

            CrashReporter.Log($"[DxvkService] Nightly info: url={downloadUrl}, hash={commitHash ?? "(unknown)"}");
            return (downloadUrl, commitHash);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DxvkService] Failed to fetch nightly.link page — {ex.Message}");
            return (null, null);
        }
    }

    // ── Staging and update ────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task EnsureStagingAsync(IProgress<(string message, double percent)>? progress = null)
    {
        try
        {
            // ── 1. Skip if staging is already valid ──────────────────────────
            if (IsStagingReady)
            {
                CrashReporter.Log("[DxvkService.EnsureStagingAsync] Staging already valid — skipping download");
                progress?.Report(("DXVK staging ready", 100));
                return;
            }

            progress?.Report(("Checking DXVK release...", 5));

            // ── 2. Route based on variant ────────────────────────────────────
            if (_selectedVariant == DxvkVariant.Development)
            {
                await EnsureStagingNightlyAsync(progress).ConfigureAwait(false);
            }
            else
            {
                await EnsureStagingGitHubAsync(progress).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DxvkService.EnsureStagingAsync] Unexpected error — {ex.Message}");
        }
    }

    /// <summary>
    /// Handles staging for the Development variant via nightly.link.
    /// Downloads the latest master build .zip artifact.
    /// </summary>
    private async Task EnsureStagingNightlyAsync(IProgress<(string message, double percent)>? progress)
    {
        // ── Fetch nightly.link page to get download URL and commit hash ──
        var (downloadUrl, commitHash) = await FetchNightlyInfoAsync().ConfigureAwait(false);
        if (downloadUrl == null)
        {
            CrashReporter.Log("[DxvkService.EnsureStagingNightlyAsync] Could not resolve nightly download URL");
            return;
        }

        var versionTag = commitHash ?? "nightly-unknown";

        // ── Check if already up to date ──────────────────────────────────
        var cachedVersion = StagedVersion;
        if (cachedVersion != null
            && string.Equals(cachedVersion, versionTag, StringComparison.Ordinal)
            && IsStagingReady)
        {
            CrashReporter.Log($"[DxvkService.EnsureStagingNightlyAsync] Already up to date ({versionTag})");
            progress?.Report(("DXVK up to date", 100));
            return;
        }

        var assetName = $"dxvk-master-{versionTag}.zip";
        progress?.Report(($"Downloading DXVK nightly ({assetName})...", 10));
        CrashReporter.Log($"[DxvkService.EnsureStagingNightlyAsync] Downloading {assetName} from {downloadUrl}");

        // ── Download the .zip archive to a temp file ─────────────────────
        Directory.CreateDirectory(StagingDir);
        var tempArchive = Path.Combine(StagingDir, assetName + ".tmp");

        try
        {
            if (!await DownloadFileAsync(downloadUrl, tempArchive, progress, 10, 70).ConfigureAwait(false))
                return;
        }
        catch (Exception ex)
        {
            if (File.Exists(tempArchive)) try { File.Delete(tempArchive); } catch { }
            CrashReporter.Log($"[DxvkService.EnsureStagingNightlyAsync] Download exception — {ex.Message}");
            return;
        }

        // ── Extract the .zip archive (single pass) ──────────────────────
        progress?.Report(("Extracting DXVK...", 75));
        try
        {
            if (!await ExtractZipToStagingAsync(tempArchive).ConfigureAwait(false))
                return;
        }
        finally
        {
            if (File.Exists(tempArchive)) try { File.Delete(tempArchive); } catch { }
        }

        // ── Write version tag ────────────────────────────────────────────
        WriteVersionTag(versionTag);

        progress?.Report(("DXVK staging ready", 100));
        CrashReporter.Log("[DxvkService.EnsureStagingNightlyAsync] Staging complete");
    }

    /// <summary>
    /// Handles staging for the Stable variant via GitHub Releases API.
    /// Downloads the .tar.gz release asset.
    /// </summary>
    private async Task EnsureStagingGitHubAsync(IProgress<(string message, double percent)>? progress)
    {
        var apiUrl = StandardGitHubApi;

        // ── Fetch latest release metadata from GitHub API ────────────────
        string json;
        try
        {
            json = await _etagCache.GetWithETagAsync(_http, apiUrl).ConfigureAwait(false);
            if (json == null)
            {
                CrashReporter.Log("[DxvkService.EnsureStagingGitHubAsync] GitHub API returned error");
                return;
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DxvkService.EnsureStagingGitHubAsync] GitHub API request failed — {ex.Message}");
            return;
        }

        // ── Parse release — find the .tar.gz asset and tag ───────────────
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
                    if (name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
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
            CrashReporter.Log($"[DxvkService.EnsureStagingGitHubAsync] Failed to parse GitHub response — {ex.Message}");
            return;
        }

        if (assetName == null || downloadUrl == null)
        {
            CrashReporter.Log("[DxvkService.EnsureStagingGitHubAsync] No .tar.gz asset found in latest release");
            return;
        }

        // ── Check if already up to date ──────────────────────────────────
        var cachedVersion = StagedVersion;
        if (cachedVersion != null
            && string.Equals(cachedVersion, tagName, StringComparison.Ordinal)
            && IsStagingReady)
        {
            CrashReporter.Log($"[DxvkService.EnsureStagingGitHubAsync] Already up to date ({tagName})");
            progress?.Report(("DXVK up to date", 100));
            return;
        }

        progress?.Report(($"Downloading DXVK ({assetName})...", 10));
        CrashReporter.Log($"[DxvkService.EnsureStagingGitHubAsync] Downloading {assetName} from {downloadUrl}");

        // ── Download the .tar.gz archive to a temp file ──────────────────
        Directory.CreateDirectory(StagingDir);
        var tempArchive = Path.Combine(StagingDir, assetName + ".tmp");

        try
        {
            if (!await DownloadFileAsync(downloadUrl, tempArchive, progress, 10, 70).ConfigureAwait(false))
                return;
        }
        catch (Exception ex)
        {
            if (File.Exists(tempArchive)) try { File.Delete(tempArchive); } catch { }
            CrashReporter.Log($"[DxvkService.EnsureStagingGitHubAsync] Download exception — {ex.Message}");
            return;
        }

        // ── Extract the .tar.gz archive using bundled 7z.exe ─────────────
        // 7-Zip requires two passes for .tar.gz:
        //   Pass 1: 7z x archive.tar.gz → produces archive.tar
        //   Pass 2: 7z x archive.tar    → produces the directory tree
        progress?.Report(("Extracting DXVK...", 75));
        try
        {
            if (!await ExtractTarGzToStagingAsync(tempArchive).ConfigureAwait(false))
                return;
        }
        finally
        {
            if (File.Exists(tempArchive)) try { File.Delete(tempArchive); } catch { }
        }

        // ── Write version tag to version.txt ─────────────────────────────
        WriteVersionTag(tagName ?? "unknown");

        progress?.Report(("DXVK staging ready", 100));
        CrashReporter.Log("[DxvkService.EnsureStagingGitHubAsync] Staging complete");
    }

    // ── Shared download / extract helpers ─────────────────────────────────────

    /// <summary>
    /// Downloads a file from the given URL to the specified local path,
    /// reporting progress between <paramref name="pctStart"/> and <paramref name="pctEnd"/>.
    /// Returns true on success.
    /// </summary>
    private async Task<bool> DownloadFileAsync(
        string url, string destPath,
        IProgress<(string message, double percent)>? progress,
        double pctStart, double pctEnd)
    {
        var dlResp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        if (!dlResp.IsSuccessStatusCode)
        {
            CrashReporter.Log($"[DxvkService.DownloadFileAsync] Download failed ({dlResp.StatusCode})");
            return false;
        }

        var total = dlResp.Content.Headers.ContentLength ?? -1L;
        long downloaded = 0;
        var buf = new byte[1024 * 1024]; // 1 MB

        using (var net = await dlResp.Content.ReadAsStreamAsync().ConfigureAwait(false))
        using (var file = new FileStream(destPath, FileMode.Create, FileAccess.Write,
            FileShare.None, bufferSize: 1024 * 1024, useAsync: true))
        {
            int read;
            while ((read = await net.ReadAsync(buf).ConfigureAwait(false)) > 0)
            {
                await file.WriteAsync(buf.AsMemory(0, read)).ConfigureAwait(false);
                downloaded += read;
                if (total > 0)
                {
                    var pct = pctStart + (double)downloaded / total * (pctEnd - pctStart);
                    progress?.Report(($"Downloading DXVK... {downloaded / 1024} KB / {total / 1024} KB", pct));
                }
            }
        }

        CrashReporter.Log($"[DxvkService.DownloadFileAsync] Downloaded {downloaded} bytes");
        return true;
    }

    /// <summary>
    /// Extracts a .zip archive to the staging directory using a single 7-Zip pass.
    /// Locates the x64/ folder inside the archive and copies the content root to staging.
    /// </summary>
    private async Task<bool> ExtractZipToStagingAsync(string zipPath)
    {
        var tempExtractDir = Path.Combine(Path.GetTempPath(), $"RHI_dxvk_{Guid.NewGuid():N}");

        try
        {
            var sevenZipExe = Find7ZipExe();
            if (sevenZipExe == null)
            {
                CrashReporter.Log("[DxvkService.ExtractZipToStagingAsync] 7-Zip not found — cannot extract archive");
                return false;
            }

            Directory.CreateDirectory(tempExtractDir);

            // Single pass: extract .zip → directory tree
            var result = await Run7ZipExtractAsync(sevenZipExe, zipPath, tempExtractDir).ConfigureAwait(false);
            if (result == null)
            {
                CrashReporter.Log("[DxvkService.ExtractZipToStagingAsync] 7z extraction failed");
                return false;
            }

            return CopyExtractedToStaging(tempExtractDir);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DxvkService.ExtractZipToStagingAsync] Extraction failed — {ex.Message}");
            return false;
        }
        finally
        {
            try { Directory.Delete(tempExtractDir, recursive: true); }
            catch (Exception ex)
            {
                CrashReporter.Log($"[DxvkService.ExtractZipToStagingAsync] Failed to clean up temp dir — {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Extracts a .tar.gz archive to the staging directory using two 7-Zip passes.
    /// Pass 1: .tar.gz → .tar, Pass 2: .tar → directory tree.
    /// </summary>
    private async Task<bool> ExtractTarGzToStagingAsync(string tarGzPath)
    {
        var tempExtractDir = Path.Combine(Path.GetTempPath(), $"RHI_dxvk_{Guid.NewGuid():N}");

        try
        {
            var sevenZipExe = Find7ZipExe();
            if (sevenZipExe == null)
            {
                CrashReporter.Log("[DxvkService.ExtractTarGzToStagingAsync] 7-Zip not found — cannot extract archive");
                return false;
            }

            Directory.CreateDirectory(tempExtractDir);

            // Pass 1: decompress .tar.gz → .tar
            var tarPath = await Run7ZipExtractAsync(sevenZipExe, tarGzPath, tempExtractDir).ConfigureAwait(false);
            if (tarPath == null)
            {
                CrashReporter.Log("[DxvkService.ExtractTarGzToStagingAsync] Pass 1 (.tar.gz → .tar) failed");
                return false;
            }

            // Pass 2: extract .tar → directory tree
            var tarExtractDir = Path.Combine(Path.GetTempPath(), $"RHI_dxvk_tar_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tarExtractDir);

            try
            {
                var innerResult = await Run7ZipExtractAsync(sevenZipExe, tarPath, tarExtractDir).ConfigureAwait(false);
                if (innerResult == null)
                {
                    CrashReporter.Log("[DxvkService.ExtractTarGzToStagingAsync] Pass 2 (.tar → files) failed");
                    return false;
                }

                return CopyExtractedToStaging(tarExtractDir);
            }
            finally
            {
                try { Directory.Delete(tarExtractDir, recursive: true); }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[DxvkService.ExtractTarGzToStagingAsync] Failed to clean up tar extract dir — {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DxvkService.ExtractTarGzToStagingAsync] Extraction failed — {ex.Message}");
            return false;
        }
        finally
        {
            try { Directory.Delete(tempExtractDir, recursive: true); }
            catch (Exception ex)
            {
                CrashReporter.Log($"[DxvkService.ExtractTarGzToStagingAsync] Failed to clean up temp dir — {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Finds the x64/ folder in the extracted directory tree, then copies
    /// the content root (parent of x64/) into the staging directory.
    /// </summary>
    private static bool CopyExtractedToStaging(string extractedDir)
    {
        var x64Candidates = Directory.GetDirectories(extractedDir, "x64", SearchOption.AllDirectories);
        if (x64Candidates.Length == 0)
        {
            CrashReporter.Log("[DxvkService.CopyExtractedToStaging] x64/ folder not found in extracted archive");
            return false;
        }

        // The content root is the parent of x64/
        var contentRoot = Path.GetDirectoryName(x64Candidates[0])!;

        // Clear existing staging contents before copying new files
        foreach (var existingFile in Directory.GetFiles(StagingDir))
        {
            try { File.Delete(existingFile); } catch { }
        }
        foreach (var existingDir in Directory.GetDirectories(StagingDir))
        {
            try { Directory.Delete(existingDir, recursive: true); } catch { }
        }

        // Copy all files from the content root to staging,
        // preserving x64/ and x32/ structure
        foreach (var file in Directory.GetFiles(contentRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(contentRoot, file);
            var destPath = Path.Combine(StagingDir, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            File.Copy(file, destPath, overwrite: true);
        }

        CrashReporter.Log($"[DxvkService.CopyExtractedToStaging] Extracted to staging from {contentRoot}");
        return true;
    }

    /// <summary>
    /// Writes the version tag to version.txt in the staging directory.
    /// </summary>
    private static void WriteVersionTag(string tag)
    {
        try
        {
            File.WriteAllText(VersionFilePath, tag);
            CrashReporter.Log($"[DxvkService] Version tag written: {tag}");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DxvkService] Failed to write version file — {ex.Message}");
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Runs 7z.exe to extract an archive to the specified output directory.
    /// Returns the path to the first extracted file (useful for .tar.gz two-pass),
    /// or null if extraction failed.
    /// </summary>
    private static async Task<string?> Run7ZipExtractAsync(
        string sevenZipExe, string archivePath, string outputDir)
    {
        var psi = new ProcessStartInfo
        {
            FileName = sevenZipExe,
            Arguments = $"x \"{archivePath}\" -o\"{outputDir}\" -y",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        CrashReporter.Log($"[DxvkService.Run7ZipExtractAsync] Running {psi.FileName} {psi.Arguments}");

        using var proc = Process.Start(psi);
        if (proc == null)
        {
            CrashReporter.Log("[DxvkService.Run7ZipExtractAsync] Failed to start 7z process");
            return null;
        }

        var stderrTask = proc.StandardError.ReadToEndAsync();
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        proc.WaitForExit(120_000); // 120 second timeout

        var stderr = await stderrTask;
        if (!string.IsNullOrWhiteSpace(stderr))
            CrashReporter.Log($"[DxvkService.Run7ZipExtractAsync] 7z stderr: {stderr}");

        if (proc.ExitCode != 0)
        {
            CrashReporter.Log($"[DxvkService.Run7ZipExtractAsync] 7z exit code {proc.ExitCode}");
            return null;
        }

        // Return the first file found in the output directory
        var files = Directory.GetFiles(outputDir, "*", SearchOption.AllDirectories);
        return files.Length > 0 ? files[0] : outputDir;
    }

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
            if (_selectedVariant == DxvkVariant.Development)
            {
                await CheckForUpdateNightlyAsync().ConfigureAwait(false);
            }
            else
            {
                await CheckForUpdateGitHubAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DxvkService.CheckForUpdateAsync] Unexpected error — {ex.Message}");
        }
    }

    /// <summary>
    /// Checks for updates for the Development variant by fetching the nightly.link page
    /// and comparing the commit hash with the cached version.
    /// </summary>
    private async Task CheckForUpdateNightlyAsync()
    {
        var (_, commitHash) = await FetchNightlyInfoAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(commitHash))
        {
            CrashReporter.Log("[DxvkService.CheckForUpdateNightlyAsync] Could not resolve nightly commit hash");
            return;
        }

        var cachedTag = StagedVersion;
        HasUpdate = CheckHasUpdate(cachedTag, commitHash);

        CrashReporter.Log($"[DxvkService.CheckForUpdateNightlyAsync] Cached={cachedTag ?? "(none)"}, Remote={commitHash}, HasUpdate={HasUpdate}");
    }

    /// <summary>
    /// Checks for updates for the Stable variant via GitHub Releases API.
    /// </summary>
    private async Task CheckForUpdateGitHubAsync()
    {
        // ── 1. GitHub API URL for the stable variant ─────────────────────
        var apiUrl = StandardGitHubApi;

        // ── 2. Fetch latest release tag from GitHub API ──────────────────
        string json;
        try
        {
            json = await _etagCache.GetWithETagAsync(_http, apiUrl).ConfigureAwait(false);
            if (json == null)
            {
                CrashReporter.Log("[DxvkService.CheckForUpdateGitHubAsync] GitHub API returned error");
                return;
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DxvkService.CheckForUpdateGitHubAsync] GitHub API request failed — {ex.Message}");
            return;
        }

        // ── 3. Extract tag_name from the response ────────────────────────
        string? remoteTag = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("tag_name", out var tagEl))
                remoteTag = tagEl.GetString();
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DxvkService.CheckForUpdateGitHubAsync] Failed to parse GitHub response — {ex.Message}");
            return;
        }

        if (string.IsNullOrEmpty(remoteTag))
        {
            CrashReporter.Log("[DxvkService.CheckForUpdateGitHubAsync] No tag_name found in latest release");
            return;
        }

        // ── 4. Compare with cached version tag (case-sensitive) ──────────
        var cachedTag = StagedVersion;
        HasUpdate = CheckHasUpdate(cachedTag, remoteTag);

        CrashReporter.Log($"[DxvkService.CheckForUpdateGitHubAsync] Cached={cachedTag ?? "(none)"}, Remote={remoteTag}, HasUpdate={HasUpdate}");
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
                    CrashReporter.Log($"[DxvkService.ClearStaging] Failed to delete file '{file}' — {ex.Message}");
                }
            }

            // Delete all subdirectories in the staging directory
            foreach (var dir in Directory.GetDirectories(StagingDir))
            {
                try { Directory.Delete(dir, recursive: true); }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[DxvkService.ClearStaging] Failed to delete directory '{dir}' — {ex.Message}");
                }
            }

            CrashReporter.Log("[DxvkService.ClearStaging] Staging folder cleared");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DxvkService.ClearStaging] Unexpected error — {ex.Message}");
        }
    }
}
