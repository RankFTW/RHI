// RtxEncoreService.NoDlssRuntimes.cs -- the NVIDIA files RTX Encore (2.0.0+) needs in a game that ships no
// DLSS, placed beside the game's exe the way RTX Encore Hub 2.x does:
//   - upscaling: nvngx_dlss.dll 310.9.16 (the mod accepts 310.9.x by version);
//   - frame generation: Streamline 2.14.16 sl.interposer / common / dlss / dlss_g / reflex / pcl + nvngx_dlssg.dll
//     310.9.16. The mod checks these by SHA-256 (hashes below, from its own table; sl.dlss.dll is in the
//     Hub's set but not checked by the mod) and refuses any other build.
// Rules (Hub: a game that ships its own frame generation or Streamline gets nothing; a file name that's
// already taken is never overwritten): upscaling only when the game has no DLSS of its own, frame generation
// only when it has no DLSS-G and no Streamline (a game with its own FSR / XeSS frame generation still gets them:
// RTX Encore decides at runtime and never adds a second frame generation); nothing with RHI's OptiScaler
// installed; no frame generation
// files with HSF installed (it adds DLSS-G itself); 64-bit only. Nothing is turned on: settings stay at the
// mod's defaults (its menu or RHI's cog turns the features on).
// Record: <installPath>\rtx-encore-runtimes\nvidia\rhi_installed.txt (RtxEncoreNvidiaMarker). DLSS detection
// skips listed files, so swaps / auto-update / Batch Deploy never touch them. A listed file the user deleted
// is never put back. Removal deletes only files whose SHA-256 still matches.

using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace RenoDXCommander.Services;

public partial class RtxEncoreService
{
    // ── Versions and accepted builds ──────────────────────────────────────────

    /// <summary>dlss_manifest.json versions RHI takes the files from.</summary>
    public const string NoDlssDlssVersion       = "310.9.16";
    public const string NoDlssDlssgVersion      = "310.9.16";
    public const string NoDlssStreamlineVersion = "2.14.16.0";

    public const string UpscalingFile = "nvngx_dlss.dll";
    public const string DlssgFile     = "nvngx_dlssg.dll";

    /// <summary>Frame generation files and the SHA-256 builds RTX Encore accepts for each.</summary>
    internal static readonly (string File, string[] Sha256)[] FrameGenFiles =
    {
        (DlssgFile,           new[] { "cadee1aa6db3907babdc3285da2aebc4553d92b89c38ba3eae8abd8bba1bea72",
                                      "8d59d49b36410dd6b62fef2503a5871d347415bd85d943fa0848563a95dd50c7" }),
        ("sl.interposer.dll", new[] { "b050969a123dcafa0e40ad64cbe8b2e6dc21e38b58b0efb7e0d79b8875509d20",
                                      "b1d7395aebd7b9722841d64dcf7433680020b6a8e421f851c16d5ba0c2182edf" }),
        ("sl.common.dll",     new[] { "38eea3b4209afc455636ea372b5933d65fcc13becec6c84d101dcfc4d8648f75" }),
        ("sl.dlss_g.dll",     new[] { "3ed9538df724993bea8a050195a6a78b9c8c8a566d5b05ade477aab3c390e90f" }),
        ("sl.reflex.dll",     new[] { "443577e869352436ff7ec8f4d9c90418d3e95368436ce219261e0f4f07a38122" }),
        ("sl.pcl.dll",        new[] { "5698c38e722318681f215c9fe68e45b9bedd51378acc699d8eee26e961e57770" }),
        ("sl.dlss.dll",       new[] { "18871eea87c90345da97d8109faa81ea7d5840e8d99b51b55b0e46cfa177f84e" }),
    };

    internal static IEnumerable<string> FrameGenFileNames => FrameGenFiles.Select(f => f.File);

    // ── Results ───────────────────────────────────────────────────────────────

    /// <summary>One unit (upscaling file or frame generation set): installed, or why not (plain English).</summary>
    public sealed record NoDlssUnitState(bool Installed, string? Reason);

    public sealed record NoDlssRuntimeResult(NoDlssUnitState Upscaling, NoDlssUnitState FrameGeneration);

    /// <summary>For the cog: what the marker lists and what is still on disk, plus the last decision this session.</summary>
    public sealed record NoDlssRuntimeStatus(
        bool UpscalingInstalled, bool UpscalingMissing,
        bool FrameGenInstalled, bool FrameGenMissing,
        NoDlssRuntimeResult? LastResult);

    private readonly ConcurrentDictionary<string, NoDlssRuntimeResult> _noDlssLastResults = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _noDlssLock = new(1, 1);

    // ── Decision (pure, unit tested) ──────────────────────────────────────────

    public const string ReasonGameHasDlss   = "the game has its own DLSS";
    public const string ReasonGameHasFg     = "the game has its own DLSS Frame Generation or Streamline";
    public const string ReasonOptiScaler    = "OptiScaler is installed";
    public const string ReasonHsf           = "HSF is installed";
    public const string ReasonDownload      = "download failed";
    public const string ReasonBadBuild      = "files didn't match the versions RTX Encore accepts";
    public const string Reason32Bit         = "32-bit game";
    public const string ReasonWriteFailed   = "the game folder couldn't be written to";
    public const string ReasonUserRemoved   = "removed from the game folder (RHI doesn't put back files you removed)";
    public static string ReasonTaken(string name) => $"a file with the same name is already there ({name})";

    /// <summary>
    /// Why a unit must not be installed, or null when it may be. <paramref name="takenFile"/> is the first
    /// target name that already exists in the install folder and isn't RHI's own copy for RTX Encore.
    /// </summary>
    public static string? NoDlssSkipReason(bool frameGenUnit, bool is32Bit, bool optiScalerInstalled, bool hsfInstalled,
        bool gameHasDlss, bool gameHasFgOrStreamline, string? takenFile)
    {
        if (is32Bit) return Reason32Bit;
        if (optiScalerInstalled) return ReasonOptiScaler;
        if (frameGenUnit)
        {
            if (hsfInstalled) return ReasonHsf;
            if (gameHasFgOrStreamline) return ReasonGameHasFg;
        }
        else if (gameHasDlss) return ReasonGameHasDlss;
        return takenFile != null ? ReasonTaken(takenFile) : null;
    }

    /// <summary>
    /// Why RHI's files of an installed unit must come out now (the game gained its own, or OptiScaler / HSF
    /// was installed), or null to keep them.
    /// </summary>
    public static string? NoDlssRemoveReason(bool frameGenUnit, bool optiScalerInstalled, bool hsfInstalled,
        bool gameHasDlss, bool gameHasFgOrStreamline)
    {
        if (optiScalerInstalled) return ReasonOptiScaler;
        if (frameGenUnit)
        {
            if (hsfInstalled) return ReasonHsf;
            if (gameHasFgOrStreamline) return ReasonGameHasFg;
            return null;
        }
        return gameHasDlss ? ReasonGameHasDlss : null;
    }

    /// <summary>True when the hash is one RTX Encore accepts for <paramref name="file"/> (frame generation files).</summary>
    public static bool IsAcceptedFrameGenBuild(string file, string sha256) =>
        FrameGenFiles.Any(f => f.File.Equals(file, StringComparison.OrdinalIgnoreCase)
                            && f.Sha256.Contains(sha256, StringComparer.OrdinalIgnoreCase));

    // ── Deploy ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Places the NVIDIA files RTX Encore needs in a game without DLSS (see file header). Units already listed in
    /// the marker are left as they are (a listed file the user deleted stays deleted). Call on a background thread.
    /// </summary>
    public async Task<NoDlssRuntimeResult> DeployNoDlssRuntimesAsync(
        string installPath, bool is32Bit, bool optiScalerInstalled, IDlssStreamlineService dlss)
    {
        await _noDlssLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var result = await DeployNoDlssRuntimesCoreAsync(installPath, is32Bit, optiScalerInstalled, dlss).ConfigureAwait(false);
            _noDlssLastResults[installPath] = result;
            return result;
        }
        finally { _noDlssLock.Release(); }
    }

    private async Task<NoDlssRuntimeResult> DeployNoDlssRuntimesCoreAsync(
        string installPath, bool is32Bit, bool optiScalerInstalled, IDlssStreamlineService dlss)
    {
        const string tag = "[RtxEncoreService.DeployNoDlssRuntimes]";
        if (string.IsNullOrEmpty(installPath) || !Directory.Exists(installPath))
            return new(new(false, ReasonWriteFailed), new(false, ReasonWriteFailed));

        var marker = RtxEncoreNvidiaMarker.Read(installPath);
        bool Listed(string name) => marker.Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        bool upListed = Listed(UpscalingFile);
        bool fgListed = FrameGenFileNames.Any(Listed);

        // Cheap checks first: no scan needed when nothing could be installed
        if (is32Bit || optiScalerInstalled)
        {
            var r = NoDlssSkipReason(false, is32Bit, optiScalerInstalled, false, false, false, null)!;
            _crashReporter.Log($"{tag} Nothing for '{installPath}' — {r}");
            return new(StateOfListed(installPath, UpscalingFile, upListed) ?? new(false, r),
                       StateOfListedSet(installPath, fgListed) ?? new(false, r));
        }

        // Both already handled: nothing to decide, no scan
        if (upListed && fgListed)
            return new(StateOfListed(installPath, UpscalingFile, true)!, StateOfListedSet(installPath, true)!);

        bool hsf = HsfService.IsInstalledIn(installPath, is32Bit);
        // Detection skips RHI's own files for RTX Encore (marker), so these are the game's
        var det = dlss.Detect(installPath);
        bool gameHasDlss = det.DlssPath != null;
        bool gameHasFg = det.DlssgPath != null || det.StreamlineFolder != null || det.StreamlineInterposerPath != null;

        NoDlssUnitState up, fg;
        var newEntries = new List<RtxEncoreNvidiaMarker.Entry>();

        // ── Upscaling: nvngx_dlss.dll ──
        if (upListed) up = StateOfListed(installPath, UpscalingFile, true)!;
        else
        {
            var reason = NoDlssSkipReason(false, false, false, hsf, gameHasDlss, gameHasFg, Taken(installPath, new[] { UpscalingFile }));
            if (reason != null) up = new(false, reason);
            else
            {
                var src = await dlss.EnsureSpecificDlssCachedAsync(NoDlssDlssVersion).ConfigureAwait(false);
                if (src == null) up = new(false, ReasonDownload);
                else if (!IsAcceptedDlssBuild(src)) up = new(false, ReasonBadBuild);
                else
                {
                    var copied = CopyUnit(installPath, new (string, string)[] { (UpscalingFile, src) });
                    if (copied == null) up = new(false, ReasonWriteFailed);
                    else { newEntries.AddRange(copied); up = new(true, null); }
                }
            }
        }
        _crashReporter.Log($"{tag} Upscaling file for '{installPath}': {(up.Installed ? "installed" : up.Reason)}");

        // ── Frame generation: Streamline 2.14.16 + nvngx_dlssg.dll 310.9.16 ──
        if (fgListed) fg = StateOfListedSet(installPath, true)!;
        else
        {
            var reason = NoDlssSkipReason(true, false, false, hsf, gameHasDlss, gameHasFg, Taken(installPath, FrameGenFileNames));
            if (reason != null) fg = new(false, reason);
            else
            {
                var dlssgSrc = await dlss.EnsureSpecificDlssgCachedAsync(NoDlssDlssgVersion).ConfigureAwait(false);
                var slDir    = await dlss.EnsureSpecificStreamlineCachedAsync(NoDlssStreamlineVersion).ConfigureAwait(false);
                if (dlssgSrc == null || slDir == null) fg = new(false, ReasonDownload);
                else
                {
                    string dlssgPath = dlssgSrc, slFolder = slDir;
                    var sources = FrameGenFiles.Select(f => (f.File, f.File.Equals(DlssgFile, StringComparison.OrdinalIgnoreCase)
                        ? dlssgPath : Path.Combine(slFolder, f.File))).ToArray();
                    var bad = sources.FirstOrDefault(s => !File.Exists(s.Item2) || !IsAcceptedFrameGenBuild(s.Item1, Sha256Of(s.Item2) ?? ""));
                    if (bad.Item1 != null)
                    {
                        _crashReporter.Log($"{tag} Staged '{bad.Item2}' isn't a build RTX Encore accepts — frame generation files not installed");
                        fg = new(false, ReasonBadBuild);
                    }
                    else
                    {
                        var copied = CopyUnit(installPath, sources);
                        if (copied == null) fg = new(false, ReasonWriteFailed);
                        else { newEntries.AddRange(copied); fg = new(true, null); }
                    }
                }
            }
        }
        _crashReporter.Log($"{tag} Frame generation files for '{installPath}': {(fg.Installed ? "installed" : fg.Reason)}");

        if (newEntries.Count > 0 && !WriteMarker(installPath, marker.Concat(newEntries).ToList()))
        {
            // Without the record detection would show the files as the game's and swaps could replace them: undo
            foreach (var e in newEntries) DeleteIfOurs(installPath, e, tag);
            return new(newEntries.Any(e => e.Name.Equals(UpscalingFile, StringComparison.OrdinalIgnoreCase)) ? new(false, ReasonWriteFailed) : up,
                       newEntries.Any(e => e.Name.Equals(DlssgFile, StringComparison.OrdinalIgnoreCase)) ? new(false, ReasonWriteFailed) : fg);
        }
        return new(up, fg);
    }

    /// <summary>State of an already-listed upscaling file: installed, or user-removed. Null when not listed.</summary>
    private static NoDlssUnitState? StateOfListed(string installPath, string name, bool listed)
    {
        if (!listed) return null;
        return File.Exists(Path.Combine(installPath, name)) ? new(true, null) : new(false, ReasonUserRemoved);
    }

    private static NoDlssUnitState? StateOfListedSet(string installPath, bool listed)
    {
        if (!listed) return null;
        return FrameGenFileNames.All(n => File.Exists(Path.Combine(installPath, n))) ? new(true, null) : new(false, ReasonUserRemoved);
    }

    /// <summary>First of <paramref name="names"/> already in the install folder (the game's or the user's), or null.</summary>
    private static string? Taken(string installPath, IEnumerable<string> names) =>
        names.FirstOrDefault(n => File.Exists(Path.Combine(installPath, n)));

    /// <summary>nvngx_dlss.dll: RTX Encore accepts 310.9.x by version; NVIDIA's own build.</summary>
    private bool IsAcceptedDlssBuild(string path)
    {
        try
        {
            var vi = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
            var ver = $"{vi.FileMajorPart}.{vi.FileMinorPart}.{vi.FileBuildPart}.{vi.FilePrivatePart}";
            bool ok = vi.FileMajorPart == 310 && vi.FileMinorPart == 9
                && string.Equals(vi.CompanyName?.Trim(), "NVIDIA", StringComparison.OrdinalIgnoreCase);
            if (!ok) _crashReporter.Log($"[RtxEncoreService.IsAcceptedDlssBuild] '{path}' is {ver} from '{vi.CompanyName}' — not 310.9.x by NVIDIA");
            return ok;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException)
        {
            _crashReporter.Log($"[RtxEncoreService.IsAcceptedDlssBuild] Could not read '{path}' — {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Copies a unit's files (none may exist yet); on any failure removes what this call copied and returns null.
    /// </summary>
    private List<RtxEncoreNvidiaMarker.Entry>? CopyUnit(string installPath, IEnumerable<(string Name, string Source)> files)
    {
        var done = new List<RtxEncoreNvidiaMarker.Entry>();
        try
        {
            foreach (var (name, source) in files)
            {
                var dest = Path.Combine(installPath, name);
                File.Copy(source, dest, overwrite: false); // never over a file that appeared since the check
                var sha = Sha256Of(dest) ?? throw new IOException($"could not read back '{dest}'");
                done.Add(new RtxEncoreNvidiaMarker.Entry(name, new FileInfo(dest).Length, sha));
            }
            return done;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"[RtxEncoreService.CopyUnit] Failed in '{installPath}' — {ex.Message}; undoing this unit");
            foreach (var e in done) DeleteIfOurs(installPath, e, "[RtxEncoreService.CopyUnit]");
            return null;
        }
    }

    private bool WriteMarker(string installPath, List<RtxEncoreNvidiaMarker.Entry> entries)
    {
        var path = RtxEncoreNvidiaMarker.MarkerPath(installPath);
        try
        {
            if (entries.Count == 0)
            {
                if (File.Exists(path)) File.Delete(path);
                RemoveEmptyRuntimeFolders(installPath);
                return true;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, RtxEncoreNvidiaMarker.Format(entries));
            File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"[RtxEncoreService.WriteMarker] Failed for '{path}' — {ex.Message}");
            return false;
        }
    }

    /// <summary>Deletes a listed file when its SHA-256 still matches. True when gone (deleted or already missing).</summary>
    private bool DeleteIfOurs(string installPath, RtxEncoreNvidiaMarker.Entry e, string tag)
    {
        var path = FileHelper.SafeCombine(installPath, e.Name);
        if (path == null) { _crashReporter.Log($"{tag} Unsafe name '{e.Name}' in the record — ignored"); return true; }
        try
        {
            if (!File.Exists(path)) return true;
            var sha = Sha256Of(path);
            if (sha == null) return false;
            if (!sha.Equals(e.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                _crashReporter.Log($"{tag} Keeping '{path}': it was replaced after RHI placed it");
                return true; // not RHI's any more: drop it from the record
            }
            File.Delete(path);
            _crashReporter.Log($"{tag} Removed '{path}'");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"{tag} Could not remove '{path}' — {ex.Message}");
            return false;
        }
    }

    private void RemoveEmptyRuntimeFolders(string installPath)
    {
        var nvidia = Path.Combine(installPath, RtxEncoreNvidiaMarker.RuntimesFolder, RtxEncoreNvidiaMarker.NvidiaFolder);
        var runtimes = Path.Combine(installPath, RtxEncoreNvidiaMarker.RuntimesFolder);
        try
        {
            if (Directory.Exists(nvidia) && !Directory.EnumerateFileSystemEntries(nvidia).Any()) Directory.Delete(nvidia);
            if (Directory.Exists(runtimes) && !Directory.EnumerateFileSystemEntries(runtimes).Any()) Directory.Delete(runtimes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"[RtxEncoreService.RemoveEmptyRuntimeFolders] '{installPath}' — {ex.Message}");
        }
    }

    private string? Sha256Of(string path)
    {
        try
        {
            using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"[RtxEncoreService.Sha256Of] Could not read '{path}' — {ex.Message}");
            return null;
        }
    }

    // ── Remove ────────────────────────────────────────────────────────────────

    /// <summary>Removes every NVIDIA file RHI placed for RTX Encore (hash-matching only) and the record. Background thread.</summary>
    public void RemoveNoDlssRuntimes(string installPath)
    {
        if (string.IsNullOrEmpty(installPath)) return;
        _noDlssLock.Wait();
        try
        {
            RemoveEntries(installPath, _ => true, "[RtxEncoreService.RemoveNoDlssRuntimes]");
            _noDlssLastResults.TryRemove(installPath, out _);
        }
        finally { _noDlssLock.Release(); }
    }

    /// <summary>Removes the listed entries matching <paramref name="which"/>; the rest stay listed. False if some couldn't be removed.</summary>
    private bool RemoveEntries(string installPath, Func<RtxEncoreNvidiaMarker.Entry, bool> which, string tag)
    {
        var marker = RtxEncoreNvidiaMarker.Read(installPath);
        if (marker.Count == 0 && !File.Exists(RtxEncoreNvidiaMarker.MarkerPath(installPath))) return true;
        var keep = new List<RtxEncoreNvidiaMarker.Entry>();
        bool allOk = true;
        foreach (var e in marker)
        {
            if (!which(e)) { keep.Add(e); continue; }
            if (!DeleteIfOurs(installPath, e, tag)) { keep.Add(e); allOk = false; }
        }
        return WriteMarker(installPath, keep) && allOk;
    }

    // ── Reconcile ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Launch / periodic catch-up for a game with RTX Encore installed: removes RHI's files where the game now has
    /// its own (game update) or OptiScaler / HSF was installed, then installs units that were never installed
    /// (existing installs, or the reason went away). Background thread.
    /// </summary>
    public async Task ReconcileNoDlssRuntimesAsync(string installPath, bool is32Bit, bool optiScalerInstalled, IDlssStreamlineService dlss)
    {
        const string tag = "[RtxEncoreService.ReconcileNoDlssRuntimes]";
        if (string.IsNullOrEmpty(installPath) || !Directory.Exists(installPath)) return;

        var marker = RtxEncoreNvidiaMarker.Read(installPath);
        if (marker.Count > 0)
        {
            await _noDlssLock.WaitAsync().ConfigureAwait(false);
            try
            {
                bool hsf = HsfService.IsInstalledIn(installPath, is32Bit);
                bool needScan = !optiScalerInstalled;
                bool gameHasDlss = false, gameHasFg = false;
                if (needScan)
                {
                    var det = dlss.Detect(installPath); // skips RHI's listed files
                    gameHasDlss = det.DlssPath != null;
                    gameHasFg = det.DlssgPath != null || det.StreamlineFolder != null || det.StreamlineInterposerPath != null;
                }
                var upWhy = NoDlssRemoveReason(false, optiScalerInstalled, hsf, gameHasDlss, gameHasFg);
                var fgWhy = NoDlssRemoveReason(true, optiScalerInstalled, hsf, gameHasDlss, gameHasFg);
                if (upWhy != null && marker.Any(e => e.Name.Equals(UpscalingFile, StringComparison.OrdinalIgnoreCase)))
                {
                    _crashReporter.Log($"{tag} Removing the upscaling file from '{installPath}' — {upWhy}");
                    RemoveEntries(installPath, e => e.Name.Equals(UpscalingFile, StringComparison.OrdinalIgnoreCase), tag);
                }
                if (fgWhy != null && marker.Any(e => FrameGenFileNames.Contains(e.Name, StringComparer.OrdinalIgnoreCase)))
                {
                    _crashReporter.Log($"{tag} Removing the frame generation files from '{installPath}' — {fgWhy}");
                    RemoveEntries(installPath, e => FrameGenFileNames.Contains(e.Name, StringComparer.OrdinalIgnoreCase), tag);
                }
            }
            finally { _noDlssLock.Release(); }
        }

        // Units not listed (never installed, or just removed above) are decided again; listed ones are left alone
        await DeployNoDlssRuntimesAsync(installPath, is32Bit, optiScalerInstalled, dlss).ConfigureAwait(false);
    }

    // ── Status (cog) ──────────────────────────────────────────────────────────

    /// <summary>Cheap: reads the record and checks the listed files exist. Safe on the UI thread.</summary>
    public NoDlssRuntimeStatus ReadNoDlssRuntimeStatus(string installPath)
    {
        if (string.IsNullOrEmpty(installPath)) return new(false, false, false, false, null);
        var marker = RtxEncoreNvidiaMarker.Read(installPath);
        bool upListed = marker.Any(e => e.Name.Equals(UpscalingFile, StringComparison.OrdinalIgnoreCase));
        bool fgListed = marker.Any(e => FrameGenFileNames.Contains(e.Name, StringComparer.OrdinalIgnoreCase));
        bool upPresent = upListed && File.Exists(Path.Combine(installPath, UpscalingFile));
        bool fgPresent = fgListed && marker.Where(e => FrameGenFileNames.Contains(e.Name, StringComparer.OrdinalIgnoreCase))
                                           .All(e => File.Exists(Path.Combine(installPath, e.Name)));
        _noDlssLastResults.TryGetValue(installPath, out var last);
        return new(upPresent, upListed && !upPresent, fgPresent, fgListed && !fgPresent, last);
    }
}
