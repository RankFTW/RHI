namespace RenoDXCommander.Services;

/// <summary>
/// Extension methods for safe async fire-and-forget patterns.
/// </summary>
public static class AsyncExtensions
{
    /// <summary>
    /// Safely executes a fire-and-forget <see cref="Task"/>, catching any unhandled
    /// exceptions and logging them via <see cref="CrashReporter"/> instead of allowing
    /// them to propagate as unobserved task exceptions.
    /// </summary>
    /// <param name="task">The task to await.</param>
    /// <param name="context">
    /// A short label identifying the call site (e.g. "MainWindow.Init") included in the
    /// log entry so the source of the error is immediately obvious.
    /// </param>
    public static async void SafeFireAndForget(this Task task, string context)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[{context}] Unobserved async error — {ex.Message}");
        }
    }
}
