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

        // The version comes from the remote DLSS manifest: keep it inside the cache folder
        var cachedDir = FileHelper.SafeCombine(DlssCacheDir, version);
        if (cachedDir == null)
        {
            CrashReporter.Log($"[DlssStreamlineService.SwapDlssAsync] Unsafe version '{version}' — skipped");
            return;
        }
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

        // The version comes from the remote DLSS manifest: keep it inside the cache folder
        var cachedDir = FileHelper.SafeCombine(DlssdCacheDir, version);
        if (cachedDir == null)
        {
            CrashReporter.Log($"[DlssStreamlineService.SwapDlssdAsync] Unsafe version '{version}' — skipped");
            return;
        }
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

        // The version comes from the remote DLSS manifest: keep it inside the cache folder
        var cachedDir = FileHelper.SafeCombine(DlssgCacheDir, version);
        if (cachedDir == null)
        {
            CrashReporter.Log($"[DlssStreamlineService.SwapDlssgAsync] Unsafe version '{version}' — skipped");
            return;
        }
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

        // The version comes from the remote DLSS manifest: keep it inside the cache folder
        var cachedDir = FileHelper.SafeCombine(DlssnrCacheDir, SanitizeVersionForPath(version));
        if (cachedDir == null)
        {
            CrashReporter.Log($"[DlssStreamlineService.SwapDlssnrAsync] Unsafe version '{version}' — skipped");
            return;
        }
        var cachedDll = Path.Combine(cachedDir, DlssnrDllName);

        if (!File.Exists(cachedDll))
            await DownloadAndCacheAsync(entry.Url, cachedDir, DlssnrDllName).ConfigureAwait(false);

        if (!File.Exists(cachedDll))
        {
            CrashReporter.Log($"[DlssStreamlineService.SwapDlssnrAsync] Failed to cache DLSS-NR {version}");
            return;
        }

        ReplaceNrDll(dllPath, cachedDll);
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

        // The version comes from the remote DLSS manifest: keep it inside the cache folder
        var cachedDir = FileHelper.SafeCombine(StreamlineCacheDir, version);
        if (cachedDir == null)
        {
            CrashReporter.Log($"[DlssStreamlineService.SwapStreamlineAsync] Unsafe version '{version}' — skipped");
            return;
        }

        // Check if staging is ready (sl.interposer.dll exists)
        if (!File.Exists(Path.Combine(cachedDir, StreamlineIndicator)))
            await DownloadAndCacheStreamlineAsync(entry.Url, cachedDir).ConfigureAwait(false);

        if (!File.Exists(Path.Combine(cachedDir, StreamlineIndicator)) &&
            !File.Exists(Path.Combine(cachedDir, "sl.common.dll")))
        {
            CrashReporter.Log($"[DlssStreamlineService.SwapStreamlineAsync] Failed to cache Streamline {version}");
            return;
        }

        if (!Directory.Exists(gameFolder))
            throw DlssSwapException.From(gameFolder, new DirectoryNotFoundException($"Folder not found: '{gameFolder}'"));

        // Before replacing, ensure game-original files are backed up to appdata
        // This gives us a fallback if the in-game .original backups are lost.
        // No backup = no overwrite (plain overwrite below has no other backup).
        if (!BackupStreamlineToAppData(gameFolder))
            throw DlssSwapException.From(gameFolder, new InvalidOperationException(
                "RHI couldn't back up this game's Streamline files, so they were left unchanged."));

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
                try
                {
                    File.Copy(cachedDllPath, gameDllPath, overwrite: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    CrashReporter.Log($"[DlssStreamlineService.SwapStreamlineAsync] Failed to write '{gameDllPath}' after {replaced} file(s) — {ex.GetType().Name}: {ex.Message}");
                    throw DlssSwapException.From(gameDllPath, ex);
                }
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

        if (targetFileName.Equals(DlssnrDllName, StringComparison.OrdinalIgnoreCase))
            ReplaceNrDll(dllPath, customDll);
        else
            BackupAndReplace(dllPath, customDll);

        // Write a sidecar marker so the UI can show "Custom" instead of the raw file version
        // (nvngx_dlssnr.dll specifically uses this; other DLLs are identified by their version being absent from manifest)
        if (targetFileName.Equals("nvngx_dlssnr.dll", StringComparison.OrdinalIgnoreCase))
        {
            try { File.WriteAllText(dllPath + ".rhi_custom", ""); } catch (Exception ex) { CrashReporter.Log($"[DlssStreamlineService.SwapDlssCustomAsync] Failed to write custom marker for '{dllPath}' — {ex.Message}"); }
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

        if (!Directory.Exists(gameFolder))
            throw DlssSwapException.From(gameFolder, new DirectoryNotFoundException($"Folder not found: '{gameFolder}'"));

        // Back up original files to AppData before replacing (no backup = no overwrite)
        if (!BackupStreamlineToAppData(gameFolder))
            throw DlssSwapException.From(gameFolder, new InvalidOperationException(
                "RHI couldn't back up this game's Streamline files, so they were left unchanged."));

        int replaced = 0;
        foreach (var slDll in KnownStreamlineDlls)
        {
            var gameDllPath = Path.Combine(gameFolder, slDll);
            var customDllPath = Path.Combine(StreamlineCustomDir, slDll);

            if (File.Exists(gameDllPath) && File.Exists(customDllPath))
            {
                // Plain overwrite — no .original backups. AppData zip backup covers restore.
                try
                {
                    File.Copy(customDllPath, gameDllPath, overwrite: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    CrashReporter.Log($"[DlssStreamlineService.SwapStreamlineCustomAsync] Failed to write '{gameDllPath}' after {replaced} file(s) — {ex.GetType().Name}: {ex.Message}");
                    throw DlssSwapException.From(gameDllPath, ex);
                }
                replaced++;
            }
        }

        // Write marker file so the UI knows Custom is active
        WriteCustomStreamlineMarker(gameFolder);

        CrashReporter.Log($"[DlssStreamlineService.SwapStreamlineCustomAsync] Replaced {replaced} Streamline DLLs with custom files");
    }

    // ── Restore operations ────────────────────────────────────────────────────

    public bool Restore(string dllPath, string? installPath = null)
    {
        // "Default" only undoes a version swap. A file another RHI component placed or owns
        // (OptiScaler, DLSS5 Tool, Feeder, ShortFuse, RTX Encore, Luma) is left alone: its
        // .original belongs to that component's uninstall.
        if (File.Exists(dllPath + BackupExtension))
        {
            var blocked = RestoreBlockedReason(dllPath, installPath);
            if (blocked != null)
            {
                CrashReporter.Log($"[DlssStreamlineService.Restore] Kept '{dllPath}' — {blocked}");
                return false;
            }
        }

        // With the NR Cost Scaler installed, nvngx_dlssnr.dll is its proxy: restore into the real DLL behind it.
        // Otherwise SentinelRestore restores a real backup; returns silently if no .original exists.
        if (!DlssNrCostScalerService.RestoreRealNrDll(dllPath))
            AuxInstallService.SentinelRestore(dllPath);
        // Clean up any custom marker sidecar regardless
        try { File.Delete(dllPath + ".rhi_custom"); } catch (Exception ex) { CrashReporter.Log($"[DlssStreamlineService.Restore] Failed to delete custom marker for '{dllPath}' — {ex.Message}"); }
        return true;
    }

    /// <summary>
    /// Why "Restore to default" must leave <paramref name="dllPath"/> alone, or null when it may restore.
    /// A 0-byte <c>.original</c> means an RHI component placed the file (a version swap only ever
    /// replaces a file that is already there, so it always leaves a real backup). A shared-file owner
    /// in <c>rhi_install.txt</c> (beside the DLL or in <paramref name="installPath"/>, by file name or
    /// relative path) means a component still needs it. An unreadable <c>rhi_install.txt</c> blocks too.
    /// </summary>
    internal static string? RestoreBlockedReason(string dllPath, string? installPath)
    {
        var backup = dllPath + BackupExtension;
        try
        {
            if (File.Exists(backup) && new FileInfo(backup).Length == 0)
                return "placed by an RHI component (0-byte .original)";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"could not read '{Path.GetFileName(backup)}' ({ex.Message})";
        }

        var fileName = Path.GetFileName(dllPath);
        var dirs = new List<string>();
        var dllDir = Path.GetDirectoryName(dllPath);
        if (!string.IsNullOrEmpty(dllDir)) dirs.Add(dllDir);
        if (!string.IsNullOrEmpty(installPath)
            && !dirs.Any(d => PathsEqual(d, installPath)))
            dirs.Add(installPath);

        foreach (var dir in dirs)
        {
            var manifest = Models.RhiInstallManifest.Read(dir, out var status);
            if (status is Models.RhiInstallManifest.ReadStatus.Unreadable or Models.RhiInstallManifest.ReadStatus.Corrupt)
                return $"rhi_install.txt in '{dir}' is {status}";
            if (manifest == null) continue;

            // A bare file name in a manifest means the file in that manifest's own folder, except
            // for components that deploy over the DLL detection found wherever it lives (ShortFuse,
            // DLSS5 Tool) and record it by bare name. Otherwise OptiScaler's root nvngx_dlss.dll
            // blocked "Default" for the game's own copy in a subfolder (UE Engine\Plugins).
            bool ownFolder = !string.IsNullOrEmpty(dllDir) && PathsEqual(dir, dllDir);
            var rel = Path.GetRelativePath(dir, dllPath).Replace('/', '\\');
            bool relInside = !rel.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(rel);

            foreach (var (key, owners) in manifest.SharedFiles)
            {
                if (owners.Count == 0) continue;
                var k = key.Replace('/', '\\');
                bool match;
                if (k.Contains('\\'))
                    match = relInside && rel.Equals(k, StringComparison.OrdinalIgnoreCase);
                else if (!k.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                    match = false;
                else
                    match = ownFolder || owners.Any(o => DetectedPathOwners.Contains(o));
                if (match)
                    return $"owned by {string.Join(", ", owners)}";
            }
        }
        return null;
    }

    /// <summary>
    /// Components that deploy DLSS DLLs at the path detection found (any subfolder) but record
    /// them in the install folder's rhi_install.txt by bare file name.
    /// </summary>
    private static readonly HashSet<string> DetectedPathOwners =
        new(StringComparer.OrdinalIgnoreCase) { "ShortFuse", "Dlss5Tool" };

    private static bool PathsEqual(string a, string b) =>
        string.Equals(
            a.Replace('/', '\\').TrimEnd('\\'),
            b.Replace('/', '\\').TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Streamline files a component other than the swap feature placed in <paramref name="gameFolder"/>
    /// (ShortFuse deploys sl.*.dll with the .original sentinel and lists them in rhi_install.txt).
    /// </summary>
    private static HashSet<string> ComponentOwnedStreamlineFiles(string? installPath)
    {
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(installPath)) return owned;
        foreach (var f in Models.RhiInstallManifest.GetComponentFiles(installPath, "ShortFuse"))
            if (KnownStreamlineDlls.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
                owned.Add(Path.GetFileName(f));
        return owned;
    }

    public int RestoreStreamline(string gameFolder, string? installPath = null)
    {
        int restored = 0;
        bool restoredFromZip = false;
        var owned = ComponentOwnedStreamlineFiles(installPath);
        // Files left alone for another component; the AppData zip must not overwrite them either.
        var keptNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var slDll in KnownStreamlineDlls)
        {
            var dllPath = Path.Combine(gameFolder, slDll);
            var backupPath = dllPath + BackupExtension;

            if (owned.Contains(slDll) && (File.Exists(dllPath) || File.Exists(backupPath)))
            {
                keptNames.Add(slDll);
                CrashReporter.Log($"[DlssStreamlineService.RestoreStreamline] Kept '{dllPath}' — deployed by ShortFuse");
                continue;
            }

            if (File.Exists(backupPath))
            {
                // A 0-byte .original is a sentinel (another component placed this file); moving it
                // over the DLL would leave an empty sl.*.dll.
                long backupLen;
                try { backupLen = new FileInfo(backupPath).Length; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    CrashReporter.Log($"[DlssStreamlineService.RestoreStreamline] Kept '{dllPath}' — could not read backup ({ex.Message})");
                    keptNames.Add(slDll);
                    continue;
                }
                if (backupLen == 0)
                {
                    keptNames.Add(slDll);
                    CrashReporter.Log($"[DlssStreamlineService.RestoreStreamline] Kept '{dllPath}' — placed by an RHI component (0-byte .original)");
                    continue;
                }
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

        // If in-game backups were missing (already consumed), fall back to appdata backup,
        // skipping files kept for another component (the zip would overwrite them).
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
                        if (string.IsNullOrEmpty(entry.Name)) continue;
                        if (keptNames.Contains(entry.Name))
                        {
                            CrashReporter.Log($"[DlssStreamlineService.RestoreStreamline] Not restoring '{entry.Name}' from the AppData backup — kept for another component");
                            continue;
                        }
                        var gameDllPath = Path.Combine(gameFolder, entry.Name);
                        if (File.Exists(gameDllPath))
                        {
                            entry.ExtractToFile(gameDllPath, overwrite: true);
                            restored++;
                        }
                    }
                    if (restored > 0)
                    {
                        restoredFromZip = true;
                        CrashReporter.Log($"[DlssStreamlineService.RestoreStreamline] Restored {restored} Streamline DLLs from AppData backup zip for '{gameFolder}'");
                    }
                }
                catch (Exception ex)
                {
                    CrashReporter.Log($"[DlssStreamlineService.RestoreStreamline] AppData zip restore failed — {ex.Message}");
                }
            }
        }

        CrashReporter.Log($"[DlssStreamlineService.RestoreStreamline] Restored Streamline in '{gameFolder}'");

        // The game's own files are back: drop the AppData copy so the next swap backs up what the game
        // has then (a game update replaces its Streamline files; an old zip would put outdated ones back).
        // Kept while a component still holds some of the files: the zip has the game's own copies of those.
        if (restoredFromZip && keptNames.Count == 0)
            DeleteStreamlineAppDataBackup(gameFolder);

        // Remove custom marker since we're reverting to originals
        RemoveCustomStreamlineMarker(gameFolder);
        return keptNames.Count;
    }

    /// <summary>
    /// Backs up the game's current Streamline DLLs to %LocalAppData%\RHI\StreamlineBackups\{hash}.zip
    /// only if no backup already exists there (preserves the original, not a later swap).
    /// Written to a temp file and moved into place, so a failed write never leaves a partial zip that
    /// counts as the backup. Returns false when the backup couldn't be made (the caller must not overwrite).
    /// </summary>
    private static bool BackupStreamlineToAppData(string gameFolder)
        => BackupStreamlineToZip(gameFolder, GetStreamlineAppDataBackupPath(gameFolder));

    internal static bool BackupStreamlineToZip(string gameFolder, string? backupZip)
    {
        if (backupZip == null) return false;
        var tmp = backupZip + ".tmp";
        try
        {
            // Only create the backup once — don't overwrite with a later-swapped version.
            // A zip that can't be opened (written by an older RHI that failed half-way) is replaced.
            if (File.Exists(backupZip))
            {
                if (IsReadableZip(backupZip)) return true;
                CrashReporter.Log($"[DlssStreamlineService.BackupStreamlineToZip] Unreadable backup '{backupZip}' — making a new one");
                File.Delete(backupZip);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(backupZip)!);
            if (File.Exists(tmp)) File.Delete(tmp);

            int backed = 0;
            using (var zip = System.IO.Compression.ZipFile.Open(tmp, System.IO.Compression.ZipArchiveMode.Create))
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
            {
                File.Delete(tmp); // nothing to back up — remove empty zip
                return true;
            }
            File.Move(tmp, backupZip);
            CrashReporter.Log($"[DlssStreamlineService] AppData backup created for '{gameFolder}' ({backed} files → {backupZip})");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            CrashReporter.Log($"[DlssStreamlineService] AppData backup failed for '{gameFolder}' — {ex.Message}");
            try { if (File.Exists(tmp)) File.Delete(tmp); }
            catch (Exception ex2) when (ex2 is IOException or UnauthorizedAccessException)
            { CrashReporter.Log($"[DlssStreamlineService] Couldn't delete '{tmp}' — {ex2.Message}"); }
            return false;
        }
    }

    private static bool IsReadableZip(string path)
    {
        try
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(path);
            return zip.Entries.Count > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return false;
        }
    }

    private static void DeleteStreamlineAppDataBackup(string gameFolder)
    {
        var zip = GetStreamlineAppDataBackupPath(gameFolder);
        if (zip == null || !File.Exists(zip)) return;
        try
        {
            File.Delete(zip);
            CrashReporter.Log($"[DlssStreamlineService] AppData backup removed after restore for '{gameFolder}'");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[DlssStreamlineService] Couldn't remove AppData backup '{zip}' — {ex.Message}");
        }
    }

    /// <summary>Returns the appdata backup zip path for a game folder, using an 8-char hash of the full path as key.</summary>
    internal static string? GetStreamlineAppDataBackupPath(string gameFolder)
    {
        if (string.IsNullOrEmpty(gameFolder)) return null;
        var normalized = Path.GetFullPath(gameFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();
        var hashBytes = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(normalized));
        var hash = Convert.ToHexString(hashBytes)[..8];
        return Path.Combine(StreamlineBackupsDir, hash + ".zip");
    }

    public int RestoreAll(DlssDetectionResult detection, string? installPath = null)
    {
        int kept = 0;
        if (detection.DlssPath != null && !Restore(detection.DlssPath, installPath)) kept++;
        if (detection.DlssdPath != null && !Restore(detection.DlssdPath, installPath)) kept++;
        if (detection.DlssgPath != null && !Restore(detection.DlssgPath, installPath)) kept++;
        if (detection.DlssnrPath != null && !Restore(detection.DlssnrPath, installPath)) kept++;
        if (detection.StreamlineFolder != null) kept += RestoreStreamline(detection.StreamlineFolder, installPath);
        return kept;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Backs up the target file (if no backup exists) and replaces it with the source.
    /// Throws <see cref="DlssSwapException"/> if the game folder can't be written to.
    /// On failure the game's original DLL is put back, so the game is never left without one.
    /// </summary>
    /// <summary>
    /// Replaces an nvngx_dlssnr.dll. When the NR Cost Scaler is installed beside it, the file is the
    /// Cost Scaler's proxy, so the real DLL behind it is replaced instead
    /// (<see cref="DlssNrCostScalerService.ReplaceRealNrDll"/>).
    /// </summary>
    private void ReplaceNrDll(string dllPath, string sourcePath)
    {
        try
        {
            if (DlssNrCostScalerService.ReplaceRealNrDll(dllPath, sourcePath)) return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[DlssStreamlineService.ReplaceNrDll] Failed to write the NR DLL behind the Cost Scaler for '{dllPath}' — {ex.GetType().Name}: {ex.Message}");
            throw DlssSwapException.From(dllPath, ex);
        }
        BackupAndReplace(dllPath, sourcePath);
    }

    private void BackupAndReplace(string targetPath, string sourcePath)
    {
        // The detected path can be stale (game moved/updated — WindowsApps paths change
        // version on every update). Check before touching anything.
        var dir = Path.GetDirectoryName(targetPath);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            throw DlssSwapException.From(targetPath, new DirectoryNotFoundException($"Folder not found: '{dir}'"));

        var backup = targetPath + BackupExtension;
        bool hadBackup = File.Exists(backup);

        // SentinelBackup handles all three cases:
        //   file exists, no .original  → real backup (File.Move)
        //   file exists, .original exists → no-op (preserve existing backup)
        //   file absent → 0-byte sentinel so Restore knows to clean up entirely
        AuxInstallService.SentinelBackup(targetPath);
        try
        {
            File.Copy(sourcePath, targetPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!hadBackup) UndoBackup(targetPath, backup);
            CrashReporter.Log($"[DlssStreamlineService.BackupAndReplace] Failed to write '{targetPath}' — {ex.GetType().Name}: {ex.Message}");
            throw DlssSwapException.From(targetPath, ex);
        }
    }

    /// <summary>
    /// Reverses a backup made moments ago by <see cref="AuxInstallService.SentinelBackup"/>
    /// after the replacement copy failed: moves the original back, or removes the 0-byte sentinel.
    /// </summary>
    private static void UndoBackup(string targetPath, string backup)
    {
        try
        {
            if (!File.Exists(backup) || File.Exists(targetPath)) return;
            if (new FileInfo(backup).Length > 0)
                File.Move(backup, targetPath);
            else
                File.Delete(backup);
            CrashReporter.Log($"[DlssStreamlineService.UndoBackup] Restored original after failed swap: '{targetPath}'");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[DlssStreamlineService.UndoBackup] Could not restore '{targetPath}' — {ex.Message}");
        }
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
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
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
                    // Through a temp file: a failed extraction never leaves a partial DLL that File.Exists accepts
                    await FileHelper.WriteStreamAtomicAsync(entry.Open, Path.Combine(cacheDir, expectedDllName), entry.Length).ConfigureAwait(false);
                    CrashReporter.Log($"[DlssStreamlineService.DownloadAndCacheAsync] Cached {expectedDllName} to '{cacheDir}'");
                }
                else
                {
                    CrashReporter.Log($"[DlssStreamlineService.DownloadAndCacheAsync] '{expectedDllName}' not found in zip from {url}");
                }
            }

            // Clean up temp zip
            try { File.Delete(tempZip); } catch (Exception) { /* temp file in %TEMP%; a leftover is harmless */ }
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
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
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

            // Extract all sl.*.dll files from the zip (flat structure) into a temp folder first, then move them in
            // with the files the cache check looks for (sl.common.dll, sl.interposer.dll) last: a failed extraction
            // never leaves a partial set that File.Exists accepts as cached.
            int extracted = 0;
            var partialDir = Path.Combine(cacheDir, "_partial");
            try
            {
                if (Directory.Exists(partialDir)) Directory.Delete(partialDir, recursive: true);
                Directory.CreateDirectory(partialDir);
                using (var zip = ZipFile.OpenRead(tempZip))
                {
                    foreach (var entry in zip.Entries)
                    {
                        var fileName = Path.GetFileName(entry.FullName);
                        if (KnownStreamlineDlls.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                        {
                            await FileHelper.WriteStreamAtomicAsync(entry.Open, Path.Combine(partialDir, fileName), entry.Length).ConfigureAwait(false);
                            extracted++;
                        }
                    }
                }
                static int CheckOrder(string name) =>
                    name.Equals(StreamlineIndicator, StringComparison.OrdinalIgnoreCase) ? 2
                    : name.Equals("sl.common.dll", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                foreach (var file in Directory.GetFiles(partialDir).OrderBy(f => CheckOrder(Path.GetFileName(f))))
                    File.Move(file, Path.Combine(cacheDir, Path.GetFileName(file)), overwrite: true);
            }
            finally
            {
                try { if (Directory.Exists(partialDir)) Directory.Delete(partialDir, recursive: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { CrashReporter.Log($"[DlssStreamlineService.DownloadAndCacheStreamlineAsync] Couldn't remove '{partialDir}' — {ex.Message}"); }
            }

            CrashReporter.Log($"[DlssStreamlineService.DownloadAndCacheStreamlineAsync] Cached {extracted} Streamline DLLs to '{cacheDir}'");

            // Clean up temp zip
            try { File.Delete(tempZip); } catch (Exception) { /* temp file in %TEMP%; a leftover is harmless */ }
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
        catch (Exception ex) { CrashReporter.Log($"[DlssStreamlineService.WriteCustomStreamlineMarker] Failed to write marker in '{gameFolder}' — {ex.Message}"); }
    }

    private static void RemoveCustomStreamlineMarker(string gameFolder)
    {
        try { var path = Path.Combine(gameFolder, CustomStreamlineMarker); if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { CrashReporter.Log($"[DlssStreamlineService.RemoveCustomStreamlineMarker] Failed to remove marker in '{gameFolder}' — {ex.Message}"); }
    }
}
