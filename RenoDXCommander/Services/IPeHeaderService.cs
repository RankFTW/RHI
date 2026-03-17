namespace RenoDXCommander.Services;

/// <summary>
/// Reads PE headers to detect executable architecture.
/// </summary>
public interface IPeHeaderService
{
    MachineType DetectArchitecture(string exePath);

    string? FindGameExe(string installPath);

    MachineType DetectGameArchitecture(string installPath);
}
