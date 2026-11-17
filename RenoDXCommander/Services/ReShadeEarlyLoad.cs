// ReShadeEarlyLoad.cs — edits ReShade's [ADDON] LoadFromDllMain list.
// ReShade reads LoadFromDllMain as a list of add-on file names (comma separated, relative to AddonPath)
// and loads each from DllMain (dll_main.cpp: config.get("ADDON", "LoadFromDllMain", std::vector<path>)).
// Several add-ons can need it at once (DLSS Fix, MFG Unlock on RTX 20/30 and for Add frame generation,
// which adds itself), so RHI only adds or removes its own entry and keeps the others.

namespace RenoDXCommander.Services;

public static class ReShadeEarlyLoad
{
    /// <summary>Splits a LoadFromDllMain value into its entries (trimmed, empty ones dropped).</summary>
    public static List<string> Parse(string? value) =>
        (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>True when an entry names <paramref name="fileName"/> (the entry may carry a folder).</summary>
    public static bool Contains(string? value, string fileName) =>
        Parse(value).Any(e => IsFile(e, fileName));

    /// <summary>Returns the value with <paramref name="fileName"/> added at the end, unless it's already listed.</summary>
    public static string Add(string? value, string fileName)
    {
        var entries = Parse(value);
        if (!entries.Any(e => IsFile(e, fileName))) entries.Add(fileName);
        return string.Join(",", entries);
    }

    /// <summary>
    /// Returns the value without the entries <paramref name="match"/> accepts (given each entry's file name),
    /// or null when nothing is left (the key should then be removed).
    /// </summary>
    public static string? Remove(string? value, Func<string, bool> match)
    {
        var entries = Parse(value).Where(e => !match(FileNameOf(e))).ToList();
        return entries.Count == 0 ? null : string.Join(",", entries);
    }

    private static bool IsFile(string entry, string fileName) =>
        string.Equals(FileNameOf(entry), fileName, StringComparison.OrdinalIgnoreCase);

    private static string FileNameOf(string entry) => Path.GetFileName(entry.Replace('/', '\\'));
}
