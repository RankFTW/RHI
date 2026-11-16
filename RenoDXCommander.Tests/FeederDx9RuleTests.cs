using RenoDXCommander.Models;
using RenoDXCommander.ViewModels;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// One rule for "DX9 game" across DLSS5 Feeder's checks (issue #135): ReShade as dxgi.dll, dgVoodoo2 as D3D9.dll.
/// </summary>
public class FeederDx9RuleTests
{
    private static GameCardViewModel Card(GraphicsApiType main, params GraphicsApiType[] detected) => new()
    {
        GameName = "Test",
        GraphicsApi = main,
        DetectedApis = new HashSet<GraphicsApiType>(detected),
    };

    [Fact]
    public void MainApiDx9_IsDx9() =>
        Assert.True(DetailPanelBuilder.IsDx9FeederGame(Card(GraphicsApiType.DirectX9)));

    [Fact]
    public void Dx9WithOpenGl_MainApiNotDx9_IsDx9() =>
        // Assassin's Creed Brotherhood: PCGW also lists OpenGL; the main API may not come out as DX9
        Assert.True(DetailPanelBuilder.IsDx9FeederGame(Card(GraphicsApiType.OpenGL, GraphicsApiType.DirectX9, GraphicsApiType.OpenGL)));

    [Theory]
    [InlineData(GraphicsApiType.DirectX11)]
    [InlineData(GraphicsApiType.DirectX12)]
    public void LegacyDx9ShimInDx11Or12Game_IsNotDx9(GraphicsApiType main) =>
        Assert.False(DetailPanelBuilder.IsDx9FeederGame(Card(main, main, GraphicsApiType.DirectX9)));

    [Fact]
    public void NoDx9_IsNotDx9() =>
        Assert.False(DetailPanelBuilder.IsDx9FeederGame(Card(GraphicsApiType.Unknown)));
}
