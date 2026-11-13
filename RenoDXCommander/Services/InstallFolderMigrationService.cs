// InstallFolderMigrationService.cs -- moves a game's RHI installs to its new install folder.
// The inventory (what RHI owns in the old folder, from each component's own tracking) is in
// InstallFolderMigrationService.Inventory.cs; the file moves are InstallFolderMover's.

using System.Runtime.InteropServices;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public partial class InstallFolderMigrationService : IInstallFolderMigrationService
{
    private readonly IAuxInstallService _aux;
    private readonly IModInstallService _mods;
    private readonly ILumaService _luma;
    private readonly IREFrameworkService _ref;
    private readonly IDxvkService _dxvk;

    public InstallFolderMigrationService(IAuxInstallService aux, IModInstallService mods, ILumaService luma,
        IREFrameworkService refService, IDxvkService dxvk)
    {
        _aux = aux;
        _mods = mods;
        _luma = luma;
        _ref = refService;
        _dxvk = dxvk;
    }

    public FolderMovePlan BuildPlan(FolderMigrationRequest request) => BuildPlanCore(request).Plan;

    public bool HasInstalls(FolderMigrationRequest request)
    {
        try { return !BuildPlan(request).IsEmpty; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[InstallFolderMigrationService.HasInstalls] '{request.GameName}' — {ex.Message}");
            return false;
        }
    }

    public FolderMigrationOutcome Migrate(FolderMigrationRequest request)
    {
        var outcome = new FolderMigrationOutcome();
        var oldDir = InstallFolderMover.Norm(request.OldDir);
        var newDir = InstallFolderMover.Norm(request.NewDir);
        CrashReporter.Log($"[InstallFolderMigrationService.Migrate] '{request.GameName}' '{oldDir}' → '{newDir}'");

        if (InstallFolderMover.SamePath(oldDir, newDir)) { outcome.NothingToMove = true; return outcome; }
        if (InstallFolderMover.IsWindowsApps(newDir) || InstallFolderMover.IsWindowsApps(oldDir))
        {
            outcome.Reason = "RHI can't change files in Game Pass / Microsoft Store (WindowsApps) folders.";
            CrashReporter.Log($"[InstallFolderMigrationService.Migrate] '{request.GameName}' skipped — WindowsApps folder");
            return outcome;
        }

        (FolderMovePlan plan, PlanContext ctx) built;
        try { built = BuildPlanCore(request); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            outcome.Reason = $"RHI couldn't read the old folder ({ex.Message}).";
            CrashReporter.Log($"[InstallFolderMigrationService.Migrate] '{request.GameName}' inventory failed — {ex.Message}");
            return outcome;
        }
        var (plan, ctx) = built;
        if (plan.IsEmpty)
        {
            outcome.NothingToMove = true;
            CrashReporter.Log($"[InstallFolderMigrationService.Migrate] '{request.GameName}' — nothing of RHI's in '{oldDir}'");
            return outcome;
        }

        var running = FindRunningProcess(new[] { request.GameRoot, oldDir, newDir });
        if (running != null)
        {
            outcome.Reason = "the game is running. Close it and try again.";
            CrashReporter.Log($"[InstallFolderMigrationService.Migrate] '{request.GameName}' skipped — '{running}' is running from the game folder");
            return outcome;
        }

        CrashReporter.Log($"[InstallFolderMigrationService.Migrate] '{request.GameName}' plan: "
            + string.Join("; ", plan.Groups.Where(g => g.Entries.Count > 0)
                .Select(g => $"{g.Name}{(g.NewDirWins ? " (new folder wins)" : "")}: {g.Entries.Count}")));

        var result = InstallFolderMover.Execute(plan);
        outcome.Result = result;
        if (!result.Success)
        {
            outcome.Reason = result.AbortReason;
            return outcome;
        }

        if (result.GroupsTouched.Count == 0 && plan.Writes.Count == 0 && plan.Deletes.Count == 0)
        {
            // Everything listed was left where it is (e.g. not provably RHI's): nothing changed
            outcome.NothingToMove = true;
            CrashReporter.Log($"[InstallFolderMigrationService.Migrate] '{request.GameName}' — nothing needed moving");
            return outcome;
        }

        RepointRecords(request, oldDir, newDir, ctx);

        outcome.Success = true;
        foreach (var g in plan.Groups.Where(g => result.GroupsTouched.Contains(g.Name)))
        {
            var label = g.Name.StartsWith(AddonGroupPrefix, StringComparison.Ordinal) ? "ReShade addons" : g.Name;
            if (!outcome.Components.Contains(label)) outcome.Components.Add(label);
        }
        outcome.MovedDlssFiles = result.Moved.Concat(result.Removed)
            .Any(p => Path.GetFileName(p).StartsWith("nvngx_", StringComparison.OrdinalIgnoreCase));
        CrashReporter.Log($"[InstallFolderMigrationService.Migrate] '{request.GameName}' done — {string.Join(", ", outcome.Components)}");
        return outcome;
    }

    /// <summary>Points every install record and the addon tracker at the new folder.</summary>
    private void RepointRecords(FolderMigrationRequest req, string oldDir, string newDir, PlanContext ctx)
    {
        void Try(string what, Action action)
        {
            try { action(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            { CrashReporter.Log($"[InstallFolderMigrationService.RepointRecords] '{req.GameName}' {what} failed — {ex.Message}"); }
        }

        foreach (var recordPath in ctx.RecordPaths)
        {
            var path = recordPath;
            Try("aux records", () => _aux.RepointRecords(req.GameName, path, newDir, r =>
                !(req.SkipReShade && (r.AddonType == AuxInstallService.TypeReShade || r.AddonType == AuxInstallService.TypeReShadeNormal))));
            if (!req.IsEmulator) Try("RenoDX record", () => _mods.RepointRecords(req.GameName, path, newDir));
            Try("Luma record", () => _luma.RepointRecords(req.GameName, path, newDir));
            Try("RE Framework record", () => _ref.RepointRecords(req.GameName, path, newDir));
            Try("DXVK record", () => _dxvk.RepointRecords(req.GameName, path, newDir));
        }

        Try("addon tracker", () =>
        {
            AddonPackService.RepointDeployments(oldDir, newDir);
            if (ctx.AddonsMovable && !InstallFolderMover.SamePath(ctx.OldAddonDir, oldDir))
                AddonPackService.RepointDeployments(ctx.OldAddonDir, ctx.NewAddonDir);
        });
    }

    // ── Running-game check ────────────────────────────────────────────────────

    /// <summary>Name of a process whose exe lives under one of the folders, or null.</summary>
    internal static string? FindRunningProcess(IEnumerable<string?> folders)
    {
        var roots = folders.Where(f => !string.IsNullOrEmpty(f) && f!.Length > 3)
            .Select(f => InstallFolderMover.Norm(f!) + "\\")
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (roots.Count == 0) return null;
        foreach (var p in System.Diagnostics.Process.GetProcesses())
        {
            using (p)
            {
                var path = GetProcessImagePath(p.Id);
                if (path != null && roots.Any(r => path.StartsWith(r, StringComparison.OrdinalIgnoreCase)))
                    return Path.GetFileName(path);
            }
        }
        return null;
    }

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageNameW(IntPtr hProcess, uint flags, System.Text.StringBuilder exeName, ref uint size);

    private static string? GetProcessImagePath(int pid)
    {
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null; // system / protected process
        try
        {
            var sb = new System.Text.StringBuilder(1024);
            uint size = (uint)sb.Capacity;
            return QueryFullProcessImageNameW(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally { NativeInterop.CloseHandle(h); }
    }
}
