using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using RenoDXCommander.Services;

namespace RHI.Linux.Core;

// Read archive entries as bounded streams. Archive paths never become filesystem write targets.
public static class ShaderArchives
{
    private const long MaximumBytes = 256L * 1024 * 1024;

    public static async Task<List<Payload>> ReadPayloads(string archivePath, string assetName, string pack)
    {
        var result = new List<Payload>();
        long total = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        async Task Add(string path, long size, Stream input)
        {
            var relative = RelativePath(path, pack);
            if (relative == null) return;
            if (!seen.Add(relative)) throw new IOException("The shader archive contains duplicate paths.");
            if (size < 0 || size > MaximumBytes - total) throw new IOException("Shader archive is too large.");
            using var output = new MemoryStream();
            await CopyBounded(input, output, MaximumBytes - total);
            total += output.Length;
            result.Add(new(relative, output.ToArray()));
        }

        var extension = Path.GetExtension(assetName);
        if (extension.Equals(".fx", StringComparison.OrdinalIgnoreCase) || extension.Equals(".fxh", StringComparison.OrdinalIgnoreCase))
        {
            var name = assetName.Replace('\\', '/').Split('/').Last();
            using var input = File.OpenRead(archivePath);
            await Add("Shaders/" + name, input.Length, input);
        }
        else
        {
            var signature = new byte[6];
            using (var input = File.OpenRead(archivePath)) _ = input.Read(signature);
            if (signature[0] == 'P' && signature[1] == 'K')
            {
                using var archive = ZipFile.OpenRead(archivePath);
                foreach (var entry in archive.Entries)
                {
                    if (DllArchive.EntryName(entry).Length == 0) continue;
                    if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                        throw new IOException("Shader archives cannot contain symbolic links.");
                    using var input = entry.Open();
                    await Add(DllArchive.EntryPath(entry), entry.Length, input);
                }
            }
            else if (signature.SequenceEqual(new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C }))
            {
                await ReadSevenZip(archivePath, pack, result);
            }
            else throw new IOException("The shader download is not a ZIP, 7z, or shader file.");
        }
        if (result.Count == 0) throw new IOException("The archive contains no shaders or textures.");
        return result;
    }

    private static string? RelativePath(string entryPath, string pack)
    {
        var normalized = entryPath.Replace('\\', '/');
        var parts = normalized.Split('/');
        if (Path.IsPathRooted(normalized) || parts.Any(p => p is "." or ".." || p.Contains(':') || p.Contains('\0')))
            throw new IOException("Unsafe path in shader archive.");
        if (ShaderPackCatalog.IsExcludedShader(parts.Last())) return null;
        var index = Array.FindIndex(parts, p => p.Equals("Shaders", StringComparison.OrdinalIgnoreCase) || p.Equals("Textures", StringComparison.OrdinalIgnoreCase));
        if (index >= 0 && index + 1 < parts.Length)
        {
            var kind = parts[index].Equals("Shaders", StringComparison.OrdinalIgnoreCase) ? "Shaders" : "Textures";
            return $"reshade-shaders/{kind}/{pack.Replace(' ', '-')}/" + string.Join('/', parts.Skip(index + 1));
        }
        // Upstream also accepts packs that put .fx/.fxh directly below the archive root.
        var extension = Path.GetExtension(normalized);
        if (extension.Equals(".fx", StringComparison.OrdinalIgnoreCase) || extension.Equals(".fxh", StringComparison.OrdinalIgnoreCase))
            return $"reshade-shaders/Shaders/{pack.Replace(' ', '-')}/" + string.Join('/', parts.Skip(parts.Length > 1 ? 1 : 0));
        return null;
    }

    private static async Task ReadSevenZip(string archivePath, string pack, List<Payload> result)
    {
        using var listing = new MemoryStream();
        await RunSevenZip(["l", "-slt", "-ba", "-sccUTF-8", "--", archivePath], listing, 4 * 1024 * 1024);
        var members = new List<(string Name, string Relative, long Size)>();
        long total = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var block in Encoding.UTF8.GetString(listing.ToArray()).Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.IsNullOrWhiteSpace(block)) continue;
            var properties = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in block.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = line.IndexOf(" = ", StringComparison.Ordinal);
                if (separator < 0 || !properties.TryAdd(line[..separator], line[(separator + 3)..]))
                    throw new IOException("Invalid shader archive entry metadata.");
            }
            if (!properties.TryGetValue("Path", out var name)) throw new IOException("Missing shader archive entry path.");
            var attributes = properties.GetValueOrDefault("Attributes", "");
            if (attributes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(p => p.StartsWith('l'))
                || properties.ContainsKey("Symbolic Link") || properties.ContainsKey("Hard Link"))
                throw new IOException("Shader archives cannot contain links.");
            var relative = RelativePath(name, pack);
            if (attributes.StartsWith('D') || properties.GetValueOrDefault("Folder") == "+" || relative == null) continue;
            if (!long.TryParse(properties.GetValueOrDefault("Size"), NumberStyles.None, CultureInfo.InvariantCulture, out var size)
                || size > MaximumBytes - total) throw new IOException("Shader archive is too large or has invalid sizes.");
            if (!seen.Add(relative)) throw new IOException("The shader archive contains duplicate paths.");
            total += size;
            members.Add((name, relative, size));
        }
        var temporary = Directory.CreateTempSubdirectory("rhi-shaders-");
        try
        {
            long remaining = MaximumBytes;
            foreach (var member in members)
            {
                // A numbered regular file isolates each member; 7-Zip cannot create a path or symlink.
                var staged = Path.Combine(temporary.FullName, "member");
                await using (var output = File.Create(staged))
                    await RunSevenZip(["x", "-so", "-spd", "-bd", "-y", "-i!" + member.Name, "--", archivePath], output, Math.Min(remaining, member.Size));
                var bytes = await File.ReadAllBytesAsync(staged);
                if (bytes.LongLength != member.Size) throw new IOException("Shader archive entry size does not match its metadata.");
                remaining -= bytes.LongLength;
                result.Add(new(member.Relative, bytes));
            }
        }
        finally { temporary.Delete(true); }
    }

    private static async Task RunSevenZip(string[] arguments, Stream output, long limit)
    {
        var start = new ProcessStartInfo(ArchiveTools.SevenZip) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Could not start the shader archive extractor.");
        var error = ReadError(process.StandardError);
        try
        {
            await CopyBounded(process.StandardOutput.BaseStream, output, limit);
            await process.WaitForExitAsync();
            if (process.ExitCode != 0) throw new IOException("Could not extract shader archive: " + (await error).Trim());
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await error;
            throw;
        }
    }

    private static async Task<string> ReadError(StreamReader reader)
    {
        var message = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer)) != 0)
            if (message.Length < 65536) message.Append(buffer, 0, Math.Min(count, 65536 - message.Length));
        return message.ToString();
    }

    private static async Task CopyBounded(Stream input, Stream output, long limit)
    {
        var buffer = new byte[81920];
        int count;
        while ((count = await input.ReadAsync(buffer)) != 0)
        {
            if (count > limit) throw new IOException("Shader archive is too large.");
            await output.WriteAsync(buffer.AsMemory(0, count));
            limit -= count;
        }
    }
}
