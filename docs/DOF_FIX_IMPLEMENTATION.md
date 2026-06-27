# DOF Fix Component — Implementation Guide

## Current State

Branch: `wip-cogs-and-dof`

**Files that EXIST and are correct:**
- `RenoDXCommander/Services/DofFixService.cs` — Full service implementation (untracked, needs `git add`)
- `RenoDXCommander/Services/IDofFixService.cs` — Interface (untracked, needs `git add`)
- `RenoDXCommander/ViewModels/GameCardViewModel.DofFix.cs` — ViewModel partial (untracked, needs `git add`)

**Files that NEED modifications re-applied** (changes were lost during a git stash/branch switch):

---

## 1. `App.xaml.cs` — Register DofFixService in DI

Find where other services are registered (look for `DxvkService` or `OptiScalerService` registration).
Add:
```csharp
services.AddSingleton<DofFixService>();
```

---

## 2. `ViewModels/MainViewModel.cs` — Add service field

Add field:
```csharp
private readonly DofFixService _dofFixService;
```

Add to constructor parameters and assign:
```csharp
_dofFixService = App.Services.GetRequiredService<DofFixService>();
```

---

## 3. `ViewModels/MainViewModel.Init.cs` — Multiple changes

### a) Staging on startup (in `InitializeAsync`, parallel task block)
Add alongside other staging tasks:
```csharp
Task.Run(() => _dofFixService.EnsureStagingAsync())
```

### b) Manifest wiring (after manifest fetch, where `ApplyManifestPresets` is called — TWO places: init path and background scan path)
```csharp
_dofFixService.SetSkipGames(_manifest?.DofFixSkipGames);
_dofFixService.SetForceGames(_manifest?.DofFixForceGames);
if (_manifest?.ComponentUrls?.TryGetValue("ueDofFix", out var dofFixUrl) == true)
    _dofFixService.ManifestUrlOverride = dofFixUrl;
```

### c) Detection in `BuildCards` (background scan, after DXVK detection block)
```csharp
// DOF Fix detection
newCard.IsDofFixEligible = _dofFixService.IsGameEligible(newCard.EngineHint, newCard.Is32Bit, game.Name);
if (newCard.IsDofFixEligible && !string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
{
    if (_dofFixService.IsInstalledIn(installPath))
    {
        newCard.DofFixStatus = GameStatus.Installed;
        newCard.DofFixInstalledVersion = _dofFixService.StagedVersion;
    }
}
```

### d) Detection in `LoadCacheAndBuildCardsAsync` (cached startup, after DXVK block)
Same code as above.

---

## 4. `MainWindow.xaml` — Add DOF Fix row

Insert BETWEEN the `DetailOptionalSeparator` and the OptiScaler row (`DetailOsRow`):

```xml
<!-- DOF Fix row -->
<Grid x:Name="DetailDofFixRow" x:FieldModifier="internal" ColumnSpacing="8" Visibility="Collapsed">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="120"/>
        <ColumnDefinition Width="80"/>
        <ColumnDefinition Width="36"/>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="36"/>
        <ColumnDefinition Width="36"/>
    </Grid.ColumnDefinitions>
    <TextBlock x:Name="DetailDofFixLabel" x:FieldModifier="internal" Grid.Column="0" Text="DOF Fix" FontSize="12"
               Foreground="{StaticResource TextSecondaryBrush}" VerticalAlignment="Center"/>
    <TextBlock x:Name="DetailDofFixStatus" x:FieldModifier="internal" Grid.Column="1" FontSize="12"
               VerticalAlignment="Center" HorizontalTextAlignment="Center"
               PointerPressed="DetailDofFixStatus_PointerPressed"
               PointerEntered="LinkText_PointerEntered" PointerExited="LinkText_PointerExited"/>
    <Button x:Name="DetailDofFixInfoBtn" x:FieldModifier="internal" Grid.Column="2"
            Content="Info" FontSize="11" Padding="6,2,6,2"
            Background="{StaticResource SurfaceOverlayBrush}"
            Foreground="{StaticResource TextSecondaryBrush}"
            BorderBrush="{StaticResource BorderStrongBrush}" BorderThickness="1"
            CornerRadius="8" Width="36" Height="32"
            Click="DofFixInfoButton_Click"/>
    <Button x:Name="DetailDofFixInstallBtn" x:FieldModifier="internal" Grid.Column="3"
            Click="InstallDofFixButton_Click"
            HorizontalAlignment="Stretch" VerticalAlignment="Stretch"
            CornerRadius="8" FontSize="12" Height="32"/>
    <Button x:Name="DetailDofFixCogBtn" x:FieldModifier="internal" Grid.Column="4"
            Click="DofFixCogButton_Click"
            Background="{StaticResource SurfaceOverlayBrush}" Foreground="{StaticResource TextSecondaryBrush}"
            BorderBrush="{StaticResource BorderStrongBrush}" BorderThickness="1"
            CornerRadius="8" Width="36" Height="32" Padding="0"
            ToolTipService.ToolTip="DOF Fix Settings">
        <TextBlock Text="⚙" FontSize="14" HorizontalAlignment="Center"/>
    </Button>
    <Button x:Name="DetailDofFixDeleteBtn" x:FieldModifier="internal" Grid.Column="5"
            Click="UninstallDofFixButton_Click"
            Background="{StaticResource AccentRedBgBrush}" Foreground="{StaticResource AccentRedBrush}"
            BorderBrush="{StaticResource AccentPurpleBorderBrush}" BorderThickness="1"
            CornerRadius="8" Width="36" Height="32" Padding="0"
            ToolTipService.ToolTip="Remove DOF Fix" Opacity="0" IsHitTestVisible="False">
        <TextBlock Text="✕" FontSize="12" HorizontalAlignment="Center" Foreground="{StaticResource AccentRedBrush}"/>
    </Button>
</Grid>

<!-- DOF Fix progress/message -->
<ProgressBar x:Name="DetailDofFixProgress" x:FieldModifier="internal" Visibility="Collapsed"
             Minimum="0" Maximum="100" Height="3" CornerRadius="2" Margin="0,-2,0,0"/>
<TextBlock x:Name="DetailDofFixMessage" x:FieldModifier="internal" Visibility="Collapsed"
           FontSize="11" Margin="0,2,0,0"/>
```

---

## 5. `MainWindow.Events.cs` — Click handlers

Add these handlers (use existing component handlers as reference for pattern):

- `InstallDofFixButton_Click` — calls `_dofFixService.InstallAsync(card.InstallPath, progress)`, updates card status
- `UninstallDofFixButton_Click` — calls `_dofFixService.Uninstall(card.InstallPath)`, clears card status
- `DofFixInfoButton_Click` — shows ContentDialog with `_dofFixService.ReleaseNotes` (fetched from GitHub)
- `DofFixCogButton_Click` — placeholder ContentDialog ("No settings available")
- `DetailDofFixStatus_PointerPressed` — opens `_dofFixService.GetReleaseUrl(version)` in browser

---

## 6. `DetailPanelBuilder.Components.cs` — Row population

In `UpdateDetailComponentRows`, add DOF Fix row logic (between Optional separator visibility and OptiScaler):

```csharp
// DOF Fix row
_window.DetailDofFixRow.Visibility = card.DofFixRowVisibility;
if (card.DofFixRowVisibility == Visibility.Visible)
{
    bool dofGreyed = !card.IsRsInstalled;
    _window.DetailDofFixStatus.Text = card.DofFixStatusText;
    _window.DetailDofFixStatus.Foreground = UIFactory.GetBrush(card.DofFixStatusColor);
    _window.DetailDofFixStatus.TextDecorations = card.IsDofFixInstalled
        ? Windows.UI.Text.TextDecorations.Underline
        : Windows.UI.Text.TextDecorations.None;
    _window.DetailDofFixInstallBtn.Tag = card;
    _window.DetailDofFixInstallBtn.Content = card.DofFixActionLabel;
    _window.DetailDofFixInstallBtn.IsEnabled = card.DofFixInstallEnabled && !dofGreyed;
    _window.DetailDofFixInstallBtn.Background = UIFactory.GetBrush(card.DofFixBtnBackground);
    _window.DetailDofFixInstallBtn.Foreground = UIFactory.GetBrush(card.DofFixBtnForeground);
    _window.DetailDofFixInstallBtn.BorderBrush = UIFactory.GetBrush(card.DofFixBtnBorderBrush);
    _window.DetailDofFixInstallBtn.BorderThickness = new Thickness(1);
    _window.DetailDofFixInstallBtn.Opacity = dofGreyed ? 0.35 : 1.0;
    _window.DetailDofFixCogBtn.Tag = card;
    _window.DetailDofFixInfoBtn.Tag = card;
    _window.DetailDofFixDeleteBtn.Tag = card;
    var dofShow = card.DofFixDeleteVisibility == Visibility.Visible;
    _window.DetailDofFixDeleteBtn.Opacity = dofShow ? 1 : 0;
    _window.DetailDofFixDeleteBtn.IsHitTestVisible = dofShow;
}

// DOF Fix progress/message
_window.DetailDofFixProgress.Visibility = card.DofFixRowVisibility == Visibility.Visible
    ? card.DofFixProgressVisibility : Visibility.Collapsed;
_window.DetailDofFixProgress.Value = card.DofFixProgress;
_window.DetailDofFixMessage.Visibility = card.DofFixRowVisibility == Visibility.Visible
    ? card.DofFixMessageVisibility : Visibility.Collapsed;
_window.DetailDofFixMessage.Text = card.DofFixActionMessage;
_window.DetailDofFixMessage.Foreground = UIFactory.GetBrush(GetMessageColor(card.DofFixActionMessage));
```

Also update `DetailOptionalSeparator.Visibility` to show when DOF Fix OR OptiScaler is visible.

---

## 7. `Services/UpdateOrchestrationService.cs` — Update All

Add a `UpdateAllDofFixAsync` method (same pattern as other UpdateAll methods).
Call it from the main Update All flow.
Add to `IUpdateOrchestrationService.cs` interface.

---

## 8. `Models/RemoteManifest.cs` — Manifest fields

Add:
```csharp
[JsonPropertyName("dofFixSkipGames")]
public List<string>? DofFixSkipGames { get; set; }

[JsonPropertyName("dofFixForceGames")]
public List<string>? DofFixForceGames { get; set; }
```

---

## 9. `Services/AddonFileWatcher.cs` — Exclusion

In the addon detection check (where filenames starting with `renodx-` trigger detection), add exclusion:
```csharp
if (fileName.StartsWith("renodx-universal_ue_dof_fix", StringComparison.OrdinalIgnoreCase))
    return;
```

---

## 10. `DragDropHandler.Addon.cs` + `MainViewModel.Install.cs` — Exclusion from mod replace

Add `"renodx-universal_ue_dof_fix"` to the exclusion list alongside `renodx-dlssfix` and `renodx-devkit` in both:
- The "existing addon" warning check
- The removal loop

---

## 11. `GameCardViewModel.UI.cs` — UpdateBadgeVisibility

Add DOF Fix to the update badge computation:
```csharp
|| (DofFixStatus == GameStatus.UpdateAvailable && !ExcludeFromUpdateAllDofFix)
```

---

## 12. `MainViewModel.Settings.cs` — AnyUpdateAvailable

Add DOF Fix to the `AnyUpdateAvailable` property:
```csharp
|| (c.DofFixStatus == GameStatus.UpdateAvailable && !c.ExcludeFromUpdateAllDofFix)
```

---

## Key Technical Details

- **Addon filename**: `renodx-universal_ue_dof_fix.addon64`
- **GitHub tag pattern**: `ue-dof-fix-{version}` (e.g. `ue-dof-fix-1.0.0`)
- **GitHub API**: `https://api.github.com/repos/RankFTW/rhi-repo/releases`
- **Download URL pattern**: `https://github.com/RankFTW/rhi-repo/releases/download/ue-dof-fix-{version}/renodx-universal_ue_dof_fix.addon64`
- **Staging dir**: `%LocalAppData%\RHI\ue-dof-fix\` with `version.txt`
- **Eligibility**: UE 5.0–5.6 AND 64-bit AND not in skip list, OR in force list (still requires 64-bit)
- **Version link URL**: `https://github.com/RankFTW/rhi-repo/releases/tag/ue-dof-fix-{version}`
- **Manifest URL override key**: `componentUrls.ueDofFix`

## Build Verification

After all changes: `dotnet build RenoDXCommander\RenoDXCommander.csproj --no-restore -v quiet`
Must be 0 errors.
