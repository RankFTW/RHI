using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for architecture validation in DXVK deployment.
/// Feature: dxvk-integration, Property 3: Architecture validation rejects mismatches
/// </summary>
public class DxvkArchitectureValidationPropertyTests
{
    // ── Generators ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates only the four DXVK-supported GraphicsApiType values.
    /// </summary>
    private static readonly Gen<GraphicsApiType> GenSupportedApi =
        Gen.Elements(
            GraphicsApiType.DirectX8,
            GraphicsApiType.DirectX9,
            GraphicsApiType.DirectX10,
            GraphicsApiType.DirectX11);

    // ── Property 3: Architecture validation rejects mismatches ────────────────────
    // Feature: dxvk-integration, Property 3: Architecture validation rejects mismatches
    // **Validates: Requirements 3.11**

    /// <summary>
    /// For any (GraphicsApiType, gameBitness, dllBitness) triple where
    /// gameBitness != dllBitness, the architecture folder selected by
    /// DetermineRequiredDlls for the game's bitness SHALL NOT match the
    /// mismatched DLL bitness folder. This ensures deployment is refused
    /// when bitness mismatches — the system selects the correct arch folder
    /// for the game, so a DLL from the wrong arch folder can never be deployed.
    ///
    /// Conversely, when gameBitness == dllBitness, the arch folder SHALL match,
    /// ensuring deployment succeeds only when architectures align.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property DetermineRequiredDlls_ArchFolder_Rejects_Bitness_Mismatch()
    {
        return Prop.ForAll(
            Arb.From(GenSupportedApi),
            Arb.From<bool>(),
            Arb.From<bool>(),
            (GraphicsApiType api, bool gameBitness, bool dllBitness) =>
            {
                // The game's bitness determines which arch folder is selected
                var (archFolder, _) = DxvkService.DetermineRequiredDlls(api, gameBitness);

                // The DLL's bitness determines which arch folder it would come from
                var dllArchFolder = dllBitness ? "x32" : "x64";

                if (gameBitness != dllBitness)
                {
                    // Mismatch: the selected arch folder must NOT match the DLL's arch folder.
                    // This means a DLL from the wrong architecture can never be selected.
                    return (archFolder != dllArchFolder)
                        .Label($"MISMATCH REJECTED: api={api}, gameBitness={gameBitness}, dllBitness={dllBitness}: " +
                               $"archFolder={archFolder} should differ from dllArchFolder={dllArchFolder}");
                }
                else
                {
                    // Match: the selected arch folder MUST match the DLL's arch folder.
                    // Deployment succeeds only when architectures align.
                    return (archFolder == dllArchFolder)
                        .Label($"MATCH ACCEPTED: api={api}, gameBitness={gameBitness}, dllBitness={dllBitness}: " +
                               $"archFolder={archFolder} should equal dllArchFolder={dllArchFolder}");
                }
            });
    }
}
