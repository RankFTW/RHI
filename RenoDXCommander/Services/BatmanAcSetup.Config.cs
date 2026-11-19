// BatmanAcSetup.Config.cs -- the launcher settings boostedwarr's Batman: Arkham City mod needs, written into the game's
// config (Documents\WB Games\Batman Arkham City GOTY\BmGame\Config). The launcher (BmLauncher) keeps its choices in
// UserEngine.ini [SystemSettings], which UE3 applies on top of BmEngine.ini [SystemSettings]; both are set so the game
// and the launcher agree. Values read from a working setup (Nov 2026). Only these lines change: UE3 ini files repeat
// key names in other sections, so the files are edited line by line (never parsed and rewritten), with a one-time
// .rhi-backup of each. Not reverted on uninstall: they're the game's own options.

using System.Text;

namespace RenoDXCommander.Services;

internal static partial class BatmanAcSetup
{
    private const string SystemSettings = "SystemSettings";

    /// <summary>DirectX 11 renderer, MSAA off, FXAA off (PostProcessAAType 0), Depth of Field on (off skips the color grade).</summary>
    internal static readonly (string Key, string Value)[] LauncherSettings =
    {
        ("AllowD3D10", "False"),
        ("AllowD3D11", "True"),
        ("MultisampleMode", "1xMSAA"),
        ("PostProcessAAType", "0"),
        ("DepthOfField", "True"),
    };

    /// <summary>The game's config folder (real Documents folder: it may be redirected, e.g. to OneDrive).</summary>
    internal static string ConfigDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "WB Games", "Batman Arkham City GOTY", "BmGame", "Config");

    /// <summary>
    /// Writes the launcher settings into UserEngine.ini and BmEngine.ini (BmEngine.ini also gets MaxMultisamples=1).
    /// Returns a short status. Never throws. Files that don't exist yet (game never started) are left for the launcher.
    /// </summary>
    internal static string ApplyLauncherSettings(string? configDir = null)
    {
        configDir ??= ConfigDir;
        var done = new List<string>();
        foreach (var (file, extra) in new[] { ("UserEngine.ini", false), ("BmEngine.ini", true) })
        {
            var path = Path.Combine(configDir, file);
            try
            {
                if (!File.Exists(path)) continue;
                if (new FileInfo(path).IsReadOnly) { done.Add($"{file} is read-only, left alone"); continue; }
                // UE3 writes these as UTF-16 LE with a BOM: read and write back in the file's own encoding
                var (text, encoding) = ReadWithEncoding(path);
                var values = extra ? LauncherSettings.Append(("MaxMultisamples", "1")).ToArray() : LauncherSettings;
                var updated = SetInSection(text, SystemSettings, values);
                if (updated == text) { done.Add($"{file} already set"); continue; }
                var backup = path + ".rhi-backup";
                if (!File.Exists(backup)) File.Copy(path, backup);
                File.WriteAllText(path, updated, encoding);
                done.Add($"{file} set");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                done.Add($"{file}: {ex.Message}");
            }
        }
        var status = done.Count == 0 ? "game config not found (start the game once, then reinstall the mod)" : string.Join(", ", done);
        CrashReporter.Log($"[BatmanAcSetup.ApplyLauncherSettings] '{configDir}': {status}");
        return status;
    }

    /// <summary>The file's text and its encoding from the byte order mark (UTF-16 LE / BE, UTF-8 with BOM; none = UTF-8 without BOM).</summary>
    private static (string Text, Encoding Encoding) ReadWithEncoding(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Encoding enc =
            bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE ? new UnicodeEncoding(bigEndian: false, byteOrderMark: true)
            : bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF ? new UnicodeEncoding(bigEndian: true, byteOrderMark: true)
            : bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)
            : new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        int bom = enc.GetPreamble().Length;
        return (enc.GetString(bytes, bom, bytes.Length - bom), enc);
    }

    /// <summary>
    /// Sets keys in the first [section] of an ini text: replaces each key's line there (first one; case-insensitive), or
    /// inserts missing keys right after the section header; appends the section when it's missing. Every other line,
    /// including the same keys in other sections, stays as it is. Keeps the text's line endings. Pure.
    /// </summary>
    internal static string SetInSection(string text, string section, IReadOnlyList<(string Key, string Value)> values)
    {
        var nl = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        bool endsWithNewline = text.EndsWith('\n');
        if (endsWithNewline) lines.RemoveAt(lines.Count - 1);

        int header = lines.FindIndex(l => l.Trim().Equals($"[{section}]", StringComparison.OrdinalIgnoreCase));
        if (header < 0)
        {
            if (lines.Count > 0 && lines[^1].Trim().Length > 0) lines.Add("");
            lines.Add($"[{section}]");
            lines.AddRange(values.Select(v => $"{v.Key}={v.Value}"));
        }
        else
        {
            int end = lines.FindIndex(header + 1, l => l.TrimStart().StartsWith('['));
            if (end < 0) end = lines.Count;
            var missing = new List<string>();
            foreach (var (key, value) in values)
            {
                int at = -1;
                for (int i = header + 1; i < end; i++)
                {
                    var l = lines[i];
                    int eq = l.IndexOf('=');
                    if (eq > 0 && l[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) { at = i; break; }
                }
                if (at >= 0) lines[at] = $"{key}={value}";
                else missing.Add($"{key}={value}");
            }
            lines.InsertRange(header + 1, missing);
        }
        return string.Join(nl, lines) + (endsWithNewline ? nl : "");
    }
}
