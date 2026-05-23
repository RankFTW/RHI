using System.IO.Compression;

namespace RenoDXCommander.Services;

public partial class DlssStreamlineService
{
    // ── Swap operations ───────────────────────────────────────────────────────

    public async Task SwapDlssAsync(string dllPath, string version)
    {
        var entry = _manifest?.Dlss?.FirstOrDefault(e => FormatVersion(e.Version) == version || e.Version == version);
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
        var entry = _manifest?.Dlssd?.FirstOrDefault(e => FormatVersion(e.Version) == version || e.Version == version);
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
        var entry = _manifest?.Dlssg?.FirstOrDefault(e => FormatVersion(e.Version) == version || e.Version == version);
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

    public async Task SwapStreamlineAsync(string gameFolder, string version)
    {
        var entry = _manifest?.Streamline?.FirstOrDefault(e => FormatVersion(e.Version) == version || e.Version == version);
        if (entry == null)
        {
            CrashReporter.Log($"[DlssStreamlineService.SwapStreamlineAsync] Version '{version}' not found in manifest");
            return;
        }

        var cachedDir = Path.Combine(StreamlineCacheDir, version);

        // Check if staging is ready (sl.interposer.dll exists)
        if (!File.Exists(Path.Combine(cachedDir, StreamlineIndicator)))
            await DownloadAndCacheStreamlineAsync(entry.Url, cachedDir).ConfigureAwait(false);

        if (!File.Exists(Path.Combine(cachedDir, StreamlineIndicator)))
        {
            CrashReporter.Log($"[DlssStreamlineService.SwapStreamlineAsync] Failed to cache Streamline {version}");
            return;
        }

        // Only replace files that already exist in the game folder
        int replaced = 0;
        foreach (var slDll in KnownStreamlineDlls)
        {
            var gameDllPath = Path.Combine(gameFolder, slDll);
            var cachedDllPath = Path.Combine(cachedDir, slDll);

            if (File.Exists(gameDllPath) && File.Exists(cachedDllPath))
            {
                BackupAndReplace(gameDllPath, cachedDllPath);
                replaced++;
            }
        }

        CrashReporter.Log($"[DlssStreamlineService.SwapStreamlineAsync] Replaced {replaced} Streamline DLLs in '{gameFolder}' with {version}");
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
        CrashReporter.Log($"[DlssStreamlineService.SwapDlssCustomAsync] Swapped '{dllPath}' with custom DLL");
    }

    public async Task SwapStreamlineCustomAsync(string gameFolder)
    {
        if (!Directory.Exists(StreamlineCustomDir))
        {
            CrashReporter.Log($"[DlssStreamlineService.SwapStreamlineCustomAsync] Custom Streamline folder not found");
            return;
        }

        int replaced = 0;
        foreach (var slDll in KnownStreamlineDlls)
        {
            var gameDllPath = Path.Combine(gameFolder, slDll);
            var customDllPath = Path.Combine(StreamlineCustomDir, slDll);

            if (File.Exists(gameDllPath) && File.Exists(customDllPath))
            {
                BackupAndReplace(gameDllPath, customDllPath);
                replaced++;
            }
        }

        CrashReporter.Log($"[DlssStreamlineService.SwapStreamlineCustomAsync] Replaced {replaced} Streamline DLLs with custom files");
    }

    // ── Restore operations ────────────────────────────────────────────────────

    public void Restore(string dllPath)
    {
        var backupPath = dllPath + BackupExtension;
        if (!File.Exists(backupPath))
        {
            CrashReporter.Log($"[DlssStreamlineService.Restore] No backup found for '{dllPath}'");
            return;
        }

        try
        {
            File.Delete(dllPath);
            File.Move(backupPath, dllPath);
            CrashReporter.Log($"[DlssStreamlineService.Restore] Restored '{dllPath}' from backup");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DlssStreamlineService.Restore] Failed to restore '{dllPath}' — {ex.Message}");
        }
    }

    public void RestoreStreamline(string gameFolder)
    {
        foreach (var slDll in KnownStreamlineDlls)
        {
            var dllPath = Path.Combine(gameFolder, slDll);
            var backupPath = dllPath + BackupExtension;
            if (File.Exists(backupPath))
            {
                try
                {
                    if (File.Exists(dllPath))
                        File.Delete(dllPath);
                    File.Move(backupPath, dllPath);
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[DlssStreamlineService.RestoreStreamline] Failed to restore '{dllPath}' — {ex.Message}");
                }
            }
        }

        CrashReporter.Log($"[DlssStreamlineService.RestoreStreamline] Restored Streamline in '{gameFolder}'");
    }

    public void RestoreAll(DlssDetectionResult detection)
    {
        if (detection.DlssPath != null) Restore(detection.DlssPath);
        if (detection.DlssdPath != null) Restore(detection.DlssdPath);
        if (detection.DlssgPath != null) Restore(detection.DlssgPath);
        if (detection.StreamlineFolder != null) RestoreStreamline(detection.StreamlineFolder);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Backs up the target file (if no backup exists) and replaces it with the source.
    /// </summary>
    private void BackupAndReplace(string targetPath, string sourcePath)
    {
        var backupPath = targetPath + BackupExtension;

        // Only create backup if one doesn't already exist (preserve the true original)
        if (!File.Exists(backupPath))
        {
            File.Copy(targetPath, backupPath, overwrite: false);
        }

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

            var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                CrashReporter.Log($"[DlssStreamlineService.DownloadAndCacheAsync] Download failed ({response.StatusCode}) for {url}");
                return;
            }

            using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var file = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true))
            {
                await stream.CopyToAsync(file).ConfigureAwait(false);
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

            var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                CrashReporter.Log($"[DlssStreamlineService.DownloadAndCacheStreamlineAsync] Download failed ({response.StatusCode}) for {url}");
                return;
            }

            using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var file = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true))
            {
                await stream.CopyToAsync(file).ConfigureAwait(false);
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
        catch (Exception ex)
        {
            CrashReporter.Log($"[DlssStreamlineService.DownloadAndCacheStreamlineAsync] Error — {ex.Message}");
        }
    }
}
