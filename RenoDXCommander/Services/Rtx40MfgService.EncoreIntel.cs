using System.Net.Http;
using System.Security.Cryptography;

namespace RenoDXCommander.Services;

// ── Intel runtimes for RTX Encore ────────────────────────────────────────────
// RTX Encore's XeSS upscaler and XeFG engine need Intel's XeSS SDK 3.0.2 files, and the mod checks their
// exact SHA-256. RHI follows what RTX Encore Hub does:
//   - a copy the game already ships (often in Engine\Plugins\...\XeSS\Binaries\ThirdParty\Win64 for Unreal
//     games) is replaced in place, the game's file kept as <file>.encore-original;
//   - a file the game doesn't have goes in the private rtx-encore-runtimes\intel folder beside the mod,
//     with Intel's licence as license.txt.
// rhi_installed.txt in that folder lists what RHI did: plain names are files in the private folder,
// "native:<path relative to the game root>" lines are the game's own copies RHI found and checked.

public partial class Rtx40MfgService
{
    /// <summary>Folder (beside the RTX Encore DLL) for its Intel runtimes.</summary>
    public const string EncoreRuntimeRelDir = @"rtx-encore-runtimes\intel";

    private const string EncoreLicenseName   = "license.txt";
    private const string EncoreBackupSuffix  = ".encore-original";
    private const string EncoreNativePrefix  = "native:";
    private const int    EncoreScanMaxDirs   = 20000;
    private const int    EncoreScanMaxDepth  = 16;

    /// <summary>The XeSS SDK 3.0.2 files RTX Encore uses (as listed, with hashes, in RTX Encore Hub's catalog).</summary>
    private static readonly (string File, string RepoPath, long Size, string Sha256)[] EncoreIntelDlls =
    {
        ("libxess.dll",    "bin/libxess.dll",    77795704, "251659dd84a3e84de67c886a4186e01f3eca49b00641906fe38bb6b807e5d5b7"),
        ("libxess_fg.dll", "bin/libxess_fg.dll", 22957432, "ec5e0c65e075570c6ede72618bb666d0be0c2e10b2ea9762c0fe8cb8e375ab27"),
        ("libxell.dll",    "bin/libxell.dll",      415368, "d2030dcd694fda8f2ec7e044b13e6db8f0b56d4ba9113a5efad334e3f3ded8c7"),
    };

    private static readonly (string File, string RepoPath, long Size, string Sha256) EncoreIntelLicense =
        ("LICENSE.txt", "LICENSE.txt", 4194, "784be42c39a4a4d03cf85d88e15e59d75828fca773ce45b8dbfffbd7eaf208df");

    private static IEnumerable<(string File, string RepoPath, long Size, string Sha256)> EncoreIntelSources
        => EncoreIntelDlls.Append(EncoreIntelLicense);

    /// <summary>True when the three DLLs and the licence are staged (checked by size; hashes are checked on download).</summary>
    public bool IsEncoreIntelStagingReady
        => EncoreIntelSources.All(f => SizeIs(Path.Combine(XessStagingDir, f.File), f.Size));

    /// <summary>
    /// Downloads the Intel files RTX Encore needs (about 100 MB the first time; libxess.dll is most of it).
    /// Each download is checked against Intel's SHA-256. No-op when already staged.
    /// </summary>
    public async Task<bool> EnsureEncoreIntelStagingAsync()
    {
        Directory.CreateDirectory(XessStagingDir);
        foreach (var f in EncoreIntelSources)
        {
            var dest = Path.Combine(XessStagingDir, f.File);
            if (SizeIs(dest, f.Size)) continue;
            var url = $"https://raw.githubusercontent.com/intel/xess/{XessSdkTag}/{f.RepoPath}";
            var temp = dest + ".tmp";
            try
            {
                using (var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                {
                    resp.EnsureSuccessStatusCode();
                    await using var src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    await using var dst = File.Create(temp);
                    await src.CopyToAsync(dst).ConfigureAwait(false);
                }
                if (!HashIs(temp, f.Sha256))
                {
                    _crashReporter.Log($"[Rtx40MfgService.EnsureEncoreIntelStaging] '{f.File}' from {url} doesn't match Intel's SHA-256 — discarded");
                    File.Delete(temp);
                    return false;
                }
                File.Move(temp, dest, overwrite: true);
                _crashReporter.Log($"[Rtx40MfgService.EnsureEncoreIntelStaging] Staged {f.File} ({f.Size:N0} bytes, XeSS SDK {XessSdkTag})");
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException)
            {
                _crashReporter.Log($"[Rtx40MfgService.EnsureEncoreIntelStaging] Download of '{f.File}' failed — {ex.Message}");
                try { if (File.Exists(temp)) File.Delete(temp); }
                catch (IOException ex2) { _crashReporter.Log($"[Rtx40MfgService.EnsureEncoreIntelStaging] Couldn't delete '{temp}' — {ex2.Message}"); }
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Puts the Intel files where RTX Encore uses them: replaces the game's own copies in place (backed up),
    /// and copies the rest, plus Intel's licence, into rtx-encore-runtimes\intel beside the mod.
    /// RHI copies in the private folder that the game now also ships are removed (the game's copy is used).
    /// Call <see cref="EnsureEncoreIntelStagingAsync"/> first. Runs a folder scan: call off the UI thread.
    /// </summary>
    public bool DeployEncoreIntelFiles(string installPath, string modDir)
    {
        if (string.IsNullOrEmpty(installPath) || string.IsNullOrEmpty(modDir) || !IsEncoreIntelStagingReady) return false;
        var gameRoot = DlssStreamlineService.ResolveSearchRoot(installPath);
        var dir = Path.Combine(modDir, EncoreRuntimeRelDir);
        var marker = Path.Combine(dir, XessMarkerFile);
        try
        {
            Directory.CreateDirectory(dir);
            var (privateFiles, nativeFiles) = ReadEncoreMarker(marker);
            var natives = FindGameIntelFiles(gameRoot);

            foreach (var f in EncoreIntelDlls)
            {
                var staged = Path.Combine(XessStagingDir, f.File);
                var privatePath = Path.Combine(dir, f.File);
                var gameCopies = natives.Where(p => Path.GetFileName(p).Equals(f.File, StringComparison.OrdinalIgnoreCase)).ToList();

                if (gameCopies.Count > 0)
                {
                    foreach (var p in gameCopies)
                    {
                        nativeFiles.Add(Path.GetRelativePath(gameRoot, p));
                        if (SameContent(p, staged)) continue; // already 3.0.2
                        // The game's file (its original, or a newer one a game update put back) becomes the backup
                        var backup = p + EncoreBackupSuffix;
                        if (File.Exists(backup)) File.Delete(backup);
                        File.Move(p, backup);
                        File.Copy(staged, p);
                        _crashReporter.Log($"[Rtx40MfgService.DeployEncoreIntelFiles] Replaced the game's '{p}' ({FileVersionOf(backup)}) with XeSS SDK {XessSdkTag}");
                    }
                    // The game has its own copy now: RHI's duplicate in the private folder goes
                    if (privateFiles.Remove(f.File) && File.Exists(privatePath))
                    {
                        File.Delete(privatePath);
                        _crashReporter.Log($"[Rtx40MfgService.DeployEncoreIntelFiles] Removed RHI's duplicate '{privatePath}'");
                    }
                }
                else
                {
                    if (File.Exists(privatePath) && !privateFiles.Contains(f.File) && !SameContent(privatePath, staged))
                    {
                        _crashReporter.Log($"[Rtx40MfgService.DeployEncoreIntelFiles] Keeping the user's own '{privatePath}'");
                        continue;
                    }
                    File.Copy(staged, privatePath, overwrite: true);
                    privateFiles.Add(f.File);
                }
            }

            File.Copy(Path.Combine(XessStagingDir, EncoreIntelLicense.File), Path.Combine(dir, EncoreLicenseName), overwrite: true);
            privateFiles.Add(EncoreLicenseName);
            WriteEncoreMarker(marker, privateFiles, nativeFiles);
            _crashReporter.Log($"[Rtx40MfgService.DeployEncoreIntelFiles] Intel runtimes for RTX Encore: {privateFiles.Count - 1} in '{dir}', {nativeFiles.Count} in the game's own folders");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"[Rtx40MfgService.DeployEncoreIntelFiles] Failed for '{installPath}' — {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Undoes <see cref="DeployEncoreIntelFiles"/>: puts the game's own Intel files back, deletes RHI's copies and
    /// the licence, and removes the runtime folders when nothing else is left in them.
    /// A game file that changed since RHI replaced it (a game update) is kept, and the old backup dropped.
    /// </summary>
    public void RemoveEncoreIntelFiles(string installPath, string modDir)
    {
        if (string.IsNullOrEmpty(installPath) || string.IsNullOrEmpty(modDir)) return;
        var gameRoot = DlssStreamlineService.ResolveSearchRoot(installPath);
        var dir = Path.Combine(modDir, EncoreRuntimeRelDir);
        var marker = Path.Combine(dir, XessMarkerFile);
        if (!File.Exists(marker)) return; // RHI didn't set it up — leave the user's files alone
        try
        {
            var (privateFiles, nativeFiles) = ReadEncoreMarker(marker);
            foreach (var rel in nativeFiles)
            {
                var p = FileHelper.SafeCombine(gameRoot, rel);
                if (p == null) continue;
                var backup = p + EncoreBackupSuffix;
                if (!File.Exists(backup)) continue; // the game's copy was already 3.0.2: RHI didn't change it
                var staged = Path.Combine(XessStagingDir, Path.GetFileName(p));
                if (File.Exists(p) && File.Exists(staged) && !SameContent(p, staged))
                {
                    File.Delete(backup);
                    _crashReporter.Log($"[Rtx40MfgService.RemoveEncoreIntelFiles] Keeping '{p}': the game replaced it after RHI did");
                    continue;
                }
                if (File.Exists(p)) File.Delete(p);
                File.Move(backup, p);
                _crashReporter.Log($"[Rtx40MfgService.RemoveEncoreIntelFiles] Restored the game's own '{p}'");
            }
            foreach (var name in privateFiles)
            {
                var p = Path.Combine(dir, name);
                if (File.Exists(p)) File.Delete(p);
            }
            File.Delete(marker);
            if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
            var parent = Path.GetDirectoryName(dir);
            if (parent != null && Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any())
                Directory.Delete(parent);
            _crashReporter.Log($"[Rtx40MfgService.RemoveEncoreIntelFiles] Removed RHI's Intel runtimes for RTX Encore from '{installPath}'");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _crashReporter.Log($"[Rtx40MfgService.RemoveEncoreIntelFiles] Failed for '{installPath}' — {ex.Message}");
        }
    }

    /// <summary>
    /// True when the Intel files need setting up: never done, done by an older RHI (no licence, no libxess.dll,
    /// game copies not checked), or a game copy RHI replaced no longer matches (a game update put the old one back).
    /// Files the user deleted from the private folder aren't re-created. Off the UI thread (touches the disk).
    /// </summary>
    public static bool NeedsEncoreIntelFiles(string installPath, string modDir)
    {
        if (string.IsNullOrEmpty(installPath) || string.IsNullOrEmpty(modDir)) return false;
        var marker = Path.Combine(modDir, EncoreRuntimeRelDir, XessMarkerFile);
        if (!File.Exists(marker)) return true;
        var (privateFiles, nativeFiles) = ReadEncoreMarker(marker);
        if (!privateFiles.Contains(EncoreLicenseName)) return true;
        var gameRoot = DlssStreamlineService.ResolveSearchRoot(installPath);
        foreach (var rel in nativeFiles)
        {
            var p = FileHelper.SafeCombine(gameRoot, rel);
            if (p == null || !File.Exists(p)) continue;
            var expected = EncoreIntelDlls.FirstOrDefault(f => f.File.Equals(Path.GetFileName(p), StringComparison.OrdinalIgnoreCase));
            if (expected.File != null && !SizeIs(p, expected.Size)) return true;
        }
        return false;
    }

    /// <summary>
    /// For the ⚙, from RHI's record only (no scan): whether every Intel DLL RTX Encore uses is set up,
    /// and how many of them are the game's own copies.
    /// </summary>
    public static (bool Complete, int InGameFolders) GetEncoreIntelState(string modDir)
    {
        if (string.IsNullOrEmpty(modDir)) return (false, 0);
        var marker = Path.Combine(modDir, EncoreRuntimeRelDir, XessMarkerFile);
        if (!File.Exists(marker)) return (false, 0);
        var (privateFiles, nativeFiles) = ReadEncoreMarker(marker);
        var nativeNames = nativeFiles.Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool complete = privateFiles.Contains(EncoreLicenseName) && EncoreIntelDlls.All(f =>
            nativeNames.Contains(f.File) || (privateFiles.Contains(f.File) && File.Exists(Path.Combine(modDir, EncoreRuntimeRelDir, f.File))));
        return (complete, EncoreIntelDlls.Count(f => nativeNames.Contains(f.File)));
    }

    /// <summary>
    /// Intel's XeSS / XeLL DLLs the game ships, anywhere under its root. Skips links, RTX Encore's own runtime
    /// folders, RHI's RTX 40 MFG folder, OptiScaler folders (its files are its own) and Game Pass installs.
    /// Bounded, like RTX Encore Hub's scan, so a huge install can't stall it.
    /// </summary>
    private List<string> FindGameIntelFiles(string gameRoot)
    {
        var found = new List<string>();
        if (gameRoot.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase) || !Directory.Exists(gameRoot)) return found;
        var names = EncoreIntelDlls.Select(f => f.File).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<(string Dir, int Depth)>();
        stack.Push((gameRoot, 0));
        int visited = 0;
        while (stack.Count > 0)
        {
            var (dir, depth) = stack.Pop();
            if (++visited > EncoreScanMaxDirs)
            {
                _crashReporter.Log($"[Rtx40MfgService.FindGameIntelFiles] Stopped after {EncoreScanMaxDirs} folders in '{gameRoot}'");
                break;
            }
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "lib*.dll"))
                    if (names.Contains(Path.GetFileName(file))) found.Add(file);
                if (depth >= EncoreScanMaxDepth) continue;
                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    var info = new DirectoryInfo(sub);
                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if (info.Name.Equals("rtx-encore-runtimes", StringComparison.OrdinalIgnoreCase)
                        || info.Name.Equals(XessFolderName, StringComparison.OrdinalIgnoreCase)) continue;
                    stack.Push((sub, depth + 1));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _crashReporter.Log($"[Rtx40MfgService.FindGameIntelFiles] Skipped '{dir}' — {ex.Message}");
            }
        }
        return found.Where(p =>
        {
            var folder = Path.GetDirectoryName(p)!;
            if (File.Exists(Path.Combine(folder, "OptiScaler.ini"))) return false;
            try
            {
                var vi = System.Diagnostics.FileVersionInfo.GetVersionInfo(p);
                return (vi.CompanyName ?? "").Contains("Intel", StringComparison.OrdinalIgnoreCase);
            }
            catch (FileNotFoundException) { return false; }
        }).ToList();
    }

    private static (HashSet<string> Private, HashSet<string> Native) ReadEncoreMarker(string marker)
    {
        var privateFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nativeFiles  = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(marker)) return (privateFiles, nativeFiles);
        var known = EncoreIntelDlls.Select(f => f.File).Append(EncoreLicenseName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in File.ReadAllLines(marker))
        {
            var line = raw.Trim();
            if (line.StartsWith(EncoreNativePrefix, StringComparison.OrdinalIgnoreCase))
            {
                var rel = line[EncoreNativePrefix.Length..];
                if (known.Contains(Path.GetFileName(rel))) nativeFiles.Add(rel);
            }
            else if (known.Contains(line)) privateFiles.Add(line);
        }
        return (privateFiles, nativeFiles);
    }

    private static void WriteEncoreMarker(string marker, HashSet<string> privateFiles, HashSet<string> nativeFiles)
        => File.WriteAllLines(marker, privateFiles.Concat(nativeFiles.Select(n => EncoreNativePrefix + n)));

    private static bool SizeIs(string path, long size)
        => File.Exists(path) && new FileInfo(path).Length == size;

    private static bool HashIs(string path, string sha256)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(fs)).Equals(sha256, StringComparison.OrdinalIgnoreCase);
    }
}
