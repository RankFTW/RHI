using System.Text.Json;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Win32;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests for VulkanLayerService.
/// Uses FsCheck with xUnit. Each property runs a minimum of 100 iterations.
/// </summary>
public class VulkanLayerServicePropertyTests
{
    // Feature: vulkan-reshade-support, Property 1: Manifest generation produces valid layer JSON
    // **Validates: Requirements 1.2, 1.3**
    [Property(MaxTest = 100)]
    public Property ManifestGeneration_ProducesValidLayerJson()
    {
        // GenerateManifestJson() is deterministic, so we use a trivial generator
        // to drive 100+ iterations confirming the invariant holds.
        var gen = Gen.Constant(0);
        return Prop.ForAll(gen.ToArbitrary(), _ =>
        {
            var json = VulkanLayerService.GenerateManifestJson();

            // Must parse as valid JSON
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(json);
            }
            catch (JsonException ex)
            {
                return false.Label($"Failed to parse JSON: {ex.Message}");
            }

            var root = doc.RootElement;

            // file_format_version == "1.0.0"
            if (!root.TryGetProperty("file_format_version", out var ffv) || ffv.GetString() != "1.0.0")
                return false.Label($"file_format_version missing or != '1.0.0', got '{TryGetString(root, "file_format_version")}'");

            // layer object must exist
            if (!root.TryGetProperty("layer", out var layer) || layer.ValueKind != JsonValueKind.Object)
                return false.Label("'layer' property missing or not an object");

            // layer.name == "VK_LAYER_reshade"
            if (!layer.TryGetProperty("name", out var name) || name.GetString() != "VK_LAYER_reshade")
                return false.Label($"layer.name missing or != 'VK_LAYER_reshade', got '{TryGetString(layer, "name")}'");

            // layer.type == "GLOBAL"
            if (!layer.TryGetProperty("type", out var type) || type.GetString() != "GLOBAL")
                return false.Label($"layer.type missing or != 'GLOBAL', got '{TryGetString(layer, "type")}'");

            // library_path ends with ReShade64.dll and is non-empty
            if (!layer.TryGetProperty("library_path", out var libPath))
                return false.Label("layer.library_path missing");
            var libPathStr = libPath.GetString() ?? "";
            if (string.IsNullOrEmpty(libPathStr))
                return false.Label("layer.library_path is empty");
            if (!libPathStr.EndsWith("ReShade64.dll"))
                return false.Label($"layer.library_path does not end with 'ReShade64.dll', got '{libPathStr}'");

            // api_version non-empty
            if (!layer.TryGetProperty("api_version", out var apiVer) || string.IsNullOrEmpty(apiVer.GetString()))
                return false.Label("layer.api_version missing or empty");

            // implementation_version non-empty
            if (!layer.TryGetProperty("implementation_version", out var implVer) || string.IsNullOrEmpty(implVer.GetString()))
                return false.Label("layer.implementation_version missing or empty");

            // description non-empty
            if (!layer.TryGetProperty("description", out var desc) || string.IsNullOrEmpty(desc.GetString()))
                return false.Label("layer.description missing or empty");

            doc.Dispose();
            return true.Label("All manifest fields valid");
        });
    }

    // Feature: vulkan-reshade-support, Property 3: Layer status detection is a conjunction of three conditions
    // **Validates: Requirements 5.1, 5.2**
    [Property(MaxTest = 100)]
    public Property LayerStatusDetection_IsConjunctionOfThreeConditions()
    {
        // Generate all 8 combinations of (registryEntry, manifestFile, dllFile) booleans.
        // FsCheck will generate random bool triples; over 100 iterations all 8 combos are covered.
        return Prop.ForAll(
            Arb.From<bool>(),
            Arb.From<bool>(),
            Arb.From<bool>(),
            (bool hasRegistry, bool hasManifest, bool hasDll) =>
            {
                var testRegSubKeyPath = @"SOFTWARE\RenoDXCommander_Test_VulkanLayer_" + Guid.NewGuid().ToString("N");
                var tempDir = Path.Combine(Path.GetTempPath(), "RenoDXTest_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);

                var manifestPath = Path.Combine(tempDir, VulkanLayerService.LayerManifestName);
                var dllPath = Path.Combine(tempDir, VulkanLayerService.LayerDllName);

                try
                {
                    // Set up registry condition
                    if (hasRegistry)
                    {
                        using var key = Registry.CurrentUser.CreateSubKey(testRegSubKeyPath, writable: true);
                        key.SetValue(manifestPath, 0, RegistryValueKind.DWord);
                    }

                    // Set up manifest file condition
                    if (hasManifest)
                    {
                        File.WriteAllText(manifestPath, "{}");
                    }

                    // Set up DLL file condition
                    if (hasDll)
                    {
                        File.WriteAllText(dllPath, "fake-dll");
                    }

                    // Call the testable overload
                    var result = VulkanLayerService.IsLayerInstalled(
                        Registry.CurrentUser, testRegSubKeyPath, manifestPath, dllPath);

                    var expected = hasRegistry && hasManifest && hasDll;

                    return (result == expected).Label(
                        $"registry={hasRegistry}, manifest={hasManifest}, dll={hasDll} => expected={expected}, got={result}");
                }
                finally
                {
                    // Clean up registry
                    try { Registry.CurrentUser.DeleteSubKeyTree(testRegSubKeyPath, throwOnMissingSubKey: false); }
                    catch { /* best effort */ }

                    // Clean up temp directory
                    try { Directory.Delete(tempDir, recursive: true); }
                    catch { /* best effort */ }
                }
            });
    }

    // Feature: vulkan-reshade-support, Property 2: Uninstall is idempotent
    // **Validates: Requirements 4.3**
    [Property(MaxTest = 100)]
    public Property UninstallLayer_IsIdempotent()
    {
        // Generate random initial states: (registry present, manifest present, dll present)
        return Prop.ForAll(
            Arb.From<bool>(),
            Arb.From<bool>(),
            Arb.From<bool>(),
            (bool hasRegistry, bool hasManifest, bool hasDll) =>
            {
                var testRegSubKeyPath = @"SOFTWARE\RenoDXCommander_Test_Uninstall_" + Guid.NewGuid().ToString("N");
                var tempDir = Path.Combine(Path.GetTempPath(), "RenoDXTest_Uninstall_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);

                var manifestPath = Path.Combine(tempDir, VulkanLayerService.LayerManifestName);
                var dllPath = Path.Combine(tempDir, VulkanLayerService.LayerDllName);

                try
                {
                    // Set up initial state
                    if (hasRegistry)
                    {
                        using var key = Registry.CurrentUser.CreateSubKey(testRegSubKeyPath, writable: true);
                        key.SetValue(manifestPath, 0, RegistryValueKind.DWord);
                    }

                    if (hasManifest)
                    {
                        File.WriteAllText(manifestPath, "{}");
                    }

                    if (hasDll)
                    {
                        File.WriteAllText(dllPath, "fake-dll");
                    }

                    // Call UninstallLayer — should not throw
                    Exception? caught = null;
                    try
                    {
                        VulkanLayerService.UninstallLayer(
                            Registry.CurrentUser, testRegSubKeyPath, manifestPath, dllPath);
                    }
                    catch (Exception ex)
                    {
                        caught = ex;
                    }

                    if (caught != null)
                        return false.Label($"UninstallLayer threw {caught.GetType().Name}: {caught.Message}");

                    // After uninstall, IsLayerInstalled must return false
                    var installed = VulkanLayerService.IsLayerInstalled(
                        Registry.CurrentUser, testRegSubKeyPath, manifestPath, dllPath);

                    return (!installed).Label(
                        $"registry={hasRegistry}, manifest={hasManifest}, dll={hasDll} => IsLayerInstalled returned true after UninstallLayer");
                }
                finally
                {
                    // Clean up registry
                    try { Registry.CurrentUser.DeleteSubKeyTree(testRegSubKeyPath, throwOnMissingSubKey: false); }
                    catch { /* best effort */ }

                    // Clean up temp directory
                    try { Directory.Delete(tempDir, recursive: true); }
                    catch { /* best effort */ }
                }
            });
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) ? prop.GetString() : "<missing>";
    }
}
