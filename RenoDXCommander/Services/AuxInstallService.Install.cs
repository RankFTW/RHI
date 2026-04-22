// AuxInstallService.Install.cs — ReShade install/uninstall, update detection, and DB persistence
using System.Text.Json;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public partial class AuxInstallService
{
    // ── Install — ReShade ─────────────────────────────────────────────────────────

    public async Task<AuxInstalledRecord> InstallReShadeAsync(
        string gameName,
        string installPath,
        string? shaderModeOverride = null,
        bool use32Bit = false,
        string? filenameOverride = null,
        IEnumerable<string>? selectedPackIds = null,
        IProgress<(string message, double percent)>? progress = null,
        string? screenshotSavePath = null,
        bool useNormalReShade = false,
        string? overlayHotkey = null,
        string? screenshotHotkey = null)
    {
        Directory.CreateDirectory(DownloadPaths.Misc);

        var destName = !string.IsNullOrWhiteSpace(filenameOverride)
            ? filenameOverride
            : RsNormalName;

        // ── OptiScaler coexistence: deploy as ReShade64.dll when OptiScaler is installed ──
        var osRecord = FindRecord(gameName, installPath, OptiScalerService.AddonType);
        if (osRecord != null)
        {
            destName = OptiScalerService.ReShadeCoexistName;
            CrashReporter.Log($"[AuxInstallService.InstallReShadeAsync] OptiScaler installed — deploying ReShade as '{destName}'");
        }

        // ── DC occupancy check: avoid overwriting a DC file at the target name ──
        var dcRecord = FindRecord(gameName, installPath, "DisplayCommander");
        if (dcRecord != null &&
            string.Equals(dcRecord.InstalledAs, destName, StringComparison.OrdinalIgnoreCase))
        {
            destName = use32Bit ? RsStaged32 : RsStaged64;
            CrashReporter.Log($"[AuxInstallService.InstallReShadeAsync] Target '{dcRecord.InstalledAs}' occupied by DC — falling back to '{destName}'");
        }

        var destPath = Path.Combine(installPath, destName);

        // ── Record-aware cleanup: remove old non-standard DLL if InstalledAs differs ─
        var addonType = useNormalReShade ? TypeReShadeNormal : TypeReShade;
        var existingRecord = FindRecord(gameName, installPath, TypeReShade)
                          ?? FindRecord(gameName, installPath, TypeReShadeNormal);
        if (existingRecord != null &&
            !string.Equals(existingRecord.InstalledAs, destName, StringComparison.OrdinalIgnoreCase))
        {
            var oldPath = Path.Combine(installPath, existingRecord.InstalledAs);
            if (File.Exists(oldPath))
                try { File.Delete(oldPath); } catch (Exception ex) { CrashReporter.Log($"[AuxInstallService.InstallReShadeAsync] Failed to delete old RS file '{oldPath}' — {ex.Message}"); }
            RestoreForeignDll(oldPath);
        }

        // ── Ensure staged DLLs exist (downloaded from reshade.me) ────────────────
        progress?.Report(("Preparing ReShade files...", 10));
        EnsureReShadeStaging();

        var rsStagedPath = useNormalReShade
            ? (use32Bit ? RsNormalStagedPath32 : RsNormalStagedPath64)
            : (use32Bit ? RsStagedPath32 : RsStagedPath64);
        if (!File.Exists(rsStagedPath))
            throw new FileNotFoundException(
                $"ReShade DLLs not found in staging directory.\n" +
                $"Expected: {rsStagedPath}\n" +
                $"Please restart RHI to download ReShade from reshade.me.");

        // ── Back up foreign DLL at destination ──────────────────────────────────
        BackupForeignDll(destPath);

        // ── Copy staged DLL to game folder ────────────────────────────────────────
        progress?.Report(("Installing ReShade...", 80));
        File.Copy(rsStagedPath, destPath, overwrite: true);

        // Deploy reshade.ini alongside the DLL.
        if (File.Exists(RsIniPath))
            MergeRsIni(installPath, screenshotSavePath, overlayHotkey, screenshotHotkey);
        // Deploy ReShadePreset.ini alongside reshade.ini when the user has placed one in the inis folder.
        CopyRsPresetIniIfPresent(installPath);

        progress?.Report(("ReShade installed!", 100));

        // ── Shader deployment ─────────────────────────────────────────────────────
        // Always deploy shaders locally to the game folder.
        // Uses Sync (prune + deploy) so switching shader selections properly
        // removes files from the previous selection.
        _shaderPackService.SyncGameFolder(installPath, selectedPackIds);

        var record = new AuxInstalledRecord
        {
            GameName       = gameName,
            InstallPath    = installPath,
            AddonType      = addonType,
            InstalledAs    = destName,
            SourceUrl      = null,       // bundled — no remote URL
            RemoteFileSize = null,       // no remote size to track
            InstalledAt    = DateTime.UtcNow,
        };
        SaveRecord(record);
        return record;
    }

    // ── Update detection ──────────────────────────────────────────────────────────

    /// <summary>
    /// Checks if an installed ReShade file is outdated by comparing its size
    /// against the staged (bundled) DLL. Returns true if an update is available.
    /// </summary>
    public static bool CheckReShadeUpdateLocal(AuxInstalledRecord record)
    {
        if (record.AddonType != TypeReShade && record.AddonType != TypeReShadeNormal)
            return false;

        var localFile = Path.Combine(record.InstallPath, record.InstalledAs);
        if (!File.Exists(localFile)) return false;

        var localSize = new FileInfo(localFile).Length;

        // Pick the correct staging paths based on the installed variant.
        var staged64 = record.AddonType == TypeReShadeNormal ? RsNormalStagedPath64 : RsStagedPath64;
        var staged32 = record.AddonType == TypeReShadeNormal ? RsNormalStagedPath32 : RsStagedPath32;

        // Defensive: skip update check if staged DLLs are suspiciously small (test artifacts)
        if (File.Exists(staged64) && new FileInfo(staged64).Length < 100_000
            && File.Exists(staged32) && new FileInfo(staged32).Length < 100_000)
            return false;

        // Check against the 64-bit staged DLL first, then 32-bit.
        // The installed file matches the staged DLL it was copied from.
        // If either staged file has a different size, an update is available.
        if (File.Exists(staged64) && localSize == new FileInfo(staged64).Length)
            return false; // matches current 64-bit — no update
        if (File.Exists(staged32) && localSize == new FileInfo(staged32).Length)
            return false; // matches current 32-bit — no update

        // Size doesn't match either staged DLL — update available
        return true;
    }

    public async Task<bool> CheckForUpdateAsync(AuxInstalledRecord record)
    {
        if (record.SourceUrl == null)
        {
            CrashReporter.Log($"[AuxInstallService.CheckForUpdateAsync] [{record.AddonType}] {record.GameName}: no SourceUrl — skipping");
            return false;
        }

        // Resolve addon search path for .addon64/.addon32 files
        var ext = Path.GetExtension(record.InstalledAs);
        var isAddon = ext.Equals(".addon64", StringComparison.OrdinalIgnoreCase)
                   || ext.Equals(".addon32", StringComparison.OrdinalIgnoreCase);
        var deployDir = isAddon
            ? ModInstallService.GetAddonDeployPath(record.InstallPath)
            : record.InstallPath;
        var localFile = Path.Combine(deployDir, record.InstalledAs);
        if (!File.Exists(localFile))
        {
            // Fallback: file may be in the base install path (pre-AddonPath)
            localFile = Path.Combine(record.InstallPath, record.InstalledAs);
        }
        if (!File.Exists(localFile))
        {
            CrashReporter.Log($"[AuxInstallService.CheckForUpdateAsync] [{record.AddonType}] {record.GameName}: local file missing — update needed");
            return true;
        }

        var localSize = new FileInfo(localFile).Length;
        CrashReporter.Log($"[AuxInstallService.CheckForUpdateAsync] [{record.AddonType}] {record.GameName}: local={localSize}, stored={record.RemoteFileSize}");

        try
        {
            // ── Strategy 1: HEAD for Content-Length ─────────────────────────────
            long? remoteSize = null;
            try
            {
                var headResp = await _http.SendAsync(new HttpRequestMessage(HttpMethod.Head, record.SourceUrl));
                if (headResp.IsSuccessStatusCode)
                    remoteSize = headResp.Content.Headers.ContentLength;
                CrashReporter.Log($"[AuxInstallService.CheckForUpdateAsync] [{record.AddonType}] {record.GameName}: HEAD status={headResp.StatusCode}, CL={remoteSize}");
            }
            catch (Exception ex) { CrashReporter.Log($"[AuxInstallService.CheckForUpdateAsync] [{record.AddonType}] HEAD failed — {ex.Message}"); }

            // ── Strategy 2: Range GET for Content-Range total ──────────────────
            if (!remoteSize.HasValue)
            {
                try
                {
                    var rangeReq = new HttpRequestMessage(HttpMethod.Get, record.SourceUrl);
                    rangeReq.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
                    var rangeResp = await _http.SendAsync(rangeReq, HttpCompletionOption.ResponseHeadersRead);
                    if (rangeResp.Content.Headers.ContentRange?.Length is long totalLen)
                        remoteSize = totalLen;
                    else if (rangeResp.IsSuccessStatusCode)
                        remoteSize = rangeResp.Content.Headers.ContentLength;
                    CrashReporter.Log($"[AuxInstallService.CheckForUpdateAsync] [{record.AddonType}] {record.GameName}: Range GET size={remoteSize}");
                    rangeResp.Dispose();
                }
                catch (Exception ex) { CrashReporter.Log($"[AuxInstallService.CheckForUpdateAsync] [{record.AddonType}] Range failed — {ex.Message}"); }
            }

            // ── Strategy 3: Full download comparison ───────────────────────────
            // If we still have no remote size, or if sizes match (could be a same-size
            // different-content update), download the file and compare bytes.
            if (!remoteSize.HasValue || remoteSize.Value == localSize)
            {
                CrashReporter.Log($"[AuxInstallService.CheckForUpdateAsync] [{record.AddonType}] {record.GameName}: falling back to download comparison (remoteSize={remoteSize}, localSize={localSize})");
                try
                {
                    var cacheName = record.InstalledAs;
                    var tempPath = Path.Combine(DownloadPaths.Misc, cacheName + $".update-check-{Guid.NewGuid():N}");
                    Directory.CreateDirectory(DownloadPaths.Misc);

                    var response = await _http.GetAsync(record.SourceUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        var bytes = await response.Content.ReadAsByteArrayAsync();
                        await File.WriteAllBytesAsync(tempPath, bytes);
                        var downloadedSize = bytes.Length;

                        CrashReporter.Log($"[AuxInstallService.CheckForUpdateAsync] [{record.AddonType}] {record.GameName}: downloaded {downloadedSize} bytes, local {localSize} bytes");

                        if (downloadedSize != localSize)
                        {
                            // Size differs — definite update. Move downloaded file to cache
                            // so the next install picks it up without re-downloading.
                            var cachePath = Path.Combine(DownloadPaths.Misc, cacheName);
                            if (File.Exists(cachePath)) File.Delete(cachePath);
                            File.Move(tempPath, cachePath);
                            return true;
                        }

                        // Same size — compare bytes directly
                        var localBytes = await File.ReadAllBytesAsync(localFile);
                        bool contentDiffers = !bytes.AsSpan().SequenceEqual(localBytes.AsSpan());
                        CrashReporter.Log($"[AuxInstallService.CheckForUpdateAsync] [{record.AddonType}] {record.GameName}: same size, content differs={contentDiffers}");

                        if (contentDiffers)
                        {
                            var cachePath = Path.Combine(DownloadPaths.Misc, cacheName);
                            if (File.Exists(cachePath)) File.Delete(cachePath);
                            File.Move(tempPath, cachePath);
                            return true;
                        }

                        // Identical — clean up temp
                        try { File.Delete(tempPath); } catch (Exception cleanupEx) { CrashReporter.Log($"[AuxInstallService.CheckForUpdateAsync] Failed to clean up temp file '{tempPath}' — {cleanupEx.Message}"); }
                        return false;
                    }
                }
                catch (Exception ex) { CrashReporter.Log($"[AuxInstallService.CheckForUpdateAsync] [{record.AddonType}] Download compare failed — {ex.Message}"); }

                return false;
            }

            // Size-based comparison
            bool update = remoteSize.Value != localSize;
            CrashReporter.Log($"[AuxInstallService.CheckForUpdateAsync] [{record.AddonType}] {record.GameName}: remote={remoteSize}, local={localSize} → update={update}");
            return update;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[AuxInstallService.CheckForUpdateAsync] [{record.AddonType}] {record.GameName} exception — {ex.Message}");
            return false;
        }
    }

    // ── Uninstall ─────────────────────────────────────────────────────────────────

    public void Uninstall(AuxInstalledRecord record)
    {
        // Resolve addon search path for .addon64/.addon32 files
        var ext = Path.GetExtension(record.InstalledAs);
        var isAddon = ext.Equals(".addon64", StringComparison.OrdinalIgnoreCase)
                   || ext.Equals(".addon32", StringComparison.OrdinalIgnoreCase);
        var deployDir = isAddon
            ? ModInstallService.GetAddonDeployPath(record.InstallPath)
            : record.InstallPath;
        var path = Path.Combine(deployDir, record.InstalledAs);
        if (File.Exists(path))
            File.Delete(path);
        else
        {
            // Fallback: file may be in the base install path (pre-AddonPath)
            var fallback = Path.Combine(record.InstallPath, record.InstalledAs);
            if (File.Exists(fallback)) File.Delete(fallback);
        }
        RemoveRecord(record);

        // Restore any foreign DLL that was backed up when RDXC took over this slot.
        RestoreForeignDll(path);

        // If a user-owned reshade-shaders folder was renamed to reshade-shaders-original
        // when we deployed ours, restore it now that RS has been uninstalled.
        if (!string.IsNullOrEmpty(record.InstallPath))
            _shaderPackService.RestoreOriginalIfPresent(record.InstallPath);
    }

    /// <inheritdoc />
    public void UninstallDllOnly(AuxInstalledRecord record)
    {
        // Resolve addon search path for .addon64/.addon32 files
        var ext = Path.GetExtension(record.InstalledAs);
        var isAddon = ext.Equals(".addon64", StringComparison.OrdinalIgnoreCase)
                   || ext.Equals(".addon32", StringComparison.OrdinalIgnoreCase);
        var deployDir = isAddon
            ? ModInstallService.GetAddonDeployPath(record.InstallPath)
            : record.InstallPath;
        var path = Path.Combine(deployDir, record.InstalledAs);
        if (File.Exists(path))
            File.Delete(path);
        else
        {
            var fallback = Path.Combine(record.InstallPath, record.InstalledAs);
            if (File.Exists(fallback)) File.Delete(fallback);
        }
        RemoveRecord(record);

        // Restore any foreign DLL that was backed up when RDXC took over this slot.
        RestoreForeignDll(path);

        // NOTE: intentionally does NOT call RestoreOriginalIfPresent —
        // this variant is used when shaders must stay untouched.
    }

    // ── DB ────────────────────────────────────────────────────────────────────────

    public List<AuxInstalledRecord> LoadAll() => LoadDb();

    public AuxInstalledRecord? FindRecord(string gameName, string installPath, string addonType)
    {
        return LoadDb().FirstOrDefault(r =>
            r.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase) &&
            r.InstallPath.Equals(installPath, StringComparison.OrdinalIgnoreCase) &&
            r.AddonType.Equals(addonType, StringComparison.OrdinalIgnoreCase));
    }

    private void SaveRecord(AuxInstalledRecord record)
    {
        var db = LoadDb();
        var i  = db.FindIndex(r =>
            r.GameName.Equals(record.GameName, StringComparison.OrdinalIgnoreCase) &&
            r.InstallPath.Equals(record.InstallPath, StringComparison.OrdinalIgnoreCase) &&
            r.AddonType.Equals(record.AddonType, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) db[i] = record; else db.Add(record);
        SaveDb(db);
    }

    public void SaveAuxRecord(AuxInstalledRecord record) => SaveRecord(record);

    public void RemoveRecord(AuxInstalledRecord record)
    {
        var db = LoadDb();
        db.RemoveAll(r =>
            r.GameName.Equals(record.GameName, StringComparison.OrdinalIgnoreCase) &&
            r.InstallPath.Equals(record.InstallPath, StringComparison.OrdinalIgnoreCase) &&
            r.AddonType.Equals(record.AddonType, StringComparison.OrdinalIgnoreCase));
        SaveDb(db);
    }

    private List<AuxInstalledRecord> LoadDb()
    {
        try
        {
            if (!File.Exists(DbPath)) return new();
            return JsonSerializer.Deserialize<List<AuxInstalledRecord>>(File.ReadAllText(DbPath)) ?? new();
        }
        catch (Exception ex) { CrashReporter.Log($"[AuxInstallService.LoadDb] Failed to load DB from '{DbPath}' — {ex.Message}"); return new(); }
    }

    private void SaveDb(List<AuxInstalledRecord> db)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
        var json = JsonSerializer.Serialize(db,
            new JsonSerializerOptions { WriteIndented = true });

        FileHelper.WriteAllTextWithRetry(DbPath, json, "AuxInstallService.SaveDb");
    }
}
