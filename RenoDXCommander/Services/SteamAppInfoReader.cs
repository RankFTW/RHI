// SteamAppInfoReader.cs -- the real names of Steam apps from Steam's own cache, <Steam>\appcache\appinfo.vdf.
// Used when an appmanifest_<id>.acf has a placeholder name ("appid_244160"): Steam writes that when it
// installed the game before it had the app's details, and keeps it until the game updates, while the
// Steam library shows the right name from appinfo.vdf (user report, Nov 2026: Homeworld Remastered
// Collection, Meccha Chameleon).
// Format (versions 39 / 40 / 41, magic 0x07564427..29): header magic + universe (+ string table offset in
// 41); then per app: appid, size (bytes after this field), info state, last updated, PICS token, SHA-1,
// change number, (40+) binary SHA-1, then binary KeyValues "appinfo" { common { name } }. Version 41
// stores key names as int32 indexes into a string table at the end of the file. Ends with appid 0.
// Only the wanted apps' KeyValues are parsed; the rest are skipped by size. Pure apart from the file read.

namespace RenoDXCommander.Services;

internal static class SteamAppInfoReader
{
    private const uint V39 = 0x07564427, V40 = 0x07564428, V41 = 0x07564429;
    private const int MaxDepth = 32;

    /// <summary>A name Steam wrote before it knew the app ("appid_123", or nothing).</summary>
    internal static bool IsPlaceholderName(string? name, int? appId) =>
        string.IsNullOrWhiteSpace(name)
        || (name.StartsWith("appid_", StringComparison.OrdinalIgnoreCase)
            && name.Length > 6 && name[6..].All(char.IsAsciiDigit)
            && (appId == null || name[6..] == appId.Value.ToString()));

    /// <summary>
    /// AppID → name for the wanted apps found in <paramref name="appInfoPath"/>. Missing file, unknown
    /// version or a damaged entry = those apps are left out (logged), never an exception.
    /// </summary>
    internal static Dictionary<int, string> ReadNames(string appInfoPath, IReadOnlyCollection<int> appIds)
    {
        var result = new Dictionary<int, string>();
        if (appIds.Count == 0 || !File.Exists(appInfoPath)) return result;
        try
        {
            using var fs = new FileStream(appInfoPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var br = new BinaryReader(fs);
            uint magic = br.ReadUInt32();
            br.ReadUInt32(); // universe
            if (magic is not (V39 or V40 or V41))
            {
                CrashReporter.Log($"[SteamAppInfoReader.ReadNames] Unknown appinfo.vdf version 0x{magic:X8}");
                return result;
            }

            List<string>? keyTable = null;
            if (magic == V41)
            {
                long tableOffset = br.ReadInt64();
                long entriesStart = fs.Position;
                if (tableOffset <= entriesStart || tableOffset >= fs.Length) return result;
                fs.Position = tableOffset;
                keyTable = ReadStringTable(br, fs.Length - tableOffset);
                fs.Position = entriesStart;
            }

            int headerAfterSize = magic == V39 ? 40 : 60; // fields between "size" and the KeyValues
            var wanted = new HashSet<int>(appIds);
            while (fs.Position + 8 <= fs.Length && wanted.Count > 0)
            {
                uint appId = br.ReadUInt32();
                if (appId == 0) break;
                uint size = br.ReadUInt32();
                long next = fs.Position + size;
                if (next > fs.Length || size < headerAfterSize) break;
                if (!wanted.Contains((int)appId))
                {
                    fs.Position = next;
                    continue;
                }
                fs.Position += headerAfterSize;
                var kv = br.ReadBytes((int)(size - headerAfterSize));
                fs.Position = next;
                wanted.Remove((int)appId);
                try
                {
                    var name = FindName(kv, keyTable);
                    if (!string.IsNullOrWhiteSpace(name)) result[(int)appId] = name.Trim();
                }
                catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException or ArgumentOutOfRangeException)
                {
                    CrashReporter.Log($"[SteamAppInfoReader.ReadNames] Couldn't read app {appId}: {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EndOfStreamException)
        {
            CrashReporter.Log($"[SteamAppInfoReader.ReadNames] {ex.GetType().Name}: {ex.Message}");
        }
        return result;
    }

    private static List<string> ReadStringTable(BinaryReader br, long length)
    {
        uint count = br.ReadUInt32();
        var bytes = br.ReadBytes((int)Math.Min(length - 4, int.MaxValue));
        var list = new List<string>((int)Math.Min(count, 1_000_000));
        int pos = 0;
        for (uint i = 0; i < count && pos < bytes.Length; i++)
        {
            int end = Array.IndexOf(bytes, (byte)0, pos);
            if (end < 0) end = bytes.Length;
            list.Add(System.Text.Encoding.UTF8.GetString(bytes, pos, end - pos));
            pos = end + 1;
        }
        return list;
    }

    /// <summary>appinfo › common › name in one app's binary KeyValues, or null.</summary>
    internal static string? FindName(byte[] kv, IReadOnlyList<string>? keyTable)
    {
        int pos = 0;
        string? found = null;
        Walk(kv, ref pos, keyTable, 0, path: "", ref found);
        return found;
    }

    private static void Walk(byte[] b, ref int pos, IReadOnlyList<string>? keys, int depth, string path, ref string? found)
    {
        if (depth > MaxDepth) throw new FormatException("KeyValues nested too deeply");
        while (pos < b.Length)
        {
            byte type = b[pos++];
            if (type is 8 or 11) return;
            string key = ReadKey(b, ref pos, keys);
            string here = path.Length == 0 ? key : path + "/" + key;
            switch (type)
            {
                case 0:
                    Walk(b, ref pos, keys, depth + 1, here, ref found);
                    break;
                case 1:
                    var s = ReadString(b, ref pos);
                    if (found == null && here.Equals("appinfo/common/name", StringComparison.OrdinalIgnoreCase)) found = s;
                    break;
                case 2 or 3 or 4 or 6:
                    pos += 4;
                    break;
                case 7 or 10:
                    pos += 8;
                    break;
                default:
                    throw new FormatException($"Unknown KeyValues type {type} at {pos - 1}");
            }
            if (pos > b.Length) throw new FormatException("Truncated KeyValues");
            if (found != null) return;
        }
    }

    private static string ReadKey(byte[] b, ref int pos, IReadOnlyList<string>? keys)
    {
        if (keys == null) return ReadString(b, ref pos);
        if (pos + 4 > b.Length) throw new FormatException("Truncated KeyValues key");
        int index = BitConverter.ToInt32(b, pos);
        pos += 4;
        if (index < 0 || index >= keys.Count) throw new FormatException($"Key index {index} outside the string table");
        return keys[index];
    }

    private static string ReadString(byte[] b, ref int pos)
    {
        int end = Array.IndexOf(b, (byte)0, pos);
        if (end < 0) throw new FormatException("Unterminated KeyValues string");
        var s = System.Text.Encoding.UTF8.GetString(b, pos, end - pos);
        pos = end + 1;
        return s;
    }
}
