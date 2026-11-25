// DetailPanelBuilder.Extras.MfgUnlockDlss5.cs — MFG Unlock ⚙ › DLSS 5 (MFG Unlock 1.12.0+): NVIDIA's DLSS 5 neural
// rendering (NR) on the game's frames in DirectX 12 and Vulkan games, off by default. The mod's settings are under
// [DLSS5] in MFGAdaUnlock.ini beside the game's exe (docs/settings.md): Enabled=0/1 is the switch, the rest (look,
// resolution, passes, hotkeys) is in the mod's DLSS 5 tab. The mod doesn't ship or download nvngx_dlssnr.dll: it
// must be beside the exe, so turning DLSS 5 on here also installs RHI's copy (owner "MfgUnlock" in rhi_install.txt,
// Rtx40MfgService.Dlss5.cs) unless one is already there; turning it off or removing the mod drops that claim.
// With another DLSS 5 add-on loaded the mod's NR stands aside ([DLSS5] Coexist=0, the default).

using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    private void AddMfgUnlockDlss5Section(CogDialog cog, GameCardViewModel card, string iniPath, HashSet<GraphicsApiType> apis)
    {
        var installPath = card.InstallPath ?? "";
        const string Owner = Rtx40MfgService.MfgUnlockNrOwnerName;
        cog.AddSection("DLSS 5");
        cog.AddIntro("MFG Unlock can run NVIDIA's DLSS 5 neural rendering on the game's frames in DirectX 12 and Vulkan games, "
            + "together with frame generation and HDR. It works best in games with DLSS Super Resolution or Ray Reconstruction. "
            + "It needs NVIDIA's nvngx_dlssnr.dll beside the game's exe: RHI installs it when you turn DLSS 5 on.");
        if (string.IsNullOrEmpty(installPath)) return;

        // DirectX 11 and older aren't supported (an unknown API is let through)
        bool apiOk = apis.Contains(GraphicsApiType.DirectX12) || apis.Contains(GraphicsApiType.Vulkan)
                     || apis.All(a => a == GraphicsApiType.Unknown);
        var family = GpuInfoService.Cached is { } gpus ? GpuInfoService.Primary(gpus)?.Family : null;
        bool isInstalled = card.MfgAdaInstalled;

        // The NR DLL beside the exe
        var mfgSvc = App.Services.GetRequiredService<Rtx40MfgService>();
        var dest = Path.Combine(installPath, Rtx40MfgService.NrDllName);
        bool present = File.Exists(dest);
        bool gamesOwn = present && !File.Exists(dest + ".original");
        var owners = present
            ? Models.RhiInstallManifest.GetSharedFileOwners(installPath, Rtx40MfgService.NrDllName).ToList()
            : new List<string>();
        string? ver = null;
        if (present)
        {
            try
            {
                var vi = System.Diagnostics.FileVersionInfo.GetVersionInfo(DlssNrCostScalerService.ResolveRealNrDllPath(dest));
                ver = $"{vi.FileMajorPart}.{vi.FileMinorPart}.{vi.FileBuildPart}";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { CrashReporter.Log($"[MfgAdaCog] Couldn't read the NR DLL version — {ex.Message}"); }
        }
        string verText = ver != null ? $" ({ver})" : "";

        if (!apiOk)
        {
            cog.AddNote($"This game uses {ApiDisplayName(card.GraphicsApi, apis)}: MFG Unlock's DLSS 5 works in DirectX 12 and Vulkan games only.");
            return;
        }
        if (family is GpuFamily.NotRtx)
        {
            cog.AddNote("DLSS 5 needs an NVIDIA RTX card, so it isn't offered on this PC.");
            return;
        }
        if (!isInstalled)
        {
            cog.AddNote("Install MFG Unlock first to use DLSS 5.");
            return;
        }

        cog.AddNote(!present ? "nvngx_dlssnr.dll: not beside the game's exe yet."
            : gamesOwn ? $"nvngx_dlssnr.dll{verText}: the game's own (left as it is)."
            : owners.Count > 0 ? $"nvngx_dlssnr.dll{verText}: installed by RHI ({string.Join(", ", owners)})."
            : $"nvngx_dlssnr.dll{verText}: installed by RHI.");

        // Another DLSS 5 add-on in the game: MFG Unlock's NR stands aside while it's loaded
        var rdx5Svc = App.Services.GetRequiredService<Renodx5AddonService>();
        var otherAddons = new List<string>();
        try
        {
            if (rdx5Svc.IsInstalledIn(installPath)) otherAddons.Add("DLSS5 Tool");
            if (rdx5Svc.IsSfInstalledIn(installPath)) otherAddons.Add("ShortFuse's DLSS 5");
            if (File.Exists(Path.Combine(installPath, FeederDeployFile64))) otherAddons.Add("DLSS5 Feeder");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { CrashReporter.Log($"[MfgAdaCog] DLSS 5 add-on check failed — {ex.Message}"); }

        bool enabled = Dlssg20_30Service.ReadIniValue(iniPath, "DLSS5", "Enabled") == "1";
        var version = Rtx40MfgService.NrVersionFor(family);
        // On in the ini but the DLL is gone (deleted by antivirus or another tool): show Off, so On installs it again
        bool on = enabled && present;
        if (enabled && !present)
            cog.AddWarning("DLSS 5 is on in MFGAdaUnlock.ini, but nvngx_dlssnr.dll is missing, so it can't run. Choose On to install it again.");
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
                Dlssg20_30Service.WriteIniValue(iniPath, "DLSS5", "Enabled", "0");
                await Task.Run(() => mfgSvc.RemoveNrDll(installPath, Owner));
                on = false;
                CrashReporter.Log($"[MfgAdaCog] '{card.GameName}': DLSS 5 = Off");
                cog.SetSuccess("DLSS 5 off. Restart the game if it's running.");
                RequestExtrasRebuild(card);
                await RefreshDlssAfterNrDllChangeAsync(card);
                return;
            }
            if (combo != null) combo.IsEnabled = false;
            try
            {
                if (!gamesOwn)
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
                        cog.SetError($"Couldn't download nvngx_dlssnr.dll {version}, so DLSS 5 stays off.");
                        return;
                    }
                    var result = await Task.Run(() => mfgSvc.DeployNrDll(installPath, cached, Owner));
                    if (result == Rtx40MfgService.NrDeployResult.Failed)
                    {
                        Revert();
                        cog.SetError("Couldn't copy nvngx_dlssnr.dll beside the game's exe, so DLSS 5 stays off. Check that the game folder is writable.");
                        return;
                    }
                    // A game with no DLSS of its own may be in the scan skip cache: un-skip it so the NR DLL shows
                    dlssS.RecordDlssFound(card.GameName);
                }
                Dlssg20_30Service.WriteIniValue(iniPath, "DLSS5", "Enabled", "1");
                on = true;
                CrashReporter.Log($"[MfgAdaCog] '{card.GameName}': DLSS 5 = On");
                cog.SetSuccess("DLSS 5 on. Restart the game once (it has to be on when the game starts); then Ctrl+F8 turns it on and off.");
                RequestExtrasRebuild(card);
                await RefreshDlssAfterNrDllChangeAsync(card);
            }
            finally
            {
                if (combo != null) combo.IsEnabled = true;
            }
        }

        async Task ApplySafeAsync(bool enable)
        {
            try { await ApplyAsync(enable); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                       or System.Net.Http.HttpRequestException or TaskCanceledException)
            {
                CrashReporter.Log($"[MfgAdaCog] DLSS 5 change failed — {ex.Message}");
                Revert();
                cog.SetError($"Couldn't change DLSS 5: {ex.Message}");
            }
        }

        combo = cog.AddCombo("DLSS 5 neural rendering",
            "On writes Enabled=1 under [DLSS5] in MFGAdaUnlock.ini and installs nvngx_dlssnr.dll "
            + $"{version} beside the game's exe if it isn't there (kept while another component still uses it). "
            + "Restart the game once after turning it on. In the game, Ctrl+F8 turns it on and off and Ctrl+F7 compares (left half without it). "
            + "Look, resolution, passes and the rest are in the DLSS 5 tab of the mod's panel in the ReShade overlay.",
            new[] { "Off", "On" }, on ? 1 : 0, i =>
            {
                if (suppress) return true;
                _ = ApplySafeAsync(i == 1);
                return true;
            });

        if (otherAddons.Count > 0)
            cog.AddWarning($"{string.Join(" and ", otherAddons)} is also installed in this game. While another DLSS 5 add-on is loaded, "
                + "MFG Unlock's DLSS 5 stands aside (Coexist in the DLSS 5 tab changes that). Use one or the other.");
        if (apis.Contains(GraphicsApiType.Vulkan) && !apis.Contains(GraphicsApiType.DirectX12))
            cog.AddNote("In Vulkan games, restart the game after turning DLSS 5 on: MFG Unlock adds what it needs to the game's device when the game starts.");
    }
}
