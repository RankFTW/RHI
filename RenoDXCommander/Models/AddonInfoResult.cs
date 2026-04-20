namespace RenoDXCommander.Models;

public class AddonInfoResult
{
    public string Content { get; init; } = "";
    public string? Url { get; init; }
    public string? UrlLabel { get; init; }
    public InfoSourceType Source { get; init; } = InfoSourceType.None;

    // RenoDX-specific: wiki status badge data
    public string? WikiStatusLabel { get; init; }
    public string? WikiStatusBadgeBg { get; init; }
    public string? WikiStatusBadgeFg { get; init; }
    public string? WikiStatusBadgeBorder { get; init; }

    // OptiScaler-specific: structured compatibility data
    public OptiScalerCompatEntry? OptiScalerCompat { get; init; }
    public OptiScalerCompatEntry? OptiScalerFsr4Compat { get; init; }
}
