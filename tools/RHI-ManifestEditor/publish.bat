@echo off
set SRC=g:\RDXC\tools\RHI-ManifestEditor\RHI.ManifestEditor
set OUT=C:\Users\Mark\OneDrive\Documents\RDXC\Publish\ManifestEditor

echo Publishing RHI Manifest Editor...
dotnet publish "%SRC%\RHI.ManifestEditor.csproj" ^
  -c Release -p:Platform=x64 ^
  -p:WindowsAppSDKSelfContained=true ^
  -p:SelfContained=true ^
  -p:PublishSingleFile=true ^
  -r win-x64 ^
  -o "%OUT%" ^
  --nologo

if %ERRORLEVEL% NEQ 0 (
  echo PUBLISH FAILED
  pause
  exit /b %ERRORLEVEL%
)

echo.
echo Published to: %OUT%
echo Run: %OUT%\RHI.ManifestEditor.exe
