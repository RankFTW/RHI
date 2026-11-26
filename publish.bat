@echo off
set OUT=C:\Users\Mark\OneDrive\Documents\RDXC\Publish\RHI
set SRC=RenoDXCommander

:: Close RHI and helpers so the publish can replace the EXE. RHI is asked to close itself first (the
:: shutdown signal file, which also works when it runs as admin and lets it save its settings); anything
:: still running after 10 s is force-stopped, and only then is there a 2 s wait for the file locks to go.
powershell -NoProfile -Command "$sig = Join-Path $env:LOCALAPPDATA 'RHI\rhi_shutdown_requested'; if (Get-Process RHI -ErrorAction SilentlyContinue) { New-Item -ItemType File -Force $sig | Out-Null; $i = 0; while ((Get-Process RHI -ErrorAction SilentlyContinue) -and $i -lt 20) { Start-Sleep -Milliseconds 500; $i++ } }; $left = Get-Process RHI,'RHI-Stats','RHI.DropHelper' -ErrorAction SilentlyContinue; if ($left) { $left | Stop-Process -Force -ErrorAction SilentlyContinue; Remove-Item $sig -Force -ErrorAction SilentlyContinue; Start-Sleep 2 }"

dotnet publish %SRC%\RenoDXCommander.csproj -c Release -r win-x64 -p:PublishSingleFile=true -p:Platform=x64 --self-contained false -o "%OUT%"
if not errorlevel 1 goto published
echo dotnet publish failed, trying MSBuild...
"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" %SRC%\RenoDXCommander.csproj /t:Publish /p:Configuration=Release /p:Platform=x64 /p:RuntimeIdentifier=win-x64 /p:PublishSingleFile=true /p:SelfContained=false /p:PublishDir="%OUT%"
if errorlevel 1 ( echo Publish failed & exit /b 1 )
:published

:: Build and copy the drop helper (non-elevated overlay for admin mode drag-drop)
dotnet build RHI.DropHelper\RHI.DropHelper.csproj -c Release -v quiet
copy /y "RHI.DropHelper\bin\x64\Release\net8.0-windows\RHI.DropHelper.exe" "%OUT%\" >nul
copy /y "RHI.DropHelper\bin\x64\Release\net8.0-windows\RHI.DropHelper.dll" "%OUT%\" >nul 2>nul
copy /y "RHI.DropHelper\bin\x64\Release\net8.0-windows\RHI.DropHelper.runtimeconfig.json" "%OUT%\" >nul

:: Copy content files that the app needs alongside the EXE
copy /y "%SRC%\icon.ico" "%OUT%\" >nul
copy /y "%SRC%\7z.exe" "%OUT%\" >nul
copy /y "%SRC%\7z.dll" "%OUT%\" >nul
copy /y "%SRC%\ReShade.ini" "%OUT%\" >nul
copy /y "%SRC%\ReShade.Vulkan.ini" "%OUT%\" >nul
copy /y "%SRC%\ReShade64.json" "%OUT%\" >nul
copy /y "%SRC%\RHI_PatchNotes.md" "%OUT%\" >nul
copy /y "%SRC%\relimiter.ini" "%OUT%\" >nul
copy /y "%SRC%\reshade.rdr2.ini" "%OUT%\" >nul
copy /y "%SRC%\DisplayCommander.ini" "%OUT%\" >nul
copy /y "%SRC%\dxvk.conf" "%OUT%\" >nul
if not exist "%OUT%\Assets\icons" mkdir "%OUT%\Assets\icons"
copy /y "%SRC%\Assets\icons\*.ico" "%OUT%\Assets\icons\" >nul
copy /y "%SRC%\Assets\icons\*.png" "%OUT%\Assets\icons\" >nul
copy /y "%SRC%\OptiScaler_nightly.nvidia.ini" "%OUT%\" >nul
copy /y "%SRC%\OptiScaler_nightly.amd-dlss.ini" "%OUT%\" >nul
copy /y "%SRC%\OptiScaler_nightly.amd-nodlss.ini" "%OUT%\" >nul
copy /y "%SRC%\OptiScaler_dlssnr.nvidia.ini" "%OUT%\" >nul
copy /y "%SRC%\OptiScaler_dlssnr.amd-dlss.ini" "%OUT%\" >nul
copy /y "%SRC%\OptiScaler_dlssnr.amd-nodlss.ini" "%OUT%\" >nul
copy /y "%SRC%\OptiScaler_f5.nvidia.ini" "%OUT%\" >nul
copy /y "%SRC%\OptiScaler_f5.amd-dlss.ini" "%OUT%\" >nul
copy /y "%SRC%\OptiScaler_f5.amd-nodlss.ini" "%OUT%\" >nul
copy /y "%SRC%\OptiScaler.nvidia.ini" "%OUT%\" >nul
copy /y "%SRC%\OptiScaler.amd-dlss.ini" "%OUT%\" >nul
copy /y "%SRC%\OptiScaler.amd-nodlss.ini" "%OUT%\" >nul

:: Deploy ClrMD for freeze diagnostics (must be loose alongside EXE, not bundled)
copy /y "%USERPROFILE%\.nuget\packages\microsoft.diagnostics.runtime\3.1.512801\lib\net6.0\Microsoft.Diagnostics.Runtime.dll" "%OUT%\" >nul
