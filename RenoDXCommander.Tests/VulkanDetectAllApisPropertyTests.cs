using System.Text;
using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for GraphicsApiDetector.DetectAllApis().
/// Uses FsCheck with xUnit. Each property runs a minimum of 100 iterations.
/// </summary>
public class VulkanDetectAllApisPropertyTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var f in _tempFiles)
        {
            try { File.Delete(f); } catch { }
        }
    }

    /// <summary>
    /// Known graphics DLLs that map directly to a GraphicsApiType in the DllMap.
    /// Excludes d3d10_1.dll since it maps to the same API as d3d10.dll and
    /// dxgi.dll which has special inference logic.
    /// </summary>
    private static readonly (string dll, GraphicsApiType api)[] KnownDlls =
    {
        ("d3d8.dll",      GraphicsApiType.DirectX8),
        ("d3d9.dll",      GraphicsApiType.DirectX9),
        ("d3d10.dll",     GraphicsApiType.DirectX10),
        ("d3d11.dll",     GraphicsApiType.DirectX11),
        ("d3d12.dll",     GraphicsApiType.DirectX12),
        ("vulkan-1.dll",  GraphicsApiType.Vulkan),
        ("opengl32.dll",  GraphicsApiType.OpenGL),
    };

    /// <summary>
    /// Builds a minimal valid PE byte array (PE32+) with the given DLL names in the import table.
    /// Layout:
    ///   DOS header at 0x00 (MZ + e_lfanew at 0x3C)
    ///   PE header at 0x80 (PE sig + COFF header + optional header with import data directory)
    ///   Section table at 0x178 (one .text section covering the rest)
    ///   Import Directory Table at 0x200
    ///   DLL name strings following the IDT entries
    /// </summary>
    private static byte[] BuildPeWithImports(params string[] dllNames)
    {
        const int peOffset = 0x80;
        const int coffHeaderSize = 20;
        const int optionalHeaderSize = 240; // PE32+ optional header
        const int numSections = 1;
        const int importTableStart = 0x200;
        const int idtEntrySize = 20;
        const uint sectionRva = 0x1000;
        const uint sectionFileOffset = 0x200;

        // Calculate sizes
        int idtSize = (dllNames.Length + 1) * idtEntrySize; // +1 for null terminator entry
        int namesStart = importTableStart + idtSize;
        int totalNamesSize = 0;
        foreach (var name in dllNames)
            totalNamesSize += name.Length + 1; // null-terminated
        int totalSize = namesStart + totalNamesSize + 16; // extra padding

        var buffer = new byte[totalSize];

        // DOS header
        buffer[0] = (byte)'M';
        buffer[1] = (byte)'Z';
        BitConverter.GetBytes(peOffset).CopyTo(buffer, 0x3C);

        // PE signature
        buffer[peOffset] = (byte)'P';
        buffer[peOffset + 1] = (byte)'E';

        int coffOffset = peOffset + 4;
        // COFF header: Machine = x64
        BitConverter.GetBytes((ushort)0x8664).CopyTo(buffer, coffOffset);
        // NumberOfSections
        BitConverter.GetBytes((ushort)numSections).CopyTo(buffer, coffOffset + 2);
        // SizeOfOptionalHeader
        BitConverter.GetBytes((ushort)optionalHeaderSize).CopyTo(buffer, coffOffset + 16);

        int optOffset = coffOffset + coffHeaderSize;
        // Optional header magic: PE32+ (0x20B)
        BitConverter.GetBytes((ushort)0x20B).CopyTo(buffer, optOffset);
        // NumberOfRvaAndSizes (at offset 108 in PE32+ optional header)
        BitConverter.GetBytes((uint)16).CopyTo(buffer, optOffset + 108);
        // Import Directory RVA (data directory index 1, at offset 120 for PE32+)
        uint importRva = sectionRva + (importTableStart - sectionFileOffset);
        BitConverter.GetBytes(importRva).CopyTo(buffer, optOffset + 120);
        BitConverter.GetBytes((uint)idtSize).CopyTo(buffer, optOffset + 124);

        // Section table (.text section)
        int secOffset = optOffset + optionalHeaderSize;
        Encoding.ASCII.GetBytes(".text\0\0\0").CopyTo(buffer, secOffset);
        // VirtualSize
        BitConverter.GetBytes((uint)(totalSize - sectionFileOffset + 0x1000)).CopyTo(buffer, secOffset + 8);
        // VirtualAddress
        BitConverter.GetBytes(sectionRva).CopyTo(buffer, secOffset + 12);
        // SizeOfRawData
        BitConverter.GetBytes((uint)(totalSize - sectionFileOffset)).CopyTo(buffer, secOffset + 16);
        // PointerToRawData
        BitConverter.GetBytes(sectionFileOffset).CopyTo(buffer, secOffset + 20);

        // Import Directory Table entries
        int nameOffset = namesStart;
        for (int i = 0; i < dllNames.Length; i++)
        {
            int entryOffset = importTableStart + (i * idtEntrySize);
            // Name RVA (offset 12 in IDT entry)
            uint nameRva = sectionRva + ((uint)nameOffset - sectionFileOffset);
            BitConverter.GetBytes(nameRva).CopyTo(buffer, entryOffset + 12);

            // Write DLL name string
            Encoding.ASCII.GetBytes(dllNames[i]).CopyTo(buffer, nameOffset);
            buffer[nameOffset + dllNames[i].Length] = 0; // null terminator
            nameOffset += dllNames[i].Length + 1;
        }
        // Null terminator IDT entry (all zeros) — already zero-initialized

        return buffer;
    }

    private string WriteTempPe(byte[] content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"detect_all_{Guid.NewGuid():N}.exe");
        File.WriteAllBytes(path, content);
        _tempFiles.Add(path);
        return path;
    }

    /// <summary>
    /// All GraphicsApiType values that count as DirectX for dual-API classification.
    /// </summary>
    private static readonly GraphicsApiType[] DxTypes =
    {
        GraphicsApiType.DirectX8,
        GraphicsApiType.DirectX9,
        GraphicsApiType.DirectX10,
        GraphicsApiType.DirectX11,
        GraphicsApiType.DirectX12,
    };

    /// <summary>
    /// All defined GraphicsApiType values used for generating random subsets.
    /// </summary>
    private static readonly GraphicsApiType[] AllApiTypes =
    {
        GraphicsApiType.Unknown,
        GraphicsApiType.DirectX8,
        GraphicsApiType.DirectX9,
        GraphicsApiType.DirectX10,
        GraphicsApiType.DirectX11,
        GraphicsApiType.DirectX12,
        GraphicsApiType.Vulkan,
        GraphicsApiType.OpenGL,
    };

    // Feature: vulkan-reshade-support, Property 9: Dual-API classification correctness
    /// <summary>
    /// **Validates: Requirements 8.2, 8.3**
    ///
    /// For any set of GraphicsApiType values, IsDualApi() shall return true if and only if
    /// the set contains at least one DirectX type (DirectX8–12) and also contains Vulkan.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property IsDualApi_ReturnsTrueIffSetContainsBothDxAndVulkan()
    {
        // Generate random subsets of all GraphicsApiType values (including empty sets)
        var gen = Gen.SubListOf(AllApiTypes)
            .Select(list => new HashSet<GraphicsApiType>(list));

        return Prop.ForAll(gen.ToArbitrary(), apiSet =>
        {
            bool result = GraphicsApiDetector.IsDualApi(apiSet);

            bool hasVulkan = apiSet.Contains(GraphicsApiType.Vulkan);
            bool hasDx = apiSet.Any(a => DxTypes.Contains(a));
            bool expected = hasVulkan && hasDx;

            return (result == expected)
                .Label($"Set={{{string.Join(", ", apiSet)}}}, Expected={expected}, Got={result}");
        });
    }

    // Feature: vulkan-reshade-support, Property 8: DetectAllApis returns all imported graphics APIs
    /// <summary>
    /// **Validates: Requirements 8.1**
    ///
    /// For any PE file whose import table contains a known subset of graphics API DLLs,
    /// DetectAllApis() shall return a set containing exactly the GraphicsApiType values
    /// corresponding to those DLLs.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property DetectAllApis_ReturnsExactlyTheImportedGraphicsApis()
    {
        // Generate a non-empty subset of known DLLs with unique API types
        var gen = Gen.Elements(KnownDlls)
            .ListOf()
            .Where(list => list.Count > 0)
            .Select(list => list.DistinctBy(x => x.api).ToList());

        return Prop.ForAll(gen.ToArbitrary(), subset =>
        {
            var dllNames = subset.Select(x => x.dll).ToArray();
            var expectedApis = new HashSet<GraphicsApiType>(subset.Select(x => x.api));

            var pe = BuildPeWithImports(dllNames);
            var path = WriteTempPe(pe);
            var result = GraphicsApiDetector.DetectAllApis(path);

            return result.SetEquals(expectedApis)
                .Label($"Expected {{{string.Join(", ", expectedApis)}}} for [{string.Join(", ", dllNames)}], got {{{string.Join(", ", result)}}}");
        });
    }
}
