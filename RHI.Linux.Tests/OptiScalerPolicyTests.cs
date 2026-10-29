using RenoDXCommander.Services;
using RHI.Linux.Core;
using Xunit;

namespace RHI.Linux.Tests;

public sealed class OptiScalerPolicyTests
{
    [Theory]
    [InlineData("NVIDIA", true, "Stable", "OptiScaler.nvidia.ini")]
    [InlineData("nvidia", false, "Nightly", "OptiScaler_nightly.nvidia.ini")]
    [InlineData("AMD", true, "DlssNr", "OptiScaler_dlssnr.amd-dlss.ini")]
    [InlineData("Intel", false, "Nightly", "OptiScaler_nightly.amd-nodlss.ini")]
    public void LinuxUsesSharedWindowsTemplatePolicy(string gpu, bool inputs, string variant, string expected)
    {
        Assert.Equal(expected, OptiScalerPolicy.TemplateName(gpu, inputs, variant));
        Assert.Equal(expected, OptiScaler.TemplateName(gpu, inputs, variant));
    }

    [Theory]
    [InlineData("insert", "0x2D")]
    [InlineData("F12", "0x7B")]
    [InlineData("0x99", "0x99")]
    public void HotkeysPreserveCaseInsensitiveNamesAndCustomCodes(string name, string expected)
    {
        Assert.Equal(expected, OptiScalerPolicy.ResolveHotkey(name));
    }

    [Theory]
    [InlineData("version.dll", "dinput8.dll", "dx9", "version.dll")]
    [InlineData(null, "dinput8.dll", "dx9", "dinput8.dll")]
    [InlineData(null, null, "DirectX9", "d3d9.dll")]
    [InlineData(null, null, "OpenGL", "opengl32.dll")]
    [InlineData(null, null, "dx12", "dxgi.dll")]
    public void ReShadeCoexistenceRetainsOverridePrecedence(string? user, string? manifest, string api, string expected)
    {
        Assert.Equal(expected, OptiScalerPolicy.ResolveReShadeFilename(user, manifest, api));
    }
}
