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

    /// <summary>Raised when a second instance sends a file path.</summary>
    public static event Action<string>? FileReceived;

    /// <summary>
    /// Tries to acquire the single-instance mutex.
    /// Returns true if this is the first instance, false if another is already running.
    /// </summary>
    public static bool TryAcquire()
    {
        _mutex = new Mutex(true, MutexName, out bool createdNew);
        return createdNew;
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
        _cts = new CancellationTokenSource();
        Task.Run(() => ListenLoop(_cts.Token));
    }

    private static async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(ct);
                using var reader = new StreamReader(server);
                var line = await reader.ReadLineAsync(ct);
                if (!string.IsNullOrWhiteSpace(line))
                    FileReceived?.Invoke(line);
            }
            catch (OperationCanceledException) { break; }
            catch { /* Log and continue listening */ }
        }
    }

    public static void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
    }
}
