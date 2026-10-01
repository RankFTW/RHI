namespace RenoDXCommander.Services;

// Shared .original contract: empty backup means RHI created the file; nonempty means restore it.
// Errors propagate so a failed backup can never be followed by an overwrite.
public static class SentinelFiles
{
    public const string Suffix = ".original";
    public static string BackupOf(string path) => path + Suffix;
    public static bool Placed(string path) => File.Exists(BackupOf(path));

    public static void Backup(string destination, bool moveOriginal = false)
    {
        var backup = BackupOf(destination);
        if (File.Exists(backup)) return;
        if (File.Exists(destination))
        {
            if (moveOriginal) File.Move(destination, backup);
            else File.Copy(destination, backup);
        }
        else File.WriteAllBytes(backup, Array.Empty<byte>());
    }

    public static void Deploy(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        Backup(destination);
        Copy(source, destination);
    }

    public static bool DeployIfAbsent(string source, string destination)
    {
        if (File.Exists(destination) && !Placed(destination)) return false;
        Deploy(source, destination);
        return true;
    }

    public static void Restore(string destination)
    {
        var backup = BackupOf(destination);
        if (!File.Exists(backup)) return;
        if (new FileInfo(backup).Length == 0)
        {
            if (File.Exists(destination)) File.Delete(destination);
            File.Delete(backup);
        }
        else File.Move(backup, destination, true);
    }

    public static void Copy(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temp = destination + ".rhi-" + Guid.NewGuid().ToString("N");
        try { File.Copy(source, temp); File.Move(temp, destination, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
