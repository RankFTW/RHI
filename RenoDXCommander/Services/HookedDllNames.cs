// HookedDllNames.cs — "<name>Hooked.dll" names for extras installed beside Ultimate ASI Loader.
// When ASI Loader is a game's X.dll it loads XHooked.dll from the same folder as the real X.dll,
// so an extra built as X.dll can sit beside it as XHooked.dll. The extra's X.dll build is copied
// under the hooked name; nothing else about the install changes.

namespace RenoDXCommander.Services;

public static class HookedDllNames
{
    private const string Suffix = "Hooked";

    /// <summary>version.dll → versionHooked.dll.</summary>
    public static string ToHooked(string dllName)
        => Path.GetFileNameWithoutExtension(dllName) + Suffix + Path.GetExtension(dllName);

    /// <summary>True for "XHooked.dll" (X non-empty); <paramref name="baseName"/> is then "X.dll".</summary>
    public static bool TryGetBase(string? name, out string baseName)
    {
        baseName = "";
        if (string.IsNullOrEmpty(name) || !name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) return false;
        var stem = Path.GetFileNameWithoutExtension(name);
        if (stem.Length <= Suffix.Length || !stem.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase)) return false;
        baseName = stem[..^Suffix.Length] + Path.GetExtension(name);
        return true;
    }

    /// <summary>The name a hooked file stands in for (versionHooked.dll → version.dll); other names unchanged.</summary>
    public static string BaseOf(string name) => TryGetBase(name, out var b) ? b : name;

    /// <summary>
    /// The extra's picker names with ASI Loader's hooked name inserted right after ASI Loader's name,
    /// when ASI Loader is installed as one of them. <paramref name="exclude"/> skips names the extra
    /// can't use hooked (e.g. RTX Encore's Bink names, which already use Hooked for the game's file).
    /// </summary>
    public static IReadOnlyList<string> WithHooked(IEnumerable<string> names, string? ualName, Func<string, bool>? exclude = null)
    {
        var list = names.ToList();
        if (string.IsNullOrEmpty(ualName) || !ualName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) return list;
        if (exclude?.Invoke(ualName) == true) return list;
        int idx = list.FindIndex(n => n.Equals(ualName, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) return list;
        var hooked = ToHooked(list[idx]);
        if (!list.Contains(hooked, StringComparer.OrdinalIgnoreCase)) list.Insert(idx + 1, hooked);
        return list;
    }

    /// <summary>
    /// The name to badge Recommended: <paramref name="preferred"/> (version.dll unless the manifest names
    /// another for the game), or its hooked name when ASI Loader is installed as it (versionHooked.dll).
    /// </summary>
    public static string Recommended(string? ualName, string preferred = "version.dll")
        => string.Equals(ualName, preferred, StringComparison.OrdinalIgnoreCase)
           && preferred.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? ToHooked(preferred) : preferred;

    /// <summary>True when <paramref name="name"/> is the hooked name for ASI Loader's <paramref name="ualName"/>.</summary>
    public static bool IsHookedFor(string? name, string? ualName)
        => !string.IsNullOrEmpty(ualName) && TryGetBase(name, out var b) && b.Equals(ualName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Moves an RHI .original sentinel/backup from <paramref name="fromPath"/> to <paramref name="toPath"/>
    /// when the component's file was renamed between them. A backup already at the target is kept, and the
    /// leftover is removed only when it's a 0-byte sentinel (RHI's own marker).
    /// </summary>
    public static void MoveSentinel(string fromPath, string toPath)
    {
        var from = fromPath + ".original";
        var to   = toPath + ".original";
        try
        {
            if (!File.Exists(from)) return;
            if (!File.Exists(to))
            {
                File.Move(from, to);
                CrashReporter.Log($"[HookedDllNames.MoveSentinel] '{Path.GetFileName(from)}' → '{Path.GetFileName(to)}'");
            }
            else if (new FileInfo(from).Length == 0)
            {
                File.Delete(from);
                CrashReporter.Log($"[HookedDllNames.MoveSentinel] Kept existing '{Path.GetFileName(to)}', removed sentinel '{Path.GetFileName(from)}'");
            }
            else
                CrashReporter.Log($"[HookedDllNames.MoveSentinel] Both '{Path.GetFileName(from)}' and '{Path.GetFileName(to)}' hold backups — left as they are");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[HookedDllNames.MoveSentinel] '{Path.GetFileName(from)}' → '{Path.GetFileName(to)}' failed — {ex.Message}");
        }
    }
}
