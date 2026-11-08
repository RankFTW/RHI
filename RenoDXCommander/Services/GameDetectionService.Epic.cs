// GameDetectionService.Epic.cs — Epic Games detection (classic launcher and the new "eglv2" launcher)
using System.Security;
using System.Text.Json;
using Microsoft.Win32;
using RenoDXCommander.Models;

namespace RenoDXCommander.Services;

public partial class GameDetectionService
{
    public List<DetectedGame> FindEpicGames()
    {
        var games = new List<DetectedGame>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string name, string path, string? catalogNamespace, string? catalogItemId, string? appName)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path)) return;
            if (!Directory.Exists(path)) return;
            if (!seenPaths.Add(path.Replace('/', '\\').TrimEnd('\\'))) return;
            games.Add(new DetectedGame
            {
                Name = name,
                InstallPath = path,
                Source = "Epic",
                EpicCatalogNamespace = catalogNamespace,
                EpicCatalogItemId = catalogItemId,
                EpicAppName = appName,
            });
        }

        // Classic launcher: one .item JSON file per install in ProgramData.
        var manifestDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (Directory.Exists(manifestDir))
        {
            foreach (var file in Directory.GetFiles(manifestDir, "*.item"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var name = ExtractJsonString(json, "DisplayName");
                    var path = ExtractJsonString(json, "InstallLocation");
                    if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(path))
                        Add(name, path, ExtractJsonString(json, "CatalogNamespace"), ExtractJsonString(json, "CatalogItemId"), ExtractJsonString(json, "AppName"));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    CrashReporter.Log($"[GameDetectionService.FindEpicGames] Failed to read manifest '{file}' — {ex.Message}");
                }
            }
        }

        // New launcher (eglv2). Migrating to it removes the ProgramData manifests above.
        int before = games.Count;
        foreach (var g in FindEpicV2Installs())
            Add(g.Name, g.Path, g.Namespace, g.ItemId, g.AppName);
        if (games.Count > before)
            CrashReporter.Log($"[GameDetectionService.FindEpicGames] New Epic launcher: {games.Count - before} game(s)");

        CrashReporter.Log($"[GameDetectionService.FindEpicGames] Found {games.Count} game(s)");
        return games;
    }

    private record EpicV2Install(string Name, string Path, string? Namespace, string? ItemId, string? AppName);

    /// <summary>
    /// The new Epic launcher keeps installs in %LOCALAPPDATA%\com.epicgames.eglv2\installations.jsonl
    /// (an append log: a header line, then one JSON entry per change, last entry per installationId wins).
    /// It has no game names, so names come from the per-user uninstall entries it writes
    /// (HKCU\...\Uninstall\EpicGames-{artifactId}: DisplayName, InstallLocation).
    /// </summary>
    private static List<EpicV2Install> FindEpicV2Installs()
    {
        var result = new List<EpicV2Install>();

        // artifactId → (name, path, namespace, item id) from the uninstall entries.
        var uninstall = new Dictionary<string, (string Name, string? Path, string? Namespace, string? ItemId)>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
            if (key != null)
            {
                foreach (var sub in key.GetSubKeyNames())
                {
                    if (!sub.StartsWith("EpicGames-", StringComparison.OrdinalIgnoreCase)) continue;
                    using var gameKey = key.OpenSubKey(sub);
                    var name = gameKey?.GetValue("DisplayName") as string;
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    var path = gameKey!.GetValue("InstallLocation") as string;
                    var uninstallCmd = gameKey.GetValue("UninstallString") as string;
                    var (uNs, uItem) = ParseEpicUriIds(uninstallCmd);
                    uninstall[sub["EpicGames-".Length..]] = (name.Trim(), path, uNs, uItem);
                }
            }
        }
        catch (SecurityException ex) { CrashReporter.Log($"[GameDetectionService.FindEpicV2Installs] Registry access denied — {ex.Message}"); }

        // The launcher records its data folder; fall back to the default location.
        string? dataDir = null;
        try
        {
            using var egl = Registry.CurrentUser.OpenSubKey(@"Software\Epic Games\EpicGamesLauncher");
            dataDir = egl?.GetValue("AppDataPath") as string;
        }
        catch (SecurityException ex) { CrashReporter.Log($"[GameDetectionService.FindEpicV2Installs] Registry access denied — {ex.Message}"); }
        if (string.IsNullOrWhiteSpace(dataDir) || !Directory.Exists(dataDir))
            dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "com.epicgames.eglv2");
        var jsonl = Path.Combine(dataDir, "installations.jsonl");
        var listedArtifacts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (File.Exists(jsonl))
        {
            var latest = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            try
            {
                // The launcher may be writing the file; open with shared access.
                using var fs = new FileStream(jsonl, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(fs);
                string? line;
                int badLines = 0;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;
                        if (root.ValueKind != JsonValueKind.Object) continue;
                        if (!root.TryGetProperty("installationId", out var idEl) || idEl.ValueKind != JsonValueKind.String) continue;
                        latest[idEl.GetString()!] = root.Clone();
                    }
                    catch (JsonException) { badLines++; } // e.g. a half-written line; the next scan picks it up
                }
                if (badLines > 0)
                    CrashReporter.Log($"[GameDetectionService.FindEpicV2Installs] Skipped {badLines} unreadable line(s) in installations.jsonl");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                CrashReporter.Log($"[GameDetectionService.FindEpicV2Installs] Failed to read '{jsonl}' — {ex.Message}");
            }

            Dictionary<string, (string Title, string? MainGameId)>? catalog = null;
            foreach (var (installId, e) in latest)
            {
                var artifactId = GetString(e, "artifactId");
                if (artifactId != null) listedArtifacts.Add(artifactId);

                if (!(e.TryGetProperty("complete", out var c) && c.ValueKind == JsonValueKind.True)) continue;
                var dir = GetString(e, "dir");
                if (string.IsNullOrEmpty(dir)) continue;

                string? name = null;
                string? ns = GetString(e, "namespace");
                string? itemId = GetString(e, "itemId");
                if (artifactId != null && uninstall.TryGetValue(artifactId, out var u))
                {
                    name = u.Name;
                    ns ??= u.Namespace;
                    itemId ??= u.ItemId;
                }
                // Games migrated from the classic launcher keep their old .item file here.
                name ??= ReadMigratedItemName(dataDir, installId);
                // Some installs have neither (no uninstall entry), so use the launcher's catalog cache.
                if (name == null)
                {
                    catalog ??= LoadEpicCatalogTitles(dataDir);
                    if (itemId != null && catalog.TryGetValue(itemId, out var cat))
                    {
                        // An add-on (e.g. DLC) shares the game's folder: name it after its main game.
                        if (cat.MainGameId != null && catalog.TryGetValue(cat.MainGameId, out var main))
                            name = main.Title;
                        else if (cat.MainGameId == null)
                            name = cat.Title;
                        if (name != null)
                            CrashReporter.Log($"[GameDetectionService.FindEpicV2Installs] Named '{dir}' from catalog cache: '{name}'");
                    }
                }

                if (name == null)
                {
                    CrashReporter.Log($"[GameDetectionService.FindEpicV2Installs] No name for install '{dir}' (artifact {artifactId}) — skipped");
                    continue;
                }
                result.Add(new EpicV2Install(name, dir, ns, itemId, artifactId));
            }
        }

        // Uninstall entries the log doesn't mention (e.g. if the log format changes).
        foreach (var (artifactId, u) in uninstall)
        {
            if (listedArtifacts.Contains(artifactId) || string.IsNullOrEmpty(u.Path)) continue;
            result.Add(new EpicV2Install(u.Name, u.Path, u.Namespace, u.ItemId, artifactId));
        }

        return result;
    }

    private static string? GetString(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Namespace and item id from "com.epicgames.launcher://apps/{namespace}:{itemId}:{artifactId}?action=…" (":" may be "%3A").</summary>
    internal static (string? Namespace, string? ItemId) ParseEpicUriIds(string? cmd)
    {
        if (string.IsNullOrEmpty(cmd)) return (null, null);
        int i = cmd.IndexOf("://apps/", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return (null, null);
        var rest = cmd[(i + "://apps/".Length)..];
        int end = rest.IndexOfAny(new[] { '?', '"', ' ' });
        if (end >= 0) rest = rest[..end];
        var parts = Uri.UnescapeDataString(rest).Split(':');
        if (parts.Length < 2 || parts[0].Length == 0) return (null, null);
        return (parts[0], parts.Length >= 3 && parts[1].Length > 0 ? parts[1] : null);
    }

    /// <summary>
    /// Launch link for an Epic game. Uses namespace:item:app (what Epic's own desktop shortcuts use; the new
    /// launcher ignores a bare app name for some games) and falls back to the app name alone.
    /// </summary>
    public static string EpicLaunchUri(string? catalogNamespace, string? catalogItemId, string appName)
    {
        string id = !string.IsNullOrEmpty(catalogNamespace) && !string.IsNullOrEmpty(catalogItemId)
            ? $"{Uri.EscapeDataString(catalogNamespace)}%3A{Uri.EscapeDataString(catalogItemId)}%3A{Uri.EscapeDataString(appName)}"
            : Uri.EscapeDataString(appName);
        return $"com.epicgames.launcher://apps/{id}?action=launch&silent=true";
    }

    /// <summary>
    /// itemId → (title, main game itemId for add-ons) from cache\prod\catalog-cache.json
    /// ({"items":{"{itemId}":{"item":{"title":…,"mainGameItem":{"id":…}|null}}}}). Empty if missing or unreadable.
    /// </summary>
    private static Dictionary<string, (string Title, string? MainGameId)> LoadEpicCatalogTitles(string dataDir)
    {
        var map = new Dictionary<string, (string, string?)>(StringComparer.OrdinalIgnoreCase);
        var file = Path.Combine(dataDir, "cache", "prod", "catalog-cache.json");
        if (!File.Exists(file))
        {
            CrashReporter.Log($"[GameDetectionService.LoadEpicCatalogTitles] No catalog cache at '{file}'");
            return map;
        }
        try
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(fs);
            if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Object)
                return map;
            foreach (var p in items.EnumerateObject())
            {
                if (!p.Value.TryGetProperty("item", out var item) || item.ValueKind != JsonValueKind.Object) continue;
                var title = GetString(item, "title");
                if (string.IsNullOrWhiteSpace(title)) continue;
                string? mainId = item.TryGetProperty("mainGameItem", out var mg) && mg.ValueKind == JsonValueKind.Object
                    ? GetString(mg, "id") : null;
                map[p.Name] = (title.Trim(), mainId);
            }
            CrashReporter.Log($"[GameDetectionService.LoadEpicCatalogTitles] Loaded {map.Count} catalog title(s)");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            CrashReporter.Log($"[GameDetectionService.LoadEpicCatalogTitles] '{file}' — {ex.Message}");
        }
        return map;
    }

    private static string? ReadMigratedItemName(string dataDir, string installId)
    {
        var item = Path.Combine(dataDir, "import-scan", "v1-items", installId + ".item");
        if (!File.Exists(item)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(item));
            var name = GetString(doc.RootElement, "DisplayName");
            return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            CrashReporter.Log($"[GameDetectionService.ReadMigratedItemName] '{item}' — {ex.Message}");
            return null;
        }
    }
}
