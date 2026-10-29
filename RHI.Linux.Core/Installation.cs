using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RenoDXCommander.Models;

namespace RHI.Linux.Core;

public sealed record Payload(string RelativePath, byte[] Content, bool SeedOnly = false, bool PreserveExisting = false);
public sealed class ManagedFile
{
    public string Path { get; set; } = "";
    public string Hash { get; set; } = "";
    public string Component { get; set; } = "";
    public string? Backup { get; set; }
    public bool Mutable { get; set; }
}
public sealed class InstallState
{
    public int Version { get; set; } = 1;
    public Dictionary<string, string> Components { get; set; } = [];
    public List<ManagedFile> Files { get; set; } = [];
    public string? Proxy { get; set; }
}
public sealed record UndoFile(string RelativePath, string? Snapshot);
public sealed class Journal
{
    public InstallState State { get; set; } = new();
    public List<UndoFile> Files { get; set; } = [];
}

// Each game owns its backups and transaction log, so uninstall survives moving the app
// or clearing its cache. Preflight all conflicts before changing any plugin files.
public sealed class Installation
{
    private readonly string _root;
    private readonly string _meta;
    private string Manifest => Path.Combine(_meta, "manifest.json");
    private string JournalPath => Path.Combine(_meta, "transaction.json");
    public Installation(string directory)
    {
        _root = LinuxPaths.Canonical(directory);
        if (!Directory.Exists(_root)) throw new DirectoryNotFoundException(_root);
        _meta = LinuxPaths.ResolveCase(_root, ".rhi-linux");
        if (Directory.Exists(_meta) && new DirectoryInfo(_meta).LinkTarget != null) throw new IOException("RHI metadata must not be a symbolic link.");
    }

    public InstallState ReadState()
    {
        if (!File.Exists(Manifest)) return new();
        var state = JsonSerializer.Deserialize<InstallState>(File.ReadAllText(Manifest), LinuxPaths.Json)
            ?? throw new IOException("Invalid RHI installation record.");
        if (state.Version != 1) throw new IOException("Unsupported RHI installation record version.");
        return state;
    }

    private FileStream Lock()
    {
        Directory.CreateDirectory(_meta);
        var stream = new FileStream(Path.Combine(_meta, "lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try { Recover(); return stream; } catch { stream.Dispose(); throw; }
    }
    public static string Hash(byte[] content) => Convert.ToHexString(SHA256.HashData(content));
    private static string HashFile(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }

    private void Recover()
    {
        if (!File.Exists(JournalPath)) return;
        var journal = JsonSerializer.Deserialize<Journal>(File.ReadAllText(JournalPath), LinuxPaths.Json)
            ?? throw new IOException("Invalid transaction journal. Restore from .rhi-linux manually.");
        foreach (var file in journal.Files)
        {
            var path = LinuxPaths.ResolveCase(_root, file.RelativePath);
            if (file.Snapshot is null) { if (File.Exists(path)) File.Delete(path); }
            else AtomicWrite(path, File.ReadAllBytes(LinuxPaths.ResolveCase(_meta, file.Snapshot)));
        }
        LinuxPaths.WriteJson(Manifest, journal.State);
        File.Delete(JournalPath);
        CleanupTransaction();
    }

    private void CleanupTransaction()
    {
        var path = Path.Combine(_meta, "transaction");
        if (Directory.Exists(path)) Directory.Delete(path, true);
    }

    private void Begin(InstallState state, IEnumerable<string> relativePaths)
    {
        CleanupTransaction();
        Directory.CreateDirectory(Path.Combine(_meta, "transaction"));
        var journal = new Journal { State = state };
        foreach (var relative in relativePaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path = LinuxPaths.ResolveCase(_root, relative);
            string? snapshot = null;
            if (File.Exists(path))
            {
                snapshot = "transaction/" + Guid.NewGuid().ToString("N");
                File.Copy(path, Path.Combine(_meta, snapshot));
            }
            journal.Files.Add(new(relative, snapshot));
        }
        LinuxPaths.WriteJson(JournalPath, journal);
    }

    private void Commit(InstallState state)
    {
        LinuxPaths.WriteJson(Manifest, state);
        File.Delete(JournalPath);
        CleanupTransaction();
    }

    private static void AtomicWrite(string path, byte[] content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".rhi-" + Guid.NewGuid().ToString("N");
        try { File.WriteAllBytes(temp, content); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public void Install(string component, string version, IEnumerable<Payload> payloads, bool replaceForeign = false, string? proxy = null)
    {
        using var guard = Lock();
        var state = ReadState();
        if (proxy != null && state.Proxy != null && proxy != state.Proxy)
            throw new IOException("Remove the existing ReShade installation before changing its graphics API / proxy DLL.");
        var files = payloads.ToList();
        if (files.Count == 0) throw new IOException("The component contains no files.");
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var planned = new List<(Payload Payload, string Path, ManagedFile? Previous)>();
        foreach (var file in files)
        {
            var path = LinuxPaths.ResolveCase(_root, file.RelativePath);
            if (path == _meta || LinuxPaths.IsWithin(_meta, path)) throw new IOException("A plugin cannot overwrite RHI metadata.");
            if (!targets.Add(path)) throw new IOException("The package contains duplicate Windows filenames.");
            if (Directory.Exists(path)) throw new IOException("A directory occupies the target file: " + path);
            var old = state.Files.FirstOrDefault(f => f.Path.Equals(Path.GetRelativePath(_root, path), StringComparison.OrdinalIgnoreCase));
            if (file.SeedOnly && File.Exists(path)) continue;
            if (file.PreserveExisting && old == null && File.Exists(path)) continue;
            if (old != null && old.Component != component) throw new IOException($"{path} belongs to {old.Component}. Remove that component first.");
            if (File.Exists(path))
            {
                if (old != null && HashFile(path) != old.Hash) throw new IOException($"{path} changed outside RHI. Move it aside before updating.");
                if (old == null && !replaceForeign) throw new IOException($"{path} already exists. Enable ‘Back up and replace existing files’ to preserve and replace it.");
            }
            planned.Add((file, path, old));
        }
        // Retire files dropped from a newer package, retaining its original backups.
        var obsolete = state.Files.Where(f => f.Component == component && !targets.Contains(Path.Combine(_root, f.Path))).ToList();
        foreach (var old in obsolete) CheckRemoval(old);
        Begin(state, planned.Select(p => Path.GetRelativePath(_root, p.Path)).Concat(obsolete.Select(f => f.Path)));
        try
        {
            foreach (var old in obsolete) { Restore(old); state.Files.Remove(old); }
            foreach (var (file, path, old) in planned)
            {
                var record = old ?? new ManagedFile { Path = Path.GetRelativePath(_root, path), Component = component, Mutable = file.SeedOnly };
                if (old == null && File.Exists(path))
                {
                    record.Backup = "originals/" + Guid.NewGuid().ToString("N");
                    Directory.CreateDirectory(Path.Combine(_meta, "originals"));
                    File.Copy(path, Path.Combine(_meta, record.Backup));
                }
                AtomicWrite(path, file.Content);
                record.Hash = Hash(file.Content);
                record.Mutable = file.SeedOnly;
                if (old == null) state.Files.Add(record);
            }
            state.Components[component] = version;
            if (proxy != null) state.Proxy = proxy;
            Commit(state);
        }
        catch { Recover(); throw; }
    }

    private void CheckRemoval(ManagedFile file)
    {
        var path = LinuxPaths.ResolveCase(_root, file.Path);
        if (File.Exists(path) && HashFile(path) != file.Hash && !file.Mutable)
            throw new IOException($"{path} changed outside RHI. It has been preserved. Move it aside before removing this component.");
        if (file.Backup != null && !File.Exists(LinuxPaths.ResolveCase(_meta, file.Backup))) throw new IOException("Original backup is missing: " + file.Backup);
    }

    private void Restore(ManagedFile file)
    {
        var path = LinuxPaths.ResolveCase(_root, file.Path);
        if (file.Mutable && File.Exists(path) && HashFile(path) != file.Hash) return;
        if (file.Backup != null) AtomicWrite(path, File.ReadAllBytes(LinuxPaths.ResolveCase(_meta, file.Backup)));
        else if (File.Exists(path)) File.Delete(path);
    }

    public void Remove(string? component = null)
    {
        using var guard = Lock();
        var state = ReadState();
        if (component == "ReShade" && state.Files.Any(f => f.Component != "ReShade" && f.Path.EndsWith(".addon64", StringComparison.OrdinalIgnoreCase)
            || f.Component != "ReShade" && f.Path.EndsWith(".addon32", StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Remove the addons first, or choose ‘Remove all managed components’.");
        var files = state.Files.Where(f => component == null || f.Component == component).ToList();
        foreach (var file in files) CheckRemoval(file);
        Begin(state, files.Select(f => f.Path));
        try
        {
            foreach (var file in files) { Restore(file); state.Files.Remove(file); }
            if (component == null) state.Components.Clear(); else state.Components.Remove(component);
            if (component is null or "ReShade") state.Proxy = null;
            Commit(state);
        }
        catch { Recover(); throw; }
    }

    // Renames one managed file in place, e.g. ReShade's dxgi.dll to ReShade64.dll so OptiScaler can
    // take the proxy name and load ReShade itself. A file that replaced a game original cannot move,
    // since its backup belongs to the original name.
    public void Move(string component, string from, string to)
    {
        using var guard = Lock();
        var state = ReadState();
        var source = LinuxPaths.ResolveCase(_root, from);
        var file = state.Files.FirstOrDefault(f => f.Component == component && LinuxPaths.ResolveCase(_root, f.Path) == source)
            ?? throw new IOException($"{from} is not managed by {component}.");
        if (file.Backup != null) throw new IOException($"{component} replaced the game's own {from}, so it cannot be renamed. Choose another DLL name.");
        var target = LinuxPaths.ResolveCase(_root, to);
        if (File.Exists(target) || state.Files.Any(f => LinuxPaths.ResolveCase(_root, f.Path) == target)) throw new IOException($"{target} already exists.");
        CheckRemoval(file);
        Begin(state, [file.Path, Path.GetRelativePath(_root, target)]);
        try
        {
            AtomicWrite(target, File.ReadAllBytes(source));
            File.Delete(source);
            file.Path = Path.GetRelativePath(_root, target);
            Commit(state);
        }
        catch { Recover(); throw; }
    }

    public const string ReShadeBesideOptiScaler = "ReShade64.dll";
    // Where ReShade goes for this proxy: OptiScaler owning the proxy name loads ReShade64.dll instead.
    public string ReShadeFile(string proxy)
    {
        var state = ReadState();
        var target = LinuxPaths.ResolveCase(_root, proxy);
        return state.Files.Any(f => f.Component != "ReShade" && LinuxPaths.ResolveCase(_root, f.Path) == target)
            || state.Files.Any(f => f.Component == "ReShade" && f.Path.Equals(ReShadeBesideOptiScaler, StringComparison.OrdinalIgnoreCase))
            ? ReShadeBesideOptiScaler : proxy;
    }

    public static string ProxyFor(GraphicsApiType api) => api switch
    {
        GraphicsApiType.DirectX9 => "d3d9.dll",
        GraphicsApiType.DirectX10 or GraphicsApiType.DirectX11 or GraphicsApiType.DirectX12 => "dxgi.dll",
        GraphicsApiType.OpenGL => "opengl32.dll",
        GraphicsApiType.Vulkan => throw new IOException("Native Vulkan ReShade injection is not supported by this Linux build. DX11/DX12 games translated by Proton are supported; select their DirectX API."),
        _ => throw new IOException("Select DirectX 9, 10, 11, 12 or OpenGL before installing ReShade.")
    };

    public static Payload DefaultIni() => new("ReShade.ini", Encoding.UTF8.GetBytes("""
        [GENERAL]
        EffectSearchPaths=.\reshade-shaders\Shaders\**
        TextureSearchPaths=.\reshade-shaders\Textures\**
        PresetPath=.\ReShadePreset.ini
        [ADDON]
        AddonPath=.
        [INPUT]
        KeyOverlay=36,0,0,0
        [OVERLAY]
        TutorialProgress=0
        """.Replace("\r\n", "\n") + "\n"), true);
}
