using System.Text.Json;
using HtmlAgilityPack;
using RenoDXCommander.Models;
using RenoDXCommander.Services;

namespace RHI.Linux.Core;

public sealed class Catalog
{
    private readonly HttpClient _http;
    private JsonElement _manifest;
    public List<GameMod> Mods { get; private set; } = [];
    public string Status { get; private set; } = "Catalogue has not been refreshed.";
    public string ManifestStatus { get; private set; } = "Using bundled manifest.";
    private string ManifestCacheFile => Path.Combine(LinuxPaths.Cache, "manifest.json");
    private string CacheFile => Path.Combine(LinuxPaths.Cache, "renodx-catalog.json");
    public Catalog(HttpClient http)
    {
        _http = http;
        var manifest = Path.Combine(AppContext.BaseDirectory, "manifest.json");
        _manifest = ReadManifestFile(manifest) ?? ParseManifest("{}");
        if (ReadManifestFile(ManifestCacheFile) is { } cachedManifest)
        {
            _manifest = cachedManifest;
            ManifestStatus = "Using cached manifest.";
        }
        if (File.Exists(CacheFile))
        {
            try { Mods = JsonSerializer.Deserialize<List<GameMod>>(File.ReadAllText(CacheFile), LinuxPaths.Json) ?? []; Status = "Using cached RenoDX catalogue."; }
            catch (JsonException) { Status = "Cached catalogue is invalid; refresh to download it again."; }
        }
        AddGenericMods();
    }

    public async Task Refresh(IProgress<string>? progress = null, bool refreshManifest = true)
    {
        if (refreshManifest) await RefreshManifest(progress);
        string html = "";
        var (mods, _) = await new WikiService(_http, GameDiscovery.NormalizeName)
            .FetchAllAsync(progress, fetched => html = fetched);
        ApplySharedDownloads(html, mods);
        if (mods.Count == 0) throw new IOException("The RenoDX wiki returned no mods. The cached catalogue has been retained.");
        Mods = mods;
        LinuxPaths.WriteJson(CacheFile, Mods);
        AddGenericMods();
        Status = $"{mods.Count} RenoDX entries refreshed {DateTime.Now:t}. {ManifestStatus}";
    }

    // Refresh independently of the wiki: manifest fixes still arrive when wiki fetching fails.
    public async Task RefreshManifest(IProgress<string>? progress = null)
    {
        progress?.Report("Fetching game compatibility manifest…");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var next = ParseManifest(await _http.GetStringAsync(Sources.Manifest, timeout.Token));
            _manifest = next;
            ManifestStatus = "Using live manifest.";
            try { LinuxPaths.WriteJson(ManifestCacheFile, next); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ManifestStatus = "Using live manifest; could not save its offline cache.";
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or TaskCanceledException)
        {
            // Preserve the last valid manifest, including one loaded at startup.
            ManifestStatus = "Manifest refresh unavailable; retaining cached or bundled compatibility data.";
        }
    }

    // Components without a catalogue instance share the same validated offline fallback.
    public static JsonElement LoadManifestSnapshot() =>
        ReadManifestFile(Path.Combine(LinuxPaths.Cache, "manifest.json"))
        ?? ReadManifestFile(Path.Combine(AppContext.BaseDirectory, "manifest.json"))
        ?? ParseManifest("{}");

    private static JsonElement? ReadManifestFile(string path)
    {
        try { return File.Exists(path) ? ParseManifest(File.ReadAllText(path)) : null; }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return null; }
    }

    private static JsonElement ParseManifest(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException("Manifest must be an object.");
        _ = root.Deserialize<RemoteManifest>();
        foreach (var section in new[] { "wikiNameOverrides", "snapshotOverrides", "graphicsApiOverrides", "launchExeOverrides", "installPathOverrides", "dgVoodooVersions" })
            if (root.TryGetProperty(section, out var map)
                && (map.ValueKind != JsonValueKind.Object || map.EnumerateObject().Any(p => p.Value.ValueKind != JsonValueKind.String)))
                throw new JsonException("Invalid manifest section: " + section);
        foreach (var section in new[] { "gameNotes", "installWarnings", "forceExternalOnly", "shaderPacks" })
            if (root.TryGetProperty(section, out var map)
                && (map.ValueKind != JsonValueKind.Object || map.EnumerateObject().Any(p => p.Value.ValueKind != JsonValueKind.Object)))
                throw new JsonException("Invalid manifest section: " + section);
        if (root.TryGetProperty("wikiUnlinks", out var unlinks)
            && (unlinks.ValueKind != JsonValueKind.Array || unlinks.EnumerateArray().Any(v => v.ValueKind != JsonValueKind.String)))
            throw new JsonException("Invalid wikiUnlinks.");
        if (root.TryGetProperty("dlssPresets", out var presets) && presets.ValueKind == JsonValueKind.Object)
            foreach (var kind in new[] { "sr", "rr", "fg", "nr" })
                if (presets.TryGetProperty(kind, out var entries) && entries.ValueKind == JsonValueKind.Array
                    && entries.EnumerateArray().Any(entry => entry.ValueKind != JsonValueKind.Object))
                    throw new JsonException("Invalid DLSS preset entry.");
        return root.Clone();
    }

    // Multi-game sections put their download link in the heading, not in each row.
    // Associate each table with its preceding download heading, preserving named mods.
    public static void ApplySharedDownloads(string html, List<GameMod> mods)
    {
        var doc = new HtmlDocument(); doc.LoadHtml(html);
        var headings = doc.DocumentNode.SelectNodes("//h3")?.ToList() ?? [];
        foreach (var table in doc.DocumentNode.SelectNodes("//table") ?? Enumerable.Empty<HtmlNode>())
        {
            var header = table.SelectSingleNode(".//tr");
            if (header?.InnerText.Contains("Links", StringComparison.OrdinalIgnoreCase) == true) continue;
            var heading = headings.LastOrDefault(h => h.StreamPosition < table.StreamPosition);
            if (heading is null) continue;
            var links = doc.DocumentNode.SelectNodes("//a[@href]")?.Where(a => a.StreamPosition >= heading.StreamPosition && a.StreamPosition < table.StreamPosition)
                .Select(a => a.GetAttributeValue("href", "")).ToList() ?? [];
            var url = links.FirstOrDefault(u => u.EndsWith(".addon64"));
            var url32 = links.FirstOrDefault(u => u.EndsWith(".addon32"));
            if (url is null) continue;
            foreach (var row in table.SelectNodes(".//tr")?.Skip(1) ?? [])
            {
                var name = HtmlEntity.DeEntitize(row.SelectSingleNode("td")?.InnerText ?? "").Trim();
                foreach (var mod in mods.Where(m => m.Name == name && m.SnapshotUrl == null && m.NexusUrl == null))
                {
                    mod.SnapshotUrl = url;
                    mod.SnapshotUrl32 = url32;
                    mod.Maintainer = "Shared mod: " + HtmlEntity.DeEntitize(heading.InnerText).Trim();
                    mod.NameUrl = Sources.Wiki;
                }
            }
        }
    }

    private void AddGenericMods()
    {
        Mods.RemoveAll(m => m.IsGenericUnity || m.IsGenericUnreal);
        Mods.Add(new() { Name = "Generic Unreal Engine", SnapshotUrl = WikiService.GenericUnrealUrl, IsGenericUnreal = true,
            Notes = "Generic engine support is game-dependent. Check the RenoDX wiki for required HDR/Engine.ini settings.", NameUrl = Sources.Wiki });
        Mods.Add(new() { Name = "Generic Unity", SnapshotUrl = WikiService.GenericUnityUrl64, SnapshotUrl32 = WikiService.GenericUnityUrl32, IsGenericUnity = true,
            Notes = "Generic engine support is game-dependent. Check the RenoDX wiki for this game's instructions.", NameUrl = Sources.Wiki });
        Mods = Mods.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public string? ManifestString(string section, string name)
    {
        var value = ManifestValue(section, name);
        return value?.ValueKind == JsonValueKind.String ? value.Value.GetString() : null;
    }

    public JsonElement? ManifestRoot(string name) => _manifest.ValueKind == JsonValueKind.Object && _manifest.TryGetProperty(name, out var value) ? value : null;

    public JsonElement? ManifestValue(string section, string name)
    {
        if (!_manifest.TryGetProperty(section, out var values) || values.ValueKind != JsonValueKind.Object) return null;
        var norm = GameDiscovery.NormalizeName(name);
        foreach (var property in values.EnumerateObject())
            if (GameDiscovery.NormalizeName(property.Name) == norm) return property.Value;
        return null;
    }

    public GameMod? Match(Game game)
    {
        if (_manifest.TryGetProperty("wikiUnlinks", out var unlinks)
            && unlinks.EnumerateArray().Any(v => GameDiscovery.NormalizeName(v.GetString() ?? "") == GameDiscovery.NormalizeName(game.Name))) return null;
        var name = ManifestString("wikiNameOverrides", game.Name) ?? game.Name;
        return Mods.FirstOrDefault(m => GameDiscovery.NormalizeName(m.Name) == GameDiscovery.NormalizeName(name));
    }

    public string? AddonUrl(Game game, GameMod mod)
    {
        if (game.Architecture == MachineType.I386) return mod.SnapshotUrl32
            ?? (mod.SnapshotUrl?.EndsWith(".addon32", StringComparison.OrdinalIgnoreCase) == true ? mod.SnapshotUrl : null);
        // Only apply a per-game override when installing that game's matched entry.
        return Match(game)?.Name == mod.Name ? ManifestString("snapshotOverrides", game.Name) ?? mod.SnapshotUrl : mod.SnapshotUrl;
    }

    public string GameNotes(Game game)
    {
        var notes = new List<string>();
        if (ManifestValue("gameNotes", game.Name) is { } gameNotes && gameNotes.TryGetProperty("notes", out var n) && n.ValueKind == JsonValueKind.String) notes.Add(n.GetString() ?? "");
        if (ManifestValue("installWarnings", game.Name) is { } warnings)
            notes.AddRange(warnings.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String).Select(p => p.Value.GetString() ?? ""));
        if (ManifestValue("forceExternalOnly", game.Name) is { } external && external.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String)
            notes.Add("This game requires its author's external package: " + url.GetString());
        return string.Join("\n\n", notes);
    }
}
