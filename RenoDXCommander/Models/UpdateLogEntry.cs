namespace RenoDXCommander.Models;

public record UpdateLogEntry
{
    public DateTime Timestamp     { get; init; }
    public string   Category      { get; init; } = "";
    public string   ComponentName { get; init; } = "";
    public string?  OldVersion    { get; init; }
    public string   NewVersion    { get; init; } = "";
    public long?    SizeBytes     { get; init; }
}
