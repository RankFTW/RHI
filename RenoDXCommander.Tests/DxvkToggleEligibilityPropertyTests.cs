using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for DXVK toggle eligibility logic on GameCardViewModel.
/// Feature: dxvk-integration, Property 5: DXVK toggle eligibility
/// </summary>
public class DxvkToggleEligibilityPropertyTests
{
    // ── Generators ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generates any GraphicsApiType enum value.
    /// </summary>
    private static readonly Gen<GraphicsApiType> GenAnyApi =
        Gen.Elements(
            GraphicsApiType.Unknown,
            GraphicsApiType.DirectX8,
            GraphicsApiType.DirectX9,
            GraphicsApiType.DirectX10,
            GraphicsApiType.DirectX11,
            GraphicsApiType.DirectX12,
            GraphicsApiType.Vulkan,
            GraphicsApiType.OpenGL);

    /// <summary>
    /// The set of DXVK-supported DirectX APIs.
    /// </summary>
    private static readonly HashSet<GraphicsApiType> SupportedApis = new()
    {
        GraphicsApiType.DirectX8,
        GraphicsApiType.DirectX9,
        GraphicsApiType.DirectX10,
        GraphicsApiType.DirectX11
    };

    // ── Property 5: DXVK toggle eligibility ───────────────────────────────────────
    // Feature: dxvk-integration, Property 5: DXVK toggle eligibility
    // **Validates: Requirements 9.1, 9.2, 9.7, 10.5, 17.4**

    /// <summary>
    /// For any (GraphicsApiType, isBlacklisted, hasApiOverride) combination:
    /// - IsDxvkToggleVisible is true iff API ∈ {DX8, DX9, DX10, DX11}
    /// - IsDxvkToggleEnabled is true iff visible AND not blacklisted AND
    ///   (API ≠ Unknown OR hasApiOverride)
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ToggleEligibility_MatchesSpecification()
    {
        return Prop.ForAll(
            Arb.From(GenAnyApi),
            Arb.From<bool>(),
            Arb.From<bool>(),
            (GraphicsApiType api, bool isBlacklisted, bool hasOverride) =>
            {
                var card = new GameCardViewModel
                {
                    GraphicsApi = api,
                    IsDxvkBlacklisted = isBlacklisted,
                    HasDxvkApiOverride = hasOverride
                };

                bool expectedVisible = SupportedApis.Contains(api);
                bool expectedEnabled = expectedVisible
                    && !isBlacklisted
                    && (api != GraphicsApiType.Unknown || hasOverride);

                bool visibleMatch = card.IsDxvkToggleVisible == expectedVisible;
                bool enabledMatch = card.IsDxvkToggleEnabled == expectedEnabled;

                return (visibleMatch && enabledMatch)
                    .Label($"api={api}, blacklisted={isBlacklisted}, override={hasOverride}: " +
                           $"visibleMatch={visibleMatch} (expected={expectedVisible}, got={card.IsDxvkToggleVisible}), " +
                           $"enabledMatch={enabledMatch} (expected={expectedEnabled}, got={card.IsDxvkToggleEnabled})");
            });
    }
}
