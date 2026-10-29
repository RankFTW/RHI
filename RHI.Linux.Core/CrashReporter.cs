namespace RenoDXCommander.Services;

// The portable services report to a local log without Windows crash dialogs.
public static class CrashReporter
{
    private static readonly object Gate = new();
    public static void Log(string message)
    {
        lock (Gate)
        {
            try
            {
                var path = Path.Combine(RHI.Linux.Core.LinuxPaths.Data, "rhi.log");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path) && new FileInfo(path).Length > 2_000_000)
                    File.Move(path, path + ".old", true);
                File.AppendAllText(path, $"{DateTimeOffset.Now:u} {message}\n");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
