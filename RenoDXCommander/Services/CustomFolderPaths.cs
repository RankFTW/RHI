namespace RenoDXCommander.Services;

/// <summary>Outcome of trying to add a folder to the custom game folder list.</summary>
public enum CustomFolderAddResult
{
    Added,
    /// <summary>The same folder (after normalisation) is already configured.</summary>
    Duplicate,
    /// <summary>Empty / malformed path.</summary>
    Invalid,
    /// <summary>A Windows / system location that must never be scanned for games.</summary>
    Unsafe,
}

/// <summary>
/// Path handling for the "Custom Game Folders" feature: normalisation, containment checks,
/// duplicate prevention and rejection of system locations. Pure string logic — nothing here
/// touches the file system, so folders on disconnected drives can still be stored and compared.
/// </summary>
public static class CustomFolderPaths
{
    /// <summary>
    /// Returns the full path with a single canonical spelling (backslashes, no trailing
    /// separator except on a drive root), or null if the input is empty or not a valid path.
    /// Existence is deliberately not checked so removable/offline drives stay valid.
    /// </summary>
    public static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var full = Path.GetFullPath(path.Trim().Trim('"'));
            var rootLen = Path.GetPathRoot(full)?.Length ?? 0;
            if (full.Length > rootLen)
                full = full.TrimEnd('\\', '/');
            return full;
        }
        catch (Exception) { return null; }
    }

    /// <summary>Normalises every entry, drops invalid ones and duplicates, and keeps the original order.</summary>
    public static List<string> NormalizeList(IEnumerable<string>? paths)
    {
        var result = new List<string>();
        if (paths == null) return result;
        foreach (var p in paths)
        {
            var n = Normalize(p);
            if (n != null && !result.Any(r => string.Equals(r, n, StringComparison.OrdinalIgnoreCase)))
                result.Add(n);
        }
        return result;
    }

    /// <summary>True when both paths normalise to the same location (case-insensitive).</summary>
    public static bool AreSame(string? a, string? b)
    {
        var na = Normalize(a);
        var nb = Normalize(b);
        return na != null && nb != null && string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when <paramref name="child"/> equals or lies below <paramref name="parent"/>.</summary>
    public static bool IsSameOrUnder(string? child, string? parent)
    {
        var nc = Normalize(child);
        var np = Normalize(parent);
        if (nc == null || np == null) return false;
        if (string.Equals(nc, np, StringComparison.OrdinalIgnoreCase)) return true;
        var prefix = np.EndsWith('\\') ? np : np + "\\";
        return nc.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when the two locations are the same or one contains the other.</summary>
    public static bool Overlaps(string? a, string? b) => IsSameOrUnder(a, b) || IsSameOrUnder(b, a);

    /// <summary>
    /// Locations that are never scanned, even if the user picks them by accident: the Windows
    /// directory (and everything below it), the root of the system drive, and ProgramData.
    /// The optional parameters exist so tests do not depend on the machine they run on.
    /// </summary>
    public static bool IsUnsafeRoot(string? path, string? windowsDir = null, string? programData = null)
    {
        var n = Normalize(path);
        if (n == null) return true;

        windowsDir ??= Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        programData ??= Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        if (!string.IsNullOrEmpty(windowsDir))
        {
            if (IsSameOrUnder(n, windowsDir)) return true;
            // Root of the drive Windows is installed on (e.g. C:\) — far too broad to scan.
            var sysRoot = Path.GetPathRoot(Normalize(windowsDir));
            if (sysRoot != null && string.Equals(n, Normalize(sysRoot), StringComparison.OrdinalIgnoreCase))
                return true;
        }
        if (!string.IsNullOrEmpty(programData) && IsSameOrUnder(n, programData)) return true;
        return false;
    }

    /// <summary>
    /// Adds <paramref name="path"/> to <paramref name="folders"/> unless it is invalid, unsafe or
    /// already present (compared after normalisation). The stored spelling is the normalised path.
    /// </summary>
    public static CustomFolderAddResult TryAdd(IList<string> folders, string? path, out string? normalized,
        string? windowsDir = null, string? programData = null)
    {
        var norm = Normalize(path);
        normalized = norm;
        if (norm == null) return CustomFolderAddResult.Invalid;
        if (IsUnsafeRoot(norm, windowsDir, programData)) return CustomFolderAddResult.Unsafe;
        if (folders.Any(f => AreSame(f, norm))) return CustomFolderAddResult.Duplicate;
        folders.Add(norm);
        return CustomFolderAddResult.Added;
    }

    /// <summary>Removes the configured root only; never touches anything on disk.</summary>
    public static bool Remove(IList<string> folders, string? path)
    {
        for (int i = folders.Count - 1; i >= 0; i--)
        {
            if (AreSame(folders[i], path))
            {
                folders.RemoveAt(i);
                return true;
            }
        }
        return false;
    }

    /// <summary>True if the folder exists right now. Never throws (offline drives, permissions).</summary>
    public static bool IsAvailable(string? path)
    {
        try { return !string.IsNullOrEmpty(path) && Directory.Exists(path); }
        catch (Exception) { return false; }
    }
}
