# Полноценная интеграция загрузок Nexus Mods — руководство по реализации

## Назначение

Этот документ — полное руководство по реализации поддержки скачивания/обновления Nexus Mods в один клик в RHI. Он охватывает то, что уже существует в RHI, Nexus API целиком и что именно нужно построить — файл за файлом.

---

## Сборка и тестирование

```powershell
# Сборка
dotnet build g:\RDXC\RenoDXCommander\RenoDXCommander.csproj --no-restore -v q -p:Platform=x64

# Публикация (деплоит в место работающей сборки)
# Запустите publish.bat из g:\RDXC\
```

---

## Что уже существует в RHI

### Определение обновлений — `NexusUpdateService.cs`
`RenoDXCommander/Services/NexusUpdateService.cs`

Уже работает. Использует **Nexus GraphQL v2 API** (`https://api.nexusmods.com/v2/graphql`) — **API-ключ не нужен**.

- Запрашивает `legacyModsByDomain` парами `{gameDomain, modId}`
- `ParseNexusUrl(url)` → извлекает `(Domain, ModId)` из ссылки вида `nexusmods.com/domain/mods/id`
- Сравнивает метку времени `updatedAt` с сохранёнными базовыми значениями в `%LocalAppData%\RHI\nexus_baselines.json`
- Модель `NexusBaseline`: `Domain`, `ModId`, `LastKnownUpdate`, `InstalledVersion`, `HasUpdate`
- Ставит `card.Status = UpdateAvailable`, когда удалённая версия новее

**Чего не хватает в `NexusBaseline`:** `FileId` (конкретный file_id установленный). Нужно добавить.

### Каталог игр — `NexusModsService.cs`
`RenoDXCommander/Services/NexusModsService.cs`

Загружает каталог игр Nexus (`https://data.nexusmods.com/file/nexus-data/games.json`), строит нормализованный поиск «имя игры → ссылка Nexus». Используется для кнопки вики-ссылки в стиле PCGW, НЕ для загрузок. Этот сервис отделён от функциональности загрузки.

### Состояние карточки для Nexus Mods
Карточка получает `IsExternalOnly = true`, когда `effectiveMod.SnapshotUrl == null && effectiveMod.NexusUrl != null`. Это моды, размещённые на Nexus, которые RHI пока не может скачать напрямую.

Когда `IsExternalOnly`:
- `card.ExternalUrl` = URL страницы мода Nexus (например, `https://www.nexusmods.com/kingdomsofamalurreckoning/mods/64`)
- `card.ExternalLabel` = `"Download from Nexus Mods"`
- Кнопка установки открывает браузер на `ExternalUrl` (см. `MainWindow.Events.cs` → `ExternalLinkButton_Click`)
- `card.NexusUrl` = то же, что `ExternalUrl` для модов Nexus (задаётся в `MainViewModel.Install.cs` и `BuildCards.cs`)
- `AutoUpdateService` сейчас **исключает** карточки с `IsExternalOnly` — строка 113: `&& !c.IsExternalOnly`

### Существующая запись об установке — `InstalledModRecord.cs`
`RenoDXCommander/Models/InstalledModRecord.cs`

Поля: `GameName`, `Store`, `AddonFileName`, `FileHash`, `InstalledAt`, `InstallPath`, `InstalledVersion` и другие.

**Чего не хватает:** `NexusFileId` (int) — file_id из Nexus API, нужен, чтобы отличать установленный файл от последнего. Добавьте это поле.

### Пересылка в единственный экземпляр — `SingleInstanceService.cs`
`RenoDXCommander/Services/SingleInstanceService.cs`

Использует именованный канал (`RenoDXCommander_AddonPipe`), чтобы пересылать пути файлов из второго экземпляра в работающий. Срабатывает событие `FileReceived` с путём. Точно этот же паттерн нужен для пересылки протокола NXM — NXM-ссылка приходит аргументом командной строки в новый процесс RHI, который должен переслать её работающему экземпляру.

### Хранение настроек — `SettingsViewModel.cs`
`RenoDXCommander/ViewModels/SettingsViewModel.cs`

Использует поля `[ObservableProperty]` с паттерном `LoadSettingsFromDict` / `SaveSettingsToDict` против `%LocalAppData%\RHI\settings.json`. Добавьте сюда `NexusApiKey` (string) и `NexusIsPremium` (bool).

Паттерн для новой строковой настройки:
```csharp
// Объявление
[ObservableProperty] private string _nexusApiKey = "";

// Загрузка
if (s.TryGetValue("NexusApiKey", out var nakVal)) NexusApiKey = nakVal ?? "";

// Сохранение
if (!string.IsNullOrEmpty(NexusApiKey)) s["NexusApiKey"] = NexusApiKey;
```

---

## Nexus Mods API — полный справочник

### Базовый URL
```
https://api.nexusmods.com/v1/
```

### Аутентификация — все REST-эндпоинты v1
Каждый запрос требует этих заголовков:
```
apikey: {USER_API_KEY}
Application-Name: RHI
Application-Version: 2.4.x
```

### Проверка API-ключа + статус подписки
```
GET /v1/users/validate.json
```
Возвращает:
```json
{
  "user_id": 12345,
  "key": "...",
  "name": "Username",
  "is_premium": true,
  "is_supporter": false
}
```
Вызывайте, когда пользователь вводит/подключает ключ. Сохраняйте `is_premium` как `NexusIsPremium` в настройках.

### Список файлов мода
```
GET /v1/games/{game_domain}/mods/{mod_id}/files.json
```
Возвращает все файлы мода. Каждый файл:
```json
{
  "file_id": 67890,
  "name": "Mod Name",
  "version": "1.5",
  "category_name": "MAIN",
  "is_primary": true,
  "uploaded_timestamp": 1700000000,
  "size_kb": 1234,
  "file_name": "modname-1.5.zip"
}
```
Чтобы найти свежий файл для скачивания: фильтруйте `category_name == "MAIN"`, берите максимальный `uploaded_timestamp`.

### Получение ссылок для скачивания (ТОЛЬКО ПРЕМИУМ)
```
GET /v1/games/{game_domain}/mods/{mod_id}/files/{file_id}/download_links.json
```
**Для бесплатных пользователей возвращает HTTP 403** с сообщением: `"You don't have permission to get download links from the API without visiting nexusmods.com — this is for premium users only."`

Для премиум-пользователей возвращает:
```json
[
  {
    "name": "Nexus CDN",
    "short_name": "Nexus",
    "URI": "https://cf-files.nexusmods.com/cdn/...?md5=xxx&expires=1234567890&user_id=xxx"
  }
]
```
`URI` — прямая HTTPS-ссылка на скачивание, которая **протухает примерно через 30 минут**. Никогда не кешируйте её — генерируйте свежую на каждую загрузку.

### Получение ссылок (БЕСПЛАТНО — путь через NXM-ключ)
Тот же эндпоинт, но с параметрами запроса из ссылки протокола NXM:
```
GET /v1/games/{game_domain}/mods/{mod_id}/files/{file_id}/download_links.json?key={key}&expires={expires}&user_id={user_id}
```
Работает для бесплатных пользователей, когда параметры приходят из NXM-ссылки, сгенерированной сайтом Nexus.

### Лимиты запросов
- 20 000 запросов за 24 часа (сброс в 00:00 GMT)
- После 20k: 500 в час
- Заголовки ответа: `X-RL-Hourly-Remaining`, `X-RL-Daily-Remaining`, `X-RL-Hourly-Reset`, `X-RL-Daily-Reset`
- Всегда проверяйте эти заголовки и корректно снижайте активность

---

## Протокол NXM — путь загрузки бесплатного пользователя

### Формат ссылки
```
nxm://{game_domain}/mods/{mod_id}/files/{file_id}?key={key}&expires={timestamp}&user_id={user_id}
```
Пример:
```
nxm://kingdomsofamalurreckoning/mods/64/files/67890?key=AbCdEf123&expires=1787000000&user_id=99999
```

### Как это работает
1. Пользователь нажимает «Mod Manager Download» на nexusmods.com
2. Браузер запускает протокол `nxm://`, Windows ищет зарегистрированный обработчик
3. Зарегистрированный обработчик (RHI) получает ссылку аргументом командной строки: `RHI.exe --nxm "nxm://..."`
4. RHI разбирает ссылку, вызывает эндпоинт download_links с параметрами ключа
5. Скачивает и устанавливает файл

### Реестр Windows — обработчик протокола
Записывать при первом запуске RHI или в установщике:
```
HKEY_CURRENT_USER\Software\Classes\nxm
  (Default) = "URL:NXM Protocol"
  "URL Protocol" = ""

HKEY_CURRENT_USER\Software\Classes\nxm\shell\open\command
  (Default) = "\"C:\Users\...\RHI.exe\" --nxm \"%1\""
```
Используйте `Environment.ProcessPath` для пути exe RHI (не `AppContext.BaseDirectory` — неверно для single-file публикации).

**Конфликт с Vortex/MO2**: эти приложения тоже регистрируют `nxm://`. Побеждает тот, кто зарегистрировался последним. Перезаписывайте только если обработчика нет, или показывайте диалог с предложением перехватить.

### Пересылка NXM в единственный экземпляр
RHI может уже работать к моменту прихода NXM-ссылки. Новый процесс должен переслать её работающему экземпляру:

В `App.OnLaunched` проверьте аргумент `--nxm`. Если RHI уже запущен, вызовите `SingleInstanceService.SendToRunningInstance("nxm:" + url)` (префикс нужен, чтобы отличать от файлов аддонов) и выйдите.

В работающем экземпляре срабатывает `SingleInstanceService.FileReceived`. Добавьте проверку: если принятая строка начинается с `"nxm:"`, направляйте в обработчик NXM вместо обработчика drag-drop аддонов.

Имя канала `SingleInstanceService`: `RenoDXCommander_AddonPipe`. Существующий канал строковый — просто добавьте к NXM-ссылке префикс `"nxm:"`, чтобы приёмник мог различить.

---

## Регистрация приложения в Nexus (нужно для публичного релиза)

Перед выпуском пользователям напишите на `support@nexusmods.com`:
- Тестовую сборку RHI с вводом API-ключа
- Название приложения: `RHI`
- Краткое описание: инструмент управления ReShade, RenoDX и HDR-модами в библиотеках ПК-игр
- Логотип: высокое разрешение, видимый на тёмном фоне

Они выдадут **slug** (например, `"rhi"`) для SSO. До регистрации для тестирования работают персональные API-ключи. Использование персональных ключей в публичном приложении нарушает их AUP.

### Поток SSO (после регистрации — опционально, но удобнее)
Вместо копипасты пользователи могут авторизоваться через браузер:
1. Сгенерируйте UUID v4
2. Откройте WebSocket: `wss://sso.nexusmods.com`
3. Отправьте: `{ "id": "<uuid>", "appid": "rhi" }`
4. Пингуйте каждые 30 с для поддержания
5. Откройте `https://www.nexusmods.com/sso?id=<uuid>` в браузере пользователя
6. Пользователь нажимает Authorise на сайте Nexus
7. WebSocket принимает API-ключ простой строкой
8. Сохраните ключ, закройте сокет

`Windows.System.Launcher.LaunchUriAsync` в WinUI 3 открывает браузер. Для WebSocket используйте `System.Net.WebSockets.ClientWebSocket`.

---

## Что нужно построить — файл за файлом

### 1. `SettingsViewModel.cs` — поля API-ключа
```csharp
[ObservableProperty] private string _nexusApiKey = "";
[ObservableProperty] private bool _nexusIsPremium;
[ObservableProperty] private string _nexusUsername = "";
```
Загрузка/сохранение по существующему паттерну. Никогда не пишите значение ключа в лог.

### 2. `NexusDownloadService.cs` (новый файл)
`RenoDXCommander/Services/NexusDownloadService.cs`

```csharp
public class NexusDownloadService
{
    Task<NexusUserInfo?> ValidateApiKeyAsync(string apiKey);
    Task<List<NexusModFile>> GetModFilesAsync(string domain, int modId);
    Task<string?> GetDownloadUriAsync(string domain, int modId, int fileId);        // premium
    Task<string?> GetDownloadUriWithNxmKeyAsync(string domain, int modId, int fileId, string key, string expires, string userId); // free
    Task<string?> DownloadToTempAsync(string uri, IProgress<(string msg, double pct)>? progress);
    bool IsApiKeyConfigured { get; }
    bool IsPremium { get; }
}
```

Модели:
```csharp
public record NexusUserInfo(int UserId, string Name, bool IsPremium);
public record NexusModFile(int FileId, string Name, string Version, string CategoryName, long UploadedTimestamp, string FileName);
```

Внедрить через DI как синглтон. Зарегистрируйте в `App.xaml.cs` рядом с другими сервисами.

### 3. `InstalledModRecord.cs` — добавить NexusFileId
Добавьте:
```csharp
public int? NexusFileId { get; set; }
```
Записывать при установке мода Nexus (сегодня drag-drop, позже прямое скачивание).

### 4. `NexusBaseline` — добавить FileId
В `NexusUpdateService.cs` добавьте в `NexusBaseline`:
```csharp
[JsonPropertyName("fileId")]
public int? FileId { get; set; }
```

### 5. UI настроек
В `MainWindow.xaml` добавьте новую секцию в соответствующую карточку настроек (или создайте карточку «Nexus Mods»). Нужно:
- `PasswordBox` (или `TextBox`) для вставки API-ключа
- Кнопка «Подключить», вызывающая `ValidateApiKeyAsync` и показывающая `"Connected as {name} (Premium)"` или `"Connected as {name} (Free)"`
- Кнопка «Отключить», очищающая ключ
- Текст описания: премиум = автоматические загрузки, бесплатный = в один клик через браузер

В `SettingsHandler.cs` инициализируйте UI из `ViewModel.Settings.NexusApiKey` в `SettingsButton_Click`.

### 6. `NxmProtocolHandler.cs` (новый файл)
`RenoDXCommander/Services/NxmProtocolHandler.cs`

Разбирает и маршрутизирует NXM-ссылки:
```csharp
public static class NxmProtocolHandler
{
    // Parses nxm://domain/mods/id/files/fileid?key=x&expires=y&user_id=z
    public static NxmLink? Parse(string nxmUrl);

    // Writes the registry entries to claim the nxm:// handler
    public static void RegisterProtocolHandler();

    // Returns true if RHI is currently the registered nxm:// handler
    public static bool IsRegistered();
}

public record NxmLink(string Domain, int ModId, int FileId, string Key, string Expires, string UserId);
```
Вызывайте `RegisterProtocolHandler()` при первом запуске (сначала проверьте `IsRegistered()`, чтобы не перезаписать Vortex).

### 7. `App.OnLaunched` — обработка NXM-аргумента
В `App.xaml.cs` `OnLaunched` уже проверяет паттерн `--nxm` в аргументах командной строки. Добавьте:
```csharp
string? nxmArg = null;
if (cmdArgs.Length > 1 && cmdArgs[1].StartsWith("nxm://", StringComparison.OrdinalIgnoreCase))
    nxmArg = cmdArgs[1];
// OR --nxm "nxm://..." (two-arg form for protocol handler)
var nxmIdx = Array.IndexOf(cmdArgs, "--nxm");
if (nxmIdx >= 0 && nxmIdx < cmdArgs.Length - 1)
    nxmArg = cmdArgs[nxmIdx + 1];
```

Если RHI уже запущен: `SingleInstanceService.SendToRunningInstance("nxm:" + nxmArg)` и выход.
Если это первый экземпляр: сохраните NXM-ссылку и обработайте её, когда `MainWindow` будет готов.

### 8. `SingleInstanceService.cs` — маршрутизация NXM
В `MainWindow.xaml.cs`, где подключается `FileReceived`:
```csharp
SingleInstanceService.FileReceived += path =>
{
    if (path.StartsWith("nxm:", StringComparison.OrdinalIgnoreCase))
        HandleIncomingNxmUrl(path.Substring(4));
    else
        HandleIncomingAddonFile(path);
};
```

`HandleIncomingNxmUrl` разбирает NXM-ссылку, вызывает `NexusDownloadService.GetDownloadUriWithNxmKeyAsync`, скачивает, затем направляет в поток установки.

### 9. `MainViewModel.Install.Nexus.cs` (новый partial-файл)
`RenoDXCommander/ViewModels/MainViewModel.Install.Nexus.cs`

```csharp
public partial class MainViewModel
{
    // Downloads and installs a specific Nexus file for a card
    public async Task InstallNexusModAsync(GameCardViewModel card, int fileId);

    // Finds the latest MAIN file and installs it (update path)
    public async Task UpdateNexusModAsync(GameCardViewModel card);

    // Handles an incoming NXM link — finds the matching card and installs
    public async Task HandleNxmLinkAsync(NxmLink link);
}
```

Поток `UpdateNexusModAsync`:
1. Разберите `card.NexusUrl` через `NexusUpdateService.ParseNexusUrl()` → получите `(domain, modId)`
2. Вызовите `NexusDownloadService.GetModFilesAsync(domain, modId)` → получите список файлов
3. Найдите свежий MAIN-файл (максимальный `UploadedTimestamp` при `CategoryName == "MAIN"`)
4. Если `NexusIsPremium`: вызовите `GetDownloadUriAsync` → скачайте → распакуйте/разверните
5. Если бесплатный: откройте браузер на странице мода (существующее поведение) — NXM-путь доделает остальное, когда пользователь нажмёт
6. При успехе: обновите `InstalledModRecord.NexusFileId`, вызовите `NexusUpdateService.ResetBaseline(card.GameName)`

### 10. `AutoUpdateService.cs` — добавить карточки Nexus
В `RunUpdatePassAsync` уберите фильтр `&& !c.IsExternalOnly` и замените специфичной для Nexus обработкой:
```csharp
// Nexus mods — premium only for silent update
var nexusCards = cards.Where(c =>
    c.Status == GameStatus.UpdateAvailable
    && !c.IsHidden
    && !c.ExcludeFromUpdateAllRenoDx
    && c.IsExternalOnly
    && !string.IsNullOrEmpty(c.NexusUrl)
    && _nexusDownloadService.IsPremium
    && !string.IsNullOrEmpty(c.InstallPath)).ToList();

foreach (var card in nexusCards)
{
    if (card.IsRunning) { EnqueueRetry(card, "Nexus"); continue; }
    await TryUpdateOneAsync("Nexus", card, () => _viewModel!.UpdateNexusModAsync(card));
    await Pause();
}
```

`AutoUpdateService` сейчас не имеет доступа к `NexusDownloadService` — внедрите через `SetViewModel` или добавьте в конструктор.

### 11. Маршрутизация кнопок установки/обновления
В `MainWindow.Events.cs` `ExternalLinkButton_Click` сейчас всегда открывает браузер. Когда API-ключ Nexus настроен И пользователь премиум, перехватывайте:

```csharp
private async void ExternalLinkButton_Click(object sender, RoutedEventArgs e)
{
    var card = GetCardFromSender(sender);
    if (card == null) return;

    var nexusService = App.Services.GetRequiredService<NexusDownloadService>();
    if (card.NexusUrl != null && nexusService.IsApiKeyConfigured && nexusService.IsPremium)
    {
        // Download directly
        await ViewModel.UpdateNexusModAsync(card);
        return;
    }

    // Existing browser-open path
    var url = card.IsExternalOnly ? card.ExternalUrl : (card.NexusUrl ?? card.DiscordUrl ?? card.ExternalUrl);
    if (!string.IsNullOrEmpty(url))
        await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
    ...
}
```

---

## Распаковка / установка после скачивания — точный паттерн

Когда CDN-URI получен, скачивание и установка должны идти через существующую инфраструктуру, а не новый конвейер. Вот точный поток:

### Скачанное с Nexus — `.zip`, внутри файл `.addon64`

Стандартный случай для модов RenoDX на Nexus. Поток:

1. Скачайте zip во временный путь через `HttpClient` (поток в файл, тот же паттерн, что шаги 3–4 в `ModInstallService.InstallAsync`)
2. Распакуйте 7-Zip'ом: `App.Services.GetRequiredService<ISevenZipExtractor>().Find7ZipExe()` даёт exe 7z, затем `Process.Start` с аргументами `x "{zipPath}" -o"{tempDir}" -y`
3. Найдите файлы `.addon64`/`.addon32` во временной папке:
   ```csharp
   var addonFiles = Directory.GetFiles(tempDir, "*.addon64", SearchOption.AllDirectories)
       .Concat(Directory.GetFiles(tempDir, "*.addon32", SearchOption.AllDirectories))
       .Where(f => Path.GetFileName(f).StartsWith("renodx-", StringComparison.OrdinalIgnoreCase))
       .ToList();
   ```
4. Скопируйте аддон в путь развертывания игры:
   ```csharp
   var deployDir = ModInstallService.GetAddonDeployPath(card.InstallPath);
   File.Copy(addonPath, Path.Combine(deployDir, addonFileName), overwrite: true);
   ```
5. Сохраните `InstalledModRecord`:
   ```csharp
   var record = new InstalledModRecord
   {
       GameName = card.GameName,
       Store = card.Source ?? "",
       InstallPath = card.InstallPath,
       AddonFileName = addonFileName,
       InstalledAt = DateTime.UtcNow,
       InstalledVersion = versionFromNexus,
       NexusFileId = fileId,  // new field — see Phase 3
   };
   _installer.SaveRecordPublic(record);
   ```
6. Пост-шаги установки (зеркало `UpdateOrchestrationService.UpdateAllRenoDxAsync`):
   - Развернуть Engine.ini LUT: `AuxInstallService.ApplyEngineIniLutSetting(...)`, если UE-игра
   - Развернуть Engine.ini HDR: `AuxInstallService.ApplyEngineIniHdrSettings(...)`, если UE-Extended
   - Применить `renodxIniOverrides` из манифеста: `AuxInstallService.ApplyRenodxIniOverrides(...)`
7. Обновите состояние карточки на диспетчере:
   ```csharp
   DispatcherQueue?.TryEnqueue(() =>
   {
       card.InstalledRecord = record;
       card.InstalledAddonFileName = addonFileName;
       card.RdxInstalledVersion = AuxInstallService.ReadInstalledVersion(record.InstallPath, record.AddonFileName);
       card.Status = GameStatus.Installed;
       card.ActionMessage = "✅ Updated!";
       card.NotifyAll();
       card.FadeMessage(m => card.ActionMessage = m, card.ActionMessage);
   });
   ```
8. Сбросьте базовую линию Nexus: `_nexusUpdateService.ResetBaseline(card.GameName)`
9. Сохраните библиотеку: `SaveLibrary()`

**НЕ направляйте через `DragDropHandler.ProcessDroppedAddon`** — этот метод показывает диалог выбора игры, спрашивая пользователя, куда ставить. Для программной установки, когда карточка уже известна, копируйте файл и сохраняйте запись напрямую, как выше.

`DragDropHandler.ProcessDroppedArchive` и `ProcessDroppedAddon` — **только для drag-drop по инициативе пользователя**: они показывают ContentDialog'ы, запрашивают подтверждение и требуют взаимодействия. Путь скачивания должен быть тихим.

### Хелпер пути развертывания
```csharp
ModInstallService.GetAddonDeployPath(card.InstallPath)
// Returns the addon subfolder if the game uses one, otherwise the install path itself
```

---

## Состояние карточки — `InstallActionLabel`, `CanInstall`, `CardRdxInstallEnabled`

Это вычисляемые свойства в `GameCardViewModel.RenoDX.cs`.

**Текущее состояние карточек с `IsExternalOnly`** (сегодня моды только на Nexus):
- `CanInstall` возвращает `false` — он явно исключает `IsExternalOnly`: `Mod?.SnapshotUrl != null && !IsInstalling && !IsExternalOnly && ...`
- `InstallActionLabel` проваливается в подписи по статусу (Install/Update/Reinstall), но кнопка отключена
- В строке карточки вместо кнопки установки показывается кнопка внешней ссылки

**Что нужно изменить** при добавлении прямого скачивания:

`CanInstall` в `GameCardViewModel.RenoDX.cs`, строка 30, должен включить премиум-путь Nexus:
```csharp
public bool CanInstall => IsRtxHdrEnabled
    || (Mod?.SnapshotUrl != null && !IsInstalling && !IsExternalOnly && (IsRsInstalled || ExcludeFromUpdateAllReShade))
    || (IsExternalOnly && NexusUrl != null && /* nexusDownloadService.IsPremium — pass via card property */);
```

Чище всего: добавить `[ObservableProperty] private bool _nexusDirectDownloadAvailable` в `GameCardViewModel.cs`, задавать в `BuildCards`/`CacheLoad`, когда `card.NexusUrl != null && settings.NexusIsPremium`, и использовать в выражении `CanInstall`. Это позволяет избежать зависимостей ViewModel от сервисов.

`InstallActionLabel` для «только внешняя» с прямым скачиванием:
- `Status == NotInstalled` → `"Download from Nexus Mods"` (или `"Install"`)
- `Status == UpdateAvailable` → `"⬆  Update"` 
- `Status == Installed` → `"↺  Reinstall"`

Существующий обработчик `ExternalLink_Click` в `MainWindow.Events.cs` (строка 683) срабатывает для кнопки внешней ссылки. Это точка перехвата для премиум-пользователей — добавьте проверку сервиса там.

---

## `GameMod.NexusUrl` — откуда он берётся

**Только чтение. Не пишите в него программно.**

`WikiService.cs` парсит таблицу модов вики RenoDX. Для каждой строки мода он сканирует все ссылки — если ссылка содержит `nexusmods.com`, она сохраняется как `nexusUrl`. Это становится `GameMod.NexusUrl`. Задаётся в `WikiService.FetchAllAsync()`, строка 233.

Словарь `nexusUrlOverrides` в манифесте может переопределить это поигрово — проверяется в `NexusModsService.ResolveUrl()`, но это для **кнопки-ссылки в стиле PCGW**, а не URL загрузки мода.

Для функции скачивания `card.NexusUrl` (из `GameMod.NexusUrl`) — URL страницы мода, из которого `NexusUpdateService.ParseNexusUrl()` извлекает `(Domain, ModId)`. Затем вызываете files-API, чтобы найти свежий file_id. **Викишный `NexusUrl` — это страница мода, а не прямая ссылка на скачивание.**

---

## `CheckInstallWarningAsync` — паттерн вызова

**Определён в:** `MainViewModel.Install.Luma.cs`, строка 1016

```csharp
public async Task<bool> CheckInstallWarningAsync(string gameName, string component)
```

- `component` — строковый ключ, совпадающий с записями словаря `manifest.InstallWarnings` (например, `"renodx"`, `"reshade"`, `"luma"`, `"dxvk"` и т.п.)
- Вернул `true` → продолжать установку
- Вернул `false` → пользователь отменил, прервать установку
- Показывает `ContentDialog` с сообщением предупреждения из манифеста, если для этой комбинации игра+компонент оно есть

**Вызывайте в `InstallNexusModAsync` перед началом скачивания:**
```csharp
if (!await CheckInstallWarningAsync(card.GameName, "renodx")) return;
```

Используйте ключ `"renodx"`, так как моды Nexus — аддоны RenoDX. Новый ключ компонента не нужен.

---

## `ExternalLink_Click` — текущая реализация

`MainWindow.Events.cs`, строка 683:
```csharp
internal async void ExternalLink_Click(object sender, RoutedEventArgs e)
{
    var card = GetCardFromSender(sender);
    if (card == null) return;

    var url = card.IsExternalOnly ? card.ExternalUrl : (card.NexusUrl ?? card.DiscordUrl ?? card.ExternalUrl);

    if (!string.IsNullOrEmpty(url))
        await Windows.System.Launcher.LaunchUriAsync(new Uri(url));

    // Reset Nexus baseline when user acknowledges the update
    if (card.Status == GameStatus.UpdateAvailable && card.IsExternalOnly)
    {
        var nexusService = App.Services.GetRequiredService<INexusUpdateService>();
        nexusService.ResetBaseline(card.GameName);
        card.Status = GameStatus.Installed;
    }
}
```

**Точка перехвата для фазы 2** — перед вызовом `LaunchUriAsync` проверьте:
```csharp
var nexusDownload = App.Services.GetRequiredService<NexusDownloadService>();
if (card.NexusUrl != null && nexusDownload.IsApiKeyConfigured && nexusDownload.IsPremium)
{
    await ViewModel.InstallNexusModAsync(card);
    return;
}
// fall through to browser open
```


---

## Порядок реализации

| Фаза | Файлы | Польза для пользователя |
|---|---|---|
| 1 | `SettingsViewModel` + `NexusDownloadService` (только проверка ключа) + UI настроек | Пользователи могут подключить аккаунт, увидеть статус подписки |
| 2 | `GetModFilesAsync` + `GetDownloadUriAsync` + `DownloadToTempAsync` + `InstallNexusModAsync` + маршрутизация кнопок | Премиум-пользователи получают скачивание/обновление с карточки в один клик |
| 3 | `InstalledModRecord.NexusFileId` + `NexusBaseline.FileId` + запись при установке | Точное определение обновлений — известно, какой файл установлен |
| 4 | `UpdateNexusModAsync` + карточки Nexus в `AutoUpdateService` | Премиум-пользователи получают тихое автообновление |
| 5 | Регистрация `NxmProtocolHandler` + разбор NXM в `App.OnLaunched` + маршрутизация в `SingleInstanceService` | Бесплатные пользователи получают установку в один клик из браузера |
| 6 | Вход через SSO (после регистрации приложения в Nexus) | Удобнее, чем копипаста |

---

## Известные подводные камни

- **CDN-ссылки протухают (~30 мин)** — генерируйте свежую на каждую загрузку, не кешируйте.
- **Категории файлов**: всегда берите `category_name == "MAIN"` с максимальным `uploaded_timestamp`. Никогда не устанавливайте автоматически файлы `OLD_VERSION` или `OPTIONAL`.
- **`content_preview_link` сломан** по состоянию на июнь 2026 — поле в `/v1/.../files.json` возвращает 404 для файлов, загруженных после ~11 июня 2026. Не используйте это поле.
- **Конфликт NXM-обработчика**: если установлен Vortex/MO2, они уже обработчик NXM. Регистрируйтесь только если другого обработчика нет. Или предложите настройку.
- **Единственный экземпляр + NXM**: у второго экземпляра RHI ещё нет `DispatcherQueue` — используйте `SingleInstanceService.SendToRunningInstance("nxm:" + url)` и немедленно `Environment.Exit(0)`. Не инициализируйте всё приложение.
- **Безопасность API-ключа**: никогда не пишите значение ключа в лог. Храните в `settings.json` открытым текстом (как остальные настройки) — это локальный файл пользователя. Не добавляйте лишнее шифрование.
- **Флаг `IsExternalOnly`**: задаётся для модов, где `SnapshotUrl == null && NexusUrl != null`. Кнопка установки сейчас открывает браузер. После фазы 2 она должна скачивать напрямую для премиум-пользователей. Возможно, придётся подправить `ExternalLabel` / `InstallActionLabel` / логику `CanInstall` карточки, чтобы корректно показывать «Скачать» против «Обновить».
- **Флаг контента 18+**: часть модов требует включённой настройки контента 18+ в аккаунте. API возвращает для них 403, если не включено. Обработайте понятным сообщением об ошибке.
- **Заголовки лимитов**: читайте из каждого ответа. Если `X-RL-Daily-Remaining < 100`, снижайте активность и пишите предупреждение в лог.
- **`NexusModsUrl` vs `NexusUrl`**: `card.NexusModsUrl` (задаётся `NexusModsService`) — кнопка-ссылка в стиле PCGW, к загрузкам отношения не имеет. `card.NexusUrl` (задаётся из `GameMod.NexusUrl`) — URL страницы мода для скачивания. Не путайте.

---

## Сводная таблица

| Возможность | Премиум с API-ключом | Бесплатный (NXM зарегистрирован) | Бесплатный (без NXM) |
|---|---|---|---|
| Определение обновлений | ✅ уже работает | ✅ уже работает | ✅ уже работает |
| Установка/обновление в один клик | ✅ Фаза 2 | ✅ Фаза 5 (клик в браузере) | ❌ вручную |
| Тихое автообновление | ✅ Фаза 4 | ❌ | ❌ |
| Без браузера | ✅ | ❌ | ❌ |
