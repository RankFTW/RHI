// XboxPaths.cs -- real folders for Xbox / Game Pass games.
//
// Windows reports a Game Pass package's location as C:\Program Files\WindowsApps\<Name>_<Version>_..._<PublisherId>.
// For games installed to an Xbox games folder (the default since 2021) that folder is only a junction to the real
// one, e.g. D:\xbox\Valor Mortis\Content: readable, writable, and the same across game updates (the WindowsApps
// name changes with every version). RHI uses the real folder, and rewrites paths it saved before.
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace RenoDXCommander.Services;

public static class XboxPaths
{
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetFinalPathNameByHandleW(IntPtr handle, StringBuilder path, uint size, uint flags);

    public static bool IsWindowsApps(string path)
        => (path.Replace('/', '\\') + "\\").Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The folder a path really points to once junctions / symlinks are followed, or null when it's the same,
    /// missing or can't be opened. Needs no read access (opens with access 0), so it works for WindowsApps.
    /// </summary>
    public static string? ResolveRealFolder(string path)
    {
        const uint FILE_SHARE_ALL = 7, OPEN_EXISTING = 3, FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
        var h = CreateFileW(path, 0, FILE_SHARE_ALL, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
        if (h == new IntPtr(-1)) return null;
        try
        {
            var sb = new StringBuilder(1024);
            var n = GetFinalPathNameByHandleW(h, sb, (uint)sb.Capacity, 0);
            if (n == 0 || n >= sb.Capacity) return null;
            var real = sb.ToString();
            if (real.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) real = @"\\" + real.Substring(8);
            else if (real.StartsWith(@"\\?\", StringComparison.Ordinal)) real = real.Substring(4);
            real = real.TrimEnd('\\');
            return string.Equals(real, path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ? null : real;
        }
        finally { NativeInterop.CloseHandle(h); }
    }

    /// <summary>
    /// For a WindowsApps package folder: the real folder it's a junction to, when that's outside WindowsApps
    /// (an Xbox games folder). Null for anything else, including games really installed inside WindowsApps.
    /// </summary>
    public static string? RealPackageFolder(string windowsAppsPath)
    {
        if (!IsWindowsApps(windowsAppsPath)) return null;
        var real = ResolveRealFolder(windowsAppsPath);
        return real != null && !IsWindowsApps(real) && Directory.Exists(real) ? real : null;
    }

    // ── Saved paths ──────────────────────────────────────────────────────────

    // X:\Program Files\WindowsApps\<Name>_<Version>_<Arch>_<ResourceId>_<PublisherId> in saved text. The separator
    // is captured so JSON (\\) and JSON inside a JSON string (\\\\, settings.json) are both handled.
    private static readonly Regex SavedPackagePath = new(
        @"(?<drive>[A-Za-z]):(?<sep>\\{1,8})(?<mid>(?:Program Files\k<sep>)?WindowsApps)\k<sep>"
        + @"(?<pkg>[A-Za-z0-9][A-Za-z0-9.\-]*_[0-9.]+_[A-Za-z0-9]+_[A-Za-z0-9.\-~]*_[A-Za-z0-9]{13})(?![A-Za-z0-9._\-~])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Replaces WindowsApps package folders in <paramref name="text"/> with their real folder. Only folders that
    /// <paramref name="resolve"/> maps are changed (the installed version; older versions no longer exist). Keeps
    /// the separator style, and lower case for lower-case cache keys.
    /// </summary>
    public static string RewriteSavedPaths(string text, Func<string, string?> resolve, out int count)
    {
        int n = 0;
        var result = SavedPackagePath.Replace(text, m =>
        {
            var sep = m.Groups["sep"].Value;
            var physical = $"{m.Groups["drive"].Value}:\\{m.Groups["mid"].Value.Replace(sep, "\\")}\\{m.Groups["pkg"].Value}";
            var real = resolve(physical);
            if (string.IsNullOrEmpty(real)) return m.Value;
            n++;
            var replacement = real.TrimEnd('\\').Replace("\\", sep);
            return m.Value == m.Value.ToLowerInvariant() ? replacement.ToLowerInvariant() : replacement;
        });
        count = n;
        return result;
    }

    // Downloaded data (manifests, wikis, ETags): never holds paths RHI saved.
    private static readonly HashSet<string> SkipFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "manifest.json", "dlss_manifest.json", "optiscaler_presets.json", "github_etag_cache.json",
        "nexus_games.json", "pcgw_central_cache.json", "pcgw_api_cache.json", "pcgw_url_cache.json",
        "lyall_repos.json", "uplift_releases.json",
    };

    /// <summary>
    /// Rewrites saved WindowsApps paths of Xbox games installed to an Xbox games folder to the real folder, in
    /// RHI's data files. Runs at startup before anything loads them; does nothing once paths are real. The
    /// files are the same either way (the WindowsApps folder is a junction), so no game file moves.
    /// </summary>
    public static void MigrateSavedPaths(string dataDir)
    {
        if (!Directory.Exists(dataDir)) return;
        var cache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        string? Resolve(string physical)
        {
            if (!cache.TryGetValue(physical, out var real)) cache[physical] = real = RealPackageFolder(physical);
            return real;
        }

        foreach (var file in Directory.EnumerateFiles(dataDir, "*.json"))
        {
            if (SkipFiles.Contains(Path.GetFileName(file))) continue;
            try
            {
                var text = File.ReadAllText(file);
                if (!text.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase)) continue;
                var updated = RewriteSavedPaths(text, Resolve, out var count);
                if (count == 0) continue;

                var backupDir = Path.Combine(dataDir, "Backups", "xbox-real-paths");
                Directory.CreateDirectory(backupDir);
                var backup = Path.Combine(backupDir, Path.GetFileName(file));
                if (!File.Exists(backup)) File.Copy(file, backup);

                var tmp = file + ".tmp";
                File.WriteAllText(tmp, updated, new UTF8Encoding(false));
                File.Move(tmp, file, overwrite: true);
                CrashReporter.Log($"[XboxPaths.MigrateSavedPaths] {Path.GetFileName(file)}: {count} Game Pass path(s) now use the game's real folder");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                CrashReporter.Log($"[XboxPaths.MigrateSavedPaths] {Path.GetFileName(file)} skipped — {ex.Message}");
            }
        }
        foreach (var (physical, real) in cache)
            if (real != null) CrashReporter.Log($"[XboxPaths.MigrateSavedPaths] '{physical}' → '{real}'");
    }

    // ── MicrosoftGame.config ─────────────────────────────────────────────────

    /// <summary>Display name and launch id (AUMID) from a GDK game's MicrosoftGame.config, for folder-based detection.</summary>
    public static (string? DisplayName, string? Aumid) ReadGameConfig(string gameFolder)
    {
        try
        {
            var path = Path.Combine(gameFolder, "MicrosoftGame.config");
            if (!File.Exists(path)) return (null, null);
            var doc = XDocument.Load(path);
            var identity = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Identity");
            var visuals = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "ShellVisuals");
            var exe = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Executable");

            var name = visuals?.Attribute("DefaultDisplayName")?.Value;
            if (string.IsNullOrWhiteSpace(name) || name.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)) name = null;

            string? aumid = null;
            var pkgName = identity?.Attribute("Name")?.Value;
            var publisher = identity?.Attribute("Publisher")?.Value;
            var appId = exe?.Attribute("Id")?.Value;
            if (!string.IsNullOrEmpty(pkgName) && !string.IsNullOrEmpty(publisher) && !string.IsNullOrEmpty(appId))
                aumid = $"{pkgName}_{PublisherId(publisher)}!{appId}";
            return (name, aumid);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            CrashReporter.Log($"[XboxPaths.ReadGameConfig] '{gameFolder}' — {ex.Message}");
            return (null, null);
        }
    }

    /// <summary>
    /// The 13-character publisher id in package family names: first 64 bits of SHA-256 of the UTF-16 publisher,
    /// padded to 65 bits, as base32 (digits + lower case without i, l, o, u).
    /// </summary>
    public static string PublisherId(string publisher)
    {
        const string Alphabet = "0123456789abcdefghjkmnpqrstvwxyz";
        var hash = SHA256.HashData(Encoding.Unicode.GetBytes(publisher));
        ulong bits = 0;
        for (int i = 0; i < 8; i++) bits = (bits << 8) | hash[i];
        var sb = new StringBuilder(13);
        for (int i = 0; i < 13; i++)
        {
            int shift = 59 - i * 5; // 65 bits: the last group gets one zero bit of padding
            int v = shift >= 0 ? (int)((bits >> shift) & 31) : (int)((bits << 1) & 31);
            sb.Append(Alphabet[v]);
        }
        return sb.ToString();
    }
}
