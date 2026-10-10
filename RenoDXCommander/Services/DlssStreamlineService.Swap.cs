using System.IO.Compression;

namespace RenoDXCommander.Services;

public partial class DlssStreamlineService
{
    /// <summary>
    /// Sanitizes a version string for use as a directory name by replacing characters
    /// that Windows treats as path separators or invalid in directory names.
    /// e.g. "310.8.2 (20/30/40/50)" → "310.8.2 (20-30-40-50)"
    /// </summary>
    private static string SanitizeVersionForPath(string version)
        => version.Replace('/', '-').Replace('\\', '-');

    // ── Entry lookup helpers (searches regular + dev lists) ───────────────────

    private DlssManifestEntry? FindDlssEntry(string version) =>
        FindEntry(_manifest?.Dlss, _manifest?.DlssDev, version);
    private DlssManifestEntry? FindDlssdEntry(string version) =>
        FindEntry(_manifest?.Dlssd, _manifest?.DlssdDev, version);
    private DlssManifestEntry? FindDlssgEntry(string version) =>
        FindEntry(_manifest?.Dlssg, _manifest?.DlssgDev, version);
    private DlssManifestEntry? FindDlssnrEntry(string version) =>
        FindEntry(_manifest?.Dlssnr, _manifest?.DlssnrDev, version);
    private DlssManifestEntry? FindStreamlineEntry(string version) =>
        FindEntry(_manifest?.Streamline, _manifest?.StreamlineDev, version);

    private static DlssManifestEntry? FindEntry(
        List<DlssManifestEntry>? regular,
        List<DlssManifestEntry>? dev,
        string version)
    {
        bool Match(DlssManifestEntry e) =>
            FormatVersion(e.Version) == version || e.Version == version;
        return regular?.FirstOrDefault(Match) ?? dev?.FirstOrDefault(Match);
    }

    // ── Swap operations ───────────────────────────────────────────────────────

    public async Task SwapDlssAsync(string dllPath, string version)
    {
        var entry = FindDlssEntry(version);
        if (entry == null)
        {
            CrashReporter.Log($"[DlssStreamlineService.SwapDlssAsync] Version '{version}' not found in manifest");
            return;
        }

        var cachedDir = Path.Combine(DlssCacheDir, version);
        var cachedDll = Path.Combine(cachedDir, DlssDllName);

        if (!File.Exists(cachedDll))
            await DownloadAndCacheAsync(entry.Url, cachedDir, DlssDllName).ConfigureAwait(false);

        if (!File.Exists(cachedDll))
        {
            CrashReporter.Log($"[DlssStreamlineService.SwapDlssAsync] Failed to cache DLSS {version}");
            return;
        }

        BackupAndReplace(dllPath, cachedDll);
        CrashReporter.Log($"[DlssStreamlineService.SwapDlssAsync] Swapped '{dllPath}' to {version}");
    }

    public async Task SwapDlssdAsync(string dllPath, string version)
    {
        var entry = FindDlssdEntry(version);
        if (entry == null)
        {
            CrashReporter.Log($"[DlssStreamlineService.SwapDlssdAsync] Version '{version}' not found in manifest");
            return;
        }

        var cachedDir = Path.Combine(DlssdCacheDir, version);
        var cachedDll = Path.Combine(cachedDir, DlssdDllName);

        if (!File.Exists(cachedDll))
            await DownloadAndCacheAsync(entry.Url, cachedDir, DlssdDllName).ConfigureAwait(false);

        if (!File.Exists(cachedDll))
        {
            CrashReporter.Log($"[DlssStreamlineService.SwapDlssdAsync] Failed to cache DLSS-D {version}");
            return;
        }

        BackupAndReplace(dllPath, cachedDll);
        CrashReporter.Log($"[DlssStreamlineService.SwapDlssdAsync] Swapped '{dllPath}' to {version}");
    }

    public async Task SwapDlssgAsync(string dllPath, string version)
    {
        var entry = FindDlssgEntry(version);
        if (entry == null)
        {
            CrashReporter.Log($"[DlssStreamlineService.SwapDlssgAsync] Version '{version}' not found in manifest");
            return;
        }

        var cachedDir = Path.Combine(DlssgCacheDir, version);
        var cachedDll = Path.Combine(cachedDir, DlssgDllName);

        if (!File.Exists(cachedDll))
            await DownloadAndCacheAsync(entry.Url, cachedDir, DlssgDllName).ConfigureAwait(false);

        if (!File.Exists(cachedDll))
        {
            CrashReporter.Log($"[DlssStreamlineService.SwapDlssgAsync] Failed to cache DLSS-G {version}");
            return;
        }

        BackupAndReplace(dllPath, cachedDll);
        CrashReporter.Log($"[DlssStreamlineService.SwapDlssgAsync] Swapped '{dllPath}' to {version}");
    }

    public async Task SwapDlssnrAsync(string dllPath, string version)
    {
        var entry = FindDlssnrEntry(version);
        if (entry == null)
        {
            CrashReporter.Log($"[DlssStreamlineService.SwapDlssnrAsync] Version '{version}' not found in manifest");
            return;
        }

        var cachedDir = Path.Combine(DlssnrCacheDir, SanitizeVersionForPath(version));
        var cachedDll = Path.Combine(cachedDir, DlssnrDllName);

        if (!File.Exists(cachedDll))
            await DownloadAndCacheAsync(entry.Url, cachedDir, DlssnrDllName).ConfigureAwait(false);

        if (!File.Exists(cachedDll))
        {
            CrashReporter.Log($"[DlssStreamlineService.SwapDlssnrAsync] Failed to cache DLSS-NR {version}");
            return;
        }

        BackupAndReplace(dllPath, cachedDll);
        CrashReporter.Log($"[DlssStreamlineService.SwapDlssnrAsync] Swapped '{dllPath}' to {version}");
    }

    public async Task SwapStreamlineAsync(string gameFolder, string version)
    {
        var entry = FindStreamlineEntry(version);
        if (entry == null)
        {
            CrashReporter.Log($"[DlssStreamlineService.SwapStreamlineAsync] Version '{version}' not found in manifest");
            return;
        }

        var cachedDir = Path.Combine(StreamlineCacheDir, version);

        // Check if staging is ready (sl.interposer.dll exists)
        if (!File.Exists(Path.Combine(cachedDir, StreamlineIndicator)))
            await DownloadAndCacheStreamlineAsync(entry.Url, cachedDir).ConfigureAwait(false);

        if (!File.Exists(Path.Combine(cachedDir, StreamlineIndicator)) &&
            !File.Exists(Path.Combine(cachedDir, "sl.common.dll")))
        {
            CrashReporter.Log($"[DlssStreamlineService.SwapStreamlineAsync] Failed to cache Streamline {version}");
            return;
        }

        // Before replacing, ensure game-original files are backed up to appdata
        // This gives us a fallback if the in-game .original backups are lost
        BackupStreamlineToAppData(gameFolder);

        // Only replace files that already exist in the game folder
        int replaced = 0;
        foreach (var slDll in KnownStreamlineDlls)
        {
            var gameDllPath = Path.Combine(gameFolder, slDll);
            var cachedDllPath = Path.Combine(cachedDir, slDll);

            if (File.Exists(gameDllPath) && File.Exists(cachedDllPath))
            {
                // Plain overwrite — no .original backups. Streamline in OptiScaler\Streamline\ is
                // always RHI-managed, never a game original. AppData zip backup covers restore.
                File.Copy(cachedDllPath, gameDllPath, overwrite: true);
                replaced++;
            }
        }

        CrashReporter.Log($"[DlssStreamlineService.SwapStreamlineAsync] Replaced {replaced} Streamline DLLs in '{gameFolder}' with {version}");

        // Remove custom marker since a versioned Streamline is now active
        RemoveCustomStreamlineMarker(gameFolder);
    }

    public async Task SwapDlssCustomAsync(string dllPath)
    {
        // Resolve the correct custom DLL based on the target filename
        var targetFileName = Path.GetFileName(dllPath);
        var customDll = Path.Combine(DlssCustomDir, targetFileName);
        if (!File.Exists(customDll))
        {
            CrashReporter.Log($"[DlssStreamlineService.SwapDlssCustomAsync] Custom DLL not found at '{customDll}'");
            return;
        }

        BackupAndReplace(dllPath, customDll);

        // Write a sidecar marker so the UI can show "Custom" instead of the raw file version
        // (nvngx_dlssnr.dll specifically uses this; other DLLs are identified by their version being absent from manifest)
        if (targetFileName.Equals("nvngx_dlssnr.dll", StringComparison.OrdinalIgnoreCase))
        {
            try { File.WriteAllText(dllPath + ".rhi_custom", ""); } catch { }
        }

        CrashReporter.Log($"[DlssStreamlineService.SwapDlssCustomAsync] Swapped '{dllPath}' with custom DLL");
    }

    public async Task SwapStreamlineCustomAsync(string gameFolder)
    {
        if (!Directory.Exists(StreamlineCustomDir))
        {
            CrashReporter.Log($"[DlssStreamlineService.SwapStreamlineCustomAsync] Custom Streamline folder not found");
            return;
        }

        // Back up original files to AppData before replacing
        BackupStreamlineToAppData(gameFolder);

        int replaced = 0;
        foreach (var slDll in KnownStreamlineDlls)
        {
            var gameDllPath = Path.Combine(gameFolder, slDll);
            var customDllPath = Path.Combine(StreamlineCustomDir, slDll);

            if (File.Exists(gameDllPath) && File.Exists(customDllPath))
            {
                // Plain overwrite — no .original backups. AppData zip backup covers restore.
                File.Copy(customDllPath, gameDllPath, overwrite: true);
                replaced++;
            }
        }

        // Write marker file so the UI knows Custom is active
        WriteCustomStreamlineMarker(gameFolder);

        CrashReporter.Log($"[DlssStreamlineService.SwapStreamlineCustomAsync] Replaced {replaced} Streamline DLLs with custom files");
    }

    // ── Restore operations ────────────────────────────────────────────────────

    public void Restore(string dllPath)
    {
        // SentinelRestore handles 0-byte sentinel (delete) and real backup (restore).
        // Returns silently if no .original exists (not placed by RHI).
        AuxInstallService.SentinelRestore(dllPath);
        // Clean up any custom marker sidecar regardless
        try { File.Delete(dllPath + ".rhi_custom"); } catch { }
    }

    public void RestoreStreamline(string gameFolder)
    {
        int restored = 0;
        foreach (var slDll in KnownStreamlineDlls)
        {
            var dllPath = Path.Combine(gameFolder, slDll);
            var backupPath = dllPath + BackupExtension;

            if (File.Exists(backupPath))
            {
                try
                {
                    if (File.Exists(dllPath)) File.Delete(dllPath);
                    File.Move(backupPath, dllPath);
                    restored++;
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[DlssStreamlineService.RestoreStreamline] Failed to restore '{dllPath}' — {ex.Message}");
                }
            }
        }

        // If in-game backups were missing (already consumed), fall back to appdata backup
        if (restored == 0)
        {
            var appDataBackup = GetStreamlineAppDataBackupPath(gameFolder);
            if (appDataBackup != null && File.Exists(appDataBackup))
            {
                try
                {
                    using var zip = System.IO.Compression.ZipFile.OpenRead(appDataBackup);
                    foreach (var entry in zip.Entries)
                    {
                        var gameDllPath = Path.Combine(gameFolder, entry.Name);
                        if (File.Exists(gameDllPath))
                        {
                            entry.ExtractToFile(gameDllPath, overwrite: true);
                            restored++;
                        }
                    }
                    if (restored > 0)
                        CrashReporter.Log($"[DlssStreamlineService.RestoreStreamline] Restored {restored} Streamline DLLs from AppData backup zip for '{gameFolder}'");
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[DlssStreamlineService.RestoreStreamline] AppData zip restore failed — {ex.Message}");
                }
            }
        }

        CrashReporter.Log($"[DlssStreamlineService.RestoreStreamline] Restored Streamline in '{gameFolder}'");

        // Remove custom marker since we're reverting to originals
        RemoveCustomStreamlineMarker(gameFolder);
    }

    /// <summary>
    /// Backs up the game's current Streamline DLLs to %LocalAppData%\RHI\StreamlineBackups\{hash}.zip
    /// only if no backup already exists there (preserves the original, not a later swap).
    /// </summary>
    private static void BackupStreamlineToAppData(string gameFolder)
    {
        try
        {
            var backupZip = GetStreamlineAppDataBackupPath(gameFolder);
            if (backupZip == null) return;

            // Only create the backup once — don't overwrite with a later-swapped version
            if (File.Exists(backupZip)) return;

            Directory.CreateDirectory(Path.GetDirectoryName(backupZip)!);

            int backed = 0;
            using (var zip = System.IO.Compression.ZipFile.Open(backupZip, System.IO.Compression.ZipArchiveMode.Create))
            {
                foreach (var slDll in KnownStreamlineDlls)
                {
                    var src = Path.Combine(gameFolder, slDll);
                    if (File.Exists(src))
                    {
                        zip.CreateEntryFromFile(src, slDll, System.IO.Compression.CompressionLevel.Fastest);
                        backed++;
                    }
                }
            }

            if (backed == 0)
                File.Delete(backupZip); // nothing to back up — remove empty zip
            else
                CrashReporter.Log($"[DlssStreamlineService] AppData backup created for '{gameFolder}' ({backed} files → {backupZip})");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DlssStreamlineService] AppData backup failed for '{gameFolder}' — {ex.Message}");
        }
    }

    /// <summary>Returns the appdata backup zip path for a game folder, using an 8-char hash of the full path as key.</summary>
    private static string? GetStreamlineAppDataBackupPath(string gameFolder)
    {
        if (string.IsNullOrEmpty(gameFolder)) return null;
        var normalized = Path.GetFullPath(gameFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();
        var hashBytes = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(normalized));
        var hash = Convert.ToHexString(hashBytes)[..8];
        return Path.Combine(StreamlineBackupsDir, hash + ".zip");
    }

    public void RestoreAll(DlssDetectionResult detection)
    {
        if (detection.DlssPath != null) Restore(detection.DlssPath);
        if (detection.DlssdPath != null) Restore(detection.DlssdPath);
        if (detection.DlssgPath != null) Restore(detection.DlssgPath);
        if (detection.DlssnrPath != null) Restore(detection.DlssnrPath);
        if (detection.StreamlineFolder != null) RestoreStreamline(detection.StreamlineFolder);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Backs up the target file (if no backup exists) and replaces it with the source.
    /// </summary>
    private void BackupAndReplace(string targetPath, string sourcePath)
    {
        // SentinelBackup handles all three cases:
        //   file exists, no .original  → real backup (File.Move)
        //   file exists, .original exists → no-op (preserve existing backup)
        //   file absent → 0-byte sentinel so Restore knows to clean up entirely
        AuxInstallService.SentinelBackup(targetPath);
        File.Copy(sourcePath, targetPath, overwrite: true);
    }

    /// <summary>
    /// Downloads a zip from the given URL, extracts the single DLL, and places it in the cache dir.
    /// </summary>
    private async Task DownloadAndCacheAsync(string url, string cacheDir, string expectedDllName)
    {
        try
        {
            Directory.CreateDirectory(cacheDir);
            var tempZip = Path.Combine(cacheDir, "download.zip.tmp");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                CrashReporter.Log($"[DlssStreamlineService.DownloadAndCacheAsync] Download failed ({response.StatusCode}) for {url}");
                return;
            }

            using (var stream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false))
            using (var file = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true))
            {
                await stream.CopyToAsync(file, cts.Token).ConfigureAwait(false);
            }

            // Extract the DLL from the zip
            using (var zip = ZipFile.OpenRead(tempZip))
            {
                var entry = zip.Entries.FirstOrDefault(e =>
                    string.Equals(Path.GetFileName(e.FullName), expectedDllName, StringComparison.OrdinalIgnoreCase));

                if (entry != null)
                {
                    entry.ExtractToFile(Path.Combine(cacheDir, expectedDllName), overwrite: true);
                    CrashReporter.Log($"[DlssStreamlineService.DownloadAndCacheAsync] Cached {expectedDllName} to '{cacheDir}'");
                }
                else
                {
                    CrashReporter.Log($"[DlssStreamlineService.DownloadAndCacheAsync] '{expectedDllName}' not found in zip from {url}");
                }
            }

            // Clean up temp zip
            try { File.Delete(tempZip); } catch { }
        }
        catch (OperationCanceledException)
        {
            CrashReporter.Log($"[DlssStreamlineService.DownloadAndCacheAsync] Download timed out for {url}");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DlssStreamlineService.DownloadAndCacheAsync] Error — {ex.Message}");
        }
    }

    /// <summary>
    /// Downloads a Streamline zip and extracts all sl.*.dll files to the cache dir (flat).
    /// </summary>
    private async Task DownloadAndCacheStreamlineAsync(string url, string cacheDir)
    {
        try
        {
            Directory.CreateDirectory(cacheDir);
            var tempZip = Path.Combine(cacheDir, "download.zip.tmp");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                CrashReporter.Log($"[DlssStreamlineService.DownloadAndCacheStreamlineAsync] Download failed ({response.StatusCode}) for {url}");
                return;
            }

            using (var stream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false))
            using (var file = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true))
            {
                await stream.CopyToAsync(file, cts.Token).ConfigureAwait(false);
            }

            // Extract all sl.*.dll files from the zip (flat structure)
            int extracted = 0;
            using (var zip = ZipFile.OpenRead(tempZip))
            {
                foreach (var entry in zip.Entries)
                {
                    var fileName = Path.GetFileName(entry.FullName);
                    if (KnownStreamlineDlls.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                    {
                        entry.ExtractToFile(Path.Combine(cacheDir, fileName), overwrite: true);
                        extracted++;
                    }
                }
            }

            CrashReporter.Log($"[DlssStreamlineService.DownloadAndCacheStreamlineAsync] Cached {extracted} Streamline DLLs to '{cacheDir}'");

            // Clean up temp zip
            try { File.Delete(tempZip); } catch { }
        }
        catch (OperationCanceledException)
        {
            CrashReporter.Log($"[DlssStreamlineService.DownloadAndCacheStreamlineAsync] Download timed out for {url}");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DlssStreamlineService.DownloadAndCacheStreamlineAsync] Error — {ex.Message}");
        }
    }

    // ── Custom Streamline marker ──────────────────────────────────────────────

    private const string CustomStreamlineMarker = "_rhi_custom_streamline";

    /// <summary>Returns true if the custom Streamline marker file exists in the game folder.</summary>
    public static bool IsCustomStreamlineActive(string gameFolder)
        => File.Exists(Path.Combine(gameFolder, CustomStreamlineMarker));

    private static void WriteCustomStreamlineMarker(string gameFolder)
    {
        try { File.WriteAllText(Path.Combine(gameFolder, CustomStreamlineMarker), "Custom Streamline deployed by RHI"); }
        catch { }
    }

    private static void RemoveCustomStreamlineMarker(string gameFolder)
    {
        try { var path = Path.Combine(gameFolder, CustomStreamlineMarker); if (File.Exists(path)) File.Delete(path); }
        catch { }
    }
}
