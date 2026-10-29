using System.Runtime.InteropServices;
using System.Text.Json;

namespace RHI.Linux.Core;

public static class LinuxPaths
{
    public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static string Data => Path.Combine(Environment.GetEnvironmentVariable("XDG_DATA_HOME") ?? Path.Combine(Home, ".local/share"), "rhi-linux");
    public static string Cache => Path.Combine(Environment.GetEnvironmentVariable("XDG_CACHE_HOME") ?? Path.Combine(Home, ".cache"), "rhi-linux");
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    [DllImport("libc", SetLastError = true)] private static extern IntPtr realpath(string path, IntPtr buffer);
    [DllImport("libc")] private static extern void free(IntPtr ptr);

    public static string Canonical(string path)
    {
        path = Path.GetFullPath(path);
        if (!OperatingSystem.IsLinux()) return path;
        var ptr = realpath(path, IntPtr.Zero);
        if (ptr == IntPtr.Zero) return path;
        try { return Marshal.PtrToStringUTF8(ptr)!; }
        finally { free(ptr); }
    }

    public static bool IsWithin(string root, string path) =>
        Canonical(path).StartsWith(Canonical(root).TrimEnd('/') + "/", StringComparison.Ordinal);

    // Windows manifest paths must be resolved case-insensitively on Linux.
    // Ambiguous casing is an error rather than creating two DLLs Wine considers identical.
    public static string ResolveCase(string root, string relative)
    {
        if (Path.IsPathRooted(relative) || relative.Contains(':')) throw new IOException("Expected a relative game path.");
        var current = Canonical(root);
        foreach (var segment in relative.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment is "." or "..") throw new IOException("Relative traversal is not allowed.");
            var matches = Directory.Exists(current) ? Directory.EnumerateFileSystemEntries(current)
                .Where(p => Path.GetFileName(p).Equals(segment, StringComparison.OrdinalIgnoreCase)).ToArray() : [];
            if (matches.Length > 1) throw new IOException($"Ambiguous filename casing: {current}/{segment}");
            current = matches.FirstOrDefault() ?? Path.Combine(current, segment);
            if (!IsWithin(root, current)) throw new IOException($"Path escapes the game directory: {relative}");
        }
        return current;
    }

    public static void WriteJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(value, Json)); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
