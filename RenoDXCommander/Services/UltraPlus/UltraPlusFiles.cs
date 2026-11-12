// UltraPlusFiles.cs — finds a game's Ultra+ files and reads / writes them safely (backup once, atomic replace,
// config_modified flag for Ultra+'s live reload, retries while the game holds the file). Text rules: UltraPlusConfig.
// Plan: docs/ultraplus-overlay-plan.md.

namespace RenoDXCommander.Services.UltraPlus;

/// <summary>Everything the overlay needs about one game's Ultra+ install, read at one moment.</summary>
public sealed record UltraPlusState(
    string DataDir,
    string ConfigPath,
    string? Version,
    IReadOnlyList<UltraPlusSetting> Settings,
    IReadOnlyDictionary<string, string> Values,
    IReadOnlyDictionary<string, string> Defaults,
    IReadOnlyList<UltraPlusPreset> Presets)
{
    /// <summary>The setting's current value as shown (file value, else the schema default), normalised.</summary>
    public string ValueOf(UltraPlusSetting s) =>
        UltraPlusConfig.Normalize(s, Values.TryGetValue(s.Key, out var v) ? v : s.Default);

    /// <summary>The value Restore defaults uses: UltraPlusConfig.default, else the schema default.</summary>
    public string DefaultOf(UltraPlusSetting s) =>
        UltraPlusConfig.Normalize(s, Defaults.TryGetValue(s.Key, out var v) ? v : s.Default);

    public bool AutoApplyOn => Values.TryGetValue(UltraPlusConfig.AutoApplyKey, out var v)
        && v.Equals("on", StringComparison.OrdinalIgnoreCase);
}

public static class UltraPlusFiles
{
    public const string BackupSuffix = ".rhi-backup";

    /// <summary>
    /// The Ultra+ config folder (schema, presets, defaults) and the UltraPlusConfig.ini in use for a game folder, or null
    /// when the game has no Ultra+. Looks in the UE4SS folder beside the exe (ue4ss\Mods\… or Mods\…) and, for legacy
    /// installs, uses an UltraPlusConfig.ini next to the exe with the UE4SS folder's other files.
    /// </summary>
    public static (string DataDir, string ConfigPath)? Find(string? installPath)
    {
        if (string.IsNullOrEmpty(installPath) || !Directory.Exists(installPath)) return null;
        try
        {
            string? dataDir = null;
            foreach (var root in new[] { Path.Combine(installPath, "ue4ss"), installPath })
            {
                var dir = Path.Combine(new[] { root }.Concat(UltraPlusConfig.ConfigRelativeSegments).ToArray());
                if (File.Exists(Path.Combine(dir, UltraPlusConfig.SchemaFileName))
                    || File.Exists(Path.Combine(dir, UltraPlusConfig.ConfigFileName)))
                { dataDir = dir; break; }
            }
            if (dataDir == null) return null;

            var config = Path.Combine(dataDir, UltraPlusConfig.ConfigFileName);
            if (!File.Exists(config))
            {
                var legacy = Path.Combine(installPath, UltraPlusConfig.ConfigFileName);
                if (File.Exists(legacy)) config = legacy;
            }
            return File.Exists(config) ? (dataDir, config) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            CrashReporter.Log($"[UltraPlusFiles.Find] '{installPath}' — {ex.Message}");
            return null;
        }
    }

    /// <summary>Reads schema, values, defaults, version and presets. Null when the game has no Ultra+ or nothing could be read.</summary>
    public static UltraPlusState? Load(string? installPath)
    {
        if (Find(installPath) is not { } where) return null;
        try
        {
            var schema = UltraPlusConfig.ParseSchema(ReadShared(Path.Combine(where.DataDir, UltraPlusConfig.SchemaFileName)));
            var values = UltraPlusConfig.ParseValues(ReadShared(where.ConfigPath));
            var defaults = UltraPlusConfig.ParseValues(ReadShared(Path.Combine(where.DataDir, UltraPlusConfig.DefaultsFileName)));
            var version = ReadShared(Path.Combine(where.DataDir, UltraPlusConfig.VersionFileName))?.Trim();

            // Older Ultra+ without a schema: show the settings in the file as plain text values
            if (schema.Count == 0)
                schema = values.Keys.Select(k => new UltraPlusSetting { Key = k, Type = "enum" }).ToList();

            var presets = new List<UltraPlusPreset>();
            foreach (var file in Directory.EnumerateFiles(where.DataDir, "preset_*.ini"))
            {
                if (UltraPlusConfig.PresetStorageName(Path.GetFileName(file)) is not { } storage) continue;
                presets.Add(UltraPlusConfig.ParsePreset(storage, ReadShared(file)));
            }
            presets.Sort((a, b) =>
            {
                int r = UltraPlusConfig.PresetRank(a).CompareTo(UltraPlusConfig.PresetRank(b));
                return r != 0 ? r : string.Compare(a.DisplayName, b.DisplayName, StringComparison.CurrentCultureIgnoreCase);
            });

            return new UltraPlusState(where.DataDir, where.ConfigPath, string.IsNullOrEmpty(version) ? null : version,
                schema, values, defaults, presets);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[UltraPlusFiles.Load] '{where.ConfigPath}' — {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Writes changed values into UltraPlusConfig.ini (only those lines change), then touches config_modified so Ultra+
    /// reloads live (with AutoApplyConfig=on). The first RHI write keeps a backup. Retries for about a second while the
    /// game holds the file. False when it couldn't be written.
    /// </summary>
    public static bool Write(string configPath, IReadOnlyDictionary<string, string> changes)
    {
        if (changes.Count == 0) return true;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                // Latin-1 maps every byte to one char and back, so lines RHI doesn't change keep their exact bytes
                // whatever the file's encoding (UTF-8 with or without BOM, ANSI); the values written are ASCII.
                var text = File.ReadAllText(configPath, System.Text.Encoding.Latin1);
                var updated = UltraPlusConfig.SetValues(text, changes);
                if (updated == text) return true;

                var backup = configPath + BackupSuffix;
                if (!File.Exists(backup)) File.Copy(configPath, backup);

                var temp = configPath + ".rhi-tmp";
                File.WriteAllText(temp, updated, System.Text.Encoding.Latin1);
                File.Move(temp, configPath, overwrite: true);

                var flag = Path.Combine(Path.GetDirectoryName(configPath)!, UltraPlusConfig.ModifiedFlagFileName);
                File.WriteAllText(flag, "");
                CrashReporter.Log($"[UltraPlusFiles.Write] {string.Join(", ", changes.Select(c => $"{c.Key}={c.Value}"))} → '{configPath}'");
                return true;
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < 5)
            {
                Thread.Sleep(200);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                CrashReporter.Log($"[UltraPlusFiles.Write] Failed '{configPath}' — {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>Saves a custom preset (preset_&lt;name&gt;.ini). Refuses to overwrite a preset the mod shipped. Error text, or null.</summary>
    public static string? SavePreset(string dataDir, string name, string? description, IEnumerable<KeyValuePair<string, string>> values)
    {
        if (UltraPlusConfig.SafePresetName(name) is not { } safe)
            return "Use a name of up to 60 characters without \\ / : * ? \" < > |.";
        var path = Path.Combine(dataDir, $"preset_{safe}.ini");
        try
        {
            if (File.Exists(path) && !UltraPlusConfig.ParsePreset(safe, File.ReadAllText(path)).IsCustom)
                return "Ultra+ has a preset with that name. Choose another name.";
            File.WriteAllText(path, UltraPlusConfig.BuildCustomPreset(description, values), new System.Text.UTF8Encoding(false));
            CrashReporter.Log($"[UltraPlusFiles.SavePreset] '{path}'");
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[UltraPlusFiles.SavePreset] '{path}' — {ex.Message}");
            return $"Couldn't save the preset: {ex.Message}";
        }
    }

    /// <summary>Deletes a custom preset. Presets the mod shipped are never deleted.</summary>
    public static bool DeletePreset(string dataDir, string storageName)
    {
        if (UltraPlusConfig.SafePresetName(storageName) is not { } safe) return false;
        var path = Path.Combine(dataDir, $"preset_{safe}.ini");
        try
        {
            if (!File.Exists(path) || !UltraPlusConfig.ParsePreset(safe, File.ReadAllText(path)).IsCustom) return false;
            File.Delete(path);
            CrashReporter.Log($"[UltraPlusFiles.DeletePreset] '{path}'");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashReporter.Log($"[UltraPlusFiles.DeletePreset] '{path}' — {ex.Message}");
            return false;
        }
    }

    /// <summary>Reads a file the game may have open. Null when it doesn't exist.</summary>
    private static string? ReadShared(string path)
    {
        if (!File.Exists(path)) return null;
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sr = new StreamReader(fs);
        return sr.ReadToEnd();
    }
}
