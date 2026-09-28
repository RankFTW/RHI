using System.Reflection.PortableExecutable;

namespace RHI.Linux.Core;

// FileVersionInfo does not read Windows version resources on Linux, so parse
// the RT_VERSION resource's VS_FIXEDFILEINFO directly from the PE file.
public static class PeVersion
{
    private const int RtVersion = 16;
    private const uint FixedInfoSignature = 0xFEEF04BD;

    public static string? Read(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length == 0) return null;
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            var directory = pe.PEHeaders.PEHeader?.ResourceTableDirectory;
            if (directory is not { RelativeVirtualAddress: > 0 } table) return null;
            var section = pe.GetSectionData(table.RelativeVirtualAddress).GetContent().ToArray();
            if (Entry(section, 0, RtVersion) is not { } names || First(section, names) is not { } languages
                || First(section, languages) is not { } leaf || leaf < 0 || leaf + 8 > section.Length) return null;
            var rva = BitConverter.ToInt32(section.AsSpan(leaf));
            var size = BitConverter.ToInt32(section.AsSpan(leaf + 4));
            var data = pe.GetSectionData(rva).GetContent().ToArray();
            if (size <= 0 || size > data.Length) size = data.Length;
            for (var i = 0; i + 16 <= size; i += 4)
            {
                if (BitConverter.ToUInt32(data.AsSpan(i)) != FixedInfoSignature) continue;
                var ms = BitConverter.ToUInt32(data.AsSpan(i + 8)); var ls = BitConverter.ToUInt32(data.AsSpan(i + 12));
                return $"{ms >> 16}.{ms & 0xFFFF}.{ls >> 16}.{ls & 0xFFFF}";
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or InvalidOperationException or ArgumentException or UnauthorizedAccessException)
        { return null; }
    }

    // Returns the offset of the matching entry's child (directory or data entry).
    private static int? Entry(byte[] section, int directory, int id)
    {
        if (directory + 16 > section.Length) return null;
        int named = BitConverter.ToUInt16(section.AsSpan(directory + 12)), ids = BitConverter.ToUInt16(section.AsSpan(directory + 14));
        for (var i = 0; i < named + ids; i++)
        {
            var entry = directory + 16 + i * 8;
            if (entry + 8 > section.Length) return null;
            var name = BitConverter.ToUInt32(section.AsSpan(entry));
            if ((name & 0x80000000) == 0 && name == id) return (int)(BitConverter.ToUInt32(section.AsSpan(entry + 4)) & 0x7FFFFFFF);
        }
        return null;
    }

    private static int? First(byte[] section, int directory)
    {
        if (directory + 24 > section.Length) return null;
        int count = BitConverter.ToUInt16(section.AsSpan(directory + 12)) + BitConverter.ToUInt16(section.AsSpan(directory + 14));
        return count == 0 ? null : (int)(BitConverter.ToUInt32(section.AsSpan(directory + 20)) & 0x7FFFFFFF);
    }

    // "310.9.1.0" → "310.9.1"; matches the Windows app's DlssStreamlineService.FormatVersion.
    public static string Format(string? raw) => RenoDXCommander.Services.DlssVersion.Format(raw);
}
