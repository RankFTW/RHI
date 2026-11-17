// Rtx40MfgService.Dlss5.cs — Universal MFG Unlocker: the optional NVIDIA NR DLL (nvngx_dlssnr.dll) for the mod's
// DLSS 5 tab (docs/menu.md, "DLSS 5": the tab appears when a DLL that supports the card is beside the game's exe).
// The mod doesn't ship or download it. RHI copies only that DLL beside the exe, with no add-on or host, and records
// "Umfg" as an owner in rhi_install.txt's shared files so other NR components (DLSS5 Tool, ShortFuse, Feeder,
// OptiScaler, RTX Encore) keep it while they still use it. A game's own DLL (no .original beside it) is never
// replaced, and a copy another RHI component placed keeps that component's version.

using System;
using System.IO;
using System.Linq;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public partial class Rtx40MfgService
{
    /// <summary>Owner name for the NR DLL in rhi_install.txt's shared files.</summary>
    public const string NrOwnerName = "Umfg";
    public const string NrDllName   = "nvngx_dlssnr.dll";

    /// <summary>dlss_manifest.json "dlssnr" version NVIDIA made for RTX 50 and newer (signed, unmodified).</summary>
    public const string NrVersionNvidia   = "310.9.16";
    /// <summary>dlss_manifest.json "dlssnr" version modified to run on RTX 20, 30 and 40 too.</summary>
    public const string NrVersionAllCards = "310.9.16 (20/30/40/50)";

    /// <summary>NR DLL version to install for the mod on this GPU: NVIDIA's own on RTX 50 and newer, otherwise the build for older cards.</summary>
    public static string NrVersionFor(GpuFamily? family)
        => family is GpuFamily.Rtx50 or GpuFamily.Newer ? NrVersionNvidia : NrVersionAllCards;

    public enum NrDeployResult { Deployed, Replaced, AlreadySelected, SharedWithOther, GamesOwn, Failed }

    /// <summary>True when the mod is recorded as an owner of the NR DLL beside the exe.</summary>
    public static bool OwnsNrDll(string installPath)
        => !string.IsNullOrEmpty(installPath)
           && RhiInstallManifest.GetSharedFileOwners(installPath, NrDllName)
                  .Contains(NrOwnerName, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Puts <paramref name="cachedNrPath"/> beside the exe as nvngx_dlssnr.dll for the mod's DLSS 5 tab.
    /// Missing → copied (0-byte .original = RHI's). The game's own (no .original) → left alone. RHI's copy owned
    /// only by the mod (or nobody) and different → replaced, behind the NR Cost Scaler's proxy when installed.
    /// RHI's copy owned by another component → kept as is (its version), the mod is added as an owner.
    /// </summary>
    public NrDeployResult DeployNrDll(string installPath, string cachedNrPath)
    {
        var dest     = Path.Combine(installPath, NrDllName);
        var sentinel = dest + ".original";
        try
        {
            if (!File.Exists(dest))
            {
                File.Copy(cachedNrPath, dest, overwrite: false);
                if (!File.Exists(sentinel)) File.WriteAllBytes(sentinel, Array.Empty<byte>());
                RhiInstallManifest.AddSharedFileOwner(installPath, NrDllName, NrOwnerName);
                _crashReporter.Log($"[Rtx40MfgService.DeployNrDll] Copied {NrDllName} to '{installPath}'");
                return NrDeployResult.Deployed;
            }
            if (!File.Exists(sentinel))
            {
                _crashReporter.Log($"[Rtx40MfgService.DeployNrDll] {NrDllName} in '{installPath}' is the game's own — left as is");
                return NrDeployResult.GamesOwn;
            }
            var others = RhiInstallManifest.GetSharedFileOwners(installPath, NrDllName)
                .Where(o => !o.Equals(NrOwnerName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (others.Count > 0)
            {
                RhiInstallManifest.AddSharedFileOwner(installPath, NrDllName, NrOwnerName);
                _crashReporter.Log($"[Rtx40MfgService.DeployNrDll] {NrDllName} in '{installPath}' belongs to {string.Join(", ", others)} — kept, mod added as an owner");
                return NrDeployResult.SharedWithOther;
            }
            var real = DlssNrCostScalerService.ResolveRealNrDllPath(dest);
            if (VulkanLayerService.FilesIdentical(cachedNrPath, real))
            {
                RhiInstallManifest.AddSharedFileOwner(installPath, NrDllName, NrOwnerName);
                return NrDeployResult.AlreadySelected;
            }
            if (!DlssNrCostScalerService.ReplaceRealNrDll(dest, cachedNrPath))
                File.Copy(cachedNrPath, dest, overwrite: true);
            RhiInstallManifest.AddSharedFileOwner(installPath, NrDllName, NrOwnerName);
            _crashReporter.Log($"[Rtx40MfgService.DeployNrDll] Replaced RHI's {NrDllName} in '{installPath}'");
            return NrDeployResult.Replaced;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"[Rtx40MfgService.DeployNrDll] Failed for '{installPath}' — {ex.Message}");
            return NrDeployResult.Failed;
        }
    }

    /// <summary>
    /// Drops the mod's claim on the NR DLL and removes it (sentinel restore) when nothing else owns it.
    /// No-op when the mod never installed it. Kept while the NR Cost Scaler is installed (its uninstall puts the real DLL back first).
    /// </summary>
    public void RemoveNrDll(string installPath)
    {
        if (!OwnsNrDll(installPath)) return;
        if (!RhiInstallManifest.RemoveSharedFileOwner(installPath, NrDllName, NrOwnerName))
        {
            _crashReporter.Log($"[Rtx40MfgService.RemoveNrDll] {NrDllName} in '{installPath}' still used by another component — kept");
            return;
        }
        var dest = Path.Combine(installPath, NrDllName);
        if (!string.Equals(DlssNrCostScalerService.ResolveRealNrDllPath(dest), dest, StringComparison.OrdinalIgnoreCase))
        {
            _crashReporter.Log($"[Rtx40MfgService.RemoveNrDll] Kept {NrDllName} in '{installPath}' — the NR Cost Scaler is installed there");
            return;
        }
        try
        {
            AuxInstallService.SentinelRestore(dest);
            _crashReporter.Log($"[Rtx40MfgService.RemoveNrDll] Removed/restored {NrDllName} in '{installPath}'");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"[Rtx40MfgService.RemoveNrDll] Failed — {ex.Message}");
        }
    }
}
