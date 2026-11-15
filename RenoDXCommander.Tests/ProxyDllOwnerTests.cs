using RenoDXCommander.Models;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// Proxy DLL names held by RHI components that track the name themselves (RE Framework's dinput8.dll,
/// DXVK, dgVoodoo2's d3d9.dll, ReShade, OptiScaler, Display Commander) must be known as taken, so
/// ASI Loader never replaces or chains them and extras' pickers block them.
/// </summary>
public class ProxyDllOwnerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rhi-proxyowner-" + Guid.NewGuid().ToString("N"));

    public ProxyDllOwnerTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* temp folder; ignore */ }
    }

    [Fact]
    public void ReFramework_OwnsDinput8()
    {
        var card = new GameCardViewModel { GameName = "RE Game", InstallPath = _dir };
        Assert.Null(UltimateAsiLoaderService.FixedRhiDllOwner(card, "dinput8.dll"));

        card.RefRecord = new REFrameworkInstalledRecord { GameName = "RE Game", InstallPath = _dir };
        Assert.Equal("RE Framework", UltimateAsiLoaderService.FixedRhiDllOwner(card, "DINPUT8.dll"));
    }

    [Fact]
    public void Dxvk_OwnsItsRootDlls()
    {
        var card = new GameCardViewModel
        {
            GameName = "DX11 Game",
            InstallPath = _dir,
            DxvkRecord = new DxvkInstalledRecord { InstalledDlls = new List<string> { "d3d11.dll", "dxgi.dll" } },
        };
        Assert.Equal("DXVK", UltimateAsiLoaderService.FixedRhiDllOwner(card, "dxgi.dll"));
        Assert.Equal("DXVK", UltimateAsiLoaderService.FixedRhiDllOwner(card, "d3d11.dll"));
        Assert.Null(UltimateAsiLoaderService.FixedRhiDllOwner(card, "version.dll"));
    }

    [Fact]
    public void DgVoodoo_OwnsD3d9_WhenConfPresent()
    {
        var card = new GameCardViewModel { GameName = "DX9 Game", InstallPath = _dir };
        Assert.Null(UltimateAsiLoaderService.FixedRhiDllOwner(card, "d3d9.dll"));

        File.WriteAllText(Path.Combine(_dir, "dgVoodoo.conf"), "");
        Assert.Equal("dgVoodoo2", UltimateAsiLoaderService.FixedRhiDllOwner(card, "d3d9.dll"));
    }
}
