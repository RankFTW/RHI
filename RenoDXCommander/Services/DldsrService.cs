// DldsrService.cs — DLDSR (Deep Learning Dynamic Super Resolution) control via registry capture/replay.
// Ports logic from C:\rhi-dldsr\DldsrControl.psm1 POC.
//
// Registry path: HKLM:\SYSTEM\CurrentControlSet\Services\nvlddmkm\State\DisplayDatabase\{MonitorId}\
// Key values:
//   - SmoothScalingData (32 bytes): metadata, smoothness%, factor count, checksum
//   - SmoothScalingMultiplierData (280 bytes): 10 slots × 28 bytes for factor definitions
//
// The driver validates registry values but accepts its own previously-written output.
// We capture state configured via NVIDIA Control Panel, then replay those exact bytes.

using System.Diagnostics;
using System.Management;
using System.Text.Json;
using Microsoft.Win32;

namespace RenoDXCommander.Services;

public class DldsrService : IDldsrService
{
    private const string DisplayDbPath = @"SYSTEM\CurrentControlSet\Services\nvlddmkm\State\DisplayDatabase";
    private const string SmoothScalingDataName = "SmoothScalingData";
    private const string SmoothScalingMultiplierDataName = "SmoothScalingMultiplierData";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string CapturesFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "dldsr-captures.json");

    public bool HasCaptures => File.Exists(CapturesFilePath) && LoadCaptures().Count > 0;

    // ── State Reading ─────────────────────────────────────────────────────────

    public List<DldsrState> GetCurrentState()
    {
        var states = new List<DldsrState>();
        var captures = LoadCaptures();

        try
        {
            using var baseKey = Registry.LocalMachine.OpenSubKey(DisplayDbPath);
            if (baseKey == null)
            {
                CrashReporter.Log("[DldsrService.GetCurrentState] DisplayDatabase key not found");
                return states;
            }

            foreach (var monitorId in baseKey.GetSubKeyNames())
            {
                using var monKey = baseKey.OpenSubKey(monitorId);
                if (monKey == null) continue;

                // Check if this monitor has DSR/DLDSR data
                var smoothData = monKey.GetValue(SmoothScalingDataName) as byte[];
                var multData = monKey.GetValue(SmoothScalingMultiplierDataName) as byte[];
                if (smoothData == null || multData == null) continue;

                // Parse the data
                var smoothParsed = ParseSmoothScalingData(smoothData);
                var factors = ParseScalingMultiplierData(multData);

                // Check which captures match this exact state
                var smoothHex = BytesToHex(smoothData);
                var multHex = BytesToHex(multData);
                var matchingCaptures = new List<string>();

                foreach (var cap in captures)
                {
                    var mon = cap.Monitors.FirstOrDefault(m => m.MonitorId == monitorId);
                    if (mon == null) continue;

                    mon.Values.TryGetValue(SmoothScalingDataName, out var capSmoothHex);
                    mon.Values.TryGetValue(SmoothScalingMultiplierDataName, out var capMultHex);

                    if (capSmoothHex == smoothHex && capMultHex == multHex)
                        matchingCaptures.Add(cap.Label);
                }

                states.Add(new DldsrState(
                    monitorId,
                    factors.Count > 0 ? string.Join(", ", factors) : null,
                    smoothParsed.Smoothness,
                    matchingCaptures.Count > 0 ? string.Join(", ", matchingCaptures) : null
                ));
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Admin required to read these keys - return empty state
            CrashReporter.Log("[DldsrService.GetCurrentState] Access denied - admin required to read DLDSR state");
            return states;
        }
        catch (System.Security.SecurityException)
        {
            CrashReporter.Log("[DldsrService.GetCurrentState] Security exception - admin required to read DLDSR state");
            return states;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DldsrService.GetCurrentState] Error: {ex.Message}");
        }

        return states;
    }

    public string? GetCurrentDriverVersion()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DriverVersion FROM Win32_VideoController WHERE PNPDeviceID LIKE 'PCI\\\\VEN_10DE%'");
            foreach (ManagementObject obj in searcher.Get())
            {
                return obj["DriverVersion"]?.ToString();
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DldsrService.GetCurrentDriverVersion] Error: {ex.Message}");
        }
        return null;
    }

    // ── Capture Management ────────────────────────────────────────────────────

    public List<DldsrCapture> LoadCaptures()
    {
        if (!File.Exists(CapturesFilePath))
            return new List<DldsrCapture>();

        try
        {
            var json = File.ReadAllText(CapturesFilePath);
            var captures = JsonSerializer.Deserialize<List<DldsrCapture>>(json, _jsonOptions);
            return captures ?? new List<DldsrCapture>();
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DldsrService.LoadCaptures] Error reading captures: {ex.Message}");
            return new List<DldsrCapture>();
        }
    }

    public void CaptureCurrentState(string label)
    {
        CrashReporter.Log($"[DldsrService.CaptureCurrentState] Capturing state as '{label}'");

        var monitors = new List<DldsrMonitorCapture>();

        try
        {
            using var baseKey = Registry.LocalMachine.OpenSubKey(DisplayDbPath);
            if (baseKey == null)
            {
                CrashReporter.Log("[DldsrService.CaptureCurrentState] DisplayDatabase key not found");
                throw new InvalidOperationException("NVIDIA display database not found in registry. Is an NVIDIA GPU installed?");
            }

            foreach (var monitorId in baseKey.GetSubKeyNames())
            {
                using var monKey = baseKey.OpenSubKey(monitorId);
                if (monKey == null) continue;

                var values = new Dictionary<string, string>();
                foreach (var valueName in monKey.GetValueNames())
                {
                    var val = monKey.GetValue(valueName);
                    if (val is byte[] bytes)
                        values[valueName] = BytesToHex(bytes);
                    else if (val != null)
                        values[valueName] = val.ToString() ?? "";
                }

                if (values.Count > 0)
                    monitors.Add(new DldsrMonitorCapture(monitorId, values));
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            CrashReporter.Log($"[DldsrService.CaptureCurrentState] Access denied: {ex.Message}");
            throw new UnauthorizedAccessException(
                "Cannot read DLDSR registry keys. Admin privileges are required.\n\n" +
                "Please enable Admin Mode in RHI Settings or run RHI as Administrator.");
        }
        catch (System.Security.SecurityException ex)
        {
            CrashReporter.Log($"[DldsrService.CaptureCurrentState] Security exception: {ex.Message}");
            throw new UnauthorizedAccessException(
                "Cannot read DLDSR registry keys. Admin privileges are required.\n\n" +
                "Please enable Admin Mode in RHI Settings or run RHI as Administrator.");
        }

        if (monitors.Count == 0)
        {
            CrashReporter.Log("[DldsrService.CaptureCurrentState] No monitor keys with values found");
            throw new InvalidOperationException(
                "No DLDSR/DSR data found in registry.\n\n" +
                "Please enable at least one DSR or DLDSR factor in NVIDIA Control Panel first, then capture.");
        }

        var capture = new DldsrCapture(label, DateTime.Now, GetCurrentDriverVersion(), monitors);

        // Load existing, append, save
        var all = LoadCaptures();
        // Remove any existing capture with the same label
        all.RemoveAll(c => c.Label.Equals(label, StringComparison.OrdinalIgnoreCase));
        all.Add(capture);

        SaveCaptures(all);
        CrashReporter.Log($"[DldsrService.CaptureCurrentState] Saved '{label}' with {monitors.Count} monitors");
    }

    public void DeleteCapture(string label)
    {
        var all = LoadCaptures();
        var removed = all.RemoveAll(c => c.Label.Equals(label, StringComparison.OrdinalIgnoreCase));
        if (removed > 0)
        {
            SaveCaptures(all);
            CrashReporter.Log($"[DldsrService.DeleteCapture] Deleted '{label}'");
        }
    }

    private void SaveCaptures(List<DldsrCapture> captures)
    {
        var dir = Path.GetDirectoryName(CapturesFilePath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(captures, _jsonOptions);
        File.WriteAllText(CapturesFilePath, json);
    }

    // ── Apply State ───────────────────────────────────────────────────────────

    // ProgramData folder for SYSTEM-accessible files
    private static readonly string ProgramDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "RHI");

    public async Task<bool> ApplyStateAsync(string label, IProgress<string>? progress = null, int? smoothnessOverride = null)
    {
        CrashReporter.Log($"[DldsrService.ApplyStateAsync] Applying state '{label}'" + (smoothnessOverride.HasValue ? $" with smoothness override {smoothnessOverride}%" : ""));
        progress?.Report("Loading capture...");
        var captures = LoadCaptures();
        var capture = captures.FirstOrDefault(c => c.Label.Equals(label, StringComparison.OrdinalIgnoreCase));
        if (capture == null)
        {
            CrashReporter.Log($"[DldsrService.ApplyStateAsync] Capture '{label}' not found");
            return false;
        }

        // Check driver version
        var currentDriver = GetCurrentDriverVersion();
        if (!string.IsNullOrEmpty(capture.Driver) && !string.IsNullOrEmpty(currentDriver)
            && capture.Driver != currentDriver)
        {
            CrashReporter.Log($"[DldsrService.ApplyStateAsync] Warning: capture driver {capture.Driver} != current {currentDriver}");
            // Continue anyway — the user can recapture if needed
        }

        // Write capture to ProgramData for SYSTEM-accessible path (user temp is inaccessible to SYSTEM)
        progress?.Report("Preparing DLDSR configuration...");
        Directory.CreateDirectory(ProgramDataDir);
        var stateFile = Path.Combine(ProgramDataDir, "dldsr-state.json");
        try
        {
            // If a smoothness override is provided, bake it into the capture blobs before writing
            DldsrCapture captureToApply = capture;
            if (smoothnessOverride.HasValue)
            {
                var modifiedMonitors = capture.Monitors.Select(mon =>
                {
                    if (!mon.Values.TryGetValue(SmoothScalingDataName, out var hex)) return mon;
                    try
                    {
                        var bytes = (byte[])hex.Split(',').Select(b => Convert.ToByte(b.Trim(), 16)).ToArray().Clone();
                        if (bytes.Length == 32)
                        {
                            var sb = BitConverter.GetBytes((uint)smoothnessOverride.Value);
                            Array.Copy(sb, 0, bytes, 12, 4);
                            uint checksum = 0;
                            for (int i = 0; i < 28; i++) checksum += bytes[i];
                            var cb = BitConverter.GetBytes(checksum);
                            Array.Copy(cb, 0, bytes, 28, 4);
                            var newValues = new Dictionary<string, string>(mon.Values)
                            {
                                [SmoothScalingDataName] = string.Join(",", bytes.Select(b => b.ToString("X2")))
                            };
                            return new DldsrMonitorCapture(mon.MonitorId, newValues);
                        }
                    }
                    catch { }
                    return mon;
                }).ToList();
                captureToApply = new DldsrCapture(capture.Label, capture.Time, capture.Driver, modifiedMonitors);
                CrashReporter.Log($"[DldsrService.ApplyStateAsync] Baked smoothness {smoothnessOverride}% into capture");
            }

            var stateJson = JsonSerializer.Serialize(captureToApply, _jsonOptions);
            await File.WriteAllTextAsync(stateFile, stateJson);
            CrashReporter.Log($"[DldsrService.ApplyStateAsync] Wrote state file to {stateFile}");
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DldsrService.ApplyStateAsync] Failed to write state file: {ex.Message}");
            return false;
        }

        // Execute registry writes via scheduled task (SYSTEM privilege)
        progress?.Report("Writing DLDSR configuration...");
        if (!await ExecuteAsSystemAsync(stateFile))
        {
            CrashReporter.Log("[DldsrService.ApplyStateAsync] Failed to execute registry writes");
            return false;
        }

        // Restart GPU adapter
        progress?.Report("Restarting GPU adapter...");
        if (!await RestartGpuAdapterAsync())
        {
            CrashReporter.Log("[DldsrService.ApplyStateAsync] Failed to restart GPU adapter");
            return false;
        }

        // Wait for displays to come back
        progress?.Report("Waiting for displays...");
        await Task.Delay(12000);

        CrashReporter.Log($"[DldsrService.ApplyStateAsync] Successfully applied '{label}'");
        return true;
    }

    private async Task<bool> ExecuteAsSystemAsync(string stateFile)
    {
        // Create the PowerShell script in ProgramData (SYSTEM-accessible)
        var scriptPath = Path.Combine(ProgramDataDir, "dldsr-apply.ps1");

        var scriptContent = GetApplyScript();
        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
        await File.WriteAllTextAsync(scriptPath, scriptContent);
        CrashReporter.Log($"[DldsrService.ExecuteAsSystemAsync] Wrote apply script to {scriptPath}");

        var taskName = "RHI-DLDSR-Apply";

        try
        {
            // Delete any existing task
            await RunProcessAsync("schtasks", $"/Delete /TN \"{taskName}\" /F", ignoreExitCode: true);

            // Create the scheduled task to run as SYSTEM
            var createArgs = $"/Create /TN \"{taskName}\" " +
                           $"/TR \"powershell.exe -ExecutionPolicy Bypass -NoProfile -File \\\"{scriptPath}\\\" -StateFile \\\"{stateFile}\\\"\" " +
                           "/SC ONCE /ST 00:00 /RU SYSTEM /RL HIGHEST /F";

            var (createExitCode, createOutput) = await RunProcessAsync("schtasks", createArgs);
            if (createExitCode != 0)
            {
                CrashReporter.Log($"[DldsrService.ExecuteAsSystemAsync] Failed to create task: {createOutput}");
                return false;
            }

            // Run the task
            var (runExitCode, runOutput) = await RunProcessAsync("schtasks", $"/Run /TN \"{taskName}\"");
            if (runExitCode != 0)
            {
                CrashReporter.Log($"[DldsrService.ExecuteAsSystemAsync] Failed to run task: {runOutput}");
                return false;
            }

            // Wait for the task to complete
            await Task.Delay(3000);

            // Check task result
            for (int i = 0; i < 10; i++)
            {
                var (queryExitCode, queryOutput) = await RunProcessAsync("schtasks", $"/Query /TN \"{taskName}\" /V /FO LIST");
                if (queryOutput.Contains("Last Result:") && 
                    (queryOutput.Contains("Last Result:                   0") || queryOutput.Contains("Last Result:                   The operation completed successfully")))
                {
                    CrashReporter.Log("[DldsrService.ExecuteAsSystemAsync] Task completed successfully");
                    break;
                }
                if (queryOutput.Contains("Status:") && queryOutput.Contains("Running"))
                {
                    await Task.Delay(1000);
                    continue;
                }
                if (i == 9)
                {
                    CrashReporter.Log($"[DldsrService.ExecuteAsSystemAsync] Task may not have completed: {queryOutput}");
                }
            }

            return true;
        }
        finally
        {
            // Cleanup: delete the scheduled task
            await RunProcessAsync("schtasks", $"/Delete /TN \"{taskName}\" /F", ignoreExitCode: true);
        }
    }

    private string GetApplyScript()
    {
        // This script runs as SYSTEM and writes the registry values
        // Uses ProgramData instead of LOCALAPPDATA since SYSTEM can't access user folders
        return @"
param([Parameter(Mandatory)][string]$StateFile)
$ErrorActionPreference = 'Stop'

$logFile = 'C:\ProgramData\RHI\dldsr-apply.log'
function Log($msg) { 
    $dir = Split-Path $logFile -Parent
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    ""$(Get-Date -Format 'HH:mm:ss.fff') $msg"" | Out-File $logFile -Append -Encoding UTF8 
}

try {
    Log ""Starting DLDSR apply from $StateFile""
    
    if (-not (Test-Path $StateFile)) {
        Log ""ERROR: State file not found: $StateFile""
        exit 1
    }
    
    $capture = Get-Content $StateFile -Raw | ConvertFrom-Json
    Log ""Loaded capture with $($capture.monitors.Count) monitors""
    
    $db = 'HKLM:\SYSTEM\CurrentControlSet\Services\nvlddmkm\State\DisplayDatabase'
    if (-not (Test-Path $db)) {
        Log ""ERROR: DisplayDatabase path not found: $db""
        exit 1
    }

    foreach ($mon in $capture.monitors) {
        $path = Join-Path $db $mon.monitorId
        if (-not (Test-Path $path)) {
            Log ""Monitor path not found, skipping: $path""
            continue
        }
        Log ""Processing monitor: $($mon.monitorId)""

        foreach ($prop in $mon.values.PSObject.Properties) {
            $name = $prop.Name
            $hexVal = $prop.Value
            
            # Skip non-hex values (like numeric strings)
            if ($hexVal -notmatch '^[0-9a-fA-F,]+$') {
                # Try to set as string/dword
                try {
                    if ($hexVal -match '^\d+$') {
                        Set-ItemProperty -Path $path -Name $name -Value ([int]$hexVal) -ErrorAction SilentlyContinue
                        Log ""Set $name = $hexVal (dword)""
                    } else {
                        Set-ItemProperty -Path $path -Name $name -Value $hexVal -ErrorAction SilentlyContinue
                        Log ""Set $name = $hexVal (string)""
                    }
                } catch {
                    Log ""Failed to set $name (non-binary): $_""
                }
                continue
            }

            try {
                $bytes = [byte[]]($hexVal -split ',' | ForEach-Object { [Convert]::ToByte($_, 16) })
                Set-ItemProperty -LiteralPath $path -Name $name -Value $bytes -Type Binary -ErrorAction Stop
                Log ""Set $name ($($bytes.Length) bytes)""
            } catch {
                Log ""Failed to set $name : $_""
            }
        }
    }
    Log ""DLDSR apply completed successfully""
    exit 0
} catch {
    Log ""FATAL ERROR: $_""
    Log $_.ScriptStackTrace
    exit 1
}
";
    }

    private async Task<bool> RestartGpuAdapterAsync()
    {
        try
        {
            // Find NVIDIA GPU device instance ID
            string? deviceId = null;
            using var searcher = new ManagementObjectSearcher(
                "SELECT PNPDeviceID FROM Win32_VideoController WHERE PNPDeviceID LIKE 'PCI\\\\VEN_10DE%'");
            foreach (ManagementObject obj in searcher.Get())
            {
                deviceId = obj["PNPDeviceID"]?.ToString();
                break;
            }

            if (string.IsNullOrEmpty(deviceId))
            {
                CrashReporter.Log("[DldsrService.RestartGpuAdapterAsync] No NVIDIA GPU found");
                return false;
            }

            CrashReporter.Log($"[DldsrService.RestartGpuAdapterAsync] Restarting device: {deviceId}");

            // Try pnputil /restart-device first
            var (restartExitCode, restartOutput) = await RunProcessAsync("pnputil", $"/restart-device \"{deviceId}\"");
            if (restartExitCode == 0)
            {
                CrashReporter.Log("[DldsrService.RestartGpuAdapterAsync] Device restarted via pnputil");
                return true;
            }

            CrashReporter.Log($"[DldsrService.RestartGpuAdapterAsync] pnputil /restart-device failed ({restartExitCode}), trying disable/enable");

            // Fallback: disable then enable
            var (disableExitCode, _) = await RunProcessAsync("pnputil", $"/disable-device \"{deviceId}\"");
            if (disableExitCode != 0)
            {
                CrashReporter.Log("[DldsrService.RestartGpuAdapterAsync] Failed to disable device");
                return false;
            }

            await Task.Delay(2000);

            var (enableExitCode, _) = await RunProcessAsync("pnputil", $"/enable-device \"{deviceId}\"");
            if (enableExitCode != 0)
            {
                CrashReporter.Log("[DldsrService.RestartGpuAdapterAsync] Failed to enable device");
                return false;
            }

            CrashReporter.Log("[DldsrService.RestartGpuAdapterAsync] Device restarted via disable/enable");
            return true;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DldsrService.RestartGpuAdapterAsync] Error: {ex.Message}");
            return false;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<(int ExitCode, string Output)> RunProcessAsync(
        string fileName, string arguments, bool ignoreExitCode = false)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process == null)
                return (-1, "Failed to start process");

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var combined = output + (string.IsNullOrEmpty(error) ? "" : "\n" + error);
            return (process.ExitCode, combined);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }

    private static string BytesToHex(byte[] bytes)
    {
        return string.Join(",", bytes.Select(b => b.ToString("x2")));
    }

    private static byte[] HexToBytes(string hex)
    {
        return hex.Split(',')
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => Convert.ToByte(s.Trim(), 16))
            .ToArray();
    }

    /// <summary>
    /// Parses SmoothScalingData (32 bytes) to extract smoothness and factor count.
    /// Structure: magic(4) | size(4) | entries(4) | smoothness(4) | factorCount(4) | 0x0a(4) | 0x1c(4) | checksum(4)
    /// </summary>
    private static (bool Valid, int Entries, int Smoothness, int FactorCount) ParseSmoothScalingData(byte[] bytes)
    {
        if (bytes.Length != 32)
            return (false, 0, 0, 0);

        uint magic = BitConverter.ToUInt32(bytes, 0);
        uint size = BitConverter.ToUInt32(bytes, 4);
        if (magic != 0x1db || size != 0x20)
            return (false, 0, 0, 0);

        int entries = (int)BitConverter.ToUInt32(bytes, 8);
        int smoothness = (int)BitConverter.ToUInt32(bytes, 12);
        int factorCount = (int)BitConverter.ToUInt32(bytes, 16);

        // Verify checksum (sum of first 28 bytes)
        uint checksum = BitConverter.ToUInt32(bytes, 28);
        uint computed = 0;
        for (int i = 0; i < 28; i++) computed += bytes[i];
        if (checksum != computed)
            return (false, 0, 0, 0);

        return (true, entries, smoothness, factorCount);
    }

    /// <summary>
    /// Parses SmoothScalingMultiplierData (280 bytes = 10 slots × 28 bytes).
    /// Each slot: magic(4) | size(4) | xMult(4) | yMult(4) | flags(4) | isDLDSR(4) | checksum(4)
    /// Returns list of human-readable factor descriptions for non-empty slots.
    /// </summary>
    private static List<string> ParseScalingMultiplierData(byte[] bytes)
    {
        var factors = new List<string>();
        if (bytes.Length == 0 || bytes.Length % 28 != 0)
            return factors;

        for (int offset = 0; offset < bytes.Length; offset += 28)
        {
            uint magic = BitConverter.ToUInt32(bytes, offset);
            uint size = BitConverter.ToUInt32(bytes, offset + 4);
            if (magic != 0x2db || size != 0x1c)
                continue;

            uint xMult = BitConverter.ToUInt32(bytes, offset + 8);
            uint yMult = BitConverter.ToUInt32(bytes, offset + 12);
            uint isDldsr = BitConverter.ToUInt32(bytes, offset + 20);

            // Empty slot has xMult=0, yMult=0
            if (xMult == 0 && yMult == 0)
                continue;

            // Factor = (x/10000) * (y/10000)
            // For DLDSR 2.25x: x=15000, y=15000 → 1.5*1.5 = 2.25
            // For DLDSR 1.78x: x=13333, y=13333 → 1.333*1.333 ≈ 1.78
            double factor = (xMult / 10000.0) * (yMult / 10000.0);
            string kind = isDldsr == 1 ? "DLDSR" : "DSR";
            factors.Add($"{kind} {factor:F2}x");
        }

        return factors;
    }

    // ── Smoothness Control ────────────────────────────────────────────────────

    public int GetCurrentSmoothness()
    {
        try
        {
            using var baseKey = Registry.LocalMachine.OpenSubKey(DisplayDbPath);
            if (baseKey == null) return -1;

            foreach (var monitorId in baseKey.GetSubKeyNames())
            {
                using var monKey = baseKey.OpenSubKey(monitorId);
                if (monKey == null) continue;

                var smoothData = monKey.GetValue(SmoothScalingDataName) as byte[];
                if (smoothData == null || smoothData.Length != 32) continue;

                var parsed = ParseSmoothScalingData(smoothData);
                if (parsed.Valid)
                {
                    CrashReporter.Log($"[DldsrService.GetCurrentSmoothness] Found smoothness: {parsed.Smoothness}%");
                    return parsed.Smoothness;
                }
            }
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DldsrService.GetCurrentSmoothness] Error: {ex.Message}");
        }
        return -1;
    }

    public async Task<bool> SetSmoothnessAsync(int smoothness, IProgress<string>? progress = null)
    {
        if (smoothness < 0 || smoothness > 100)
        {
            CrashReporter.Log($"[DldsrService.SetSmoothnessAsync] Invalid smoothness value: {smoothness}");
            return false;
        }

        CrashReporter.Log($"[DldsrService.SetSmoothnessAsync] Setting smoothness to {smoothness}%");
        progress?.Report("Reading current DLDSR state...");

        try
        {
            // Read current state from the last applied capture file — NOT from live registry.
            // After a GPU restart, the driver may reset DisplayDatabase to defaults before
            // the next read, which would zero out the DLDSR factor bits. The capture file
            // always contains the correct blob with factors intact.
            var lastStateFile = Path.Combine(ProgramDataDir, "dldsr-state.json");
            DldsrCapture? existingCapture = null;
            try
            {
                if (File.Exists(lastStateFile))
                {
                    var json = await File.ReadAllTextAsync(lastStateFile);
                    existingCapture = JsonSerializer.Deserialize<DldsrCapture>(json, _jsonOptions);
                }
            }
            catch (Exception ex)
            {
                CrashReporter.Log($"[DldsrService.SetSmoothnessAsync] Failed to read state file — {ex.Message}");
            }

            var monitorData = new List<(string MonitorId, byte[] SmoothData, byte[] MultData)>();

            if (existingCapture != null)
            {
                // Use the saved capture as the base — has correct factor bits
                foreach (var mon in existingCapture.Monitors)
                {
                    var smoothHex = mon.Values.TryGetValue(SmoothScalingDataName, out var sh) ? sh : null;
                    var multHex = mon.Values.TryGetValue(SmoothScalingMultiplierDataName, out var mh) ? mh : null;
                    if (smoothHex == null || multHex == null) continue;
                    try
                    {
                        var smoothData = smoothHex.Split(',').Select(b => Convert.ToByte(b.Trim(), 16)).ToArray();
                        var multData = multHex.Split(',').Select(b => Convert.ToByte(b.Trim(), 16)).ToArray();
                        if (smoothData.Length == 32)
                            monitorData.Add((mon.MonitorId, smoothData, multData));
                    }
                    catch { }
                }
            }

            // Fallback to live registry if no state file exists
            if (monitorData.Count == 0)
            {
                using var baseKey = Registry.LocalMachine.OpenSubKey(DisplayDbPath);
                if (baseKey == null)
                {
                    CrashReporter.Log("[DldsrService.SetSmoothnessAsync] DisplayDatabase key not found");
                    return false;
                }

                foreach (var monitorId in baseKey.GetSubKeyNames())
                {
                    using var monKey = baseKey.OpenSubKey(monitorId);
                    if (monKey == null) continue;

                    var smoothData = monKey.GetValue(SmoothScalingDataName) as byte[];
                    var multData = monKey.GetValue(SmoothScalingMultiplierDataName) as byte[];
                    if (smoothData == null || smoothData.Length != 32) continue;
                    if (multData == null) continue;

                    monitorData.Add((monitorId, smoothData, multData));
                }
            }

            if (monitorData.Count == 0)
            {
                CrashReporter.Log("[DldsrService.SetSmoothnessAsync] No monitors with DLDSR data found");
                return false;
            }

            progress?.Report("Preparing smoothness configuration...");

            // Build a capture with modified smoothness
            var monitors = new List<DldsrMonitorCapture>();
            foreach (var (monitorId, smoothData, multData) in monitorData)
            {
                // Clone the data and modify smoothness
                var newSmoothData = (byte[])smoothData.Clone();

                // Set smoothness at bytes 12-15 (uint32)
                var smoothnessBytes = BitConverter.GetBytes((uint)smoothness);
                Array.Copy(smoothnessBytes, 0, newSmoothData, 12, 4);

                // Recalculate checksum (sum of first 28 bytes) at bytes 28-31
                uint checksum = 0;
                for (int i = 0; i < 28; i++) checksum += newSmoothData[i];
                var checksumBytes = BitConverter.GetBytes(checksum);
                Array.Copy(checksumBytes, 0, newSmoothData, 28, 4);

                var values = new Dictionary<string, string>
                {
                    [SmoothScalingDataName] = BytesToHex(newSmoothData),
                    [SmoothScalingMultiplierDataName] = BytesToHex(multData)
                };
                monitors.Add(new DldsrMonitorCapture(monitorId, values));
            }

            var capture = new DldsrCapture($"_smoothness_{smoothness}", DateTime.Now, GetCurrentDriverVersion(), monitors);

            // Write to ProgramData state file
            Directory.CreateDirectory(ProgramDataDir);
            var stateFile = Path.Combine(ProgramDataDir, "dldsr-state.json");
            var stateJson = JsonSerializer.Serialize(capture, _jsonOptions);
            await File.WriteAllTextAsync(stateFile, stateJson);
            CrashReporter.Log($"[DldsrService.SetSmoothnessAsync] Wrote state file to {stateFile}");

            // Execute registry writes via scheduled task (SYSTEM privilege)
            progress?.Report("Writing smoothness configuration...");
            if (!await ExecuteAsSystemAsync(stateFile))
            {
                CrashReporter.Log("[DldsrService.SetSmoothnessAsync] Failed to execute registry writes");
                return false;
            }

            // GPU restart required — the driver reads DisplayDatabase at adapter startup only,
            // not live. nvcontainer.exe updates smoothness live via a privileged IPC channel
            // that is not available to third-party apps.
            progress?.Report("Restarting GPU adapter...");
            if (!await RestartGpuAdapterAsync())
            {
                CrashReporter.Log("[DldsrService.SetSmoothnessAsync] Failed to restart GPU adapter");
                return false;
            }

            progress?.Report("Waiting for displays...");
            await Task.Delay(12000);

            CrashReporter.Log($"[DldsrService.SetSmoothnessAsync] Successfully set smoothness to {smoothness}%");
            return true;
        }
        catch (Exception ex)
        {
            CrashReporter.Log($"[DldsrService.SetSmoothnessAsync] Error: {ex.Message}");
            return false;
        }
    }
}
