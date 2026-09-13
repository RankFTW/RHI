using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RHI.Linux.Core;

public sealed record IniKey(string Section, string Key, string Value);
public sealed record IniChange(string Section, string Key, string? Original, string Applied);
public sealed class IniRecord
{
    public string Path { get; set; } = "";
    public string OriginalText { get; set; } = "";
    public bool Existed { get; set; }
    public List<IniChange> Changes { get; set; } = [];
    public string AppliedHash { get; set; } = "";
    public int? OriginalMode { get; set; }
    public bool ReadOnly { get; set; }
}

public static class IniSettings
{
    public static readonly IniKey[] UnrealHdr =
    [
        new("SystemSettings", "r.AllowHDR", "1"),
        new("SystemSettings", "r.HDR.EnableHDROutput", "1"),
        new("SystemSettings", "r.HDR.Display.OutputDevice", "3"),
        new("SystemSettings", "r.HDR.Display.ColorGamut", "2"),
        new("SystemSettings", "r.HDR.UI.CompositeMode", "1"),
        new("/Script/Engine.RendererSettings", "r.LUT.UpdateEveryFrame", "1")
    ];
    public static readonly IniKey[] RenoDxHdr =
    [
        new("renodx", "Set_Path", "0"), new("renodx", "ForceBorderless", "1"),
        new("renodx", "PreventFullscreen", "1"), new("renodx", "SettingsMode", "2")
    ];

    public static List<string> FindEngineInis(Game game)
    {
        if (game.Prefix is null || !Directory.Exists(game.Prefix)) return [];
        var local = Proton.LocalAppData(game.Prefix);
        if (!Directory.Exists(local)) return [];
        return Directory.EnumerateFiles(local, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true,
                MaxRecursionDepth = 6, AttributesToSkip = FileAttributes.ReparsePoint })
            .Where(p => Path.GetFileName(p).Equals("Engine.ini", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => Path.GetFileNameWithoutExtension(game.Executable ?? "").StartsWith(Path.GetRelativePath(local, p).Split('/')[0], StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static string RecordPath(string path) => Path.Combine(LinuxPaths.Data, "ini-backups",
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(LinuxPaths.Canonical(path)))) + ".json");
    private static string Hash(string text) => Installation.Hash(Encoding.UTF8.GetBytes(text));

    public static string? Get(string text, string section, string key)
    {
        var matches = Find(text.Replace("\r\n", "\n").Split('\n').ToList(), section, key);
        if (matches.Count > 1) throw new IOException($"Duplicate [{section}] {key} keys. Resolve them before applying HDR settings.");
        return matches.Count == 0 ? null : matches[0].Value;
    }
    private static List<(int Index, string Value)> Find(List<string> lines, string section, string key)
    {
        var result = new List<(int, string)>(); string current = "";
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim().TrimStart('\uFEFF');
            if (line.StartsWith('[') && line.EndsWith(']')) current = line[1..^1];
            else if (current.Equals(section, StringComparison.OrdinalIgnoreCase) && !line.StartsWith(';') && !line.StartsWith('#'))
            {
                var pair = line.Split('=', 2);
                if (pair.Length == 2 && pair[0].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) result.Add((i, pair[1].Trim()));
            }
        }
        return result;
    }

    public static string Set(string text, string section, string key, string? value)
    {
        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        var matches = Find(lines, section, key);
        if (matches.Count > 1) throw new IOException($"Duplicate [{section}] {key} keys.");
        if (matches.Count == 1)
        {
            if (value is null) lines.RemoveAt(matches[0].Index);
            else lines[matches[0].Index] = key + "=" + value;
        }
        else if (value != null)
        {
            var index = lines.FindIndex(l => l.Trim().Equals("[" + section + "]", StringComparison.OrdinalIgnoreCase));
            if (index < 0) { lines.Add("[" + section + "]"); lines.Add(key + "=" + value); }
            else lines.Insert(index + 1, key + "=" + value);
        }
        return string.Join(newline, lines);
    }

    public static void Apply(string path, IEnumerable<IniKey> keys, bool readOnly = false)
    {
        path = LinuxPaths.Canonical(path);
        var recordPath = RecordPath(path);
        var current = File.Exists(path) ? File.ReadAllText(path) : "";
        var record = File.Exists(recordPath) ? JsonSerializer.Deserialize<IniRecord>(File.ReadAllText(recordPath), LinuxPaths.Json)!
            : new IniRecord { Path = path, Existed = File.Exists(path), OriginalText = current };
        if (OperatingSystem.IsLinux() && record.OriginalMode == null && File.Exists(path)) record.OriginalMode = (int)File.GetUnixFileMode(path);
        record.ReadOnly |= readOnly;
        var text = current;
        foreach (var key in keys)
        {
            var old = Get(text, key.Section, key.Key);
            var managed = record.Changes.FirstOrDefault(c => c.Section == key.Section && c.Key == key.Key);
            if (managed != null && old != managed.Applied) throw new IOException($"{key.Key} was changed outside RHI; restore or edit it before applying again.");
            if (managed == null) record.Changes.Add(new(key.Section, key.Key, old, key.Value));
            text = Set(text, key.Section, key.Key, key.Value);
        }
        record.AppliedHash = Hash(text);
        // Write the recovery record first; an interrupted edit can always be undone.
        LinuxPaths.WriteJson(recordPath, record);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".rhi-temp";
        try
        {
            File.WriteAllText(temp, text); File.Move(temp, path, true);
            if (record.ReadOnly && OperatingSystem.IsLinux()) File.SetUnixFileMode(path,
                (UnixFileMode)(record.OriginalMode ?? (int)(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead))
                & ~(UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite));
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public static void Restore(string path)
    {
        var recordPath = RecordPath(path);
        if (!File.Exists(recordPath)) return;
        var record = JsonSerializer.Deserialize<IniRecord>(File.ReadAllText(recordPath), LinuxPaths.Json)!;
        if (!File.Exists(path)) { File.Delete(recordPath); return; }
        var current = File.ReadAllText(path);
        if (OperatingSystem.IsLinux() && record.ReadOnly) File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserWrite);
        if (Hash(current) == record.AppliedHash)
        {
            if (record.Existed) File.WriteAllText(path, record.OriginalText);
            else File.Delete(path);
        }
        else
        {
            foreach (var change in record.Changes)
                if (Get(current, change.Section, change.Key) == change.Applied)
                    current = Set(current, change.Section, change.Key, change.Original);
            File.WriteAllText(path, current);
        }
        if (OperatingSystem.IsLinux() && record.OriginalMode is { } mode && File.Exists(path)) File.SetUnixFileMode(path, (UnixFileMode)mode);
        File.Delete(recordPath);
    }
}
