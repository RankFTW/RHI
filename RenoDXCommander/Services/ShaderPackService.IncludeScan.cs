// ShaderPackService.IncludeScan.cs — Per-file #include dependency scanner and GetPackShaderFiles
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RenoDXCommander.Services;

public partial class ShaderPackService
{
    // ── Include map cache ─────────────────────────────────────────────────────────

    private Dictionary<string, HashSet<string>>? _includeMapCache;
    private static readonly object _includeMapLock = new();

    // Matches #include "file" or #include <file> — captures just the path argument
    private static readonly Regex IncludeRegex = new(
        @"#include\s+[""<]([^"">]+)["">""]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Matches /* ... */ block comments (DOTALL equivalent via Singleline)
    private static readonly Regex BlockCommentRegex = new(
        @"/\*.*?\*/",
        RegexOptions.Compiled | RegexOptions.Singleline);

    // Matches // line comments
    private static readonly Regex LineCommentRegex = new(
        @"//[^\r\n]*",
        RegexOptions.Compiled);

    /// <summary>
    /// Scans all .fx and .fxh files in the staging Shaders directory and builds a map of
    /// filename → set of filenames it directly #includes (relative filename only, no path).
    /// Used to auto-select header dependencies when individual shaders are chosen.
    /// Result is cached in-memory after first call; cleared on pack update.
    /// </summary>
    public Dictionary<string, HashSet<string>> BuildIncludeMap()
    {
        lock (_includeMapLock)
        {
            if (_includeMapCache != null)
                return _includeMapCache;

            CrashReporter.Log("[ShaderPackService.BuildIncludeMap] Building include dependency map");

            var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            if (!Directory.Exists(ShadersDir))
            {
                _includeMapCache = map;
                return map;
            }

            try
            {
                foreach (var filePath in Directory.EnumerateFiles(ShadersDir, "*", SearchOption.AllDirectories))
                {
                    var ext = Path.GetExtension(filePath);
                    if (!ext.Equals(".fx", StringComparison.OrdinalIgnoreCase) &&
                        !ext.Equals(".fxh", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var leafName = Path.GetFileName(filePath);

                    try
                    {
                        var content = File.ReadAllText(filePath, System.Text.Encoding.UTF8);

                        // Strip block comments then line comments
                        var clean = BlockCommentRegex.Replace(content, " ");
                        clean = LineCommentRegex.Replace(clean, "");

                        var deps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                        foreach (Match m in IncludeRegex.Matches(clean))
                        {
                            // Extract just the leaf filename, normalising forward/back slashes
                            var raw = m.Groups[1].Value.Replace('\\', '/');
                            var leaf = Path.GetFileName(raw);
                            if (!string.IsNullOrEmpty(leaf))
                                deps.Add(leaf);
                        }

                        if (deps.Count > 0)
                            map[leafName] = deps;
                    }
                    catch (Exception ex)
                    {
                        CrashReporter.Log($"[ShaderPackService.BuildIncludeMap] Could not read '{leafName}' — {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[ShaderPackService.BuildIncludeMap] Walk failed — {ex.Message}");
            }

            CrashReporter.Log($"[ShaderPackService.BuildIncludeMap] Built map with {map.Count} entries");
            _includeMapCache = map;
            return map;
        }
    }

    /// <summary>Clears the cached include map so it is rebuilt on the next call.</summary>
    public void ClearIncludeCache()
    {
        lock (_includeMapLock)
        {
            _includeMapCache = null;
        }
    }

    // ── GetPackShaderFiles ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all .fx filenames (leaf names) belonging to the given pack IDs, from the staging dir.
    /// De-duplicated, sorted alphabetically.
    /// </summary>
    public IReadOnlyList<string> GetPackShaderFiles(IEnumerable<string> packIds)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            // Virtual custom files pack — return leaf names from the Custom folder
            var packIdList = packIds.ToList();
            if (packIdList.Any(id => id.Equals(CustomFilePackId, StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var f in GetCustomPackFiles())
                    result.Add(Path.GetFileName(f));
                packIdList = packIdList.Where(id => !id.Equals(CustomFilePackId, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (!packIdList.Any()) return result.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly();

            if (!File.Exists(SettingsPath)) return result.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly();
            var d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(SettingsPath));
            if (d == null) return result.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly();

            foreach (var packId in packIdList)
            {
                if (!d.TryGetValue(FileListKey(packId), out var json) || string.IsNullOrEmpty(json))
                    continue;
                var files = JsonSerializer.Deserialize<List<string>>(json) ?? new();
                foreach (var rel in files)
                {
                    // Only entries under Shaders\
                    if (!rel.StartsWith("Shaders" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        continue;
                    var ext = Path.GetExtension(rel);
                    if (!ext.Equals(".fx", StringComparison.OrdinalIgnoreCase))
                        continue;
                    result.Add(Path.GetFileName(rel));
                }
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[ShaderPackService.GetPackShaderFiles] Failed — {ex.Message}");
        }

        return result.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly();
    }
}
