namespace RenoDXCommander.Services;

public interface IAutoHdrService
{
    /// <summary>
    /// Resolves the full exe path for Auto HDR registry writes.
    /// Priority: user override → manifest override → largest exe in installPath.
    /// </summary>
    string? ResolveExePath(string gameName, string installPath,
        Dictionary<string, string>? userLaunchExeOverrides,
        Dictionary<string, string>? manifestLaunchExeOverrides);

    /// <summary>Returns whether Auto HDR is currently forced for the given exe path.</summary>
    bool IsEnabled(string exePath);

    /// <summary>Returns the current AutoHDRStrength (0–100), or -1 if not set.</summary>
    int GetStrength(string exePath);

    /// <summary>Enables Auto HDR for the exe with the given strength (0–100).</summary>
    bool Enable(string exePath, int strength = 50);

    /// <summary>Disables Auto HDR for the exe, preserving unrelated GPU preference flags.</summary>
    bool Disable(string exePath);

    /// <summary>Updates AutoHDRStrength without changing the enable state.</summary>
    bool SetStrength(string exePath, int strength);
}
