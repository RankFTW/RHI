// MainWindow.AntivirusNotice.cs — tells the user when an antivirus deleted a file RHI had just
// downloaded (see Services/AntivirusGuard.cs). Shown once per component per session.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RenoDXCommander.Services;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    private readonly HashSet<string> _antivirusNoticeShown = new(StringComparer.OrdinalIgnoreCase);

    private void HookAntivirusNotice()
    {
        AntivirusGuard.Blocked += (component, path) =>
            DispatcherQueue?.TryEnqueue(() => _ = ShowAntivirusNoticeAsync(component, path));
    }

    private async Task ShowAntivirusNoticeAsync(string component, string path)
    {
        try
        {
            if (IsShuttingDown || !_antivirusNoticeShown.Add(component)) return;
            ShowStatusBarMessage($"Your antivirus removed {component} after RHI downloaded it.", TimeSpan.FromSeconds(15));
            if (Content?.XamlRoot is not { } root) return;

            var folder = Path.GetDirectoryName(path) ?? path;
            var dialog = new ContentDialog
            {
                Title = "Antivirus removed a download",
                Content =
                    $"Your antivirus deleted {component} right after RHI downloaded it:\n{path}\n\n"
                    + "Mods like this change how games load DLLs, so antivirus programs often flag them even though they're safe "
                    + "(a false positive). RHI won't download it again automatically.\n\n"
                    + "To use it, add an exclusion for %LocalAppData%\\RHI in your antivirus (in Windows Security: Virus & threat "
                    + "protection > Manage settings > Exclusions), restore the file if your antivirus quarantined it, then install it again from RHI.",
                PrimaryButtonText = "Open folder",
                CloseButtonText = "OK",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = root,
                RequestedTheme = ElementTheme.Dark,
            };
            if (await DialogService.ShowSafeAsync(dialog) == ContentDialogResult.Primary)
            {
                var open = Directory.Exists(folder) ? folder
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI");
                System.Diagnostics.Process.Start("explorer.exe", $"\"{open}\"");
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainWindow.ShowAntivirusNoticeAsync] {ex.Message}");
        }
    }
}
