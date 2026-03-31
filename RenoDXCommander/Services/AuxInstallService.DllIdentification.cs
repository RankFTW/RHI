// AuxInstallService.DllIdentification.cs — DLL type identification, foreign DLL backup/restore, ReShade detection
namespace RenoDXCommander.Services;

public partial class AuxInstallService
{
    /// <summary>
    /// Classifies what a dxgi.dll file is based on its content.
    /// </summary>
    public enum DxgiFileType { Unknown, ReShade }

    /// <summary>
    /// Identifies what type of dxgi.dll is at the given path.
    /// Uses strict checks: exact size match against known staged binaries,
    /// and binary string scanning for definitive markers.
    /// Returns Unknown unless there is positive evidence — never guesses based on size alone.
    /// </summary>
    public static DxgiFileType IdentifyDxgiFile(string filePath)
    {
        if (!File.Exists(filePath)) return DxgiFileType.Unknown;

        if (IsReShadeFileStrict(filePath)) return DxgiFileType.ReShade;

        return DxgiFileType.Unknown;
    }

    // ── Foreign DLL backup / restore ──────────────────────────────────────────

    /// <summary>
    /// If <paramref name="dllPath"/> exists and is a foreign (unrecognised) DLL,
    /// renames it to <c>dllPath + ".original"</c> so it is preserved.
    /// Only applies to <c>dxgi.dll</c> and <c>winmm.dll</c>.
    /// Returns true if a backup was made.
    /// </summary>
    public static bool BackupForeignDll(string dllPath)
    {
        if (!File.Exists(dllPath)) return false;

        var name = Path.GetFileName(dllPath);
        bool isForeign;
        if (name.Equals("dxgi.dll", StringComparison.OrdinalIgnoreCase))
            isForeign = IdentifyDxgiFile(dllPath) == DxgiFileType.Unknown;
        else
            return false;

        if (!isForeign) return false;

        var backupPath = dllPath + ".original";
        try
        {
            if (File.Exists(backupPath)) File.Delete(backupPath);
            File.Move(dllPath, backupPath);
            CrashReporter.Log($"[AuxInstallService.BackupForeignDll] {name} → {name}.original in {Path.GetDirectoryName(dllPath)}");
            return true;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[AuxInstallService.BackupForeignDll] Failed for '{dllPath}' — {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// If a <c>.original</c> backup exists for <paramref name="dllPath"/> and the
    /// slot is now vacant, restores the backup to its original name.
    /// </summary>
    public static void RestoreForeignDll(string dllPath)
    {
        var backupPath = dllPath + ".original";
        if (!File.Exists(backupPath)) return;
        if (File.Exists(dllPath)) return;   // slot still occupied — don't overwrite

        try
        {
            File.Move(backupPath, dllPath);
            var name = Path.GetFileName(dllPath);
            CrashReporter.Log($"[AuxInstallService.RestoreForeignDll] {name}.original → {name} in {Path.GetDirectoryName(dllPath)}");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[AuxInstallService.RestoreForeignDll] Failed for '{dllPath}' — {ex.Message}");
        }
    }

    /// <summary>
    /// Strict ReShade check: exact size match against staged ReShade DLLs,
    /// OR binary scan for ReShade-specific strings. Never falls back to a size threshold.
    /// </summary>
    public static bool IsReShadeFileStrict(string filePath)
    {
        if (!File.Exists(filePath)) return false;
        var fileSize = new FileInfo(filePath).Length;

        // ReShade DLLs are typically 4-8 MB. Files over 15 MB are almost certainly
        // something else (e.g. OptiScaler at ~28 MB) that may contain "ReShade"
        // in config comments but are not actually ReShade.
        if (fileSize > 15 * 1024 * 1024) return false;

        // Exact size match against staged ReShade64.dll
        if (File.Exists(RsStagedPath64) && fileSize == new FileInfo(RsStagedPath64).Length)
            return true;
        // Exact size match against staged ReShade32.dll
        if (File.Exists(RsStagedPath32) && fileSize == new FileInfo(RsStagedPath32).Length)
            return true;

        // Binary string scan for ReShade markers
        // Only match on strings unique to the actual ReShade binary — "reshade.me" (the URL
        // embedded in the PE resources) and "crosire" (the author name). Do NOT match on
        // generic phrases like "ReShade DLL" which appear in config comments of tools like
        // OptiScaler that load ReShade externally.
        try
        {
            var bytes = File.ReadAllBytes(filePath);
            var text = System.Text.Encoding.ASCII.GetString(bytes);
            if (text.Contains("ReShade", StringComparison.Ordinal) &&
                (text.Contains("reshade.me", StringComparison.OrdinalIgnoreCase) ||
                 text.Contains("crosire", StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        catch (Exception ex) { CrashReporter.Log($"[AuxInstallService.IsReShadeFileStrict] Binary scan failed for '{filePath}' — {ex.Message}"); }

        return false;
    }

    /// <summary>
    /// Returns true if the file at <paramref name="filePath"/> is a ReShade DLL.
    /// Uses exact size match against staged copies first, then binary string scan.
    /// Falls back to a 2 MB size threshold ONLY if neither staged copies nor
    /// binary markers are available — this is the legacy heuristic and is less reliable.
    /// </summary>
    public static bool IsReShadeFile(string filePath)
    {
        if (!File.Exists(filePath)) return false;

        // Prefer the strict check which requires positive evidence
        if (IsReShadeFileStrict(filePath)) return true;

        // Legacy fallback: if no staged copies exist AND binary scan didn't find markers,
        // use the size heuristic. This only triggers on first run before staging.
        var fileSize = new FileInfo(filePath).Length;
        bool hasStagedCopies = File.Exists(RsStagedPath64) || File.Exists(RsStagedPath32);
        if (!hasStagedCopies && fileSize > 2 * 1024 * 1024)
            return true;

        return false;
    }
}
