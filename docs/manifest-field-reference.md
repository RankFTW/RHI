# Справочник полей манифеста RHI

Полный справочник по каждому полю `RemoteManifest`. Для каждого поля: что оно делает, где в коде читается и чем управляет.

---

## Поля определения игр

### `blacklist` — `List<string>`
Полностью скрывает найденные игры из RHI (например, лаунчеры DLC, хелперы античита).
- `GameInitializationService.ApplyManifest()` → наполняет `_manifestBlacklist`
- `MainViewModel.Init`, `BackgroundScan`, `CacheLoad` → фильтрует `allGames` перед сборкой карточек

### `wikiNameOverrides` — `Dict<string,string>`
Отображение «найденная папка/магазин → имя мода в вики RenoDX». Чинит случаи, когда имена папок Steam не совпадают с вики.
- `GameInitializationService.ApplyManifest()` → добавляет в `_nameMappings` (пользовательские переопределения сильнее)
- Помечено как источник-манифест: невидимо в UI, исключается из сохранений settings.json

### `lumaNameOverrides` — `Dict<string,string>`
Отображение «имя найденной игры → имя записи в списке завершённых модов Luma». Отдельно от переопределений имён вики.
- `MainViewModel.Install.Luma.MatchLumaGame()` → наивысший приоритет, до нечёткого сопоставления

### `installPathOverrides` — `Dict<string,string>`
Задаёт подпуть внутри найденной папки установки, куда разворачиваются моды (например, `"bin\\x64"`).
Поддерживает кандидаты через пайп: `"Win64|WinGDK"` — пробуются по порядку, побеждает первый существующий.
- `GameInitializationService.ApplyManifest()` → вливается в общий словарь `_installPathOverrides`
- `BuildCards`, `CacheLoad`, `AddManualGame` → применяет разрешение пути перед созданием карточки

### `splitGames` — `Dict<string,List<SplitGameEntry>>`
Разбивает одну найденную запись магазина на несколько карточек, каждая указывает на подпапку.
- `MainViewModel.BuildCards` → очень рано, заменяет одну запись `DetectedGame` на N суб-записей
- У `SplitGameEntry` есть `name` (отображаемое имя карточки) и `subPath` (относительный путь от корня бандла)
- Запись создаётся, только если папка subPath реально существует на диске

### `engineOverrides` — `Dict<string,string>`
Форсирует конкретный тип движка и подпись, перекрывая автоопределение.
Специальные значения: `"Unreal"`, `"Unreal (Legacy)"`, `"Unity"`, `"RE Engine"` → отображаются на перечисление `EngineType`.
Любая другая строка (например, `"Silk"`) сохраняется как есть и показывается на значке движка, но не влияет на логику запасных модов.
- `GameInitializationService.ApplyManifest()` → наполняет `_manifestEngineOverrides`
- `MainViewModel.GameMatching.ResolveEngineOverride()` → вызывается при сборке карточек
- При наличии: значок движка `isClickable = false` (без переключения пользователем)

### `engineHintOverrides` — `Dict<string,string>`
Задаёт отображаемую строку `EngineHint` на карточке (например, `"4.27.2"`, `"5.3.2"`). В отличие от `engineOverrides` — только отображение/подсказка, на перечисление `EngineType` не влияет.
- `BuildCards`, `CacheLoad` → `card.EngineHint = manifestEngineHint` после сборки карточки
- Используется развертыванием Engine.ini для выбора поведения ключей HDR UE4 против UE5

### `engineIniPathOverrides` — `Dict<string,string>`
Переопределяет имя проекта Unreal для поиска `Engine.ini` в `%LocalAppData%`. Поддерживает кандидаты через пайп и абсолютные пути.
- `BuildCards`, `CacheLoad`, `AddManualGame` → `card.EngineIniProjectOverride`
- Потребляется всеми методами Engine.ini в `AuxInstallService`, кнопкой 📋 INI и обработчиками HDR/LUT диалога шестерёнки

### `emulatorGames` — `Dict<string,EmulatorConfig>`
Управляет бандлом эмулятора Ryubing. Определяет, какие аддоны скачивать, и их запасные ссылки.
- `BuildCards`, `CacheLoad`, `AddManualGame` → задаёт `card.EmulatorAddonNames` и синтетический `GameMod`
- `InstallEmulatorAddonsAsync()` → читает `AddonUrls` по имени в вики (манифест приоритетнее вики-скрейпа)
- Поля `EmulatorConfig`: `addons` (список имён игр в вики), `addonUrls` (поигровые переопределения ссылок)

### `thirtyTwoBitGames` / `sixtyFourBitGames` — `List<string>`
Форсирует 32-бит или 64-бит режим независимо от анализа PE-заголовка. Определяет, какой вариант DLL развернётся.
- `GameInitializationService.ApplyManifest()` → наполняет `_manifest32BitGames` / `_manifest64BitGames`
- `MainViewModel.GameMatching.ResolveIs32Bit()` → проверяется после пользовательского переопределения, до PE-заголовочного запасного варианта

### `dlssSkipGames` — `List<string>`
Подавляет сканирование файлов DLSS/Streamline для конкретных игр (ложные срабатывания, заведомо нерелевантные или медленные).
- `MainViewModel.BuildCards` → инлайн-проверка, при совпадении пропускает весь блок обнаружения DLSS

### `steamAppIdOverrides` — `Dict<string,int>`
Форсирует конкретный Steam AppID независимо от ACF-манифеста или `steam_appid.txt`.
- `SteamAppIdResolver.ResolveAsync()` → наивысший приоритет (шаг 1 из 5)
- Используется для запросов PCGW (база HDR, данные DLSS) и команд запуска Steam

---

## Флаги UE-Extended / HDR

### `ueExtendedGames` — `List<string>`
Игры, которые должны использовать `renodx-ue-extended.addon64` вместо стандартного универсального UE-аддона.
- `GameInitializationService.ApplyManifest()` → добавляет в `gameNameService.UeExtendedGames`
- Самый низкоприоритетный сигнал UE-Extended — перекрывается `ueExtendedCompatibility`, блокируется `noUeExtendedGames`

### `nativeHdrGames` — `List<string>`
Игры, которые форсируют включение UE-Extended и обходят блок `hasNamedMod` (именной мод из вики не помешает UE-Extended).
- `GameInitializationService.ApplyManifest()` → наполняет `_manifestNativeHdrGames`
- `IsNativeHdrGameMatch()` → включает `isNativeHdr = true` в BuildCards/Install
- Эффект: форсирует UE-Extended, скрывает переключатель, ставит `card.IsNativeHdrGame = true`

### `noUeExtendedGames` — `List<string>`
Высший по приоритету блок UE-Extended. Перекрывает всё, включая `nativeHdrGames` и согласие пользователя.
- `GameInitializationService.ApplyManifest()` → наполняет `_manifestNoUeExtendedGames`
- BuildCards/AddManualGame → врата `noUeExtended` — первая проверка в дереве решения `useUeExt`

### `ueExtendedCompatibility` — `Dict<string,UeExtendedCompatEntry>`
Конфигурация UE-Extended с наивысшим приоритетом. Заменяет и `nativeHdrGames`, и `ueExtendedGames` (функция с v2+).
Присутствие в словаре = форсированный UE-Extended. Запись управляет развертыванием Engine.ini:
- `hdr: false` → пропустить ключи HDR (у игры есть собственная HDR-опция в движке); по умолчанию: разворачивать для UE5, пропускать для UE4
- `lut: false` → пропустить ключ LUT; по умолчанию: всегда разворачивать
- `GameInitializationService.ApplyManifest()` → наполняет `_manifestUeExtendedCompat` И добавляет ключи в `_manifestNativeHdrGames`
- `MainViewModel.Install.InstallModAsync()`, `UpdateOrchestrationService` → читают `deployHdr`/`deployLut` из записи

### `lumaRenodxCompat` — `List<string>`
Игры, где RenoDX и Luma могут сосуществовать. Обычно включение Luma удаляет мод RenoDX.
- `BuildCards`, `CacheLoad`, `AddManualGame` → `card.LumaRenodxCompatible = true`
- **Важно:** использует прямой `Contains(game.Name)` — точное совпадение, без нормализации
- Эффект: строка RenoDX остаётся видимой в режиме Luma, при установке Luma удаление RenoDX пропускается

### `lumaDefaultGames` — `List<string>`
Игры, автоматически включающие режим Luma при первом обнаружении без действий пользователя. Учитывает ранее заданные переключения.
- `MainViewModel.BuildCards` → авто-добавляет составной ключ в `_lumaEnabledGames`, если пользовательского переключения не было
- НЕ применяется на фазе CacheLoad (срабатывает только во второй фазе)

---

## Поля поведения установки

### `forceExternalOnly` — `Dict<string,ForceExternalEntry>`
Форсирует карточку в режим «только перенаправление». Кнопка установки становится внешней ссылкой вместо прямого скачивания.
- `GameInitializationService.ApplyManifestCardOverrides()` → ставит `card.IsExternalOnly = true`, `card.ExternalUrl`, `card.ExternalLabel`, `card.WikiStatus`
- У записи есть `url` (ссылка на скачивание) и `label` (текст кнопки)
- Ключ должен совпадать с **найденным** именем игры, а НЕ с привязанным именем вики

### `snapshotOverrides` — `Dict<string,string>`
Внедряет или переопределяет ссылку скачивания аддона для игры, когда вики-скрейп не удался или выхватил неверный URL.
- `BuildCards`, `Install`, `AddManualGame` → задаёт `effectiveMod.SnapshotUrl`
- **Предупреждение:** любой непустой `SnapshotUrl` делает `hasNamedMod = true`, блокируя UE-Extended. Никогда не добавляйте сюда универсальные UE-игры с NativeHDR.

### `installWarnings` — `Dict<string,Dict<string,string>>`
Поигровые, покомпонентные блокирующие диалоги подтверждения перед установкой. Пользователь может отменить и прервать.
Структура: `{ "Game Name": { "reshade": "warning text", "renodx": "...", ... } }`
- `MainViewModel.Install.Luma.CheckInstallWarningAsync(gameName, component)` → показывает ContentDialog
- Подключено для всех 8 компонентов: `reshade`, `renodx`, `relimiter`, `dc`, `optiscaler`, `luma`, `reframework`, `dxvk`

### `dllNameOverrides` — `Dict<string,ManifestDllNames>`
Форсирует конкретные имена прокси-DLL (ReShade и/или DC) для игры. Пример: `{ "reshade": "winmm.dll", "dc": "" }`
- `GameInitializationService.ApplyManifest()` → наполняет `_manifestDllNameOverrides`
- `GetManifestDllNames()` → точное → без торговых знаков → нормализованное сопоставление
- `InstallReShadeAsync()`, `ApplyManifestDllRenames()` → используют имя `.ReShade`

### `optiScalerDllOverrides` — `Dict<string,string>`
Поигровое переопределение имени DLL OptiScaler. **Объявлено в модели, но кодом пока не потребляется** — зарезервировано на будущее.

### `gacSymlinkGames` — `Dict<string,string>`
Направляет игры на XNA Framework (например, Terraria) через установку GAC-симлинком вместо обычного копирования DLL.
- `GetGacSymlinkPath()` → проверяет этот словарь; при наличии `InstallReShadeAsync` уходит в `InstallReShadeGacAsync`
- Значение словаря = абсолютный путь каталога GAC. Требуются права администратора.

### `legacyReShadeVersions` — `Dict<string,string>`
Автоназначает игре заблокированный старый канал ReShade (например, `"Max Payne 3": "6.4.1"`). Никогда не перезаписывает существующие пользовательские переопределения.
- `MainViewModel.Init`, `BackgroundScan` → вызывает `SetReShadeChannelOverride(gameName, version)`, если переопределения нет (проверяются и ключ по имени, и составной)

### `legacyReShadeAvailable` — `List<string>`
Список строк версий, показываемых в диалоге выбора старых версий ReShade. Управляется сервером.
- `DetailPanelBuilder.Overrides.RsChannel` → выбор канала RS, наполняет `RadioButtons` в диалоге выбора «Legacy…»

### `launchExeOverrides` — `Dict<string,string>`
Относительный путь exe от InstallPath. Используется для двух целей:
1. **Запуск игры** (приоритет 2, после пользовательского переопределения): `MainWindow.Events.Install.LaunchGame()` → `Path.Combine(card.InstallPath, manifestExe)`
2. **Сопоставление профилей NVIDIA**: `DlssPresetService.FindProfileUncached()` → сверяет имя exe с записями приложений в профилях драйвера NVIDIA

### `renodxIniOverrides` — `Dict<string,Dict<string,string>>`
Поигровые ключи INI секции `[renodx]`, записываемые в `reshade.ini` при установке/обновлении. Только добавляет/обновляет — никогда не удаляет пользовательские значения (если не `forceOverwrite: true`).
- Читается через `AuxInstallService.GlobalManifest?.RenodxIniOverrides` в: `InstallModAsync`, `UpdateAllRenoDxAsync`, кнопке «Слить RS INI», повторном развертывании `RdxCogButton_Click`

### `renodxExtraSettings` — `List<RenodxExtraSetting>`
Добавляет дополнительные строки ComboBox в сетку настроек совместимости шестерёнки ⚙ RenoDX без обновления клиента.
- `MainWindow.Events.Components.RdxCogButton_Click` → добавляет строки; `SelectionChanged` пишет выбранное значение в секцию INI `[renodx]`
- Каждая запись: `key`, `label`, `default`, `options` (массив пар `{value, name}`)

### `pdUpscalerGames` — `Dict<string,string>`
Игры RE Engine, которым нужна сборка REFramework с PD-Upscaler, когда установлен OptiScaler.
- `InstallEventHandler.InstallOsButton_Click` → после установки OptiScaler, если существует `dinput8.dll`, подменяет на PD-Upscaler REFramework
- `UninstallOsButton_Click` → при удалении возвращает стандартный REFramework
- Значение словаря = имя артефакта на nightly.link (например, `"RE2"`, `"RE7"`, `"RE8"`)

---

## Поля DXVK

### `dxvkBlacklist` — `List<string>`
Запрещает включение переключателя DXVK (серый с подсказкой про античит).
- `GameInitializationService.ApplyManifestCardOverrides()` → `card.IsDxvkBlacklisted = true`
- `GameCardViewModel.Dxvk.IsDxvkToggleEnabled` → возвращает `false`; в подсказке предупреждение об античите

### `dxvkApiOverrides` — `Dict<string,string>`
Предполагаемое поигровое переопределение выбора DLL DXVK (`"DX8"`, `"DX9"` и т.п.). **Строка-значение пока не потребляется** — проверяется только сам факт наличия.
- `ApplyManifestCardOverrides()` → `card.HasDxvkApiOverride = true` (только наличие)
- Эффект: разблокирует переключатель DXVK для игр с GraphicsApi = Unknown

### `dxvkGameNotes` — `Dict<string,GameNoteEntry>`
Поигровые заметки в диалоге информации DXVK. Добавляются после общего описания DXVK.
- `MainWindow.Events.Install.DxvkInfoButton_Click()` → прямой инлайн-чтение (мимо `AddonInfoResolver`)

---

## Поля содержимого кнопок «Инфо»

Все поля `*GameInfo` и `gameNotes` идут через `AddonInfoResolver.GetManifestDict(AddonType)` в диалогах кнопок информации компонентов. Исключение — `DxvkGameNotes`: читается напрямую в обработчике кнопки информации DXVK.

| Поле | Кнопка информации компонента | Примечания |
|-------|----------------------|-------|
| `gameNotes` | RenoDX | Есть и второй путь через `ApplyManifestCardOverrides` → `card.Notes` |
| `reshadeGameInfo` | ReShade | — |
| `relimiterGameInfo` | ReLimiter | — |
| `displayCommanderGameInfo` | Display Commander | — |
| `reframeworkGameInfo` | RE Framework | — |
| `optiScalerGameInfo` | OptiScaler | — |
| `lumaGameInfo` | Luma (основное) | — |
| `lumaGameNotes` | Luma (дополнительное) | Есть и второй путь через `ApplyManifestCardOverrides` → `card.LumaNotes`; также читается напрямую в `AddonInfoResolver.TryResolveLumaWiki()` как дополнение после заметок вики LumaMod |
| `dxvkGameNotes` | DXVK | Читается инлайн в `DxvkInfoButton_Click`, НЕ через `AddonInfoResolver` |

Все используют схему `GameNoteEntry`: `{ notes, notesUrl, notesUrlLabel }`.

---

## Поля статуса вики / авторов

### `wikiStatusOverrides` — `Dict<string,string>`
Переопределяет значок статуса у записей `GameMod` вики (например, `"✅"`, `"🚧"`) без правки вики.
- `GameInitializationService.ApplyManifestStatusOverrides()` → обходит `_allMods`, ставит `mod.Status`
- Вызывается после загрузки вики и в Init, и в BackgroundScan

### `wikiUnlinks` — `List<string>`
Полностью отрезает игру от системы вики/модов. Ни строки RenoDX, ни универсального запасного мода движка.
В отличие от `blacklist`, карточка остаётся видимой — просто без опций модов.
- `GameInitializationService.ApplyManifest()` → наполняет `_manifestWikiUnlinks`
- `BuildCards`, `InstallModAsync`, `AddManualGame` → если `_manifestWikiUnlinks.Contains(game.Name)` → `mod = null`, `fallback = null`

### `donationUrls` — `Dict<string,string>`
Ссылки на страницы пожертвований по отображаемым именам авторов. Вливаются в захардкоженный словарь; записи манифеста приоритетнее.
- `MainViewModel.Init`, `BackgroundScan` → `GameCardViewModel.MergeManifestAuthorData(DonationUrls, AuthorDisplayNames)`

### `authorDisplayNames` — `Dict<string,string>`
Переопределения отображаемых имён мейнтейнеров из вики (например, `"oopydoopy": "Jon"`). Вливаются в захардкоженный словарь.
- `GameCardViewModel.MergeManifestAuthorData()` → тот же вызов, что у `donationUrls`

### `authorOverrides` — `Dict<string,string>`
Задаёт автора мода для игр без записи в вики (моды только на Discord/Nexus).
- `GameInitializationService.ApplyManifestCardOverrides()` → `card.Maintainer = author` (только если `card.Maintainer` пуст)

---

## Поля переопределения ссылок

Все следуют одному паттерну в своих сервисах — проверяются первыми, до любых источников из скрейпа/кеша.

| Поле | Где используется | Что переопределяет |
|-------|---------|-------------------|
| `nexusUrlOverrides` | `NexusModsService` | Игра → URL страницы Nexus Mods |
| `pcgwUrlOverrides` | `PcgwService` | Игра → URL страницы PCGamingWiki |
| `uwFixUrlOverrides` | `UltraWideFixService` | Игра → URL ультраширокого исправления |
| `ultraPlusUrlOverrides` | `UltraPlusService` | Игра → URL Ultra+ |
| `steamAppIdOverrides` | `SteamAppIdResolver.ResolveAsync()` | Форсирует Steam AppID (наивысший приоритет, до ACF/файлов) |

### `optiScalerWikiNames` — `Dict<string,string>`
Отображает имена игр RHI на имена в списке совместимости вики OptiScaler (когда они различаются).
- `AddonInfoResolver.ResolveOptiScalerWikiName()` → используется перед всеми запросами вики OptiScaler

---

## Поля профилей NVIDIA / DLSS

### `profileExeExclusions` — `List<string>`
Дополнительные имена exe, исключаемые из сопоставления профилей NVIDIA (расширяет захардкоженные значения по умолчанию).
- `DlssPresetService.ApplyManifestProfileConfig()` → вливается в `_excludedProfileExeNames`
- `FindProfileUncached()` → `exeNames.ExceptWith(_excludedProfileExeNames)`

### `profileNameOverrides` — `Dict<string,string>`
Перенаправляет имя игры на другое имя профиля драйвера NVIDIA (первая попытка сопоставления, до сканирования exe).
- `DlssPresetService.ApplyManifestProfileConfig()` → сохраняется как `_profileNameOverrides`
- `FindProfileUncached()` → проверяется до точного совпадения названия и сканирования exe

### `dlssPresets` — `ManifestDlssPresets`
Внедряет новые варианты пресетов DLSS (SR/RR/FG) в выпадающие списки панели подробностей без обновления клиента.
- `DlssPresetService.ApplyManifestPresets()` → вливает `.Sr`, `.Rr`, `.Fg` в статические массивы пресетов
- Вызывается при Init и BackgroundScan. Записи с `disabled: true` удаляют существующие пресеты по имени.

### `rtxHdrInfoUrl` — `string`
URL гиперссылки «Руководство по калибровке RTX HDR» в диалоге информации RenoDX. Запасной вариант — захардкоженный пост на Reddit.
- Читается в конструкторе диалога информации `DialogService.Game`

---

## Поля DOF Fix

### `dofFixSkipGames` — `List<string>`
Подавляет применимость DOF Fix для конкретных игр (нет проблемы с DOF или заведомо несовместимо).
- `MainViewModel.Init`, `BackgroundScan` → `DofFixService.SetSkipGames()`
- `DofFixService.IsEligible()` → возвращает `false`, если игра в списке

### `dofFixForceGames` — `List<string>`
Форсирует применимость DOF Fix для игр, где не сработало определение движка UE. Требование 64-бит остаётся.
- `MainViewModel.Init`, `BackgroundScan` → `DofFixService.SetForceGames()`
- `DofFixService.IsForceEligible()` → возвращает `true`

---

## Поля графического API

### `graphicsApiOverrides` — `Dict<string,string>`
Форсирует конкретный значок графического API, перекрывая весь анализ PE-импортов.
Поддерживает несколько API через запятую: `"DX12, VLK"` помечает игру как dual-API.
Допустимые токены: `DX8`, `DX9`, `DX10`, `DX11`, `DX12`, `Vulkan`/`VLK`, `OpenGL`/`OGL`.
- `MainViewModel.GameMatching.DetectGraphicsApi()` и `_DetectAllApisForCard()` → проверяется после пользовательского переопределения API, до любого сканирования файловой системы
- Влияет на: значок API на карточке, автоматически выбранное имя DLL ReShade, видимость переключателя DXVK, развертывание DLL DXVK

---

## Поля переопределения наборов / пресетов

### `shaderPacks` — `Dict<string,ManifestShaderPack>`
Добавление, переопределение или отключение наборов шейдеров без обновления клиента.
- `ShaderPackService.ApplyManifestOverrides()` → вызывается при Init и BackgroundScan
- `disabled: true` убирает набор из активного списка
- Для новых наборов минимум: `url` и `kind` (`"GhRelease"` или `"DirectUrl"`)

### `addonPacks` — `Dict<string,ManifestAddonPack>`
Добавление, переопределение или отключение записей аддонов без обновления клиента. Ключ — `SectionId`.
- `AddonPackService.ApplyManifestOverrides()` → вызывается при Init и BackgroundScan; также повторно применяется при открытии диалога менеджера аддонов
- `disabled: true` убирает аддон

### `componentUrls` — `Dict<string,string>`
Переопределение базовых ссылок скачивания компонентов. Активные ключи:
- `"ueExtended"` → ссылка скачивания аддона UE-Extended (`MainViewModel.Install.UeExtendedUrl`)
- `"ueDofFix"` → переопределение URL DOF Fix (`DofFixService.ManifestUrlOverride`, задаётся при Init/BackgroundScan)

---

## Прочие поля

### `version` — `int`
Целочисленная версия манифеста. Пишется в лог при каждом вызове `ApplyManifest` для диагностики.

### `gacSymlinkGames` — см. [Поля поведения установки](#gacSymlinkGames)

---

## Ключевые архитектурные заметки

1. **Два пути применения**: большинство полей обрабатываются в `GameInitializationService.ApplyManifest()` (наполняет общие множества/словари), затем читаются BuildCards/CacheLoad. Поигровые поля (`forceExternalOnly`, `gameNotes`, `authorOverrides` и т.п.) применяются `ApplyManifestCardOverrides()` после сборки карточек.

2. **Три пути инициализации**: поля, влияющие на отображение игр, должны применяться во ВСЕХ трёх: `InitializeAsync` (полное сканирование), `LoadCacheAndBuildCardsAsync` (фаза 1, показ из кеша) И `RunBackgroundScanAndMergeAsync` (фаза 2, обновление). Пропуск одного приводит к рассинхрону состояний между фазами.

3. **Регистр символов**: `ManifestService.Normalize()` пересобирает большинство словарей с `StringComparer.OrdinalIgnoreCase`. НЕ нормализуются: `dxvkApiOverrides` (поиск чувствителен к регистру, как десериализовано). `lumaRenodxCompat` использует прямой `Contains(game.Name)` — точное совпадение.

4. **Сопоставление имён**: `GetManifestDllNames()`, `GetGacSymlinkPath()`, `ResolveEngineOverride()` пробуют: (1) точное совпадение, (2) без торговых знаков (™®©), (3) полностью нормализованное. Большинство остальных полей — только точное совпадение.

5. **Приоритет пользовательских переопределений**: манифестный `WikiNameOverrides` добавляется только если ключа ещё нет в `_nameMappings` (пользователь сильнее). Манифестные `DllNameOverrides` блокируются поигровыми отказами в `DllOverrideService`. Манифестный `LegacyReShadeVersions` не перезаписывает существующие переопределения канала RS.

6. **`AuxInstallService.GlobalManifest`**: статическая ссылка на живой манифест, доступная сервисам, которые не получают манифест через DI. Используется `RenodxIniOverrides`, `RenodxExtraSettings`, `UeExtendedCompatibility` в потоке обновления.
