@echo off
set OUT=C:\Users\Mark\OneDrive\Documents\RDXC\Publish\RHI
set SRC=RenoDXCommander

:: Force close RHI and helpers if running so the publish can replace the EXE
powershell -NoProfile -Command "Get-Process RHI,'RHI-Stats','RHI.DropHelper' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue"
timeout /t 2 /nobreak >nul

dotnet publish %SRC%\RenoDXCommander.csproj -c Release -r win-x64 -p:PublishSingleFile=true -p:Platform=x64 --self-contained false -o "%OUT%"
if %errorlevel% neq 0 (
    echo dotnet publish failed, trying MSBuild...
    "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" %SRC%\RenoDXCommander.csproj /t:Publish /p:Configuration=Release /p:Platform=x64 /p:RuntimeIdentifier=win-x64 /p:PublishSingleFile=true /p:SelfContained=false /p:PublishDir="%OUT%"
)

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
if not exist "%OUT%\Assets\icons" mkdir "%OUT%\Assets\icons"
copy /y "%SRC%\Assets\icons\*.ico" "%OUT%\Assets\icons\" >nul
copy /y "%SRC%\Assets\icons\*.png" "%OUT%\Assets\icons\" >nul
copy /y "%SRC%\OptiScaler_nightly.nvidia.ini" "%OUT%\" >nul
copy /y "%SRC%\OptiScaler_nightly.amd-dlss.ini" "%OUT%\" >nul
copy /y "%SRC%\OptiScaler_nightly.amd-nodlss.ini" "%OUT%\" >nul
copy /y "%SRC%\OptiScaler_dlssnr.nvidia.ini" "%OUT%\" >nul
copy /y "%SRC%\OptiScaler_dlssnr.amd-dlss.ini" "%OUT%\" >nul
copy /y "%SRC%\OptiScaler_dlssnr.amd-nodlss.ini" "%OUT%\" >nul
copy /y "%SRC%\OptiScaler.nvidia.ini" "%OUT%\" >nul
copy /y "%SRC%\OptiScaler.amd-dlss.ini" "%OUT%\" >nul
copy /y "%SRC%\OptiScaler.amd-nodlss.ini" "%OUT%\" >nul

:: Deploy ClrMD for freeze diagnostics (must be loose alongside EXE, not bundled)
copy /y "%USERPROFILE%\.nuget\packages\microsoft.diagnostics.runtime\3.1.512801\lib\net6.0\Microsoft.Diagnostics.Runtime.dll" "%OUT%\" >nul
