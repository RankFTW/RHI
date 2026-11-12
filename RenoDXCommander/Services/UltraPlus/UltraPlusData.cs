// UltraPlusData.cs — display data for Ultra+ settings from Ultra+ Manager's repository (readable names, descriptions,
// categories, performance cost per value). Not shipped with RHI: fetched at startup only when unlock_extra.txt exists,
// with the last good copy kept in %LocalAppData%\RHI\ultraplus\ for offline use. Without it the overlay shows the raw
// keys and the mod's own descriptions. Plan: docs/ultraplus-overlay-plan.md.

using System.Text.Json;

namespace RenoDXCommander.Services.UltraPlus;

public static class UltraPlusData
{
    private const string BaseUrl = "https://gitlab.com/ultra-plus/ultraplus-manager/-/raw/main/src/Data/";
    private const string NamesFile       = "UltraPlusSettings_name_overrides.json";
    private const string DescriptionsFile = "UltraPlusSettings_description_overrides.json";
    private const string CategoriesFile  = "UltraPlusSettings_UI.json";
    private const string CostsFile       = "UltraPlusSettings_performance.json";

    public static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "ultraplus");

    private static readonly JsonSerializerOptions Json = new() { AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip };
    private static readonly object Gate = new();

    // UPM's files have keys that differ only by case (RTSkyLight / RTSkylight, RaytracingMode / RayTracingMode),
    // which UPM treats as different settings: exact key first, then a case-insensitive fallback (first one wins).
    private static Lookup _names = new(null);
    private static Lookup _descriptions = new(null);
    private static List<(string Name, List<string> Keys)> _categories = new();
    private static Dictionary<string, Dictionary<string, string>> _costs = new(StringComparer.Ordinal);
    private static bool _loaded;

    /// <summary>Downloads the four files (keeping the cached copy of any that fails) and loads them. No-op without unlock_extra.txt.</summary>
    public static async Task FetchAsync(HttpClient http, CancellationToken ct = default)
    {
        if (!DevUnlockService.IsExtraUnlocked) return;
        Directory.CreateDirectory(CacheDir);
        int ok = 0;
        foreach (var file in new[] { NamesFile, DescriptionsFile, CategoriesFile, CostsFile })
        {
            try
            {
                var text = await http.GetStringAsync(BaseUrl + file, ct).ConfigureAwait(false);
                using (JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip })) { }
                var path = Path.Combine(CacheDir, file);
                await File.WriteAllTextAsync(path + ".tmp", text, ct).ConfigureAwait(false);
                File.Move(path + ".tmp", path, overwrite: true);
                ok++;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException or UnauthorizedAccessException)
            {
                CrashReporter.Log($"[UltraPlusData.FetchAsync] {file} — {ex.Message} (keeping the cached copy)");
            }
        }
        CrashReporter.Log($"[UltraPlusData.FetchAsync] {ok} of 4 Ultra+ Manager data files updated");
        Load(force: true);
    }

    /// <summary>Loads the cached files (once, unless forced). Missing or broken files leave that data empty.</summary>
    public static void Load(bool force = false)
    {
        lock (Gate)
        {
            if (_loaded && !force) return;
            try { LoadCore(); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or JsonException or NotSupportedException)
            {
                CrashReporter.Log($"[UltraPlusData.Load] Data not usable, showing plain names — {ex.Message}");
                _names = new(null); _descriptions = new(null); _costs = new(StringComparer.Ordinal); _categories = new();
            }
            _loaded = true;
        }
    }

    private static void LoadCore()
    {
        {
            _names = new Lookup(Read<Dictionary<string, string>>(NamesFile));
            _descriptions = new Lookup(Read<Dictionary<string, string>>(DescriptionsFile));
            _costs = new(StringComparer.Ordinal);
            foreach (var kv in Read<Dictionary<string, Dictionary<string, string>>>(CostsFile) ?? new())
            {
                var byValue = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var v in kv.Value) byValue.TryAdd(v.Key, v.Value);
                _costs[kv.Key] = byValue;
            }
            _categories = new();
            // Keep the file's order (it's the display order)
            var catText = ReadText(CategoriesFile);
            if (catText != null)
            {
                try
                {
                    using var doc = JsonDocument.Parse(catText, new JsonDocumentOptions { AllowTrailingCommas = true });
                    foreach (var prop in doc.RootElement.EnumerateObject())
                        if (prop.Value.ValueKind == JsonValueKind.Array)
                            _categories.Add((prop.Name, prop.Value.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToList()));
                }
                catch (JsonException ex) { CrashReporter.Log($"[UltraPlusData.Load] {CategoriesFile} — {ex.Message}"); }
            }
        }
    }

    /// <summary>Readable name, else the key.</summary>
    public static string NameOf(string key) { Load(); lock (Gate) return _names.Get(key) is { } n && !string.IsNullOrWhiteSpace(n) ? n : key; }

    /// <summary>The description to show: the mod's when it asks for that (or there's no other), else UPM's.</summary>
    public static string? DescriptionOf(UltraPlusSetting s)
    {
        Load();
        if (s.PreferModDescription) return s.ModDescription;
        lock (Gate) return _descriptions.Get(s.Key) is { } d && !string.IsNullOrWhiteSpace(d) ? d : s.ModDescription;
    }

    /// <summary>"none" / "low" / "medium" / "high" … for a value, or null when unknown.</summary>
    public static string? CostOf(string key, string value)
    {
        Load();
        lock (Gate)
        {
            if (!_costs.TryGetValue(key, out var byValue))
                byValue = _costs.FirstOrDefault(kv => kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value;
            return byValue != null && byValue.TryGetValue(value, out var c) ? c : null;
        }
    }

    /// <summary>Category names in display order, each with its keys; settings in no category go to "Other" (added by the caller).</summary>
    public static IReadOnlyList<(string Name, List<string> Keys)> Categories { get { Load(); lock (Gate) return _categories.ToList(); } }

    public static bool HasData { get { Load(); lock (Gate) return _names.Count > 0 || _categories.Count > 0; } }

    /// <summary>Exact key first, then the first entry whose key matches ignoring case.</summary>
    internal sealed class Lookup
    {
        private readonly Dictionary<string, string> _exact = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _folded = new(StringComparer.OrdinalIgnoreCase);
        public Lookup(Dictionary<string, string>? source)
        {
            foreach (var kv in source ?? new())
            {
                _exact[kv.Key] = kv.Value;
                _folded.TryAdd(kv.Key, kv.Value);
            }
        }
        public int Count => _exact.Count;
        public string? Get(string key) =>
            _exact.TryGetValue(key, out var v) ? v : _folded.TryGetValue(key, out var f) ? f : null;
    }

    private static string? ReadText(string file)
    {
        try { var p = Path.Combine(CacheDir, file); return File.Exists(p) ? File.ReadAllText(p) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { CrashReporter.Log($"[UltraPlusData] {file} — {ex.Message}"); return null; }
    }

    private static T? Read<T>(string file) where T : class
    {
        var text = ReadText(file);
        if (text == null) return null;
        try { return JsonSerializer.Deserialize<T>(text, Json); }
        catch (JsonException ex) { CrashReporter.Log($"[UltraPlusData] {file} — {ex.Message}"); return null; }
    }
}
