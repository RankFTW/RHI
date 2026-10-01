using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using RenoDXCommander.Services;

namespace RHI.Linux.Core;

public sealed class Downloads(HttpClient http)
{
    public static HttpClient CreateClient()
    {
        CoreLog.Sink = CrashReporter.Log;
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(Sources.UserAgent);
        return client;
    }

    public async Task<string> Fetch(string url, IProgress<string>? progress = null, bool refresh = false)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https") throw new IOException("Downloads require an HTTPS URL.");
        Directory.CreateDirectory(LinuxPaths.Cache);
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
        string path = Path.Combine(LinuxPaths.Cache, hash + ".download");
        if (!refresh && File.Exists(path)) return path;
        progress?.Report("Downloading " + Path.GetFileName(uri.AbsolutePath));
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (uri.Host.EndsWith("reshade.me", StringComparison.Ordinal)) request.Headers.Referrer = new Uri(Sources.ReShade);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        string temp = path + "." + Guid.NewGuid().ToString("N");
        try
        {
            await using (var input = await response.Content.ReadAsStreamAsync())
            await using (var output = File.Create(temp))
            {
                var buffer = new byte[131072]; long total = 0; int read;
                while ((read = await input.ReadAsync(buffer)) > 0)
                {
                    total += read;
                    if (total > 512L * 1024 * 1024) throw new IOException("Download exceeds the 512 MiB component limit.");
                    await output.WriteAsync(buffer.AsMemory(0, read));
                }
                if (response.Content.Headers.ContentLength is { } expected && total != expected) throw new IOException("Incomplete download.");
                if (total == 0) throw new IOException("The server returned an empty file.");
            }
            File.Move(temp, path, true);
            return path;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public async Task<(string Path, string Version)> ReShade(string channel, MachineType architecture, IProgress<string>? progress = null)
    {
        RequireArchitecture(architecture);
        var bits = architecture == MachineType.I386 ? 32 : 64;
        string archive, version;
        if (channel == "Nightly")
        {
            version = "Nightly " + DateTime.UtcNow.ToString("yyyy-MM-dd");
            archive = await Fetch(Sources.ReShadeNightly(bits), progress, true);
        }
        else
        {
            var html = await http.GetStringAsync(Sources.ReShade);
            var match = Regex.Match(html, @"/downloads/ReShade_Setup_([\d.]+)_Addon\.exe", RegexOptions.IgnoreCase);
            if (!match.Success) throw new IOException("Could not find the current ReShade addon installer on reshade.me. Use a local ReShade DLL or try again later.");
            version = match.Groups[1].Value;
            archive = await Fetch(new Uri(new Uri(Sources.ReShade), match.Value).AbsoluteUri, progress);
        }
        var dest = Path.Combine(LinuxPaths.Cache, $"reshade-{Guid.NewGuid():N}.dll");
        await Extract(archive, $"ReShade{bits}.dll", dest);
        try { ValidatePe(dest, architecture); }
        catch { File.Delete(dest); File.Delete(archive); throw; }
        return (dest, version);
    }

    public static async Task Extract(string archive, string entry, string output)
    {
        // 7z understands the appended archive in the official Windows installer; no Wine needed.
        var start = new ProcessStartInfo(ArchiveTools.SevenZip) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in new[] { "e", "-so", archive, entry }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("Could not start 7z. Install 7zip or use a local DLL.");
        var error = process.StandardError.ReadToEndAsync();
        await using (var stream = File.Create(output)) await process.StandardOutput.BaseStream.CopyToAsync(stream);
        await process.WaitForExitAsync();
        if (process.ExitCode != 0 || !File.Exists(output) || new FileInfo(output).Length == 0)
        { File.Delete(output); throw new IOException("Could not extract " + entry + ": " + await error); }
    }

    public async Task<Payload> ShaderCompiler(MachineType architecture, IProgress<string>? progress = null)
    {
        RequireArchitecture(architecture);
        // Microsoft compiler redistributed by Mozilla, pinned to the verified archives
        // used by reshade-steam-proton. Supports SM5.1 (the 2013 SDK compiler does not).
        var bits = architecture == MachineType.I386 ? 32 : 64;
        var expected = Sources.CompilerArchiveSha256(bits);
        var archive = await Fetch(Sources.CompilerArchive(bits), progress);
        using (var stream = File.OpenRead(archive))
            if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Shader compiler archive checksum mismatch. Delete the cached download and retry.");
        var path = Path.Combine(LinuxPaths.Cache, "compiler-" + Guid.NewGuid().ToString("N") + ".dll");
        try
        {
            await Extract(archive, "core/d3dcompiler_47.dll", path);
            ValidatePe(path, architecture);
            // Preserve a compiler already supplied by the game.
            return new("d3dcompiler_47.dll", await File.ReadAllBytesAsync(path), PreserveExisting: true);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    public static void RequireArchitecture(MachineType architecture)
    {
        if (architecture is not MachineType.I386 and not MachineType.x64) throw new IOException("Select a valid x86 or x64 Windows game executable.");
    }

    public static void ValidatePe(string path, MachineType architecture)
    {
        RequireArchitecture(architecture);
        if (new PeHeaderService().DetectArchitecture(path) != architecture) throw new IOException("The downloaded/local DLL is invalid or has the wrong architecture for this game.");
    }

    public async Task<List<Payload>> Shaders(string pack, IProgress<string>? progress = null)
    {
        var catalog = new ShaderPackCatalog();
        catalog.ApplyManifestOverrides(Catalog.LoadManifestSnapshot().Deserialize<RenoDXCommander.Models.RemoteManifest>());
        var id = pack switch
        {
            "Lilium HDR" => "Lilium",
            "Standard" => "CrosireMaster",
            _ => throw new ArgumentException("Unknown shader pack.")
        };
        var source = catalog.FindPack(id) ?? throw new IOException("This shader pack is disabled by the manifest.");
        var url = source.PortableZipUrl ?? source.Url;
        var assetName = Path.GetFileName(new Uri(url).AbsolutePath);
        if (source.PortableZipUrl == null && source.Kind == ShaderPackCatalog.SourceKind.GhRelease)
        {
            var json = await new GitHubETagCache(CrashReporter.Log).GetWithETagAsync(http, url)
                ?? throw new IOException("Could not retrieve the shader pack release.");
            using var release = JsonDocument.Parse(json);
            var selected = ShaderPackCatalog.ResolveRelease(release.RootElement, source.AssetExt);
            url = selected.Url ?? throw new IOException("The shader release has no matching download.");
            assetName = selected.Name;
        }
        var archive = await Fetch(url, progress, true);
        return await ShaderArchives.ReadPayloads(archive, assetName, pack);
    }
}
