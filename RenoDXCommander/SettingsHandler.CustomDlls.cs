// SettingsHandler.CustomDlls.cs -- "Custom DLLs" button in Settings › Tools › Folders and logs, right after the
// Custom folder button: opens %LocalAppData%\RHI\Custom\DLLs\ (the user's own DLLs for the Extras row).
// Built in C# (XAML constraint), added the first time Settings opens.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public partial class SettingsHandler
{
    private Button? _customDllFolderBtn;

    private void EnsureCustomDllFolderButton()
    {
        if (_customDllFolderBtn != null) return;
        try
        {
            // OpenCustomBtn is a private generated field: find it by Name (searching down; Parent can be null here)
            var (panel, customBtn) = FindButtonAndPanel(_window.SettingsCardsPanel, "OpenCustomBtn");
            if (panel == null || customBtn == null)
            {
                CrashReporter.Log("[SettingsHandler.EnsureCustomDllFolderButton] Custom folder button not found — button not added");
                return;
            }
            _customDllFolderBtn = new Button { Content = "Custom DLLs", Style = FindStyle("SettingsButtonStyle") };
            if (Application.Current.Resources.TryGetValue("SettingsLabelBrush", out var brush) && brush is Microsoft.UI.Xaml.Media.Brush b)
                _customDllFolderBtn.Foreground = b;
            ToolTipService.SetToolTip(_customDllFolderBtn,
                "Open the custom DLL folder. DLLs you put there (for example Special K) can be installed per game from Extras › Custom DLLs.");
            _customDllFolderBtn.Click += (s, e) =>
            {
                try
                {
                    var dir = ViewModel.CustomDlls.Folder;
                    CrashReporter.Log("[SettingsHandler.CustomDllFolder] User opened the custom DLL folder");
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dir) { UseShellExecute = true });
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    CrashReporter.Log($"[SettingsHandler.CustomDllFolder] Open failed — {ex.Message}");
                }
            };
            int idx = panel.Children.IndexOf(customBtn);
            panel.Children.Insert(idx + 1, _customDllFolderBtn);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            CrashReporter.Log($"[SettingsHandler.EnsureCustomDllFolderButton] Failed — {ex.Message}");
        }
    }
}
