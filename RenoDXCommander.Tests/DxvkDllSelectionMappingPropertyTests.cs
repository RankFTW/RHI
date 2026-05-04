using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for DxvkService.DetermineRequiredDlls.
/// Feature: dxvk-integration, Property 2: DLL selection mapping
/// </summary>
public class DxvkDllSelectionMappingPropertyTests
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

    // ── Expected mappings ─────────────────────────────────────────────────────────

    private static readonly Dictionary<GraphicsApiType, List<string>> ExpectedDlls = new()
    {
        { GraphicsApiType.DirectX8,  new List<string> { "d3d8.dll" } },
        { GraphicsApiType.DirectX9,  new List<string> { "d3d9.dll" } },
        { GraphicsApiType.DirectX10, new List<string> { "d3d10core.dll", "dxgi.dll" } },
        { GraphicsApiType.DirectX11, new List<string> { "d3d11.dll", "dxgi.dll" } },
    };

    // ── Property 2: DLL selection mapping ─────────────────────────────────────────
    // Feature: dxvk-integration, Property 2: DLL selection mapping
    // **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5**

    /// <summary>
    /// For any valid (GraphicsApiType ∈ {DX8, DX9, DX10, DX11}, bool is32Bit) pair,
    /// DetermineRequiredDlls returns the correct architecture folder ("x32" for 32-bit,
    /// "x64" for 64-bit) and the correct set of DLL names.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property DetermineRequiredDlls_Returns_Correct_ArchFolder_And_DllNames()
    {
        return Prop.ForAll(
            Arb.From(GenSupportedApi),
            Arb.From<bool>(),
            (GraphicsApiType api, bool is32Bit) =>
            {
                var (archFolder, dllNames) = DxvkService.DetermineRequiredDlls(api, is32Bit);

                var expectedArch = is32Bit ? "x32" : "x64";
                var expectedDlls = ExpectedDlls[api];

                bool archMatch = archFolder == expectedArch;
                bool dllsMatch = dllNames.SequenceEqual(expectedDlls);

                return (archMatch && dllsMatch)
                    .Label($"api={api}, is32Bit={is32Bit}: " +
                           $"archMatch={archMatch} (expected={expectedArch}, got={archFolder}), " +
                           $"dllsMatch={dllsMatch} (expected=[{string.Join(", ", expectedDlls)}], got=[{string.Join(", ", dllNames)}])");
            });
    }
}
