// SettingsHandler.GSyncMonitors.cs -- the ⚙ beside Settings › Driver Settings › G-Sync: allow G-SYNC per monitor
// (the NVIDIA app's System › Displays › G-SYNC switch, needed for G-SYNC Compatible monitors NVIDIA hasn't
// validated). The G-Sync combo is a global driver setting and can't turn it on for such a monitor.
// Built in C# (XAML constraint). Setting: DlssPresetService.MonitorGSync.cs; monitors / restart: GSyncMonitorService.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public partial class SettingsHandler
{
    private bool _gsyncCogAdded;
    // True while the GPU restart runs (the dialog can be closed and reopened meanwhile)
    private bool _gsyncRestarting;

    /// <summary>
    /// Adds the ⚙ to the G-Sync row once, right of the dropdown like the Windows HDR / Change resolution rows:
    /// the dropdown moves into an inner Grid [* | Auto] in the row's value column (the XAML those rows use).
    /// </summary>
    private void EnsureGSyncMonitorsCog()
    {
        if (_gsyncCogAdded) return;
        var combo = _window.GSyncEnableCombo;
        if (combo.Parent is not Grid row) return;
        _gsyncCogAdded = true;

        var cog = new Button
        {
            Content = "⚙",
            Style = (Style)Application.Current.Resources["SettingsIconButtonStyle"],
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SettingsLabelBrush"],
        };
        ToolTipService.SetToolTip(cog, "Allow G-SYNC on each monitor (the NVIDIA app's per-monitor G-SYNC switch).");
        cog.Click += async (_, _) =>
        {
            try { await ShowGSyncMonitorsDialogAsync(); }
            catch (Exception ex) { CrashReporter.Log($"[SettingsHandler.GSyncMonitorsCog] {ex}"); }
        };

        var inner = new Grid { ColumnSpacing = 6 };
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(inner, 1);
        row.Children.Remove(combo);   // one parent only: take it out before adding it to the inner Grid
        Grid.SetColumn(combo, 0);
        inner.Children.Add(combo);
        Grid.SetColumn(cog, 1);
        inner.Children.Add(cog);
        row.Children.Add(inner);
    }

    private async Task ShowGSyncMonitorsDialogAsync()
    {
        var xamlRoot = _window.Content.XamlRoot;
        if (xamlRoot == null) return;
        var presetSvc = App.Services.GetRequiredService<DlssPresetService>();
        bool isAdmin = VulkanLayerService.IsRunningAsAdmin();

        var (monitors, savedByProfile) = await Task.Run(() =>
        {
            var list = GSyncMonitorService.GetMonitors();
            var saved = new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in list)
                if (m.ProfileName != null && !saved.ContainsKey(m.ProfileName))
                    saved[m.ProfileName] = presetSvc.GetMonitorGSyncAllowed(m.ProfileName);
            return (list, saved);
        });

        var cog = new CogDialog("G-SYNC Monitors", xamlRoot);
        cog.AddIntro("Allow G-SYNC on each monitor. This is the G-SYNC switch in the NVIDIA app (System › Displays), "
            + "which G-SYNC Compatible monitors that NVIDIA hasn't validated need before G-SYNC works on them. "
            + "The G-Sync setting in Driver Settings can't turn it on for those monitors.");
        if (_gsyncRestarting)
            cog.AddWarning("The graphics driver is restarting. Wait for it to finish before changing anything here.");

        // Per monitor: the value saved in the driver profile and what the driver uses now
        var wanted = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var live = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var helpBlocks = new Dictionary<string, TextBlock>(StringComparer.OrdinalIgnoreCase);
        var combos = new List<ComboBox>();
        Button? restartBtn = null;

        string HelpFor(string profile) =>
            wanted[profile] == live[profile]
                ? (live[profile] ? "G-SYNC is allowed on this monitor now." : "G-SYNC is off on this monitor now.")
                : $"Set to {(wanted[profile] ? "allowed" : "off")}. Takes effect when the graphics driver restarts.";

        void Refresh()
        {
            foreach (var (p, tb) in helpBlocks) tb.Text = HelpFor(p);
            bool pending = wanted.Any(w => w.Value != live[w.Key]);
            if (restartBtn != null) restartBtn.IsEnabled = isAdmin && pending && !_gsyncRestarting;
        }

        cog.AddSection("Monitors");
        if (monitors.Count == 0) cog.AddEmpty("No NVIDIA displays found.");
        foreach (var m in monitors)
        {
            bool usable = m.VrrCapable && m.ProfileName != null;
            string? profile = m.ProfileName;
            bool current = m.LiveAllowed;
            if (usable)
            {
                current = savedByProfile.TryGetValue(profile!, out var s) && s.HasValue ? s.Value : m.LiveAllowed;
                // Two identical monitors share one profile: the first one decides
                if (!wanted.ContainsKey(profile!)) { wanted[profile!] = current; live[profile!] = m.LiveAllowed; }
            }

            var combo = CogDialog.MakeCombo(new[] { "Allowed", "Off" }, current ? 0 : 1, isEnabled: usable && !_gsyncRestarting);
            string help = !m.VrrCapable
                ? "This monitor doesn't report variable refresh rate support to the NVIDIA driver."
                : profile == null ? "RHI couldn't read this monitor's details (EDID)." : HelpFor(profile);
            var row = cog.AddRow(m.FriendlyName, combo, help);

            if (usable)
            {
                combos.Add(combo);
                if (row is StackPanel sp && sp.Children.Count > 1 && sp.Children[^1] is TextBlock tb)
                    helpBlocks[profile!] = tb;
                combo.SelectionChanged += async (_, _) =>
                {
                    bool allow = combo.SelectedIndex == 0;
                    bool ok = await Task.Run(() => presetSvc.SetMonitorGSyncAllowed(profile!, allow));
                    if (ok)
                    {
                        wanted[profile!] = allow;
                        cog.ClearStatus();
                    }
                    else
                    {
                        cog.SetError("RHI couldn't save this to the NVIDIA driver settings. The log has the details.");
                    }
                    Refresh();
                };
            }
        }

        if (wanted.Count > 0)
        {
            cog.AddSection("Apply");
            cog.AddNote("Changes save straight away and the graphics driver uses them the next time it starts: "
                + "restart Windows, or restart the driver now. Restarting the driver turns your screens black for "
                + "a few seconds and running games may close, so close them first. The result shows in the status bar.");
            cog.AddWarning(GSyncMonitorService.OverclockResetNote);
            if (!isAdmin)
                cog.AddWarning("Restarting the driver from RHI needs admin rights (Admin Mode in Settings › General). "
                    + "Without it, restart Windows to apply the change.");

            restartBtn = cog.MakeButton("Restart driver now", CogButtonKind.Primary, async () =>
            {
                // Same flow as DLDSR's Apply, whose window survives the GPU restart: the dialog closes first, and
                // nothing touches NVAPI after the restart. Reading the monitors through NVAPI in-process 6-12 s after
                // the restart left the window without text or images (Nov 2026; an outside restart with RHI open,
                // a G-SYNC change and the ⚙ opened beforehand were all fine). Reopening the ⚙ shows the real state.
                _gsyncRestarting = true;
                cog.Hide();
                await Task.Delay(300);   // let the dialog finish closing before the device goes away
                bool ok = false;
                try
                {
                    ok = await GSyncMonitorService.RestartGpuAsync();
                    if (ok) await Task.Delay(12000);
                }
                finally
                {
                    _gsyncRestarting = false;
                }
                var message = ok
                    ? "The graphics driver restarted with the new G-SYNC settings. Open the G-Sync ⚙ to check each monitor."
                    : "RHI couldn't restart the graphics driver. Restart Windows to apply the G-SYNC change.";
                CrashReporter.Log($"[SettingsHandler.GSyncMonitors] {message}");
                _window.ShowStatusBarMessage(message, TimeSpan.FromSeconds(15));
            }, "Closes this window and restarts the graphics driver so it uses the new settings. Screens go black for a few seconds.");
            cog.AddButtons(restartBtn);
            Refresh();
        }

        await cog.ShowAsync();
    }
}
