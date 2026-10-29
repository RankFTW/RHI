namespace RenoDXCommander.Services;

// Shared display identity for manifest selections and detected DLL versions.
public static class DlssVersion
{
    public static string Format(string? rawVersion)
    {
        if (string.IsNullOrEmpty(rawVersion)) return "Unknown";

        // Only trim the last .0 if there are 4 parts and the last part is "0"
        var parts = rawVersion.Split('.');
        if (parts.Length == 4 && parts[3] == "0")
            return $"{parts[0]}.{parts[1]}.{parts[2]}";

        return rawVersion;
    }

}
