using System.Security.Cryptography;
using System.Text.Json;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Downloads, installs, updates, and uninstalls RenoDX addon files.
/// Tracks installations via a local JSON database.
///
/// Download cache: files go to %LocalAppData%\RenoDXCommander\downloads\ so
/// reinstalling or installing the same addon on another game skips the download.
///
/// Update detection: stores RemoteFileSize at install time and compares against
/// the current remote Content-Length — stable across relaunches regardless of
/// local filesystem behaviour or CDN edge-server variation.
/// </summary>
public class ModInstallService : IModInstallService
{
    public event Action<InstalledModRecord>? InstallCompleted;

    private static readonly string DbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RenoDXCommander", "installed.json");

    public static readonly string DownloadCacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RenoDXCommander", "downloads");

    private readonly HttpClient _http;

    public ModInstallService(HttpClient http) => _http = http;

    // ── Install ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Maps addon filenames to their authoritative download URLs when the file is hosted
    /// somewhere other than the default RenoDX snapshot CDN.
    /// Checked by both InstallAsync and CheckForUpdateAsync so installs and update
    /// detection always use the correct source.
    /// </summary>
    private static readonly Dictionary<string, string> _addonUrlOverrides =
        new(StringComparer.OrdinalIgnoreCase)
    {
        // Extended UE addon maintained by marat569 at a separate repo
        ["renodx-ue-extended.addon64"] = "https://marat569.github.io/renodx/renodx-ue-extended.addon64",
    };

    /// <summary>
    /// Returns the authoritative URL for a snapshot URL, substituting an override
    /// when the addon filename has a known alternative source.
    /// </summary>
    private static string ResolveSnapshotUrl(string url)
    {
        try
        {
            var fileName = Path.GetFileName(new Uri(url).LocalPath);
            if (_addonUrlOverrides.TryGetValue(fileName, out var overrideUrl))
                return overrideUrl;
        }
        catch (Exception ex) { CrashReporter.Log($"[ModInstallService.ResolveSnapshotUrl] Failed to resolve URL '{url}' — {ex.Message}"); }
        return url;
    }

    public async Task<InstalledModRecord> InstallAsync(
        GameMod mod,
        string gameInstallPath,
        IProgress<(string message, double percent)>? progress = null,
        string? gameName = null)
    {
        if (mod.SnapshotUrl == null)
            throw new InvalidOperationException($"{mod.Name} has no Snapshot download URL.");

        // Apply URL override before anything else — this ensures the correct CDN is
        // used for both the HEAD size check and the actual download.
        var resolvedUrl = ResolveSnapshotUrl(mod.SnapshotUrl);

        Directory.CreateDirectory(DownloadCacheDir);

        var fileName  = Path.GetFileName(resolvedUrl);

        // Only allow .addon64 / .addon32 files from the RenoDX wiki.
        // Reject any other extension to avoid installing unexpected file types.
        var ext = Path.GetExtension(fileName);
        if (!ext.Equals(".addon64", StringComparison.OrdinalIgnoreCase)
         && !ext.Equals(".addon32", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Unsupported file type '{ext}' for {mod.Name}. Only .addon64 and .addon32 files are supported.");
        }

        var destPath  = Path.Combine(gameInstallPath, fileName);
        var cachePath = Path.Combine(DownloadCacheDir, fileName);

        // ── Step 1: get remote Content-Length (single HEAD) ───────────────────────
        long? remoteSize = null;
        try
        {
            var headResp = await _http.SendAsync(
                new HttpRequestMessage(HttpMethod.Head, resolvedUrl));
            if (headResp.IsSuccessStatusCode)
                remoteSize = headResp.Content.Headers.ContentLength;
        }
        catch (Exception ex) { CrashReporter.Log($"[ModInstallService.InstallAsync] HEAD request failed for size check — {ex.Message}"); }

        // ── Step 2: use cache if it matches remote size (or size unknown) ─────────
        bool usedCache = false;
        if (File.Exists(cachePath))
        {
            var cacheSize = new FileInfo(cachePath).Length;
            bool sizeOk   = !remoteSize.HasValue || remoteSize.Value == cacheSize;
            if (sizeOk)
            {
                progress?.Report(("Installing from cache...", 50));
                File.Copy(cachePath, destPath, overwrite: true);
                usedCache = true;
                progress?.Report(("Installed from cache!", 100));
            }
        }

        // ── Step 3: fresh download if no usable cache ─────────────────────────────
        if (!usedCache)
        {
            progress?.Report(("Downloading...", 0));
            HttpResponseMessage? response = null;
            var tried      = new List<string>();
            var candidates = new List<string> { resolvedUrl };
            try
            {
                var uri = new Uri(resolvedUrl);
                var fn  = Path.GetFileName(uri.LocalPath);

                if (fn.Equals("renodx-unityengine.addon64", StringComparison.OrdinalIgnoreCase)
                 || fn.Equals("renodx-unityengine.addon32", StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add($"https://notvoosh.github.io/renodx-unity/{fn}");
                    candidates.Add($"https://clshortfuse.github.io/renodx/{fn}");
                    candidates.Add($"https://github.com/clshortfuse/renodx/releases/download/snapshot/{fn}");
                }

                if (uri.Host.EndsWith("github.io", StringComparison.OrdinalIgnoreCase))
                {
                    var fn2 = Path.GetFileName(uri.LocalPath);
                    if (!string.IsNullOrEmpty(fn2))
                        candidates.Add($"https://github.com/clshortfuse/renodx/releases/download/snapshot/{fn2}");
                }
            }
            catch (Exception ex) { CrashReporter.Log($"[ModInstallService.InstallAsync] Failed to build fallback URL candidates — {ex.Message}"); }

            foreach (var url in candidates.Where(u => !string.IsNullOrEmpty(u)).Distinct())
            {
                tried.Add(url);
                try
                {
                    response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                    if (response.IsSuccessStatusCode) break;
                }
                catch (Exception ex) { CrashReporter.Log($"[ModInstallService.InstallAsync] Download attempt failed for '{url}' — {ex.Message}"); }
            }

            if (response == null || !response.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"Failed to download snapshot. Tried: {string.Join(", ", tried)}");

            // Capture size from actual download response if HEAD didn't return one
            if (!remoteSize.HasValue)
                remoteSize = response.Content.Headers.ContentLength;

            var total      = remoteSize ?? -1L;
            var buffer     = new byte[1024 * 1024]; // 1 MB
            long downloaded = 0;

            // Download into cache, then copy to game folder
            var tempPath = cachePath + ".tmp";
            using (var netStream = await response.Content.ReadAsStreamAsync())
            using (var cacheFile = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 1024 * 1024, useAsync: true))
            {
                int read;
                while ((read = await netStream.ReadAsync(buffer)) > 0)
                {
                    await cacheFile.WriteAsync(buffer.AsMemory(0, read));
                    downloaded += read;
                    if (total > 0)
                        progress?.Report(($"Downloading... {downloaded / 1024} KB",
                                          (double)downloaded / total * 100));
                }
                cacheFile.Flush();
            }

            if (File.Exists(cachePath)) File.Delete(cachePath);
            File.Move(tempPath, cachePath);
            File.Copy(cachePath, destPath, overwrite: true);

            // Record actual downloaded size if we didn't have it from HEAD
            if (!remoteSize.HasValue)
                remoteSize = new FileInfo(cachePath).Length;
        }

        var hash = await ComputeHashAsync(destPath);

        var record = new InstalledModRecord
        {
            GameName       = gameName ?? mod.Name,
            InstallPath    = gameInstallPath,
            AddonFileName  = fileName,
            FileHash       = hash,
            InstalledAt    = DateTime.UtcNow,
            SnapshotUrl    = resolvedUrl,   // resolved URL ensures future update checks hit the right CDN
            RemoteFileSize = remoteSize,   // ← stored for stable update detection
        };
        SaveRecord(record);
        try { InstallCompleted?.Invoke(record); } catch (Exception ex) { CrashReporter.Log($"[ModInstallService.InstallAsync] InstallCompleted event handler failed — {ex.Message}"); }
        progress?.Report(("Installed!", 100));
        return record;
    }

    // ── Update detection ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the remote snapshot is newer than what was installed.
    ///
    /// Strategy (ordered by reliability):
    ///   1. Missing local file → always reinstall.
    ///   2. RemoteFileSize stored → compare current remote Content-Length against it.
    ///      This is stable because the stored value came from the actual download,
    ///      not from the local file copy (which can differ due to FS/copy behaviour).
    ///   3. No stored size → compare remote Content-Length against local file size.
    ///      Less reliable but better than nothing.
    ///   4. No Content-Length from server → assume no update (avoid false positives).
    /// </summary>
    public async Task<bool> CheckForUpdateAsync(InstalledModRecord record)
    {
        if (record.SnapshotUrl == null) return false;

        var localFile = Path.Combine(record.InstallPath, record.AddonFileName);
        if (!File.Exists(localFile)) return true;

        // Always resolve to the authoritative URL — handles records written before
        // the override table existed (their stored URL may be the generic CDN).
        var checkUrl = ResolveSnapshotUrl(record.SnapshotUrl);

        // For CDNs that don't serve reliable Content-Length on HEAD (e.g. marat569
        // github.io), fall back to a full download comparison.
        if (ShouldUseDownloadCheck(checkUrl))
            return await CheckForUpdateByDownloadAsync(record, checkUrl, localFile);

        try
        {
            var req  = new HttpRequestMessage(HttpMethod.Head, checkUrl);
            var resp = await _http.SendAsync(req);

            if (resp.IsSuccessStatusCode)
            {
                var currentRemoteSize = resp.Content.Headers.ContentLength;
                if (!currentRemoteSize.HasValue) return false; // can't tell → no update

                if (record.RemoteFileSize.HasValue)
                {
                    // Primary check: stored install-time size vs current remote HEAD size.
                    if (currentRemoteSize.Value == record.RemoteFileSize.Value)
                        return false; // sizes match — no update

                    // Sizes differ — real update detected.
                    // The local file should still match the stored install-time size
                    // (nobody modifies addon files manually), confirming the remote
                    // file genuinely changed.
                    return true;
                }
                else
                {
                    // Fallback for legacy records without RemoteFileSize.
                    // Compare remote size against local file size.
                    if (File.Exists(localFile))
                    {
                        var localSize = new FileInfo(localFile).Length;
                        return currentRemoteSize.Value != localSize;
                    }
                    return false;
                }
            }
        }
        catch (Exception ex) { CrashReporter.Log($"[ModInstallService.CheckForUpdateAsync] Network error checking update for '{record.GameName}' — {ex.Message}"); }

        return false;
    }

    // ── Download-based update detection ──────────────────────────────────────────

    /// <summary>
    /// Determines whether the given URL should use download-based update detection
    /// instead of HEAD Content-Length comparison. Returns <c>true</c> for any
    /// <c>*.github.io</c> host, because GitHub Pages CDN may return compressed
    /// transfer sizes or omit Content-Length entirely, making HEAD unreliable.
    /// </summary>
    /// <param name="url">The resolved snapshot URL to evaluate.</param>
    /// <returns>
    /// <c>true</c> if the URL is hosted on a <c>*.github.io</c> domain and should
    /// be routed through <see cref="CheckForUpdateByDownloadAsync"/>; otherwise <c>false</c>.
    /// </returns>
    private static bool ShouldUseDownloadCheck(string url)
    {
        try
        {
            var host = new Uri(url).Host;
            return host.EndsWith(".github.io", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// For CDNs where HEAD Content-Length is unreliable, downloads the remote file
    /// to a temp path and compares its size against the locally installed file.
    /// If a genuine update is detected, the downloaded file is moved into the
    /// download cache so the next install can reuse it without re-downloading.
    /// </summary>
    private async Task<bool> CheckForUpdateByDownloadAsync(
        InstalledModRecord record,
        string url,
        string localFile)
    {
        var cacheFile = Path.Combine(DownloadCacheDir, Path.GetFileName(localFile));
        var tempFile  = cacheFile + $".update_check_{Guid.NewGuid():N}.tmp";

        try
        {
            Directory.CreateDirectory(DownloadCacheDir);

            // Clean up any orphaned temp files from previous update checks for this addon
            // (e.g. from crashes or process kills mid-check)
            try
            {
                var addonName = Path.GetFileName(cacheFile);
                foreach (var stale in Directory.EnumerateFiles(DownloadCacheDir, addonName + ".update*"))
                    try { File.Delete(stale); } catch { /* best-effort */ }
            }
            catch { /* best-effort — don't block the update check */ }

            // Download the remote file to a temp path
            var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!resp.IsSuccessStatusCode) return false;

            using (var net  = await resp.Content.ReadAsStreamAsync())
            using (var file = File.Create(tempFile))
            {
                var buf = new byte[1024 * 1024]; // 1 MB
                int read;
                while ((read = await net.ReadAsync(buf)) > 0)
                    await file.WriteAsync(buf.AsMemory(0, read));
            }

            var localSize  = new FileInfo(localFile).Length;
            var remoteSize = new FileInfo(tempFile).Length;

            if (remoteSize == localSize)
            {
                // Sizes match — compare hashes to catch same-size updates
                var localHash  = await ComputeHashAsync(localFile);
                var remoteHash = await ComputeHashAsync(tempFile);
                if (localHash == remoteHash)
                {
                    // Identical content — no update
                    File.Delete(tempFile);
                    return false;
                }
                // Same size but different content — fall through to update path
            }

            // Real update detected — move temp into cache so install can reuse it
            if (File.Exists(cacheFile)) File.Delete(cacheFile);
            File.Move(tempFile, cacheFile);

            // Update the stored RemoteFileSize so the record reflects the new version
            record.RemoteFileSize = remoteSize;
            SaveRecordPublic(record);

            CrashReporter.Log($"[ModInstallService.CheckForUpdateByDownloadAsync] Update detected via download ({localSize} → {remoteSize} bytes)");
            return true;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[ModInstallService.CheckForUpdateByDownloadAsync] Download check failed — {ex.Message}");
            if (File.Exists(tempFile)) try { File.Delete(tempFile); } catch (Exception cleanupEx) { CrashReporter.Log($"[ModInstallService.CheckForUpdateByDownloadAsync] Failed to clean up temp file '{tempFile}' — {cleanupEx.Message}"); }
            return false;
        }
    }

    // ── Uninstall ─────────────────────────────────────────────────────────────────

    public void Uninstall(InstalledModRecord record)
    {
        var filePath = Path.Combine(record.InstallPath, record.AddonFileName);
        if (File.Exists(filePath)) File.Delete(filePath);
        // Cache copy intentionally kept for future reinstalls.

        var db = LoadDb();
        db.RemoveAll(r => r.GameName == record.GameName && r.InstallPath == record.InstallPath);
        SaveDb(db);
    }

    // ── Database ──────────────────────────────────────────────────────────────────

    public List<InstalledModRecord> LoadAll() => LoadDb();

    public InstalledModRecord? FindRecord(string gameName, string? installPath = null)
    {
        var db = LoadDb();
        return db.FirstOrDefault(r =>
            r.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase) &&
            (installPath == null || r.InstallPath.Equals(installPath, StringComparison.OrdinalIgnoreCase)));
    }

    public void SaveRecordPublic(InstalledModRecord record) => SaveRecord(record);

    /// <summary>Removes the install record from the DB without touching any files on disk.</summary>
    public void RemoveRecord(InstalledModRecord record)
    {
        var db = LoadDb();
        db.RemoveAll(r => r.GameName == record.GameName && r.InstallPath == record.InstallPath);
        SaveDb(db);
    }

    private void SaveRecord(InstalledModRecord record)
    {
        var db = LoadDb();
        var i  = db.FindIndex(r =>
            r.GameName == record.GameName &&
            r.InstallPath.Equals(record.InstallPath, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) db[i] = record; else db.Add(record);
        SaveDb(db);
    }

    private List<InstalledModRecord> LoadDb()
    {
        try
        {
            if (!File.Exists(DbPath)) return new();
            return JsonSerializer.Deserialize<List<InstalledModRecord>>(File.ReadAllText(DbPath)) ?? new();
        }
        catch (Exception ex) { CrashReporter.Log($"[ModInstallService.LoadDb] Failed to load DB from '{DbPath}' — {ex.Message}"); return new(); }
    }

    private void SaveDb(List<InstalledModRecord> db)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
        var json = JsonSerializer.Serialize(db,
            new JsonSerializerOptions { WriteIndented = true });

        FileHelper.WriteAllTextWithRetry(DbPath, json, "ModInstallService.SaveDb");
    }

    private static async Task<string> ComputeHashAsync(string path)
    {
        using var sha    = SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(await sha.ComputeHashAsync(stream));
    }

    public static bool IsValidGameFolder(string path) =>
        Directory.Exists(path) && Directory.GetFiles(path, "*.exe").Length > 0;
}
