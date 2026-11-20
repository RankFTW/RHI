// PclPermissionService.cs -- ReLimiter v4's native PC Latency (PCL) readings need the Windows account that runs the
// game to be in the built-in Performance Log Users group (S-1-5-32-559), which allows starting performance traces
// (ETW) without admin rights. Same change as ReLimiter's own pcl_permissions tool: add / remove the account's
// direct membership. The group is found by SID (its name is translated on non-English Windows). Changing it needs
// admin, so net.exe runs elevated (one Windows prompt; none when RHI already runs as admin). Reading it doesn't.
// A change only reaches the account's logon token after signing out of Windows and back in.

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace RenoDXCommander.Services;

internal static class PclPermissionService
{
    internal const string GroupSid = "S-1-5-32-559"; // BUILTIN\Performance Log Users

    /// <param name="DirectMember">The account is in the group (null = couldn't read the group).</param>
    /// <param name="InToken">The current sign-in already has the group (sign-out / in has happened since the change).</param>
    internal readonly record struct PclStatus(bool? DirectMember, bool InToken);

    /// <summary>Reads the current account's membership. Never throws. Doesn't need admin.</summary>
    internal static PclStatus GetStatus()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            bool inToken = new WindowsPrincipal(id).IsInRole(new SecurityIdentifier(GroupSid));
            return new PclStatus(id.User is { } user ? IsDirectMember(user) : null, inToken);
        }
        catch (Exception ex) when (ex is SystemException)
        {
            CrashReporter.Log($"[PclPermissionService.GetStatus] {ex.Message}");
            return new PclStatus(null, false);
        }
    }

    /// <summary>
    /// Adds (enable) or removes the current account. Shows one Windows admin prompt unless RHI runs as admin.
    /// Returns ok when the membership afterwards is what was asked for. Never throws.
    /// </summary>
    internal static async Task<(bool Ok, string? Error)> SetAsync(bool enable)
    {
        string action = enable ? "add" : "delete";
        try
        {
            string userName, groupName;
            SecurityIdentifier userSid;
            using (var id = WindowsIdentity.GetCurrent())
            {
                userSid = id.User ?? throw new InvalidOperationException("Couldn't read your Windows account.");
                userName = ((NTAccount)userSid.Translate(typeof(NTAccount))).Value;
            }
            groupName = ((NTAccount)new SecurityIdentifier(GroupSid).Translate(typeof(NTAccount))).Value;
            int slash = groupName.IndexOf('\\');
            if (slash >= 0) groupName = groupName[(slash + 1)..]; // "BUILTIN\Performance Log Users" → group name only

            bool elevated = VulkanLayerService.IsRunningAsAdmin();
            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "net.exe"))
            {
                // Account and group names can't contain double quotes
                Arguments = $"localgroup \"{groupName}\" \"{userName}\" /{action}",
                UseShellExecute = true,
                Verb = elevated ? "" : "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            CrashReporter.Log($"[PclPermissionService.SetAsync] {action} '{userName}' ({userSid.Value}) in '{groupName}' (elevated={elevated})");

            int exit;
            using (var p = Process.Start(psi) ?? throw new InvalidOperationException("Couldn't start net.exe."))
            {
                await p.WaitForExitAsync();
                exit = p.ExitCode;
            }

            // The group itself is the answer (net.exe also fails when it was already that way)
            bool? member = IsDirectMember(userSid);
            CrashReporter.Log($"[PclPermissionService.SetAsync] net.exe exit {exit}, member now {member?.ToString() ?? "unknown"}");
            if (member == enable) return (true, null);
            return (false, member == null
                ? "RHI couldn't check the Performance Log Users group afterwards."
                : $"Windows didn't make the change (net.exe exit code {exit}).");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED: the admin prompt was declined
        {
            CrashReporter.Log("[PclPermissionService.SetAsync] Admin prompt declined");
            return (false, "The Windows admin prompt was cancelled, so nothing changed.");
        }
        catch (Exception ex) when (ex is SystemException or InvalidOperationException)
        {
            CrashReporter.Log($"[PclPermissionService.SetAsync] {action} failed — {ex.Message}");
            return (false, ex.Message);
        }
    }

    /// <summary>Whether the account is listed in the group itself (not through another group). Null when the group can't be read.</summary>
    private static bool? IsDirectMember(SecurityIdentifier user)
    {
        string groupName;
        try
        {
            groupName = ((NTAccount)new SecurityIdentifier(GroupSid).Translate(typeof(NTAccount))).Value;
            int slash = groupName.IndexOf('\\');
            if (slash >= 0) groupName = groupName[(slash + 1)..];
        }
        catch (Exception ex) when (ex is SystemException)
        {
            CrashReporter.Log($"[PclPermissionService.IsDirectMember] Group name — {ex.Message}");
            return null;
        }

        IntPtr resume = IntPtr.Zero;
        int rc = NetLocalGroupGetMembers(null, groupName, 0, out var buf, MaxPreferredLength, out int read, out _, ref resume);
        try
        {
            if (rc != 0)
            {
                CrashReporter.Log($"[PclPermissionService.IsDirectMember] NetLocalGroupGetMembers returned {rc}");
                return null;
            }
            for (int i = 0; i < read; i++)
            {
                var sidPtr = Marshal.ReadIntPtr(buf, i * IntPtr.Size); // LOCALGROUP_MEMBERS_INFO_0 = one PSID
                if (sidPtr != IntPtr.Zero && new SecurityIdentifier(sidPtr).Equals(user)) return true;
            }
            return false;
        }
        finally
        {
            if (buf != IntPtr.Zero) NetApiBufferFree(buf);
        }
    }

    private const int MaxPreferredLength = -1;

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetLocalGroupGetMembers(string? serverName, string localGroupName, int level, out IntPtr bufPtr,
        int prefMaxLen, out int entriesRead, out int totalEntries, ref IntPtr resumeHandle);

    [DllImport("netapi32.dll")]
    private static extern int NetApiBufferFree(IntPtr buffer);
}
