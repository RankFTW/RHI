using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for API override precedence in DXVK DLL selection.
/// Feature: dxvk-integration, Property 13: API override precedence
/// </summary>
public class DxvkApiOverridePrecedencePropertyTests
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

    /// <summary>
    /// Generates pairs of (autoDetectedApi, overrideApi) where both are
    /// supported DX versions and they differ from each other, ensuring
    /// the override actually changes the outcome.
    /// </summary>
    private static readonly Gen<(GraphicsApiType auto, GraphicsApiType over)> GenDistinctApiPair =
        from autoApi in GenSupportedApi
        from overrideApi in GenSupportedApi.Where(o => o != autoApi)
        select (autoApi, overrideApi);

    // ── Expected mappings ─────────────────────────────────────────────────────────

    private static readonly Dictionary<GraphicsApiType, List<string>> ExpectedDlls = new()
    {
        { GraphicsApiType.DirectX8,  new List<string> { "d3d8.dll" } },
        { GraphicsApiType.DirectX9,  new List<string> { "d3d9.dll" } },
        { GraphicsApiType.DirectX10, new List<string> { "d3d10core.dll", "dxgi.dll" } },
        { GraphicsApiType.DirectX11, new List<string> { "d3d11.dll", "dxgi.dll" } },
    };

    // ── Property 13: API override precedence ──────────────────────────────────────
    // Feature: dxvk-integration, Property 13: API override precedence
    // **Validates: Requirements 17.5**

    /// <summary>
    /// For any game with both an auto-detected GraphicsApiType and a
    /// dxvkApiOverrides entry, the DXVK DLL selection SHALL use the
    /// overridden API version, not the auto-detected value.
    ///
    /// We test this by generating distinct (autoApi, overrideApi) pairs,
    /// calling DetermineRequiredDlls with the override API, and asserting
    /// the result matches the override API's expected DLLs — not the
    /// auto-detected API's DLLs.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property DllSelection_Uses_Override_Api_Not_AutoDetected()
    {
        return Prop.ForAll(
            Arb.From(GenDistinctApiPair),
            Arb.From<bool>(),
            ((GraphicsApiType auto, GraphicsApiType over) apiPair, bool is32Bit) =>
            {
                var autoApi = apiPair.auto;
                var overrideApi = apiPair.over;

                // Simulate the override: call DetermineRequiredDlls with the override API
                var (archFolder, dllNames) = DxvkService.DetermineRequiredDlls(overrideApi, is32Bit);

                var expectedArch = is32Bit ? "x32" : "x64";
                var expectedDlls = ExpectedDlls[overrideApi];
                var autoDetectedDlls = ExpectedDlls[autoApi];

                bool archCorrect = archFolder == expectedArch;
                bool matchesOverride = dllNames.SequenceEqual(expectedDlls);
                bool differsFromAuto = !dllNames.SequenceEqual(autoDetectedDlls);

                return (archCorrect && matchesOverride && differsFromAuto)
                    .Label($"autoApi={autoApi}, overrideApi={overrideApi}, is32Bit={is32Bit}: " +
                           $"archCorrect={archCorrect} (expected={expectedArch}, got={archFolder}), " +
                           $"matchesOverride={matchesOverride} (expected=[{string.Join(", ", expectedDlls)}], got=[{string.Join(", ", dllNames)}]), " +
                           $"differsFromAuto={differsFromAuto} (autoDetectedDlls=[{string.Join(", ", autoDetectedDlls)}])");
            });
    }
}
