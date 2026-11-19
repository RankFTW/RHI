// RenoDxZipPackage.cs -- RenoDX mods whose wiki snapshot link is a .zip instead of a bare addon
// (e.g. boostedwarr's Batman: Arkham City: renodx-batmanac.zip = renodx-batmanac.addon32 + a 64-bit helper exe).
// The addon and every runtime file the zip ships go next to the addon (docs are skipped), and the extra files are
// listed in "<addon>.rhi-package.txt" beside it so uninstall removes exactly them. Other RHI steps for the same mod
// (e.g. BatmanAcSetup's DLSS / FSR files) add their files to the same list.

using System.IO.Compression;

namespace RenoDXCommander.Services;

internal static class RenoDxZipPackage
{
    /// <summary>The addon inside a known zip, when its name isn't simply the zip's name with .addon64.</summary>
    private static readonly Dictionary<string, string> KnownAddons = new(StringComparer.OrdinalIgnoreCase)
    {
        ["renodx-batmanac.zip"] = "renodx-batmanac.addon32", // Batman: Arkham City is 32-bit
    };

    /// <summary>Files a package may ship that RHI deploys (everything else, such as readmes, is skipped).</summary>
    private static readonly HashSet<string> RuntimeExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".addon64", ".addon32", ".exe", ".dll", ".json", ".ini", ".fx", ".fxh" };

    internal const string ListSuffix = ".rhi-package.txt";

    /// <summary>True when a URL or file name is a .zip package.</summary>
    internal static bool IsZip(string? urlOrName) =>
        !string.IsNullOrEmpty(urlOrName) && Path.GetExtension(StripQuery(urlOrName)).Equals(".zip", StringComparison.OrdinalIgnoreCase);

    /// <summary>The addon file name a zip package installs ("renodx-batmanac.zip" → "renodx-batmanac.addon32").</summary>
    internal static string AddonNameFor(string zipFileName)
    {
        var name = Path.GetFileName(StripQuery(zipFileName));
        return KnownAddons.TryGetValue(name, out var addon) ? addon : Path.ChangeExtension(name, ".addon64");
    }

    /// <summary>The list file that records a package's extra files, beside the addon.</summary>
    internal static string ListPath(string addonDir, string addonFileName) => Path.Combine(addonDir, addonFileName + ListSuffix);

    /// <summary>
    /// Extracts the package into <paramref name="addonDir"/>: the addon plus its runtime files (paths inside the zip
    /// are kept, checked with SafeCombine). Returns the addon's file name. Throws when the zip has no addon.
    /// Files RHI listed before (from an older version of the package or another RHI step) stay listed.
    /// </summary>
    internal static string Extract(string zipPath, string addonDir)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var files = zip.Entries
            .Where(e => !string.IsNullOrEmpty(e.Name) && RuntimeExtensions.Contains(Path.GetExtension(e.Name)))
            .ToList();
        var addon = files.FirstOrDefault(e => e.Name.EndsWith(".addon64", StringComparison.OrdinalIgnoreCase)
                                           || e.Name.EndsWith(".addon32", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"'{Path.GetFileName(zipPath)}' has no ReShade addon in it.");

        var extras = new List<string>();
        foreach (var entry in files)
        {
            var rel = entry == addon ? entry.Name : entry.FullName.Replace('/', '\\');
            var dest = FileHelper.SafeCombine(addonDir, rel);
            if (dest == null)
            {
                CrashReporter.Log($"[RenoDxZipPackage.Extract] Skipped unsafe path '{entry.FullName}'");
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            var tmp = dest + ".rhi-tmp";
            entry.ExtractToFile(tmp, overwrite: true);
            File.Move(tmp, dest, overwrite: true);
            if (entry != addon) extras.Add(rel);
        }
        AddToList(addonDir, addon.Name, extras);
        CrashReporter.Log($"[RenoDxZipPackage.Extract] '{Path.GetFileName(zipPath)}' → {addon.Name} + {extras.Count} file(s) in '{addonDir}'");
        return addon.Name;
    }

    /// <summary>Adds files (relative to the addon folder) to the package's list. Never throws.</summary>
    internal static void AddToList(string addonDir, string addonFileName, IEnumerable<string> relPaths)
    {
        var list = ListPath(addonDir, addonFileName);
        try
        {
            var all = ReadList(list).Concat(relPaths).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            File.WriteAllLines(list, all);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[RenoDxZipPackage.AddToList] '{list}' — {ex.Message}");
        }
    }

    /// <summary>True when a file (relative to the addon folder) is on the package's list, i.e. RHI placed it.</summary>
    internal static bool IsListed(string addonDir, string addonFileName, string relPath) =>
        ReadList(ListPath(addonDir, addonFileName)).Contains(relPath, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Removes the files a package installed (its list), restoring any .original backups RHI made, then the list
    /// itself. The addon is removed by the caller. Never throws.
    /// </summary>
    internal static void RemoveFiles(string addonDir, string addonFileName)
    {
        var list = ListPath(addonDir, addonFileName);
        foreach (var rel in ReadList(list))
        {
            var path = FileHelper.SafeCombine(addonDir, rel);
            if (path == null) continue;
            try
            {
                if (File.Exists(path)) File.Delete(path);
                AuxInstallService.SentinelRestore(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                CrashReporter.Log($"[RenoDxZipPackage.RemoveFiles] Couldn't remove '{path}' — {ex.Message}");
            }
        }
        try { if (File.Exists(list)) File.Delete(list); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { CrashReporter.Log($"[RenoDxZipPackage.RemoveFiles] Couldn't delete '{list}' — {ex.Message}"); }
    }

    private static List<string> ReadList(string list)
    {
        try
        {
            return File.Exists(list)
                ? File.ReadAllLines(list).Select(l => l.Trim()).Where(l => l.Length > 0).ToList()
                : new List<string>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[RenoDxZipPackage.ReadList] '{list}' — {ex.Message}");
            return new List<string>();
        }
    }

    private static string StripQuery(string s)
    {
        int q = s.IndexOfAny(new[] { '?', '#' });
        return q >= 0 ? s[..q] : s;
    }
}
