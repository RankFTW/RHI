using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// With the NR Cost Scaler installed, nvngx_dlssnr.dll is its proxy and the real NR DLL is
/// nvngx_dlssnr_real.dll. Version swaps, "Default" and RTX Encore's NR deploy must write the real DLL.
/// </summary>
public class NrCostScalerSafetyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rhi-nrcs-" + Guid.NewGuid().ToString("N"));

    public NrCostScalerSafetyTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* temp folder; ignore */ }
    }

    private string Put(string name, byte[] content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static readonly byte[] Proxy = { 7, 7, 7 };
    private static readonly byte[] RealNr = { 1, 2, 3 };
    private static readonly byte[] NewNr = { 4, 5, 6, 7 };
    private static readonly byte[] Game = { 9, 9 };

    [Fact]
    public void Resolve_NoCostScaler_ReturnsSamePath()
    {
        var nr = Put("nvngx_dlssnr.dll", RealNr);
        Assert.Equal(nr, DlssNrCostScalerService.ResolveRealNrDllPath(nr));
    }

    [Fact]
    public void Resolve_CostScaler_ReturnsRealDll()
    {
        var nr = Put("nvngx_dlssnr.dll", Proxy);
        var real = Put("nvngx_dlssnr_real.dll", RealNr);
        Assert.Equal(real, DlssNrCostScalerService.ResolveRealNrDllPath(nr));
    }

    [Fact]
    public void Resolve_OtherFileName_Unchanged()
    {
        Put("nvngx_dlssnr_real.dll", RealNr);
        var dlss = Put("nvngx_dlss.dll", RealNr);
        Assert.Equal(dlss, DlssNrCostScalerService.ResolveRealNrDllPath(dlss));
    }

    [Fact]
    public void Replace_NoCostScaler_DoesNothing()
    {
        var nr = Put("nvngx_dlssnr.dll", RealNr);
        var src = Put("src.bin", NewNr);
        Assert.False(DlssNrCostScalerService.ReplaceRealNrDll(nr, src));
        Assert.Equal(RealNr, File.ReadAllBytes(nr));
        Assert.False(File.Exists(nr + ".original"));
    }

    [Fact]
    public void Replace_CostScaler_KeepsProxyAndExistingSentinel()
    {
        var nr = Put("nvngx_dlssnr.dll", Proxy);
        var real = Put("nvngx_dlssnr_real.dll", RealNr);
        Put("nvngx_dlssnr.dll.original", Array.Empty<byte>()); // NR method placed the DLL
        var src = Put("src.bin", NewNr);

        Assert.True(DlssNrCostScalerService.ReplaceRealNrDll(nr, src));
        Assert.Equal(Proxy, File.ReadAllBytes(nr));
        Assert.Equal(NewNr, File.ReadAllBytes(real));
        Assert.Empty(File.ReadAllBytes(nr + ".original"));
    }

    [Fact]
    public void Replace_CostScaler_NoBackup_BacksUpRealUnderNrName()
    {
        var nr = Put("nvngx_dlssnr.dll", Proxy);
        var real = Put("nvngx_dlssnr_real.dll", Game);
        var src = Put("src.bin", NewNr);

        Assert.True(DlssNrCostScalerService.ReplaceRealNrDll(nr, src));
        Assert.Equal(Game, File.ReadAllBytes(nr + ".original"));
        Assert.Equal(NewNr, File.ReadAllBytes(real));
        Assert.False(File.Exists(real + ".original"));
    }

    [Fact]
    public void Restore_CostScaler_RealBackup_RestoresBehindProxy()
    {
        var nr = Put("nvngx_dlssnr.dll", Proxy);
        var real = Put("nvngx_dlssnr_real.dll", NewNr);
        Put("nvngx_dlssnr.dll.original", Game);

        var svc = new DlssStreamlineService(new HttpClient(), new GitHubETagCache());
        Assert.True(svc.Restore(nr, _dir));
        Assert.Equal(Proxy, File.ReadAllBytes(nr));
        Assert.Equal(Game, File.ReadAllBytes(real));
        Assert.False(File.Exists(nr + ".original"));
    }

    [Fact]
    public void Restore_CostScaler_ZeroByteSentinel_Kept()
    {
        var nr = Put("nvngx_dlssnr.dll", Proxy);
        var real = Put("nvngx_dlssnr_real.dll", RealNr);
        Put("nvngx_dlssnr.dll.original", Array.Empty<byte>());

        Assert.True(DlssNrCostScalerService.RestoreRealNrDll(nr));
        Assert.Equal(Proxy, File.ReadAllBytes(nr));
        Assert.Equal(RealNr, File.ReadAllBytes(real));
        Assert.True(File.Exists(nr + ".original"));
    }
}
