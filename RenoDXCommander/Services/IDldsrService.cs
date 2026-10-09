// IDldsrService.cs — Interface for DLDSR (Deep Learning Dynamic Super Resolution) control.
// Enables/disables DLDSR factors (1.78x, 2.25x) at the registry level via capture/replay.

namespace RenoDXCommander.Services;

/// <summary>
/// A captured DLDSR state snapshot that can be replayed to restore a specific configuration.
/// </summary>
/// <param name="Label">User-assigned name for this capture (e.g., "dldsr-on", "all-off").</param>
/// <param name="Time">When the capture was taken.</param>
/// <param name="Driver">NVIDIA driver version at capture time.</param>
/// <param name="Monitors">Per-monitor registry value snapshots.</param>
public record DldsrCapture(string Label, DateTime Time, string? Driver, List<DldsrMonitorCapture> Monitors);

/// <summary>
/// Registry values for a single monitor's DLDSR configuration.
/// </summary>
/// <param name="MonitorId">Monitor registry key name (e.g., "CND3DE2_03_07E8_53").</param>
/// <param name="Values">Registry value name → hex-encoded bytes (comma-separated).</param>
public record DldsrMonitorCapture(string MonitorId, Dictionary<string, string> Values);

/// <summary>
/// Current DLDSR state for a single monitor, parsed from the registry.
/// </summary>
/// <param name="MonitorId">Monitor registry key name.</param>
/// <param name="EnabledFactors">Human-readable list of enabled factors (e.g., "DLDSR 2.25x, DLDSR 1.78x").</param>
/// <param name="Smoothness">Current smoothness percentage (0-100).</param>
/// <param name="MatchesCapture">Comma-separated labels of captures that match this exact state, or null.</param>
public record DldsrState(string MonitorId, string? EnabledFactors, int Smoothness, string? MatchesCapture);

/// <summary>
/// Service for capturing and replaying DLDSR registry states.
/// DLDSR cannot be enabled via any public NVIDIA API — the only way is to replay
/// previously-captured registry bytes that were written by NVIDIA Control Panel.
/// </summary>
public interface IDldsrService
{
    /// <summary>
    /// Gets the current DLDSR state from the registry for all monitors with DSR/DLDSR data.
    /// </summary>
    /// <returns>List of per-monitor states.</returns>
    List<DldsrState> GetCurrentState();

    /// <summary>
    /// Loads all saved captures from the captures file.
    /// </summary>
    /// <returns>List of all captures, or empty list if file doesn't exist.</returns>
    List<DldsrCapture> LoadCaptures();

    /// <summary>
    /// Captures the current DLDSR registry state with the given label.
    /// The user should configure DLDSR in NVIDIA Control Panel first, then capture.
    /// Requires admin elevation to read the protected registry keys.
    /// </summary>
    /// <param name="label">User-assigned name for this capture.</param>
    void CaptureCurrentState(string label);

    /// <summary>
    /// Deletes a capture by label.
    /// </summary>
    /// <param name="label">The label of the capture to delete.</param>
    void DeleteCapture(string label);

    /// <summary>
    /// Applies a saved capture to the registry and restarts the GPU adapter.
    /// This writes registry values and causes a ~15 second display blackout.
    /// Requires SYSTEM-level access (via scheduled task) for registry writes.
    /// </summary>
    /// <param name="label">The label of the capture to apply.</param>
    /// <param name="progress">Optional progress reporter for status updates.</param>
    /// <returns>True if successful, false otherwise.</returns>
    Task<bool> ApplyStateAsync(string label, IProgress<string>? progress = null, int? smoothnessOverride = null);

    /// <summary>
    /// Path to the captures JSON file (%LocalAppData%\RHI\dldsr-captures.json).
    /// </summary>
    string CapturesFilePath { get; }

    /// <summary>
    /// Checks if the service has any saved captures available.
    /// </summary>
    bool HasCaptures { get; }

    /// <summary>
    /// Gets the current NVIDIA driver version string.
    /// </summary>
    string? GetCurrentDriverVersion();

    /// <summary>
    /// Sets the DSR smoothness percentage (0-100) for all monitors and restarts the GPU.
    /// This modifies bytes 12-15 of SmoothScalingData in the registry.
    /// Requires SYSTEM-level access (via scheduled task) for registry writes.
    /// </summary>
    /// <param name="smoothness">Smoothness percentage (0-100).</param>
    /// <param name="progress">Optional progress reporter for status updates.</param>
    /// <returns>True if successful, false otherwise.</returns>
    Task<bool> SetSmoothnessAsync(int smoothness, IProgress<string>? progress = null);

    /// <summary>
    /// Gets the current DSR smoothness percentage from the first monitor that has DLDSR/DSR data.
    /// </summary>
    /// <returns>Smoothness percentage (0-100), or -1 if not available.</returns>
    int GetCurrentSmoothness();
}
