// ExtractionCacheCleaner.cs — removes old single-file extraction folders.
// RHI ships as one exe; .NET unpacks its bundled DLLs to %TEMP%\.net\RHI\<build hash>\ and never
// deletes old builds, so every update left ~160 MB behind. On launch this deletes the other build
// folders and keeps the one RHI is running from. A folder another running RHI still uses is skipped:
// its Microsoft.ui.xaml.dll is loaded, and Windows refuses to delete a loaded DLL.

namespace RenoDXCommander.Services;

public static class ExtractionCacheCleaner
{
    /// <summary>Always loaded by a running RHI, so deleting it first tells whether a folder is in use.</summary>
    private const string InUseProbe = "Microsoft.ui.xaml.dll";

    public static async Task RunAsync(CancellationToken token)
    {
        try
        {
            // Let startup finish first; nothing here is urgent
            await Task.Delay(TimeSpan.FromSeconds(30), token).ConfigureAwait(false);
            await Task.Run(() => Clean(token), token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* app closing */ }
        catch (Exception ex)
        {
            CrashReporter.Log($"[ExtractionCacheCleaner.RunAsync] {ex.Message}");
        }
    }

    private static void Clean(CancellationToken token)
    {
        // Only when running unpacked from a single-file bundle: BaseDirectory is then the extraction folder
        var current = AppContext.BaseDirectory.TrimEnd('\\', '/');
        var exeDir = (Path.GetDirectoryName(Environment.ProcessPath) ?? "").TrimEnd('\\', '/');
        if (string.IsNullOrEmpty(current) || current.Equals(exeDir, StringComparison.OrdinalIgnoreCase)) return;

        // Expect ...\.net\RHI\<hash>: never touch anything that doesn't look like that
        var appRoot = Path.GetDirectoryName(current);
        if (appRoot == null
            || !Path.GetFileName(appRoot).Equals("RHI", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(Path.Combine(current, InUseProbe)))
        {
            CrashReporter.Log($"[ExtractionCacheCleaner.Clean] Unexpected extraction folder '{current}', skipped");
            return;
        }

        int removed = 0, inUse = 0, failed = 0;
        long freed = 0;
        foreach (var dir in Directory.EnumerateDirectories(appRoot))
        {
            if (token.IsCancellationRequested) break;
            if (dir.TrimEnd('\\', '/').Equals(current, StringComparison.OrdinalIgnoreCase)) continue;

            var probe = Path.Combine(dir, InUseProbe);
            // Not an RHI build folder (no probe file and not empty): leave it alone
            if (!File.Exists(probe) && Directory.EnumerateFileSystemEntries(dir).Any()) continue;
            long size = FolderSize(dir);
            try
            {
                if (File.Exists(probe)) File.Delete(probe); // throws if another RHI has it loaded
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                inUse++;
                continue;
            }
            try
            {
                Directory.Delete(dir, recursive: true);
                removed++;
                freed += size;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                failed++;
                CrashReporter.Log($"[ExtractionCacheCleaner.Clean] Couldn't fully remove '{Path.GetFileName(dir)}' — {ex.Message}");
            }
        }

        if (removed + inUse + failed > 0)
            CrashReporter.Log($"[ExtractionCacheCleaner.Clean] Removed {removed} old build folder(s) ({freed / (1024 * 1024):N0} MB), {inUse} in use, {failed} failed");
    }

    private static long FolderSize(string dir)
    {
        try
        {
            return new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return 0;
        }
    }
}
