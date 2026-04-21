using System.Text.Json;
using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Services;
using RenoDXCommander.ViewModels;

namespace RenoDXCommander.Tests;

public class ShaderPackDeployPropertyTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly ShaderPackService _service;
    private static readonly string[] AllPackIds =
        new ShaderPackService(new HttpClient(), new GitHubETagCache()).AvailablePacks.Select(p => p.Id).ToArray();

    public ShaderPackDeployPropertyTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "RdxcDeployProp_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempRoot);
        _service = new ShaderPackService(new HttpClient(), new GitHubETagCache());
    }

    public void Dispose() { try { Directory.Delete(_tempRoot, true); } catch { } }

    private static Gen<List<string>> GenPackSubset() =>
        Gen.ListOf(AllPackIds.Length, Arb.Generate<bool>()).Select(flags =>
        {
            var s = new List<string>();
            for (int i = 0; i < AllPackIds.Length; i++) if (flags[i]) s.Add(AllPackIds[i]);
            return s;
        });

    private static Gen<string> GenSafeFilename() =>
        Gen.Choose(1, 20).SelectMany(len =>
            Gen.ArrayOf(len, Gen.Elements("abcdefghijklmnopqrstuvwxyz0123456789".ToCharArray()))
            .Select(chars => new string(chars)));

    private static Gen<List<string>> GenRelativeFilePaths()
    {
        var p = from dir in Gen.Elements("Shaders", "Textures")
                from name in GenSafeFilename()
                from ext in Gen.Elements(".fx", ".fxh", ".png", ".jpg", ".txt")
                select Path.Combine(dir, name + ext);
        return Gen.Choose(1, 5).SelectMany(c => Gen.ListOf(c, p).Select(l => l.ToList()));
    }

    private static Gen<List<string>> GenPackIdStrings()
    {
        var id = Gen.Choose(1, 15).SelectMany(len =>
            Gen.ArrayOf(len, Gen.Elements("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789".ToCharArray()))
            .Select(chars => new string(chars)));
        return Gen.Choose(0, 10).SelectMany(c => Gen.ListOf(c, id).Select(l => l.ToList()));
    }
}
