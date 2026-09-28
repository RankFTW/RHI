using System.IO.Compression;
using System.Text.Json;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RHI.Linux.Core;

public sealed partial class NeuralRenderingSetup
{
    public static bool IsDx9(Game game, GraphicsApiType api) => api == GraphicsApiType.DirectX9;

    private async Task<List<Payload>> DgVoodoo(bool is32, IProgress<string>? progress)
    {
        var versions = catalog.ManifestRoot("dgVoodooVersions");
        var url = versions is { ValueKind: JsonValueKind.Object } list ? list.EnumerateObject().Select(p => p.Value.GetString()).FirstOrDefault(u => u != null) : null;
        url ??= "https://github.com/dege-diosg/dgVoodoo2/releases/download/v2.87.3/dgVoodoo2_87_3.zip";
        progress?.Report("Downloading dgVoodoo2…");
        var zip = await downloads.Fetch(url, progress);
        using var archive = ZipFile.OpenRead(zip);
        var name = is32 ? "MS/x86/D3D9.dll" : "MS/x64/D3D9.dll";
        var entry = archive.Entries.FirstOrDefault(e => DlssCatalog.EntryPath(e).Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new IOException("The dgVoodoo2 download does not contain " + name + ".");
        using var memory = new MemoryStream();
        using (var stream = entry.Open()) await stream.CopyToAsync(memory);
        // Same configuration as Windows RHI: translate D3D9 to D3D11 so the Feeder can hook it.
        const string conf = "; dgVoodoo2 configuration — managed by RHI\n\n[General]\nOutputAPI = d3d11_fl11_0\n\n[DirectX]\nDisableAndPassThru = false\nVideoCard = geforce_9800_gt\nVRAM = 1024\ndgVoodooWatermark = false\n";
        return [new(NrFiles.DgVoodooDll, memory.ToArray()), new(NrFiles.DgVoodooConf, System.Text.Encoding.UTF8.GetBytes(conf))];
    }

}
