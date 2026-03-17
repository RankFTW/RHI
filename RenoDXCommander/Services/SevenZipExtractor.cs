using System.IO.Compression;
using System.Diagnostics;

namespace RenoDXCommander.Services;

/// <summary>
/// Extracts named files from a ReShade installer exe.
///
/// ReShade 6.7.3+ uses an NSIS installer which cannot be opened by System.IO.Compression
/// or SharpCompress. Only 7-Zip (7z.exe + 7z.dll) can extract NSIS archives.
/// Falls back to ZIP extraction for older ReShade versions.
/// </summary>
public class ReShadeExtractor : ISevenZipExtractor
{
    /// <summary>
    /// Extracts a single file from the ReShade installer exe.
    /// Tries System.IO.Compression (ZIP) first, then 7-Zip for NSIS.
    /// </summary>
    public void ExtractFile(string exePath, string entryName, string outputPath)
    {
        if (!File.Exists(exePath))
            throw new FileNotFoundException($"ReShade installer not found: {exePath}");

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        // ── Strategy 1: ZIP (appended archive — older ReShade versions) ─────
        try
        {
            using var zip = ZipFile.OpenRead(exePath);
            var entry = zip.Entries.FirstOrDefault(e =>
                e.Name.Equals(entryName, StringComparison.OrdinalIgnoreCase));

            if (entry != null)
            {
                using var src  = entry.Open();
                using var dest = File.Create(outputPath);
                src.CopyTo(dest);
                CrashReporter.Log($"[ReShadeExtractor.ExtractFile] Extracted '{entryName}' via ZIP");
                return;
            }

            var zipNames = string.Join(", ", zip.Entries.Select(e => e.FullName));
            CrashReporter.Log($"[ReShadeExtractor.ExtractFile] ZIP opened but '{entryName}' not found. Entries: [{zipNames}]");
        }
        catch (InvalidDataException)
        {
            CrashReporter.Log("[ReShadeExtractor.ExtractFile] Not a ZIP archive, trying 7-Zip");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[ReShadeExtractor.ExtractFile] ZIP failed ({ex.GetType().Name}: {ex.Message}), trying 7-Zip");
        }

        // ── Strategy 2: 7-Zip (NSIS installer — ReShade 6.7.3+) ────────────
        var sevenZipPath = Find7ZipExe();
        if (sevenZipPath != null)
        {
            if (ExtractWith7Zip(sevenZipPath, exePath, entryName, outputPath))
                return;
        }
        else
        {
            CrashReporter.Log("[ReShadeExtractor.ExtractFile] 7-Zip not found. Please install 7-Zip from https://www.7-zip.org/");
        }

        throw new FileNotFoundException(
            $"Could not extract '{entryName}' from '{Path.GetFileName(exePath)}'.\n" +
            "The ReShade installer is an NSIS archive which requires 7-Zip to extract.\n" +
            "Please install 7-Zip from https://www.7-zip.org/ and restart RDXC.");
    }

    /// <summary>
    /// Uses 7z.exe to extract a specific file from the NSIS installer.
    /// </summary>
    private static bool ExtractWith7Zip(string sevenZipExe, string archivePath, string entryName, string outputPath)
    {
        try
        {
            // Extract to a temp directory, then move the target file
            var tempDir = Path.Combine(Path.GetTempPath(), $"rdxc_reshade_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                // 7z e <archive> -o<outdir> <filename> -y -r
                // e = extract without paths, -r = recurse (find in subdirs)
                var psi = new ProcessStartInfo
                {
                    FileName = sevenZipExe,
                    Arguments = $"e \"{archivePath}\" -o\"{tempDir}\" \"{entryName}\" -y -r",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };

                CrashReporter.Log($"[ReShadeExtractor.ExtractWith7Zip] Running {psi.FileName} {psi.Arguments}");

                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    CrashReporter.Log("[ReShadeExtractor.ExtractWith7Zip] Failed to start 7z process");
                    return false;
                }

                var stdout = proc.StandardOutput.ReadToEnd();
                var stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit(30_000); // 30 second timeout

                CrashReporter.Log($"[ReShadeExtractor.ExtractWith7Zip] 7z exit={proc.ExitCode}, stdout={stdout.Length} chars");
                if (!string.IsNullOrWhiteSpace(stderr))
                    CrashReporter.Log($"[ReShadeExtractor.ExtractWith7Zip] 7z stderr: {stderr}");

                // Find the extracted file
                var extracted = Path.Combine(tempDir, entryName);
                if (File.Exists(extracted))
                {
                    File.Copy(extracted, outputPath, overwrite: true);
                    CrashReporter.Log($"[ReShadeExtractor.ExtractWith7Zip] Extracted '{entryName}' via 7-Zip ({new FileInfo(outputPath).Length} bytes)");
                    return true;
                }

                // Maybe it's in a subdirectory
                var found = Directory.GetFiles(tempDir, entryName, SearchOption.AllDirectories).FirstOrDefault();
                if (found != null)
                {
                    File.Copy(found, outputPath, overwrite: true);
                    CrashReporter.Log($"[ReShadeExtractor.ExtractWith7Zip] Extracted '{entryName}' via 7-Zip from subdir ({new FileInfo(outputPath).Length} bytes)");
                    return true;
                }

                // List what was extracted for diagnostics
                var files = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories);
                CrashReporter.Log($"[ReShadeExtractor.ExtractWith7Zip] '{entryName}' not found in 7z output. Extracted files: [{string.Join(", ", files.Select(Path.GetFileName))}]");
                return false;
            }
            finally
            {
                try { Directory.Delete(tempDir, recursive: true); } catch (Exception ex) { CrashReporter.Log($"[ReShadeExtractor] Operation failed — {ex.Message}"); }
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[ReShadeExtractor.ExtractWith7Zip] 7-Zip extraction failed — {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Finds 7z.exe on the system. Checks common install locations and PATH.
    /// </summary>
    public string? Find7ZipExe()
    {
        // Check bundled 7z.exe next to the app exe first
        var bundled = Path.Combine(AppContext.BaseDirectory, "7z.exe");
        if (File.Exists(bundled))
        {
            CrashReporter.Log($"[ReShadeExtractor.Find7ZipExe] Using bundled 7-Zip at {bundled}");
            return bundled;
        }

        // Check common install locations
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "7-Zip", "7z.exe"),
            @"C:\Program Files\7-Zip\7z.exe",
            @"C:\Program Files (x86)\7-Zip\7z.exe",
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path))
            {
                CrashReporter.Log($"[ReShadeExtractor.Find7ZipExe] Found 7-Zip at {path}");
                return path;
            }
        }

        // Check PATH
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "7z.exe",
                Arguments = "--help",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                proc.WaitForExit(5000);
                CrashReporter.Log("[ReShadeExtractor.Find7ZipExe] Found 7z.exe on PATH");
                return "7z.exe";
            }
        }
        catch (Exception ex) { CrashReporter.Log($"[ReShadeExtractor] Operation failed — {ex.Message}"); }

        CrashReporter.Log("[ReShadeExtractor.Find7ZipExe] 7-Zip not found at any known location");
        return null;
    }

    /// <summary>
    /// Lists all entry names found inside the archive (for diagnostics).
    /// </summary>
    public List<string> ListEntries(string exePath)
    {
        var results = new List<string>();
        try
        {
            using var zip = ZipFile.OpenRead(exePath);
            results.AddRange(zip.Entries.Select(e => $"[ZIP] {e.FullName}"));
        }
        catch (Exception ex) { CrashReporter.Log($"[ReShadeExtractor] Operation failed — {ex.Message}"); }

        // Try 7z listing
        var sevenZip = Find7ZipExe();
        if (sevenZip != null)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = sevenZip,
                    Arguments = $"l \"{exePath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    var output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(10_000);
                    results.Add($"[7z listing] {output[..Math.Min(output.Length, 2000)]}");
                }
            }
            catch (Exception ex) { CrashReporter.Log($"[ReShadeExtractor] Operation failed — {ex.Message}"); }
        }

        return results;
    }
}
