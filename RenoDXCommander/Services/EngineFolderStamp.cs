// EngineFolderStamp.cs — a cheap fingerprint of a game folder's layout, used to reuse an
// "Unknown engine" result between launches instead of re-scanning the whole folder.
//
// Engine detection looks for markers a few levels down (Binaries\Win64, *_Data\Mono,
// UnityPlayer.dll, re_chunk_000.pak...), so the stamp hashes the NAMES of the folders at the
// first two levels plus the exe names at the top two levels. Names change when a game is still
// downloading, installs further or is restructured by an update.
//
// Modified times are deliberately not used: RHI's shader sync rewrites reshade-shaders in every
// game folder on each launch, and ReShade writes logs next to the exe, so times change constantly.
// RHI/ReShade-owned folders are excluded for the same reason. Full Refresh clears the stamps.

namespace RenoDXCommander.Services;

public static class EngineFolderStamp
{
    // Bound the work for huge folders; past this the stamp still covers the first entries.
    private const int MaxEntries = 400;

    // Folders RHI or ReShade create/recreate inside game folders.
    private static readonly HashSet<string> IgnoredFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "reshade-shaders", "reshade-shaders-original", "reshade-presets",
    };

    /// <summary>Returns a fingerprint for <paramref name="root"/>, or null if it can't be read.</summary>
    public static string? Compute(string root)
    {
        try
        {
            var rootInfo = new DirectoryInfo(root);
            if (!rootInfo.Exists) return null;

            ulong hash = 14695981039346656037UL;
            int count = 0;

            foreach (var exe in SafeExeNames(rootInfo)) hash = Fnv(hash, "/" + exe);

            foreach (var level1 in SafeDirs(rootInfo))
            {
                hash = Fnv(hash, "1:" + level1.Name);
                if (++count >= MaxEntries) break;

                foreach (var exe in SafeExeNames(level1)) hash = Fnv(hash, level1.Name + "/" + exe);

                foreach (var level2 in SafeDirs(level1))
                {
                    hash = Fnv(hash, "2:" + level1.Name + "/" + level2.Name);
                    if (++count >= MaxEntries) break;
                }
                if (count >= MaxEntries) break;
            }

            return hash.ToString("x16");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[EngineFolderStamp.Compute] Couldn't fingerprint '{root}' — {ex.Message}");
            return null;
        }
    }

    // Sorted so the stamp doesn't depend on enumeration order.
    private static IEnumerable<DirectoryInfo> SafeDirs(DirectoryInfo dir)
    {
        try
        {
            return dir.GetDirectories()
                .Where(d => !IgnoredFolders.Contains(d.Name))
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        // Hot path, every launch: restricted subfolders are normal under WindowsApps.
        // Leave them out of the stamp (same result every time) without logging.
        catch (UnauthorizedAccessException) { return Array.Empty<DirectoryInfo>(); }
        catch (IOException) { return Array.Empty<DirectoryInfo>(); }
    }

    private static IEnumerable<string> SafeExeNames(DirectoryInfo dir)
    {
        try
        {
            return dir.GetFiles("*.exe")
                .Select(f => f.Name)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (UnauthorizedAccessException) { return Array.Empty<string>(); }
        catch (IOException) { return Array.Empty<string>(); }
    }

    // FNV-1a over UTF-16 code units: stable across runs (string.GetHashCode is randomised per process).
    private static ulong Fnv(ulong hash, string value)
    {
        foreach (var c in value.ToUpperInvariant())
        {
            hash ^= (byte)c;
            hash *= 1099511628211UL;
            hash ^= (byte)(c >> 8);
            hash *= 1099511628211UL;
        }
        // Separator so "ab"+"c" and "a"+"bc" differ.
        hash ^= 0xFF;
        hash *= 1099511628211UL;
        return hash;
    }
}
