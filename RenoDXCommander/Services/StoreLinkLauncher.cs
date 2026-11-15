// StoreLinkLauncher.cs — opens a store's launch link (Epic, Steam) without RHI's admin rights.
// When RHI runs elevated (Admin Mode) and the store isn't running yet, opening its link directly starts the
// store elevated too. An elevated store then silently drops every later link from normal processes: Windows
// blocks the second, non-elevated instance from handing the link to it (seen with the new Epic launcher in
// Nov 2026: nothing launched, nothing logged, until the elevated launcher was closed). Explorer runs at normal
// integrity, so a link opened through it starts the store normally. Same approach as the Drop Helper.

using System.Diagnostics;

namespace RenoDXCommander.Services;

public static class StoreLinkLauncher
{
    /// <summary>Opens <paramref name="uri"/>; through Explorer when RHI is elevated, so the store starts without admin rights.</summary>
    public static void Open(string uri, string logTag)
    {
        if (VulkanLayerService.IsRunningAsAdmin())
        {
            // Explorer opens the link with the user's normal rights; quoted so "&" in the query stays part of it
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "\"" + uri + "\"",
                UseShellExecute = false,
            });
            CrashReporter.Log($"[{logTag}] Opened through Explorer (RHI is elevated, so the store starts without admin rights): {uri}");
            return;
        }
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
    }
}
