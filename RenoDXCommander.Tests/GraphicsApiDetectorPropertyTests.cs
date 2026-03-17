using System.Text;
using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for the GraphicsApiDetector feature.
/// Uses FsCheck with xUnit. Each property runs a minimum of 100 iterations.
/// </summary>
public class GraphicsApiDetectorPropertyTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var f in _tempFiles)
        {
            try { File.Delete(f); } catch { }
        }
    }

    /// <summary>Known graphics DLL names and their expected GraphicsApiType.</summary>
    private static readonly (string dll, GraphicsApiType api)[] KnownDlls =
    {
        ("d3d8.dll",      GraphicsApiType.DirectX8),
        ("d3d9.dll",      GraphicsApiType.DirectX9),
        ("d3d10.dll",     GraphicsApiType.DirectX10),
        ("d3d10_1.dll",   GraphicsApiType.DirectX10),
        ("d3d11.dll",     GraphicsApiType.DirectX11),
        ("d3d12.dll",     GraphicsApiType.DirectX12),
        ("vulkan-1.dll",  GraphicsApiType.Vulkan),
        ("opengl32.dll",  GraphicsApiType.OpenGL),
    };

    /// <summary>Priority lookup for determining highest-priority API.</summary>
    private static readonly Dictionary<GraphicsApiType, int> PriorityMap = new()
    {
        [GraphicsApiType.DirectX12] = 7,
        [GraphicsApiType.Vulkan]    = 6,
        [GraphicsApiType.DirectX11] = 5,
        [GraphicsApiType.DirectX10] = 4,
        [GraphicsApiType.OpenGL]    = 3,
        [GraphicsApiType.DirectX9]  = 2,
        [GraphicsApiType.DirectX8]  = 1,
        [GraphicsApiType.Unknown]   = 0,
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
        // Import Directory RVA (data directory index 1, at offset 112 + 8 = 120)
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
        var path = Path.Combine(Path.GetTempPath(), $"gfx_api_{Guid.NewGuid():N}.exe");
        File.WriteAllBytes(path, content);
        _tempFiles.Add(path);
        return path;
    }

    // Feature: graphics-api-detection, Property 1: Single DLL detection correctness
    [Property(MaxTest = 100)]
    public Property SingleDll_DetectsCorrectApi()
    {
        var gen = Gen.Elements(KnownDlls);
        return Prop.ForAll(gen.ToArbitrary(), entry =>
        {
            var pe = BuildPeWithImports(entry.dll);
            var path = WriteTempPe(pe);
            var result = GraphicsApiDetector.Detect(path);
            return (result == entry.api)
                .Label($"Expected {entry.api} for {entry.dll}, got {result}");
        });
    }

    // Feature: graphics-api-detection, Property 2: Multi-import priority resolution
    [Property(MaxTest = 100)]
    public Property MultiImport_ReturnsHighestPriority()
    {
        // Generate a non-empty subset of known DLLs (use unique API types to avoid duplicates)
        var uniqueDlls = new (string dll, GraphicsApiType api)[]
        {
            ("d3d8.dll",      GraphicsApiType.DirectX8),
            ("d3d9.dll",      GraphicsApiType.DirectX9),
            ("d3d10.dll",     GraphicsApiType.DirectX10),
            ("d3d11.dll",     GraphicsApiType.DirectX11),
            ("d3d12.dll",     GraphicsApiType.DirectX12),
            ("vulkan-1.dll",  GraphicsApiType.Vulkan),
            ("opengl32.dll",  GraphicsApiType.OpenGL),
        };

        var gen = Gen.Elements(uniqueDlls)
            .ListOf()
            .Where(list => list.Count > 0)
            .Select(list => list.DistinctBy(x => x.api).ToList());

        return Prop.ForAll(gen.ToArbitrary(), subset =>
        {
            var dllNames = subset.Select(x => x.dll).ToArray();
            var pe = BuildPeWithImports(dllNames);
            var path = WriteTempPe(pe);
            var result = GraphicsApiDetector.Detect(path);

            var expectedApi = subset.OrderByDescending(x => PriorityMap[x.api]).First().api;
            return (result == expectedApi)
                .Label($"Expected {expectedApi} for [{string.Join(", ", dllNames)}], got {result}");
        });
    }

    // Feature: graphics-api-detection, Property 3: Label mapping correctness
    [Property(MaxTest = 100)]
    public Property GetLabel_ReturnsExpectedString()
    {
        var expectedLabels = new Dictionary<GraphicsApiType, string>
        {
            [GraphicsApiType.Unknown]   = "",
            [GraphicsApiType.DirectX8]  = "DX8",
            [GraphicsApiType.DirectX9]  = "DX9",
            [GraphicsApiType.DirectX10] = "DX10",
            [GraphicsApiType.DirectX11] = "DX11/12",
            [GraphicsApiType.DirectX12] = "DX11/12",
            [GraphicsApiType.Vulkan]    = "VLK",
            [GraphicsApiType.OpenGL]    = "OGL",
        };

        var gen = Gen.Elements(Enum.GetValues<GraphicsApiType>());
        return Prop.ForAll(gen.ToArbitrary(), api =>
        {
            var label = GraphicsApiDetector.GetLabel(api);
            return (label == expectedLabels[api])
                .Label($"Expected '{expectedLabels[api]}' for {api}, got '{label}'");
        });
    }

    // Feature: graphics-api-detection, Property 4: Badge visibility is determined by detection result
    [Property(MaxTest = 100)]
    public Property BadgeVisibility_MatchesDetectionResult()
    {
        var gen = Gen.Elements(Enum.GetValues<GraphicsApiType>());
        return Prop.ForAll(gen.ToArbitrary(), api =>
        {
            var card = new GameCardViewModel
            {
                GraphicsApi = api,
                EngineHint = "",
            };
            var expected = api != GraphicsApiType.Unknown;
            return (card.HasGraphicsApiBadge == expected)
                .Label($"HasGraphicsApiBadge should be {expected} for {api}, got {card.HasGraphicsApiBadge}");
        });
    }
}
