using System.Security;
using System.Text;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

/// <summary>
/// Detects the graphics API used by a game executable by reading its PE import table.
/// </summary>
public static class GraphicsApiDetector
{
    private const int HeaderBufferSize = 4096; // enough for DOS + PE + section headers
    private const int ImportReadSize = 8192;   // enough for import directory + DLL name strings

    private static readonly Dictionary<string, GraphicsApiType> DllMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["d3d8.dll"]      = GraphicsApiType.DirectX8,
        ["d3d9.dll"]      = GraphicsApiType.DirectX9,
        ["d3d10.dll"]     = GraphicsApiType.DirectX10,
        ["d3d10_1.dll"]   = GraphicsApiType.DirectX10,
        ["d3d11.dll"]     = GraphicsApiType.DirectX11,
        ["d3d12.dll"]     = GraphicsApiType.DirectX12,
        ["vulkan-1.dll"]  = GraphicsApiType.Vulkan,
        ["opengl32.dll"]  = GraphicsApiType.OpenGL,
    };

    /// <summary>
    /// Priority order: higher value = higher priority.
    /// DX12 > Vulkan > DX11 > DX10 > OpenGL > DX9 > DX8.
    /// </summary>
    private static readonly Dictionary<GraphicsApiType, int> Priority = new()
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
    /// Reads the PE import table of the given executable and returns the
    /// highest-priority graphics API found.
    /// Returns <see cref="GraphicsApiType.Unknown"/> on any error.
    /// </summary>
    public static GraphicsApiType Detect(string exePath)
    {
        if (string.IsNullOrEmpty(exePath))
            return GraphicsApiType.Unknown;

        try
        {
            using var stream = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            // Phase 1: Read headers (DOS header, PE header, section table)
            var header = new byte[HeaderBufferSize];
            int headerRead = stream.Read(header, 0, header.Length);

            if (headerRead < 0x40 || header[0] != (byte)'M' || header[1] != (byte)'Z')
            {
                CrashReporter.Log($"[GraphicsApiDetector] Invalid MZ signature in '{exePath}'");
                return GraphicsApiType.Unknown;
            }

            int peOffset = BitConverter.ToInt32(header, 0x3C);
            if (peOffset < 0 || peOffset + 24 > headerRead)
            {
                CrashReporter.Log($"[GraphicsApiDetector] PE offset out of range ({peOffset}) in '{exePath}'");
                return GraphicsApiType.Unknown;
            }

            if (header[peOffset] != (byte)'P' || header[peOffset + 1] != (byte)'E' ||
                header[peOffset + 2] != 0 || header[peOffset + 3] != 0)
            {
                CrashReporter.Log($"[GraphicsApiDetector] Invalid PE signature in '{exePath}'");
                return GraphicsApiType.Unknown;
            }

            int coffOffset = peOffset + 4;
            int numberOfSections = BitConverter.ToUInt16(header, coffOffset + 2);
            int sizeOfOptionalHeader = BitConverter.ToUInt16(header, coffOffset + 16);
            int optionalHeaderOffset = coffOffset + 20;

            if (optionalHeaderOffset + sizeOfOptionalHeader > headerRead)
                return GraphicsApiType.Unknown;

            ushort magic = BitConverter.ToUInt16(header, optionalHeaderOffset);
            int importDirOffset;
            if (magic == 0x10B) // PE32
                importDirOffset = optionalHeaderOffset + 104;
            else if (magic == 0x20B) // PE32+
                importDirOffset = optionalHeaderOffset + 120;
            else
                return GraphicsApiType.Unknown;

            if (importDirOffset + 8 > headerRead)
                return GraphicsApiType.Unknown;

            uint importRva = BitConverter.ToUInt32(header, importDirOffset);
            if (importRva == 0)
                return GraphicsApiType.Unknown;

            // Parse section table to build RVA-to-file-offset mapping
            int sectionTableOffset = optionalHeaderOffset + sizeOfOptionalHeader;
            var sections = new List<(uint va, uint vsize, uint rawPtr)>();
            for (int i = 0; i < numberOfSections; i++)
            {
                int secOff = sectionTableOffset + (i * 40);
                if (secOff + 40 > headerRead) break;
                uint va = BitConverter.ToUInt32(header, secOff + 12);
                uint vs = BitConverter.ToUInt32(header, secOff + 8);
                uint rp = BitConverter.ToUInt32(header, secOff + 20);
                sections.Add((va, vs, rp));
            }

            // Phase 2: Seek to import table and read it
            long importFileOffset = RvaToFileOffset(sections, importRva);
            if (importFileOffset < 0)
                return GraphicsApiType.Unknown;

            stream.Seek(importFileOffset, SeekOrigin.Begin);
            var importBuf = new byte[ImportReadSize];
            int importRead = stream.Read(importBuf, 0, importBuf.Length);

            // Walk Import Directory Table entries (each 20 bytes)
            var bestApi = GraphicsApiType.Unknown;
            int bestPriority = 0;
            bool importsDxgi = false;

            for (int i = 0; ; i++)
            {
                int entryOffset = i * 20;
                if (entryOffset + 20 > importRead)
                    break;

                uint nameRva = BitConverter.ToUInt32(importBuf, entryOffset + 12);
                if (nameRva == 0)
                    break;

                // The DLL name string might be in the same section or a different one.
                // Calculate its file offset and read it.
                string? dllName = ReadDllName(stream, sections, nameRva);
                if (dllName == null)
                    continue;

                // Track dxgi.dll separately — it's the DXGI factory used by DX10/11/12.
                // Many DX12 games import only dxgi.dll without d3d12.dll.
                if (dllName.Equals("dxgi.dll", StringComparison.OrdinalIgnoreCase))
                {
                    importsDxgi = true;
                    continue;
                }

                if (DllMap.TryGetValue(dllName, out var api))
                {
                    int p = Priority[api];
                    if (p > bestPriority)
                    {
                        bestPriority = p;
                        bestApi = api;
                    }
                }
            }

            // If dxgi.dll was imported but no explicit D3D DLL was found (or only
            // a lower-priority API like OpenGL), infer DX12. Modern DX12 games
            // often create devices through DXGI alone without importing d3d12.dll.
            if (importsDxgi && bestPriority < Priority[GraphicsApiType.DirectX11])
                return GraphicsApiType.DirectX12;

            return bestApi;
        }
        catch (FileNotFoundException)
        {
            CrashReporter.Log($"[GraphicsApiDetector] File not found: '{exePath}'");
            return GraphicsApiType.Unknown;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            CrashReporter.Log($"[GraphicsApiDetector] I/O error reading '{exePath}': {ex.Message}");
            return GraphicsApiType.Unknown;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[GraphicsApiDetector] Unexpected error reading '{exePath}': {ex.Message}");
            return GraphicsApiType.Unknown;
        }
    }

    /// <summary>
    /// Reads the PE import table and returns ALL graphics APIs found,
    /// not just the highest-priority one. Returns an empty set on error.
    /// </summary>
    public static HashSet<GraphicsApiType> DetectAllApis(string exePath)
    {
        var result = new HashSet<GraphicsApiType>();

        if (string.IsNullOrEmpty(exePath))
            return result;

        try
        {
            using var stream = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            // Phase 1: Read headers (DOS header, PE header, section table)
            var header = new byte[HeaderBufferSize];
            int headerRead = stream.Read(header, 0, header.Length);

            if (headerRead < 0x40 || header[0] != (byte)'M' || header[1] != (byte)'Z')
                return result;

            int peOffset = BitConverter.ToInt32(header, 0x3C);
            if (peOffset < 0 || peOffset + 24 > headerRead)
                return result;

            if (header[peOffset] != (byte)'P' || header[peOffset + 1] != (byte)'E' ||
                header[peOffset + 2] != 0 || header[peOffset + 3] != 0)
                return result;

            int coffOffset = peOffset + 4;
            int numberOfSections = BitConverter.ToUInt16(header, coffOffset + 2);
            int sizeOfOptionalHeader = BitConverter.ToUInt16(header, coffOffset + 16);
            int optionalHeaderOffset = coffOffset + 20;

            if (optionalHeaderOffset + sizeOfOptionalHeader > headerRead)
                return result;

            ushort magic = BitConverter.ToUInt16(header, optionalHeaderOffset);
            int importDirOffset;
            if (magic == 0x10B) // PE32
                importDirOffset = optionalHeaderOffset + 104;
            else if (magic == 0x20B) // PE32+
                importDirOffset = optionalHeaderOffset + 120;
            else
                return result;

            if (importDirOffset + 8 > headerRead)
                return result;

            uint importRva = BitConverter.ToUInt32(header, importDirOffset);
            if (importRva == 0)
                return result;

            // Parse section table to build RVA-to-file-offset mapping
            int sectionTableOffset = optionalHeaderOffset + sizeOfOptionalHeader;
            var sections = new List<(uint va, uint vsize, uint rawPtr)>();
            for (int i = 0; i < numberOfSections; i++)
            {
                int secOff = sectionTableOffset + (i * 40);
                if (secOff + 40 > headerRead) break;
                uint va = BitConverter.ToUInt32(header, secOff + 12);
                uint vs = BitConverter.ToUInt32(header, secOff + 8);
                uint rp = BitConverter.ToUInt32(header, secOff + 20);
                sections.Add((va, vs, rp));
            }

            // Phase 2: Seek to import table and read it
            long importFileOffset = RvaToFileOffset(sections, importRva);
            if (importFileOffset < 0)
                return result;

            stream.Seek(importFileOffset, SeekOrigin.Begin);
            var importBuf = new byte[ImportReadSize];
            int importRead = stream.Read(importBuf, 0, importBuf.Length);

            // Walk Import Directory Table entries (each 20 bytes)
            bool importsDxgi = false;
            bool hasExplicitDx = false;

            for (int i = 0; ; i++)
            {
                int entryOffset = i * 20;
                if (entryOffset + 20 > importRead)
                    break;

                uint nameRva = BitConverter.ToUInt32(importBuf, entryOffset + 12);
                if (nameRva == 0)
                    break;

                string? dllName = ReadDllName(stream, sections, nameRva);
                if (dllName == null)
                    continue;

                if (dllName.Equals("dxgi.dll", StringComparison.OrdinalIgnoreCase))
                {
                    importsDxgi = true;
                    continue;
                }

                if (DllMap.TryGetValue(dllName, out var api))
                {
                    result.Add(api);
                    if (api is GraphicsApiType.DirectX8 or GraphicsApiType.DirectX9 or
                        GraphicsApiType.DirectX10 or GraphicsApiType.DirectX11 or GraphicsApiType.DirectX12)
                    {
                        hasExplicitDx = true;
                    }
                }
            }

            // If dxgi.dll was imported but no explicit DX DLL was found (or only
            // lower-priority APIs), add DX12 — same inference as Detect().
            if (importsDxgi && !hasExplicitDx)
                result.Add(GraphicsApiType.DirectX12);

            return result;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[GraphicsApiDetector] Error in DetectAllApis for '{exePath}': {ex.Message}");
            return result;
        }
    }

    /// <summary>
    /// Returns true if the API set contains both a DirectX API (DirectX8–12) and Vulkan,
    /// indicating a dual-API game.
    /// </summary>
    public static bool IsDualApi(HashSet<GraphicsApiType> apis)
    {
        if (apis == null || !apis.Contains(GraphicsApiType.Vulkan))
            return false;

        return apis.Contains(GraphicsApiType.DirectX8) ||
               apis.Contains(GraphicsApiType.DirectX9) ||
               apis.Contains(GraphicsApiType.DirectX10) ||
               apis.Contains(GraphicsApiType.DirectX11) ||
               apis.Contains(GraphicsApiType.DirectX12);
    }

    /// <summary>
    /// Returns the short display label for a <see cref="GraphicsApiType"/> value.
    /// </summary>
    public static string GetLabel(GraphicsApiType api) => api switch
    {
        GraphicsApiType.DirectX8  => "DX8",
        GraphicsApiType.DirectX9  => "DX9",
        GraphicsApiType.DirectX10 => "DX10",
        GraphicsApiType.DirectX11 => "DX11/12",
        GraphicsApiType.DirectX12 => "DX11/12",
        GraphicsApiType.Vulkan    => "VLK",
        GraphicsApiType.OpenGL    => "OGL",
        _                         => "",
    };

    /// <summary>
    /// Builds a display label from a set of detected APIs, filtering out legacy/alternative
    /// APIs when a modern API is present. Valid multi-API combos: DX11/12 + VLK only.
    /// OGL, DX9, DX10, DX8 only appear alone.
    /// </summary>
    public static string GetMultiLabel(HashSet<GraphicsApiType> apis, GraphicsApiType primary)
    {
        if (apis.Count <= 1) return GetLabel(primary);

        var hasDx = apis.Contains(GraphicsApiType.DirectX11) || apis.Contains(GraphicsApiType.DirectX12);
        var hasVlk = apis.Contains(GraphicsApiType.Vulkan);

        // Only DX11/12 + VLK is a valid multi-label combo
        if (hasDx && hasVlk)
            return "DX11/12 / VLK";

        // Otherwise just show the primary
        return GetLabel(primary);
    }

    /// <summary>
    /// Detects the graphics API for a Unity game by reading its boot.config file.
    /// Unity's <c>gfx-device-type</c> setting maps to a graphics API; when absent,
    /// Unity defaults to DirectX 11 on Windows.
    /// </summary>
    /// <param name="installPath">The game's install directory (where the exe lives).</param>
    /// <returns>The detected API, or <see cref="GraphicsApiType.Unknown"/> if no Unity data folder is found.</returns>
    public static GraphicsApiType DetectUnityFromBootConfig(string installPath)
    {
        try
        {
            // Find the _Data folder (e.g. "AI-LIMIT_Data")
            foreach (var dir in Directory.GetDirectories(installPath))
            {
                if (!Path.GetFileName(dir).EndsWith("_Data", StringComparison.OrdinalIgnoreCase))
                    continue;

                var bootConfig = Path.Combine(dir, "boot.config");
                if (!File.Exists(bootConfig))
                {
                    // Old Unity builds (Unity 5 and earlier) don't have boot.config
                    // but the _Data folder is still a definitive Unity signal.
                    // Unity defaults to DX11 on Windows.
                    return GraphicsApiType.DirectX11;
                }

                // Parse key=value lines looking for gfx-device-type
                foreach (var line in File.ReadLines(bootConfig))
                {
                    var trimmed = line.Trim();
                    if (!trimmed.StartsWith("gfx-device-type=", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var valueStr = trimmed.Substring("gfx-device-type=".Length).Trim();
                    if (int.TryParse(valueStr, out int deviceType))
                    {
                        return deviceType switch
                        {
                            2  => GraphicsApiType.DirectX9,
                            17 => GraphicsApiType.DirectX11,
                            18 => GraphicsApiType.DirectX12,
                            21 => GraphicsApiType.Vulkan,
                            4  => GraphicsApiType.OpenGL,
                            _  => GraphicsApiType.DirectX11, // unknown value → Unity default
                        };
                    }
                }

                // boot.config exists but no gfx-device-type → Unity defaults to DX11 on Windows
                return GraphicsApiType.DirectX11;
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[GraphicsApiDetector] Error reading Unity boot.config in '{installPath}': {ex.Message}");
        }

        return GraphicsApiType.Unknown;
    }

    /// <summary>
    /// Parses a graphics API string from the manifest into a <see cref="GraphicsApiType"/>.
    /// Accepts values like "DX8", "DX9", "DX10", "DX11", "DX12", "Vulkan", "OpenGL".
    /// Returns <see cref="GraphicsApiType.Unknown"/> for unrecognized values.
    /// </summary>
    public static GraphicsApiType ParseApiString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return GraphicsApiType.Unknown;

        return value.Trim().ToUpperInvariant() switch
        {
            "DX8"    => GraphicsApiType.DirectX8,
            "DX9"    => GraphicsApiType.DirectX9,
            "DX10"   => GraphicsApiType.DirectX10,
            "DX11"   => GraphicsApiType.DirectX11,
            "DX12"   => GraphicsApiType.DirectX12,
            "VULKAN" => GraphicsApiType.Vulkan,
            "VLK"    => GraphicsApiType.Vulkan,
            "OPENGL" => GraphicsApiType.OpenGL,
            "OGL"    => GraphicsApiType.OpenGL,
            _        => GraphicsApiType.Unknown,
        };
    }
    /// <summary>
    /// Parses a comma-separated list of API tags (e.g. "DX12, VLK") into a set
    /// of GraphicsApiType values. Unknown tokens are silently ignored.
    /// Returns an empty set if the input is null/empty or contains no valid tokens.
    /// </summary>
    public static HashSet<GraphicsApiType> ParseApiStrings(string? value)
    {
        var result = new HashSet<GraphicsApiType>();
        if (string.IsNullOrWhiteSpace(value))
            return result;

        foreach (var token in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var api = ParseApiString(token);
            if (api != GraphicsApiType.Unknown)
                result.Add(api);
        }

        return result;
    }

    /// <summary>
    /// Reads a null-terminated DLL name from the file at the given RVA.
    /// </summary>
    private static string? ReadDllName(FileStream stream, List<(uint va, uint vsize, uint rawPtr)> sections, uint nameRva)
    {
        long nameFileOffset = RvaToFileOffset(sections, nameRva);
        if (nameFileOffset < 0)
            return null;

        long savedPos = stream.Position;
        try
        {
            stream.Seek(nameFileOffset, SeekOrigin.Begin);
            var nameBuf = new byte[256]; // DLL names are short
            int nameRead = stream.Read(nameBuf, 0, nameBuf.Length);
            if (nameRead == 0)
                return null;

            int end = 0;
            while (end < nameRead && nameBuf[end] != 0)
                end++;
            return Encoding.ASCII.GetString(nameBuf, 0, end);
        }
        finally
        {
            stream.Seek(savedPos, SeekOrigin.Begin);
        }
    }

    /// <summary>
    /// Converts an RVA to a file offset using the section table.
    /// Returns -1 if the RVA cannot be mapped.
    /// </summary>
    private static long RvaToFileOffset(List<(uint va, uint vsize, uint rawPtr)> sections, uint rva)
    {
        foreach (var (va, vsize, rawPtr) in sections)
        {
            if (rva >= va && rva < va + vsize)
                return rawPtr + (rva - va);
        }
        return -1;
    }
}
