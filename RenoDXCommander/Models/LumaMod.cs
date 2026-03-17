namespace RenoDXCommander.Models;

public class LumaMod
{
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public string? DownloadUrl { get; set; }
    public string Status { get; set; } = "✅";
    public string? SpecialNotes { get; set; }
    public string? FeatureNotes { get; set; }
}
