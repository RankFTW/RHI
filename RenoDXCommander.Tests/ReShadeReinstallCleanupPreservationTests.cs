using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.Tests;

/// <summary>
/// Preservation property tests for ReShade reinstall cleanup.
/// These tests capture the EXISTING correct behavior on UNFIXED code for non-buggy inputs
/// (cases where isBugCondition returns false) and must PASS both before and after the fix.
///
/// **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5**
///
/// EXPECTED OUTCOME on UNFIXED code: All tests PASS.
/// After the fix is applied: All tests MUST still PASS (no regressions).
/// </summary>
public class ReShadeReinstallCleanupPreservationTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly AuxInstallService _service;
    private readonly List<AuxInstalledRecord> _seededRecords = new();

    public ReShadeReinstallCleanupPreservationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "RdxcPreserve_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempRoot);
        _service = new AuxInstallService(new HttpClient());
    }

    public void Dispose()
    {
        foreach (var record in _seededRecords)
        {
            try { _service.RemoveRecord(record); } catch { }
        }
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates fake DLL bytes that pass IsReShadeFileStrict's binary signature scan.
    /// Contains "ReShade", "reshade.me", and "crosire" as ASCII strings, well under 15 MB.
    /// </summary>
    private static byte[] CreateFakeReShadeBytes()
    {
        var content = new byte[1024];
        var rng = new System.Random(42);
        rng.NextBytes(content);
        var markers = "ReShade version 5.9.2 | https://reshade.me | by crosire"u8;
        markers.CopyTo(content.AsSpan(256));
        return content;
    }

    /// <summary>
    /// Creates fake DLL bytes that do NOT pass IsReShadeFileStrict (foreign DLL).
    /// Contains no ReShade markers — just random bytes.
    /// </summary>
    private static byte[] CreateForeignDllBytes()
    {
        var content = new byte[1024];
        var rng = new System.Random(99);
        rng.NextBytes(content);
        // Ensure no accidental ReShade markers
        System.Text.Encoding.ASCII.GetBytes("SomeForeignDLL v1.0").CopyTo(content.AsSpan(256));
        return content;
    }

    /// <summary>
    /// Ensures the ReShade staging directory has a 64-bit DLL so InstallReShadeAsync
    /// can copy it to the game folder.
    /// </summary>
    private static void EnsureStagedReShadeExists()
    {
        Directory.CreateDirectory(AuxInstallService.RsStagingDir);
        if (!File.Exists(AuxInstallService.RsStagedPath64))
        {
            File.WriteAllBytes(AuxInstallService.RsStagedPath64, CreateFakeReShadeBytes());
        }
    }

    // ── Property 2a: Fresh install — no prior record, destination DLL installed ──

    /// <summary>
    /// Property 2a: For fresh installs (no existing AuxInstalledRecord), only the
    /// destination DLL exists after install, no unexpected file deletions occur.
    ///
    /// Observed on UNFIXED code: InstallReShadeAsync with no prior record copies
    /// the staged DLL to dxgi.dll (dcMode=false), creates a new record, and does
    /// not attempt to delete any prior DLL.
    ///
    /// **Validates: Requirements 3.1, 3.2**
    /// </summary>
    [Property(MaxTest = 3)]
    public Property FreshInstall_OnlyDestinationDllExists()
    {
        // Generate unique game names to avoid collisions
        var genSuffix = Gen.Choose(1000, 99999);

        return Prop.ForAll(
            Arb.From(genSuffix),
            (int suffix) =>
            {
                // ── Arrange ──────────────────────────────────────────────────
                var gameName = $"FreshInstall_{suffix}";
                var gameFolder = Path.Combine(_tempRoot, gameName);
                Directory.CreateDirectory(gameFolder);

                // No existing record — this is a fresh install
                // Place a sentinel file to verify it is NOT deleted
                var sentinelPath = Path.Combine(gameFolder, "game.exe");
                File.WriteAllBytes(sentinelPath, new byte[64]);

                EnsureStagedReShadeExists();

                // ── Act ──────────────────────────────────────────────────────
                var resultRecord = _service.InstallReShadeAsync(
                    gameName, gameFolder,
                    dcMode: false,
                    dcIsInstalled: false).GetAwaiter().GetResult();

                _seededRecords.Add(resultRecord);

                // ── Assert ───────────────────────────────────────────────────
                var destPath = Path.Combine(gameFolder, "dxgi.dll");
                var destExists = File.Exists(destPath);
                var sentinelStillExists = File.Exists(sentinelPath);
                var recordCorrect = resultRecord.InstalledAs == "dxgi.dll"
                    && resultRecord.AddonType == AuxInstallService.TypeReShade;

                return (destExists && sentinelStillExists && recordCorrect)
                    .Label($"Fresh install: destExists={destExists}, " +
                           $"sentinelStillExists={sentinelStillExists}, " +
                           $"record.InstalledAs={resultRecord.InstalledAs}");
            });
    }

    // ── Property 2b: Standard-filename reinstall — file overwritten in place ────

    /// <summary>
    /// Property 2b: For standard-filename reinstalls where InstalledAs matches
    /// destName, the file is overwritten in place with no orphaned DLLs.
    ///
    /// Observed on UNFIXED code: When InstalledAs = "dxgi.dll" and dcMode=false
    /// (destName = "dxgi.dll"), File.Copy overwrites the existing dxgi.dll.
    /// No extra files are created or deleted.
    ///
    /// **Validates: Requirements 3.1, 3.3**
    /// </summary>
    [Property(MaxTest = 3)]
    public Property StandardReinstall_FileOverwrittenInPlace()
    {
        var genSuffix = Gen.Choose(1000, 99999);

        return Prop.ForAll(
            Arb.From(genSuffix),
            (int suffix) =>
            {
                // ── Arrange ──────────────────────────────────────────────────
                var gameName = $"StdReinstall_{suffix}";
                var gameFolder = Path.Combine(_tempRoot, gameName);
                Directory.CreateDirectory(gameFolder);

                // Place existing dxgi.dll (ReShade) — this is the "already installed" state
                var dxgiPath = Path.Combine(gameFolder, "dxgi.dll");
                File.WriteAllBytes(dxgiPath, CreateFakeReShadeBytes());

                // Seed a record with InstalledAs = "dxgi.dll" (matches destName)
                var existingRecord = new AuxInstalledRecord
                {
                    GameName    = gameName,
                    InstallPath = gameFolder,
                    AddonType   = AuxInstallService.TypeReShade,
                    InstalledAs = "dxgi.dll",
                    InstalledAt = DateTime.UtcNow.AddDays(-1),
                };
                _service.SaveAuxRecord(existingRecord);
                _seededRecords.Add(existingRecord);

                EnsureStagedReShadeExists();

                // ── Act ──────────────────────────────────────────────────────
                var resultRecord = _service.InstallReShadeAsync(
                    gameName, gameFolder,
                    dcMode: false,
                    dcIsInstalled: false).GetAwaiter().GetResult();

                _seededRecords.Add(resultRecord);

                // ── Assert ───────────────────────────────────────────────────
                var dxgiExists = File.Exists(dxgiPath);
                var recordCorrect = resultRecord.InstalledAs == "dxgi.dll";

                // Count DLL files in the folder (exclude non-DLL files)
                var dllFiles = Directory.GetFiles(gameFolder, "*.dll");
                var onlyOneDll = dllFiles.Length == 1;

                return (dxgiExists && recordCorrect && onlyOneDll)
                    .Label($"Standard reinstall: dxgiExists={dxgiExists}, " +
                           $"record.InstalledAs={resultRecord.InstalledAs}, " +
                           $"dllCount={dllFiles.Length}");
            });
    }

    // ── Property 2c: Hardcoded cleanup of ReShade64/32.dll when DC Mode OFF ──────

    /// <summary>
    /// Property 2c: When InstalledAs is "ReShade64.dll" or "ReShade32.dll" and
    /// dcMode=false, the hardcoded cleanup block deletes those files before
    /// installing dxgi.dll. This is existing correct behavior.
    ///
    /// Observed on UNFIXED code: The hardcoded cleanup (lines 693-699) deletes
    /// ReShade64.dll and ReShade32.dll when dcMode=false, then dxgi.dll is installed.
    ///
    /// **Validates: Requirements 3.1, 3.5**
    /// </summary>
    [Property(MaxTest = 2)]
    public Property HardcodedCleanup_ReShade64And32_DeletedWhenDcModeOff()
    {
        var genHardcodedName = Gen.Elements("ReShade64.dll", "ReShade32.dll");

        return Prop.ForAll(
            Arb.From(genHardcodedName),
            (string hardcodedName) =>
            {
                // ── Arrange ──────────────────────────────────────────────────
                var gameName = $"HardcodedCleanup_{hardcodedName.Replace(".", "_")}_{Guid.NewGuid().ToString("N")[..6]}";
                var gameFolder = Path.Combine(_tempRoot, gameName);
                Directory.CreateDirectory(gameFolder);

                // Place the hardcoded-name ReShade DLL on disk
                var hardcodedPath = Path.Combine(gameFolder, hardcodedName);
                File.WriteAllBytes(hardcodedPath, CreateFakeReShadeBytes());

                // Seed a record with InstalledAs = the hardcoded name
                var existingRecord = new AuxInstalledRecord
                {
                    GameName    = gameName,
                    InstallPath = gameFolder,
                    AddonType   = AuxInstallService.TypeReShade,
                    InstalledAs = hardcodedName,
                    InstalledAt = DateTime.UtcNow.AddDays(-1),
                };
                _service.SaveAuxRecord(existingRecord);
                _seededRecords.Add(existingRecord);

                EnsureStagedReShadeExists();

                // ── Act ──────────────────────────────────────────────────────
                var resultRecord = _service.InstallReShadeAsync(
                    gameName, gameFolder,
                    dcMode: false,
                    dcIsInstalled: false).GetAwaiter().GetResult();

                _seededRecords.Add(resultRecord);

                // ── Assert ───────────────────────────────────────────────────
                var hardcodedStillExists = File.Exists(hardcodedPath);
                var dxgiPath = Path.Combine(gameFolder, "dxgi.dll");
                var dxgiExists = File.Exists(dxgiPath);
                var recordCorrect = resultRecord.InstalledAs == "dxgi.dll";

                // Hardcoded name should be deleted, dxgi.dll should exist
                return (!hardcodedStillExists && dxgiExists && recordCorrect)
                    .Label($"Hardcoded cleanup '{hardcodedName}': " +
                           $"hardcodedStillExists={hardcodedStillExists} (expected false), " +
                           $"dxgiExists={dxgiExists}, " +
                           $"record.InstalledAs={resultRecord.InstalledAs}");
            });
    }

    // ── Property 2d: Foreign DLL backup preserved ──────────────────────────────

    /// <summary>
    /// Property 2d: When a foreign (non-ReShade) DLL exists at the destination path,
    /// BackupForeignDll creates a .original backup before overwriting.
    ///
    /// Observed on UNFIXED code: BackupForeignDll is called for destPath before
    /// File.Copy. A foreign dxgi.dll is renamed to dxgi.dll.original, then the
    /// staged ReShade DLL is copied as dxgi.dll.
    ///
    /// **Validates: Requirements 3.4**
    /// </summary>
    [Property(MaxTest = 3)]
    public Property ForeignDllBackup_CreatesOriginalBeforeOverwriting()
    {
        var genSuffix = Gen.Choose(1000, 99999);

        return Prop.ForAll(
            Arb.From(genSuffix),
            (int suffix) =>
            {
                // ── Arrange ──────────────────────────────────────────────────
                var gameName = $"ForeignBackup_{suffix}";
                var gameFolder = Path.Combine(_tempRoot, gameName);
                Directory.CreateDirectory(gameFolder);

                // Place a foreign (non-ReShade) dxgi.dll at the destination
                var dxgiPath = Path.Combine(gameFolder, "dxgi.dll");
                File.WriteAllBytes(dxgiPath, CreateForeignDllBytes());

                // No existing ReShade record — this is a fresh install over a foreign DLL
                EnsureStagedReShadeExists();

                // ── Act ──────────────────────────────────────────────────────
                var resultRecord = _service.InstallReShadeAsync(
                    gameName, gameFolder,
                    dcMode: false,
                    dcIsInstalled: false).GetAwaiter().GetResult();

                _seededRecords.Add(resultRecord);

                // ── Assert ───────────────────────────────────────────────────
                var backupPath = dxgiPath + ".original";
                var backupExists = File.Exists(backupPath);
                var dxgiExists = File.Exists(dxgiPath);
                var recordCorrect = resultRecord.InstalledAs == "dxgi.dll";

                // The foreign DLL should be backed up as .original
                // The new ReShade dxgi.dll should exist
                return (backupExists && dxgiExists && recordCorrect)
                    .Label($"Foreign DLL backup: backupExists={backupExists}, " +
                           $"dxgiExists={dxgiExists}, " +
                           $"record.InstalledAs={resultRecord.InstalledAs}");
            });
    }
}
