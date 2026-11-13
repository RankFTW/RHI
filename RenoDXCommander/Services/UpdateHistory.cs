using Microsoft.Extensions.DependencyInjection;

namespace RenoDXCommander.Services;

/// <summary>
/// Adds entries to the Update History window (<see cref="IUpdateLogService"/>) for components
/// that replace a previously staged / installed version with a different one.
/// First-time downloads (no previous version) and re-downloads of the same version are skipped.
/// </summary>
internal static class UpdateHistory
{
    /// <summary>
    /// True when <paramref name="newVersion"/> is a real update of <paramref name="oldVersion"/>:
    /// both known, new not "unknown", and different (case-insensitive, surrounding whitespace ignored).
    /// </summary>
    internal static bool ShouldRecord(string? oldVersion, string? newVersion)
    {
        var o = oldVersion?.Trim();
        var n = newVersion?.Trim();
        if (string.IsNullOrEmpty(o) || string.IsNullOrEmpty(n)) return false;
        if (string.Equals(n, "unknown", StringComparison.OrdinalIgnoreCase)) return false;
        return !string.Equals(o, n, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Records an update when <see cref="ShouldRecord"/> allows it. Never throws.</summary>
    public static void Record(string category, string componentName, string? oldVersion, string? newVersion,
        long? sizeBytes = null)
    {
        if (!ShouldRecord(oldVersion, newVersion)) return;

        try
        {
            var services = App.Services;
            if (services == null) return; // unit tests: no DI container
            services.GetRequiredService<IUpdateLogService>().Record(new Models.UpdateLogEntry
            {
                Timestamp     = DateTime.UtcNow,
                Category      = category,
                ComponentName = componentName,
                OldVersion    = oldVersion!.Trim(),
                NewVersion    = newVersion!.Trim(),
                SizeBytes     = sizeBytes,
            });
        }
        catch (InvalidOperationException ex)
        {
            CrashReporter.Log($"[UpdateHistory.Record] Could not write update history for {componentName} — {ex.Message}");
        }
        catch (IOException ex)
        {
            CrashReporter.Log($"[UpdateHistory.Record] Could not write update history for {componentName} — {ex.Message}");
        }
    }
}
