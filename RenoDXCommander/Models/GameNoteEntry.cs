using System.Text.Json.Serialization;

namespace RenoDXCommander.Models;

public class GameNoteEntry
{
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("notesUrl")]
    public string? NotesUrl { get; set; }

    [JsonPropertyName("notesUrlLabel")]
    public string? NotesUrlLabel { get; set; }
}
