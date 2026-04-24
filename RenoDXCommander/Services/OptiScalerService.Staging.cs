using System.Diagnostics;
using System.Text.Json;

namespace RenoDXCommander.Services;

public partial class OptiScalerService
{
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
                json = await _etagCache.GetWithETagAsync(_http, GitHubReleasesApi).ConfigureAwait(false);
                if (json == null)
                {
                    CrashReporter.Log($"[OptiScalerService.EnsureStagingAsync] GitHub API returned error");
                    return;
                }
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
                json = await _etagCache.GetWithETagAsync(_http, GitHubReleasesApi).ConfigureAwait(false);
                if (json == null)
                {
                    CrashReporter.Log($"[OptiScalerService.CheckForUpdateAsync] GitHub API returned error");
                    return;
                }
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
                json = await _etagCache.GetWithETagAsync(_http, OptiPatcherReleasesApi).ConfigureAwait(false);
                if (json == null)
                {
                    CrashReporter.Log($"[OptiScalerService.EnsureOptiPatcherStagingAsync] GitHub API returned error");
                    return;
                }
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
                json = await _etagCache.GetWithETagAsync(_http, OptiPatcherReleasesApi).ConfigureAwait(false);
                if (json == null)
                {
                    CrashReporter.Log($"[OptiScalerService.CheckOptiPatcherUpdateAsync] GitHub API returned error");
                    return false;
                }
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

    // ── DLSS DLL staging and update ───────────────────────────────────────────

    /// <summary>
    /// Downloads the latest nvngx_dlss.dll from the DLSS Swapper manifest to the staging folder.
    /// The manifest is a JSON file hosted on GitHub that contains structured records with
    /// direct download URLs (Cloudflare R2 CDN) for every known DLSS DLL version.
    /// No-op if staging is already valid and up to date.
    /// </summary>
    public async Task EnsureDlssStagingAsync(IProgress<(string message, double percent)>? progress = null)
    {
        try
        {
            progress?.Report(("Checking DLSS release...", 0));

            // ── 1. Fetch manifest from GitHub ────────────────────────────────
            string manifestJson;
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Get, DlssManifestUrl);
                req.Headers.Add("User-Agent", "RHI");
                var resp = await _http.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                {
                    CrashReporter.Log($"[OptiScalerService.EnsureDlssStagingAsync] Manifest fetch returned {resp.StatusCode}");
                    return;
                }
                manifestJson = await resp.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[OptiScalerService.EnsureDlssStagingAsync] Manifest fetch failed — {ex.Message}");
                return;
            }

            // ── 2. Parse manifest — find the latest stable non-dev DLSS record ──
            string? latestVersion = null;
            string? downloadUrl = null;
            string? md5Hash = null;
            try
            {
                using var doc = JsonDocument.Parse(manifestJson);
                if (doc.RootElement.TryGetProperty("dlss", out var dlssArray))
                {
                    // Records are ordered oldest-first; find the latest non-dev stable entry
                    foreach (var record in dlssArray.EnumerateArray())
                    {
                        // Skip dev files (larger debug builds not intended for end users)
                        if (record.TryGetProperty("is_dev_file", out var isDevEl) && isDevEl.GetBoolean())
                            continue;

                        var version = record.TryGetProperty("version", out var vEl) ? vEl.GetString() : null;
                        var url = record.TryGetProperty("download_url", out var urlEl) ? urlEl.GetString() : null;
                        var hash = record.TryGetProperty("md5_hash", out var hashEl) ? hashEl.GetString() : null;

                        if (!string.IsNullOrEmpty(version) && !string.IsNullOrEmpty(url))
                        {
                            latestVersion = version;
                            downloadUrl = url;
                            md5Hash = hash;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[OptiScalerService.EnsureDlssStagingAsync] Failed to parse manifest — {ex.Message}");
                return;
            }

            if (latestVersion == null || downloadUrl == null)
            {
                CrashReporter.Log("[OptiScalerService.EnsureDlssStagingAsync] No stable DLSS record found in manifest");
                return;
            }

            // ── 3. Check if already up to date ──────────────────────────────
            var cachedVersion = File.Exists(DlssVersionPath)
                ? File.ReadAllText(DlssVersionPath).Trim()
                : null;
            var stagedDll = Path.Combine(DlssStagingDir, DlssDllFileName);

            bool dlssUpToDate = cachedVersion != null
                && string.Equals(cachedVersion, latestVersion, StringComparison.Ordinal)
                && File.Exists(stagedDll);

            // ── 4. Download DLSS SR if needed ────────────────────────────────
            Directory.CreateDirectory(DlssStagingDir);

            if (dlssUpToDate)
            {
                CrashReporter.Log($"[OptiScalerService.EnsureDlssStagingAsync] DLSS SR already up to date ({latestVersion})");
            }
            else
            {
                progress?.Report(($"Downloading DLSS {latestVersion}...", 20));
                CrashReporter.Log($"[OptiScalerService.EnsureDlssStagingAsync] Downloading DLSS {latestVersion} from {downloadUrl}");
            var tempZip = Path.Combine(DlssStagingDir, $"dlss_{latestVersion}.zip.tmp");
            try
            {
                var dlResp = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                if (!dlResp.IsSuccessStatusCode)
                {
                    CrashReporter.Log($"[OptiScalerService.EnsureDlssStagingAsync] Download failed ({dlResp.StatusCode})");
                    return;
                }

                var total = dlResp.Content.Headers.ContentLength ?? -1L;
                long downloaded = 0;
                var buf = new byte[512 * 1024]; // 512 KB

                using (var net = await dlResp.Content.ReadAsStreamAsync())
                using (var file = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 512 * 1024, useAsync: true))
                {
                    int read;
                    while ((read = await net.ReadAsync(buf)) > 0)
                    {
                        await file.WriteAsync(buf.AsMemory(0, read));
                        downloaded += read;
                        if (total > 0)
                        {
                            var pct = 20 + (double)downloaded / total * 50; // 20–70%
                            progress?.Report(($"Downloading DLSS... {downloaded / 1024} KB / {total / 1024} KB", pct));
                        }
                    }
                }

                CrashReporter.Log($"[OptiScalerService.EnsureDlssStagingAsync] Downloaded {downloaded} bytes");
            }
            catch (Exception ex)
            {
                if (File.Exists(tempZip)) try { File.Delete(tempZip); } catch { }
                CrashReporter.Log($"[OptiScalerService.EnsureDlssStagingAsync] Download exception — {ex.Message}");
                return;
            }

            // ── 5. Extract nvngx_dlss.dll from the zip ──────────────────────
            progress?.Report(("Extracting DLSS DLL...", 75));
            try
            {
                using var archive = SharpCompress.Archives.ArchiveFactory.Open(tempZip);
                var dllEntry = archive.Entries.FirstOrDefault(e =>
                    !e.IsDirectory &&
                    Path.GetFileName(e.Key ?? "").Equals(DlssDllFileName, StringComparison.OrdinalIgnoreCase));

                if (dllEntry == null)
                {
                    CrashReporter.Log("[OptiScalerService.EnsureDlssStagingAsync] nvngx_dlss.dll not found in zip");
                    return;
                }

                using (var entryStream = dllEntry.OpenEntryStream())
                using (var outFile = new FileStream(stagedDll, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await entryStream.CopyToAsync(outFile);
                }

                CrashReporter.Log($"[OptiScalerService.EnsureDlssStagingAsync] Extracted {DlssDllFileName} ({new FileInfo(stagedDll).Length} bytes)");
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[OptiScalerService.EnsureDlssStagingAsync] Extraction failed — {ex.Message}");
                return;
            }
            finally
            {
                if (File.Exists(tempZip)) try { File.Delete(tempZip); } catch { }
            }

            // ── Write DLSS SR version ────────────────────────────────────────
            try
            {
                File.WriteAllText(DlssVersionPath, latestVersion);
                CrashReporter.Log($"[OptiScalerService.EnsureDlssStagingAsync] DLSS SR version written: {latestVersion}");
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[OptiScalerService.EnsureDlssStagingAsync] Failed to write DLSS SR version — {ex.Message}");
            }
            } // end of DLSS SR download else block

            // ── 6. Download and extract DLSS-D (Ray Reconstruction) ──────────
            // Uses its own version tracking so it can update independently.
            progress?.Report(("Checking DLSS Ray Reconstruction...", 78));
            try
            {
                string? dlssdVersion = null;
                string? dlssdDownloadUrl = null;

                using var dlssdDoc = JsonDocument.Parse(manifestJson);
                if (dlssdDoc.RootElement.TryGetProperty("dlss_d", out var dlssdArray))
                {
                    foreach (var record in dlssdArray.EnumerateArray())
                    {
                        if (record.TryGetProperty("is_dev_file", out var isDevEl) && isDevEl.GetBoolean())
                            continue;
                        var version = record.TryGetProperty("version", out var vEl) ? vEl.GetString() : null;
                        var url = record.TryGetProperty("download_url", out var urlEl) ? urlEl.GetString() : null;
                        if (!string.IsNullOrEmpty(version) && !string.IsNullOrEmpty(url))
                        {
                            dlssdVersion = version;
                            dlssdDownloadUrl = url;
                        }
                    }
                }

                // Check if DLSS-D is already up to date
                var cachedDlssdVersion = File.Exists(DlssdVersionPath)
                    ? File.ReadAllText(DlssdVersionPath).Trim()
                    : null;
                var dlssdDestPath = Path.Combine(DlssStagingDir, DlssdDllFileName);
                bool dlssdUpToDate = cachedDlssdVersion != null
                    && dlssdVersion != null
                    && string.Equals(cachedDlssdVersion, dlssdVersion, StringComparison.Ordinal)
                    && File.Exists(dlssdDestPath);

                if (dlssdUpToDate)
                {
                    CrashReporter.Log($"[OptiScalerService.EnsureDlssStagingAsync] DLSS-D already up to date ({dlssdVersion})");
                }
                else if (dlssdDownloadUrl != null)
                {
                    progress?.Report(($"Downloading DLSS Ray Reconstruction {dlssdVersion}...", 80));
                    CrashReporter.Log($"[OptiScalerService.EnsureDlssStagingAsync] Downloading DLSS-D {dlssdVersion} from {dlssdDownloadUrl}");

                    var tempDlssdZip = Path.Combine(DlssStagingDir, $"dlssd_{dlssdVersion}.zip.tmp");
                    try
                    {
                        var dlssdResp = await _http.GetAsync(dlssdDownloadUrl);
                        if (dlssdResp.IsSuccessStatusCode)
                        {
                            var dlssdBytes = await dlssdResp.Content.ReadAsByteArrayAsync();
                            await File.WriteAllBytesAsync(tempDlssdZip, dlssdBytes);

                            using var dlssdArchive = SharpCompress.Archives.ArchiveFactory.Open(tempDlssdZip);
                            var dlssdEntry = dlssdArchive.Entries.FirstOrDefault(e =>
                                !e.IsDirectory &&
                                Path.GetFileName(e.Key ?? "").Equals(DlssdDllFileName, StringComparison.OrdinalIgnoreCase));

                            if (dlssdEntry != null)
                            {
                                using var entryStream = dlssdEntry.OpenEntryStream();
                                using var outFile = new FileStream(dlssdDestPath, FileMode.Create, FileAccess.Write, FileShare.None);
                                await entryStream.CopyToAsync(outFile);
                                CrashReporter.Log($"[OptiScalerService.EnsureDlssStagingAsync] Extracted {DlssdDllFileName} v{dlssdVersion} ({new FileInfo(dlssdDestPath).Length} bytes)");

                                // Write DLSS-D version
                                File.WriteAllText(DlssdVersionPath, dlssdVersion!);
                                CrashReporter.Log($"[OptiScalerService.EnsureDlssStagingAsync] DLSS-D version written: {dlssdVersion}");
                            }
                            else
                            {
                                CrashReporter.Log("[OptiScalerService.EnsureDlssStagingAsync] nvngx_dlssd.dll not found in zip");
                            }
                        }
                        else
                        {
                            CrashReporter.Log($"[OptiScalerService.EnsureDlssStagingAsync] DLSS-D download failed ({dlssdResp.StatusCode})");
                        }
                    }
                    finally
                    {
                        if (File.Exists(tempDlssdZip)) try { File.Delete(tempDlssdZip); } catch { }
                    }
                }
                else
                {
                    CrashReporter.Log("[OptiScalerService.EnsureDlssStagingAsync] No stable DLSS-D record found in manifest");
                }
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[OptiScalerService.EnsureDlssStagingAsync] DLSS-D staging failed — {ex.Message}");
            }

            // ── 7. Download and extract DLSS-G (Frame Generation) ────────────
            progress?.Report(("Checking DLSS Frame Generation...", 90));
            try
            {
                string? dlssgVersion = null;
                string? dlssgDownloadUrl = null;

                using var dlssgDoc = JsonDocument.Parse(manifestJson);
                if (dlssgDoc.RootElement.TryGetProperty("dlss_g", out var dlssgArray))
                {
                    foreach (var record in dlssgArray.EnumerateArray())
                    {
                        if (record.TryGetProperty("is_dev_file", out var isDevEl) && isDevEl.GetBoolean())
                            continue;
                        var version = record.TryGetProperty("version", out var vEl) ? vEl.GetString() : null;
                        var url = record.TryGetProperty("download_url", out var urlEl) ? urlEl.GetString() : null;
                        if (!string.IsNullOrEmpty(version) && !string.IsNullOrEmpty(url))
                        {
                            dlssgVersion = version;
                            dlssgDownloadUrl = url;
                        }
                    }
                }

                var cachedDlssgVersion = File.Exists(DlssgVersionPath)
                    ? File.ReadAllText(DlssgVersionPath).Trim()
                    : null;
                var dlssgDestPath = Path.Combine(DlssStagingDir, DlssgDllFileName);
                bool dlssgUpToDate = cachedDlssgVersion != null
                    && dlssgVersion != null
                    && string.Equals(cachedDlssgVersion, dlssgVersion, StringComparison.Ordinal)
                    && File.Exists(dlssgDestPath);

                if (dlssgUpToDate)
                {
                    CrashReporter.Log($"[OptiScalerService.EnsureDlssStagingAsync] DLSS-G already up to date ({dlssgVersion})");
                }
                else if (dlssgDownloadUrl != null)
                {
                    progress?.Report(($"Downloading DLSS Frame Generation {dlssgVersion}...", 92));
                    CrashReporter.Log($"[OptiScalerService.EnsureDlssStagingAsync] Downloading DLSS-G {dlssgVersion} from {dlssgDownloadUrl}");

                    var tempDlssgZip = Path.Combine(DlssStagingDir, $"dlssg_{dlssgVersion}.zip.tmp");
                    try
                    {
                        var dlssgResp = await _http.GetAsync(dlssgDownloadUrl);
                        if (dlssgResp.IsSuccessStatusCode)
                        {
                            var dlssgBytes = await dlssgResp.Content.ReadAsByteArrayAsync();
                            await File.WriteAllBytesAsync(tempDlssgZip, dlssgBytes);

                            using var dlssgArchive = SharpCompress.Archives.ArchiveFactory.Open(tempDlssgZip);
                            var dlssgEntry = dlssgArchive.Entries.FirstOrDefault(e =>
                                !e.IsDirectory &&
                                Path.GetFileName(e.Key ?? "").Equals(DlssgDllFileName, StringComparison.OrdinalIgnoreCase));

                            if (dlssgEntry != null)
                            {
                                using var entryStream = dlssgEntry.OpenEntryStream();
                                using var outFile = new FileStream(dlssgDestPath, FileMode.Create, FileAccess.Write, FileShare.None);
                                await entryStream.CopyToAsync(outFile);
                                CrashReporter.Log($"[OptiScalerService.EnsureDlssStagingAsync] Extracted {DlssgDllFileName} v{dlssgVersion} ({new FileInfo(dlssgDestPath).Length} bytes)");

                                File.WriteAllText(DlssgVersionPath, dlssgVersion!);
                                CrashReporter.Log($"[OptiScalerService.EnsureDlssStagingAsync] DLSS-G version written: {dlssgVersion}");
                            }
                            else
                            {
                                CrashReporter.Log("[OptiScalerService.EnsureDlssStagingAsync] nvngx_dlssg.dll not found in zip");
                            }
                        }
                        else
                        {
                            CrashReporter.Log($"[OptiScalerService.EnsureDlssStagingAsync] DLSS-G download failed ({dlssgResp.StatusCode})");
                        }
                    }
                    finally
                    {
                        if (File.Exists(tempDlssgZip)) try { File.Delete(tempDlssgZip); } catch { }
                    }
                }
                else
                {
                    CrashReporter.Log("[OptiScalerService.EnsureDlssStagingAsync] No stable DLSS-G record found in manifest");
                }
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[OptiScalerService.EnsureDlssStagingAsync] DLSS-G staging failed — {ex.Message}");
            }

            progress?.Report(("DLSS staging ready", 100));
            CrashReporter.Log("[OptiScalerService.EnsureDlssStagingAsync] Staging complete");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[OptiScalerService.EnsureDlssStagingAsync] Unexpected error — {ex.Message}");
        }
    }

    /// <summary>
    /// Checks the DLSS Swapper manifest for a newer DLSS version than the staged one.
    /// Returns true if an update is available.
    /// </summary>
    public async Task<bool> CheckDlssUpdateAsync()
    {
        try
        {
            string manifestJson;
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Get, DlssManifestUrl);
                req.Headers.Add("User-Agent", "RHI");
                var resp = await _http.SendAsync(req);
                if (!resp.IsSuccessStatusCode)
                {
                    CrashReporter.Log($"[OptiScalerService.CheckDlssUpdateAsync] Manifest fetch returned {resp.StatusCode}");
                    return false;
                }
                manifestJson = await resp.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[OptiScalerService.CheckDlssUpdateAsync] Manifest fetch failed — {ex.Message}");
                return false;
            }

            string? remoteVersion = null;
            try
            {
                using var doc = JsonDocument.Parse(manifestJson);
                if (doc.RootElement.TryGetProperty("dlss", out var dlssArray))
                {
                    foreach (var record in dlssArray.EnumerateArray())
                    {
                        if (record.TryGetProperty("is_dev_file", out var isDevEl) && isDevEl.GetBoolean())
                            continue;
                        var version = record.TryGetProperty("version", out var vEl) ? vEl.GetString() : null;
                        if (!string.IsNullOrEmpty(version))
                            remoteVersion = version;
                    }
                }
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[OptiScalerService.CheckDlssUpdateAsync] Failed to parse manifest — {ex.Message}");
                return false;
            }

            if (string.IsNullOrEmpty(remoteVersion))
            {
                CrashReporter.Log("[OptiScalerService.CheckDlssUpdateAsync] No version found in manifest");
                return false;
            }

            var cachedVersion = File.Exists(DlssVersionPath)
                ? File.ReadAllText(DlssVersionPath).Trim()
                : null;
            var hasUpdate = !string.Equals(cachedVersion, remoteVersion, StringComparison.Ordinal);

            CrashReporter.Log($"[OptiScalerService.CheckDlssUpdateAsync] Cached={cachedVersion ?? "(none)"}, Remote={remoteVersion}, HasUpdate={hasUpdate}");
            return hasUpdate;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[OptiScalerService.CheckDlssUpdateAsync] Unexpected error — {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Returns the path to the staged nvngx_dlss.dll, or null if not staged.
    /// </summary>
    public static string? GetStagedDlssPath()
    {
        var path = Path.Combine(DlssStagingDir, DlssDllFileName);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Returns the path to the staged nvngx_dlssd.dll (Ray Reconstruction), or null if not staged.
    /// </summary>
    public static string? GetStagedDlssdPath()
    {
        var path = Path.Combine(DlssStagingDir, DlssdDllFileName);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Returns the path to the staged nvngx_dlssg.dll (Frame Generation), or null if not staged.
    /// </summary>
    public static string? GetStagedDlssgPath()
    {
        var path = Path.Combine(DlssStagingDir, DlssgDllFileName);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Returns the staged DLSS version string, or null if not staged.
    /// </summary>
    public static string? GetStagedDlssVersion()
    {
        try
        {
            return File.Exists(DlssVersionPath)
                ? File.ReadAllText(DlssVersionPath).Trim()
                : null;
        }
        catch { return null; }
    }
}
