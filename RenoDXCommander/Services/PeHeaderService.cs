using System.Security;

namespace RenoDXCommander.Services;

/// <summary>PE machine architecture values from the COFF header.</summary>
public enum MachineType : ushort
{
    Native  = 0x0000,
    I386    = 0x014C,
    Itanium = 0x0200,
    x64     = 0x8664,
}

public class PeHeaderService : IPeHeaderService
{
    private const int PeHeaderBufferSize = 4096;

    /// <summary>
    /// Reads the PE header of the given file and returns its MachineType.
    /// Returns MachineType.Native on any error (missing file, invalid PE, I/O).
    /// Reads at most 4096 bytes.
    /// </summary>
    public MachineType DetectArchitecture(string exePath)
    {
        try
        {
            using var stream = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[PeHeaderBufferSize];
            int bytesRead = stream.Read(buffer, 0, buffer.Length);

            // Validate MZ signature at offset 0
            if (bytesRead < 2 || buffer[0] != (byte)'M' || buffer[1] != (byte)'Z')
            {
                CrashReporter.Log($"[PeHeaderService] Invalid MZ signature in '{exePath}'");
                return MachineType.Native;
            }

            // Read e_lfanew at offset 0x3C (Int32 — offset to PE header)
            if (bytesRead < 0x3C + 4)
            {
                CrashReporter.Log($"[PeHeaderService] File too small to contain e_lfanew: '{exePath}'");
                return MachineType.Native;
            }

            int peOffset = BitConverter.ToInt32(buffer, 0x3C);

            // Validate PE signature at peOffset (bytes 'P','E',0,0)
            if (peOffset < 0 || peOffset + 6 > bytesRead)
            {
                CrashReporter.Log($"[PeHeaderService] PE offset out of range ({peOffset}) in '{exePath}'");
                return MachineType.Native;
            }

            if (buffer[peOffset] != (byte)'P' || buffer[peOffset + 1] != (byte)'E' ||
                buffer[peOffset + 2] != 0 || buffer[peOffset + 3] != 0)
            {
                CrashReporter.Log($"[PeHeaderService] Invalid PE signature in '{exePath}'");
                return MachineType.Native;
            }

            // Read Machine field at PE offset + 4 (UInt16)
            ushort machineValue = BitConverter.ToUInt16(buffer, peOffset + 4);
            var machineType = (MachineType)machineValue;

            CrashReporter.Log($"[PeHeaderService] Detected {machineType} (0x{machineValue:X4}) for '{exePath}'");
            return machineType;
        }
        catch (FileNotFoundException)
        {
            CrashReporter.Log($"[PeHeaderService] File not found: '{exePath}'");
            return MachineType.Native;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            CrashReporter.Log($"[PeHeaderService] I/O error reading '{exePath}': {ex.Message}");
            return MachineType.Native;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[PeHeaderService] Unexpected error reading '{exePath}': {ex.Message}");
            return MachineType.Native;
        }
    }

    /// <summary>
    /// Searches the given directory (non-recursive) for .exe files and returns
    /// the path of the largest one by file size. Returns null if none found or
    /// the directory is inaccessible.
    /// </summary>
    public string? FindGameExe(string installPath)
    {
        try
        {
            var dir = new DirectoryInfo(installPath);
            if (!dir.Exists)
            {
                CrashReporter.Log($"[PeHeaderService] Install directory does not exist: '{installPath}'");
                return null;
            }

            var exeFiles = dir.GetFiles("*.exe", SearchOption.TopDirectoryOnly);
            if (exeFiles.Length == 0)
            {
                CrashReporter.Log($"[PeHeaderService] No .exe files found in '{installPath}'");
                return null;
            }

            var largest = exeFiles[0];
            for (int i = 1; i < exeFiles.Length; i++)
            {
                if (exeFiles[i].Length > largest.Length)
                    largest = exeFiles[i];
            }

            return largest.FullName;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            CrashReporter.Log($"[PeHeaderService] Error accessing directory '{installPath}': {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[PeHeaderService] Unexpected error scanning '{installPath}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Convenience: finds the game exe and detects its architecture.
    /// Returns MachineType.Native if no exe is found.
    /// </summary>
    public MachineType DetectGameArchitecture(string installPath)
    {
        string? exePath = FindGameExe(installPath);
        if (exePath is null)
        {
            CrashReporter.Log($"[PeHeaderService] No game executable found in '{installPath}', defaulting to Native");
            return MachineType.Native;
        }

        return DetectArchitecture(exePath);
    }
}
