namespace RHI.Linux.Core;

/// <summary>Portable releases carry a static 7-Zip; source builds can use the host tool.</summary>
public static class ArchiveTools
{
    public static string SevenZip => ResolveSevenZip(AppContext.BaseDirectory, Environment.GetEnvironmentVariable("PATH"));

    public static string ResolveSevenZip(string applicationDirectory, string? searchPath)
    {
        var bundled = Path.Combine(applicationDirectory, "tools", "7zz");
        if (Executable(bundled)) return bundled;

        foreach (var name in new[] { "7zz", "7z", "7za" })
        {
            foreach (var directory in (searchPath ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(directory, name);
                if (Executable(candidate)) return candidate;
            }
        }

        throw new IOException("The archive extractor is missing. Run scripts/build-linux.sh to build a complete portable package, or install 7zip for a source-only run.");
    }

    private static bool Executable(string path)
    {
        if (!File.Exists(path)) return false;
        if (!OperatingSystem.IsLinux()) return true;
        return (File.GetUnixFileMode(path) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
    }
}
