// MainViewModel.Update.cs -- Update-all commands, update checking, UL caching, and version management.

using Microsoft.Extensions.DependencyInjection;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.ViewModels;

public partial class MainViewModel
{
    private System.Threading.Timer? _updateCheckTimer;
    private System.Threading.Timer? _heartbeatTimer;
    private volatile string _lastUiAction = "none";
    private readonly object _backgroundTimerLock = new();
    private volatile bool _backgroundStopped;
    private readonly CancellationTokenSource _backgroundLifetime = new();

    /// <summary>Native thread ID of the UI thread — captured once at startup for CPU sampling.</summary>
    internal uint UiThreadNativeId { get; set; }

    /// <summary>HWND of the main window — captured once at startup for pump-responsiveness probe.</summary>
    internal IntPtr MainWindowHwnd { get; set; }

    /// <summary>Prevents more than one stack capture per freeze event.</summary>
    private int _freezeStackCaptured; // 0 = not captured, 1 = captured; Interlocked

    /// <summary>Timestamp of the last DispatcherQueueTimer tick — for true freeze onset detection.</summary>
    private long _dispatcherTimerLastTickUtc; // DateTime.UtcNow.Ticks — read/write via Interlocked

    /// <summary>Held in a field to prevent GC collection — DispatcherQueueTimer is a COM object.</summary>
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _freezeDiagTimer;

    private System.Threading.Timer? _resourceLogTimer;

    /// <summary>Logs process resource counters — private bytes, GC heap, handles, GDI/USER objects, system memory.</summary>
    private static void LogResourceCounters(string context)
    {
        try
        {
            using var proc = System.Diagnostics.Process.GetCurrentProcess();
            proc.Refresh();
            long privateBytes  = proc.PrivateMemorySize64;
            long workingSet    = proc.WorkingSet64;
            long gcHeap        = GC.GetTotalMemory(false);
            int  handleCount   = proc.HandleCount;
            int  threadCount   = proc.Threads.Count;

            // GDI and USER objects — diagnostic for WinUI dispatcher failure from resource exhaustion
            var hProc = proc.Handle;
            uint gdiObj  = NativeInterop.GetGuiResources(hProc, NativeInterop.GR_GDIOBJECTS);
            uint userObj = NativeInterop.GetGuiResources(hProc, NativeInterop.GR_USEROBJECTS);

            // System memory
            var mem = new NativeInterop.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeInterop.MEMORYSTATUSEX>() };
            NativeInterop.GlobalMemoryStatusEx(ref mem);
            long availPhysMB   = (long)(mem.ullAvailPhys   / 1024 / 1024);
            long availPageMB   = (long)(mem.ullAvailPageFile / 1024 / 1024);
            uint memLoadPct    = mem.dwMemoryLoad;

            CrashReporter.LogSync(
                $"[Resources] {context} | " +
                $"Private={privateBytes/1024/1024}MB WS={workingSet/1024/1024}MB GC={gcHeap/1024/1024}MB | " +
                $"Handles={handleCount} Threads={threadCount} GDI={gdiObj} USER={userObj} | " +
                $"SysAvailPhys={availPhysMB}MB AvailPage={availPageMB}MB MemLoad={memLoadPct}%");
        }
        catch (Exception ex)
        {
            CrashReporter.LogSync($"[Resources] {context} — failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    internal void StopBackgroundWork()
    {
        lock (_backgroundTimerLock)
        {
            _backgroundStopped = true;
            _heartbeatTimer?.Dispose();
            _heartbeatTimer = null;
            _updateCheckTimer?.Dispose();
            _updateCheckTimer = null;
            _resourceLogTimer?.Dispose();
            _resourceLogTimer = null;
        }
        _backgroundLifetime.Cancel();
        PeriodicAppUpdateCheck = null;
        _autoUpdateService.Stop();
    }

    /// <summary>Tracks the last action dispatched to the UI thread for freeze diagnostics.</summary>
    internal void SetLastUiAction(string action)
    {
        _lastUiAction = action;
        var entry = $"[{DateTime.Now:HH:mm:ss.fff}] [UIAction] {action}";
        _crashReporter.Log($"[UIAction] {action}");
        CrashReporter.RecordUiAction(entry); // dedicated ring buffer, never contaminated by diagnostic output
    }

    /// <summary>
    /// Starts a 10-second heartbeat timer. On each tick it posts a quick probe to the UI thread.
    /// If the probe doesn't come back within 3 seconds, logs the last known UI action — that's
    /// what the UI thread was doing when it froze.
    /// </summary>
    internal void StartHeartbeatTimer()
    {
        lock (_backgroundTimerLock)
        {
            if (_backgroundStopped || _heartbeatTimer != null) return;

            // ── 5-minute resource counter timer ─────────────────────────────────
            // Logs private bytes, GC heap, handles, GDI/USER objects, system memory.
            // Runs independently of heartbeat so it fires even during long freezes.
            _resourceLogTimer = new System.Threading.Timer(_ =>
            {
                if (_backgroundStopped) return;
                LogResourceCounters("periodic-5min");
            }, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));

            // ── DispatcherQueueTimer last-tick tracker ───────────────────────────
            // A DispatcherQueueTimer fires on the UI thread via the dispatcher.
            // Tracking when it last ticked tells us the true onset of a stall:
            // if the timer stopped ticking before the heartbeat probe failed,
            // the dispatcher was already dead before the navigation.
            _dispatcherTimerLastTickUtc = DateTime.UtcNow.Ticks;
            DispatcherQueue?.TryEnqueue(() =>
            {
                _freezeDiagTimer = DispatcherQueue!.CreateTimer();
                _freezeDiagTimer.Interval = TimeSpan.FromSeconds(1);
                _freezeDiagTimer.IsRepeating = true;
                _freezeDiagTimer.Tick += (_, _) =>
                {
                    System.Threading.Interlocked.Exchange(ref _dispatcherTimerLastTickUtc, DateTime.UtcNow.Ticks);
                };
                _freezeDiagTimer.Start();
                CrashReporter.Log("[Heartbeat] DispatcherQueueTimer started successfully");
            });
            _heartbeatTimer = new System.Threading.Timer(async _ =>
            {
                if (_backgroundStopped) return;
                var probe = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (DispatcherQueue?.TryEnqueue(() => probe.TrySetResult(true)) != true) return;
                try
                {
                    await probe.Task.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                    if (!_backgroundStopped)
                    {
                        System.Threading.Interlocked.Exchange(ref _freezeStackCaptured, 0); // reset for next freeze
                        _crashReporter.Log($"[Heartbeat] UI responsive — last action: {_lastUiAction}");
                    }
                }
                catch (TimeoutException)
                {
                    if (_backgroundStopped) return;

                    // ── 1. Last N UI actions from breadcrumb ring buffer ────────────────
                    var recentActions = CrashReporter.GetRecentUiActions(20);

                    // ── 2. UI thread CPU delta (1-second sample) ────────────────────────
                    // Point 1: use a FRESH Process instance for the second sample.
                    // Process.Threads caches ProcessThread objects — reading TotalProcessorTime
                    // from the same instance before and after the delay returns ~0 (false IDLE).
                    // Point 4: await Task.Delay on the background timer thread, not the UI thread.
                    string cpuInfo = "unknown";
                    try
                    {
                        var uiNativeId = UiThreadNativeId;
                        if (uiNativeId > 0)
                        {
                            using var proc0 = System.Diagnostics.Process.GetCurrentProcess();
                            var thread0 = proc0.Threads.Cast<System.Diagnostics.ProcessThread>()
                                              .FirstOrDefault(t => t.Id == (int)uiNativeId);
                            var t0 = thread0?.TotalProcessorTime;

                            await Task.Delay(1000).ConfigureAwait(false); // stays on background thread

                            using var proc1 = System.Diagnostics.Process.GetCurrentProcess(); // fresh instance
                            var thread1 = proc1.Threads.Cast<System.Diagnostics.ProcessThread>()
                                              .FirstOrDefault(t => t.Id == (int)uiNativeId);
                            if (t0.HasValue && thread1 != null)
                            {
                                var delta = thread1.TotalProcessorTime - t0.Value;
                                cpuInfo = delta.TotalMilliseconds > 900
                                    ? $"PEGGED ({delta.TotalMilliseconds:F0}ms/1s — layout or compute loop)"
                                    : $"IDLE ({delta.TotalMilliseconds:F0}ms/1s — waiting on lock or native call)";
                            }
                            else
                            {
                                cpuInfo = "UI thread not found in process";
                            }
                        }
                        else
                        {
                            cpuInfo = "UI thread ID not captured";
                        }
                    }
                    catch (Exception ex) { cpuInfo = $"CPU sample failed: {ex.Message}"; }

                    // ── 3. Write the freeze lines synchronously so a Task Manager kill
                    //       doesn't lose them. CPU + actions first, stack second, so a
                    //       quick kill still leaves the most useful lines.
                    CrashReporter.LogSync($"[Heartbeat] *** UI FROZEN *** last action: {_lastUiAction} | UI thread CPU: {cpuInfo}");
                    if (recentActions.Count > 0)
                    {
                        // Cap the summary line at 4 KB — belt-and-suspenders against any future feedback path.
                        var summary = string.Join(" → ", recentActions);
                        if (summary.Length > 4096) summary = summary.Substring(0, 4096) + "… (truncated)";
                        CrashReporter.LogSync($"[Heartbeat] Recent UI actions: {summary}");
                    }

                    // ── 3b–5. Detailed diagnostics — once per stall only ─────────────────
                    // The UI FROZEN + CPU lines above fire every heartbeat tick so the log
                    // shows the full freeze duration. Everything below is expensive and
                    // verbose (thread stacks, RSP scan, module list) — gate it so it only
                    // runs once per freeze event, not every 10 seconds.
                    if (System.Threading.Interlocked.CompareExchange(ref _freezeStackCaptured, 1, 0) == 0)
                    {

                    // ── 3a. Dispatcher timer last-tick + resource counters ────────────────
                    // How long ago did the DispatcherQueueTimer last fire? That's the true onset.
                    var timerLastTick = new DateTime(System.Threading.Interlocked.Read(ref _dispatcherTimerLastTickUtc), DateTimeKind.Utc);
                    var timerAgo = DateTime.UtcNow - timerLastTick;
                    CrashReporter.LogSync($"[Heartbeat.Timer] DispatcherQueueTimer last ticked {timerAgo.TotalSeconds:F1}s ago (at {timerLastTick:HH:mm:ss.fff} UTC)");

                    LogResourceCounters("at-freeze");
                    // WaitReason requires ThreadState == Wait; any other state throws.
                    // LpcReceive (9) / LpcReply (10) = cross-process COM/RPC wait — points
                    // at a hung shell, overlay, or input-method server.
                    // Refresh() forces a re-snapshot of thread state — without it the
                    // Threads collection reports state from when the Process was created.
                    try
                    {
                        var uiNativeId = UiThreadNativeId;
                        if (uiNativeId > 0)
                        {
                            using var procWR = System.Diagnostics.Process.GetCurrentProcess();
                            procWR.Refresh(); // force fresh thread state snapshot
                            var uiThread = procWR.Threads
                                .Cast<System.Diagnostics.ProcessThread>()
                                .FirstOrDefault(t => t.Id == (int)uiNativeId);
                            if (uiThread != null)
                            {
                                var state = uiThread.ThreadState;
                                string waitInfo = state == System.Diagnostics.ThreadState.Wait
                                    ? $"WaitReason={uiThread.WaitReason} ({(int)uiThread.WaitReason})"
                                    : "N/A (not in Wait state)";
                                CrashReporter.LogSync($"[Heartbeat.Wait] UI thread state={state} | {waitInfo}");
                            }
                            else
                            {
                                CrashReporter.LogSync($"[Heartbeat.Wait] UI thread not found (OSId={uiNativeId})");
                            }
                        }
                        else
                        {
                            CrashReporter.LogSync("[Heartbeat.Wait] UiThreadNativeId not captured");
                        }
                    }
                    catch (Exception wrEx)
                    {
                        CrashReporter.LogSync($"[Heartbeat.Wait] ThreadState/WaitReason read failed: {wrEx.GetType().Name}: {wrEx.Message}");
                    }

                    // ── 3b2. Pump-responsiveness probe ────────────────────────────────────
                    // SendMessageTimeout(WM_NULL, 1s): returns immediately if the UI thread
                    // is in a message wait; times out if it's blocked in any other native wait.
                    // IsHungAppWindow: Windows 5s heuristic — independent check.
                    // Together these distinguish "pump idle" from "thread stuck".
                    try
                    {
                        var hwnd = MainWindowHwnd;
                        if (hwnd != IntPtr.Zero)
                        {
                            var smtResult = NativeInterop.SendMessageTimeoutW(
                                hwnd, NativeInterop.WM_NULL, IntPtr.Zero, IntPtr.Zero,
                                NativeInterop.SMTO_ABORTIFHUNG | NativeInterop.SMTO_BLOCK,
                                1000, out IntPtr _smtOut);
                            bool isHung = NativeInterop.IsHungAppWindow(hwnd);
                            string pumpState = smtResult != IntPtr.Zero
                                ? "responded (pump alive — thread is in a message wait or just resumed)"
                                : $"timed out (pump not responding — thread blocked in non-message wait) | IsHungAppWindow={isHung}";
                            CrashReporter.LogSync($"[Heartbeat.Pump] WM_NULL probe: {pumpState}");
                        }
                        else
                        {
                            CrashReporter.LogSync("[Heartbeat.Pump] MainWindowHwnd not captured");
                        }
                    }
                    catch (Exception pumpEx)
                    {
                        CrashReporter.LogSync($"[Heartbeat.Pump] Pump probe failed: {pumpEx.GetType().Name}: {pumpEx.Message}");
                    }

                    // ── 3c. Loaded modules ────────────────────────────────────────────────
                    // Two buckets:
                    //   GPU/driver — nvapi64, nvwgf2um*, amdxx*, atio*, ig7icd*, atig*, etc.
                    //                Live in System32 so the \Windows\ filter would hide them.
                    //                Logged with FileVersion and LastWriteTime: a driver bug or
                    //                stale driver is a prime candidate for a native GPU wait.
                    //   Third-party — anything outside \Windows\, \dotnet\, \Microsoft\,
                    //                 \WindowsApps\. Overlays, hooks, injected DLLs.
                    try
                    {
                        using var procMod = System.Diagnostics.Process.GetCurrentProcess();
                        var allModules = procMod.Modules.Cast<System.Diagnostics.ProcessModule>().ToList();

                        // GPU / driver DLLs — match by filename prefix regardless of path
                        static bool IsGpuDriver(string name)
                        {
                            var n = name.ToLowerInvariant();
                            return n.StartsWith("nvapi") || n.StartsWith("nvwgf2") || n.StartsWith("nvcuda")
                                || n.StartsWith("amdxx") || n.StartsWith("atio") || n.StartsWith("atig")
                                || n.StartsWith("ig7icd") || n.StartsWith("ig75icd") || n.StartsWith("igdusc")
                                || n.StartsWith("d3d12core") || n.StartsWith("dxgi")
                                || n.StartsWith("dxcore") || n.StartsWith("directml");
                        }

                        var gpuMods = allModules
                            .Where(m => IsGpuDriver(System.IO.Path.GetFileName(m.FileName ?? "")))
                            .Select(m =>
                            {
                                var fn = m.FileName ?? "";
                                string ver = "?", date = "?";
                                try { var fi = System.IO.File.Exists(fn)
                                    ? System.Diagnostics.FileVersionInfo.GetVersionInfo(fn) : null;
                                    ver = fi?.FileVersion ?? "?"; } catch { }
                                try { date = System.IO.File.GetLastWriteTime(fn).ToString("yyyy-MM-dd"); } catch { }
                                return $"{System.IO.Path.GetFileName(fn)} v{ver} ({date})";
                            })
                            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        if (gpuMods.Count > 0)
                            CrashReporter.LogSync($"[Heartbeat.Modules] GPU/driver DLLs ({gpuMods.Count}): {string.Join(", ", gpuMods)}");

                        // Third-party non-GPU modules
                        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                        var thirdParty = allModules
                            .Where(m =>
                            {
                                var p = m.FileName ?? "";
                                return !p.StartsWith(winDir, StringComparison.OrdinalIgnoreCase)
                                    && !p.Contains(@"\dotnet\", StringComparison.OrdinalIgnoreCase)
                                    && !p.Contains(@"\Microsoft\", StringComparison.OrdinalIgnoreCase)
                                    && !p.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase);
                            })
                            .Select(m => System.IO.Path.GetFileName(m.FileName ?? "?"))
                            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                            .ToList();

                        if (thirdParty.Count > 0)
                            CrashReporter.LogSync($"[Heartbeat.Modules] Third-party modules ({thirdParty.Count}): {string.Join(", ", thirdParty)}");
                        else
                            CrashReporter.LogSync("[Heartbeat.Modules] No third-party modules detected");
                    }
                    catch (Exception modEx)
                    {
                        CrashReporter.LogSync($"[Heartbeat.Modules] Module enumeration failed: {modEx.GetType().Name}: {modEx.Message}");
                    }

                    // ── 4. ClrMD stack capture ───────────────────────────────────────────
                    // ClrMD is loaded dynamically (not a static package reference) to avoid
                    // crashing the WinUI XAML compiler's type resolution during publish.
                    // Frame enumeration may return 0 frames in single-file publish without PDB;
                    // we log the count explicitly so the cause is visible in the log.
                    _ = Task.Run(() =>
                    {
                            try
                            {
                                var clrMdPath = System.IO.Path.Combine(
                                    System.IO.Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory,
                                    "Microsoft.Diagnostics.Runtime.dll");
                                if (!System.IO.File.Exists(clrMdPath))
                                {
                                    CrashReporter.LogSync($"[Heartbeat.Stack] ClrMD not found at '{clrMdPath}' — stack capture unavailable");
                                    return;
                                }

                                var clrMdAsm = System.Reflection.Assembly.LoadFrom(clrMdPath);

                                // Log the version actually loaded — helps confirm 3.x vs 4.x mismatch
                                var loadedVer = clrMdAsm.GetName().Version?.ToString() ?? "unknown";
                                CrashReporter.LogSync($"[Heartbeat.Stack] ClrMD loaded: {loadedVer} from '{clrMdPath}'");

                                var dataTargetType = clrMdAsm.GetType("Microsoft.Diagnostics.Runtime.DataTarget");
                                if (dataTargetType == null) { CrashReporter.LogSync("[Heartbeat.Stack] ClrMD: DataTarget type not found"); return; }

                                // DataTarget.CreateSnapshotAndAttach(pid)
                                var createSnapshot = dataTargetType.GetMethod("CreateSnapshotAndAttach",
                                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                                    null, new[] { typeof(int) }, null);
                                if (createSnapshot == null) { CrashReporter.LogSync("[Heartbeat.Stack] ClrMD: CreateSnapshotAndAttach not found"); return; }

                                using var target = (IDisposable)createSnapshot.Invoke(null, new object[] { Environment.ProcessId })!;

                                var clrVersionsProp = target.GetType().GetProperty("ClrVersions");
                                var clrVersions = clrVersionsProp?.GetValue(target) as System.Collections.IEnumerable;
                                object? firstVersion = null;
                                if (clrVersions != null)
                                    foreach (var v in clrVersions) { firstVersion = v; break; }
                                if (firstVersion == null) { CrashReporter.LogSync("[Heartbeat.Stack] ClrMD: no CLR runtime found in snapshot"); return; }

                                // Log the CLR version ClrMD found, and where it resolved the DAC from.
                                try
                                {
                                    // Framework-dependent publish: coreclr.dll is in the shared runtime folder.
                                    var coreclrPath = System.Diagnostics.Process.GetCurrentProcess().Modules
                                        .Cast<System.Diagnostics.ProcessModule>()
                                        .FirstOrDefault(m => m.ModuleName?.Equals("coreclr.dll", StringComparison.OrdinalIgnoreCase) == true)
                                        ?.FileName ?? "not found";
                                    CrashReporter.LogSync($"[Heartbeat.Stack] coreclr.dll loaded from: '{coreclrPath}'");

                                    var clrVerProp = firstVersion.GetType().GetProperty("Version");
                                    var clrVer = clrVerProp?.GetValue(firstVersion)?.ToString() ?? "unknown";
                                    CrashReporter.LogSync($"[Heartbeat.Stack] CLR runtime version in snapshot: {clrVer}");

                                    // DebuggingLibraries is the 3.x property — enumerate to find the DAC entry
                                    var debugLibsProp = firstVersion.GetType().GetProperty("DebuggingLibraries");
                                    var debugLibs = debugLibsProp?.GetValue(firstVersion) as System.Collections.IEnumerable;
                                    var dacPaths = new System.Text.StringBuilder();
                                    if (debugLibs != null)
                                    {
                                        foreach (var lib in debugLibs)
                                        {
                                            var kind = lib.GetType().GetProperty("Kind")?.GetValue(lib)?.ToString() ?? "?";
                                            var fname = lib.GetType().GetProperty("FileName")?.GetValue(lib)?.ToString() ?? "?";
                                            dacPaths.Append($" [{kind}:{fname}]");
                                        }
                                    }
                                    CrashReporter.LogSync($"[Heartbeat.Stack] DebuggingLibraries:{(dacPaths.Length > 0 ? dacPaths.ToString() : " (empty)")}");

                                    var exeDir = System.IO.Path.GetDirectoryName(Environment.ProcessPath) ?? "";
                                    var dacBesideExe = System.IO.Path.Combine(exeDir, "mscordaccore.dll");
                                    CrashReporter.LogSync($"[Heartbeat.Stack] mscordaccore.dll beside exe: {System.IO.File.Exists(dacBesideExe)}");
                                }
                                catch (Exception dacEx)
                                {
                                    CrashReporter.LogSync($"[Heartbeat.Stack] DAC info lookup threw: {dacEx.GetType().Name}: {dacEx.Message}");
                                }

                                var createRuntime = firstVersion.GetType().GetMethod("CreateRuntime", Type.EmptyTypes);
                                var runtime = createRuntime?.Invoke(firstVersion, null);
                                if (runtime == null) { CrashReporter.LogSync("[Heartbeat.Stack] ClrMD: CreateRuntime returned null"); return; }

                                var threadsProp = runtime.GetType().GetProperty("Threads");
                                var threads = threadsProp?.GetValue(runtime) as System.Collections.IEnumerable;

                                var sb = new System.Text.StringBuilder();
                                sb.AppendLine("[Heartbeat.Stack] Managed thread stacks at time of freeze:");
                                int threadCount = 0;
                                if (threads != null)
                                {
                                    foreach (var thread in threads)
                                    {
                                        threadCount++;
                                        var osId      = (uint)(thread.GetType().GetProperty("OSThreadId")?.GetValue(thread) ?? 0u);
                                        var managedId = (int)(thread.GetType().GetProperty("ManagedThreadId")?.GetValue(thread) ?? 0);
                                        bool isUiThread = UiThreadNativeId > 0 && osId == UiThreadNativeId;
                                        sb.AppendLine($"  Thread OSId={osId}{(isUiThread ? " <-- UI THREAD" : "")} ManagedId={managedId}");

                                        // EnumerateStackTrace in ClrMD 3.x takes optional parameters:
                                        // EnumerateStackTrace(bool includeContext, int maxFrames)
                                        // GetMethod with Type.EmptyTypes finds neither overload — use name-only
                                        // lookup and pass includeContext=true explicitly.
                                        try
                                        {
                                            var enumStackTrace = thread.GetType().GetMethod("EnumerateStackTrace",
                                                new[] { typeof(bool) });
                                            var frames = enumStackTrace?.Invoke(thread, new object[] { true })
                                                         as System.Collections.IEnumerable;
                                            int frameCount = 0;
                                            if (frames != null)
                                            {
                                                foreach (var frame in frames)
                                                {
                                                    sb.AppendLine($"    {frame}");
                                                    if (++frameCount >= 40) { sb.AppendLine("    ... (truncated)"); break; }
                                                }
                                            }
                                            if (frameCount == 0)
                                                sb.AppendLine($"    (0 managed frames — EnumerateStackTrace returned empty{(enumStackTrace == null ? "; method not found on type" : "")})");
                                        }
                                        catch (Exception frameEx)
                                        {
                                            sb.AppendLine($"    (frame enumeration threw: {frameEx.GetType().Name}: {frameEx.Message})");
                                        }
                                    }
                                }
                                sb.AppendLine($"[Heartbeat.Stack] Total threads enumerated: {threadCount}");
                                CrashReporter.LogSync(sb.ToString());
                            }
                            catch (Exception ex)
                            {
                                CrashReporter.LogSync($"[Heartbeat.Stack] ClrMD capture failed: {ex.GetType().Name}: {ex.Message}");
                            }
                        });

                        // ── 4b. Native stack capture — runs for all users ────────────────
                        // Captures the UI thread's native call stack using SuspendThread +
                        // GetThreadContext + ResumeThread. Walk is top-frame-only (safe).
                        // RtlVirtualUnwind crashes on JIT frames — disabled until a safe
                        // unwinder is available. Top frame alone distinguishes message-wait
                        // (NtUserGetMessage) from a blocked wait (NtWaitForSingleObject etc).
                        _ = Task.Run(CaptureNativeUiThreadStack);

                        // ── 4c. Multi-priority dispatcher probes ─────────────────────────────
                        // Enqueue probes at High, Normal and Low. Log which ones complete
                        // within 2 seconds. If High/Normal run but Low doesn't, only the
                        // low-priority queue is starved. If none run, the dispatcher is wedged.
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var probeHigh   = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                                var probeNormal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                                var probeLow    = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                                bool qHigh   = DispatcherQueue?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.High,   () => probeHigh.TrySetResult(true))   == true;
                                bool qNormal = DispatcherQueue?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () => probeNormal.TrySetResult(true)) == true;
                                bool qLow    = DispatcherQueue?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,    () => probeLow.TrySetResult(true))    == true;

                                CrashReporter.LogSync($"[Heartbeat.Probes] Enqueued: High={qHigh} Normal={qNormal} Low={qLow} — waiting 2s for results...");

                                bool rHigh = false, rNormal = false, rLow = false;
                                try { await probeHigh.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);   rHigh   = true; } catch (TimeoutException) { }
                                try { await probeNormal.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); rNormal = true; } catch (TimeoutException) { }
                                try { await probeLow.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);    rLow    = true; } catch (TimeoutException) { }

                                CrashReporter.LogSync($"[Heartbeat.Probes] Completed: High={rHigh} Normal={rNormal} Low={rLow}");
                            }
                            catch (Exception ex)
                            {
                                CrashReporter.LogSync($"[Heartbeat.Probes] Multi-priority probe failed: {ex.GetType().Name}: {ex.Message}");
                            }
                        });

                        // ── 5. Minidump fallback — dev only (unlock.txt), fires ~10s after freeze ──
                        // Writes a full-memory dump to %LocalAppData%\RHI\freeze_dump.dmp.
                        // Not written for regular users — nearly 1 GB and contains process memory.
                        if (DevUnlockService.IsUnlocked)
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await Task.Delay(10_000).ConfigureAwait(false);
                                if (_backgroundStopped) return;

                                // Re-probe: only dump if still frozen (probe doesn't come back in 1s)
                                var reProbe = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                                if (DispatcherQueue?.TryEnqueue(() => reProbe.TrySetResult(true)) != true) return;
                                bool stillFrozen;
                                try   { await reProbe.Task.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); stillFrozen = false; }
                                catch (TimeoutException) { stillFrozen = true; }

                                if (!stillFrozen)
                                {
                                    CrashReporter.LogSync("[Heartbeat.Dump] UI recovered before 10s — skipping minidump");
                                    return;
                                }

                                var dumpDir  = System.IO.Path.Combine(
                                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                    "RHI");
                                System.IO.Directory.CreateDirectory(dumpDir);
                                var dumpPath = System.IO.Path.Combine(dumpDir, "freeze_dump.dmp");
                                var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC");

                                CrashReporter.LogSync($"[Heartbeat.Dump] Writing minidump at {timestamp} to '{dumpPath}'...");

                                var hProcess = System.Diagnostics.Process.GetCurrentProcess().Handle;
                                var pid = (uint)Environment.ProcessId;

                                // Capture a snapshot with VA clone + threads + thread contexts so the dump
                                // contains usable stacks. Pass the snapshot handle as the process handle
                                // to MiniDumpWriteDump — this avoids suspending the calling thread.
                                const NativeInterop.PssCaptureFlags captureFlags =
                                    NativeInterop.PssCaptureFlags.PSS_CAPTURE_VA_CLONE         |
                                    NativeInterop.PssCaptureFlags.PSS_CAPTURE_HANDLES          |
                                    NativeInterop.PssCaptureFlags.PSS_CAPTURE_HANDLE_BASIC_INFORMATION |
                                    NativeInterop.PssCaptureFlags.PSS_CAPTURE_THREADS          |
                                    NativeInterop.PssCaptureFlags.PSS_CAPTURE_THREAD_CONTEXT   |
                                    NativeInterop.PssCaptureFlags.PSS_CREATE_BREAKAWAY_OPTIONAL;

                                IntPtr snapshotHandle = IntPtr.Zero;
                                uint pssErr = NativeInterop.PssCaptureSnapshot(
                                    hProcess, captureFlags, NativeInterop.CONTEXT_ALL_X64, out snapshotHandle);

                                if (pssErr != 0)
                                {
                                    CrashReporter.LogSync($"[Heartbeat.Dump] PssCaptureSnapshot failed (error {pssErr}) — falling back to direct dump");
                                    snapshotHandle = IntPtr.Zero;
                                }

                                try
                                {
                                    using var fs = new System.IO.FileStream(dumpPath, System.IO.FileMode.Create,
                                        System.IO.FileAccess.ReadWrite, System.IO.FileShare.None);

                                    // If snapshot succeeded, pass snapshotHandle as the process handle;
                                    // otherwise fall back to the live process handle (less safe but better than nothing).
                                    var dumpHandle = snapshotHandle != IntPtr.Zero ? snapshotHandle : hProcess;
                                    bool ok = NativeInterop.MiniDumpWriteDump(
                                        dumpHandle, pid,
                                        fs.SafeFileHandle.DangerousGetHandle(),
                                        NativeInterop.MiniDumpType.MiniDumpWithFullMemory |
                                        NativeInterop.MiniDumpType.MiniDumpWithThreadInfo,
                                        IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

                                    if (ok)
                                        CrashReporter.LogSync($"[Heartbeat.Dump] Minidump written at {timestamp} ({new System.IO.FileInfo(dumpPath).Length / 1024 / 1024} MB) — '{dumpPath}'");
                                    else
                                        CrashReporter.LogSync($"[Heartbeat.Dump] MiniDumpWriteDump failed — Win32 error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
                                }
                                finally
                                {
                                    if (snapshotHandle != IntPtr.Zero)
                                        NativeInterop.PssFreeSnapshot(hProcess, snapshotHandle);
                                }
                            }
                            catch (Exception ex)
                            {
                                CrashReporter.LogSync($"[Heartbeat.Dump] Minidump failed: {ex.GetType().Name}: {ex.Message}");
                            }
                        });
                    } // end once-per-stall diagnostics gate

                        // ── 6. Auto-restart after 30s of confirmed total dispatcher death ──────
                        // Only fires when ALL three priority probes (High, Normal, Low) failed.
                        // Loop guard: reads/writes %LocalAppData%\RHI\restart_log.txt (timestamps).
                        // Max 2 restarts in any 5-minute window — if exceeded, logs and gives up.
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await Task.Delay(30_000).ConfigureAwait(false);
                                if (_backgroundStopped) return;

                                // Confirm all three priorities are still dead
                                var h2 = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                                var n2 = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                                var l2 = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                                DispatcherQueue?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.High,   () => h2.TrySetResult(true));
                                DispatcherQueue?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () => n2.TrySetResult(true));
                                DispatcherQueue?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,    () => l2.TrySetResult(true));
                                bool hOk = false, nOk = false, lOk = false;
                                try { await h2.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); hOk = true; } catch (TimeoutException) { }
                                try { await n2.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); nOk = true; } catch (TimeoutException) { }
                                try { await l2.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); lOk = true; } catch (TimeoutException) { }

                                if (hOk || nOk || lOk)
                                {
                                    CrashReporter.LogSync($"[Heartbeat.Restart] Dispatcher recovered after 30s (High={hOk} Normal={nOk} Low={lOk}) — restart cancelled");
                                    return;
                                }

                                // Loop guard: count restarts in the last 5 minutes
                                var restartLogPath = System.IO.Path.Combine(
                                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                    "RHI", "restart_log.txt");
                                var now = DateTime.UtcNow;
                                var window = now.AddMinutes(-5);
                                var recentRestarts = 0;
                                try
                                {
                                    if (System.IO.File.Exists(restartLogPath))
                                    {
                                        var lines = System.IO.File.ReadAllLines(restartLogPath);
                                        recentRestarts = lines.Count(l =>
                                            DateTime.TryParse(l, System.Globalization.CultureInfo.InvariantCulture,
                                                System.Globalization.DateTimeStyles.AssumeUniversal, out var t) && t > window);
                                    }
                                }
                                catch { }

                                if (recentRestarts >= 2)
                                {
                                    CrashReporter.LogSync($"[Heartbeat.Restart] Loop guard triggered — {recentRestarts} restarts in the last 5 minutes. Not restarting.");
                                    return;
                                }

                                // Record this restart
                                try { System.IO.File.AppendAllText(restartLogPath, now.ToString("O") + Environment.NewLine); }
                                catch { }

                                CrashReporter.LogSync($"[Heartbeat.Restart] All dispatcher priorities unresponsive after 30s — restarting RHI (restart #{recentRestarts + 1} in 5-min window)");
                                CrashReporter.Shutdown();
                                var exePath = Environment.ProcessPath;
                                if (!string.IsNullOrEmpty(exePath))
                                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exePath) { UseShellExecute = true });
                                Environment.Exit(2); // exit code 2 = auto-restart
                            }
                            catch (Exception ex)
                            {
                                CrashReporter.LogSync($"[Heartbeat.Restart] Auto-restart failed: {ex.GetType().Name}: {ex.Message}");
                            }
                        });
                }
            }, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        }
    }

    // DbgHelp is single-threaded — serialize SymFromAddr calls.
    private static readonly object _nativeStackLock = new();

    internal void CaptureNativeUiThreadStackPublic() => CaptureNativeUiThreadStack();

    /// <summary>
    /// Captures a native call stack of the UI thread using StackWalk64.
    /// Safe to call from a background thread. The suspend window is minimal:
    /// SuspendThread → GetThreadContext (copies CONTEXT blob) → ResumeThread.
    /// All further work (stack walking, symbol resolution) happens after resume.
    ///
    /// Alignment: x64 CONTEXT requires 16-byte alignment. We allocate SIZE+16 extra
    /// bytes and align the pointer manually — stackalloc alone does not guarantee it.
    /// </summary>
    private unsafe void CaptureNativeUiThreadStack()
    {
        var uiNativeId = UiThreadNativeId;
        if (uiNativeId == 0) { CrashReporter.LogSync("[Heartbeat.Native] UiThreadNativeId not captured"); return; }

        IntPtr hThread = IntPtr.Zero;
        try
        {
            hThread = NativeInterop.OpenThread(
                NativeInterop.THREAD_SUSPEND_RESUME | NativeInterop.THREAD_GET_CONTEXT | NativeInterop.THREAD_QUERY_INFORMATION,
                false, uiNativeId);
            if (hThread == IntPtr.Zero)
            {
                CrashReporter.LogSync($"[Heartbeat.Native] OpenThread failed (error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()})");
                return;
            }

            // Allocate CONTEXT + 16 extra bytes for alignment, then align to 16-byte boundary.
            // stackalloc guarantees no heap allocation — zero-init via Unsafe.InitBlock.
            // CONTEXT_X64_SIZE = 1232 bytes; with 16 extra the aligned pointer is always valid.
            byte* ctxRaw = stackalloc byte[NativeInterop.CONTEXT_X64_SIZE + 16];
            // Align up to next 16-byte boundary
            var rawAddr = (ulong)ctxRaw;
            var alignedAddr = (rawAddr + 15UL) & ~15UL;
            byte* ctxBuf = (byte*)alignedAddr;
            System.Runtime.CompilerServices.Unsafe.InitBlock(ctxBuf, 0, (uint)NativeInterop.CONTEXT_X64_SIZE);
            // ContextFlags at byte offset 48 in x64 CONTEXT — CONTEXT_FULL = 0x10007
            *(uint*)(ctxBuf + 48) = 0x00010007u;
            var ctxPtr = (IntPtr)ctxBuf;

            // ── SUSPEND WINDOW: zero managed allocations ────────────────────────
            // Only GetThreadContext runs inside the window — no module lookups,
            // no locks, no allocations. GetModuleHandleExW may take the loader
            // lock; calling it while the UI thread is suspended could deadlock
            // if the UI thread holds that lock (e.g. during a DLL load freeze).
            // All module resolution and name lookups run after ResumeThread.
            var suspendSw = System.Diagnostics.Stopwatch.StartNew();
            NativeInterop.SuspendThread(hThread);
            bool ctxOk;
            try
            {
                ctxOk = NativeInterop.GetThreadContext(hThread, ctxPtr);
            }
            finally
            {
                // ResumeThread is ALWAYS called — even if GetThreadContext throws.
                // Safety limit: if we somehow reach here after 200ms, resume anyway.
                NativeInterop.ResumeThread(hThread);
                suspendSw.Stop();
                if (suspendSw.ElapsedMilliseconds > 200)
                    CrashReporter.LogSync($"[Heartbeat.Native] WARNING: suspend window took {suspendSw.ElapsedMilliseconds}ms — possible deadlock risk");
            }
            // ── END SUSPEND WINDOW ──────────────────────────────────────────────

            if (!ctxOk)
            {
                int err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                CrashReporter.LogSync($"[Heartbeat.Native] GetThreadContext failed — Win32 error {err} (998=misalignment, 5=access denied)");
                return;
            }

            // Extract Rip (offset 248), Rsp (offset 152), Rbp (offset 160) from x64 CONTEXT
            ulong rip = *(ulong*)(ctxBuf + 248);
            ulong rsp = *(ulong*)(ctxBuf + 152);
            ulong rbp = *(ulong*)(ctxBuf + 160);
            CrashReporter.LogSync($"[Heartbeat.Native] Context captured: RIP=0x{rip:X16} RSP=0x{rsp:X16} RBP=0x{rbp:X16}");

            var frames   = new ulong[1]; // top frame only — walk is disabled pending stability fix
            var imgBases = new ulong[1];
            int frameCount = 1;
            frames[0]   = rip;

            // Resolve module for the top frame via GetModuleHandleExW(FROM_ADDRESS)
            if (NativeInterop.GetModuleHandleExW(
                    NativeInterop.GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                    NativeInterop.GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                    (IntPtr)(long)rip, out IntPtr _hmodTop) && _hmodTop != IntPtr.Zero)
            {
                imgBases[0] = (ulong)(long)_hmodTop;
                // Prefer the image base from RtlLookupFunctionEntry (more precise than module handle)
                NativeInterop.RtlLookupFunctionEntry(rip, out ulong imgBaseTop, IntPtr.Zero);
                if (imgBaseTop != 0) imgBases[0] = imgBaseTop;
            }

            CrashReporter.LogSync("[Heartbeat.Native] Walk: top-frame-only mode (RtlVirtualUnwind disabled — crashes on managed frames)");

            // Resolve frames: use GetModuleHandleExW(FROM_ADDRESS) for accurate module lookup
            // (doesn't miss KnownDlls or late-loaded modules like Process.Modules can).
            // SymFromAddr still attempted first; PE export fallback handles the rest.
            var hProcess2 = System.Diagnostics.Process.GetCurrentProcess().Handle;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[Heartbeat.Native] Native stack ({frameCount} frames):");

            // Pre-allocate SYMBOL_INFO on the stack
            NativeInterop.SYMBOL_INFO symInfo;
            symInfo.SizeOfStruct = (uint)sizeof(NativeInterop.SYMBOL_INFO);
            symInfo.MaxNameLen   = NativeInterop.MAX_SYM_NAME;

            // Track which modules we've already logged version info for
            var loggedModules = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < frameCount; i++)
            {
                var addr = frames[i];

                // Resolve module from address — accurate even for KnownDlls / late-loaded modules
                string modName     = "?";
                string modFullPath = "";
                ulong  modBase     = imgBases[i]; // from RtlLookupFunctionEntry

                // GetModuleHandleExW(FROM_ADDRESS) for name — doesn't increment refcount
                if (NativeInterop.GetModuleHandleExW(
                        NativeInterop.GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                        NativeInterop.GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                        (IntPtr)addr, out IntPtr hMod) && hMod != IntPtr.Zero)
                {
                    var sb2 = new System.Text.StringBuilder(512);
                    if (NativeInterop.GetModuleFileNameW(hMod, sb2, (uint)sb2.Capacity) > 0)
                    {
                        modFullPath = sb2.ToString();
                        modName     = System.IO.Path.GetFileName(modFullPath);
                        // Sync imgBase from module handle if RtlLookupFunctionEntry returned 0
                        if (modBase == 0) modBase = (ulong)hMod.ToInt64();
                    }
                }

                // Log PE version + TimeDateStamp + SizeOfImage once per unique module
                if (modName != "?" && loggedModules.Add(modName))
                {
                    try
                    {
                        string fileVer = "?";
                        try { fileVer = System.Diagnostics.FileVersionInfo
                            .GetVersionInfo(modFullPath).FileVersion ?? "?"; } catch { }

                        uint ts = 0, soi = 0;
                        try
                        {
                            var basePtr = (byte*)modBase;
                            int lfanew  = *(int*)(basePtr + 0x3C);
                            ts  = *(uint*)(basePtr + lfanew + 0x08);
                            soi = *(uint*)(basePtr + lfanew + 0x50);
                        }
                        catch { }

                        sb.AppendLine($"       [{modName}  ver={fileVer}  ts=0x{ts:X8}  soi=0x{soi:X}]");
                    }
                    catch { }
                }

                // Try SymFromAddr first
                string frameName;
                ulong displacement = 0;
                System.Runtime.CompilerServices.Unsafe.InitBlock(symInfo.Name, 0, (uint)(NativeInterop.MAX_SYM_NAME * 2));
                bool symOk;
                lock (_nativeStackLock)
                {
                    symOk = NativeInterop.SymFromAddr(hProcess2, addr, out displacement, &symInfo);
                }

                if (symOk && symInfo.NameLen > 0)
                {
                    var name = new string(symInfo.Name, 0, (int)symInfo.NameLen);
                    frameName = $"{name}+0x{displacement:X}";
                }
                else if (modBase != 0)
                {
                    // PE export table fallback: find nearest exported name at or below addr.
                    string? exportName = null;
                    uint    exportDisp = 0;
                    try
                    {
                        var basePtr = (byte*)modBase;
                        var rva     = (uint)(addr - modBase);
                        int lfanew  = *(int*)(basePtr + 0x3C);
                        uint expDirRva  = *(uint*)(basePtr + lfanew + 0x88);
                        uint expDirSize = *(uint*)(basePtr + lfanew + 0x8C);
                        if (expDirRva != 0 && expDirSize != 0)
                        {
                            var expDir  = (uint*)(basePtr + expDirRva);
                            uint numFuncs = expDir[5];
                            uint numNames = expDir[6];
                            uint* funcs   = (uint*)(basePtr + expDir[7]);
                            uint* names   = (uint*)(basePtr + expDir[8]);
                            ushort* ords  = (ushort*)(basePtr + expDir[9]);
                            uint bestRva = 0;
                            uint bestIdx = uint.MaxValue;
                            for (uint n = 0; n < numNames; n++)
                            {
                                ushort ord  = ords[n];
                                if (ord >= numFuncs) continue;
                                uint funcRva = funcs[ord];
                                if (funcRva >= expDirRva && funcRva < expDirRva + expDirSize) continue;
                                if (funcRva <= rva && funcRva > bestRva) { bestRva = funcRva; bestIdx = n; }
                            }
                            if (bestIdx != uint.MaxValue)
                            {
                                exportName = new string((sbyte*)(basePtr + names[bestIdx]));
                                exportDisp = rva - bestRva;
                            }
                        }
                    }
                    catch { }

                    frameName = exportName != null
                        ? $"{exportName}+0x{exportDisp:X}"
                        : (modName != "?" ? $"{modName}+0x{addr - modBase:X}" : $"0x{addr:X16}");
                }
                else
                {
                    frameName = $"0x{addr:X16}";
                }

                sb.AppendLine($"  {i,2}: {frameName}");
            }
            if (frameCount == 0)
                sb.AppendLine("  (no frames captured — thread may be in kernel wait or context was invalid)");
            CrashReporter.LogSync(sb.ToString());

            // ── RSP stack scan — heuristic return-address search ─────────────────
            // ReadProcessMemory copies the stack while the thread is already resumed.
            // Uses VirtualQuery to clamp the read to committed memory (avoids guard pages).
            // Uses bytes-actually-read even on partial copy (error 299) so shallow stacks work.
            // Export-offset threshold: if the nearest export is > 0x1000 bytes away, prints
            // "module+0xRVA (no nearby export)" instead of a misleading name.
            // All module resolution happens here, post-resume — no loader lock risk.
            if (rsp != 0)
            {
                try
                {
                    const int MaxScanBytes = 12 * 1024; // 12 KB ceiling
                    const int MaxHits      = 10;
                    const uint ExportOffsetThreshold = 0x1000; // max offset before name is misleading

                    // Clamp read to committed memory region starting at RSP
                    int scanBytes = MaxScanBytes;
                    if (NativeInterop.VirtualQuery((IntPtr)(long)rsp,
                            out NativeInterop.MEMORY_BASIC_INFORMATION mbi,
                            (UIntPtr)System.Runtime.InteropServices.Marshal.SizeOf<NativeInterop.MEMORY_BASIC_INFORMATION>()) != UIntPtr.Zero
                        && mbi.State == 0x1000 /* MEM_COMMIT */)
                    {
                        ulong regionEnd  = (ulong)(long)mbi.BaseAddress + (ulong)mbi.RegionSize;
                        ulong available  = regionEnd > rsp ? regionEnd - rsp : 0;
                        if (available < (ulong)MaxScanBytes)
                            scanBytes = (int)available;
                    }

                    if (scanBytes < 8) goto skipScan;

                    var stackCopy = System.Runtime.InteropServices.Marshal.AllocHGlobal(scanBytes);
                    try
                    {
                        var hProc = System.Diagnostics.Process.GetCurrentProcess().Handle;
                        bool readOk = NativeInterop.ReadProcessMemory(
                            hProc, (IntPtr)(long)rsp, stackCopy,
                            (UIntPtr)scanBytes, out UIntPtr bytesRead);

                        int actualBytes = (int)bytesRead;

                        // Use bytes actually read even on partial copy (error 299 = partial)
                        if (!readOk && actualBytes < 8)
                        {
                            int err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                            CrashReporter.LogSync($"[Heartbeat.Scan] ReadProcessMemory failed (error {err}, bytesRead={actualBytes})");
                        }
                        else
                        {
                            var scanSb = new System.Text.StringBuilder();
                            scanSb.AppendLine($"[Heartbeat.Scan] Probable return addresses (heuristic, {actualBytes} bytes scanned, may include stale values):");
                            int hits = 0;
                            unsafe
                            {
                                byte* buf = (byte*)stackCopy;
                                for (int off = 0; off + 8 <= actualBytes && hits < MaxHits; off += 8)
                                {
                                    ulong candidate = *(ulong*)(buf + off);
                                    if (candidate < 0x10000UL || candidate > 0x7FFF_FFFF_FFFFUL) continue;

                                    // Call-prefix filter: E8 at candidate-5 (near call)
                                    // or FF at candidate-6 (indirect call)
                                    bool likelyReturn = false;
                                    try
                                    {
                                        byte prefix = 0;
                                        UIntPtr nRead;
                                        if (NativeInterop.ReadProcessMemory(hProc,
                                            (IntPtr)(long)(candidate - 5), (IntPtr)(&prefix), (UIntPtr)1, out nRead)
                                            && nRead == (UIntPtr)1 && prefix == 0xE8)
                                            likelyReturn = true;
                                        else if (!likelyReturn && NativeInterop.ReadProcessMemory(hProc,
                                            (IntPtr)(long)(candidate - 6), (IntPtr)(&prefix), (UIntPtr)1, out nRead)
                                            && nRead == (UIntPtr)1 && prefix == 0xFF)
                                            likelyReturn = true;
                                    }
                                    catch { likelyReturn = true; }

                                    if (!likelyReturn) continue;

                                    if (!NativeInterop.GetModuleHandleExW(
                                            NativeInterop.GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                                            NativeInterop.GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                                            (IntPtr)(long)candidate, out IntPtr hScanMod) || hScanMod == IntPtr.Zero)
                                        continue;

                                    string scanModName = "?";
                                    ulong  scanModBase = (ulong)(long)hScanMod;
                                    var sb3 = new System.Text.StringBuilder(512);
                                    if (NativeInterop.GetModuleFileNameW(hScanMod, sb3, (uint)sb3.Capacity) > 0)
                                        scanModName = System.IO.Path.GetFileName(sb3.ToString());

                                    NativeInterop.RtlLookupFunctionEntry(candidate, out ulong scanImgBase, IntPtr.Zero);
                                    if (scanImgBase != 0) scanModBase = scanImgBase;

                                    // Export name lookup with offset threshold
                                    string scanFrame;
                                    try
                                    {
                                        uint scanRva = (uint)(candidate - scanModBase);
                                        byte* scanBase = (byte*)scanModBase;
                                        int scanLfanew = *(int*)(scanBase + 0x3C);
                                        uint expRva  = *(uint*)(scanBase + scanLfanew + 0x88);
                                        uint expSize = *(uint*)(scanBase + scanLfanew + 0x8C);
                                        string? expName = null;
                                        uint   expDisp  = 0;
                                        if (expRva != 0 && expSize != 0)
                                        {
                                            var expDir   = (uint*)(scanBase + expRva);
                                            uint nFuncs  = expDir[5], nNames = expDir[6];
                                            uint* funcs  = (uint*)(scanBase + expDir[7]);
                                            uint* names  = (uint*)(scanBase + expDir[8]);
                                            ushort* ords = (ushort*)(scanBase + expDir[9]);
                                            uint bestRva2 = 0; uint bestIdx2 = uint.MaxValue;
                                            for (uint n = 0; n < nNames; n++)
                                            {
                                                ushort ord = ords[n];
                                                if (ord >= nFuncs) continue;
                                                uint fr = funcs[ord];
                                                if (fr >= expRva && fr < expRva + expSize) continue;
                                                if (fr <= scanRva && fr > bestRva2) { bestRva2 = fr; bestIdx2 = n; }
                                            }
                                            if (bestIdx2 != uint.MaxValue && (scanRva - bestRva2) <= ExportOffsetThreshold)
                                            {
                                                expName = new string((sbyte*)(scanBase + names[bestIdx2]));
                                                expDisp = scanRva - bestRva2;
                                            }
                                        }
                                        // Apply threshold: large offsets get RVA only (not a misleading name)
                                        scanFrame = expName != null
                                            ? $"{expName}+0x{expDisp:X}"
                                            : $"{scanModName}+0x{scanRva:X} (no nearby export)";
                                    }
                                    catch { scanFrame = $"{scanModName}+0x{candidate - scanModBase:X}"; }

                                    scanSb.AppendLine($"  {hits,2}: {scanFrame}  (rsp+0x{off:X})");
                                    hits++;
                                }
                            }
                            if (hits == 0)
                                scanSb.AppendLine("  (no return addresses found)");
                            CrashReporter.LogSync(scanSb.ToString());
                        }
                    }
                    finally
                    {
                        System.Runtime.InteropServices.Marshal.FreeHGlobal(stackCopy);
                    }
                    skipScan:;
                }
                catch (Exception scanEx)
                {
                    CrashReporter.LogSync($"[Heartbeat.Scan] Stack scan failed: {scanEx.GetType().Name}: {scanEx.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            CrashReporter.LogSync($"[Heartbeat.Native] Native stack capture failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            if (hThread != IntPtr.Zero)
                NativeInterop.CloseHandle(hThread);
        }
    }

    /// <summary>
    /// Starts a repeating 4-hour timer that re-runs all update checks.
    /// Called after the initial background scan completes.
    /// </summary>
    internal void StartPeriodicUpdateCheckTimer()
    {
        lock (_backgroundTimerLock)
        {
            if (_backgroundStopped || _updateCheckTimer != null) return;
            _updateCheckTimer = CreatePeriodicUpdateCheckTimer();
            _crashReporter.Log("[MainViewModel] Periodic update check timer started (4h interval)");
        }
    }

    private System.Threading.Timer CreatePeriodicUpdateCheckTimer()
    {
        var interval = TimeSpan.FromHours(4);
        return new System.Threading.Timer(async _ =>
        {
            if (_backgroundStopped) return;
            try
            {
                _crashReporter.Log("[MainViewModel] Periodic update check triggered (4h timer)");

                // Re-fetch manifest (picks up new game entries, overrides, etc.)
                try
                {
                    var freshManifest = await _manifestService.FetchAsync();
                    if (freshManifest != null)
                    {
                        _manifest = freshManifest;
                        AuxInstallService.GlobalManifest = _manifest;
                        ApplyManifest(_manifest);
                        _pcgwService.CheckManifestCacheVersion(_manifest);
                        _crashReporter.Log("[MainViewModel] Periodic manifest refresh complete");
                    }
                }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel] Periodic manifest fetch failed — {ex.Message}"); }

                // Re-fetch wiki to detect new mods
                try
                {
                    var wikiResult = await _wikiService.FetchAllAsync();
                    if (wikiResult.Mods != null)
                    {
                        _allMods = wikiResult.Mods;
                        _genericNotes = wikiResult.GenericNotes ?? new();
                        _crashReporter.Log($"[MainViewModel] Periodic wiki refresh complete — {_allMods.Count} mods");

                        // Detect new wiki mods
                        var currentModNames = _allMods
                            .Where(m => m.SnapshotUrl != null || m.NexusUrl != null)
                            .Select(m => m.Name)
                            .ToList();
                        _crashReporter.Log($"[MainViewModel] Periodic wiki mods check: {currentModNames.Count} downloadable mods");
                        var newMods = _seenWikiModsService.GetNewMods(currentModNames);
                        _crashReporter.Log($"[MainViewModel] Periodic new wiki mods: {newMods.Count} (seen: {_seenWikiModsService.GetSeenMods().Count})");
                        if (newMods.Count > 0)
                        {
                            _crashReporter.Log($"[MainViewModel] Periodic new mods: {string.Join(", ", newMods.Take(10))}{(newMods.Count > 10 ? "..." : "")}");
                            DispatcherQueue?.TryEnqueue(() => NewWikiMods = newMods);
                        }
                    }
                }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel] Periodic wiki fetch failed — {ex.Message}"); }

                // Re-fetch Ultra+ mods and detect new ones
                try
                {
                    await _ultraPlusService.InitAsync();
                    var currentUltraPlusMods = _ultraPlusService.GetAllGameNames().ToList();
                    _crashReporter.Log($"[MainViewModel] Periodic Ultra+ mods check: {currentUltraPlusMods.Count} mods");
                    var newUltraPlusMods = _seenUltraPlusModsService.GetNewMods(currentUltraPlusMods);
                    _crashReporter.Log($"[MainViewModel] Periodic new Ultra+ mods: {newUltraPlusMods.Count} (seen: {_seenUltraPlusModsService.GetSeenMods().Count})");
                    if (newUltraPlusMods.Count > 0)
                    {
                        _crashReporter.Log($"[MainViewModel] Periodic new Ultra+ mods: {string.Join(", ", newUltraPlusMods.Take(10))}{(newUltraPlusMods.Count > 10 ? "..." : "")}");
                        DispatcherQueue?.TryEnqueue(() => NewUltraPlusMods = newUltraPlusMods);
                    }
                }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel] Periodic Ultra+ fetch failed — {ex.Message}"); }

                // Re-fetch Luma mods and detect new ones
                try
                {
                    var lumaTask = _lumaService.FetchCompletedModsAsync();
                    _lumaMods = await lumaTask;
                    var currentLumaMods = _lumaMods.Select(m => m.Name).ToList();
                    _crashReporter.Log($"[MainViewModel] Periodic Luma mods check: {currentLumaMods.Count} mods");
                    var newLumaMods = _seenLumaModsService.GetNewMods(currentLumaMods);
                    _crashReporter.Log($"[MainViewModel] Periodic new Luma mods: {newLumaMods.Count} (seen: {_seenLumaModsService.GetSeenMods().Count})");
                    if (newLumaMods.Count > 0)
                    {
                        _crashReporter.Log($"[MainViewModel] Periodic new Luma mods: {string.Join(", ", newLumaMods.Take(10))}{(newLumaMods.Count > 10 ? "..." : "")}");
                        DispatcherQueue?.TryEnqueue(() => NewLumaMods = newLumaMods);
                    }
                }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel] Periodic Luma fetch failed — {ex.Message}"); }
                try { await _dlssStreamlineService.FetchManifestAsync(); }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel] Periodic DLSS manifest fetch failed — {ex.Message}"); }

                // Check and re-stage Cost Scaler + RTX40MFG, auto-deploy to installed games
                try
                {
                    await _nrCostScalerService.CheckForUpdateAsync(forceRefresh: true);
                    if (_nrCostScalerService.HasUpdate)
                    {
                        await _nrCostScalerService.EnsureStagingAsync();
                        if (_nrCostScalerService.IsStagingReady)
                            foreach (var c in _allCards.Where(c => !string.IsNullOrEmpty(c.InstallPath)
                                && DlssNrCostScalerService.IsInstalled(c.InstallPath)))
                                _nrCostScalerService.Install(c.InstallPath);
                    }
                }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel] Periodic NR Cost Scaler check failed — {ex.Message}"); }

                try
                {
                    await _rtx40MfgService.CheckForUpdateAsync(forceRefresh: true);
                    if (_rtx40MfgService.HasUpdate)
                    {
                        await _rtx40MfgService.EnsureStagingAsync();
                        if (_rtx40MfgService.IsStagingReady)
                            foreach (var c in _allCards.Where(c => !string.IsNullOrEmpty(c.InstallPath)))
                            {
                                var installedAs = GetRtx40MfgInstalledAs(c.GameName, c.Source ?? "");
                                if (!string.IsNullOrEmpty(installedAs) && File.Exists(Path.Combine(c.InstallPath!, installedAs)))
                                    _rtx40MfgService.Install(c.InstallPath!, installedAs);
                            }
                    }
                }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel] Periodic RTX40MFG check failed — {ex.Message}"); }

                try
                {
                    await _dlssg2030Service.CheckForUpdateAsync();
                    if (_dlssg2030Service.HasUpdate)
                    {
                        await _dlssg2030Service.EnsureStagingAsync();
                        if (_dlssg2030Service.IsStagingReady)
                            foreach (var c in _allCards.Where(c => !string.IsNullOrEmpty(c.InstallPath)))
                            {
                                var installedAs = GetDlssg2030InstalledAs(c.GameName, c.Source ?? "");
                                if (!string.IsNullOrEmpty(installedAs) && File.Exists(Path.Combine(c.InstallPath!, installedAs)))
                                    _dlssg2030Service.Update(c.InstallPath!, installedAs, GetDlssg2030GpuGen(c.GameName, c.Source ?? ""));
                            }
                    }
                }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel] Periodic Dlssg2030 check failed — {ex.Message}"); }

                _forceUpdateCheck = true; // bypass cooldown since we ARE the cooldown
                var records = _installer.LoadAll();
                var auxRecords = _auxInstaller.LoadAll();
                await CheckForUpdatesAsync(_allCards, records, auxRecords, userInitiated: false);

                // Check for custom ReShade DLL changes and redeploy
                try
                {
                    var redeployed = _customReShadeHashService.CheckAndRedeploy(_allCards);
                    if (redeployed > 0)
                        _crashReporter.Log($"[MainViewModel] Periodic custom ReShade redeploy — {redeployed} game(s) updated");
                }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel] Custom ReShade hash check failed — {ex.Message}"); }

                // Silent component auto-update (runs after update check flags pending updates)
                try { _autoUpdateService.TriggerAsync(); }
                catch (Exception ex) { _crashReporter.Log($"[MainViewModel] Periodic component auto-update trigger failed — {ex.Message}"); }
            }
            catch (Exception ex)
            {
                _crashReporter.Log($"[MainViewModel] Periodic update check failed — {ex.Message}");
            }

            // Also check for RHI app updates on the UI thread
            DispatcherQueue?.TryEnqueue(() =>
            {
                PeriodicAppUpdateCheck?.Invoke();
            });
        }, null, interval, interval);
    }

    /// <summary>Callback set by MainWindow to trigger app update check from the periodic timer.</summary>
    public Action? PeriodicAppUpdateCheck { get; set; }

    /// <summary>Triggers a silent auto-install pass — safe to call from any thread.</summary>
    public void TriggerAutoUpdate() => _autoUpdateService.TriggerAsync();

    internal void NotifyUpdateButtonChanged()
    {
        HasUpdatesAvailable = AnyUpdateAvailable;
        OnPropertyChanged(nameof(AnyUpdateAvailable));
        OnPropertyChanged(nameof(UpdateAllBtnBackground));
        OnPropertyChanged(nameof(UpdateAllBtnForeground));
        OnPropertyChanged(nameof(UpdateAllBtnBorder));
    }

    /// <summary>Returns true if the current app version differs from the last seen version.</summary>
    public bool IsNewVersion()
    {
        var current = _updateService.CurrentVersion;
        var currentStr = $"{current.Major}.{current.Minor}.{current.Build}";
        return LastSeenVersion != currentStr;
    }

    /// <summary>Marks the current version as seen and saves settings.</summary>
    public void MarkVersionSeen()
    {
        var current = _updateService.CurrentVersion;
        LastSeenVersion = $"{current.Major}.{current.Minor}.{current.Build}";
        SaveSettingsPublic();
    }

    /// <summary>
    /// Reads the bundled RHI_PatchNotes.md and extracts the last N version sections.
    /// Each section starts with "## vX.Y.Z".
    /// </summary>
    public static string GetRecentPatchNotes(int count = 3)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "RHI_PatchNotes.md");
            if (!File.Exists(path)) return "Patch notes file not found.";

            var lines = File.ReadAllLines(path);
            var sections = new List<string>();
            var currentSection = new List<string>();
            bool inSection = false;
            var preamble = new List<string>();

            foreach (var line in lines)
            {
                if (line.StartsWith("## v"))
                {
                    if (inSection && currentSection.Count > 0)
                    {
                        sections.Add(string.Join("\n", currentSection));
                        if (sections.Count >= count) break;
                        currentSection.Clear();
                    }
                    inSection = true;
                    currentSection.Add(line);
                }
                else if (inSection)
                {
                    // Stop at the "---" separator between versions (but don't include it)
                    if (line.Trim() == "---")
                    {
                        sections.Add(string.Join("\n", currentSection));
                        if (sections.Count >= count) break;
                        currentSection.Clear();
                        inSection = false;
                    }
                    else
                    {
                        currentSection.Add(line);
                    }
                }
                else
                {
                    preamble.Add(line);
                }
            }

            // Capture final section if still in progress
            if (inSection && currentSection.Count > 0 && sections.Count < count)
                sections.Add(string.Join("\n", currentSection));

            var body = sections.Count > 0
                ? string.Join("\n\n---\n\n", sections)
                : "No patch notes available.";

            // Prepend preamble (e.g. the GitHub API warning banner) if present
            var preambleText = string.Join("\n", preamble).Trim();
            return string.IsNullOrEmpty(preambleText)
                ? body
                : preambleText + "\n\n---\n\n" + body;
        }
        catch (Exception ex)
        {
            return $"Error reading patch notes: {ex.Message}";
        }
    }

    /// <summary>
    /// Fetches the latest UL release info (version + download URL) from GitHub.
    /// Populates _latestUlVersion and _latestUlDownloadUrl.
    /// </summary>

    /// <summary>Persists UL installed version to the meta file.</summary>
    private static void SaveUlMeta(string version, bool is32Bit)
    {
        try
        {
            var cleanVersion = version.TrimStart('v', 'V');

            // Read existing meta to preserve the other bitness entry
            Dictionary<string, object>? meta = null;
            if (File.Exists(UlMetaPath))
            {
                try
                {
                    var existing = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
                        File.ReadAllText(UlMetaPath));
                    if (existing != null) meta = existing;
                }
                catch { /* corrupt file — start fresh */ }
            }
            meta ??= new Dictionary<string, object>();

            var key = is32Bit ? "InstalledVersion32" : "InstalledVersion64";
            meta[key] = cleanVersion;
            meta["UpdatedAt"] = DateTime.UtcNow.ToString("o");

            // Migrate: also write legacy key so older builds still work
            meta["InstalledVersion"] = cleanVersion;

            Directory.CreateDirectory(Path.GetDirectoryName(UlMetaPath)!);
            File.WriteAllText(UlMetaPath, System.Text.Json.JsonSerializer.Serialize(meta,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainViewModel.SaveUlMeta] Failed to save UL metadata — {ex.Message}");
        }
    }

    /// <summary>Reads the installed UL version for the given bitness from the meta file, or null if not found.</summary>
    private static string? ReadUlInstalledVersion(bool is32Bit)
    {
        try
        {
            if (!File.Exists(UlMetaPath)) return null;
            var metaJson = File.ReadAllText(UlMetaPath);
            var meta = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(metaJson);
            if (meta == null) return null;

            var key = is32Bit ? "InstalledVersion32" : "InstalledVersion64";
            if (meta.TryGetValue(key, out var verEl))
                return verEl.GetString()?.TrimStart('v', 'V');
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainViewModel.ReadUlInstalledVersion] Failed — {ex.Message}");
        }
        return null;
    }

    /// <summary>Persists DC installed version to the meta file.</summary>
    private static void SaveDcMeta(string version, bool is32Bit)
    {
        try
        {
            var cleanVersion = version.TrimStart('v', 'V');

            Dictionary<string, object>? meta = null;
            if (File.Exists(DcMetaPath))
            {
                try
                {
                    var existing = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
                        File.ReadAllText(DcMetaPath));
                    if (existing != null) meta = existing;
                }
                catch { /* corrupt file — start fresh */ }
            }
            meta ??= new Dictionary<string, object>();

            var key = is32Bit ? "InstalledVersion32" : "InstalledVersion64";
            meta[key] = cleanVersion;
            meta["UpdatedAt"] = DateTime.UtcNow.ToString("o");
            meta["InstalledVersion"] = cleanVersion;

            Directory.CreateDirectory(Path.GetDirectoryName(DcMetaPath)!);
            File.WriteAllText(DcMetaPath, System.Text.Json.JsonSerializer.Serialize(meta,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainViewModel.SaveDcMeta] Failed to save DC metadata — {ex.Message}");
        }
    }

    /// <summary>Reads the installed DC version for the given bitness from the meta file, or null if not found.</summary>
    internal static string? ReadDcInstalledVersion(bool is32Bit)
    {
        try
        {
            if (!File.Exists(DcMetaPath)) return null;
            var metaJson = File.ReadAllText(DcMetaPath);
            var meta = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(metaJson);
            if (meta == null) return null;

            var key = is32Bit ? "InstalledVersion32" : "InstalledVersion64";
            if (meta.TryGetValue(key, out var verEl))
                return verEl.GetString()?.TrimStart('v', 'V');
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[MainViewModel.ReadDcInstalledVersion] Failed — {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// Checks if a newer ReLimiter is available by comparing the latest GitHub
    /// release tag version against the locally installed version from meta.
    /// Returns true if an update is available.
    /// </summary>
    public async Task<bool> CheckUlUpdateAsync(List<GameCardViewModel> cards)
    {
        _crashReporter.Log($"[CheckUlUpdateAsync] Starting");

        bool anyInstalled = cards.Any(c => c.UlStatus == GameStatus.Installed);

        // ── Determine which bitness variants are in use ───────────────────
        bool needs64 = cards.Any(c => c.UlStatus == GameStatus.Installed && !c.Is32Bit);
        bool needs32 = cards.Any(c => c.UlStatus == GameStatus.Installed && c.Is32Bit);

        // If nothing is specifically installed yet (legacy/meta-only), default to 64-bit
        if (!needs64 && !needs32)
            needs64 = true;

        // ── Read installed version from meta (use oldest across bitness variants) ──
        var installedVersion64 = needs64 ? ReadUlInstalledVersion(false) : null;
        var installedVersion32 = needs32 ? ReadUlInstalledVersion(true) : null;
        var installedVersion = (installedVersion64, installedVersion32) switch
        {
            (null, null) => null,
            (null, var v) => v,
            (var v, null) => v,
            // Use the older of the two so we trigger an update if either is behind
            var (v64, v32) => (Version.TryParse(v64, out var ver64) && Version.TryParse(v32, out var ver32))
                ? (ver64 <= ver32 ? v64 : v32)
                : v64, // fallback to 64-bit if parsing fails
        };

        if (installedVersion == null && !anyInstalled)
        {
            _crashReporter.Log("[CheckUlUpdateAsync] No UL installed and no meta — skipping");
            return false;
        }

        // If UL is installed but no version in meta (legacy install), treat as needing update
        if (installedVersion == null && anyInstalled)
        {
            _crashReporter.Log("[CheckUlUpdateAsync] UL installed but no version in meta — treating as update needed");
        }

        try
        {
            // ── Fetch latest release from GitHub API ──────────────────────
            var json = await _etagCache.GetWithETagAsync(_http, UltraLimiterReleasesApiUrl).ConfigureAwait(false);
            if (json == null)
            {
                _crashReporter.Log($"[CheckUlUpdateAsync] API returned error");
                return false;
            }

            using var doc = System.Text.Json.JsonDocument.Parse(json);

            // Get the tag name (version)
            string? remoteVersion = null;
            if (doc.RootElement.TryGetProperty("tag_name", out var tagEl))
                remoteVersion = tagEl.GetString();

            // Always capture the release body for the Info dialog
            if (doc.RootElement.TryGetProperty("body", out var ulBodyEl))
                _latestUlReleaseBody = ulBodyEl.GetString();

            if (string.IsNullOrEmpty(remoteVersion))
            {
                _crashReporter.Log("[CheckUlUpdateAsync] No tag_name in latest release");
                return false;
            }

            // Get the download URLs for both bitness variants from assets
            string? downloadUrl64 = null;
            string? downloadUrl32 = null;
            if (doc.RootElement.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    if (asset.TryGetProperty("name", out var name) &&
                        asset.TryGetProperty("browser_download_url", out var urlEl))
                    {
                        var assetName = name.GetString();
                        if (assetName?.Equals(UltraLimiterFileName64, StringComparison.OrdinalIgnoreCase) == true)
                            downloadUrl64 = urlEl.GetString();
                        else if (assetName?.Equals(UltraLimiterFileName32, StringComparison.OrdinalIgnoreCase) == true)
                            downloadUrl32 = urlEl.GetString();
                    }

                    if (downloadUrl64 != null && downloadUrl32 != null)
                        break;
                }
            }

            // Need at least one matching asset for the variants we care about
            if ((needs64 && string.IsNullOrEmpty(downloadUrl64)) && (needs32 && string.IsNullOrEmpty(downloadUrl32)))
            {
                _crashReporter.Log("[CheckUlUpdateAsync] No matching asset found in latest release");
                return false;
            }

            _crashReporter.Log($"[CheckUlUpdateAsync] Remote version={remoteVersion}, installed={installedVersion ?? "(none)"}");

            // ── Compare versions ──────────────────────────────────────────
            if (installedVersion != null && !IsNewerVersion(remoteVersion, installedVersion))
            {
                _crashReporter.Log("[CheckUlUpdateAsync] No update (installed is current)");
                return false;
            }

            // ── Update available — store remote info and pre-cache ────────
            _latestUlVersion = remoteVersion;
            _latestUlDownloadUrl = downloadUrl64;
            _latestUlDownloadUrl32 = downloadUrl32;
            _crashReporter.Log($"[CheckUlUpdateAsync] Update available: {installedVersion ?? "(none)"} → {remoteVersion}");
            App.Services.GetRequiredService<IUpdateLogService>().Record(new Models.UpdateLogEntry
            {
                Timestamp     = DateTime.UtcNow,
                Category      = "Component",
                ComponentName = "ReLimiter",
                OldVersion    = installedVersion,
                NewVersion    = remoteVersion,
            });

            await PreCacheRemoteUlAsync(needs64, needs32);
            return true;
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[CheckUlUpdateAsync] Failed — {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Checks if a newer Display Commander is available by comparing the latest GitHub
    /// release tag version against the locally installed version from meta.
    /// Returns true if an update is available.
    /// </summary>
    public async Task<bool> CheckDcUpdateAsync(List<GameCardViewModel> cards)
    {
        _crashReporter.Log($"[CheckDcUpdateAsync] Starting");

        bool anyInstalled = cards.Any(c => c.DcStatus == GameStatus.Installed || c.DcStatus == GameStatus.UpdateAvailable);

        // ── Determine which bitness variants are in use ───────────────────
        bool needs64 = cards.Any(c => (c.DcStatus == GameStatus.Installed || c.DcStatus == GameStatus.UpdateAvailable) && !c.Is32Bit);
        bool needs32 = cards.Any(c => (c.DcStatus == GameStatus.Installed || c.DcStatus == GameStatus.UpdateAvailable) && c.Is32Bit);

        // If nothing is specifically installed yet (legacy/meta-only), default to 64-bit
        if (!needs64 && !needs32)
            needs64 = true;

        // ── Read installed version from meta (use oldest across bitness variants) ──
        var installedVersion64 = needs64 ? ReadDcInstalledVersion(false) : null;
        var installedVersion32 = needs32 ? ReadDcInstalledVersion(true) : null;
        var installedVersion = (installedVersion64, installedVersion32) switch
        {
            (null, null) => null,
            (null, var v) => v,
            (var v, null) => v,
            // Use the older of the two so we trigger an update if either is behind
            var (v64, v32) => (Version.TryParse(v64, out var ver64) && Version.TryParse(v32, out var ver32))
                ? (ver64 <= ver32 ? v64 : v32)
                : v64, // fallback to 64-bit if parsing fails
        };

        if (installedVersion == null && !anyInstalled)
        {
            _crashReporter.Log("[CheckDcUpdateAsync] No DC installed and no meta — skipping");
            return false;
        }

        // If DC is installed but no version in meta (legacy install), treat as needing update
        if (installedVersion == null && anyInstalled)
        {
            _crashReporter.Log("[CheckDcUpdateAsync] DC installed but no version in meta — treating as update needed");
        }

        try
        {
            // ── Fetch latest release from GitHub API ──────────────────────
            var json = await _etagCache.GetWithETagAsync(_http, EffectiveDcReleasesApiUrl).ConfigureAwait(false);
            if (json == null)
            {
                _crashReporter.Log($"[CheckDcUpdateAsync] API returned error");
                return false;
            }

            using var doc = System.Text.Json.JsonDocument.Parse(json);

            // Get the tag name (version) — DC uses a fixed "latest_build" tag,
            // so extract the real version from the release body ("Version in binaries: X.Y.Z.W")
            string? remoteVersion = null;
            if (doc.RootElement.TryGetProperty("tag_name", out var tagEl))
                remoteVersion = tagEl.GetString();

            // DC's tag is always "latest_build" which is useless for comparison.
            // Parse the actual version from the release body text.
            if (doc.RootElement.TryGetProperty("body", out var bodyEl))
            {
                var body = bodyEl.GetString() ?? "";
                _latestDcReleaseBody = body; // Always capture for the Info dialog
                var versionMatch = System.Text.RegularExpressions.Regex.Match(
                    body, @"\*{0,2}Version in binaries\*{0,2}:\s*([\d.]+)");
                if (versionMatch.Success)
                    remoteVersion = versionMatch.Groups[1].Value;
            }

            if (string.IsNullOrEmpty(remoteVersion))
            {
                _crashReporter.Log("[CheckDcUpdateAsync] No version found in tag or release body");
                return false;
            }

            // Get the download URLs for both bitness variants from assets
            string? downloadUrl64 = null;
            string? downloadUrl32 = null;
            if (doc.RootElement.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    if (asset.TryGetProperty("name", out var name) &&
                        asset.TryGetProperty("browser_download_url", out var urlEl))
                    {
                        var assetName = name.GetString();
                        if (assetName?.Equals(DcFileName64, StringComparison.OrdinalIgnoreCase) == true)
                            downloadUrl64 = urlEl.GetString();
                        else if (assetName?.Equals(DcFileName32, StringComparison.OrdinalIgnoreCase) == true)
                            downloadUrl32 = urlEl.GetString();
                    }

                    if (downloadUrl64 != null && downloadUrl32 != null)
                        break;
                }
            }

            // Need at least one matching asset for the variants we care about
            if ((needs64 && string.IsNullOrEmpty(downloadUrl64)) && (needs32 && string.IsNullOrEmpty(downloadUrl32)))
            {
                _crashReporter.Log("[CheckDcUpdateAsync] No matching asset found in latest release");
                return false;
            }

            _crashReporter.Log($"[CheckDcUpdateAsync] Remote version={remoteVersion}, installed={installedVersion ?? "(none)"}");

            // ── Compare versions ──────────────────────────────────────────
            if (installedVersion != null && !IsNewerVersion(remoteVersion, installedVersion))
            {
                _crashReporter.Log("[CheckDcUpdateAsync] No update (installed is current)");
                return false;
            }

            // ── Update available — store remote info and pre-cache ────────
            _latestDcVersion = remoteVersion;
            _latestDcDownloadUrl = downloadUrl64;
            _latestDcDownloadUrl32 = downloadUrl32;
            _crashReporter.Log($"[CheckDcUpdateAsync] Update available: {installedVersion ?? "(none)"} → {remoteVersion}");
            App.Services.GetRequiredService<IUpdateLogService>().Record(new Models.UpdateLogEntry
            {
                Timestamp     = DateTime.UtcNow,
                Category      = "Component",
                ComponentName = "Display Commander",
                OldVersion    = installedVersion,
                NewVersion    = remoteVersion,
            });

            await PreCacheRemoteDcAsync(needs64, needs32);
            return true;
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[CheckDcUpdateAsync] Failed — {ex.Message}");
            return false;
        }
    }

    /// <summary>Downloads the remote DC file(s) into the cache using the URLs from the latest release.</summary>
    private async Task PreCacheRemoteDcAsync(bool needs64, bool needs32)
    {
        if (needs64)
            await PreCacheRemoteDcVariantAsync(false);
        if (needs32)
            await PreCacheRemoteDcVariantAsync(true);
    }

    /// <summary>Downloads a single bitness variant of the DC file into the cache.</summary>
    private async Task PreCacheRemoteDcVariantAsync(bool is32Bit)
    {
        try
        {
            var url = is32Bit ? _latestDcDownloadUrl32 : _latestDcDownloadUrl;
            if (string.IsNullOrEmpty(url))
            {
                _crashReporter.Log($"[PreCacheRemoteDcAsync] No download URL available for {(is32Bit ? "32-bit" : "64-bit")}");
                return;
            }

            var tempPath = GetDcCachePath(is32Bit) + ".precache.tmp";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true, NoStore = true };
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            if (!resp.IsSuccessStatusCode) return;

            using (var net = await resp.Content.ReadAsStreamAsync())
            using (var file = File.Create(tempPath))
            {
                var buf = new byte[1024 * 1024];
                int read;
                while ((read = await net.ReadAsync(buf)) > 0)
                    await file.WriteAsync(buf.AsMemory(0, read));
            }

            if (File.Exists(GetDcCachePath(is32Bit))) File.Delete(GetDcCachePath(is32Bit));
            File.Move(tempPath, GetDcCachePath(is32Bit));
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[PreCacheRemoteDcAsync] Failed ({(is32Bit ? "32-bit" : "64-bit")}) — {ex.Message}");
        }
    }

    /// <summary>
    /// Compares two version strings (e.g. "1.0.0" vs "1.0.1").
    /// Returns true if <paramref name="remote"/> is newer than <paramref name="installed"/>.
    /// Strips leading 'v' if present.
    /// </summary>
    private static bool IsNewerVersion(string remote, string installed)
    {
        static Version? Parse(string s)
        {
            s = s.TrimStart('v', 'V').Trim();
            return Version.TryParse(s, out var v) ? v : null;
        }

        var r = Parse(remote);
        var i = Parse(installed);
        if (r == null || i == null) return !string.Equals(remote, installed, StringComparison.OrdinalIgnoreCase);
        return r > i;
    }

    // Cached latest release info from the update check
    private string? _latestUlVersion;
    private string? _latestUlDownloadUrl;
    private string? _latestUlDownloadUrl32;
    private string? _latestUlReleaseBody;

    /// <summary>Markdown body of the latest ReLimiter GitHub release, or null if not yet fetched.</summary>
    public string? LatestUlReleaseBody => _latestUlReleaseBody;

    /// <summary>URL to the GitHub release page for the latest UL version, or null if unknown.</summary>
    public string? LatestUlReleasePageUrl => _latestUlVersion != null
        ? $"https://github.com/RankFTW/ReLimiter/releases/tag/{_latestUlVersion}"
        : null;

    /// <summary>Downloads the remote UL file(s) into the cache using the URLs from the latest release.</summary>
    private async Task PreCacheRemoteUlAsync(bool needs64, bool needs32)
    {
        if (needs64)
            await PreCacheRemoteUlVariantAsync(false);
        if (needs32)
            await PreCacheRemoteUlVariantAsync(true);
    }

    /// <summary>Downloads a single bitness variant of the UL file into the cache.</summary>
    private async Task PreCacheRemoteUlVariantAsync(bool is32Bit)
    {
        try
        {
            var url = is32Bit ? _latestUlDownloadUrl32 : _latestUlDownloadUrl;
            if (string.IsNullOrEmpty(url))
            {
                _crashReporter.Log($"[PreCacheRemoteUlAsync] No download URL available for {(is32Bit ? "32-bit" : "64-bit")}");
                return;
            }

            var tempPath = GetUlCachePath(is32Bit) + ".precache.tmp";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true, NoStore = true };
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            if (!resp.IsSuccessStatusCode) return;

            using (var net = await resp.Content.ReadAsStreamAsync())
            using (var file = File.Create(tempPath))
            {
                var buf = new byte[1024 * 1024];
                int read;
                while ((read = await net.ReadAsync(buf)) > 0)
                    await file.WriteAsync(buf.AsMemory(0, read));
            }

            if (File.Exists(GetUlCachePath(is32Bit))) File.Delete(GetUlCachePath(is32Bit));
            File.Move(tempPath, GetUlCachePath(is32Bit));
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[PreCacheRemoteUlAsync] Failed ({(is32Bit ? "32-bit" : "64-bit")}) — {ex.Message}");
        }
    }

    /// <summary>
    /// Eligibility: card must not be hidden, not excluded from Update All.
    /// </summary>
    private IEnumerable<GameCardViewModel> UpdateAllEligible() =>
        _updateOrchestrationService.UpdateAllEligible(_allCards);

    public async Task UpdateAllRenoDxAsync()
    {
        // Capture games with Nexus update flags BEFORE running updates
        var nexusFlaggedGames = _allCards
            .Where(c => c.Status == GameStatus.UpdateAvailable && !c.IsExternalOnly)
            .Select(c => c.GameName)
            .ToList();

        await _updateOrchestrationService.UpdateAllRenoDxAsync(
            _allCards, _dllOverrideService, DispatcherQueue,
            () => SaveLibrary(),
            () =>
            {
                _filterViewModel.UpdateCounts();
                HasUpdatesAvailable = AnyUpdateAvailable;
                OnPropertyChanged(nameof(AnyUpdateAvailable));
                OnPropertyChanged(nameof(UpdateAllBtnBackground));
                OnPropertyChanged(nameof(UpdateAllBtnForeground));
                OnPropertyChanged(nameof(UpdateAllBtnBorder));
            });

        // Reset Nexus baselines for games that had update flags (clears false positives from Nexus page edits)
        foreach (var gameName in nexusFlaggedGames)
            _nexusUpdateService.ResetBaseline(gameName);

        NotifyUpdateButtonChanged();

        // Update emulator cards (bundle re-download)
        var emuCards = _allCards
            .Where(c => c.IsEmulator && c.Status == GameStatus.UpdateAvailable && !c.ExcludeFromUpdateAllRenoDx)
            .ToList();
        _crashReporter.Log($"[UpdateAllRenoDxAsync] Emulator cards eligible: {emuCards.Count} (total emulators: {_allCards.Count(c => c.IsEmulator)}, with UpdateAvailable: {_allCards.Count(c => c.IsEmulator && c.Status == GameStatus.UpdateAvailable)})");
        foreach (var card in emuCards)
            await InstallEmulatorAddonsAsync(card);
    }

    public async Task UpdateAllReShadeAsync()
    {
        await _updateOrchestrationService.UpdateAllReShadeAsync(
            _allCards, _dllOverrideService, DispatcherQueue,
            () =>
            {
                HasUpdatesAvailable = AnyUpdateAvailable;
                OnPropertyChanged(nameof(AnyUpdateAvailable));
                OnPropertyChanged(nameof(UpdateAllBtnBackground));
                OnPropertyChanged(nameof(UpdateAllBtnForeground));
                OnPropertyChanged(nameof(UpdateAllBtnBorder));
            },
            shaderResolver: (gameName, store, shaderModeOverride) => ResolveShaderSelection(gameName, shaderModeOverride, store),
            manifestDllResolver: GetManifestDllNames,
            channelResolver: (gameName, store) => ResolveReShadeChannel(gameName, store ?? ""),
            keepRsIniUpdatedResolver: (gameName, store) => GetKeepRsIniUpdated(gameName, store));
    }

    public async Task UpdateAllUlAsync()
    {
        var ulCards = _allCards.Where(c => c.UlStatus == GameStatus.UpdateAvailable && !c.IsHidden && !c.ExcludeFromUpdateAllUl).ToList();
        if (ulCards.Count == 0) return;

        foreach (var card in ulCards)
        {
            try { await InstallUlAsync(card); }
            catch (Exception ex) { _crashReporter.Log($"[UpdateAllUlAsync] Failed for '{card.GameName}': {ex.Message}"); }
        }

        DispatcherQueue?.TryEnqueue(() =>
        {
            HasUpdatesAvailable = AnyUpdateAvailable;
            OnPropertyChanged(nameof(AnyUpdateAvailable));
            OnPropertyChanged(nameof(UpdateAllBtnBackground));
            OnPropertyChanged(nameof(UpdateAllBtnForeground));
            OnPropertyChanged(nameof(UpdateAllBtnBorder));
        });
    }

    public async Task UpdateAllDcAsync()
    {
        var dcCards = _allCards.Where(c => c.DcStatus == GameStatus.UpdateAvailable && !c.IsHidden && !c.ExcludeFromUpdateAllDc).ToList();
        if (dcCards.Count == 0) return;

        foreach (var card in dcCards)
        {
            try { await InstallDcAsync(card); }
            catch (Exception ex) { _crashReporter.Log($"[UpdateAllDcAsync] Failed for '{card.GameName}': {ex.Message}"); }
        }

        DispatcherQueue?.TryEnqueue(() =>
        {
            HasUpdatesAvailable = AnyUpdateAvailable;
            OnPropertyChanged(nameof(AnyUpdateAvailable));
            OnPropertyChanged(nameof(UpdateAllBtnBackground));
            OnPropertyChanged(nameof(UpdateAllBtnForeground));
            OnPropertyChanged(nameof(UpdateAllBtnBorder));
        });
    }

    public async Task UpdateAllDxvkAsync()
    {
        var dxvkCards = GetUpdateAllDxvkEligibleCards(_allCards);
        if (dxvkCards.Count == 0) return;

        // Ensure global variant staging is available
        if (!_dxvkService.IsStagingReady)
        {
            try { await _dxvkService.EnsureStagingAsync(); }
            catch (Exception ex) { _crashReporter.Log($"[UpdateAllDxvkAsync] Staging failed — {ex.Message}"); return; }
        }

        var globalVariant = _dxvkService.SelectedVariant;

        foreach (var card in dxvkCards)
        {
            try
            {
                // Resolve per-game variant (may differ from global)
                var effectiveVariant = ResolveDxvkVariant(card.GameName, card.Source ?? "");
                if (effectiveVariant != globalVariant)
                {
                    // Switch to per-game variant, ensure its staging is ready
                    _dxvkService.SelectedVariant = effectiveVariant;
                    if (!_dxvkService.IsStagingReady)
                        await _dxvkService.EnsureStagingAsync();
                }
                else if (_dxvkService.SelectedVariant != globalVariant)
                {
                    _dxvkService.SelectedVariant = globalVariant;
                }

                await _dxvkService.UpdateAsync(card);
            }
            catch (Exception ex) { _crashReporter.Log($"[UpdateAllDxvkAsync] Failed for '{card.GameName}': {ex.Message}"); }
        }

        // Restore global variant
        _dxvkService.SelectedVariant = globalVariant;

        DispatcherQueue?.TryEnqueue(() =>
        {
            HasUpdatesAvailable = AnyUpdateAvailable;
            OnPropertyChanged(nameof(AnyUpdateAvailable));
            OnPropertyChanged(nameof(UpdateAllBtnBackground));
            OnPropertyChanged(nameof(UpdateAllBtnForeground));
            OnPropertyChanged(nameof(UpdateAllBtnBorder));
        });
    }

    /// <summary>
    /// Returns the list of cards eligible for DXVK Update All.
    /// A card is eligible when DxvkStatus == UpdateAvailable, not excluded, and not hidden.
    /// Extracted as a static helper so the filtering logic can be property-tested
    /// without MainViewModel dependencies.
    /// </summary>
    internal static List<GameCardViewModel> GetUpdateAllDxvkEligibleCards(IEnumerable<GameCardViewModel> cards)
        => cards.Where(c => c.DxvkStatus == GameStatus.UpdateAvailable
                         && !c.ExcludeFromUpdateAllDxvk
                         && !c.IsHidden)
                .ToList();

    public async Task UpdateAllOsAsync()
    {
        var osCards = _allCards.Where(c => c.OsStatus == GameStatus.UpdateAvailable && !c.IsHidden && !c.ExcludeFromUpdateAllOs).ToList();
        if (osCards.Count == 0) return;

        var stableCards  = osCards.Where(c => GetOsVariant(c.GameName, c.Source ?? "") == "Stable" || GetOsVariant(c.GameName, c.Source ?? "") == "").ToList();
        var nightlyCards = osCards.Where(c => GetOsVariant(c.GameName, c.Source ?? "") == "Nightly").ToList();
        var dlssNrCards  = osCards.Where(c => GetOsVariant(c.GameName, c.Source ?? "") == "DlssNr").ToList();

        // Ensure stable staging if any stable cards need updating
        if (stableCards.Count > 0 && !_optiScalerService.IsStagingReady)
        {
            try { await _optiScalerService.EnsureStagingAsync(); }
            catch (Exception ex) { _crashReporter.Log($"[UpdateAllOsAsync] Stable staging failed — {ex.Message}"); return; }
        }

        // Ensure nightly staging if any nightly cards need updating
        if (nightlyCards.Count > 0 && !_optiScalerService.IsStagingReadyNightly)
        {
            try { await _optiScalerService.EnsureNightlyStagingAsync(); }
            catch (Exception ex) { _crashReporter.Log($"[UpdateAllOsAsync] Nightly staging failed — {ex.Message}"); return; }
        }

        // Ensure DLSS NR staging if any DlssNr cards need updating
        if (dlssNrCards.Count > 0 && !_optiScalerService.IsStagingReadyDlssNr)
        {
            try { await _optiScalerService.EnsureDlssNrStagingAsync(); }
            catch (Exception ex) { _crashReporter.Log($"[UpdateAllOsAsync] DLSS NR staging failed — {ex.Message}"); return; }
        }

        foreach (var card in osCards)
        {
            var cardVariant = GetOsVariant(card.GameName, card.Source ?? "");
            try { await _optiScalerService.UpdateAsync(card, variantHint: cardVariant); }
            catch (Exception ex) { _crashReporter.Log($"[UpdateAllOsAsync] Failed for '{card.GameName}': {ex.Message}"); }
        }

        DispatcherQueue?.TryEnqueue(() =>
        {
            HasUpdatesAvailable = AnyUpdateAvailable;
            OnPropertyChanged(nameof(AnyUpdateAvailable));
            OnPropertyChanged(nameof(UpdateAllBtnBackground));
            OnPropertyChanged(nameof(UpdateAllBtnForeground));
            OnPropertyChanged(nameof(UpdateAllBtnBorder));
        });
    }

    public async Task UpdateAllRefAsync()
    {
        await _updateOrchestrationService.UpdateAllREFrameworkAsync(
            _allCards, DispatcherQueue,
            () =>
            {
                HasUpdatesAvailable = AnyUpdateAvailable;
                OnPropertyChanged(nameof(AnyUpdateAvailable));
                OnPropertyChanged(nameof(UpdateAllBtnBackground));
                OnPropertyChanged(nameof(UpdateAllBtnForeground));
                OnPropertyChanged(nameof(UpdateAllBtnBorder));
            });
    }

    public async Task UpdateAllLumaAsync()
    {
        var lumaCards = _allCards.Where(c => c.LumaStatus == GameStatus.UpdateAvailable
            && !c.IsHidden && c.LumaMod?.DownloadUrl != null).ToList();
        if (lumaCards.Count == 0) return;

        foreach (var card in lumaCards)
        {
            try
            {
                await InstallLumaAsync(card);
            }
            catch (Exception ex)
            {
                _crashReporter.Log($"[UpdateAllLumaAsync] Failed for '{card.GameName}': {ex.Message}");
            }
        }

        DispatcherQueue?.TryEnqueue(() =>
        {
            HasUpdatesAvailable = AnyUpdateAvailable;
            OnPropertyChanged(nameof(AnyUpdateAvailable));
            OnPropertyChanged(nameof(UpdateAllBtnBackground));
            OnPropertyChanged(nameof(UpdateAllBtnForeground));
            OnPropertyChanged(nameof(UpdateAllBtnBorder));
        });
    }

    public async Task UpdateAllDofFixAsync()
    {
        await _updateOrchestrationService.UpdateAllDofFixAsync(
            _allCards, _dofFixService, DispatcherQueue,
            () =>
            {
                HasUpdatesAvailable = AnyUpdateAvailable;
                OnPropertyChanged(nameof(AnyUpdateAvailable));
                OnPropertyChanged(nameof(UpdateAllBtnBackground));
                OnPropertyChanged(nameof(UpdateAllBtnForeground));
                OnPropertyChanged(nameof(UpdateAllBtnBorder));
            });
    }

    // ── Update checking ───────────────────────────────────────────────────────────

    private async Task CheckForUpdatesAsync(List<GameCardViewModel> cards, List<InstalledModRecord> records, List<AuxInstalledRecord> auxRecords, bool userInitiated = false)
    {
        // ── Cooldown: skip update checks if last check was recent ──────────────
        const int CooldownHours = 4;
        var forceCheck = _forceUpdateCheck;
        _forceUpdateCheck = false;

        if (!forceCheck && DateTime.TryParse(_settingsViewModel.LastUpdateCheckUtc,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var lastCheck))
        {
            var elapsed = DateTime.UtcNow - lastCheck;
            if (elapsed < TimeSpan.FromHours(CooldownHours))
            {
                _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] Cooldown active — last check was {elapsed.TotalMinutes:F0}m ago, skipping API calls");
                return;
            }
        }

        // Nexus GraphQL calls are gated by the "Background Update Checks" setting.
        // "On" (default): full checks including Nexus. "Minimal": Nexus only on explicit user action.
        // forceCheck = true when user clicks Refresh/Update All, so Nexus always runs on explicit action.
        bool nexusEnabled = userInitiated || _settingsViewModel.BackgroundUpdateChecks == "On";

        // "Minimal" background checks: skip all component update checks unless user explicitly triggered.
        // Manifests, PCGW, DLSS manifest, and shader packs still run (they're in RunBackgroundScanAndMergeAsync).
        if (!userInitiated && _settingsViewModel.BackgroundUpdateChecks == "Minimal")
        {
            _crashReporter.Log("[MainViewModel.CheckForUpdatesAsync] BackgroundUpdateChecks=Minimal — skipping all component update checks (user-initiated only)");
            return;
        }

        // If the user changed the channel to Custom after the last install, the record's Channel
        // is stale — patch it here so CheckReShadeUpdateLocal correctly skips custom/legacy channels.
        foreach (var card in cards)
        {
            if (card.RsRecord == null) continue;
            var effectiveChannel = ResolveReShadeChannel(card.GameName, card.Source ?? "");
            if (!string.Equals(card.RsRecord.Channel, effectiveChannel, StringComparison.OrdinalIgnoreCase))
            {
                _crashReporter.Log($"[CheckForUpdatesAsync] Patching RS channel for '{card.GameName}': '{card.RsRecord.Channel}' → '{effectiveChannel}'");
                card.RsRecord.Channel = effectiveChannel;
            }
        }

        await _updateOrchestrationService.CheckForUpdatesAsync(
            cards, records, auxRecords, DispatcherQueue,
            () =>
            {
                HasUpdatesAvailable = AnyUpdateAvailable;
                OnPropertyChanged(nameof(AnyUpdateAvailable));
                OnPropertyChanged(nameof(UpdateAllBtnBackground));
                OnPropertyChanged(nameof(UpdateAllBtnForeground));
                OnPropertyChanged(nameof(UpdateAllBtnBorder));
            },
            skipRdx: _settingsViewModel.GlobalSkipRdxUpdates,
            skipRs: _settingsViewModel.GlobalSkipRsUpdates,
            skipRef: _settingsViewModel.GlobalSkipRefUpdates);

        // ── Rate-limit guard: if earlier checks triggered 403, skip remaining API calls ──
        // Note: Nexus check uses a different API (GraphQL, not GitHub) so it runs regardless
        if (_etagCache.IsRateLimited)
        {
            _crashReporter.Log("[MainViewModel.CheckForUpdatesAsync] GitHub API rate limited — skipping remaining GitHub-based update checks");

            // Still run the Nexus check (uses Nexus GraphQL API, not GitHub) — only if background checks are enabled
            if (nexusEnabled)
            try
            {
                var nexusModsToCheck = cards
                    .Where(c => !c.ExcludeFromUpdateAllRenoDx
                        && (c.Status == GameStatus.Installed || c.Status == GameStatus.Available || c.Status == GameStatus.NotInstalled))
                    .Select(c =>
                    {
                        string? nexusUrl = null;
                        if (c.IsExternalOnly && !string.IsNullOrEmpty(c.ExternalUrl)
                            && c.ExternalUrl.Contains("nexusmods.com", StringComparison.OrdinalIgnoreCase))
                            nexusUrl = c.ExternalUrl;
                        else if (!string.IsNullOrEmpty(c.NexusUrl))
                            nexusUrl = c.NexusUrl;
                        return (c.GameName, NexusUrl: nexusUrl, c.RdxInstalledVersion);
                    })
                    .Where(x => !string.IsNullOrEmpty(x.NexusUrl))
                    .Select(x => (x.GameName, x.NexusUrl!, x.RdxInstalledVersion))
                    .ToList();

                if (nexusModsToCheck.Count > 0)
                {
                    var nexusUpdated = await _nexusUpdateService.CheckForUpdatesAsync(nexusModsToCheck).ConfigureAwait(false);
                    if (nexusUpdated.Count > 0)
                    {
                        DispatcherQueue?.TryEnqueue(() =>
                        {
                            foreach (var card in cards.Where(c => nexusUpdated.Contains(c.GameName)))
                            {
                                if (card.Status == GameStatus.Installed)
                                    card.Status = GameStatus.UpdateAvailable;
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] Nexus update check failed (rate-limited path) — {ex.Message}");
            }

            // ── Nexus update check for Luma mods (rate-limited path) ─────────────
            if (nexusEnabled)
            try
            {
                var lumaModsToCheck = cards
                    .Where(c => c.LumaStatus == GameStatus.Installed
                             && !c.IsHidden
                             && c.LumaNexusUrl != null
                             && c.LumaRecord?.NexusFileId != null)  // only premium installs have a reliable baseline
                    .Select(c => (c.GameName, c.LumaNexusUrl!, c.LumaRecord!.NexusFileId!.Value.ToString()))
                    .ToList();

                if (lumaModsToCheck.Count > 0)
                {
                    _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] Checking {lumaModsToCheck.Count} Luma Nexus mod(s) for updates (rate-limited path)");
                    var lumaUpdated = await _nexusUpdateService.CheckForUpdatesAsync(lumaModsToCheck).ConfigureAwait(false);
                    if (lumaUpdated.Count > 0)
                    {
                        DispatcherQueue?.TryEnqueue(() =>
                        {
                            foreach (var card in cards.Where(c => lumaUpdated.Contains(c.GameName)))
                            {
                                if (card.LumaStatus == GameStatus.Installed)
                                    card.LumaStatus = GameStatus.UpdateAvailable;
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] Luma Nexus update check failed (rate-limited path) — {ex.Message}");
            }

            // Record successful check time even if rate-limited, so we don't hammer the API again immediately
            _settingsViewModel.LastUpdateCheckUtc = DateTime.UtcNow.ToString("o");
            SaveSettingsPublic();
            SaveLibrary();
            _crashReporter.Log("[MainViewModel.CheckForUpdatesAsync] Update check complete (partial, rate limited) — cooldown timestamp saved");
            return;
        }

        // Check ReLimiter for updates (single global check, applies to all cards with UL installed)
        if (!_settingsViewModel.GlobalSkipUlUpdates)
        {
        try
        {
            var ulUpdateAvailable = await CheckUlUpdateAsync(cards).ConfigureAwait(false);
            _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] UL update result: {ulUpdateAvailable}, cards with UL installed: {cards.Count(c => c.UlStatus == GameStatus.Installed)}");
            if (ulUpdateAvailable)
            {
                DispatcherQueue?.TryEnqueue(() =>
                {
                    foreach (var card in cards.Where(c => c.UlStatus == GameStatus.Installed))
                        card.UlStatus = GameStatus.UpdateAvailable;

                    HasUpdatesAvailable = AnyUpdateAvailable;
                    OnPropertyChanged(nameof(AnyUpdateAvailable));
                    OnPropertyChanged(nameof(UpdateAllBtnBackground));
                    OnPropertyChanged(nameof(UpdateAllBtnForeground));
                    OnPropertyChanged(nameof(UpdateAllBtnBorder));
                });
            }
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] UL update check failed — {ex.Message}");
        }
        }

        // Check Display Commander for updates (single global check, applies to all cards with DC installed)
        if (!_settingsViewModel.GlobalSkipDcUpdates)
        {
        try
        {
            var dcUpdateAvailable = await CheckDcUpdateAsync(cards).ConfigureAwait(false);
            _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] DC update result: {dcUpdateAvailable}, cards with DC installed: {cards.Count(c => c.DcStatus == GameStatus.Installed)}");
            if (dcUpdateAvailable)
            {
                DispatcherQueue?.TryEnqueue(() =>
                {
                    foreach (var card in cards.Where(c => c.DcStatus == GameStatus.Installed))
                        card.DcStatus = GameStatus.UpdateAvailable;

                    HasUpdatesAvailable = AnyUpdateAvailable;
                    OnPropertyChanged(nameof(AnyUpdateAvailable));
                    OnPropertyChanged(nameof(UpdateAllBtnBackground));
                    OnPropertyChanged(nameof(UpdateAllBtnForeground));
                    OnPropertyChanged(nameof(UpdateAllBtnBorder));
                });
            }
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] DC update check failed — {ex.Message}");
        }
        }

        // Check OptiScaler for updates (single global check, applies to all cards with OS installed)
        if (!_settingsViewModel.GlobalSkipOsUpdates)
        {
        try
        {
            await _optiScalerService.CheckForUpdateAsync().ConfigureAwait(false);
            var osUpdateAvailable = _optiScalerService.HasUpdate;
            _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] OS stable update result: {osUpdateAvailable}, cards with OS installed: {cards.Count(c => c.OsStatus == GameStatus.Installed)}");

            // Also check nightly if any installed game uses it
            bool anyNightly = cards.Any(c => c.OsStatus == GameStatus.Installed && GetOsVariant(c.GameName, c.Source ?? "") == "Nightly");
            if (anyNightly)
            {
                await _optiScalerService.CheckForNightlyUpdateAsync().ConfigureAwait(false);
                _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] OS nightly update result: {_optiScalerService.HasUpdateNightly}");
            }

            // Also check DLSS NR if any installed game uses it
            bool anyDlssNr = cards.Any(c => c.OsStatus == GameStatus.Installed && GetOsVariant(c.GameName, c.Source ?? "") == "DlssNr");
            if (anyDlssNr)
            {
                await _optiScalerService.CheckForDlssNrUpdateAsync().ConfigureAwait(false);
                _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] OS DLSS NR update result: {_optiScalerService.HasUpdateDlssNr}");
            }

            DispatcherQueue?.TryEnqueue(() =>
            {
                foreach (var card in cards.Where(c => c.OsStatus == GameStatus.Installed))
                {
                    var osVariant = GetOsVariant(card.GameName, card.Source ?? "");
                    bool osHasUpdate = osVariant switch {
                        "Nightly" => _optiScalerService.HasUpdateNightly,
                        "DlssNr"  => _optiScalerService.HasUpdateDlssNr,
                        _         => _optiScalerService.HasUpdate
                    };
                    if (osHasUpdate) card.OsStatus = GameStatus.UpdateAvailable;
                }

                HasUpdatesAvailable = AnyUpdateAvailable;
                OnPropertyChanged(nameof(AnyUpdateAvailable));
                OnPropertyChanged(nameof(UpdateAllBtnBackground));
                OnPropertyChanged(nameof(UpdateAllBtnForeground));
                OnPropertyChanged(nameof(UpdateAllBtnBorder));
            });

            // Check OptiPatcher update and auto-deploy if newer version available
            try
            {
                bool optiPatcherHasUpdate = await _optiScalerService.CheckOptiPatcherUpdateAsync().ConfigureAwait(false);
                if (optiPatcherHasUpdate)
                {
                    _crashReporter.Log("[MainViewModel.CheckForUpdatesAsync] OptiPatcher update available — downloading and auto-deploying");
                    await _optiScalerService.EnsureOptiPatcherStagingAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] OptiPatcher update check failed — {ex.Message}");
            }

            // Check DLSS Enabler update and auto-deploy if newer version available
            try
            {
                var dlssEnablerService = App.Services.GetRequiredService<DlssEnablerService>();
                bool deHasUpdate = await dlssEnablerService.CheckForUpdateAsync().ConfigureAwait(false);
                if (deHasUpdate)
                {
                    await dlssEnablerService.EnsureStagingAsync().ConfigureAwait(false);
                    dlssEnablerService.AutoUpdateStandaloneInstalls(_allCards, this);
                }
            }
            catch (Exception ex)
            {
                _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] DLSS Enabler check failed — {ex.Message}");
            }

            // Check RenoDX DLSS5 addon update and auto-deploy if newer version available
            try
            {
                var rdx5Service = App.Services.GetRequiredService<Renodx5AddonService>();
                bool rdx5HasUpdate = await rdx5Service.CheckForUpdateAsync().ConfigureAwait(false);
                if (rdx5HasUpdate)
                    await rdx5Service.EnsureStagingAsync().ConfigureAwait(false);
                // Also refresh the available versions cache (used by the NR section addon version picker)
                await rdx5Service.FetchAndCacheAvailableVersionsAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] RenoDX DLSS5 addon check failed — {ex.Message}");
            }

            // Check DLSS Tool (ShortFuse) SF variant update
            try
            {
                var rdx5Service = App.Services.GetRequiredService<Renodx5AddonService>();
                bool sfHasUpdate = await rdx5Service.CheckForSfUpdateAsync().ConfigureAwait(false);
                if (sfHasUpdate)
                    await rdx5Service.EnsureSfStagingAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] DLSS Tool SF check failed — {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] OS update check failed — {ex.Message}");
        }
        }

        // Check DXVK for updates — check each variant that's actually in use
        try
        {
            var dxvkCards = cards.Where(c => c.DxvkStatus == GameStatus.Installed).ToList();
            var variantsInUse = dxvkCards
                .Select(c => ResolveDxvkVariant(c.GameName, c.Source ?? ""))
                .Distinct()
                .ToList();

            _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] DXVK variants in use: [{string.Join(", ", variantsInUse)}], cards with DXVK installed: {dxvkCards.Count}");

            foreach (var variant in variantsInUse)
            {
                // Temporarily switch the service variant to check this one
                var savedVariant = _dxvkService.SelectedVariant;
                _dxvkService.SelectedVariant = variant;
                await _dxvkService.CheckForUpdateAsync().ConfigureAwait(false);
                var hasUpdate = _dxvkService.HasUpdate;
                _dxvkService.SelectedVariant = savedVariant;

                _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] DXVK {variant} update result: {hasUpdate}");

                if (hasUpdate)
                {
                    var capturedVariant = variant;
                    DispatcherQueue?.TryEnqueue(() =>
                    {
                        int flagged = 0;
                        foreach (var card in dxvkCards)
                        {
                            var resolvedVariant = ResolveDxvkVariant(card.GameName, card.Source ?? "");
                            if (resolvedVariant == capturedVariant)
                            {
                                card.DxvkStatus = GameStatus.UpdateAvailable;
                                flagged++;
                            }
                        }
                        _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] DXVK {capturedVariant}: flagged {flagged} card(s) as UpdateAvailable");

                        HasUpdatesAvailable = AnyUpdateAvailable;
                        OnPropertyChanged(nameof(AnyUpdateAvailable));
                        OnPropertyChanged(nameof(UpdateAllBtnBackground));
                        OnPropertyChanged(nameof(UpdateAllBtnForeground));
                        OnPropertyChanged(nameof(UpdateAllBtnBorder));
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] DXVK update check failed — {ex.Message}");
        }

        // ── Nexus Mods update check (external-only games with Nexus URLs) ─────────
        if (nexusEnabled)
        try
        {
            // For external-only games, the Nexus URL is in ExternalUrl (set by manifest forceExternalOnly).
            // For wiki-matched games with both Snapshot+Nexus, it's in NexusUrl (from GameMod).
            var nexusModsToCheck = cards
                .Where(c => !c.ExcludeFromUpdateAllRenoDx
                    && (c.Status == GameStatus.Installed || c.Status == GameStatus.Available || c.Status == GameStatus.NotInstalled))
                .Select(c =>
                {
                    // Resolve the Nexus URL: ExternalUrl for external-only, NexusUrl for wiki-matched
                    string? nexusUrl = null;
                    if (c.IsExternalOnly && !string.IsNullOrEmpty(c.ExternalUrl)
                        && c.ExternalUrl.Contains("nexusmods.com", StringComparison.OrdinalIgnoreCase))
                        nexusUrl = c.ExternalUrl;
                    else if (!string.IsNullOrEmpty(c.NexusUrl))
                        nexusUrl = c.NexusUrl;
                    return (c.GameName, NexusUrl: nexusUrl, c.RdxInstalledVersion);
                })
                .Where(x => !string.IsNullOrEmpty(x.NexusUrl))
                .Select(x => (x.GameName, x.NexusUrl!, x.RdxInstalledVersion))
                .ToList();

            if (nexusModsToCheck.Count > 0)
            {
                var nexusUpdated = await _nexusUpdateService.CheckForUpdatesAsync(nexusModsToCheck).ConfigureAwait(false);
                if (nexusUpdated.Count > 0)
                {
                    DispatcherQueue?.TryEnqueue(() =>
                    {
                        foreach (var card in cards.Where(c => nexusUpdated.Contains(c.GameName)))
                        {
                            if (card.Status == GameStatus.Installed)
                                card.Status = GameStatus.UpdateAvailable;
                        }
                        // Nexus updates do NOT contribute to Update All button
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] Nexus update check failed — {ex.Message}");
        }

        // ── Nexus Mods update check for Luma mods ─────────────────────────────
        if (nexusEnabled)
        try
        {
            var lumaModsToCheck = cards
                .Where(c => c.LumaStatus == GameStatus.Installed
                         && !c.IsHidden
                         && c.LumaNexusUrl != null
                         && c.LumaRecord?.NexusFileId != null)  // only premium installs have a reliable baseline
                .Select(c => (c.GameName, c.LumaNexusUrl!, c.LumaRecord!.NexusFileId!.Value.ToString()))
                .ToList();

            if (lumaModsToCheck.Count > 0)
            {
                _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] Checking {lumaModsToCheck.Count} Luma Nexus mod(s) for updates");
                var lumaUpdated = await _nexusUpdateService.CheckForUpdatesAsync(lumaModsToCheck).ConfigureAwait(false);
                if (lumaUpdated.Count > 0)
                {
                    DispatcherQueue?.TryEnqueue(() =>
                    {
                        foreach (var card in cards.Where(c => lumaUpdated.Contains(c.GameName)))
                        {
                            if (card.LumaStatus == GameStatus.Installed)
                                card.LumaStatus = GameStatus.UpdateAvailable;
                        }
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] Luma Nexus update check failed — {ex.Message}");
        }
        try
        {
            var emuCards = cards.Where(c => c.IsEmulator && c.Status == GameStatus.Installed && c.EmulatorAddonNames?.Count > 0).ToList();
            _crashReporter.Log($"[CheckForUpdatesAsync] Emulator update check: {emuCards.Count} card(s) eligible (total emulators={cards.Count(c => c.IsEmulator)}, installed={cards.Count(c => c.IsEmulator && c.Status == GameStatus.Installed)}, hasAddons={cards.Count(c => c.IsEmulator && c.EmulatorAddonNames?.Count > 0)})");
            foreach (var emuCard in emuCards)
            {
                var deployPath = ModInstallService.GetAddonDeployPath(emuCard.InstallPath);
                bool anyUpdate = false;

                foreach (var wikiName in emuCard.EmulatorAddonNames!)
                {
                    var mod = _allMods.FirstOrDefault(m => m.Name.Equals(wikiName, StringComparison.OrdinalIgnoreCase));
                    if (mod?.SnapshotUrl == null) { _crashReporter.Log($"[CheckForUpdatesAsync] Emulator addon '{wikiName}' — no wiki match or no SnapshotUrl"); continue; }

                    var fileName = Path.GetFileName(mod.SnapshotUrl);
                    var localFile = Path.Combine(deployPath, fileName);
                    if (!File.Exists(localFile)) { _crashReporter.Log($"[CheckForUpdatesAsync] Emulator addon '{wikiName}' — file missing: {fileName}"); anyUpdate = true; break; }

                    // Check remote size
                    try
                    {
                        var headResp = await _http.SendAsync(new HttpRequestMessage(HttpMethod.Head, mod.SnapshotUrl)).ConfigureAwait(false);
                        if (headResp.IsSuccessStatusCode && headResp.Content.Headers.ContentLength.HasValue)
                        {
                            var localSize = new FileInfo(localFile).Length;
                            if (headResp.Content.Headers.ContentLength.Value != localSize)
                            {
                                _crashReporter.Log($"[CheckForUpdatesAsync] Emulator addon '{wikiName}' — size mismatch: local={localSize}, remote={headResp.Content.Headers.ContentLength.Value}");
                                anyUpdate = true;
                                break;
                            }
                        }
                    }
                    catch { /* Skip on error — don't flag as update */ }
                }

                if (anyUpdate)
                {
                    _crashReporter.Log($"[CheckForUpdatesAsync] Emulator '{emuCard.GameName}' has updates available");
                    DispatcherQueue?.TryEnqueue(() =>
                    {
                        emuCard.Status = GameStatus.UpdateAvailable;
                        HasUpdatesAvailable = AnyUpdateAvailable;
                        OnPropertyChanged(nameof(AnyUpdateAvailable));
                        OnPropertyChanged(nameof(UpdateAllBtnBackground));
                        OnPropertyChanged(nameof(UpdateAllBtnForeground));
                        OnPropertyChanged(nameof(UpdateAllBtnBorder));
                    });
                }
                else
                {
                    _crashReporter.Log($"[CheckForUpdatesAsync] Emulator '{emuCard.GameName}' is up to date");
                }
            }
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] Emulator update check failed — {ex.Message}");
        }

        // ── DOF Fix update check ──────────────────────────────────────────────
        try
        {
            await _dofFixService.CheckForUpdateAsync().ConfigureAwait(false);
            if (_dofFixService.HasUpdate)
            {
                DispatcherQueue?.TryEnqueue(() =>
                {
                    foreach (var card in cards.Where(c => c.IsDofFixEligible && c.DofFixStatus == GameStatus.Installed))
                        card.DofFixStatus = GameStatus.UpdateAvailable;

                    HasUpdatesAvailable = AnyUpdateAvailable;
                    OnPropertyChanged(nameof(AnyUpdateAvailable));
                    OnPropertyChanged(nameof(UpdateAllBtnBackground));
                    OnPropertyChanged(nameof(UpdateAllBtnForeground));
                    OnPropertyChanged(nameof(UpdateAllBtnBorder));
                });
            }
        }
        catch (Exception ex)
        {
            _crashReporter.Log($"[MainViewModel.CheckForUpdatesAsync] DOF Fix update check failed — {ex.Message}");
        }

        // Record successful check time
        _settingsViewModel.LastUpdateCheckUtc = DateTime.UtcNow.ToString("o");
        SaveSettingsPublic();
        SaveLibrary();
        _crashReporter.Log("[MainViewModel.CheckForUpdatesAsync] Update check complete — cooldown timestamp saved");
    }
}

