using Microsoft.Win32;

namespace RenoDXCommander.Services;

/// <summary>
/// Registers .addon64 and .addon32 file associations so double-clicking
/// an addon file opens RDXC with the file path as a command-line argument.
/// Uses HKCU (per-user, no admin required).
/// </summary>
public static class FileAssociationService
{
    private const string ProgId = "RenoDXCommander.Addon";

    public static void Register(ICrashReporter crashReporter)
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return;

            // Create ProgId: HKCU\Software\Classes\RenoDXCommander.Addon
            using var progKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}");
            progKey?.SetValue("", "RenoDX Addon");

            // Icon
            using var iconKey = progKey?.CreateSubKey("DefaultIcon");
            iconKey?.SetValue("", $"\"{exePath}\",0");

            // Open command: "path\to\RenoDXCommander.exe" "%1"
            using var cmdKey = progKey?.CreateSubKey(@"shell\open\command");
            cmdKey?.SetValue("", $"\"{exePath}\" \"%1\"");

            // Associate .addon64 and .addon32 with the ProgId
            foreach (var ext in new[] { ".addon64", ".addon32" })
            {
                using var extKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ext}");
                extKey?.SetValue("", ProgId);
            }

            crashReporter.Log("[FileAssociationService] Registered .addon64/.addon32 file associations");
        }
        catch (Exception ex)
        {
            crashReporter.Log($"[FileAssociationService] Registration failed — {ex.Message}");
        }
    }
}
