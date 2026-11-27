// CustomDllService.cs — the user's own single-file DLLs (Special K and the like) in %LocalAppData%\RHI\Custom\DLLs\:
// list them (bitness from the PE header), copy one into a game under a chosen name and take it out again.
// Updates (folder watcher, redeploy pass), rename and folder-move record changes are in CustomDllService.Update.cs.
//
// Per-game state lives in two places: rhi_install.txt component "CustomDll:<source>" (what makes RHI treat the
// name as its own) and custom_dll_installs.json in RHI's data folder (keyed "GameName|Store": source, installed-as,
// folder and the SHA-256 of the copy RHI made, which tells RHI's copy from a file the user replaced by hand).
// Safety: the game's own file at the target name is kept as .original (sentinel pattern); a copy is only deleted
// or replaced while its hash is still RHI's; files a mod writes beside its DLL (dxgi.ini...) are never touched.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public partial class CustomDllService : ICustomDllService
{
    /// <summary>rhi_install.txt component name prefix; the source file name follows.</summary>
    public const string ComponentPrefix = "CustomDll:";
    public const string StoreFileName = "custom_dll_installs.json";

    /// <summary>The real custom DLL folder: next to Custom\ReShade.</summary>
    public static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "Custom", "DLLs");

    private static string DefaultDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI");

    private readonly string _folder;
    private readonly string _storePath;

    public CustomDllService() : this(null, null) { }

    /// <param name="folder">Custom DLL folder; null = <see cref="DefaultFolder"/> (tests pass a temp folder).</param>
    /// <param name="dataDir">Folder for the record store; null = RHI's data folder.</param>
    public CustomDllService(string? folder, string? dataDir)
    {
        _folder = folder ?? DefaultFolder;
        _storePath = Path.Combine(dataDir ?? DefaultDataDir, StoreFileName);
    }

    public string Folder
    {
        get
        {
            try { Directory.CreateDirectory(_folder); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { CrashReporter.Log($"[CustomDllService.Folder] Couldn't create '{_folder}' — {ex.Message}"); }
            return _folder;
        }
    }

    /// <summary>Standard proxy names, offered first in the "Install as" list.</summary>
    public static readonly IReadOnlyList<string> StandardNames = new[]
    {
        "dxgi.dll", "d3d11.dll", "d3d12.dll", "d3d9.dll", "dinput8.dll", "version.dll", "winmm.dll", "dbghelp.dll",
    };

    /// <summary>
    /// The "Install as" names for a game of this bitness: <see cref="StandardNames"/>, then ASI Loader's names
    /// (Bink and other names that need the game's own file chained are left out).
    /// </summary>
    public static IReadOnlyList<string> TargetNames(bool is32Bit)
    {
        var list = new List<string>(StandardNames);
        foreach (var n in is32Bit ? UltimateAsiLoaderService.Win32Names : UltimateAsiLoaderService.Win64Names)
        {
            if (n.StartsWith("bink", StringComparison.OrdinalIgnoreCase)
                || n.Equals("xlive.dll", StringComparison.OrdinalIgnoreCase)
                || n.Equals("vorbisFile.dll", StringComparison.OrdinalIgnoreCase)) continue;
            if (!list.Contains(n, StringComparer.OrdinalIgnoreCase)) list.Add(n);
        }
        return list;
    }

    // ── Sources ───────────────────────────────────────────────────────────────

    public IReadOnlyList<CustomDllSource> ListSources()
    {
        var result = new List<CustomDllSource>();
        string[] files;
        try { files = Directory.GetFiles(Folder, "*.dll", SearchOption.TopDirectoryOnly); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[CustomDllService.ListSources] Couldn't list '{_folder}' — {ex.Message}");
            return result;
        }
        foreach (var path in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(path);
            var (is64, problem) = ReadPe(path);
            result.Add(new CustomDllSource(name, path, LabelFor(path, name), is64, problem));
        }
        return result;
    }

    /// <summary>
    /// Bitness of a PE DLL and why it can't be used (null = usable). Reads the DOS and PE file headers:
    /// machine x86 / x64, and the IMAGE_FILE_DLL flag.
    /// </summary>
    internal static (bool Is64Bit, string? Problem) ReadPe(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buf = new byte[4096];
            int n = fs.Read(buf, 0, buf.Length);
            if (n < 0x40 || buf[0] != 'M' || buf[1] != 'Z') return (false, "not a Windows DLL (no MZ header)");
            int pe = BitConverter.ToInt32(buf, 0x3C);
            if (pe < 0 || pe + 24 > n) return (false, "not a Windows DLL (PE header out of range)");
            if (buf[pe] != 'P' || buf[pe + 1] != 'E' || buf[pe + 2] != 0 || buf[pe + 3] != 0)
                return (false, "not a Windows DLL (no PE header)");
            ushort machine = BitConverter.ToUInt16(buf, pe + 4);
            ushort characteristics = BitConverter.ToUInt16(buf, pe + 22);
            if ((characteristics & 0x2000) == 0) return (false, "not a DLL (it's a program)");
            return machine switch
            {
                0x8664 => (true, null),
                0x014C => (false, null),
                _ => (false, $"unsupported machine type 0x{machine:X4}"),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (false, $"can't be read ({ex.Message})");
        }
    }

    private static string LabelFor(string path, string fileName)
    {
        try
        {
            var vi = FileVersionInfo.GetVersionInfo(path);
            var product = vi.ProductName?.Trim();
            var version = (vi.FileVersion ?? vi.ProductVersion)?.Trim();
            if (!string.IsNullOrEmpty(product))
                return string.IsNullOrEmpty(version) ? product : $"{product} {version}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            CrashReporter.Log($"[CustomDllService.LabelFor] '{fileName}' — {ex.Message}");
        }
        return fileName;
    }

    // ── Records ───────────────────────────────────────────────────────────────

    private Dictionary<string, List<CustomDllRecord>> LoadDb()
    {
        var raw = SafeJsonFile.Read<Dictionary<string, List<CustomDllRecord>>>(_storePath, out _);
        var db = new Dictionary<string, List<CustomDllRecord>>(StringComparer.OrdinalIgnoreCase);
        if (raw == null) return db;
        foreach (var (k, v) in raw)
        {
            if (v == null) continue;
            if (db.TryGetValue(k, out var existing)) existing.AddRange(v);
            else db[k] = v.Where(r => r != null).ToList();
        }
        return db;
    }

    private bool SaveDb(Dictionary<string, List<CustomDllRecord>> db, string caller)
    {
        foreach (var k in db.Where(kv => kv.Value.Count == 0).Select(kv => kv.Key).ToList()) db.Remove(k);
        return SafeJsonFile.Write(_storePath, db, caller, SafeJsonFile.Indented);
    }

    private object DbLock => SafeJsonFile.LockFor(_storePath);

    public IReadOnlyList<CustomDllRecord> GetRecords(string gameName, string store)
    {
        lock (DbLock)
        {
            var db = LoadDb();
            return db.TryGetValue(GameKey.FromCard(gameName, store).ToKey(), out var list)
                ? list.Select(Clone).ToList()
                : Array.Empty<CustomDllRecord>();
        }
    }

    private static CustomDllRecord Clone(CustomDllRecord r) => new()
    {
        Source = r.Source, InstalledAs = r.InstalledAs, InstallPath = r.InstallPath,
        Sha256 = r.Sha256, InstalledUtc = r.InstalledUtc, UpdatedUtc = r.UpdatedUtc,
    };

    // ── Names RHI holds through custom DLLs (rhi_install.txt) ─────────────────

    private static readonly ConcurrentDictionary<string, (DateTime Stamp, long Len, Dictionary<string, string> Owners)> _ownerCache
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The game-folder file names custom DLLs were installed as in <paramref name="gameDir"/> (from rhi_install.txt),
    /// mapped to "Custom DLL (&lt;source&gt;)". Cached by the file's write time, so it's cheap to call per row.
    /// </summary>
    public static Dictionary<string, string> OwnersInFolder(string? gameDir)
    {
        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(gameDir)) return owners;
        var path = Path.Combine(gameDir, RhiInstallManifest.FileName);
        DateTime stamp;
        long len;
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) return owners;
            stamp = fi.LastWriteTimeUtc;
            len = fi.Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[CustomDllService.OwnersInFolder] '{gameDir}' — {ex.Message}");
            return owners;
        }
        if (_ownerCache.TryGetValue(gameDir, out var hit) && hit.Stamp == stamp && hit.Len == len)
            return new Dictionary<string, string>(hit.Owners, StringComparer.OrdinalIgnoreCase);

        var manifest = RhiInstallManifest.Read(gameDir);
        if (manifest != null)
            foreach (var (component, rec) in manifest.Components)
            {
                if (!component.StartsWith(ComponentPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                var source = component[ComponentPrefix.Length..];
                foreach (var f in rec.Files)
                    if (!string.IsNullOrWhiteSpace(f)) owners.TryAdd(Path.GetFileName(f), OwnerLabel(source));
            }
        _ownerCache[gameDir] = (stamp, len, new Dictionary<string, string>(owners, StringComparer.OrdinalIgnoreCase));
        return owners;
    }

    /// <summary>How other components name a custom DLL that holds a file name.</summary>
    public static string OwnerLabel(string source) => $"Custom DLL ({source})";

    /// <summary>True when <paramref name="fileName"/> in <paramref name="gameDir"/> is a custom DLL RHI installed.</summary>
    public static bool IsCustomDllFile(string? gameDir, string fileName) =>
        OwnersInFolder(gameDir).ContainsKey(Path.GetFileName(fileName));

    /// <summary>Another RHI component that recorded <paramref name="fileName"/> (at the folder root) in rhi_install.txt, or null.</summary>
    private static string? ManifestOwnerOf(string gameDir, string fileName)
    {
        var m = RhiInstallManifest.Read(gameDir);
        if (m == null) return null;
        bool Is(string? f) => !string.IsNullOrEmpty(f) && f.IndexOfAny(new[] { '\\', '/' }) < 0
                              && f.Equals(fileName, StringComparison.OrdinalIgnoreCase);
        if (Is(m.InstalledAs) || m.Files.Any(Is)) return string.IsNullOrEmpty(m.Component) ? "OptiScaler" : m.Component;
        foreach (var (component, rec) in m.Components)
            if (!component.StartsWith(ComponentPrefix, StringComparison.OrdinalIgnoreCase) && rec.Files.Any(Is))
                return component;
        foreach (var (file, owners) in m.SharedFiles)
            if (Is(file) && owners.Count > 0) return string.Join(", ", owners);
        return null;
    }

    // ── Install / remove ──────────────────────────────────────────────────────

    /// <summary>Why nothing can be written into the game folder, or null.</summary>
    internal static string? FolderBlockedReason(CustomDllGame game)
    {
        if (string.IsNullOrEmpty(game.InstallPath)) return "The game has no install folder.";
        if (XboxPaths.IsWindowsApps(game.InstallPath))
            return "RHI can't write into Game Pass / Microsoft Store (WindowsApps) folders.";
        if (!Directory.Exists(game.InstallPath)) return $"The game folder doesn't exist: {game.InstallPath}";
        if (game.IsRunning) return $"{game.GameName} is running. Close it and try again.";
        return null;
    }

    /// <summary>A plain DLL file name (no folders), or null.</summary>
    internal static string? CleanTargetName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var n = name.Trim();
        if (n.IndexOfAny(new[] { '\\', '/', ':' }) >= 0 || n.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
        if (!n.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && !n.EndsWith(".asi", StringComparison.OrdinalIgnoreCase)) return null;
        return n;
    }

    public string? Install(CustomDllGame game, string source, string targetName, IReadOnlyDictionary<string, string>? otherOwners = null)
    {
        const string tag = "[CustomDllService.Install]";
        if (FolderBlockedReason(game) is { } blocked) return blocked;
        var target = CleanTargetName(targetName);
        if (target == null) return $"'{targetName}' isn't a usable DLL name.";
        var srcPath = FileHelper.SafeCombine(_folder, source);
        if (srcPath == null || !File.Exists(srcPath)) return $"{source} isn't in the custom DLL folder any more.";
        var (is64, problem) = ReadPe(srcPath);
        if (problem != null) return $"{source} can't be used: {problem}.";
        if (is64 == game.Is32Bit)
            return $"{source} is a {(is64 ? "64" : "32")}-bit DLL and {game.GameName} is a {(game.Is32Bit ? "32" : "64")}-bit game.";

        if (otherOwners != null && otherOwners.TryGetValue(target, out var owner))
            return $"{target} is used by {owner} in this game. Pick another name.";
        var ownersHere = OwnersInFolder(game.InstallPath);
        if (ownersHere.TryGetValue(target, out var customOwner) && customOwner != OwnerLabel(source))
            return $"{target} is used by {customOwner}. Two custom DLLs can't share a name.";
        if (ManifestOwnerOf(game.InstallPath, target) is { } manOwner)
            return $"{target} is used by {manOwner} in this game. Pick another name.";

        var destPath = FileHelper.SafeCombine(game.InstallPath, target);
        if (destPath == null) return $"'{target}' isn't a usable DLL name.";

        lock (DbLock)
        {
            var db = LoadDb();
            var list = db.TryGetValue(game.Key, out var l) ? l : new List<CustomDllRecord>();
            if (list.FirstOrDefault(r => r.Source.Equals(source, StringComparison.OrdinalIgnoreCase)) is { } already)
                return $"{source} is already installed in this game as {already.InstalledAs}.";
            if (list.FirstOrDefault(r => r.InstalledAs.Equals(target, StringComparison.OrdinalIgnoreCase)) is { } taken)
                return $"{target} is already used by {taken.Source}. Two custom DLLs can't share a name.";

            // A file there plus a backup beside it: overwriting would lose one of them
            if (File.Exists(destPath) && File.Exists(destPath + ".original"))
                return $"{target} and {target}.original are both in the game folder already. RHI won't overwrite either. " +
                       "Move one of them out of the game folder and try again.";

            var tmp = destPath + ".rhi-tmp";
            try
            {
                // Throws when the game's own file couldn't be moved to .original: then nothing is copied
                AuxInstallService.SentinelBackup(destPath);
                File.Copy(srcPath, tmp, overwrite: true);
                File.Move(tmp, destPath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                CrashReporter.Log($"{tag} '{game.GameName}': {source} → {target} failed — {ex.Message}");
                TryDelete(tmp, tag);
                if (!File.Exists(destPath)) AuxInstallService.SentinelRestore(destPath); // put the game's file back
                return $"Couldn't copy {source} into the game folder: {ex.Message}";
            }

            string hash;
            try { hash = Sha256Of(destPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                CrashReporter.Log($"{tag} '{game.GameName}': couldn't hash the copy — {ex.Message}");
                hash = "";
            }

            RhiInstallManifest.SetComponent(game.InstallPath, ComponentPrefix + source, new[] { target });
            list.Add(new CustomDllRecord
            {
                Source = source,
                InstalledAs = target,
                InstallPath = game.InstallPath,
                Sha256 = hash,
                InstalledUtc = DateTime.UtcNow,
            });
            db[game.Key] = list;
            SaveDb(db, "CustomDllService.Install");
        }
        CrashReporter.Log($"{tag} '{game.GameName}': {source} installed as {target} in '{game.InstallPath}'");
        return null;
    }

    public string? Remove(CustomDllGame game, string source)
    {
        const string tag = "[CustomDllService.Remove]";
        if (game.IsRunning) return $"{game.GameName} is running. Close it and try again.";
        lock (DbLock)
        {
            var db = LoadDb();
            if (!db.TryGetValue(game.Key, out var list)) return null;
            var rec = list.FirstOrDefault(r => r.Source.Equals(source, StringComparison.OrdinalIgnoreCase));
            if (rec == null) return null;
            var dir = string.IsNullOrEmpty(rec.InstallPath) ? game.InstallPath : rec.InstallPath;
            var path = FileHelper.SafeCombine(dir, rec.InstalledAs);
            string? note = null;

            if (path != null && Directory.Exists(dir) && !XboxPaths.IsWindowsApps(dir))
            {
                var backup = path + ".original";
                if (File.Exists(path))
                {
                    string? hash = null;
                    try { hash = Sha256Of(path); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        CrashReporter.Log($"{tag} '{game.GameName}': couldn't read {rec.InstalledAs} — {ex.Message}");
                        return $"Couldn't read {rec.InstalledAs} in the game folder: {ex.Message}";
                    }
                    if (!string.Equals(hash, rec.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        // Changed by hand: it's the user's file now. Keep it; a 0-byte sentinel would mark it as RHI's
                        CrashReporter.Log($"{tag} '{game.GameName}': {rec.InstalledAs} was changed outside RHI — left in place");
                        if (IsSentinel(backup)) TryDelete(backup, tag);
                        note = $"Note: {rec.InstalledAs} was changed outside RHI, so it was left in the game folder.";
                    }
                    else if (File.Exists(backup))
                    {
                        AuxInstallService.SentinelRestore(path); // deletes RHI's copy, puts the game's file back
                        if (File.Exists(path) && SameHash(path, rec.Sha256))
                            return $"Couldn't remove {rec.InstalledAs} (is the game running?). Nothing was changed.";
                    }
                    else
                    {
                        try { File.Delete(path); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            CrashReporter.Log($"{tag} '{game.GameName}': delete {rec.InstalledAs} failed — {ex.Message}");
                            return $"Couldn't remove {rec.InstalledAs}: {ex.Message}";
                        }
                    }
                }
                else
                {
                    // Already gone (user removed it): just put the game's own file back / drop the sentinel
                    AuxInstallService.SentinelRestore(path);
                }
            }

            list.Remove(rec);
            db[game.Key] = list;
            SaveDb(db, "CustomDllService.Remove");
            if (!string.IsNullOrEmpty(dir)) RhiInstallManifest.RemoveComponent(dir, ComponentPrefix + rec.Source);
            CrashReporter.Log($"{tag} '{game.GameName}': {rec.Source} ({rec.InstalledAs}) removed");
            return note;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool IsSentinel(string path)
    {
        try { return File.Exists(path) && new FileInfo(path).Length == 0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static void TryDelete(string path, string tag)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { CrashReporter.Log($"{tag} Couldn't delete '{path}' — {ex.Message}"); }
    }

    private static readonly ConcurrentDictionary<string, (long Len, DateTime Stamp, string Hash)> _hashCache
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>SHA-256 (hex) of a file, cached by size + write time. Throws IOException / UnauthorizedAccessException.</summary>
    internal static string Sha256Of(string path)
    {
        var fi = new FileInfo(path);
        if (_hashCache.TryGetValue(path, out var hit) && hit.Len == fi.Length && hit.Stamp == fi.LastWriteTimeUtc)
            return hit.Hash;
        string hash;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            hash = Convert.ToHexString(SHA256.HashData(fs));
        _hashCache[path] = (fi.Length, fi.LastWriteTimeUtc, hash);
        return hash;
    }

    private static bool SameHash(string path, string expected)
    {
        try { return string.Equals(Sha256Of(path), expected, StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}
