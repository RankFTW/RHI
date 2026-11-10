// OsIniLibrary.cs — the user's saved OptiScaler.ini files ("F5 super ini", ...), kept in
// %LocalAppData%\RHI\optiscaler\saved-inis\<name>.ini. Any number can be saved from a game, added from
// a file, and put into a game as its OptiScaler.ini. The ini a game uses is remembered per game
// (OsIniProfile) and deployed again after a reinstall or update.

namespace RenoDXCommander.Services;

public static class OsIniLibrary
{
    public const int MaxNameLength = 60;

    public static string Dir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "optiscaler", "saved-inis");

    /// <summary>Saved names, A to Z.</summary>
    public static List<string> List()
    {
        try
        {
            if (!Directory.Exists(Dir)) return new List<string>();
            return Directory.GetFiles(Dir, "*.ini")
                .Select(p => Path.GetFileNameWithoutExtension(p))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[OsIniLibrary.List] Failed — {ex.Message}");
            return new List<string>();
        }
    }

    /// <summary>A usable file name from what the user typed, or null if nothing is left.</summary>
    public static string? CleanName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Trim().Where(c => !invalid.Contains(c)).ToArray()).Trim().TrimEnd('.');
        if (clean.Length > MaxNameLength) clean = clean[..MaxNameLength].Trim();
        return clean.Length == 0 ? null : clean;
    }

    public static string PathFor(string name) => Path.Combine(Dir, name + ".ini");

    public static bool Exists(string? name) => !string.IsNullOrEmpty(name) && File.Exists(PathFor(name));

    /// <summary>Copies <paramref name="sourceIni"/> into the library as <paramref name="name"/> (replacing one with that name).</summary>
    public static void Save(string name, string sourceIni)
    {
        Directory.CreateDirectory(Dir);
        File.Copy(sourceIni, PathFor(name), overwrite: true);
        CrashReporter.Log($"[OsIniLibrary.Save] Saved '{sourceIni}' as '{name}'");
    }

    public static void Delete(string name)
    {
        var p = PathFor(name);
        if (File.Exists(p)) File.Delete(p);
        CrashReporter.Log($"[OsIniLibrary.Delete] Deleted '{name}'");
    }

    /// <summary>A name for an ini added from a file: its file name, or its folder's name for "OptiScaler.ini".</summary>
    public static string SuggestName(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        if (name.Equals("OptiScaler", StringComparison.OrdinalIgnoreCase))
        {
            var folder = Path.GetFileName(Path.GetDirectoryName(filePath) ?? "");
            if (!string.IsNullOrWhiteSpace(folder)) name = folder;
        }
        var clean = CleanName(name) ?? "Imported ini";
        // Don't replace a saved ini by accident: "Name (2)", "Name (3)", ...
        var unique = clean;
        for (int i = 2; Exists(unique); i++) unique = $"{clean} ({i})";
        return unique;
    }

    /// <summary>
    /// Puts the saved ini into the game as OptiScaler.ini (the old one is backed up), with the settings RHI
    /// always sets (LoadReshade, LoadAsiPlugins, the OptiScaler menu key). Returns an error message, or null.
    /// </summary>
    public static string? Deploy(string name, string gameDir, string? hotkey)
    {
        var source = PathFor(name);
        if (!File.Exists(source)) return $"The saved ini \"{name}\" wasn't found.";
        var dest = Path.Combine(gameDir, OptiScalerService.IniFileName);
        try
        {
            if (File.Exists(dest))
            {
                OsIniEditor.Backup(gameDir, dest);
                var attrs = File.GetAttributes(dest);
                if (attrs.HasFlag(FileAttributes.ReadOnly)) File.SetAttributes(dest, attrs & ~FileAttributes.ReadOnly);
            }
            File.Copy(source, dest, overwrite: true);
            OptiScalerService.EnforceLoadReshade(dest);
            OptiScalerService.EnforceLoadAsiPlugins(dest);
            if (!string.IsNullOrEmpty(hotkey)) OptiScalerService.WriteShortcutKey(dest, hotkey);
            CrashReporter.Log($"[OsIniLibrary.Deploy] Deployed '{name}' as OptiScaler.ini in '{gameDir}'");
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[OsIniLibrary.Deploy] Failed for '{gameDir}' — {ex.Message}");
            return ex.Message;
        }
    }
}
