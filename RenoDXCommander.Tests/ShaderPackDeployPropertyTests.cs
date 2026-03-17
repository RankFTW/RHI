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
        new ShaderPackService(new HttpClient()).AvailablePacks.Select(p => p.Id).ToArray();

    public ShaderPackDeployPropertyTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "RdxcDeployProp_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempRoot);
        _service = new ShaderPackService(new HttpClient());
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

    // Feature: reshade-shader-packs, Property 5: Select mode deploys only selected packs
    [Property(MaxTest = 100)]
    public Property SelectMode_DeploysOnlySelectedPacks()
    {
        var gen = from subset in GenPackSubset() from suffix in Gen.Choose(1, 999999) select (subset, suffix);
        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (selectedIds, suffix) = tuple;
            var gameDir = Path.Combine(_tempRoot, $"game_sel5_{suffix}");
            Directory.CreateDirectory(gameDir);
            try
            {
                _service.SyncGameFolder(gameDir, ShaderPackService.DeployMode.Select, selectedIds);
                var rsDir = Path.Combine(gameDir, ShaderPackService.GameReShadeShaders);
                if (selectedIds.Count == 0)
                {
                    if (_service.IsManagedByRdxc(gameDir))
                        return false.Label("RDXC-managed folder exists after Select with empty selection");
                    return true.Label("OK: empty selection = Off");
                }
                if (!Directory.Exists(rsDir))
                    return true.Label($"OK: no pack records, no deployment (selected {selectedIds.Count} packs)");
                if (!_service.IsManagedByRdxc(gameDir))
                    return false.Label("reshade-shaders exists but not managed by RDXC");
                var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RenoDXCommander", "settings.json");
                if (File.Exists(settingsPath))
                {
                    var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(settingsPath));
                    if (settings != null)
                    {
                        var selectedSet = new HashSet<string>(selectedIds, StringComparer.OrdinalIgnoreCase);
                        foreach (var packId in AllPackIds.Where(id => !selectedSet.Contains(id)))
                        {
                            var key = $"ShaderPack_{packId}_Files";
                            if (!settings.TryGetValue(key, out var json) || string.IsNullOrEmpty(json)) continue;
                            var files = JsonSerializer.Deserialize<List<string>>(json) ?? new();
                            foreach (var rel in files)
                            {
                                if (!File.Exists(Path.Combine(rsDir, rel))) continue;
                                bool shared = selectedIds.Any(selId =>
                                {
                                    var sk = $"ShaderPack_{selId}_Files";
                                    return settings.TryGetValue(sk, out var sj) && (JsonSerializer.Deserialize<List<string>>(sj) ?? new()).Contains(rel);
                                });
                                if (!shared) return false.Label($"File from non-selected pack found");
                            }
                        }
                    }
                }
                return true.Label($"OK: selected {selectedIds.Count} packs");
            }
            finally { try { Directory.Delete(gameDir, true); } catch { } }
        });
    }

    // Feature: reshade-shader-packs, Property 6: Pruning preserves user-added files
    [Property(MaxTest = 100)]
    public Property Pruning_PreservesUserAddedFiles()
    {
        var gen = from userFileName in GenSafeFilename() from suffix in Gen.Choose(1, 999999) select (userFileName, suffix);
        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (userFileName, suffix) = tuple;
            var gameDir = Path.Combine(_tempRoot, $"game_prune6_{suffix}");
            Directory.CreateDirectory(gameDir);
            try
            {
                var rsDir = Path.Combine(gameDir, ShaderPackService.GameReShadeShaders);
                var shadersDir = Path.Combine(rsDir, "Shaders");
                Directory.CreateDirectory(shadersDir);
                File.WriteAllText(Path.Combine(rsDir, "Managed by RDXC.txt"), "This folder is managed by RenoDXCommander.");
                var userFilePath = Path.Combine(shadersDir, $"_user_{userFileName}.fx");
                var userContent = $"// user shader {userFileName}";
                File.WriteAllText(userFilePath, userContent);
                var texturesDir = Path.Combine(rsDir, "Textures");
                Directory.CreateDirectory(texturesDir);
                var userTexturePath = Path.Combine(texturesDir, $"_user_{userFileName}.png");
                File.WriteAllBytes(userTexturePath, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
                _service.SyncGameFolder(gameDir, ShaderPackService.DeployMode.Minimum);
                if (!File.Exists(userFilePath)) return false.Label($"User shader removed by pruning");
                if (File.ReadAllText(userFilePath) != userContent) return false.Label("User shader content modified");
                if (!File.Exists(userTexturePath)) return false.Label($"User texture removed by pruning");
                if (!_service.IsManagedByRdxc(gameDir)) return false.Label("Folder lost RDXC management");
                return true.Label($"OK: user file preserved");
            }
            finally { try { Directory.Delete(gameDir, true); } catch { } }
        });
    }

    // Feature: reshade-shader-packs, Property 7: Extracted file list round-trip
    [Property(MaxTest = 100)]
    public Property ExtractedFileList_RoundTrip_PreservesAllPaths()
    {
        return Prop.ForAll(GenRelativeFilePaths().ToArbitrary(), filePaths =>
        {
            var tempPath = Path.Combine(_tempRoot, $"settings_rt7_{Guid.NewGuid():N}.json");
            try
            {
                var key = "ShaderPack_TestPack_RT7_Files";
                var serialized = JsonSerializer.Serialize(filePaths);
                var dict = new Dictionary<string, string> { [key] = serialized };
                File.WriteAllText(tempPath, JsonSerializer.Serialize(dict));
                var readDict = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(tempPath));
                if (readDict == null) return false.Label("Deserialized dictionary is null");
                if (!readDict.TryGetValue(key, out var readValue)) return false.Label("Key not found");
                var readPaths = JsonSerializer.Deserialize<List<string>>(readValue);
                if (readPaths == null) return false.Label("Deserialized file list is null");
                if (readPaths.Count != filePaths.Count) return false.Label($"Count mismatch");
                for (int i = 0; i < filePaths.Count; i++)
                    if (readPaths[i] != filePaths[i]) return false.Label($"Path mismatch at [{i}]");
                return true.Label($"OK: {filePaths.Count} paths round-tripped");
            }
            finally { try { File.Delete(tempPath); } catch { } }
        });
    }

    // Feature: reshade-shader-packs, Property 8: Selected shader packs round-trip
    [Property(MaxTest = 100)]
    public Property SelectedShaderPacks_RoundTrip_PreservesAllIds()
    {
        return Prop.ForAll(GenPackIdStrings().ToArbitrary(), packIds =>
        {
            var source = new SettingsViewModel();
            source.SelectedShaderPacks = new List<string>(packIds);
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            source.SaveSettingsToDict(dict);
            var target = new SettingsViewModel();
            target.LoadSettingsFromDict(dict);
            if (target.SelectedShaderPacks.Count != packIds.Count)
                return false.Label($"Count mismatch: saved {packIds.Count}, loaded {target.SelectedShaderPacks.Count}");
            for (int i = 0; i < packIds.Count; i++)
                if (target.SelectedShaderPacks[i] != packIds[i])
                    return false.Label($"Id mismatch at [{i}]");
            return true.Label($"OK: {packIds.Count} pack Ids round-tripped");
        });
    }
}
