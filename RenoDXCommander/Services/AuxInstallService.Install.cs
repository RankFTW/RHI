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
        string? screenshotHotkey = null,
        string? channel = null,
        string? store = null,
        bool mergeIni = true)
    {
        Directory.CreateDirectory(DownloadPaths.Misc);

        var destName = ResolveReShadeDestName(gameName, installPath, filenameOverride, use32Bit, store, log: true);
        var destPath = Path.Combine(installPath, destName);

        var addonType = useNormalReShade ? TypeReShadeNormal : TypeReShade;

        // Refuse before anything changes: a name another RHI component holds, or a foreign file
        // whose .original already holds a backup. (The old ReShade is only removed further down,
        // once the new name is known to be usable, so a refusal never leaves the game without it.)
        var refusal = ReShadeTargetBlockedReason(gameName, installPath, destName);
        if (refusal != null)
        {
            CrashReporter.Log($"[AuxInstallService.InstallReShadeAsync] '{gameName}': not installing as '{destName}' — {refusal}");
            throw new ReShadeInstallRefusedException(refusal);
        }

        // ── Ensure staged DLLs exist (downloaded from reshade.me) ────────────────
        progress?.Report(("Preparing ReShade files...", 10));
        EnsureReShadeStaging();

        var effectiveChannel = channel ?? ChannelStable;
        var rsStagedPath = useNormalReShade
            ? (use32Bit ? RsNormalStagedPath32 : RsNormalStagedPath64)
            : GetStagedPathForChannel(effectiveChannel, use32Bit);

        // If this is a Custom channel with a per-game file selection, use that specific file
        if (!useNormalReShade && string.Equals(effectiveChannel, ChannelCustom, StringComparison.OrdinalIgnoreCase))
        {
            // Look up per-game custom selection via static accessor (set by the caller/DI)
            var customSelection = CustomReShadeSelectionResolver?.Invoke(gameName, store ?? "");
            if (!string.IsNullOrEmpty(customSelection))
            {
                var customFilePath = GetCustomReShadePathForFile(customSelection);
                if (File.Exists(customFilePath))
                    rsStagedPath = customFilePath;
            }
        }

        // If this is a legacy version and not cached, download it on-demand
        if (!File.Exists(rsStagedPath) && !useNormalReShade && IsLegacyVersion(effectiveChannel))
        {
            progress?.Report(($"Downloading ReShade {effectiveChannel}...", 15));
            var downloaded = await DownloadLegacyReShadeAsync(effectiveChannel, _http, progress);
            if (downloaded)
                rsStagedPath = GetStagedPathForChannel(effectiveChannel, use32Bit);
        }

        if (!File.Exists(rsStagedPath))
        {
            if (string.Equals(effectiveChannel, ChannelCustom, StringComparison.OrdinalIgnoreCase))
                throw new FileNotFoundException(
                    $"Custom ReShade DLL not found.\n" +
                    $"Expected: {rsStagedPath}\n" +
                    $"Place your {(use32Bit ? "ReShade32.dll" : "ReShade64.dll")} in:\n" +
                    $"{DlssStreamlineService.RsCustomDir}");

            throw new FileNotFoundException(
                $"ReShade DLLs not found in staging directory.\n" +
                $"Expected: {rsStagedPath}\n" +
                $"Please restart RHI to download ReShade from reshade.me.");
        }

        // ── Back up foreign DLL at destination ──────────────────────────────────
        // Never overwrite a file that isn't ReShade unless it's safely kept as .original.
        switch (BackupForeignDllChecked(destPath))
        {
            case ForeignBackupResult.Blocked:
                throw new ReShadeInstallRefusedException(
                    $"{destName} in the game folder isn't ReShade, and {destName}.original already holds a backup of an earlier file. " +
                    "RHI won't overwrite either. Move one of them out of the game folder and try again.");
            case ForeignBackupResult.Failed:
                throw new ReShadeInstallRefusedException(
                    $"Couldn't back up the existing {destName} in the game folder (is the game running?). Nothing was changed.");
        }

        // ── Record-aware cleanup: remove old non-standard DLL if InstalledAs differs ─
        // After the backup above, so a refused or failed backup leaves the old ReShade in place.
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

        // ── Copy staged DLL to game folder ────────────────────────────────────────
        progress?.Report(("Installing ReShade...", 80));
        File.Copy(rsStagedPath, destPath, overwrite: true);

        // Deploy reshade.ini alongside the DLL (skip if caller has locked ini updates for this game).
        if (mergeIni && File.Exists(RsIniPath))
            MergeRsIni(installPath, screenshotSavePath, overlayHotkey, screenshotHotkey, gameName);

        progress?.Report(("ReShade installed!", 100));

        // ── Shader deployment ─────────────────────────────────────────────────────
        // Build exclusions off the calling thread to avoid blocking the UI thread
        // on _settingsLock if a background shader pack check holds it concurrently.
        var exclAux = selectedPackIds == null ? null
            : await Task.Run(() => selectedPackIds
                .ToDictionary(id => id, id => _shaderPackService.GetExcludedFiles(id),
                    StringComparer.OrdinalIgnoreCase)).ConfigureAwait(false);
        _shaderPackService.SyncGameFolder(installPath, selectedPackIds, exclAux);

        var record = new AuxInstalledRecord
        {
            GameName       = gameName,
            InstallPath    = installPath,
            Store          = store ?? "",
            AddonType      = addonType,
            InstalledAs    = destName,
            SourceUrl      = null,       // bundled — no remote URL
            RemoteFileSize = null,       // no remote size to track
            InstalledAt    = DateTime.UtcNow,
            Channel        = useNormalReShade ? null : effectiveChannel,
        };
        SaveRecord(record);
        return record;
    }

    /// <summary>
    /// Why ReShade can't be installed as <paramref name="destName"/> in <paramref name="installPath"/>,
    /// or null when it can. Changes nothing. Refused: a name another RHI component holds
    /// (<see cref="OtherDllOwnerResolver"/>: ASI Loader, RE Framework, 20/30 FG, Universal MFG
    /// Unlocker, RTX Encore, ...), or a foreign file there whose <c>.original</c> already holds a backup.
    /// </summary>
    public static string? ReShadeTargetBlockedReason(string gameName, string installPath, string destName)
    {
        string? owner = null;
        try { owner = OtherDllOwnerResolver?.Invoke(gameName, installPath, destName); }
        catch (InvalidOperationException ex) // card list changed while it was read
        { CrashReporter.Log($"[AuxInstallService.ReShadeTargetBlockedReason] Owner lookup failed for '{gameName}' — {ex.Message}"); }
        if (owner != null)
            return $"{destName} is used by {owner} in this game. RHI won't install ReShade over it. " +
                   $"Choose another DLL name for ReShade, or remove {owner} first.";

        var destPath = Path.Combine(installPath, destName);
        if (IsForeignForReShade(destPath) && HasRealBackup(destPath))
            return $"{destName} in the game folder isn't ReShade, and {destName}.original already holds a backup of an earlier file. " +
                   "RHI won't overwrite either. Move one of them out of the game folder and try again.";
        return null;
    }

    /// <summary>
    /// The file name ReShade will be deployed as: <paramref name="filenameOverride"/> (or dxgi.dll),
    /// then "Load ReShade as ASI", then ReShade64.dll / ReShade32.dll when OptiScaler or Display
    /// Commander already holds that name. Used by the install and by the foreign-DLL check before it.
    /// </summary>
    public string ResolveReShadeDestName(string gameName, string installPath, string? filenameOverride,
        bool use32Bit, string? store, bool log = false)
    {
        var destName = !string.IsNullOrWhiteSpace(filenameOverride)
            ? filenameOverride
            : RsNormalName;

        // "Load ReShade as ASI" wins over any DLL name (the DLL name dropdown is greyed out while it's on)
        if (RsAsiModeResolver?.Invoke(gameName, store ?? "") == true)
        {
            destName = RsAsiName(use32Bit);
            if (log) CrashReporter.Log($"[AuxInstallService.InstallReShadeAsync] '{gameName}' loads ReShade as ASI — deploying as '{destName}'");
        }

        // ── OptiScaler coexistence: deploy as ReShade64.dll only when filenames actually conflict ──
        // Only rename when RS and OS would use the same DLL name (e.g. both want dxgi.dll).
        // If they use different names (e.g. OS=d3d12.dll, RS=dxgi.dll), no rename is needed.
        var osRecord = FindRecord(gameName, installPath, OptiScalerService.AddonType);
        if (osRecord != null && string.Equals(osRecord.InstalledAs, destName, StringComparison.OrdinalIgnoreCase))
        {
            if (log) CrashReporter.Log($"[AuxInstallService.InstallReShadeAsync] OptiScaler occupies '{destName}' — deploying ReShade as '{OptiScalerService.ReShadeCoexistName}'");
            destName = OptiScalerService.ReShadeCoexistName;
        }

        // ── DC occupancy check: avoid overwriting a DC file at the target name ──
        var dcRecord = FindRecord(gameName, installPath, "DisplayCommander");
        if (dcRecord != null &&
            string.Equals(dcRecord.InstalledAs, destName, StringComparison.OrdinalIgnoreCase))
        {
            destName = use32Bit ? RsStaged32 : RsStaged64;
            if (log) CrashReporter.Log($"[AuxInstallService.InstallReShadeAsync] Target '{dcRecord.InstalledAs}' occupied by DC — falling back to '{destName}'");
        }
        return destName;
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

        // Legacy versions are pinned — never show update available
        if (!string.IsNullOrEmpty(record.Channel)
            && !string.Equals(record.Channel, ChannelStable, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(record.Channel, ChannelNightly, StringComparison.OrdinalIgnoreCase))
            return false;

        var localFile = Path.Combine(record.InstallPath, record.InstalledAs);
        if (!File.Exists(localFile)) return false;

        var localSize = new FileInfo(localFile).Length;

        // Pick the correct staging paths based on the installed variant.
        string staged64, staged32;
        if (record.AddonType == TypeReShadeNormal)
        {
            staged64 = RsNormalStagedPath64;
            staged32 = RsNormalStagedPath32;
        }
        else if (string.Equals(record.Channel, ChannelNightly, StringComparison.OrdinalIgnoreCase))
        {
            staged64 = RsNightlyStagedPath64;
            staged32 = RsNightlyStagedPath32;
        }
        else
        {
            staged64 = RsStagedPath64;
            staged32 = RsStagedPath32;
        }

        // If neither staged DLL exists, we can't compare — assume no update
        // to avoid false positives (e.g. normal RS staging not downloaded yet).
        if (!File.Exists(staged64) && !File.Exists(staged32))
        {
            CrashReporter.Log($"[AuxInstallService.CheckReShadeUpdateLocal] [{record.AddonType}] {record.GameName}: no staged DLLs found — skipping");
            return false;
        }

        // Defensive: skip update check if staged DLLs are suspiciously small (corrupted/truncated)
        // ReShade DLLs are always 4-5MB+, so anything under 1MB is invalid.
        if (File.Exists(staged64) && new FileInfo(staged64).Length < 1_000_000)
        {
            CrashReporter.Log($"[AuxInstallService.CheckReShadeUpdateLocal] [{record.AddonType}] {record.GameName}: staged64 too small ({new FileInfo(staged64).Length} bytes) — skipping update check");
            return false;
        }
        if (File.Exists(staged32) && new FileInfo(staged32).Length < 1_000_000)
        {
            CrashReporter.Log($"[AuxInstallService.CheckReShadeUpdateLocal] [{record.AddonType}] {record.GameName}: staged32 too small ({new FileInfo(staged32).Length} bytes) — skipping update check");
            return false;
        }

        var staged64Size = File.Exists(staged64) ? new FileInfo(staged64).Length : -1;
        var staged32Size = File.Exists(staged32) ? new FileInfo(staged32).Length : -1;

        // Check against the primary staged DLLs for this record's channel
        if (File.Exists(staged64) && localSize == staged64Size)
            return false; // matches current 64-bit — no update
        if (File.Exists(staged32) && localSize == staged32Size)
            return false; // matches current 32-bit — no update

        // Also check against ALL other variant staging DLLs to avoid false update badges
        // when the user has switched channels but not yet reinstalled.
        var altPaths = new List<string>();
        if (record.AddonType == TypeReShadeNormal)
        {
            altPaths.AddRange(new[] { RsStagedPath64, RsStagedPath32, RsNightlyStagedPath64, RsNightlyStagedPath32 });
        }
        else
        {
            // Check both the other addon channel and normal staging
            if (!string.Equals(record.Channel, ChannelNightly, StringComparison.OrdinalIgnoreCase))
                altPaths.AddRange(new[] { RsNightlyStagedPath64, RsNightlyStagedPath32 });
            else
                altPaths.AddRange(new[] { RsStagedPath64, RsStagedPath32 });
            altPaths.AddRange(new[] { RsNormalStagedPath64, RsNormalStagedPath32 });
        }

        foreach (var altPath in altPaths)
        {
            if (File.Exists(altPath) && localSize == new FileInfo(altPath).Length)
                return false; // matches an alternate variant — no update
        }

        // Size doesn't match any staged DLL — update available
        CrashReporter.Log($"[AuxInstallService.CheckReShadeUpdateLocal] [{record.AddonType}] {record.GameName}: size mismatch — local={localSize}, staged64={staged64Size}, staged32={staged32Size} → update flagged");
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
                using var headResp = await _http.SendAsync(new HttpRequestMessage(HttpMethod.Head, record.SourceUrl));
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
                    using var rangeReq = new HttpRequestMessage(HttpMethod.Get, record.SourceUrl);
                    rangeReq.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
                    using var rangeResp = await _http.SendAsync(rangeReq, HttpCompletionOption.ResponseHeadersRead);
                    if (rangeResp.Content.Headers.ContentRange?.Length is long totalLen)
                        remoteSize = totalLen;
                    else if (rangeResp.IsSuccessStatusCode)
                        remoteSize = rangeResp.Content.Headers.ContentLength;
                    CrashReporter.Log($"[AuxInstallService.CheckForUpdateAsync] [{record.AddonType}] {record.GameName}: Range GET size={remoteSize}");
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

                    using var response = await _http.GetAsync(record.SourceUrl);
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

        // ── ReShade cleanup: remove associated INI files ──────────────────────
        if (!string.IsNullOrEmpty(record.InstallPath)
            && (record.AddonType == TypeReShade || record.AddonType == TypeReShadeNormal))
        {
            bool iniLocked = RsIniLockedResolver?.Invoke(record.GameName, record.Store ?? "") == true;
            foreach (var file in ReShadeUninstallCleanupFiles(iniLocked))
            {
                var filePath = Path.Combine(record.InstallPath, file);
                if (!File.Exists(filePath)) continue;
                try { File.Delete(filePath); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { CrashReporter.Log($"[AuxInstallService.Uninstall] Couldn't delete '{filePath}' — {ex.Message}"); }
            }
            CrashReporter.Log($"[AuxInstallService.Uninstall] '{record.GameName}': kept ReShadePreset.ini{(iniLocked ? ", reshade.ini and ReShade2.ini (locked)" : "")}");
        }
    }

    /// <summary>
    /// Config files ReShade uninstall deletes from the game folder. ReShadePreset.ini is never
    /// deleted: RHI doesn't record whether it put it there (the ⚙ button, a dropped preset) or the
    /// user made it, so it's kept. reshade.ini and ReShade2.ini (the second runtime's config, Source
    /// engine games) are kept when the game's ini is locked.
    /// </summary>
    internal static IReadOnlyList<string> ReShadeUninstallCleanupFiles(bool iniLocked) =>
        iniLocked ? Array.Empty<string>() : new[] { "reshade.ini", "ReShade2.ini" };

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
        lock (SafeJsonFile.LockFor(DbPath))
        {
            var db = LoadDb();
            var i  = db.FindIndex(r =>
                r.GameName.Equals(record.GameName, StringComparison.OrdinalIgnoreCase) &&
                r.InstallPath.Equals(record.InstallPath, StringComparison.OrdinalIgnoreCase) &&
                r.AddonType.Equals(record.AddonType, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) db[i] = record; else db.Add(record);
            SaveDb(db);
        }
    }

    public void SaveAuxRecord(AuxInstalledRecord record) => SaveRecord(record);

    public void RemoveRecord(AuxInstalledRecord record)
    {
        lock (SafeJsonFile.LockFor(DbPath))
        {
            var db = LoadDb();
            db.RemoveAll(r =>
                r.GameName.Equals(record.GameName, StringComparison.OrdinalIgnoreCase) &&
                r.InstallPath.Equals(record.InstallPath, StringComparison.OrdinalIgnoreCase) &&
                r.AddonType.Equals(record.AddonType, StringComparison.OrdinalIgnoreCase));
            SaveDb(db);
        }
    }

    /// <inheritdoc/>
    public int RepointRecords(string gameName, string oldPath, string newPath, Func<AuxInstalledRecord, bool>? include = null)
    {
        lock (SafeJsonFile.LockFor(DbPath))
        {
            var db = LoadDb();
            int changed = 0;
            foreach (var r in db.Where(r => r.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase)
                                         && r.InstallPath.Equals(oldPath, StringComparison.OrdinalIgnoreCase)
                                         && (include == null || include(r))).ToList())
            {
                bool newExists = db.Any(o => o != r && o.GameName.Equals(gameName, StringComparison.OrdinalIgnoreCase) && o.InstallPath.Equals(newPath, StringComparison.OrdinalIgnoreCase) && o.AddonType.Equals(r.AddonType, StringComparison.OrdinalIgnoreCase));
                if (newExists) db.Remove(r); else r.InstallPath = newPath;
                changed++;
                CrashReporter.Log($"[AuxInstallService.RepointRecords] '{gameName}': {(newExists ? "dropped old record (new folder has one)" : "moved")} '{oldPath}' → '{newPath}'");
            }
            if (changed > 0) SaveDb(db);
            return changed;
        }
    }

    /// <summary>Drops records left in an old folder of a game that has the same component recorded in its current one (see StaleRecords).</summary>
    public static void RemoveStaleDuplicateRecords()
    {
        lock (SafeJsonFile.LockFor(DbPath))
        {
            if (!File.Exists(DbPath)) return;
            var db = SafeJsonFile.ReadList<AuxInstalledRecord>(DbPath);
            var kept = StaleRecords.WithoutStaleDuplicates(db, r => $"{r.GameName}|{r.Store}|{r.AddonType}", r => r.InstallPath,
                Directory.Exists, out var removed);
            if (removed.Count == 0) return;
            foreach (var r in removed)
                CrashReporter.Log($"[AuxInstallService.RemoveStaleDuplicateRecords] '{r.GameName}' {r.AddonType}: dropped old record for '{r.InstallPath}' (folder gone; current folder has one)");
            SafeJsonFile.Write(DbPath, kept, "AuxInstallService.RemoveStaleDuplicateRecords", SafeJsonFile.Indented);
        }
    }

    // SafeJsonFile: atomic write + .bak, corrupt-file recovery, no save over an unreadable file.
    private List<AuxInstalledRecord> LoadDb() => SafeJsonFile.ReadList<AuxInstalledRecord>(DbPath);

    private void SaveDb(List<AuxInstalledRecord> db) =>
        SafeJsonFile.Write(DbPath, db, "AuxInstallService.SaveDb", SafeJsonFile.Indented);
}

/// <summary>
/// ReShade wasn't installed and nothing in the game folder was changed: the DLL name is held by
/// another RHI component, or a foreign file there can't be backed up safely. The message is for the user.
/// </summary>
public sealed class ReShadeInstallRefusedException : IOException
{
    public ReShadeInstallRefusedException(string message) : base(message) { }
}
