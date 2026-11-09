// MainWindow.LaunchSessions.cs -- watches games launched from RHI and puts HDR / resolution back when
// they exit. Sessions are saved (LaunchSessionStore), so this still happens after a Refresh or after
// RHI was restarted while the game was running.
using RenoDXCommander.Services;

namespace RenoDXCommander;

public sealed partial class MainWindow
{
    // Without this, a game closed while RHI wasn't running for longer than this is just forgotten:
    // the user may have changed HDR / resolution themselves since.
    private static readonly TimeSpan StaleSessionAge = TimeSpan.FromHours(24);
    private static readonly TimeSpan GameStartTimeout = TimeSpan.FromSeconds(120);

    /// <summary>
    /// Saves the launch and watches the game. <paramref name="proc"/> is the started process for
    /// direct exe launches, null for store launches (the game process is searched for in its folder).
    /// </summary>
    private void MonitorLaunch(System.Diagnostics.Process? proc, LaunchSession session)
    {
        if (proc == null && string.IsNullOrEmpty(session.InstallPath))
        {
            _crashReporter.Log($"[MainWindow.MonitorLaunch] '{session.GameName}' has no install folder to watch — display changes won't be reverted on exit");
            return;
        }
        session.GameSeen = proc != null;
        LaunchSessionStore.Default.Add(session);
        _ = Task.Run(() => WatchLaunchAsync(session, proc, resumed: false));
    }

    /// <summary>
    /// Startup: picks up games launched from RHI before it was restarted. Still running = watched
    /// again; closed while RHI wasn't running = HDR / resolution put back now.
    /// </summary>
    private void ResumeLaunchSessions()
    {
        var sessions = LaunchSessionStore.Default.GetAll();
        if (sessions.Count == 0) return;
        _crashReporter.Log($"[MainWindow.ResumeLaunchSessions] {sessions.Count} saved launch(es): {string.Join(", ", sessions.Select(s => $"'{s.GameName}'"))}");
        foreach (var s in sessions)
            _ = Task.Run(() => WatchLaunchAsync(s, null, resumed: true));
    }

    private async Task WatchLaunchAsync(LaunchSession session, System.Diagnostics.Process? proc, bool resumed)
    {
        var gameName = session.GameName;
        bool counted = false;
        try
        {
            var roots = string.IsNullOrEmpty(session.InstallPath)
                ? new List<string>()
                : GameProcessSearchRoots(session.InstallPath);
            List<System.Diagnostics.Process> procs = new();

            if (proc != null)
            {
                // Direct exe launch: wait for it, then check whether it was a wrapper (SKSE, MO2)
                // that handed over to the real game in the same folder.
                SetLaunchRunning(session, true);
                ViewModel.NoteGameRunning(true);
                counted = true;
                await proc.WaitForExitAsync();
                if (roots.Count > 0)
                {
                    procs = await FindGameProcessesForAsync(roots, TimeSpan.FromSeconds(5));
                    if (procs.Count > 0)
                        _crashReporter.Log($"[MainWindow.MonitorProcess] '{gameName}' wrapper exited, found surviving process(es) {DescribeProcesses(procs)}");
                }
            }
            else
            {
                if (roots.Count == 0) { FinishLaunch(session, "no install folder"); return; }
                // Store launch, or a session saved before RHI restarted: find the game's process.
                // A session whose game was already seen gets a short look; one still starting
                // gets whatever is left of the start timeout.
                var wait = session.GameSeen
                    ? TimeSpan.FromSeconds(3)
                    : session.StartedUtc + GameStartTimeout - DateTime.UtcNow;
                if (wait < TimeSpan.FromSeconds(1)) wait = TimeSpan.FromSeconds(1);
                _crashReporter.Log($"[MainWindow.MonitorProcess] Polling for '{gameName}' in {string.Join(", ", roots.Select(r => $"'{r}'"))}...");
                procs = await FindGameProcessesForAsync(roots, wait);

                if (procs.Count == 0)
                {
                    if (resumed && session.GameSeen && DateTime.UtcNow - session.StartedUtc < StaleSessionAge)
                    {
                        // The game was closed while RHI wasn't running.
                        _crashReporter.Log($"[MainWindow.MonitorProcess] '{gameName}' was closed while RHI wasn't running — reverting display changes");
                        RevertLaunchChanges(session);
                    }
                    else
                    {
                        _crashReporter.Log($"[MainWindow.MonitorProcess] '{gameName}' — game process not found{(resumed ? " (saved launch dropped)" : $" after {GameStartTimeout.TotalSeconds:0}s")}");
                        LaunchSessionStore.Default.Remove(session.Id);
                    }
                    return;
                }

                LaunchSessionStore.Default.MarkSeen(session.Id);
                SetLaunchRunning(session, true);
                ViewModel.NoteGameRunning(true);
                counted = true;
                _crashReporter.Log($"[MainWindow.MonitorProcess] '{gameName}' — found {DescribeProcesses(procs)}, waiting for exit{(resumed ? " (resumed after RHI restart)" : "")}...");
            }

            if (procs.Count > 0) await WaitForGameExitAsync(roots, procs);
            RevertLaunchChanges(session);
            DispatcherQueue?.TryEnqueue(() => RefocusAfterGameExit(gameName));
        }
        catch (Exception ex)
        {
            // The session stays saved, so the next start of RHI checks the game again.
            SetLaunchRunning(session, false);
            _crashReporter.Log($"[MainWindow.MonitorProcess] '{gameName}' monitoring failed — {ex.Message}");
        }
        finally { if (counted) ViewModel.NoteGameRunning(false); }
    }

    /// <summary>Looks for the game's processes once a second until found or <paramref name="timeout"/> passes.</summary>
    private static async Task<List<System.Diagnostics.Process>> FindGameProcessesForAsync(List<string> roots, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        List<System.Diagnostics.Process> found = new();
        do
        {
            await Task.Delay(1000);
            try { found = FindGameProcesses(roots); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Process enumeration can fail transiently; the next pass retries.
                CrashReporter.Log($"[MainWindow.FindGameProcessesForAsync] Process scan failed — {ex.Message}");
            }
        } while (found.Count == 0 && DateTime.UtcNow < deadline);
        return found;
    }

    /// <summary>Puts HDR / resolution back (unless another game launched from RHI still needs them).</summary>
    private void RevertLaunchChanges(LaunchSession session)
    {
        var (revertHdr, revertRes) = LaunchSessionStore.Default.Finish(session.Id);
        if (revertHdr) HdrToggleService.DisableHdr(session.HdrTargets);
        if (revertRes)
        {
            var res = ResolutionToggleService.DisplayResolution.Parse(session.ResolutionToRestore);
            if (res != null) ResolutionToggleService.SetResolution(res);
            else ResolutionToggleService.RestoreResolution();
        }
        SetLaunchRunning(session, false);
        var kept = (session.HdrToggled && !revertHdr) || (session.ResToggled && !revertRes)
            ? " — another game launched from RHI is still running, so its display changes stay" : "";
        _crashReporter.Log($"[MainWindow.MonitorProcess] '{session.GameName}' exited{(revertHdr ? " — HDR disabled" : "")}{(revertRes ? " — resolution restored" : "")}{kept}");
    }

    private void FinishLaunch(LaunchSession session, string reason)
    {
        _crashReporter.Log($"[MainWindow.MonitorProcess] '{session.GameName}' — {reason}; saved launch dropped");
        LaunchSessionStore.Default.Remove(session.Id);
    }

    /// <summary>Marks the game's card(s) as running / not running. Looked up each time: Refresh replaces cards.</summary>
    private void SetLaunchRunning(LaunchSession session, bool running)
    {
        DispatcherQueue?.TryEnqueue(() =>
        {
            foreach (var card in ViewModel.AllCards)
                if (session.Matches(card.GameName, card.Source))
                    card.IsRunning = running;
        });
    }
}
