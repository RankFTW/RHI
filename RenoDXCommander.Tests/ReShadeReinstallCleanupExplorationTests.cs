using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.Tests;

/// <summary>
/// Bug condition exploration tests for ReShade reinstall cleanup.
/// These tests exercise InstallReShadeAsync to surface counterexamples proving the bug:
/// when ReShade is detected under a non-standard filename (e.g. d3d11.dll) and the user
/// clicks "Reinstall ReShade", the old non-standard DLL is NOT removed, leaving two
/// ReShade DLLs in the game folder.
///
/// **Validates: Requirements 1.1, 1.2, 2.1, 2.2**
///
/// EXPECTED OUTCOME on UNFIXED code: Tests FAIL (confirms the bug exists).
/// After the fix is applied, these same tests should PASS.
/// </summary>
public class ReShadeReinstallCleanupExplorationTests : IDisposable
{
    /// <summary>
    /// Non-standard filenames that ReShade can be installed as via content-based detection.
    /// These are NOT in the hardcoded cleanup set {ReShade64.dll, ReShade32.dll}.
    /// When dcMode=false, destName="dxgi.dll", so these all differ from the destination.
    /// </summary>
    private static readonly string[] NonStandardDllNames = { "d3d11.dll", "dinput8.dll", "version.dll" };

    private readonly string _tempRoot;
    private readonly AuxInstallService _service;
    private readonly List<AuxInstalledRecord> _seededRecords = new();

    public ReShadeReinstallCleanupExplorationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "RdxcReinstall_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempRoot);
        _service = new AuxInstallService(new HttpClient(), new TestHelpers.StubShaderPackService());
    }

    public void Dispose()
    {
        // Clean up seeded records from the real DB
        foreach (var record in _seededRecords)
        {
            try { _service.RemoveRecord(record); } catch { }
        }

        // Clean up temp folders
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    /// <summary>
    /// Property 1: Bug Condition — Old Non-Standard DLL Not Removed on Reinstall
    ///
    /// For any non-standard DLL filename (d3d11.dll, dinput8.dll, version.dll) recorded
    /// in AuxInstalledRecord.InstalledAs, when InstallReShadeAsync is called with
    /// dcMode=false (destName="dxgi.dll"), the old non-standard DLL SHALL be deleted
    /// and the new dxgi.dll SHALL exist after the call.
    ///
    /// On UNFIXED code this MUST FAIL — InstallReShadeAsync only deletes hardcoded
    /// ReShade64.dll/ReShade32.dll, never consulting the existing record's InstalledAs.
    ///
    /// **Validates: Requirements 1.1, 1.2, 2.1, 2.2**
    /// </summary>
    [Property(MaxTest = 3)]
    public Property BugCondition_OldNonStandardDll_ShouldBeRemovedOnReinstall()
    {
        var genDllName = Gen.Elements(NonStandardDllNames);

        return Prop.ForAll(
            Arb.From(genDllName),
            (string oldDllName) =>
            {
                // ── Arrange ──────────────────────────────────────────────────

                // Create a unique game folder with the old non-standard ReShade DLL
                var gameName = "ReinstallTest_" + oldDllName.Replace(".", "_") + "_" + Guid.NewGuid().ToString("N")[..6];
                var gameFolder = Path.Combine(_tempRoot, gameName);
                Directory.CreateDirectory(gameFolder);

                var oldDllPath = Path.Combine(gameFolder, oldDllName);
                File.WriteAllBytes(oldDllPath, CreateFakeReShadeBytes());

                // Seed an AuxInstalledRecord with InstalledAs = the non-standard filename
                var existingRecord = new AuxInstalledRecord
                {
                    GameName    = gameName,
                    InstallPath = gameFolder,
                    AddonType   = AuxInstallService.TypeReShade,
                    InstalledAs = oldDllName,
                    InstalledAt = DateTime.UtcNow.AddDays(-1),
                };
                _service.SaveAuxRecord(existingRecord);
                _seededRecords.Add(existingRecord);

                // Ensure a staged ReShade DLL exists so InstallReShadeAsync can copy it
                EnsureStagedReShadeExists();

                // ── Act ──────────────────────────────────────────────────────

                // Call InstallReShadeAsync → destName = "dxgi.dll"
                var resultRecord = _service.InstallReShadeAsync(
                    gameName,
                    gameFolder).GetAwaiter().GetResult();

                // Track the new record for cleanup
                _seededRecords.Add(resultRecord);

                // ── Assert ───────────────────────────────────────────────────

                var newDllPath = Path.Combine(gameFolder, "dxgi.dll");
                var oldDllStillExists = File.Exists(oldDllPath);
                var newDllExists = File.Exists(newDllPath);

                // The old non-standard DLL should NOT exist after reinstall
                // The new dxgi.dll SHOULD exist after reinstall
                // On UNFIXED code: oldDllStillExists=true (BUG!) → test FAILS
                return (!oldDllStillExists && newDllExists)
                    .Label($"After reinstall with oldDll='{oldDllName}': " +
                           $"oldDllStillExists={oldDllStillExists} (expected false), " +
                           $"newDllExists={newDllExists} (expected true)");
            });
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
    /// Ensures the ReShade staging directory has a 64-bit DLL so InstallReShadeAsync
    /// can copy it to the game folder. Creates a fake DLL if one doesn't already exist.
    /// </summary>
    private static void EnsureStagedReShadeExists()
    {
        Directory.CreateDirectory(AuxInstallService.RsStagingDir);
        if (!File.Exists(AuxInstallService.RsStagedPath64))
        {
            File.WriteAllBytes(AuxInstallService.RsStagedPath64, CreateFakeReShadeBytes());
        }
    }
}
