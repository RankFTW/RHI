using RenoDXCommander.Models;
using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Unit tests for <see cref="DxvkService.DetermineRequiredDlls"/>.
/// Validates Requirements 3.1, 3.2, 3.3, 3.4, 3.5.
/// </summary>
public class DetermineRequiredDllsUnitTests
{
    // ── DX8 ───────────────────────────────────────────────────────────

    [Fact]
    public void DirectX8_64Bit_Returns_x64_And_d3d8()
    {
        var (arch, dlls) = DxvkService.DetermineRequiredDlls(GraphicsApiType.DirectX8, is32Bit: false);

        Assert.Equal("x64", arch);
        Assert.Single(dlls);
        Assert.Contains("d3d8.dll", dlls);
    }

    [Fact]
    public void DirectX8_32Bit_Returns_x32_And_d3d8()
    {
        var (arch, dlls) = DxvkService.DetermineRequiredDlls(GraphicsApiType.DirectX8, is32Bit: true);

        Assert.Equal("x32", arch);
        Assert.Single(dlls);
        Assert.Contains("d3d8.dll", dlls);
    }

    // ── DX9 ───────────────────────────────────────────────────────────

    [Fact]
    public void DirectX9_64Bit_Returns_x64_And_d3d9()
    {
        var (arch, dlls) = DxvkService.DetermineRequiredDlls(GraphicsApiType.DirectX9, is32Bit: false);

        Assert.Equal("x64", arch);
        Assert.Single(dlls);
        Assert.Contains("d3d9.dll", dlls);
    }

    [Fact]
    public void DirectX9_32Bit_Returns_x32_And_d3d9()
    {
        var (arch, dlls) = DxvkService.DetermineRequiredDlls(GraphicsApiType.DirectX9, is32Bit: true);

        Assert.Equal("x32", arch);
        Assert.Single(dlls);
        Assert.Contains("d3d9.dll", dlls);
    }

    // ── DX10 ──────────────────────────────────────────────────────────

    [Fact]
    public void DirectX10_64Bit_Returns_x64_And_d3d10core_dxgi()
    {
        var (arch, dlls) = DxvkService.DetermineRequiredDlls(GraphicsApiType.DirectX10, is32Bit: false);

        Assert.Equal("x64", arch);
        Assert.Equal(2, dlls.Count);
        Assert.Contains("d3d10core.dll", dlls);
        Assert.Contains("dxgi.dll", dlls);
    }

    [Fact]
    public void DirectX10_32Bit_Returns_x32_And_d3d10core_dxgi()
    {
        var (arch, dlls) = DxvkService.DetermineRequiredDlls(GraphicsApiType.DirectX10, is32Bit: true);

        Assert.Equal("x32", arch);
        Assert.Equal(2, dlls.Count);
        Assert.Contains("d3d10core.dll", dlls);
        Assert.Contains("dxgi.dll", dlls);
    }

    // ── DX11 ──────────────────────────────────────────────────────────

    [Fact]
    public void DirectX11_64Bit_Returns_x64_And_d3d11_dxgi()
    {
        var (arch, dlls) = DxvkService.DetermineRequiredDlls(GraphicsApiType.DirectX11, is32Bit: false);

        Assert.Equal("x64", arch);
        Assert.Equal(2, dlls.Count);
        Assert.Contains("d3d11.dll", dlls);
        Assert.Contains("dxgi.dll", dlls);
    }

    [Fact]
    public void DirectX11_32Bit_Returns_x32_And_d3d11_dxgi()
    {
        var (arch, dlls) = DxvkService.DetermineRequiredDlls(GraphicsApiType.DirectX11, is32Bit: true);

        Assert.Equal("x32", arch);
        Assert.Equal(2, dlls.Count);
        Assert.Contains("d3d11.dll", dlls);
        Assert.Contains("dxgi.dll", dlls);
    }

    // ── Unsupported APIs throw ────────────────────────────────────────

    [Theory]
    [InlineData(GraphicsApiType.DirectX12)]
    [InlineData(GraphicsApiType.Vulkan)]
    [InlineData(GraphicsApiType.OpenGL)]
    [InlineData(GraphicsApiType.Unknown)]
    public void UnsupportedApi_Throws_InvalidOperationException(GraphicsApiType api)
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => DxvkService.DetermineRequiredDlls(api, is32Bit: false));

        Assert.Contains("DXVK does not support", ex.Message);
        Assert.Contains(api.ToString(), ex.Message);
    }

    [Theory]
    [InlineData(GraphicsApiType.DirectX12)]
    [InlineData(GraphicsApiType.Vulkan)]
    [InlineData(GraphicsApiType.OpenGL)]
    [InlineData(GraphicsApiType.Unknown)]
    public void UnsupportedApi_32Bit_Also_Throws(GraphicsApiType api)
    {
        Assert.Throws<InvalidOperationException>(
            () => DxvkService.DetermineRequiredDlls(api, is32Bit: true));
    }
}
