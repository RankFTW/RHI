using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace RenoDXCommander.Services;

/// <summary>
/// Ensures only one instance of RDXC runs at a time.
/// If a second instance launches with a file argument, it forwards the path
/// to the running instance via a named pipe and exits.
/// </summary>
public static class SingleInstanceService
{
    private const string MutexName = "RenoDXCommander_SingleInstance";
    private const string PipeName = "RenoDXCommander_AddonPipe";
    private static Mutex? _mutex;
    private static CancellationTokenSource? _cts;
    private static Task? _listenerTask;

    /// <summary>Raised when a second instance sends a file path.</summary>
    public static event Action<string>? FileReceived;

    /// <summary>
    /// Tries to acquire the single-instance mutex.
    /// Returns true if this is the first instance, false if another is already running.
    /// </summary>
    public static bool TryAcquire()
    {
        if (_mutex != null) return true;
        var mutex = new Mutex(false, MutexName);
        bool acquired;
        try { acquired = mutex.WaitOne(0); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired)
        {
            mutex.Dispose();
            return false;
        }
        _mutex = mutex;
        return true;
    }

    /// <summary>
    /// Sends a file path to the running instance via named pipe, then returns.
    /// </summary>
    public static void SendToRunningInstance(string filePath)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(3000); // 3s timeout
            // A user-launched secondary instance can transfer its foreground rights
            // to the existing instance before asking that instance to show itself.
            if (NativeInterop.GetNamedPipeServerProcessId(client.SafePipeHandle, out var owner) && owner != 0)
            {
                var granted = NativeInterop.AllowSetForegroundWindow(owner);
                CrashReporter.Log($"[Foreground] Existing-instance grant to PID {owner}: {granted}");
            }
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(filePath);
        }
        catch { /* Running instance may not be listening yet — silently fail */ }
    }

    /// <summary>
    /// Starts listening for file paths from subsequent instances.
    /// Call this from the first (owning) instance after the window is created.
    /// </summary>
    public static void StartListening()
    {
        if (_cts != null) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _listenerTask = Task.Run(() => ListenLoop(token));
    }

    private static async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var pipeSecurity = new System.IO.Pipes.PipeSecurity();
                pipeSecurity.AddAccessRule(new System.IO.Pipes.PipeAccessRule(
                    new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.WorldSid, null),
                    System.IO.Pipes.PipeAccessRights.ReadWrite,
                    System.Security.AccessControl.AccessControlType.Allow));

                using var server = NamedPipeServerStreamAcl.Create(PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, pipeSecurity);
                await server.WaitForConnectionAsync(ct);
                using var reader = new StreamReader(server);
                var line = await reader.ReadLineAsync(ct);
                if (!string.IsNullOrWhiteSpace(line))
                    FileReceived?.Invoke(line);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                CrashReporter.Log($"[SingleInstance] Listener failed — {ex.Message}");
                // Do not hot-spin if pipe creation repeatedly fails.
                try { await Task.Delay(250, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    public static void Stop()
    {
        var cancellation = _cts;
        _cts = null;
        cancellation?.Cancel();
        if (cancellation != null)
        {
            var listener = _listenerTask ?? Task.CompletedTask;
            _ = listener.ContinueWith(_ => cancellation.Dispose(), TaskScheduler.Default);
        }
        _listenerTask = null;
        FileReceived = null;
        var mutex = _mutex;
        _mutex = null;
        if (mutex != null)
        {
            try { mutex.ReleaseMutex(); }
            finally { mutex.Dispose(); }
        }
    }
}
