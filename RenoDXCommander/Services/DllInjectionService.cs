using System.Diagnostics;
using System.Runtime.InteropServices;

namespace RenoDXCommander.Services;

/// <summary>
/// Injects a DLL into a target process using CreateRemoteThread + LoadLibraryW.
/// Used for games where the launcher spawns the game exe in a way that bypasses local DLL proxy loading.
/// </summary>
public static class DllInjectionService
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out int lpNumberOfBytesWritten);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, uint dwCreationFlags, out uint lpThreadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint dwFreeType);

    private const uint PROCESS_ALL_ACCESS = 0x1F0FFF;
    private const uint MEM_COMMIT = 0x1000;
    private const uint MEM_RELEASE = 0x8000;
    private const uint PAGE_READWRITE = 0x04;

    /// <summary>
    /// Injects a DLL into the specified process.
    /// </summary>
    /// <param name="processId">Target process ID.</param>
    /// <param name="dllPath">Full path to the DLL to inject.</param>
    /// <returns>True if injection succeeded.</returns>
    public static bool Inject(int processId, string dllPath)
    {
        if (!File.Exists(dllPath))
        {
            CrashReporter.Log($"[DllInjectionService.Inject] DLL not found: {dllPath}");
            return false;
        }

        IntPtr hProcess = IntPtr.Zero;
        IntPtr allocMem = IntPtr.Zero;

        try
        {
            hProcess = OpenProcess(PROCESS_ALL_ACCESS, false, processId);
            if (hProcess == IntPtr.Zero)
            {
                CrashReporter.Log($"[DllInjectionService.Inject] OpenProcess failed for PID {processId} — error {Marshal.GetLastWin32Error()}");
                return false;
            }

            // Get LoadLibraryW address (same in all processes due to ASLR base sharing for system DLLs)
            var kernel32 = GetModuleHandle("kernel32.dll");
            var loadLibraryAddr = GetProcAddress(kernel32, "LoadLibraryW");
            if (loadLibraryAddr == IntPtr.Zero)
            {
                CrashReporter.Log("[DllInjectionService.Inject] Failed to get LoadLibraryW address");
                return false;
            }

            // Write DLL path into target process memory
            byte[] dllPathBytes = System.Text.Encoding.Unicode.GetBytes(dllPath + '\0');
            allocMem = VirtualAllocEx(hProcess, IntPtr.Zero, (uint)dllPathBytes.Length, MEM_COMMIT, PAGE_READWRITE);
            if (allocMem == IntPtr.Zero)
            {
                CrashReporter.Log($"[DllInjectionService.Inject] VirtualAllocEx failed — error {Marshal.GetLastWin32Error()}");
                return false;
            }

            if (!WriteProcessMemory(hProcess, allocMem, dllPathBytes, (uint)dllPathBytes.Length, out _))
            {
                CrashReporter.Log($"[DllInjectionService.Inject] WriteProcessMemory failed — error {Marshal.GetLastWin32Error()}");
                return false;
            }

            // Create remote thread calling LoadLibraryW with the DLL path
            var hThread = CreateRemoteThread(hProcess, IntPtr.Zero, 0, loadLibraryAddr, allocMem, 0, out _);
            if (hThread == IntPtr.Zero)
            {
                CrashReporter.Log($"[DllInjectionService.Inject] CreateRemoteThread failed — error {Marshal.GetLastWin32Error()}");
                return false;
            }

            // Wait for LoadLibrary to complete (max 10 seconds)
            WaitForSingleObject(hThread, 10000);
            CloseHandle(hThread);

            CrashReporter.Log($"[DllInjectionService.Inject] Successfully injected '{Path.GetFileName(dllPath)}' into PID {processId}");
            return true;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DllInjectionService.Inject] Exception — {ex.Message}");
            return false;
        }
        finally
        {
            if (allocMem != IntPtr.Zero && hProcess != IntPtr.Zero)
                VirtualFreeEx(hProcess, allocMem, 0, MEM_RELEASE);
            if (hProcess != IntPtr.Zero)
                CloseHandle(hProcess);
        }
    }

    /// <summary>
    /// Monitors for a process by name and injects the DLL once found.
    /// Polls every 500ms for up to 30 seconds.
    /// </summary>
    /// <param name="processName">Process name without extension (e.g. "ddo").</param>
    /// <param name="dllPath">Full path to the DLL to inject.</param>
    /// <param name="delayMs">Delay after finding process before injecting (to let it initialize).</param>
    /// <returns>True if injection succeeded.</returns>
    public static async Task<bool> MonitorAndInjectAsync(string processName, string dllPath, int delayMs = 2000)
    {
        CrashReporter.Log($"[DllInjectionService.MonitorAndInjectAsync] Waiting for '{processName}' to start...");

        for (int i = 0; i < 60; i++) // 30 seconds max (60 * 500ms)
        {
            await Task.Delay(500);

            var procs = Process.GetProcessesByName(processName);
            if (procs.Length > 0)
            {
                var proc = procs[0];
                CrashReporter.Log($"[DllInjectionService.MonitorAndInjectAsync] Found '{processName}' (PID {proc.Id}), waiting {delayMs}ms before injection...");

                // Wait for the process to initialize D3D
                await Task.Delay(delayMs);

                return Inject(proc.Id, dllPath);
            }
        }

        CrashReporter.Log($"[DllInjectionService.MonitorAndInjectAsync] Timeout — '{processName}' did not appear within 30s");
        return false;
    }
}
