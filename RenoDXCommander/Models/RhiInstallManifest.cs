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
    /// The Neural Rendering method currently installed in this game folder, if any.
    /// Kept for backwards compatibility — use SharedFiles for ownership tracking.
    /// </summary>
    [JsonPropertyName("nrMethod")]
    public string? NrMethod { get; set; }

    // ── Static helpers ──────────────────────────────────────────────────────

    /// <summary>Filename written inside the game folder.</summary>
    public const string FileName = "rhi_install.txt";

    private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

    /// <summary>
    /// Writes the manifest as <c>rhi_install.txt</c> inside <paramref name="gameDir"/>.
    /// Overwrites any existing file (used by both fresh installs and updates).
    /// </summary>
    public static void Write(string gameDir, RhiInstallManifest manifest)
    {
        try
        {
            var path = Path.Combine(gameDir, FileName);
            File.WriteAllText(path, JsonSerializer.Serialize(manifest, _opts));
            CrashReporter.Log($"[RhiInstallManifest.Write] Written for {manifest.Component} {manifest.Version} ({manifest.Variant}) → {gameDir}");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[RhiInstallManifest.Write] Failed in '{gameDir}' — {ex.Message}");
        }
    }

    /// <summary>
    /// Reads <c>rhi_install.txt</c> from <paramref name="gameDir"/>.
    /// Returns <c>null</c> if the file does not exist or cannot be parsed.
    /// </summary>
    public static RhiInstallManifest? Read(string gameDir)
    {
        try
        {
            var path = Path.Combine(gameDir, FileName);
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<RhiInstallManifest>(File.ReadAllText(path), _opts);
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[RhiInstallManifest.Read] Failed in '{gameDir}' — {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Deletes <c>rhi_install.txt</c> from <paramref name="gameDir"/> during uninstall.
    /// No-op if the file is already absent.
    /// </summary>
    public static void Delete(string gameDir)
    {
        try
        {
            var path = Path.Combine(gameDir, FileName);
            if (File.Exists(path))
            {
                File.Delete(path);
                CrashReporter.Log($"[RhiInstallManifest.Delete] Removed from {gameDir}");
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[RhiInstallManifest.Delete] Failed in '{gameDir}' — {ex.Message}");
        }
    }

    /// <summary>
    /// Updates just the <c>nrMethod</c> field in an existing <c>rhi_install.txt</c>.
    /// Pass <c>null</c> to clear it (NR uninstalled). No-op if no manifest exists.
    /// </summary>
    public static void SetNrMethod(string gameDir, string? nrMethod)
    {
        try
        {
            var manifest = Read(gameDir);
            if (manifest == null) return;
            manifest.NrMethod = nrMethod;
            Write(gameDir, manifest);
            CrashReporter.Log($"[RhiInstallManifest.SetNrMethod] NrMethod={nrMethod ?? "null"} in {gameDir}");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[RhiInstallManifest.SetNrMethod] Failed in '{gameDir}' — {ex.Message}");
        }
    }

    /// <summary>
    /// Registers <paramref name="component"/> as an owner of <paramref name="fileName"/>
    /// in the shared files tracking dict. Creates the manifest if it doesn't exist yet.
    /// Call this when a component deploys a shared file (e.g. nvngx_dlss.dll).
    /// </summary>
    public static void AddSharedFileOwner(string gameDir, string fileName, string component)
    {
        try
        {
            // Read existing manifest, or create a minimal one to hold sharedFiles
            var manifest = Read(gameDir) ?? new RhiInstallManifest { Component = "RHI" };
            if (!manifest.SharedFiles.TryGetValue(fileName, out var owners))
            {
                owners = new List<string>();
                manifest.SharedFiles[fileName] = owners;
            }
            if (!owners.Contains(component, StringComparer.OrdinalIgnoreCase))
                owners.Add(component);
            Write(gameDir, manifest);
            CrashReporter.Log($"[RhiInstallManifest] SharedFile '{fileName}' owner added: {component}");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[RhiInstallManifest.AddSharedFileOwner] Failed — {ex.Message}");
        }
    }

    /// <summary>
    /// Removes <paramref name="component"/> from the owner list of <paramref name="fileName"/>.
    /// Returns <c>true</c> if the file now has NO remaining owners and is safe to delete/restore.
    /// Returns <c>false</c> if other components still own it (leave it alone).
    /// </summary>
    public static bool RemoveSharedFileOwner(string gameDir, string fileName, string component)
    {
        try
        {
            var manifest = Read(gameDir);
            if (manifest == null) return true; // no manifest — assume safe to delete
            if (!manifest.SharedFiles.TryGetValue(fileName, out var owners))
                return true; // no ownership tracking for this file — safe to delete

            owners.RemoveAll(o => o.Equals(component, StringComparison.OrdinalIgnoreCase));

            if (owners.Count == 0)
                manifest.SharedFiles.Remove(fileName);

            Write(gameDir, manifest);

            bool safeToDelete = owners.Count == 0;
            if (!safeToDelete)
                CrashReporter.Log($"[RhiInstallManifest] SharedFile '{fileName}': {component} removed, still owned by [{string.Join(", ", owners)}] — skip delete");
            else
                CrashReporter.Log($"[RhiInstallManifest] SharedFile '{fileName}': {component} was last owner — safe to delete");
            return safeToDelete;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[RhiInstallManifest.RemoveSharedFileOwner] Failed — {ex.Message}");
            return true; // on error, default to allowing delete
        }
    }
    /// has been renamed on disk (e.g. via DLL naming overrides).
    /// Also renames the <c>&lt;oldDllName&gt;.original</c> sentinel file to
    /// <c>&lt;newDllName&gt;.original</c> so uninstall can still locate and restore it.
    /// No-op if no manifest file exists.
    /// </summary>
    public static void UpdateInstalledAs(string gameDir, string oldDllName, string newDllName)
    {
        if (oldDllName.Equals(newDllName, StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            // Update the manifest file
            var manifest = Read(gameDir);
            if (manifest != null)
            {
                manifest.InstalledAs = newDllName;
                // Also update the Files list — replace the old name with the new one
                var idx = manifest.Files.FindIndex(f => f.Equals(oldDllName, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                    manifest.Files[idx] = newDllName;
                Write(gameDir, manifest);
                CrashReporter.Log($"[RhiInstallManifest.UpdateInstalledAs] Updated installedAs '{oldDllName}' → '{newDllName}' in {gameDir}");
            }

            // Rename the .original sentinel file so uninstall can still find it
            var oldSentinel = Path.Combine(gameDir, oldDllName + ".original");
            var newSentinel = Path.Combine(gameDir, newDllName + ".original");
            if (File.Exists(oldSentinel) && !File.Exists(newSentinel))
            {
                File.Move(oldSentinel, newSentinel);
                CrashReporter.Log($"[RhiInstallManifest.UpdateInstalledAs] Renamed sentinel '{oldDllName}.original' → '{newDllName}.original'");
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[RhiInstallManifest.UpdateInstalledAs] Failed in '{gameDir}' — {ex.Message}");
        }
    }
}
