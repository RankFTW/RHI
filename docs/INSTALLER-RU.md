# Сборка русифицированного инсталлятора RHI

> 💡 **Попроще:** если у вас есть аккаунт GitHub — закиньте патч в форк RHI,
> и воркфлоу `.github/workflows/build-installer-ru.yml` соберёт exe на серверах
> GitHub (Actions → «Сборка инсталлятора (RU)» → артефакт RHI-Setup-RU).
> Подробнее — в способе 3 ниже.

Само приложение внутри инсталлятора уже русифицировано (переведены исходники
интерфейса). Русифицирован и мастер установки: `RHI Setup.iss` использует
встроенный язык `Russian.isl` из Inno Setup 6.

## Что нужно

- Windows 10/11 x64
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Inno Setup 6](https://jrsoftware.org/isdl.php) (ставится в `C:\Program Files (x86)\Inno Setup 6`)

## Способ 1 — локально, одним скриптом

```bat
build-setup.bat
```

Скрипт: закрывает запущенный RHI → `dotnet publish` WinUI-приложения →
собирает DropHelper → копирует сопутствующие файлы → компилирует
`Installers\RHI-Setup.exe`.

## Способ 2 — вручную

```bat
publish.bat
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "RHI Setup.iss"
```

`publish.bat` публикует в `C:\Users\Mark\...` (пути автора по умолчанию).
Пути инсталлятора переопределяются через `/D`:

```bat
ISCC.exe /DPublishDir="C:\путь\к\publish\RHI" /DIconFile="C:\путь\к\icon.ico" /DInstallerOutDir="C:\путь\к\Installers" "RHI Setup.iss"
```

## Способ 3 — GitHub Actions (без своего Windows-окружения)

1. Запушьте репозиторий (с русификацией и `.github/workflows/build-installer-ru.yml`) в свой форк.
2. Вкладка **Actions** → «Сборка инсталлятора (RU)» → **Run workflow**.
3. Скачайте артефакт **RHI-Setup-RU** — это готовый `RHI-Setup.exe`.

## Примечания

- Версия в заголовке установщика задаётся `#define MyAppVersion` в `RHI Setup.iss`
  (автор обновляет её вручную при релизах).
- Чтобы вернуть в мастере выбор языка (русский/английский), раскомментируйте
  строку `Name: "english"` в секции `[Languages]`.
