// Program.cs — entry point. Wraps the XAML-generated Main (DISABLE_XAML_GENERATED_MAIN in the csproj)
// so that if WinUI can't start (Windows App Runtime missing or broken), the user gets a plain Windows
// message box with a download link instead of RHI silently not opening. The installer no longer
// installs the runtime itself.

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RenoDXCommander;

public static class Program
{
    internal const string RuntimeDownloadUrl = "https://aka.ms/windowsappsdk/2.5/latest/windowsappruntimeinstall-x64.exe";

    /// <summary>Set once App's constructor runs: from then on WinUI itself started, so later failures aren't the runtime.</summary>
    internal static volatile bool XamlStarted;

    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            XamlGeneratedProgram.XamlGeneratedMain();
        }
        catch (Exception ex) when (!XamlStarted)
        {
            try { Services.CrashReporter.Log($"[Program.Main] WinUI failed to start — {ex.GetType().Name}: {ex.Message}"); }
            catch (Exception logEx) { Debug.WriteLine($"[Program.Main] Log failed — {logEx.Message}"); }
            ShowRuntimeMissing(ex);
        }
    }

    private static void ShowRuntimeMissing(Exception ex)
    {
        const uint MB_YESNO = 0x4, MB_ICONWARNING = 0x30, MB_SETFOREGROUND = 0x10000;
        const int IDYES = 6;
        var text =
            "RHI couldn't start because the Windows App Runtime 2.5 is missing or damaged.\n\n" +
            "Download and install it now? Then open RHI again.\n\n" +
            $"Details: {ex.GetType().Name}: {ex.Message}";
        if (MessageBoxW(IntPtr.Zero, text, "RHI", MB_YESNO | MB_ICONWARNING | MB_SETFOREGROUND) != IDYES) return;
        try
        {
            Process.Start(new ProcessStartInfo(RuntimeDownloadUrl) { UseShellExecute = true });
        }
        catch (Exception openEx) when (openEx is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBoxW(IntPtr.Zero, $"Couldn't open the browser. Download the runtime from:\n{RuntimeDownloadUrl}", "RHI", MB_ICONWARNING);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
