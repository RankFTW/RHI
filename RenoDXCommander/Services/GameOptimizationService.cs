using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Services;

/// <summary>
/// The non-UI half of "One-Click Optimize": reads a game's current state, builds the preflight input, captures the restore
/// point, and undoes the NVIDIA-profile and DLL parts. Applying the defaults is <see cref="DlssDefaultsApplier"/> (shared with
/// Quick Apply). Installing / removing Neural Rendering is driven from the UI layer (DetailPanelBuilder.OneClick.cs) because
/// RHI's existing NR install code is UI-bound; it reuses that code rather than duplicating it.
/// Nothing here is hardware specific: it only uses the defaults the user configured in RHI.
/// </summary>
public sealed class GameOptimizationService
{
    private readonly INvidiaGameProfile _profile;
    private readonly IDlssStreamlineService _dlss;

    public GameOptimizationService(INvidiaGameProfile profile, IDlssStreamlineService dlss)
    {
        _profile = profile;
        _dlss = dlss;
    }

    // ── Reading state ─────────────────────────────────────────────────────────────

    public GameOptimizationSnapshot CaptureSnapshot(GameCardViewModel card, string? nrRoute, bool nrSupported, bool nrInstalled, string? nrDefaultLabel = null)
    {
        var name = card.GameName;
        var path = card.InstallPath ?? "";
        bool nv = _profile.IsSupported && path.Length > 0;
        return new GameOptimizationSnapshot
        {
            HasDlss = card.HasDlss, HasDlssd = card.HasDlssd, HasDlssg = card.HasDlssg,
            HasStreamline = card.HasStreamline, HasDlssnr = card.HasDlssnr,
            DlssVersion = card.DlssInstalledVersion, DlssdVersion = card.DlssdInstalledVersion,
            DlssgVersion = card.DlssgInstalledVersion, StreamlineVersion = card.StreamlineInstalledVersion,
            DlssnrVersion = card.DlssnrInstalledVersion,
            SrPreset = nv ? _profile.GetSrPreset(name, path) : 0,
            RrPreset = nv ? _profile.GetRrPreset(name, path) : 0,
            FgPreset = nv ? _profile.GetFgPreset(name, path) : 0,
            NrPreset = nv ? _profile.GetNrPreset(name, path) : 0,
            SrRenderScale = nv ? _profile.GetSrRenderScale(name, path) : 0,
            RrRenderScale = nv ? _profile.GetRrRenderScale(name, path) : 0,
            SrOverride = nv && _profile.IsSrDriverOverrideActive(name, path),
            RrOverride = nv && _profile.IsRrDriverOverrideActive(name, path),
            FgOverride = nv && _profile.IsFgDriverOverrideActive(name, path),
            NrRoute = nrRoute, NrRouteSupported = nrSupported, NrInstalled = nrInstalled,
            ReShadeInstalled = card.IsRsInstalled,
            NrDllMatchesDefault = card.HasDlssnr ? NrDllMatchesCached(card.DlssDetection?.DlssnrPath, NrCachePath(nrDefaultLabel)) : null,
        };
    }

    /// <summary>Where RHI keeps the downloaded NR DLL for a build label (mirrors DlssStreamlineService's cache layout).</summary>
    public static string? NrCachePath(string? label)
        => string.IsNullOrWhiteSpace(label) ? null
         : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "DLSS-NR", label.Trim(), "nvngx_dlssnr.dll");

    /// <summary>
    /// True/false when the game's DLL is / is not the same build as the cached one; null when either file is missing.
    /// Build labels are not what the DLL reports as its file version, so identity is decided by content: identical length
    /// plus identical 256 KB blocks at the start, middle and end (these DLLs are ~160 MB; a full hash on every status
    /// refresh would be wasteful and two different builds do not agree on all three blocks).
    /// </summary>
    public static bool? NrDllMatchesCached(string? gameDll, string? cachedDll)
    {
        try
        {
            if (string.IsNullOrEmpty(gameDll) || string.IsNullOrEmpty(cachedDll)) return null;
            var a = new FileInfo(gameDll); var b = new FileInfo(cachedDll);
            if (!a.Exists || !b.Exists) return null;
            if (a.Length != b.Length) return false;
            const int block = 256 * 1024;
            using var fa = new FileStream(gameDll, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var fb = new FileStream(cachedDll, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var ba = new byte[block]; var bb = new byte[block];
            foreach (long off in new[] { 0L, Math.Max(0, a.Length / 2 - block / 2), Math.Max(0, a.Length - block) })
            {
                fa.Seek(off, SeekOrigin.Begin); fb.Seek(off, SeekOrigin.Begin);
                int na = fa.ReadAtLeast(ba.AsSpan(0, block), block, throwOnEndOfStream: false);
                int nb = fb.ReadAtLeast(bb.AsSpan(0, block), block, throwOnEndOfStream: false);
                if (na != nb || !ba.AsSpan(0, na).SequenceEqual(bb.AsSpan(0, nb))) return false;
            }
            return true;
        }
        catch { return null; }
    }

    /// <summary>Components whose presence makes an automatic change risky; the user must review them first.</summary>
    public static IReadOnlyList<string> DetectForeignComponents(GameCardViewModel card)
    {
        var list = new List<string>();
        if (card.IsOsInstalled) list.Add("OptiScaler");
        return list;
    }

    /// <summary>True when a process is running from inside the game's install folder.</summary>
    public static bool IsGameRunning(string installPath)
    {
        if (string.IsNullOrEmpty(installPath)) return false;
        var root = installPath.TrimEnd('\\', '/') + "\\";
        foreach (var p in System.Diagnostics.Process.GetProcesses())
        {
            try
            {
                var exe = p.MainModule?.FileName;
                if (exe != null && exe.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { /* access denied / exited: cannot be the game we can see */ }
            finally { p.Dispose(); }
        }
        return false;
    }

    // ── Restore point ─────────────────────────────────────────────────────────────

    public OptimizationRestorePoint CaptureRestorePoint(
        GameCardViewModel card, OptimizationDefaults defaults, string? nrStoredMethodBefore, bool nrInstalledBefore)
    {
        var name = card.GameName;
        var path = card.InstallPath ?? "";
        var rp = new OptimizationRestorePoint
        {
            GameKey = OptimizationEvaluator.GameKey(name, card.Source),
            CreatedUtc = DateTime.UtcNow,
            DefaultsFingerprint = defaults.Fingerprint(),
            NrStoredMethodBefore = nrStoredMethodBefore,
            NrWasInstalledBefore = nrInstalledBefore,
        };

        if (_profile.IsSupported && path.Length > 0)
        {
            rp.SrPreset = _profile.GetSrPreset(name, path);
            rp.RrPreset = _profile.GetRrPreset(name, path);
            rp.FgPreset = _profile.GetFgPreset(name, path);
            rp.NrPreset = _profile.GetNrPreset(name, path);
            rp.SrRenderScale = _profile.GetSrRenderScale(name, path);
            rp.RrRenderScale = _profile.GetRrRenderScale(name, path);
            rp.SrOverride = _profile.IsSrDriverOverrideActive(name, path);
            rp.RrOverride = _profile.IsRrDriverOverrideActive(name, path);
            rp.FgOverride = _profile.IsFgDriverOverrideActive(name, path);
            rp.ProfileCaptured = true;
        }

        var det = card.DlssDetection;
        if (det != null)
        {
            void Add(string comp, string? p, string? ver, bool backup)
            {
                if (!string.IsNullOrEmpty(p)) rp.Dlls.Add(new DllStateRecord { Component = comp, Path = p, VersionBefore = ver, HadBackupBefore = backup });
            }
            Add("Dlss", det.DlssPath, card.DlssInstalledVersion, card.DlssHasBackup);
            Add("Dlssd", det.DlssdPath, card.DlssdInstalledVersion, card.DlssdHasBackup);
            Add("Dlssg", det.DlssgPath, card.DlssgInstalledVersion, card.DlssgHasBackup);
            Add("Dlssnr", det.DlssnrPath, card.DlssnrInstalledVersion, card.DlssnrHasBackup);
            Add("Streamline", det.StreamlineFolder, card.StreamlineInstalledVersion, card.StreamlineHasBackup);
        }
        return rp;
    }

    // ── Undo (NVIDIA profile + DLLs) ──────────────────────────────────────────────

    /// <summary>Puts the per-game NVIDIA profile values back exactly as captured. Returns human-readable log lines.</summary>
    public IReadOnlyList<string> UndoProfile(GameCardViewModel card, OptimizationRestorePoint rp)
    {
        var log = new List<string>();
        var name = card.GameName;
        var path = card.InstallPath ?? "";
        if (!rp.ProfileCaptured) { log.Add("NVIDIA profile: nothing was captured, left as is."); return log; }
        if (!_profile.IsSupported) { log.Add("NVIDIA profile: driver profile access unavailable, could not restore."); return log; }

        // A preset/scale of 0 means "not set": the Set* methods delete the per-game override, which is exactly the prior state.
        _profile.SetSrPreset(name, path, rp.SrPreset);
        _profile.SetRrPreset(name, path, rp.RrPreset);
        _profile.SetFgPreset(name, path, rp.FgPreset);
        _profile.SetNrPreset(name, path, rp.NrPreset);
        _profile.SetSrRenderScale(name, path, rp.SrRenderScale);
        _profile.SetRrRenderScale(name, path, rp.RrRenderScale);
        _profile.SetSrDriverOverride(name, path, rp.SrOverride);
        _profile.SetRrDriverOverride(name, path, rp.RrOverride);
        _profile.SetFgDriverOverride(name, path, rp.FgOverride);
        log.Add("NVIDIA profile: presets, render scales and driver overrides restored.");
        return log;
    }

    /// <summary>Restores DLLs using RHI's own backups / version swaps. Returns human-readable log lines (limits included).</summary>
    public async Task<IReadOnlyList<string>> UndoDllsAsync(GameCardViewModel card, OptimizationRestorePoint rp)
    {
        var log = new List<string>();
        if (rp.Dlls.Count == 0) return log;

        card.RefreshDlssVersions(_dlss);   // read the current versions / backup flags from disk
        var now = new Dictionary<string, (string? version, bool hasBackup)>
        {
            ["Dlss"] = (card.DlssInstalledVersion, card.DlssHasBackup),
            ["Dlssd"] = (card.DlssdInstalledVersion, card.DlssdHasBackup),
            ["Dlssg"] = (card.DlssgInstalledVersion, card.DlssgHasBackup),
            ["Dlssnr"] = (card.DlssnrInstalledVersion, card.DlssnrHasBackup),
            ["Streamline"] = (card.StreamlineInstalledVersion, card.StreamlineHasBackup),
        };

        foreach (var step in OptimizationEvaluator.PlanDllUndo(rp.Dlls, now))
        {
            try
            {
                switch (step.Action)
                {
                    case DllUndoAction.RestoreOriginal:
                        if (step.Component == "Streamline") _dlss.RestoreStreamline(step.Path); else _dlss.Restore(step.Path);
                        break;
                    case DllUndoAction.SwapToPreviousVersion:
                        switch (step.Component)
                        {
                            case "Dlss": await _dlss.SwapDlssAsync(step.Path, step.Version!); break;
                            case "Dlssd": await _dlss.SwapDlssdAsync(step.Path, step.Version!); break;
                            case "Dlssg": await _dlss.SwapDlssgAsync(step.Path, step.Version!); break;
                            case "Dlssnr": await _dlss.SwapDlssnrAsync(step.Path, step.Version!); break;
                            case "Streamline": await _dlss.SwapStreamlineAsync(step.Path, step.Version!); break;
                        }
                        break;
                }
                if (step.Action != DllUndoAction.None) log.Add($"{step.Component}: {step.Reason}");
            }
            catch (Exception ex)
            {
                log.Add($"{step.Component}: could not restore ({ex.Message})");
                CrashReporter.Log($"[GameOptimizationService.UndoDlls] {step.Component} failed — {ex.Message}");
            }
        }
        card.RefreshDlssVersions(_dlss);
        return log;
    }
}
