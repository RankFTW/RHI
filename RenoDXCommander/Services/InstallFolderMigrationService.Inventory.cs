// InstallFolderMigrationService.Inventory.cs -- what RHI owns in a game's old folder, read from each
// component's own tracking (install records, rhi_install.txt, the addon tracker, per-game settings,
// markers). Only files RHI can show it placed are listed; the game's own files are never moved.

using System.Text.Json;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public partial class InstallFolderMigrationService
{
    internal const string AddonGroupPrefix = "Addon ";
    private const string ShadersMarker = "Managed by RDXC.txt";   // ShaderPackService.ManagedMarkerFileName
    private const string LumaShadersMarker = ".rdxc-managed";     // LumaService's reshade-shaders marker
    private const string EncoreMarker = "rhi_installed.txt";      // Rtx40MfgService.XessMarkerFile
    private const string EncoreNativePrefix = "native:";

    private static readonly JsonSerializerOptions ManifestJson = new() { WriteIndented = true };

    private sealed class PlanContext
    {
        public required string OldDir { get; init; }
        public required string NewDir { get; init; }
        public string OldAddonDir { get; set; } = "";
        public string NewAddonDir { get; set; } = "";
        public bool AddonsMovable { get; set; } = true;
        /// <summary>Record InstallPath spellings that point at the old folder (repointed after the move).</summary>
        public HashSet<string> RecordPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private (FolderMovePlan Plan, PlanContext Ctx) BuildPlanCore(FolderMigrationRequest req)
    {
        var oldDir = InstallFolderMover.Norm(req.OldDir);
        var newDir = InstallFolderMover.Norm(req.NewDir);
        var plan = new FolderMovePlan { OldDir = oldDir, NewDir = newDir };
        var ctx = new PlanContext { OldDir = oldDir, NewDir = newDir, OldAddonDir = oldDir, NewAddonDir = newDir };
        ctx.RecordPaths.Add(oldDir);
        if (!Directory.Exists(oldDir) || InstallFolderMover.SamePath(oldDir, newDir)) return (plan, ctx);

        bool IsOld(string? p) => !string.IsNullOrEmpty(p) && InstallFolderMover.SamePath(p, oldDir);
        bool IsNew(string? p) => !string.IsNullOrEmpty(p) && InstallFolderMover.SamePath(p, newDir);
        bool Mine(string name) => name.Equals(req.GameName, StringComparison.OrdinalIgnoreCase);

        var aux = _aux.LoadAll().Where(r => Mine(r.GameName)).ToList();
        foreach (var r in aux.Where(r => IsOld(r.InstallPath))) ctx.RecordPaths.Add(r.InstallPath);
        AuxInstalledRecord? AuxAt(bool old, params string[] types) => aux.FirstOrDefault(r =>
            (old ? IsOld(r.InstallPath) : IsNew(r.InstallPath))
            && types.Any(t => r.AddonType.Equals(t, StringComparison.OrdinalIgnoreCase)));

        var rsTypes = new[] { AuxInstallService.TypeReShade, AuxInstallService.TypeReShadeNormal };
        var rsOld = req.SkipReShade ? null : AuxAt(true, rsTypes);
        var rsNew = AuxAt(false, rsTypes);
        bool rsNewWins = rsOld != null && (rsNew != null || IsReShadeAt(Path.Combine(newDir, rsOld.InstalledAs)));
        ResolveAddonDirs(ctx, rsNewWins);

        var oldManifest = RhiInstallManifest.Read(oldDir);
        var newManifest = RhiInstallManifest.Read(newDir);
        var b = new PlanBuilder(plan, ctx);

        // ── ReShade (+ its settings, preset and log) ─────────────────────────
        if (rsOld != null)
        {
            var g = b.Group("ReShade", rsNewWins);
            b.File(g, rsOld.InstalledAs);
            AddReShadeConfig(b, g, oldDir);
        }
        // Vulkan games: no DLL in the folder, just the footprint marker and the settings
        if (VulkanFootprintService.Exists(oldDir))
        {
            var g = b.Group("ReShade (Vulkan)", VulkanFootprintService.Exists(newDir));
            b.File(g, VulkanFootprintService.FootprintFileName);
            AddReShadeConfig(b, g, oldDir);
        }

        // ── Shaders: only RHI's own reshade-shaders folder ───────────────────
        var shaders = Path.Combine(oldDir, InstallFolderMover.ShadersFolder);
        if (File.Exists(Path.Combine(shaders, ShadersMarker)) || File.Exists(Path.Combine(shaders, LumaShadersMarker)))
            b.Group("Shaders").Entries.Add(new FolderMoveEntry
            {
                Source = shaders,
                Dest = Path.Combine(newDir, InstallFolderMover.ShadersFolder),
                IsDirectory = true,
                IsManagedShaders = true,
                ManagedMarkers = new[] { ShadersMarker, LumaShadersMarker },
            });

        // ── RenoDX mod ───────────────────────────────────────────────────────
        if (!req.IsEmulator)
        {
            var mods = _mods.LoadAll().Where(r => Mine(r.GameName)).ToList();
            foreach (var r in mods.Where(r => IsOld(r.InstallPath))) ctx.RecordPaths.Add(r.InstallPath);
            var modOld = mods.FirstOrDefault(r => IsOld(r.InstallPath));
            if (modOld != null && !string.IsNullOrEmpty(modOld.AddonFileName))
            {
                bool newHas = mods.Any(r => IsNew(r.InstallPath))
                           || File.Exists(Path.Combine(ctx.NewAddonDir, modOld.AddonFileName));
                b.Addon(b.Group("RenoDX", newHas), modOld.AddonFileName);
            }
        }

        // ── RenoDX Source engine mod's DXVK proxy (RHI placed it: sentinel beside it) ──
        if (File.Exists(Path.Combine(oldDir, SourceEngineSetup.ProxyDllName + SourceEngineSetup.SentinelSuffix)))
        {
            var g = b.Group("RenoDX Source DXVK",
                File.Exists(Path.Combine(ctx.NewDir, SourceEngineSetup.ProxyDllName + SourceEngineSetup.SentinelSuffix)));
            b.File(g, SourceEngineSetup.ProxyDllName, requireBackup: false);
            b.File(g, SourceEngineSetup.ProxyDllName + SourceEngineSetup.SentinelSuffix, suffix: null, requireBackup: false);
            // ReShade2.ini RHI made for the proxy's runtime (its own sentinel)
            if (File.Exists(Path.Combine(oldDir, SourceEngineSetup.SecondRuntimeIni + SourceEngineSetup.SentinelSuffix)))
            {
                b.File(g, SourceEngineSetup.SecondRuntimeIni, requireBackup: false);
                b.File(g, SourceEngineSetup.SecondRuntimeIni + SourceEngineSetup.SentinelSuffix, suffix: null, requireBackup: false);
            }
        }

        // ── Display Commander ────────────────────────────────────────────────
        var dcOld = AuxAt(true, "DisplayCommander");
        if (dcOld != null)
        {
            bool newHas = AuxAt(false, "DisplayCommander") != null
                       || File.Exists(Path.Combine(ctx.NewAddonDir, dcOld.InstalledAs));
            var g = b.Group("Display Commander", newHas);
            b.Addon(g, dcOld.InstalledAs);
            b.Addon(g, "DisplayCommander.ini");
        }

        // ── ReLimiter (no record: its file names) ────────────────────────────
        var ulNames = new[] { "relimiter.addon64", "relimiter.addon32", "ultra_limiter.addon64", "ultra_limiter.addon32" };
        if (ulNames.Any(n => File.Exists(Path.Combine(ctx.OldAddonDir, n)) || File.Exists(Path.Combine(oldDir, n))))
        {
            var g = b.Group("ReLimiter", ulNames.Any(n => File.Exists(Path.Combine(ctx.NewAddonDir, n))));
            foreach (var n in ulNames) b.Addon(g, n);
            b.Addon(g, "relimiter.ini");
            foreach (var pattern in new[] { "relimiter*.log", "relimiter*.csv" })
                foreach (var f in SafeFiles(oldDir, pattern)) b.File(g, Path.GetFileName(f), suffix: null);
        }

        // ── ReShade addons RHI deployed (addon tracker) ─────────────────────
        var tracked = AddonPackService.GetTrackedAddons(ctx.OldAddonDir);
        if (!InstallFolderMover.SamePath(ctx.OldAddonDir, oldDir))
            tracked = tracked.Concat(AddonPackService.GetTrackedAddons(oldDir)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var name in tracked)
        {
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(new[] { '\\', '/' }) >= 0) continue;
            b.Addon(b.Group(AddonGroupPrefix + name, File.Exists(Path.Combine(ctx.NewAddonDir, name))), name);
        }

        // ── OptiScaler ───────────────────────────────────────────────────────
        var osOld = AuxAt(true, OptiScalerService.AddonType);
        bool osInManifest = string.Equals(oldManifest?.Component, "OptiScaler", StringComparison.OrdinalIgnoreCase);
        bool osNewWins = AuxAt(false, OptiScalerService.AddonType) != null
                      || string.Equals(newManifest?.Component, "OptiScaler", StringComparison.OrdinalIgnoreCase);
        if (osOld != null || osInManifest)
        {
            var g = b.Group("OptiScaler", osNewWins);
            if (osOld != null) b.File(g, osOld.InstalledAs);
            if (osInManifest)
            {
                if (!string.IsNullOrEmpty(oldManifest!.InstalledAs)) b.File(g, oldManifest.InstalledAs);
                foreach (var f in oldManifest.Files) b.File(g, f);
                foreach (var d in oldManifest.Folders.Where(d => !d.Equals("plugins", StringComparison.OrdinalIgnoreCase)))
                    b.Dir(g, d);
            }
            b.File(g, OptiScalerService.IniFileName);
            foreach (var d in new[] { "OptiScaler", "D3D12_OptiScaler" }) b.Dir(g, d);
            // Root plugins\ (OptiScaler 0.9): only ASI plugins and their settings, never the folder
            // itself (a game can have its own plugins folder).
            foreach (var asi in SafeFiles(Path.Combine(oldDir, "plugins"), "*.asi"))
            {
                var stem = Path.GetFileNameWithoutExtension(asi);
                b.File(g, Path.Combine("plugins", Path.GetFileName(asi)));
                b.File(g, Path.Combine("plugins", stem + ".ini"));
            }
        }

        // ── RTX Encore (settings: installed-as name) ─────────────────────────
        if (!string.IsNullOrEmpty(req.RtxEncoreInstalledAs) && req.RtxEncoreInstalledAs.IndexOfAny(new[] { '\\', '/' }) < 0)
        {
            var name = req.RtxEncoreInstalledAs;
            var modRel = name.EndsWith(".asi", StringComparison.OrdinalIgnoreCase)
                         && File.Exists(Path.Combine(oldDir, "plugins", name)) ? "plugins" : "";
            var modFile = Path.Combine(modRel, name);
            if (File.Exists(Path.Combine(oldDir, modFile)))
            {
                bool bink = IsBinkName(name);
                var newMod = Path.Combine(newDir, modFile);
                bool newHas = File.Exists(newMod)
                    && (File.Exists(newMod + ".original") || (bink && File.Exists(InstallFolderMover.HookedPath(newMod))));
                var g = b.Group("RTX Encore", newHas);
                b.File(g, modFile, hooked: bink, requireBackup: false);
                if (req.RtxEncoreNrDllDeployed) b.File(g, Path.Combine(modRel, RtxEncoreService.NrDllName), requireBackup: false);
                b.File(g, Path.Combine(modRel, "rtx-encore.jsonc"));
                b.Dir(g, Path.Combine(modRel, "rtx-encore-runtimes"));
                AddEncoreMarkerRebase(plan, oldDir, newDir, Path.Combine(modRel, Rtx40MfgService.EncoreRuntimeRelDir, EncoreMarker));
            }
        }

        // ── Universal MFG Unlocker (proxy; the OptiScaler plugin moves with OptiScaler\) ──
        if (!string.IsNullOrEmpty(req.Rtx40MfgInstalledAs) && req.Rtx40MfgInstalledAs.IndexOfAny(new[] { '\\', '/' }) < 0)
        {
            var g = b.Group("Universal MFG Unlocker", HasSentinelCopy(newDir, req.Rtx40MfgInstalledAs));
            b.File(g, req.Rtx40MfgInstalledAs, requireBackup: false);
            b.Dir(g, Rtx40MfgService.XessFolderName);
        }

        // ── 20/30 FG Unlock ──────────────────────────────────────────────────
        if (!string.IsNullOrEmpty(req.Dlssg2030InstalledAs) && req.Dlssg2030InstalledAs.IndexOfAny(new[] { '\\', '/' }) < 0)
        {
            var g = b.Group("20/30 FG Unlock", HasSentinelCopy(newDir, req.Dlssg2030InstalledAs));
            b.File(g, req.Dlssg2030InstalledAs, requireBackup: false);
            b.File(g, Dlssg20_30Service.IniFileName);
            b.Dir(g, Dlssg20_30Service.ModFolderName);
        }

        // ── Standalone DLSS Enabler (no backup of its own) ──────────────────
        if (!string.IsNullOrEmpty(req.DeInstalledAs) && req.DeInstalledAs.IndexOfAny(new[] { '\\', '/' }) < 0)
            b.File(b.Group("DLSS Enabler"), req.DeInstalledAs, suffix: null, requireBackup: false);

        // ── NR methods and shared DLLs (rhi_install.txt) ─────────────────────
        if (oldManifest != null)
        {
            foreach (var (component, record) in oldManifest.Components)
            {
                if (component.Equals(RtxEncoreService.ComponentName, StringComparison.OrdinalIgnoreCase)) continue; // above
                var g = b.Group(NrLabel(component), newManifest?.Components.ContainsKey(component) == true);
                bool addonFirst = component.Equals(UpliftService.Component, StringComparison.OrdinalIgnoreCase);
                foreach (var f in record.Files) b.Recorded(g, f, addonFirst);
                if (component.Equals("Feeder", StringComparison.OrdinalIgnoreCase))
                {
                    b.Dir(g, "host64");
                    if (File.Exists(Path.Combine(oldDir, "dgVoodoo.conf")))
                    {
                        b.File(g, "d3d9.dll");
                        b.File(g, "dgVoodoo.conf");
                    }
                }
                if (addonFirst) b.AddonDir(g, UpliftService.DllFolder);
            }
            if (oldManifest.SharedFiles.Count > 0)
            {
                var g = b.Group("Shared DLLs");
                foreach (var key in oldManifest.SharedFiles.Keys) b.Recorded(g, key, addonFirst: false);
            }
            plan.Deletes.Add(Path.Combine(oldDir, RhiInstallManifest.FileName));
            plan.Writes.Add(new FolderMoveWrite(Path.Combine(newDir, RhiInstallManifest.FileName),
                MergeManifest(oldManifest, newManifest, oldDir, newDir, osNewWins)));
        }

        // ── NR Cost Scaler (proxy over nvngx_dlssnr.dll) ────────────────────
        if (File.Exists(Path.Combine(oldDir, DlssNrCostScalerService.RealDllName)))
        {
            var g = b.Group("NR Cost Scaler", File.Exists(Path.Combine(newDir, DlssNrCostScalerService.RealDllName)));
            b.File(g, DlssNrCostScalerService.ProxyDllName, requireBackup: false);
            b.File(g, DlssNrCostScalerService.RealDllName, suffix: null, requireBackup: false);
            b.File(g, DlssNrCostScalerService.IniFileName);
            b.File(g, DlssNrCostScalerService.CompanionAddonName);
        }

        // ── dgVoodoo2 standalone ─────────────────────────────────────────────
        if (req.DgVoodooStandalone && File.Exists(Path.Combine(oldDir, "dgVoodoo.conf")))
        {
            var g = b.Group("dgVoodoo2", File.Exists(Path.Combine(newDir, "dgVoodoo.conf")));
            b.File(g, "d3d9.dll");
            b.File(g, "dgVoodoo.conf");
        }

        // ── DOF Fix (file only) / MFG Ada Unlock settings ────────────────────
        b.File(b.Group("DOF Fix"), "renodx-universal_ue-dof-fix.addon64", suffix: null);
        b.File(b.Group("MFG Unlock"), "MFGAdaUnlock.ini");

        // ── RE Framework ─────────────────────────────────────────────────────
        var refs = _ref.GetRecords().Where(r => Mine(r.GameName)).ToList();
        foreach (var r in refs.Where(r => IsOld(r.InstallPath))) ctx.RecordPaths.Add(r.InstallPath);
        if (refs.Any(r => IsOld(r.InstallPath)))
        {
            var g = b.Group("RE Framework", refs.Any(r => IsNew(r.InstallPath)));
            b.File(g, "dinput8.dll", requireBackup: false);
            b.File(g, "dinput8.dll.rhi_standard_backup", suffix: null);
        }

        // ── DXVK ─────────────────────────────────────────────────────────────
        var dxvks = _dxvk.LoadAllRecords().Where(r => Mine(r.GameName)).ToList();
        foreach (var r in dxvks.Where(r => IsOld(r.InstallPath))) ctx.RecordPaths.Add(r.InstallPath);
        var dxvkOld = dxvks.FirstOrDefault(r => IsOld(r.InstallPath));
        if (dxvkOld != null)
        {
            var g = b.Group("DXVK", dxvks.Any(r => IsNew(r.InstallPath)));
            foreach (var dll in dxvkOld.InstalledDlls) b.File(g, dll, requireBackup: false);
            if (dxvkOld.DeployedConf) b.File(g, "dxvk.conf");
            b.File(g, "d3d9.dll.reshade_backup", suffix: null);
        }

        // ── Luma ─────────────────────────────────────────────────────────────
        var lumas = LumaService.GetAllRecords().Where(r => Mine(r.GameName)).ToList();
        foreach (var r in lumas.Where(r => IsOld(r.InstallPath))) ctx.RecordPaths.Add(r.InstallPath);
        var lumaOld = lumas.FirstOrDefault(r => IsOld(r.InstallPath));
        if (lumaOld != null)
        {
            var g = b.Group("Luma", lumas.Any(r => IsNew(r.InstallPath)));
            foreach (var f in lumaOld.InstalledFiles)
            {
                var rel = f.Replace('/', '\\');
                if (rel.StartsWith(InstallFolderMover.ShadersFolder + "\\", StringComparison.OrdinalIgnoreCase)) continue;
                b.Recorded(g, rel, addonFirst: false, requireBackupForDlss: false);
            }
            b.Dir(g, "Luma");
        }

        // ── Ultimate ASI Loader (last: an RHI extra may sit at its XHooked.dll name) ──
        var ualOld = AuxAt(true, UltimateAsiLoaderService.AddonType);
        if (ualOld != null)
        {
            var extras = new[] { req.RtxEncoreInstalledAs, req.Rtx40MfgInstalledAs, req.Dlssg2030InstalledAs, req.DeInstalledAs };
            bool hookedIsExtra = extras.Any(e => string.Equals(e, HookedDllNames.ToHooked(ualOld.InstalledAs), StringComparison.OrdinalIgnoreCase));
            var g = b.Group("ASI Loader", AuxAt(false, UltimateAsiLoaderService.AddonType) != null);
            b.File(g, ualOld.InstalledAs, hooked: !hookedIsExtra, requireBackup: false);
            b.File(g, Path.GetFileNameWithoutExtension(ualOld.InstalledAs) + ".ini");
        }

        return (plan, ctx);
    }

    // ── Pieces ────────────────────────────────────────────────────────────────

    private static void AddReShadeConfig(PlanBuilder b, FolderMoveGroup g, string oldDir)
    {
        foreach (var f in new[] { "reshade.ini", "ReShade2.ini", "ReShadePreset.ini" }) b.File(g, f);
        // A preset reshade.ini points at, when it's inside the game folder
        var preset = UpliftService.PresetPath(oldDir);
        if (InstallFolderMover.IsInside(preset, oldDir) && !InstallFolderMover.SamePath(preset, oldDir))
            b.File(g, Path.GetRelativePath(oldDir, preset));
        b.File(g, "ReShade.log", suffix: null);
    }

    /// <summary>Reads [ADDON] AddonPath from reshade.ini without creating the folder (unlike ResolveAddonSearchPath).</summary>
    internal static string? ReadAddonPathValue(string dir)
    {
        try
        {
            var ini = Path.Combine(dir, "reshade.ini");
            if (!File.Exists(ini)) return null;
            bool inAddon = false;
            foreach (var raw in File.ReadLines(ini))
            {
                var line = raw.Trim();
                if (line.StartsWith('[')) { inAddon = line.Equals("[ADDON]", StringComparison.OrdinalIgnoreCase); continue; }
                if (inAddon && line.StartsWith("AddonPath", StringComparison.OrdinalIgnoreCase) && line.Contains('='))
                {
                    var value = line[(line.IndexOf('=') + 1)..].Trim();
                    return string.IsNullOrEmpty(value) ? null : value;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[InstallFolderMigrationService.ReadAddonPathValue] '{dir}' — {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// Old and new addon folders. Addons move only when the old AddonPath is unset or relative inside the
    /// game folder; an absolute AddonPath (possibly shared) is left where it is.
    /// </summary>
    private static void ResolveAddonDirs(PlanContext ctx, bool newReShadeWins)
    {
        var oldRaw = ReadAddonPathValue(ctx.OldDir);
        if (oldRaw != null)
        {
            if (Path.IsPathRooted(oldRaw)) { ctx.OldAddonDir = InstallFolderMover.Norm(oldRaw); ctx.AddonsMovable = false; }
            else
            {
                ctx.OldAddonDir = InstallFolderMover.Norm(Path.Combine(ctx.OldDir, oldRaw));
                ctx.AddonsMovable = InstallFolderMover.IsInside(ctx.OldAddonDir, ctx.OldDir);
            }
        }
        // The reshade.ini that ends up in the new folder decides where the addons go
        var newRaw = newReShadeWins ? ReadAddonPathValue(ctx.NewDir) : oldRaw;
        if (newRaw == null) ctx.NewAddonDir = ctx.NewDir;
        else if (Path.IsPathRooted(newRaw)) { ctx.NewAddonDir = InstallFolderMover.Norm(newRaw); ctx.AddonsMovable = false; }
        else ctx.NewAddonDir = InstallFolderMover.Norm(Path.Combine(ctx.NewDir, newRaw));
        if (!ctx.AddonsMovable)
            CrashReporter.Log($"[InstallFolderMigrationService] Addons in '{ctx.OldAddonDir}' stay where they are (AddonPath is an absolute folder)");
    }

    private static bool IsReShadeAt(string path)
    {
        try { return File.Exists(path) && AuxInstallService.IsReShadeFileStrict(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static bool HasSentinelCopy(string dir, string name)
        => File.Exists(Path.Combine(dir, name)) && File.Exists(Path.Combine(dir, name) + ".original");

    private static bool IsBinkName(string name)
        => name.Equals("binkw64.dll", StringComparison.OrdinalIgnoreCase) || name.Equals("bink2w64.dll", StringComparison.OrdinalIgnoreCase);

    /// <summary>DLLs a game may ship itself: moved only when RHI's .original sits beside them.</summary>
    internal static bool IsDlssFamily(string fileName)
        => (fileName.StartsWith("nvngx_", StringComparison.OrdinalIgnoreCase) || fileName.StartsWith("sl.", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("nvngx.dll", StringComparison.OrdinalIgnoreCase))
           && fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

    private static string NrLabel(string component) => component switch
    {
        "Dlss5Tool" => "DLSS5 Tool",
        "ShortFuse" => "DLSS Tool (ShortFuse)",
        "Feeder" => "DLSS5 Feeder",
        "Uplift" => "GITC Uplift",
        _ => component,
    };

    private static IEnumerable<string> SafeFiles(string dir, string pattern)
    {
        try { return Directory.Exists(dir) ? Directory.GetFiles(dir, pattern) : Array.Empty<string>(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[InstallFolderMigrationService.SafeFiles] '{dir}' — {ex.Message}");
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// RTX Encore's runtime marker lists game files it replaced in place as native:&lt;path relative to the
    /// DLSS search root&gt;. Those stay where they are; when the search root differs between the old and
    /// new folder the paths are rewritten so removal still finds them.
    /// </summary>
    private static void AddEncoreMarkerRebase(FolderMovePlan plan, string oldDir, string newDir, string markerRel)
    {
        var marker = Path.Combine(oldDir, markerRel);
        if (!File.Exists(marker)) return;
        var oldRoot = DlssStreamlineService.ResolveSearchRoot(oldDir);
        var newRoot = DlssStreamlineService.ResolveSearchRoot(newDir);
        if (InstallFolderMover.SamePath(oldRoot, newRoot)) return;
        var lines = File.ReadAllLines(marker).Select(raw =>
        {
            var line = raw.Trim();
            if (!line.StartsWith(EncoreNativePrefix, StringComparison.OrdinalIgnoreCase)) return raw;
            var abs = FileHelper.SafeCombine(oldRoot, line[EncoreNativePrefix.Length..]);
            if (abs == null || !InstallFolderMover.IsInside(abs, newRoot)) return raw;
            return EncoreNativePrefix + Path.GetRelativePath(newRoot, abs);
        }).ToList();
        plan.Writes.Add(new FolderMoveWrite(Path.Combine(newDir, markerRel), string.Join(Environment.NewLine, lines) + Environment.NewLine));
    }

    /// <summary>
    /// rhi_install.txt for the new folder: the old one with absolute paths moved across, merged with a
    /// manifest already there (the new folder's entries win; components only the old folder had are added).
    /// </summary>
    internal static string MergeManifest(RhiInstallManifest old, RhiInstallManifest? existing, string oldDir, string newDir, bool optiScalerNewWins)
    {
        string Rebase(string f)
        {
            if (!Path.IsPathRooted(f)) return f;
            return InstallFolderMover.IsInside(f, oldDir)
                ? Path.Combine(newDir, Path.GetRelativePath(oldDir, InstallFolderMover.Norm(f)))
                : f;
        }
        foreach (var rec in old.Components.Values) rec.Files = rec.Files.Select(Rebase).ToList();

        if (existing == null) return JsonSerializer.Serialize(old, ManifestJson);

        var merged = existing;
        foreach (var (name, rec) in old.Components)
            if (!merged.Components.ContainsKey(name)) merged.Components[name] = rec;
        foreach (var (file, owners) in old.SharedFiles)
        {
            if (!merged.SharedFiles.TryGetValue(file, out var list)) merged.SharedFiles[file] = list = new List<string>();
            foreach (var o in owners)
            {
                if (o.Equals("OptiScaler", StringComparison.OrdinalIgnoreCase) && optiScalerNewWins) continue;
                if (!list.Contains(o, StringComparer.OrdinalIgnoreCase)) list.Add(o);
            }
        }
        bool oldIsOs = old.Component.Equals("OptiScaler", StringComparison.OrdinalIgnoreCase);
        if (oldIsOs && !optiScalerNewWins)
        {
            merged.Component = old.Component;
            merged.Variant = old.Variant;
            merged.Version = old.Version;
            merged.InstalledAs = old.InstalledAs;
            merged.InstalledAt = old.InstalledAt;
            merged.Files = merged.Files.Union(old.Files, StringComparer.OrdinalIgnoreCase).ToList();
            merged.Folders = merged.Folders.Union(old.Folders, StringComparer.OrdinalIgnoreCase).ToList();
        }
        merged.NrMethod ??= old.NrMethod;
        if (merged.Dlss5Build == null && old.Dlss5Build != null)
        {
            merged.Dlss5Build        = old.Dlss5Build;
            merged.Dlss5BuildVersion = old.Dlss5BuildVersion;
            merged.Dlss5BuildPinned  = old.Dlss5BuildPinned;
        }
        return JsonSerializer.Serialize(merged, ManifestJson);
    }

    // ── Plan builder ──────────────────────────────────────────────────────────

    private sealed class PlanBuilder
    {
        private readonly FolderMovePlan _plan;
        private readonly PlanContext _ctx;

        public PlanBuilder(FolderMovePlan plan, PlanContext ctx) { _plan = plan; _ctx = ctx; }

        public FolderMoveGroup Group(string name, bool newDirWins = false)
        {
            var g = new FolderMoveGroup { Name = name, NewDirWins = newDirWins };
            _plan.Groups.Add(g);
            return g;
        }

        /// <summary>A file at <paramref name="rel"/> in the game folder (skipped if it isn't there).</summary>
        public void File(FolderMoveGroup g, string rel, string? suffix = ".original", bool hooked = false, bool? requireBackup = null)
        {
            var src = FileHelper.SafeCombine(_ctx.OldDir, rel);
            var dst = FileHelper.SafeCombine(_ctx.NewDir, rel);
            if (src == null || dst == null || !System.IO.File.Exists(src)) return;
            bool needsProof = requireBackup ?? IsDlssFamily(Path.GetFileName(rel));
            // Without RHI's backup beside it, it's the game's own file: not part of the plan
            if (needsProof && (suffix == null || !System.IO.File.Exists(src + suffix))) return;
            g.Entries.Add(new FolderMoveEntry
            {
                Source = src,
                Dest = dst,
                BackupSuffix = suffix,
                HookedBackup = hooked,
                RequireBackup = needsProof,
            });
        }

        public void Dir(FolderMoveGroup g, string rel)
        {
            var src = FileHelper.SafeCombine(_ctx.OldDir, rel);
            var dst = FileHelper.SafeCombine(_ctx.NewDir, rel);
            if (src == null || dst == null || !Directory.Exists(src)) return;
            if (InstallFolderMover.IsInside(_ctx.NewDir, src)) return; // never move a folder that holds the new folder
            g.Entries.Add(new FolderMoveEntry { Source = src, Dest = dst, IsDirectory = true });
        }

        /// <summary>A ReShade addon file: in the addon folder, or the game folder root (older installs).</summary>
        public void Addon(FolderMoveGroup g, string name)
        {
            if (!_ctx.AddonsMovable) return;
            // Addons have no backup; a DLL name (Display Commander via a DLL override) may have a .original
            string? suffix = name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? ".original" : null;
            var src = FileHelper.SafeCombine(_ctx.OldAddonDir, name);
            var dst = FileHelper.SafeCombine(_ctx.NewAddonDir, name);
            if (src != null && dst != null && System.IO.File.Exists(src))
            {
                g.Entries.Add(new FolderMoveEntry { Source = src, Dest = dst, BackupSuffix = suffix });
                return;
            }
            if (!InstallFolderMover.SamePath(_ctx.OldAddonDir, _ctx.OldDir)) File(g, name, suffix: suffix, requireBackup: false);
        }

        public void AddonDir(FolderMoveGroup g, string rel)
        {
            if (!_ctx.AddonsMovable) return;
            var src = FileHelper.SafeCombine(_ctx.OldAddonDir, rel);
            var dst = FileHelper.SafeCombine(_ctx.NewAddonDir, rel);
            if (src == null || dst == null || !Directory.Exists(src)) return;
            g.Entries.Add(new FolderMoveEntry { Source = src, Dest = dst, IsDirectory = true });
        }

        /// <summary>
        /// A path from a tracking file: absolute (must be inside the old folder), or relative to the game
        /// folder / addon folder (<paramref name="addonFirst"/> picks which is tried first).
        /// </summary>
        public void Recorded(FolderMoveGroup g, string path, bool addonFirst, bool requireBackupForDlss = true)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            var p = path.Replace('/', '\\');
            if (Path.IsPathRooted(p))
            {
                if (!InstallFolderMover.IsInside(p, _ctx.OldDir)) return;
                p = Path.GetRelativePath(_ctx.OldDir, InstallFolderMover.Norm(p));
            }
            if (p.StartsWith(InstallFolderMover.ShadersFolder + "\\", StringComparison.OrdinalIgnoreCase)) return; // with the shaders
            bool dlss = requireBackupForDlss && IsDlssFamily(Path.GetFileName(p));
            var inAddon = _ctx.AddonsMovable ? FileHelper.SafeCombine(_ctx.OldAddonDir, p) : null;
            bool addonExists = inAddon != null && !InstallFolderMover.SamePath(_ctx.OldAddonDir, _ctx.OldDir) && System.IO.File.Exists(inAddon);
            var inRoot = FileHelper.SafeCombine(_ctx.OldDir, p);
            bool rootExists = inRoot != null && System.IO.File.Exists(inRoot);
            if (addonExists && (addonFirst || !rootExists))
            {
                var dst = FileHelper.SafeCombine(_ctx.NewAddonDir, p);
                if (dst != null && (!dlss || System.IO.File.Exists(inAddon + ".original")))
                    g.Entries.Add(new FolderMoveEntry { Source = inAddon!, Dest = dst, RequireBackup = dlss });
                return;
            }
            if (rootExists) File(g, p, requireBackup: dlss);
        }
    }
}
