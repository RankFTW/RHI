// AddonFileNames.cs -- treats duplicate-download copies of a ReShade addon as the same addon.
// Browsers and Explorer save a second download as "renodx-ue-extended (1).addon64" or
// "renodx-ue-extended - Copy.addon64". RHI compared names exactly, so such a file wasn't recognised
// (the card showed "Download from Discord") and an update left both copies, which ReShade then loads twice.
using System.Text.RegularExpressions;

namespace RenoDXCommander.Services;

internal static class AddonFileNames
{
    // "<name> (1).addon64", "<name>(2).addon32", "<name> - Copy.addon64", "<name> - Copy (2).addon64"
    private static readonly Regex Duplicate = new(
        @"^(?<name>.+?)(?:\s*\(\d{1,3}\)|\s+-\s+Copy(?:\s*\(\d{1,3}\))?)(?<ext>\.addon(?:64|32))$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The addon's real name without a duplicate-download suffix ("renodx-x (1).addon64" → "renodx-x.addon64").</summary>
    internal static string Canonical(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return fileName;
        var m = Duplicate.Match(fileName);
        return m.Success ? m.Groups["name"].Value + m.Groups["ext"].Value : fileName;
    }

    /// <summary>True when both names are the same addon (ignoring case and duplicate-download suffixes).</summary>
    internal static bool SameAddon(string a, string b) =>
        Canonical(a).Equals(Canonical(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Deletes duplicate copies of <paramref name="keepFileName"/> in <paramref name="dir"/> (same addon, other name),
    /// so ReShade doesn't load it twice. Returns the names deleted. Never throws.
    /// </summary>
    internal static List<string> RemoveDuplicates(string dir, string keepFileName)
    {
        var removed = new List<string>();
        try
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return removed;
            var ext = Path.GetExtension(keepFileName);
            foreach (var path in Directory.GetFiles(dir, "*" + ext))
            {
                var name = Path.GetFileName(path);
                if (name.Equals(keepFileName, StringComparison.OrdinalIgnoreCase) || !SameAddon(name, keepFileName)) continue;
                try
                {
                    File.Delete(path);
                    removed.Add(name);
                    CrashReporter.Log($"[AddonFileNames.RemoveDuplicates] Deleted duplicate '{name}' (same addon as '{keepFileName}') in '{dir}'");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    CrashReporter.Log($"[AddonFileNames.RemoveDuplicates] Couldn't delete '{name}' — {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            CrashReporter.Log($"[AddonFileNames.RemoveDuplicates] Scan of '{dir}' failed — {ex.Message}");
        }
        return removed;
    }
}
