# База данных RHI — план полной замены вики

## Цель

Заменить вики RenoDX (`https://github.com/clshortfuse/renodx/wiki/Mods`) как источник истины по данным модов на базу, поддерживаемую RHI (`rhi-repo`). Вики перестанет загружаться вовсе, как только база будет полна и ей можно доверять.

---

## Текущее состояние

- Настройка `RenoDxDbSource` управляет тем, какой источник кормит `_allMods`: `"WikiOnly"` (по умолчанию), `"DbOnly"`, `"Hybrid"`
- `WikiService.FetchAllAsync` выполняется всегда, независимо от `RenoDxDbSource` — в режиме DbOnly результат вики просто отбрасывается в `MergeDbSources`
- Вики также используется для сопоставления Luma, нормализации имён игр и отслеживания `SeenWikiMods`
- База пока доступна только разработчикам (врата `DevUnlockService.IsUnlocked` в `InitializeAsync` и `RunBackgroundScanAndMergeAsync`)

---

## Фаза 1 — пропуск загрузки вики в режиме DbOnly (быстрая победа)

**Файлы:** `MainViewModel.Init.cs`, `MainViewModel.BackgroundScan.cs`

Добавьте ограждение вокруг вызовов `WikiService.FetchAllAsync`:

```csharp
if (!string.Equals(_settingsViewModel.RenoDxDbSource, "DbOnly", StringComparison.OrdinalIgnoreCase))
{
    _allMods = await _wikiService.FetchAllAsync(progress).ConfigureAwait(false);
}
```

Также оградите обновления `SeenWikiMods` — они должны выполняться, только когда вики активный источник.

**Результат:** экономит ~300 мс при запуске и один лишний HTTP-запрос на фоновое сканирование в режиме DbOnly.

---

## Фаза 2 — перенос оставшихся зависимостей от вики на базу

Эти функции, доступные только через вики, нужно перенести, прежде чем вики можно будет удалить полностью:

### 2a. Сопоставление Luma
`MatchLumaGame(gameName)` ищет записи Luma по имени в `_allMods`. Сейчас источник — вики. В базе есть отдельная таблица `RenoDXdb-unreal.json` для игр UE-Extended. Именные моды Luma стоит добавить в базу полем `lumaUrl` или `isLuma: true`, либо держать отдельным эндпоинтом базы.

### 2b. `SeenWikiMods` / определение «новых модов»
`SeenWikiModsService` отслеживает, какие имена модов пользователь уже видел — используется для значка уведомления «Новые моды». Сейчас наполняется именами модов из вики. Должно наполняться именами из базы. Изменение с низким риском.

### 2c. Нормализация имён игр для сопоставления с вики
`GameDetectionService.MatchGame` сверяет найденные имена игр с нормализованными именами вики для `IsWikiExclusion`, `WikiStatusBadge` и т.п. Если вики удаляется, эти поиски должны питаться записями базы. Поле `name` в базе уже использует ту же конвенцию имён.

### 2d. `ToggleWikiExclusion` и поигровые переопределения включения в вики
Семантика `IGameNameService.WikiExclusions` / `WikiOptIns` сейчас отсылает к «вики». Переименуйте в `ModDbExclusions` / `ModDbOptIns` и обновите все точки вызова. Чистое переименование, без смены логики.

### 2e. Фильтр устаревших модов
`WikiService` пропускает моды, перечисленные после заголовка «Deprecated». В базе должно быть `"status": "Deprecated"`, либо устаревшие моды просто опускаются. Перед снятием фильтра убедитесь, что в базе нет устаревших записей.

---

## Фаза 3 — полное удаление вики

1. Удалить `WikiService.cs` и `IWikiService.cs`
2. Убрать `WikiService` из регистрации DI в `App.xaml.cs`
3. Убрать все ссылки на поле `_wikiService` в `MainViewModel`
4. Убрать наполнение `_allMods` из `WikiService.FetchAllAsync` — заменить загрузкой только из базы
5. Поменять умолчание `RenoDxDbSource` на `"DbOnly"` навсегда (или убрать настройку целиком)
6. Убрать `RenoDxDbSourceCard` из `MainWindow.xaml` и `SettingsHandler.InitRenoDxDbSourceCombo()`
7. Убрать врата `DevUnlockService.IsUnlocked` из мест вызова `RenoDXDbService.FetchAllAsync`
8. Обновить ссылки `SeenWikiMods` → `SeenDbMods` по всему коду

---

## Требования к полноте базы перед фазой 3

База должна покрыть все моды, сейчас присутствующие на вики RenoDX, прежде чем вики можно будет убрать:
- Все именные поигровые моды (сейчас ~954 записи в вики против 272 в базе)
- Все универсальные игры UE-Extended (сейчас скрейпятся с вики; в базе ~70)
- Паритет поля статуса: `"Done"`, `"WIP"`, `"Deprecated"` с семантикой ✅/🚧 вики
- `snapshotUrl` для каждого мода (те же ссылки, что в вики — для существующих записей базы уже так)

Запускайте `docs/merge_game_db.py` по заявкам сообщества, чтобы быстрее наращивать покрытие базы.

---

## Ключевые файлы

| Файл | Роль |
|------|------|
| `RenoDXCommander/Services/WikiService.cs` | Загружает и парсит вики RenoDX — удаляется в фазе 3 |
| `RenoDXCommander/Services/RenoDXDbService.cs` | Загружает базу rhi-repo — станет единственным источником модов |
| `RenoDXCommander/ViewModels/MainViewModel.cs` | `MergeDbSources()` — логику слияния упростить после удаления вики |
| `RenoDXCommander/ViewModels/MainViewModel.Init.cs` | Сюда добавить ограждение фазы 1 |
| `RenoDXCommander/ViewModels/MainViewModel.BackgroundScan.cs` | Сюда добавить ограждение фазы 1 |
| `RenoDXCommander/Services/SeenWikiModsService.cs` | Переименовать + перенаправить на базу в фазе 2 |
| `game-db/game_db.json` | Данные заявок сообщества |
| `docs/RenoDXdb.json` | Файл базы именных модов |
| `docs/RenoDXdb-unreal-ue-extended.json` | Файл базы UE-Extended |
