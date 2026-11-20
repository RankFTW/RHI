// BackupRestoreHelper.cs -- the parts of restoring a backup shared by the first-run window (SetupWindow), Settings
// (SettingsHandler.Backup.cs) and startup (App.OnLaunched): picking a .rhibackup file, describing it, and a restore
// requested from Settings, which is staged and applied by the next RHI start before any setting is read.
// Plan: docs/backup-restore-plan.md.

using RenoDXCommander.Services;

namespace RenoDXCommander;

internal static class BackupRestoreHelper
{
    private static string RhiDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI");

    private static string PendingZip => Path.Combine(RhiDir, "restore-pending" + BackupArchive.Extension);
    private static string PendingPid => Path.Combine(RhiDir, "restore-pending.pid");

    /// <summary>Win32 open dialog for a .rhibackup file (WinRT pickers have COM threading problems here).</summary>
    public static Task<string?> PickBackupFileAsync(IntPtr hwnd, string? startFolder) => Task.Run(() =>
    {
        var ofn = new NativeInterop.OpenFileName();
        ofn.structSize = System.Runtime.InteropServices.Marshal.SizeOf(ofn);
        ofn.hwndOwner = hwnd;
        ofn.filter = "RHI backups (*.rhibackup)\0*.rhibackup\0All files (*.*)\0*.*\0";
        ofn.file = new string(new char[1024]);
        ofn.maxFile = ofn.file.Length;
        ofn.title = "Choose an RHI backup";
        if (!string.IsNullOrEmpty(startFolder) && Directory.Exists(startFolder)) ofn.initialDir = startFolder;
        ofn.flags = 0x00080000 | 0x00001000; // OFN_EXPLORER | OFN_FILEMUSTEXIST
        return NativeInterop.GetOpenFileName(ref ofn) ? ofn.file.TrimEnd('\0') : null;
    });

    /// <summary>One line about a backup: when it was made and what's in it.</summary>
    public static string Describe(BackupManifest m)
    {
        int installs = m.Games.Sum(g => g.Components.Count);
        var when = m.CreatedUtc.ToLocalTime().ToString("d MMMM yyyy, HH:mm");
        var text = $"Made {when} by RHI {m.RhiVersion}: {m.Games.Count} {(m.Games.Count == 1 ? "game" : "games")} with {installs} {(installs == 1 ? "install" : "installs")}";
        if (m.HasNvidia) text += ", NVIDIA driver settings";
        return text + ".";
    }

    /// <summary>
    /// Settings › Restore: copies the backup to restore-pending.rhibackup and notes this process, so the next
    /// RHI start applies it once this one has exited (it saves its settings on the way out).
    /// </summary>
    public static void StageRestore(string backupPath)
    {
        BackupArchive.ReadManifest(backupPath); // throws InvalidDataException for a bad file
        Directory.CreateDirectory(RhiDir);
        File.Copy(backupPath, PendingZip, overwrite: true);
        File.WriteAllText(PendingPid, Environment.ProcessId.ToString());
        CrashReporter.Log($"[BackupRestoreHelper.StageRestore] Staged '{backupPath}' for the next start");
    }

    /// <summary>
    /// Startup: applies a restore staged from Settings. Waits for the RHI that staged it to exit first
    /// (at most 20 s). Returns true when RHI's data was restored.
    /// </summary>
    public static async Task<bool> ApplyStagedRestoreAsync()
    {
        if (!File.Exists(PendingZip)) return false;
        try
        {
            if (File.Exists(PendingPid) && int.TryParse(File.ReadAllText(PendingPid).Trim(), out var pid) && pid != Environment.ProcessId)
            {
                try
                {
                    using var old = System.Diagnostics.Process.GetProcessById(pid);
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                    await old.WaitForExitAsync(cts.Token);
                }
                catch (ArgumentException) { /* already gone */ }
                catch (OperationCanceledException)
                {
                    CrashReporter.Log("[BackupRestoreHelper.ApplyStagedRestoreAsync] The previous RHI is still running after 20 s — restoring anyway");
                }
            }
            BackupArchive.ApplyData(PendingZip, RhiDir);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
        {
            CrashReporter.Log($"[BackupRestoreHelper.ApplyStagedRestoreAsync] Restore failed — {ex.Message}");
            return false;
        }
        finally
        {
            foreach (var f in new[] { PendingZip, PendingPid })
            {
                try { if (File.Exists(f)) File.Delete(f); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    CrashReporter.Log($"[BackupRestoreHelper.ApplyStagedRestoreAsync] Couldn't delete '{f}' — {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Starts a new RHI and closes this one (Settings restore). The new one waits for this one to exit before
    /// restoring, and becomes the single instance once this one releases it.
    /// </summary>
    public static void RestartRhi(MainWindow window)
    {
        if (Environment.ProcessPath is not { } exe) return;
        SingleInstanceService.Stop();
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true });
        window.RequestExit();
    }
}
