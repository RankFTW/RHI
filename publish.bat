@echo off
set OUT=C:\Users\Mark\OneDrive\Documents\RDXC\Publish\UPST
set SRC=RenoDXCommander

dotnet publish %SRC%\RenoDXCommander.csproj -c Release -r win-x64 -p:PublishSingleFile=true -p:Platform=x64 --self-contained false -o "%OUT%"

:: Copy content files that the app needs alongside the EXE
copy /y "%SRC%\icon.ico" "%OUT%\" >nul
copy /y "%SRC%\7z.exe" "%OUT%\" >nul
copy /y "%SRC%\7z.dll" "%OUT%\" >nul
copy /y "%SRC%\ReShade.ini" "%OUT%\" >nul
copy /y "%SRC%\ReShade.Vulkan.ini" "%OUT%\" >nul
copy /y "%SRC%\ReShade64.json" "%OUT%\" >nul
copy /y "%SRC%\UPST_PatchNotes.md" "%OUT%\" >nul
copy /y "%SRC%\ultra_limiter.addon64" "%OUT%\" >nul
if not exist "%OUT%\Assets\icons" mkdir "%OUT%\Assets\icons"
copy /y "%SRC%\Assets\icons\*.ico" "%OUT%\Assets\icons\" >nul
