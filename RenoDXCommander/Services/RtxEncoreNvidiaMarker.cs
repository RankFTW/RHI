// RtxEncoreNvidiaMarker.cs -- the record of NVIDIA files RHI placed beside the game's exe for RTX Encore
// in a game without DLSS: <installPath>\rtx-encore-runtimes\nvidia\rhi_installed.txt, one line per file,
// "name|size|sha256". DLSS detection reads it (size check only) so these files are never shown as the
// game's DLSS / Streamline, and never swapped or auto-updated: RTX Encore accepts only exact builds.

namespace RenoDXCommander.Services;

public static class RtxEncoreNvidiaMarker
{
    /// <summary>Marker path relative to the folder holding the files.</summary>
    public const string RelPath = @"rtx-encore-runtimes\nvidia\rhi_installed.txt";
    public const string RuntimesFolder = "rtx-encore-runtimes";
    public const string NvidiaFolder = "nvidia";

    public sealed record Entry(string Name, long Size, string Sha256);

    public static string MarkerPath(string folder) => Path.Combine(folder, RelPath);

    /// <summary>
    /// Parses marker text. Lines that aren't "name|size|sha256" with a plain file name (no path parts),
    /// a non-negative size and a 64-hex hash are skipped. Duplicate names keep the first line.
    /// </summary>
    public static List<Entry> Parse(string? text)
    {
        var list = new List<Entry>();
        if (string.IsNullOrEmpty(text)) return list;
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var parts = line.Split('|');
            if (parts.Length != 3) continue;
            var name = parts[0].Trim();
            if (name.Length == 0 || name != Path.GetFileName(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) continue;
            if (!long.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var size)) continue;
            var sha = parts[2].Trim().ToLowerInvariant();
            if (sha.Length != 64 || !sha.All(Uri.IsHexDigit)) continue;
            if (list.Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
            list.Add(new Entry(name, size, sha));
        }
        return list;
    }

    public static string Format(IEnumerable<Entry> entries) =>
        string.Concat(entries.Select(e => $"{e.Name}|{e.Size.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{e.Sha256.ToLowerInvariant()}\r\n"));

    /// <summary>Entries of the marker in <paramref name="folder"/>; empty when there is none or it can't be read.</summary>
    public static List<Entry> Read(string folder)
    {
        var path = MarkerPath(folder);
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : new List<Entry>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[RtxEncoreNvidiaMarker.Read] Could not read '{path}' — {ex.Message}");
            return new List<Entry>();
        }
    }

    /// <summary>
    /// True when <paramref name="fileName"/> is listed and the file's size still matches: RHI placed it for
    /// RTX Encore and nothing has replaced it since. A different size means the game or the user replaced it.
    /// </summary>
    public static bool IsOwned(IReadOnlyList<Entry> entries, string fileName, long actualSize) =>
        entries.Any(e => e.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase) && e.Size == actualSize);

    /// <summary>
    /// For DLSS detection: whether the DLL at <paramref name="filePath"/> is one of RTX Encore's NVIDIA files.
    /// Reads the marker beside it (callers cache per folder with <paramref name="cache"/>).
    /// </summary>
    public static bool IsOwnedFile(string filePath, Dictionary<string, List<Entry>>? cache = null)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(dir)) return false;
        List<Entry> entries;
        if (cache == null || !cache.TryGetValue(dir, out entries!))
        {
            entries = File.Exists(MarkerPath(dir)) ? Read(dir) : new List<Entry>();
            if (cache != null) cache[dir] = entries;
        }
        if (entries.Count == 0) return false;
        try
        {
            var fi = new FileInfo(filePath);
            return fi.Exists && IsOwned(entries, fi.Name, fi.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[RtxEncoreNvidiaMarker.IsOwnedFile] Could not check '{filePath}' — {ex.Message}");
            return false;
        }
    }
}
