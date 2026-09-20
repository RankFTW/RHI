@echo off
rem ============================================================
rem  Сборка русского инсталлятора RHI в один клик.
rem  Требуется: Windows, .NET 8 SDK, Inno Setup 6 (в C:\Program Files (x86)\Inno Setup 6).
rem  Результат: Installers\RHI-Setup.exe
rem ============================================================
setlocal
cd /d "%~dp0"

set OUT=%~dp0publish\RHI
set ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe

if not exist "%ISCC%" (
  echo [ОШИБКА] Inno Setup 6 не найден: %ISCC%
  echo Установите: https://jrsoftware.org/isdl.php
  exit /b 1
)

rem Закрываем RHI и хелперы, чтобы публикация смогла заменить exe
taskkill /F /IM RHI.exe >nul 2>nul
taskkill /F /IM RHI-Stats.exe >nul 2>nul
taskkill /F /IM RHI.DropHelper.exe >nul 2>nul
timeout /t 2 /nobreak >nul

echo [1/4] Публикация RHI (WinUI 3)...
dotnet publish RenoDXCommander\RenoDXCommander.csproj -c Release -r win-x64 -p:PublishSingleFile=true -p:Platform=x64 --self-contained false -o "%OUT%"
if errorlevel 1 goto :err

echo [2/4] Сборка DropHelper...
dotnet build RHI.DropHelper\RHI.DropHelper.csproj -c Release -v quiet
if errorlevel 1 goto :err
copy /y "RHI.DropHelper\bin\x64\Release\net8.0-windows\RHI.DropHelper.exe" "%OUT%\" >nul
copy /y "RHI.DropHelper\bin\x64\Release\net8.0-windows\RHI.DropHelper.dll" "%OUT%\" >nul 2>nul
copy /y "RHI.DropHelper\bin\x64\Release\net8.0-windows\RHI.DropHelper.runtimeconfig.json" "%OUT%\" >nul

echo [3/4] Копирование сопутствующих файлов...
copy /y "RenoDXCommander\icon.ico" "%OUT%\" >nul
copy /y "RenoDXCommander\7z.exe" "%OUT%\" >nul
copy /y "RenoDXCommander\7z.dll" "%OUT%\" >nul
copy /y "RenoDXCommander\ReShade.ini" "%OUT%\" >nul
copy /y "RenoDXCommander\ReShade.Vulkan.ini" "%OUT%\" >nul
copy /y "RenoDXCommander\ReShade64.json" "%OUT%\" >nul
copy /y "RenoDXCommander\RHI_PatchNotes.md" "%OUT%\" >nul
copy /y "RenoDXCommander\relimiter.ini" "%OUT%\" >nul
copy /y "RenoDXCommander\reshade.rdr2.ini" "%OUT%\" >nul
if not exist "%OUT%\Assets\icons" mkdir "%OUT%\Assets\icons"
copy /y "RenoDXCommander\Assets\icons\*.ico" "%OUT%\Assets\icons\" >nul
copy /y "RenoDXCommander\Assets\icons\*.png" "%OUT%\Assets\icons\" >nul
copy /y "RenoDXCommander\OptiScaler_nightly.nvidia.ini" "%OUT%\" >nul
copy /y "RenoDXCommander\OptiScaler_nightly.amd-dlss.ini" "%OUT%\" >nul
copy /y "RenoDXCommander\OptiScaler_nightly.amd-nodlss.ini" "%OUT%\" >nul
copy /y "RenoDXCommander\OptiScaler_dlssnr.nvidia.ini" "%OUT%\" >nul
copy /y "RenoDXCommander\OptiScaler_dlssnr.amd-dlss.ini" "%OUT%\" >nul
copy /y "RenoDXCommander\OptiScaler_dlssnr.amd-nodlss.ini" "%OUT%\" >nul
copy /y "RenoDXCommander\OptiScaler.nvidia.ini" "%OUT%\" >nul
copy /y "RenoDXCommander\OptiScaler.amd-dlss.ini" "%OUT%\" >nul
copy /y "RenoDXCommander\OptiScaler.amd-nodlss.ini" "%OUT%\" >nul

echo [4/4] Компиляция инсталлятора (Inno Setup, русский)...
if not exist "Installers" mkdir "Installers"
"%ISCC%" /DPublishDir="%OUT%" /DIconFile="%~dp0RenoDXCommander\icon.ico" /DInstallerOutDir="%~dp0Installers" "RHI Setup.iss"
if errorlevel 1 goto :err

echo.
echo Готово: %~dp0Installers\RHI-Setup.exe
exit /b 0

:err
echo.
echo [ОШИБКА] Сборка не удалась — см. сообщения выше.
exit /b 1
