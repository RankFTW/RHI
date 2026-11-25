// DetailPanelBuilder.Extras.HsfEngine.cs: HSF ⚙ › Frame generation engine (DLSS-G / XeMFG), HSF.ini [HSF] FgEngine.
// Shown when the game's HSF build knows XeMFG and Intel's XeSS frame generation files are there (HsfService.FgEngine).
// XeMFG without Universal MFG Unlocker (or MFG Ada Unlock) is allowed but limited to 2x, on every RTX card.

using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public partial class DetailPanelBuilder
{
    private void AddHsfFgEngineSection(CogDialog cog, GameCardViewModel card, HsfService svc)
    {
        var installPath = card.InstallPath ?? "";
        if (!HsfService.SupportsXeMfg(installPath, card.Is32Bit) || !HsfService.HasXeFgFiles(installPath)) return;

        cog.AddSection("Frame generation engine");
        cog.AddIntro("XeMFG runs Intel's XeSS frame generation inside DLSS-G in place of NVIDIA's own (experimental). "
            + "The change applies the next time the game starts. HSF's own menu has the same choice (FG ENGINE).");
        var unlockerNote = cog.AddNote("");
        // XeMFG needs Universal MFG Unlocker for 3x-6x on every RTX card: offer it right here (HSF 0.10 expects it)
        Microsoft.UI.Xaml.Controls.Button? installMfgBtn = null;
        Microsoft.UI.Xaml.FrameworkElement? mfgButtons = null;
        installMfgBtn = cog.MakeButton("Install Universal MFG Unlocker", CogButtonKind.Primary, async () =>
        {
            installMfgBtn!.IsEnabled = false;
            cog.SetStatus("Installing Universal MFG Unlocker...");
            var error = await _window.ViewModel.InstallMfgForHsfAutoAsync(card);
            installMfgBtn.IsEnabled = true;
            if (error != null) { cog.SetError(error); return; }
            cog.SetSuccess("Universal MFG Unlocker installed. XeMFG can now run 2x to 6x; HSF follows the multiplier the unlocker sets. Restart the game.");
            ShowUnlocker(true);
            RequestExtrasRebuild(card);
        }, "Installs Universal MFG Unlocker by dashdogy for this game: as version.dll next to the exe in 64-bit games "
           + "(HSF 0.10: in DirectX 12 games it only loads as version.dll), in HSF_streamline\\mods in 32-bit games.");
        mfgButtons = cog.AddButtons(installMfgBtn);
        void ShowUnlocker(bool xemfg)
        {
            bool missing = xemfg && !card.Rtx40MfgInstalled && !card.MfgAdaInstalled;
            unlockerNote.Text = !xemfg
                ? "DLSS-G: NVIDIA's frame generation, as before."
                : missing
                    ? "Limited to 2x: Universal MFG Unlocker isn't installed. XeMFG needs it for 3x to 6x on every RTX card, RTX 50 included."
                    : "XeMFG with the unlocker: 2x to 6x. HSF follows the multiplier the unlocker sets (Universal MFG Unlocker's menu).";
            if (mfgButtons != null) mfgButtons.Visibility = missing ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        }

        bool isXeMfg = HsfService.ReadFgEngine(installPath) == HsfService.EngineXeMfg;
        ShowUnlocker(isXeMfg);
        cog.AddCombo("Engine",
            "DLSS-G: NVIDIA's frame generation. XeMFG: Intel's XeSS frame generation run inside DLSS-G; needs Universal MFG Unlocker "
            + "for more than 2x on every RTX card. If XeMFG can't start, HSF falls back to DLSS-G and its overlay says "
            + "DLSS-G (XEMFG FAILED); HSF.log says why.",
            new[] { "DLSS-G", "XeMFG" }, isXeMfg ? 1 : 0,
            i =>
            {
                var engine = i == 1 ? HsfService.EngineXeMfg : HsfService.EngineDlssg;
                if (!svc.WriteFgEngine(installPath, engine))
                {
                    cog.SetError("Couldn't change HSF.ini. Close the game and try again.");
                    return false;
                }
                ShowUnlocker(i == 1);
                cog.SetSuccess(i == 1
                    ? "XeMFG set. It starts the next time the game starts."
                    : "DLSS-G set. It applies the next time the game starts.");
                return true;
            });
        cog.AddNote("XeFG can't use scRGB HDR output: in a game that has it, HSF stays on DLSS-G.");
    }
}
