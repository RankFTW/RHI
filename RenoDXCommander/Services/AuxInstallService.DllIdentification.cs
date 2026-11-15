// AuxInstallService.DllIdentification.cs — DLL type identification, foreign DLL backup/restore, ReShade detection
namespace RenoDXCommander.Services;

public partial class AuxInstallService
{
    /// <summary>
    /// Classifies what a dxgi.dll file is based on its content.
    /// </summary>
    public enum DxgiFileType { Unknown, ReShade, OptiScaler, Dxvk }

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

        // Check for OptiScaler binary signatures
        if (OptiScalerService.IsOptiScalerFileStatic(filePath)) return DxgiFileType.OptiScaler;

        // Check for DXVK binary signatures
        if (DxvkService.IsDxvkFileStatic(filePath)) return DxgiFileType.Dxvk;

        return DxgiFileType.Unknown;
    }

    // ── Foreign DLL backup / restore ──────────────────────────────────────────

    /// <summary>Outcome of <see cref="BackupForeignDllChecked"/>.</summary>
    public enum ForeignBackupResult
    {
        /// <summary>No file there, or it's ReShade / a file ReShade may replace (DXVK, OptiScaler as dxgi.dll).</summary>
        NotNeeded,
        /// <summary>The foreign file was moved to .original.</summary>
        BackedUp,
        /// <summary>A non-empty .original already holds an earlier file; nothing was moved.</summary>
        Blocked,
        /// <summary>The move failed; the foreign file is still there.</summary>
        Failed,
    }

    /// <summary>
    /// True when <paramref name="dllPath"/> exists and is a file ReShade must not overwrite without
    /// keeping a backup: for dxgi.dll anything not identified as ReShade / OptiScaler / DXVK; for
    /// DXVK's names (d3d8/9/10core/11) anything not DXVK or ReShade; for every other name
    /// (opengl32, d3d12, version, winmm, dinput8, the .asi, ...) anything that isn't ReShade.
    /// </summary>
    public static bool IsForeignForReShade(string dllPath)
    {
        if (!File.Exists(dllPath)) return false;
        var name = Path.GetFileName(dllPath);
        if (name.Equals("dxgi.dll", StringComparison.OrdinalIgnoreCase))
            return IdentifyDxgiFile(dllPath) == DxgiFileType.Unknown;
        if (IsDxvkManagedDllName(name))
            // ReShade can be installed as d3d9.dll for DX9 games — don't back that up.
            return !DxvkService.IsDxvkFileStatic(dllPath) && !IsReShadeFile(dllPath);
        return !IsReShadeFile(dllPath);
    }

    /// <summary>True when <c>dllPath.original</c> exists and is not empty (a real backup).</summary>
    public static bool HasRealBackup(string dllPath)
    {
        var backupPath = dllPath + ".original";
        try { return File.Exists(backupPath) && new FileInfo(backupPath).Length > 0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[AuxInstallService.HasRealBackup] Can't read '{backupPath}' — {ex.Message}");
            return true; // can't tell: treat as a real backup so it's never replaced
        }
    }

    /// <summary>
    /// If <paramref name="dllPath"/> holds a foreign DLL (<see cref="IsForeignForReShade"/>), moves it
    /// to <c>dllPath + ".original"</c> so ReShade's uninstall can put it back. A non-empty
    /// .original is never replaced (it's the game's first file): the result is then
    /// <see cref="ForeignBackupResult.Blocked"/> and the foreign file stays where it is.
    /// A 0-byte .original (another component's "nothing was here" sentinel) is replaced.
    /// </summary>
    public static ForeignBackupResult BackupForeignDllChecked(string dllPath)
    {
        if (!IsForeignForReShade(dllPath)) return ForeignBackupResult.NotNeeded;

        var name = Path.GetFileName(dllPath);
        var backupPath = dllPath + ".original";
        if (HasRealBackup(dllPath))
        {
            CrashReporter.Log($"[AuxInstallService.BackupForeignDll] {name} isn't ReShade and {name}.original already holds a backup in {Path.GetDirectoryName(dllPath)}; not replacing either");
            return ForeignBackupResult.Blocked;
        }
        try
        {
            if (File.Exists(backupPath)) File.Delete(backupPath); // 0-byte sentinel only (checked above)
            File.Move(dllPath, backupPath);
            CrashReporter.Log($"[AuxInstallService.BackupForeignDll] {name} → {name}.original in {Path.GetDirectoryName(dllPath)}");
            return ForeignBackupResult.BackedUp;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[AuxInstallService.BackupForeignDll] Failed for '{dllPath}' — {ex.Message}");
            return ForeignBackupResult.Failed;
        }
    }

    /// <summary>
    /// <see cref="BackupForeignDllChecked"/> as a bool: true if a backup was made.
    /// </summary>
    public static bool BackupForeignDll(string dllPath) =>
        BackupForeignDllChecked(dllPath) == ForeignBackupResult.BackedUp;

    /// <summary>
    /// Returns true if the given DLL filename is one that DXVK may deploy
    /// (d3d8.dll, d3d9.dll, d3d10core.dll, d3d11.dll). dxgi.dll is handled
    /// separately by <see cref="IdentifyDxgiFile"/>.
    /// </summary>
    private static bool IsDxvkManagedDllName(string fileName) =>
        fileName.Equals("d3d8.dll", StringComparison.OrdinalIgnoreCase)
        || fileName.Equals("d3d9.dll", StringComparison.OrdinalIgnoreCase)
        || fileName.Equals("d3d10core.dll", StringComparison.OrdinalIgnoreCase)
        || fileName.Equals("d3d11.dll", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// If a non-empty <c>.original</c> backup exists for <paramref name="dllPath"/> and the
    /// slot is now vacant, restores the backup to its original name. A 0-byte .original is
    /// another component's sentinel, not a DLL, and is left alone.
    /// </summary>
    public static void RestoreForeignDll(string dllPath)
    {
        var backupPath = dllPath + ".original";
        if (!File.Exists(backupPath)) return;
        if (File.Exists(dllPath)) return;   // slot still occupied — don't overwrite
        if (!HasRealBackup(dllPath))
        {
            CrashReporter.Log($"[AuxInstallService.RestoreForeignDll] {Path.GetFileName(backupPath)} is empty (a sentinel); not restoring it");
            return;
        }

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
        // Exact size match against nightly staged DLLs
        if (File.Exists(RsNightlyStagedPath64) && fileSize == new FileInfo(RsNightlyStagedPath64).Length)
            return true;
        if (File.Exists(RsNightlyStagedPath32) && fileSize == new FileInfo(RsNightlyStagedPath32).Length)
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
        bool hasStagedCopies = File.Exists(RsStagedPath64) || File.Exists(RsStagedPath32)
            || File.Exists(RsNightlyStagedPath64) || File.Exists(RsNightlyStagedPath32);
        if (!hasStagedCopies && fileSize > 2 * 1024 * 1024)
            return true;

        return false;
    }
}
