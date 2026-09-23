using System.IO;

namespace RenoDXCommander.Services;

/// <summary>
/// Shared file-write utilities with built-in retry logic for transient IO failures.
/// </summary>
public static class FileHelper
{
    /// <summary>
    /// Writes <paramref name="content"/> to <paramref name="path"/> with up to 3 attempts.
    /// On transient <see cref="IOException"/>, retries immediately without blocking.
    /// On non-IOException or final failure, logs via <see cref="CrashReporter"/> using
    /// <paramref name="callerTag"/> and returns without throwing.
    /// </summary>
    /// <param name="path">Destination file path.</param>
    /// <param name="content">Text content to write.</param>
    /// <param name="callerTag">
    /// Context string for log messages, e.g. "ModInstallService.SaveDb".
    /// </param>
    public static void WriteAllTextWithRetry(string path, string content, string callerTag)
        => WriteAllTextWithRetry(path, content, callerTag, File.WriteAllText);

    /// <summary>
    /// Internal overload that accepts a write delegate for testability.
    /// </summary>
    internal static void WriteAllTextWithRetry(string path, string content, string callerTag, Action<string, string> writeAction)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                writeAction(path, content);
                return;
            }
            catch (IOException ex) when (attempt < 2)
            {
                // Log and retry immediately — no Thread.Sleep to avoid blocking UI thread
                CrashReporter.Log($"[{callerTag}] IO retry {attempt + 1}: {ex.Message}");
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[{callerTag}] Failed to write file — {ex.Message}");
                return;
            }
        }
    }
}
