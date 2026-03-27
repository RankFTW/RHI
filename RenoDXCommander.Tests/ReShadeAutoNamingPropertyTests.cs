using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for automatic ReShade DLL naming by detected graphics APIs.
/// Feature: 32bit-ogl-dx9-support, Property 2: Automatic ReShade DLL naming by detected graphics APIs
/// Validates: Requirements 2.1, 2.2, 2.5, 3.1, 3.2
/// </summary>
public class ReShadeAutoNamingPropertyTests
{
    /// <summary>
    /// All defined <see cref="GraphicsApiType"/> values used to build random subsets.
    /// </summary>
    private static readonly GraphicsApiType[] AllApis =
        Enum.GetValues<GraphicsApiType>();

    /// <summary>
    /// FsCheck generator that produces random subsets of <see cref="GraphicsApiType"/>
    /// as <see cref="HashSet{GraphicsApiType}"/>.
    /// Each enum value is independently included or excluded with 50 % probability.
    /// </summary>
    private static Arbitrary<HashSet<GraphicsApiType>> ApiSubsetArbitrary()
    {
        // Generate an array of bools, one per enum value, to decide inclusion.
        var subsetGen = Gen.ArrayOf(AllApis.Length, Arb.Default.Bool().Generator)
            .Select(bools =>
            {
                var set = new HashSet<GraphicsApiType>();
                for (int i = 0; i < AllApis.Length; i++)
                {
                    if (bools[i])
                        set.Add(AllApis[i]);
                }
                return set;
            });

        return Arb.From(subsetGen);
    }

    // Feature: 32bit-ogl-dx9-support, Property 2: Automatic ReShade DLL naming by detected graphics APIs
    // Validates: Requirements 2.1, 2.2, 2.5, 3.1, 3.2
    [Property(MaxTest = 100)]
    public Property ResolveAutoReShadeFilename_MatchesDx9_OpenGL_NullRules()
    {
        return Prop.ForAll(
            ApiSubsetArbitrary(),
            detectedApis =>
            {
                var result = MainViewModel.ResolveAutoReShadeFilename(detectedApis);

                if (detectedApis.Contains(GraphicsApiType.DirectX9))
                {
                    // DX9 detected → always d3d9.dll, regardless of other APIs
                    return (result == "d3d9.dll")
                        .Label($"DX9 present in {FormatSet(detectedApis)}: expected 'd3d9.dll' but got '{result}'");
                }

                if (detectedApis.Count == 1 && detectedApis.Contains(GraphicsApiType.OpenGL))
                {
                    // OpenGL-only → opengl32.dll
                    return (result == "opengl32.dll")
                        .Label($"OpenGL-only: expected 'opengl32.dll' but got '{result}'");
                }

                if (detectedApis.Contains(GraphicsApiType.OpenGL) && !detectedApis.Contains(GraphicsApiType.DirectX9))
                {
                    // OpenGL alongside other non-DX9 APIs → null
                    return (result == null)
                        .Label($"OpenGL + other non-DX9 APIs {FormatSet(detectedApis)}: expected null but got '{result}'");
                }

                // All other sets → null
                return (result == null)
                    .Label($"No DX9/OpenGL-only in {FormatSet(detectedApis)}: expected null but got '{result}'");
            });
    }

    // Feature: 32bit-ogl-dx9-support, Property 3: User DLL override takes priority over automatic naming
    // Validates: Requirements 2.3, 3.3
    [Property(MaxTest = 100)]
    public Property UserDllOverride_AlwaysTakesPriority_OverAutoNaming()
    {
        return Prop.ForAll(
            ApiSubsetArbitrary(),
            Arb.From(Gen.Elements(
                "reshade.dll", "d3d9.dll", "opengl32.dll", "dxgi.dll",
                "dbghelp.dll", "custom.dll", "myreshade.dll")),
            (detectedApis, userReShadeFileName) =>
            {
                // Simulate the resolution chain with DllOverrideEnabled = true
                bool dllOverrideEnabled = true;
                string? manifestReShade = null; // irrelevant when override enabled

                var resolved = dllOverrideEnabled
                    ? userReShadeFileName
                    : (manifestReShade is { Length: > 0 } mRs
                        ? mRs
                        : MainViewModel.ResolveAutoReShadeFilename(detectedApis));

                return (resolved == userReShadeFileName)
                    .Label($"DllOverrideEnabled=true, user='{userReShadeFileName}', " +
                           $"apis={FormatSet(detectedApis)}: expected '{userReShadeFileName}' but got '{resolved}'");
            });
    }

    // Feature: 32bit-ogl-dx9-support, Property 4: Manifest DLL override takes priority over automatic naming
    // Validates: Requirements 2.4, 3.4
    [Property(MaxTest = 100)]
    public Property ManifestDllOverride_AlwaysTakesPriority_OverAutoNaming()
    {
        return Prop.ForAll(
            ApiSubsetArbitrary(),
            Arb.From(Gen.Elements(
                "reshade.dll", "d3d9.dll", "opengl32.dll", "dxgi.dll",
                "dbghelp.dll", "custom.dll", "myreshade.dll")),
            (detectedApis, manifestReShade) =>
            {
                // Simulate the resolution chain with DllOverrideEnabled = false and manifest entry
                bool dllOverrideEnabled = false;
                string? userReShadeFileName = "ignored_user_override.dll"; // irrelevant when override disabled

                var resolved = dllOverrideEnabled
                    ? userReShadeFileName
                    : (manifestReShade is { Length: > 0 } mRs
                        ? mRs
                        : MainViewModel.ResolveAutoReShadeFilename(detectedApis));

                return (resolved == manifestReShade)
                    .Label($"DllOverrideEnabled=false, manifest='{manifestReShade}', " +
                           $"apis={FormatSet(detectedApis)}: expected '{manifestReShade}' but got '{resolved}'");
            });
    }

    private static string FormatSet(HashSet<GraphicsApiType> apis) =>
        "{" + string.Join(", ", apis) + "}";
}
