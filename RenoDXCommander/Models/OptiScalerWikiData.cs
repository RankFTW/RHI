namespace RenoDXCommander.Models;

public class OptiScalerCompatEntry
{
    public string GameName { get; init; } = "";
    public string Status { get; init; } = "";
    public List<string> Upscalers { get; init; } = [];
    public string? Notes { get; init; }
    public string? DetailPageUrl { get; init; }
}

public class OptiScalerWikiData
{
    public Dictionary<string, OptiScalerCompatEntry> StandardCompat { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, OptiScalerCompatEntry> Fsr4Compat { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
