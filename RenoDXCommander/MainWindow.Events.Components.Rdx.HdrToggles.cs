// MainWindow.Events.Components.Rdx.HdrToggles.cs — RTX HDR and Auto HDR toggles in the RenoDX cog dialog.
// RTX HDR, Auto HDR and RenoDX are mutually exclusive: turning one HDR option on turns the other
// off and uninstalls RenoDX. The slider config dialogs live in MainWindow.Events.Components.Hdr.cs.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    private static List<string> RdxOffOn() => new() { "Off", "On" };

    /// <summary>Adds the RTX HDR and Auto HDR sections to the RenoDX cog dialog.</summary>
    private async Task AddRdxHdrToggleSectionsAsync(CogDialog cog, GameCardViewModel card)
    {
        // autoHdrCombo is declared up front so the RTX HDR handler can reference it for
        // mutual exclusivity (before the Auto HDR section builds it).
        ComboBox? autoHdrCombo = null;
        ComboBox? rtxHdrCombo = null;

        // ── RTX HDR ─────────────────────────────────────────────────────────────────
        cog.AddSection("RTX HDR");

        var gameNameService = App.Services.GetRequiredService<IGameNameService>();
        // Read live driver state off the UI thread — GetRtxHdrEnable calls _sessionLock.Wait(5000)
        var dlssPresetServiceCog = App.Services.GetRequiredService<DlssPresetService>();
        bool isRtxHdrEnabled;
        if (dlssPresetServiceCog.IsSupported && !string.IsNullOrEmpty(card.InstallPath))
        {
            var capturedName = card.GameName; var capturedPath = card.InstallPath;
            isRtxHdrEnabled = await Task.Run(() => dlssPresetServiceCog.GetRtxHdrEnable(capturedName, capturedPath) == 0x01);
        }
        else
        {
            isRtxHdrEnabled = gameNameService.RtxHdrGames.Contains(card.GameName);
        }
        // Sync persisted state to match driver
        if (isRtxHdrEnabled) gameNameService.RtxHdrGames.Add(card.GameName);
        else gameNameService.RtxHdrGames.Remove(card.GameName);
        card.IsRtxHdrEnabled = isRtxHdrEnabled;

        rtxHdrCombo = cog.AddCombo("Enable RTX HDR",
            "NVIDIA RTX HDR for this game, set in its driver profile. Turning it on uninstalls RenoDX and turns off Auto HDR. " +
            "Needs the NVIDIA App with Overlay and Game Filters enabled.",
            RdxOffOn(), isRtxHdrEnabled ? 1 : 0, idx =>
        {
            bool enable = idx == 1;
            var dlssPresetService = App.Services.GetRequiredService<DlssPresetService>();

            if (enable)
            {
                gameNameService.RtxHdrGames.Add(card.GameName);
                card.IsRtxHdrEnabled = true;

                // Mutual exclusivity: if Auto HDR is on, turn it off
                if (card.IsAutoHdrEnabled)
                {
                    gameNameService.AutoHdrGames.Remove(card.GameName);
                    card.IsAutoHdrEnabled = false;
                    if (autoHdrCombo != null) autoHdrCombo.SelectedIndex = 0;
                    var capturedAutoHdrSvc = App.Services.GetRequiredService<IAutoHdrService>();
                    var capturedAutoHdrCard = card;
                    _ = Task.Run(() =>
                    {
                        var exePath = capturedAutoHdrSvc.ResolveExePath(
                            capturedAutoHdrCard.GameName, capturedAutoHdrCard.InstallPath,
                            gameNameService.LaunchExeOverrides, ViewModel.Manifest?.LaunchExeOverrides);
                        if (!string.IsNullOrEmpty(exePath)) capturedAutoHdrSvc.Disable(exePath);
                    });
                }

                // Uninstall RenoDX if installed
                if (card.Status == GameStatus.Installed && card.InstalledRecord != null)
                {
                    ViewModel.UninstallMod(card);
                }

                // Set RTX HDR profile settings (Allow + Enable + sensible defaults)
                // Default to Gamma 2.2 (Contrast = +25, stored = 125) — matches conventional SDR gamma
                var enablePeakNits = ViewModel.Settings.PeakNits > 0 ? ViewModel.Settings.PeakNits : 510;
                // Calculate ITU-correct Middle Grey for Gamma 2.2 at the user's peak nits
                // paperWhite lookup table: (peak, pw nits) — interpolated
                static double Lerp(double a, double b, double t) => a + t * (b - a);
                double enablePaperWhite;
                (double peak, double pw)[] ituTable = { (400,101),(600,138),(800,172),(1000,203),(1500,276),(2000,343) };
                if (enablePeakNits <= ituTable[0].peak) enablePaperWhite = ituTable[0].pw;
                else if (enablePeakNits >= ituTable[^1].peak) enablePaperWhite = ituTable[^1].pw;
                else
                {
                    enablePaperWhite = ituTable[^1].pw;
                    for (int i = 0; i < ituTable.Length - 1; i++)
                    {
                        if (enablePeakNits >= ituTable[i].peak && enablePeakNits <= ituTable[i+1].peak)
                        {
                            double t = (enablePeakNits - ituTable[i].peak) / (ituTable[i+1].peak - ituTable[i].peak);
                            enablePaperWhite = Lerp(ituTable[i].pw, ituTable[i+1].pw, t);
                            break;
                        }
                    }
                }
                var enableMidGrey = (uint)Math.Clamp((int)Math.Round(enablePaperWhite * Math.Pow(0.5, 2.2)), 10, 100);
                var capturedEnablePeakNits = enablePeakNits;
                var capturedGameName = card.GameName;
                var capturedInstallPath = card.InstallPath;
                _ = Task.Run(() =>
                {
                    dlssPresetService.SetRtxHdrEnable(capturedGameName, capturedInstallPath, 0x01);
                    dlssPresetService.SetRtxHdrPeakBrightness(capturedGameName, capturedInstallPath, (uint)capturedEnablePeakNits);
                    dlssPresetService.SetRtxHdrContrast(capturedGameName, capturedInstallPath, 125);
                    dlssPresetService.SetRtxHdrSaturation(capturedGameName, capturedInstallPath, 75);
                    dlssPresetService.SetRtxHdrMiddleGrey(capturedGameName, capturedInstallPath, enableMidGrey);
                    CrashReporter.Log($"[RdxCogButton_Click] RTX HDR enabled for '{capturedGameName}': PeakNits={capturedEnablePeakNits}, Contrast=125 (Gamma 2.2), Sat=75 (-25), MidGrey={enableMidGrey}");
                });
            }
            else
            {
                gameNameService.RtxHdrGames.Remove(card.GameName);
                card.IsRtxHdrEnabled = false;

                var capturedGameNameD = card.GameName;
                var capturedInstallPathD = card.InstallPath;
                _ = Task.Run(() =>
                {
                    dlssPresetService.SetRtxHdrEnable(capturedGameNameD, capturedInstallPathD, 0x00);
                    dlssPresetService.SetRtxHdrContrast(capturedGameNameD, capturedInstallPathD, 100);
                    dlssPresetService.SetRtxHdrSaturation(capturedGameNameD, capturedInstallPathD, 100);
                    dlssPresetService.SetRtxHdrPeakBrightness(capturedGameNameD, capturedInstallPathD, 0);
                    dlssPresetService.SetRtxHdrMiddleGrey(capturedGameNameD, capturedInstallPathD, 50);
                    dlssPresetService.DeleteSettingRaw(capturedGameNameD, capturedInstallPathD, 0x00432F84);
                    CrashReporter.Log($"[RdxCogButton_Click] RTX HDR disabled for '{capturedGameNameD}' — all settings deleted from profile");
                });
            }

            card.NotifyAll();
            ViewModel.SaveSettingsPublic();
            _detailPanelBuilder?.UpdateDetailComponentRows(card);
            PopulateDetailPanel(card);
        });

        // ── Auto HDR ────────────────────────────────────────────────────────────────
        cog.AddSection("Auto HDR");

        var autoHdrService = App.Services.GetRequiredService<IAutoHdrService>();
        bool isAutoHdrEnabled = gameNameService.AutoHdrGames.Contains(card.GameName);
        int autoHdrStrength = gameNameService.AutoHdrStrengths.TryGetValue(card.GameName, out var savedStrength) ? savedStrength : 50;
        card.IsAutoHdrEnabled = isAutoHdrEnabled;
        card.AutoHdrStrength  = autoHdrStrength;

        autoHdrCombo = cog.AddCombo("Enable Auto HDR",
            "Forces Windows 11 Auto HDR for this game through the registry, even when the global Auto HDR setting is off. " +
            "Turning it on uninstalls RenoDX and turns off RTX HDR.",
            RdxOffOn(), isAutoHdrEnabled ? 1 : 0, idx =>
        {
            bool enableAh = idx == 1;

            if (enableAh)
            {
                gameNameService.AutoHdrGames.Add(card.GameName);
                card.IsAutoHdrEnabled = true;

                // Mutual exclusivity: if RTX HDR is on, turn it off first
                if (card.IsRtxHdrEnabled)
                {
                    gameNameService.RtxHdrGames.Remove(card.GameName);
                    card.IsRtxHdrEnabled = false;
                    if (rtxHdrCombo != null) rtxHdrCombo.SelectedIndex = 0;
                    var capturedDlssSvc = App.Services.GetRequiredService<DlssPresetService>();
                    var capturedName2 = card.GameName; var capturedPath2 = card.InstallPath;
                    _ = Task.Run(() =>
                    {
                        capturedDlssSvc.SetRtxHdrEnable(capturedName2, capturedPath2, 0x00);
                        capturedDlssSvc.SetRtxHdrContrast(capturedName2, capturedPath2, 100);
                        capturedDlssSvc.SetRtxHdrSaturation(capturedName2, capturedPath2, 100);
                        capturedDlssSvc.SetRtxHdrPeakBrightness(capturedName2, capturedPath2, 0);
                        capturedDlssSvc.SetRtxHdrMiddleGrey(capturedName2, capturedPath2, 50);
                        capturedDlssSvc.DeleteSettingRaw(capturedName2, capturedPath2, 0x00432F84);
                    });
                }

                // Mutual exclusivity: uninstall RenoDX if installed
                if (card.Status == GameStatus.Installed && card.InstalledRecord != null)
                    ViewModel.UninstallMod(card);

                // Write registry entry on background thread
                var capturedEnableCard = card;
                var capturedEnableStrength = card.AutoHdrStrength;
                _ = Task.Run(() =>
                {
                    var exePath = autoHdrService.ResolveExePath(
                        capturedEnableCard.GameName,
                        capturedEnableCard.InstallPath,
                        gameNameService.LaunchExeOverrides,
                        ViewModel.Manifest?.LaunchExeOverrides);
                    if (!string.IsNullOrEmpty(exePath))
                        autoHdrService.Enable(exePath, capturedEnableStrength);
                    else
                        CrashReporter.Log($"[RdxCog.AutoHDR] Could not resolve exe for '{capturedEnableCard.GameName}' — registry not written");
                });
            }
            else
            {
                gameNameService.AutoHdrGames.Remove(card.GameName);
                card.IsAutoHdrEnabled = false;

                var capturedDisableCard = card;
                _ = Task.Run(() =>
                {
                    var exePath = autoHdrService.ResolveExePath(
                        capturedDisableCard.GameName,
                        capturedDisableCard.InstallPath,
                        gameNameService.LaunchExeOverrides,
                        ViewModel.Manifest?.LaunchExeOverrides);
                    if (!string.IsNullOrEmpty(exePath))
                        autoHdrService.Disable(exePath);
                });
            }

            card.NotifyAll();
            ViewModel.SaveSettingsPublic();
            _detailPanelBuilder?.UpdateDetailComponentRows(card);
            PopulateDetailPanel(card);
        });
    }
}
