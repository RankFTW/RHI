// AntivirusGuard.cs — notices when an antivirus deletes a file RHI has just downloaded.
//
// Some components (32-bit DXVK DLLs, Universal MFG Unlocker) are regularly flagged as false
// positives. Without this, RHI downloaded them again on every launch and the antivirus flagged
// them again, with no explanation for the user. Now a component whose files vanish right after
// staging is remembered as blocked: automatic (startup / background) downloads skip it until
// the user installs it themselves or a new version comes out, and the user is told once.

using System.Text.Json;

namespace RenoDXCommander.Services;

internal static class AntivirusGuard
{
    private sealed class Entry
    {
        public string? Version { get; set; }
        public string? Path { get; set; }
        public DateTime Utc { get; set; }
    }

    private static readonly object Lock = new();

    private static readonly string StateFile = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "av_blocked.json");

    /// <summary>How long to wait after staging before checking the files are still there.</summary>
    private const int VerifyDelayMs = 3000;

    // HRESULT_FROM_WIN32(ERROR_VIRUS_INFECTED / ERROR_VIRUS_DELETED)
    private const int HrVirusInfected = unchecked((int)0x800700E1);
    private const int HrVirusDeleted  = unchecked((int)0x800700E2);

    /// <summary>
    /// Raised (on a background thread) when an antivirus removed a staged file:
    /// (component display name, path of the missing file).
    /// </summary>
    internal static event Action<string, string>? Blocked;

    /// <summary>True when the exception is Windows reporting that a file was blocked as a virus.</summary>
    internal static bool IsVirusError(Exception ex) =>
        ex.HResult == HrVirusInfected || ex.HResult == HrVirusDeleted
        || (ex.InnerException != null && IsVirusError(ex.InnerException));

    /// <summary>
    /// Waits a moment, then checks <paramref name="files"/> still exist. Returns true when they do
    /// (and clears any earlier block). Otherwise records the component as blocked and raises
    /// <see cref="Blocked"/>.
    /// </summary>
    internal static async Task<bool> VerifyStagedAsync(string component, string? version, IEnumerable<string> files)
    {
        var list = files.ToList();
        try { await Task.Delay(VerifyDelayMs).ConfigureAwait(false); }
        catch (TaskCanceledException) { return true; }

        var missing = list.FirstOrDefault(f => !File.Exists(f));
        if (missing == null)
        {
            Clear(component);
            return true;
        }
        Report(component, version, missing);
        return false;
    }

    /// <summary>
    /// Records a block found another way (a virus error while extracting). Logs, saves and raises
    /// <see cref="Blocked"/>.
    /// </summary>
    internal static void Report(string component, string? version, string path)
    {
        CrashReporter.Log($"[AntivirusGuard] '{component}' {version ?? ""}: '{path}' was removed right after download — antivirus likely deleted it. Automatic downloads paused.");
        lock (Lock)
        {
            var state = Load();
            state[component] = new Entry { Version = version, Path = path, Utc = DateTime.UtcNow };
            Save(state);
        }
        try { Blocked?.Invoke(component, path); }
        catch (Exception ex) { CrashReporter.Log($"[AntivirusGuard.Report] Handler failed — {ex.Message}"); }
    }

    /// <summary>
    /// True when automatic downloads of <paramref name="component"/> should be skipped. Passing the
    /// latest available version lets a new release through (it may not be flagged).
    /// </summary>
    internal static bool IsBlocked(string component, string? latestVersion = null)
    {
        lock (Lock)
        {
            if (!Load().TryGetValue(component, out var e)) return false;
            if (!string.IsNullOrEmpty(latestVersion) && !string.IsNullOrEmpty(e.Version)
                && !string.Equals(latestVersion, e.Version, StringComparison.OrdinalIgnoreCase))
                return false;
            return true;
        }
    }

    internal static void Clear(string component)
    {
        lock (Lock)
        {
            var state = Load();
            if (state.Remove(component))
            {
                Save(state);
                CrashReporter.Log($"[AntivirusGuard] '{component}' staged files are intact again — block cleared");
            }
        }
    }

    private static Dictionary<string, Entry> Load()
    {
        try
        {
            if (File.Exists(StateFile))
                return JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(StateFile))
                       ?? new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            CrashReporter.Log($"[AntivirusGuard.Load] {ex.Message}");
        }
        return new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
    }

    private static void Save(Dictionary<string, Entry> state)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(StateFile)!);
            File.WriteAllText(StateFile, JsonSerializer.Serialize(state));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[AntivirusGuard.Save] {ex.Message}");
        }
    }
}
