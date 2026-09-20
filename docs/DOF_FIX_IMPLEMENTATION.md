# Компонент DOF Fix — руководство по реализации

## Текущее состояние

Ветка: `wip-cogs-and-dof`

**Файлы, которые СУЩЕСТВУЮТ и корректны:**
- `RenoDXCommander/Services/DofFixService.cs` — полная реализация сервиса (не отслеживается, нужен `git add`)
- `RenoDXCommander/Services/IDofFixService.cs` — интерфейс (не отслеживается, нужен `git add`)
- `RenoDXCommander/ViewModels/GameCardViewModel.DofFix.cs` — частичный ViewModel (не отслеживается, нужен `git add`)

**Файлы, в которые НУЖНО заново внести правки** (изменения были потеряны при git stash/переключении ветки):

---

## 1. `App.xaml.cs` — зарегистрировать DofFixService в DI

Найдите место регистрации других сервисов (ищите регистрацию `DxvkService` или `OptiScalerService`).
Добавьте:
```csharp
services.AddSingleton<DofFixService>();
```

---

## 2. `ViewModels/MainViewModel.cs` — добавить поле сервиса

Добавьте поле:
```csharp
private readonly DofFixService _dofFixService;
```

Добавьте в параметры конструктора и присвойте:
```csharp
_dofFixService = App.Services.GetRequiredService<DofFixService>();
```

---

## 3. `ViewModels/MainViewModel.Init.cs` — несколько изменений

### a) Подготовка хранилища при запуске (в `InitializeAsync`, блок параллельных задач)
Добавьте рядом с остальными задачами подготовки:
```csharp
Task.Run(() => _dofFixService.EnsureStagingAsync())
```

### b) Привязка манифеста (после загрузки манифеста, там где вызывается `ApplyManifestPresets` — ДВА места: путь инициализации и путь фонового сканирования)
```csharp
_dofFixService.SetSkipGames(_manifest?.DofFixSkipGames);
_dofFixService.SetForceGames(_manifest?.DofFixForceGames);
if (_manifest?.ComponentUrls?.TryGetValue("ueDofFix", out var dofFixUrl) == true)
    _dofFixService.ManifestUrlOverride = dofFixUrl;
```

### c) Определение в `BuildCards` (фоновое сканирование, после блока DXVK)
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

### d) Определение в `LoadCacheAndBuildCardsAsync` (стартовый путь из кеша, после блока DXVK)
Тот же код, что и выше.

---

## 4. `MainWindow.xaml` — добавить строку DOF Fix

Вставьте МЕЖДУ `DetailOptionalSeparator` и строкой OptiScaler (`DetailOsRow`):

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

## 5. `MainWindow.Events.cs` — обработчики кликов

Добавьте эти обработчики (образец — существующие обработчики компонентов):

- `InstallDofFixButton_Click` — вызывает `_dofFixService.InstallAsync(card.InstallPath, progress)`, обновляет статус карточки
- `UninstallDofFixButton_Click` — вызывает `_dofFixService.Uninstall(card.InstallPath)`, очищает статус карточки
- `DofFixInfoButton_Click` — показывает ContentDialog с `_dofFixService.ReleaseNotes` (загружается с GitHub)
- `DofFixCogButton_Click` — заглушка ContentDialog («Настройки недоступны»)
- `DetailDofFixStatus_PointerPressed` — открывает `_dofFixService.GetReleaseUrl(version)` в браузере

---

## 6. `DetailPanelBuilder.Components.cs` — наполнение строки

В `UpdateDetailComponentRows` добавьте логику строки DOF Fix (между видимостью разделителя «Необязательные» и OptiScaler):

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

Также обновите `DetailOptionalSeparator.Visibility` — показывать, когда видим DOF Fix ИЛИ OptiScaler.

---

## 7. `Services/UpdateOrchestrationService.cs` — «Обновить всё»

Добавьте метод `UpdateAllDofFixAsync` (по образцу остальных UpdateAll-методов).
Вызовите его из основного потока «Обновить всё».
Добавьте в интерфейс `IUpdateOrchestrationService.cs`.

---

## 8. `Models/RemoteManifest.cs` — поля манифеста

Добавьте:
```csharp
[JsonPropertyName("dofFixSkipGames")]
public List<string>? DofFixSkipGames { get; set; }

[JsonPropertyName("dofFixForceGames")]
public List<string>? DofFixForceGames { get; set; }
```

---

## 9. `Services/AddonFileWatcher.cs` — исключение

В проверке обнаружения аддонов (где имена, начинающиеся с `renodx-`, запускают обнаружение), добавьте исключение:
```csharp
if (fileName.StartsWith("renodx-universal_ue_dof_fix", StringComparison.OrdinalIgnoreCase))
    return;
```

---

## 10. `DragDropHandler.Addon.cs` + `MainViewModel.Install.cs` — исключение из замены модов

Добавьте `"renodx-universal_ue_dof_fix"` в список исключений рядом с `renodx-dlssfix` и `renodx-devkit` в обоих файлах:
- Проверка предупреждения «существующий аддон»
- Цикл удаления

---

## 11. `GameCardViewModel.UI.cs` — UpdateBadgeVisibility

Добавьте DOF Fix в вычисление значка обновления:
```csharp
|| (DofFixStatus == GameStatus.UpdateAvailable && !ExcludeFromUpdateAllDofFix)
```

---

## 12. `MainViewModel.Settings.cs` — AnyUpdateAvailable

Добавьте DOF Fix в свойство `AnyUpdateAvailable`:
```csharp
|| (c.DofFixStatus == GameStatus.UpdateAvailable && !c.ExcludeFromUpdateAllDofFix)
```

---

## Ключевые технические детали

- **Имя файла аддона**: `renodx-universal_ue_dof_fix.addon64`
- **Схема тегов GitHub**: `ue-dof-fix-{version}` (например, `ue-dof-fix-1.0.0`)
- **GitHub API**: `https://api.github.com/repos/RankFTW/rhi-repo/releases`
- **Схема ссылок скачивания**: `https://github.com/RankFTW/rhi-repo/releases/download/ue-dof-fix-{version}/renodx-universal_ue_dof_fix.addon64`
- **Каталог хранилища**: `%LocalAppData%\RHI\ue-dof-fix\` с `version.txt`
- **Условия применимости**: UE 5.0–5.6 И 64-бит И не в списке пропуска, ИЛИ в списке форсированных (64-бит всё равно требуется)
- **Ссылка на версию**: `https://github.com/RankFTW/rhi-repo/releases/tag/ue-dof-fix-{version}`
- **Ключ переопределения URL в манифесте**: `componentUrls.ueDofFix`

## Проверка сборки

После всех изменений: `dotnet build RenoDXCommander\RenoDXCommander.csproj --no-restore -v quiet`
Ошибок быть не должно (0 errors).
