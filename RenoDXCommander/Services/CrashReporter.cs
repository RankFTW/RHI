using System.Collections.Concurrent;
using System.Reflection;
using System.Text;

namespace RenoDXCommander.Services;

/// <summary>
/// Captures unhandled exceptions and operational log entries, writing structured
/// crash reports to %LocalAppData%\RHI\logs\.
///
/// Call CrashReporter.Log() at key points throughout the app so each crash file
/// contains a breadcrumb trail of what was happening before the crash.
/// </summary>
public static class CrashReporter
{
    // ── Config ────────────────────────────────────────────────────────────────────

    public static string AppVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v != null ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";
        }
    }

    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "logs");

    /// <summary>Maximum number of crash/error log files kept on disk.</summary>
    private const int MaxLogFiles = 10;

    /// <summary>Maximum breadcrumb entries kept in the in-memory ring buffer.</summary>
    private const int MaxBreadcrumbs = 300;

    // ── Verbose (continuous) logging ──────────────────────────────────────────────

    private static volatile bool _verboseLogging;
    private static readonly object _verboseLogLock = new();

    /// <summary>Session log file path, created fresh each time the app starts.</summary>
    private static readonly string SessionLogPath;

    /// <summary>Maximum number of session log files kept on disk.</summary>
    private const int MaxSessionLogs = 10;

    static CrashReporter()
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            PruneSessionLogs();
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            SessionLogPath = Path.Combine(LogDir, $"session_{timestamp}.txt");
            File.WriteAllText(SessionLogPath,
                $"═══ RHI v{AppVersion} — Session started {DateTime.Now:yyyy-MM-dd HH:mm:ss} ═══{Environment.NewLine}",
                Encoding.UTF8);
        }
        catch { SessionLogPath = Path.Combine(LogDir, "session_fallback.txt"); }
    }

    /// <summary>
    /// When true, additional verbose detail may be included in log entries.
    /// Session logging is always active regardless of this setting.
    /// </summary>
    public static bool VerboseLogging
    {
        get => _verboseLogging;
        set
        {
            _verboseLogging = value;
            if (value)
                Log("Verbose logging enabled");
        }
    }

    // ── Breadcrumb ring buffer ────────────────────────────────────────────────────

    private static readonly ConcurrentQueue<string> _breadcrumbs = new();

    /// <summary>
    /// Log a short message describing what the app is currently doing.
    /// These entries are included in crash reports to show the sequence of events
    /// leading up to the crash. When verbose logging is enabled, the entry is also
    /// appended to RHI_log.txt on disk.
    /// </summary>
    public static void Log(string message)
    {
        var entry = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        _breadcrumbs.Enqueue(entry);

        // Keep the buffer bounded
        while (_breadcrumbs.Count > MaxBreadcrumbs)
            _breadcrumbs.TryDequeue(out _);

        // Always write to the session log file
        AppendSessionLog(entry);
    }

    private static void AppendSessionLog(string entry)
    {
        try
        {
            lock (_verboseLogLock)
            {
                File.AppendAllText(SessionLogPath, entry + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch { /* Never let logging crash the app */ }
    }

    /// <summary>Delete oldest session logs when count exceeds the limit.</summary>
    private static void PruneSessionLogs()
    {
        try
        {
            var files = Directory.GetFiles(LogDir, "session_*.txt")
                .OrderBy(f => f)
                .ToList();

            while (files.Count >= MaxSessionLogs)
            {
                File.Delete(files[0]);
                files.RemoveAt(0);
            }
        }
        catch { }
    }

    // ── Hook registration ─────────────────────────────────────────────────────────

    /// <summary>
    /// Register all available unhandled-exception hooks for a WinUI 3 unpackaged app.
    /// Call once from App constructor, before anything else runs.
    /// </summary>
    public static void Register(Microsoft.UI.Xaml.Application app)
    {
        // 1. CLR thread exceptions (non-UI threads, synchronous throws)
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            WriteCrashReport("AppDomain.UnhandledException", ex,
                isTerminating: e.IsTerminating,
                note: e.IsTerminating ? "Process is terminating." : null);
        };

        // 2. Unobserved Task exceptions (async void, fire-and-forget Tasks)
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteCrashReport("TaskScheduler.UnobservedTaskException", e.Exception,
                note: "Task exception was unobserved. Marking as observed to prevent crash.");
            e.SetObserved(); // Prevent the process from being killed
        };

        // 3. WinUI / XAML dispatcher exceptions
        app.UnhandledException += (_, e) =>
        {
            WriteCrashReport("Microsoft.UI.Xaml.Application.UnhandledException", e.Exception,
                note: $"WinUI exception. Handled = true (app will attempt to continue). Message: {e.Message}");
            e.Handled = true; // Try to keep the app alive
        };

        Log("CrashReporter registered.");
    }

    // ── Report writer ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Write a crash/error report to disk. Safe to call from any thread.
    /// Swallows its own exceptions — the reporter must never cause a secondary crash.
    /// </summary>
    public static void WriteCrashReport(
        string source,
        Exception? ex,
        bool isTerminating = false,
        string? note = null)
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            PruneOldLogs();

            var timestamp  = DateTime.Now;
            var fileName   = $"crash_{timestamp:yyyy-MM-dd_HH-mm-ss}.txt";
            var filePath   = Path.Combine(LogDir, fileName);

            var sb = new StringBuilder();

            // ── Header ──────────────────────────────────────────────────────────
            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine("  RenoDX Mod Manager — Error / Crash Report");
            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine();

            // ── Basic info ───────────────────────────────────────────────────────
            sb.AppendLine($"Timestamp    : {timestamp:yyyy-MM-dd HH:mm:ss} (local)");
            sb.AppendLine($"App version  : {AppVersion}");
            sb.AppendLine($"Source       : {source}");
            sb.AppendLine($"Terminating  : {isTerminating}");
            sb.AppendLine($"OS           : {Environment.OSVersion}");
            sb.AppendLine($"Architecture : {RuntimeInformation()}");
            sb.AppendLine($".NET runtime : {Environment.Version}");
            sb.AppendLine($"Machine      : {Environment.MachineName}");

            if (note != null)
            {
                sb.AppendLine();
                sb.AppendLine($"Note: {note}");
            }

            // ── Exception chain ──────────────────────────────────────────────────
            sb.AppendLine();
            sb.AppendLine("───────────────────────────────────────────────────────────────");
            sb.AppendLine("  Exception Details");
            sb.AppendLine("───────────────────────────────────────────────────────────────");

            if (ex == null)
            {
                sb.AppendLine("(No exception object available)");
            }
            else
            {
                AppendException(sb, ex, depth: 0);
            }

            // ── Breadcrumb trail ─────────────────────────────────────────────────
            sb.AppendLine();
            sb.AppendLine("───────────────────────────────────────────────────────────────");
            sb.AppendLine("  Recent Activity Log (newest last)");
            sb.AppendLine("───────────────────────────────────────────────────────────────");

            var crumbs = _breadcrumbs.ToArray();
            if (crumbs.Length == 0)
            {
                sb.AppendLine("(no breadcrumbs recorded)");
            }
            else
            {
                foreach (var crumb in crumbs)
                    sb.AppendLine(crumb);
            }

            // ── Loaded assemblies (helps detect version conflicts) ───────────────
            sb.AppendLine();
            sb.AppendLine("───────────────────────────────────────────────────────────────");
            sb.AppendLine("  Loaded Assemblies");
            sb.AppendLine("───────────────────────────────────────────────────────────────");
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()
                    .OrderBy(a => a.GetName().Name))
                {
                    var name = asm.GetName();
                    sb.AppendLine($"  {name.Name,-50} {name.Version}");
                }
            }
            catch { sb.AppendLine("(could not enumerate assemblies)"); }

            sb.AppendLine();
            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine($"  End of report — {fileName}");
            sb.AppendLine("═══════════════════════════════════════════════════════════════");

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);

            // Also log that we wrote the report so the next crash knows about this one
            Log($"Crash report written: {fileName}");
        }
        catch
        {
            // Swallow — the reporter must never cause a secondary crash
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static void AppendException(StringBuilder sb, Exception ex, int depth)
    {
        var indent = new string(' ', depth * 4);
        var label  = depth == 0 ? "Exception" : "Inner Exception";

        sb.AppendLine($"{indent}{label}  : {ex.GetType().FullName}");
        sb.AppendLine($"{indent}Message   : {ex.Message}");

        if (!string.IsNullOrEmpty(ex.Source))
            sb.AppendLine($"{indent}Source    : {ex.Source}");

        if (ex.StackTrace != null)
        {
            sb.AppendLine($"{indent}Stack trace:");
            foreach (var line in ex.StackTrace.Split('\n'))
                sb.AppendLine($"{indent}  {line.TrimEnd()}");
        }

        if (ex is AggregateException agg)
        {
            sb.AppendLine($"{indent}Aggregate inner exceptions ({agg.InnerExceptions.Count}):");
            for (int i = 0; i < agg.InnerExceptions.Count; i++)
            {
                sb.AppendLine($"{indent}  [{i}]");
                AppendException(sb, agg.InnerExceptions[i], depth + 1);
            }
        }
        else if (ex.InnerException != null)
        {
            sb.AppendLine();
            AppendException(sb, ex.InnerException, depth + 1);
        }
    }

    private static void PruneOldLogs()
    {
        try
        {
            var files = Directory.GetFiles(LogDir, "crash_*.txt")
                .OrderBy(f => f)
                .ToList();

            while (files.Count >= MaxLogFiles)
            {
                File.Delete(files[0]);
                files.RemoveAt(0);
            }
        }
        catch { }
    }

    private static string RuntimeInformation()
    {
        try { return System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(); }
        catch { return "unknown"; }
    }
}
