using System.Text;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Unit tests for GraphicsApiDetector edge cases.
/// Validates: Requirements 2.1–2.11, 4.4, 7.2, 7.3
/// </summary>
public class GraphicsApiDetectorEdgeCaseTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var f in _tempFiles)
        {
            try { File.Delete(f); } catch { }
        }
    }

    private string WriteTempFile(byte[] content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"gfx_edge_{Guid.NewGuid():N}.exe");
        File.WriteAllBytes(path, content);
        _tempFiles.Add(path);
        return path;
    }

    /// <summary>Reuse the PE builder from property tests.</summary>
    private static byte[] BuildPeWithImports(params string[] dllNames)
    {
        const int peOffset = 0x80;
        const int coffHeaderSize = 20;
        const int optionalHeaderSize = 240;
        const int numSections = 1;
        const int importTableStart = 0x200;
        const int idtEntrySize = 20;
        const uint sectionRva = 0x1000;
        const uint sectionFileOffset = 0x200;

        int idtSize = (dllNames.Length + 1) * idtEntrySize;
        int namesStart = importTableStart + idtSize;
        int totalNamesSize = 0;
        foreach (var name in dllNames)
            totalNamesSize += name.Length + 1;
        int totalSize = namesStart + totalNamesSize + 16;

        var buffer = new byte[totalSize];

        buffer[0] = (byte)'M';
        buffer[1] = (byte)'Z';
        BitConverter.GetBytes(peOffset).CopyTo(buffer, 0x3C);

        buffer[peOffset] = (byte)'P';
        buffer[peOffset + 1] = (byte)'E';

        int coffOffset = peOffset + 4;
        BitConverter.GetBytes((ushort)0x8664).CopyTo(buffer, coffOffset);
        BitConverter.GetBytes((ushort)numSections).CopyTo(buffer, coffOffset + 2);
        BitConverter.GetBytes((ushort)optionalHeaderSize).CopyTo(buffer, coffOffset + 16);

        int optOffset = coffOffset + coffHeaderSize;
        BitConverter.GetBytes((ushort)0x20B).CopyTo(buffer, optOffset);
        BitConverter.GetBytes((uint)16).CopyTo(buffer, optOffset + 108);
        uint importRva = sectionRva + (importTableStart - sectionFileOffset);
        BitConverter.GetBytes(importRva).CopyTo(buffer, optOffset + 120);
        BitConverter.GetBytes((uint)idtSize).CopyTo(buffer, optOffset + 124);

        int secOffset = optOffset + optionalHeaderSize;
        Encoding.ASCII.GetBytes(".text\0\0\0").CopyTo(buffer, secOffset);
        BitConverter.GetBytes((uint)(totalSize - sectionFileOffset + 0x1000)).CopyTo(buffer, secOffset + 8);
        BitConverter.GetBytes(sectionRva).CopyTo(buffer, secOffset + 12);
        BitConverter.GetBytes((uint)(totalSize - sectionFileOffset)).CopyTo(buffer, secOffset + 16);
        BitConverter.GetBytes(sectionFileOffset).CopyTo(buffer, secOffset + 20);

        int nameOffset = namesStart;
        for (int i = 0; i < dllNames.Length; i++)
        {
            int entryOffset = importTableStart + (i * idtEntrySize);
            uint nameRva = sectionRva + ((uint)nameOffset - sectionFileOffset);
            BitConverter.GetBytes(nameRva).CopyTo(buffer, entryOffset + 12);
            Encoding.ASCII.GetBytes(dllNames[i]).CopyTo(buffer, nameOffset);
            buffer[nameOffset + dllNames[i].Length] = 0;
            nameOffset += dllNames[i].Length + 1;
        }

        return buffer;
    }

    [Fact]
    public void Detect_EmptyFile_ReturnsUnknown()
    {
        var path = WriteTempFile(Array.Empty<byte>());
        Assert.Equal(GraphicsApiType.Unknown, GraphicsApiDetector.Detect(path));
    }

    [Fact]
    public void Detect_TruncatedFile_ReturnsUnknown()
    {
        var path = WriteTempFile(new byte[] { (byte)'M', (byte)'Z' });
        Assert.Equal(GraphicsApiType.Unknown, GraphicsApiDetector.Detect(path));
    }

    [Fact]
    public void Detect_BadMzSignature_ReturnsUnknown()
    {
        var buffer = new byte[256];
        buffer[0] = (byte)'X';
        buffer[1] = (byte)'Y';
        var path = WriteTempFile(buffer);
        Assert.Equal(GraphicsApiType.Unknown, GraphicsApiDetector.Detect(path));
    }

    [Fact]
    public void Detect_BadPeSignature_ReturnsUnknown()
    {
        var buffer = new byte[256];
        buffer[0] = (byte)'M';
        buffer[1] = (byte)'Z';
        BitConverter.GetBytes(0x80).CopyTo(buffer, 0x3C);
        buffer[0x80] = (byte)'X'; // bad PE sig
        buffer[0x81] = (byte)'X';
        var path = WriteTempFile(buffer);
        Assert.Equal(GraphicsApiType.Unknown, GraphicsApiDetector.Detect(path));
    }

    [Fact]
    public void Detect_NonExistentFile_ReturnsUnknown()
    {
        var fakePath = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid():N}.exe");
        Assert.Equal(GraphicsApiType.Unknown, GraphicsApiDetector.Detect(fakePath));
    }

    [Fact]
    public void Detect_NullPath_ReturnsUnknown()
    {
        Assert.Equal(GraphicsApiType.Unknown, GraphicsApiDetector.Detect(null!));
    }

    [Fact]
    public void Detect_EmptyPath_ReturnsUnknown()
    {
        Assert.Equal(GraphicsApiType.Unknown, GraphicsApiDetector.Detect(""));
    }

    [Fact]
    public void Detect_NoGraphicsImports_ReturnsUnknown()
    {
        var pe = BuildPeWithImports("kernel32.dll", "user32.dll", "ntdll.dll");
        var path = WriteTempFile(pe);
        Assert.Equal(GraphicsApiType.Unknown, GraphicsApiDetector.Detect(path));
    }

    [Fact]
    public void Detect_CaseInsensitive_UpperCase_DetectsDX12()
    {
        var pe = BuildPeWithImports("D3D12.DLL");
        var path = WriteTempFile(pe);
        Assert.Equal(GraphicsApiType.DirectX12, GraphicsApiDetector.Detect(path));
    }

    [Fact]
    public void Detect_CaseInsensitive_MixedCase_DetectsVulkan()
    {
        var pe = BuildPeWithImports("Vulkan-1.DLL");
        var path = WriteTempFile(pe);
        Assert.Equal(GraphicsApiType.Vulkan, GraphicsApiDetector.Detect(path));
    }

    [Fact]
    public void Detect_D3d10_1Variant_DetectsDX10()
    {
        var pe = BuildPeWithImports("d3d10_1.dll");
        var path = WriteTempFile(pe);
        Assert.Equal(GraphicsApiType.DirectX10, GraphicsApiDetector.Detect(path));
    }

    [Fact]
    public void GraphicsApiType_HasExactly8Members()
    {
        var members = Enum.GetValues<GraphicsApiType>();
        Assert.Equal(8, members.Length);
    }

    [Fact]
    public void GraphicsApiType_HasExpectedNames()
    {
        var names = Enum.GetNames<GraphicsApiType>();
        Assert.Contains("Unknown", names);
        Assert.Contains("DirectX8", names);
        Assert.Contains("DirectX9", names);
        Assert.Contains("DirectX10", names);
        Assert.Contains("DirectX11", names);
        Assert.Contains("DirectX12", names);
        Assert.Contains("Vulkan", names);
        Assert.Contains("OpenGL", names);
    }

    [Fact]
    public void GraphicsApiType_IsInModelsNamespace()
    {
        var type = typeof(GraphicsApiType);
        Assert.Equal("RenoDXCommander.Models", type.Namespace);
    }

    [Fact]
    public void Detect_RealSystemExe_ReturnsWithoutCrash()
    {
        // Test against a real PE file on the system to verify seek-based reading works
        var notepadPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");
        if (!File.Exists(notepadPath))
            return; // skip if not available

        // notepad.exe doesn't import graphics DLLs, so should return Unknown
        var result = GraphicsApiDetector.Detect(notepadPath);
        Assert.Equal(GraphicsApiType.Unknown, result);
    }
}
