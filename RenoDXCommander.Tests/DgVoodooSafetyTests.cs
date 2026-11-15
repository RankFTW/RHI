using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// dgVoodoo2 removal runs on every Feeder / Luma uninstall: it must only remove dgVoodoo2's own D3D9.dll.
/// A d3d9.dll.original alone (ReShade's or DXVK's backup) must not make RHI think dgVoodoo2 is installed.
/// </summary>
public class DgVoodooSafetyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rhi-dgv-" + Guid.NewGuid().ToString("N"));
    private string D3d9 => Path.Combine(_dir, "d3d9.dll");
    private readonly DgVoodooService _svc = new(new HttpClient());

    public DgVoodooSafetyTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* temp folder; ignore */ }
    }

    [Fact]
    public void OriginalAlone_IsNotDgVoodoo()
    {
        File.WriteAllBytes(D3d9, new byte[] { 1, 2, 3 });            // ReShade / DXVK / the game's own
        File.WriteAllBytes(D3d9 + ".original", new byte[] { 4, 5 }); // someone else's backup
        Assert.False(_svc.IsDeployed(_dir));
    }

    [Fact]
    public void Remove_LeavesOtherD3d9AndItsBackup()
    {
        File.WriteAllBytes(D3d9, new byte[] { 1, 2, 3 });
        File.WriteAllBytes(D3d9 + ".original", new byte[] { 4, 5 });

        _svc.RemoveFromGame(_dir);

        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(D3d9));
        Assert.True(File.Exists(D3d9 + ".original"));
    }

    [Fact]
    public void Remove_DllAlreadyGone_CleansItsSentinel()
    {
        File.WriteAllText(Path.Combine(_dir, "dgVoodoo.conf"), "");
        File.WriteAllBytes(D3d9 + ".original", Array.Empty<byte>());
        Assert.True(_svc.IsDeployed(_dir));

        _svc.RemoveFromGame(_dir);

        Assert.False(File.Exists(D3d9 + ".original"));
        Assert.False(File.Exists(Path.Combine(_dir, "dgVoodoo.conf")));
    }
}
