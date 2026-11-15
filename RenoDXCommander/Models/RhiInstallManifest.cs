using System.Text.Json;
using System.Text.Json.Serialization;
using RenoDXCommander.Services;

namespace RenoDXCommander.Models;

/// <summary>
/// Written to the game folder as <c>rhi_install.txt</c> alongside every OptiScaler install.
/// Records the exact version, variant, and file/folder list that was deployed so that:
/// <list type="bullet">
///   <item>RHI always knows the correct installed version at startup (not the current staging version)</item>
///   <item>Uninstall always removes the correct set of files regardless of whether the staging folder is present or has changed</item>
/// </list>
/// </summary>
public class RhiInstallManifest
{
    /// <summary>Human-readable component name, e.g. "OptiScaler".</summary>
    [JsonPropertyName("component")]
    public string Component { get; set; } = "";

    /// <summary>Variant that was installed: "Stable", "Nightly", or "DlssNr".</summary>
    [JsonPropertyName("variant")]
    public string Variant { get; set; } = "Stable";

    /// <summary>
    /// Version string at install time — the GitHub release tag (stable) or date string (nightly).
    /// E.g. "v0.9.4", "20260813".
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    /// <summary>The filename used for the main OptiScaler DLL (e.g. "dxgi.dll", "winmm.dll").</summary>
    [JsonPropertyName("installedAs")]
    public string InstalledAs { get; set; } = "";

    /// <summary>UTC timestamp of the install/update.</summary>
    [JsonPropertyName("installedAt")]
    public DateTime InstalledAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// All root-level files deployed to the game folder (excluding <c>rhi_install.txt</c> itself).
    /// Used by uninstall to know exactly which files to remove/restore.
    /// </summary>
    [JsonPropertyName("files")]
    public List<string> Files { get; set; } = [];

    /// <summary>
    /// Subdirectory names deployed inside the game folder (e.g. "D3D12_OptiScaler", "OptiScaler").
    /// Used by uninstall to know which dirs to remove.
    /// </summary>
    [JsonPropertyName("folders")]
    public List<string> Folders { get; set; } = [];

    /// <summary>
    /// Tracks which RHI components have deployed each shared file (e.g. nvngx_dlss.dll, nvngx_dlssnr.dll).
    /// Key = filename, Value = list of component names ("OptiScaler", "ShortFuse", "Dlss5Tool", etc.)
    /// A file is only deleted on uninstall when the uninstalling component is the LAST owner.
    /// This prevents OptiScaler uninstall from deleting dlss files that ShortFuse NR still needs,
    /// and vice versa.
    /// </summary>
    [JsonPropertyName("sharedFiles")]
    public Dictionary<string, List<string>> SharedFiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Per-component file lists. Each NR component (ShortFuse, Dlss5Tool, Bridge, Feeder)
    /// records every file it deployed here. Provides redundancy alongside sentinels —
    /// uninstall can cross-reference this list to clean up reliably even if sentinels fail.
    /// Key = component name; Value = list of filenames relative to the game folder.
    /// </summary>
    [JsonPropertyName("components")]
    public Dictionary<string, RhiComponentRecord> Components { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The Neural Rendering method currently installed in this game folder, if any.
    /// Kept for backwards compatibility — use SharedFiles for ownership tracking.
    /// </summary>
    [JsonPropertyName("nrMethod")]
    public string? NrMethod { get; set; }

    /// <summary>
    /// Which build of renodx-dlss5.addon64 the DLSS5 Tool deployed: <see cref="Dlss5BuildFoveated"/>
    /// or null for the stock add-on. The stock auto-update skips folders with a Foveated build.
    /// </summary>
    [JsonPropertyName("dlss5Build")]
    public string? Dlss5Build { get; set; }

    /// <summary>Release tag of the deployed Foveated build (e.g. "v0.4.0").</summary>
    [JsonPropertyName("dlss5BuildVersion")]
    public string? Dlss5BuildVersion { get; set; }

    /// <summary>True when the user picked a specific Foveated version (no auto-update).</summary>
    [JsonPropertyName("dlss5BuildPinned")]
    public bool Dlss5BuildPinned { get; set; }

    public const string Dlss5BuildFoveated = "Foveated";

    /// <summary>True when this folder's DLSS5 Tool add-on is the Foveated build.</summary>
    [JsonIgnore]
    public bool IsFoveatedDlss5 =>
        string.Equals(Dlss5Build, Dlss5BuildFoveated, StringComparison.OrdinalIgnoreCase);

    // ── Static helpers ──────────────────────────────────────────────────────
    //
    // Several components (OptiScaler, the Neural Rendering methods, RTX Encore, HSF, Uplift...) share this
    // one file, sometimes from background threads. Every change is read → modify → write under a per-file
    // lock (SafeJsonFile.LockFor), written to a .tmp file and swapped in, so a crash can't leave half a file.
    // A file that exists but can't be read is never treated as absent: writers leave it alone, because a
    // fresh manifest written over it would drop every other component's data.
    // (SafeJsonFile's own Read/Write aren't used: they keep a .bak beside the file and restore it when the
    // file is missing, which would bring back a manifest that uninstall or a folder move deleted.)

    /// <summary>Filename written inside the game folder.</summary>
    public const string FileName = "rhi_install.txt";

    private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

    /// <summary>Result of <see cref="Read(string, out ReadStatus)"/>.</summary>
    public enum ReadStatus
    {
        /// <summary>Read and parsed.</summary>
        Ok,
        /// <summary>No file.</summary>
        Missing,
        /// <summary>The file exists but couldn't be read (locked, access denied). Writers must not replace it.</summary>
        Unreadable,
        /// <summary>The file was read but isn't valid JSON.</summary>
        Corrupt,
    }

    /// <summary>True when nothing is recorded (no OptiScaler data, shared files, components, NR method or DLSS5 build).</summary>
    [JsonIgnore]
    public bool IsEmpty =>
        string.IsNullOrEmpty(InstalledAs)
        && Files.Count == 0
        && Folders.Count == 0
        && SharedFiles.Count == 0
        && Components.Count == 0
        && NrMethod == null
        && Dlss5Build == null;

    private static string PathIn(string gameDir) => Path.Combine(gameDir, FileName);

    private static object LockFor(string gameDir) => SafeJsonFile.LockFor(PathIn(gameDir));

    /// <summary>
    /// Writes the manifest as <c>rhi_install.txt</c> inside <paramref name="gameDir"/>, replacing the file.
    /// Prefer <see cref="Update"/>, which keeps the other components' data.
    /// </summary>
    public static void Write(string gameDir, RhiInstallManifest manifest)
    {
        if (string.IsNullOrEmpty(gameDir)) return;
        lock (LockFor(gameDir))
        {
            if (WriteAtomic(gameDir, manifest, "RhiInstallManifest.Write"))
                CrashReporter.Log($"[RhiInstallManifest.Write] Written for {manifest.Component} {manifest.Version} ({manifest.Variant}) → {gameDir}");
        }
    }

    /// <summary>Writes to a .tmp file, flushes it, then moves it over the real file. Caller holds the lock.</summary>
    private static bool WriteAtomic(string gameDir, RhiInstallManifest manifest, string caller)
    {
        var path = PathIn(gameDir);
        var tmp  = path + ".tmp";
        for (int attempt = 0; ; attempt++)
        try
        {
            var json = JsonSerializer.Serialize(manifest, _opts);
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var w = new StreamWriter(fs))
            {
                w.Write(json);
                w.Flush();
                fs.Flush(flushToDisk: true);
            }
            File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch (IOException ex) when (attempt < 2 && ex is not DirectoryNotFoundException)
        {
            // Briefly held open by something else (antivirus, indexer)
            CrashReporter.Log($"[{caller}] Writing {FileName} attempt {attempt + 1} failed in '{gameDir}', retrying — {ex.Message}");
            Thread.Sleep(50 * (attempt + 1));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            CrashReporter.Log($"[{caller}] Writing {FileName} failed in '{gameDir}' — {ex.GetType().Name}: {ex.Message}");
            try { if (File.Exists(tmp)) File.Delete(tmp); }
            catch (Exception cleanupEx) when (cleanupEx is IOException or UnauthorizedAccessException)
            {
                CrashReporter.Log($"[{caller}] Couldn't remove {FileName}.tmp in '{gameDir}' — {cleanupEx.Message}");
            }
            return false;
        }
    }

    /// <summary>
    /// Reads <c>rhi_install.txt</c> from <paramref name="gameDir"/>.
    /// Returns <c>null</c> if the file does not exist or cannot be read or parsed.
    /// Use <see cref="Read(string, out ReadStatus)"/> when "can't read" must not be mistaken for "absent".
    /// </summary>
    public static RhiInstallManifest? Read(string gameDir) => Read(gameDir, out _);

    /// <summary>Reads <c>rhi_install.txt</c>; <paramref name="status"/> says why it returned null.</summary>
    public static RhiInstallManifest? Read(string gameDir, out ReadStatus status)
    {
        if (string.IsNullOrEmpty(gameDir)) { status = ReadStatus.Missing; return null; }
        var path = PathIn(gameDir);
        lock (LockFor(gameDir))
        {
            if (!File.Exists(path)) { status = ReadStatus.Missing; return null; }

            string json;
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    json = File.ReadAllText(path);
                    break;
                }
                catch (IOException ex) when (attempt < 2)
                {
                    CrashReporter.Log($"[RhiInstallManifest.Read] Read attempt {attempt + 1} failed in '{gameDir}', retrying — {ex.Message}");
                    Thread.Sleep(50 * (attempt + 1));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    CrashReporter.Log($"[RhiInstallManifest.Read] Unreadable in '{gameDir}' — {ex.GetType().Name}: {ex.Message}");
                    status = ReadStatus.Unreadable;
                    return null;
                }
            }

            try
            {
                var manifest = JsonSerializer.Deserialize<RhiInstallManifest>(json, _opts);
                if (manifest != null) { status = ReadStatus.Ok; return manifest; }
            }
            catch (JsonException ex)
            {
                CrashReporter.Log($"[RhiInstallManifest.Read] Corrupt in '{gameDir}' ({json.Length} chars) — {ex.Message}");
            }
            status = ReadStatus.Corrupt;
            return null;
        }
    }

    /// <summary>
    /// Read → change → write under the file's lock. <paramref name="change"/> gets the current manifest
    /// (null when there is none, or it was corrupt) and returns the manifest to save, or null to leave the
    /// file as it is. When the file exists but can't be read, nothing is changed and this returns false.
    /// With <paramref name="deleteIfEmpty"/>, a result with nothing left in it deletes the file.
    /// Returns false when the file couldn't be read or written.
    /// </summary>
    public static bool Update(string gameDir, Func<RhiInstallManifest?, RhiInstallManifest?> change, string caller, bool deleteIfEmpty = false)
    {
        if (string.IsNullOrEmpty(gameDir)) return false;
        try
        {
            lock (LockFor(gameDir))
            {
                var current = Read(gameDir, out var status);
                if (status == ReadStatus.Unreadable)
                {
                    CrashReporter.Log($"[{caller}] {FileName} in '{gameDir}' couldn't be read — left unchanged so other components' data isn't lost");
                    return false;
                }
                if (status == ReadStatus.Corrupt)
                    CrashReporter.Log($"[{caller}] {FileName} in '{gameDir}' is corrupt — replacing it");

                var next = change(current);
                if (next == null) return true;
                if (deleteIfEmpty && next.IsEmpty) return DeleteCore(gameDir, caller);
                return WriteAtomic(gameDir, next, caller);
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[{caller}] Updating {FileName} failed in '{gameDir}' — {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Deletes <c>rhi_install.txt</c> from <paramref name="gameDir"/>.
    /// No-op if the file is already absent.
    /// </summary>
    public static void Delete(string gameDir)
    {
        if (string.IsNullOrEmpty(gameDir)) return;
        lock (LockFor(gameDir)) DeleteCore(gameDir, "RhiInstallManifest.Delete");
    }

    private static bool DeleteCore(string gameDir, string caller)
    {
        try
        {
            var path = PathIn(gameDir);
            if (File.Exists(path))
            {
                File.Delete(path);
                CrashReporter.Log($"[{caller}] Removed {FileName} from {gameDir}");
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[{caller}] Deleting {FileName} failed in '{gameDir}' — {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Records the file list for a component in <c>rhi_install.txt</c>.
    /// Creates a minimal manifest if none exists. Updates an existing one.
    /// </summary>
    public static void SetComponent(string gameDir, string component, IEnumerable<string> files)
    {
        var list = files.ToList();
        if (Update(gameDir, m =>
            {
                m ??= new RhiInstallManifest { Component = "RHI" };
                m.Components[component] = new RhiComponentRecord { Files = list };
                return m;
            }, "RhiInstallManifest.SetComponent"))
            CrashReporter.Log($"[RhiInstallManifest.SetComponent] Recorded {list.Count} file(s) for {component} in {gameDir}");
    }

    /// <summary>
    /// Returns the file list for a component, or an empty list if not recorded.
    /// </summary>
    public static IReadOnlyList<string> GetComponentFiles(string gameDir, string component)
    {
        // An unreadable rhi_install.txt reads as "no record"; called per game in update loops
        var manifest = Read(gameDir);
        if (manifest != null && manifest.Components.TryGetValue(component, out var rec))
            return rec.Files.AsReadOnly();
        return Array.Empty<string>();
    }

    /// <summary>
    /// Removes a component's record from <c>rhi_install.txt</c>.
    /// No-op if the manifest or component entry doesn't exist.
    /// </summary>
    public static void RemoveComponent(string gameDir, string component)
    {
        bool removed = false;
        // A manifest with nothing left in it (no OptiScaler data, no shared files, no other
        // components, no NR method) is deleted rather than left as a hollow shell.
        Update(gameDir, m =>
        {
            if (m == null || !m.Components.Remove(component)) return null;
            removed = true;
            // The DLSS5 Tool add-on is gone, so its build record goes with it
            if (component.Equals("Dlss5Tool", StringComparison.OrdinalIgnoreCase))
            {
                m.Dlss5Build = null;
                m.Dlss5BuildVersion = null;
                m.Dlss5BuildPinned = false;
            }
            return m;
        }, "RhiInstallManifest.RemoveComponent", deleteIfEmpty: true);
        if (removed)
            CrashReporter.Log($"[RhiInstallManifest.RemoveComponent] Removed {component} record from {gameDir}");
    }

    /// <summary>
    /// Records which DLSS5 Tool build is in the folder. Pass <paramref name="foveated"/> = false to
    /// mark it as the stock add-on (clears the Foveated fields). Creates the manifest if needed.
    /// </summary>
    public static void SetDlss5Build(string gameDir, bool foveated, string? version = null, bool pinned = false)
    {
        if (Update(gameDir, m =>
            {
                if (m == null)
                {
                    if (!foveated) return null;
                    m = new RhiInstallManifest { Component = "RHI" };
                }
                m.Dlss5Build        = foveated ? Dlss5BuildFoveated : null;
                m.Dlss5BuildVersion = foveated ? version : null;
                m.Dlss5BuildPinned  = foveated && pinned;
                return m;
            }, "RhiInstallManifest.SetDlss5Build"))
            CrashReporter.Log($"[RhiInstallManifest.SetDlss5Build] {(foveated ? $"Foveated {version ?? "?"}{(pinned ? " (pinned)" : "")}" : "Stock")} in {gameDir}");
    }

    /// <summary>
    /// Updates just the <c>nrMethod</c> field in an existing <c>rhi_install.txt</c>.
    /// Pass <c>null</c> to clear it (NR uninstalled); a manifest with nothing else in it is then deleted.
    /// No-op if no manifest exists.
    /// </summary>
    public static void SetNrMethod(string gameDir, string? nrMethod)
    {
        bool changed = false;
        Update(gameDir, m =>
        {
            if (m == null) return null;
            m.NrMethod = nrMethod;
            changed = true;
            return m;
        }, "RhiInstallManifest.SetNrMethod", deleteIfEmpty: true);
        if (changed)
            CrashReporter.Log($"[RhiInstallManifest.SetNrMethod] NrMethod={nrMethod ?? "null"} in {gameDir}");
    }

    /// <summary>
    /// Registers <paramref name="component"/> as an owner of <paramref name="fileName"/>
    /// in the shared files tracking dict. Creates the manifest if it doesn't exist yet.
    /// Call this when a component deploys a shared file (e.g. nvngx_dlss.dll).
    /// </summary>
    public static void AddSharedFileOwner(string gameDir, string fileName, string component)
    {
        if (Update(gameDir, m =>
            {
                // Existing manifest, or a minimal one to hold sharedFiles
                m ??= new RhiInstallManifest { Component = "RHI" };
                if (!m.SharedFiles.TryGetValue(fileName, out var owners))
                {
                    owners = new List<string>();
                    m.SharedFiles[fileName] = owners;
                }
                if (!owners.Contains(component, StringComparer.OrdinalIgnoreCase))
                    owners.Add(component);
                return m;
            }, "RhiInstallManifest.AddSharedFileOwner"))
            CrashReporter.Log($"[RhiInstallManifest] SharedFile '{fileName}' owner added: {component}");
    }

    /// <summary>The components that own <paramref name="fileName"/> (empty when untracked).</summary>
    public static IReadOnlyList<string> GetSharedFileOwners(string gameDir, string fileName)
    {
        var manifest = Read(gameDir);
        if (manifest != null && manifest.SharedFiles.TryGetValue(fileName, out var owners))
            return owners.ToList();
        return Array.Empty<string>();
    }

    /// <summary>
    /// Removes <paramref name="component"/> from the owner list of <paramref name="fileName"/>.
    /// Returns <c>true</c> if the file now has NO remaining owners (or isn't tracked at all), so the
    /// caller may release it — callers still go through the <c>.original</c> sentinel to decide what to do.
    /// Returns <c>false</c> if other components still own it, or if <c>rhi_install.txt</c> couldn't be
    /// read or written (leave the file alone).
    /// </summary>
    public static bool RemoveSharedFileOwner(string gameDir, string fileName, string component)
    {
        if (string.IsNullOrEmpty(gameDir)) return false;
        try
        {
            lock (LockFor(gameDir))
            {
                var manifest = Read(gameDir, out var status);
                if (status is ReadStatus.Unreadable or ReadStatus.Corrupt)
                {
                    CrashReporter.Log($"[RhiInstallManifest.RemoveSharedFileOwner] {FileName} in '{gameDir}' is {status} — keeping '{fileName}'");
                    return false;
                }
                if (manifest == null) return true; // no manifest — nothing tracks this file
                if (!manifest.SharedFiles.TryGetValue(fileName, out var owners))
                    return true; // no ownership tracking for this file

                owners.RemoveAll(o => o.Equals(component, StringComparison.OrdinalIgnoreCase));
                if (owners.Count == 0)
                    manifest.SharedFiles.Remove(fileName);

                if (!WriteAtomic(gameDir, manifest, "RhiInstallManifest.RemoveSharedFileOwner"))
                    return false;

                bool safeToDelete = owners.Count == 0;
                if (!safeToDelete)
                    CrashReporter.Log($"[RhiInstallManifest] SharedFile '{fileName}': {component} removed, still owned by [{string.Join(", ", owners)}] — skip delete");
                else
                    CrashReporter.Log($"[RhiInstallManifest] SharedFile '{fileName}': {component} was last owner — safe to delete");
                return safeToDelete;
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[RhiInstallManifest.RemoveSharedFileOwner] Failed — {ex.Message}");
            return false; // on error, keep the file
        }
    }

    /// <summary>
    /// Updates <c>installedAs</c> after the OptiScaler DLL
    /// has been renamed on disk (e.g. via DLL naming overrides).
    /// Also renames the <c>&lt;oldDllName&gt;.original</c> sentinel file to
    /// <c>&lt;newDllName&gt;.original</c> so uninstall can still locate and restore it.
    /// No-op if no manifest file exists.
    /// </summary>
    public static void UpdateInstalledAs(string gameDir, string oldDllName, string newDllName)
    {
        if (oldDllName.Equals(newDllName, StringComparison.OrdinalIgnoreCase)) return;
        bool updated = false;
        Update(gameDir, m =>
        {
            if (m == null) return null;
            m.InstalledAs = newDllName;
            // Also update the Files list — replace the old name with the new one
            var idx = m.Files.FindIndex(f => f.Equals(oldDllName, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
                m.Files[idx] = newDllName;
            updated = true;
            return m;
        }, "RhiInstallManifest.UpdateInstalledAs");
        if (updated)
            CrashReporter.Log($"[RhiInstallManifest.UpdateInstalledAs] Updated installedAs '{oldDllName}' → '{newDllName}' in {gameDir}");

        // Rename the .original sentinel file so uninstall can still find it
        try
        {
            var oldSentinel = Path.Combine(gameDir, oldDllName + ".original");
            var newSentinel = Path.Combine(gameDir, newDllName + ".original");
            if (File.Exists(oldSentinel) && !File.Exists(newSentinel))
            {
                File.Move(oldSentinel, newSentinel);
                CrashReporter.Log($"[RhiInstallManifest.UpdateInstalledAs] Renamed sentinel '{oldDllName}.original' → '{newDllName}.original'");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[RhiInstallManifest.UpdateInstalledAs] Sentinel rename failed in '{gameDir}' — {ex.Message}");
        }
    }
}

/// <summary>
/// Record for a single NR component's install state inside <see cref="RhiInstallManifest.Components"/>.
/// </summary>
public class RhiComponentRecord
{
    /// <summary>
    /// All files this component deployed to the game folder, relative to the install root.
    /// Paths that live in a subdirectory are stored with a forward slash separator
    /// (e.g. "host64/nvngx_dlssnr.dll"). Files in the reshade-shaders tree use the
    /// conventional relative path (e.g. "reshade-shaders/Shaders/DLSS5_Feed.fx").
    /// </summary>
    [JsonPropertyName("files")]
    public List<string> Files { get; set; } = [];
}
