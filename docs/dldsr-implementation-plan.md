# DLDSR Control Implementation Plan

**Date:** October 8, 2026  
**Purpose:** Enable DLDSR factors (1.78x, 2.25x) from within RHI  
**Target Audience:** Implementing agent/developer  
**Prerequisites:** Review `g:\RDXC\docs\dldsr-control-research.md` for technical background

---

## Executive Summary

**The Feature:** A settings panel in RHI to enable/disable DLDSR 1.78x and 2.25x — the same thing users currently do in NVIDIA Control Panel, but without leaving RHI.

**Why:** Once DLDSR is enabled, the virtual resolutions (e.g., 5160x2160) appear in RHI's resolution picker. The existing resolution auto-toggle feature handles everything else.

**How:** Capture/replay approach — capture registry state when DLDSR is configured via NVIDIA CP, replay those bytes to enable/disable DLDSR from RHI.

---

## Scope

### In Scope
- Settings UI to enable/disable DLDSR (Off / 1.78x / 2.25x / Both)
- Capture management (save current NVIDIA CP state, delete captures)
- Apply DLDSR state (writes registry, restarts GPU adapter)
- ~15 second display blackout during apply (unavoidable)

### Out of Scope (Already Implemented)
- Resolution picker — exists in Settings
- Per-game resolution toggle — the RES button
- Resolution switch on game launch — `LaunchGameAsync`
- Resolution restore on game exit — `MonitorProcessForHdr`

---

## Technical Approach

### The Problem
DLDSR cannot be enabled via any public NVIDIA API. The only way is through NVIDIA Control Panel, which writes to a protected registry location.

### The Solution
1. **Capture:** When user configures DLDSR in NVIDIA CP, capture those exact registry bytes
2. **Replay:** Write those bytes back to enable the same DLDSR state
3. **Restart GPU:** The driver reads the new values after adapter restart

### Why This Works
The driver validates registry values but accepts its own previously-written output. Replayed bytes pass validation because they're authentic.

### Requirements
- **SYSTEM-level access** for registry writes (via scheduled task)
- **GPU adapter restart** via `pnputil` (~15 seconds)
- **Admin elevation** for capture (reading registry requires admin)

---

## Registry Details

**Path:** `HKLM:\SYSTEM\CurrentControlSet\Services\nvlddmkm\State\DisplayDatabase\{MonitorId}\`

**Key Values:**

| Value | Size | Purpose |
|-------|------|---------|
| `SmoothScalingData` | 32 bytes | Metadata: smoothness %, factor count, checksum |
| `SmoothScalingMultiplierData` | 280 bytes | Factor definitions: 10 slots × 28 bytes |

**Factor Slot Structure (28 bytes):**
```
Offset  Field
0x00    Magic (0x000002db)
0x04    Slot size (0x0000001c = 28)
0x08    X multiplier × 10000 (e.g., 15000 = 1.5x for DLDSR 2.25x)
0x0C    Y multiplier × 10000 (same as X)
0x10    Flags (0x00000000)
0x14    isDLDSR (0x01 = DLDSR, 0x00 = DSR)
0x18    Checksum
```

**DLDSR Factor Values:**
- **2.25x:** X/Y = 15000 (√2.25 = 1.5 per axis)
- **1.78x:** X/Y = 13333 (√1.78 ≈ 1.333 per axis)

---

## Implementation

### Files to Create

```
g:\RDXC\RenoDXCommander\Services\
├── DldsrService.cs           # Main service (capture, apply, state reading)
├── IDldsrService.cs          # Interface
└── Resources\
    └── dldsr-apply.ps1       # Embedded PowerShell script for SYSTEM execution
```

### 1. `IDldsrService.cs`

```csharp
namespace RenoDXCommander.Services;

public record DldsrCapture(string Label, DateTime Time, string Driver, List<DldsrMonitorCapture> Monitors);
public record DldsrMonitorCapture(string MonitorId, Dictionary<string, string> Values);
public record DldsrState(string MonitorId, List<string> EnabledFactors, int Smoothness, string? MatchesCapture);

public interface IDldsrService
{
    /// <summary>Get current DLDSR state from registry.</summary>
    List<DldsrState> GetCurrentState();
    
    /// <summary>Load all saved captures.</summary>
    List<DldsrCapture> LoadCaptures();
    
    /// <summary>Capture current state with given label.</summary>
    void CaptureCurrentState(string label);
    
    /// <summary>Delete a capture by label.</summary>
    void DeleteCapture(string label);
    
    /// <summary>Apply a capture (writes registry, restarts GPU). Returns success.</summary>
    Task<bool> ApplyStateAsync(string label, IProgress<string>? progress = null);
    
    /// <summary>Path to captures file.</summary>
    string CapturesFilePath { get; }
}
```

### 2. `DldsrService.cs`

**Key implementation points:**

```csharp
public class DldsrService : IDldsrService
{
    private const string BasePath = @"SYSTEM\CurrentControlSet\Services\nvlddmkm\State\DisplayDatabase";
    
    public string CapturesFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RHI", "dldsr-captures.json");

    public List<DldsrState> GetCurrentState()
    {
        // 1. Open HKLM\...\DisplayDatabase
        // 2. Enumerate subkeys (monitor IDs)
        // 3. For each monitor with SmoothScalingData:
        //    - Parse SmoothScalingMultiplierData to extract enabled factors
        //    - Parse SmoothScalingData to get smoothness %
        //    - Compare against saved captures to find match
        // 4. Return list of states
    }
    
    public void CaptureCurrentState(string label)
    {
        // 1. Read all values from all monitor subkeys
        // 2. Convert byte[] to hex strings
        // 3. Create DldsrCapture with current driver version
        // 4. Append to captures file
    }
    
    public async Task<bool> ApplyStateAsync(string label, IProgress<string>? progress = null)
    {
        // 1. Find capture by label
        var capture = LoadCaptures().FirstOrDefault(c => c.Label == label);
        if (capture == null) return false;
        
        // 2. Write capture to temp file for PowerShell script
        var stateFile = Path.Combine(Path.GetTempPath(), "rhi-dldsr-state.json");
        File.WriteAllText(stateFile, JsonSerializer.Serialize(capture));
        
        // 3. Execute via scheduled task (SYSTEM privilege)
        progress?.Report("Writing DLDSR configuration...");
        if (!await ExecuteAsSystemAsync(stateFile)) return false;
        
        // 4. Restart GPU adapter
        progress?.Report("Restarting GPU adapter...");
        if (!await RestartGpuAsync()) return false;
        
        progress?.Report("Waiting for displays...");
        await Task.Delay(12000);
        
        return true;
    }
    
    private async Task<bool> ExecuteAsSystemAsync(string stateFile)
    {
        // Register and run scheduled task with -User SYSTEM
        // Task executes dldsr-apply.ps1 with stateFile as argument
    }
    
    private async Task<bool> RestartGpuAsync()
    {
        // 1. Find NVIDIA GPU: query Win32_VideoController for VEN_10DE
        // 2. pnputil /disable-device "PCI\VEN_10DE&..."
        // 3. Wait 2 seconds
        // 4. pnputil /enable-device "PCI\VEN_10DE&..."
        // 5. Return success
    }
}
```

### 3. `dldsr-apply.ps1` (Embedded Resource)

```powershell
param([string]$StateFile)
$ErrorActionPreference = 'Stop'
$capture = Get-Content $StateFile -Raw | ConvertFrom-Json
$db = 'HKLM:\SYSTEM\CurrentControlSet\Services\nvlddmkm\State\DisplayDatabase'

foreach ($mon in $capture.Monitors) {
    $path = Join-Path $db $mon.MonitorId
    if (-not (Test-Path $path)) { continue }
    
    foreach ($prop in $mon.Values.PSObject.Properties) {
        $bytes = [byte[]]($prop.Value -split ',' | ForEach-Object { [Convert]::ToByte($_, 16) })
        Set-ItemProperty -Path $path -Name $prop.Name -Value $bytes -Type Binary
    }
}
exit 0
```

### 4. Settings UI

**Add to `MainWindow.xaml`** (in the Resolution Control card or as a new card):

```xml
<!-- DLDSR Control -->
<StackPanel Spacing="12" Margin="0,16,0,0">
    <TextBlock Text="DLDSR (Deep Learning Super Resolution)" FontSize="13" 
               Foreground="{StaticResource PrimaryTextBrush}"/>
    <TextBlock FontSize="11" TextWrapping="Wrap" Opacity="0.7"
               Foreground="{StaticResource InlineDescriptionBrush}"
               Text="Enable DLDSR to access higher virtual resolutions. Requires ~15 second display blackout."/>
    
    <Grid ColumnDefinitions="*,Auto" ColumnSpacing="12">
        <ComboBox x:Name="DldsrStateCombo" x:FieldModifier="internal"
                  FontSize="12" HorizontalAlignment="Stretch">
            <!-- Populated from saved captures -->
        </ComboBox>
        <Button x:Name="DldsrApplyBtn" x:FieldModifier="internal"
                Content="Apply" FontSize="12"
                Click="DldsrApplyBtn_Click"/>
    </Grid>
    
    <StackPanel Orientation="Horizontal" Spacing="8">
        <Button x:Name="DldsrCaptureBtn" x:FieldModifier="internal"
                Content="Capture Current" FontSize="11"
                Click="DldsrCaptureBtn_Click"
                ToolTipService.ToolTip="Save the current DLDSR state from NVIDIA Control Panel"/>
        <Button x:Name="DldsrDeleteBtn" x:FieldModifier="internal"
                Content="Delete" FontSize="11"
                Click="DldsrDeleteBtn_Click"/>
    </StackPanel>
</StackPanel>
```

### 5. Event Handlers

**Add to `MainWindow.Events.Settings.cs`:**

```csharp
private async void DldsrApplyBtn_Click(object sender, RoutedEventArgs e)
{
    if (DldsrStateCombo.SelectedItem is not string label || string.IsNullOrEmpty(label)) return;
    
    var dldsrService = App.Services.GetRequiredService<IDldsrService>();
    
    // Show progress
    DldsrApplyBtn.IsEnabled = false;
    DldsrApplyBtn.Content = "Applying...";
    
    var result = await dldsrService.ApplyStateAsync(label, new Progress<string>(msg =>
    {
        DispatcherQueue.TryEnqueue(() => DldsrApplyBtn.Content = msg);
    }));
    
    DldsrApplyBtn.Content = "Apply";
    DldsrApplyBtn.IsEnabled = true;
    
    if (result)
    {
        // Refresh resolution list — new resolutions now available
        var resolutions = ResolutionToggleService.GetSupportedResolutions();
        ResolutionTargetCombo.ItemsSource = resolutions;
    }
    else
    {
        await ShowErrorDialogAsync("Failed to apply DLDSR configuration. Check logs.");
    }
}

private async void DldsrCaptureBtn_Click(object sender, RoutedEventArgs e)
{
    // Prompt for label
    var label = await ShowInputDialogAsync("Capture DLDSR State", "Enter a name:", "e.g., dldsr-225");
    if (string.IsNullOrEmpty(label)) return;
    
    var dldsrService = App.Services.GetRequiredService<IDldsrService>();
    dldsrService.CaptureCurrentState(label);
    RefreshDldsrCombo();
}

private void DldsrDeleteBtn_Click(object sender, RoutedEventArgs e)
{
    if (DldsrStateCombo.SelectedItem is not string label) return;
    
    var dldsrService = App.Services.GetRequiredService<IDldsrService>();
    dldsrService.DeleteCapture(label);
    RefreshDldsrCombo();
}

private void RefreshDldsrCombo()
{
    var dldsrService = App.Services.GetRequiredService<IDldsrService>();
    var captures = dldsrService.LoadCaptures();
    DldsrStateCombo.ItemsSource = captures.Select(c => c.Label).ToList();
}
```

### 6. DI Registration

**Add to `App.xaml.cs`:**

```csharp
services.AddSingleton<IDldsrService, DldsrService>();
```

### 7. Initialize in SettingsHandler

**Add to `SettingsHandler.InitSettings()`:**

```csharp
if (FeatureFlags.ResolutionControl)
{
    _window.RefreshDldsrCombo();
}
```

---

## User Workflow

### First-Time Setup
1. Open NVIDIA Control Panel
2. Enable DLDSR 2.25x (and/or 1.78x), set smoothness
3. Apply and wait for displays
4. In RHI Settings, click "Capture Current", name it "dldsr-on"
5. Back to NVIDIA CP, disable DLDSR
6. In RHI Settings, click "Capture Current", name it "dldsr-off"

### Regular Use
1. Open RHI Settings
2. Select "dldsr-on" from dropdown
3. Click Apply
4. Wait ~15 seconds
5. DLDSR resolutions now appear in resolution picker
6. Select desired resolution, enable RES toggle on games

### Switching Off
1. Select "dldsr-off" from dropdown
2. Click Apply
3. DLDSR resolutions disappear

---

## File Locations

| File | Location |
|------|----------|
| Captures JSON | `%LocalAppData%\RHI\dldsr-captures.json` |
| Apply script | `%LocalAppData%\RHI\dldsr-apply.ps1` (extracted from embedded resource) |
| Temp state file | `%TEMP%\rhi-dldsr-state.json` (deleted after use) |

---

## POC Reference

Working proof-of-concept at `C:\rhi-dldsr\`:

| File | Purpose | Port To |
|------|---------|---------|
| `DldsrControl.psm1` | All logic | `DldsrService.cs` |
| `Capture-DsrState.ps1` | Capture helper | `CaptureCurrentState()` |
| `apply.ps1` | Task wrapper | `dldsr-apply.ps1` |
| `dsr-captures.json` | Storage format | Same format |

Key functions to port:
- `Get-DldsrState` → `GetCurrentState()`
- `Set-DldsrState` → `ApplyStateAsync()`
- `Restart-NvidiaAdapter` → `RestartGpuAsync()`
- `ConvertFrom-SmoothScalingData` → Parsing helper
- `ConvertFrom-ScalingMultiplierData` → Parsing helper

---

## Estimated Effort

| Task | Time |
|------|------|
| DldsrService.cs (capture, state reading) | 1 day |
| DldsrService.cs (apply via scheduled task) | 1 day |
| GPU restart via pnputil | 0.5 day |
| Settings UI | 0.5 day |
| Testing and polish | 1 day |
| **Total** | **4 days** |

---

## Risks

| Risk | Mitigation |
|------|------------|
| GPU restart fails | Timeout + manual recovery instructions |
| Registry format changes | Store driver version; warn on mismatch |
| Captures are monitor-specific | Document this; captures only work on same monitor setup |
| User lacks admin | Feature requires admin; show appropriate message |

---

*End of Implementation Plan*
