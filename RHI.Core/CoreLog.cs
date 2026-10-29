namespace RenoDXCommander.Services;

/// <summary>Diagnostic sink supplied by the host; the shared core has no UI dependency.</summary>
public static class CoreLog
{
    public static Action<string>? Sink { get; set; }
    public static void Log(string message) => Sink?.Invoke(message);
}
