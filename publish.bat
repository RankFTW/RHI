@echo off
set OUT=C:\Users\Mark\OneDrive\Documents\RDXC\Publish\RHI
set SRC=RenoDXCommander

dotnet publish %SRC%\RenoDXCommander.csproj -c Release -r win-x64 -p:PublishSingleFile=true -p:Platform=x64 --self-contained false -o "%OUT%"

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
