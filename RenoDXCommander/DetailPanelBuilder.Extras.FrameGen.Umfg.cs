// DetailPanelBuilder.Extras.FrameGen.Umfg.cs — Universal MFG Unlocker ⚙ sections: RTX Hair (dxcompiler.dll / dxil.dll
// beside the exe, RTXMFG.RtxHair.ini) and DLSS 5 (nvngx_dlssnr.dll beside the exe). Wording follows the mod's
// docs/compatibility.md ("Path traced hair") and docs/menu.md.

using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;
using System;
using System.IO;
using System.Linq;

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

    private static void AddUmfgDlss5Section(CogDialog cog, string installPath)
    {
        cog.AddSection("DLSS 5");
        cog.AddIntro("The mod's DLSS 5 tab appears when NVIDIA's nvngx_dlssnr.dll (a version that supports your card) is beside the game's exe, "
            + "in games running DLSS Super Resolution, DLAA or Ray Reconstruction. RHI's Neural Rendering section can install it.");
        if (string.IsNullOrEmpty(installPath)) return;
        cog.AddNote(File.Exists(Path.Combine(installPath, "nvngx_dlssnr.dll"))
            ? "nvngx_dlssnr.dll: beside the game's exe."
            : "nvngx_dlssnr.dll: not beside the game's exe.");
    }
}
