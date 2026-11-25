// DetailPanelBuilder.Extras.FrameGen.Umfg.cs — Universal MFG Unlocker ⚙ sections: RTX Hair (dxcompiler.dll / dxil.dll
// beside the exe, RTXMFG.RtxHair.ini) and DLSS 5 (nvngx_dlssnr.dll beside the exe). Wording follows the mod's
// docs/compatibility.md ("Path traced hair") and docs/menu.md.

using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    private void AddUmfgRtxHairSection(CogDialog cog, GameCardViewModel card, Rtx40MfgService mfgSvc, bool isInstalled, bool isHsfMods)
    {
        var installPath = card.InstallPath ?? "";
        cog.AddSection("RTX Hair");
        cog.AddIntro("In DirectX 12 and Vulkan games with NVIDIA RTX Hair, which needs an RTX 50, the mod lets the game offer RTX Hair on RTX 40 and RTX 30 too (experimental). "
            + "It's on by default. In DirectX 12 games it needs Microsoft's dxcompiler.dll and dxil.dll beside the game: RHI installs them with the mod. Vulkan games need no extra files.");

        if (!isInstalled || string.IsNullOrEmpty(installPath) || isHsfMods)
        {
            if (isHsfMods) cog.AddNote("Not used here: HSF's helper loads the mod in its own process.");
            return;
        }

        // Status of the two files
        var family = GpuInfoService.Cached is { } gpus ? GpuInfoService.Primary(gpus)?.Family : null;
        bool dx12 = _window.ViewModel.UmfgRunsDx12(card);
        var owned = Rtx40MfgService.DxcFilesOwnedIn(installPath);
        var present = Rtx40MfgService.DxcFiles.Where(f => File.Exists(Path.Combine(installPath, f))).ToList();
        if (family is GpuFamily.Rtx50 or GpuFamily.Newer)
            cog.AddNote($"Your {GpuInfoService.FamilyLabel(family.Value)} card has RTX Hair itself, so RHI doesn't install the extra files.");
        else if (!dx12)
            cog.AddNote(card.RequiresVulkanInstall || card.IsVulkanOnly
                ? "This game runs Vulkan: no extra files needed."
                : "RHI doesn't see DirectX 12 in this game, so the extra files aren't installed.");
        else if (present.Count == Rtx40MfgService.DxcFiles.Length)
            cog.AddNote(owned.Count == present.Count
                ? $"dxcompiler.dll and dxil.dll: installed by RHI{(mfgSvc.DxcStagedVersion is { } v ? $" ({v})" : "")}."
                : owned.Count == 0 ? "dxcompiler.dll and dxil.dll: the game's own copies are there (left as they are)."
                : "dxcompiler.dll and dxil.dll: there (one is the game's own, left as it is).");
        else if (family is GpuFamily.Rtx20 or GpuFamily.NotRtx)
            cog.AddNote("RTX Hair is for RTX 40 and RTX 30 cards, so RHI doesn't install the extra files on this PC.");
        else
            cog.AddWarning($"Missing beside the game: {string.Join(", ", Rtx40MfgService.DxcFiles.Except(present, StringComparer.OrdinalIgnoreCase))}. "
                + "Reinstall the mod to add them (RHI downloads them from Microsoft's DirectX Shader Compiler release).");

        bool on = Rtx40MfgService.IsRtxHairOn(installPath);
        cog.AddCombo("RTX Hair",
            $"Off writes Universal=0 under [RtxHair] in {Rtx40MfgService.RtxHairIniName} beside the game's exe. Restart the game after changing it.",
            new[] { "On", "Off" }, on ? 0 : 1,
            idx =>
            {
                bool ok = Rtx40MfgService.SetRtxHairOn(installPath, idx == 0);
                if (ok) cog.SetSuccess(idx == 0 ? "RTX Hair on (the mod's default)." : "RTX Hair off for this game.");
                else cog.SetError($"Couldn't write {Rtx40MfgService.RtxHairIniName}. Check that the game folder is writable.");
                return ok;
            });
    }

    private void AddUmfgDlss5Section(CogDialog cog, GameCardViewModel card, Rtx40MfgService mfgSvc, bool isInstalled, bool isHsfMods)
    {
        var installPath = card.InstallPath ?? "";
        cog.AddSection("DLSS 5");
        cog.AddIntro("The mod's DLSS 5 tab appears when NVIDIA's nvngx_dlssnr.dll (a version that supports your card) is beside the game's exe, "
            + "in games running DLSS Super Resolution, DLAA or Ray Reconstruction. RHI can install just that DLL here, or a full method from the Neural Rendering section.");
        if (string.IsNullOrEmpty(installPath)) return;

        var dest = Path.Combine(installPath, Rtx40MfgService.NrDllName);
        bool present = File.Exists(dest);
        bool gamesOwn = present && !File.Exists(dest + ".original");
        bool owned = Rtx40MfgService.OwnsNrDll(installPath);
        var others = present
            ? Models.RhiInstallManifest.GetSharedFileOwners(installPath, Rtx40MfgService.NrDllName)
                .Where(o => !o.Equals(Rtx40MfgService.NrOwnerName, StringComparison.OrdinalIgnoreCase)).ToList()
            : new System.Collections.Generic.List<string>();
        string? ver = null;
        if (present)
        {
            try
            {
                var vi = System.Diagnostics.FileVersionInfo.GetVersionInfo(DlssNrCostScalerService.ResolveRealNrDllPath(dest));
                ver = $"{vi.FileMajorPart}.{vi.FileMinorPart}.{vi.FileBuildPart}";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { CrashReporter.Log($"[UmfgCog] Couldn't read the NR DLL version — {ex.Message}"); }
        }
        string verText = ver != null ? $" ({ver})" : "";
        cog.AddNote(!present ? "nvngx_dlssnr.dll: not beside the game's exe."
            : gamesOwn ? $"nvngx_dlssnr.dll{verText}: the game's own (left as it is)."
            : others.Count > 0 ? $"nvngx_dlssnr.dll{verText}: installed by RHI ({string.Join(", ", others)})."
            : $"nvngx_dlssnr.dll{verText}: installed by RHI for the mod.");

        if (!isInstalled || isHsfMods || gamesOwn) return;
        var family = GpuInfoService.Cached is { } gpus ? GpuInfoService.Primary(gpus)?.Family : null;
        if (family is GpuFamily.NotRtx)
        {
            cog.AddNote("DLSS 5 needs an NVIDIA RTX card, so RHI doesn't offer the DLL on this PC.");
            return;
        }
        var version = Rtx40MfgService.NrVersionFor(family);

        bool on = owned;
        bool suppress = false;
        Microsoft.UI.Xaml.Controls.ComboBox? combo = null;
        void Revert()
        {
            if (combo == null) return;
            suppress = true;
            combo.SelectedIndex = on ? 1 : 0;
            suppress = false;
        }

        async Task ApplyAsync(bool enable)
        {
            if (enable == on) return;
            if (!enable)
            {
                await Task.Run(() => mfgSvc.RemoveNrDll(installPath));
                on = false;
                cog.SetSuccess(others.Count > 0
                    ? "The mod no longer uses its own copy; nvngx_dlssnr.dll stays for the other components that use it."
                    : "Removed nvngx_dlssnr.dll.");
                RequestExtrasRebuild(card);
                await RefreshDlssAfterNrDllChangeAsync(card); // #174: NR column / NR section
                return;
            }
            if (combo != null) combo.IsEnabled = false;
            try
            {
                var dlssS = App.Services.GetRequiredService<IDlssStreamlineService>();
                var cached = ResolveCachedNrDllPath(version);
                if (cached == null)
                {
                    cog.SetStatus($"Downloading nvngx_dlssnr.dll {version}...");
                    cached = await dlssS.EnsureSpecificDlssnrCachedAsync(version);
                }
                if (cached == null)
                {
                    Revert();
                    cog.SetError($"Couldn't download nvngx_dlssnr.dll {version}. Nothing was installed.");
                    return;
                }
                var result = await Task.Run(() => mfgSvc.DeployNrDll(installPath, cached));
                switch (result)
                {
                    case Rtx40MfgService.NrDeployResult.Failed:
                        Revert();
                        cog.SetError("Couldn't copy nvngx_dlssnr.dll beside the game's exe. Check that the game folder is writable.");
                        return;
                    case Rtx40MfgService.NrDeployResult.GamesOwn:
                        Revert();
                        cog.SetError("The game has its own nvngx_dlssnr.dll, so RHI left it as it is.");
                        return;
                    case Rtx40MfgService.NrDeployResult.SharedWithOther:
                        cog.SetSuccess("RHI's nvngx_dlssnr.dll from the Neural Rendering section is already here; the mod uses that one. It stays while either needs it.");
                        break;
                    default:
                        cog.SetSuccess($"Installed nvngx_dlssnr.dll {version} beside the game's exe. Restart the game to see the mod's DLSS 5 tab.");
                        break;
                }
                // A game with no DLSS of its own may be in the scan skip cache: un-skip it so the NR DLL shows
                dlssS.RecordDlssFound(card.GameName);
                on = true;
                RequestExtrasRebuild(card);
                await RefreshDlssAfterNrDllChangeAsync(card); // #174: NR column / NR section
            }
            finally
            {
                if (combo != null) combo.IsEnabled = true;
            }
        }

        combo = cog.AddCombo("Install NR DLL",
            $"Copies only NVIDIA's Neural Rendering DLL (nvngx_dlssnr.dll {version}, picked for your "
            + $"{(family is { } f ? GpuInfoService.FamilyLabel(f) : "card")}) beside the game's exe for the mod's DLSS 5 tab: no ReShade, add-on or host. "
            + "No removes it again (kept while a Neural Rendering method still uses it). Removing the mod removes it too.",
            new[] { "No", "Yes" }, on ? 1 : 0, i =>
            {
                if (suppress) return true;
                _ = ApplySafeAsync(i == 1);
                return true;
            });

        async Task ApplySafeAsync(bool enable)
        {
            try { await ApplyAsync(enable); }
            catch (Exception ex)
            {
                CrashReporter.Log($"[UmfgCog] NR DLL change failed — {ex.Message}");
                Revert();
                cog.SetError($"Couldn't change the NR DLL: {ex.Message}");
            }
        }
    }
}
