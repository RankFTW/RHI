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
    private string CacheFile => Path.Combine(LinuxPaths.Cache, "renodx-catalog.json");
    public Catalog(HttpClient http)
    {
        _http = http;
        var manifest = Path.Combine(AppContext.BaseDirectory, "manifest.json");
        _manifest = JsonDocument.Parse(File.Exists(manifest) ? File.ReadAllText(manifest) : "{}").RootElement.Clone();
        if (File.Exists(CacheFile))
        {
            try { Mods = JsonSerializer.Deserialize<List<GameMod>>(File.ReadAllText(CacheFile), LinuxPaths.Json) ?? []; Status = "Using cached RenoDX catalogue."; }
            catch (JsonException) { Status = "Cached catalogue is invalid; refresh to download it again."; }
        }
        AddGenericMods();
    }

    public async Task Refresh(IProgress<string>? progress = null)
    {
        progress?.Report("Fetching the RenoDX wiki…");
        var html = await _http.GetStringAsync("https://github.com/clshortfuse/renodx/wiki/Mods");
        var (mods, _) = new WikiService(_http, GameDiscovery.NormalizeName).ParseHtml(html, progress);
        ApplySharedDownloads(html, mods);
        if (mods.Count == 0) throw new IOException("The RenoDX wiki returned no mods. The cached catalogue has been retained.");
        Mods = mods;
        LinuxPaths.WriteJson(CacheFile, Mods);
        AddGenericMods();
        Status = $"{mods.Count} RenoDX entries refreshed {DateTime.Now:t}.";
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
                    mod.NameUrl = "https://github.com/clshortfuse/renodx/wiki/Mods";
                }
            }
        }
    }

    private void AddGenericMods()
    {
        Mods.RemoveAll(m => m.IsGenericUnity || m.IsGenericUnreal);
        Mods.Add(new() { Name = "Generic Unreal Engine", SnapshotUrl = WikiService.GenericUnrealUrl, IsGenericUnreal = true,
            Notes = "Generic engine support is game-dependent. Check the RenoDX wiki for required HDR/Engine.ini settings.", NameUrl = "https://github.com/clshortfuse/renodx/wiki/Mods" });
        Mods.Add(new() { Name = "Generic Unity", SnapshotUrl = WikiService.GenericUnityUrl64, SnapshotUrl32 = WikiService.GenericUnityUrl32, IsGenericUnity = true,
            Notes = "Generic engine support is game-dependent. Check the RenoDX wiki for this game's instructions.", NameUrl = "https://github.com/clshortfuse/renodx/wiki/Mods" });
        Mods = Mods.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public string? ManifestString(string section, string name)
    {
        var value = ManifestValue(section, name);
        return value?.ValueKind == JsonValueKind.String ? value.Value.GetString() : null;
    }

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
        if (ManifestValue("gameNotes", game.Name) is { } gameNotes && gameNotes.TryGetProperty("notes", out var n)) notes.Add(n.GetString() ?? "");
        if (ManifestValue("installWarnings", game.Name) is { } warnings)
            notes.AddRange(warnings.EnumerateObject().Select(p => p.Value.GetString() ?? ""));
        if (ManifestValue("forceExternalOnly", game.Name) is { } external && external.TryGetProperty("url", out var url))
            notes.Add("This game requires its author's external package: " + url.GetString());
        return string.Join("\n\n", notes);
    }
}
