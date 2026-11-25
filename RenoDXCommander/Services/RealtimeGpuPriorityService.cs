// RealtimeGpuPriorityService.cs -- BFI Presenter's presenter GPU queue runs at "global realtime" (PresenterQueuePriority=2)
// only in a game whose token holds Windows' "Increase scheduling priority" right (SeIncreaseBasePriorityPrivilege);
// otherwise it falls back to high. Same change as the BFI Presenter Manager's GPU priority row (manager/core.cpp
// GrantRealtimeGpuPriority): add / remove the right for the current account through the local security policy.
// Changing it needs admin: an elevated Windows PowerShell runs the same LSA calls (one admin prompt; its exit code is
// the result). Reading the policy needs admin too (LsaEnumerateAccountRights returns access denied otherwise, seen
// Nov 2026), so without it the status is what RHI last did (realtime_gpu_priority.txt, per account SID) or this
// sign-in's token. It reaches games after signing out and back in.

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace RenoDXCommander.Services;

internal static class RealtimeGpuPriorityService
{
    internal const string Right = "SeIncreaseBasePriorityPrivilege";

    /// <param name="Granted">The account has the right in the security policy (null = couldn't read it).</param>
    /// <param name="InToken">This sign-in already has it (null when RHI runs as admin: an admin token always has it).</param>
    internal readonly record struct Status(bool? Granted, bool? InToken);

    internal static Status GetStatus()
    {
        try
        {
            bool elevated = VulkanLayerService.IsRunningAsAdmin();
            using var id = WindowsIdentity.GetCurrent();
            // The policy can only be read with admin rights (LsaEnumerateAccountRights: access denied otherwise),
            // so without them: what RHI last did, else this sign-in's token
            bool? granted = elevated && id.User is { } sid ? HasRight(sid) : null;
            bool? inToken = elevated ? null : TokenHasRight();
            granted ??= ReadMarker() ?? (inToken == true ? true : null);
            return new Status(granted, inToken);
        }
        catch (Exception ex) when (ex is SystemException)
        {
            CrashReporter.Log($"[RealtimeGpuPriorityService.GetStatus] {ex.Message}");
            return new Status(null, null);
        }
    }

    /// <summary>Adds (grant) or removes the right for the current account. One admin prompt unless RHI runs as admin. Never throws.</summary>
    internal static async Task<(bool Ok, string? Error)> SetAsync(bool grant)
    {
        try
        {
            string sid;
            using (var id = WindowsIdentity.GetCurrent())
                sid = id.User?.Value ?? throw new InvalidOperationException("Couldn't read your Windows account.");

            var script = Script.Replace("%SID%", sid).Replace("%REMOVE%", grant ? "$false" : "$true");
            bool elevated = VulkanLayerService.IsRunningAsAdmin();
            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"))
            {
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand "
                          + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)),
                UseShellExecute = true,
                Verb = elevated ? "" : "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            CrashReporter.Log($"[RealtimeGpuPriorityService.SetAsync] {(grant ? "grant" : "remove")} for {sid} (elevated={elevated})");

            int exit;
            using (var p = Process.Start(psi) ?? throw new InvalidOperationException("Couldn't start PowerShell."))
            {
                await p.WaitForExitAsync();
                exit = p.ExitCode;
            }

            // The script's exit code is the LSA result (0 = done); the policy itself is readable only when elevated
            bool? has = elevated ? HasRight(new SecurityIdentifier(sid)) : null;
            bool ok = has.HasValue ? has == grant : exit == 0;
            CrashReporter.Log($"[RealtimeGpuPriorityService.SetAsync] exit {exit}, granted now {has?.ToString() ?? "unreadable without admin"}, ok={ok}");
            if (!ok) return (false, $"Windows didn't make the change (error {exit}).");
            WriteMarker(grant);
            return (true, null);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // the admin prompt was declined
        {
            CrashReporter.Log("[RealtimeGpuPriorityService.SetAsync] Admin prompt declined");
            return (false, "The Windows admin prompt was cancelled, so nothing changed.");
        }
        catch (Exception ex) when (ex is SystemException or InvalidOperationException)
        {
            CrashReporter.Log($"[RealtimeGpuPriorityService.SetAsync] Failed — {ex.Message}");
            return (false, ex.Message);
        }
    }

    // Exit code = Windows error (0 = done; removing a right the account doesn't have counts as done)
    private const string Script = """
        $ErrorActionPreference = 'Stop'
        Add-Type @'
        using System;
        using System.Runtime.InteropServices;
        public static class RhiLsa {
            [StructLayout(LayoutKind.Sequential)] public struct U { public ushort Length; public ushort MaximumLength; [MarshalAs(UnmanagedType.LPWStr)] public string Buffer; }
            [StructLayout(LayoutKind.Sequential)] public struct OA { public int Length; public IntPtr Root; public IntPtr Name; public uint Attributes; public IntPtr Sd; public IntPtr Sqos; }
            [DllImport("advapi32.dll")] static extern uint LsaOpenPolicy(IntPtr system, ref OA oa, uint access, out IntPtr policy);
            [DllImport("advapi32.dll")] static extern uint LsaAddAccountRights(IntPtr policy, byte[] sid, U[] rights, uint count);
            [DllImport("advapi32.dll")] static extern uint LsaRemoveAccountRights(IntPtr policy, byte[] sid, bool all, U[] rights, uint count);
            [DllImport("advapi32.dll")] static extern uint LsaClose(IntPtr policy);
            [DllImport("advapi32.dll")] static extern int LsaNtStatusToWinError(uint status);
            public static int Run(byte[] sid, bool remove) {
                var oa = new OA(); oa.Length = Marshal.SizeOf(typeof(OA));
                IntPtr pol;
                uint r = LsaOpenPolicy(IntPtr.Zero, ref oa, 0x10 | 0x800, out pol);
                if (r != 0) return LsaNtStatusToWinError(r);
                var name = "SeIncreaseBasePriorityPrivilege";
                var right = new U[] { new U { Buffer = name, Length = (ushort)(name.Length * 2), MaximumLength = (ushort)(name.Length * 2 + 2) } };
                r = remove ? LsaRemoveAccountRights(pol, sid, false, right, 1) : LsaAddAccountRights(pol, sid, right, 1);
                LsaClose(pol);
                int e = r == 0 ? 0 : LsaNtStatusToWinError(r);
                return (remove && e == 2) ? 0 : e;
            }
        }
        '@
        $s = New-Object System.Security.Principal.SecurityIdentifier('%SID%')
        $b = New-Object byte[] $s.BinaryLength
        $s.GetBinaryForm($b, 0)
        exit [RhiLsa]::Run($b, %REMOVE%)
        """;

    // ── What RHI last did (the policy can't be read without admin) ────────────
    private static string MarkerPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "realtime_gpu_priority.txt");

    /// <summary>True / false = RHI last granted / removed the right for this account; null = never changed it here.</summary>
    private static bool? ReadMarker()
    {
        try
        {
            if (!File.Exists(MarkerPath)) return null;
            var lines = File.ReadAllLines(MarkerPath);
            using var id = WindowsIdentity.GetCurrent();
            if (lines.Length < 2 || !string.Equals(lines[1].Trim(), id.User?.Value, StringComparison.OrdinalIgnoreCase)) return null;
            return lines[0].Trim() switch { "granted" => true, "removed" => false, _ => null };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[RealtimeGpuPriorityService.ReadMarker] {ex.Message}");
            return null;
        }
    }

    private static void WriteMarker(bool granted)
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath)!);
            File.WriteAllLines(MarkerPath, new[] { granted ? "granted" : "removed", id.User?.Value ?? "" });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[RealtimeGpuPriorityService.WriteMarker] {ex.Message}");
        }
    }

    // ── Reading ───────────────────────────────────────────────────────────────

    private static bool? HasRight(SecurityIdentifier sid)
    {
        var bytes = new byte[sid.BinaryLength];
        sid.GetBinaryForm(bytes, 0);
        var oa = new LSA_OBJECT_ATTRIBUTES { Length = Marshal.SizeOf<LSA_OBJECT_ATTRIBUTES>() };
        uint r = LsaOpenPolicy(IntPtr.Zero, ref oa, PolicyLookupNames, out var policy);
        if (r != 0)
        {
            CrashReporter.Log($"[RealtimeGpuPriorityService.HasRight] LsaOpenPolicy error {LsaNtStatusToWinError(r)}");
            return null;
        }
        try
        {
            r = LsaEnumerateAccountRights(policy, bytes, out var rights, out uint count);
            if (r == StatusObjectNameNotFound) return false; // the account has no rights of its own
            if (r != 0)
            {
                CrashReporter.Log($"[RealtimeGpuPriorityService.HasRight] LsaEnumerateAccountRights error {LsaNtStatusToWinError(r)}");
                return null;
            }
            try
            {
                int size = Marshal.SizeOf<LSA_UNICODE_STRING>();
                for (int i = 0; i < count; i++)
                {
                    var s = Marshal.PtrToStructure<LSA_UNICODE_STRING>(rights + i * size);
                    var name = Marshal.PtrToStringUni(s.Buffer, s.Length / 2);
                    if (string.Equals(name, Right, StringComparison.OrdinalIgnoreCase)) return true;
                }
                return false;
            }
            finally { LsaFreeMemory(rights); }
        }
        finally { LsaClose(policy); }
    }

    private static bool? TokenHasRight()
    {
        if (!LookupPrivilegeValue(null, Right, out long luid)) return null;
        using var id = WindowsIdentity.GetCurrent();
        GetTokenInformation(id.Token, TokenPrivileges, IntPtr.Zero, 0, out int len);
        if (len <= 0) return null;
        var buf = Marshal.AllocHGlobal(len);
        try
        {
            if (!GetTokenInformation(id.Token, TokenPrivileges, buf, len, out len)) return null;
            int n = Marshal.ReadInt32(buf);
            for (int i = 0; i < n; i++)
                if (Marshal.ReadInt64(buf + 4 + i * 12) == luid) return true; // LUID_AND_ATTRIBUTES: LUID (8) + attributes (4)
            return false;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private const uint PolicyLookupNames = 0x800;
    private const uint StatusObjectNameNotFound = 0xC0000034;
    private const int TokenPrivileges = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct LSA_OBJECT_ATTRIBUTES { public int Length; public IntPtr Root; public IntPtr Name; public uint Attributes; public IntPtr Sd; public IntPtr Sqos; }

    [StructLayout(LayoutKind.Sequential)]
    private struct LSA_UNICODE_STRING { public ushort Length; public ushort MaximumLength; public IntPtr Buffer; }

    [DllImport("advapi32.dll")] private static extern uint LsaOpenPolicy(IntPtr system, ref LSA_OBJECT_ATTRIBUTES oa, uint access, out IntPtr policy);
    [DllImport("advapi32.dll")] private static extern uint LsaEnumerateAccountRights(IntPtr policy, byte[] sid, out IntPtr rights, out uint count);
    [DllImport("advapi32.dll")] private static extern uint LsaFreeMemory(IntPtr buffer);
    [DllImport("advapi32.dll")] private static extern uint LsaClose(IntPtr policy);
    [DllImport("advapi32.dll")] private static extern int LsaNtStatusToWinError(uint status);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool LookupPrivilegeValue(string? system, string name, out long luid);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(IntPtr token, int cls, IntPtr info, int len, out int returned);
}
