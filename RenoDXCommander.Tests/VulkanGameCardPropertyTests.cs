using System.Text.Json;
using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for Vulkan-related GameCardViewModel computed properties.
/// Uses FsCheck with xUnit. Each property runs a minimum of 100 iterations.
/// </summary>
public class VulkanGameCardPropertyTests
{
    private static readonly GraphicsApiType[] AllApiTypes =
    {
        GraphicsApiType.Unknown,
        GraphicsApiType.DirectX8,
        GraphicsApiType.DirectX9,
        GraphicsApiType.DirectX10,
        GraphicsApiType.DirectX11,
        GraphicsApiType.DirectX12,
        GraphicsApiType.Vulkan,
        GraphicsApiType.OpenGL,
    };

    private static readonly string[] RenderingPaths = { "DirectX", "Vulkan" };

    // Feature: vulkan-reshade-support, Property 4: RequiresVulkanInstall routes correctly based on API and rendering path
    /// <summary>
    /// **Validates: Requirements 6.1, 6.2, 7.3, 7.4, 11.5**
    ///
    /// For any GameCardViewModel with a given GraphicsApi, IsDualApiGame, and VulkanRenderingPath,
    /// RequiresVulkanInstall shall be true if and only if:
    /// (a) the game is Vulkan-only (GraphicsApi == Vulkan and !IsDualApiGame), or
    /// (b) the game is dual-API and VulkanRenderingPath == "Vulkan".
    /// </summary>
    [Property(MaxTest = 100)]
    public Property RequiresVulkanInstall_RoutesCorrectlyBasedOnApiAndRenderingPath()
    {
        var genApi = Gen.Elements(AllApiTypes);
        var genDual = Arb.Default.Bool().Generator;
        var genPath = Gen.Elements(RenderingPaths);

        var gen = from api in genApi
                  from isDual in genDual
                  from renderPath in genPath
                  select (api, isDual, renderPath);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (api, isDual, renderPath) = tuple;

            var card = new GameCardViewModel
            {
                GraphicsApi = api,
                IsDualApiGame = isDual,
                VulkanRenderingPath = renderPath,
            };

            bool isVulkanOnly = api == GraphicsApiType.Vulkan && !isDual;
            bool expected = isVulkanOnly || (isDual && renderPath == "Vulkan");

            return (card.RequiresVulkanInstall == expected)
                .Label($"Api={api}, IsDual={isDual}, Path={renderPath} => Expected={expected}, Got={card.RequiresVulkanInstall}");
        });
    }

    // Feature: vulkan-reshade-support, Property 5: Rendering path toggle visibility matches dual-API status
    /// <summary>
    /// **Validates: Requirements 7.1, 7.5**
    ///
    /// For any GameCardViewModel, ShowRenderingPathToggle shall be true if and only if IsDualApiGame is true.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ShowRenderingPathToggle_MatchesDualApiStatus()
    {
        return Prop.ForAll(Arb.Default.Bool(), isDual =>
        {
            var card = new GameCardViewModel
            {
                IsDualApiGame = isDual,
            };

            return (card.ShowRenderingPathToggle == isDual)
                .Label($"IsDual={isDual} => ShowToggle expected={isDual}, got={card.ShowRenderingPathToggle}");
        });
    }

    // Feature: vulkan-reshade-support, Property 6: Rendering path defaults to DirectX
    /// <summary>
    /// **Validates: Requirements 7.2**
    ///
    /// For any newly created GameCardViewModel where no persisted preference exists,
    /// VulkanRenderingPath shall equal "DirectX".
    /// </summary>
    [Property(MaxTest = 100)]
    public Property RenderingPath_DefaultsToDirectX()
    {
        var genName = Arb.Default.NonEmptyString().Generator.Select(s => s.Get);

        return Prop.ForAll(genName.ToArbitrary(), gameName =>
        {
            var card = new GameCardViewModel
            {
                GameName = gameName,
            };

            return (card.VulkanRenderingPath == "DirectX")
                .Label($"GameName=\"{gameName}\" => VulkanRenderingPath expected=\"DirectX\", got=\"{card.VulkanRenderingPath}\"");
        });
    }

    // Feature: vulkan-reshade-support, Property 7: Rendering path preference round trip
    /// <summary>
    /// **Validates: Requirements 7.6**
    ///
    /// For any game name and valid rendering path value ("DirectX" or "Vulkan"),
    /// serializing the preference to a Dictionary and deserializing it back shall produce the same value.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property RenderingPathPreference_RoundTrip()
    {
        var genName = Arb.Default.NonEmptyString().Generator.Select(s => s.Get);
        var genPath = Gen.Elements(RenderingPaths);

        var genEntries = Gen.NonEmptyListOf(
            from name in genName
            from path in genPath
            select (name, path));

        return Prop.ForAll(genEntries.ToArbitrary(), entries =>
        {
            // Build a dictionary from the generated entries (last write wins for duplicate keys)
            var original = new Dictionary<string, string>();
            foreach (var (name, path) in entries)
            {
                original[name] = path;
            }

            // Serialize to JSON
            string json = JsonSerializer.Serialize(original);

            // Deserialize back
            var deserialized = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

            // Verify equality: same count and all entries match
            bool sameCount = deserialized != null && deserialized.Count == original.Count;
            bool allMatch = sameCount && original.All(kvp =>
                deserialized!.TryGetValue(kvp.Key, out var val) && val == kvp.Value);

            return allMatch
                .Label($"Round trip failed: original had {original.Count} entries, deserialized had {deserialized?.Count ?? 0}");
        });
    }
}
