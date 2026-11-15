using RenoDXCommander.Services;
using Xunit;

namespace RenoDXCommander.Tests;

/// <summary>
/// A reshade-shaders folder RHI didn't create (no marker) must never be mirrored or deployed into
/// when it can't be moved aside to reshade-shaders-original (review item 2.1).
/// </summary>
public class ShaderFolderSafetyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rhi-shsafety-" + Guid.NewGuid().ToString("N"));
    private readonly ShaderPackService _svc = new(new HttpClient(), new GitHubETagCache());
    private static readonly string[] UnknownPack = { "__rhi_test_no_such_pack__" };

    private string RsDir => Path.Combine(_dir, ShaderPackService.GameReShadeShaders);
    private string OrigDir => Path.Combine(_dir, ShaderPackService.GameReShadeOriginal);
    private string UserFile => Path.Combine(RsDir, "Shaders", "MyOwn.fx");

    public ShaderFolderSafetyTests()
    {
        Directory.CreateDirectory(Path.Combine(RsDir, "Shaders"));
        File.WriteAllText(UserFile, "user shader");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* temp folder; ignore */ }
    }

    [Fact]
    public void Sync_UnmanagedFolder_WithExistingOriginal_IsLeftAlone()
    {
        Directory.CreateDirectory(OrigDir);
        _svc.SyncGameFolder(_dir, UnknownPack);
        Assert.True(File.Exists(UserFile));
        Assert.False(_svc.IsManagedByRdxc(_dir));
    }

    [Fact]
    public void Deploy_UnmanagedFolder_WithExistingOriginal_IsLeftAlone()
    {
        Directory.CreateDirectory(OrigDir);
        _svc.DeployToGameFolder(_dir, UnknownPack);
        Assert.True(File.Exists(UserFile));
        Assert.False(_svc.IsManagedByRdxc(_dir));
    }

    [Fact]
    public void Sync_UnmanagedFolder_IsMovedToOriginal_ThenManaged()
    {
        _svc.SyncGameFolder(_dir, UnknownPack);
        Assert.True(File.Exists(Path.Combine(OrigDir, "Shaders", "MyOwn.fx")));
        Assert.True(_svc.IsManagedByRdxc(_dir));
    }

    [Fact]
    public void Sync_UnmanagedFolder_HeldOpen_IsLeftAlone()
    {
        // An open handle inside the folder makes Directory.Move fail on Windows
        using (new FileStream(UserFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            _svc.SyncGameFolder(_dir, UnknownPack);
        Assert.True(File.Exists(UserFile));
        Assert.False(_svc.IsManagedByRdxc(_dir));
    }

    [Fact]
    public void Preserve_EmptyUnmanagedFolder_IsTakenOver()
    {
        File.Delete(UserFile);
        Directory.CreateDirectory(OrigDir);
        Assert.True(_svc.PreserveUnmanagedFolder(_dir, "Test"));
    }
}
