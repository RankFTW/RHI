# Отчёт о передаче — сессия 13 сентября 2026 (вечер)

**Ветка:** main  
**Версия приложения:** 2.7.1 (в работе, не выпущена)  
**Публикуется:** RHI.exe  
**HEAD:** b744b81

---

## Что сделано в этой сессии

### 1. Манифест в папке игры `rhi_install.txt`

Новый JSON-файл, записываемый в каждую папку игры при установке/обновлении OptiScaler. Решает две застарелые проблемы:

**Путаница с версиями:** OptiScaler читал установленную версию из хранилищного `version.txt`, который меняется при скачивании новой версии. Если вы поставили nightly `20260912`, а затем скачался новый nightly, карточка показывала новую версию вместо реально установленной. `rhi_install.txt` фиксирует версию на момент установки и при запуске читается в приоритете.

**Устаревшая очистка при удалении:** удаление раньше сканировало папку хранилища, чтобы узнать, какие файлы чистить. Если хранилище менялось (новый nightly скачался между установкой и удалением), удалялись не те файлы. Теперь удаление читает манифест в папке игры как авторитетный список файлов.

**Модель:** `Models/RhiInstallManifest.cs`  
Поля: `component`, `variant`, `version`, `installedAs`, `installedAt`, `files`, `folders`, `sharedFiles`, `components`, `nrMethod`  
Статические хелперы: `Write`, `Read`, `Delete`, `UpdateInstalledAs`, `SetNrMethod`, `AddSharedFileOwner`, `RemoveSharedFileOwner`, `SetComponent`, `GetComponentFiles`, `RemoveComponent`

**Изменённые файлы:** `OptiScalerService.Install.cs`, `MainViewModel.BuildCards.cs`, `MainViewModel.CacheLoad.cs`, `DetailPanelBuilder.Overrides.cs`

---

### 2. Совместное владение файлами DLSS (`sharedFiles` в манифесте)

Несколько компонентов (OptiScaler, ShortFuse NR, DLSS5 Tool, Feeder) разворачивают одни и те же файлы `nvngx_dlss*.dll` в папку игры. Раньше они затирали sentinel-файлы друг друга и удаляли файлы, нужные другому компоненту.

**Решение:** словарь `sharedFiles` в `rhi_install.txt` отображает каждое имя файла на список компонентов-владельцев. Файл удаляется/восстанавливается только когда удалён последний владелец.

**Подключённые компоненты:**
- OptiScaler (все варианты): всегда регистрирует `nvngx_dlss.dll`, `nvngx_dlssd.dll`, `nvngx_dlssg.dll`; `nvngx_dlssnr.dll` — только для варианта DlssNr
- ShortFuse: регистрирует все 4 DLL в `DeploySfDllsAsync`; снимает регистрацию в `UninstallSf`
- DLSS5 Tool: регистрирует все 4 в `UpgradeDlssDllsAsync`; снимает в `Renodx5AddonService.Uninstall`
- Bridge: использует владение DLSS5 Tool
- Feeder: регистрирует `nvngx_dlssnr.dll` в `DeployNrDllIfAbsentAsync("Feeder")`; снимает в `RemoveNrDll("Feeder")`

**Исправленный краевой случай:** когда DLSS5 Tool разворачивает в найденную подпапку плагина (например, `Engine\Plugins\Runtime\Nvidia\DLSS\...`), а OptiScaler — в корень игры, `RestoreDlssDllsWithSentinel` теперь чистит и корневые копии, если пути различаются.

**Изменённые файлы:** `Renodx5AddonService.cs`, `DetailPanelBuilder.NeuralRendering.cs`, `OptiScalerService.Install.cs`

---

### 3. Поигровые записи о файлах (`components` в манифесте)

Каждый NR-компонент теперь записывает свой полный список развёрнутых файлов в `rhi_install.txt` в словаре `components`. Это избыточность рядом с sentinel-файлами — если sentinel'ы отсутствуют или неверны, RHI всё равно знает, что чистить.

**Что записывает каждый компонент:**
- `ShortFuse`: `renodx-dlss.addon64` + 4 DLL nvngx + 11 файлов sl.*.dll
- `Dlss5Tool`: `renodx-dlss5.addon64` + 4 DLL nvngx; Bridge добавляет `dlss5-bridge.addon64`
- `Feeder`: аддон feeder + nvngx_dlssnr + nvngx_dlss + renodx-dlss5 + шейдеры + `ReShadePreset.ini`; 32-бит добавляет `host64\*`; DX9 добавляет `D3D9.dll` + `dgVoodoo.conf`

**Важно:** `InstallAsync` и `UpdateAsync` OptiScaler оба сохраняют `sharedFiles`, `components` и `nrMethod` при перезаписи манифеста. Прошлый баг: создавался свежий объект `RhiInstallManifest`, стиравший эти поля.

---

### 4. Исправления лимитов GitHub API

**Поддержка токена:** `DevUnlockService.GitHubApiToken` теперь сначала читает `%LocalAppData%\RHI\github_api.txt` (любой пользователь), с откатом к строке `github_api=` в `unlock.txt` (только разработчики). Токен применяется ко всем вызовам `GitHubETagCache.GetWithETagAsync`.

**Обработка 403:** раньше любой 403 ставил `_rateLimited = true` на всю сессию. Теперь только при подтверждении `X-RateLimit-Remaining: 0` — единичные 403 больше не убивают все вызовы API на сессию. В лог теперь пишется URL и то, был ли запрос аутентифицирован.

**Изменённые файлы:** `DevUnlockService.cs`, `GitHubETagCache.cs`

---

### 5. Исправление обновления базы

Изменения базы RHI (rhi-repo) не подхватывались после обычного «Обновить» — требовался полный перезапуск. Причина: `GitHubETagCache` живёт в памяти и ограничен сессией. При обновлении ETag оставался закешированным, и GitHub возвращал 304 со старыми данными.

Фикс: `InitializeAsync` вызывает `_renoDxDbService.InvalidateCache()` при `forceRescan=true`, очищая URL базы из ETag-кеша перед загрузкой.

**Изменённые файлы:** `MainViewModel.Init.cs`, `RenoDXDbService.cs`, `IRenoDXDbService.cs`, `GitHubETagCache.cs`

---

### 6. Обновление панели подробностей после «Обновить»

После «Обновить» `ViewModel.SelectedGame` становится null (новые объекты карточек не совпадают со старыми ссылками). `GameList.SelectedItem` всё ещё держит старый объект, поэтому `SelectionChanged` не срабатывает и `PopulateDetailPanel` не выполняется. Исправлено в `MainWindow.UISync.cs`: когда `IsLoading` становится false при тихом (обновление) переходе, берём текущий `GameList.SelectedItem`, находим эквивалентную новую карточку в `DisplayedGames` и форсируем `PopulateDetailPanel`.

**Изменённый файл:** `MainWindow.UISync.cs`

---

### 7. Заметки UE-Extended из базы в диалоге информации

`RenoDXDbUnrealEntry.Comments` не доходил до диалога информации для игр NativeHDR/UE-Extended из-за трёх отдельных блоков:
1. `MergeDbSources` писал Comments в `_genericNotes` только в режиме DbOnly/Hybrid — теперь пишет всегда, независимо от режима источника
2. `BuildNotes` рано возвращался для игр NativeHDR до вызова `GetGenericNote` — теперь включает комментарий базы после предупреждения об HDR
3. `AddonInfoResolver` никогда не читал `card.Notes` — теперь добавляет комментарий базы (без строки предупреждения об HDR) к запасному тексту

---

### 8. Исправление переустановки MFG Ada Unlock

При установке через секцию Extras переключатель в окне выбора аддонов серый (extrasInstalledConflict). Пользователи не могли его снять в окне выбора. Кнопка удаления в Extras теперь также убирает запись из `EnabledGlobalAddons` и `PerGameAddonSelection`.

**Изменённый файл:** `DetailPanelBuilder.Extras.cs`

---

### 9. Прочие исправления

- **Удаление OptiScaler Stable/Nightly удаляло принадлежащий NR `nvngx_dlssnr.dll`** — шаг 2e теперь выполняется только для варианта DlssNr
- **Двойное удаление файлов DLSS при деинсталляции** — общий цикл шага 3 повторно удалял файлы, уже обработанные шагами 2b/2c/2d. Исправлено множеством `explicitlyHandled`
- **Зависание массового развертывания DLSS** — `MassDlssDeployDialog` теперь проверяет `Directory.Exists` перед обработкой каждой игры
- **Переименование папки набора шейдеров в `reshade-shaders-original`** — гонка в `ShaderPackService.Deploy.cs`, исправлена записью управляемого маркера до переименования/развертывания
- **Путь Engine.ini для Solasta 2** — добавлен в `engineIniPathOverrides` в манифесте (`"Solasta 2": "Solasta 2"`)
- **Устаревшие sentinel'ы OptiScaler** — `CleanOrphanedOptiScalerSentinels()` запускается при старте, удаляет файлы `.original`, чьё базовое имя не совпадает с `InstalledAs`

---

## Активные/ожидающие пункты

### Регистрация appid в Nexus
Mark переписывается с поддержкой Nexus. Обновите константу `AppId` в `NexusSsoService.cs` (~строка 20), когда выдадут официальный slug.

### Ограничения `rhi_install.txt`
- Существующие установки (до этой сессии) манифеста не имеют — удаление откатывается к сканированию хранилища. Фикс с защитой sentinel'ов (удалять корневые файлы, только если `.original` есть в запасном скане хранилища) помогает, но не идеален для всех краевых случаев.
- Отслеживание `sharedFiles` начинает работать только после того, как оба компонента установлены/переустановлены с новым кодом. У тех, кто ставил OptiScaler + ShortFuse до этого обновления, отслеживания владения не будет до переустановки.

---

## Ключевые изменённые файлы этой сессии

| Файл | Изменение |
|------|--------|
| `Models/RhiInstallManifest.cs` | **Новый** — полная модель со всеми полями и статическими хелперами |
| `Services/OptiScalerService.Install.cs` | Запись/чтение/удаление манифеста в Install/Update/Uninstall; совместное владение файлами; сохранение полей при записи |
| `Services/Renodx5AddonService.cs` | Совместное владение для ShortFuse/DLSS5Tool; записи компонентов; RemoveNrDll с параметром компонента |
| `DetailPanelBuilder.NeuralRendering.cs` | Записи компонентов для Dlss5Tool/Bridge/Feeder; чистка корня в RestoreDlssDllsWithSentinel; вызовы владения |
| `DetailPanelBuilder.Overrides.cs` | UpdateInstalledAs вызывается при переименовании DLL OptiScaler (чинит sentinel и манифест при смене переопределения) |
| `DetailPanelBuilder.Extras.cs` | Удаление MFG Ada Unlock также чистит выбор аддона |
| `Services/GitHubETagCache.cs` | Токен-аутентификация во всех запросах; корректная обработка 403 (лимит только при Remaining=0) |
| `Services/DevUnlockService.cs` | Поддержка github_api.txt для всех пользователей |
| `Services/RenoDXDbService.cs` | Метод InvalidateCache() |
| `Services/IRenoDXDbService.cs` | InvalidateCache() в интерфейсе |
| `Services/AddonInfoResolver.cs` | Комментарии базы добавляются к запасному тексту NativeHDR |
| `ViewModels/MainViewModel.cs` | MergeDbSources всегда пишет Comments в _genericNotes |
| `ViewModels/MainViewModel.Init.cs` | Инвалидация кеша базы при «Обновить»; CleanOrphanedOptiScalerSentinels |
| `ViewModels/MainViewModel.CacheLoad.cs` | BuildNotes включает комментарий базы для игр NativeHDR |
| `ViewModels/MainViewModel.BackgroundScan.cs` | Метод CleanOrphanedOptiScalerSentinels |
| `ViewModels/MainViewModel.BuildCards.cs` | OsInstalledVersion сначала из манифеста |
| `MainWindow.UISync.cs` | Принудительная пересборка панели выбранной игры после «Обновить» |
| `MassDlssDeployDialog.cs` | Защита Directory.Exists |
| `manifest.json` | Solasta 2 engineIniPathOverrides |
| `RHI_PatchNotes.md` | Patch notes v2.7.1 |

---

## Заметки следующему агенту

- Никогда не поднимайте версию — Mark делает это вручную
- Всегда спрашивайте перед коммитом или пушем
- Сборка: `dotnet build g:\RDXC\RenoDXCommander\RenoDXCommander.csproj --no-restore -v q -p:Platform=x64`
- Публикация: сначала закройте RHI, затем `& "g:\RDXC\publish.bat"` — если RHI работает, exe заблокирован, и публикация тихо провалится
- Доступ к логам: `Copy-Item "$env:LOCALAPPDATA\RHI\Logs\*" "g:\RDXC\Logs\" -Force`, затем читайте из `g:\RDXC\Logs\`
