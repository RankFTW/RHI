using System.IO.Compression;

namespace RenoDXCommander.Services;

// Windows archives may contain backslashes and omit Unix permission bits.
public static class DllArchive
{
    public static string EntryPath(ZipArchiveEntry entry) => entry.FullName.Replace('\\', '/');
    public static string EntryName(ZipArchiveEntry entry) => EntryPath(entry).Split('/').Last();

    public static void Extract(ZipArchiveEntry entry, string destination)
    {
        using var input = entry.Open();
        using var output = File.Create(destination);
        input.CopyTo(output);
    }

    public static void ExtractDll(string archivePath, string destination, string name)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var entry = archive.Entries.FirstOrDefault(e => EntryName(e).Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new FileNotFoundException($"{name} not found inside {Path.GetFileName(archivePath)}");
        Extract(entry, destination);
    }
}
