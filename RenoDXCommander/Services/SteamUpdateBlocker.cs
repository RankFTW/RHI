// SteamUpdateBlocker.cs — Blocks Steam updates for one game by making its appmanifest_<appid>.acf read-only.
// No saved setting: the file's read-only attribute is the state, so the toggle always shows the truth.
using System.Text.RegularExpressions;

namespace RenoDXCommander.Services;

public static class SteamUpdateBlocker
{
    /// <summary>
    /// Finds the game's app manifest: walks up from the install path to steamapps\common\&lt;installdir&gt;,
    /// then uses appmanifest_&lt;appId&gt;.acf if it exists, otherwise the .acf whose installdir matches.
    /// Returns null when the game isn't in a Steam library or no manifest matches.
    /// </summary>
    public static string? FindAppManifest(string? installPath, int? appId)
    {
        if (string.IsNullOrWhiteSpace(installPath)) return null;
        try
        {
            var dir = new DirectoryInfo(installPath.Replace('/', '\\').TrimEnd('\\'));
            while (dir?.Parent != null
                   && !(dir.Parent.Name.Equals("common", StringComparison.OrdinalIgnoreCase)
                        && dir.Parent.Parent?.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase) == true))
                dir = dir.Parent;
            if (dir?.Parent?.Parent == null) return null;

            var steamapps = dir.Parent.Parent.FullName;
            var installDir = dir.Name;

            if (appId.HasValue)
            {
                var byId = Path.Combine(steamapps, $"appmanifest_{appId.Value}.acf");
                if (File.Exists(byId) && InstallDirMatches(byId, installDir)) return byId;
            }
            foreach (var acf in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"))
                if (InstallDirMatches(acf, installDir)) return acf;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            CrashReporter.Log($"[SteamUpdateBlocker.FindAppManifest] Lookup failed for '{installPath}' — {ex.Message}");
        }
        return null;
    }

    private static bool InstallDirMatches(string acfPath, string installDir)
    {
        try
        {
            var m = Regex.Match(File.ReadAllText(acfPath), "\"installdir\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase);
            return m.Success && m.Groups[1].Value.Equals(installDir, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[SteamUpdateBlocker.InstallDirMatches] Couldn't read '{acfPath}' — {ex.Message}");
            return false;
        }
    }

    /// <summary>True when the app manifest is read-only (updates blocked).</summary>
    public static bool IsBlocked(string acfPath)
    {
        try { return File.GetAttributes(acfPath).HasFlag(FileAttributes.ReadOnly); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[SteamUpdateBlocker.IsBlocked] Couldn't read attributes of '{acfPath}' — {ex.Message}");
            return false;
        }
    }

    /// <summary>Sets or clears the read-only attribute. Returns null on success, or the error message.</summary>
    public static string? SetBlocked(string acfPath, bool blocked)
    {
        try
        {
            var attrs = File.GetAttributes(acfPath);
            var updated = blocked ? attrs | FileAttributes.ReadOnly : attrs & ~FileAttributes.ReadOnly;
            if (updated != attrs) File.SetAttributes(acfPath, updated);
            CrashReporter.Log($"[SteamUpdateBlocker.SetBlocked] {(blocked ? "Blocked" : "Unblocked")} updates: '{acfPath}'");
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[SteamUpdateBlocker.SetBlocked] Failed for '{acfPath}' — {ex.Message}");
            return ex.Message;
        }
    }
}
