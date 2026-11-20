// BackupArchive.cs -- reads and writes .rhibackup files (a zip) and applies one to %LocalAppData%\RHI.
// No UI and no app state: callers pass the folders, so it is unit tested (BackupArchiveTests).
// Plan: docs/backup-restore-plan.md.
//
// Zip layout:
//   backup.json          BackupManifest
//   rhi/<path>           RHI data files (only DataFiles / DataFolders; anything else is ignored on restore)
//   games/<n>/<path>     a game's config files, relative to its install folder
//   nvidia.json          NVIDIA profile export (DlssPresetService.ExportProfiles), when there is one

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RenoDXCommander.Services;

public static class BackupArchive
{
    public const string Extension = ".rhibackup";
    public const string ManifestEntry = "backup.json";
    public const string NvidiaEntry = "nvidia.json";
    public const string GameLibraryFile = "game_library.json";

    /// <summary>RHI data files a backup carries (relative to %LocalAppData%\RHI).</summary>
    public static readonly string[] DataFiles =
    {
        "settings.json",
        "game_journal.json",
        "os_presets.json",
        "rtx_encore_presets.json",
        "shader_profiles.json",
        "rtx_hdr_defaults.json",
        "dldsr-captures.json",      // captured DLDSR display modes (same PC after a reformat)
        @"artwork\sgdb\chosen.json",
    };

    /// <summary>RHI data folders a backup carries, with everything in them.</summary>
    public static readonly string[] DataFolders =
    {
        "inis",
        "ReLimiter_Presets",
        "Custom",
        @"optiscaler\saved-inis",
        "optiscaler-custom",
        @"artwork\custom",
    };

    /// <summary>The parts of game_library.json that are the user's (the rest is a scan cache).</summary>
    public static readonly string[] GameLibraryKeys =
    {
        "ManualGames", "HiddenGames", "FavouriteGames", "DxvkEnabledGames", "ExcludeFromUpdateAllDxvk", "LastSelectedGame",
    };

    /// <summary>Config files a backup keeps per game, relative to the install folder (or the add-on folder).</summary>
    public static readonly string[] GameConfigNames =
    {
        "reshade.ini", "ReShade2.ini", "relimiter.ini", "DisplayCommander.ini", "OptiScaler.ini",
        @"OptiScaler\OptiScaler.ini", "HSF.ini", "dxvk.conf",
    };

    public const long MaxConfigFileBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    // ── Writing ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every RHI data file a backup would carry: (path relative to rhiDir, full path). Folders are walked;
    /// game_library.json is handled separately (only its user parts are kept).
    /// </summary>
    public static List<(string Rel, string Full)> CollectDataFiles(string rhiDir)
    {
        var list = new List<(string, string)>();
        foreach (var rel in DataFiles)
        {
            var full = Path.Combine(rhiDir, rel);
            if (File.Exists(full)) list.Add((rel, full));
        }
        foreach (var relDir in DataFolders)
        {
            var dir = Path.Combine(rhiDir, relDir);
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                // Never follow links out of RHI's folder
                if (new FileInfo(file).Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                list.Add((Path.GetRelativePath(rhiDir, file), file));
            }
        }
        return list;
    }

    /// <summary>The user parts of a game_library.json (null when the file is missing or unreadable).</summary>
    public static string? UserPartsOfGameLibrary(string rhiDir)
    {
        var path = Path.Combine(rhiDir, GameLibraryFile);
        if (!File.Exists(path)) return null;
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            if (root == null) return null;
            var kept = new JsonObject();
            foreach (var key in GameLibraryKeys)
                if (root.TryGetPropertyValue(key, out var value) && value != null)
                    kept[key] = value.DeepClone();
            return kept.ToJsonString(JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[BackupArchive.UserPartsOfGameLibrary] {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// The config files worth keeping for one game: names from GameConfigNames found in the install folder or the
    /// add-on folder, plus the ReShade preset reshade.ini points at. Paths are relative to installPath.
    /// </summary>
    public static List<string> FindGameConfigFiles(string installPath, string? addonDir = null)
    {
        var found = new List<string>();
        if (string.IsNullOrEmpty(installPath) || !Directory.Exists(installPath)) return found;
        var dirs = new List<string> { installPath };
        if (!string.IsNullOrEmpty(addonDir) && !PathsEqual(addonDir, installPath)
            && FileHelper.SafeCombine(installPath, Path.GetRelativePath(installPath, addonDir)) != null)
            dirs.Add(addonDir);

        void Add(string full)
        {
            try
            {
                var fi = new FileInfo(full);
                if (!fi.Exists || fi.Length > MaxConfigFileBytes) return;
                var rel = Path.GetRelativePath(installPath, fi.FullName);
                if (FileHelper.SafeCombine(installPath, rel) == null) return;
                if (!found.Contains(rel, StringComparer.OrdinalIgnoreCase)) found.Add(rel);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                CrashReporter.Log($"[BackupArchive.FindGameConfigFiles] '{full}' — {ex.Message}");
            }
        }

        foreach (var dir in dirs)
            foreach (var name in GameConfigNames)
                Add(Path.Combine(dir, name));

        // The ReShade preset reshade.ini uses ([GENERAL] PresetPath), when it's inside the game folder
        foreach (var dir in dirs)
        {
            var ini = Path.Combine(dir, "reshade.ini");
            var preset = ReadIniValue(ini, "GENERAL", "PresetPath");
            if (string.IsNullOrWhiteSpace(preset)) continue;
            preset = preset.Trim().Trim('"');
            var full = Path.IsPathRooted(preset) ? preset : FileHelper.SafeCombine(dir, preset);
            if (full != null && FileHelper.SafeCombine(installPath, Path.GetRelativePath(installPath, full)) != null)
                Add(full);
        }
        return found;
    }

    /// <summary>Writes the backup to zipPath (via a temp file, so a failed write never leaves half a backup).</summary>
    public static void Write(string zipPath, BackupManifest manifest, string rhiDir,
        IReadOnlyDictionary<int, string> gameInstallPaths, string? nvidiaJson)
    {
        var dir = Path.GetDirectoryName(zipPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var temp = zipPath + ".tmp";
        try
        {
            using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                AddText(zip, ManifestEntry, JsonSerializer.Serialize(manifest, JsonOptions));

                foreach (var (rel, full) in CollectDataFiles(rhiDir))
                    AddFile(zip, "rhi/" + ToEntry(rel), full);
                var lib = UserPartsOfGameLibrary(rhiDir);
                if (lib != null) AddText(zip, "rhi/" + GameLibraryFile, lib);

                for (int i = 0; i < manifest.Games.Count; i++)
                {
                    var game = manifest.Games[i];
                    if (!gameInstallPaths.TryGetValue(i, out var installPath)) continue;
                    foreach (var rel in game.ConfigFiles)
                    {
                        var full = FileHelper.SafeCombine(installPath, rel);
                        if (full != null) AddFile(zip, game.ConfigFolder + "/" + ToEntry(rel), full);
                    }
                }

                if (!string.IsNullOrEmpty(nvidiaJson)) AddText(zip, NvidiaEntry, nvidiaJson);
            }
            if (File.Exists(zipPath)) File.Delete(zipPath);
            File.Move(temp, zipPath);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (IOException ex) { CrashReporter.Log($"[BackupArchive.Write] Temp file not removed — {ex.Message}"); }
        }
    }

    /// <summary>
    /// A fingerprint of what a backup would hold now (data files by size + date, the install list, config files),
    /// so automatic backups only run when something changed.
    /// </summary>
    public static string Fingerprint(string rhiDir, BackupManifest manifest, IReadOnlyDictionary<int, string> gameInstallPaths)
    {
        var sb = new StringBuilder();
        foreach (var (rel, full) in CollectDataFiles(rhiDir).OrderBy(f => f.Rel, StringComparer.OrdinalIgnoreCase))
            AppendStamp(sb, rel, full);
        sb.Append(UserPartsOfGameLibrary(rhiDir)).Append('\n');
        var games = manifest.Games.Select(g => new { g.Name, g.Store, g.InstallPath, g.Is32Bit, g.Components, g.ConfigFiles });
        sb.Append(JsonSerializer.Serialize(games)).Append('\n');
        for (int i = 0; i < manifest.Games.Count; i++)
        {
            if (!gameInstallPaths.TryGetValue(i, out var installPath)) continue;
            foreach (var rel in manifest.Games[i].ConfigFiles)
                if (FileHelper.SafeCombine(installPath, rel) is { } full) AppendStamp(sb, rel, full);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    private static void AppendStamp(StringBuilder sb, string rel, string full)
    {
        try
        {
            var fi = new FileInfo(full);
            if (fi.Exists) sb.Append(rel).Append('|').Append(fi.Length).Append('|').Append(fi.LastWriteTimeUtc.Ticks).Append('\n');
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            sb.Append(rel).Append("|?\n");
        }
    }

    // ── Reading ──────────────────────────────────────────────────────────────────

    /// <summary>Reads backup.json. Throws InvalidDataException when the file isn't an RHI backup.</summary>
    public static BackupManifest ReadManifest(string zipPath)
    {
        BackupManifest? manifest;
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var entry = zip.GetEntry(ManifestEntry);
            if (entry == null) throw new InvalidDataException("This file isn't an RHI backup.");
            using var reader = new StreamReader(entry.Open());
            manifest = JsonSerializer.Deserialize<BackupManifest>(reader.ReadToEnd());
        }
        catch (JsonException ex) { throw new InvalidDataException("This backup is damaged.", ex); }
        catch (InvalidDataException ex) when (ex.InnerException == null && !ex.Message.StartsWith("This ", StringComparison.Ordinal))
        {
            // ZipFile's own message for a file that isn't a zip
            throw new InvalidDataException("This file isn't an RHI backup, or it's damaged.", ex);
        }
        if (manifest == null) throw new InvalidDataException("This backup is empty.");
        if (manifest.Format > BackupManifest.CurrentFormat)
            throw new InvalidDataException("This backup was made by a newer version of RHI. Update RHI and try again.");
        return manifest;
    }

    /// <summary>True when a data path from a zip may be restored (it's one of DataFiles / DataFolders or game_library.json).</summary>
    public static bool IsRestorableDataPath(string rel)
    {
        rel = rel.Replace('/', '\\').TrimStart('\\');
        if (rel.Equals(GameLibraryFile, StringComparison.OrdinalIgnoreCase)) return true;
        if (DataFiles.Any(f => f.Equals(rel, StringComparison.OrdinalIgnoreCase))) return true;
        return DataFolders.Any(d => rel.StartsWith(d + "\\", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Restores RHI's data from a backup into rhiDir and writes the restore plan + game config files into
    /// rhiDir\restore\. Existing data files are copied to rhiDir\Backups\before-restore-&lt;time&gt;\ first.
    /// Install records are never restored. Returns the plan.
    /// </summary>
    public static RestorePlan ApplyData(string zipPath, string rhiDir)
    {
        var manifest = ReadManifest(zipPath);
        var restoreDir = Path.Combine(rhiDir, "restore");
        if (Directory.Exists(restoreDir)) Directory.Delete(restoreDir, recursive: true);
        Directory.CreateDirectory(restoreDir);
        var keepDir = Path.Combine(rhiDir, "Backups", "before-restore-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));

        using (var zip = ZipFile.OpenRead(zipPath))
        {
            foreach (var entry in zip.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue; // folder entry
                var name = entry.FullName.Replace('\\', '/');
                if (name.StartsWith("rhi/", StringComparison.OrdinalIgnoreCase))
                {
                    var rel = name[4..].Replace('/', '\\');
                    if (!IsRestorableDataPath(rel)) { CrashReporter.Log($"[BackupArchive.ApplyData] Skipped '{name}' (not restorable data)"); continue; }
                    var dest = FileHelper.SafeCombine(rhiDir, rel);
                    if (dest == null) continue;
                    KeepCurrent(dest, rhiDir, keepDir);
                    if (rel.Equals(GameLibraryFile, StringComparison.OrdinalIgnoreCase))
                    {
                        using var r = new StreamReader(entry.Open());
                        MergeGameLibrary(dest, r.ReadToEnd());
                        continue;
                    }
                    Extract(entry, dest);
                }
                else if (name.StartsWith("games/", StringComparison.OrdinalIgnoreCase))
                {
                    var dest = FileHelper.SafeCombine(restoreDir, name.Replace('/', '\\'));
                    if (dest != null) Extract(entry, dest);
                }
                else if (name.Equals(NvidiaEntry, StringComparison.OrdinalIgnoreCase))
                {
                    Extract(entry, Path.Combine(restoreDir, NvidiaEntry));
                }
            }
        }

        var plan = new RestorePlan
        {
            BackupCreatedUtc = manifest.CreatedUtc,
            RhiVersion = manifest.RhiVersion,
            HasNvidia = File.Exists(Path.Combine(restoreDir, NvidiaEntry)),
            Games = manifest.Games,
        };
        SavePlan(rhiDir, plan);
        CrashReporter.Log($"[BackupArchive.ApplyData] Restored data from '{zipPath}' ({manifest.Games.Count} games, made {manifest.CreatedUtc:u} by RHI {manifest.RhiVersion})");
        return plan;
    }

    public static string PlanPath(string rhiDir) => Path.Combine(rhiDir, "restore", "plan.json");

    public static void SavePlan(string rhiDir, RestorePlan plan)
    {
        var path = PlanPath(rhiDir);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(plan, JsonOptions));
    }

    public static RestorePlan? LoadPlan(string rhiDir)
    {
        var path = PlanPath(rhiDir);
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<RestorePlan>(File.ReadAllText(path)); }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            CrashReporter.Log($"[BackupArchive.LoadPlan] {ex.Message}");
            return null;
        }
    }

    /// <summary>Removes the restore folder (plan, config files, NVIDIA export).</summary>
    public static void ClearRestore(string rhiDir)
    {
        var dir = Path.Combine(rhiDir, "restore");
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[BackupArchive.ClearRestore] {ex.Message}");
        }
    }

    /// <summary>
    /// Puts the user parts of a backed-up game_library.json into the current one (or writes them as a new file).
    /// The current file's scan cache stays.
    /// </summary>
    public static void MergeGameLibrary(string path, string backedUpParts)
    {
        var parts = JsonNode.Parse(backedUpParts) as JsonObject ?? new JsonObject();
        JsonObject root;
        try { root = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject() : new JsonObject(); }
        catch (JsonException) { root = new JsonObject(); }
        foreach (var key in GameLibraryKeys)
            if (parts.TryGetPropertyValue(key, out var value) && value != null)
                root[key] = value.DeepClone();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, root.ToJsonString(JsonOptions));
    }

    /// <summary>
    /// Copies a game's backed-up config files from rhiDir\restore\ into its install folder, except files that
    /// existed before the restore started (preExisting): those are the user's current files and are kept.
    /// Returns how many were written.
    /// </summary>
    public static int RestoreGameConfigs(string rhiDir, BackupGame game, string installPath, ISet<string> preExisting)
    {
        int written = 0;
        foreach (var rel in game.ConfigFiles)
        {
            var src = FileHelper.SafeCombine(Path.Combine(rhiDir, "restore"), Path.Combine(game.ConfigFolder.Replace('/', '\\'), rel));
            var dest = FileHelper.SafeCombine(installPath, rel);
            if (src == null || dest == null || !File.Exists(src)) continue;
            if (preExisting.Contains(rel)) { CrashReporter.Log($"[BackupArchive.RestoreGameConfigs] '{game.Name}': kept the existing {rel}"); continue; }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(src, dest, overwrite: true);
                written++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                CrashReporter.Log($"[BackupArchive.RestoreGameConfigs] '{game.Name}': {rel} — {ex.Message}");
            }
        }
        return written;
    }

    /// <summary>Which of a game's backed-up config files exist in its folder right now.</summary>
    public static HashSet<string> ExistingConfigs(BackupGame game, string installPath)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rel in game.ConfigFiles)
            if (FileHelper.SafeCombine(installPath, rel) is { } full && File.Exists(full)) set.Add(rel);
        return set;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static string ToEntry(string rel) => rel.Replace('\\', '/');

    private static void AddText(ZipArchive zip, string entryName, string text)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        w.Write(text);
    }

    private static void AddFile(ZipArchive zip, string entryName, string full)
    {
        try
        {
            // FileShare.ReadWrite: settings.json and inis may be open in RHI or a running game
            using var src = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            entry.LastWriteTime = File.GetLastWriteTime(full);
            using var dst = entry.Open();
            src.CopyTo(dst);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[BackupArchive.AddFile] Skipped '{full}' — {ex.Message}");
        }
    }

    private static void Extract(ZipArchiveEntry entry, string dest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        entry.ExtractToFile(dest, overwrite: true);
    }

    private static void KeepCurrent(string dest, string rhiDir, string keepDir)
    {
        if (!File.Exists(dest)) return;
        try
        {
            var copy = Path.Combine(keepDir, Path.GetRelativePath(rhiDir, dest));
            if (File.Exists(copy)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(dest, copy);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[BackupArchive.KeepCurrent] '{dest}' — {ex.Message}");
        }
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads one key from an ini section (line based, first match).</summary>
    internal static string? ReadIniValue(string iniPath, string section, string key)
    {
        try
        {
            if (!File.Exists(iniPath) || new FileInfo(iniPath).Length > MaxConfigFileBytes) return null;
            bool inSection = false;
            foreach (var raw in File.ReadLines(iniPath))
            {
                var line = raw.Trim();
                if (line.StartsWith('['))
                {
                    inSection = line.Equals("[" + section + "]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (!inSection) continue;
                int eq = line.IndexOf('=');
                if (eq > 0 && line[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                    return line[(eq + 1)..].Trim();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[BackupArchive.ReadIniValue] '{iniPath}' — {ex.Message}");
        }
        return null;
    }
}
