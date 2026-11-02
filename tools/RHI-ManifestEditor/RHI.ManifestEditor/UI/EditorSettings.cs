using System.Text.Json;

namespace RHI.ManifestEditor.UI;

/// <summary>Remembers the last file, game and list options between runs.</summary>
public sealed class EditorSettings
{
    public string? LastPath { get; set; }
    public string? LastGame { get; set; }
    public string? LastField { get; set; }
    public string? LastMode { get; set; }
    public bool ShowLibrary { get; set; } = true;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RHI", "ManifestEditor", "editor.json");

    public static EditorSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<EditorSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[EditorSettings.Load] {ex.Message}");
        }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"[EditorSettings.Save] {ex.Message}");
        }
    }
}
