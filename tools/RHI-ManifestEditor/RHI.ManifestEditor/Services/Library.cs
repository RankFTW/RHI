using System.Text.Json;

namespace RHI.ManifestEditor.Services;

public sealed record LibraryGame(string Name, string Source);

/// <summary>
/// Game names from the local RHI library (%LocalAppData%\RHI\game_library.json).
/// Manifest keys must match the detected name exactly, so these are the names to pick from.
/// </summary>
public static class Library
{
    public static List<LibraryGame> Load()
    {
        var result = new List<LibraryGame>();
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "game_library.json");
            if (!File.Exists(path)) return result;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var listName in new[] { "Games", "ManualGames" })
            {
                if (!doc.RootElement.TryGetProperty(listName, out var list) || list.ValueKind != JsonValueKind.Array) continue;
                foreach (var g in list.EnumerateArray())
                {
                    var name = g.TryGetProperty("Name", out var n) ? n.GetString() : null;
                    var src = g.TryGetProperty("Source", out var s) ? s.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(name) && seen.Add(name))
                        result.Add(new LibraryGame(name, src ?? ""));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[Library.Load] {ex.Message}");
        }
        return result;
    }

    /// <summary>Lower-case letters and digits only, for "did you mean" matching.</summary>
    public static string Loose(string s)
    {
        var chars = s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray();
        return new string(chars);
    }
}
